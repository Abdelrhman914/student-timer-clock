using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
#pragma warning disable CS0162 // branches on the compile-time Enforced constant are intentional

namespace StudentTimerClock.Security;

/// <summary>
/// Runtime tamper protection.
///
///  Startup (before any window or data is loaded) and then continuously:
///   • Authenticode self-verification of the running executable + pinned signer certificate   (every 5 min)
///   • signed integrity manifest (integrity.json + integrity.sig): SHA-256 of every shipped file,
///     and no unexpected DLL/EXE in the program folder (DLL planting)                          (every 5 min)
///   • Windows WinVerifyTrust verdict on the executable (bad digest / no signature / explicitly distrusted = fail)
///   • loaded-module scan: every DLL mapped into the process must come from Windows/Program Files, be listed in the
///     signed manifest, or carry a valid trusted Authenticode signature (DLL injection)       (every 10 s)
///   • debugger / profiler / startup-hook detection                                          (every 10 s)
///  Any failure is logged to security.log and the application is terminated.
///
///  Only enforced in release builds made by scripts/build-release.sh (INTEGRITY_ENFORCED); development builds
///  run the same code in report-only mode so they can be debugged.
/// </summary>
public static class IntegrityGuard
{
#if INTEGRITY_ENFORCED
    public const bool Enforced = true;
#else
    public const bool Enforced = false;
#endif
    public const string ManifestName = "integrity.json", ManifestSigName = "integrity.sig";
    static readonly TimeSpan FullCheckEvery = TimeSpan.FromMinutes(5), QuickCheckEvery = TimeSpan.FromSeconds(10);

    public static volatile bool Compromised;
    public static string Status { get; private set; } = "Not checked yet";
    public static DateTime? LastFullCheck { get; private set; }
    public static AuthenticodeVerifier.Result LastSignature { get; private set; }
    public static string ExpectedExeSha256 { get; private set; }

    static Thread _monitor;
    static Manifest _manifest;
    static string _appDir, _exe;
    static readonly Dictionary<string, (long size, DateTime time, bool ok)> _moduleCache = new(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> _reported = new(StringComparer.OrdinalIgnoreCase);

    sealed class Manifest
    {
        public string Product { get; set; }
        public string Version { get; set; }
        public string Created { get; set; }
        public List<ManifestFile> Files { get; set; } = new();
        public List<string> AllowedExtraFiles { get; set; } = new();
    }
    sealed class ManifestFile { public string Path { get; set; } public long Size { get; set; } public string Sha256 { get; set; } }

    // ------------------------------------------------------------------ entry points

    /// <summary>Called first thing in Main. Terminates the process if the program was tampered with.</summary>
    public static void Startup(string[] args)
    {
        Hardening.Apply();
        _exe = Environment.ProcessPath ?? "";
        _appDir = Path.GetDirectoryName(_exe) ?? AppContext.BaseDirectory;

        var env = CheckEnvironment();
        if (env != null) Violation(env);

        var full = FullCheck();
        if (full != null) Violation(full);
        var quick = QuickCheck();
        if (quick != null) Violation(quick);

        if (!Enforced) Status = "Development build - integrity checks run in report-only mode (" + (full ?? quick ?? "no findings") + ")";
        _monitor = new Thread(MonitorLoop) { IsBackground = true, Name = "IntegrityMonitor", Priority = ThreadPriority.BelowNormal };
        _monitor.Start();
    }

    static void MonitorLoop()
    {
        var nextFull = DateTime.UtcNow + FullCheckEvery;
        while (true)
        {
            Thread.Sleep(QuickCheckEvery);
            try
            {
                var q = CheckEnvironment() ?? QuickCheck();
                if (q != null) Violation(q);
                if (DateTime.UtcNow >= nextFull)
                {
                    nextFull = DateTime.UtcNow + FullCheckEvery;
                    var f = FullCheck();
                    if (f != null) Violation(f);
                }
            }
            catch (Exception ex) { SecurityLog.Write("Integrity monitor error: " + ex.Message); }
        }
    }

    // ------------------------------------------------------------------ checks

    /// <summary>Signature, hash and manifest. Returns null when everything is intact, otherwise the reason.</summary>
    public static string FullCheck()
    {
        var sig = AuthenticodeVerifier.Verify(_exe, SignerPin.Sha256);
        LastSignature = sig;
        if (!sig.Ok) return "Digital signature check failed: " + sig.Error;

        string os = OsTrustCheck(_exe);
        if (os != null) return os;

        // signed manifest (signature made with the same pinned code-signing key)
        string mPath = Path.Combine(_appDir, ManifestName), sPath = Path.Combine(_appDir, ManifestSigName);
        if (!File.Exists(mPath) || !File.Exists(sPath)) return "The integrity manifest (integrity.json / integrity.sig) is missing.";
        byte[] mBytes, sBytes;
        try { mBytes = File.ReadAllBytes(mPath); sBytes = Convert.FromBase64String(File.ReadAllText(sPath).Trim()); }
        catch (Exception ex) { return "Cannot read the integrity manifest: " + ex.Message; }
        if (mBytes.Length > 1024 * 1024) return "The integrity manifest is too large.";
        if (!AuthenticodeVerifier.VerifyDetached(mBytes, sBytes, sig.Signer)) return "The integrity manifest's signature is invalid (manifest was altered).";
        Manifest m;
        try { m = JsonSerializer.Deserialize<Manifest>(mBytes); } catch { return "The integrity manifest is corrupt."; }
        if (m?.Files == null || m.Files.Count == 0) return "The integrity manifest is empty.";
        if (m.Version != HelpForm.Version) return $"The integrity manifest belongs to version {m.Version}, not {HelpForm.Version}.";
        _manifest = m;

        // every listed file: present, same size, same SHA-256
        string exeName = Path.GetFileName(_exe);
        bool exeListed = false;
        foreach (var f in m.Files)
        {
            if (f.Path.Contains("..") || Path.IsPathRooted(f.Path)) return "The integrity manifest contains an invalid path.";
            string p = Path.Combine(_appDir, f.Path);
            if (!File.Exists(p)) return $"Program file missing: {f.Path}";
            if (new FileInfo(p).Length != f.Size) return $"Program file was modified: {f.Path} (size)";
            string h;
            if (string.Equals(f.Path, exeName, StringComparison.OrdinalIgnoreCase)) { h = sig.FileSha256; exeListed = true; ExpectedExeSha256 = f.Sha256; }
            else h = HashFile(p);
            if (!string.Equals(h, f.Sha256, StringComparison.OrdinalIgnoreCase)) return $"Program file was modified: {f.Path} (SHA-256 mismatch)";
        }
        if (!exeListed) return "The executable is not listed in the integrity manifest (renamed or replaced).";

        // no unexpected code files in the program folder (DLL planting / side-loading)
        var allowed = new HashSet<string>(m.Files.Select(f => f.Path).Concat(m.AllowedExtraFiles ?? new()), StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(_appDir, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(_appDir, file);
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is ".dll" or ".exe" or ".sys" or ".ocx" or ".cpl" or ".drv" or ".scr" or ".com" or ".winmd" or ".manifest" or ".config"
                    || rel.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase) || rel.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase))
                if (!allowed.Contains(rel)) return $"Unexpected file in the program folder: {rel} (possible DLL planting / injection).";
        }

        LastFullCheck = DateTime.Now;
        Status = $"Verified {DateTime.Now:HH:mm:ss} - signature OK, {m.Files.Count} file(s) match the signed manifest";
        return null;
    }

    /// <summary>Cheap checks: debugger and loaded modules.</summary>
    public static string QuickCheck()
    {
        if (Debugger.IsAttached || Native.IsDebuggerPresent()) return "A debugger is attached to the process.";
        try { if (Native.CheckRemoteDebuggerPresent(Process.GetCurrentProcess().Handle, out bool remote) && remote) return "A remote debugger is attached to the process."; } catch { }
        return ModuleScan();
    }

    static string CheckEnvironment()
    {
        // ways to load foreign code into a .NET process at start-up
        foreach (var v in new[] { "DOTNET_STARTUP_HOOKS", "DOTNET_ADDITIONAL_DEPS", "DOTNET_SHARED_STORE", "COMPlus_EnableDiagnostics_Profiler" })
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(v))) return $"Code-injection environment variable is set: {v}";
        foreach (var v in new[] { "CORECLR_ENABLE_PROFILING", "COR_ENABLE_PROFILING", "DOTNET_ENABLE_PROFILING" })
            if (Environment.GetEnvironmentVariable(v) == "1") return $"A .NET profiler is being injected ({v}=1).";
        return null;
    }

    static string ModuleScan()
    {
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var trustedRoots = new[] { win, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetEnvironmentVariable("ProgramW6432") }
            .Where(p => !string.IsNullOrEmpty(p)).Select(p => p.TrimEnd('\\') + "\\").ToArray();
        ProcessModuleCollection mods;
        try { mods = Process.GetCurrentProcess().Modules; } catch { return null; }
        foreach (ProcessModule mod in mods)
        {
            string f;
            try { f = mod.FileName; } catch { continue; }
            if (string.IsNullOrEmpty(f)) continue;
            if (string.Equals(f, _exe, StringComparison.OrdinalIgnoreCase)) continue;
            if (trustedRoots.Any(r => f.StartsWith(r, StringComparison.OrdinalIgnoreCase))) continue; // admin-protected locations
            if (f.StartsWith(_appDir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
            {
                var rel = Path.GetRelativePath(_appDir, f);
                if (_manifest != null && _manifest.Files.Any(x => string.Equals(x.Path, rel, StringComparison.OrdinalIgnoreCase))) continue;
                if (!Enforced && _manifest == null) continue;
                return $"Unknown module loaded from the program folder: {rel}";
            }
            if (Native.IsWine) continue; // Wine maps host libraries outside C:\windows
            // anywhere else (user-writable): must carry a valid, trusted Authenticode signature
            try
            {
                var fi = new FileInfo(f);
                if (_moduleCache.TryGetValue(f, out var c) && c.size == fi.Length && c.time == fi.LastWriteTimeUtc) { if (c.ok) continue; }
                else
                {
                    bool ok = Native.WinVerifyTrustFile(f) == 0;
                    _moduleCache[f] = (fi.Length, fi.LastWriteTimeUtc, ok);
                    if (ok) continue;
                }
                return $"Unsigned code was injected into the process: {f}";
            }
            catch { }
        }
        return null;
    }

    /// <summary>Windows' own Authenticode verdict. With the project's self-signed certificate Windows reports
    /// "untrusted root", which is accepted because the certificate is pinned; tampering-related verdicts are not.</summary>
    static string OsTrustCheck(string path)
    {
        if (Native.IsWine) return null;
        uint hr;
        try { hr = Native.WinVerifyTrustFile(path); } catch { return null; }
        return hr switch
        {
            0 => null,
            0x800B0109 => null, // CERT_E_UNTRUSTEDROOT - self-signed root, pinned above
            0x800B010A => null, // CERT_E_CHAINING
            0x800B0101 => null, // CERT_E_EXPIRED - signature is still pinned & time-stamped
            0x80096010 => "Windows reports that the executable was modified after signing (TRUST_E_BAD_DIGEST).",
            0x800B0100 => "Windows reports that the executable has no valid signature (TRUST_E_NOSIGNATURE).",
            0x800B0111 => "The signing certificate has been explicitly distrusted on this PC (TRUST_E_EXPLICIT_DISTRUST).",
            0x800B010C => "The signing certificate has been revoked (CERT_E_REVOKED).",
            0x80096001 or 0x80096004 or 0x80096019 => $"Windows rejected the signature (0x{hr:X8}).",
            _ => null
        };
    }

    static string HashFile(string p)
    {
        using var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(fs));
    }

    // ------------------------------------------------------------------ reaction

    static void Violation(string reason)
    {
        if (!Enforced)
        {
            lock (_reported) if (_reported.Add(reason)) SecurityLog.Write("[report-only] integrity finding: " + reason);
            return;
        }
        Terminate(reason);
    }

    /// <summary>Freezes all functionality, records the event, tells the user and kills the process.</summary>
    public static void Terminate(string reason)
    {
        if (Compromised) return;
        Compromised = true;
        SecurityLog.Write("INTEGRITY VIOLATION - application terminated: " + reason);
        try { File.AppendAllText(Path.Combine(AppState.DataDir, "error.log"), $"[{DateTime.Now}] INTEGRITY VIOLATION: {reason}\n\n"); } catch { }
        // freeze immediately: hide every window of this process (the app is unusable from this moment)
        try
        {
            uint me = (uint)Environment.ProcessId;
            Native.EnumWindows((h, _) => { Native.GetWindowThreadProcessId(h, out uint pid); if (pid == me) Native.ShowWindow(h, 0); return true; }, IntPtr.Zero);
        }
        catch { }
        // hard watchdog: the process is killed after 15 s at the latest, whatever happens to the message box
        var watchdog = new Thread(() => { Thread.Sleep(15000); try { Process.GetCurrentProcess().Kill(); } catch { } Environment.FailFast("integrity violation"); })
        { IsBackground = true, Priority = ThreadPriority.Highest };
        watchdog.Start();
        if (!Debugger.IsAttached && !Native.IsDebuggerPresent())
        {
            // native, system-modal box (works from any thread, even before WinForms is initialised); closes itself after 15 s
            Native.MessageBoxTimeout(IntPtr.Zero,
                "Student Timer Clock has detected that it was modified or that foreign code was injected into it, and has been stopped for your protection.\n\n" +
                "Reason: " + reason + "\n\nPlease reinstall Student Timer Clock from the official installer. Your timers and history are not affected.\n\n" +
                "Details: " + SecurityLog.FilePath,
                "Student Timer Clock - integrity check failed", 0x10 | 0x1000 | 0x40000, 0, 15000);
        }
        try { Process.GetCurrentProcess().Kill(); } catch { }
        Environment.Exit(0x5EC);
    }
}

/// <summary>Process-level hardening against DLL search-order hijacking and classic injection techniques.</summary>
public static class Hardening
{
    public static readonly List<string> Applied = new();

    public static void Apply()
    {
        // 1. remove the current directory from the DLL search path; search System32 + the app folder only
        try { if (Native.SetDllDirectory("")) Applied.Add("current directory removed from DLL search path"); } catch { }
        try { if (Native.SetDefaultDllDirectories(0x00000800 | 0x00000200)) Applied.Add("DLL search limited to System32 and the program folder"); } catch { }
        if (Native.IsWine) return;
        // 2. block legacy extension points (AppInit_DLLs, global window hooks, Winsock LSPs, legacy IMEs) - classic DLL-injection vectors
        try
        {
            int flags = 1; // DisableExtensionPoints
            if (Native.SetProcessMitigationPolicy(6, ref flags, 4)) Applied.Add("extension-point DLLs blocked (AppInit, global hooks, LSP)");
        }
        catch { }
        // 3. image load policy: no DLLs from network shares or low-integrity locations, prefer System32 copies
        try
        {
            int flags = 1 | 2 | 4; // NoRemoteImages | NoLowMandatoryLabelImages | PreferSystem32Images
            if (Native.SetProcessMitigationPolicy(10, ref flags, 4)) Applied.Add("remote / low-integrity images blocked, System32 preferred");
        }
        catch { }
    }
}

static class Native
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);

    [DllImport("kernel32.dll")] public static extern bool IsDebuggerPresent();
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CheckRemoteDebuggerPresent(IntPtr hProcess, out bool present);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool SetDllDirectory(string path);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool SetDefaultDllDirectories(uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool SetProcessMitigationPolicy(int policy, ref int buffer, int length);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxTimeoutW")] static extern int MessageBoxTimeoutW(IntPtr h, string text, string caption, uint type, ushort lang, uint ms);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int MessageBoxW(IntPtr h, string text, string caption, uint type);
    [DllImport("ntdll.dll", EntryPoint = "wine_get_version")] static extern IntPtr wine_get_version();

    public static void MessageBoxTimeout(IntPtr h, string text, string caption, uint type, ushort lang, uint ms)
    {
        try { MessageBoxTimeoutW(h, text, caption, type, lang, ms); }
        catch { try { MessageBoxW(h, text, caption, type); } catch { } }
    }

    static bool? _wine;
    public static bool IsWine
    {
        get
        {
            if (_wine == null) { try { _wine = wine_get_version() != IntPtr.Zero; } catch { _wine = false; } }
            return _wine.Value;
        }
    }

    // ---- WinVerifyTrust (no UI, no network revocation lookups)
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WINTRUST_FILE_INFO { public uint cbStruct; public string pcwszFilePath; public IntPtr hFile; public IntPtr pgKnownSubject; }
    [StructLayout(LayoutKind.Sequential)]
    struct WINTRUST_DATA
    {
        public uint cbStruct; public IntPtr pPolicyCallbackData; public IntPtr pSIPClientData; public uint dwUIChoice; public uint fdwRevocationChecks;
        public uint dwUnionChoice; public IntPtr pFile; public uint dwStateAction; public IntPtr hWVTStateData; public IntPtr pwszURLReference;
        public uint dwProvFlags; public uint dwUIContext; public IntPtr pSignatureSettings;
    }
    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)] static extern uint WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WINTRUST_DATA data);
    static Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public static uint WinVerifyTrustFile(string path)
    {
        var fi = new WINTRUST_FILE_INFO { cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(), pcwszFilePath = path };
        IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(fi, pFile, false);
            var d = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(), dwUIChoice = 2 /*WTD_UI_NONE*/, fdwRevocationChecks = 0,
                dwUnionChoice = 1 /*WTD_CHOICE_FILE*/, pFile = pFile, dwStateAction = 1 /*VERIFY*/, dwProvFlags = 0x1000 /*CACHE_ONLY_URL_RETRIEVAL*/
            };
            uint hr = WinVerifyTrust(IntPtr.Zero, ref GenericVerifyV2, ref d);
            d.dwStateAction = 2; // CLOSE
            WinVerifyTrust(IntPtr.Zero, ref GenericVerifyV2, ref d);
            return hr;
        }
        finally { Marshal.FreeHGlobal(pFile); }
    }
}

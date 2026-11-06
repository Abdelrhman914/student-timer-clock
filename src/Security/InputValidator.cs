using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;

namespace StudentTimerClock.Security;

/// <summary>
/// Central allow-list validation for every value that can reach a dangerous sink
/// (process start, shell execute, mailto:, SendKeys, process termination, file paths, CSV export)
/// and sanitisation of all data that comes from disk (data.json, backups).
/// Rule of thumb: values are rejected, never "repaired" into something that is then executed.
/// </summary>
public static class InputValidator
{
    // ------------------------------------------------------------------ limits
    public const int MaxName = 120, MaxGroup = 80, MaxLabel = 200, MaxPath = 260, MaxArgs = 1024, MaxUrl = 2048,
                     MaxEmailList = 512, MaxSubject = 200, MaxBody = 4000, MaxKeys = 256, MaxProcessName = 64;
    public const int MaxItems = 2000, MaxHistory = 500_000, MaxLaps = 20_000, MaxActions = 32, MaxGroups = 500;
    public const double MaxDurationSeconds = 366d * 86400 * 10;       // 10 years
    public const long MaxDataFileBytes = 64L * 1024 * 1024;           // data.json / backup data.json
    public const long MaxBackupEntryBytes = 64L * 1024 * 1024;        // any single entry inside a backup zip
    public const long MaxImageBytes = 25L * 1024 * 1024, MaxSoundBytes = 50L * 1024 * 1024;

    public static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };
    public static readonly string[] SoundExts = { ".wav", ".mp3", ".wma", ".m4a", ".aac", ".mid", ".midi" };
    static readonly string[] RunnableExts = { ".exe", ".com", ".bat", ".cmd", ".lnk", ".msc", ".cpl", ".ps1", ".vbs", ".js", ".msi", ".appref-ms" };
    static readonly string[] OpenUrlBlockedExts = { ".exe", ".com", ".bat", ".cmd", ".scr", ".pif", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh",
                                                    ".hta", ".msi", ".msp", ".cpl", ".reg", ".lnk", ".url", ".jar", ".dll", ".sys", ".inf", ".application", ".appref-ms", ".msc", ".scf" };
    static readonly string[] AllowedUrlSchemes = { "http", "https" };
    static readonly HashSet<string> ProtectedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso", "svchost", "fontdrvhost", "dwm",
        "explorer", "taskmgr", "registry", "secure system", "memcompression", "msmpeng", "securityhealthservice", "sgrmbroker", "ntoskrnl",
        "studenttimerclock", "conhost", "audiodg", "spoolsv", "searchindexer", "runtimebroker", "sihost", "ctfmon"
    };
    public static readonly HashSet<string> KnownActionTypes = TimerAction.Types.Select(t => t.id).ToHashSet();
    /// <summary>Actions that can execute code or affect other programs. When they arrive from an untrusted source they are quarantined.</summary>
    public static readonly HashSet<string> SensitiveActionTypes = new() { "RunProgram", "OpenUrl", "SendEmail", "EndProcess", "PressKeys" };
    static readonly HashSet<string> WindowStyles = new() { "Normal Window", "Minimized", "Maximized", "Hidden" };

    static readonly Regex ProcessNameRx = new(@"^[A-Za-z0-9][A-Za-z0-9 ._\-()]{0,63}$", RegexOptions.CultureInvariant);
    static readonly Regex HexIdRx = new(@"^[A-Za-z0-9\-]{1,64}$", RegexOptions.CultureInvariant);
    // SendKeys: printable text plus the documented {KEY}/{KEY n} tokens and + ^ % ~ ( ) modifiers. No control characters.
    static readonly Regex SendKeysTokenRx = new(@"\{([A-Za-z]+[0-9]*|[+^%~(){}\[\]])( [0-9]{1,3})?\}", RegexOptions.CultureInvariant);
    static readonly HashSet<string> SendKeysNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "BACKSPACE","BS","BKSP","BREAK","CAPSLOCK","DELETE","DEL","DOWN","END","ENTER","ESC","HELP","HOME","INSERT","INS","LEFT","NUMLOCK",
        "PGDN","PGUP","PRTSC","RIGHT","SCROLLLOCK","TAB","UP","ADD","SUBTRACT","MULTIPLY","DIVIDE",
        "F1","F2","F3","F4","F5","F6","F7","F8","F9","F10","F11","F12","F13","F14","F15","F16"
    };

    // ------------------------------------------------------------------ primitives

    /// <summary>True if the text contains control characters (except TAB/CR/LF when allowed), bidi overrides or NUL.</summary>
    public static bool HasForbiddenChars(string s, bool allowNewLines = false)
    {
        if (s == null) return false;
        foreach (char c in s)
        {
            if (c == '\0') return true;
            if (char.IsControl(c) && !(allowNewLines && (c == '\r' || c == '\n' || c == '\t'))) return true;
            // bidi overrides / isolates can disguise file names ("exe.txt" trick) and zero-width chars
            if (c is '\u202A' or '\u202B' or '\u202C' or '\u202D' or '\u202E' or '\u2066' or '\u2067' or '\u2068' or '\u2069' or '\u200E' or '\u200F' or '\uFEFF') return true;
        }
        return false;
    }

    /// <summary>Cleans free text for display/storage: strips control & bidi characters, trims and limits the length.</summary>
    public static string CleanText(string s, int max, bool allowNewLines = false)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(Math.Min(s.Length, max));
        foreach (char c in s)
        {
            if (sb.Length >= max) break;
            if (c == '\0') continue;
            if (char.IsControl(c) && !(allowNewLines && (c == '\r' || c == '\n' || c == '\t'))) continue;
            if (c is '\u202A' or '\u202B' or '\u202C' or '\u202D' or '\u202E' or '\u2066' or '\u2067' or '\u2068' or '\u2069' or '\uFEFF') continue;
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }

    public static bool IsValidName(string s, out string error, int max = MaxName)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(s)) { error = "Please enter a name."; return false; }
        if (s.Length > max) { error = $"The name is too long (max {max} characters)."; return false; }
        if (HasForbiddenChars(s)) { error = "The name contains invisible / control characters that are not allowed."; return false; }
        return true;
    }

    static string Expand(string p) => Environment.ExpandEnvironmentVariables(p ?? "").Trim();

    /// <summary>A local, absolute, normalised file system path without device / UNC / alternate-data-stream tricks.</summary>
    public static bool IsSafeLocalPath(string raw, out string full, out string error, bool mustExist = true)
    {
        full = null; error = null;
        var p = Expand(raw);
        if (p.Length == 0) { error = "No path entered."; return false; }
        if (p.Length > MaxPath) { error = $"The path is too long (max {MaxPath} characters)."; return false; }
        if (HasForbiddenChars(p)) { error = "The path contains control characters."; return false; }
        if (p.IndexOfAny(new[] { '"', '<', '>', '|', '*', '?' }) >= 0) { error = "The path contains characters that are not allowed (\" < > | * ?)."; return false; }
        if (p.StartsWith(@"\\?\") || p.StartsWith(@"\\.\") || p.StartsWith("//")) { error = "Device paths are not allowed."; return false; }
        if (p.StartsWith(@"\\")) { error = "Network (UNC) paths are not allowed - copy the file to this PC first."; return false; }
        if (p.IndexOf(':', 2) >= 0) { error = "Alternate data streams (\":\" in a file name) are not allowed."; return false; }
        if (!Path.IsPathFullyQualified(p)) { error = "Please use a full path, e.g. C:\\Folder\\file.ext."; return false; }
        try { full = Path.GetFullPath(p); } catch (Exception ex) { error = "Invalid path: " + ex.Message; return false; }
        if (mustExist && !File.Exists(full)) { error = "The file does not exist:\n" + full; return false; }
        return true;
    }

    public static bool IsSafeFolder(string raw, out string full, out string error)
    {
        full = null; error = null;
        if (string.IsNullOrWhiteSpace(raw)) return true; // optional
        if (!IsSafeLocalPath(raw, out full, out error, mustExist: false)) return false;
        if (!Directory.Exists(full)) { error = "The folder does not exist:\n" + full; return false; }
        return true;
    }

    public static bool IsSafeImageFile(string path, out string error)
    {
        if (!IsSafeLocalPath(path, out var full, out error)) return false;
        if (!ImageExts.Contains(Path.GetExtension(full).ToLowerInvariant())) { error = "Only PNG, JPG, BMP or GIF images can be used."; return false; }
        if (new FileInfo(full).Length > MaxImageBytes) { error = "The image is too big (max 25 MB)."; return false; }
        return true;
    }

    public static bool IsSafeSoundFile(string path, out string error)
    {
        if (!IsSafeLocalPath(path, out var full, out error)) return false;
        if (!SoundExts.Contains(Path.GetExtension(full).ToLowerInvariant())) { error = "Only WAV, MP3, WMA, M4A, AAC or MIDI sound files can be used."; return false; }
        if (new FileInfo(full).Length > MaxSoundBytes) { error = "The sound file is too big (max 50 MB)."; return false; }
        return true;
    }

    /// <summary>Program for "Run program": existing local file with a runnable extension; arguments without control characters.</summary>
    public static bool IsSafeProgram(string path, string args, out string error)
    {
        if (!IsSafeLocalPath(path, out var full, out error)) return false;
        if (!RunnableExts.Contains(Path.GetExtension(full).ToLowerInvariant()))
        { error = "Only programs and scripts can be run (" + string.Join(", ", RunnableExts) + ")."; return false; }
        if ((args ?? "").Length > MaxArgs) { error = $"The parameters are too long (max {MaxArgs} characters)."; return false; }
        if (HasForbiddenChars(args)) { error = "The parameters contain line breaks or control characters, which are not allowed."; return false; }
        return true;
    }

    /// <summary>"Open URL / file": http(s) web address, or an existing local document/folder (never an executable or script).</summary>
    public static bool IsSafeUrlOrDocument(string raw, out string normalized, out string error)
    {
        normalized = null; error = null;
        var u = (raw ?? "").Trim();
        if (u.Length == 0) { error = "Please enter a web address or a file."; return false; }
        if (u.Length > MaxUrl) { error = "The address is too long."; return false; }
        if (HasForbiddenChars(u) || u.Any(char.IsWhiteSpace) && u.Contains("://")) { error = "The address contains spaces or control characters."; return false; }

        // local document / folder
        if (Path.IsPathFullyQualified(Expand(u)) && !u.Contains("://"))
        {
            if (!IsSafeLocalPath(u, out var full, out error, mustExist: false)) return false;
            if (Directory.Exists(full)) { normalized = full; return true; }
            if (!File.Exists(full)) { error = "The file does not exist:\n" + full; return false; }
            if (OpenUrlBlockedExts.Contains(Path.GetExtension(full).ToLowerInvariant()))
            { error = "Programs and scripts can't be opened with \"Open URL / file\". Use the \"Run program\" action instead."; return false; }
            normalized = full; return true;
        }

        if (!u.Contains("://")) u = "https://" + u;
        if (!Uri.TryCreate(u, UriKind.Absolute, out var uri) || !AllowedUrlSchemes.Contains(uri.Scheme.ToLowerInvariant()))
        { error = "Only web addresses starting with http:// or https:// are allowed."; return false; }
        if (string.IsNullOrEmpty(uri.Host) || !uri.Host.Contains('.') && uri.Host != "localhost") { error = "This is not a valid web address."; return false; }
        if (!string.IsNullOrEmpty(uri.UserInfo)) { error = "Addresses with a user name/password (user@host) are not allowed."; return false; }
        normalized = uri.AbsoluteUri; return true;
    }

    /// <summary>One or more e-mail addresses separated by ; or , – strictly parsed so nothing can be smuggled into the mailto: link.</summary>
    public static bool IsSafeEmailList(string raw, out string normalized, out string error)
    {
        normalized = null; error = null;
        var s = (raw ?? "").Trim();
        if (s.Length == 0) { error = "Please enter an e-mail address."; return false; }
        if (s.Length > MaxEmailList) { error = "Too many addresses."; return false; }
        if (HasForbiddenChars(s) || s.IndexOfAny(new[] { '?', '&', '#', '%', '<', '>', '"', '\'', '\\', '/' }) >= 0)
        { error = "The address contains characters that are not allowed."; return false; }
        var parts = s.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Length > 20) { error = "Enter between 1 and 20 addresses."; return false; }
        var ok = new List<string>();
        foreach (var p in parts)
        {
            try
            {
                var m = new MailAddress(p);
                if (m.Address != p || !m.Host.Contains('.')) throw new FormatException();
                ok.Add(m.Address);
            }
            catch { error = "Not a valid e-mail address: " + p; return false; }
        }
        normalized = string.Join(",", ok);
        return true;
    }

    public static bool IsSafeProcessName(string raw, out string name, out string error)
    {
        name = (raw ?? "").Trim(); error = null;
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (!ProcessNameRx.IsMatch(name)) { error = "Enter only the program's file name, e.g. notepad.exe (letters, digits, space . _ - ( ) )."; return false; }
        if (ProtectedProcesses.Contains(name)) { error = $"\"{name}\" is a Windows system process (or this app) and can't be ended."; return false; }
        return true;
    }

    public static bool IsSafeSendKeys(string keys, out string error)
    {
        error = null;
        if (string.IsNullOrEmpty(keys)) { error = "Please enter the keys to press."; return false; }
        if (keys.Length > MaxKeys) { error = $"Too many keys (max {MaxKeys} characters)."; return false; }
        if (HasForbiddenChars(keys)) { error = "Control characters are not allowed - use {ENTER}, {TAB} etc."; return false; }
        int depth = 0;
        for (int i = 0; i < keys.Length; i++)
        {
            char c = keys[i];
            if (c == '{')
            {
                var m = SendKeysTokenRx.Match(keys, i);
                if (!m.Success || m.Index != i) { error = "Unknown key code near: " + keys[i..Math.Min(keys.Length, i + 12)]; return false; }
                var nm = m.Groups[1].Value;
                if (nm.Length > 1 && !SendKeysNames.Contains(nm)) { error = "Unknown key: {" + nm + "}"; return false; }
                i += m.Length - 1;
            }
            else if (c == '}') { error = "Unmatched } - write {}} to press the } key."; return false; }
            else if (c == '(') depth++;
            else if (c == ')') { if (--depth < 0) { error = "Unmatched ) in the keys."; return false; } }
        }
        if (depth != 0) { error = "Unmatched ( in the keys."; return false; }
        return true;
    }

    /// <summary>Validates a single action before it is saved or executed.</summary>
    public static bool ValidateAction(TimerAction a, out string error)
    {
        error = null;
        if (a == null || !KnownActionTypes.Contains(a.Type ?? "")) { error = "Unknown action type."; return false; }
        switch (a.Type)
        {
            case "RunProgram":
                if (!IsSafeProgram(a.Path, a.Args, out error)) return false;
                if (!IsSafeFolder(a.Folder, out _, out error)) { error = "Start folder: " + error; return false; }
                if (!WindowStyles.Contains(a.WindowStyle ?? "")) { error = "Unknown window style."; return false; }
                return true;
            case "OpenUrl": return IsSafeUrlOrDocument(a.Url, out _, out error);
            case "SendEmail":
                if (!IsSafeEmailList(a.EmailTo, out _, out error)) return false;
                if ((a.Subject ?? "").Length > MaxSubject || HasForbiddenChars(a.Subject)) { error = "The subject is too long or contains control characters."; return false; }
                if ((a.Body ?? "").Length > MaxBody || HasForbiddenChars(a.Body, allowNewLines: true)) { error = "The message is too long or contains control characters."; return false; }
                return true;
            case "EndProcess": return IsSafeProcessName(a.ProcessName, out _, out error);
            case "PressKeys": return IsSafeSendKeys(a.Keys, out error);
            case "StartTimer":
                if (string.IsNullOrEmpty(a.TargetTimerId) || !HexIdRx.IsMatch(a.TargetTimerId)) { error = "Please choose a timer."; return false; }
                return true;
            case "Pause":
                if (a.Seconds < 0 || a.Seconds > 86400) { error = "The pause must be between 0 and 86400 seconds."; return false; }
                return true;
            default: return true;
        }
    }

    // ------------------------------------------------------------------ CSV

    /// <summary>Quotes a CSV field and neutralises spreadsheet formula injection (=, +, -, @, TAB, CR at the start).</summary>
    public static string CsvField(string s)
    {
        s ??= "";
        s = CleanText(s, 1000);
        if (s.Length > 0 && "=+-@\t\r|%".IndexOf(s[0]) >= 0) s = "'" + s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }

    // ------------------------------------------------------------------ whole data sanitisation

    public sealed class SanitizeReport
    {
        public int Fixed;
        public int Dropped;
        public List<string> Quarantined = new();
        public bool Any => Fixed > 0 || Dropped > 0 || Quarantined.Count > 0;
    }

    static double ClampD(double v, double lo, double hi) => double.IsNaN(v) || double.IsInfinity(v) ? lo : Math.Clamp(v, lo, hi);

    /// <summary>
    /// Brings data loaded from disk into a safe, consistent state: limits sizes, strips control characters, clamps numbers,
    /// removes unknown action types and – if <paramref name="untrusted"/> – quarantines every sensitive action so that it
    /// can't run until the user has reviewed and confirmed it in the Action dialog.
    /// </summary>
    public static SanitizeReport Sanitize(AppData d, bool untrusted)
    {
        var r = new SanitizeReport();
        d.Timers ??= new(); d.Stopwatches ??= new(); d.History ??= new(); d.Groups ??= new(); d.Settings ??= new AppSettings();
        if (d.Timers.Count > MaxItems) { r.Dropped += d.Timers.Count - MaxItems; d.Timers = d.Timers.Take(MaxItems).ToList(); }
        if (d.Stopwatches.Count > MaxItems) { r.Dropped += d.Stopwatches.Count - MaxItems; d.Stopwatches = d.Stopwatches.Take(MaxItems).ToList(); }
        if (d.History.Count > MaxHistory) { r.Dropped += d.History.Count - MaxHistory; d.History = d.History.Skip(d.History.Count - MaxHistory).ToList(); }
        d.Timers.RemoveAll(t => t == null); d.Stopwatches.RemoveAll(s => s == null); d.History.RemoveAll(h => h == null);

        string Fix(string s, int max, bool nl = false)
        {
            var c = CleanText(s, max, nl);
            if (!string.Equals(c, (s ?? "").Trim(), StringComparison.Ordinal)) r.Fixed++;
            return c;
        }

        var ids = new HashSet<string>();
        foreach (var it in d.Timers.Cast<TimeItem>().Concat(d.Stopwatches))
        {
            if (string.IsNullOrEmpty(it.Id) || !HexIdRx.IsMatch(it.Id) || !ids.Add(it.Id)) { it.Id = Guid.NewGuid().ToString("N"); r.Fixed++; ids.Add(it.Id); }
            it.Name = Fix(it.Name, MaxName); if (it.Name.Length == 0) it.Name = "Untitled";
            it.Group = Fix(it.Group, MaxGroup);
            if (it.Style != null) SanitizeStyle(it.Style, r);
        }
        foreach (var t in d.Timers)
        {
            t.DurationSeconds = ClampD(t.DurationSeconds, 1, MaxDurationSeconds);
            t.Sound = Fix(t.Sound, 60);
            if (!string.IsNullOrEmpty(t.CustomSoundPath) && !IsSafeSoundFile(t.CustomSoundPath, out _)) { t.CustomSoundPath = ""; r.Fixed++; }
            t.Actions ??= new();
            if (t.Actions.Count > MaxActions) { r.Dropped += t.Actions.Count - MaxActions; t.Actions = t.Actions.Take(MaxActions).ToList(); }
            int removed = t.Actions.RemoveAll(a => a == null || !KnownActionTypes.Contains(a.Type ?? ""));
            r.Dropped += removed;
            foreach (var a in t.Actions)
            {
                a.Path = Fix(a.Path, MaxPath); a.Args = Fix(a.Args, MaxArgs); a.Folder = Fix(a.Folder, MaxPath);
                a.Url = Fix(a.Url, MaxUrl); a.EmailTo = Fix(a.EmailTo, MaxEmailList); a.Subject = Fix(a.Subject, MaxSubject);
                a.Body = Fix(a.Body, MaxBody, true); a.ProcessName = Fix(a.ProcessName, MaxProcessName); a.Keys = Fix(a.Keys, MaxKeys);
                a.Seconds = Math.Clamp(a.Seconds, 0, 86400);
                if (!WindowStyles.Contains(a.WindowStyle ?? "")) a.WindowStyle = "Normal Window";
                if (SensitiveActionTypes.Contains(a.Type) && (untrusted || !ValidateAction(a, out _)) && !a.Quarantined)
                {
                    a.Quarantined = true;
                    r.Quarantined.Add($"{(t.Name.Length > 40 ? t.Name[..40] + "…" : t.Name)}: {a.Describe()}");
                }
            }
        }
        foreach (var s in d.Stopwatches)
        {
            s.Laps ??= new(); s.LapNames ??= new();
            if (s.Laps.Count > MaxLaps) { r.Dropped += s.Laps.Count - MaxLaps; s.Laps = s.Laps.Skip(s.Laps.Count - MaxLaps).ToList(); }
            s.Laps.RemoveAll(l => l == null);
            foreach (var l in s.Laps) { l.Name = Fix(l.Name, MaxName); l.LapSeconds = ClampD(l.LapSeconds, 0, MaxDurationSeconds); l.TotalSeconds = ClampD(l.TotalSeconds, 0, MaxDurationSeconds); }
            s.LapNames = s.LapNames.Where(n => n != null).Select(n => CleanText(n, MaxName)).Where(n => n.Length > 0).Distinct().Take(200).ToList();
            s.CurrentLapName = Fix(s.CurrentLapName, MaxName);
            s.LapStartElapsed = ClampD(s.LapStartElapsed, 0, MaxDurationSeconds);
        }
        foreach (var h in d.History)
        {
            h.Name = Fix(h.Name, MaxName); h.Group = Fix(h.Group, MaxGroup); h.Parent = Fix(h.Parent, MaxName);
            if (h.Kind is not ("Timer" or "Stopwatch" or "Lap")) { h.Kind = "Stopwatch"; r.Fixed++; }
            h.Seconds = ClampD(h.Seconds, 0, MaxDurationSeconds);
        }
        d.Groups = d.Groups.Where(g => g != null).Select(g => CleanText(g, MaxGroup)).Where(g => g.Length > 0).Distinct().Take(MaxGroups).ToList();

        var st = d.Settings;
        if (st.DefaultWidgetStyle != null) SanitizeStyle(st.DefaultWidgetStyle, r);
        st.SoundVolume = Math.Clamp(st.SoundVolume, 0, 100);
        st.WidgetOpacity = Math.Clamp(st.WidgetOpacity, 20, 100);
        st.MinSessionSeconds = ClampD(st.MinSessionSeconds, 0, 600);
        st.BackupKeep = Math.Clamp(st.BackupKeep, 1, 365);
        st.AutoBackupFrequency = Math.Clamp(st.AutoBackupFrequency, 0, 2);
        if (!string.IsNullOrEmpty(st.BackupFolder) && !IsSafeLocalPath(st.BackupFolder, out _, out _, mustExist: false)) { st.BackupFolder = ""; r.Fixed++; }
        st.CollapsedGroups = (st.CollapsedGroups ?? new()).Where(g => g != null).Select(g => CleanText(g, MaxGroup + 20)).Distinct().Take(MaxGroups).ToList();
        st.WinW = Math.Clamp(st.WinW, 400, 10000); st.WinH = Math.Clamp(st.WinH, 300, 10000);
        return r;
    }

    static void SanitizeStyle(WidgetStyle s, SanitizeReport r)
    {
        s.TitleText = CleanText(s.TitleText, MaxLabel);
        s.TimeFont = CleanText(s.TimeFont, 100);
        s.TitleFont = CleanText(s.TitleFont, 100);
        if (!string.IsNullOrEmpty(s.BackImage) && !IsSafeImageFile(s.BackImage, out _)) { s.BackImage = ""; r.Fixed++; }
        s.BackImageDim = Math.Clamp(s.BackImageDim, 0, 80);
    }
}

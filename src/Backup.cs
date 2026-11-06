using System.IO.Compression;
using System.Text.Json;

namespace StudentTimerClock;

/// <summary>
/// Backup / restore of everything the user made: timers, stopwatches (incl. laps), history, groups,
/// settings and widget styles. A backup is a .stcbackup file (a normal zip) containing data.json,
/// backup-info.json and copies of custom widget background images / custom alarm sounds.
/// </summary>
public static class Backup
{
    public const string Ext = ".stcbackup";
    public const string Filter = "Student Timer Clock backup (*.stcbackup)|*.stcbackup|Raw data file (*.json)|*.json|All files (*.*)|*.*";
    public static readonly string[] Frequencies = { "Every time the app starts", "Every day", "Every week" };

    static readonly JsonSerializerOptions _json = new() { WriteIndented = true, MaxDepth = 32 };
    /// <summary>Sensitive actions that were quarantined by the last <see cref="Read"/>.</summary>
    public static List<string> LastReadQuarantined = new();

    /// <summary>Read-only stream wrapper that throws once more than <c>max</c> bytes were read (zip-bomb protection).</summary>
    sealed class LimitedStream : Stream
    {
        readonly Stream _s; readonly long _max; long _read;
        public LimitedStream(Stream s, long max) { _s = s; _max = max; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = _s.Read(buffer, offset, count);
            if ((_read += n) > _max) throw new InvalidDataException("The backup is larger than allowed.");
            return n;
        }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin so) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }
    static bool _startupBackupDone;
    static volatile bool _busy;

    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Student Timer Clock Backups");

    /// <summary>The folder chosen in Settings, or Documents\Student Timer Clock Backups.</summary>
    public static string Folder
    {
        get
        {
            var f = AppState.Settings.BackupFolder;
            if (!string.IsNullOrWhiteSpace(f))
            {
                try { Directory.CreateDirectory(f); return f; } catch { }
            }
            try { Directory.CreateDirectory(DefaultFolder); } catch { }
            return DefaultFolder;
        }
    }

    public class Info
    {
        public string App { get; set; } = "Student Timer Clock";
        public string AppVersion { get; set; } = "0.3";
        public DateTime CreatedLocal { get; set; } = DateTime.Now;
        public string Computer { get; set; } = Environment.MachineName;
        public bool Automatic { get; set; }
        public int Timers { get; set; }
        public int Stopwatches { get; set; }
        public int Laps { get; set; }
        public int HistoryRecords { get; set; }
        /// <summary>original full path → entry name inside the zip (files/...)</summary>
        public Dictionary<string, string> Files { get; set; } = new();
    }

    // ------------------------------------------------------------------ create

    static IEnumerable<WidgetStyle> AllStyles(AppData d)
    {
        if (d.Settings?.DefaultWidgetStyle != null) yield return d.Settings.DefaultWidgetStyle;
        foreach (var i in d.Timers.Cast<TimeItem>().Concat(d.Stopwatches))
            if (i.Style != null) yield return i.Style;
    }

    static IEnumerable<string> ReferencedFiles(AppData d)
    {
        foreach (var s in AllStyles(d)) if (!string.IsNullOrWhiteSpace(s.BackImage)) yield return s.BackImage;
        foreach (var t in d.Timers) if (!string.IsNullOrWhiteSpace(t.CustomSoundPath)) yield return t.CustomSoundPath;
    }

    /// <summary>Takes a snapshot of the current data (must be called on the UI thread).</summary>
    static (byte[] json, Info info) Snapshot(bool automatic)
    {
        var d = AppState.Data;
        var info = new Info
        {
            Automatic = automatic,
            AppVersion = Application.ProductVersion.Split('+')[0],
            Timers = d.Timers.Count,
            Stopwatches = d.Stopwatches.Count,
            Laps = d.Stopwatches.Sum(s => s.Laps.Count),
            HistoryRecords = d.History.Count,
        };
        if (AppState.Settings.BackupIncludeFiles)
        {
            int n = 0;
            foreach (var f in ReferencedFiles(d).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (!File.Exists(f) || new FileInfo(f).Length > 50L * 1024 * 1024) continue; // skip missing / huge files
                    info.Files[f] = $"files/{++n:000}_{Path.GetFileName(f)}";
                }
                catch { }
            }
        }
        return (JsonSerializer.SerializeToUtf8Bytes(d, _json), info);
    }

    static void WriteZip(string path, byte[] json, Info info)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string tmp = path + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);
        using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            using (var s = zip.CreateEntry("data.json", CompressionLevel.Optimal).Open()) s.Write(json);
            foreach (var (src, entry) in info.Files.ToList())
            {
                try { zip.CreateEntryFromFile(src, entry, CompressionLevel.Optimal); }
                catch { info.Files.Remove(src); }
            }
            using (var s = zip.CreateEntry("backup-info.json").Open()) s.Write(JsonSerializer.SerializeToUtf8Bytes(info, _json));
            using (var w = new StreamWriter(zip.CreateEntry("README.txt").Open()))
                w.Write("Student Timer Clock backup\r\n\r\nTo restore it: open Student Timer Clock > Settings > Backup > \"Restore from backup...\"\r\n" +
                        "(or File > Restore from backup...).\r\n\r\ndata.json contains all timers, stopwatches, laps, history and settings.\r\n");
        }
        if (File.Exists(path)) File.Delete(path);
        File.Move(tmp, path);
    }

    /// <summary>Creates a backup file synchronously. Throws on error.</summary>
    public static Info CreateBackup(string path, bool automatic = false)
    {
        var (json, info) = Snapshot(automatic);
        WriteZip(path, json, info);
        RememberSuccess(path);
        return info;
    }

    static void RememberSuccess(string path)
    {
        AppState.Settings.LastBackupLocal = DateTime.Now;
        AppState.Settings.LastBackupFile = path;
        AppState.Settings.LastBackupError = "";
    }

    public static string SuggestedName(bool automatic) =>
        $"StudentTimerClock-{(automatic ? "auto" : "backup")}-{DateTime.Now:yyyy-MM-dd_HHmm}{Ext}";

    // ------------------------------------------------------------------ automatic

    /// <summary>Called by the main window once a minute (and shortly after start). Runs the zip work in the background.</summary>
    public static void AutoBackupIfDue()
    {
        var st = AppState.Settings;
        if (!st.AutoBackup || _busy) return;
        bool due;
        var last = st.LastAutoBackupLocal;
        switch (st.AutoBackupFrequency)
        {
            case 0: due = !_startupBackupDone; break;                            // every app start
            case 2: due = last == null || (DateTime.Now - last.Value).TotalDays >= 7; break; // weekly
            default: due = last == null || last.Value.Date < DateTime.Today; break;         // daily
        }
        if (!due) return;
        _startupBackupDone = true;

        // nothing worth saving yet (fresh install with only sample items and no history)? still back up, it is tiny.
        byte[] json; Info info; string folder, path;
        try
        {
            (json, info) = Snapshot(true);
            folder = Folder;
            path = Path.Combine(folder, SuggestedName(true));
        }
        catch (Exception ex) { st.LastBackupError = ex.Message; return; }

        int keep = Math.Max(1, st.BackupKeep);
        _busy = true;
        var ui = SynchronizationContext.Current;
        Task.Run(() =>
        {
            string error = null;
            try
            {
                WriteZip(path, json, info);
                Prune(folder, keep);
            }
            catch (Exception ex) { error = ex.Message; }
            void Done()
            {
                _busy = false;
                if (error == null) { RememberSuccess(path); st.LastAutoBackupLocal = DateTime.Now; }
                else st.LastBackupError = error;
                AppState.Save();
            }
            if (ui != null) ui.Post(_ => Done(), null); else Done();
        });
    }

    /// <summary>Deletes the oldest automatic backups so that only <paramref name="keep"/> remain. Manual backups are never deleted.</summary>
    static void Prune(string folder, int keep)
    {
        var autos = new DirectoryInfo(folder).GetFiles("StudentTimerClock-auto-*" + Ext)
            .OrderByDescending(f => f.LastWriteTimeUtc).ToList();
        foreach (var f in autos.Skip(keep)) { try { f.Delete(); } catch { } }
    }

    // ------------------------------------------------------------------ restore

    /// <summary>Reads a backup (.stcbackup zip or a raw data.json). Throws a friendly exception if it is not valid.</summary>
    public static (AppData data, Info info) Read(string path)
    {
        AppData data;
        Info info = null;
        bool isZip;
        using (var fs = File.OpenRead(path))
        {
            int b1 = fs.ReadByte(), b2 = fs.ReadByte();
            isZip = b1 == 'P' && b2 == 'K';
        }
        if (new FileInfo(path).Length > 1024L * 1024 * 1024) throw new InvalidDataException("The file is too large to be a backup (over 1 GB).");
        if (isZip)
        {
            using var zip = ZipFile.OpenRead(path);
            if (zip.Entries.Count > 1000) throw new InvalidDataException("The backup contains too many files.");
            var e = zip.GetEntry("data.json") ?? throw new InvalidDataException("This file is not a Student Timer Clock backup (data.json is missing).");
            if (e.Length > Security.InputValidator.MaxDataFileBytes) throw new InvalidDataException("data.json inside the backup is too large.");
            using (var s = e.Open()) data = JsonSerializer.Deserialize<AppData>(new LimitedStream(s, Security.InputValidator.MaxDataFileBytes), _json);
            var ie = zip.GetEntry("backup-info.json");
            if (ie != null && ie.Length < 1024 * 1024) { using var s = ie.Open(); try { info = JsonSerializer.Deserialize<Info>(s, _json); } catch { } }
        }
        else
        {
            if (new FileInfo(path).Length > Security.InputValidator.MaxDataFileBytes) throw new InvalidDataException("The file is too large.");
            data = JsonSerializer.Deserialize<AppData>(File.ReadAllBytes(path), _json);
        }
        if (data == null || (data.Timers == null && data.Stopwatches == null && data.History == null))
            throw new InvalidDataException("This file does not contain Student Timer Clock data.");
        data.Timers ??= new(); data.Stopwatches ??= new(); data.History ??= new(); data.Groups ??= new();
        data.Settings ??= new AppSettings();
        // a backup is untrusted input: sanitise everything and quarantine actions that could run code
        var report = Security.InputValidator.Sanitize(data, untrusted: true);
        LastReadQuarantined = report.Quarantined;
        if (info?.Files != null)
            info.Files = info.Files.Where(kv => kv.Key != null && kv.Value != null && kv.Value.StartsWith("files/") && !kv.Value.Contains("..") && kv.Value.Length < 300)
                                   .Take(500).ToDictionary(kv => kv.Key, kv => kv.Value);
        info ??= new Info { CreatedLocal = File.GetLastWriteTime(path), Computer = "?" };
        info.Timers = data.Timers.Count; info.Stopwatches = data.Stopwatches.Count;
        info.Laps = data.Stopwatches.Sum(s => s.Laps?.Count ?? 0); info.HistoryRecords = data.History.Count;
        return (data, info);
    }

    /// <summary>Copies files stored inside the backup to the data folder when the original path no longer exists, and fixes the paths.</summary>
    static void ExtractFiles(string path, AppData data, Info info)
    {
        if (info?.Files == null || info.Files.Count == 0) return;
        bool isZip;
        using (var fs = File.OpenRead(path)) isZip = fs.ReadByte() == 'P';
        if (!isZip) return;
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string target = Path.Combine(AppState.DataDir, "RestoredFiles");
        using (var zip = ZipFile.OpenRead(path))
        {
            foreach (var (orig, entryName) in info.Files)
            {
                try
                {
                    if (File.Exists(orig)) continue; // still there on this PC
                    var e = zip.GetEntry(entryName);
                    if (e == null || e.Length > Security.InputValidator.MaxSoundBytes) continue;
                    string name = Security.InputValidator.CleanText(Path.GetFileName(entryName), 120);
                    string ext = Path.GetExtension(name).ToLowerInvariant();
                    // only pictures and sounds are ever extracted (never programs, scripts, shortcuts…)
                    if (!Security.InputValidator.ImageExts.Contains(ext) && !Security.InputValidator.SoundExts.Contains(ext)) continue;
                    if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.StartsWith(".")) continue;
                    Directory.CreateDirectory(target);
                    string dest = Path.GetFullPath(Path.Combine(target, name));
                    if (!dest.StartsWith(Path.GetFullPath(target) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue; // zip-slip
                    e.ExtractToFile(dest, true);
                    map[orig] = dest;
                }
                catch { }
            }
        }
        if (map.Count == 0) return;
        foreach (var s in AllStyles(data))
            if (!string.IsNullOrEmpty(s.BackImage) && map.TryGetValue(s.BackImage, out var n)) s.BackImage = n;
        foreach (var t in data.Timers)
            if (!string.IsNullOrEmpty(t.CustomSoundPath) && map.TryGetValue(t.CustomSoundPath, out var n)) t.CustomSoundPath = n;
    }

    static string Describe(Info i) =>
        $"Created: {i.CreatedLocal:dddd d MMMM yyyy}, {Fmt.Clock(i.CreatedLocal)}" + (string.IsNullOrEmpty(i.Computer) || i.Computer == "?" ? "" : $" on {i.Computer}") + "\n" +
        $"Contains: {i.Timers} timers, {i.Stopwatches} stopwatches ({i.Laps} laps), {i.HistoryRecords} history records" +
        (i.Files?.Count > 0 ? $", {i.Files.Count} image/sound file(s)" : "");

    // ------------------------------------------------------------------ UI helpers

    /// <summary>"Back up now…" – asks where to save, then creates the backup.</summary>
    public static void BackupNowInteractive(IWin32Window owner)
    {
        using var dlg = new SaveFileDialog
        {
            Title = "Save a backup of your Student Timer Clock data",
            Filter = "Student Timer Clock backup (*.stcbackup)|*.stcbackup",
            DefaultExt = "stcbackup",
            FileName = SuggestedName(false),
            InitialDirectory = Folder,
            OverwritePrompt = true,
        };
        if (dlg.ShowDialog(owner) != DialogResult.OK) return;
        try
        {
            Cursor.Current = Cursors.WaitCursor;
            var info = CreateBackup(dlg.FileName);
            AppState.Save();
            Cursor.Current = Cursors.Default;
            if (MessageBox.Show(owner, "Backup saved:\n" + dlg.FileName + "\n\n" + Describe(info) +
                    "\n\nTip: copy it to a USB stick, Google Drive or OneDrive to keep it safe.\n\nOpen the folder now?",
                    "Backup saved", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                ShowInExplorer(dlg.FileName);
        }
        catch (Exception ex)
        {
            Cursor.Current = Cursors.Default;
            AppState.Settings.LastBackupError = ex.Message;
            MessageBox.Show(owner, "Could not save the backup:\n\n" + ex.Message, "Backup", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>"Restore from backup…" – returns true if data was restored (the app then restarts itself).</summary>
    public static bool RestoreInteractive(IWin32Window owner)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Restore Student Timer Clock data from a backup",
            Filter = Filter,
            InitialDirectory = Folder,
        };
        if (dlg.ShowDialog(owner) != DialogResult.OK) return false;
        return RestoreFile(owner, dlg.FileName);
    }

    public static bool RestoreFile(IWin32Window owner, string file)
    {
        AppData data; Info info;
        try { (data, info) = Read(file); }
        catch (Exception ex)
        {
            MessageBox.Show(owner, "This backup can't be opened:\n\n" + ex.Message, "Restore", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        var cur = AppState.Data;
        string msg = "Restore this backup?\n\n" + Path.GetFileName(file) + "\n" + Describe(info) + "\n\n" +
                     $"This REPLACES your current data ({cur.Timers.Count} timers, {cur.Stopwatches.Count} stopwatches, {cur.History.Count} history records).\n" +
                     "Your current data is backed up first automatically, so you can undo this.\n\nThe app will restart." +
                     (LastReadQuarantined.Count > 0
                        ? $"\n\nSECURITY: {LastReadQuarantined.Count} action(s) in this backup can run programs, open links, send keys or end processes. They will be BLOCKED until you review and confirm each one:\n • " +
                          string.Join("\n • ", LastReadQuarantined.Take(6)) + (LastReadQuarantined.Count > 6 ? "\n • …" : "")
                        : "");
        if (MessageBox.Show(owner, msg, "Restore from backup", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK)
            return false;

        // 1. safety backup of the current data
        string safety = Path.Combine(Folder, $"StudentTimerClock-before-restore-{DateTime.Now:yyyy-MM-dd_HHmmss}{Ext}");
        try { CreateBackup(safety); }
        catch (Exception ex)
        {
            if (MessageBox.Show(owner, "Could not back up the current data first:\n" + ex.Message + "\n\nRestore anyway?", "Restore",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return false;
            safety = null;
        }

        // 2. keep this PC's own preferences for the backup itself and the window position
        var old = AppState.Settings;
        var s = data.Settings;
        s.AutoBackup = old.AutoBackup; s.AutoBackupFrequency = old.AutoBackupFrequency; s.BackupKeep = old.BackupKeep;
        s.BackupFolder = old.BackupFolder; s.BackupIncludeFiles = old.BackupIncludeFiles;
        s.LastAutoBackupLocal = old.LastAutoBackupLocal; s.LastBackupLocal = old.LastBackupLocal;
        s.LastBackupFile = old.LastBackupFile; s.LastBackupError = "";
        s.WinX = old.WinX; s.WinY = old.WinY; s.WinW = old.WinW; s.WinH = old.WinH; s.WinMax = old.WinMax;
        s.FirstRunDone = true;

        // 3. image / sound files that only exist inside the backup
        try { ExtractFiles(file, data, info); } catch { }

        // 4. swap the data and restart
        AppState.ReplaceData(data);
        AppState.Save();
        MessageBox.Show(owner, "Your data was restored." + (safety != null ? "\n\nThe data you had before was saved as:\n" + safety : "") +
                        "\n\nStudent Timer Clock will now restart.", "Restore complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        Program.Main?.Restart();
        return true;
    }

    public static void ShowInExplorer(string file)
    {
        try
        {
            // paths are validated so they can't break out of the quoted argument
            if (!string.IsNullOrEmpty(file) && Security.InputValidator.IsSafeLocalPath(file, out var full, out _) && File.Exists(full))
                System.Diagnostics.Process.Start(Security.SafeShell.Explorer, "/select,\"" + full + "\"");
            else if (Security.InputValidator.IsSafeLocalPath(Folder, out var dir, out _, mustExist: false))
                System.Diagnostics.Process.Start(Security.SafeShell.Explorer, "\"" + dir + "\"");
        }
        catch { }
    }
}

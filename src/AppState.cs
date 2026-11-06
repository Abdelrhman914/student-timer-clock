using System.Text.Json;

namespace StudentTimerClock;

/// <summary>Central state + logic for timers, stopwatches and the activity history.</summary>
public static class AppState
{
    public static AppData Data { get; private set; } = new();
    public static AppSettings Settings => Data.Settings;

    public static event Action Changed;                 // structure / state changed (repaint lists)
    public static event Action<TimerItem, bool> TimerFinished; // bool = finished while app was closed
    public static event Action<TimeItem> WidgetRequested;

    public static readonly string DataDir = InitDataDir();
    public static string DataFile => Path.Combine(DataDir, "data.json");

    static string InitDataDir()
    {
        // portable mode: put a file named "portable.txt" next to the exe
        string exeDir = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(exeDir, "portable.txt"))) return exeDir;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StudentTimerClock");
    }

    static readonly JsonSerializerOptions _json = new() { WriteIndented = true, MaxDepth = 32 };

    /// <summary>Security warning to show once the main window is visible (e.g. data file modified outside the app).</summary>
    public static string PendingWarning;

    public static IEnumerable<TimeItem> AllItems => Data.Timers.Cast<TimeItem>().Concat(Data.Stopwatches);

    public static void Load()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            if (File.Exists(DataFile))
            {
                if (new FileInfo(DataFile).Length > Security.InputValidator.MaxDataFileBytes)
                    throw new InvalidDataException("The data file is unreasonably large (over 64 MB).");
                var bytes = File.ReadAllBytes(DataFile);
                var seal = Security.DataSeal.Check(DataFile, bytes);
                Data = JsonSerializer.Deserialize<AppData>(bytes, _json) ?? new AppData();
                Data.Settings ??= new AppSettings();
                var report = Security.InputValidator.Sanitize(Data, untrusted: seal == Security.DataSeal.State.Modified);
                if (seal == Security.DataSeal.State.Modified)
                {
                    Security.SecurityLog.Write("data.json was modified outside the app (seal mismatch). Quarantined actions: " + report.Quarantined.Count);
                    PendingWarning = "Your data file was changed outside Student Timer Clock (or copied from another PC/user)." +
                        (report.Quarantined.Count > 0
                            ? "\n\nFor your safety these actions were BLOCKED until you confirm them (Edit the timer → Actions → Edit → OK):\n • " + string.Join("\n • ", report.Quarantined.Take(10)) + (report.Quarantined.Count > 10 ? $"\n • … and {report.Quarantined.Count - 10} more" : "")
                            : "\n\nNo actions that can run programs were found, so nothing was blocked.") +
                        "\n\nIf you did not expect this, restore a backup (File → Restore from backup…).";
                    Save(); // re-seal (quarantine flags are now stored)
                }
                else if (seal == Security.DataSeal.State.FirstUse || report.Any) Save();
                return;
            }
        }
        catch (Exception ex)
        {
            try { File.Copy(DataFile, DataFile + ".broken-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true); } catch { }
            if (TryRestoreLatestBackup(ex.Message)) return;
            MessageBox.Show("Could not read saved data, starting fresh.\n\n" + ex.Message, "Student Timer Clock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        Data = CreateSample();
        Save();
    }

    /// <summary>data.json is damaged: offer the newest backup from the default backup folder.</summary>
    static bool TryRestoreLatestBackup(string error)
    {
        try
        {
            var dir = new DirectoryInfo(Backup.DefaultFolder);
            if (!dir.Exists) return false;
            var newest = dir.GetFiles("*" + Backup.Ext).OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
            if (newest == null) return false;
            if (MessageBox.Show("Your saved data could not be read:\n" + error + "\n\nRestore your latest backup instead?\n\n" + newest.Name + "\n(" + newest.LastWriteTime.ToString("g") + ")",
                    "Student Timer Clock", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return false;
            var (d, _) = Backup.Read(newest.FullName); // Read() sanitises and quarantines
            Data = d;
            Save();
            return true;
        }
        catch { return false; }
    }

    /// <summary>Replaces all data (used by Restore from backup).</summary>
    public static void ReplaceData(AppData d)
    {
        d.Settings ??= new AppSettings();
        Data = d;
    }

    public static void Save()
    {
        if (Security.IntegrityGuard.Compromised) return;
        try
        {
            Directory.CreateDirectory(DataDir);
            string tmp = DataFile + ".tmp";
            var bytes = JsonSerializer.SerializeToUtf8Bytes(Data, _json);
            File.WriteAllBytes(tmp, bytes);
            if (File.Exists(DataFile)) File.Replace(tmp, DataFile, null);
            else File.Move(tmp, DataFile);
            Security.DataSeal.Write(DataFile, bytes);
        }
        catch { }
    }

    public static void NotifyChanged(bool save = true)
    {
        if (save) Save();
        Changed?.Invoke();
    }

    public static void RequestWidget(TimeItem item) => WidgetRequested?.Invoke(item);

    // ------------------------------------------------------------ control
    public static void Start(TimeItem item)
    {
        if (item.IsRunning) return;
        if (item is TimerItem t && (t.Finished || t.Remaining(DateTime.UtcNow) <= 0))
        {
            if (t.IsTarget && t.CountdownTo.Value <= DateTime.Now) { t.Finished = true; NotifyChanged(); return; } // date already passed
            t.Finished = false; t.FinishedAtUtc = null; t.ElapsedBefore = 0;
        }
        item.IsRunning = true;
        item.StartedAtUtc = DateTime.UtcNow;
        NotifyChanged();
    }

    public static void Pause(TimeItem item)
    {
        if (!item.IsRunning) return;
        var now = DateTime.UtcNow;
        CloseSegment(item, now);
        item.IsRunning = false;
        item.StartedAtUtc = null;
        NotifyChanged();
    }

    public static void Toggle(TimeItem item)
    {
        if (item.IsRunning) Pause(item); else Start(item);
    }

    public static void Reset(TimeItem item)
    {
        var now = DateTime.UtcNow;
        if (item.IsRunning) CloseSegment(item, now);
        item.IsRunning = false;
        item.StartedAtUtc = null;
        item.ElapsedBefore = 0;
        if (item is TimerItem t) { t.Finished = false; t.FinishedAtUtc = null; }
        if (item is StopwatchItem s) { s.Laps.Clear(); s.LapStartElapsed = 0; }
        NotifyChanged();
    }

    public static void Restart(TimeItem item)
    {
        Reset(item);
        Start(item);
    }

    /// <summary>Give a finished timer some extra time (snooze).</summary>
    public static void AddTime(TimerItem t, double seconds)
    {
        var now = DateTime.UtcNow;
        if (t.IsTarget)
        {
            if (t.Finished || t.CountdownTo.Value <= DateTime.Now) t.CountdownTo = DateTime.Now.AddSeconds(seconds);
            else t.CountdownTo = t.CountdownTo.Value.AddSeconds(seconds);
            t.Finished = false; t.FinishedAtUtc = null; t.IsRunning = true; t.StartedAtUtc = now;
            NotifyChanged();
            return;
        }
        if (t.Finished || t.Remaining(now) <= 0)
        {
            t.Finished = false; t.FinishedAtUtc = null;
            t.ElapsedBefore = Math.Max(0, t.DurationSeconds - seconds);
            t.IsRunning = true; t.StartedAtUtc = now;
        }
        else
        {
            // still running: shift elapsed back
            t.ElapsedBefore -= seconds;
            if (t.ElapsedBefore < -t.DurationSeconds * 10) t.ElapsedBefore = 0;
        }
        NotifyChanged();
    }

    /// <summary>Records the running segment [StartedAt, end] and folds it into ElapsedBefore.</summary>
    static void CloseSegment(TimeItem item, DateTime endUtc)
    {
        if (!item.StartedAtUtc.HasValue) return;
        if (item is TimerItem tt && tt.IsTarget) { item.StartedAtUtc = endUtc; return; } // date countdowns are not "time spent"
        var start = item.StartedAtUtc.Value;
        double secs = Math.Max(0, (endUtc - start).TotalSeconds);
        item.ElapsedBefore += secs;
        item.StartedAtUtc = endUtc;
        Record(item.Name, item.Kind.ToString(), item.Group, "", start, endUtc, secs, item.ColorArgb);
    }

    public static void Record(string name, string kind, string group, string parent, DateTime start, DateTime end, double secs, int color)
    {
        if (!Settings.RecordHistory) return;
        if (secs < Settings.MinSessionSeconds) return;
        Data.History.Add(new SessionRecord
        {
            Name = string.IsNullOrWhiteSpace(name) ? "(unnamed)" : name.Trim(),
            Kind = kind, Group = group ?? "", Parent = parent ?? "",
            StartUtc = start, EndUtc = end, Seconds = Math.Round(secs, 2), ColorArgb = color
        });
    }

    public static Lap Lap(StopwatchItem s, string lapName = null)
    {
        var now = DateTime.UtcNow;
        double total = s.Elapsed(now);
        double lapSecs = Math.Max(0, total - s.LapStartElapsed);
        string name = (lapName ?? s.CurrentLapName ?? "").Trim();
        if (name == "") name = "Lap " + (s.Laps.Count + 1);
        var lap = new Lap { Name = name, LapSeconds = lapSecs, TotalSeconds = total, TimeUtc = now };
        s.Laps.Add(lap);
        s.LapStartElapsed = total;
        if (!string.IsNullOrWhiteSpace(s.CurrentLapName) && !s.LapNames.Contains(s.CurrentLapName.Trim()))
            s.LapNames.Add(s.CurrentLapName.Trim());
        Record(name, "Lap", s.Group, s.Name, now.AddSeconds(-lapSecs), now, lapSecs, s.ColorArgb);
        NotifyChanged();
        return lap;
    }

    /// <summary>Called by the UI clock ~10x per second.</summary>
    public static void Tick(bool startup = false)
    {
        var now = DateTime.UtcNow;
        bool any = false;
        foreach (var t in Data.Timers)
        {
            if (!t.IsRunning || t.Finished) continue;
            double rem = t.Remaining(now);
            if (rem > 0) continue;

            var finishAt = now.AddSeconds(rem); // rem <= 0 -> moment it actually hit zero
            CloseSegment(t, finishAt);
            t.TimesCompleted++;
            if (t.IsTarget)
            {
                t.IsRunning = false; t.StartedAtUtc = null; t.Finished = true; t.FinishedAtUtc = finishAt;
            }
            else if (t.AutoRestart && !startup)
            {
                t.ElapsedBefore = 0;
                t.StartedAtUtc = finishAt;
                t.IsRunning = true;
                t.Finished = false;
            }
            else
            {
                t.IsRunning = false;
                t.StartedAtUtc = null;
                t.ElapsedBefore = t.DurationSeconds;
                t.Finished = true;
                t.FinishedAtUtc = finishAt;
            }
            any = true;
            TimerFinished?.Invoke(t, startup);
        }
        if (any) NotifyChanged();
    }

    public static IEnumerable<string> AllGroups(ItemKind? kind = null)
    {
        var g = new List<string>();
        if (kind != ItemKind.Stopwatch) g.AddRange(Data.Timers.Select(x => x.Group));
        if (kind != ItemKind.Timer) g.AddRange(Data.Stopwatches.Select(x => x.Group));
        g.AddRange(Data.Groups);
        return g.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x);
    }

    // ------------------------------------------------------------ sample data
    static AppData CreateSample()
    {
        var d = new AppData();
        TimerItem T(string name, string group, double mins, int color, string icon, string sound = "Digital") =>
            new TimerItem { Name = name, Group = group, DurationSeconds = mins * 60, ColorArgb = Palette.Colors[color].ToArgb(), IconKey = icon, Sound = sound };

        var focus = T("Pomodoro - Focus", "Study", 25, 4, "tomato", "Bell");
        var brk = T("Short break", "Study", 5, 1, "cup", "Soft");
        focus.Actions.Add(new TimerAction { Type = "StartNextTimer" });
        d.Timers.Add(focus); d.Timers.Add(brk);

        var l1 = T("Teacher explanation", "Lesson segments", 25, 8, "person", "Chime");
        var l2 = T("Group activity", "Lesson segments", 15, 2, "group", "Chime");
        var l3 = T("Wrap-up", "Lesson segments", 10, 0, "notes", "Bell");
        l1.ShowPopup = false; l2.ShowPopup = false; l1.LoopSound = false; l2.LoopSound = false;
        l1.Actions.Add(new TimerAction { Type = "StartNextTimer" });
        l2.Actions.Add(new TimerAction { Type = "StartNextTimer" });
        d.Timers.AddRange(new[] { l1, l2, l3 });

        var exam = new TimerItem
        {
            Name = "Final exam", Group = "Exams", ColorArgb = Palette.Colors[5].ToArgb(), IconKey = "book", Sound = "Alarm",
            CountdownTo = DateTime.Today.AddDays(45).AddHours(9), CreatedUtc = DateTime.UtcNow, IsRunning = true, StartedAtUtc = DateTime.UtcNow,
        };
        exam.Style = new WidgetStyle(); exam.Style.ApplyPreset("Navy"); exam.Style.ShowDays = "Always"; exam.Style.ShowSeconds = false;
        d.Timers.Add(exam);
        d.Timers.Add(T("Tea / Coffee", "Home", 4, 3, "cup", "Chime"));

        d.Stopwatches.Add(new StopwatchItem { Name = "Studying", Group = "Activities", ColorArgb = Palette.Colors[2].ToArgb(), IconKey = "book", LapNames = new() { "Math", "Physics", "English", "Break" }, CurrentLapName = "Math" });
        d.Stopwatches.Add(new StopwatchItem { Name = "Homework", Group = "Activities", ColorArgb = Palette.Colors[1].ToArgb(), IconKey = "notes" });
        d.Stopwatches.Add(new StopwatchItem { Name = "Reading", Group = "Activities", ColorArgb = Palette.Colors[7].ToArgb(), IconKey = "book" });
        var rd = new StopwatchItem { Name = "Reaction drills", Group = "Training", ColorArgb = Palette.Colors[0].ToArgb(), IconKey = "star", LapNames = new() { "Mary", "James", "Elizabeth", "William" }, CurrentLapName = "Elizabeth" };
        double tot = 0;
        foreach (var (n, sec) in new[] { ("Mary", 4.75), ("James", 6.08), ("Elizabeth", 10.87), ("William", 6.21), ("James", 4.07), ("Mary", 4.66), ("William", 8.50), ("Elizabeth", 9.87) })
        { tot += sec; rd.Laps.Add(new Lap { Name = n, LapSeconds = sec, TotalSeconds = tot }); }
        rd.ElapsedBefore = tot; rd.LapStartElapsed = tot;
        d.Stopwatches.Add(rd);
        return d;
    }
}

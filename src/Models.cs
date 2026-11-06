using System.Text.Json.Serialization;

namespace StudentTimerClock;

public enum ItemKind { Timer, Stopwatch }

/// <summary>Base for anything that counts time (timer or stopwatch).</summary>
public abstract class TimeItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Group { get; set; } = "General";
    public int ColorArgb { get; set; } = unchecked((int)0xFFFFF4CC);
    public string IconKey { get; set; } = "clock";
    public string Notes { get; set; } = "";

    // running state (wall-clock based => survives restarts, never drifts)
    public bool IsRunning { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public double ElapsedBefore { get; set; }

    // floating widget state
    public bool WidgetOpen { get; set; }
    public int WidgetX { get; set; } = -1;
    public int WidgetY { get; set; } = -1;
    public int WidgetSize { get; set; } = 1; // legacy preset
    public int WidgetW { get; set; }          // 0 = default size for the layout
    public int WidgetH { get; set; }
    public WidgetStyle Style { get; set; }     // null = use the default widget style

    [JsonIgnore] public abstract ItemKind Kind { get; }
    [JsonIgnore] public Color Color { get => Color.FromArgb(ColorArgb); set => ColorArgb = value.ToArgb(); }

    public double Elapsed(DateTime nowUtc)
    {
        double e = ElapsedBefore;
        if (IsRunning && StartedAtUtc.HasValue) e += (nowUtc - StartedAtUtc.Value).TotalSeconds;
        return Math.Max(0, e);
    }
}

public class TimerItem : TimeItem
{
    public double DurationSeconds { get; set; } = 300;
    /// <summary>If set, the timer counts down to this local date/time (e.g. an exam) instead of a duration.</summary>
    public DateTime? CountdownTo { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public List<TimerAction> Actions { get; set; } = new();
    public string Sound { get; set; } = "Digital";
    public string CustomSoundPath { get; set; } = "";
    public bool LoopSound { get; set; } = true;
    public bool AutoRestart { get; set; }
    public bool ShowPopup { get; set; } = true;
    public bool Finished { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public int TimesCompleted { get; set; }

    [JsonIgnore] public override ItemKind Kind => ItemKind.Timer;
    [JsonIgnore] public bool IsTarget => CountdownTo.HasValue;
    public double Remaining(DateTime nowUtc)
    {
        if (IsTarget) return Finished ? 0 : (CountdownTo.Value.ToUniversalTime() - nowUtc).TotalSeconds;
        return DurationSeconds - Elapsed(nowUtc);
    }
    /// <summary>Total length used for progress / dial (for date countdowns: from creation to target).</summary>
    public double TotalSeconds => IsTarget ? Math.Max(1, (CountdownTo.Value.ToUniversalTime() - CreatedUtc).TotalSeconds) : DurationSeconds;
    public double Progress(DateTime nowUtc) => TotalSeconds <= 0 ? 1 : Math.Clamp(1 - Remaining(nowUtc) / TotalSeconds, 0, 1);
    /// <summary>Local time the timer will reach zero (null if not running).</summary>
    public DateTime? EndsAtLocal(DateTime nowUtc)
    {
        if (IsTarget) return CountdownTo.Value;
        if (Finished) return FinishedAtUtc?.ToLocalTime();
        if (!IsRunning) return null;
        return DateTime.Now.AddSeconds(Remaining(nowUtc));
    }
    public TimerItem Clone()
    {
        var t = (TimerItem)MemberwiseClone();
        t.Id = Guid.NewGuid().ToString("N");
        t.IsRunning = false; t.StartedAtUtc = null; t.ElapsedBefore = 0; t.Finished = false;
        t.WidgetOpen = false; t.WidgetX = t.WidgetY = -1; t.TimesCompleted = 0;
        t.CreatedUtc = DateTime.UtcNow;
        t.Actions = Actions.Select(a => a.Clone()).ToList();
        t.Style = Style?.Clone();
        return t;
    }
}

public class Lap
{
    public string Name { get; set; } = "";
    public double LapSeconds { get; set; }
    public double TotalSeconds { get; set; }
    public DateTime TimeUtc { get; set; } = DateTime.UtcNow;
}

public class StopwatchItem : TimeItem
{
    public List<Lap> Laps { get; set; } = new();
    public string CurrentLapName { get; set; } = "";
    public List<string> LapNames { get; set; } = new();
    public double LapStartElapsed { get; set; }

    [JsonIgnore] public override ItemKind Kind => ItemKind.Stopwatch;
    public double CurrentLap(DateTime nowUtc) => Math.Max(0, Elapsed(nowUtc) - LapStartElapsed);
    public StopwatchItem Clone()
    {
        var s = (StopwatchItem)MemberwiseClone();
        s.Id = Guid.NewGuid().ToString("N");
        s.IsRunning = false; s.StartedAtUtc = null; s.ElapsedBefore = 0; s.LapStartElapsed = 0;
        s.Laps = new(); s.LapNames = new(LapNames);
        s.WidgetOpen = false; s.WidgetX = s.WidgetY = -1;
        s.Style = Style?.Clone();
        return s;
    }
}

/// <summary>One continuous block of time spent on something - feeds the statistics graphs.</summary>
public class SessionRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "Stopwatch"; // Timer | Stopwatch | Lap
    public string Group { get; set; } = "";
    public string Parent { get; set; } = "";
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public double Seconds { get; set; }
    public int ColorArgb { get; set; }
}

public class AppSettings
{
    public bool MinimizeToTray { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;
    public bool FirstRunDone { get; set; }
    public WidgetStyle DefaultWidgetStyle { get; set; } = new();
    public string BoardSource { get; set; } = "Running";
    public bool BoardFullscreen { get; set; }
    public bool StartMinimized { get; set; }
    public bool ConfirmDelete { get; set; } = true;
    public int WidgetOpacity { get; set; } = 95;
    public bool WidgetTopMost { get; set; } = true;
    public bool WidgetDigitalFont { get; set; } = true;
    public bool WidgetSnapToEdges { get; set; } = true;
    public string WidgetTheme { get; set; } = "Dark";
    public bool ShowHundredths { get; set; } = true;
    public bool Use24Hour { get; set; } = false;
    public bool TimerOvertime { get; set; } = true;
    public string DefaultSound { get; set; } = "Digital";
    public int SoundVolume { get; set; } = 80;
    public bool RecordHistory { get; set; } = true;
    public double MinSessionSeconds { get; set; } = 1;
    public int WinX { get; set; } = -1;
    public int WinY { get; set; } = -1;
    public int WinW { get; set; } = 1240;
    public int WinH { get; set; } = 700;
    public bool WinMax { get; set; }
    public int LastPage { get; set; }
    public List<string> CollapsedGroups { get; set; } = new();

    // ---- backup
    public bool AutoBackup { get; set; } = true;
    public int AutoBackupFrequency { get; set; } = 1;    // 0 = every app start, 1 = daily, 2 = weekly
    public int BackupKeep { get; set; } = 10;            // how many automatic backups to keep
    public string BackupFolder { get; set; } = "";       // "" = Documents\Student Timer Clock Backups
    public bool BackupIncludeFiles { get; set; } = true; // include custom widget images and alarm sounds
    public DateTime? LastAutoBackupLocal { get; set; }
    public DateTime? LastBackupLocal { get; set; }
    public string LastBackupFile { get; set; } = "";
    public string LastBackupError { get; set; } = "";
}

public class AppData
{
    public int Version { get; set; } = 1;
    public List<TimerItem> Timers { get; set; } = new();
    public List<StopwatchItem> Stopwatches { get; set; } = new();
    public List<SessionRecord> History { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
    public List<string> Groups { get; set; } = new();
}

/// <summary>Look of a floating desktop widget (fonts, colours, background, what to show).</summary>
public class WidgetStyle
{
    public string Layout { get; set; } = "Digital";   // Digital | Dial | Compact
    public string Preset { get; set; } = "Light";
    public string TimeFont { get; set; } = "";         // "" = built-in digital LCD
    public bool TimeBold { get; set; } = true;
    public bool TimeItalic { get; set; }
    public string TitleFont { get; set; } = "Segoe UI";
    public bool TitleItalic { get; set; }
    public bool TitleBold { get; set; }
    public string TitleText { get; set; } = "";        // "" = item name
    public int ForeColor { get; set; } = unchecked((int)0xFF202020);
    public int LabelColor { get; set; } = unchecked((int)0xFF505050);
    public int BackColor { get; set; } = unchecked((int)0xFFFFFFFF);
    public int BackColor2 { get; set; } = unchecked((int)0xFFF0F2F5);
    public bool Gradient { get; set; } = true;
    public bool UseItemColor { get; set; }
    public int AccentColor { get; set; } = unchecked((int)0xFFFF8A1F); // dial / progress colour
    public string BackImage { get; set; } = "";
    public bool BackImageStretch { get; set; }
    public int BackImageDim { get; set; }              // 0-80 % darkening overlay
    public bool ShowTitle { get; set; } = true;
    public bool ShowUnitLabels { get; set; } = true;   // DAY HOUR MIN SEC
    public string ShowDays { get; set; } = "Auto";     // Auto | Always | Never
    public bool ShowSeconds { get; set; } = true;
    public bool ShowHundredths { get; set; }
    public bool ShowDate { get; set; } = true;
    public bool ShowEndTime { get; set; } = true;
    public bool ShowProgress { get; set; } = true;
    public string ShowButtons { get; set; } = "Always"; // Always | Hover | Never
    public bool RoundCorners { get; set; } = true;
    public bool Border { get; set; } = true;
    public int Opacity { get; set; } = 100;
    public bool TopMost { get; set; } = true;
    public bool Locked { get; set; }

    public WidgetStyle Clone() => (WidgetStyle)MemberwiseClone();

    public static readonly string[] Presets = { "Light", "Dark", "Navy", "Sky", "Classroom", "Item colour" };
    public void ApplyPreset(string p)
    {
        Preset = p; UseItemColor = false; Gradient = true;
        switch (p)
        {
            case "Dark": BackColor = Argb(48, 52, 60); BackColor2 = Argb(26, 28, 34); ForeColor = Argb(255, 255, 255); LabelColor = Argb(170, 176, 186); AccentColor = Argb(255, 138, 31); break;
            case "Navy": BackColor = Argb(40, 56, 84); BackColor2 = Argb(28, 40, 62); ForeColor = Argb(255, 255, 255); LabelColor = Argb(200, 214, 235); AccentColor = Argb(90, 170, 255); break;
            case "Sky": BackColor = Argb(232, 243, 255); BackColor2 = Argb(176, 210, 245); ForeColor = Argb(20, 50, 100); LabelColor = Argb(60, 90, 140); AccentColor = Argb(30, 110, 200); break;
            case "Classroom": BackColor = Argb(88, 112, 170); BackColor2 = Argb(64, 86, 142); ForeColor = Argb(255, 255, 255); LabelColor = Argb(235, 240, 255); AccentColor = Argb(255, 200, 60); TitleItalic = true; break;
            case "Item colour": UseItemColor = true; ForeColor = Argb(25, 25, 25); LabelColor = Argb(70, 70, 70); AccentColor = Argb(232, 93, 40); break;
            default: BackColor = Argb(255, 255, 255); BackColor2 = Argb(240, 242, 245); ForeColor = Argb(32, 32, 32); LabelColor = Argb(80, 80, 80); AccentColor = Argb(255, 138, 31); break;
        }
    }
    static int Argb(int r, int g, int b) => Color.FromArgb(r, g, b).ToArgb();
}

/// <summary>Something to do automatically when a timer finishes.</summary>
public class TimerAction
{
    public string Type { get; set; } = "RunProgram";
    public string Path { get; set; } = "";
    public string Args { get; set; } = "";
    public string Folder { get; set; } = "";
    public string WindowStyle { get; set; } = "Normal Window";
    public bool SwitchToExisting { get; set; }
    public string Url { get; set; } = "";
    public string EmailTo { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public string Keys { get; set; } = "";
    public string TargetTimerId { get; set; } = "";
    public bool MoveWidget { get; set; } = true;
    public int Seconds { get; set; } = 5;
    public bool Confirm { get; set; } = true;
    /// <summary>Set when a sensitive action came from an untrusted source (modified data file, restored backup).
    /// A quarantined action never runs until the user opens it in the Action dialog and confirms it with OK.</summary>
    public bool Quarantined { get; set; }

    public TimerAction Clone() => (TimerAction)MemberwiseClone();

    public static readonly (string id, string text, string icon)[] Types =
    {
        ("StartNextTimer", "Start next timer", "next"),
        ("StartTimer", "Start timer", "timers"),
        ("RunProgram", "Run program", "run_prog"),
        ("OpenUrl", "Open URL / file", "globe"),
        ("SendEmail", "Send email", "mail"),
        ("Lock", "Lock this computer", "lock"),
        ("LogOff", "Log Off", "key"),
        ("Restart", "Restart", "power_blue"),
        ("Sleep", "Sleep", "power_green"),
        ("Hibernate", "Hibernate", "power_green"),
        ("ShutDown", "Shut Down", "power_red"),
        ("MonitorOff", "Turn off monitor", "monitor"),
        ("ScreenSaver", "Start screen saver", "monitor"),
        ("EndProcess", "End process", "endproc"),
        ("PressKeys", "Press keys", "keys"),
        ("MediaPause", "Play / pause music", "music"),
        ("Pause", "Pause (wait)", "pause_bars"),
    };

    public string Describe()
    {
        var t = Types.FirstOrDefault(x => x.id == Type).text ?? Type;
        string extra = Type switch
        {
            "RunProgram" => System.IO.Path.GetFileName(Path),
            "OpenUrl" => Url,
            "SendEmail" => EmailTo,
            "EndProcess" => ProcessName,
            "PressKeys" => Keys,
            "Pause" => Seconds + " s",
            "StartTimer" => AppState.Data.Timers.FirstOrDefault(x => x.Id == TargetTimerId)?.Name ?? "?",
            _ => ""
        };
        var d = string.IsNullOrEmpty(extra) ? t : $"{t}: {extra}";
        return Quarantined ? "⚠ BLOCKED - " + d : d;
    }
}

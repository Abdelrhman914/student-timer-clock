using Microsoft.Win32;

namespace StudentTimerClock;

public class SettingsForm : Form
{
    readonly AppSettings _s = AppState.Settings;
    readonly CheckBox _minTray = C("Minimize to the system tray"), _closeTray = C("Closing the window keeps the app running in the tray"),
        _startWin = C("Start with Windows"), _startMin = C("Start minimized (in tray)"), _confirm = C("Ask before deleting"),
        _hist = C("Record history for statistics"), _hund = C("Show hundredths of a second on stopwatches"), _h24 = C("24-hour clock"),
        _over = C("Count overtime after a timer finishes (shows -00:00:12)"),
        _wSnap = C("Snap widgets to screen edges and each other");
    readonly NumericUpDown _minSess = new() { Minimum = 0, Maximum = 600, Width = Theme.S(60) };
    readonly ComboBox _sound = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(160) };
    readonly TrackBar _vol = new() { Minimum = 0, Maximum = 100, TickFrequency = 10, Width = Theme.S(200) };
    readonly CheckBox _autoBk = C("Back up my data automatically"), _bkFiles = C("Include widget background images and custom alarm sounds");
    readonly ComboBox _bkFreq = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(190) };
    readonly NumericUpDown _bkKeep = new() { Minimum = 1, Maximum = 365, Width = Theme.S(60) };
    readonly TextBox _bkFolder = new() { ReadOnly = true, Width = Theme.S(250), BackColor = Color.White };
    readonly Label _bkLast = new() { AutoSize = true, ForeColor = Theme.TextDim, MaximumSize = new Size(Theme.S(440), 0), Margin = new Padding(0, Theme.S(4), 0, 0) };
    string _bkFolderValue;

    static CheckBox C(string t) => new() { Text = t, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };

    public SettingsForm()
    {
        Text = "Settings";
        Icon = Program.AppIcon;
        Font = Theme.Normal;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Color.White;

        _minTray.Checked = _s.MinimizeToTray; _closeTray.Checked = _s.CloseToTray; _startWin.Checked = Autostart.IsEnabled; _startMin.Checked = _s.StartMinimized;
        _confirm.Checked = _s.ConfirmDelete; _hist.Checked = _s.RecordHistory; _hund.Checked = _s.ShowHundredths; _h24.Checked = _s.Use24Hour; _over.Checked = _s.TimerOvertime;
        _wSnap.Checked = _s.WidgetSnapToEdges;
        _minSess.Value = (decimal)Math.Clamp(_s.MinSessionSeconds, 0, 600);
        _sound.Items.AddRange(Sound.BuiltIn); _sound.SelectedItem = _sound.Items.Contains(_s.DefaultSound) ? _s.DefaultSound : "Digital";
        _vol.Value = Math.Clamp(_s.SoundVolume, 0, 100);

        _autoBk.Checked = _s.AutoBackup; _bkFiles.Checked = _s.BackupIncludeFiles;
        _bkFreq.Items.AddRange(Backup.Frequencies); _bkFreq.SelectedIndex = Math.Clamp(_s.AutoBackupFrequency, 0, Backup.Frequencies.Length - 1);
        _bkKeep.Value = Math.Clamp(_s.BackupKeep, 1, 365);
        _bkFolderValue = _s.BackupFolder ?? "";
        _bkFolder.Text = string.IsNullOrWhiteSpace(_bkFolderValue) ? Backup.DefaultFolder : _bkFolderValue;
        _autoBk.CheckedChanged += (a, b) => { _bkFreq.Enabled = _bkKeep.Enabled = _autoBk.Checked; };
        _bkFreq.Enabled = _bkKeep.Enabled = _autoBk.Checked;
        UpdateLastBackup();

        var outer = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false, Padding = new Padding(Theme.S(14), Theme.S(14), Theme.S(14), Theme.S(4)), Dock = DockStyle.Fill };
        var root = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, Theme.S(12), 0) };
        var right = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        outer.Controls.Add(root); outer.Controls.Add(right);
        GroupBox G(string title, params Control[] cs)
        {
            var g = new GroupBox { Text = title, AutoSize = true, Padding = new Padding(Theme.S(10), Theme.S(4), Theme.S(10), Theme.S(6)), Margin = new Padding(0, 0, 0, Theme.S(8)), MinimumSize = new Size(Theme.S(440), 0) };
            var f = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
            f.Controls.AddRange(cs);
            g.Controls.Add(f);
            return g;
        }
        FlowLayoutPanel Row(string l, params Control[] cs)
        {
            var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
            f.Controls.Add(new Label { Text = l, AutoSize = true, Margin = new Padding(0, Theme.S(5), Theme.S(6), 0) });
            f.Controls.AddRange(cs);
            return f;
        }
        var test = new Button { Text = "▶", Width = Theme.S(30), Height = Theme.S(24) };
        test.Click += (a, b) => Sound.Play((string)_sound.SelectedItem, "", false, _vol.Value);
        root.Controls.Add(G("General", _minTray, _closeTray, _startWin, _startMin, _confirm, _h24));
        root.Controls.Add(G("Timers && stopwatches", _hund, _over, Row("Default sound for new timers:", _sound, test), Row("Volume:", _vol)));
        var defStyle = new Button { Text = "  Default widget style (font, colours, background)…", AutoSize = true, Height = Theme.S(28), Image = Icons.Get("style", 16), TextImageRelation = TextImageRelation.ImageBeforeText };
        defStyle.Click += (a, b) => { using var f = new WidgetStyleForm(null, true); f.ShowDialog(this); };
        right.Controls.Add(G("Desktop widgets", _wSnap, defStyle, new Label { Text = "Each widget can also have its own style: hover it and click the brush,\nor right-click it → Font, colours & background.", AutoSize = true, ForeColor = Theme.TextDim, UseMnemonic = false }));
        root.Controls.Add(G("Statistics", _hist, Row("Ignore sessions shorter than", _minSess, new Label { Text = "seconds", AutoSize = true, Margin = new Padding(Theme.S(4), Theme.S(5), 0, 0) })));
        var dataLink = new LinkLabel { Text = "Open data folder (" + AppState.DataDir + ")", AutoSize = true, MaximumSize = new Size(Theme.S(440), 0) };
        dataLink.LinkClicked += (a, b) => { try { Security.SafeShell.OpenFolder(AppState.DataDir); } catch { } };
        root.Controls.Add(dataLink);

        // ---- Backup
        Button Btn(string t, string icon)
        {
            var bt = new Button { Text = " " + t, AutoSize = true, Height = Theme.S(30), Image = icon == null ? null : Icons.Get(icon, Theme.S(18)), TextImageRelation = TextImageRelation.ImageBeforeText, Margin = new Padding(0, 0, Theme.S(6), Theme.S(4)), Padding = new Padding(Theme.S(4), 0, Theme.S(4), 0) };
            return bt;
        }
        var bkNow = Btn("Back up now…", "backup");
        var bkRestore = Btn("Restore from backup…", "restore");
        var bkOpen = new LinkLabel { Text = "Open backup folder", AutoSize = true, Margin = new Padding(0, Theme.S(8), 0, 0) };
        var bkChange = new Button { Text = "Change…", AutoSize = true, Height = Theme.S(24), Margin = new Padding(Theme.S(4), 0, 0, 0) };
        var bkDefault = new LinkLabel { Text = "Default", AutoSize = true, Margin = new Padding(Theme.S(4), Theme.S(5), 0, 0) };
        bkNow.Click += (a, b) => { ApplyBackupSettings(); Backup.BackupNowInteractive(this); UpdateLastBackup(); };
        bkRestore.Click += (a, b) =>
        {
            ApplyBackupSettings();
            if (Backup.RestoreInteractive(this)) DialogResult = DialogResult.Abort; // the app restarts
        };
        bkOpen.LinkClicked += (a, b) => { ApplyBackupSettings(); Backup.ShowInExplorer(_s.LastBackupFile); };
        bkChange.Click += (a, b) =>
        {
            using var fb = new FolderBrowserDialog { Description = "Choose where backups are saved (e.g. a OneDrive, Google Drive or USB folder)", UseDescriptionForTitle = true, SelectedPath = _bkFolder.Text, ShowNewFolderButton = true };
            if (fb.ShowDialog(this) == DialogResult.OK) { _bkFolderValue = fb.SelectedPath; _bkFolder.Text = fb.SelectedPath; }
        };
        bkDefault.LinkClicked += (a, b) => { _bkFolderValue = ""; _bkFolder.Text = Backup.DefaultFolder; };
        var bkButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, Theme.S(2), 0, Theme.S(4)) };
        bkButtons.Controls.AddRange(new Control[] { bkNow, bkRestore, bkOpen });
        var bkHint = new Label
        {
            Text = "A backup holds all your timers, stopwatches, laps, statistics history, groups, widget styles and settings in one .stcbackup file. " +
                   "Tip: choose a OneDrive / Google Drive / USB folder so a copy is kept off this PC.",
            AutoSize = true, MaximumSize = new Size(Theme.S(440), 0), ForeColor = Theme.TextDim, Margin = new Padding(0, Theme.S(6), 0, 0)
        };
        right.Controls.Add(G("Backup", bkButtons,
            _autoBk,
            Row("     How often:", _bkFreq),
            Row("     Keep the last", _bkKeep, new Label { Text = "automatic backups (older ones are deleted)", AutoSize = true, Margin = new Padding(Theme.S(4), Theme.S(5), 0, 0) }),
            Row("Backup folder:", _bkFolder, bkChange, bkDefault),
            _bkFiles, _bkLast, bkHint));

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(Theme.S(8)), BackColor = Theme.HeaderBack };
        var ok = new Button { Text = "OK", Width = Theme.S(90), Height = Theme.S(28) };
        var cancel = new Button { Text = "Cancel", Width = Theme.S(90), Height = Theme.S(28), DialogResult = DialogResult.Cancel };
        ok.Click += (a, b) => { Save(); DialogResult = DialogResult.OK; };
        buttons.Controls.Add(cancel); buttons.Controls.Add(ok);
        Controls.Add(outer);
        Controls.Add(buttons);
        AcceptButton = ok; CancelButton = cancel;
        FormClosed += (a, b) => Sound.Stop();
    }

    void UpdateLastBackup()
    {
        string t;
        if (_s.LastBackupLocal is DateTime d)
        {
            string day = d.Date == DateTime.Today ? "Today" : d.Date == DateTime.Today.AddDays(-1) ? "Yesterday" : d.ToString("ddd d MMM yyyy");
            t = $"Last backup: {day}, {Fmt.Clock(d)}" + (string.IsNullOrEmpty(_s.LastBackupFile) ? "" : "  (" + Path.GetFileName(_s.LastBackupFile) + ")");
        }
        else t = "Last backup: never";
        if (!string.IsNullOrEmpty(_s.LastBackupError)) t += "\n⚠ Last automatic backup failed: " + _s.LastBackupError;
        _bkLast.Text = t;
        _bkLast.ForeColor = string.IsNullOrEmpty(_s.LastBackupError) ? Theme.TextDim : Color.Firebrick;
    }

    void ApplyBackupSettings()
    {
        _s.AutoBackup = _autoBk.Checked;
        _s.AutoBackupFrequency = Math.Max(0, _bkFreq.SelectedIndex);
        _s.BackupKeep = (int)_bkKeep.Value;
        _s.BackupFolder = _bkFolderValue ?? "";
        _s.BackupIncludeFiles = _bkFiles.Checked;
    }

    void Save()
    {
        ApplyBackupSettings();
        _s.MinimizeToTray = _minTray.Checked; _s.CloseToTray = _closeTray.Checked; _s.StartMinimized = _startMin.Checked;
        _s.ConfirmDelete = _confirm.Checked; _s.RecordHistory = _hist.Checked; _s.ShowHundredths = _hund.Checked; _s.Use24Hour = _h24.Checked; _s.TimerOvertime = _over.Checked;
        _s.WidgetSnapToEdges = _wSnap.Checked;
        _s.MinSessionSeconds = (double)_minSess.Value;
        _s.DefaultSound = (string)_sound.SelectedItem;
        _s.SoundVolume = _vol.Value;
        if (Autostart.IsEnabled != _startWin.Checked) Autostart.Set(_startWin.Checked);
        _s.StartWithWindows = _startWin.Checked;
        AppState.Save();
    }
}

/// <summary>"Start with Windows" via HKCU\...\Run (no admin rights needed).</summary>
public static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "StudentTimerClock";
    static string Command => $"\"{Environment.ProcessPath}\" --autostart";

    public static bool IsEnabled
    {
        get
        {
            try { using var k = Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue(ValueName) is string; }
            catch { return false; }
        }
    }

    public static void Set(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (on) key.SetValue(ValueName, Command);
            else key.DeleteValue(ValueName, false);
        }
        catch (Exception ex) { MessageBox.Show("Could not change \"Start with Windows\": " + ex.Message, "Student Timer Clock"); }
    }

    /// <summary>
    /// First run: honour the choice made in the installer (HKCU\Software\StudentTimerClock\AutostartChoice);
    /// without an installer value autostart is ON by default. Later runs: keep the Run entry pointing at this exe.
    /// </summary>
    public static void OnStartup()
    {
        var st = AppState.Settings;
        try
        {
            if (!st.FirstRunDone)
            {
                bool want = true;
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\StudentTimerClock"))
                    if (k?.GetValue("AutostartChoice") is int v) want = v != 0;
                Set(want);
                st.StartWithWindows = want;
                st.FirstRunDone = true;
                AppState.Save();
            }
            else if (IsEnabled)
            {
                using var k = Registry.CurrentUser.OpenSubKey(RunKey);
                if (!string.Equals(k?.GetValue(ValueName) as string, Command, StringComparison.OrdinalIgnoreCase)) Set(true); // exe was moved / updated
            }
        }
        catch { }
    }
}

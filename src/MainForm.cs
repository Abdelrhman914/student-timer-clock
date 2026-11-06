namespace StudentTimerClock;

public class MainForm : Form
{
    readonly Ribbon _ribbon = new();
    readonly NavBar _nav = new();
    readonly ItemGrid _timers = new(ItemKind.Timer) { Dock = DockStyle.Fill };
    readonly ItemGrid _stopwatches = new(ItemKind.Stopwatch) { Dock = DockStyle.Fill };
    readonly StatsPage _stats = new() { Dock = DockStyle.Fill };
    readonly Panel _host = new() { Dock = DockStyle.Fill, Padding = new Padding(1) };
    readonly Panel _searchBar = new() { Dock = DockStyle.Top };
    readonly TextBox _search = new() { PlaceholderText = "Search (Ctrl+F)", BorderStyle = BorderStyle.FixedSingle };
    readonly Label _pageTitle = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
    readonly StatusStrip _status = new() { SizingGrip = true };
    readonly ToolStripStatusLabel _next = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft, ImageAlign = ContentAlignment.MiddleLeft };
    readonly ToolStripStatusLabel _running = new() { TextAlign = ContentAlignment.MiddleRight };
    readonly NotifyIcon _tray = new();
    readonly System.Windows.Forms.Timer _ui = new() { Interval = 100 };
    readonly Dictionary<string, WidgetForm> _widgets = new();
    readonly Dictionary<string, StopwatchWindow> _swWindows = new();
    readonly List<AlertForm> _alerts = new();
    readonly Stack<(TimeItem item, int index)> _undo = new();
    TimerItem _soundOwner;
    bool _exiting, _trayHintShown;
    int _lastSecond = -1, _statsCounter;
    readonly bool _startHidden;

    public MainForm(bool startHidden)
    {
        _startHidden = startHidden;
        Text = "Student Timer Clock";
        Icon = Program.AppIcon;
        Font = Theme.Normal;
        BackColor = Color.White;
        MinimumSize = new Size(Theme.S(980), Theme.S(500));
        KeyPreview = true;
        var st = AppState.Settings;
        StartPosition = FormStartPosition.Manual;
        var bounds = new Rectangle(st.WinX, st.WinY, Math.Max(MinimumSize.Width, st.WinW), Math.Max(MinimumSize.Height, st.WinH));
        if (st.WinX < 0 || !Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(bounds)))
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            bounds = new Rectangle(wa.X + (wa.Width - Theme.S(1100)) / 2, wa.Y + (wa.Height - Theme.S(680)) / 2, Math.Min(wa.Width, Theme.S(1100)), Math.Min(wa.Height, Theme.S(680)));
        }
        Bounds = bounds;
        if (st.WinMax) WindowState = FormWindowState.Maximized;

        BuildRibbon();
        _nav.Items.Add(new("Timers", "timers"));
        _nav.Items.Add(new("Stopwatches", "stopwatches"));
        _nav.Items.Add(new("Statistics", "stats"));
        _nav.Badge = i =>
        {
            int n = i == 0 ? AppState.Data.Timers.Count(t => t.IsRunning) : i == 1 ? AppState.Data.Stopwatches.Count(s => s.IsRunning) : 0;
            return n > 0 ? n.ToString() : null;
        };
        _nav.SelectedChanged += i => ShowPage(i);

        // search bar above grid
        _searchBar.Height = Theme.S(36);
        _searchBar.BackColor = Color.White;
        _searchBar.Padding = new Padding(Theme.S(8), Theme.S(6), Theme.S(8), Theme.S(4));
        _search.Width = Theme.S(230);
        _search.Dock = DockStyle.Right;
        _search.TextChanged += (s, e) => { _timers.Filter = _search.Text; _stopwatches.Filter = _search.Text; };
        _pageTitle.Dock = DockStyle.Fill;
        _pageTitle.Font = Theme.UI(11f, FontStyle.Bold);
        _pageTitle.ForeColor = Color.FromArgb(50, 50, 50);
        _searchBar.Controls.Add(_pageTitle);
        _searchBar.Controls.Add(_search);
        _searchBar.Paint += (s, e) => { using var p = new Pen(Theme.Border); e.Graphics.DrawLine(p, 0, _searchBar.Height - 1, _searchBar.Width, _searchBar.Height - 1); };

        _host.Controls.Add(_timers);
        _host.Controls.Add(_stopwatches);
        _host.Controls.Add(_stats);
        var right = new Panel { Dock = DockStyle.Fill };
        right.Controls.Add(_host);
        right.Controls.Add(_searchBar);

        // status bar
        _next.Image = Icons.Get("clock", Theme.S(16));
        _status.Items.Add(_next);
        _status.Items.Add(_running);
        _status.BackColor = Theme.HeaderBack;

        Controls.Add(right);
        Controls.Add(_nav);
        Controls.Add(_status);
        Controls.Add(_ribbon);

        foreach (var g in new[] { _timers, _stopwatches })
        {
            g.SelectionChanged += UpdateRibbon;
            g.ItemDoubleClicked += OpenItem;
            g.ActionClicked += OnGridAction;
            g.ContextMenuRequested += ShowItemMenu;
        }

        SetupTray();

        AppState.Changed += OnStateChanged;
        AppState.TimerFinished += OnTimerFinished;
        AppState.WidgetRequested += item => OpenWidget(item);

        _ui.Tick += (s, e) => OnUiTick();
        var secWarn = new System.Windows.Forms.Timer { Interval = 1500 };
        secWarn.Tick += (s, e) =>
        {
            secWarn.Stop(); secWarn.Dispose();
            var w = AppState.PendingWarning; AppState.PendingWarning = null;
            if (w != null) MessageBox.Show(this, w, "Student Timer Clock - security notice", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };
        secWarn.Start();
        _nav.Selected = Math.Clamp(st.LastPage, 0, 2);
        ShowPage(_nav.Selected);
    }

    protected override void SetVisibleCore(bool value)
    {
        if (_startHidden && !IsHandleCreated) { CreateHandle(); value = false; }
        base.SetVisibleCore(value);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // restore widgets & catch timers that finished while the app was closed
        BeginInvoke(() =>
        {
            foreach (var it in AppState.AllItems.Where(i => i.WidgetOpen).ToList()) OpenWidget(it);
            AppState.Tick(startup: true);
            _ui.Start();
        });
    }

    // ------------------------------------------------------------------ ribbon
    void BuildRibbon()
    {
        var edit = new RibbonGroup { Title = "Editing" };
        var addMenu = new ContextMenuStrip();
        addMenu.Items.Add("New timer…", Icons.Get("timers", 16), (s, e) => AddItem(ItemKind.Timer));
        addMenu.Items.Add("New stopwatch…", Icons.Get("stopwatches", 16), (s, e) => AddItem(ItemKind.Stopwatch));
        edit.Buttons.Add(new RibbonButton { Id = "add", Text = "Add", Icon = "add", Tooltip = "Add a new timer or stopwatch (Ctrl+N)", Click = () => AddCurrent() });
        edit.Buttons.Add(new RibbonButton { Id = "edit", Text = "Edit", Icon = "edit", Tooltip = "Edit the selected item (F2)", Click = () => { if (Sel != null) EditItem(Sel); } });
        edit.Buttons.Add(new RibbonButton { Id = "copy", Text = "Copy", Icon = "copy", Tooltip = "Duplicate the selected item (Ctrl+D)", Click = CopySelected });
        edit.Buttons.Add(new RibbonButton { Id = "delete", Text = "Delete", Icon = "delete", Tooltip = "Delete the selected item (Del)", Click = DeleteSelected });
        edit.Buttons.Add(new RibbonButton { Id = "undo", Text = "Undo", Icon = "undo", Tooltip = "Undo delete (Ctrl+Z)", Click = Undo });
        _addMenu = addMenu;

        var ctl = new RibbonGroup { Title = "Control" };
        ctl.Buttons.Add(new RibbonButton { Id = "toggle", Text = "Start", Icon = "start", Tooltip = "Start / pause the selected item (Space)", Click = () => { if (Sel != null) AppState.Toggle(Sel); } });
        ctl.Buttons.Add(new RibbonButton { Id = "reset", Text = "Reset", Icon = "reset", Tooltip = "Reset the selected item", Click = () => { if (Sel != null) ResetItem(Sel); } });
        ctl.Buttons.Add(new RibbonButton { Id = "lap", Text = "Lap", Icon = "lap", Tooltip = "Record a lap (L)", Click = () => { if (Sel is StopwatchItem s && s.IsRunning) AppState.Lap(s); } });
        ctl.Buttons.Add(new RibbonButton { Id = "pauseall", Text = "Pause all", Icon = "pause", Large = false, Click = () => { foreach (var i in AppState.AllItems.Where(i => i.IsRunning).ToList()) AppState.Pause(i); } });
        ctl.Buttons.Add(new RibbonButton { Id = "openwin", Text = "Stopwatch window", Icon = "stopwatches", Large = false, Click = () => { if (Sel is StopwatchItem s) OpenItem(s); } });
        ctl.Buttons.Add(new RibbonButton { Id = "laprep", Text = "Lap report", Icon = "report", Large = false, Click = () => { if (Sel is StopwatchItem s) new LapReportForm(s).Show(this); } });

        var wid = new RibbonGroup { Title = "Desktop" };
        wid.Buttons.Add(new RibbonButton { Id = "widget", Text = "Float Widget", Icon = "widget", Tooltip = "Show the selected item as a floating desktop widget (Ctrl+W)", Click = () => { if (Sel != null) ToggleWidget(Sel); } });
        wid.Buttons.Add(new RibbonButton { Id = "wstyle", Text = "Widget Style", Icon = "style", Tooltip = "Font, colours, background, layout and size of the selected item's widget", Click = EditWidgetStyle });
        wid.Buttons.Add(new RibbonButton { Id = "board", Text = "Board", Icon = "board", Tooltip = "Show several timers as big tiles - fullscreen for the classroom (Ctrl+B)", Click = ShowBoard });
        wid.Buttons.Add(new RibbonButton { Id = "showall", Text = "Widgets for running", Icon = "start", Large = false, Click = () => { foreach (var i in AppState.AllItems.Where(i => i.IsRunning)) OpenWidget(i); } });
        wid.Buttons.Add(new RibbonButton { Id = "closeall", Text = "Close all widgets", Icon = "delete", Large = false, Click = CloseAllWidgets });
        wid.Buttons.Add(new RibbonButton { Id = "totray", Text = "Hide to tray", Icon = "tray", Large = false, Click = HideToTray });

        var grid = new RibbonGroup { Title = "Grid" };
        grid.Buttons.Add(new RibbonButton { Id = "collapse", Text = "Collapse All", Icon = "collapse", Large = false, Click = () => CurrentGrid?.SetAllCollapsed(true) });
        grid.Buttons.Add(new RibbonButton { Id = "expand", Text = "Expand All", Icon = "expand", Large = false, Click = () => CurrentGrid?.SetAllCollapsed(false) });

        var info = new RibbonGroup { Title = "Information" };
        info.Buttons.Add(new RibbonButton { Id = "stats", Text = "Statistics", Icon = "stats", Tooltip = "Graphs of how long you spent on each activity", Click = () => _nav.Selected = 2 });
        info.Buttons.Add(new RibbonButton { Id = "settings", Text = "Settings", Icon = "settings", Click = ShowSettings });
        info.Buttons.Add(new RibbonButton { Id = "help", Text = "Help", Icon = "help", Click = () => ShowHelp() });

        _ribbon.Groups.AddRange(new[] { edit, ctl, wid, grid, info });

        var file = new ContextMenuStrip();
        file.Items.Add("New timer…", Icons.Get("timers", 16), (s, e) => AddItem(ItemKind.Timer));
        file.Items.Add("New stopwatch…", Icons.Get("stopwatches", 16), (s, e) => AddItem(ItemKind.Stopwatch));
        file.Items.Add(new ToolStripSeparator());
        file.Items.Add("Settings…", Icons.Get("settings", 16), (s, e) => ShowSettings());
        file.Items.Add("Default widget style…", Icons.Get("style", 16), (s, e) => { using var f = new WidgetStyleForm(null, true); f.ShowDialog(this); RefreshWidgets(); });
        file.Items.Add("Board view", Icons.Get("board", 16), (s, e) => ShowBoard());
        file.Items.Add("Terms && conditions", Icons.Get("terms", 16), (s, e) => ShowHelp(1));
        file.Items.Add("About Student Timer Clock", Icons.Get("help", 16), (s, e) => ShowHelp(2));
        file.Items.Add(new ToolStripSeparator());
        file.Items.Add("Back up my data now…", Icons.Get("backup", 16), (s, e) => Backup.BackupNowInteractive(this));
        file.Items.Add("Restore from backup…", Icons.Get("restore", 16), (s, e) => Backup.RestoreInteractive(this));
        file.Items.Add("Backup settings…", null, (s, e) => ShowSettings());
        file.Items.Add("Open backup folder", null, (s, e) => Backup.ShowInExplorer(AppState.Settings.LastBackupFile));
        file.Items.Add("Open data folder", null, (s, e) => { try { Security.SafeShell.OpenFolder(AppState.DataDir); } catch { } });
        file.Items.Add(new ToolStripSeparator());
        file.Items.Add("Exit", null, (s, e) => ExitApp());
        _ribbon.FileMenu = file;
    }
    ContextMenuStrip _addMenu;

    ItemGrid CurrentGrid => _nav.Selected == 0 ? _timers : _nav.Selected == 1 ? _stopwatches : null;
    TimeItem Sel => CurrentGrid?.Selected;

    void ShowPage(int i)
    {
        _timers.Visible = i == 0; _stopwatches.Visible = i == 1; _stats.Visible = i == 2;
        _search.Visible = i != 2;
        _pageTitle.Text = i == 0 ? "  Timers  -  countdowns that alert you when time is up" : i == 1 ? "  Stopwatches  -  track how long you spend on things" : "  Statistics  -  how long you did what";
        if (i == 2) _stats.RefreshData();
        else CurrentGrid.Focus();
        AppState.Settings.LastPage = i;
        UpdateRibbon();
    }

    void UpdateRibbon()
    {
        var sel = Sel;
        bool grid = CurrentGrid != null;
        _ribbon.SetEnabled("edit", sel != null);
        _ribbon.SetEnabled("copy", sel != null);
        _ribbon.SetEnabled("delete", sel != null);
        _ribbon.SetEnabled("undo", _undo.Count > 0);
        _ribbon.SetEnabled("toggle", sel != null);
        _ribbon.SetEnabled("reset", sel != null);
        _ribbon.SetEnabled("widget", sel != null);
        _ribbon.SetEnabled("collapse", grid);
        _ribbon.SetEnabled("expand", grid);
        _ribbon.SetEnabled("pauseall", AppState.AllItems.Any(i => i.IsRunning));
        _ribbon.SetEnabled("closeall", _widgets.Count > 0);
        bool sw = _nav.Selected == 1;
        _ribbon.SetEnabled("lap", sel is StopwatchItem s && s.IsRunning);
        _ribbon.SetEnabled("openwin", sel is StopwatchItem);
        _ribbon.SetEnabled("laprep", sel is StopwatchItem);
        var lap = _ribbon["lap"];
        if (lap != null && lap.Visible != sw) { lap.Visible = sw; _ribbon["openwin"].Visible = sw; _ribbon["laprep"].Visible = sw; _ribbon.DoLayout(); _ribbon.Invalidate(); }
        _ribbon.SetButton("toggle", sel?.IsRunning == true ? "Pause" : sel != null && sel.ElapsedBefore > 0 && !(sel is TimerItem t && t.Finished) ? "Resume" : "Start", sel?.IsRunning == true ? "pause" : "start");
        _ribbon.SetButton("widget", sel != null && _widgets.ContainsKey(sel.Id) ? "Close Widget" : "Float Widget", "widget");
        _ribbon["add"].DropDown = _nav.Selected == 2 ? _addMenu : null;
    }

    // ------------------------------------------------------------------ state events
    void OnStateChanged()
    {
        if (InvokeRequired) { BeginInvoke(OnStateChanged); return; }
        _timers.Rebuild();
        _stopwatches.Rebuild();
        foreach (var w in _widgets.Values) w.Invalidate();
        UpdateRibbon();
        _nav.Invalidate();
        if (_stats.Visible) _stats.RefreshData();
    }

    void OnTimerFinished(TimerItem t, bool whileAway)
    {
        var st = AppState.Settings;
        bool loop = t.LoopSound && !t.AutoRestart;
        Sound.Play(t.Sound, t.CustomSoundPath, loop, st.SoundVolume);
        _soundOwner = t;
        if (t.ShowPopup)
        {
            foreach (var old in _alerts.Where(a => a.Timer == t).ToList()) { _alerts.Remove(old); old.Close(); }
            var a = new AlertForm(t, whileAway);
            _alerts.Add(a);
            a.Show();
        }
        else if (t.Actions == null || t.Actions.Count == 0)
        {
            _tray.ShowBalloonTip(5000, t.Name, "Time is up!", ToolTipIcon.Info);
        }
        if (!_widgets.ContainsKey(t.Id) && !Visible && t.ShowPopup) _tray.ShowBalloonTip(5000, t.Name, "Time is up!", ToolTipIcon.Info);
        ActionRunner.RunForFinished(t, whileAway);
    }

    public void DismissTimer(TimerItem t, bool closeAlerts = true)
    {
        if (t.Finished)
        {
            t.Finished = false; t.FinishedAtUtc = null; t.ElapsedBefore = 0; t.IsRunning = false; t.StartedAtUtc = null;
            AppState.NotifyChanged();
        }
        if (closeAlerts) foreach (var a in _alerts.Where(a => a.Timer == t).ToList()) a.Close();
        if (_soundOwner == t || !_alerts.Any()) { Sound.Stop(); _soundOwner = null; }
    }

    public void AlertClosed(AlertForm a)
    {
        _alerts.Remove(a);
        if (a.Timer.Finished) DismissTimer(a.Timer, false);
        if (_alerts.Count == 0) { Sound.Stop(); _soundOwner = null; }
    }

    void OnUiTick()
    {
        AppState.Tick();
        var now = DateTime.Now;
        bool anyRunning = AppState.AllItems.Any(i => i.IsRunning);
        bool anyFinished = AppState.Data.Timers.Any(t => t.Finished);
        if (Visible && WindowState != FormWindowState.Minimized && (anyRunning || anyFinished))
            CurrentGrid?.Invalidate();

        // stop a looping sound if its timer was handled somewhere else (widget/grid)
        if (_soundOwner != null && !_soundOwner.Finished && _alerts.Count == 0 && !_soundOwner.AutoRestart) { Sound.Stop(); _soundOwner = null; }

        if (now.Second != _lastSecond)
        {
            _lastSecond = now.Second;
            Text = $"{now:dd.MM.yyyy} {Fmt.Clock(now)} {now:ddd} - Student Timer Clock";
            _nav.Invalidate();
            UpdateStatus();
            if (_stats.Visible && anyRunning && ++_statsCounter % 5 == 0) _stats.RefreshData();
            if (now.Second == 0 && anyRunning) AppState.Save(); // periodic safety save
            if (now.Second == 30 && !RestartRequested) Backup.AutoBackupIfDue(); // automatic backup check (once a minute)
            if (!anyRunning) CurrentGrid?.Invalidate(); // keep clock column ticking
        }
    }

    void UpdateStatus()
    {
        var nowU = DateTime.UtcNow;
        var next = AppState.Data.Timers.Where(t => t.IsRunning && !t.Finished).OrderBy(t => t.Remaining(nowU)).FirstOrDefault();
        string txt = next != null
            ? $"Next timer: {next.Name}  -  {Fmt.Long(Math.Ceiling(next.Remaining(nowU) - 0.0001))} left (ends {WidgetRenderer.When(DateTime.Now.AddSeconds(next.Remaining(nowU)), true)})"
            : "No timers running";
        if (_next.Text != txt) _next.Text = txt;
        int r = AppState.AllItems.Count(i => i.IsRunning);
        _running.Text = $"{r} running  •  {_widgets.Count} widget(s)  •  today: {Fmt.Human(TodayTotal())}";
        string tip = next != null ? $"Student Timer Clock\n{next.Name}: {Fmt.Remaining(next.Remaining(nowU))}" : "Student Timer Clock";
        _tray.Text = tip.Length > 63 ? tip[..63] : tip;
    }

    static double TodayTotal()
    {
        var today = DateTime.Today.ToUniversalTime(); var now = DateTime.UtcNow;
        double s = AppState.Data.History.Where(h => h.Kind != "Lap" && h.EndUtc >= today).Sum(h => h.Seconds);
        s += AppState.AllItems.Where(i => i.IsRunning && i.StartedAtUtc.HasValue).Sum(i => (now - (i.StartedAtUtc.Value < today ? today : i.StartedAtUtc.Value)).TotalSeconds);
        return s;
    }

    // ------------------------------------------------------------------ item actions
    void AddCurrent()
    {
        if (_nav.Selected == 1) AddItem(ItemKind.Stopwatch);
        else if (_nav.Selected == 0) AddItem(ItemKind.Timer);
        else _addMenu.Show(_ribbon, new Point(_ribbon["add"].Bounds.X, _ribbon.Height));
    }

    void AddItem(ItemKind kind)
    {
        string group = Sel?.Group ?? "General";
        int ci = (kind == ItemKind.Timer ? AppState.Data.Timers.Count : AppState.Data.Stopwatches.Count) % (Palette.Colors.Length - 1);
        if (kind == ItemKind.Timer)
        {
            var t = new TimerItem { Name = "", Group = group, DurationSeconds = 600, ColorArgb = Palette.Colors[ci].ToArgb(), Sound = AppState.Settings.DefaultSound };
            using var f = new TimerEditForm(t, true);
            if (f.ShowDialog(this) != DialogResult.OK) return;
            f.Apply();
            AppState.Data.Timers.Add(t);
            if (_nav.Selected != 0) _nav.Selected = 0;
            _timers.Rebuild(); _timers.Selected = t;
            if (f.StartImmediately) AppState.Start(t);
            if (f.WantWidget) OpenWidget(t);
        }
        else
        {
            var s = new StopwatchItem { Name = "", Group = group, ColorArgb = Palette.Colors[ci].ToArgb(), IconKey = "clock" };
            using var f = new StopwatchEditForm(s, true);
            if (f.ShowDialog(this) != DialogResult.OK) return;
            f.Apply();
            AppState.Data.Stopwatches.Add(s);
            if (_nav.Selected != 1) _nav.Selected = 1;
            _stopwatches.Rebuild(); _stopwatches.Selected = s;
            if (f.StartImmediately) AppState.Start(s);
            if (f.WantWidget) OpenWidget(s);
        }
        AppState.NotifyChanged();
    }

    public void EditItem(TimeItem item)
    {
        string oldName = item.Name;
        bool ok; bool wantWidget;
        if (item is TimerItem t)
        {
            using var f = new TimerEditForm(t, false);
            ok = f.ShowDialog(this) == DialogResult.OK;
            if (ok) f.Apply();
            wantWidget = ok && f.WantWidget; if (ok && f.StartImmediately) AppState.Start(t);
        }
        else
        {
            var s = (StopwatchItem)item;
            using var f = new StopwatchEditForm(s, false);
            ok = f.ShowDialog(this) == DialogResult.OK;
            if (ok) f.Apply();
            wantWidget = ok && f.WantWidget; if (ok && f.StartImmediately) AppState.Start(s);
        }
        if (!ok) return;
        if (oldName != item.Name && MessageBox.Show(this, $"Also rename \"{oldName}\" to \"{item.Name}\" in your statistics history?", "Rename", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            foreach (var h in AppState.Data.History.Where(h => h.Kind != "Lap" && h.Name == oldName)) h.Name = item.Name;
        if (wantWidget) OpenWidget(item);
        else if (_widgets.TryGetValue(item.Id, out var w)) w.ApplySettings();
        AppState.NotifyChanged();
    }

    public void OpenItem(TimeItem item)
    {
        if (item is StopwatchItem s)
        {
            if (_swWindows.TryGetValue(s.Id, out var w) && !w.IsDisposed) { if (w.WindowState == FormWindowState.Minimized) w.WindowState = FormWindowState.Normal; w.Activate(); return; }
            w = new StopwatchWindow(s);
            _swWindows[s.Id] = w;
            w.FormClosed += (a, b) => _swWindows.Remove(s.Id);
            w.Show();
        }
        else
        {
            ShowMain();
            EditItem(item);
        }
    }

    void ResetItem(TimeItem item)
    {
        if (item is TimerItem t && t.Finished) { DismissTimer(t); return; }
        AppState.Reset(item);
    }

    void CopySelected()
    {
        var s = Sel; if (s == null) return;
        if (s is TimerItem t)
        {
            var c = t.Clone(); c.Name = t.Name + " (copy)";
            AppState.Data.Timers.Insert(AppState.Data.Timers.IndexOf(t) + 1, c);
            AppState.NotifyChanged(); _timers.Selected = c;
        }
        else if (s is StopwatchItem sw)
        {
            var c = sw.Clone(); c.Name = sw.Name + " (copy)";
            AppState.Data.Stopwatches.Insert(AppState.Data.Stopwatches.IndexOf(sw) + 1, c);
            AppState.NotifyChanged(); _stopwatches.Selected = c;
        }
    }

    void DeleteSelected()
    {
        var s = Sel; if (s == null) return;
        if (AppState.Settings.ConfirmDelete && MessageBox.Show(this, $"Delete \"{s.Name}\"?\n(Its recorded statistics are kept. You can Undo.)", "Delete", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        if (s.IsRunning) AppState.Pause(s);
        if (_widgets.TryGetValue(s.Id, out var w)) { w.Close(); _widgets.Remove(s.Id); }
        s.WidgetOpen = false;
        if (_swWindows.TryGetValue(s.Id, out var sw)) sw.Close();
        foreach (var a in _alerts.Where(a => a.Timer == s).ToList()) a.Close();
        int idx;
        if (s is TimerItem t) { idx = AppState.Data.Timers.IndexOf(t); AppState.Data.Timers.Remove(t); }
        else { idx = AppState.Data.Stopwatches.IndexOf((StopwatchItem)s); AppState.Data.Stopwatches.Remove((StopwatchItem)s); }
        _undo.Push((s, idx));
        AppState.NotifyChanged();
    }

    void Undo()
    {
        if (_undo.Count == 0) return;
        var (item, idx) = _undo.Pop();
        if (item is TimerItem t) { AppState.Data.Timers.Insert(Math.Clamp(idx, 0, AppState.Data.Timers.Count), t); _nav.Selected = 0; AppState.NotifyChanged(); _timers.Selected = t; }
        else { var s = (StopwatchItem)item; AppState.Data.Stopwatches.Insert(Math.Clamp(idx, 0, AppState.Data.Stopwatches.Count), s); _nav.Selected = 1; AppState.NotifyChanged(); _stopwatches.Selected = s; }
    }

    void OnGridAction(TimeItem it, string action)
    {
        switch (action)
        {
            case "toggle": if (it is TimerItem t && t.Finished) { DismissTimer(t); AppState.Start(t); } else AppState.Toggle(it); break;
            case "reset": ResetItem(it); break;
            case "lap": if (it is StopwatchItem s && s.IsRunning) AppState.Lap(s); break;
            case "widget": ToggleWidget(it); break;
        }
        UpdateRibbon();
    }

    void ShowItemMenu(TimeItem it, Point p)
    {
        var m = new ContextMenuStrip();
        if (it != null)
        {
            m.Items.Add(it.IsRunning ? "Pause" : "Start", Icons.Get(it.IsRunning ? "pause" : "start", 16), (s, e) => OnGridAction(it, "toggle"));
            m.Items.Add("Reset", Icons.Get("reset", 16), (s, e) => ResetItem(it));
            if (it is StopwatchItem sw)
            {
                m.Items.Add("Lap", Icons.Get("lap", 16), (s, e) => AppState.Lap(sw)).Enabled = sw.IsRunning;
                m.Items.Add("Open stopwatch window", Icons.Get("stopwatches", 16), (s, e) => OpenItem(sw));
                m.Items.Add("Lap report", Icons.Get("report", 16), (s, e) => new LapReportForm(sw).Show(this));
            }
            if (it is TimerItem tm)
            {
                m.Items.Add("+1 minute", null, (s, e) => AppState.AddTime(tm, 60));
                m.Items.Add("+5 minutes", null, (s, e) => AppState.AddTime(tm, 300));
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(_widgets.ContainsKey(it.Id) ? "Close desktop widget" : "Float as desktop widget", Icons.Get("widget", 16), (s, e) => ToggleWidget(it));
            m.Items.Add("Edit…", Icons.Get("edit", 16), (s, e) => EditItem(it));
            m.Items.Add("Copy", Icons.Get("copy", 16), (s, e) => CopySelected());
            var color = new ToolStripMenuItem("Color");
            foreach (var c in Palette.Colors)
            {
                var bmp = new Bitmap(16, 16); using (var g = Graphics.FromImage(bmp)) { g.Clear(c); g.DrawRectangle(Pens.Gray, 0, 0, 15, 15); }
                var cc = c;
                color.DropDownItems.Add(new ToolStripMenuItem("", bmp, (s, e) => { it.Color = cc; AppState.NotifyChanged(); }) { Checked = it.ColorArgb == c.ToArgb() });
            }
            m.Items.Add(color);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Delete", Icons.Get("delete", 16), (s, e) => DeleteSelected());
        }
        else
        {
            m.Items.Add(_nav.Selected == 0 ? "New timer…" : "New stopwatch…", Icons.Get("add", 16), (s, e) => AddCurrent());
        }
        m.Show(CurrentGrid, p);
    }

    // ------------------------------------------------------------------ widgets
    public void OpenWidget(TimeItem item)
    {
        if (_widgets.TryGetValue(item.Id, out var w) && !w.IsDisposed) { w.ApplySettings(); w.Show(); w.BringToFront(); return; }
        w = new WidgetForm(item);
        _widgets[item.Id] = w;
        w.FormClosed += (s, e) => { _widgets.Remove(item.Id); UpdateRibbon(); _timers.Invalidate(); _stopwatches.Invalidate(); };
        w.Show();
        item.WidgetOpen = true;
        item.WidgetX = w.Left; item.WidgetY = w.Top;
        AppState.Save();
        UpdateRibbon();
        CurrentGrid?.Invalidate();
    }

    void ToggleWidget(TimeItem item)
    {
        if (_widgets.TryGetValue(item.Id, out var w)) w.CloseWidget();
        else OpenWidget(item);
        UpdateRibbon();
    }

    void CloseAllWidgets()
    {
        foreach (var w in _widgets.Values.ToList()) w.CloseWidget();
        UpdateRibbon();
    }

    /// <summary>Moves a widget from one item to another (used by "Start next timer").</summary>
    public void HandOverWidget(TimeItem from, TimeItem to)
    {
        if (!_widgets.TryGetValue(from.Id, out var w) || w.IsDisposed) return;
        if (_widgets.ContainsKey(to.Id)) return;
        var loc = w.Location;
        if (to.Style == null && from.Style != null) { to.Style = from.Style.Clone(); to.Style.TitleText = ""; }
        if (WidgetRenderer.Effective(to).Layout == WidgetRenderer.Effective(from).Layout) { to.WidgetW = w.Width; to.WidgetH = w.Height; }
        w.CloseWidget();
        to.WidgetX = loc.X; to.WidgetY = loc.Y;
        OpenWidget(to);
    }

    public void RefreshWidgets()
    {
        foreach (var w in _widgets.Values) w.ApplySettings();
    }

    // ------------------------------------------------------------------ tray / window
    void SetupTray()
    {
        _tray.Icon = Program.AppIcon;
        _tray.Text = "Student Timer Clock";
        _tray.Visible = true;
        _tray.DoubleClick += (s, e) => ShowMain();
        var m = new ContextMenuStrip();
        m.Opening += (s, e) =>
        {
            m.Items.Clear();
            m.Items.Add("Show Student Timer Clock", Program.AppIcon.ToBitmap(), (a, b) => ShowMain()).Font = Theme.Bold;
            m.Items.Add(new ToolStripSeparator());
            var quick = new ToolStripMenuItem("Quick timer", Icons.Get("timers", 16));
            foreach (var mins in new[] { 1, 3, 5, 10, 15, 25, 30, 45, 60 })
            {
                int mm = mins;
                quick.DropDownItems.Add($"{mm} minute{(mm > 1 ? "s" : "")}", null, (a, b) => QuickTimer(mm));
            }
            m.Items.Add(quick);
            m.Items.Add("New timer…", Icons.Get("add", 16), (a, b) => { ShowMain(); AddItem(ItemKind.Timer); });
            m.Items.Add("New stopwatch…", Icons.Get("add", 16), (a, b) => { ShowMain(); AddItem(ItemKind.Stopwatch); });
            var running = AppState.AllItems.Where(i => i.IsRunning).ToList();
            var items = new ToolStripMenuItem("Start / pause");
            foreach (var it in AppState.AllItems.Take(30))
            {
                var item = it;
                items.DropDownItems.Add(new ToolStripMenuItem($"{item.Name}", Icons.Get(item.IsRunning ? "pause" : "start", 16), (a, b) => AppState.Toggle(item)) { Checked = false });
            }
            m.Items.Add(items);
            if (running.Count > 0) m.Items.Add("Pause all", Icons.Get("pause", 16), (a, b) => { foreach (var i in running) AppState.Pause(i); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Close all widgets", null, (a, b) => CloseAllWidgets()).Enabled = _widgets.Count > 0;
            m.Items.Add("Statistics", Icons.Get("stats", 16), (a, b) => { ShowMain(); _nav.Selected = 2; });
            m.Items.Add("Settings…", Icons.Get("settings", 16), (a, b) => { ShowMain(); ShowSettings(); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Exit", null, (a, b) => ExitApp());
            e.Cancel = false;
        };
        m.Items.Add("-");
        _tray.ContextMenuStrip = m;
    }

    void QuickTimer(int mins)
    {
        string name = $"Quick {mins} min";
        var t = AppState.Data.Timers.FirstOrDefault(x => x.Name == name);
        if (t == null)
        {
            t = new TimerItem { Name = name, Group = "Quick timers", DurationSeconds = mins * 60, ColorArgb = Palette.Colors[5].ToArgb(), Sound = AppState.Settings.DefaultSound, IconKey = "bell" };
            AppState.Data.Timers.Add(t);
        }
        AppState.Restart(t);
        OpenWidget(t);
    }

    public void ShowMain()
    {
        if (InvokeRequired) { BeginInvoke(ShowMain); return; }
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = AppState.Settings.WinMax ? FormWindowState.Maximized : FormWindowState.Normal;
        ShowInTaskbar = true;
        Activate();
        BringToFront();
    }

    void HideToTray()
    {
        Hide();
        if (!_trayHintShown) { _tray.ShowBalloonTip(3000, "Student Timer Clock", "Still running in the tray. Double-click the icon to open.", ToolTipIcon.Info); _trayHintShown = true; }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized && AppState.Settings.MinimizeToTray) HideToTray();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveBounds();
        if (!_exiting && e.CloseReason == CloseReason.UserClosing && AppState.Settings.CloseToTray)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        _exiting = true;
        _ui.Stop();
        AppState.Save();
        _tray.Visible = false;
        base.OnFormClosing(e);
    }

    void SaveBounds()
    {
        var st = AppState.Settings;
        st.WinMax = WindowState == FormWindowState.Maximized;
        var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        if (b.Width > 100 && WindowState != FormWindowState.Minimized) { st.WinX = b.X; st.WinY = b.Y; st.WinW = b.Width; st.WinH = b.Height; }
    }

    public bool RestartRequested { get; private set; }

    /// <summary>Closes the app and starts it again (used after restoring a backup).</summary>
    public void Restart()
    {
        RestartRequested = true;
        _exiting = true;
        SaveBounds();
        Sound.Stop();
        AppState.Save();
        _tray.Visible = false;
        Application.Exit();
    }

    public void ExitApp()
    {
        _exiting = true;
        SaveBounds();
        foreach (var w in _widgets.Values.ToList()) { w.Item.WidgetX = w.Left; w.Item.WidgetY = w.Top; }
        Sound.Stop();
        AppState.Save();
        _tray.Visible = false;
        Application.Exit();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.N: AddCurrent(); return true;
            case Keys.F2: if (Sel != null) EditItem(Sel); return true;
            case Keys.Delete when !_search.Focused && CurrentGrid != null: DeleteSelected(); return true;
            case Keys.Control | Keys.D: CopySelected(); return true;
            case Keys.Control | Keys.Z when !_search.Focused: Undo(); return true;
            case Keys.Control | Keys.F: if (_search.Visible) { _search.Focus(); _search.SelectAll(); } return true;
            case Keys.Control | Keys.W: if (Sel != null) ToggleWidget(Sel); return true;
            case Keys.Control | Keys.B: ShowBoard(); return true;
            case Keys.Control | Keys.D1: _nav.Selected = 0; return true;
            case Keys.Control | Keys.D2: _nav.Selected = 1; return true;
            case Keys.Control | Keys.D3: _nav.Selected = 2; return true;
            case Keys.L when CurrentGrid != null && CurrentGrid.Focused: if (Sel is StopwatchItem s && s.IsRunning) AppState.Lap(s); return true;
            case Keys.Escape when _search.Focused: _search.Text = ""; CurrentGrid?.Focus(); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    void ShowSettings()
    {
        using var f = new SettingsForm();
        if (f.ShowDialog(this) == DialogResult.OK) { RefreshWidgets(); OnStateChanged(); }
    }

    void ShowHelp(int tab = 0)
    {
        using var f = new HelpForm(tab);
        f.ShowDialog(this);
    }

    public void ShowBoard()
    {
        var b = Application.OpenForms.OfType<BoardForm>().FirstOrDefault();
        if (b != null) { if (b.WindowState == FormWindowState.Minimized) b.WindowState = FormWindowState.Normal; b.Activate(); return; }
        new BoardForm().Show();
    }

    void EditWidgetStyle()
    {
        if (Sel != null)
        {
            using (var f = new WidgetStyleForm(Sel)) f.ShowDialog(this);
            if (_widgets.TryGetValue(Sel.Id, out var w)) w.ApplySettings();
        }
        else { using var f = new WidgetStyleForm(null, true); f.ShowDialog(this); RefreshWidgets(); }
    }
}

namespace StudentTimerClock;

/// <summary>Row of colour swatches.</summary>
public class ColorPicker : FlowLayoutPanel
{
    Color _value;
    public Color Value { get => _value; set { _value = value; foreach (Control c in Controls) c.Invalidate(); } }

    public ColorPicker()
    {
        AutoSize = true; WrapContents = false; Margin = new Padding(0);
        foreach (var c in Palette.Colors) Controls.Add(Swatch(c, false));
        Controls.Add(Swatch(Color.Empty, true));
    }

    Control Swatch(Color c, bool more)
    {
        var p = new Panel { Width = Theme.S(24), Height = Theme.S(24), Margin = new Padding(0, 0, Theme.S(3), 0), Cursor = Cursors.Hand };
        p.Paint += (s, e) =>
        {
            var r = new Rectangle(2, 2, p.Width - 5, p.Height - 5);
            if (more)
            {
                using var br = new System.Drawing.Drawing2D.LinearGradientBrush(r, Color.Red, Color.Blue, 45f);
                e.Graphics.FillRectangle(br, r);
                TextRenderer.DrawText(e.Graphics, "…", Theme.Bold, r, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            else using (var br = new SolidBrush(c)) e.Graphics.FillRectangle(br, r);
            bool sel = !more && c.ToArgb() == _value.ToArgb();
            using var pen = new Pen(sel ? Theme.Accent : Color.Gray, sel ? 3 : 1);
            e.Graphics.DrawRectangle(pen, sel ? new Rectangle(1, 1, p.Width - 3, p.Height - 3) : r);
        };
        p.Click += (s, e) =>
        {
            if (more)
            {
                using var cd = new ColorDialog { Color = _value, FullOpen = true };
                if (cd.ShowDialog(FindForm()) == DialogResult.OK) Value = cd.Color;
            }
            else Value = c;
        };
        return p;
    }
}

/// <summary>Row of selectable icons.</summary>
public class IconPicker : FlowLayoutPanel
{
    string _value = "clock";
    public string Value { get => _value; set { _value = value; foreach (Control c in Controls) c.Invalidate(); } }

    public IconPicker()
    {
        AutoSize = true; WrapContents = true; Margin = new Padding(0);
        MaximumSize = new Size(Theme.S(430), 0);
        foreach (var k in Icons.ItemIcons)
        {
            var key = k;
            var p = new Panel { Width = Theme.S(30), Height = Theme.S(30), Margin = new Padding(0, 0, Theme.S(2), Theme.S(2)), Cursor = Cursors.Hand };
            p.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                if (key == _value)
                {
                    using var br = new SolidBrush(Theme.AccentLight); e.Graphics.FillRectangle(br, 0, 0, p.Width, p.Height);
                    using var pen = new Pen(Theme.Accent); e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
                }
                int s2 = Theme.S(22);
                e.Graphics.DrawImage(Icons.Get(key, s2), (p.Width - s2) / 2, (p.Height - s2) / 2, s2, s2);
            };
            p.Click += (s, e) => Value = key;
            Controls.Add(p);
        }
    }
}

public abstract class EditFormBase : Form
{
    protected readonly TableLayoutPanel Table = new() { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Location = new Point(0, 0), Padding = new Padding(12, 12, 12, 4) };
    protected readonly TabControl Tabs = new() { Dock = DockStyle.Fill, Padding = new Point(14, 5) };
    protected readonly TextBox NameBox = new() { Width = Theme.S(300) };
    protected readonly ComboBox GroupBox = new() { Width = Theme.S(300), DropDownStyle = ComboBoxStyle.DropDown };
    protected readonly ColorPicker Colors = new();
    protected readonly IconPicker IconSel = new();
    protected readonly TextBox NotesBox = new() { Width = Theme.S(420), Height = Theme.S(44), Multiline = true, ScrollBars = ScrollBars.Vertical };
    protected readonly CheckBox StartNow = new() { Text = "Start now", AutoSize = true };
    protected readonly CheckBox OpenWidget = new() { Text = "Float on desktop as a widget", AutoSize = true };
    protected readonly Button StyleBtn = new() { Text = "  Widget style: font, colours, background…", AutoSize = true, Height = Theme.S(28), TextImageRelation = TextImageRelation.ImageBeforeText };
    protected readonly Button OkBtn = new() { Text = "OK", DialogResult = DialogResult.None, Width = Theme.S(90), Height = Theme.S(28) };
    protected readonly Button CancelBtn = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = Theme.S(90), Height = Theme.S(28) };
    readonly FlowLayoutPanel _buttons = new() { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8), BackColor = Theme.HeaderBack };

    protected EditFormBase(string title, ItemKind kind, TimeItem item)
    {
        Text = title;
        Font = Theme.Normal;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.White;
        Icon = Program.AppIcon;
        Table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        GroupBox.Items.AddRange(AppState.AllGroups(kind).Cast<object>().ToArray());
        StyleBtn.Image = Icons.Get("style", 16);
        StyleBtn.Click += (s, e) =>
        {
            var oldName = item.Name;
            if (!string.IsNullOrWhiteSpace(NameBox.Text)) item.Name = NameBox.Text.Trim(); // preview shows the name being typed
            using (var f = new WidgetStyleForm(item)) f.ShowDialog(this);
            item.Name = oldName;
            Program.Main?.RefreshWidgets();
        };

        var general = new TabPage("General") { BackColor = Color.White, AutoScroll = true };
        general.Controls.Add(Table);
        Tabs.TabPages.Add(general);
        _buttons.Controls.Add(CancelBtn); _buttons.Controls.Add(OkBtn);
        Controls.Add(Tabs);
        Controls.Add(_buttons);
        AcceptButton = OkBtn; CancelButton = CancelBtn;
        OkBtn.Click += (s, e) =>
        {
            if (!Security.InputValidator.IsValidName(NameBox.Text, out var nameErr)) { Tabs.SelectedIndex = 0; MessageBox.Show(this, nameErr, Text, MessageBoxButtons.OK, MessageBoxIcon.Information); NameBox.Focus(); return; }
            if (GroupBox.Text.Length > Security.InputValidator.MaxGroup || Security.InputValidator.HasForbiddenChars(GroupBox.Text)) { Tabs.SelectedIndex = 0; MessageBox.Show(this, $"The group name is too long (max {Security.InputValidator.MaxGroup}) or contains control characters.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); GroupBox.Focus(); return; }
            if (!Validate2()) return;
            DialogResult = DialogResult.OK;
        };
    }


    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var pref = Table.GetPreferredSize(Size.Empty);
        int w = Math.Max(pref.Width + Theme.S(30), Theme.S(600));
        int h = Math.Min(pref.Height + Theme.S(40), Screen.FromControl(this).WorkingArea.Height - Theme.S(140));
        ClientSize = new Size(w, h + _buttons.Height);
        CenterToParent();
    }

    protected TabPage AddTab(string title)
    {
        var p = new TabPage(title) { BackColor = Color.White, Padding = new Padding(Theme.S(12)), AutoScroll = true };
        Tabs.TabPages.Add(p);
        return p;
    }

    protected virtual bool Validate2() => true;

    protected void Row(string label, Control c) => Row(Table, label, c);

    protected static void Row(TableLayoutPanel t, string label, Control c)
    {
        var l = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(0, 6, 10, 0) };
        c.Margin = new Padding(0, 3, 0, 5);
        t.Controls.Add(l);
        t.Controls.Add(c);
    }

    protected static FlowLayoutPanel Flow(params Control[] cs)
    {
        var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        foreach (var c in cs) { c.Margin = new Padding(0, 0, 6, 0); f.Controls.Add(c); }
        return f;
    }
    protected static Label Unit(string t) => new() { Text = t, AutoSize = true, Margin = new Padding(0, 4, 8, 0) };
}

public class TimerEditForm : EditFormBase
{
    readonly TimerItem _t;
    readonly RadioButton _modeDur = new() { Text = "Count down a duration", AutoSize = true };
    readonly RadioButton _modeDate = new() { Text = "Count down to a date && time (exam, holiday, deadline…)", AutoSize = true };
    readonly NumericUpDown _d = new() { Maximum = 999, Width = Theme.S(52) };
    readonly NumericUpDown _h = new() { Maximum = 23, Width = Theme.S(48) };
    readonly NumericUpDown _m = new() { Maximum = 59, Width = Theme.S(48) };
    readonly NumericUpDown _s = new() { Maximum = 59, Width = Theme.S(48) };
    readonly DateTimePicker _date = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "dddd, d MMMM yyyy", Width = Theme.S(220) };
    readonly DateTimePicker _time = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "h:mm tt", ShowUpDown = true, Width = Theme.S(100) };
    readonly Label _dateInfo = new() { AutoSize = true, ForeColor = Theme.TextDim, Margin = new Padding(0, 4, 0, 0) };
    readonly FlowLayoutPanel _durRow, _dateRow, _presets;
    readonly ComboBox _sound = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(170) };
    readonly TextBox _custom = new() { Width = Theme.S(250), ReadOnly = true };
    readonly CheckBox _loop = new() { Text = "Repeat sound until dismissed", AutoSize = true };
    readonly CheckBox _auto = new() { Text = "Restart automatically when finished (repeating timer)", AutoSize = true };
    readonly CheckBox _popup = new() { Text = "Show reminder window when finished", AutoSize = true };
    readonly ListView _actions = new() { View = View.Details, FullRowSelect = true, HideSelection = false, HeaderStyle = ColumnHeaderStyle.Nonclickable, MultiSelect = false };
    readonly List<TimerAction> _actList;
    readonly ImageList _actIcons = new() { ImageSize = new Size(Theme.S(18), Theme.S(18)), ColorDepth = ColorDepth.Depth32Bit };

    public TimerEditForm(TimerItem t, bool isNew) : base(isNew ? "Add Timer" : "Edit Timer", ItemKind.Timer, t)
    {
        _t = t;
        _actList = (t.Actions ?? new()).Select(a => a.Clone()).ToList();
        NameBox.Text = t.Name;
        GroupBox.Text = t.Group;
        var ts = TimeSpan.FromSeconds(t.DurationSeconds);
        _d.Value = Math.Min(999, ts.Days); _h.Value = ts.Hours; _m.Value = ts.Minutes; _s.Value = ts.Seconds;
        var target = t.CountdownTo ?? DateTime.Today.AddDays(7).AddHours(9);
        _date.Value = target.Date; _time.Value = DateTime.Today.Add(target.TimeOfDay);
        (t.IsTarget ? _modeDate : _modeDur).Checked = true;
        Colors.Value = t.Color;
        IconSel.Value = t.IconKey;
        _sound.Items.AddRange(Sound.BuiltIn); _sound.Items.Add(Sound.CustomLabel);
        _sound.SelectedItem = _sound.Items.Contains(t.Sound) ? t.Sound : "Digital";
        _custom.Text = t.CustomSoundPath;
        _loop.Checked = t.LoopSound; _auto.Checked = t.AutoRestart; _popup.Checked = t.ShowPopup;
        NotesBox.Text = t.Notes;
        StartNow.Checked = isNew;
        OpenWidget.Checked = t.WidgetOpen;

        // ---------------- General tab
        Row("Label:", NameBox);
        Row("Group:", GroupBox);
        var modes = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        modes.Controls.Add(_modeDur); modes.Controls.Add(_modeDate);
        Row("Type:", modes);
        _durRow = Flow(_d, Unit("days"), _h, Unit("h"), _m, Unit("min"), _s, Unit("sec"));
        Row("Duration:", _durRow);
        _presets = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(Theme.S(440), 0) };
        foreach (var mins in new[] { 1, 2, 3, 5, 10, 15, 20, 25, 30, 45, 60, 90 })
        {
            var b = new Button { Text = mins < 60 ? $"{mins}m" : $"{mins / 60.0:0.#}h", Width = Theme.S(40), Height = Theme.S(24), Margin = new Padding(0, 0, 3, 3), FlatStyle = FlatStyle.System };
            int mm = mins;
            b.Click += (s, e) => { _d.Value = 0; _h.Value = mm / 60; _m.Value = mm % 60; _s.Value = 0; };
            _presets.Controls.Add(b);
        }
        Row("Quick set:", _presets);
        _dateRow = Flow(_date, _time);
        var dateBox = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        dateBox.Controls.Add(_dateRow); dateBox.Controls.Add(_dateInfo);
        Row("Date && time:", dateBox);
        Row("Color:", Colors);
        Row("Icon:", IconSel);
        var opts = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        opts.Controls.AddRange(new Control[] { StartNow, OpenWidget, StyleBtn });
        Row("Options:", opts);
        Row("Notes:", NotesBox);
        _modeDur.CheckedChanged += (s, e) => UpdateMode();
        _date.ValueChanged += (s, e) => UpdateMode();
        _time.ValueChanged += (s, e) => UpdateMode();
        UpdateMode();

        // ---------------- Alarm tab
        var alarm = AddTab("Alarm");
        var at = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top };
        var test = new Button { Text = "▶ Test", Width = Theme.S(64), Height = Theme.S(24) };
        var stop = new Button { Text = "■", Width = Theme.S(28), Height = Theme.S(24) };
        var browse = new Button { Text = "Browse…", Width = Theme.S(72), Height = Theme.S(24) };
        test.Click += (s, e) => Sound.Play((string)_sound.SelectedItem, _custom.Text, false, AppState.Settings.SoundVolume);
        stop.Click += (s, e) => Sound.Stop();
        browse.Click += (s, e) => Browse();
        _sound.SelectedIndexChanged += (s, e) => { if ((string)_sound.SelectedItem == Sound.CustomLabel && _custom.Text == "") Browse(); };
        Row(at, "Sound:", Flow(_sound, test, stop));
        Row(at, "Sound file:", Flow(_custom, browse));
        var aopts = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        aopts.Controls.AddRange(new Control[] { _loop, _popup, _auto });
        Row(at, "Options:", aopts);
        alarm.Controls.Add(at);

        // ---------------- Actions tab
        var act = AddTab("When done (actions)");
        var info = new Label { Dock = DockStyle.Top, Height = Theme.S(40), ForeColor = Theme.TextDim, Text = "Things to do automatically when this timer reaches zero - one after another.\nTip: \"Start next timer\" chains timers together (lesson segments, Pomodoro study / break)." };
        _actions.Columns.Add("Action", Theme.S(420));
        _actions.SmallImageList = _actIcons;
        foreach (var ty in TimerAction.Types) if (!_actIcons.Images.ContainsKey(ty.icon)) _actIcons.Images.Add(ty.icon, Icons.Get(ty.icon, Theme.S(18)));
        _actions.Dock = DockStyle.Fill;
        var side = new FlowLayoutPanel { Dock = DockStyle.Right, Width = Theme.S(130), FlowDirection = FlowDirection.TopDown, Padding = new Padding(Theme.S(8), 0, 0, 0) };
        Button SB(string text, Action click) { var b = new Button { Text = text, Width = Theme.S(118), Height = Theme.S(28), Margin = new Padding(0, 0, 0, Theme.S(5)) }; b.Click += (s, e) => click(); side.Controls.Add(b); return b; }
        SB("Add…", AddAction);
        SB("Edit…", EditAction);
        SB("Remove", () => { int i = SelIdx; if (i < 0) return; _actList.RemoveAt(i); FillActions(Math.Min(i, _actList.Count - 1)); });
        SB("Move up", () => MoveAction(-1));
        SB("Move down", () => MoveAction(1));
        side.Controls.Add(new Label { Height = Theme.S(10) });
        SB("+ Start next timer", () => { _actList.Add(new TimerAction { Type = "StartNextTimer" }); FillActions(_actList.Count - 1); });
        _actions.DoubleClick += (s, e) => EditAction();
        _actions.Resize += (s, e) => { if (_actions.Columns.Count > 0) _actions.Columns[0].Width = Math.Max(100, _actions.ClientSize.Width - 4); };
        act.Controls.Add(_actions);
        act.Controls.Add(side);
        act.Controls.Add(info);
        FillActions(0);

        FormClosed += (s, e) => Sound.Stop();
    }

    int SelIdx => _actions.SelectedIndices.Count > 0 ? _actions.SelectedIndices[0] : -1;

    void FillActions(int select)
    {
        _actions.Items.Clear();
        foreach (var a in _actList)
        {
            var icon = TimerAction.Types.FirstOrDefault(x => x.id == a.Type).icon;
            _actions.Items.Add(new ListViewItem(a.Describe(), icon));
        }
        if (_actList.Count == 0) _actions.Items.Add(new ListViewItem("(nothing - the timer just stops and rings)") { ForeColor = Theme.TextDim });
        else if (select >= 0 && select < _actList.Count) _actions.Items[select].Selected = true;
    }

    void AddAction()
    {
        using var f = new ActionForm(_t, null);
        if (f.ShowDialog(this) == DialogResult.OK) { _actList.Add(f.Action); FillActions(_actList.Count - 1); }
    }

    void EditAction()
    {
        int i = SelIdx;
        if (i < 0 || i >= _actList.Count) { if (_actList.Count == 0) AddAction(); return; }
        using var f = new ActionForm(_t, _actList[i]);
        if (f.ShowDialog(this) == DialogResult.OK) { _actList[i] = f.Action; FillActions(i); }
    }

    void MoveAction(int d)
    {
        int i = SelIdx, j = i + d;
        if (i < 0 || i >= _actList.Count || j < 0 || j >= _actList.Count) return;
        (_actList[i], _actList[j]) = (_actList[j], _actList[i]);
        FillActions(j);
    }

    DateTime TargetValue => _date.Value.Date.Add(new TimeSpan(_time.Value.Hour, _time.Value.Minute, 0));

    void UpdateMode()
    {
        bool dur = _modeDur.Checked;
        _durRow.Enabled = dur; _presets.Enabled = dur;
        _dateRow.Enabled = !dur;
        var left = TargetValue - DateTime.Now;
        _dateInfo.Text = dur ? "" : left.TotalSeconds <= 0 ? "This moment has already passed." : $"That is {Fmt.Human(left.TotalSeconds)} from now  ({Fmt.Clock(TargetValue, false)})";
    }

    void Browse()
    {
        using var ofd = new OpenFileDialog { Filter = "Sound files (*.wav;*.mp3)|*.wav;*.mp3|All files|*.*" };
        if (ofd.ShowDialog(this) == DialogResult.OK) { _custom.Text = ofd.FileName; _sound.SelectedItem = Sound.CustomLabel; }
    }

    double DurValue => (double)(_d.Value * 86400 + _h.Value * 3600 + _m.Value * 60 + _s.Value);

    protected override bool Validate2()
    {
        if (_modeDur.Checked && DurValue <= 0) { Tabs.SelectedIndex = 0; MessageBox.Show(this, "Duration must be longer than 0 seconds.", Text); return false; }
        if (_modeDate.Checked && TargetValue <= DateTime.Now) { Tabs.SelectedIndex = 0; MessageBox.Show(this, "Please choose a date and time in the future.", Text); return false; }
        if (DurValue > Security.InputValidator.MaxDurationSeconds) { Tabs.SelectedIndex = 0; MessageBox.Show(this, "The duration is too long (max 10 years).", Text); return false; }
        if (!string.IsNullOrWhiteSpace(_custom.Text) && !Security.InputValidator.IsSafeSoundFile(_custom.Text, out var sndErr)) { MessageBox.Show(this, "Custom sound: " + sndErr, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
        foreach (var a in _actList)
            if (!a.Quarantined && !Security.InputValidator.ValidateAction(a, out var aErr)) { MessageBox.Show(this, $"The action \"{a.Describe()}\" is not valid:\n\n{aErr}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
        return true;
    }

    public bool StartImmediately => StartNow.Checked || (_modeDate.Checked && !_t.IsRunning);
    public bool WantWidget => OpenWidget.Checked;

    public void Apply()
    {
        if (_modeDate.Checked)
        {
            var tv = TargetValue;
            if (!_t.IsTarget || _t.CountdownTo != tv)
            {
                _t.CountdownTo = tv; _t.CreatedUtc = DateTime.UtcNow;
                _t.Finished = false; _t.FinishedAtUtc = null; _t.ElapsedBefore = 0;
            }
        }
        else
        {
            if (_t.IsTarget) { _t.CountdownTo = null; _t.IsRunning = false; _t.StartedAtUtc = null; _t.ElapsedBefore = 0; _t.Finished = false; }
            double newDur = DurValue;
            if (Math.Abs(newDur - _t.DurationSeconds) > 0.01 && !_t.IsRunning && !_t.Finished) _t.ElapsedBefore = Math.Min(_t.ElapsedBefore, newDur);
            if (Math.Abs(newDur - _t.DurationSeconds) > 0.01 && _t.Finished) { _t.Finished = false; _t.ElapsedBefore = 0; }
            _t.DurationSeconds = newDur;
        }
        _t.Name = NameBox.Text.Trim();
        _t.Group = string.IsNullOrWhiteSpace(GroupBox.Text) ? "General" : GroupBox.Text.Trim();
        _t.Color = Colors.Value;
        _t.IconKey = IconSel.Value;
        _t.Sound = (string)_sound.SelectedItem;
        _t.CustomSoundPath = _custom.Text;
        _t.LoopSound = _loop.Checked; _t.AutoRestart = _auto.Checked; _t.ShowPopup = _popup.Checked;
        _t.Notes = NotesBox.Text;
        _t.Actions = _actList;
    }
}

public class StopwatchEditForm : EditFormBase
{
    readonly StopwatchItem _s;
    readonly TextBox _laps = new() { Width = Theme.S(300), Height = Theme.S(90), Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true };

    public StopwatchEditForm(StopwatchItem s, bool isNew) : base(isNew ? "Add Stopwatch" : "Edit Stopwatch", ItemKind.Stopwatch, s)
    {
        _s = s;
        NameBox.Text = s.Name;
        GroupBox.Text = s.Group;
        Colors.Value = s.Color;
        IconSel.Value = s.IconKey;
        _laps.Text = string.Join(Environment.NewLine, s.LapNames);
        NotesBox.Text = s.Notes;
        StartNow.Checked = false;
        OpenWidget.Checked = s.WidgetOpen;

        Row("Label:", NameBox);
        Row("Group:", GroupBox);
        Row("Color:", Colors);
        Row("Icon:", IconSel);
        var lapInfo = new Label { Text = "Lap names you use often (one per line).\nE.g. sub-activities: Reading, Writing, Break…\nor people: Mary, James, Elizabeth…", AutoSize = true, ForeColor = Theme.TextDim, Margin = new Padding(8, 0, 0, 0) };
        Row("Lap names:", Flow(_laps, lapInfo));
        var opts = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        opts.Controls.AddRange(new Control[] { StartNow, OpenWidget, StyleBtn });
        Row("Options:", opts);
        Row("Notes:", NotesBox);
    }

    public bool StartImmediately => StartNow.Checked;
    public bool WantWidget => OpenWidget.Checked;

    public void Apply()
    {
        _s.Name = NameBox.Text.Trim();
        _s.Group = string.IsNullOrWhiteSpace(GroupBox.Text) ? "General" : GroupBox.Text.Trim();
        _s.Color = Colors.Value;
        _s.IconKey = IconSel.Value;
        _s.LapNames = _laps.Lines.Select(l => l.Trim()).Where(l => l != "").Distinct().ToList();
        if (_s.LapNames.Count > 0 && !_s.LapNames.Contains(_s.CurrentLapName)) _s.CurrentLapName = _s.LapNames[0];
        _s.Notes = NotesBox.Text;
    }
}

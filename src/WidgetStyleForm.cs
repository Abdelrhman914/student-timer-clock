using System.Drawing.Text;

namespace StudentTimerClock;

/// <summary>Editor for a widget's layout, fonts, colours, background and information lines - with live preview.</summary>
public class WidgetStyleForm : Form
{
    readonly TimeItem _item;          // null when editing the default style
    readonly WidgetStyle _st;
    readonly Panel _preview = new() { Dock = DockStyle.Fill };
    readonly System.Windows.Forms.Timer _tick = new() { Interval = 200 };
    readonly NumericUpDown _w = new() { Minimum = 80, Maximum = 2400, Width = Theme.S(64) };
    readonly NumericUpDown _h = new() { Minimum = 20, Maximum = 1600, Width = Theme.S(64) };
    bool _loading = true;
    int _sizeW, _sizeH;
    static string[] _families;
    readonly Dictionary<string, Font> _fontCache = new();
    readonly List<Action> _refreshers = new();

    public WidgetStyleForm(TimeItem item, bool editDefault = false)
    {
        _item = editDefault ? null : item;
        var sample = item ?? AppState.Data.Timers.FirstOrDefault() ?? (TimeItem)new TimerItem { Name = "Sample timer", DurationSeconds = 300 };
        _st = (editDefault ? AppState.Settings.DefaultWidgetStyle : WidgetRenderer.Effective(item)).Clone();
        _sizeW = editDefault ? 0 : item.WidgetW; _sizeH = editDefault ? 0 : item.WidgetH;

        Text = editDefault ? "Default widget style" : $"Widget style - {item.Name}";
        Icon = Program.AppIcon;
        Font = Theme.Normal;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(Theme.S(940), Theme.S(640));
        MinimumSize = new Size(Theme.S(820), Theme.S(560));
        BackColor = Color.White;
        ShowInTaskbar = false;

        _families ??= new InstalledFontCollection().Families.Select(f => f.Name).OrderBy(n => n).ToArray();

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 5) };
        tabs.TabPages.Add(LayoutPage());
        tabs.TabPages.Add(TextPage());
        tabs.TabPages.Add(ColorPage());
        tabs.TabPages.Add(InfoPage());

        // preview side
        var right = new Panel { Dock = DockStyle.Right, Width = Theme.S(400), Padding = new Padding(Theme.S(10)), BackColor = Color.FromArgb(70, 80, 96) };
        var cap = new Label { Text = "Live preview (desktop)", Dock = DockStyle.Top, Height = Theme.S(24), ForeColor = Color.White, Font = Theme.Bold };
        _preview.BackColor = Color.FromArgb(70, 80, 96);
        typeof(Panel).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(_preview, true);
        _preview.Paint += (s, e) => PaintPreview(e.Graphics, sample);
        right.Controls.Add(_preview);
        right.Controls.Add(cap);

        // buttons
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(Theme.S(8)), BackColor = Theme.HeaderBack, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "Cancel", Width = Theme.S(90), Height = Theme.S(30), DialogResult = DialogResult.Cancel };
        var ok = new Button { Text = "OK", Width = Theme.S(90), Height = Theme.S(30) };
        var all = new Button { Text = "Apply to ALL widgets", AutoSize = true, Height = Theme.S(30) };
        var def = new Button { Text = "Save as default for new widgets", AutoSize = true, Height = Theme.S(30) };
        bottom.Controls.AddRange(new Control[] { cancel, ok, all });
        if (!editDefault) bottom.Controls.Add(def);
        ok.Click += (s, e) => { Commit(); DialogResult = DialogResult.OK; };
        def.Click += (s, e) => { AppState.Settings.DefaultWidgetStyle = _st.Clone(); AppState.Save(); MessageBox.Show(this, "Saved. New widgets will use this style.", Text); };
        all.Click += (s, e) =>
        {
            if (MessageBox.Show(this, "Use this style for every timer and stopwatch widget?", Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            foreach (var it in AppState.AllItems) { it.Style = _st.Clone(); if (_st.Layout != WidgetRenderer.Effective(it).Layout) it.WidgetW = it.WidgetH = 0; }
            AppState.Settings.DefaultWidgetStyle = _st.Clone();
            Commit();
            DialogResult = DialogResult.OK;
        };
        AcceptButton = ok; CancelButton = cancel;

        Controls.Add(tabs);
        Controls.Add(right);
        Controls.Add(bottom);
        _tick.Tick += (s, e) => _preview.Invalidate();
        _tick.Start();
        FormClosed += (s, e) => { _tick.Stop(); foreach (var f in _fontCache.Values) f.Dispose(); };
        _loading = false;
        RefreshAll();
    }

    void Commit()
    {
        if (_item == null) { AppState.Settings.DefaultWidgetStyle = _st.Clone(); }
        else
        {
            _item.Style = _st.Clone();
            _item.WidgetW = _sizeW; _item.WidgetH = _sizeH;
        }
        AppState.NotifyChanged();
    }

    void Changed()
    {
        if (_loading) return;
        _st.Preset = _st.Preset; // keep
        _preview.Invalidate();
    }

    void RefreshAll() { _loading = true; foreach (var r in _refreshers) r(); _loading = false; _preview.Invalidate(); }

    Size CurrentSize()
    {
        var d = WidgetRenderer.DefaultSize(_st.Layout);
        return new Size(_sizeW > 0 ? _sizeW : d.Width, _sizeH > 0 ? _sizeH : d.Height);
    }

    void PaintPreview(Graphics g, TimeItem sample)
    {
        g.Clear(_preview.BackColor);
        // fake desktop
        using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(_preview.ClientRectangle, Color.FromArgb(64, 110, 170), Color.FromArgb(40, 60, 90), 60f)) g.FillRectangle(br, _preview.ClientRectangle);
        var sz = CurrentSize();
        float sc = Math.Min(1f, Math.Min((_preview.Width - 20f) / sz.Width, (_preview.Height - 20f) / sz.Height));
        int w = (int)(sz.Width * sc), h = (int)(sz.Height * sc);
        var r = new Rectangle((_preview.Width - w) / 2, (_preview.Height - h) / 2, w, h);
        var st = _st.Clone();
        var state = g.Save();
        if (sc < 1f)
        {
            g.TranslateTransform(r.X, r.Y); g.ScaleTransform(sc, sc);
            r = new Rectangle(0, 0, sz.Width, sz.Height);
        }
        using (var clip = Theme.Round(r, st.RoundCorners ? Theme.S(8) : 0)) g.SetClip(clip);
        WidgetRenderer.Draw(g, r, sample, st, true, null, DateTime.UtcNow, chrome: true);
        g.Restore(state);
        if (st.Opacity < 100) using (var br = new SolidBrush(Color.FromArgb((100 - st.Opacity) * 255 / 100, 64, 100, 150))) g.FillRectangle(br, new Rectangle((_preview.Width - w) / 2, (_preview.Height - h) / 2, w, h));
        TextRenderer.DrawText(g, $"{sz.Width} × {sz.Height}" + (sc < 1 ? $"  (shown at {sc * 100:0}%)" : ""), Theme.Small, new Rectangle(0, _preview.Height - Theme.S(20), _preview.Width, Theme.S(18)), Color.White, TextFormatFlags.HorizontalCenter);
    }

    // ------------------------------------------------------------------ helpers
    static TabPage Page(string title, out FlowLayoutPanel flow)
    {
        var p = new TabPage(title) { BackColor = Color.White, Padding = new Padding(Theme.S(10)), AutoScroll = true };
        flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Top };
        p.Controls.Add(flow);
        return p;
    }

    static Label Head(string t) => new() { Text = t, AutoSize = true, Font = Theme.Bold, ForeColor = Theme.Accent, Margin = new Padding(0, Theme.S(10), 0, Theme.S(4)) };

    static FlowLayoutPanel Row(params Control[] cs)
    {
        var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
        foreach (var c in cs) { c.Margin = new Padding(0, c is Label ? Theme.S(5) : 0, Theme.S(8), 0); f.Controls.Add(c); }
        return f;
    }
    static Label L(string t) => new() { Text = t, AutoSize = true };

    CheckBox Check(string text, Func<bool> get, Action<bool> set)
    {
        var c = new CheckBox { Text = text, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
        _refreshers.Add(() => c.Checked = get());
        c.CheckedChanged += (s, e) => { if (_loading) return; set(c.Checked); Changed(); };
        return c;
    }

    ComboBox Combo(string[] items, Func<string> get, Action<string> set, int width = 140)
    {
        var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(width) };
        c.Items.AddRange(items);
        _refreshers.Add(() => c.SelectedItem = items.Contains(get()) ? get() : items[0]);
        c.SelectedIndexChanged += (s, e) => { if (_loading) return; set((string)c.SelectedItem); Changed(); };
        return c;
    }

    Button ColorBtn(string text, Func<int> get, Action<int> set)
    {
        var b = new Button { Text = "   " + text, AutoSize = true, Height = Theme.S(28), TextImageRelation = TextImageRelation.ImageBeforeText, ImageAlign = ContentAlignment.MiddleLeft, TextAlign = ContentAlignment.MiddleLeft, MinimumSize = new Size(Theme.S(190), 0) };
        void Upd()
        {
            var bmp = new Bitmap(Theme.S(22), Theme.S(16));
            using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.FromArgb(get())); g.DrawRectangle(Pens.Gray, 0, 0, bmp.Width - 1, bmp.Height - 1); }
            var old = b.Image; b.Image = bmp; old?.Dispose();
        }
        _refreshers.Add(Upd);
        b.Click += (s, e) =>
        {
            using var cd = new ColorDialog { Color = Color.FromArgb(get()), FullOpen = true, AnyColor = true };
            if (cd.ShowDialog(this) != DialogResult.OK) return;
            set(cd.Color.ToArgb()); _st.Preset = "Custom"; Upd(); Changed();
        };
        return b;
    }

    ComboBox FontCombo(bool allowLcd, Func<string> get, Action<string> set)
    {
        var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(250), DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = Theme.S(22), MaxDropDownItems = 16 };
        const string lcd = "Digital LCD (built-in)";
        if (allowLcd) c.Items.Add(lcd);
        c.Items.AddRange(_families);
        c.DrawItem += (s, e) =>
        {
            e.DrawBackground();
            if (e.Index < 0) return;
            string name = (string)c.Items[e.Index];
            Font f = Theme.Normal;
            if (name != lcd)
            {
                if (!_fontCache.TryGetValue(name, out f)) { f = Theme.SafeFont(name, 10f); _fontCache[name] = f; }
            }
            TextRenderer.DrawText(e.Graphics, name, f, e.Bounds, (e.State & DrawItemState.Selected) != 0 ? SystemColors.HighlightText : Color.Black, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };
        _refreshers.Add(() =>
        {
            string v = get();
            if (allowLcd && string.IsNullOrEmpty(v)) c.SelectedItem = lcd;
            else c.SelectedItem = c.Items.Contains(v) ? v : (c.Items.Contains("Segoe UI") ? "Segoe UI" : c.Items[0]);
        });
        c.SelectedIndexChanged += (s, e) => { if (_loading) return; var v = (string)c.SelectedItem; set(v == lcd ? "" : v); Changed(); };
        return c;
    }

    // ------------------------------------------------------------------ pages
    TabPage LayoutPage()
    {
        var page = Page("Layout & size", out var f);
        f.Controls.Add(Head("Layout"));
        foreach (var (id, text) in new[] { ("Digital", "Digital - big digits with DAY / HOUR / MIN / SEC labels"), ("Dial", "Clock dial - coloured pie shows the time left (like a classroom timer)"), ("Compact", "Compact bar - small one-line widget") })
        {
            var rb = new RadioButton { Text = text, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
            _refreshers.Add(() => rb.Checked = _st.Layout == id);
            rb.CheckedChanged += (s, e) =>
            {
                if (_loading || !rb.Checked) return;
                _st.Layout = id; _sizeW = _sizeH = 0;
                _loading = true; var d = WidgetRenderer.DefaultSize(id); _w.Value = Math.Clamp(d.Width, 80, 2400); _h.Value = Math.Clamp(d.Height, 20, 1600); _loading = false;
                Changed();
            };
            f.Controls.Add(rb);
        }
        f.Controls.Add(Head("Colour theme (quick start)"));
        var presets = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(Theme.S(480), 0) };
        foreach (var p in WidgetStyle.Presets)
        {
            var tmp = new WidgetStyle(); tmp.ApplyPreset(p);
            var b = new Button { Text = p, Width = Theme.S(100), Height = Theme.S(34), FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, Theme.S(4), Theme.S(4)) };
            b.BackColor = p == "Item colour" ? (_item?.Color ?? Palette.Colors[0]) : Color.FromArgb(tmp.BackColor);
            b.ForeColor = Color.FromArgb(tmp.ForeColor);
            b.FlatAppearance.BorderColor = Color.Gray;
            b.Click += (s, e) => { _st.ApplyPreset(p); _st.BackImage = ""; RefreshAll(); };
            presets.Controls.Add(b);
        }
        f.Controls.Add(presets);

        f.Controls.Add(Head("Size"));
        var reset = new Button { Text = "Default size", AutoSize = true };
        _refreshers.Add(() => { var s = CurrentSize(); _w.Value = Math.Clamp(s.Width, 80, 2400); _h.Value = Math.Clamp(s.Height, 20, 1600); });
        _w.ValueChanged += (s, e) => { if (_loading) return; _sizeW = (int)_w.Value; if (_sizeH == 0) _sizeH = (int)_h.Value; Changed(); };
        _h.ValueChanged += (s, e) => { if (_loading) return; _sizeH = (int)_h.Value; if (_sizeW == 0) _sizeW = (int)_w.Value; Changed(); };
        reset.Click += (s, e) => { _sizeW = _sizeH = 0; RefreshAll(); };
        f.Controls.Add(Row(L("Width"), _w, L("Height"), _h, reset));
        f.Controls.Add(new Label { Text = "Tip: you can also resize a widget by dragging its bottom-right corner.", AutoSize = true, ForeColor = Theme.TextDim });

        f.Controls.Add(Head("Window"));
        var op = new TrackBar { Minimum = 20, Maximum = 100, TickFrequency = 10, Width = Theme.S(220) };
        var opLbl = L("100%");
        _refreshers.Add(() => { op.Value = Math.Clamp(_st.Opacity, 20, 100); opLbl.Text = op.Value + "%"; });
        op.ValueChanged += (s, e) => { opLbl.Text = op.Value + "%"; if (_loading) return; _st.Opacity = op.Value; Changed(); };
        f.Controls.Add(Row(L("Opacity"), op, opLbl));
        f.Controls.Add(Check("Always on top of other windows", () => _st.TopMost, v => _st.TopMost = v));
        f.Controls.Add(Check("Lock position and size (can't be moved by accident)", () => _st.Locked, v => _st.Locked = v));
        f.Controls.Add(Check("Rounded corners", () => _st.RoundCorners, v => _st.RoundCorners = v));
        f.Controls.Add(Check("Thin border", () => _st.Border, v => _st.Border = v));
        return page;
    }

    TabPage TextPage()
    {
        var page = Page("Text & fonts", out var f);
        f.Controls.Add(Head("Title"));
        f.Controls.Add(Check("Show title", () => _st.ShowTitle, v => _st.ShowTitle = v));
        var title = new TextBox { Width = Theme.S(300), PlaceholderText = _item?.Name ?? "(timer name)" };
        _refreshers.Add(() => title.Text = _st.TitleText);
        title.TextChanged += (s, e) => { if (_loading) return; _st.TitleText = title.Text; Changed(); };
        f.Controls.Add(Row(L("Title text"), title));
        f.Controls.Add(new Label { Text = "Leave empty to show the timer's name - or write anything, e.g. \"Good morning my dear\".", AutoSize = true, ForeColor = Theme.TextDim });
        f.Controls.Add(Row(L("Title font"), FontCombo(false, () => _st.TitleFont, v => _st.TitleFont = v),
            Check("Bold", () => _st.TitleBold, v => _st.TitleBold = v), Check("Italic", () => _st.TitleItalic, v => _st.TitleItalic = v)));

        f.Controls.Add(Head("Time digits"));
        f.Controls.Add(Row(L("Time font"), FontCombo(true, () => _st.TimeFont, v => _st.TimeFont = v),
            Check("Bold", () => _st.TimeBold, v => _st.TimeBold = v), Check("Italic", () => _st.TimeItalic, v => _st.TimeItalic = v)));
        f.Controls.Add(Check("Show DAY / HOUR / MIN / SEC labels above the digits", () => _st.ShowUnitLabels, v => _st.ShowUnitLabels = v));
        f.Controls.Add(Row(L("Days"), Combo(new[] { "Auto", "Always", "Never" }, () => _st.ShowDays, v => _st.ShowDays = v, 100),
            new Label { Text = "Auto = only for timers of 24 hours or more", AutoSize = true, ForeColor = Theme.TextDim }));
        f.Controls.Add(Check("Show seconds", () => _st.ShowSeconds, v => _st.ShowSeconds = v));
        f.Controls.Add(Check("Show hundredths of a second (stopwatches)", () => _st.ShowHundredths, v => _st.ShowHundredths = v));
        return page;
    }

    TabPage ColorPage()
    {
        var page = Page("Colours & background", out var f);
        f.Controls.Add(Head("Text colours"));
        f.Controls.Add(Row(ColorBtn("Time digits colour", () => _st.ForeColor, v => _st.ForeColor = v), ColorBtn("Title / labels colour", () => _st.LabelColor, v => _st.LabelColor = v)));
        f.Controls.Add(Row(ColorBtn("Dial / progress colour", () => _st.AccentColor, v => _st.AccentColor = v)));

        f.Controls.Add(Head("Background"));
        f.Controls.Add(Check("Use the timer's own row colour as background", () => _st.UseItemColor, v => _st.UseItemColor = v));
        f.Controls.Add(Row(ColorBtn("Background colour", () => _st.BackColor, v => { _st.BackColor = v; _st.UseItemColor = false; }), ColorBtn("Gradient colour", () => _st.BackColor2, v => { _st.BackColor2 = v; _st.UseItemColor = false; })));
        f.Controls.Add(Check("Gradient (blend background colour into gradient colour)", () => _st.Gradient, v => _st.Gradient = v));

        f.Controls.Add(Head("Background picture"));
        var path = new TextBox { Width = Theme.S(300), ReadOnly = true };
        var browse = new Button { Text = "Choose picture…", AutoSize = true };
        var clear = new Button { Text = "Remove", AutoSize = true };
        _refreshers.Add(() => path.Text = _st.BackImage);
        browse.Click += (s, e) =>
        {
            using var ofd = new OpenFileDialog { Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*" };
            if (ofd.ShowDialog(this) != DialogResult.OK) return;
            WidgetRenderer.ForgetImage(ofd.FileName);
            if (!Security.InputValidator.IsSafeImageFile(ofd.FileName, out var imgErr)) { MessageBox.Show(this, imgErr, "Background picture", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            _st.BackImage = ofd.FileName; path.Text = ofd.FileName; Changed();
        };
        clear.Click += (s, e) => { _st.BackImage = ""; path.Text = ""; Changed(); };
        f.Controls.Add(Row(path, browse, clear));
        f.Controls.Add(Check("Stretch picture (otherwise it fills and crops)", () => _st.BackImageStretch, v => _st.BackImageStretch = v));
        var dim = new TrackBar { Minimum = 0, Maximum = 80, TickFrequency = 10, Width = Theme.S(200) };
        _refreshers.Add(() => dim.Value = Math.Clamp(_st.BackImageDim, 0, 80));
        dim.ValueChanged += (s, e) => { if (_loading) return; _st.BackImageDim = dim.Value; Changed(); };
        f.Controls.Add(Row(L("Darken picture"), dim));
        return page;
    }

    TabPage InfoPage()
    {
        var page = Page("Date & end time", out var f);
        f.Controls.Add(Head("Below the timer"));
        f.Controls.Add(Check("Show today's date  (e.g. Tuesday, 13 October 2026)", () => _st.ShowDate, v => _st.ShowDate = v));
        f.Controls.Add(Check("Show when it will finish  (e.g. 5 min timer at 10:00 → \"Ends at 10:05 PM\")", () => _st.ShowEndTime, v => _st.ShowEndTime = v));
        var h24 = new CheckBox { Text = "Use 24-hour clock (22:05) instead of AM / PM (10:05 PM)", AutoSize = true };
        _refreshers.Add(() => h24.Checked = AppState.Settings.Use24Hour);
        h24.CheckedChanged += (s, e) => { if (_loading) return; AppState.Settings.Use24Hour = h24.Checked; AppState.Save(); Changed(); };
        f.Controls.Add(h24);
        f.Controls.Add(Check("Show progress bar (digital layout)", () => _st.ShowProgress, v => _st.ShowProgress = v));
        f.Controls.Add(Head("Buttons"));
        f.Controls.Add(Row(L("Start / pause / reset buttons"), Combo(new[] { "Always", "Hover", "Never" }, () => _st.ShowButtons, v => _st.ShowButtons = v, 110),
            new Label { Text = "Hover = only when the mouse is over the widget", AutoSize = true, ForeColor = Theme.TextDim }));
        return page;
    }
}

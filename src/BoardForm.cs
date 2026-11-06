namespace StudentTimerClock;

/// <summary>Multi-timer board: every selected timer / stopwatch as a big coloured tile. Can go fullscreen (projector / classroom).</summary>
public class BoardForm : Form
{
    readonly Panel _bar = new() { Dock = DockStyle.Top, Height = Theme.S(42), BackColor = Theme.RibbonBack };
    readonly ComboBox _source = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(230) };
    readonly BoardCanvas _canvas;
    readonly System.Windows.Forms.Timer _tick = new() { Interval = 200 };
    readonly List<string> _sourceIds = new();
    FormWindowState _prevState;
    Rectangle _prevBounds;
    bool _full;

    public BoardForm()
    {
        Text = "Board - Student Timer Clock";
        Icon = Program.AppIcon;
        Font = Theme.Normal;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(Theme.S(1100), Theme.S(680));
        MinimumSize = new Size(Theme.S(480), Theme.S(320));
        KeyPreview = true;
        _canvas = new BoardCanvas(this) { Dock = DockStyle.Fill };

        var lbl = new Label { Text = "Show:", AutoSize = true, Location = new Point(Theme.S(10), Theme.S(13)) };
        _source.Location = new Point(Theme.S(56), Theme.S(9));
        var full = new Button { Text = "  Fullscreen  (F11)", AutoSize = true, Height = Theme.S(28), Location = new Point(Theme.S(300), Theme.S(7)), Image = Icons.Get("fullscreen", 16), TextImageRelation = TextImageRelation.ImageBeforeText };
        var startAll = new Button { Text = "Start all", AutoSize = true, Height = Theme.S(28), Location = new Point(Theme.S(450), Theme.S(7)), Image = Icons.Get("start", 16), TextImageRelation = TextImageRelation.ImageBeforeText };
        var pauseAll = new Button { Text = "Pause all", AutoSize = true, Height = Theme.S(28), Location = new Point(Theme.S(548), Theme.S(7)), Image = Icons.Get("pause", 16), TextImageRelation = TextImageRelation.ImageBeforeText };
        var hint = new Label { Text = "Double-click a tile to start / pause  •  Esc leaves fullscreen", AutoSize = true, ForeColor = Theme.TextDim, Location = new Point(Theme.S(660), Theme.S(13)) };
        full.Click += (s, e) => ToggleFull();
        startAll.Click += (s, e) => { foreach (var it in Items().Where(i => !i.IsRunning && !(i is TimerItem t && t.Finished))) AppState.Start(it); };
        pauseAll.Click += (s, e) => { foreach (var it in Items().Where(i => i.IsRunning)) AppState.Pause(it); };
        _bar.Controls.AddRange(new Control[] { lbl, _source, full, startAll, pauseAll, hint });
        _bar.Paint += (s, e) => e.Graphics.DrawLine(new Pen(Theme.Border), 0, _bar.Height - 1, _bar.Width, _bar.Height - 1);

        FillSources();
        _source.SelectedIndexChanged += (s, e) =>
        {
            if (_source.SelectedIndex < 0) return;
            AppState.Settings.BoardSource = _sourceIds[_source.SelectedIndex];
            AppState.Save();
            _canvas.Invalidate();
        };

        Controls.Add(_canvas);
        Controls.Add(_bar);
        _tick.Tick += (s, e) => _canvas.Invalidate();
        _tick.Start();
        Shown += (s, e) => { if (AppState.Settings.BoardFullscreen) ToggleFull(); };
    }

    void FillSources()
    {
        _source.Items.Clear(); _sourceIds.Clear();
        void Add(string id, string text) { _sourceIds.Add(id); _source.Items.Add(text); }
        Add("Running", "Running timers & stopwatches");
        Add("Timers", "All timers");
        Add("Stopwatches", "All stopwatches");
        Add("All", "Everything");
        foreach (var g in AppState.AllGroups()) Add("Group:" + g, "Group: " + g);
        int i = _sourceIds.IndexOf(AppState.Settings.BoardSource ?? "Running");
        _source.SelectedIndex = i < 0 ? 0 : i;
    }

    public List<TimeItem> Items()
    {
        string src = AppState.Settings.BoardSource ?? "Running";
        IEnumerable<TimeItem> all = AppState.AllItems;
        return (src switch
        {
            "Timers" => AppState.Data.Timers.Cast<TimeItem>(),
            "Stopwatches" => AppState.Data.Stopwatches.Cast<TimeItem>(),
            "All" => all,
            _ when src.StartsWith("Group:") => all.Where(i => i.Group == src[6..]),
            _ => all.Where(i => i.IsRunning || (i is TimerItem t && t.Finished)),
        }).ToList();
    }

    void ToggleFull()
    {
        _full = !_full;
        if (_full)
        {
            _prevState = WindowState; _prevBounds = Bounds;
            WindowState = FormWindowState.Normal;
            FormBorderStyle = FormBorderStyle.None;
            Bounds = Screen.FromControl(this).Bounds;
            TopMost = true;
            _bar.Visible = false;
        }
        else
        {
            TopMost = false;
            FormBorderStyle = FormBorderStyle.Sizable;
            Bounds = _prevBounds;
            WindowState = _prevState;
            _bar.Visible = true;
        }
        AppState.Settings.BoardFullscreen = _full;
        AppState.Save();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F11) { ToggleFull(); return true; }
        if (keyData == Keys.Escape) { if (_full) ToggleFull(); else Close(); return true; }
        if (keyData == Keys.Space) { var h = _canvas.HoverItem; if (h != null) AppState.Toggle(h); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _tick.Stop();
        if (_full) { AppState.Settings.BoardFullscreen = true; AppState.Save(); }
        base.OnFormClosed(e);
    }

    public void ShowFullToggleMenu(Point screen, TimeItem it)
    {
        var m = new ContextMenuStrip();
        if (it != null)
        {
            m.Items.Add(it.IsRunning ? "Pause" : "Start", Icons.Get(it.IsRunning ? "pause" : "start", 16), (a, b) => AppState.Toggle(it));
            m.Items.Add("Reset", Icons.Get("reset", 16), (a, b) => { if (it is TimerItem t && t.Finished) Program.Main?.DismissTimer(t); else AppState.Reset(it); });
            if (it is StopwatchItem sw) m.Items.Add("Lap", Icons.Get("lap", 16), (a, b) => AppState.Lap(sw)).Enabled = sw.IsRunning;
            m.Items.Add("Float as desktop widget", Icons.Get("widget", 16), (a, b) => Program.Main?.OpenWidget(it));
            m.Items.Add(new ToolStripSeparator());
        }
        m.Items.Add(_full ? "Leave fullscreen (Esc)" : "Fullscreen (F11)", Icons.Get("fullscreen", 16), (a, b) => ToggleFull());
        m.Items.Add("Close board", null, (a, b) => Close());
        m.Show(screen);
    }

    /// <summary>The tile area.</summary>
    class BoardCanvas : Control
    {
        readonly BoardForm _f;
        readonly List<(TimeItem it, Rectangle r)> _tiles = new();
        Point _mouse = new(-1, -1);
        string _hoverBtn;
        public TimeItem HoverItem => _tiles.FirstOrDefault(t => t.r.Contains(_mouse)).it;

        public BoardCanvas(BoardForm f)
        {
            _f = f;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(236, 238, 241);
        }

        void LayoutTiles(List<TimeItem> items)
        {
            _tiles.Clear();
            int n = items.Count;
            if (n == 0) return;
            int gap = Theme.S(10);
            var area = Rectangle.Inflate(ClientRectangle, -gap, -gap);
            int bestCols = 1; float bestScore = -1;
            for (int cols = 1; cols <= n; cols++)
            {
                int rows = (n + cols - 1) / cols;
                float w = (area.Width - (cols - 1) * gap) / (float)cols, h = (area.Height - (rows - 1) * gap) / (float)rows;
                float score = Math.Min(w, h * 2.3f);
                if (score > bestScore) { bestScore = score; bestCols = cols; }
            }
            int c = bestCols, rws = (n + c - 1) / c;
            int tw = (area.Width - (c - 1) * gap) / c, th = (area.Height - (rws - 1) * gap) / rws;
            th = Math.Min(th, (int)(tw * 0.75f)); // don't let tiles become towers
            int totalH = rws * th + (rws - 1) * gap;
            int y0 = area.Y + Math.Max(0, (area.Height - totalH) / 2);
            for (int i = 0; i < n; i++)
            {
                int row = i / c, col = i % c;
                int inRow = Math.Min(c, n - row * c);
                int x0 = area.X + (area.Width - (inRow * tw + (inRow - 1) * gap)) / 2;
                _tiles.Add((items[i], new Rectangle(x0 + col * (tw + gap), y0 + row * (th + gap), tw, th)));
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            var items = _f.Items();
            LayoutTiles(items);
            if (items.Count == 0)
            {
                TextRenderer.DrawText(g, "Nothing to show yet.\n\nStart a timer or stopwatch - or choose \"All timers\" / a group in the list above.",
                    Theme.UI(13f), ClientRectangle, Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }
            var now = DateTime.UtcNow;
            foreach (var (it, r) in _tiles)
            {
                var st = WidgetRenderer.BoardStyle(it);
                bool hover = r.Contains(_mouse);
                var state = g.Save();
                g.SetClip(r);
                WidgetRenderer.Draw(g, r, it, st, hover, hover ? _hoverBtn : null, now, chrome: false);
                g.Restore(state);
                using var p = new Pen(Color.FromArgb(60, 0, 0, 0));
                g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
            }
        }

        string HitBtn(Point p, out TimeItem item)
        {
            item = null;
            foreach (var (it, r) in _tiles)
            {
                if (!r.Contains(p)) continue;
                item = it;
                return WidgetRenderer.Buttons(it, WidgetRenderer.BoardStyle(it), r, true, false).FirstOrDefault(b => b.R.Contains(p))?.Id;
            }
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            _mouse = e.Location;
            var b = HitBtn(e.Location, out _);
            if (b != _hoverBtn) { _hoverBtn = b; Cursor = b != null ? Cursors.Hand : Cursors.Default; }
            Invalidate();
        }
        protected override void OnMouseLeave(EventArgs e) { _mouse = new Point(-1, -1); _hoverBtn = null; Invalidate(); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            var b = HitBtn(e.Location, out var it);
            if (e.Button == MouseButtons.Right) { _f.ShowFullToggleMenu(PointToScreen(e.Location), it); return; }
            if (it == null || b == null) return;
            switch (b)
            {
                case "toggle":
                    if (it is TimerItem tt && tt.Finished) { Program.Main?.DismissTimer(tt); AppState.Start(tt); } else AppState.Toggle(it);
                    break;
                case "reset":
                    if (it is TimerItem tr && tr.Finished) Program.Main?.DismissTimer(tr); else AppState.Reset(it);
                    break;
                case "lap": if (it is StopwatchItem sw && sw.IsRunning) AppState.Lap(sw); break;
            }
            Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            var b = HitBtn(e.Location, out var it);
            if (it == null || b != null || e.Button != MouseButtons.Left) return;
            if (it is TimerItem t && t.Finished) Program.Main?.DismissTimer(t); else AppState.Toggle(it);
        }
    }
}

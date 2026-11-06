using System.Drawing.Drawing2D;

namespace StudentTimerClock;

/// <summary>Grouped, colour-coded grid of timers or stopwatches (Hot Alarm Clock style).</summary>
public class ItemGrid : Control
{
    class Col { public string Id, Title; public int W; public bool Fill; public int X; }
    class GroupRow { public string Name; public int Count, Running; public bool Collapsed; }

    public readonly ItemKind Kind;
    readonly List<Col> _cols = new();
    readonly List<object> _rows = new();
    readonly VScrollBar _vs = new() { Dock = DockStyle.Right };
    int _scroll;
    TimeItem _sel;
    int _hoverRow = -1;
    string _hoverAction;
    string _filter = "";

    public event Action SelectionChanged;
    public event Action<TimeItem> ItemDoubleClicked;
    public event Action<TimeItem, Point> ContextMenuRequested;
    public event Action<TimeItem, string> ActionClicked;

    int RowH => Theme.S(42);
    int GroupH => Theme.S(24);
    int HeadH => Theme.S(28);

    public ItemGrid(ItemKind kind)
    {
        Kind = kind;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Font = Theme.Normal;
        BackColor = Color.White;
        Controls.Add(_vs);
        _vs.Scroll += (s, e) => { _scroll = _vs.Value; Invalidate(); };

        void C(string id, string t, int w, bool fill = false) => _cols.Add(new Col { Id = id, Title = t, W = Theme.S(w), Fill = fill });
        C("run", "Run", 50); C("clock", "Clock", 46);
        if (kind == ItemKind.Timer) { C("dur", "Duration", 92); C("time", "Remaining", 116); }
        else { C("time", "Elapsed", 140); C("lap", "Current lap", 112); }
        C("name", "Label", kind == ItemKind.Timer ? 170 : 190); C("icon", "Icon", 42);
        if (kind == ItemKind.Timer) { C("prog", "Progress", 110); C("ends", "Ends at", 130); C("done", "When done", 128); } else C("laps", "Laps", 54);
        C("sum", "Summary", 200, true);
        C("act", "Actions", kind == ItemKind.Timer ? 74 : 98);
        C("color", "Color", 46);
        Rebuild();
    }

    public TimeItem Selected
    {
        get => _sel;
        set { _sel = value; EnsureVisible(value); Invalidate(); SelectionChanged?.Invoke(); }
    }

    public string Filter { get => _filter; set { _filter = value ?? ""; Rebuild(); } }

    IEnumerable<TimeItem> Source => Kind == ItemKind.Timer ? AppState.Data.Timers : AppState.Data.Stopwatches;
    string GKey(string g) => (Kind == ItemKind.Timer ? "T:" : "S:") + g;

    public void Rebuild()
    {
        _rows.Clear();
        var items = Source.Where(i => _filter == "" || i.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase) || i.Group.Contains(_filter, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var grp in items.GroupBy(i => string.IsNullOrWhiteSpace(i.Group) ? "General" : i.Group.Trim(), StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key))
        {
            bool col = AppState.Settings.CollapsedGroups.Contains(GKey(grp.Key));
            _rows.Add(new GroupRow { Name = grp.Key, Count = grp.Count(), Running = grp.Count(i => i.IsRunning), Collapsed = col });
            if (!col) _rows.AddRange(grp);
        }
        if (_sel != null && !Source.Contains(_sel)) { _sel = null; SelectionChanged?.Invoke(); }
        UpdateScroll();
        Invalidate();
    }

    public void SetAllCollapsed(bool collapsed)
    {
        foreach (var g in Source.Select(i => string.IsNullOrWhiteSpace(i.Group) ? "General" : i.Group.Trim()).Distinct())
        {
            AppState.Settings.CollapsedGroups.Remove(GKey(g));
            if (collapsed) AppState.Settings.CollapsedGroups.Add(GKey(g));
        }
        AppState.Save();
        Rebuild();
    }

    int ContentHeight => _rows.Sum(r => r is GroupRow ? GroupH : RowH);
    int ViewH => Height - HeadH;

    void UpdateScroll()
    {
        int max = Math.Max(0, ContentHeight - ViewH);
        _vs.Visible = max > 0;
        _vs.Minimum = 0;
        _vs.LargeChange = Math.Max(1, ViewH);
        _vs.SmallChange = RowH;
        _vs.Maximum = Math.Max(0, ContentHeight - 1);
        _scroll = Math.Clamp(_scroll, 0, max);
        if (_vs.Visible) _vs.Value = Math.Min(_scroll, _vs.Maximum);
        LayoutCols();
    }

    void LayoutCols()
    {
        int avail = Width - (_vs.Visible ? _vs.Width : 0);
        int fixedW = _cols.Where(c => !c.Fill).Sum(c => c.W);
        int x = 0;
        foreach (var c in _cols)
        {
            c.X = x;
            if (c.Fill) c.W = Math.Max(Theme.S(120), avail - fixedW);
            x += c.W;
        }
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateScroll(); }

    void EnsureVisible(TimeItem item)
    {
        if (item == null) return;
        int y = 0;
        foreach (var r in _rows)
        {
            int h = r is GroupRow ? GroupH : RowH;
            if (r == item)
            {
                if (y < _scroll) _scroll = y;
                else if (y + h > _scroll + ViewH) _scroll = y + h - ViewH;
                UpdateScroll();
                return;
            }
            y += h;
        }
    }

    (object row, Rectangle rect, int index) RowAt(Point p)
    {
        if (p.Y < HeadH) return (null, Rectangle.Empty, -1);
        int y = HeadH - _scroll;
        for (int i = 0; i < _rows.Count; i++)
        {
            int h = _rows[i] is GroupRow ? GroupH : RowH;
            if (p.Y >= y && p.Y < y + h) return (_rows[i], new Rectangle(0, y, Width, h), i);
            y += h;
        }
        return (null, Rectangle.Empty, -1);
    }

    Col ColOf(string id) => _cols.First(c => c.Id == id);

    List<(string id, Rectangle r, string icon, string tip)> ActionButtons(TimeItem it, Rectangle row)
    {
        var c = ColOf("act");
        int s = Theme.S(22), gap = Theme.S(4);
        var list = new List<(string, string, string)>();
        if (it is StopwatchItem) list.Add(("lap", "lap", "Lap"));
        list.Add(("reset", "reset", "Reset"));
        list.Add(("widget", "widget", "Float as desktop widget"));
        int total = list.Count * s + (list.Count - 1) * gap;
        int x = c.X + (c.W - total) / 2, y = row.Y + (row.Height - s) / 2;
        var res = new List<(string, Rectangle, string, string)>();
        foreach (var (id, icon, tip) in list) { res.Add((id, new Rectangle(x, y, s, s), icon, tip)); x += s + gap; }
        return res;
    }

    Rectangle RunButton(Rectangle row)
    {
        var c = ColOf("run"); int s = Theme.S(26);
        return new Rectangle(c.X + (c.W - s) / 2, row.Y + (row.Height - s) / 2, s, s);
    }

    // ------------------------------------------------------------------ painting
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.White);
        LayoutCols();
        var now = DateTime.UtcNow;
        int y = HeadH - _scroll;
        int rowW = Width - (_vs.Visible ? _vs.Width : 0);

        for (int i = 0; i < _rows.Count; i++)
        {
            int h = _rows[i] is GroupRow ? GroupH : RowH;
            if (y + h >= HeadH && y < Height)
            {
                var rr = new Rectangle(0, y, rowW, h);
                if (_rows[i] is GroupRow gr) PaintGroup(g, gr, rr);
                else PaintItem(g, (TimeItem)_rows[i], rr, now, i == _hoverRow);
            }
            y += h;
        }

        if (_rows.Count == 0)
        {
            string msg = _filter != "" ? $"Nothing matches \"{_filter}\"." :
                Kind == ItemKind.Timer ? "No timers yet.\nClick  ➕ Add  to create your first countdown timer." :
                "No stopwatches yet.\nClick  ➕ Add  to create a stopwatch for something you want to track.";
            TextRenderer.DrawText(g, msg, Theme.UI(11f), new Rectangle(0, HeadH + Theme.S(40), rowW, Theme.S(80)), Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
        }

        // header (drawn last so it stays fixed)
        var hr = new Rectangle(0, 0, Width, HeadH);
        using (var br = new SolidBrush(Theme.HeaderBack)) g.FillRectangle(br, hr);
        using (var p = new Pen(Theme.GridLine))
        {
            g.DrawLine(p, 0, HeadH - 1, Width, HeadH - 1);
            foreach (var c in _cols)
            {
                TextRenderer.DrawText(g, c.Title, Font, new Rectangle(c.X + Theme.S(6), 0, c.W - Theme.S(8), HeadH), Color.FromArgb(40, 40, 40), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                g.DrawLine(p, c.X + c.W - 1, Theme.S(4), c.X + c.W - 1, HeadH - Theme.S(4));
            }
        }
    }

    void PaintGroup(Graphics g, GroupRow gr, Rectangle r)
    {
        using (var br = new SolidBrush(Theme.GroupBack)) g.FillRectangle(br, r);
        using (var p = new Pen(Theme.GridLine)) g.DrawLine(p, r.X, r.Bottom - 1, r.Right, r.Bottom - 1);
        int ax = r.X + Theme.S(10), ay = r.Y + r.Height / 2;
        using (var br = new SolidBrush(Color.FromArgb(90, 90, 90)))
        {
            int a = Theme.S(4);
            if (gr.Collapsed) g.FillPolygon(br, new[] { new Point(ax, ay - a), new Point(ax + a, ay), new Point(ax, ay + a) });
            else g.FillPolygon(br, new[] { new Point(ax - a + 2, ay - a / 2), new Point(ax + a + 2, ay - a / 2), new Point(ax + 2, ay + a / 2 + 1) });
        }
        string txt = $"Group : {gr.Name}";
        TextRenderer.DrawText(g, txt, Font, new Rectangle(r.X + Theme.S(22), r.Y, r.Width, r.Height), Color.FromArgb(50, 50, 50), TextFormatFlags.VerticalCenter);
        int tw = TextRenderer.MeasureText(txt, Font).Width;
        string extra = $"({gr.Count})" + (gr.Running > 0 ? $"  •  {gr.Running} running" : "");
        TextRenderer.DrawText(g, extra, Theme.Small, new Rectangle(r.X + Theme.S(26) + tw, r.Y, r.Width, r.Height), gr.Running > 0 ? Theme.Green : Theme.TextDim, TextFormatFlags.VerticalCenter);
    }

    void PaintItem(Graphics g, TimeItem it, Rectangle r, DateTime now, bool hover)
    {
        bool sel = it == _sel;
        var back = it.Color;
        var timer = it as TimerItem;
        var sw = it as StopwatchItem;
        bool finished = timer != null && timer.Finished;
        bool blink = finished && DateTime.Now.Millisecond < 500;

        using (var br = new SolidBrush(back)) g.FillRectangle(br, r);
        if (finished) using (var br = new SolidBrush(Color.FromArgb(blink ? 70 : 30, 230, 60, 50))) g.FillRectangle(br, r);
        if (hover && !sel) using (var br = new SolidBrush(Color.FromArgb(28, 0, 90, 200))) g.FillRectangle(br, r);
        using (var p = new Pen(Theme.GridLine))
        {
            g.DrawLine(p, r.X, r.Bottom - 1, r.Right, r.Bottom - 1);
            foreach (var c in _cols) g.DrawLine(p, c.X + c.W - 1, r.Y, c.X + c.W - 1, r.Bottom - 1);
        }

        foreach (var c in _cols)
        {
            var cr = new Rectangle(c.X, r.Y, c.W, r.Height);
            switch (c.Id)
            {
                case "run":
                    {
                        var b = RunButton(r);
                        g.DrawImage(Icons.Get(it.IsRunning ? "pause" : "start", b.Width), b);
                        break;
                    }
                case "clock":
                    {
                        int s = Theme.S(32);
                        var b = new Rectangle(cr.X + (cr.Width - s) / 2, cr.Y + (cr.Height - s) / 2, s, s);
                        if (timer != null) Icons.Analog(g, b, DateTime.Now, Color.White, Color.FromArgb(70, 70, 70), (float)timer.Progress(now), Color.FromArgb(140, 255, 170, 60));
                        else
                        {
                            double e = it.Elapsed(now);
                            Icons.Analog(g, b, DateTime.Now, Color.White, Color.FromArgb(70, 70, 70), (float)(e % 60 / 60.0), Color.FromArgb(120, 0, 140, 220));
                        }
                        break;
                    }
                case "dur":
                    if (timer.IsTarget)
                        TextRenderer.DrawText(g, "Until\n" + timer.CountdownTo.Value.ToString("d MMM yyyy"), Theme.Bold, cr, Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    else
                        TextRenderer.DrawText(g, Fmt.Long(timer.DurationSeconds), timer.DurationSeconds >= 86400 ? Theme.Bold : Theme.Big, cr, Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    break;
                case "ends":
                    {
                        var end = timer.EndsAtLocal(now);
                        string txt = end == null ? (timer.ElapsedBefore > 0 ? "Paused" : "-") : WidgetRenderer.When(end.Value, true);
                        TextRenderer.DrawText(g, txt, Theme.Normal, new Rectangle(cr.X + Theme.S(4), cr.Y, cr.Width - Theme.S(8), cr.Height), end == null ? Theme.TextDim : (finished ? Theme.Red : Color.Black), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                        break;
                    }
                case "done":
                    TextRenderer.DrawText(g, ActionRunner.WhenDoneText(timer), Theme.Normal, new Rectangle(cr.X + Theme.S(4), cr.Y, cr.Width - Theme.S(8), cr.Height), Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
                    break;
                case "time":
                    {
                        string t; Color fc = Color.Black;
                        if (timer != null)
                        {
                            double rem = timer.Remaining(now);
                            if (finished)
                            {
                                double over = timer.FinishedAtUtc.HasValue ? (now - timer.FinishedAtUtc.Value).TotalSeconds : 0;
                                t = AppState.Settings.TimerOvertime ? "-" + Fmt.Time(over) : "00:00:00";
                                fc = Theme.Red;
                            }
                            else t = Fmt.Long(Math.Ceiling(rem - 0.0001));
                            if (!it.IsRunning && !finished && it.ElapsedBefore > 0) fc = Color.FromArgb(150, 100, 0);
                        }
                        else
                        {
                            t = Fmt.Time(it.Elapsed(now), AppState.Settings.ShowHundredths);
                        }
                        using var f = Theme.UI(t.Length > 9 ? 10f : 12.5f, FontStyle.Bold);
                        if (it.IsRunning)
                        {
                            var hr = Rectangle.Inflate(cr, -Theme.S(5), -Theme.S(7));
                            using var path = Theme.Round(hr, Theme.S(4));
                            using var br = new SolidBrush(Color.FromArgb(110, 255, 255, 255));
                            g.FillPath(br, path);
                        }
                        TextRenderer.DrawText(g, t, f, cr, fc, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                        break;
                    }
                case "lap":
                    {
                        string ln = string.IsNullOrWhiteSpace(sw.CurrentLapName) ? $"Lap {sw.Laps.Count + 1}" : sw.CurrentLapName;
                        var top = new Rectangle(cr.X, cr.Y + Theme.S(4), cr.Width, cr.Height / 2 - Theme.S(2));
                        var bot = new Rectangle(cr.X, cr.Y + cr.Height / 2, cr.Width, cr.Height / 2 - Theme.S(4));
                        TextRenderer.DrawText(g, ln, Theme.Small, top, Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis);
                        TextRenderer.DrawText(g, Fmt.Time(sw.CurrentLap(now)), Theme.Bold, bot, Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
                        break;
                    }
                case "name":
                    {
                        var nr = new Rectangle(cr.X + Theme.S(8), cr.Y, cr.Width - Theme.S(12), cr.Height);
                        TextRenderer.DrawText(g, it.Name, Theme.UI(9.5f), nr, Color.Black, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.Left);
                        if (it.WidgetOpen)
                        {
                            var wr = new Rectangle(cr.Right - Theme.S(20), cr.Y + Theme.S(4), Theme.S(14), Theme.S(14));
                            g.DrawImage(Icons.Get("widget", wr.Width), wr);
                        }
                        break;
                    }
                case "icon":
                    {
                        int s = Theme.S(26);
                        g.DrawImage(Icons.Get(it.IconKey, s), cr.X + (cr.Width - s) / 2, cr.Y + (cr.Height - s) / 2, s, s);
                        break;
                    }
                case "prog":
                    {
                        var pr = new Rectangle(cr.X + Theme.S(8), cr.Y + cr.Height / 2 - Theme.S(7), cr.Width - Theme.S(16), Theme.S(14));
                        using (var path = Theme.Round(pr, Theme.S(7))) using (var br = new SolidBrush(Color.FromArgb(150, 255, 255, 255))) g.FillPath(br, path);
                        double p = timer.Progress(now);
                        if (p > 0)
                        {
                            var fr = new Rectangle(pr.X, pr.Y, Math.Max(Theme.S(14), (int)(pr.Width * p)), pr.Height);
                            using var path = Theme.Round(fr, Theme.S(7));
                            using var br = new LinearGradientBrush(fr, finished ? Theme.Red : Theme.Lighten(Theme.Green, .2f), finished ? Theme.Darken(Theme.Red, .1f) : Theme.Green, 90f);
                            g.FillPath(br, path);
                        }
                        using (var path = Theme.Round(pr, Theme.S(7))) using (var pen = new Pen(Color.FromArgb(60, 0, 0, 0))) g.DrawPath(pen, path);
                        TextRenderer.DrawText(g, $"{p * 100:0}%", Theme.Small, pr, Color.FromArgb(40, 40, 40), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                        break;
                    }
                case "laps":
                    TextRenderer.DrawText(g, sw.Laps.Count.ToString(), Theme.Bold, cr, Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    break;
                case "sum":
                    {
                        string s = Summary(it, now);
                        TextRenderer.DrawText(g, s, Theme.Small, new Rectangle(cr.X + Theme.S(6), cr.Y + Theme.S(2), cr.Width - Theme.S(10), cr.Height - Theme.S(4)), Color.FromArgb(30, 30, 30), TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
                        break;
                    }
                case "act":
                    foreach (var (id, br, icon, _) in ActionButtons(it, r))
                    {
                        if (hover && _hoverAction == id)
                        {
                            using var path = Theme.Round(Rectangle.Inflate(br, Theme.S(2), Theme.S(2)), Theme.S(3));
                            using var b2 = new SolidBrush(Color.FromArgb(120, 255, 255, 255));
                            g.FillPath(b2, path);
                        }
                        bool enabled = id != "lap" || it.IsRunning;
                        g.DrawImage(Icons.Get(icon, br.Width, !enabled), br);
                    }
                    break;
                case "color":
                    {
                        var sr = Rectangle.Inflate(cr, -Theme.S(6), -Theme.S(6));
                        using (var br = new SolidBrush(back)) g.FillRectangle(br, sr);
                        using (var p = new Pen(Color.FromArgb(150, 150, 150))) g.DrawRectangle(p, sr);
                        break;
                    }
            }
        }

        if (sel)
        {
            using var p = new Pen(Theme.Accent, Theme.S(2));
            g.DrawRectangle(p, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3);
        }
    }

    static string Summary(TimeItem it, DateTime now)
    {
        if (it is TimerItem t)
        {
            string s;
            if (t.Finished) s = $"Time is up! (finished {Fmt.Clock(t.FinishedAtUtc?.ToLocalTime() ?? DateTime.Now)})";
            else if (t.IsTarget) s = $"Counts down to {t.CountdownTo.Value:dddd d MMMM yyyy}, {Fmt.Clock(t.CountdownTo.Value, false)}.";
            else if (t.IsRunning) s = $"Running - ends at {Fmt.Clock(DateTime.Now.AddSeconds(t.Remaining(now)))}.";
            else if (t.ElapsedBefore > 0) s = $"Paused with {Fmt.Human(t.Remaining(now))} left.";
            else s = $"Counts down {Fmt.Human(t.DurationSeconds)}.";
            s += $" Sound: {t.Sound}{(t.LoopSound ? " (repeat)" : "")}.";
            if (t.AutoRestart) s += " Auto-restarts.";
            if (t.TimesCompleted > 0) s += $" Completed {t.TimesCompleted}×.";
            return s;
        }
        var sw = (StopwatchItem)it;
        var today = DateTime.Today.ToUniversalTime();
        double todaySecs = AppState.Data.History.Where(h => h.Kind == "Stopwatch" && h.Name == sw.Name && h.EndUtc >= today).Sum(h => h.Seconds);
        if (sw.IsRunning && sw.StartedAtUtc.HasValue) todaySecs += (now - (sw.StartedAtUtc.Value < today ? today : sw.StartedAtUtc.Value)).TotalSeconds;
        string r = sw.IsRunning ? "Running." : sw.ElapsedBefore > 0 ? "Paused." : "Ready.";
        r += $" Today: {Fmt.Human(todaySecs)}.";
        // time per lap title today (finished laps + the lap in progress)
        var perLap = AppState.Data.History.Where(h => h.Kind == "Lap" && h.Parent == sw.Name && h.EndUtc >= today)
            .GroupBy(h => h.Name).ToDictionary(g => g.Key, g => g.Sum(h => h.Seconds));
        double cur = sw.CurrentLap(now);
        if (cur >= 1)
        {
            string cn = string.IsNullOrWhiteSpace(sw.CurrentLapName) ? "Lap " + (sw.Laps.Count + 1) : sw.CurrentLapName.Trim();
            perLap[cn] = perLap.GetValueOrDefault(cn) + cur;
        }
        if (perLap.Count > 0)
            r += " Laps: " + string.Join(", ", perLap.OrderByDescending(kv => kv.Value).Take(4).Select(kv => $"{kv.Key} {Fmt.Human(kv.Value)}")) + (perLap.Count > 4 ? "…" : ".");
        else if (sw.Laps.Count > 0)
        {
            var last = sw.Laps[^1];
            r += $" Last lap: {last.Name} {Fmt.Time(last.LapSeconds, true, false)}.";
        }
        return r;
    }

    // ------------------------------------------------------------------ input
    protected override void OnMouseMove(MouseEventArgs e)
    {
        var (row, rect, idx) = RowAt(e.Location);
        string act = null;
        if (row is TimeItem it)
        {
            if (RunButton(rect).Contains(e.Location)) act = "toggle";
            foreach (var (id, br, _, _) in ActionButtons(it, rect)) if (br.Contains(e.Location)) act = id;
        }
        if (idx != _hoverRow || act != _hoverAction) { _hoverRow = idx; _hoverAction = act; Invalidate(); }
        Cursor = act != null || row is GroupRow ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e) { _hoverRow = -1; _hoverAction = null; Invalidate(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        var (row, rect, _) = RowAt(e.Location);
        if (row is GroupRow gr)
        {
            if (e.Button != MouseButtons.Left) return;
            var key = GKey(gr.Name);
            if (!AppState.Settings.CollapsedGroups.Remove(key)) AppState.Settings.CollapsedGroups.Add(key);
            AppState.Save();
            Rebuild();
            return;
        }
        if (row is TimeItem it)
        {
            if (_sel != it) { _sel = it; SelectionChanged?.Invoke(); Invalidate(); }
            if (e.Button == MouseButtons.Left)
            {
                if (RunButton(rect).Contains(e.Location)) { ActionClicked?.Invoke(it, "toggle"); return; }
                foreach (var (id, br, _, _) in ActionButtons(it, rect))
                    if (br.Contains(e.Location)) { ActionClicked?.Invoke(it, id); return; }
            }
            if (e.Button == MouseButtons.Right) ContextMenuRequested?.Invoke(it, e.Location);
        }
        else if (e.Button == MouseButtons.Right) ContextMenuRequested?.Invoke(null, e.Location);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        var (row, rect, _) = RowAt(e.Location);
        if (row is TimeItem it && !RunButton(rect).Contains(e.Location) && !ActionButtons(it, rect).Any(a => a.r.Contains(e.Location)))
            ItemDoubleClicked?.Invoke(it);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (!_vs.Visible) return;
        _scroll = Math.Clamp(_scroll - e.Delta / 120 * RowH, 0, Math.Max(0, ContentHeight - ViewH));
        _vs.Value = Math.Min(_scroll, _vs.Maximum);
        Invalidate();
    }

    protected override bool IsInputKey(Keys k) => k is Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.Space or Keys.Enter || base.IsInputKey(k);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var items = _rows.OfType<TimeItem>().ToList();
        if (items.Count == 0) return;
        int i = _sel == null ? -1 : items.IndexOf(_sel);
        switch (e.KeyCode)
        {
            case Keys.Down: Selected = items[Math.Min(items.Count - 1, i + 1)]; e.Handled = true; break;
            case Keys.Up: Selected = items[Math.Max(0, i - 1)]; e.Handled = true; break;
            case Keys.Home: Selected = items[0]; break;
            case Keys.End: Selected = items[^1]; break;
            case Keys.Space: if (_sel != null) ActionClicked?.Invoke(_sel, "toggle"); e.Handled = true; break;
            case Keys.Enter: if (_sel != null) ItemDoubleClicked?.Invoke(_sel); e.Handled = true; break;
        }
    }
}

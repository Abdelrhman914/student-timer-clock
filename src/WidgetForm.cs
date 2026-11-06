using System.Runtime.InteropServices;

namespace StudentTimerClock;

/// <summary>Floating, resizable, fully customisable desktop widget for one timer / stopwatch.</summary>
public class WidgetForm : Form
{
    public readonly TimeItem Item;
    readonly System.Windows.Forms.Timer _tick = new();
    Point _downAt, _offset;
    bool _down, _dragging, _hover;
    string _hoverBtn;
    readonly ToolTip _tip = new();

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
    static readonly bool Win11 = Environment.OSVersion.Version.Build >= 22000;

    WidgetStyle St => WidgetRenderer.Effective(Item);

    public WidgetForm(TimeItem item)
    {
        Item = item;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Text = item.Name;
        Font = Theme.Normal;
        ApplySettings();
        PlaceInitially();
        _tick.Tick += (s, e) => { Invalidate(); AdjustInterval(); };
        AdjustInterval();
        _tick.Start();
        ContextMenuStrip = BuildMenu();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80;        // WS_EX_TOOLWINDOW: hide from Alt+Tab
            cp.ClassStyle |= 0x20000;  // CS_DROPSHADOW
            return cp;
        }
    }
    protected override bool ShowWithoutActivation => true;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyCorners();
    }

    void ApplyCorners()
    {
        if (!IsHandleCreated) return;
        if (Win11)
        {
            int pref = St.RoundCorners ? 2 : 1;
            try { DwmSetWindowAttribute(Handle, 33, ref pref, 4); } catch { }
            Region = null;
        }
        else if (St.RoundCorners)
        {
            using var p = Theme.Round(new RectangleF(0, 0, Width, Height), Theme.S(10));
            Region = new Region(p);
        }
        else Region = null;
    }

    public void ApplySettings()
    {
        var st = St;
        TopMost = st.TopMost;
        Opacity = Math.Clamp(st.Opacity, 20, 100) / 100.0;
        MinimumSize = WidgetRenderer.MinSize(st.Layout);
        var def = WidgetRenderer.DefaultSize(st.Layout);
        Size = new Size(Item.WidgetW > 0 ? Item.WidgetW : def.Width, Item.WidgetH > 0 ? Item.WidgetH : def.Height);
        Text = Item.Name;
        ApplyCorners();
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (!Win11 && IsHandleCreated) ApplyCorners();
    }

    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        Item.WidgetW = Width; Item.WidgetH = Height;
        AppState.Save();
    }

    void PlaceInitially()
    {
        var wa = Screen.PrimaryScreen.WorkingArea;
        var pt = new Point(Item.WidgetX, Item.WidgetY);
        bool onScreen = Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(new Rectangle(pt, new Size(40, 20))));
        if (Item.WidgetX < 0 || !onScreen)
        {
            // find a free spot: columns from the right edge, stacked top to bottom
            var others = Application.OpenForms.OfType<WidgetForm>().Where(w => w != this && w.Visible).Select(w => w.Bounds).ToList();
            int gap = Theme.S(10);
            pt = new Point(wa.Right - Width - Theme.S(20), wa.Top + Theme.S(20));
            for (int guard = 0; guard < 200; guard++)
            {
                var r = new Rectangle(pt, Size);
                var hit = others.FirstOrDefault(o => o.IntersectsWith(Rectangle.Inflate(r, gap / 2, gap / 2)));
                if (hit.IsEmpty) break;
                pt.Y = hit.Bottom + gap;
                if (pt.Y + Height > wa.Bottom) { pt.Y = wa.Top + Theme.S(20); pt.X -= Width + gap; if (pt.X < wa.Left) { pt = new Point(wa.Right - Width - Theme.S(20), wa.Top + Theme.S(20)); break; } }
            }
        }
        Location = pt;
    }

    void AdjustInterval()
    {
        var st = St;
        int iv = Item.IsRunning ? (Item is StopwatchItem && st.ShowHundredths && st.ShowSeconds ? 45 : 200) : (Item is TimerItem t && t.Finished ? 250 : 500);
        if (_tick.Interval != iv) _tick.Interval = iv;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        WidgetRenderer.Draw(e.Graphics, ClientRectangle, Item, St, _hover, _hoverBtn, DateTime.UtcNow, chrome: true);
    }

    // ------------------------------------------------------------ resize by dragging edges / corner
    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x84;
        if (m.Msg == WM_NCHITTEST && !St.Locked)
        {
            var p = PointToClient(new Point((short)(m.LParam.ToInt64() & 0xFFFF), (short)((m.LParam.ToInt64() >> 16) & 0xFFFF)));
            int grip = Theme.S(10), edge = Theme.S(5);
            bool right = p.X >= Width - edge, bottom = p.Y >= Height - edge;
            if (p.X >= Width - grip && p.Y >= Height - grip) { m.Result = (IntPtr)17; return; } // HTBOTTOMRIGHT
            if (right) { m.Result = (IntPtr)11; return; }  // HTRIGHT
            if (bottom) { m.Result = (IntPtr)15; return; } // HTBOTTOM
        }
        base.WndProc(ref m);
    }

    // ------------------------------------------------------------ mouse
    string HitButton(Point p) => WidgetRenderer.Buttons(Item, St, ClientRectangle, _hover, true).FirstOrDefault(b => b.R.Contains(p))?.Id;

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e)
    {
        if (!ClientRectangle.Contains(PointToClient(Cursor.Position))) { _hover = false; _hoverBtn = null; Invalidate(); }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _down = true; _dragging = false; _downAt = Cursor.Position; _offset = new Point(Cursor.Position.X - Left, Cursor.Position.Y - Top);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_hover) { _hover = true; Invalidate(); }
        if (_down)
        {
            if (St.Locked) return;
            var c = Cursor.Position;
            if (!_dragging && (Math.Abs(c.X - _downAt.X) > 3 || Math.Abs(c.Y - _downAt.Y) > 3)) _dragging = true;
            if (_dragging) Location = Snap(new Point(c.X - _offset.X, c.Y - _offset.Y));
            return;
        }
        var h = HitButton(e.Location);
        if (h != _hoverBtn)
        {
            _hoverBtn = h; Invalidate();
            var tip = WidgetRenderer.Buttons(Item, St, ClientRectangle, _hover, true).FirstOrDefault(b => b.Id == h)?.Tip;
            _tip.SetToolTip(this, tip);
            Cursor = h != null ? Cursors.Hand : Cursors.Default;
        }
    }

    Point Snap(Point p)
    {
        if (!AppState.Settings.WidgetSnapToEdges) return p;
        int d = Theme.S(14);
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        if (Math.Abs(p.X - wa.Left) < d) p.X = wa.Left;
        if (Math.Abs(p.Y - wa.Top) < d) p.Y = wa.Top;
        if (Math.Abs(p.X + Width - wa.Right) < d) p.X = wa.Right - Width;
        if (Math.Abs(p.Y + Height - wa.Bottom) < d) p.Y = wa.Bottom - Height;
        foreach (var w in Application.OpenForms.OfType<WidgetForm>())
        {
            if (w == this) continue;
            var r = w.Bounds;
            if (Math.Abs(p.X - r.Left) < d && Math.Abs(p.Y - r.Bottom) < d * 3) { p.X = r.Left; p.Y = r.Bottom + Theme.S(6); }
            else if (Math.Abs(p.X - r.Left) < d && Math.Abs(p.Y + Height - r.Top) < d * 3) { p.X = r.Left; p.Y = r.Top - Height - Theme.S(6); }
        }
        return p;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        bool wasDrag = _dragging;
        _down = false; _dragging = false;
        if (wasDrag)
        {
            Item.WidgetX = Left; Item.WidgetY = Top;
            AppState.Save();
            return;
        }
        if (e.Button != MouseButtons.Left) return;
        switch (HitButton(e.Location))
        {
            case "close": CloseWidget(); return;
            case "stylebtn": EditStyle(); return;
            case "toggle":
                if (Item is TimerItem tt && tt.Finished) { Program.Main?.DismissTimer(tt); AppState.Start(tt); }
                else AppState.Toggle(Item);
                break;
            case "reset":
                if (Item is TimerItem tr && tr.Finished) Program.Main?.DismissTimer(tr);
                else AppState.Reset(Item);
                break;
            case "lap": if (Item is StopwatchItem sw && sw.IsRunning) AppState.Lap(sw); break;
            default:
                if (Item is TimerItem t && t.Finished) Program.Main?.DismissTimer(t);
                break;
        }
        AdjustInterval();
        Invalidate();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (HitButton(e.Location) != null) return;
        Program.Main?.OpenItem(Item);
    }

    public void EditStyle()
    {
        using var f = new WidgetStyleForm(Item);
        f.ShowDialog(this);
        Program.Main?.RefreshWidgets();
    }

    public void CloseWidget()
    {
        Item.WidgetOpen = false;
        Item.WidgetX = Left; Item.WidgetY = Top;
        AppState.NotifyChanged();
        Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e) { _tick.Stop(); _tick.Dispose(); base.OnFormClosed(e); }

    // ------------------------------------------------------------ menu
    ContextMenuStrip BuildMenu()
    {
        var m = new ContextMenuStrip();
        m.Opening += (s, e) =>
        {
            m.Items.Clear();
            var st = St;
            m.Items.Add(Item.IsRunning ? "Pause" : "Start", Icons.Get(Item.IsRunning ? "pause" : "start", 16), (a, b) => AppState.Toggle(Item));
            if (Item is StopwatchItem sw) m.Items.Add("Lap", Icons.Get("lap", 16), (a, b) => AppState.Lap(sw)).Enabled = Item.IsRunning;
            m.Items.Add("Reset", Icons.Get("reset", 16), (a, b) => AppState.Reset(Item));
            if (Item is TimerItem t)
            {
                m.Items.Add("+1 minute", null, (a, b) => AppState.AddTime(t, 60));
                m.Items.Add("+5 minutes", null, (a, b) => AppState.AddTime(t, 300));
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Font, colours && background…", Icons.Get("style", 16), (a, b) => EditStyle()).Font = Theme.Bold;
            var layout = new ToolStripMenuItem("Layout");
            foreach (var (id, text) in new[] { ("Digital", "Digital (DAY HOUR MIN SEC)"), ("Dial", "Clock dial"), ("Compact", "Compact bar") })
                layout.DropDownItems.Add(new ToolStripMenuItem(text, null, (a, b) => SetStyle(x => { x.Layout = id; Item.WidgetW = Item.WidgetH = 0; })) { Checked = st.Layout == id });
            m.Items.Add(layout);
            var preset = new ToolStripMenuItem("Colour theme");
            foreach (var p in WidgetStyle.Presets)
                preset.DropDownItems.Add(new ToolStripMenuItem(p, null, (a, b) => SetStyle(x => { x.ApplyPreset(p); x.BackImage = ""; })) { Checked = st.Preset == p && string.IsNullOrEmpty(st.BackImage) });
            m.Items.Add(preset);
            var op = new ToolStripMenuItem("Opacity");
            foreach (var o in new[] { 100, 90, 80, 70, 60, 50, 40 })
                op.DropDownItems.Add(new ToolStripMenuItem(o + "%", null, (a, b) => SetStyle(x => x.Opacity = o)) { Checked = st.Opacity == o });
            m.Items.Add(op);
            m.Items.Add(new ToolStripMenuItem("Show date", null, (a, b) => SetStyle(x => x.ShowDate = !x.ShowDate)) { Checked = st.ShowDate });
            m.Items.Add(new ToolStripMenuItem("Show end time", null, (a, b) => SetStyle(x => x.ShowEndTime = !x.ShowEndTime)) { Checked = st.ShowEndTime });
            m.Items.Add(new ToolStripMenuItem("Always on top", null, (a, b) => SetStyle(x => x.TopMost = !x.TopMost)) { Checked = st.TopMost });
            m.Items.Add(new ToolStripMenuItem("Lock position && size", null, (a, b) => SetStyle(x => x.Locked = !x.Locked)) { Checked = st.Locked });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Item is StopwatchItem ? "Open stopwatch window" : "Edit timer…", Icons.Get("edit", 16), (a, b) => Program.Main?.OpenItem(Item));
            m.Items.Add("Show main window", null, (a, b) => Program.Main?.ShowMain());
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Close widget", Icons.Get("delete", 16), (a, b) => CloseWidget());
            e.Cancel = false;
        };
        m.Items.Add("-");
        return m;
    }

    void SetStyle(Action<WidgetStyle> change)
    {
        Item.Style ??= AppState.Settings.DefaultWidgetStyle.Clone();
        change(Item.Style);
        ApplySettings();
        AppState.Save();
    }
}

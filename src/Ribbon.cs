namespace StudentTimerClock;

public class RibbonButton
{
    public string Id, Text, Icon, Tooltip;
    public bool Large = true, Enabled = true, Visible = true;
    public Action Click;
    public ContextMenuStrip DropDown;
    internal Rectangle Bounds;
}

public class RibbonGroup
{
    public string Title;
    public List<RibbonButton> Buttons = new();
    internal Rectangle Bounds;
}

/// <summary>Office-style ribbon (tabs + grouped big buttons) like Hot Alarm Clock's toolbar.</summary>
public class Ribbon : Control
{
    public List<RibbonGroup> Groups = new();
    public ContextMenuStrip FileMenu;
    public string[] Tabs = { "File", "Home" };
    RibbonButton _hover, _pressed;
    Rectangle _fileTab;
    readonly ToolTip _tip = new() { InitialDelay = 600 };
    RibbonButton _tipFor;

    int TabH => Theme.S(26);
    int BodyH => Theme.S(92);

    public Ribbon()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Dock = DockStyle.Top;
        Height = TabH + BodyH;
        Font = Theme.Normal;
        BackColor = Color.White;
    }

    public RibbonButton this[string id] => Groups.SelectMany(g => g.Buttons).FirstOrDefault(b => b.Id == id);

    public void SetEnabled(string id, bool en)
    {
        var b = this[id];
        if (b != null && b.Enabled != en) { b.Enabled = en; Invalidate(); }
    }
    public void SetButton(string id, string text, string icon)
    {
        var b = this[id];
        if (b != null && (b.Text != text || b.Icon != icon)) { b.Text = text; b.Icon = icon; DoLayout(); Invalidate(); }
    }

    public void DoLayout()
    {
        using var g = CreateGraphics();
        int x = Theme.S(6), top = TabH + Theme.S(4), labelH = Theme.S(18);
        int areaH = BodyH - Theme.S(8) - labelH;
        foreach (var grp in Groups)
        {
            int gx = x;
            int smallCol = -1, smallRow = 0, smallColW = 0;
            foreach (var b in grp.Buttons.Where(b => b.Visible))
            {
                if (b.Large)
                {
                    if (smallCol >= 0) { x += smallColW + Theme.S(4); smallCol = -1; }
                    var lines = SplitText(b.Text);
                    int tw = lines.Max(l => TextRenderer.MeasureText(g, l, Font).Width);
                    int w = Math.Max(Theme.S(44), tw + Theme.S(10));
                    b.Bounds = new Rectangle(x, top, w, areaH);
                    x += w + Theme.S(2);
                }
                else
                {
                    int rowH = areaH / 3;
                    if (smallCol < 0 || smallRow >= 3) { if (smallCol >= 0) x += smallColW + Theme.S(4); smallCol = x; smallRow = 0; smallColW = 0; }
                    int w = TextRenderer.MeasureText(g, b.Text, Font).Width + Theme.S(28);
                    smallColW = Math.Max(smallColW, w);
                    b.Bounds = new Rectangle(smallCol, top + Theme.S(2) + smallRow * rowH, w, rowH);
                    smallRow++;
                }
            }
            if (smallCol >= 0) x += smallColW + Theme.S(4);
            int titleW = TextRenderer.MeasureText(g, grp.Title, Font).Width + Theme.S(12);
            if (x - gx < titleW)
            {
                int shift = (titleW - (x - gx)) / 2;
                foreach (var b in grp.Buttons) b.Bounds.Offset(shift, 0);
                x = gx + titleW;
            }
            grp.Bounds = new Rectangle(gx, top, x - gx, BodyH - Theme.S(6));
            x += Theme.S(10);
        }
    }

    static string[] SplitText(string t)
    {
        if (!t.Contains(' ') || t.Length < 9) return new[] { t };
        int mid = t.Length / 2, best = -1;
        for (int i = 0; i < t.Length; i++) if (t[i] == ' ' && (best < 0 || Math.Abs(i - mid) < Math.Abs(best - mid))) best = i;
        return new[] { t[..best], t[(best + 1)..] };
    }

    protected override void OnCreateControl() { base.OnCreateControl(); DoLayout(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.White);
        // tabs
        int tx = Theme.S(8);
        for (int i = 0; i < Tabs.Length; i++)
        {
            var sz = TextRenderer.MeasureText(Tabs[i], Font);
            var tr = new Rectangle(tx, 0, sz.Width + Theme.S(20), TabH);
            if (i == 0) _fileTab = tr;
            bool sel = i == 1;
            TextRenderer.DrawText(g, Tabs[i], Font, tr, sel ? Color.Black : Color.FromArgb(50, 50, 50), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (sel) using (var br = new SolidBrush(Theme.Accent)) g.FillRectangle(br, tr.X + Theme.S(6), TabH - Theme.S(3), tr.Width - Theme.S(12), Theme.S(2));
            tx += tr.Width;
        }
        // body
        var body = new Rectangle(0, TabH, Width, BodyH);
        using (var br = new SolidBrush(Theme.RibbonBack)) g.FillRectangle(br, body);
        using (var p = new Pen(Theme.Border)) { g.DrawLine(p, 0, TabH, Width, TabH); g.DrawLine(p, 0, Height - 1, Width, Height - 1); }

        foreach (var grp in Groups)
        {
            var lr = new Rectangle(grp.Bounds.X, Height - Theme.S(22), grp.Bounds.Width, Theme.S(18));
            TextRenderer.DrawText(g, grp.Title, Font, lr, Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, grp.Bounds.Right + Theme.S(5), TabH + Theme.S(8), grp.Bounds.Right + Theme.S(5), Height - Theme.S(8));
            foreach (var b in grp.Buttons.Where(b => b.Visible))
            {
                var r = b.Bounds;
                if (b.Enabled && (b == _hover || b == _pressed))
                {
                    using var path = Theme.Round(new RectangleF(r.X, r.Y, r.Width - 1, r.Height - 1), Theme.S(3));
                    using var br = new SolidBrush(b == _pressed ? Theme.AccentLight : Theme.AccentHover);
                    using var p = new Pen(Color.FromArgb(160, 200, 235));
                    g.FillPath(br, path); g.DrawPath(p, path);
                }
                var fg = b.Enabled ? Color.FromArgb(30, 30, 30) : Color.FromArgb(160, 160, 160);
                if (b.Large)
                {
                    int isz = Theme.S(32);
                    g.DrawImage(Icons.Get(b.Icon, isz, !b.Enabled), r.X + (r.Width - isz) / 2, r.Y + Theme.S(4), isz, isz);
                    var lines = SplitText(b.Text);
                    int ly = r.Y + isz + Theme.S(7);
                    foreach (var l in lines)
                    {
                        TextRenderer.DrawText(g, l, Font, new Rectangle(r.X, ly, r.Width, Theme.S(16)), fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
                        ly += Theme.S(15);
                    }
                    if (b.DropDown != null)
                        using (var br = new SolidBrush(fg)) { int ax = r.X + r.Width / 2, ay = ly + Theme.S(3); g.FillPolygon(br, new[] { new Point(ax - 3, ay), new Point(ax + 3, ay), new Point(ax, ay + 3) }); }
                }
                else
                {
                    int isz = Theme.S(16);
                    g.DrawImage(Icons.Get(b.Icon, isz, !b.Enabled), r.X + Theme.S(4), r.Y + (r.Height - isz) / 2, isz, isz);
                    TextRenderer.DrawText(g, b.Text, Font, new Rectangle(r.X + isz + Theme.S(8), r.Y, r.Width - isz - Theme.S(8), r.Height), fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                }
            }
        }
    }

    RibbonButton HitTest(Point p) => Groups.SelectMany(g => g.Buttons).FirstOrDefault(b => b.Visible && b.Bounds.Contains(p));

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var h = HitTest(e.Location);
        if (h != _hover) { _hover = h; Invalidate(); }
        if (h != _tipFor)
        {
            _tipFor = h;
            if (h?.Tooltip != null) _tip.SetToolTip(this, h.Tooltip); else _tip.SetToolTip(this, null);
        }
    }
    protected override void OnMouseLeave(EventArgs e) { _hover = null; _pressed = null; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (_fileTab.Contains(e.Location) && FileMenu != null) { FileMenu.Show(this, new Point(_fileTab.X, _fileTab.Bottom)); return; }
        _pressed = HitTest(e.Location); Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        var b = _pressed; _pressed = null; Invalidate();
        if (b != null && b.Enabled && b.Bounds.Contains(e.Location))
        {
            if (b.DropDown != null) b.DropDown.Show(this, new Point(b.Bounds.X, b.Bounds.Bottom));
            else b.Click?.Invoke();
        }
    }
}

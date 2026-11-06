namespace StudentTimerClock;

/// <summary>Left side navigation (Alarms / Timers / Clocks ... style).</summary>
public class NavBar : Control
{
    public record Item(string Text, string Icon);
    public List<Item> Items = new();
    int _sel, _hover = -1;
    public event Action<int> SelectedChanged;
    public Func<int, string> Badge; // optional small text under label (e.g. "2 running")

    public NavBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Dock = DockStyle.Left;
        Width = Theme.S(86);
        Font = Theme.Normal;
    }

    public int Selected
    {
        get => _sel;
        set { if (value == _sel) return; _sel = value; Invalidate(); SelectedChanged?.Invoke(value); }
    }

    int ItemH => Theme.S(70);
    Rectangle ItemRect(int i) => new(Theme.S(4), Theme.S(6) + i * (ItemH + Theme.S(2)), Width - Theme.S(8), ItemH);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.White);
        using (var p = new Pen(Theme.Border)) g.DrawLine(p, Width - 1, 0, Width - 1, Height);
        for (int i = 0; i < Items.Count; i++)
        {
            var r = ItemRect(i);
            if (i == _sel || i == _hover)
            {
                using var path = Theme.Round(r, Theme.S(3));
                using var br = new SolidBrush(i == _sel ? Theme.AccentLight : Theme.AccentHover);
                g.FillPath(br, path);
            }
            int isz = Theme.S(32);
            g.DrawImage(Icons.Get(Items[i].Icon, isz), r.X + (r.Width - isz) / 2, r.Y + Theme.S(6), isz, isz);
            TextRenderer.DrawText(g, Items[i].Text, Font, new Rectangle(r.X, r.Y + isz + Theme.S(10), r.Width, Theme.S(18)), Color.FromArgb(30, 30, 30), TextFormatFlags.HorizontalCenter);
            var badge = Badge?.Invoke(i);
            if (!string.IsNullOrEmpty(badge))
            {
                using var f = Theme.SafeFont("Segoe UI", 7f, FontStyle.Bold);
                var sz = TextRenderer.MeasureText(badge, f);
                var br2 = new Rectangle(r.Right - sz.Width - Theme.S(6), r.Y + Theme.S(3), sz.Width + Theme.S(2), sz.Height);
                using var path = Theme.Round(br2, br2.Height / 2f);
                using var b = new SolidBrush(Theme.Green);
                g.FillPath(b, path);
                TextRenderer.DrawText(g, badge, f, br2, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
        // live clock at the bottom
        var now = DateTime.Now;
        int cs = Theme.S(46);
        var cr = new Rectangle((Width - cs) / 2, Height - cs - Theme.S(44), cs, cs);
        if (cr.Y > ItemRect(Items.Count - 1).Bottom + Theme.S(10))
        {
            Icons.Analog(g, cr, now, Color.White, Color.FromArgb(60, 60, 60));
            using var sec = new Pen(Theme.Red, 1);
            float cx = cr.X + cs / 2f, cy = cr.Y + cs / 2f; double a = now.Second * Math.PI / 30 - Math.PI / 2;
            g.DrawLine(sec, cx, cy, cx + (float)Math.Cos(a) * cs * .36f, cy + (float)Math.Sin(a) * cs * .36f);
            TextRenderer.DrawText(g, Fmt.Clock(now), Theme.Bold, new Rectangle(0, cr.Bottom + Theme.S(4), Width, Theme.S(16)), Color.FromArgb(40, 40, 40), TextFormatFlags.HorizontalCenter);
            TextRenderer.DrawText(g, now.ToString("ddd dd MMM"), Theme.Small, new Rectangle(0, cr.Bottom + Theme.S(20), Width, Theme.S(16)), Theme.TextDim, TextFormatFlags.HorizontalCenter);
        }
    }

    int Hit(Point p) { for (int i = 0; i < Items.Count; i++) if (ItemRect(i).Contains(p)) return i; return -1; }
    protected override void OnMouseMove(MouseEventArgs e) { int h = Hit(e.Location); if (h != _hover) { _hover = h; Invalidate(); Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; } }
    protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { int h = Hit(e.Location); if (h >= 0) Selected = h; }
}

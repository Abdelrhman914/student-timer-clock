using System.Drawing.Drawing2D;

namespace StudentTimerClock;

public enum ChartType { Bar, Pie, HorizontalBar }

public class ChartPoint
{
    public string Label;
    public double Value;   // seconds
    public Color Color;
    public int Count;
}

/// <summary>Owner-drawn bar / pie chart for "how long did I spend on what".</summary>
public class ChartControl : Control
{
    public List<ChartPoint> Points = new();
    public string Title = "";
    public string Subtitle = "";
    public ChartType Type = ChartType.Bar;
    public bool ShowPercent = true;
    readonly List<(RectangleF r, GraphicsPath path, int idx)> _hit = new();
    int _hover = -1;
    readonly ToolTip _tip = new();

    public ChartControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.White;
        Font = Theme.Normal;
    }

    public void SetData(IEnumerable<ChartPoint> pts) { Points = pts.ToList(); _hover = -1; Invalidate(); }

    static string AxisLabel(double s) => s >= 3600 ? $"{s / 3600:0.#}h" : s >= 60 ? $"{s / 60:0.#}m" : $"{s:0.#}s";

    static double NiceStep(double max, int ticks)
    {
        double[] steps = { 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200, 10800, 21600, 43200, 86400, 172800, 432000, 864000 };
        foreach (var s in steps) if (max / s <= ticks) return s;
        return Math.Ceiling(max / ticks / 86400) * 86400;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(BackColor);
        foreach (var h in _hit) h.path?.Dispose();
        _hit.Clear();

        int top = Theme.S(8);
        if (!string.IsNullOrEmpty(Title))
        {
            TextRenderer.DrawText(g, Title, Theme.UI(11f, FontStyle.Bold), new Rectangle(0, top, Width, Theme.S(22)), Color.Black, TextFormatFlags.HorizontalCenter);
            top += Theme.S(22);
        }
        if (!string.IsNullOrEmpty(Subtitle))
        {
            TextRenderer.DrawText(g, Subtitle, Theme.Small, new Rectangle(0, top, Width, Theme.S(16)), Theme.TextDim, TextFormatFlags.HorizontalCenter);
            top += Theme.S(18);
        }
        var area = new Rectangle(Theme.S(10), top + Theme.S(6), Width - Theme.S(20), Height - top - Theme.S(14));
        if (Points.Count == 0 || Points.All(p => p.Value <= 0))
        {
            TextRenderer.DrawText(g, "No recorded time for this selection yet.\nStart a timer or stopwatch - every run is recorded here automatically.", Theme.UI(10f), area, Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }
        if (area.Width < 50 || area.Height < 50) return;
        var type = Type;
        if (type == ChartType.Bar && Points.Count > Math.Max(6, area.Width / Theme.S(70))) type = ChartType.HorizontalBar;
        if (type == ChartType.Pie) DrawPie(g, area);
        else if (type == ChartType.Bar) DrawBars(g, area);
        else DrawHBars(g, area);
    }

    void DrawBars(Graphics g, Rectangle area)
    {
        double max = Points.Max(p => p.Value);
        double step = NiceStep(max * 1.15, 6);
        double axisMax = Math.Ceiling(max * 1.15 / step) * step;
        int labelW = Theme.S(48);
        var plot = new Rectangle(area.X + labelW, area.Y + Theme.S(10), area.Width - labelW - Theme.S(10), area.Height - Theme.S(44));
        using var gridPen = new Pen(Color.FromArgb(230, 232, 235));
        using var axisPen = new Pen(Color.FromArgb(80, 80, 80));
        for (double v = 0; v <= axisMax + 0.001; v += step)
        {
            float y = plot.Bottom - (float)(v / axisMax * plot.Height);
            g.DrawLine(gridPen, plot.X, y, plot.Right, y);
            TextRenderer.DrawText(g, AxisLabel(v), Theme.Small, new Rectangle(area.X, (int)y - Theme.S(8), labelW - Theme.S(6), Theme.S(16)), Theme.TextDim, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }
        g.DrawLine(axisPen, plot.X, plot.Y, plot.X, plot.Bottom);
        g.DrawLine(axisPen, plot.X, plot.Bottom, plot.Right, plot.Bottom);

        int n = Points.Count;
        float slot = plot.Width / (float)n;
        float bw = Math.Min(slot * 0.62f, Theme.S(110));
        double total = Points.Sum(p => p.Value);
        for (int i = 0; i < n; i++)
        {
            var p = Points[i];
            float h = (float)(p.Value / axisMax * plot.Height);
            var br = new RectangleF(plot.X + slot * i + (slot - bw) / 2, plot.Bottom - h, bw, h);
            var c = p.Color;
            if (i == _hover) c = Theme.Lighten(c, .25f);
            using (var b = new LinearGradientBrush(new RectangleF(br.X, br.Y - 1, br.Width, br.Height + 2), Theme.Lighten(c, .12f), c, 0f)) g.FillRectangle(b, br);
            using (var pen = new Pen(Theme.Darken(p.Color, .35f))) g.DrawRectangle(pen, br.X, br.Y, br.Width, br.Height);
            var path = new GraphicsPath(); path.AddRectangle(br);
            _hit.Add((br, path, i));

            // callout label above bar (like Hot Alarm Clock's lap report)
            string l1 = p.Label, l2 = Fmt.Time(p.Value, p.Value < 60, false);
            using var f = Theme.UI(8f);
            var s1 = TextRenderer.MeasureText(l1, f); var s2 = TextRenderer.MeasureText(l2, f);
            int cw = Math.Min((int)slot - 4, Math.Max(s1.Width, s2.Width) + Theme.S(8)), ch = s1.Height + s2.Height + Theme.S(2);
            var cr = new Rectangle((int)(br.X + br.Width / 2 - cw / 2f), (int)br.Y - ch - Theme.S(6), cw, ch);
            if (cr.Y < area.Y) cr.Y = area.Y;
            using (var b = new SolidBrush(Color.FromArgb(255, 252, 220))) g.FillRectangle(b, cr);
            using (var pen = new Pen(Color.FromArgb(120, 120, 120))) g.DrawRectangle(pen, cr);
            TextRenderer.DrawText(g, l1, f, new Rectangle(cr.X, cr.Y + 1, cr.Width, s1.Height), Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, l2, f, new Rectangle(cr.X, cr.Y + s1.Height, cr.Width, s2.Height), Color.Black, TextFormatFlags.HorizontalCenter);

            string pct = ShowPercent ? $"{p.Value / total * 100:0.#}%" : "";
            TextRenderer.DrawText(g, p.Label, Theme.Small, new Rectangle((int)(plot.X + slot * i), plot.Bottom + Theme.S(4), (int)slot, Theme.S(16)), Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, pct, Theme.Small, new Rectangle((int)(plot.X + slot * i), plot.Bottom + Theme.S(20), (int)slot, Theme.S(16)), Theme.TextDim, TextFormatFlags.HorizontalCenter);
        }
    }

    void DrawHBars(Graphics g, Rectangle area)
    {
        double max = Points.Max(p => p.Value), total = Points.Sum(p => p.Value);
        int labelW = Math.Min(Theme.S(170), Points.Max(p => TextRenderer.MeasureText(p.Label, Theme.Small).Width) + Theme.S(12));
        int valW = Theme.S(120);
        float rowH = Math.Min(Theme.S(30), area.Height / (float)Points.Count);
        float bh = Math.Max(3, rowH * 0.7f);
        int plotW = area.Width - labelW - valW;
        for (int i = 0; i < Points.Count; i++)
        {
            var p = Points[i];
            float y = area.Y + i * rowH;
            TextRenderer.DrawText(g, p.Label, Theme.Small, new Rectangle(area.X, (int)y, labelW - Theme.S(6), (int)rowH), Color.Black, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            float w = (float)(p.Value / max * plotW);
            var br = new RectangleF(area.X + labelW, y + (rowH - bh) / 2, Math.Max(2, w), bh);
            var c = i == _hover ? Theme.Lighten(p.Color, .25f) : p.Color;
            using (var b = new SolidBrush(c)) g.FillRectangle(b, br);
            using (var pen = new Pen(Theme.Darken(p.Color, .35f))) g.DrawRectangle(pen, br.X, br.Y, br.Width, br.Height);
            var path = new GraphicsPath(); path.AddRectangle(br);
            _hit.Add((br, path, i));
            TextRenderer.DrawText(g, $"{Fmt.Human(p.Value)}  ({p.Value / total * 100:0.#}%)", Theme.Small, new Rectangle((int)br.Right + Theme.S(6), (int)y, valW + plotW - (int)w, (int)rowH), Theme.TextDim, TextFormatFlags.VerticalCenter);
        }
    }

    void DrawPie(Graphics g, Rectangle area)
    {
        double total = Points.Sum(p => p.Value);
        int legendW = Math.Min(Theme.S(260), area.Width / 2);
        int d = Math.Min(area.Width - legendW - Theme.S(20), area.Height - Theme.S(10));
        if (d < 40) d = Math.Min(area.Width, area.Height) - 10;
        var pr = new RectangleF(area.X + (area.Width - legendW - d) / 2f, area.Y + (area.Height - d) / 2f, d, d);
        float start = -90;
        for (int i = 0; i < Points.Count; i++)
        {
            var p = Points[i];
            float sweep = (float)(p.Value / total * 360);
            if (sweep <= 0) continue;
            var r = pr;
            if (i == _hover)
            {
                double mid = (start + sweep / 2) * Math.PI / 180;
                r.Offset((float)Math.Cos(mid) * Theme.S(8), (float)Math.Sin(mid) * Theme.S(8));
            }
            var path = new GraphicsPath();
            if (sweep >= 359.99f) path.AddEllipse(r); else path.AddPie(r.X, r.Y, r.Width, r.Height, start, sweep);
            using (var b = new SolidBrush(p.Color)) g.FillPath(b, path);
            using (var pen = new Pen(Color.White, Theme.S(2))) g.DrawPath(pen, path);
            _hit.Add((r, path, i));
            if (sweep > 18)
            {
                double mid = (start + sweep / 2) * Math.PI / 180;
                float lx = r.X + r.Width / 2 + (float)Math.Cos(mid) * r.Width * .32f, ly = r.Y + r.Height / 2 + (float)Math.Sin(mid) * r.Height * .32f;
                string txt = $"{p.Value / total * 100:0}%";
                var sz = TextRenderer.MeasureText(txt, Theme.Bold);
                TextRenderer.DrawText(g, txt, Theme.Bold, new Point((int)(lx - sz.Width / 2), (int)(ly - sz.Height / 2)), Color.FromArgb(30, 30, 30));
            }
            start += sweep;
        }
        // legend
        int lx0 = (int)pr.Right + Theme.S(24), ly0 = area.Y + Math.Max(0, (area.Height - Points.Count * Theme.S(22)) / 2);
        for (int i = 0; i < Points.Count; i++)
        {
            int y = ly0 + i * Theme.S(22);
            if (y > area.Bottom - Theme.S(16)) { TextRenderer.DrawText(g, $"… {Points.Count - i} more", Theme.Small, new Point(lx0, y), Theme.TextDim); break; }
            using (var b = new SolidBrush(Points[i].Color)) g.FillRectangle(b, lx0, y + Theme.S(3), Theme.S(14), Theme.S(14));
            var font = i == _hover ? Theme.Bold : Theme.Normal;
            TextRenderer.DrawText(g, $"{Points[i].Label}  -  {Fmt.Human(Points[i].Value)}", font, new Rectangle(lx0 + Theme.S(20), y, area.Right - lx0 - Theme.S(20), Theme.S(20)), Color.Black, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = -1;
        for (int i = _hit.Count - 1; i >= 0; i--) if (_hit[i].path.IsVisible(e.Location)) { h = _hit[i].idx; break; }
        if (h != _hover)
        {
            _hover = h; Invalidate();
            if (h >= 0)
            {
                var p = Points[h]; double total = Points.Sum(x => x.Value);
                _tip.SetToolTip(this, $"{p.Label}\n{Fmt.Time(p.Value, true, false)}  ({Fmt.Human(p.Value)})\n{p.Value / total * 100:0.0}% of total" + (p.Count > 0 ? $"\n{p.Count} session(s)" : ""));
            }
            else _tip.SetToolTip(this, null);
        }
    }
    protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); }

    public Bitmap Snapshot()
    {
        var b = new Bitmap(Width, Height);
        DrawToBitmap(b, new Rectangle(0, 0, Width, Height));
        return b;
    }
}

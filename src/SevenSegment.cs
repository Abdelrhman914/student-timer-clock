using System.Drawing.Drawing2D;

namespace StudentTimerClock;

/// <summary>Draws text like a digital (LCD / seven-segment) display.</summary>
public static class SevenSegment
{
    static readonly Dictionary<char, string> Map = new()
    {
        ['0'] = "abcdef", ['1'] = "bc", ['2'] = "abged", ['3'] = "abgcd", ['4'] = "fgbc",
        ['5'] = "afgcd", ['6'] = "afgedc", ['7'] = "abc", ['8'] = "abcdefg", ['9'] = "abcdfg",
        ['-'] = "g", ['+'] = "g", [' '] = "",
    };

    const float DigitW = 0.56f, ColonW = 0.24f, DotW = 0.2f, Gap = 0.09f;

    public static float Width(string s, float h)
    {
        float w = 0;
        foreach (char ch in s) w += (ch == ':' ? ColonW : ch == '.' ? DotW : DigitW) * h + Gap * h;
        return Math.Max(0, w - Gap * h);
    }

    /// <summary>Draws text into bounds (fitted, centred). smallTail: characters after this index drawn at 60% height (e.g. hundredths).</summary>
    public static void Draw(Graphics g, string text, RectangleF bounds, Color on, Color? off = null, int smallTail = -1, StringAlignment align = StringAlignment.Center)
    {
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        string main = smallTail >= 0 && smallTail < text.Length ? text[..smallTail] : text;
        string tail = smallTail >= 0 && smallTail < text.Length ? text[smallTail..] : "";
        const float tailScale = 0.62f;

        float h = bounds.Height * 0.86f;
        float total = Width(main, h) + (tail.Length > 0 ? Gap * h + Width(tail, h * tailScale) : 0);
        if (total > bounds.Width * 0.97f) { h *= bounds.Width * 0.97f / total; total = Width(main, h) + (tail.Length > 0 ? Gap * h + Width(tail, h * tailScale) : 0); }

        float x = align == StringAlignment.Near ? bounds.X : align == StringAlignment.Far ? bounds.Right - total : bounds.X + (bounds.Width - total) / 2;
        float y = bounds.Y + (bounds.Height - h) / 2;
        x = DrawRun(g, main, x, y, h, on, off);
        if (tail.Length > 0) DrawRun(g, tail, x + Gap * h, y + h * (1 - tailScale), h * tailScale, on, off);
        g.SmoothingMode = old;
    }

    public static float DrawAt(Graphics g, string s, float x, float y, float h, Color on, Color? off) { var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias; var r = DrawRun(g, s, x, y, h, on, off); g.SmoothingMode = old; return r; }

    static float DrawRun(Graphics g, string s, float x, float y, float h, Color on, Color? off)
    {
        using var bOn = new SolidBrush(on);
        using var bOff = off.HasValue ? new SolidBrush(off.Value) : null;
        foreach (char ch in s)
        {
            if (ch == ':')
            {
                float d = h * 0.12f, cx = x + ColonW * h / 2 - d / 2;
                g.FillEllipse(bOn, cx, y + h * 0.28f - d / 2, d, d);
                g.FillEllipse(bOn, cx, y + h * 0.72f - d / 2, d, d);
                x += ColonW * h + Gap * h; continue;
            }
            if (ch == '.')
            {
                float d = h * 0.12f;
                g.FillEllipse(bOn, x + DotW * h / 2 - d / 2, y + h - d, d, d);
                x += DotW * h + Gap * h; continue;
            }
            string segs = Map.TryGetValue(ch, out var m) ? m : "";
            DrawDigit(g, segs, x, y, DigitW * h, h, bOn, bOff, ch == '+');
            x += DigitW * h + Gap * h;
        }
        return x - Gap * h;
    }

    static void DrawDigit(Graphics g, string on, float x, float y, float w, float h, Brush bOn, Brush bOff, bool plus)
    {
        float t = h * 0.105f;        // segment thickness
        float sk = h * 0.06f;        // italic skew
        float g2 = t * 0.12f;        // gap between segments
        float mid = y + h / 2;
        PointF S(float px, float py) => new(px + sk * (1 - (py - y) / h), py);

        PointF[] Horiz(float yy, float x0, float x1) => new[]
        { S(x0 + g2, yy), S(x0 + t / 2 + g2, yy - t / 2), S(x1 - t / 2 - g2, yy - t / 2), S(x1 - g2, yy), S(x1 - t / 2 - g2, yy + t / 2), S(x0 + t / 2 + g2, yy + t / 2) };
        PointF[] Vert(float xx, float y0, float y1) => new[]
        { S(xx, y0 + g2), S(xx + t / 2, y0 + t / 2 + g2), S(xx + t / 2, y1 - t / 2 - g2), S(xx, y1 - g2), S(xx - t / 2, y1 - t / 2 - g2), S(xx - t / 2, y0 + t / 2 + g2) };

        float l = x + t / 2, r = x + w - t / 2, top = y + t / 2, bot = y + h - t / 2;
        var segs = new Dictionary<char, PointF[]>
        {
            ['a'] = Horiz(top, l, r), ['d'] = Horiz(bot, l, r), ['g'] = Horiz(mid, l, r),
            ['f'] = Vert(l, top, mid), ['b'] = Vert(r, top, mid), ['e'] = Vert(l, mid, bot), ['c'] = Vert(r, mid, bot),
        };
        foreach (var kv in segs)
        {
            bool lit = on.Contains(kv.Key);
            if (lit) g.FillPolygon(bOn, kv.Value);
            else if (bOff != null) g.FillPolygon(bOff, kv.Value);
        }
        if (plus) g.FillPolygon(bOn, Vert(x + w / 2, mid - h * 0.22f, mid + h * 0.22f));
    }
}

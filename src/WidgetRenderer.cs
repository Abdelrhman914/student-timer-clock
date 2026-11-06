using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace StudentTimerClock;

/// <summary>
/// Draws a timer / stopwatch widget in any style. Used by the floating desktop widgets,
/// the style editor preview and the multi-timer board, so they always look the same.
/// </summary>
public static class WidgetRenderer
{
    public record Btn(string Id, Rectangle R, string Icon, string Tip);

    public static Size DefaultSize(string layout) => layout switch
    {
        "Dial" => new Size(Theme.S(260), Theme.S(330)),
        "Compact" => new Size(Theme.S(260), Theme.S(44)),
        _ => new Size(Theme.S(330), Theme.S(160)),
    };
    public static Size MinSize(string layout) => layout switch
    {
        "Dial" => new Size(Theme.S(130), Theme.S(150)),
        "Compact" => new Size(Theme.S(110), Theme.S(24)),
        _ => new Size(Theme.S(140), Theme.S(56)),
    };

    public static WidgetStyle Effective(TimeItem it) => it.Style ?? AppState.Settings.DefaultWidgetStyle;

    /// <summary>Style used for tiles on the multi-timer board (item colour + dark digits, like a classroom display).</summary>
    public static WidgetStyle BoardStyle(TimeItem it)
    {
        var st = it.Style?.Clone() ?? new WidgetStyle();
        if (it.Style == null) { st.ApplyPreset("Item colour"); st.ForeColor = Color.FromArgb(30, 30, 30).ToArgb(); }
        st.Layout = "Digital";
        st.ShowTitle = true; st.ShowUnitLabels = true; st.ShowDate = false; st.ShowEndTime = true;
        st.ShowButtons = "Hover"; st.RoundCorners = false;
        return st;
    }

    // ------------------------------------------------------------------ time parts
    public class TimeParts { public bool Neg; public long D, H, M, S, Cs; public bool ShowDays, ShowSec, ShowCs; }

    public static TimeParts Parts(TimeItem it, WidgetStyle st, DateTime now)
    {
        double v; bool countdown = false, neg = false;
        if (it is TimerItem t)
        {
            if (t.Finished)
            {
                v = AppState.Settings.TimerOvertime && t.FinishedAtUtc.HasValue ? (now - t.FinishedAtUtc.Value).TotalSeconds : 0;
                neg = v >= 1;
            }
            else { v = Math.Max(0, t.Remaining(now)); countdown = true; }
        }
        else v = it.Elapsed(now);

        bool showCs = it is StopwatchItem && st.ShowHundredths && st.ShowSeconds;
        long unit = st.ShowSeconds ? 1 : 60;
        long totalCs;
        if (showCs) totalCs = (long)Math.Floor(v * 100 + 1e-4);
        else
        {
            double q = v / unit;
            long u = countdown ? (long)Math.Ceiling(q - 1e-6) : (long)Math.Floor(q + 1e-6);
            totalCs = u * unit * 100;
        }
        var p = new TimeParts { Neg = neg, ShowSec = st.ShowSeconds, ShowCs = showCs };
        p.Cs = totalCs % 100;
        long secs = totalCs / 100;
        p.S = secs % 60; p.M = secs / 60 % 60;
        long hours = secs / 3600;
        double reference = it is TimerItem tt ? Math.Max(tt.TotalSeconds, v) : v;
        p.ShowDays = st.ShowDays == "Always" || (st.ShowDays == "Auto" && (hours >= 24 || reference >= 86400));
        if (p.ShowDays) { p.D = hours / 24; p.H = hours % 24; } else p.H = hours;
        return p;
    }

    record Tok(string Text, bool Digits, string Label, bool Tail);

    static List<Tok> Tokens(TimeParts p)
    {
        var l = new List<Tok>();
        string sign = p.Neg ? "-" : "";
        if (p.ShowDays) { l.Add(new(sign + p.D.ToString("00"), true, "DAY", false)); l.Add(new(" ", false, null, false)); sign = ""; }
        l.Add(new(sign + p.H.ToString("00"), true, "HOUR", false));
        l.Add(new(":", false, null, false));
        l.Add(new(p.M.ToString("00"), true, "MIN", false));
        if (p.ShowSec) { l.Add(new(":", false, null, false)); l.Add(new(p.S.ToString("00"), true, "SEC", false)); }
        if (p.ShowCs) l.Add(new("." + p.Cs.ToString("00"), true, null, true));
        return l;
    }

    public static string TimeText(TimeItem it, WidgetStyle st, DateTime now) => string.Concat(Tokens(Parts(it, st, now)).Select(t => t.Text));

    // ------------------------------------------------------------------ fonts
    class FontMetrics { public FontFamily Fam; public FontStyle Style; public float DigitTop, DigitH; public readonly Dictionary<string, float> W = new(); }
    static readonly Dictionary<string, FontMetrics> _fm = new();
    static readonly Dictionary<string, Image> _images = new();
    static readonly Bitmap _measureBmp = new(1, 1);

    static FontFamily Family(string name)
    {
        foreach (var n in new[] { string.IsNullOrWhiteSpace(name) ? "Segoe UI" : name, "Segoe UI", "Tahoma", "Arial", "DejaVu Sans" })
        {
            try { return new FontFamily(n); } catch { }
        }
        try { return FontFamily.GenericSansSerif; } catch { }
        return FontFamily.Families[0];
    }

    static FontMetrics Metrics(string family, FontStyle style)
    {
        string key = family + "|" + (int)style;
        if (_fm.TryGetValue(key, out var m)) return m;
        var fam = Family(family);
        if (!fam.IsStyleAvailable(style)) style = fam.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular : FontStyle.Bold;
        m = new FontMetrics { Fam = fam, Style = style };
        using (var path = new GraphicsPath())
        {
            path.AddString("0123456789", fam, (int)style, 100, PointF.Empty, StringFormat.GenericTypographic);
            var b = path.GetBounds();
            m.DigitTop = b.Height > 1 ? b.Top : 20; m.DigitH = b.Height > 1 ? b.Height : 70;
        }
        _fm[key] = m;
        return m;
    }

    static float FontWidth(FontMetrics m, string text)
    {
        if (m.W.TryGetValue(text, out var w)) return w;
        using var g = Graphics.FromImage(_measureBmp);
        using var f = new Font(m.Fam, 100, m.Style, GraphicsUnit.Pixel);
        using var sf = (StringFormat)StringFormat.GenericTypographic.Clone();
        sf.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
        w = g.MeasureString(text, f, PointF.Empty, sf).Width;
        m.W[text] = w;
        return w;
    }

    public static Font MakeFont(string family, float px, bool bold, bool italic)
    {
        var st = (bold ? FontStyle.Bold : 0) | (italic ? FontStyle.Italic : 0);
        var fam = Family(family);
        if (!fam.IsStyleAvailable(st)) st = fam.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular : FontStyle.Bold;
        return new Font(fam, Math.Max(4, px), st, GraphicsUnit.Pixel);
    }

    static Image GetImage(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (_images.TryGetValue(path, out var img)) return img;
        try { using var tmp = Image.FromFile(path); img = new Bitmap(tmp); } catch { img = null; }
        _images[path] = img;
        return img;
    }
    public static void ForgetImage(string path) { if (path != null && _images.Remove(path, out var i)) i?.Dispose(); }

    static string Zero(string s) => new(s.Select(c => char.IsDigit(c) ? '0' : c).ToArray());

    // ------------------------------------------------------------------ time drawing (LCD or any font)
    static void DrawTime(Graphics g, RectangleF area, List<Tok> toks, WidgetStyle st, Color fore, Color label, bool labels, Color? ghost)
    {
        if (area.Width < 4 || area.Height < 4) return;
        bool lcd = string.IsNullOrEmpty(st.TimeFont);
        var fstyle = (st.TimeBold ? FontStyle.Bold : 0) | (st.TimeItalic ? FontStyle.Italic : 0);
        FontMetrics fm = lcd ? null : Metrics(st.TimeFont, fstyle);
        const float gapW = 0.38f, tailScale = 0.55f, sepPad = 0.05f, spacing = 0.09f;

        float UnitW(Tok t)
        {
            if (t.Text == " ") return gapW;
            string z = Zero(t.Text);
            float w = lcd ? SevenSegment.Width(z, 1) : FontWidth(fm, z) / fm.DigitH;
            if (!t.Digits) w += sepPad * 2;
            return t.Tail ? w * tailScale : w;
        }

        bool anyLabel = labels && toks.Any(t => t.Label != null);
        float labelFrac = anyLabel ? 0.26f : 0f, labelGap = anyLabel ? 0.07f : 0f;
        float total = toks.Sum(UnitW) + spacing * (toks.Count - 1);
        float h = Math.Min(area.Height / (1 + labelFrac + labelGap), area.Width * 0.98f / total);
        float labelH = h * labelFrac;
        float blockH = h * (1 + labelFrac + labelGap);
        float x = area.X + (area.Width - total * h) / 2;
        float yDigits = area.Y + (area.Height - blockH) / 2 + h * (labelFrac + labelGap);

        using var fill = new SolidBrush(fore);
        using var labelBrush = new SolidBrush(label);
        using var labelFont = anyLabel ? MakeFont("Segoe UI", labelH * 0.85f, false, false) : null;
        using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Far, Trimming = StringTrimming.None, FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip };
        var oldSm = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;

        foreach (var t in toks)
        {
            float w = UnitW(t) * h;
            if (t.Text != " ")
            {
                float th = t.Tail ? h * tailScale : h;
                float ty = yDigits + (h - th);
                if (lcd)
                {
                    float aw = SevenSegment.Width(t.Text, th);
                    SevenSegment.DrawAt(g, t.Text, x + (w - aw) / 2, ty, th, fore, ghost);
                }
                else
                {
                    float k = th / fm.DigitH;
                    float tw = FontWidth(fm, t.Text) * k;
                    using var path = new GraphicsPath();
                    path.AddString(t.Text, fm.Fam, (int)fm.Style, 100, PointF.Empty, StringFormat.GenericTypographic);
                    using var mtx = new Matrix(k, 0, 0, k, x + (w - tw) / 2, ty - fm.DigitTop * k);
                    path.Transform(mtx);
                    g.FillPath(fill, path);
                }
                if (anyLabel && t.Label != null)
                    g.DrawString(t.Label, labelFont, labelBrush, new RectangleF(x - w, yDigits - h * labelGap - labelH, w * 3, labelH), center);
            }
            x += w + spacing * h;
        }
        g.SmoothingMode = oldSm;
    }

    // ------------------------------------------------------------------ info lines
    public static string When(DateTime local, bool grid = false)
    {
        string clock = Fmt.Clock(local, grid);
        if (local.Date == DateTime.Today) return grid ? "Today " + clock : clock;
        if (local.Date == DateTime.Today.AddDays(1)) return (grid ? "Tomorrow " : "tomorrow ") + clock;
        if (local.Date == DateTime.Today.AddDays(-1)) return (grid ? "Yesterday " : "yesterday ") + clock;
        return local.ToString(grid ? "ddd d MMM" : "ddd d MMM yyyy") + ", " + Fmt.Clock(local, false);
    }

    public static string EndText(TimeItem it, DateTime now)
    {
        if (it is TimerItem t)
        {
            if (t.IsTarget) return (t.Finished ? "Reached " : "Until ") + When(t.CountdownTo.Value);
            if (t.Finished) return "Time is up!  (" + Fmt.Clock(t.FinishedAtUtc?.ToLocalTime() ?? DateTime.Now, false) + ")";
            if (t.IsRunning) return "Ends at " + When(t.EndsAtLocal(now).Value);
            if (t.ElapsedBefore > 0) return "Paused - " + Fmt.Human(t.Remaining(now)) + " left";
            return "Ready - " + Fmt.Human(t.DurationSeconds);
        }
        var s = (StopwatchItem)it;
        string lap = !string.IsNullOrWhiteSpace(s.CurrentLapName) || s.Laps.Count > 0
            ? $"{(string.IsNullOrWhiteSpace(s.CurrentLapName) ? "Lap " + (s.Laps.Count + 1) : s.CurrentLapName)} {Fmt.Time(s.CurrentLap(now), false, false)}" : null;
        if (s.IsRunning) return lap ?? ("Running since " + Fmt.Clock(s.StartedAtUtc?.ToLocalTime() ?? DateTime.Now, false));
        return s.ElapsedBefore > 0 ? "Paused" + (lap != null ? "  •  " + lap : "") : "Ready";
    }

    public static string DateText() => DateTime.Now.ToString("dddd, d MMMM yyyy");

    // ------------------------------------------------------------------ geometry
    class Geo { public float Pad, Bs, ProgH, TitleH, LineH; public bool BtnVisible, BtnReserve; }

    static Geo Geometry(Rectangle rc, WidgetStyle st, TimeItem it, bool hover)
    {
        float H = rc.Height;
        var g = new Geo();
        bool compact = st.Layout == "Compact";
        g.Pad = compact ? Math.Max(3, H * 0.1f) : Math.Clamp(H * 0.05f, 4, 16);
        g.Bs = compact ? Math.Clamp(H * 0.55f, 14, 26) : Math.Clamp(Math.Min(H, rc.Width) * 0.13f, 16, 30);
        g.ProgH = it is TimerItem && st.ShowProgress && st.Layout == "Digital" ? Math.Clamp(H * 0.03f, 3, 6) : 0;
        g.TitleH = st.ShowTitle && !compact ? Math.Clamp(H * (st.Layout == "Dial" ? 0.09f : 0.15f), 11, 44) : 0;
        g.LineH = Math.Clamp(H * (st.Layout == "Dial" ? 0.065f : 0.105f), 10, 28);
        g.BtnVisible = st.ShowButtons == "Always" || (st.ShowButtons == "Hover" && hover);
        g.BtnReserve = st.ShowButtons == "Always";
        return g;
    }

    public static List<Btn> Buttons(TimeItem it, WidgetStyle st, Rectangle rc, bool hover, bool chrome)
    {
        var list = new List<Btn>();
        var geo = Geometry(rc, st, it, hover);
        if (geo.BtnVisible)
        {
            var ids = new List<(string, string, string)> { ("toggle", it.IsRunning ? "pause" : "start", it.IsRunning ? "Pause" : "Start") };
            if (it is StopwatchItem) ids.Add(("lap", "lap", "Lap"));
            ids.Add(("reset", "reset", "Reset"));
            int bs = (int)geo.Bs, gap = Math.Max(2, bs / 6);
            int x, y;
            if (st.Layout == "Compact")
            {
                x = rc.Right - (int)geo.Pad - ids.Count * bs - (ids.Count - 1) * gap - (chrome && hover ? Theme.S(18) : 0);
                y = rc.Y + (rc.Height - bs) / 2;
            }
            else if (st.Layout == "Dial")
            {
                x = rc.X + (rc.Width - (ids.Count * bs + (ids.Count - 1) * gap)) / 2;
                y = rc.Bottom - (int)(geo.Pad * 0.6f) - bs;
            }
            else
            {
                x = rc.X + (int)geo.Pad;
                y = rc.Bottom - (int)geo.ProgH - (int)(geo.Pad * 0.6f) - bs;
            }
            foreach (var (id, icon, tip) in ids) { list.Add(new Btn(id, new Rectangle(x, y, bs, bs), icon, tip)); x += bs + gap; }
        }
        if (chrome && hover)
        {
            int c = Theme.S(16), m = Theme.S(4);
            list.Add(new Btn("close", new Rectangle(rc.Right - c - m, rc.Y + m, c, c), "x", "Close widget"));
            if (st.Layout != "Compact" || rc.Height > Theme.S(40))
                list.Add(new Btn("stylebtn", new Rectangle(rc.Right - 2 * c - 2 * m, rc.Y + m, c, c), "style", "Change font, colours and background…"));
        }
        return list;
    }

    // ------------------------------------------------------------------ main draw
    public static void Draw(Graphics g, Rectangle rc, TimeItem it, WidgetStyle st, bool hover, string hoverBtn, DateTime now, bool chrome = true)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var fore = Color.FromArgb(st.ForeColor);
        var lab = Color.FromArgb(st.LabelColor);
        bool finished = it is TimerItem tf && tf.Finished;
        bool flash = finished && DateTime.Now.Millisecond < 500;

        // ---- background
        var img = GetImage(st.BackImage);
        if (img != null)
        {
            var old = g.InterpolationMode; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            if (st.BackImageStretch) g.DrawImage(img, rc);
            else
            {
                float sc = Math.Max(rc.Width / (float)img.Width, rc.Height / (float)img.Height);
                float w = img.Width * sc, h = img.Height * sc;
                g.DrawImage(img, rc.X + (rc.Width - w) / 2, rc.Y + (rc.Height - h) / 2, w, h);
            }
            g.InterpolationMode = old;
            if (st.BackImageDim > 0) using (var br = new SolidBrush(Color.FromArgb(Math.Clamp(st.BackImageDim, 0, 90) * 255 / 100, 0, 0, 0))) g.FillRectangle(br, rc);
        }
        else
        {
            Color b1, b2;
            if (st.UseItemColor) { b1 = Theme.Lighten(it.Color, .3f); b2 = it.Color; }
            else { b1 = Color.FromArgb(st.BackColor); b2 = st.Gradient ? Color.FromArgb(st.BackColor2) : b1; }
            using var br = new LinearGradientBrush(new Rectangle(rc.X, rc.Y - 1, rc.Width, rc.Height + 2), b1, b2, 90f);
            g.FillRectangle(br, rc);
        }
        if (finished) using (var br = new SolidBrush(Color.FromArgb(flash ? 150 : 50, 220, 40, 30))) g.FillRectangle(br, rc);
        if (flash) { fore = Color.White; lab = Color.FromArgb(255, 230, 230); }

        var geo = Geometry(rc, st, it, hover);
        var ghost = Color.FromArgb(Math.Min(40, fore.A / 7), fore);
        switch (st.Layout)
        {
            case "Dial": DrawDial(g, rc, it, st, geo, now, fore, lab, ghost); break;
            case "Compact": DrawCompact(g, rc, it, st, geo, now, fore, lab, ghost, chrome && hover); break;
            default: DrawDigital(g, rc, it, st, geo, now, fore, lab, ghost); break;
        }

        // ---- buttons
        foreach (var b in Buttons(it, st, rc, hover, chrome))
        {
            if (b.Id == "close")
            {
                using var p = new Pen(hoverBtn == "close" ? Theme.Red : lab, Math.Max(1.5f, Theme.S(2f)));
                var r = Rectangle.Inflate(b.R, -Theme.S(4), -Theme.S(4));
                g.DrawLine(p, r.Left, r.Top, r.Right, r.Bottom); g.DrawLine(p, r.Right, r.Top, r.Left, r.Bottom);
                continue;
            }
            if (hoverBtn == b.Id) using (var br = new SolidBrush(Color.FromArgb(70, 255, 255, 255))) g.FillEllipse(br, Rectangle.Inflate(b.R, 2, 2));
            bool en = b.Id != "lap" || it.IsRunning;
            g.DrawImage(Icons.Get(b.Icon, b.R.Width, !en), b.R);
        }

        // ---- resize grip
        if (chrome && hover && !st.Locked)
        {
            using var br = new SolidBrush(Color.FromArgb(150, lab));
            int d = Math.Max(2, Theme.S(2)), sp = Theme.S(4);
            for (int i = 0; i < 3; i++) for (int j = 0; j <= i; j++)
                g.FillRectangle(br, rc.Right - sp * (j + 1) - 2, rc.Bottom - sp * (i - j + 1) - 2, d, d);
        }
        if (st.Border) using (var p = new Pen(Color.FromArgb(80, 0, 0, 0))) g.DrawRectangle(p, rc.X, rc.Y, rc.Width - 1, rc.Height - 1);
    }

    static void DrawTitle(Graphics g, RectangleF r, TimeItem it, WidgetStyle st, Color c, StringAlignment align = StringAlignment.Center)
    {
        if (r.Height < 4 || r.Width < 10) return;
        string text = string.IsNullOrWhiteSpace(st.TitleText) ? it.Name : st.TitleText;
        using var f = MakeFont(st.TitleFont, r.Height * 0.78f, st.TitleBold, st.TitleItalic);
        using var br = new SolidBrush(c);
        using var sf = new StringFormat { Alignment = align, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        g.DrawString(text, f, br, r, sf);
    }

    static void DrawLines(Graphics g, RectangleF r, List<string> lines, float lineH, Color c)
    {
        if (lines.Count == 0) return;
        using var f = MakeFont("Segoe UI", lineH * 0.74f, false, false);
        using var br = new SolidBrush(c);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        for (int i = 0; i < lines.Count; i++) g.DrawString(lines[i], f, br, new RectangleF(r.X, r.Y + i * lineH, r.Width, lineH), sf);
    }

    static List<string> InfoLines(TimeItem it, WidgetStyle st, DateTime now, float availH, float lineH)
    {
        var lines = new List<string>();
        if (st.ShowDate) lines.Add(DateText());
        if (st.ShowEndTime) lines.Add(EndText(it, now));
        if (lines.Count == 2 && availH < lineH * 6.5f) lines = new List<string> { lines[1] + "   •   " + DateTime.Now.ToString("ddd d MMM yyyy") };
        return lines;
    }

    static void DrawDigital(Graphics g, Rectangle rc, TimeItem it, WidgetStyle st, Geo geo, DateTime now, Color fore, Color lab, Color ghost)
    {
        float pad = geo.Pad, H = rc.Height;
        float y = rc.Y + pad * 0.7f;
        if (geo.TitleH > 0)
        {
            DrawTitle(g, new RectangleF(rc.X + pad + Theme.S(20), y, rc.Width - 2 * pad - Theme.S(40), geo.TitleH), it, st, lab);
            y += geo.TitleH;
        }
        var lines = InfoLines(it, st, now, H, geo.LineH);
        float linesH = lines.Count * geo.LineH;
        float bottom = Math.Max(linesH, geo.BtnReserve ? geo.Bs + pad * 0.3f : 0) + geo.ProgH + pad * 0.6f;
        var timeArea = new RectangleF(rc.X + pad, y + pad * 0.2f, rc.Width - 2 * pad, rc.Bottom - bottom - y - pad * 0.3f);
        DrawTime(g, timeArea, Tokens(Parts(it, st, now)), st, fore, lab, st.ShowUnitLabels, ghost);

        float side = geo.BtnVisible ? (it is StopwatchItem ? 3 : 2) * (geo.Bs * 1.17f) + pad : pad;
        float ly = rc.Bottom - geo.ProgH - pad * 0.6f - linesH;
        if (geo.BtnReserve && linesH < geo.Bs) ly -= (geo.Bs - linesH) / 2;
        DrawLines(g, new RectangleF(rc.X + side, ly, rc.Width - 2 * side, linesH), lines, geo.LineH, lab);

        if (geo.ProgH > 0 && it is TimerItem t)
        {
            var pr = new RectangleF(rc.X + pad, rc.Bottom - geo.ProgH - pad * 0.35f, rc.Width - 2 * pad, geo.ProgH);
            using (var br = new SolidBrush(Color.FromArgb(45, lab))) g.FillRectangle(br, pr);
            using (var br = new SolidBrush(t.Finished ? Theme.Red : Color.FromArgb(st.AccentColor)))
                g.FillRectangle(br, pr.X, pr.Y, pr.Width * (float)(1 - t.Progress(now)), pr.Height);
        }
    }

    static void DrawCompact(Graphics g, Rectangle rc, TimeItem it, WidgetStyle st, Geo geo, DateTime now, Color fore, Color lab, Color ghost, bool chromeHover)
    {
        float pad = geo.Pad;
        float right = rc.Right - pad;
        if (geo.BtnVisible) right -= (it is StopwatchItem ? 3 : 2) * (geo.Bs * 1.17f);
        if (chromeHover) right -= Theme.S(18);
        float left = rc.X + pad + Theme.S(3);
        if (st.ShowTitle)
        {
            float tw = (right - left) * 0.42f;
            DrawTitle(g, new RectangleF(left, rc.Y, tw, rc.Height), it, st, lab, StringAlignment.Near);
            left += tw + pad;
        }
        var lines = rc.Height > Theme.S(70) ? InfoLines(it, st, now, 0, geo.LineH * 0.8f) : new List<string>();
        float lh = lines.Count * geo.LineH * 0.8f;
        DrawTime(g, new RectangleF(left, rc.Y + pad, right - left, rc.Height - 2 * pad - lh), Tokens(Parts(it, st, now)), st, fore, lab, false, ghost);
        if (lines.Count > 0) DrawLines(g, new RectangleF(left, rc.Bottom - pad - lh, right - left, lh), lines, geo.LineH * 0.8f, lab);
        using (var br = new SolidBrush(Color.FromArgb(st.UseItemColor ? Theme.Saturate(it.Color).ToArgb() : st.AccentColor))) g.FillRectangle(br, rc.X, rc.Y, Math.Max(3, Theme.S(4)), rc.Height);
    }

    static void DrawDial(Graphics g, Rectangle rc, TimeItem it, WidgetStyle st, Geo geo, DateTime now, Color fore, Color lab, Color ghost)
    {
        float pad = geo.Pad;
        float y = rc.Y + pad * 0.7f;
        if (geo.TitleH > 0) { DrawTitle(g, new RectangleF(rc.X + pad + Theme.S(20), y, rc.Width - 2 * pad - Theme.S(40), geo.TitleH), it, st, lab); y += geo.TitleH; }
        var lines = InfoLines(it, st, now, rc.Height, geo.LineH);
        float linesH = lines.Count * geo.LineH;
        float btnRow = geo.BtnReserve ? geo.Bs + pad * 0.4f : 0;
        float bottom = linesH + btnRow + pad * 0.5f;
        var area = new RectangleF(rc.X + pad, y + pad * 0.3f, rc.Width - 2 * pad, rc.Bottom - bottom - y - pad * 0.3f);
        float D = Math.Min(area.Width, area.Height);
        if (D < 30) return;
        float cx = area.X + area.Width / 2, cy = area.Y + area.Height / 2;
        float R = D / 2 * 0.80f;

        // scale of the dial
        double frac, scaleMin;
        if (it is TimerItem t)
        {
            double total = t.TotalSeconds;
            scaleMin = total <= 60 ? 1 : total <= 720 ? 12 : total <= 3600 ? 60 : Math.Ceiling(total / 3600) * 60;
            frac = t.Finished ? 0 : Math.Clamp(Math.Max(0, t.Remaining(now)) / (scaleMin * 60), 0, 1);
        }
        else { scaleMin = 60; frac = it.Elapsed(now) % 3600 / 3600.0; }

        var face = new RectangleF(cx - R, cy - R, 2 * R, 2 * R);
        using (var br = new SolidBrush(Color.FromArgb(235, 248, 249, 251))) g.FillEllipse(br, face);
        var ink = Color.FromArgb(55, 55, 60);
        // pie (remaining time) in accent colour
        if (frac > 0.0005)
        {
            var pr = RectangleF.Inflate(face, -R * 0.04f, -R * 0.04f);
            using var br = new SolidBrush(Color.FromArgb(st.AccentColor));
            if (frac >= 0.9999) g.FillEllipse(br, pr); else g.FillPie(br, pr.X, pr.Y, pr.Width, pr.Height, -90f, (float)(frac * 360));
        }
        using (var p = new Pen(ink, Math.Max(1, R / 60)))
            for (int i = 0; i < 60; i++)
            {
                double a = i * Math.PI / 30 - Math.PI / 2;
                float l1 = i % 5 == 0 ? 0.86f : 0.92f;
                p.Width = i % 5 == 0 ? Math.Max(1.4f, R / 34) : Math.Max(1f, R / 70);
                g.DrawLine(p, cx + (float)Math.Cos(a) * R * l1, cy + (float)Math.Sin(a) * R * l1, cx + (float)Math.Cos(a) * R * 0.99f, cy + (float)Math.Sin(a) * R * 0.99f);
            }
        using (var p = new Pen(Color.FromArgb(120, ink), Math.Max(1, R / 50))) g.DrawEllipse(p, face);
        // numbers around the dial
        using (var f = MakeFont("Segoe UI", R * 0.15f, false, false))
        using (var br = new SolidBrush(lab))
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            for (int k = 0; k < 12; k++)
            {
                double a = k * Math.PI / 6 - Math.PI / 2;
                double v = k * scaleMin / 12;
                string txt = scaleMin == 1 ? (v * 60).ToString("0") : scaleMin <= 180 ? v.ToString("0") : (v / 60).ToString("0.#") + "h";
                float nx = cx + (float)Math.Cos(a) * R * 1.14f, ny = cy + (float)Math.Sin(a) * R * 1.14f;
                g.DrawString(txt, f, br, new RectangleF(nx - R * 0.3f, ny - R * 0.12f, R * 0.6f, R * 0.24f), sf);
            }
        // hand
        double ha = frac * Math.PI * 2 - Math.PI / 2;
        using (var p = new Pen(Color.FromArgb(25, 25, 25), Math.Max(2, R / 22)) { StartCap = LineCap.Round, EndCap = LineCap.Triangle })
            g.DrawLine(p, cx, cy, cx + (float)Math.Cos(ha) * R * 0.72f, cy + (float)Math.Sin(ha) * R * 0.72f);
        if (it is StopwatchItem)
        {
            double sa = it.Elapsed(now) % 60 / 60 * Math.PI * 2 - Math.PI / 2;
            using var p = new Pen(Theme.Red, Math.Max(1, R / 50));
            g.DrawLine(p, cx, cy, cx + (float)Math.Cos(sa) * R * 0.85f, cy + (float)Math.Sin(sa) * R * 0.85f);
        }
        using (var br = new SolidBrush(Color.FromArgb(25, 25, 25))) g.FillEllipse(br, cx - R * 0.07f, cy - R * 0.07f, R * 0.14f, R * 0.14f);

        // digital readout inside the dial
        var box = new RectangleF(cx - R * 0.68f, cy + R * 0.3f, R * 1.36f, R * 0.3f);
        using (var path = Theme.Round(box, box.Height * 0.25f))
        {
            using var br = new SolidBrush(Color.FromArgb(225, Theme.Darken(Color.FromArgb(st.AccentColor), 0.45f)));
            g.FillPath(br, path);
        }
        var mini = st.Clone(); mini.ShowUnitLabels = false;
        DrawTime(g, RectangleF.Inflate(box, -box.Height * 0.15f, -box.Height * 0.16f), Tokens(Parts(it, mini, now)), mini, Color.White, Color.White, false, null);

        DrawLines(g, new RectangleF(rc.X + pad, rc.Bottom - pad * 0.5f - btnRow - linesH, rc.Width - 2 * pad, linesH), lines, geo.LineH, lab);
    }
}

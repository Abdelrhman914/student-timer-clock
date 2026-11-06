using System.Drawing.Drawing2D;

namespace StudentTimerClock;

public static class Palette
{
    // Pastel row colours in the spirit of Hot Alarm Clock
    public static readonly Color[] Colors =
    {
        Color.FromArgb(255, 244, 204), // light yellow
        Color.FromArgb(226, 240, 217), // light green
        Color.FromArgb(218, 227, 243), // light blue
        Color.FromArgb(252, 228, 214), // peach
        Color.FromArgb(248, 203, 173), // orange
        Color.FromArgb(221, 235, 247), // pale sky
        Color.FromArgb(237, 237, 237), // grey
        Color.FromArgb(234, 222, 248), // lavender
        Color.FromArgb(255, 221, 230), // pink
        Color.FromArgb(208, 240, 236), // mint
        Color.White,
    };
}

public static class Theme
{
    public static readonly Color Accent = Color.FromArgb(0, 120, 212);
    public static readonly Color AccentLight = Color.FromArgb(204, 228, 247);
    public static readonly Color AccentHover = Color.FromArgb(229, 241, 251);
    public static readonly Color RibbonBack = Color.FromArgb(248, 249, 250);
    public static readonly Color Border = Color.FromArgb(213, 216, 220);
    public static readonly Color GridLine = Color.FromArgb(200, 205, 212);
    public static readonly Color HeaderBack = Color.FromArgb(242, 243, 245);
    public static readonly Color GroupBack = Color.FromArgb(246, 247, 248);
    public static readonly Color TextDim = Color.FromArgb(96, 100, 108);
    public static readonly Color Green = Color.FromArgb(76, 175, 120);
    public static readonly Color Red = Color.FromArgb(214, 60, 50);
    public static readonly Color Orange = Color.FromArgb(232, 93, 40);

    public static float Scale = 1f;
    public static int S(int v) => (int)Math.Round(v * Scale);
    public static float S(float v) => v * Scale;

    public static Font UI(float size, FontStyle st = FontStyle.Regular) => SafeFont("Segoe UI", size, st);

    /// <summary>Creates a font, falling back to common UI fonts if the requested family is missing (never throws).</summary>
    public static Font SafeFont(string family, float size, FontStyle st = FontStyle.Regular, GraphicsUnit unit = GraphicsUnit.Point)
    {
        foreach (var name in new[] { family, "Segoe UI", "Tahoma", "Arial", "Microsoft Sans Serif", "DejaVu Sans" })
        {
            try { return new Font(name, size, st, unit); } catch { }
        }
        try { return new Font(FontFamily.GenericSansSerif, size, st, unit); } catch { }
        try { return new Font(SystemFonts.DefaultFont.FontFamily, size, st, unit); } catch { }
        try { return new Font(FontFamily.Families[0], size, st, unit); } catch { }
        return (Font)SystemFonts.DefaultFont.Clone();
    }
    public static readonly Font Normal = UI(9f);
    public static readonly Font Bold = UI(9f, FontStyle.Bold);
    public static readonly Font Small = UI(8f);
    public static readonly Font Big = UI(11.5f, FontStyle.Bold);

    public static void Init()
    {
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        Scale = Math.Max(1f, g.DpiX / 96f);
    }

    public static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static Color Darken(Color c, float f) => Color.FromArgb(c.A, (int)(c.R * (1 - f)), (int)(c.G * (1 - f)), (int)(c.B * (1 - f)));
    public static Color Lighten(Color c, float f) => Color.FromArgb(c.A, c.R + (int)((255 - c.R) * f), c.G + (int)((255 - c.G) * f), c.B + (int)((255 - c.B) * f));
    public static Color Saturate(Color c)
    {
        // turn a pastel into a stronger chart colour
        float h = c.GetHue(), s = c.GetSaturation();
        if (s < 0.05f) return Color.FromArgb(140, 146, 156);
        return FromHsl(h, 0.65f, 0.55f);
    }
    public static Color FromHsl(float h, float s, float l)
    {
        float c = (1 - Math.Abs(2 * l - 1)) * s, x = c * (1 - Math.Abs(h / 60f % 2 - 1)), m = l - c / 2;
        float r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; } else if (h < 120) { r = x; g = c; } else if (h < 180) { g = c; b = x; }
        else if (h < 240) { g = x; b = c; } else if (h < 300) { r = x; b = c; } else { r = c; b = x; }
        return Color.FromArgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
    }
    public static readonly Color[] ChartColors =
    {
        Color.FromArgb(64, 196, 255), Color.FromArgb(124, 200, 60), Color.FromArgb(150, 150, 150), Color.FromArgb(248, 204, 132),
        Color.FromArgb(240, 110, 90), Color.FromArgb(160, 120, 220), Color.FromArgb(40, 170, 160), Color.FromArgb(250, 160, 200),
        Color.FromArgb(255, 200, 40), Color.FromArgb(90, 120, 200), Color.FromArgb(200, 140, 90), Color.FromArgb(120, 210, 170),
    };
}

public static class Fmt
{
    /// <summary>hh:mm:ss  (optionally with .cc hundredths)</summary>
    public static string Time(double seconds, bool hundredths = false, bool forceHours = true)
    {
        bool neg = seconds < 0;
        seconds = Math.Abs(seconds);
        if (!hundredths) seconds = Math.Floor(seconds + 0.0001);
        long totalCs = (long)Math.Floor(seconds * 100 + 0.0001);
        long cs = totalCs % 100, s = totalCs / 100 % 60, m = totalCs / 6000 % 60, h = totalCs / 360000;
        string r = h > 0 || forceHours ? $"{h:00}:{m:00}:{s:00}" : $"{m:00}:{s:00}";
        if (hundredths) r += $".{cs:00}";
        return (neg ? "-" : "") + r;
    }

    /// <summary>Like Time but with a day part for long spans: "45d 03:12:10".</summary>
    public static string Long(double seconds)
    {
        if (Math.Abs(seconds) < 86400) return Time(seconds);
        long d = (long)(Math.Abs(seconds) / 86400);
        return (seconds < 0 ? "-" : "") + d + "d " + Time(Math.Abs(seconds) - d * 86400);
    }

    /// <summary>Countdown display: rounds up so a timer shows 00:00:01 until it really hits zero.</summary>
    public static string Remaining(double seconds) => Time(Math.Ceiling(seconds - 0.0001));

    /// <summary>Human friendly: 2h 05m, 4m 10s, 12s</summary>
    public static string Human(double seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (ts.TotalDays >= 1) return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes:00}m";
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes:00}m";
        if (ts.TotalMinutes >= 1) return $"{ts.Minutes}m {ts.Seconds:00}s";
        return $"{ts.TotalSeconds:0.#}s";
    }

    public static string Clock(DateTime local, bool seconds = true) =>
        AppState.Settings.Use24Hour ? local.ToString(seconds ? "HH:mm:ss" : "HH:mm") : local.ToString(seconds ? "h:mm:ss tt" : "h:mm tt");
}

using System.Drawing.Drawing2D;

namespace StudentTimerClock;

/// <summary>All icons are drawn as vectors so they stay crisp at any DPI - no image files needed.</summary>
public static class Icons
{
    public static readonly string[] ItemIcons = { "clock", "book", "notes", "person", "group", "computer", "tomato", "cup", "food", "run", "star", "bell", "moon", "heart", "music", "pill", "game", "flask", "calc" };

    static readonly Dictionary<string, Bitmap> _cache = new();

    public static Bitmap Get(string key, int size, bool disabled = false)
    {
        string k = $"{key}|{size}|{disabled}";
        if (_cache.TryGetValue(k, out var b)) return b;
        b = new Bitmap(size, size);
        using (var g = Graphics.FromImage(b))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            Draw(g, key, new RectangleF(0, 0, size, size));
        }
        if (disabled) MakeGray(b);
        _cache[k] = b;
        return b;
    }

    static void MakeGray(Bitmap b)
    {
        for (int y = 0; y < b.Height; y++)
            for (int x = 0; x < b.Width; x++)
            {
                var c = b.GetPixel(x, y);
                int l = (int)(c.R * 0.3 + c.G * 0.59 + c.B * 0.11);
                l = 150 + l / 3;
                b.SetPixel(x, y, Color.FromArgb(c.A / 2, l, l, l));
            }
    }

    public static Icon MakeIcon(string key, int size)
    {
        using var bmp = Get(key, size);
        return Icon.FromHandle(new Bitmap(bmp).GetHicon());
    }

    public static void Draw(Graphics g, string key, RectangleF r)
    {
        float W = r.Width, H = r.Height;
        PointF P(float fx, float fy) => new(r.X + fx * W, r.Y + fy * H);
        RectangleF R(float fx, float fy, float fw, float fh) => new(r.X + fx * W, r.Y + fy * H, fw * W, fh * H);
        float lw = Math.Max(1.2f, W / 14f);
        var dark = Color.FromArgb(64, 72, 84);

        switch (key)
        {
            case "add":
                using (var p = new Pen(Color.FromArgb(46, 160, 90), W * 0.2f))
                { g.DrawLine(p, P(.5f, .1f), P(.5f, .9f)); g.DrawLine(p, P(.1f, .5f), P(.9f, .5f)); }
                break;
            case "edit":
                Doc(g, R(.12f, .06f, .6f, .8f));
                using (var path = new GraphicsPath())
                {
                    path.AddPolygon(new[] { P(.42f, .82f), P(.48f, .62f), P(.86f, .24f), P(.98f, .36f), P(.6f, .74f) });
                    using var br = new SolidBrush(Color.FromArgb(70, 110, 170)); g.FillPath(br, path);
                }
                using (var br = new SolidBrush(Color.FromArgb(250, 200, 80))) g.FillPolygon(br, new[] { P(.42f, .82f), P(.48f, .62f), P(.6f, .74f) });
                break;
            case "copy":
                Doc(g, R(.08f, .06f, .55f, .68f));
                Doc(g, R(.36f, .28f, .55f, .68f));
                break;
            case "delete":
                using (var p = new Pen(Color.FromArgb(90, 96, 106), W * 0.13f))
                { p.StartCap = p.EndCap = LineCap.Round; g.DrawLine(p, P(.15f, .15f), P(.85f, .85f)); g.DrawLine(p, P(.85f, .15f), P(.15f, .85f)); }
                break;
            case "undo":
                using (var p = new Pen(Color.FromArgb(120, 130, 140), W * 0.12f))
                {
                    p.EndCap = LineCap.Round;
                    g.DrawArc(p, R(.2f, .25f, .65f, .6f), 200, 250);
                    using var br = new SolidBrush(Color.FromArgb(120, 130, 140));
                    g.FillPolygon(br, new[] { P(.05f, .35f), P(.4f, .32f), P(.2f, .64f) });
                }
                break;
            case "start":
                Circle(g, r, Theme.Green);
                using (var br = new SolidBrush(Color.White)) g.FillPolygon(br, new[] { P(.4f, .28f), P(.74f, .5f), P(.4f, .72f) });
                break;
            case "pause":
                Circle(g, r, Color.FromArgb(240, 160, 40));
                using (var br = new SolidBrush(Color.White)) { g.FillRectangle(br, R(.33f, .29f, .12f, .42f)); g.FillRectangle(br, R(.55f, .29f, .12f, .42f)); }
                break;
            case "stop":
                Circle(g, r, Theme.Red);
                using (var br = new SolidBrush(Color.White)) g.FillRectangle(br, R(.32f, .32f, .36f, .36f));
                break;
            case "reset":
                Circle(g, r, Theme.Accent);
                using (var p = new Pen(Color.White, W * 0.09f))
                {
                    g.DrawArc(p, R(.27f, .27f, .46f, .46f), -60, 290);
                    using var br = new SolidBrush(Color.White);
                    g.FillPolygon(br, new[] { P(.56f, .16f), P(.78f, .3f), P(.55f, .42f) });
                }
                break;
            case "lap":
                Circle(g, r, Color.FromArgb(140, 90, 200));
                using (var p = new Pen(Color.White, W * 0.08f))
                {
                    g.DrawLine(p, P(.35f, .25f), P(.35f, .78f));
                    using var br = new SolidBrush(Color.White);
                    g.FillPolygon(br, new[] { P(.35f, .25f), P(.72f, .36f), P(.35f, .5f) });
                }
                break;
            case "widget":
                using (var br = new SolidBrush(Color.FromArgb(232, 236, 242))) g.FillRectangle(br, R(.04f, .12f, .92f, .76f));
                using (var p = new Pen(dark, lw)) g.DrawRectangle(p, r.X + .04f * W, r.Y + .12f * H, .92f * W, .76f * H);
                using (var path = Theme.Round(R(.28f, .36f, .62f, .4f), W * .06f))
                using (var br = new SolidBrush(Theme.Orange)) g.FillPath(br, path);
                using (var f = Theme.SafeFont("Segoe UI", Math.Max(4, W * .17f), FontStyle.Bold, GraphicsUnit.Pixel))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString("12:34", f, Brushes.White, R(.28f, .36f, .62f, .4f), sf);
                break;
            case "collapse":
            case "expand":
                using (var p = new Pen(dark, lw))
                {
                    for (int i = 0; i < 3; i++) g.DrawLine(p, P(.45f, .22f + i * .28f), P(.95f, .22f + i * .28f));
                    using var br = new SolidBrush(Theme.Accent);
                    if (key == "collapse") g.FillPolygon(br, new[] { P(.05f, .62f), P(.35f, .62f), P(.2f, .38f) });
                    else g.FillPolygon(br, new[] { P(.05f, .38f), P(.35f, .38f), P(.2f, .62f) });
                }
                break;
            case "report":
            case "stats":
                using (var p = new Pen(dark, lw)) { g.DrawLine(p, P(.08f, .08f), P(.08f, .92f)); g.DrawLine(p, P(.08f, .92f), P(.95f, .92f)); }
                using (var br = new SolidBrush(Color.FromArgb(64, 196, 255))) g.FillRectangle(br, R(.18f, .3f, .17f, .58f));
                using (var br = new SolidBrush(Color.FromArgb(124, 200, 60))) g.FillRectangle(br, R(.42f, .5f, .17f, .38f));
                using (var br = new SolidBrush(Color.FromArgb(248, 180, 90))) g.FillRectangle(br, R(.66f, .15f, .17f, .73f));
                break;
            case "pie":
                using (var br = new SolidBrush(Color.FromArgb(64, 196, 255))) g.FillPie(br, r.X + W * .08f, r.Y + H * .08f, W * .84f, H * .84f, -90, 140);
                using (var br = new SolidBrush(Color.FromArgb(124, 200, 60))) g.FillPie(br, r.X + W * .08f, r.Y + H * .08f, W * .84f, H * .84f, 50, 120);
                using (var br = new SolidBrush(Color.FromArgb(248, 180, 90))) g.FillPie(br, r.X + W * .08f, r.Y + H * .08f, W * .84f, H * .84f, 170, 100);
                break;
            case "settings":
                Gear(g, r, dark);
                break;
            case "help":
                Circle(g, r, Theme.Accent);
                using (var f = Theme.SafeFont("Segoe UI", Math.Max(4, H * .6f), FontStyle.Bold, GraphicsUnit.Pixel))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString("?", f, Brushes.White, new RectangleF(r.X, r.Y + H * .03f, W, H), sf);
                break;
            case "info":
                using (var br = new SolidBrush(Color.FromArgb(250, 190, 60))) g.FillEllipse(br, R(.15f, .1f, .7f, .7f));
                using (var br = new SolidBrush(Color.FromArgb(220, 150, 30))) g.FillRectangle(br, R(.25f, .55f, .5f, .2f));
                using (var br = new SolidBrush(Color.FromArgb(160, 110, 20))) g.FillEllipse(br, R(.4f, .78f, .2f, .15f));
                Circle(g, R(.55f, .5f, .45f, .45f), Theme.Accent);
                using (var f = Theme.SafeFont("Georgia", Math.Max(4, H * .32f), FontStyle.Bold, GraphicsUnit.Pixel))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString("i", f, Brushes.White, R(.55f, .5f, .45f, .45f), sf);
                break;
            case "export":
                Doc(g, R(.1f, .06f, .6f, .84f));
                using (var br = new SolidBrush(Color.FromArgb(30, 130, 70))) g.FillRectangle(br, R(.45f, .45f, .5f, .4f));
                using (var f = Theme.SafeFont("Segoe UI", Math.Max(4, H * .2f), FontStyle.Bold, GraphicsUnit.Pixel))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString("csv", f, Brushes.White, R(.45f, .45f, .5f, .4f), sf);
                break;
            case "clipboard":
                using (var br = new SolidBrush(Color.FromArgb(190, 140, 90))) g.FillRectangle(br, R(.15f, .12f, .7f, .82f));
                using (var br = new SolidBrush(Color.White)) g.FillRectangle(br, R(.22f, .22f, .56f, .66f));
                using (var br = new SolidBrush(dark)) g.FillRectangle(br, R(.35f, .05f, .3f, .14f));
                using (var p = new Pen(Color.Silver, lw * .7f)) for (int i = 0; i < 4; i++) g.DrawLine(p, P(.3f, .35f + i * .13f), P(.7f, .35f + i * .13f));
                break;
            case "tray":
                using (var br = new SolidBrush(Color.FromArgb(232, 236, 242))) g.FillRectangle(br, R(.04f, .7f, .92f, .22f));
                using (var p = new Pen(dark, lw)) g.DrawRectangle(p, r.X + .04f * W, r.Y + .7f * H, .92f * W, .22f * H);
                using (var p = new Pen(Theme.Accent, W * .12f)) { p.EndCap = LineCap.ArrowAnchor; g.DrawLine(p, P(.5f, .08f), P(.5f, .6f)); }
                break;

            // ---- navigation
            case "timers":
                Hourglass(g, r);
                break;
            case "stopwatches":
                Stopwatch(g, r);
                break;
            case "clock":
                Analog(g, r, DateTime.Now.Date.AddHours(10).AddMinutes(10), Color.White, dark);
                break;

            // ---- item icons
            case "cup":
                using (var br = new SolidBrush(Color.FromArgb(150, 95, 60))) g.FillPie(br, R(.12f, .05f, .62f, .9f), 0, 180);
                using (var br = new SolidBrush(Color.FromArgb(240, 240, 240))) g.FillRectangle(br, R(.12f, .4f, .62f, .1f));
                using (var p = new Pen(Color.FromArgb(150, 95, 60), W * .09f)) g.DrawArc(p, R(.6f, .45f, .3f, .3f), -90, 180);
                using (var p = new Pen(Color.FromArgb(170, 170, 170), W * .05f)) { g.DrawBezier(p, P(.3f, .35f), P(.25f, .25f), P(.38f, .2f), P(.32f, .08f)); g.DrawBezier(p, P(.5f, .35f), P(.45f, .25f), P(.58f, .2f), P(.52f, .08f)); }
                break;
            case "food":
                using (var br = new SolidBrush(Color.FromArgb(70, 80, 95))) g.FillPie(br, R(.08f, .2f, .84f, .7f), 0, 180);
                using (var br = new SolidBrush(Color.FromArgb(250, 210, 90))) g.FillEllipse(br, R(.2f, .38f, .6f, .2f));
                using (var p = new Pen(Color.FromArgb(70, 80, 95), W * .08f)) g.DrawLine(p, P(.0f, .55f), P(.1f, .55f));
                using (var p = new Pen(Color.FromArgb(200, 200, 200), W * .05f)) { g.DrawBezier(p, P(.35f, .32f), P(.3f, .22f), P(.42f, .16f), P(.36f, .04f)); g.DrawBezier(p, P(.6f, .32f), P(.55f, .22f), P(.67f, .16f), P(.61f, .04f)); }
                break;
            case "tomato":
                using (var br = new SolidBrush(Color.FromArgb(230, 60, 50))) g.FillEllipse(br, R(.08f, .2f, .84f, .74f));
                using (var br = new SolidBrush(Color.FromArgb(255, 255, 255, 120))) g.FillEllipse(br, R(.22f, .32f, .2f, .14f));
                using (var br = new SolidBrush(Color.FromArgb(60, 160, 60))) g.FillPolygon(br, new[] { P(.5f, .05f), P(.56f, .2f), P(.75f, .18f), P(.6f, .3f), P(.5f, .26f), P(.4f, .3f), P(.25f, .18f), P(.44f, .2f) });
                break;
            case "run":
                using (var br = new SolidBrush(Color.FromArgb(40, 140, 210))) g.FillEllipse(br, R(.52f, .04f, .2f, .2f));
                using (var p = new Pen(Color.FromArgb(40, 140, 210), W * .12f))
                {
                    p.StartCap = p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                    g.DrawLines(p, new[] { P(.55f, .3f), P(.42f, .58f), P(.6f, .72f), P(.55f, .95f) });
                    g.DrawLines(p, new[] { P(.42f, .58f), P(.25f, .8f), P(.08f, .78f) });
                    g.DrawLines(p, new[] { P(.2f, .42f), P(.35f, .32f), P(.55f, .3f), P(.68f, .45f), P(.85f, .42f) });
                }
                break;
            case "book":
                using (var br = new SolidBrush(Color.FromArgb(60, 120, 200))) g.FillRectangle(br, R(.15f, .1f, .7f, .8f));
                using (var br = new SolidBrush(Color.FromArgb(245, 245, 235))) g.FillRectangle(br, R(.2f, .75f, .65f, .12f));
                using (var br = new SolidBrush(Color.FromArgb(40, 90, 160))) g.FillRectangle(br, R(.15f, .1f, .08f, .8f));
                using (var br = new SolidBrush(Color.FromArgb(250, 200, 70))) g.FillRectangle(br, R(.38f, .25f, .35f, .12f));
                break;
            case "computer":
                using (var br = new SolidBrush(Color.FromArgb(60, 66, 76))) g.FillRectangle(br, R(.06f, .12f, .88f, .6f));
                using (var br = new SolidBrush(Color.FromArgb(90, 180, 240))) g.FillRectangle(br, R(.12f, .18f, .76f, .48f));
                using (var br = new SolidBrush(Color.FromArgb(60, 66, 76))) { g.FillRectangle(br, R(.42f, .72f, .16f, .12f)); g.FillRectangle(br, R(.25f, .84f, .5f, .07f)); }
                break;
            case "star":
                {
                    var pts = new PointF[10];
                    for (int i = 0; i < 10; i++)
                    {
                        double a = -Math.PI / 2 + i * Math.PI / 5; float rr = i % 2 == 0 ? .47f : .2f;
                        pts[i] = P(.5f + (float)Math.Cos(a) * rr, .53f + (float)Math.Sin(a) * rr);
                    }
                    using var br = new LinearGradientBrush(r, Color.FromArgb(255, 220, 80), Color.FromArgb(245, 160, 20), 90f);
                    g.FillPolygon(br, pts);
                }
                break;
            case "bell":
                using (var br = new SolidBrush(Color.FromArgb(245, 185, 40)))
                {
                    using var path = new GraphicsPath();
                    path.AddBezier(P(.15f, .75f), P(.25f, .6f), P(.2f, .15f), P(.5f, .15f));
                    path.AddBezier(P(.5f, .15f), P(.8f, .15f), P(.75f, .6f), P(.85f, .75f));
                    path.CloseFigure();
                    g.FillPath(br, path);
                    g.FillEllipse(br, R(.44f, .05f, .12f, .12f));
                }
                using (var br = new SolidBrush(Color.FromArgb(200, 140, 20))) { g.FillRectangle(br, R(.12f, .72f, .76f, .08f)); g.FillEllipse(br, R(.42f, .8f, .16f, .14f)); }
                break;
            case "moon":
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(R(.1f, .1f, .8f, .8f));
                    using var reg = new Region(path);
                    using var p2 = new GraphicsPath(); p2.AddEllipse(R(.32f, .0f, .75f, .75f));
                    reg.Exclude(p2);
                    using var br = new SolidBrush(Color.FromArgb(250, 200, 60)); g.FillRegion(br, reg);
                }
                break;
            case "heart":
                using (var path = new GraphicsPath())
                {
                    path.AddBezier(P(.5f, .3f), P(.35f, .0f), P(-.05f, .2f), P(.15f, .5f));
                    path.AddLine(P(.15f, .5f), P(.5f, .9f));
                    path.AddLine(P(.5f, .9f), P(.85f, .5f));
                    path.AddBezier(P(.85f, .5f), P(1.05f, .2f), P(.65f, .0f), P(.5f, .3f));
                    using var br = new SolidBrush(Color.FromArgb(230, 70, 90)); g.FillPath(br, path);
                }
                break;
            case "music":
                using (var br = new SolidBrush(Color.FromArgb(130, 80, 200)))
                {
                    g.FillEllipse(br, R(.1f, .65f, .28f, .22f)); g.FillEllipse(br, R(.55f, .55f, .28f, .22f));
                    g.FillRectangle(br, R(.3f, .15f, .08f, .62f)); g.FillRectangle(br, R(.75f, .05f, .08f, .62f));
                    g.FillPolygon(br, new[] { P(.3f, .15f), P(.83f, .05f), P(.83f, .2f), P(.3f, .3f) });
                }
                break;
            case "pill":
                g.TranslateTransform(r.X + W / 2, r.Y + H / 2); g.RotateTransform(-40); g.TranslateTransform(-(r.X + W / 2), -(r.Y + H / 2));
                using (var path = Theme.Round(R(.1f, .32f, .8f, .36f), H * .18f)) using (var br = new SolidBrush(Color.FromArgb(240, 240, 245))) g.FillPath(br, path);
                using (var path = Theme.Round(R(.1f, .32f, .4f, .36f), H * .18f)) using (var br = new SolidBrush(Color.FromArgb(230, 80, 80))) g.FillPath(br, path);
                using (var br = new SolidBrush(Color.FromArgb(230, 80, 80))) g.FillRectangle(br, R(.35f, .32f, .15f, .36f));
                using (var path = Theme.Round(R(.1f, .32f, .8f, .36f), H * .18f)) using (var p = new Pen(Color.FromArgb(150, 150, 160), lw * .6f)) g.DrawPath(p, path);
                g.ResetTransform();
                break;
            case "game":
                using (var path = Theme.Round(R(.05f, .28f, .9f, .46f), H * .2f)) using (var br = new SolidBrush(Color.FromArgb(70, 76, 90))) g.FillPath(br, path);
                using (var br = new SolidBrush(Color.White)) { g.FillRectangle(br, R(.18f, .46f, .22f, .08f)); g.FillRectangle(br, R(.25f, .39f, .08f, .22f)); }
                using (var br = new SolidBrush(Color.FromArgb(240, 80, 80))) g.FillEllipse(br, R(.62f, .38f, .1f, .1f));
                using (var br = new SolidBrush(Color.FromArgb(80, 200, 120))) g.FillEllipse(br, R(.74f, .5f, .1f, .1f));
                break;
            case "person":
                using (var br = new SolidBrush(Color.FromArgb(240, 190, 150))) g.FillEllipse(br, R(.3f, .06f, .4f, .4f));
                using (var br = new SolidBrush(Color.FromArgb(60, 70, 90))) g.FillPie(br, r.X + W * .3f, r.Y + H * .02f, W * .4f, H * .3f, 180, 180);
                using (var br = new SolidBrush(Color.FromArgb(40, 110, 200))) g.FillPie(br, R(.1f, .5f, .8f, .9f), 180, 180);
                using (var br = new SolidBrush(Color.White)) g.FillPolygon(br, new[] { P(.42f, .5f), P(.58f, .5f), P(.5f, .66f) });
                using (var br = new SolidBrush(Color.FromArgb(200, 40, 40))) g.FillPolygon(br, new[] { P(.47f, .53f), P(.53f, .53f), P(.55f, .8f), P(.5f, .9f), P(.45f, .8f) });
                break;
            case "group":
                foreach (var (fx, c) in new[] { (.02f, Color.FromArgb(120, 170, 220)), (.5f, Color.FromArgb(120, 200, 150)), (.26f, Color.FromArgb(240, 150, 80)) })
                {
                    float y0 = fx == .26f ? .25f : .12f;
                    using var br = new SolidBrush(Color.FromArgb(240, 195, 160)); g.FillEllipse(br, R(fx + .1f, y0, .28f, .28f));
                    using var b2 = new SolidBrush(c); g.FillPie(b2, R(fx, y0 + .3f, .48f, .6f), 180, 180);
                }
                break;
            case "notes":
                using (var br = new SolidBrush(Color.FromArgb(255, 245, 190))) g.FillRectangle(br, R(.15f, .08f, .7f, .86f));
                using (var p = new Pen(Color.FromArgb(170, 150, 90), lw * .6f)) { g.DrawRectangle(p, r.X + .15f * W, r.Y + .08f * H, .7f * W, .86f * H); for (int i = 0; i < 4; i++) g.DrawLine(p, P(.25f, .3f + i * .15f), P(.75f, .3f + i * .15f)); }
                using (var p = new Pen(Color.FromArgb(40, 110, 200), W * .1f)) { p.EndCap = LineCap.Triangle; g.DrawLine(p, P(.85f, .12f), P(.5f, .7f)); }
                break;
            case "flask":
                using (var path = new GraphicsPath())
                {
                    path.AddPolygon(new[] { P(.4f, .08f), P(.6f, .08f), P(.6f, .38f), P(.9f, .9f), P(.1f, .9f), P(.4f, .38f) });
                    using var br = new SolidBrush(Color.FromArgb(225, 240, 250)); g.FillPath(br, path);
                    using var p = new Pen(dark, lw * .7f); g.DrawPath(p, path);
                }
                using (var br = new SolidBrush(Color.FromArgb(120, 200, 90))) g.FillPolygon(br, new[] { P(.27f, .62f), P(.73f, .62f), P(.88f, .88f), P(.12f, .88f) });
                break;
            case "calc":
                using (var path = Theme.Round(R(.15f, .05f, .7f, .9f), W * .08f)) using (var br = new SolidBrush(Color.FromArgb(70, 76, 90))) g.FillPath(br, path);
                using (var br = new SolidBrush(Color.FromArgb(180, 230, 190))) g.FillRectangle(br, R(.23f, .12f, .54f, .2f));
                using (var br = new SolidBrush(Color.White)) for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) g.FillRectangle(br, R(.25f + i * .18f, .4f + j * .17f, .12f, .11f));
                break;

            // ---- action icons
            case "next":
                Circle(g, r, Theme.Green);
                using (var br = new SolidBrush(Color.White)) { g.FillPolygon(br, new[] { P(.28f, .3f), P(.52f, .5f), P(.28f, .7f) }); g.FillPolygon(br, new[] { P(.5f, .3f), P(.74f, .5f), P(.5f, .7f) }); }
                break;
            case "run_prog":
                using (var br = new SolidBrush(Color.FromArgb(60, 160, 90))) g.FillPolygon(br, new[] { P(.15f, .08f), P(.9f, .5f), P(.15f, .92f) });
                break;
            case "globe":
                Circle(g, r, Color.FromArgb(40, 120, 210));
                using (var p = new Pen(Color.White, lw * .7f))
                {
                    g.DrawEllipse(p, R(.3f, .1f, .4f, .8f)); g.DrawLine(p, P(.5f, .08f), P(.5f, .92f));
                    g.DrawLine(p, P(.1f, .5f), P(.9f, .5f)); g.DrawArc(p, R(.15f, .2f, .7f, .25f), 180, 180); g.DrawArc(p, R(.15f, .55f, .7f, .25f), 0, 180);
                }
                break;
            case "mail":
                using (var br = new SolidBrush(Color.White)) g.FillRectangle(br, R(.06f, .2f, .88f, .6f));
                using (var p = new Pen(dark, lw)) { g.DrawRectangle(p, r.X + .06f * W, r.Y + .2f * H, .88f * W, .6f * H); g.DrawLines(p, new[] { P(.06f, .2f), P(.5f, .55f), P(.94f, .2f) }); }
                break;
            case "lock":
                using (var p = new Pen(Color.FromArgb(150, 120, 40), W * .1f)) g.DrawArc(p, R(.28f, .08f, .44f, .5f), 180, 180);
                using (var br = new SolidBrush(Color.FromArgb(245, 185, 40))) g.FillRectangle(br, R(.18f, .4f, .64f, .52f));
                using (var br = new SolidBrush(Color.FromArgb(120, 90, 20))) g.FillEllipse(br, R(.44f, .55f, .12f, .14f));
                break;
            case "key":
                using (var br = new SolidBrush(Color.FromArgb(240, 175, 40))) { g.FillEllipse(br, R(.05f, .1f, .42f, .42f)); g.FillPolygon(br, new[] { P(.35f, .4f), P(.42f, .33f), P(.92f, .83f), P(.85f, .9f) }); g.FillRectangle(br, R(.68f, .66f, .1f, .14f)); }
                using (var br = new SolidBrush(Color.White)) g.FillEllipse(br, R(.17f, .22f, .14f, .14f));
                break;
            case "power_blue":
            case "power_green":
            case "power_red":
                Circle(g, r, key == "power_blue" ? Theme.Accent : key == "power_green" ? Theme.Green : Theme.Red);
                using (var p = new Pen(Color.White, W * .09f)) { p.StartCap = p.EndCap = LineCap.Round; g.DrawArc(p, R(.27f, .27f, .46f, .46f), -60, 300); g.DrawLine(p, P(.5f, .2f), P(.5f, .48f)); }
                break;
            case "monitor":
                using (var br = new SolidBrush(dark)) { g.FillRectangle(br, R(.06f, .12f, .88f, .58f)); g.FillRectangle(br, R(.42f, .7f, .16f, .12f)); g.FillRectangle(br, R(.26f, .82f, .48f, .07f)); }
                using (var br = new SolidBrush(Color.FromArgb(110, 180, 240))) g.FillRectangle(br, R(.12f, .18f, .76f, .46f));
                break;
            case "endproc":
                using (var path = Theme.Round(R(.08f, .08f, .84f, .84f), W * .12f)) using (var br = new SolidBrush(Theme.Red)) g.FillPath(br, path);
                using (var p = new Pen(Color.White, W * .12f)) { g.DrawLine(p, P(.3f, .3f), P(.7f, .7f)); g.DrawLine(p, P(.7f, .3f), P(.3f, .7f)); }
                break;
            case "keys":
                using (var path = Theme.Round(R(.12f, .12f, .76f, .76f), W * .1f))
                {
                    using var br = new SolidBrush(Color.White); g.FillPath(br, path);
                    using var p = new Pen(dark, lw); g.DrawPath(p, path);
                }
                using (var f = Theme.SafeFont("Segoe UI", Math.Max(4, H * .45f), FontStyle.Bold, GraphicsUnit.Pixel))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                using (var br = new SolidBrush(dark)) g.DrawString("A", f, br, R(.12f, .12f, .76f, .76f), sf);
                break;
            case "pause_bars":
                using (var br = new SolidBrush(Color.FromArgb(40, 110, 200))) { g.FillRectangle(br, R(.22f, .12f, .18f, .76f)); g.FillRectangle(br, R(.6f, .12f, .18f, .76f)); }
                break;
            case "board":
                {
                    Color[] cs = { Color.FromArgb(226, 240, 217), Color.FromArgb(218, 227, 243), Color.FromArgb(255, 205, 205), Color.FromArgb(255, 240, 200) };
                    for (int i = 0; i < 4; i++)
                    {
                        var rr = R(.05f + (i % 2) * .46f, .1f + (i / 2) * .42f, .44f, .38f);
                        using var br = new SolidBrush(cs[i]); g.FillRectangle(br, rr);
                        using var p = new Pen(dark, Math.Max(1, lw * .6f)); g.DrawRectangle(p, rr.X, rr.Y, rr.Width, rr.Height);
                        using var p2 = new Pen(Color.FromArgb(40, 40, 40), Math.Max(1, lw * .8f)); g.DrawLine(p2, rr.X + rr.Width * .2f, rr.Y + rr.Height * .55f, rr.Right - rr.Width * .2f, rr.Y + rr.Height * .55f);
                    }
                }
                break;
            case "style":
                using (var br = new SolidBrush(Color.FromArgb(240, 220, 180))) g.FillEllipse(br, R(.05f, .1f, .9f, .8f));
                using (var br = new SolidBrush(Color.White)) g.FillEllipse(br, R(.55f, .55f, .18f, .16f));
                foreach (var (fx, fy, c) in new[] { (.2f, .3f, Color.Red), (.42f, .2f, Color.FromArgb(40, 120, 220)), (.64f, .28f, Color.FromArgb(60, 170, 80)), (.22f, .55f, Color.FromArgb(250, 190, 30)) })
                    using (var br = new SolidBrush(c)) g.FillEllipse(br, R(fx, fy, .16f, .16f));
                break;
            case "fullscreen":
                using (var p = new Pen(dark, lw * 1.2f))
                {
                    g.DrawLines(p, new[] { P(.1f, .35f), P(.1f, .1f), P(.35f, .1f) }); g.DrawLines(p, new[] { P(.65f, .1f), P(.9f, .1f), P(.9f, .35f) });
                    g.DrawLines(p, new[] { P(.9f, .65f), P(.9f, .9f), P(.65f, .9f) }); g.DrawLines(p, new[] { P(.35f, .9f), P(.1f, .9f), P(.1f, .65f) });
                }
                break;
            case "backup":   // box (archive) with a green arrow going in
            case "restore":  // box with a blue arrow coming out
            {
                var box = R(.08f, .46f, .84f, .46f);
                using (var br = new LinearGradientBrush(box, Color.FromArgb(250, 214, 140), Color.FromArgb(222, 168, 80), 90f)) g.FillRectangle(br, box);
                using (var p = new Pen(Color.FromArgb(150, 105, 40), lw * .8f)) { g.DrawRectangle(p, box.X, box.Y, box.Width, box.Height); g.DrawLine(p, P(.08f, .6f), P(.92f, .6f)); }
                using (var br = new SolidBrush(Color.FromArgb(150, 105, 40))) g.FillRectangle(br, R(.38f, .66f, .24f, .08f));
                bool inArrow = key == "backup";
                using (var p = new Pen(inArrow ? Theme.Green : Theme.Accent, W * .14f))
                {
                    p.EndCap = LineCap.ArrowAnchor;
                    if (inArrow) g.DrawLine(p, P(.5f, .02f), P(.5f, .5f));
                    else g.DrawLine(p, P(.5f, .55f), P(.5f, .04f));
                }
                break;
            }
            case "terms":
                Doc(g, R(.12f, .06f, .66f, .86f));
                Circle(g, R(.5f, .5f, .48f, .48f), Theme.Green);
                using (var p = new Pen(Color.White, W * .07f)) g.DrawLines(p, new[] { P(.6f, .74f), P(.7f, .84f), P(.88f, .62f) });
                break;
            default:
                Analog(g, r, DateTime.Now, Color.White, dark);
                break;
        }
    }

    static void Circle(Graphics g, RectangleF r, Color c)
    {
        var rr = RectangleF.Inflate(r, -r.Width * .04f, -r.Height * .04f);
        using var br = new LinearGradientBrush(rr, Theme.Lighten(c, .15f), Theme.Darken(c, .08f), 90f);
        g.FillEllipse(br, rr);
    }

    static void Doc(Graphics g, RectangleF r)
    {
        float f = r.Width * .3f;
        var pts = new[] { new PointF(r.X, r.Y), new PointF(r.Right - f, r.Y), new PointF(r.Right, r.Y + f), new PointF(r.Right, r.Bottom), new PointF(r.X, r.Bottom) };
        using (var br = new SolidBrush(Color.White)) g.FillPolygon(br, pts);
        using (var p = new Pen(Color.FromArgb(90, 100, 115), Math.Max(1f, r.Width / 12f))) g.DrawPolygon(p, pts);
        using (var p = new Pen(Color.FromArgb(170, 180, 190), Math.Max(1f, r.Width / 16f)))
            for (int i = 0; i < 3; i++) g.DrawLine(p, r.X + r.Width * .18f, r.Y + r.Height * (.4f + i * .17f), r.Right - r.Width * .18f, r.Y + r.Height * (.4f + i * .17f));
    }

    static void Gear(Graphics g, RectangleF r, Color c)
    {
        float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, ro = r.Width * .46f, ri = r.Width * .34f;
        var pts = new List<PointF>();
        int teeth = 8;
        for (int i = 0; i < teeth * 4; i++)
        {
            double a = i * Math.PI * 2 / (teeth * 4);
            float rr = (i % 4 == 0 || i % 4 == 1) ? ro : ri;
            pts.Add(new PointF(cx + (float)Math.Cos(a) * rr, cy + (float)Math.Sin(a) * rr));
        }
        using (var br = new SolidBrush(c)) g.FillPolygon(br, pts.ToArray());
        using (var br = new SolidBrush(Color.White)) g.FillEllipse(br, cx - r.Width * .14f, cy - r.Width * .14f, r.Width * .28f, r.Width * .28f);
    }

    static void Hourglass(Graphics g, RectangleF r)
    {
        PointF P(float fx, float fy) => new(r.X + fx * r.Width, r.Y + fy * r.Height);
        var dark = Color.FromArgb(64, 72, 84);
        using (var br = new SolidBrush(Color.FromArgb(225, 238, 250)))
            g.FillPolygon(br, new[] { P(.22f, .1f), P(.78f, .1f), P(.55f, .5f), P(.78f, .9f), P(.22f, .9f), P(.45f, .5f) });
        using (var br = new SolidBrush(Color.FromArgb(240, 170, 60)))
        {
            g.FillPolygon(br, new[] { P(.32f, .24f), P(.68f, .24f), P(.5f, .46f) });
            g.FillPolygon(br, new[] { P(.5f, .66f), P(.74f, .88f), P(.26f, .88f) });
            g.FillRectangle(br, r.X + r.Width * .48f, r.Y + r.Height * .46f, r.Width * .04f, r.Height * .3f);
        }
        using (var p = new Pen(dark, Math.Max(1.2f, r.Width / 16f)))
        {
            g.DrawPolygon(p, new[] { P(.22f, .1f), P(.78f, .1f), P(.55f, .5f), P(.78f, .9f), P(.22f, .9f), P(.45f, .5f) });
            p.Width *= 1.8f; g.DrawLine(p, P(.12f, .08f), P(.88f, .08f)); g.DrawLine(p, P(.12f, .92f), P(.88f, .92f));
        }
    }

    public static void Stopwatch(Graphics g, RectangleF r)
    {
        var dark = Color.FromArgb(64, 72, 84);
        float W = r.Width;
        using (var br = new SolidBrush(dark))
        {
            g.FillRectangle(br, r.X + W * .42f, r.Y + W * .02f, W * .16f, W * .1f);
            g.FillRectangle(br, r.X + W * .46f, r.Y + W * .1f, W * .08f, W * .08f);
        }
        var face = new RectangleF(r.X + W * .12f, r.Y + W * .17f, W * .76f, W * .76f);
        using (var br = new SolidBrush(Color.White)) g.FillEllipse(br, face);
        using (var br = new SolidBrush(Color.FromArgb(255, 210, 150))) g.FillPie(br, RectangleF.Inflate(face, -W * .06f, -W * .06f), -90, 120);
        using (var p = new Pen(Theme.Orange, Math.Max(1.5f, W * .08f))) g.DrawEllipse(p, face);
        float cx = face.X + face.Width / 2, cy = face.Y + face.Height / 2;
        using (var p = new Pen(dark, Math.Max(1.2f, W * .06f))) { p.EndCap = LineCap.Round; g.DrawLine(p, cx, cy, cx + W * .2f, cy - W * .12f); }
    }

    /// <summary>Small analog clock face like the "Clock" column in Hot Alarm Clock.</summary>
    public static void Analog(Graphics g, RectangleF r, DateTime t, Color face, Color ink, float? progress = null, Color? progressColor = null)
    {
        float W = Math.Min(r.Width, r.Height);
        var fr = new RectangleF(r.X + (r.Width - W) / 2 + W * .06f, r.Y + (r.Height - W) / 2 + W * .06f, W * .88f, W * .88f);
        using (var br = new SolidBrush(face)) g.FillEllipse(br, fr);
        if (progress.HasValue && progress > 0)
            using (var br = new SolidBrush(progressColor ?? Color.FromArgb(120, 0, 120, 212)))
                g.FillPie(br, RectangleF.Inflate(fr, -W * .04f, -W * .04f), -90, Math.Min(360, progress.Value * 360));
        float cx = fr.X + fr.Width / 2, cy = fr.Y + fr.Height / 2, rad = fr.Width / 2;
        using (var p = new Pen(ink, Math.Max(1, W / 24f)))
        {
            for (int i = 0; i < 12; i++)
            {
                double a = i * Math.PI / 6;
                float l = i % 3 == 0 ? .78f : .86f;
                g.DrawLine(p, cx + (float)Math.Cos(a) * rad * l, cy + (float)Math.Sin(a) * rad * l, cx + (float)Math.Cos(a) * rad * .95f, cy + (float)Math.Sin(a) * rad * .95f);
            }
        }
        if (!progress.HasValue)
        {
            double ha = (t.Hour % 12 + t.Minute / 60.0) * Math.PI / 6 - Math.PI / 2;
            double ma = (t.Minute + t.Second / 60.0) * Math.PI / 30 - Math.PI / 2;
            using var p = new Pen(ink, Math.Max(1.4f, W / 14f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(p, cx, cy, cx + (float)Math.Cos(ha) * rad * .5f, cy + (float)Math.Sin(ha) * rad * .5f);
            p.Width = Math.Max(1.2f, W / 20f);
            g.DrawLine(p, cx, cy, cx + (float)Math.Cos(ma) * rad * .78f, cy + (float)Math.Sin(ma) * rad * .78f);
        }
        else
        {
            double a = progress.Value * Math.PI * 2 - Math.PI / 2;
            using var p = new Pen(Theme.Red, Math.Max(1.2f, W / 18f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(p, cx, cy, cx + (float)Math.Cos(a) * rad * .82f, cy + (float)Math.Sin(a) * rad * .82f);
        }
        using (var p = new Pen(Theme.Darken(face, .55f), Math.Max(1, W / 22f))) g.DrawEllipse(p, fr);
    }
}

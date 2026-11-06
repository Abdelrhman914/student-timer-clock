namespace StudentTimerClock;

/// <summary>Reminder popup shown when a timer reaches zero.</summary>
public class AlertForm : Form
{
    public readonly TimerItem Timer;
    readonly System.Windows.Forms.Timer _blink = new() { Interval = 500 };
    bool _flash;
    readonly Label _over = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };

    public AlertForm(TimerItem t, bool whileAway)
    {
        Timer = t;
        Text = "Student Timer Clock - " + t.Name;
        Icon = Program.AppIcon;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = true;
        Font = Theme.Normal;
        BackColor = Color.White;
        ClientSize = new Size(Theme.S(450), Theme.S(178));

        var head = new Panel { Dock = DockStyle.Top, Height = Theme.S(70) };
        head.Paint += (s, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var c = _flash ? Color.FromArgb(255, 225, 220) : t.Color;
            using (var br = new SolidBrush(c)) g.FillRectangle(br, head.ClientRectangle);
            g.DrawImage(Icons.Get(t.IconKey, Theme.S(44)), Theme.S(14), Theme.S(13), Theme.S(44), Theme.S(44));
            TextRenderer.DrawText(g, t.Name, Theme.UI(13f, FontStyle.Bold), new Rectangle(Theme.S(70), Theme.S(8), head.Width - Theme.S(80), Theme.S(28)), Color.Black, TextFormatFlags.EndEllipsis);
            string sub = whileAway ? $"Finished while the app was closed ({t.FinishedAtUtc?.ToLocalTime():HH:mm})" : $"Time is up!  ({Fmt.Human(t.DurationSeconds)} timer)";
            TextRenderer.DrawText(g, sub, Theme.Normal, new Rectangle(Theme.S(70), Theme.S(38), head.Width - Theme.S(80), Theme.S(20)), Color.FromArgb(60, 60, 60), TextFormatFlags.EndEllipsis);
        };

        _over.SetBounds(Theme.S(14), Theme.S(76), Theme.S(420), Theme.S(22));
        _over.ForeColor = Theme.Red;
        _over.Font = Theme.Bold;
        var notes = new Label { Text = t.Notes, AutoEllipsis = true, ForeColor = Theme.TextDim };
        notes.SetBounds(Theme.S(14), Theme.S(98), Theme.S(420), Theme.S(20));

        var flow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Theme.S(46), Padding = new Padding(Theme.S(8)), BackColor = Theme.HeaderBack, FlowDirection = FlowDirection.RightToLeft };
        Button B(string text, Action a, bool accent = false)
        {
            var b = new Button { Text = text, AutoSize = true, Height = Theme.S(28), Margin = new Padding(Theme.S(3), 0, 0, 0) };
            if (accent) { b.BackColor = Theme.Accent; b.ForeColor = Color.White; b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0; }
            b.Click += (s, e) => { a(); Close(); };
            flow.Controls.Add(b);
            return b;
        }
        var dismiss = B("Dismiss", () => Program.Main?.DismissTimer(t, false), true);
        B("Restart", () => { Program.Main?.DismissTimer(t, false); AppState.Restart(t); });
        B("+10 min", () => { AppState.AddTime(t, 600); });
        B("+5 min", () => { AppState.AddTime(t, 300); });
        B("+1 min", () => { AppState.AddTime(t, 60); });
        AcceptButton = dismiss;

        Controls.Add(_over);
        Controls.Add(notes);
        Controls.Add(flow);
        Controls.Add(head);

        _blink.Tick += (s, e) =>
        {
            _flash = !_flash; head.Invalidate();
            if (t.FinishedAtUtc.HasValue && t.Finished) _over.Text = $"Overtime: {Fmt.Time((DateTime.UtcNow - t.FinishedAtUtc.Value).TotalSeconds)}";
            else if (!t.Finished && !t.AutoRestart) Close(); // dismissed elsewhere (widget / main window)
            else _over.Text = t.AutoRestart ? $"Restarted automatically - next in {Fmt.Remaining(t.Remaining(DateTime.UtcNow))}" : "";
        };
        _blink.Start();
        FormClosed += (s, e) => { _blink.Stop(); Program.Main?.AlertClosed(this); };
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var wa = Screen.PrimaryScreen.WorkingArea;
        int n = Application.OpenForms.OfType<AlertForm>().Count(a => a != this && a.Visible);
        Location = new Point(wa.Right - Width - Theme.S(12), Math.Max(wa.Top, wa.Bottom - (Height + Theme.S(8)) * (n + 1)));
        Activate();
    }
}

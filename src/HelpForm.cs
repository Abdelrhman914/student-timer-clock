namespace StudentTimerClock;

/// <summary>Help, terms &amp; conditions and about - in one tabbed window.</summary>
public class HelpForm : Form
{
    public const string Version = "0.3";

    public static string Terms()
    {
        try
        {
            using var st = typeof(HelpForm).Assembly.GetManifestResourceStream("terms.txt");
            if (st != null) using (var r = new StreamReader(st)) return r.ReadToEnd();
        }
        catch { }
        return "Terms and conditions are not available.";
    }

    public HelpForm(int tab = 0)
    {
        Text = "Student Timer Clock - Help";
        Icon = Program.AppIcon;
        Font = Theme.Normal;
        Size = new Size(Theme.S(700), Theme.S(620));
        MinimumSize = new Size(Theme.S(500), Theme.S(400));
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 5) };
        tabs.TabPages.Add(TextPage("Help", HelpText));
        tabs.TabPages.Add(TextPage("Terms & Conditions", Terms()));
        tabs.TabPages.Add(AboutPage());
        tabs.TabPages.Add(SecurityPage());
        tabs.SelectedIndex = Math.Clamp(tab, 0, 3);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(Theme.S(8)), BackColor = Theme.HeaderBack };
        var close = new Button { Text = "Close", Width = Theme.S(90), Height = Theme.S(28), DialogResult = DialogResult.OK };
        bottom.Controls.Add(close);
        CancelButton = close; AcceptButton = close;
        Controls.Add(tabs);
        Controls.Add(bottom);
    }

    static TabPage TextPage(string title, string text)
    {
        var p = new TabPage(title) { BackColor = Color.White, Padding = new Padding(Theme.S(8)) };
        var tb = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White, Font = Theme.UI(9.5f), Text = text, DetectUrls = false };
        p.Controls.Add(tb);
        return p;
    }

    static TabPage AboutPage()
    {
        var p = new TabPage("About") { BackColor = Color.White };
        var pic = new PictureBox { Image = Program.AppIcon != null ? new Icon(Program.AppIcon, 128, 128).ToBitmap() : null, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(Theme.S(110), Theme.S(110)), Location = new Point(Theme.S(30), Theme.S(30)) };
        var name = new Label { Text = "Student Timer Clock", Font = Theme.UI(20f, FontStyle.Bold), AutoSize = true, Location = new Point(Theme.S(160), Theme.S(40)) };
        var ver = new Label { Text = "Version " + Version, Font = Theme.UI(11f), AutoSize = true, ForeColor = Theme.TextDim, Location = new Point(Theme.S(163), Theme.S(82)) };
        var desc = new Label
        {
            Text = "Timers, exam countdowns, stopwatches with named laps, customisable desktop widgets, a fullscreen classroom board, \"when done\" actions and study statistics.\n\n" +
                   "Your data stays on this computer:\n" + AppState.DataDir + "\n\n" +
                   "Start with Windows: " + (Autostart.IsEnabled ? "ON" : "off") + "   (change it in File → Settings)",
            AutoSize = false, Location = new Point(Theme.S(30), Theme.S(160)), Size = new Size(Theme.S(600), Theme.S(160))
        };
        var data = new LinkLabel { Text = "Open data folder", AutoSize = true, Location = new Point(Theme.S(30), Theme.S(330)) };
        data.LinkClicked += (s, e) => { try { Security.SafeShell.OpenFolder(AppState.DataDir); } catch { } };
        p.Controls.AddRange(new Control[] { pic, name, ver, desc, data });
        return p;
    }

    static TabPage SecurityPage()
    {
        var p = new TabPage("Security") { BackColor = Color.White, Padding = new Padding(Theme.S(8)) };
        var g = Security.IntegrityGuard.LastSignature;
        string Grp(string h) => string.IsNullOrEmpty(h) ? "-" : string.Join(" ", Enumerable.Range(0, (h.Length + 7) / 8).Select(i => h.Substring(i * 8, Math.Min(8, h.Length - i * 8))));
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("INTEGRITY PROTECTION");
        sb.AppendLine(" Status:       " + Security.IntegrityGuard.Status);
        sb.AppendLine(" Mode:         " + (Security.IntegrityGuard.Enforced ? "Enforced - the program stops if it detects modification or injected code" : "Report-only (development build)"));
        sb.AppendLine(" Last full check: " + (Security.IntegrityGuard.LastFullCheck?.ToString("dddd d MMM yyyy, h:mm:ss tt") ?? "-"));
        sb.AppendLine();
        sb.AppendLine("PROGRAM FILE");
        sb.AppendLine(" " + Environment.ProcessPath);
        sb.AppendLine(" SHA-256:      " + Grp(g?.FileSha256));
        sb.AppendLine(" Expected:     " + Grp(Security.IntegrityGuard.ExpectedExeSha256) + "   (from the signed integrity manifest)");
        sb.AppendLine();
        sb.AppendLine("DIGITAL SIGNATURE");
        if (g?.Signer != null)
        {
            sb.AppendLine(" Signed by:    " + g.Signer.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, false));
            sb.AppendLine(" Subject:      " + g.Signer.Subject);
            sb.AppendLine(" Issuer:       " + g.Signer.Issuer);
            sb.AppendLine(" Serial:       " + g.Signer.SerialNumber);
            sb.AppendLine(" Valid:        " + g.Signer.NotBefore.ToString("d MMM yyyy") + " - " + g.Signer.NotAfter.ToString("d MMM yyyy"));
            sb.AppendLine(" Thumbprint:   " + g.Signer.Thumbprint + "  (SHA-1)");
            sb.AppendLine(" SHA-256:      " + Grp(g.SignerSha256));
            if (g.SigningTime.HasValue) sb.AppendLine(" Signed on:    " + g.SigningTime.Value.ToLocalTime().ToString("d MMM yyyy, h:mm:ss tt"));
        }
        else sb.AppendLine(" " + (g?.Error ?? "not checked"));
        sb.AppendLine();
        sb.AppendLine("PROCESS HARDENING");
        foreach (var h in Security.Hardening.Applied) sb.AppendLine(" • " + h);
        if (Security.Hardening.Applied.Count == 0) sb.AppendLine(" -");
        sb.AppendLine();
        sb.AppendLine("Security events are written to: " + Security.SecurityLog.FilePath);
        var tb = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White, Font = new Font(FontFamily.GenericMonospace, 8.6f), Text = sb.ToString(), DetectUrls = false, WordWrap = false };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, Theme.S(4), 0, 0) };
        var copy = new Button { Text = "Copy details", AutoSize = true };
        copy.Click += (s, e) => { try { Clipboard.SetText(sb.ToString()); } catch { } };
        var log = new Button { Text = "Open security log", AutoSize = true };
        log.Click += (s, e) => { try { if (File.Exists(Security.SecurityLog.FilePath)) Security.SafeShell.OpenFolder(Path.GetDirectoryName(Security.SecurityLog.FilePath)); else MessageBox.Show("No security events have been recorded.", "Security log"); } catch { } };
        bar.Controls.AddRange(new Control[] { copy, log });
        p.Controls.Add(tb); p.Controls.Add(bar);
        return p;
    }

    const string HelpText =
@"STUDENT TIMER CLOCK  v0.3

TIMERS (countdowns)
 • Add a timer: give it a name, group, duration (days / hours / minutes / seconds), colour, icon and sound.
 • Or choose ""Count down to a date & time"" for an exam, holiday or deadline - it shows the days, hours,
   minutes and seconds left.
 • Click the green ▶ in the Run column (or press Space) to start / pause.
 • The ""Ends at"" column shows when each running timer will finish (e.g. Today 10:05:00 PM).
 • When it reaches zero a reminder window pops up and the sound plays: Dismiss, Restart or snooze.

WHEN DONE (actions)
 • Edit a timer → ""When done (actions)"" tab → Add…  Pick an action on the left, fill in the right side,
   press Test to try it.
 • Start next timer - chains timers in the same group (lesson segments, Pomodoro study / break).
   The desktop widget moves on to the next timer automatically.
 • Run program, open a web page or file, send an email, end a process (e.g. a game), press keys,
   play / pause music, turn off the monitor, screen saver, lock, log off, sleep, hibernate, restart,
   shut down, and Pause (wait) between actions.
 • Restart / shut down / log off / sleep show a 30 second countdown first so you can cancel.

STOPWATCHES (track how long you do things)
 • One stopwatch per activity: Studying, Homework, Reading…
 • Laps: double-click a stopwatch to open its big window. Type or choose a name in 'Current lap'
   and press Lap (or L / Enter). Report = bar or pie chart of the laps grouped by name.

DESKTOP WIDGETS
 • Select an item → Float Widget (Ctrl+W). Drag it anywhere; it snaps to edges and other widgets.
 • Resize it by dragging the bottom-right corner (or any right / bottom edge).
 • Hover to see ▶ / lap / reset buttons, the brush (style) and × (close).
 • Widget Style (ribbon, brush button, or right-click) lets you change:
     - layout: Digital with DAY / HOUR / MIN / SEC labels, clock Dial, or Compact bar
     - title text (e.g. ""Good morning my dear""), fonts for the title and the digits
     - text colours, background colour / gradient / picture, colour themes
     - date and finish time below the timer (12-hour AM/PM or 24-hour), progress bar,
       opacity, always on top, lock position
   Apply to one widget, to all widgets, or save it as the default for new widgets.

BOARD (Ctrl+B)
 • Shows running timers (or all, or a group) as big coloured tiles - press F11 for fullscreen,
   ideal for a classroom projector. Double-click a tile to start / pause, Esc to leave fullscreen.

STATISTICS
 • Every time a timer or stopwatch runs, the time is recorded under its name.
 • Lap titles count too: Show ""Timers + stopwatches + lap names"" puts the time of each titled lap
   (e.g. Math, Physics from the Studying stopwatch) next to your timers and other stopwatches -
   nothing is counted twice. Stopwatch time without a lap title appears as ""(no lap title)"".
 • The lap that is running right now is included live.
 • Totals tab = total time per name (laps with the same title in different stopwatches are added up).
   Sessions tab = every single session. Double-click a total to see only its sessions.
 • Choose a period, group by name / group / type / day, bar or pie chart, export to CSV, save as PNG.

BACKUP & RESTORE
 • Settings → Backup → ""Back up now…"" saves everything you made (timers, stopwatches, laps,
   statistics history, groups, widget styles, settings) into one .stcbackup file.
 • Automatic backups are ON by default (every day) and go to Documents\Student Timer Clock Backups.
   Choose how often, how many to keep, and the folder (e.g. OneDrive / Google Drive / USB) in Settings.
 • ""Restore from backup…"" (Settings or File menu) brings it all back - on this PC or a new one.
   Your current data is backed up first, so a restore can always be undone.
 • If the saved data is ever damaged, the app offers to restore your latest backup at startup.

SECURITY & TAMPER PROTECTION
 • The program is digitally signed. At start-up and every few minutes it checks its own signature and
   SHA-256 fingerprint and the files in its folder. If anything was changed or foreign code was injected,
   it stops and tells you - reinstall from the official installer. Help → Security shows the details.
 • Everything you type is validated. ""When done"" actions that arrive in a backup, or in a data file that was
   changed outside the app, are blocked (⚠ BLOCKED) until you confirm them.

KEYBOARD
 Ctrl+N  new        F2 / Enter  edit       Del  delete      Ctrl+D  copy
 Ctrl+Z  undo       Space  start/pause     L  lap           Ctrl+W  widget
 Ctrl+B  board      Ctrl+F  search         Ctrl+1/2/3  switch page

START WITH WINDOWS
 • On by default, so your timers are ready when you sign in. Turn it off in File → Settings.
";
}

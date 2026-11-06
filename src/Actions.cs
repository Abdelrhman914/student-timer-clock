using System.Diagnostics;
using System.Runtime.InteropServices;

namespace StudentTimerClock;

/// <summary>Executes the "When done" actions of a timer, one after another.</summary>
public static class ActionRunner
{
    [DllImport("user32.dll")] static extern bool LockWorkStation();
    [DllImport("user32.dll")] static extern bool ExitWindowsEx(uint flags, uint reason);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    public static readonly HashSet<string> PowerActions = new() { "LogOff", "Restart", "Sleep", "Hibernate", "ShutDown" };

    /// <summary>Next timer in the same group (list order) - used by "Start next timer".</summary>
    public static TimerItem NextTimer(TimerItem src)
    {
        var list = AppState.Data.Timers.Where(t => t.Group == src.Group).ToList();
        int i = list.IndexOf(src);
        return i >= 0 && i + 1 < list.Count ? list[i + 1] : null;
    }

    public static string WhenDoneText(TimerItem t)
    {
        if (t.Actions == null || t.Actions.Count == 0) return t.AutoRestart ? "Restart timer" : "Stop timer";
        var first = t.Actions[0].Describe();
        return t.Actions.Count > 1 ? $"{first} (+{t.Actions.Count - 1})" : first;
    }

    public static async void RunForFinished(TimerItem t, bool whileAway)
    {
        if (t.Actions == null || t.Actions.Count == 0) return;
        if (whileAway) return; // never shut down / start programs because of something that happened while the app was closed
        await RunList(t, t.Actions.ToList(), false);
    }

    public static async Task RunList(TimerItem src, IList<TimerAction> list, bool test)
    {
        foreach (var a in list)
        {
            try
            {
                if (!await Run(src, a, test)) break;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"The action \"{a.Describe()}\" of \"{src.Name}\" failed:\n\n{ex.Message}", "Student Timer Clock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    /// <returns>false to stop running the remaining actions.</returns>
    public static async Task<bool> Run(TimerItem src, TimerAction a, bool test)
    {
        // ---- security gate: nothing reaches Process.Start / SendKeys / mailto without passing validation
        if (Security.IntegrityGuard.Compromised) return false;
        if (a.Quarantined)
        {
            Security.SecurityLog.Write($"Blocked quarantined action \"{a.Describe()}\" of \"{src.Name}\"");
            if (test) MessageBox.Show("This action is blocked because it came from a backup or a data file that was changed outside the app.\n\nOpen it with Edit and press OK to confirm that you trust it.", "Blocked action", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return true;
        }
        if (!Security.InputValidator.ValidateAction(a, out var invalid))
        {
            Security.SecurityLog.Write($"Rejected invalid action \"{a.Describe()}\" of \"{src.Name}\": {invalid}");
            MessageBox.Show($"The action \"{a.Describe()}\" of \"{src.Name}\" was not run:\n\n{invalid}", "Student Timer Clock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return true;
        }
        string what = TimerAction.Types.FirstOrDefault(x => x.id == a.Type).text ?? a.Type;
        if (PowerActions.Contains(a.Type))
        {
            if (test)
            {
                await ConfirmCountdownForm.Ask($"TEST: {what}", "This is only a test - nothing will happen when the countdown ends.", 10);
                return true;
            }
            if (a.Confirm && !await ConfirmCountdownForm.Ask(what, $"\"{src.Name}\" has finished. The computer will {what.ToLower()} when the countdown ends.", 30))
                return false;
            AppState.Save();
        }

        switch (a.Type)
        {
            case "StartNextTimer":
            case "StartTimer":
            {
                var next = a.Type == "StartNextTimer" ? NextTimer(src) : AppState.Data.Timers.FirstOrDefault(x => x.Id == a.TargetTimerId);
                if (next == null || next == src)
                {
                    if (test) MessageBox.Show(a.Type == "StartNextTimer" ? $"\"{src.Name}\" is the last timer in group \"{src.Group}\" - there is no next timer to start." : "No timer selected.", "Test");
                    return true;
                }
                if (test) { MessageBox.Show($"When \"{src.Name}\" finishes, \"{next.Name}\" will start" + (a.MoveWidget ? " and the desktop widget moves to it." : "."), "Test"); return true; }
                if (src.Finished && !src.ShowPopup) Program.Main?.DismissTimer(src);
                AppState.Reset(next);
                AppState.Start(next);
                if (a.MoveWidget) Program.Main?.HandOverWidget(src, next);
                break;
            }
            case "RunProgram":
            {
                if (string.IsNullOrWhiteSpace(a.Path)) return true;
                if (a.SwitchToExisting)
                {
                    var name = Path.GetFileNameWithoutExtension(a.Path);
                    var p = Process.GetProcessesByName(name).FirstOrDefault(x => x.MainWindowHandle != IntPtr.Zero);
                    if (p != null)
                    {
                        if (IsIconic(p.MainWindowHandle)) ShowWindow(p.MainWindowHandle, 9);
                        SetForegroundWindow(p.MainWindowHandle);
                        break;
                    }
                }
                Security.InputValidator.IsSafeLocalPath(a.Path, out var exe, out _);
                var psi = new ProcessStartInfo(exe, a.Args ?? "") { UseShellExecute = true };
                if (Security.InputValidator.IsSafeFolder(a.Folder, out var wd, out _) && wd != null) psi.WorkingDirectory = wd;
                else psi.WorkingDirectory = Path.GetDirectoryName(exe);
                psi.WindowStyle = a.WindowStyle switch
                {
                    "Minimized" => ProcessWindowStyle.Minimized,
                    "Maximized" => ProcessWindowStyle.Maximized,
                    "Hidden" => ProcessWindowStyle.Hidden,
                    _ => ProcessWindowStyle.Normal
                };
                Process.Start(psi);
                break;
            }
            case "OpenUrl":
                if (Security.InputValidator.IsSafeUrlOrDocument(a.Url, out var target, out _))
                    Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                break;
            case "SendEmail":
            {
                Security.InputValidator.IsSafeEmailList(a.EmailTo, out var to, out _);
                var mail = $"mailto:{to}?subject={Uri.EscapeDataString(Expand(a.Subject, src))}&body={Uri.EscapeDataString(Expand(a.Body, src))}";
                Process.Start(new ProcessStartInfo(mail) { UseShellExecute = true });
                break;
            }
            case "Lock": LockWorkStation(); break;
            case "LogOff": ExitWindowsEx(0, 0); break;
            case "Restart": Shell("shutdown", "/r /t 0"); break;
            case "ShutDown": Shell("shutdown", "/s /t 0"); break;
            case "Sleep": Application.SetSuspendState(PowerState.Suspend, false, false); break;
            case "Hibernate": Application.SetSuspendState(PowerState.Hibernate, false, false); break;
            case "MonitorOff":
                await Task.Delay(500); // let the mouse settle so it doesn't wake immediately
                PostMessage((IntPtr)0xFFFF, 0x0112, (IntPtr)0xF170, (IntPtr)2);
                break;
            case "ScreenSaver": PostMessage(GetDesktopWindow(), 0x0112, (IntPtr)0xF140, IntPtr.Zero); break;
            case "EndProcess":
            {
                if (!Security.InputValidator.IsSafeProcessName(a.ProcessName, out var name, out _)) break;
                int n = 0;
                foreach (var p in Process.GetProcessesByName(name)) { try { p.Kill(); n++; } catch { } }
                if (test) MessageBox.Show($"Ended {n} process(es) named \"{name}\".", "Test");
                break;
            }
            case "PressKeys":
                if (!string.IsNullOrEmpty(a.Keys))
                {
                    if (test) await Task.Delay(2000); // time to switch to the target window
                    SendKeys.SendWait(a.Keys);
                }
                break;
            case "MediaPause":
                keybd_event(0xB3, 0, 0, UIntPtr.Zero);
                keybd_event(0xB3, 0, 2, UIntPtr.Zero);
                break;
            case "Pause":
                await Task.Delay(Math.Clamp(a.Seconds, 0, 86400) * 1000);
                break;
        }
        return true;
    }

    static string Expand(string s, TimerItem t) => Security.InputValidator.CleanText((s ?? "").Replace("{name}", t.Name).Replace("{time}", Fmt.Clock(DateTime.Now)), Security.InputValidator.MaxBody, true);

    static void Shell(string file, string args) =>
        // always the real System32 binary - never resolved through the search path (no planting next to the app)
        Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, file + ".exe"), args) { CreateNoWindow = true, UseShellExecute = false });
}

/// <summary>Big always-on-top countdown that lets the user cancel a shutdown/restart/etc.</summary>
public class ConfirmCountdownForm : Form
{
    readonly TaskCompletionSource<bool> _tcs = new();
    readonly System.Windows.Forms.Timer _t = new() { Interval = 200 };
    readonly DateTime _end;
    readonly int _secs;
    readonly Label _count = new() { Dock = DockStyle.Top, Height = Theme.S(70), TextAlign = ContentAlignment.MiddleCenter, Font = Theme.UI(28f, FontStyle.Bold), ForeColor = Theme.Red };
    readonly ProgressBar _bar = new() { Dock = DockStyle.Top, Height = Theme.S(10) };
    bool _done;

    public static Task<bool> Ask(string title, string message, int seconds)
    {
        var f = new ConfirmCountdownForm(title, message, seconds);
        f.Show();
        f.Activate();
        return f._tcs.Task;
    }

    ConfirmCountdownForm(string title, string message, int seconds)
    {
        _secs = seconds; _end = DateTime.UtcNow.AddSeconds(seconds);
        Text = "Student Timer Clock - " + title;
        Icon = Program.AppIcon;
        Font = Theme.Normal;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(Theme.S(440), Theme.S(250));
        BackColor = Color.White;
        var head = new Label { Text = title, Dock = DockStyle.Top, Height = Theme.S(40), TextAlign = ContentAlignment.MiddleCenter, Font = Theme.UI(15f, FontStyle.Bold) };
        var msg = new Label { Text = message, Dock = DockStyle.Top, Height = Theme.S(50), TextAlign = ContentAlignment.MiddleCenter, Padding = new Padding(Theme.S(12), 0, Theme.S(12), 0) };
        _bar.Maximum = seconds * 10;
        var btns = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Theme.S(48), FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(Theme.S(8)), BackColor = Theme.HeaderBack };
        var cancel = new Button { Text = "Cancel", Width = Theme.S(120), Height = Theme.S(32), Font = Theme.Bold };
        var now = new Button { Text = "Do it now", Width = Theme.S(100), Height = Theme.S(32) };
        cancel.Click += (s, e) => Finish(false);
        now.Click += (s, e) => Finish(true);
        btns.Controls.Add(cancel); btns.Controls.Add(now);
        Controls.Add(_count); Controls.Add(_bar); Controls.Add(msg); Controls.Add(head); Controls.Add(btns);
        CancelButton = cancel;
        _t.Tick += (s, e) => UpdateCount();
        _t.Start();
        UpdateCount();
        Sound.Play("Chime", "", false, AppState.Settings.SoundVolume);
    }

    void UpdateCount()
    {
        double left = (_end - DateTime.UtcNow).TotalSeconds;
        if (left <= 0) { Finish(true); return; }
        _count.Text = Math.Ceiling(left).ToString("0") + " s";
        _bar.Value = Math.Clamp((int)((_secs - left) * 10), 0, _bar.Maximum);
    }

    void Finish(bool go)
    {
        if (_done) return;
        _done = true; _t.Stop();
        _tcs.TrySetResult(go);
        Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (!_done) { _done = true; _t.Stop(); _tcs.TrySetResult(false); }
        base.OnFormClosed(e);
    }
}

/// <summary>The "Action" dialog: list of action types on the left, parameters on the right.</summary>
public class ActionForm : Form
{
    public TimerAction Action { get; private set; }
    readonly TimerItem _owner;
    readonly ListBox _list = new() { Dock = DockStyle.Left, DrawMode = DrawMode.OwnerDrawFixed, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle };
    readonly GroupBox _box = new() { Dock = DockStyle.Fill, Padding = new Padding(Theme.S(12)) };
    readonly Panel _host = new() { Dock = DockStyle.Fill, AutoScroll = true };

    public ActionForm(TimerItem owner, TimerAction action)
    {
        _owner = owner;
        Action = action?.Clone() ?? new TimerAction { Type = "StartNextTimer" };
        Text = "Action";
        Icon = Program.AppIcon;
        Font = Theme.Normal;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(Theme.S(740), Theme.S(520));
        BackColor = Color.White;

        _list.Width = Theme.S(210);
        _list.ItemHeight = Theme.S(25);
        foreach (var t in TimerAction.Types) _list.Items.Add(t);
        _list.DrawItem += (s, e) =>
        {
            if (e.Index < 0) return;
            var (id, text, icon) = ((string, string, string))_list.Items[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Theme.AccentLight : Color.White)) e.Graphics.FillRectangle(b, e.Bounds);
            if (sel) using (var p = new Pen(Theme.Accent)) e.Graphics.DrawRectangle(p, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
            int isz = Theme.S(20);
            e.Graphics.DrawImage(Icons.Get(icon, isz), e.Bounds.X + Theme.S(6), e.Bounds.Y + (e.Bounds.Height - isz) / 2);
            TextRenderer.DrawText(e.Graphics, text, Theme.Normal, new Rectangle(e.Bounds.X + Theme.S(32), e.Bounds.Y, e.Bounds.Width - Theme.S(34), e.Bounds.Height), Color.Black, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        };
        _list.SelectedIndexChanged += (s, e) =>
        {
            if (_list.SelectedIndex < 0) return;
            Action.Type = TimerAction.Types[_list.SelectedIndex].id;
            BuildParams();
        };

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(50), BackColor = Theme.HeaderBack };
        var test = new Button { Text = "Test", Width = Theme.S(90), Height = Theme.S(30), Location = new Point(Theme.S(10), Theme.S(10)) };
        var ok = new Button { Text = "OK", Width = Theme.S(90), Height = Theme.S(30) };
        var cancel = new Button { Text = "Cancel", Width = Theme.S(90), Height = Theme.S(30), DialogResult = DialogResult.Cancel };
        var right = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, Width = Theme.S(220), Padding = new Padding(0, Theme.S(7), Theme.S(6), 0) };
        right.Controls.Add(cancel); right.Controls.Add(ok);
        ok.Click += (s, e) =>
        {
            if (!Security.InputValidator.ValidateAction(Action, out var err))
            {
                MessageBox.Show(this, err, "Please check this action", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (Action.Quarantined)
            {
                if (MessageBox.Show(this, "This action was blocked because it came from a backup or from a data file that was changed outside the app.\n\n" +
                        Action.Describe().Replace("⚠ BLOCKED - ", "") + "\n\nDo you trust it and want to allow it to run?", "Allow this action?",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                Action.Quarantined = false;
                Security.SecurityLog.Write("User approved previously quarantined action: " + Action.Describe());
            }
            DialogResult = DialogResult.OK;
        };
        test.Click += async (s, e) =>
        {
            test.Enabled = false;
            try { await ActionRunner.Run(_owner ?? new TimerItem { Name = "Test timer" }, Action, true); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Test failed", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            finally { if (!IsDisposed) test.Enabled = true; }
        };
        bottom.Controls.AddRange(new Control[] { test, right });
        AcceptButton = ok; CancelButton = cancel;

        var mid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Theme.S(10)) };
        _box.Controls.Add(_host);
        mid.Controls.Add(_box);
        var split = new Panel { Dock = DockStyle.Left, Width = Theme.S(4) };
        Controls.Add(mid);
        Controls.Add(split);
        Controls.Add(_list);
        Controls.Add(bottom);
        _list.Margin = new Padding(Theme.S(10));
        Padding = new Padding(Theme.S(10), Theme.S(10), 0, 0);

        int idx = Array.FindIndex(TimerAction.Types, x => x.id == Action.Type);
        _list.SelectedIndex = idx < 0 ? 0 : idx;
    }

    // ---------------------------------------------------------------- parameter panel
    int _y;
    void Add(Control c, int height = 0, int indent = 0)
    {
        c.Location = new Point(indent, _y);
        if (c.Width < 10 || c is TextBox || c is ComboBox) { if (!(c is CheckBox) && !(c is Label { AutoSize: true })) c.Width = _host.ClientSize.Width - indent - Theme.S(4); }
        c.Anchor = AnchorStyles.Left | AnchorStyles.Top | (c is TextBox || c is ComboBox || c is Label { AutoSize: false } ? AnchorStyles.Right : 0);
        _host.Controls.Add(c);
        _y += (height > 0 ? height : c.Height) + Theme.S(4);
    }
    void Caption(string t) => Add(new Label { Text = t, AutoSize = true }, Theme.S(16));
    void Note(string t, int h = 36) => Add(new Label { Text = t, AutoSize = false, Width = _host.ClientSize.Width - Theme.S(4), Height = Theme.S(h), ForeColor = Theme.TextDim });
    void Gap(int px = 8) => _y += Theme.S(px);

    TextBox TextIn(string value, Action<string> set, bool multi = false)
    {
        var t = new TextBox { Text = value ?? "", Multiline = multi, ScrollBars = multi ? ScrollBars.Vertical : ScrollBars.None };
        if (multi) t.Height = Theme.S(90);
        t.TextChanged += (s, e) => set(t.Text);
        return t;
    }

    void PathRow(string value, Action<string> set, bool folder, string filter = "Programs|*.exe;*.bat;*.cmd;*.lnk|All files|*.*")
    {
        var t = TextIn(value, set);
        var b = new Button { Text = "…", Width = Theme.S(32), Height = t.Height + 2, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        int w = _host.ClientSize.Width - Theme.S(4);
        t.Location = new Point(0, _y); t.Width = w - b.Width - Theme.S(4); t.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        b.Location = new Point(w - b.Width, _y - 1);
        b.Click += (s, e) =>
        {
            if (folder)
            {
                using var d = new FolderBrowserDialog();
                if (d.ShowDialog(this) == DialogResult.OK) t.Text = d.SelectedPath;
            }
            else
            {
                using var d = new OpenFileDialog { Filter = filter };
                if (d.ShowDialog(this) == DialogResult.OK) t.Text = d.FileName;
            }
        };
        _host.Controls.Add(t); _host.Controls.Add(b);
        _y += t.Height + Theme.S(6);
    }

    CheckBox Check(string text, bool value, Action<bool> set)
    {
        var c = new CheckBox { Text = text, Checked = value, AutoSize = true };
        c.CheckedChanged += (s, e) => set(c.Checked);
        return c;
    }

    void BuildParams()
    {
        _host.SuspendLayout();
        foreach (Control c in _host.Controls.Cast<Control>().ToList()) c.Dispose();
        _host.Controls.Clear();
        _y = Theme.S(4);
        var a = Action;
        var info = TimerAction.Types.First(x => x.id == a.Type);
        _box.Text = info.text;

        switch (a.Type)
        {
            case "StartNextTimer":
            {
                var next = _owner == null ? null : ActionRunner.NextTimer(_owner);
                Note("When this timer finishes, the next timer in the same group starts automatically - perfect for lesson segments or Pomodoro study / break cycles.", 52);
                Gap();
                Add(new Label { Text = next != null ? $"Next timer:  {next.Name}" : "Next timer:  (none yet - add another timer below this one in the same group)", AutoSize = true, Font = Theme.Bold });
                Gap();
                Add(Check("Move the desktop widget to the next timer", a.MoveWidget, v => a.MoveWidget = v));
                break;
            }
            case "StartTimer":
            {
                Caption("Timer to start:");
                var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
                var timers = AppState.Data.Timers.Where(t => t != _owner).ToList();
                foreach (var t in timers) cb.Items.Add($"{t.Name}   ({t.Group})");
                cb.SelectedIndex = timers.FindIndex(t => t.Id == a.TargetTimerId);
                cb.SelectedIndexChanged += (s, e) => a.TargetTimerId = cb.SelectedIndex >= 0 ? timers[cb.SelectedIndex].Id : "";
                Add(cb);
                Gap();
                Add(Check("Move the desktop widget to that timer", a.MoveWidget, v => a.MoveWidget = v));
                break;
            }
            case "RunProgram":
            {
                Caption("Program:");
                PathRow(a.Path, v => a.Path = v, false);
                Caption("Parameters:");
                Add(TextIn(a.Args, v => a.Args = v));
                Caption("Start in folder:");
                PathRow(a.Folder, v => a.Folder = v, true);
                Caption("Window:");
                var ws = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(200) };
                ws.Items.AddRange(new object[] { "Normal Window", "Minimized", "Maximized", "Hidden" });
                ws.SelectedItem = ws.Items.Contains(a.WindowStyle) ? a.WindowStyle : "Normal Window";
                ws.SelectedIndexChanged += (s, e) => a.WindowStyle = (string)ws.SelectedItem;
                Add(ws);
                Gap();
                Add(Check("If the program is already running, just switch to it", a.SwitchToExisting, v => a.SwitchToExisting = v));
                break;
            }
            case "OpenUrl":
                Caption("Web address, document or folder:");
                PathRow(a.Url, v => a.Url = v, false, "All files|*.*");
                Note("Examples:  https://classroom.google.com   or   C:\\Users\\me\\Documents\\notes.docx");
                break;
            case "SendEmail":
                Caption("To:");
                Add(TextIn(a.EmailTo, v => a.EmailTo = v));
                Caption("Subject:");
                Add(TextIn(a.Subject, v => a.Subject = v));
                Caption("Message:");
                Add(TextIn(a.Body, v => a.Body = v, true));
                Note("Opens your email program with the message ready to send. {name} = timer name, {time} = current time.");
                break;
            case "Lock": Note("Locks this computer (like pressing Windows + L)."); break;
            case "LogOff":
            case "Restart":
            case "Sleep":
            case "Hibernate":
            case "ShutDown":
                Note(a.Type switch
                {
                    "LogOff" => "Signs you out of Windows. Unsaved work in other programs may be lost.",
                    "Restart" => "Restarts the computer. Unsaved work in other programs may be lost.",
                    "Sleep" => "Puts the computer to sleep.",
                    "Hibernate" => "Hibernates the computer (if hibernation is enabled in Windows).",
                    _ => "Shuts down the computer. Unsaved work in other programs may be lost."
                }, 40);
                Gap();
                Add(Check("Ask first - show a 30 second countdown that can be cancelled", a.Confirm, v => a.Confirm = v));
                break;
            case "MonitorOff": Note("Turns the screen off. Move the mouse or press a key to turn it back on."); break;
            case "ScreenSaver": Note("Starts the Windows screen saver that is selected in your display settings."); break;
            case "MediaPause": Note("Presses the Play / Pause media key - pauses (or resumes) music and videos."); break;
            case "EndProcess":
            {
                Caption("Process name:");
                var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Text = a.ProcessName };
                try { cb.Items.AddRange(Process.GetProcesses().Select(p => p.ProcessName + ".exe").Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).Cast<object>().ToArray()); } catch { }
                cb.TextChanged += (s, e) => a.ProcessName = cb.Text;
                Add(cb);
                Note("Closes every running program with this name, e.g. game.exe  (great for ending game time!).");
                break;
            }
            case "PressKeys":
                Caption("Keys to press:");
                Add(TextIn(a.Keys, v => a.Keys = v));
                Note("Text is typed as-is. Special keys:  ^ = Ctrl,  % = Alt,  + = Shift,  {ENTER}  {TAB}  {ESC}  {F5}\nExamples:  ^s  (Ctrl+S)    %{F4}  (Alt+F4)    {F5}", 60);
                break;
            case "Pause":
            {
                Caption("Wait (seconds):");
                var n = new NumericUpDown { Minimum = 0, Maximum = 86400, Value = Math.Clamp(a.Seconds, 0, 86400), Width = Theme.S(100) };
                n.ValueChanged += (s, e) => a.Seconds = (int)n.Value;
                Add(n);
                Note("Waits before the next action in the list is run.");
                break;
            }
        }
        _host.ResumeLayout();
    }
}

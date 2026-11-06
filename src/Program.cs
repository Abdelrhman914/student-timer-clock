namespace StudentTimerClock;

static class Program
{
    public static MainForm Main;
    public static Icon AppIcon;

    static readonly HashSet<string> KnownArgs = new(StringComparer.OrdinalIgnoreCase) { "--autostart", "--minimized", "--restart" };

    internal static void Run(string[] args)
    {
        // 1. tamper protection runs before anything else (no window, no data loaded yet)
        Security.IntegrityGuard.Startup(args);

        // 2. command line: only known switches are accepted, everything else is ignored and logged
        var unknown = args.Where(a => !KnownArgs.Contains(a)).ToList();
        if (unknown.Count > 0) Security.SecurityLog.Write("Ignored unknown command-line arguments: " + string.Join(" ", unknown.Select(u => Security.InputValidator.CleanText(u, 100))));
        args = args.Where(a => KnownArgs.Contains(a)).Select(a => a.ToLowerInvariant()).ToArray();

        using var mutex = new Mutex(false, @"Local\StudentTimerClock_SingleInstance");
        bool first;
        // "--restart" (after restoring a backup): wait until the old instance has closed
        try { first = mutex.WaitOne(args.Contains("--restart") ? 20000 : 0); }
        catch (AbandonedMutexException) { first = true; }
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\StudentTimerClock_Show");
        if (!first)
        {
            showEvent.Set(); // ask the running instance to show itself
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (s, e) => ShowError(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowError(e.ExceptionObject as Exception);

        Theme.Init();
        try
        {
            using var st = typeof(Program).Assembly.GetManifestResourceStream("app.ico");
            AppIcon = st != null ? new Icon(st) : SystemIcons.Application;
        }
        catch { AppIcon = SystemIcons.Application; }

        AppState.Load();
        Autostart.OnStartup();
        bool hidden = args.Contains("--autostart") && AppState.Settings.StartMinimized || args.Contains("--minimized");
        Main = new MainForm(hidden);

        var waiter = new Thread(() =>
        {
            while (true)
            {
                showEvent.WaitOne();
                try { Main?.ShowMain(); } catch { }
            }
        }) { IsBackground = true };
        waiter.Start();

        Application.Run(Main);
        AppState.Save();
        try { mutex.ReleaseMutex(); } catch { }
        if (Main?.RestartRequested == true)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath, "--restart") { UseShellExecute = false }); } catch { }
        }
        GC.KeepAlive(mutex);
    }


    static void ShowError(Exception ex)
    {
        try
        {
            File.AppendAllText(Path.Combine(AppState.DataDir, "error.log"), $"[{DateTime.Now}] {ex}\n\n");
            MessageBox.Show("Something went wrong:\n\n" + ex?.Message + "\n\nDetails were written to error.log in the data folder.", "Student Timer Clock", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch { }
    }
}

static class Entry
{
    [STAThread]
    static void Main(string[] args) => Program.Run(args);
}

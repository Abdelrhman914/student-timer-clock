namespace StudentTimerClock.Security;

/// <summary>Append-only security event log (%APPDATA%\StudentTimerClock\security.log, rotated at 1 MB).</summary>
public static class SecurityLog
{
    static readonly object _lock = new();
    public static string FilePath => Path.Combine(AppState.DataDir, "security.log");

    public static void Write(string message)
    {
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(AppState.DataDir);
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > 1024 * 1024)
                {
                    File.Copy(FilePath, FilePath + ".1", true);
                    File.Delete(FilePath);
                }
                File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {InputValidator.CleanText(message, 4000, true)}\r\n");
            }
        }
        catch { }
    }
}

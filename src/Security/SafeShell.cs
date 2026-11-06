namespace StudentTimerClock.Security;

/// <summary>Starts Windows shell tools by absolute path with validated, quoted arguments.</summary>
public static class SafeShell
{
    public static string Explorer => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    public static void OpenFolder(string folder)
    {
        try
        {
            if (InputValidator.IsSafeLocalPath(folder, out var full, out _, mustExist: false) && Directory.Exists(full))
                System.Diagnostics.Process.Start(Explorer, "\"" + full + "\"");
        }
        catch { }
    }
}

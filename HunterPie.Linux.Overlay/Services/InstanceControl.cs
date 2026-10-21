using System.Runtime.InteropServices;

namespace HunterPie.Linux.Overlay.Services;

/// <summary>
/// Lets a second invocation (<c>--toggle-edit</c>) reach the running overlay: the overlay records its pid in
/// $XDG_RUNTIME_DIR and the second process sends it SIGUSR1, the same signal as <c>kill -USR1</c>.
/// </summary>
internal static class InstanceControl
{
    private const int SigUsr1 = 10;

    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int signal);

    private static string PidFile =>
        Path.Combine(
            Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } runtime ? runtime : Path.GetTempPath(),
            "hunterpie-overlay.pid"
        );

    public static void Register()
    {
        try
        {
            File.WriteAllText(PidFile, Environment.ProcessId.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not write {PidFile}, --toggle-edit will not work: {ex.Message}");
        }
    }

    public static void Unregister()
    {
        try
        {
            if (File.Exists(PidFile) && File.ReadAllText(PidFile).Trim() == Environment.ProcessId.ToString())
                File.Delete(PidFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Asks the running overlay to toggle layout edit mode. Returns a process exit code</summary>
    public static int ToggleEdit()
    {
        if (!File.Exists(PidFile) || !int.TryParse(File.ReadAllText(PidFile).Trim(), out int pid) || !IsOverlay(pid))
        {
            Console.Error.WriteLine("No running HunterPie overlay found. Start it first with ./HunterPie.Linux.Overlay");
            return 1;
        }

        if (kill(pid, SigUsr1) != 0)
        {
            Console.Error.WriteLine($"Could not signal the overlay (pid {pid}): errno {Marshal.GetLastWin32Error()}");
            return 1;
        }

        Console.WriteLine($"Toggled layout edit mode on the overlay (pid {pid}).");
        return 0;
    }

    // A stale pid file may point at an unrelated process that reused the number
    private static bool IsOverlay(int pid)
    {
        try
        {
            return File.ReadAllText($"/proc/{pid}/cmdline").Contains("HunterPie.Linux.Overlay");
        }
        catch (IOException)
        {
            return false;
        }
    }
}

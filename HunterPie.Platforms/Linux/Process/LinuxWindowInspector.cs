using SystemProcess = System.Diagnostics.Process;
using System.Diagnostics;

namespace HunterPie.Platforms.Linux.Process;

/// <summary>
/// Best effort window information through xdotool. Proton/Wine games run through XWayland on Wayland sessions,
/// so X11 tooling can still see them. Everything here degrades to "unknown" when xdotool is not installed.
/// </summary>
internal static class LinuxWindowInspector
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMilliseconds(750);

    private static readonly Lazy<bool> IsXdotoolAvailable = new(() => Run("xdotool", "version") is not null);

    public static bool IsAvailable => IsXdotoolAvailable.Value;

    public static string GetWindowTitle(int pid)
    {
        if (!IsAvailable)
            return string.Empty;

        string? windows = Run("xdotool", "search", "--onlyvisible", "--pid", pid.ToString());

        if (string.IsNullOrWhiteSpace(windows))
            return string.Empty;

        foreach (string window in windows.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string? title = Run("xdotool", "getwindowname", window)?.Trim();

            if (!string.IsNullOrEmpty(title))
                return title;
        }

        return string.Empty;
    }

    /// <summary>
    /// Pid owning the currently focused window, null when it can't be determined
    /// </summary>
    public static int? GetActiveWindowPid()
    {
        if (!IsAvailable)
            return null;

        string? output = Run("xdotool", "getactivewindow", "getwindowpid")?.Trim();

        return int.TryParse(output, out int pid) ? pid : null;
    }

    private static string? Run(string fileName, params string[] arguments)
    {
        try
        {
            var info = new ProcessStartInfo(fileName)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (string argument in arguments)
                info.ArgumentList.Add(argument);

            using SystemProcess? process = SystemProcess.Start(info);

            if (process is null)
                return null;

            string output = process.StandardOutput.ReadToEnd();

            if (!process.WaitForExit(ProcessTimeout))
            {
                process.Kill();
                return null;
            }

            return process.ExitCode == 0 ? output : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}

using System.Text;

namespace HunterPie.Linux.Logging;

/// <summary>Where log files go and how they are named and pruned</summary>
internal static class LogLocation
{
    private const string SessionPrefix = "session-";
    private const string HuntPrefix = "hunt-";

    /// <summary>$HUNTERPIE_LOG_DIR, else $XDG_STATE_HOME/HunterPie/logs, else ~/.local/state/HunterPie/logs</summary>
    public static string Directory
    {
        get
        {
            string? custom = Environment.GetEnvironmentVariable("HUNTERPIE_LOG_DIR");

            if (!string.IsNullOrWhiteSpace(custom) && Path.IsPathRooted(custom))
                return custom;

            string? xdg = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
            string root = !string.IsNullOrWhiteSpace(xdg) && Path.IsPathRooted(xdg)
                ? xdg
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state");

            return Path.Combine(root, "HunterPie", "logs");
        }
    }

    public static string SessionFile(string directory, DateTime start) =>
        Path.Combine(directory, $"{SessionPrefix}{start:yyyyMMdd-HHmmss}.log");

    public static string HuntFile(string directory, DateTime start, string questName) =>
        Path.Combine(directory, $"{HuntPrefix}{start:yyyyMMdd-HHmmss}-{Slug(questName)}.log");

    /// <summary>"Hunt: Quematrice!" becomes "hunt-quematrice"; empty or unnamed quests become "hunt"</summary>
    public static string Slug(string name)
    {
        var builder = new StringBuilder();

        foreach (char c in name.Trim().ToLowerInvariant())
        {
            if (builder.Length >= 40)
                break;

            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
                builder.Append(c);
            else if (builder.Length > 0 && builder[^1] != '-')
                builder.Append('-');
        }

        string slug = builder.ToString().Trim('-');

        return slug.Length == 0 ? "hunt" : slug;
    }

    /// <summary>Keeps the newest files of each kind. Only files this program names are ever touched.</summary>
    public static void Prune(string directory, int keepSessions = 20, int keepHunts = 100)
    {
        try
        {
            if (!System.IO.Directory.Exists(directory))
                return;

            Prune(directory, SessionPrefix, keepSessions);
            Prune(directory, HuntPrefix, keepHunts);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Old logs are only clutter, never worth failing over
        }
    }

    private static void Prune(string directory, string prefix, int keep)
    {
        // The timestamp is in the name, so name order is age order
        string[] files = System.IO.Directory.GetFiles(directory, $"{prefix}*.log");
        Array.Sort(files, StringComparer.Ordinal);

        for (int i = 0; i < files.Length - keep; i++)
            File.Delete(files[i]);
    }
}

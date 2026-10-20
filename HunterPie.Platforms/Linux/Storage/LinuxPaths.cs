namespace HunterPie.Platforms.Linux.Storage;

internal static class LinuxPaths
{
    /// <summary>
    /// $XDG_CONFIG_HOME/HunterPie, defaulting to ~/.config/HunterPie
    /// </summary>
    public static string ConfigDirectory
    {
        get
        {
            string? xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            string root = !string.IsNullOrWhiteSpace(xdg) && Path.IsPathRooted(xdg)
                ? xdg
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

            string directory = Path.Combine(root, "HunterPie");
            Directory.CreateDirectory(directory);

            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            return directory;
        }
    }

    /// <summary>
    /// Writes the file atomically and readable only by the current user, since it may hold keys or credentials
    /// </summary>
    public static void WritePrivateFile(string path, string content)
    {
        string temp = $"{path}.{Environment.ProcessId}.tmp";

        File.WriteAllText(temp, content);
        File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temp, path, overwrite: true);
    }
}

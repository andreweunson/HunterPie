using System.Globalization;

namespace HunterPie.Platforms.Linux.Process;

/// <summary>
/// Helpers for reading process information from /proc. Windows games run through Wine/Proton show up as regular
/// Linux processes, with their PE image mapped as a file in /proc/[pid]/maps.
/// </summary>
internal static class LinuxProcFs
{
    private const string PROC = "/proc";

    public readonly record struct Mapping(nint Start, nint End, string Path);

    public static IEnumerable<int> EnumeratePids()
    {
        foreach (string dir in Directory.EnumerateDirectories(PROC))
        {
            string name = System.IO.Path.GetFileName(dir);

            if (int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int pid))
                yield return pid;
        }
    }

    /// <summary>
    /// Reads the process command line, arguments are separated by NUL
    /// </summary>
    public static string[] ReadCommandLine(int pid)
    {
        try
        {
            string raw = File.ReadAllText($"{PROC}/{pid}/cmdline");

            return raw.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Finds the executable name of the (Windows) program running, removing the directory and extension.
    /// Wine usually reports a DOS path (Z:\home\user\Game.exe) as argv[0], but it can also be a Unix path.
    /// </summary>
    public static string? GetExecutableName(string[] commandLine)
    {
        if (commandLine.Length == 0)
            return null;

        string argv0 = commandLine[0];
        int separator = argv0.LastIndexOfAny(['/', '\\']);
        string file = separator >= 0 ? argv0[(separator + 1)..] : argv0;

        return file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? file[..^".exe".Length]
            : null;
    }

    public static IReadOnlyList<Mapping> ReadMaps(int pid)
    {
        var mappings = new List<Mapping>();

        try
        {
            foreach (string line in File.ReadLines($"{PROC}/{pid}/maps"))
            {
                if (TryParseMapLine(line, out Mapping mapping))
                    mappings.Add(mapping);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        return mappings;
    }

    /// <summary>
    /// Finds the lowest address that the file '[name].exe' is mapped at
    /// </summary>
    public static (nint BaseAddress, string Path)? FindImage(int pid, string name)
    {
        string suffix = $"/{name}.exe";

        Mapping[] matches = ReadMaps(pid)
            .Where(it => it.Path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length == 0)
            return null;

        Mapping first = matches.MinBy(it => it.Start);

        return (first.Start, first.Path);
    }

    // 7f1c4a000000-7f1c4a001000 r--p 00000000 08:01 1234   /path/to/file.exe
    internal static bool TryParseMapLine(string line, out Mapping mapping)
    {
        mapping = default;

        string[] parts = line.Split(' ', 6, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 6)
            return false;

        int dash = parts[0].IndexOf('-');

        if (dash < 0)
            return false;

        bool hasStart = long.TryParse(parts[0].AsSpan(0, dash), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long start);
        bool hasEnd = long.TryParse(parts[0].AsSpan(dash + 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long end);

        if (!hasStart || !hasEnd)
            return false;

        mapping = new Mapping((nint)start, (nint)end, parts[5].Trim());

        return true;
    }
}

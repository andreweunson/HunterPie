using HunterPie.Core.Client;
using HunterPie.Integrations.Datasources;
using HunterPie.Platforms.Linux.Pe;
using HunterPie.Platforms.Linux.Process;
using Microsoft.Win32.SafeHandles;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HunterPie.Linux.Services;

/// <summary>
/// --find-map: builds the address map for a Monster Hunter Wilds version that has none yet, by working out how far
/// the globals moved since the newest older map. See <see cref="MapProbe"/> for what that rests on.
/// The map is only written when one shift is clearly right, and never over an existing file.
/// </summary>
internal static partial class MapFinder
{
    private const string GameName = SupportedGameNames.MONSTER_HUNTER_WILDS;

    [GeneratedRegex(@"^[ \t]*Address[ \t]+(?<key>\S+)[ \t]+0x(?<value>[0-9A-Fa-f]+)", RegexOptions.Multiline)]
    private static partial Regex AddressLine();

    [GeneratedRegex(@"^[ \t]*Offset[ \t]+(?<key>\S+)[ \t]+(?<value>\S+)", RegexOptions.Multiline)]
    private static partial Regex OffsetLine();

    public static int Run()
    {
        Console.WriteLine("HunterPie Linux address map finder");
        Console.WriteLine();

        if (!TryFindGame(out int pid, out nint imageBase, out string version))
            return 1;

        string directory = ClientInfo.AddressPath;
        string target = Path.Combine(directory, $"{GameName}.{version}.map");

        if (File.Exists(target))
        {
            Console.WriteLine($"{Path.GetFileName(target)} already exists in {directory}, nothing to do.");
            return 0;
        }

        if (FindSourceMap(directory, version) is not { } source)
        {
            Console.Error.WriteLine($"No older {GameName} map in {directory} to work from.");
            return 1;
        }

        Console.WriteLine($"Game version {version}, working from {Path.GetFileName(source)}");

        string text = File.ReadAllText(source);
        Dictionary<string, long> addresses = ParseAddresses(text);
        Dictionary<string, int[]> offsets = ParseOffsets(text);

        using var memory = new ProcMemReader(pid);

        if (!TryReadImageSize(memory, imageBase, out long imageSize))
        {
            Console.Error.WriteLine("Could not read the game's PE header, is the game still running and its memory readable? (try --doctor)");
            return 1;
        }

        Console.WriteLine($"Image base 0x{imageBase:X}, size 0x{imageSize:X}. Trying shifts of 0x{MapProbe.Step:X} within +-0x{MapProbe.DefaultRange:X}...");

        ProbeResult result = MapProbe.Find(memory, imageBase, imageSize, addresses, offsets);

        Console.WriteLine();
        Console.WriteLine("Best candidates (score = 2 x pointers that look right + offset chains that reach readable memory):");

        foreach (ProbeCandidate candidate in result.Top)
        {
            Console.WriteLine($"  shift {FormatShift(candidate.Shift),-12} pointers {candidate.ValidSlots}/{MapProbe.RequiredAddresses.Count}  chains {candidate.ChainsPassed}  score {candidate.Score}");
        }

        Console.WriteLine();

        if (!result.Accepted || result.Best is not { } best)
        {
            Console.Error.WriteLine($"No map written: {result.Reason}.");
            Console.Error.WriteLine("This usually means the patch moved the globals by different amounts, so a shift of the old map is not enough.");
            Console.Error.WriteLine("Make sure you are logged in with a character (the world loaded) and run this again; if it still fails the map needs the real executable to be made.");
            return 1;
        }

        string newText = ApplyShift(text, best.Shift);
        bool hadBom = HasBom(source);

        File.WriteAllText(target, newText, new UTF8Encoding(hadBom));

        Console.WriteLine($"Wrote {target}");
        Console.WriteLine($"  shift {FormatShift(best.Shift)} applied to {addresses.Count} addresses; the {offsets.Count} offsets are copied unchanged.");
        Console.WriteLine("  Encryption::Round and Encryption::Key moved by the same amount but cannot be checked this way.");
        Console.WriteLine();
        Console.WriteLine("Restart HunterPie to use it, and check that the monster and party data look right.");
        Console.WriteLine("If they do, copy the file to HunterPie/Address/ in the source tree so it survives a rebuild and can be shared.");

        return 0;
    }

    internal static string ApplyShift(string mapText, long shift) =>
        AddressLine().Replace(mapText, match =>
        {
            long value = long.Parse(match.Groups["value"].ValueSpan, NumberStyles.HexNumber, CultureInfo.InvariantCulture) + shift;
            Group group = match.Groups["value"];
            string prefix = match.Value[..(group.Index - match.Index)];

            return $"{prefix}{value:X8}";
        });

    internal static Dictionary<string, long> ParseAddresses(string mapText)
    {
        var result = new Dictionary<string, long>();

        foreach (Match match in AddressLine().Matches(mapText))
            result[match.Groups["key"].Value] = long.Parse(match.Groups["value"].ValueSpan, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        return result;
    }

    internal static Dictionary<string, int[]> ParseOffsets(string mapText)
    {
        var result = new Dictionary<string, int[]>();

        foreach (Match match in OffsetLine().Matches(mapText))
        {
            string[] parts = match.Groups["value"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var chain = new List<int>(parts.Length);

            foreach (string part in parts)
            {
                string hex = part.Trim().StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? part.Trim()[2..] : part.Trim();

                if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int offset))
                    break;

                chain.Add(offset);
            }

            if (chain.Count == parts.Length)
                result[match.Groups["key"].Value] = [.. chain];
        }

        return result;
    }

    /// <summary>
    /// The newest map with a version below the game's, so the smallest possible jump is being measured
    /// </summary>
    internal static string? FindSourceMap(string directory, string gameVersion)
    {
        if (!Directory.Exists(directory) || !TryParseVersion(gameVersion, out Version? current))
            return null;

        string prefix = $"{GameName}.";

        return Directory.GetFiles(directory, $"{prefix}*.map")
            .Select(path => (Path: path, Ok: TryParseVersion(Path.GetFileNameWithoutExtension(path)[prefix.Length..], out Version? v), Version: v))
            .Where(it => it.Ok && it.Version! < current!)
            .OrderByDescending(it => it.Version)
            .Select(it => it.Path)
            .FirstOrDefault();
    }

    private static bool TryParseVersion(string text, out Version? version) => Version.TryParse(text, out version) && version is not null;

    private static bool HasBom(string path)
    {
        using FileStream file = File.OpenRead(path);
        Span<byte> head = stackalloc byte[3];

        return file.Read(head) == 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF;
    }

    private static string FormatShift(long shift) => shift < 0 ? $"-0x{-shift:X}" : $"+0x{shift:X}";

    // The "PE\0\0" header is at e_lfanew (0x3C); SizeOfImage sits 0x50 bytes after it in the optional header
    private static bool TryReadImageSize(IProbeMemory memory, nint imageBase, out long size)
    {
        size = 0;
        Span<byte> header = stackalloc byte[0x100];

        if (!memory.TryRead(imageBase, header) || header[0] != 'M' || header[1] != 'Z')
            return false;

        int pe = BinaryPrimitives.ReadInt32LittleEndian(header[0x3C..]);

        if (pe is < 0x40 or > 0xC0 || header[pe] != 'P' || header[pe + 1] != 'E')
            return false;

        size = BinaryPrimitives.ReadUInt32LittleEndian(header[(pe + 0x50)..]);

        return size > 0;
    }

    private static bool TryFindGame(out int pid, out nint imageBase, out string version)
    {
        pid = 0;
        imageBase = 0;
        version = string.Empty;

        foreach (int candidate in LinuxProcFs.EnumeratePids())
        {
            string[] commandLine = LinuxProcFs.ReadCommandLine(candidate);

            if (LinuxProcFs.GetExecutableName(commandLine) is not { } name
                || !name.Equals(GameName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (LinuxProcFs.FindImage(candidate, name) is not { } image)
                continue;

            string? fileVersion = PeVersionReader.ReadFileVersion(image.Path);

            if (fileVersion is null)
            {
                Console.Error.WriteLine($"Found {name} (pid {candidate}) but could not read its version.");
                return false;
            }

            pid = candidate;
            imageBase = image.BaseAddress;
            version = fileVersion;

            return true;
        }

        Console.Error.WriteLine($"{GameName} is not running. Start the game, load into the world with your character, then run this again.");

        return false;
    }

    /// <summary>
    /// Read-only view of another process' memory through /proc/[pid]/mem
    /// </summary>
    internal sealed class ProcMemReader : IProbeMemory, IDisposable
    {
        private readonly SafeFileHandle _handle;

        public ProcMemReader(int pid)
        {
            _handle = File.OpenHandle($"/proc/{pid}/mem", FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }

        public bool TryRead(nint address, Span<byte> buffer)
        {
            if (address <= 0)
                return false;

            try
            {
                return RandomAccess.Read(_handle, buffer, address) == buffer.Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return false;
            }
        }

        public void Dispose() => _handle.Dispose();
    }
}

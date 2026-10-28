using HunterPie.Core.Client;
using HunterPie.Integrations.Datasources;
using HunterPie.Linux.Logging;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Xml;

namespace HunterPie.Linux.Services;

/// <summary>
/// --dump-skills and --diff-skills: find where the game keeps a skill HunterPie does not track yet.
/// The scanner reads Wilds skills from a table of pointers (the map's Player::Abnormalities::Skills), one slot per
/// tracked skill, each pointing at a small struct with the timer at +0x10. The dump saves every slot and the start of
/// the struct it points at; the diff compares two dumps (skill off, skill on) and shows which slots changed. Reads only.
/// </summary>
internal static class SkillTableDump
{
    private const string GameName = SupportedGameNames.MONSTER_HUNTER_WILDS;
    private const string Header = "# HunterPie skill table dump";

    // 128 slots of 8 bytes; the tracked skills use the first 0x100 or so, and the rest is cheap to look at too
    private const int TableBytes = 0x400;
    private const int FullStructBytes = 0x80;
    private const int ShortStructBytes = 0x40;

    private const long MinPointer = 0x10000;
    private const long MaxPointer = 0x7FFFFFFFFFFF;

    private sealed record Slot(int Offset, bool IsPointer, byte[] Data);

    public static int Dump(string? label)
    {
        Console.WriteLine("HunterPie Linux skill table dump");
        Console.WriteLine();

        if (!MapFinder.TryFindGame(out int pid, out nint imageBase, out string version))
            return 1;

        string mapPath = Path.Combine(ClientInfo.AddressPath, $"{GameName}.{version}.map");

        if (!File.Exists(mapPath))
        {
            Console.Error.WriteLine($"There is no {Path.GetFileName(mapPath)} in {ClientInfo.AddressPath}. Run --find-map first.");
            return 1;
        }

        string text = File.ReadAllText(mapPath);
        Dictionary<string, long> addresses = MapFinder.ParseAddresses(text);
        Dictionary<string, int[]> offsets = MapFinder.ParseOffsets(text);

        if (!addresses.TryGetValue("Game::PlayerManager", out long managerRva)
            || !offsets.TryGetValue("Player::Local", out int[]? localChain)
            || !offsets.TryGetValue("Player::Abnormalities::Skills", out int[]? skillsChain))
        {
            Console.Error.WriteLine("The map has no Game::PlayerManager / Player::Local / Player::Abnormalities::Skills.");
            return 1;
        }

        using var memory = new MapFinder.ProcMemReader(pid);

        if (!TryResolveTable(memory, imageBase + (nint)managerRva, localChain, skillsChain, out nint table, out string? problem))
        {
            Console.Error.WriteLine($"Cannot find the skill table: {problem}.");
            return 1;
        }

        byte[] raw = new byte[TableBytes];

        if (!memory.TryRead(table, raw))
        {
            Console.Error.WriteLine($"The skill table at 0x{table:X} cannot be read.");
            return 1;
        }

        var slots = new List<Slot>();

        for (int offset = 0; offset < TableBytes; offset += 8)
        {
            byte[] slotBytes = raw.AsSpan(offset, 8).ToArray();
            long value = BinaryPrimitives.ReadInt64LittleEndian(slotBytes);

            if (value < MinPointer || value > MaxPointer)
            {
                slots.Add(new Slot(offset, false, slotBytes));
                continue;
            }

            byte[] data = new byte[FullStructBytes];

            // A struct at the very end of a mapped region may only have its first part readable
            if (!memory.TryRead((nint)value, data))
            {
                data = new byte[ShortStructBytes];

                if (!memory.TryRead((nint)value, data))
                {
                    slots.Add(new Slot(offset, false, slotBytes));
                    continue;
                }
            }

            slots.Add(new Slot(offset, true, data));
        }

        Dictionary<int, string> known = LoadKnownSkills();

        Console.WriteLine($"Game version {version}. Skill table at 0x{table:X}: {slots.Count(it => it.IsPointer)} of {slots.Count} slots hold a readable pointer.");
        Console.WriteLine();
        Console.WriteLine("Slots with a running timer right now (Timer at +0x10 above 0, MaxTimer at +0x14):");

        int running = 0;

        foreach (Slot slot in slots.Where(it => it.IsPointer))
        {
            float timer = ReadFloat(slot.Data, 0x10);
            float max = ReadFloat(slot.Data, 0x14);

            if (!(timer > 0 && max > 0 && timer <= max * 1.01f && max < 100000))
                continue;

            Console.WriteLine($"  slot {slot.Offset:X2}  {timer:0.0} / {max:0.0}s  {Describe(known, slot.Offset)}");
            running++;
        }

        if (running == 0)
            Console.WriteLine("  (none)");

        string directory = LogLocation.Directory;
        string slug = string.IsNullOrWhiteSpace(label) ? "dump" : LogLocation.Slug(label);
        string path = Path.Combine(directory, $"skills-{slug}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

        var lines = new List<string>
        {
            Header,
            $"# game {version}",
            $"# label {label ?? string.Empty}",
            $"# taken {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            $"# table 0x{table:X}"
        };

        foreach (Slot slot in slots)
            lines.Add($"slot {slot.Offset:X} {(slot.IsPointer ? "ptr" : "raw")} {Convert.ToHexString(slot.Data)}");

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not write {path}: {ex.Message}");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine($"Saved {path}");
        Console.WriteLine();
        Console.WriteLine("To find a skill: take one dump with it off and one with it on (same area, same weapon), then");
        Console.WriteLine("  ./HunterPie.Linux --diff-skills <off-dump> <on-dump>");
        Console.WriteLine("Adding a third dump taken with the skill off again hides values that change on their own:");
        Console.WriteLine("  ./HunterPie.Linux --diff-skills <off-dump> <on-dump> <off-again-dump>");

        return 0;
    }

    public static int Diff(string[] files)
    {
        if (files.Length is < 2 or > 3)
        {
            Console.Error.WriteLine("--diff-skills expects two dump files (skill off, then skill on) and optionally a third (skill off again).");
            return 2;
        }

        var dumps = new List<(string Version, Dictionary<int, Slot> Slots)>();

        foreach (string file in files)
        {
            if (!TryLoad(file, out string version, out Dictionary<int, Slot> slots, out string? problem))
            {
                Console.Error.WriteLine($"{file}: {problem}");
                return 1;
            }

            dumps.Add((version, slots));
        }

        Console.WriteLine("HunterPie Linux skill table diff");
        Console.WriteLine();

        if (dumps.Select(it => it.Version).Distinct().Count() > 1)
            Console.WriteLine("Warning: the dumps are from different game versions, so the table may have moved between them.");

        Dictionary<int, Slot> before = dumps[0].Slots;
        Dictionary<int, Slot> after = dumps[1].Slots;
        Dictionary<int, Slot>? control = dumps.Count == 3 ? dumps[2].Slots : null;
        Dictionary<int, string> known = LoadKnownSkills();

        var unknownChanges = new List<string>();
        var knownChanges = new List<string>();

        foreach (int offset in before.Keys.Intersect(after.Keys).OrderBy(it => it))
        {
            Slot? noise = null;
            control?.TryGetValue(offset, out noise);

            List<string> changes = Compare(before[offset], after[offset], noise);

            if (changes.Count == 0)
                continue;

            List<string> target = known.ContainsKey(offset) ? knownChanges : unknownChanges;
            target.Add($"slot {offset:X2}  {Describe(known, offset)}");
            target.AddRange(changes);
        }

        Console.WriteLine($"{files[0]}  ->  {files[1]}{(control is null ? string.Empty : $"  (ignoring what also changed in {files[2]})")}");
        Console.WriteLine();

        Console.WriteLine("Changed slots that are NOT in AbnormalityData.xml (where a new skill would be):");
        PrintAll(unknownChanges);
        Console.WriteLine();

        Console.WriteLine("Changed slots that are already tracked:");
        PrintAll(knownChanges);
        Console.WriteLine();

        if (unknownChanges.Count == 0)
        {
            Console.WriteLine("Nothing new changed. Either the skill is not in this table (it may live somewhere else), or it was not");
            Console.WriteLine("active in the second dump. Check that the skill's condition really held when you took it.");
        }
        else
        {
            Console.WriteLine("A timer-based skill shows a float going from 0 to a positive number at +0x10 (and +0x14). A skill that");
            Console.WriteLine("switches on and off without a timer shows a small int flag flipping instead. Repeat with a different");
            Console.WriteLine("situation to confirm one slot always follows the skill, then send me the slot and the lines under it.");
        }

        return 0;
    }

    private static void PrintAll(List<string> lines)
    {
        if (lines.Count == 0)
        {
            Console.WriteLine("  (none)");
            return;
        }

        foreach (string line in lines)
            Console.WriteLine(line.StartsWith("slot", StringComparison.Ordinal) ? $"  {line}" : $"    {line}");
    }

    /// <summary>The changed 4-byte words of a slot's struct, leaving out words that also differ in the control dump</summary>
    private static List<string> Compare(Slot before, Slot after, Slot? control)
    {
        var result = new List<string>();

        if (before.IsPointer != after.IsPointer)
        {
            result.Add(after.IsPointer
                ? "the slot now holds a pointer (it did not before)"
                : "the slot no longer holds a readable pointer");

            return result;
        }

        if (!before.IsPointer)
            return result;

        int length = Math.Min(before.Data.Length, after.Data.Length);

        for (int word = 0; word + 4 <= length; word += 4)
        {
            ReadOnlySpan<byte> x = before.Data.AsSpan(word, 4);
            ReadOnlySpan<byte> y = after.Data.AsSpan(word, 4);

            if (x.SequenceEqual(y))
                continue;

            // Something that differs between the two "off" dumps moves on its own and says nothing about the skill
            if (control is { IsPointer: true } && word + 4 <= control.Data.Length && !x.SequenceEqual(control.Data.AsSpan(word, 4)))
                continue;

            int oldInt = BinaryPrimitives.ReadInt32LittleEndian(x);
            int newInt = BinaryPrimitives.ReadInt32LittleEndian(y);
            float oldFloat = BitConverter.Int32BitsToSingle(oldInt);
            float newFloat = BitConverter.Int32BitsToSingle(newInt);

            result.Add($"+0x{word:X2}  {Convert.ToHexString(x)} -> {Convert.ToHexString(y)}   int {oldInt} -> {newInt}   float {oldFloat:G6} -> {newFloat:G6}");
        }

        return result;
    }

    private static string Describe(Dictionary<int, string> known, int offset) =>
        known.TryGetValue(offset, out string? names) ? $"tracked: {names}" : "not in AbnormalityData.xml";

    private static float ReadFloat(byte[] data, int offset) =>
        offset + 4 <= data.Length ? BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset, 4)) : 0f;

    /// <summary>Slot offset -> the skills the game data already puts there</summary>
    private static Dictionary<int, string> LoadKnownSkills()
    {
        var known = new Dictionary<int, string>();

        try
        {
            string path = ClientInfo.GetPathFor("Game/Wilds/Data/AbnormalityData.xml");

            if (!File.Exists(path))
                return known;

            var document = new XmlDocument();
            document.Load(path);

            if (document.SelectNodes("//Skills/Abnormality") is not { } nodes)
                return known;

            foreach (XmlNode node in nodes)
            {
                if (!int.TryParse(node.Attributes?["Offset"]?.Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int offset))
                    continue;

                string name = AilmentNames.Prettify(node.Attributes?["Name"]?.Value ?? node.Attributes?["Id"]?.Value ?? "?");

                known[offset] = known.TryGetValue(offset, out string? existing) ? $"{existing} / {name}" : name;
            }
        }
        catch (Exception ex) when (ex is IOException or XmlException or UnauthorizedAccessException)
        {
            // Names are only a convenience
        }

        return known;
    }

    private static bool TryLoad(string path, out string version, out Dictionary<int, Slot> slots, out string? problem)
    {
        version = string.Empty;
        slots = new Dictionary<int, Slot>();
        problem = null;

        string[] lines;

        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = ex.Message;
            return false;
        }

        if (lines.Length == 0 || lines[0] != Header)
        {
            problem = "not a skill table dump made by --dump-skills";
            return false;
        }

        foreach (string line in lines)
        {
            if (line.StartsWith("# game ", StringComparison.Ordinal))
            {
                version = line["# game ".Length..].Trim();
                continue;
            }

            string[] parts = line.Split(' ');

            if (parts.Length != 4 || parts[0] != "slot" || !int.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int offset))
                continue;

            try
            {
                slots[offset] = new Slot(offset, parts[2] == "ptr", Convert.FromHexString(parts[3]));
            }
            catch (FormatException)
            {
                problem = $"the line for slot {parts[1]} is damaged";
                return false;
            }
        }

        if (slots.Count == 0)
        {
            problem = "no slots in the file";
            return false;
        }

        return true;
    }

    /// <summary>
    /// The same walk the scanner does: the local player object from the player manager (Player::Local), then the
    /// Player::Abnormalities::Skills chain, where each step reads a pointer at the current address plus the offset
    /// </summary>
    private static bool TryResolveTable(
        IProbeMemory memory,
        nint globalAddress,
        int[] localChain,
        int[] skillsChain,
        out nint table,
        out string? problem)
    {
        table = 0;
        problem = null;

        nint address = globalAddress;

        foreach (int offset in localChain)
        {
            if (!TryReadPointer(memory, address, out nint next) || next == 0)
            {
                problem = "the player manager is empty, load into the world with your character first";
                return false;
            }

            address = next + offset;
        }

        if (!TryReadPointer(memory, address, out nint player) || player == 0)
        {
            problem = "the local player is not there yet, load into the world with your character first";
            return false;
        }

        nint current = player;

        foreach (int offset in skillsChain)
        {
            if (!TryReadPointer(memory, current + offset, out nint next) || next == 0)
            {
                problem = "the skill table is not reachable yet, stand in a hunting area (a quest or the open world) and try again";
                return false;
            }

            current = next;
        }

        table = current;

        return true;
    }

    private static bool TryReadPointer(IProbeMemory memory, nint address, out nint pointer)
    {
        Span<byte> buffer = stackalloc byte[sizeof(long)];
        pointer = 0;

        if (!memory.TryRead(address, buffer))
            return false;

        pointer = (nint)BinaryPrimitives.ReadInt64LittleEndian(buffer);

        return true;
    }
}

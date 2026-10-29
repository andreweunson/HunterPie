using HunterPie.Core.Client;
using HunterPie.Integrations.Datasources;
using System.Buffers.Binary;

namespace HunterPie.Linux.Services;

/// <summary>
/// --dump-status: the damage widget's attack and affinity come from two small structs hanging off the player, each
/// holding pointers to encrypted floats (HunterPie reads one fixed field of each). When a number does not match what the
/// game shows, this decrypts every pointer in both structs so the field that does match can be picked. Reads only.
/// </summary>
internal static class StatusDump
{
    private const string GameName = SupportedGameNames.MONSTER_HUNTER_WILDS;
    private const int StructBytes = 0x100;

    // What HunterPie shows today, from MHWildsAffinityStatus and MHWildsDamageStatus
    private static readonly Dictionary<int, string> AffinityFields = new()
    {
        [0x10] = "WeaponAffinity",
        [0x18] = "OriginalAffinity",
        [0x20] = "CurrentCriticalRate   <- HunterPie shows this as affinity",
    };

    private static readonly Dictionary<int, string> DamageFields = new()
    {
        [0x18] = "CurrentRawAttack      <- HunterPie shows this as attack",
        [0x40] = "CurrentElementalAttack  (shown x10)",
    };

    public static int Run()
    {
        Console.WriteLine("HunterPie Linux player status dump");
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
            || !addresses.TryGetValue("Encryption::Round", out long roundRva)
            || !offsets.TryGetValue("Player::List", out int[]? listChain)
            || !offsets.TryGetValue("Player::Status::Affinity", out int[]? affinityChain)
            || !offsets.TryGetValue("Player::Status::Damage", out int[]? damageChain))
        {
            Console.Error.WriteLine("The map has no Game::PlayerManager / Encryption::Round / Player::List / Player::Status entries.");
            return 1;
        }

        using var memory = new MapFinder.ProcMemReader(pid);

        byte[] pair = new byte[32];

        if (!memory.TryRead(imageBase + (nint)roundRva, pair))
        {
            Console.Error.WriteLine("The encryption key in the map cannot be read.");
            return 1;
        }

        // Same walk as the scanner: the manager, Player::List, then player 0 (you) and its base pointer at +0x10
        nint address = imageBase + (nint)managerRva;

        foreach (int offset in listChain)
        {
            if (!CryptoProbe.TryReadPointer(memory, address, out nint next) || next == 0)
            {
                Console.Error.WriteLine("The player manager is empty. Load into the world with your character first.");
                return 1;
            }

            address = next + offset;
        }

        if (!CryptoProbe.TryReadPointer(memory, address, out nint player) || player == 0
            || !CryptoProbe.TryReadPointer(memory, player + 0x10, out nint basePointer) || basePointer == 0)
        {
            Console.Error.WriteLine("The local player is not there yet. Load into the world with your character first.");
            return 1;
        }

        Console.WriteLine($"Game version {version}. Your player base is at 0x{basePointer:X}.");
        Console.WriteLine("Compare these with the character screen in the game, in the same moment, and tell me which line matches");
        Console.WriteLine("each number (affinity %, attack, element) and what the game shows for them.");

        PrintStruct(memory, "Affinity struct", basePointer, affinityChain, AffinityFields, pair);
        PrintStruct(memory, "Damage struct", basePointer, damageChain, DamageFields, pair);

        return 0;
    }

    private static void PrintStruct(
        IProbeMemory memory,
        string title,
        nint basePointer,
        int[] chain,
        Dictionary<int, string> known,
        byte[] pair)
    {
        Console.WriteLine();
        Console.WriteLine($"{title}:");

        if (!CryptoProbe.TryFollow(memory, basePointer, chain, out nint structure))
        {
            Console.WriteLine("  (could not follow the offsets from the map: they may have changed in this patch)");
            return;
        }

        byte[] data = new byte[StructBytes];

        if (!memory.TryRead(structure, data))
        {
            Console.WriteLine($"  (0x{structure:X} cannot be read)");
            return;
        }

        Console.WriteLine($"  at 0x{structure:X}");

        int shown = 0;

        for (int offset = 0; offset + 8 <= data.Length; offset += 8)
        {
            long value = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(offset, 8));

            // A pointer to an encrypted float object, whose 16 bytes sit at +0x10
            if (value < 0x10000 || value > 0x7FFFFFFFFFFF)
                continue;

            byte[] encrypted = new byte[16];

            if (!memory.TryRead((nint)value + 0x10, encrypted))
                continue;

            float decrypted = CryptoProbe.Decrypt(encrypted, pair.AsSpan(16, 16), pair.AsSpan(0, 16));
            string name = known.TryGetValue(offset, out string? field) ? field : string.Empty;

            Console.WriteLine($"  +0x{offset:X2}  {decrypted,12:G7}  {name}");
            shown++;
        }

        if (shown == 0)
            Console.WriteLine("  (no pointers to encrypted values found here)");
    }
}

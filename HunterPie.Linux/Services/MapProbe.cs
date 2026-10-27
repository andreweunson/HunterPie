using System.Buffers.Binary;

namespace HunterPie.Linux.Services;

internal interface IProbeMemory
{
    /// <summary>
    /// Fills the whole buffer from the target process, false when any part of it can't be read
    /// </summary>
    bool TryRead(nint address, Span<byte> buffer);
}

internal readonly record struct ProbeCandidate(long Shift, int ValidSlots, int ChainsPassed)
{
    // A pointer that really is a pointer is worth more than a chain that happened to end up readable
    public int Score => (ValidSlots * 2) + ChainsPassed;
}

internal sealed record ProbeResult(bool Accepted, ProbeCandidate? Best, IReadOnlyList<ProbeCandidate> Top, string Reason);

/// <summary>
/// Finds the address shift between a known map and the running game. Capcom's small patches (the last digit of the
/// version) recompile the executable and move every global by the same, page-aligned amount, while the offsets that
/// describe the game's structures stay the same. Every shift is tried and judged by whether the globals then hold
/// believable pointers and whether the offset chains hanging off them lead to readable memory.
/// </summary>
internal static class MapProbe
{
    public const long Step = 0x1000;
    public const long DefaultRange = 0x800000;

    // Globals that hold a pointer to a long-lived manager object, and the chains from the map that start at each
    private static readonly (string Address, string[] Chains)[] Managers =
    [
        ("Game::PauseManager", ["Game::PauseState"]),
        ("Game::SaveManager", ["Save::Index", "Save::Data", "Save::Player::HunterRank", "Save::SpecializedTool"]),
        ("Game::EnvironmentManager", ["Environment::MonsterList", "Environment::Moon"]),
        ("Game::EnemyManager", []),
        ("Data::SizeTables", ["Monster::SizeTable"]),
        ("Game::QuestManager", ["Quest::Data", "Quest::DieCount", "Quest::IsRetrying", "Quest::CurrentInformation", "Quest::MissionId"]),
        ("Game::GUIManager", ["GUI::IsNPCIndicatorVisible"]),
        ("Game::WorldManager", []),
        ("Game::CameraManager", ["Camera::Monster::Target"]),
        ("Game::NpcManager", ["Npcs::Party"]),
        ("Game::NetworkManager", ["Network::Party"]),
        ("Game::PlayerManager", ["Player::List", "Player::Party", "Player::Local", "Player::Quest::PlayerIndexes"])
    ];

    // Below this many believable pointers the candidate is noise, and the winner must be clearly ahead of the rest
    private const int MinValidSlots = 10;
    private const int MinMargin = 6;

    public static IReadOnlyList<string> RequiredAddresses { get; } = [.. Managers.Select(it => it.Address)];

    public static ProbeResult Find(
        IProbeMemory memory,
        nint imageBase,
        long imageSize,
        IReadOnlyDictionary<string, long> addresses,
        IReadOnlyDictionary<string, int[]> offsets,
        long range = DefaultRange)
    {
        string[] missing = [.. RequiredAddresses.Where(it => !addresses.ContainsKey(it))];

        if (missing.Length > 0)
            return new ProbeResult(false, null, [], $"the source map has no {string.Join(", ", missing)}");

        var candidates = new List<ProbeCandidate>();

        for (long shift = -range; shift <= range; shift += Step)
        {
            if (!FitsInImage(addresses, imageSize, shift))
                continue;

            ProbeCandidate candidate = Evaluate(memory, imageBase, addresses, offsets, shift);

            if (candidate.ValidSlots >= MinValidSlots / 2)
                candidates.Add(candidate);
        }

        ProbeCandidate[] top = [.. candidates.OrderByDescending(it => it.Score).ThenBy(it => Math.Abs(it.Shift)).Take(5)];

        if (top.Length == 0)
            return new ProbeResult(false, null, top, "no shift made the managers' globals hold readable pointers");

        ProbeCandidate best = top[0];

        if (best.ValidSlots < MinValidSlots)
            return new ProbeResult(false, best, top, $"the best shift only had {best.ValidSlots} of {Managers.Length} globals pointing at readable memory");

        if (top.Length > 1 && best.Score - top[1].Score < MinMargin)
            return new ProbeResult(false, best, top, "two shifts look about equally plausible, so it can't tell which one is right");

        return new ProbeResult(true, best, top, "one shift stands clearly above the rest");
    }

    private static bool FitsInImage(IReadOnlyDictionary<string, long> addresses, long imageSize, long shift)
    {
        foreach (long rva in addresses.Values)
        {
            long moved = rva + shift;

            if (moved < 0 || moved + sizeof(long) > imageSize)
                return false;
        }

        return true;
    }

    private static ProbeCandidate Evaluate(
        IProbeMemory memory,
        nint imageBase,
        IReadOnlyDictionary<string, long> addresses,
        IReadOnlyDictionary<string, int[]> offsets,
        long shift)
    {
        int valid = 0;

        foreach ((string address, _) in Managers)
        {
            nint slot = imageBase + (nint)(addresses[address] + shift);

            if (TryReadPointer(memory, slot, out nint pointer) && IsPlausible(pointer) && IsReadable(memory, pointer))
                valid++;
        }

        // Walking chains is much dearer than reading slots, and a candidate with few good slots can't win anyway
        if (valid < MinValidSlots / 2)
            return new ProbeCandidate(shift, valid, 0);

        int chains = 0;

        foreach ((string address, string[] names) in Managers)
        {
            nint slot = imageBase + (nint)(addresses[address] + shift);

            foreach (string name in names)
            {
                if (offsets.TryGetValue(name, out int[]? chain) && WalkChain(memory, slot, chain))
                    chains++;
            }
        }

        return new ProbeCandidate(shift, valid, chains);
    }

    /// <summary>
    /// Follows a chain like the game's reader does (read a pointer, add the offset, repeat) and reports whether it
    /// reaches memory that can be read without meeting a null pointer on the way
    /// </summary>
    internal static bool WalkChain(IProbeMemory memory, nint start, int[] chain)
    {
        nint address = start;

        foreach (int offset in chain)
        {
            if (!TryReadPointer(memory, address, out nint next) || !IsPlausible(next))
                return false;

            address = next + offset;
        }

        return IsReadable(memory, address);
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

    private static bool IsReadable(IProbeMemory memory, nint address)
    {
        Span<byte> buffer = stackalloc byte[sizeof(long)];

        return memory.TryRead(address, buffer);
    }

    // User-space addresses only, and never the low pages where small integers and flags live
    private static bool IsPlausible(nint pointer) => (long)pointer is >= 0x10000 and <= 0x7FFFFFFFFFFF;
}

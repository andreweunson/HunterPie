using HunterPie.Integrations.Datasources.MonsterHunterWilds.Entity.Crypto;
using System.Buffers.Binary;
using System.Runtime.Intrinsics;
using X86Aes = System.Runtime.Intrinsics.X86.Aes;

namespace HunterPie.Linux.Services;

internal enum SampleKind
{
    Affinity,
    Attack,
    Element,
    MonsterHealth,
    MonsterMaxHealth
}

/// <summary>
/// One 16 byte encrypted float read from the game, of a kind whose real value is known to fall in a narrow range
/// </summary>
internal sealed record CryptoSample(string Name, SampleKind Kind, byte[] Encrypted);

internal sealed record CryptoHit(long RoundRva, int Clean, float[] Values)
{
    public long KeyRva => RoundRva + KeyDistance;

    public const long KeyDistance = 0x10;
}

/// <summary>
/// Wilds hides its floats behind AES: value = AesDecryptLast(key ^ encrypted, round), with the 16 byte key and round
/// kept in two adjacent globals (the key always 0x10 after the round, in every map so far). Decrypting a handful of
/// values whose real range is known - the player's own affinity and attack - tells whether a key/round pair is the
/// right one: with a wrong pair the results are random floats, and random floats almost never land in all those ranges.
/// </summary>
internal static class CryptoProbe
{
    public static float Decrypt(ReadOnlySpan<byte> encrypted, ReadOnlySpan<byte> key, ReadOnlySpan<byte> round)
    {
        Vector128<byte> values = Vector128.Create(key) ^ Vector128.Create(encrypted);
        Vector128<byte> roundKey = Vector128.Create(round);

        Vector128<byte> decrypted = X86Aes.IsSupported
            ? X86Aes.DecryptLast(values, roundKey)
            : ManualAesCrypto.DecryptLast(values, roundKey);

        return decrypted.AsSingle()[0];
    }

    public static bool IsSane(SampleKind kind, float value)
    {
        if (!float.IsFinite(value))
            return false;

        float magnitude = Math.Abs(value);

        return kind switch
        {
            // A percentage, sometimes negative; zero is fine, tiny non-zero values are the signature of noise
            SampleKind.Affinity => value == 0 || magnitude is >= 0.001f and <= 100f,
            SampleKind.Attack => value is >= 1f and <= 100_000f,
            SampleKind.Element => value == 0 || value is >= 0.001f and <= 10_000f,
            SampleKind.MonsterHealth => value is >= 0f and <= 5_000_000f,
            SampleKind.MonsterMaxHealth => value is >= 100f and <= 5_000_000f,
            _ => false
        };
    }

    /// <summary>
    /// Percentages and elemental values are short decimals (15, 0.25, 21). A wrong key still lands in the believable
    /// ranges now and then, since only 4 of the 16 decrypted bytes matter, but almost never on a short decimal.
    /// Attack values are skipped: buffs multiply them into arbitrary fractions.
    /// </summary>
    public static bool IsClean(SampleKind kind, float value)
    {
        if (kind == SampleKind.Attack)
            return false;

        // A monster's maximum health is a whole number, which random floats of that size practically never are
        if (kind == SampleKind.MonsterMaxHealth)
            return Math.Abs(value - MathF.Round(value)) <= 0.001f;

        if (kind == SampleKind.MonsterHealth)
            return false;

        double scaled = Math.Abs((double)value) * 1000;

        return scaled <= 1_000_000 && Math.Abs(scaled - Math.Round(scaled)) <= 0.01;
    }

    public static int CountClean(IReadOnlyList<CryptoSample> samples, float[] values)
    {
        int clean = 0;

        for (int i = 0; i < samples.Count; i++)
        {
            if (IsClean(samples[i].Kind, values[i]))
                clean++;
        }

        return clean;
    }

    /// <summary>
    /// The hit with clearly the most short-decimal values, or null when none stands out
    /// </summary>
    public static CryptoHit? Pick(IReadOnlyList<CryptoHit> hits)
    {
        CryptoHit[] ranked = [.. hits.OrderByDescending(it => it.Clean)];

        if (ranked.Length == 0 || ranked[0].Clean < 3)
            return null;

        return ranked.Length == 1 || ranked[0].Clean - ranked[1].Clean >= 2 ? ranked[0] : null;
    }

    public static float[] DecryptAll(IReadOnlyList<CryptoSample> samples, ReadOnlySpan<byte> round, ReadOnlySpan<byte> key)
    {
        var values = new float[samples.Count];

        for (int i = 0; i < samples.Count; i++)
            values[i] = Decrypt(samples[i].Encrypted, key, round);

        return values;
    }

    public static bool AllSane(IReadOnlyList<CryptoSample> samples, float[] values)
    {
        for (int i = 0; i < samples.Count; i++)
        {
            if (!IsSane(samples[i].Kind, values[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Tries every round/key pair that starts at one of the positions from <paramref name="first"/> (an RVA), spaced by
    /// <paramref name="step"/>, inside <paramref name="region"/> (which starts at the RVA <paramref name="regionRva"/>).
    /// Only pairs for which every sample decrypts to a believable value are returned.
    /// </summary>
    public static List<CryptoHit> Scan(
        ReadOnlySpan<byte> region,
        long regionRva,
        long first,
        long last,
        long step,
        IReadOnlyList<CryptoSample> samples)
    {
        var hits = new List<CryptoHit>();

        if (samples.Count == 0)
            return hits;

        for (long rva = first; rva <= last; rva += step)
        {
            long offset = rva - regionRva;

            if (offset < 0 || offset + CryptoHit.KeyDistance + 16 > region.Length)
                continue;

            ReadOnlySpan<byte> round = region.Slice((int)offset, 16);
            ReadOnlySpan<byte> key = region.Slice((int)(offset + CryptoHit.KeyDistance), 16);

            if (IsBlank(round) && IsBlank(key))
                continue;

            // Most pairs fail on the first sample, so look at one before decrypting the rest
            if (!IsSane(samples[0].Kind, Decrypt(samples[0].Encrypted, key, round)))
                continue;

            float[] values = DecryptAll(samples, round, key);

            if (AllSane(samples, values))
                hits.Add(new CryptoHit(rva, CountClean(samples, values), values));
        }

        return hits;
    }

    private static bool IsBlank(ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
        {
            if (b != 0)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Reads the local player's affinity and attack values the way the game's reader does, as encrypted blocks.
    /// Returns an explanation instead of samples when the player isn't in a state where they can be read.
    /// </summary>
    public static (List<CryptoSample> Samples, string? Problem) CollectPlayerSamples(
        IProbeMemory memory,
        nint imageBase,
        IReadOnlyDictionary<string, long> addresses,
        IReadOnlyDictionary<string, int[]> offsets)
    {
        var samples = new List<CryptoSample>();

        if (!addresses.TryGetValue("Game::PlayerManager", out long managerRva)
            || !offsets.TryGetValue("Player::List", out int[]? listChain)
            || !offsets.TryGetValue("Player::Status::Affinity", out int[]? affinityChain)
            || !offsets.TryGetValue("Player::Status::Damage", out int[]? damageChain))
            return (samples, "the map has no Player::List / Player::Status entries");

        // Player::List is read as: pointer at the global, plus the offset, repeated
        nint address = imageBase + (nint)managerRva;

        foreach (int offset in listChain)
        {
            if (!TryReadPointer(memory, address, out nint next) || next == 0)
                return (samples, "the player manager is empty. Load into the world with your character first");

            address = next + offset;
        }

        // Player index 0 is the local player
        if (!TryReadPointer(memory, address, out nint playerPointer) || playerPointer == 0)
            return (samples, "the local player is not there yet. Load into the world with your character first");

        if (!TryReadPointer(memory, playerPointer + 0x10, out nint basePointer) || basePointer == 0)
            return (samples, "the local player has no base pointer yet");

        // Status structs are reached as: add the offset, read the pointer, repeat
        if (!TryFollow(memory, basePointer, affinityChain, out nint affinity))
            return (samples, "could not follow Player::Status::Affinity (the offsets may have changed in this patch)");

        if (!TryFollow(memory, basePointer, damageChain, out nint damage))
            return (samples, "could not follow Player::Status::Damage (the offsets may have changed in this patch)");

        (string Name, SampleKind Kind, nint Struct, int Field)[] fields =
        [
            ("weapon affinity", SampleKind.Affinity, affinity, 0x10),
            ("original affinity", SampleKind.Affinity, affinity, 0x18),
            ("critical rate", SampleKind.Affinity, affinity, 0x20),
            ("raw attack", SampleKind.Attack, damage, 0x18),
            ("elemental attack", SampleKind.Element, damage, 0x40)
        ];

        foreach ((string name, SampleKind kind, nint structure, int field) in fields)
        {
            // A Ref<MHWildsEncryptedFloat> field holds the address of the float object, whose 16 bytes sit at +0x10
            if (!TryReadPointer(memory, structure + field, out nint reference) || reference == 0)
                continue;

            byte[] encrypted = new byte[16];

            if (memory.TryRead(reference + 0x10, encrypted))
                samples.Add(new CryptoSample(name, kind, encrypted));
        }

        return samples.Count >= 4
            ? (samples, null)
            : (samples, $"only {samples.Count} of 5 encrypted values could be read");
    }

    /// <summary>
    /// Reads the health and maximum health of up to three monsters the way the game's reader does. Optional: without a
    /// monster in the area this is empty and the player's own values have to do.
    /// </summary>
    public static List<CryptoSample> CollectMonsterSamples(
        IProbeMemory memory,
        nint imageBase,
        IReadOnlyDictionary<string, long> addresses,
        IReadOnlyDictionary<string, int[]> offsets)
    {
        var samples = new List<CryptoSample>();

        if (!addresses.TryGetValue("Game::EnemyManager", out long managerRva)
            || !offsets.TryGetValue("Environment::MonsterList", out int[]? listChain)
            || !offsets.TryGetValue("Monster::Magic", out int[]? magicChain)
            || !offsets.TryGetValue("Monster::BasicData", out int[]? basicChain)
            || !offsets.TryGetValue("Monster::Health", out int[]? healthChain))
            return samples;

        // Monster::MonsterList is read as: pointer at the global plus the offset, giving a dynamic array
        nint array = imageBase + (nint)managerRva;

        foreach (int offset in listChain)
        {
            if (!TryReadPointer(memory, array, out nint next) || next == 0)
                return samples;

            array = next + offset;
        }

        // { Elements pointer, int Count }, with the elements starting 0x20 into the buffer
        Span<byte> header = stackalloc byte[12];

        if (!memory.TryRead(array, header))
            return samples;

        nint elements = (nint)BinaryPrimitives.ReadInt64LittleEndian(header);
        int count = Math.Clamp(BinaryPrimitives.ReadInt32LittleEndian(header[8..]), 0, 700);

        for (int i = 0; i < count && samples.Count < 6; i++)
        {
            if (!TryReadPointer(memory, elements + 0x20 + (i * 8), out nint monster) || monster == 0)
                continue;

            // Same filter as the game's reader: an "Em" asset, enabled, and a regular monster
            if (!TryFollow(memory, monster, magicChain, out nint magic) || (int)magic != 0x6D0045)
                continue;

            if (!TryFollow(memory, monster, basicChain, out nint basic)
                || !TryReadByte(memory, basic + 0x10, out byte enabled) || enabled != 1
                || !TryReadInt(memory, basic + 0x54, out int category) || category != 0)
                continue;

            if (!TryFollow(memory, monster, healthChain, out nint health))
                continue;

            AddEncrypted(memory, samples, "monster health", SampleKind.MonsterHealth, health, 0x10);
            AddEncrypted(memory, samples, "monster max health", SampleKind.MonsterMaxHealth, health, 0x18);
        }

        return samples;
    }

    private static void AddEncrypted(IProbeMemory memory, List<CryptoSample> samples, string name, SampleKind kind, nint structure, int field)
    {
        if (!TryReadPointer(memory, structure + field, out nint reference) || reference == 0)
            return;

        byte[] encrypted = new byte[16];

        if (memory.TryRead(reference + 0x10, encrypted))
            samples.Add(new CryptoSample(name, kind, encrypted));
    }

    private static bool TryReadInt(IProbeMemory memory, nint address, out int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        value = 0;

        if (!memory.TryRead(address, buffer))
            return false;

        value = BinaryPrimitives.ReadInt32LittleEndian(buffer);

        return true;
    }

    private static bool TryReadByte(IProbeMemory memory, nint address, out byte value)
    {
        Span<byte> buffer = stackalloc byte[1];
        value = 0;

        if (!memory.TryRead(address, buffer))
            return false;

        value = buffer[0];

        return true;
    }

    internal static bool TryFollow(IProbeMemory memory, nint start, int[] chain, out nint result)
    {
        result = start;

        foreach (int offset in chain)
        {
            if (!TryReadPointer(memory, result + offset, out nint next) || next == 0)
                return false;

            result = next;
        }

        return true;
    }

    internal static bool TryReadPointer(IProbeMemory memory, nint address, out nint pointer)
    {
        Span<byte> buffer = stackalloc byte[sizeof(long)];
        pointer = 0;

        if (!memory.TryRead(address, buffer))
            return false;

        pointer = (nint)BinaryPrimitives.ReadInt64LittleEndian(buffer);

        return true;
    }
}

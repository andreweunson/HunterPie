using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Text;

namespace HunterPie.Platforms.Linux.Pe;

/// <summary>
/// Reads the VS_VERSION_INFO resource out of a Windows executable. On Windows this is what
/// FileVersionInfo.FileVersion returns, but .NET can't parse PE resources on Linux.
/// </summary>
internal static class PeVersionReader
{
    private const int RT_VERSION = 16;
    private const uint FIXED_FILE_INFO_SIGNATURE = 0xFEEF04BD;

    /// <summary>
    /// Returns the "FileVersion" string, falling back to the binary fixed version (a.b.c.d). Null when there's none.
    /// </summary>
    public static string? ReadFileVersion(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            using var pe = new PEReader(stream);

            byte[]? block = ReadVersionResource(pe);

            return block is null ? null : ParseFileVersion(block);
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    private static byte[]? ReadVersionResource(PEReader pe)
    {
        PEHeaders headers = pe.PEHeaders;
        DirectoryEntry resources = headers.PEHeader?.ResourceTableDirectory ?? default;

        if (resources.Size == 0 || !headers.TryGetDirectoryOffset(resources, out _))
            return null;

        PEMemoryBlock section = pe.GetSectionData(resources.RelativeVirtualAddress);

        if (section.Length == 0)
            return null;

        ReadOnlySpan<byte> root = section.GetContent().AsSpan();

        // Type level -> RT_VERSION, name level -> first entry, language level -> first entry
        if (!TryFindEntry(root, 0, RT_VERSION, out uint typeOffset) || !IsDirectory(typeOffset))
            return null;

        if (!TryFirstEntry(root, DirectoryOffset(typeOffset), out uint nameOffset) || !IsDirectory(nameOffset))
            return null;

        if (!TryFirstEntry(root, DirectoryOffset(nameOffset), out uint dataEntryOffset) || IsDirectory(dataEntryOffset))
            return null;

        int entry = (int)dataEntryOffset;

        if (entry < 0 || entry + 16 > root.Length)
            return null;

        int dataRva = BinaryPrimitives.ReadInt32LittleEndian(root[entry..]);
        int dataSize = BinaryPrimitives.ReadInt32LittleEndian(root[(entry + 4)..]);

        PEMemoryBlock data = pe.GetSectionData(dataRva);

        if (dataSize <= 0 || data.Length < dataSize)
            return null;

        return data.GetContent(0, dataSize).ToArray();
    }

    private static bool IsDirectory(uint offset) => (offset & 0x80000000) != 0;

    private static uint DirectoryOffset(uint offset) => offset & 0x7FFFFFFF;

    // IMAGE_RESOURCE_DIRECTORY is 16 bytes, followed by 8 byte entries (named entries first, then id entries)
    private static bool TryFindEntry(ReadOnlySpan<byte> root, uint directory, int id, out uint offset)
    {
        offset = 0;
        int dir = (int)directory;

        if (dir < 0 || dir + 16 > root.Length)
            return false;

        int named = BinaryPrimitives.ReadUInt16LittleEndian(root[(dir + 12)..]);
        int ids = BinaryPrimitives.ReadUInt16LittleEndian(root[(dir + 14)..]);

        for (int i = named; i < named + ids; i++)
        {
            int entry = dir + 16 + i * 8;

            if (entry + 8 > root.Length)
                return false;

            if (BinaryPrimitives.ReadUInt32LittleEndian(root[entry..]) != (uint)id)
                continue;

            offset = BinaryPrimitives.ReadUInt32LittleEndian(root[(entry + 4)..]);
            return true;
        }

        return false;
    }

    private static bool TryFirstEntry(ReadOnlySpan<byte> root, uint directory, out uint offset)
    {
        offset = 0;
        int dir = (int)directory;

        if (dir < 0 || dir + 16 + 8 > root.Length)
            return false;

        int count = BinaryPrimitives.ReadUInt16LittleEndian(root[(dir + 12)..])
                  + BinaryPrimitives.ReadUInt16LittleEndian(root[(dir + 14)..]);

        if (count == 0)
            return false;

        offset = BinaryPrimitives.ReadUInt32LittleEndian(root[(dir + 16 + 4)..]);
        return true;
    }

    /// <summary>
    /// VS_VERSIONINFO: { WORD length, valueLength, type; WCHAR key[]; pad; value; pad; children }
    /// </summary>
    private static string? ParseFileVersion(byte[] block)
    {
        if (block.Length < 6)
            return null;

        int totalLength = Math.Min(BinaryPrimitives.ReadUInt16LittleEndian(block), block.Length);
        int valueLength = BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(2));

        int cursor = 6;
        cursor = SkipKey(block, cursor);
        cursor = Align4(cursor);

        string? fixedVersion = null;

        if (valueLength >= 52 && cursor + 52 <= block.Length
            && BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(cursor)) == FIXED_FILE_INFO_SIGNATURE)
        {
            uint ms = BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(cursor + 8));
            uint ls = BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(cursor + 12));
            fixedVersion = $"{ms >> 16}.{ms & 0xFFFF}.{ls >> 16}.{ls & 0xFFFF}";
        }

        cursor = Align4(cursor + valueLength);

        string? stringVersion = FindStringFileVersion(block, cursor, totalLength);

        return stringVersion ?? fixedVersion;
    }

    private static string? FindStringFileVersion(byte[] block, int start, int end)
    {
        // Children of the root: StringFileInfo and VarFileInfo
        int cursor = start;

        while (cursor + 6 <= end)
        {
            int length = BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(cursor));

            if (length < 6)
                return null;

            int childEnd = Math.Min(cursor + length, end);
            string key = ReadKey(block, cursor + 6, out int afterKey);

            if (key == "StringFileInfo")
            {
                string? version = FindInStringTables(block, Align4(afterKey), childEnd);

                if (version is not null)
                    return version;
            }

            cursor = Align4(cursor + length);
        }

        return null;
    }

    private static string? FindInStringTables(byte[] block, int start, int end)
    {
        int tableCursor = start;

        while (tableCursor + 6 <= end)
        {
            int tableLength = BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(tableCursor));

            if (tableLength < 6)
                return null;

            int tableEnd = Math.Min(tableCursor + tableLength, end);
            ReadKey(block, tableCursor + 6, out int afterTableKey);

            int stringCursor = Align4(afterTableKey);

            while (stringCursor + 6 <= tableEnd)
            {
                int stringLength = BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(stringCursor));
                int valueChars = BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(stringCursor + 2));

                if (stringLength < 6)
                    break;

                string key = ReadKey(block, stringCursor + 6, out int afterKey);

                if (key == "FileVersion" && valueChars > 0)
                {
                    // Some linkers store the value length in bytes instead of characters, which would read into the next
                    // entry. The value is NUL terminated either way, so read up to the terminator.
                    string value = ReadKey(block, Align4(afterKey), out _).Trim();

                    if (value.Length > 0)
                        return value;
                }

                stringCursor = Align4(stringCursor + stringLength);
            }

            tableCursor = Align4(tableCursor + tableLength);
        }

        return null;
    }

    private static int Align4(int value) => (value + 3) & ~3;

    private static int SkipKey(byte[] block, int cursor)
    {
        ReadKey(block, cursor, out int after);

        return after;
    }

    private static string ReadKey(byte[] block, int cursor, out int afterKey)
    {
        int end = cursor;

        while (end + 1 < block.Length && (block[end] != 0 || block[end + 1] != 0))
            end += 2;

        string key = Encoding.Unicode.GetString(block, cursor, end - cursor);
        afterKey = Math.Min(end + 2, block.Length);

        return key;
    }
}

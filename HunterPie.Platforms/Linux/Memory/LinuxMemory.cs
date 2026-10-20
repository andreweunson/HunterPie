using HunterPie.Core.Domain.Memory;
using HunterPie.Core.Utils;
using Microsoft.Win32.SafeHandles;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;

namespace HunterPie.Platforms.Linux.Memory;

/// <summary>
/// Reads and writes the memory of another process through /proc/[pid]/mem.
/// The kernel must allow it: same user and kernel.yama.ptrace_scope=0, or CAP_SYS_PTRACE on this executable.
/// </summary>
internal sealed class LinuxMemory : IMemoryAsync, IDisposable
{
    private readonly SafeFileHandle _handle;

    public LinuxMemory(int pid)
    {
        // FileShare.ReadWrite avoids .NET taking an advisory flock on the proc file
        _handle = File.OpenHandle(
            path: $"/proc/{pid}/mem",
            mode: FileMode.Open,
            access: FileAccess.ReadWrite,
            share: FileShare.ReadWrite
        );
    }

    public Task<string> ReadAsync(IntPtr address, int length, Encoding? encoding = null) =>
        Task.Run(() =>
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(length);

            try
            {
                Array.Clear(buffer, 0, length);
                TryRead(address, buffer.AsSpan(0, length));

                string raw = (encoding ?? Encoding.UTF8).GetString(buffer, 0, length);
                int nullCharIndex = raw.IndexOf('\x00');

                return nullCharIndex < 0
                    ? raw
                    : raw[..nullCharIndex];
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, true);
            }
        });

    public async Task<T> ReadAsync<T>(IntPtr address) where T : struct => (await ReadAsync<T>(address, 1))[0];

    public Task<T[]> ReadAsync<T>(IntPtr address, int count) where T : struct =>
        Task.Run(() => typeof(T).IsPrimitive
            ? ReadPrimitive<T>(address, count)
            : ReadStructure<T>(address, count));

    public async Task<IntPtr> ReadAsync(IntPtr address, int[] offsets)
    {
        foreach (int offset in offsets)
        {
            IntPtr nextAddress = await ReadAsync<IntPtr>(address);

            if (nextAddress == IntPtr.Zero)
                return IntPtr.Zero;

            address = nextAddress + offset;
        }

        return address;
    }

    public async Task<IntPtr> ReadPtrAsync(IntPtr address, int[] offsets)
    {
        foreach (int offset in offsets)
        {
            IntPtr newAddress = address + offset;
            IntPtr nextAddress = await ReadAsync<IntPtr>(newAddress);

            if (nextAddress == IntPtr.Zero)
                return IntPtr.Zero;

            address = nextAddress;
        }

        return address;
    }

    public async Task<T> DerefAsync<T>(IntPtr address, int[] offsets) where T : struct
    {
        IntPtr valuePtr = await ReadAsync(address, offsets);

        if (valuePtr == IntPtr.Zero)
            return default;

        return await ReadAsync<T>(valuePtr);
    }

    public async Task<T> DerefPtrAsync<T>(IntPtr address, int[] offsets) where T : struct
    {
        IntPtr valuePtr = await ReadPtrAsync(address, offsets);

        if (valuePtr == IntPtr.Zero)
            return default;

        return await ReadAsync<T>(valuePtr);
    }

    public Task WriteAsync<T>(IntPtr address, T[] data) where T : struct =>
        Task.Run(() =>
        {
            byte[] buffer = MarshalHelper.StructureToBuffer(data);

            // Writes through /proc/pid/mem ignore page protections (FOLL_FORCE), so this also patches code pages
            RandomAccess.Write(_handle, buffer, (long)address);
        });

    public Task InjectAsmAsync(IntPtr address, byte[] asm) => WriteAsync(address, asm);

    public Task InjectAsync(string dll) =>
        throw new PlatformNotSupportedException(
            "Injecting Windows DLLs from a native Linux process is not supported. " +
            "Native modules must be loaded by running HunterPie inside the same Wine/Proton prefix as the game.");

    public void Dispose() => _handle.Dispose();

    private T[] ReadPrimitive<T>(IntPtr address, int count) where T : struct
    {
        if (count <= 0)
            return [];

        var result = new T[count];
        TryRead(address, MemoryMarshal.AsBytes(result.AsSpan()));

        return result;
    }

    private T[] ReadStructure<T>(IntPtr address, int count) where T : struct
    {
        if (count <= 0)
            return [];

        int byteCount = Marshal.SizeOf<T>() * count;
        byte[] buffer = new byte[byteCount];
        TryRead(address, buffer);

        IntPtr unmanaged = Marshal.AllocHGlobal(byteCount);

        try
        {
            Marshal.Copy(buffer, 0, unmanaged, byteCount);

            return MarshalHelper.BufferToStructures<T>(unmanaged, count);
        }
        finally
        {
            Marshal.FreeHGlobal(unmanaged);
        }
    }

    /// <summary>
    /// Mirrors ReadProcessMemory behavior from the Windows implementation: a failed read leaves the buffer untouched (zeroed)
    /// instead of throwing, since the game memory is full of pointers that are momentarily invalid.
    /// </summary>
    private bool TryRead(IntPtr address, Span<byte> buffer)
    {
        long offset = (long)address;

        if (offset <= 0)
            return false;

        try
        {
            int total = 0;

            while (total < buffer.Length)
            {
                int read = RandomAccess.Read(_handle, buffer[total..], offset + total);

                if (read <= 0)
                    return false;

                total += read;
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }
}

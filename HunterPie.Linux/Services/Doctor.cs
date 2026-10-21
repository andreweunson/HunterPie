using HunterPie.Core.Client;
using HunterPie.Integrations.Datasources;
using HunterPie.Platforms.Linux.Pe;
using HunterPie.Platforms.Linux.Process;

namespace HunterPie.Linux.Services;

/// <summary>
/// Checks everything the Linux port needs from the machine, without attaching to anything.
/// Exit code is 0 only when a supported game was found and its memory is readable.
/// </summary>
internal static class Doctor
{
    private static readonly string[] GameNames =
    [
        SupportedGameNames.MONSTER_HUNTER_WORLD,
        SupportedGameNames.MONSTER_HUNTER_RISE,
        SupportedGameNames.MONSTER_HUNTER_WILDS
    ];

    public static int Run()
    {
        Console.WriteLine("HunterPie Linux doctor");
        Console.WriteLine();

        bool ptraceOk = CheckPtrace();
        CheckXdotool();
        CheckAddressMaps();
        bool gameReadable = CheckGames();

        Console.WriteLine();
        Console.WriteLine(gameReadable
            ? "Ready: a supported game is running and its memory is readable."
            : ptraceOk
                ? "No supported game is running (or it was not found). Start the game and run this again."
                : "Fix the ptrace problem above first, then run this again.");

        return gameReadable ? 0 : 1;
    }

    private static bool CheckPtrace()
    {
        const string path = "/proc/sys/kernel/yama/ptrace_scope";

        if (ClientInfo.IsAdmin)
            return Report(true, "ptrace", "running as root, memory access allowed");

        if (!File.Exists(path))
            return Report(true, "ptrace", "Yama is not enabled, memory access of your own processes is allowed");

        string value = File.ReadAllText(path).Trim();

        return value == "0"
            ? Report(true, "ptrace", "kernel.yama.ptrace_scope=0")
            : Report(false, "ptrace", $"kernel.yama.ptrace_scope={value} blocks reading the game's memory. " +
                "Run `sudo sysctl kernel.yama.ptrace_scope=0` (until reboot), or `sudo setcap cap_sys_ptrace=eip <path to HunterPie.Linux>`");
    }

    private static void CheckXdotool() =>
        Report(
            LinuxWindowInspector.IsAvailable,
            "xdotool",
            LinuxWindowInspector.IsAvailable
                ? "found (window titles and focus tracking enabled)"
                : "not found: install it for window titles and focus. Required for Monster Hunter: World (the build number is in the window title)");

    private static void CheckAddressMaps()
    {
        string directory = ClientInfo.AddressPath;
        int count = Directory.Exists(directory) ? Directory.GetFiles(directory, "*.map").Length : 0;

        Report(count > 0, "address maps", count > 0
            ? $"{count} map(s) in {directory}"
            : $"none found in {directory}. Build output should copy HunterPie/Address/*.map there");
    }

    private static bool CheckGames()
    {
        bool anyReadable = false;
        bool anyFound = false;

        foreach (int pid in LinuxProcFs.EnumeratePids())
        {
            string[] commandLine = LinuxProcFs.ReadCommandLine(pid);

            if (LinuxProcFs.GetExecutableName(commandLine) is not { } name
                || !GameNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;

            if (LinuxProcFs.FindImage(pid, name) is not { } image)
                continue;

            anyFound = true;
            string version = PeVersionReader.ReadFileVersion(image.Path) ?? "unknown";
            string title = LinuxWindowInspector.GetWindowTitle(pid);
            string mapName = $"{name}.{version}.map";
            bool hasMap = File.Exists(Path.Combine(ClientInfo.AddressPath, mapName));

            Report(true, "game", $"{name} pid {pid}, image base 0x{image.BaseAddress:X}, version {version}, title '{title}'");

            // World's maps are named after the build number in the window title, not the file version
            if (!name.Equals(SupportedGameNames.MONSTER_HUNTER_WORLD, StringComparison.OrdinalIgnoreCase))
                Report(hasMap, "address map", hasMap ? mapName : $"{mapName} is missing: this game version is not supported yet");

            bool readable = CanReadMemory(pid, image.BaseAddress);
            anyReadable |= readable;
        }

        if (!anyFound)
            Report(false, "game", "no Monster Hunter World/Rise/Wilds process found (looked for <name>.exe mapped by Wine/Proton)");

        return anyReadable;
    }

    private static bool CanReadMemory(int pid, nint address)
    {
        try
        {
            using var memory = new HunterPie.Platforms.Linux.Memory.LinuxMemory(pid);
            short magic = memory.ReadAsync<short>(address).GetAwaiter().GetResult();

            // Every Windows executable starts with "MZ"
            return magic == 0x5A4D
                ? Report(true, "memory", "read the PE header (MZ) from the game's memory")
                : Report(false, "memory", $"opened /proc/{pid}/mem but read 0x{magic:X4} instead of the MZ header");
        }
        catch (UnauthorizedAccessException)
        {
            return Report(false, "memory", $"permission denied opening /proc/{pid}/mem (see ptrace above)");
        }
        catch (IOException ex)
        {
            return Report(false, "memory", ex.Message);
        }
    }

    private static bool Report(bool ok, string label, string message)
    {
        Console.WriteLine($"  [{(ok ? " ok " : "FAIL")}] {label}: {message}");

        return ok;
    }
}

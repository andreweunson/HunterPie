namespace HunterPie.Linux.Bootstrap;

internal sealed record HostOptions(
    bool ShowHelp,
    bool Doctor,
    bool Verbose,
    TimeSpan? RunFor,
    bool WriteLogs = true,
    bool FindMap = false,
    bool FindCrypto = false,
    bool DumpSkills = false,
    string? DumpLabel = null,
    string[]? DiffSkills = null
)
{
    public const string Usage = """
        HunterPie for Linux (headless)

        Usage: HunterPie.Linux [options]

          --doctor        Check the environment (ptrace, xdotool, address maps) and look for a running game, then exit
          --find-map      For a Monster Hunter Wilds version that has no address map yet: work out the map from the
                          newest older one by measuring how far the game's globals moved, then exit. Run it with the
                          game open and your character loaded into the world
          --find-crypto   Check that the encryption key in the Wilds map decrypts your character's stats to believable
                          numbers and, if not, look for where the key moved to. Game open, character loaded
          --dump-skills [label]
                          Save the Wilds skill table (where the game keeps skill timers) to a file in the log folder,
                          for finding skills HunterPie does not track. Game open, standing in a hunting area
          --diff-skills <off> <on> [<off-again>]
                          Compare two or three of those dumps and show which slots changed, then exit
          --verbose       Also print debug logs from HunterPie
          --run-for <s>   Exit by itself after <s> seconds (useful for scripts and tests)
          --no-logs       Do not write log files (default: a session log, and one file per hunt with a summary,
                          in ~/.local/state/HunterPie/logs or $HUNTERPIE_LOG_DIR)
          -h, --help      Show this help

        Start the game (through Steam/Proton), then run this. Without --doctor it attaches and prints
        player, quest and monster events until Ctrl+C.
        """;

    public static HostOptions Parse(string[] args)
    {
        bool help = false, doctor = false, verbose = false, noLogs = false, findMap = false, findCrypto = false, dumpSkills = false;
        string? dumpLabel = null;
        var diffFiles = new List<string>();
        TimeSpan? runFor = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h" or "--help":
                    help = true;
                    break;
                case "--doctor":
                    doctor = true;
                    break;
                case "--find-map":
                    findMap = true;
                    break;
                case "--find-crypto":
                    findCrypto = true;
                    break;
                case "--dump-skills":
                    dumpSkills = true;

                    if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                        dumpLabel = args[++i];

                    break;
                case "--diff-skills":
                    while (i + 1 < args.Length && !args[i + 1].StartsWith('-') && diffFiles.Count < 3)
                        diffFiles.Add(args[++i]);

                    if (diffFiles.Count < 2)
                        throw new ArgumentException("--diff-skills expects two or three dump files");

                    break;
                case "--verbose":
                    verbose = true;
                    break;
                case "--no-logs":
                    noLogs = true;
                    break;
                case "--run-for":
                    if (i + 1 >= args.Length || !double.TryParse(args[++i], System.Globalization.CultureInfo.InvariantCulture, out double seconds) || seconds <= 0)
                        throw new ArgumentException("--run-for expects a positive number of seconds");

                    runFor = TimeSpan.FromSeconds(seconds);
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{args[i]}'");
            }
        }

        return new HostOptions(
            help,
            doctor,
            verbose,
            runFor,
            !noLogs,
            findMap,
            findCrypto,
            dumpSkills,
            dumpLabel,
            diffFiles.Count > 0 ? diffFiles.ToArray() : null);
    }
}

namespace HunterPie.Linux.Bootstrap;

internal sealed record HostOptions(
    bool ShowHelp,
    bool Doctor,
    bool Verbose,
    TimeSpan? RunFor
)
{
    public const string Usage = """
        HunterPie for Linux (headless)

        Usage: HunterPie.Linux [options]

          --doctor        Check the environment (ptrace, xdotool, address maps) and look for a running game, then exit
          --verbose       Also print debug logs from HunterPie
          --run-for <s>   Exit by itself after <s> seconds (useful for scripts and tests)
          -h, --help      Show this help

        Start the game (through Steam/Proton), then run this. Without --doctor it attaches and prints
        player, quest and monster events until Ctrl+C.
        """;

    public static HostOptions Parse(string[] args)
    {
        bool help = false, doctor = false, verbose = false;
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
                case "--verbose":
                    verbose = true;
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

        return new HostOptions(help, doctor, verbose, runFor);
    }
}

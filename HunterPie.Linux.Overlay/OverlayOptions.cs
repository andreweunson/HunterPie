namespace HunterPie.Linux.Overlay;

internal sealed record OverlayOptions(bool ShowHelp, bool Edit, bool Demo, bool Verbose, int? Screen)
{
    public const string Usage = """
        HunterPie overlay for Linux

        Usage: HunterPie.Linux.Overlay [options]

          --edit          Start in layout edit mode: drag widgets, scroll over one to resize it
          --demo          Show fake data instead of attaching to a game (to position the widgets)
          --screen <n>    Show the overlay on monitor n (0 = primary), saved to overlay.json
          --verbose       Debug logs
          -h, --help      Show this help

        While running: `kill -USR1 <pid>` toggles layout edit mode (leaving it saves positions to
        ~/.config/HunterPie/overlay.json). Bind that command to a key in your desktop environment.
        The game must run in borderless windowed mode, overlays cannot draw over exclusive fullscreen.
        """;

    public static OverlayOptions Parse(string[] args)
    {
        bool help = false, edit = false, demo = false, verbose = false;
        int? screen = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h" or "--help":
                    help = true;
                    break;
                case "--edit":
                    edit = true;
                    break;
                case "--demo":
                    demo = true;
                    break;
                case "--verbose":
                    verbose = true;
                    break;
                case "--screen":
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], out int index) || index < 0)
                        throw new ArgumentException("--screen expects a monitor number (0 = primary)");

                    screen = index;
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{args[i]}'");
            }
        }

        return new OverlayOptions(help, edit, demo, verbose, screen);
    }
}

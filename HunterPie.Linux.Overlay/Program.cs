using Avalonia;
using HunterPie.Linux.Overlay.Services;

namespace HunterPie.Linux.Overlay;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        OverlayOptions options;

        try
        {
            options = OverlayOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(OverlayOptions.Usage);
            return 2;
        }

        if (options.ShowHelp)
        {
            Console.WriteLine(OverlayOptions.Usage);
            return 0;
        }

        if (options.ToggleEdit && OperatingSystem.IsLinux())
            return InstanceControl.ToggleEdit();

        if (!OperatingSystem.IsLinux())
        {
            Console.Error.WriteLine("HunterPie.Linux.Overlay only runs on Linux. On Windows use the HunterPie WPF client.");
            return 2;
        }

        App.Options = options;

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}

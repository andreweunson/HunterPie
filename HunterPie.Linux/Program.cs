using HunterPie.Core.Observability.Logging;
using HunterPie.DI;
using HunterPie.Linux.Bootstrap;
using HunterPie.Linux.Services;
using System.Runtime.InteropServices;

namespace HunterPie.Linux;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        HostOptions options;

        try
        {
            options = HostOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(HostOptions.Usage);
            return 2;
        }

        if (options.ShowHelp)
        {
            Console.WriteLine(HostOptions.Usage);
            return 0;
        }

        if (!OperatingSystem.IsLinux())
        {
            Console.Error.WriteLine("HunterPie.Linux only runs on Linux. On Windows use the HunterPie WPF client.");
            return 2;
        }

        if (options.Doctor)
        {
            await HostBootstrapper.InitializeAsync(options);

            return Doctor.Run();
        }

        if (options.FindMap)
        {
            await HostBootstrapper.InitializeAsync(options with { WriteLogs = false });

            return MapFinder.Run();
        }

        if (options.FindCrypto)
        {
            await HostBootstrapper.InitializeAsync(options with { WriteLogs = false });

            return MapFinder.RunCrypto();
        }

        using var shutdown = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            TryCancel(shutdown);
        };

        // systemd, `kill` and terminal close send SIGTERM/SIGHUP, shut down cleanly for those too
        using PosixSignalRegistration sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            TryCancel(shutdown);
        });
        using PosixSignalRegistration sighup = PosixSignalRegistration.Create(PosixSignal.SIGHUP, context =>
        {
            context.Cancel = true;
            TryCancel(shutdown);
        });

        if (options.RunFor is { } runFor)
            shutdown.CancelAfter(runFor);

        await HostBootstrapper.InitializeAsync(options);

        ILogger logger = LoggerFactory.Create();
        logger.Info("HunterPie (Linux, headless) started. Waiting for Monster Hunter World/Rise/Wilds, Ctrl+C to quit.");

        using GameSessionController session = DependencyContainer.Get<GameSessionController>();
        session.Start();

        try
        {
            await Task.Delay(Timeout.Infinite, shutdown.Token);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown path
        }

        logger.Info("Shutting down");

        return 0;
    }

    // A signal can arrive while the host is already tearing down and the source is disposed
    private static void TryCancel(CancellationTokenSource source)
    {
        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}

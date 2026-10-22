using HunterPie.Core.Domain.Process.Events;
using HunterPie.Core.Domain.Process.Internal;
using HunterPie.Core.Game;
using HunterPie.Core.Observability.Logging;
using HunterPie.Features.Scan.Service;
using HunterPie.Integrations.Services;
using HunterPie.Linux.Bootstrap;
using HunterPie.Linux.Logging;

namespace HunterPie.Linux.Services;

/// <summary>
/// Headless counterpart of the WPF client's GameContextController: when the watcher attaches to a game, builds the
/// game context, starts the scan loop and hands the context to the reporter. When the game exits, tears it all down.
/// </summary>
internal sealed class GameSessionController(
    IControllableWatcherService watcher,
    IGameContextService gameContextService,
    ScanService scanService) : IDisposable
{
    private readonly ILogger _logger = LoggerFactory.Create();

    private readonly IControllableWatcherService _watcher = watcher;
    private readonly IGameContextService _gameContextService = gameContextService;
    private readonly IControllableScanService _scanService = scanService;

    private CancellationTokenSource? _scanCancellation;
    private Context? _context;
    private ConsoleReporter? _reporter;
    private HuntRecorder? _recorder;

    /// <summary>
    /// Raised with the new game context before the scan loop starts, so listeners never miss the first events
    /// </summary>
    public event EventHandler<Context>? ContextStarted;

    /// <summary>
    /// Raised when the game closes, before the context is disposed
    /// </summary>
    public event EventHandler? ContextEnded;

    /// <summary>
    /// Print events to the console. Turn off for hosts that show them some other way (the overlay).
    /// </summary>
    public bool ReportToConsole { get; set; } = true;

    public void Start()
    {
        _watcher.ProcessStart += OnProcessStart;
        _watcher.ProcessExit += OnProcessExit;
        _watcher.Start();
    }

    private void OnProcessStart(object? sender, ProcessEventArgs e)
    {
        try
        {
            _scanCancellation = new CancellationTokenSource();
            _context = _gameContextService.Get(e.Game);
            _logger.Info($"Attached to {e.Game.Name} (pid {e.Game.SystemProcess.Id}), scanning...");

            if (ReportToConsole)
                _reporter = new ConsoleReporter(_context);

            if (HostBootstrapper.LogSink is { } sink)
                _recorder = new HuntRecorder(_context, sink);

            ContextStarted?.Invoke(this, _context);
            _scanService.Start(_scanCancellation.Token);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to start the game session: {ex}");
            Teardown();
        }
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        _logger.Info("Game has closed");
        ContextEnded?.Invoke(this, EventArgs.Empty);
        Teardown();
    }

    private void Teardown()
    {
        CancellationTokenSource? scanCancellation = _scanCancellation;

        scanCancellation?.Cancel();
        _reporter?.Dispose();
        _recorder?.Dispose();
        _context?.Dispose();

        // The scan loop may still be observing the token for a moment after Cancel(); disposing right away makes it throw
        if (scanCancellation is { })
            _ = Task.Delay(TimeSpan.FromSeconds(2)).ContinueWith(_ => scanCancellation.Dispose(), TaskScheduler.Default);

        _reporter = null;
        _recorder = null;
        _context = null;
        _scanCancellation = null;
    }

    public void Dispose()
    {
        _watcher.ProcessStart -= OnProcessStart;
        _watcher.ProcessExit -= OnProcessExit;
        Teardown();
        (_watcher as IDisposable)?.Dispose();
    }
}

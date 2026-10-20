using HunterPie.Core.Address.Map;
using HunterPie.Core.Domain.Interfaces;
using HunterPie.Core.Domain.Process;
using HunterPie.Core.Domain.Process.Entity;
using HunterPie.Core.Domain.Process.Events;
using HunterPie.Core.Domain.Process.Exceptions;
using HunterPie.Core.Domain.Process.Internal;
using HunterPie.Core.Domain.Process.Service;
using HunterPie.Core.Extensions;
using HunterPie.Core.Observability.Logging;
using HunterPie.Platforms.Linux.Memory;
using HunterPie.Platforms.Linux.Pe;
using SystemProcess = System.Diagnostics.Process;

namespace HunterPie.Platforms.Linux.Process;

/// <summary>
/// Looks for the supported games running under Wine/Proton by scanning /proc, then reads their memory
/// through /proc/[pid]/mem.
/// </summary>
internal class LinuxProcessWatcher(IProcessAttachStrategy[] strategies) : IControllableWatcherService, IEventDispatcher, IDisposable
{
    private readonly ILogger _logger = LoggerFactory.Create();

    private int _isWatching;
    private Timer? _timer;
    private readonly IProcessAttachStrategy[] _strategies = strategies;
    private readonly HashSet<string> _failedProcesses = new();

    public LinuxGameProcess? CurrentProcess
    {
        get;
        private set
        {
            if (value == field)
                return;

            field = value;

            if (value is { })
                this.Dispatch(
                    toDispatch: ProcessStart,
                    data: new ProcessEventArgs
                    {
                        Game = value
                    });
            else
                this.Dispatch(
                    toDispatch: ProcessExit,
                    data: EventArgs.Empty);
        }
    }

    public event EventHandler<ProcessEventArgs>? ProcessStart;
    public event EventHandler<EventArgs>? ProcessExit;

    public void Start()
    {
        foreach (IProcessAttachStrategy strategy in _strategies)
        {
            _logger.Info($"Waiting for process '{strategy.Name}' to start...");
            strategy.SetStatus(ProcessStatus.Waiting);
        }

        if (!LinuxWindowInspector.IsAvailable)
            _logger.Warning("xdotool was not found: window titles and focus tracking are unavailable. Install xdotool (works on X11 and XWayland).");

        _timer = new Timer(
            callback: Watch,
            state: null,
            dueTime: TimeSpan.Zero,
            // Scanning /proc is more expensive than asking Windows by name, so poll slower than the Windows watcher
            period: TimeSpan.FromMilliseconds(500)
        );
    }

    private async void Watch(object? _)
    {
        if (Interlocked.Exchange(ref _isWatching, 1) == 1)
            return;

        try
        {
            if (CurrentProcess?.SystemProcess is { } running && HasExited(running))
            {
                CurrentProcess.Dispose();
                CurrentProcess = null;
                return;
            }

            if (CurrentProcess is { } current)
            {
                _strategies
                    .Where(it => it.Status != ProcessStatus.Hooked && it.Status != ProcessStatus.Paused)
                    .ForEach(it => it.SetStatus(ProcessStatus.Paused));

                await current.UpdateAsync();
                return;
            }

            IProcessAttachStrategy[] pending = _strategies
                .Where(strategy => !_failedProcesses.Contains(strategy.Name))
                .ToArray();

            if (pending.Length == 0)
                return;

            Dictionary<string, int> candidates = FindGameProcesses(pending);

            foreach (IProcessAttachStrategy strategy in pending)
            {
                strategy.SetStatus(ProcessStatus.Waiting);

                if (candidates.TryGetValue(strategy.Name, out int pid))
                    await Task.Run(() => FindAndAttach(strategy, pid));

                if (CurrentProcess is { })
                    break;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isWatching, 0);
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        CurrentProcess?.Dispose();
    }

    private static bool HasExited(SystemProcess process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    /// <summary>
    /// Maps each strategy name (executable name without .exe) to the pid of the matching process
    /// </summary>
    private static Dictionary<string, int> FindGameProcesses(IProcessAttachStrategy[] strategies)
    {
        var found = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> wanted = strategies.Select(it => it.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (int pid in LinuxProcFs.EnumeratePids())
        {
            string[] commandLine = LinuxProcFs.ReadCommandLine(pid);

            if (LinuxProcFs.GetExecutableName(commandLine) is not { } name || !wanted.Contains(name))
                continue;

            // Launcher wrappers (reaper, pressure-vessel, proton) may carry the exe name in their arguments,
            // only the real Wine process has the PE image mapped
            if (LinuxProcFs.FindImage(pid, name) is null)
                continue;

            found.TryAdd(name, pid);
        }

        return found;
    }

    private void FindAndAttach(IProcessAttachStrategy strategy, int pid)
    {
        try
        {
            if (LinuxProcFs.FindImage(pid, strategy.Name) is not { } image)
                return;

            var candidate = new ProcessCandidate(
                Id: pid,
                Name: strategy.Name,
                WindowTitle: LinuxWindowInspector.GetWindowTitle(pid),
                FileVersion: PeVersionReader.ReadFileVersion(image.Path),
                BaseAddress: image.BaseAddress
            );

            if (!strategy.CanAttach(candidate))
                return;

            CurrentProcess = AttachToGame(strategy, candidate);
            return;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Error(
                "Failed to open the game's memory. Linux blocks reading other processes by default, either:\n" +
                "  - run: sudo sysctl kernel.yama.ptrace_scope=0 (until reboot), or\n" +
                "  - grant the capability: sudo setcap cap_sys_ptrace=eip <path to the HunterPie executable>\n" +
                $"Details: {ex.Message}");
        }
        catch (UnsupportedGamePatchException err)
        {
            _logger.Error($"Current version ({err.Version}) of {err.Game} is not supported.\n" +
                $"This usually happens due to a new patch of the game that hasn't been mapped yet, please wait patiently until the developer has time to look into it.");
        }
        catch (Exception err)
        {
            _logger.Error($"Error details: {err}");
        }

        _failedProcesses.Add(strategy.Name);
    }

    private static LinuxGameProcess AttachToGame(
        IProcessAttachStrategy strategy,
        ProcessCandidate candidate)
    {
        var memory = new LinuxMemory(candidate.Id);

        strategy.SetStatus(ProcessStatus.Hooked);

        AddressMap.Add("BASE", candidate.BaseAddress);

        return new LinuxGameProcess
        {
            SystemProcess = SystemProcess.GetProcessById(candidate.Id),
            Name = strategy.Name,
            Type = strategy.Game,
            Memory = memory
        };
    }
}

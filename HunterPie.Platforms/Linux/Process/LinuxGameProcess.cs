using HunterPie.Core.Domain.Enums;
using HunterPie.Core.Domain.Memory;
using HunterPie.Core.Domain.Process.Entity;
using HunterPie.Core.Extensions;
using SystemProcess = System.Diagnostics.Process;

namespace HunterPie.Platforms.Linux.Process;

internal class LinuxGameProcess : IGameProcess, IDisposable
{
    private static readonly TimeSpan FocusPollInterval = TimeSpan.FromMilliseconds(500);

    private DateTime _lastFocusCheck = DateTime.MinValue;
    private bool? _hasFocus;

    public required SystemProcess SystemProcess { get; init; }
    public required string Name { get; init; }
    public required GameProcessType Type { get; init; }
    public required IMemoryAsync Memory { get; init; }

    public event EventHandler<EventArgs>? Focus;
    public event EventHandler<EventArgs>? Blur;

    public async Task UpdateAsync()
    {
        // Without a window inspector there's no way to know, so the game is assumed to be focused and overlays stay visible
        if (!LinuxWindowInspector.IsAvailable)
        {
            Notify(true);
            return;
        }

        if (DateTime.UtcNow - _lastFocusCheck < FocusPollInterval)
            return;

        _lastFocusCheck = DateTime.UtcNow;

        int? activePid = await Task.Run(LinuxWindowInspector.GetActiveWindowPid);

        // Unknown (e.g. a native Wayland window is focused): keep the previous state
        if (activePid is not { } pid)
            return;

        Notify(pid == SystemProcess.Id);
    }

    private void Notify(bool isFocused)
    {
        if (_hasFocus == isFocused)
            return;

        _hasFocus = isFocused;

        this.Dispatch(
            toDispatch: isFocused ? Focus : Blur,
            data: EventArgs.Empty
        );
    }

    public void Dispose()
    {
        (Memory as IDisposable)?.Dispose();
        SystemProcess.Dispose();
    }
}

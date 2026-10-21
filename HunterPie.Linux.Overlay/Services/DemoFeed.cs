using Avalonia.Threading;
using HunterPie.Linux.Overlay.ViewModels;

namespace HunterPie.Linux.Overlay.Services;

/// <summary>
/// Fake monsters and a running quest timer, so the layout can be set up (and the overlay checked) without a game.
/// </summary>
internal sealed class DemoFeed : IDisposable
{
    private readonly OverlayViewModel _viewModel;
    private readonly MonsterViewModel _first = new("Quematrice", 5760, 5760, false);
    private readonly MonsterViewModel _second = new("Rathian", 9200, 9200, false);
    private readonly DispatcherTimer _timer;
    private readonly DateTime _start = DateTime.UtcNow;

    public DemoFeed(OverlayViewModel viewModel)
    {
        _viewModel = viewModel;
        _viewModel.IsDemo = true;
        _viewModel.Monsters.Add(_first);
        _viewModel.Monsters.Add(_second);

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => Tick());
        _timer.Start();
        Tick();
    }

    private void Tick()
    {
        double seconds = (DateTime.UtcNow - _start).TotalSeconds;

        // Health drains over 60s then starts over; enrage kicks in for a few seconds each cycle
        double cycle = seconds % 60 / 60;
        _first.Set("Quematrice", 5760 * (1 - cycle), 5760, cycle is > 0.3 and < 0.5);
        _second.Set("Rathian", 9200 * (1 - cycle * 0.7), 9200, false);

        _viewModel.Quest.Set("Demo hunt", TimeSpan.FromMinutes(50) - TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(seconds));
    }

    public void Dispose() => _timer.Stop();
}

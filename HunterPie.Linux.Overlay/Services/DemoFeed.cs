using Avalonia.Threading;
using HunterPie.Core.Game.Entity.Enemy;
using HunterPie.Core.Game.Enums;
using HunterPie.Linux.Overlay.ViewModels;

namespace HunterPie.Linux.Overlay.Services;

/// <summary>
/// Fake monsters and a running quest timer, so the layout can be set up (and the overlay checked) without a game.
/// </summary>
internal sealed class DemoFeed : IDisposable
{
    private readonly OverlayViewModel _viewModel;
    private readonly MonsterViewModel _first;
    private readonly MonsterViewModel _second;
    private readonly DispatcherTimer _timer;
    private readonly DateTime _start = DateTime.UtcNow;
    private long _lastDamage;

    public DemoFeed(OverlayViewModel viewModel)
    {
        _viewModel = viewModel;
        _viewModel.IsDemo = true;

        // Icon ids are real ones, so with the option on the demo fetches (and caches) these two portraits
        _first = new MonsterViewModel("Quematrice", 5760, 5760, false, viewModel.MonsterOptions, "Wilds_20");
        _first.SetCapture(0.25);
        _first.SetDetails([Element.Fire, Element.Ice], VariantType.Normal);

        _second = new MonsterViewModel("Rathian", 9200, 9200, false, viewModel.MonsterOptions, "Wilds_0");
        _second.SetCapture(0.2);
        _second.SetDetails([Element.Dragon, Element.Thunder, Element.Water], VariantType.Tempered);

        _first.EnsureIcon();
        _second.EnsureIcon();
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

        // Stamina drains and refills so the bar can be seen moving
        _first.SetStamina(1000 * (1 - seconds % 20 / 20), 1000);
        _second.SetStamina(800 * (seconds % 30 / 30), 800);

        if (System.Diagnostics.Stopwatch.GetElapsedTime(_lastDamage) >= TimeSpan.FromMilliseconds(250))
        {
            _lastDamage = System.Diagnostics.Stopwatch.GetTimestamp();
            _viewModel.Damage.Demo(seconds);
        }

        _viewModel.Quest.Set("Demo hunt", TimeSpan.FromMinutes(50) - TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(seconds));
    }

    public void Dispose() => _timer.Stop();
}

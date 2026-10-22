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
    private readonly AilmentViewModel _enrage = new("STATUS_ENRAGE", "Enrage");
    private readonly AilmentViewModel _poison = new("AILMENT_POISON", "Poison");
    private readonly AilmentViewModel _paralysis = new("AILMENT_PARALYSIS", "Paralysis");
    private readonly AilmentViewModel _sleep = new("AILMENT_SLEEP", "Sleep");
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

        _first.AddAilment(_enrage);
        _first.AddAilment(_poison);
        _first.AddAilment(_paralysis);
        _second.AddAilment(_sleep);

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

        // Enrage lasts 12s of each cycle, poison 20s of every 30s, paralysis and sleep build up and reset
        double enrageLeft = Math.Max(0, (0.5 - cycle) * 60);
        _enrage.Update(cycle is > 0.3 and < 0.5 ? (float)enrageLeft : 0, 12, 0, 0, seconds > 18 ? 1 : 0);

        double poisonPhase = seconds % 30;
        _poison.Update(poisonPhase < 20 ? (float)(20 - poisonPhase) : 0, 20, poisonPhase < 20 ? 0 : (float)((poisonPhase - 20) * 10), 100, 1 + (int)(seconds / 30));
        _paralysis.Update(0, 0, (float)(seconds % 24 / 24 * 100), 100, 0);
        _sleep.Update(0, 0, (float)(seconds % 40 / 40 * 100), 100, 2);

        _first.RefreshAilments();
        _second.RefreshAilments();

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

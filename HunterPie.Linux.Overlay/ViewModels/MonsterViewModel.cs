using HunterPie.Core.Game.Entity.Enemy;

namespace HunterPie.Linux.Overlay.ViewModels;

/// <summary>
/// What the monster widget shows for one monster. The game raises events from its scan threads, so instead of
/// reacting to each one this is refreshed on the UI timer, which also caps redraws at the timer rate.
/// </summary>
public sealed class MonsterViewModel : ViewModelBase
{
    private string _name = string.Empty;
    private double _percent;
    private string _healthText = string.Empty;
    private bool _isEnraged;

    public IMonster? Source { get; }

    public string Name { get => _name; private set => Set(ref _name, value); }

    /// <summary>0 to 100</summary>
    public double Percent
    {
        get => _percent;
        private set
        {
            if (!Set(ref _percent, value))
                return;

            Raise(nameof(PercentText));
            Raise(nameof(IsLow));
            Raise(nameof(IsMedium));
        }
    }

    public string PercentText => $"{Percent:0}%";

    public string HealthText { get => _healthText; private set => Set(ref _healthText, value); }

    public bool IsEnraged { get => _isEnraged; private set => Set(ref _isEnraged, value); }

    /// <summary>Below 25%: the bar turns red</summary>
    public bool IsLow => Percent < 25;

    /// <summary>Below 50%: the bar turns amber</summary>
    public bool IsMedium => Percent is >= 25 and < 50;

    public MonsterViewModel(IMonster source)
    {
        Source = source;
        Refresh();
    }

    // Used by the demo feed and previews
    public MonsterViewModel(string name, double health, double maxHealth, bool isEnraged)
    {
        Set(name, health, maxHealth, isEnraged);
    }

    public void Refresh()
    {
        if (Source is not { } monster)
            return;

        Set(monster.Name, monster.Health, monster.MaxHealth, monster.IsEnraged);
    }

    public void Set(string name, double health, double maxHealth, bool isEnraged)
    {
        Name = name.Trim();
        HealthText = maxHealth > 0 ? $"{health:N0} / {maxHealth:N0}" : "-";
        Percent = maxHealth > 0 ? Math.Clamp(health / maxHealth * 100, 0, 100) : 0;
        IsEnraged = isEnraged;
    }
}

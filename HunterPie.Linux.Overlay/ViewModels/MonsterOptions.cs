namespace HunterPie.Linux.Overlay.ViewModels;

/// <summary>What the monster cards show. One instance is shared by every card, so a checkbox changes all of them</summary>
public sealed class MonsterOptions : ViewModelBase
{
    private bool _showIcons = true;
    private bool _showStamina = true;
    private bool _showWeaknesses = true;
    private bool _showAilments = true;

    /// <summary>Monster portraits. Turning this off also stops them being downloaded</summary>
    public bool ShowIcons { get => _showIcons; set => Set(ref _showIcons, value); }

    public bool ShowStamina { get => _showStamina; set => Set(ref _showStamina, value); }

    public bool ShowWeaknesses { get => _showWeaknesses; set => Set(ref _showWeaknesses, value); }

    /// <summary>Rings for the ailments (enrage, poison, paralysis, ...) that are building up or active</summary>
    public bool ShowAilments { get => _showAilments; set => Set(ref _showAilments, value); }
}

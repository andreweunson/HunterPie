using HunterPie.Core.Client.Configuration.Enums;
using HunterPie.Linux.Overlay.Services;

namespace HunterPie.Linux.Overlay.ViewModels;

/// <summary>What the monster cards show. One instance is shared by every card, so a checkbox changes all of them</summary>
public sealed class MonsterOptions : ViewModelBase
{
    private bool _showIcons = true;
    private bool _showStamina = true;
    private bool _showWeaknesses = true;
    private bool _showAilments = true;
    private bool _targeting = true;
    private bool _onlyTarget = true;
    private TargetModeType _targetMode = TargetModeType.Infer;

    /// <summary>Ailments that never get a ring. Shared by every card, so an edit applies to all of them at once</summary>
    public AilmentBlacklist Blacklist { get; } = new();

    /// <summary>Monster portraits. Turning this off also stops them being downloaded</summary>
    public bool ShowIcons { get => _showIcons; set => Set(ref _showIcons, value); }

    public bool ShowStamina { get => _showStamina; set => Set(ref _showStamina, value); }

    public bool ShowWeaknesses { get => _showWeaknesses; set => Set(ref _showWeaknesses, value); }

    /// <summary>Rings for the ailments (enrage, poison, paralysis, ...) that are building up or active</summary>
    public bool ShowAilments { get => _showAilments; set => Set(ref _showAilments, value); }

    /// <summary>Pick the monsters to show the way HunterPie does. Off shows every monster</summary>
    public bool TargetingEnabled { get => _targeting; set => Set(ref _targeting, value); }

    /// <summary>Hide the other monsters while one is targeted (and while none is, hide all). Off falls back to all</summary>
    public bool ShowOnlyTarget { get => _onlyTarget; set => Set(ref _onlyTarget, value); }

    public TargetModeType TargetMode
    {
        get => _targetMode;
        set
        {
            if (Set(ref _targetMode, value))
                Raise(nameof(TargetModeIndex));
        }
    }

    /// <summary>Index into <see cref="TargetModes"/>, for the settings combo box</summary>
    public int TargetModeIndex
    {
        get => Array.IndexOf(TargetModes, _targetMode);
        set { if (value >= 0 && value < TargetModes.Length) TargetMode = TargetModes[value]; }
    }

    public static TargetModeType[] TargetModes { get; } = [TargetModeType.Infer, TargetModeType.LockOn, TargetModeType.MapPin];
}

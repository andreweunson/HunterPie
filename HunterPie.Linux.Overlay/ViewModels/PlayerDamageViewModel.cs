using HunterPie.Core.Game.Entity.Party;
using HunterPie.Core.Game.Enums;

namespace HunterPie.Linux.Overlay.ViewModels;

/// <summary>
/// One row of the damage widget. Text is only formatted when the underlying number changes: the widget refreshes a few
/// times a second but damage usually moves less often, and formatting is the only per-row allocation.
/// </summary>
public sealed class PlayerDamageViewModel : ViewModelBase
{
    /// <summary>Colour slot for players, this one is used for support hunters</summary>
    public const int CompanionSlot = 4;

    private string _name = string.Empty;
    private int _damage = -1;
    private int _dpsTenths = -1;
    private int _sharePercent = -1;
    private double _share;
    private string _damageText = "0";
    private string _dpsText = "0.0";
    private string _shareText = "0%";

    public IPartyMember? Source { get; }

    /// <summary>Quest time (seconds) at which this member was first seen, used for the DPS denominator</summary>
    public double JoinedAt { get; }

    /// <summary>0-3 for players in party order, <see cref="CompanionSlot"/> for support hunters</summary>
    public int Slot { get; }

    public bool IsSelf { get; }

    public bool IsCompanion => Slot == CompanionSlot;

    public int Damage => Math.Max(_damage, 0);

    public string Name { get => _name; private set => Set(ref _name, value); }

    /// <summary>Share of the team's damage, 0-100</summary>
    public double Share { get => _share; private set => Set(ref _share, value); }

    public string DamageText { get => _damageText; private set => Set(ref _damageText, value); }

    public string DpsText { get => _dpsText; private set => Set(ref _dpsText, value); }

    public string ShareText { get => _shareText; private set => Set(ref _shareText, value); }

    public PlayerDamageViewModel(IPartyMember source, double joinedAt)
    {
        Source = source;
        JoinedAt = joinedAt;
        IsSelf = source.IsMyself;
        Slot = source.Type == MemberType.Player ? Math.Clamp(source.Slot, 0, CompanionSlot - 1) : CompanionSlot;
        Name = source.Name;
    }

    // Demo feed and edit mode placeholders
    public PlayerDamageViewModel(string name, int slot, bool isSelf)
    {
        Name = name;
        Slot = slot;
        IsSelf = isSelf;
    }

    public void Set(int damage, double dps, double share)
    {
        if (damage != _damage)
        {
            _damage = damage;
            DamageText = damage.ToString("N0");
        }

        int tenths = (int)(dps * 10);
        if (tenths != _dpsTenths)
        {
            _dpsTenths = tenths;
            DpsText = (tenths / 10.0).ToString("N1");
        }

        int percent = (int)Math.Round(share);
        if (percent != _sharePercent)
        {
            _sharePercent = percent;
            ShareText = $"{percent}%";
        }

        Share = share;
    }

    public void SetName(string name) => Name = name;
}

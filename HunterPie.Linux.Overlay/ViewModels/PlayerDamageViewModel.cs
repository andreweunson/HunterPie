using HunterPie.Core.Game.Entity.Party;
using HunterPie.Core.Game.Entity.Player;
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

    /// <summary>Samples kept for the DPS graph, see <see cref="DamageViewModel"/></summary>
    public const int HistoryCapacity = 240;

    private int _raw = int.MinValue;
    private int _elemental = int.MinValue;
    private int _affinity = int.MinValue;
    private string _rawText = string.Empty;
    private string _elementalText = string.Empty;
    private string _affinityText = string.Empty;
    private bool _hasStats;
    private bool _showStats;

    public IPartyMember? Source { get; }

    /// <summary>DPS samples for the graph, NaN where this member had not joined yet</summary>
    public float[] History { get; } = CreateHistory();

    public double Dps { get; private set; }

    /// <summary>Attack, elemental attack and affinity, only for players whose gear status is being read</summary>
    public bool HasStats { get => _hasStats; private set { if (Set(ref _hasStats, value)) Raise(nameof(StatsVisible)); } }

    /// <summary>Whether the user turned the stats line on</summary>
    public bool ShowStats { get => _showStats; set { if (Set(ref _showStats, value)) Raise(nameof(StatsVisible)); } }

    public bool StatsVisible => HasStats && ShowStats;

    public string RawText { get => _rawText; private set => Set(ref _rawText, value); }

    public string ElementalText { get => _elementalText; private set => Set(ref _elementalText, value); }

    public string AffinityText { get => _affinityText; private set => Set(ref _affinityText, value); }

    private static float[] CreateHistory()
    {
        float[] history = new float[HistoryCapacity];
        Array.Fill(history, float.NaN);
        return history;
    }

    /// <summary>Quest time (seconds) at which this member was first seen, used for the DPS denominator</summary>
    public double JoinedAt { get; }

    /// <summary>0-3 for players in party order, <see cref="CompanionSlot"/> for support hunters</summary>
    public int Slot { get; }

    public bool IsSelf { get; }

    public bool IsCompanion => Slot == CompanionSlot;

    public int Damage => Math.Max(_damage, 0);

    private Weapon _weapon = Weapon.None;

    /// <summary>Weapon.None (also used for support hunters without one) shows an empty slot</summary>
    public Weapon Weapon { get => _weapon; private set => Set(ref _weapon, value); }

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
        Weapon = source.Weapon;
    }

    // Demo feed and edit mode placeholders
    public PlayerDamageViewModel(string name, int slot, bool isSelf, Weapon weapon = Weapon.None)
    {
        Name = name;
        Weapon = weapon;
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
        Dps = dps;
    }

    /// <summary>Gear status of the hunter, read by the host's party scan. Null for support hunters</summary>
    public void SetStatus(IPlayerStatus? status)
    {
        if (status is null)
        {
            HasStats = false;
            return;
        }

        SetStats(status.RawDamage, status.ElementalDamage, status.Affinity);
    }

    public void SetStats(double raw, double elemental, double affinity)
    {
        int rawValue = (int)Math.Round(raw);
        int elementalValue = (int)Math.Round(elemental);
        int affinityValue = (int)Math.Round(affinity);

        if (rawValue != _raw)
        {
            _raw = rawValue;
            RawText = rawValue.ToString();
        }

        if (elementalValue != _elemental)
        {
            _elemental = elementalValue;
            ElementalText = elementalValue.ToString();
        }

        if (affinityValue != _affinity)
        {
            _affinity = affinityValue;
            AffinityText = $"{affinityValue}%";
        }

        // Nothing read yet (the scan has not reached this member) looks like all zeros
        HasStats = rawValue > 0 || elementalValue > 0 || affinityValue != 0;
    }

    public void SetName(string name) => Name = name;

    public void SetWeapon(Weapon weapon) => Weapon = weapon;
}

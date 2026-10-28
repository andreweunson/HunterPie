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

    public IPartyMember? Source { get; private set; }

    // A hunter who leaves and comes back (support hunters are swapped in and out as the party changes) keeps this row.
    // The game starts the returning hunter's damage from zero again, so the earlier stays are kept here and added.
    private int _stayPeak;

    /// <summary>Damage dealt in earlier stays in the party</summary>
    public int Carried { get; private set; }

    /// <summary>Total damage for a reading of the member's own counter, remembering the highest of this stay</summary>
    public int Observe(int raw)
    {
        if (raw > _stayPeak)
            _stayPeak = raw;

        return Carried + raw;
    }

    /// <summary>The hunter is back under a new party member object</summary>
    public void Rebind(IPartyMember source)
    {
        // A counter that carried on is the same total; one that restarted is a new stay
        if (Math.Max(source.Damage, 0) < _stayPeak)
        {
            Carried += _stayPeak;
            _stayPeak = 0;
        }

        Source = source;
        Slot = source.Type == MemberType.Player ? Math.Clamp(source.Slot, 0, CompanionSlot - 1) : CompanionSlot;
        Name = source.Name;
        Weapon = source.Weapon;
    }

    // One history per graph mode, so switching modes in the settings keeps what was already recorded
    private readonly float[] _perSecondHistory = CreateHistory();
    private readonly float[] _rollingHistory = CreateHistory();
    private readonly float[] _totalHistory = CreateHistory();

    // Damage at the start of each recent second of quest time (and when exactly that was), for the moving average
    private const int RingSize = 256;
    private readonly int[] _ringDamage = new int[RingSize];
    private readonly float[] _ringTime = new float[RingSize];
    private int _ringStart = -1;
    private int _ringSecond = -1;

    // The last RecentCapacity seconds of moving average, newest last, for the graph that discards old points
    public const int RecentCapacity = 120;
    private readonly float[] _recentHistory = CreateRecent();

    /// <summary>The longest moving average window the ring can answer (it must also cover the visible time span)</summary>
    public const int MaxWindowSeconds = 60;

    /// <summary>Which of the histories <see cref="History"/> returns</summary>
    public GraphMode Mode { get; set; }

    /// <summary>With the moving average: use the history that only keeps recent seconds</summary>
    public bool UseRecent { get; set; }

    /// <summary>Quest time of this hunter's first hit, null until they have dealt damage here</summary>
    public double? FirstHitAt { get; private set; }

    public float[] RecentHistory => _recentHistory;

    private static float[] CreateRecent()
    {
        float[] history = new float[RecentCapacity];
        Array.Fill(history, float.NaN);
        return history;
    }

    /// <summary>Samples for the graph in the current mode, NaN where this member had not joined yet</summary>
    public float[] History => Mode switch
    {
        GraphMode.MovingAverage => UseRecent ? _recentHistory : _rollingHistory,
        GraphMode.TotalDamage => _totalHistory,
        _ => _perSecondHistory
    };

    public float[] PerSecondHistory => _perSecondHistory;

    public float[] RollingHistory => _rollingHistory;

    public float[] TotalHistory => _totalHistory;

    /// <summary>
    /// Remembers the damage at the first look in each second of quest time. A refresh that skipped seconds fills them
    /// with the current reading.
    /// </summary>
    public void RecordSecond(double elapsed, int damage)
    {
        int second = (int)elapsed;

        if (second <= _ringSecond)
            return;

        if (_ringStart < 0)
            _ringStart = second;

        // Nothing older than the ring holds can be asked for
        int from = Math.Max(Math.Max(_ringSecond + 1, _ringStart), second - RingSize + 1);

        for (int s = from; s <= second; s++)
        {
            _ringDamage[s % RingSize] = damage;
            _ringTime[s % RingSize] = (float)elapsed;
        }

        _ringSecond = second;
    }

    /// <summary>
    /// Damage per second over the last <paramref name="window"/> seconds. Before that much time has passed since the
    /// hunter was first seen it is the average since then, so the line starts where the plain DPS does.
    /// </summary>
    public double MovingAverage(double elapsed, int damage, int window, DpsStrategy strategy = DpsStrategy.RelativeToJoin)
    {
        double start = StartTime(strategy);

        if (double.IsNaN(start))
            return 0;

        window = Math.Clamp(window, 1, MaxWindowSeconds);
        int past = (int)elapsed - window;

        int pastDamage;
        double pastTime;

        if (_ringStart >= 0 && past >= _ringStart && past <= _ringSecond)
        {
            pastDamage = _ringDamage[past % RingSize];

            // Time before the start of the count (a first hit that came later) is not part of the average
            pastTime = Math.Max(_ringTime[past % RingSize], start);
        }
        else
        {
            pastDamage = BaselineDamage;
            pastTime = start;
        }

        return Math.Max(damage - pastDamage, 0) / Math.Max(1, elapsed - pastTime);
    }

    /// <summary>Throws away the moving average samples, which were made with a different window or DPS strategy</summary>
    public void ClearMovingAverage()
    {
        Array.Fill(_rollingHistory, float.NaN);
        Array.Fill(_recentHistory, float.NaN);
    }

    public void ClearPerSecond() => Array.Fill(_perSecondHistory, float.NaN);

    /// <summary>Notes the moment of the first hit, which is when the first-hit strategy starts counting</summary>
    public void NoteDamage(double elapsed, int damage)
    {
        if (FirstHitAt is null && damage > BaselineDamage)
            FirstHitAt = elapsed;
    }

    /// <summary>Quest time the strategy counts from; NaN while that moment has not happened yet</summary>
    public double StartTime(DpsStrategy strategy) => strategy switch
    {
        DpsStrategy.RelativeToQuest => 0,
        DpsStrategy.RelativeToFirstHit => FirstHitAt ?? double.NaN,
        _ => JoinedAt
    };

    /// <summary>Damage dealt here (the damage from before the quest clock is left out) over the time since the start of the count</summary>
    public double ComputeDps(double elapsed, int damage, DpsStrategy strategy)
    {
        double start = StartTime(strategy);

        if (double.IsNaN(start))
            return 0;

        double seconds = Math.Max(1, elapsed - Math.Min(elapsed, start));

        return Math.Max(damage - BaselineDamage, 0) / seconds;
    }

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

    /// <summary>
    /// Damage the member already had when first seen. It was not dealt within the time DPS is divided by (the quest
    /// clock can start well after the fight did), so it is shown but left out of DPS.
    /// </summary>
    public int BaselineDamage { get; }

    /// <summary>0-3 for players in party order, <see cref="CompanionSlot"/> for support hunters</summary>
    public int Slot { get; private set; }

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

    public PlayerDamageViewModel(IPartyMember source, double joinedAt, int baselineDamage = 0)
    {
        Source = source;
        JoinedAt = joinedAt;
        BaselineDamage = Math.Max(baselineDamage, 0);
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

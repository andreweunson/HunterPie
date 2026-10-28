using HunterPie.Core.Game.Entity.Game;
using HunterPie.Core.Game.Entity.Party;
using HunterPie.Core.Game.Enums;
using System.Collections.ObjectModel;

namespace HunterPie.Linux.Overlay.ViewModels;

/// <summary>
/// The damage widget: one row per party member (and support hunter) sorted by damage. It is polled from the UI
/// timer instead of subscribing to game events, so it never touches the game's scan threads and its cost is a
/// snapshot of at most eight members plus a few multiplications per refresh.
/// </summary>
public sealed class DamageViewModel : ViewModelBase
{
    // A member first seen within this many seconds of the quest clock starting may carry damage from before it
    private const double EarlyQuestSeconds = 15;

    public const int DefaultWindowSeconds = 10;
    public const int MinWindowSeconds = 5;
    public const int DefaultVisibleSeconds = 60;
    public const int MinVisibleSeconds = 10;

    private readonly List<IPartyMember> _snapshot = new(8);

    // Rows of hunters who left, kept so one who comes back picks up where they were
    private readonly List<PlayerDamageViewModel> _departed = new(8);
    private bool _isActive;
    private bool _isEditing;
    private bool _wasInQuest;
    private bool _frozen;
    private bool _primed;
    private string _summary = string.Empty;
    private bool _showGraph = true;
    private bool _showStats = true;
    private int _graphVersion;
    private int _sampleCount;
    private int _sampleStride = 1;
    private double _nextSampleAt;
    private double _graphMax = 10;
    private string _graphMaxText = string.Empty;
    private GraphMode _graphMode = GraphMode.DamagePerSecond;
    private int _windowSeconds = DefaultWindowSeconds;
    private DpsStrategy _strategy = DpsStrategy.RelativeToJoin;
    private bool _discardOld;
    private int _visibleSeconds = DefaultVisibleSeconds;
    private int _recentCount;
    private double _nextRecentAt;
    private int _summaryTotal = -1;
    private int _summarySecond = -1;

    public ObservableCollection<PlayerDamageViewModel> Rows { get; } = new();

    /// <summary>Placeholder rows shown in edit mode while the party is empty</summary>
    public IReadOnlyList<PlayerDamageViewModel> SampleRows { get; } =
    [
        new("Hunter 1", 0, true, Weapon.Greatsword),
        new("Hunter 2", 1, false, Weapon.Bow),
        new("Hunter 3", 2, false, Weapon.SwitchAxe),
        new("Support hunter", PlayerDamageViewModel.CompanionSlot, false, Weapon.Lance),
    ];

    /// <summary>True while a quest runs, or after one ended until the player leaves the hunting zone</summary>
    public bool IsActive { get => _isActive; private set => Set(ref _isActive, value); }

    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (!Set(ref _isEditing, value))
                return;

            Raise(nameof(ShowSample));
            Raise(nameof(ShowSampleGraph));
        }
    }

    public bool ShowSample => IsEditing && Rows.Count == 0;

    /// <summary>User option: DPS graph under the rows</summary>
    public bool ShowGraph
    {
        get => _showGraph;
        set
        {
            if (!Set(ref _showGraph, value))
                return;

            Raise(nameof(ShowGraphPanel));
            Raise(nameof(ShowSampleGraph));
        }
    }

    /// <summary>User option: attack, elemental and affinity under each hunter</summary>
    public bool ShowStats
    {
        get => _showStats;
        set
        {
            if (!Set(ref _showStats, value))
                return;

            for (int i = 0; i < Rows.Count; i++)
                Rows[i].ShowStats = value;

            foreach (PlayerDamageViewModel sample in SampleRows)
                sample.ShowStats = value;
        }
    }

    /// <summary>User option: what the graph plots (DPS since joining, moving average, total damage)</summary>
    public GraphMode GraphMode
    {
        get => _graphMode;
        set
        {
            if (!Set(ref _graphMode, value))
                return;

            Raise(nameof(IsMovingAverage));
            ApplyHistorySelection();
        }
    }

    /// <summary>User option: length of the moving average, in seconds</summary>
    public int WindowSeconds
    {
        get => _windowSeconds;
        set
        {
            value = Math.Clamp(value, MinWindowSeconds, PlayerDamageViewModel.MaxWindowSeconds);

            if (!Set(ref _windowSeconds, value))
                return;

            // The samples already taken were averaged over the old window
            for (int i = 0; i < Rows.Count; i++)
                Rows[i].ClearMovingAverage();

            _recentCount = 0;
            Raise(nameof(GraphCount));
            Raise(nameof(GraphStart));
            RefreshPeak();
            GraphVersion++;
        }
    }

    public bool IsMovingAverage => GraphMode == GraphMode.MovingAverage;

    /// <summary>User option: which moment the seconds in DPS are counted from</summary>
    public DpsStrategy Strategy
    {
        get => _strategy;
        set
        {
            if (!Set(ref _strategy, value))
                return;

            // Samples taken with the old strategy would be a different curve
            for (int i = 0; i < Rows.Count; i++)
            {
                Rows[i].ClearPerSecond();
                Rows[i].ClearMovingAverage();
            }

            _recentCount = 0;
            Raise(nameof(GraphCount));
            Raise(nameof(GraphStart));
            RefreshPeak();
            GraphVersion++;
        }
    }

    /// <summary>User option (moving average only): the graph shows just the latest seconds instead of the whole quest</summary>
    public bool DiscardOldPoints
    {
        get => _discardOld;
        set
        {
            if (!Set(ref _discardOld, value))
                return;

            ApplyHistorySelection();
        }
    }

    /// <summary>User option: how many seconds the graph shows when old points are discarded</summary>
    public int VisibleSeconds
    {
        get => _visibleSeconds;
        set
        {
            value = Math.Clamp(value, MinVisibleSeconds, PlayerDamageViewModel.RecentCapacity);

            if (!Set(ref _visibleSeconds, value))
                return;

            ApplyHistorySelection();
        }
    }

    private bool UsesRecent => DiscardOldPoints && GraphMode == GraphMode.MovingAverage;

    /// <summary>Number of valid slots in the history the graph draws</summary>
    public int GraphCount => UsesRecent ? _recentCount : _sampleCount;

    /// <summary>First slot the graph draws</summary>
    public int GraphStart => UsesRecent ? Math.Max(0, _recentCount - VisibleSeconds) : 0;

    private void ApplyHistorySelection()
    {
        bool recent = UsesRecent;

        for (int i = 0; i < Rows.Count; i++)
        {
            Rows[i].Mode = GraphMode;
            Rows[i].UseRecent = recent;
        }

        Raise(nameof(GraphCount));
        Raise(nameof(GraphStart));
        RefreshPeak();
        GraphVersion++;
    }

    public bool ShowGraphPanel => ShowGraph && HasRows;

    public bool ShowSampleGraph => ShowGraph && ShowSample;

    /// <summary>Bumped whenever a new graph sample is taken; the graph control redraws on it</summary>
    public int GraphVersion { get => _graphVersion; private set => Set(ref _graphVersion, value); }

    /// <summary>Number of valid slots in every row's history</summary>
    public int SampleCount => _sampleCount;

    /// <summary>Top of the graph's vertical axis, with some headroom</summary>
    public double GraphMax { get => _graphMax; private set => Set(ref _graphMax, value); }

    public string GraphMaxText { get => _graphMaxText; private set => Set(ref _graphMaxText, value); }

    /// <summary>Fake history for the edit mode placeholder graph</summary>
    public int SampleGraphCount => 60;

    public double SampleGraphMax => 80;

    /// <summary>"Team total · elapsed"</summary>
    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    public bool HasRows => Rows.Count > 0;

    /// <summary>The demo feed drives the rows itself</summary>
    public bool IsDemo { get; set; }

    public DamageViewModel()
    {
        Rows.CollectionChanged += (_, _) =>
        {
            Raise(nameof(HasRows));
            Raise(nameof(ShowSample));
            Raise(nameof(ShowGraphPanel));
            Raise(nameof(ShowSampleGraph));
        };

        SampleRows[0].Set(18240, 61.3, 45);
        SampleRows[1].Set(11980, 40.2, 30);
        SampleRows[2].Set(7410, 24.9, 18);
        SampleRows[3].Set(2830, 9.5, 7);
        Summary = "40,460 · 04:58";

        double[] peaks = [61, 40, 25, 10];
        double[] stats = [312, 61, 15, 288, 0, 20, 245, 105, 0];
        for (int i = 0; i < SampleRows.Count; i++)
        {
            PlayerDamageViewModel row = SampleRows[i];
            row.ShowStats = ShowStats;

            if (i < 3)
                row.SetStats(stats[i * 3], stats[i * 3 + 1], stats[i * 3 + 2]);

            // A gentle rise that levels off, like a real DPS average
            for (int t = 0; t < SampleGraphCount; t++)
                row.History[t] = (float)(peaks[i] * (1 - Math.Exp(-(t + 4) / 12.0)) * (1 + 0.06 * Math.Sin(t / 4.0 + i)));
        }
    }

    /// <summary>Called from the UI thread a few times a second</summary>
    public void Refresh(IGame? game)
    {
        if (game is null)
        {
            Reset();
            return;
        }

        bool inQuest = game.Quest is not null;

        if (inQuest && !_wasInQuest)
        {
            // A new quest: start from a clean slate
            ClearRows();
            _frozen = false;
            _primed = false;
        }
        else if (!inQuest && _wasInQuest && Rows.Count > 0)
        {
            // Quest over: keep the final numbers on screen instead of letting them reset with the game state
            _frozen = true;
        }

        _wasInQuest = inQuest;

        if (!inQuest && !game.Player.InHuntingZone)
        {
            Reset();
            return;
        }

        IsActive = true;

        if (_frozen)
            return;

        double elapsed = game.TimeElapsed;

        if (!TrySnapshot(game.Player.Party))
            return;

        Reconcile(elapsed);
        Recalculate(elapsed);
    }

    /// <summary>Demo feed: four fake hunters that keep dealing damage</summary>
    public void Demo(double seconds)
    {
        IsActive = true;

        if (Rows.Count == 0)
        {
            Rows.Add(new PlayerDamageViewModel("You", 0, true, Weapon.Greatsword) { ShowStats = ShowStats, Mode = GraphMode, UseRecent = UsesRecent });
            Rows.Add(new PlayerDamageViewModel("Hunter 2", 1, false, Weapon.Bow) { ShowStats = ShowStats, Mode = GraphMode, UseRecent = UsesRecent });
            Rows.Add(new PlayerDamageViewModel("Hunter 3", 2, false, Weapon.SwitchAxe) { ShowStats = ShowStats, Mode = GraphMode, UseRecent = UsesRecent });
            Rows.Add(new PlayerDamageViewModel("Support hunter", PlayerDamageViewModel.CompanionSlot, false, Weapon.Lance) { ShowStats = ShowStats, Mode = GraphMode, UseRecent = UsesRecent });
        }

        double[] rates = [64, 48, 33, 12];
        int total = 0;
        var damages = new int[Rows.Count];

        for (int i = 0; i < Rows.Count; i++)
        {
            damages[i] = (int)(seconds * rates[i % rates.Length] * (1 + 0.15 * Math.Sin(seconds / 4 + i)));
            total += damages[i];
        }

        for (int i = 0; i < Rows.Count; i++)
            Rows[i].Set(damages[i], damages[i] / Math.Max(1, seconds), total > 0 ? damages[i] * 100.0 / total : 0);

        for (int i = 0; i < Rows.Count; i++)
            if (i < 3)
                Rows[i].SetStats(312 - i * 40, 61 - i * 20, 15 + i * 5);

        UpdateSummary(total, seconds);
        Sort();
        RecordSeconds(seconds);
        SampleIfDue(seconds);
    }

    private bool TrySnapshot(IParty party)
    {
        _snapshot.Clear();

        try
        {
            // The scan threads change this collection, and the getter hands out a live view
            _snapshot.AddRange(party.Members);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // Raced with a party update; the next refresh is a fraction of a second away
            return false;
        }
    }

    private void Reconcile(double elapsed)
    {
        for (int i = Rows.Count - 1; i >= 0; i--)
        {
            if (Rows[i].Source is not { } source || _snapshot.Contains(source))
                continue;

            if (_departed.Count >= 16)
                _departed.RemoveAt(0);

            _departed.Add(Rows[i]);
            Rows.RemoveAt(i);
        }

        foreach (IPartyMember member in _snapshot)
        {
            if (Contains(member))
                continue;

            if (TakeDeparted(member) is { } returning)
            {
                returning.Rebind(member);
                returning.ShowStats = ShowStats;
                Rows.Add(returning);
                continue;
            }

            // Members already there when we start looking are assumed to have been there since the beginning. Damage
            // they already have when first seen does not count towards DPS if that is early in the quest (the quest
            // clock can start after the fight did) or if they joined later; attaching mid-quest keeps the old behaviour.
            int baseline = _primed || elapsed < EarlyQuestSeconds ? Math.Max(member.Damage, 0) : 0;
            Rows.Add(new PlayerDamageViewModel(member, _primed || baseline > 0 ? elapsed : 0, baseline) { ShowStats = ShowStats, Mode = GraphMode, UseRecent = UsesRecent });
        }

        if (Rows.Count > 0)
            _primed = true;
    }

    private PlayerDamageViewModel? TakeDeparted(IPartyMember member)
    {
        bool player = member.Type == MemberType.Player;

        for (int i = 0; i < _departed.Count; i++)
        {
            PlayerDamageViewModel row = _departed[i];

            if (row.IsCompanion == player || !string.Equals(row.Name, member.Name, StringComparison.Ordinal))
                continue;

            _departed.RemoveAt(i);
            return row;
        }

        return null;
    }

    private bool Contains(IPartyMember member)
    {
        // Index loops: foreach over an ObservableCollection allocates an enumerator on every call
        for (int i = 0; i < Rows.Count; i++)
            if (ReferenceEquals(Rows[i].Source, member))
                return true;

        return false;
    }

    private void Recalculate(double elapsed)
    {
        int total = 0;

        for (int i = 0; i < Rows.Count; i++)
            total += Rows[i].Carried + Math.Max(Rows[i].Source?.Damage ?? 0, 0);

        for (int i = 0; i < Rows.Count; i++)
        {
            PlayerDamageViewModel row = Rows[i];
            IPartyMember member = row.Source!;
            int damage = row.Observe(Math.Max(member.Damage, 0));

            row.SetName(member.Name);
            row.SetWeapon(member.Weapon);
            row.SetStatus(member.Status);
            row.NoteDamage(elapsed, damage);
            row.Set(damage, row.ComputeDps(elapsed, damage, Strategy), total > 0 ? damage * 100.0 / total : 0);
        }

        UpdateSummary(total, elapsed);
        Sort();
        RecordSeconds(elapsed);
        SampleIfDue(elapsed);
    }

    /// <summary>
    /// Records every member's current DPS for the graph once per <see cref="_sampleStride"/> seconds of quest time.
    /// The history has a fixed size: when it fills up every second sample is dropped and the stride doubles, so the
    /// whole quest always fits and the cost of drawing it stops growing.
    /// </summary>
    private void SampleIfDue(double elapsed)
    {
        bool sampled = SampleRecent(elapsed);

        if (elapsed >= _nextSampleAt)
        {
            _nextSampleAt = Math.Floor(elapsed) + _sampleStride;

            if (_sampleCount >= PlayerDamageViewModel.HistoryCapacity)
                Compact();

            for (int i = 0; i < Rows.Count; i++)
            {
                PlayerDamageViewModel row = Rows[i];
                row.PerSecondHistory[_sampleCount] = (float)row.Dps;
                row.RollingHistory[_sampleCount] = (float)row.MovingAverage(elapsed, row.Damage, WindowSeconds, Strategy);
                row.TotalHistory[_sampleCount] = row.Damage;
            }

            _sampleCount++;
            Raise(nameof(SampleCount));
            sampled = true;
        }

        if (!sampled)
            return;

        Raise(nameof(GraphCount));
        Raise(nameof(GraphStart));
        RefreshPeak();
        GraphVersion++;
    }

    /// <summary>
    /// The moving average of the last <see cref="PlayerDamageViewModel.RecentCapacity"/> seconds, one value per second
    /// and always, so turning "discard old points" on shows a full graph straight away. Slides left once it is full.
    /// </summary>
    private bool SampleRecent(double elapsed)
    {
        if (elapsed < _nextRecentAt)
            return false;

        _nextRecentAt = Math.Floor(elapsed) + 1;
        bool full = _recentCount >= PlayerDamageViewModel.RecentCapacity;

        for (int i = 0; i < Rows.Count; i++)
        {
            PlayerDamageViewModel row = Rows[i];
            float[] recent = row.RecentHistory;

            if (full)
                Array.Copy(recent, 1, recent, 0, recent.Length - 1);

            recent[full ? recent.Length - 1 : _recentCount] = (float)row.MovingAverage(elapsed, row.Damage, WindowSeconds, Strategy);
        }

        if (!full)
            _recentCount++;

        return true;
    }

    private void RecordSeconds(double elapsed)
    {
        for (int i = 0; i < Rows.Count; i++)
            Rows[i].RecordSecond(elapsed, Rows[i].Damage);
    }

    /// <summary>Sets the top of the axis and its caption from everything the current mode's history still holds</summary>
    private void RefreshPeak()
    {
        double max = 0;

        for (int i = 0; i < Rows.Count; i++)
        {
            float[] history = Rows[i].History;

            for (int t = GraphStart; t < GraphCount; t++)
                if (history[t] > max)
                    max = history[t];
        }

        GraphMax = Math.Max(10, max * 1.1);
        GraphMaxText = GraphMode switch
        {
            GraphMode.TotalDamage => $"{max:N0} damage",
            GraphMode.MovingAverage when UsesRecent => $"peak {max:N0} DPS · {WindowSeconds} s average, last {VisibleSeconds} s",
            GraphMode.MovingAverage => $"peak {max:N0} DPS · {WindowSeconds} s average",
            _ => $"peak {max:N0} DPS"
        };
    }

    private void Compact()
    {
        int half = PlayerDamageViewModel.HistoryCapacity / 2;

        for (int i = 0; i < Rows.Count; i++)
        {
            foreach (float[] history in (ReadOnlySpan<float[]>)[Rows[i].PerSecondHistory, Rows[i].RollingHistory, Rows[i].TotalHistory])
            {
                for (int t = 0; t < half; t++)
                    history[t] = history[t * 2];

                Array.Fill(history, float.NaN, half, history.Length - half);
            }
        }

        _sampleCount = half;
        _sampleStride *= 2;
    }

    private void UpdateSummary(int total, double elapsed)
    {
        // The text only has second resolution, so most refreshes format nothing
        int second = (int)elapsed;

        if (total == _summaryTotal && second == _summarySecond)
            return;

        _summaryTotal = total;
        _summarySecond = second;
        Summary = $"{total:N0} · {Format(elapsed)}";
    }

    // Selection sort with Move: n is at most 8 and nothing is touched unless the ranking really changed
    private void Sort()
    {
        for (int target = 0; target < Rows.Count - 1; target++)
        {
            int best = target;

            for (int i = target + 1; i < Rows.Count; i++)
                if (Ranks(Rows[i], Rows[best]))
                    best = i;

            if (best != target)
                Rows.Move(best, target);
        }
    }

    private static bool Ranks(PlayerDamageViewModel a, PlayerDamageViewModel b) =>
        a.Damage != b.Damage ? a.Damage > b.Damage : a.Slot < b.Slot;

    private void ClearRows()
    {
        Rows.Clear();
        _departed.Clear();
        Summary = string.Empty;
        _summaryTotal = -1;
        _summarySecond = -1;
        _sampleCount = 0;
        _sampleStride = 1;
        _nextSampleAt = 0;
        _recentCount = 0;
        _nextRecentAt = 0;
        GraphMax = 10;
        GraphMaxText = string.Empty;
        Raise(nameof(SampleCount));
        Raise(nameof(GraphCount));
        Raise(nameof(GraphStart));
        GraphVersion++;
    }

    private void Reset()
    {
        if (IsDemo)
            return;

        _wasInQuest = false;
        _frozen = false;
        _primed = false;
        IsActive = false;
        _departed.Clear();

        if (Rows.Count > 0)
            ClearRows();
    }

    private static string Format(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));

        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes:00}:{time.Seconds:00}";
    }
}

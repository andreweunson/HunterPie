using HunterPie.Core.Game.Entity.Game;
using HunterPie.Core.Game.Entity.Party;
using System.Collections.ObjectModel;

namespace HunterPie.Linux.Overlay.ViewModels;

/// <summary>
/// The damage widget: one row per party member (and support hunter) sorted by damage. It is polled from the UI
/// timer instead of subscribing to game events, so it never touches the game's scan threads and its cost is a
/// snapshot of at most eight members plus a few multiplications per refresh.
/// </summary>
public sealed class DamageViewModel : ViewModelBase
{
    private readonly List<IPartyMember> _snapshot = new(8);
    private bool _isActive;
    private bool _isEditing;
    private bool _wasInQuest;
    private bool _frozen;
    private bool _primed;
    private string _summary = string.Empty;
    private int _summaryTotal = -1;
    private int _summarySecond = -1;

    public ObservableCollection<PlayerDamageViewModel> Rows { get; } = new();

    /// <summary>Placeholder rows shown in edit mode while the party is empty</summary>
    public IReadOnlyList<PlayerDamageViewModel> SampleRows { get; } =
    [
        new("Hunter 1", 0, true),
        new("Hunter 2", 1, false),
        new("Hunter 3", 2, false),
        new("Support hunter", PlayerDamageViewModel.CompanionSlot, false),
    ];

    /// <summary>True while a quest runs, or after one ended until the player leaves the hunting zone</summary>
    public bool IsActive { get => _isActive; private set => Set(ref _isActive, value); }

    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (Set(ref _isEditing, value))
                Raise(nameof(ShowSample));
        }
    }

    public bool ShowSample => IsEditing && Rows.Count == 0;

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
        };

        SampleRows[0].Set(18240, 61.3, 45);
        SampleRows[1].Set(11980, 40.2, 30);
        SampleRows[2].Set(7410, 24.9, 18);
        SampleRows[3].Set(2830, 9.5, 7);
        Summary = "40,460 · 04:58";
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
            Rows.Add(new PlayerDamageViewModel("You", 0, true));
            Rows.Add(new PlayerDamageViewModel("Hunter 2", 1, false));
            Rows.Add(new PlayerDamageViewModel("Hunter 3", 2, false));
            Rows.Add(new PlayerDamageViewModel("Support hunter", PlayerDamageViewModel.CompanionSlot, false));
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

        UpdateSummary(total, seconds);
        Sort();
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
            if (Rows[i].Source is { } source && !_snapshot.Contains(source))
                Rows.RemoveAt(i);

        foreach (IPartyMember member in _snapshot)
        {
            if (Contains(member))
                continue;

            // Members already there when we start looking are assumed to have been there since the beginning
            Rows.Add(new PlayerDamageViewModel(member, _primed ? elapsed : 0));
        }

        if (Rows.Count > 0)
            _primed = true;
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
            total += Math.Max(Rows[i].Source?.Damage ?? 0, 0);

        for (int i = 0; i < Rows.Count; i++)
        {
            PlayerDamageViewModel row = Rows[i];
            IPartyMember member = row.Source!;
            int damage = Math.Max(member.Damage, 0);
            double seconds = Math.Max(1, elapsed - Math.Min(elapsed, row.JoinedAt));

            row.SetName(member.Name);
            row.Set(damage, damage / seconds, total > 0 ? damage * 100.0 / total : 0);
        }

        UpdateSummary(total, elapsed);
        Sort();
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
        Summary = string.Empty;
        _summaryTotal = -1;
        _summarySecond = -1;
    }

    private void Reset()
    {
        if (IsDemo)
            return;

        _wasInQuest = false;
        _frozen = false;
        _primed = false;
        IsActive = false;

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

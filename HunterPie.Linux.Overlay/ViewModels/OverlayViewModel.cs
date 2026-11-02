using Avalonia.Threading;
using HunterPie.Core.Game;
using HunterPie.Core.Game.Entity.Enemy;
using HunterPie.Core.Game.Enums;
using HunterPie.Linux.Overlay.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace HunterPie.Linux.Overlay.ViewModels;

/// <summary>
/// Everything the overlay window binds to. <see cref="Attach"/> and <see cref="Detach"/> are called from the host's
/// session events (scan threads); game events are handed to the UI thread before touching any collection.
/// </summary>
public sealed class OverlayViewModel : ViewModelBase
{
    private Context? _context;
    private bool _isEditing;
    private string _status = "Waiting for Monster Hunter to start";
    private readonly DispatcherTimer _timer;
    private long _lastDamageRefresh;

    // Damage only changes a few times a second, so it is refreshed at a quarter of the 100 ms timer rate
    private static readonly TimeSpan DamageRefreshInterval = TimeSpan.FromMilliseconds(250);

    public LayoutViewModel Layout { get; }

    public MonsterOptions MonsterOptions { get; } = new();

    public ObservableCollection<MonsterViewModel> Monsters { get; } = new();

    /// <summary>The monsters the widget draws: <see cref="Monsters"/> narrowed by the target selection</summary>
    public ObservableCollection<MonsterViewModel> VisibleMonsters { get; } = new();

    private readonly MonsterTargetTracker _tracker = new();

    public QuestViewModel Quest { get; } = new();

    public DamageViewModel Damage { get; } = new();

    /// <summary>Placeholder card shown in edit mode while no monster is present</summary>
    public MonsterViewModel Sample { get; }

    /// <summary>The demo feed drives the view models itself, so the game-backed refresh is skipped</summary>
    public bool IsDemo
    {
        get => Damage.IsDemo;
        set
        {
            Damage.IsDemo = value;
            if (value)
                Status = "Demo mode (no game)";
        }
    }

    /// <summary>
    /// Layout edit mode: every widget is visible with placeholder data and can be dragged
    /// </summary>
    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (!Set(ref _isEditing, value))
                return;

            Raise(nameof(ShowMonsters));
            Raise(nameof(ShowQuest));
            Raise(nameof(ShowSample));
            Raise(nameof(ShowDamage));
            Damage.IsEditing = value;
        }
    }

    /// <summary>Shown in the settings window</summary>
    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public bool ShowSample => IsEditing && VisibleMonsters.Count == 0;

    public bool ShowMonsters => IsEditing || (Layout.MonstersEnabled && VisibleMonsters.Count > 0);

    public bool ShowQuest => IsEditing || (Layout.QuestEnabled && Quest.IsActive);

    public bool ShowDamage => IsEditing || (Layout.DamageEnabled && Damage.IsActive && Damage.HasRows);

    public OverlayViewModel(OverlaySettings? settings = null)
    {
        Layout = new LayoutViewModel(settings ?? new OverlaySettings());
        Layout.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(LayoutViewModel.MonstersEnabled):
                    Raise(nameof(ShowMonsters));
                    break;
                case nameof(LayoutViewModel.QuestEnabled):
                    Raise(nameof(ShowQuest));
                    break;
                case nameof(LayoutViewModel.DamageEnabled):
                    Raise(nameof(ShowDamage));
                    break;
            }
        };

        Sample = new MonsterViewModel("Monster (sample)", 6500, 10000, true, MonsterOptions);
        Sample.SetStamina(620, 1000);
        Sample.SetCapture(0.35);
        Sample.SetDetails([Element.Thunder, Element.Ice], VariantType.Normal);

        // Static sample rings so the card has its real height while the layout is edited
        var poison = new AilmentViewModel("AILMENT_POISON", "Poison");
        poison.Update(14, 20, 0, 100, 1);
        var paralysis = new AilmentViewModel("AILMENT_PARALYSIS", "Paralysis");
        paralysis.Update(0, 0, 45, 100, 0);
        Sample.AddAilment(poison);
        Sample.AddAilment(paralysis);
        Sample.RefreshAilments();

        // Turning portraits on while monsters are already in the list should fetch them
        MonsterOptions.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MonsterOptions.TargetingEnabled) or nameof(MonsterOptions.ShowOnlyTarget) or nameof(MonsterOptions.TargetMode))
                UpdateVisibleMonsters();

            if (e.PropertyName != nameof(MonsterOptions.ShowIcons))
                return;

            foreach (MonsterViewModel monster in Monsters)
                monster.EnsureIcon();
        };

        Monsters.CollectionChanged += (_, _) => UpdateVisibleMonsters();
        VisibleMonsters.CollectionChanged += (_, _) =>
        {
            Raise(nameof(ShowMonsters));
            Raise(nameof(ShowSample));
        };
        Quest.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(QuestViewModel.IsActive))
                Raise(nameof(ShowQuest));
        };

        Damage.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(DamageViewModel.IsActive) or nameof(DamageViewModel.HasRows))
                Raise(nameof(ShowDamage));
        };

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => Tick());
    }

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    /// <summary>Safe to call from any thread</summary>
    public void Attach(Context context) => Dispatcher.UIThread.Post(() => AttachOnUiThread(context));

    /// <summary>Safe to call from any thread</summary>
    public void Detach() => Dispatcher.UIThread.Post(DetachOnUiThread);

    private void AttachOnUiThread(Context context)
    {
        DetachOnUiThread();
        _context = context;
        Status = $"Attached to {context.Process.Name}";

        // Subscribe first, then take the snapshot: a monster that spawns in between is deduplicated in Add
        context.Game.OnMonsterSpawn += OnMonsterSpawn;
        context.Game.OnMonsterDespawn += OnMonsterDespawn;

        foreach (IMonster monster in Snapshot(context))
            Add(monster);
    }

    private void DetachOnUiThread()
    {
        if (_context is { } context)
        {
            context.Game.OnMonsterSpawn -= OnMonsterSpawn;
            context.Game.OnMonsterDespawn -= OnMonsterDespawn;
        }

        _context = null;
        DisposeMonsters();
        Status = IsDemo ? "Demo mode (no game)" : "Waiting for Monster Hunter to start";
        Monsters.Clear();
        _tracker.Clear();
        Quest.Refresh(null);
    }

    private void DisposeMonsters()
    {
        for (int i = 0; i < Monsters.Count; i++)
            Monsters[i].Dispose();
    }

    private static IMonster[] Snapshot(Context context)
    {
        // The game mutates this collection from its scan thread
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return [.. context.Game.Monsters];
            }
            catch (InvalidOperationException)
            {
            }
        }

        return [];
    }

    private void OnMonsterSpawn(object? sender, IMonster monster) => Dispatcher.UIThread.Post(() => Add(monster));

    private void OnMonsterDespawn(object? sender, IMonster monster) => Dispatcher.UIThread.Post(() =>
    {
        MonsterViewModel? existing = Monsters.FirstOrDefault(it => ReferenceEquals(it.Source, monster));

        if (existing is { })
        {
            Monsters.Remove(existing);
            existing.Dispose();
        }
    });

    private void Add(IMonster monster)
    {
        if (Monsters.Any(it => ReferenceEquals(it.Source, monster)))
            return;

        Monsters.Add(new MonsterViewModel(monster, MonsterOptions));
    }

    /// <summary>Recomputes which monsters the widget shows; the collection only changes when the set does</summary>
    private void UpdateVisibleMonsters()
    {
        MonsterViewModel[] all = [.. Monsters];
        MonsterOptions options = MonsterOptions;

        // Without a game (sample/demo cards) there is nothing to infer from
        if (_context is not { } context || all.Any(it => it.Source is null))
        {
            Sync(all);
            return;
        }

        DateTime now = DateTime.UtcNow;
        IMonster[] sources = [.. all.Select(it => it.Source!)];

        object? inferred = null;
        if (options.TargetingEnabled && options.TargetMode == HunterPie.Core.Client.Configuration.Enums.TargetModeType.Infer)
            inferred = MonsterTargetSelector.Infer(_tracker.Observe(sources, context.Game.Player.Position, now), now);

        var targets = new Target[all.Length];
        var alive = new bool[all.Length];
        for (int i = 0; i < all.Length; i++)
        {
            IMonster m = sources[i];
            alive[i] = m.Health > 0;
            Target inferredTarget = inferred is null ? Target.None : ReferenceEquals(inferred, m) ? Target.Self : Target.Another;
            targets[i] = MonsterTargetSelector.Adapt(options.TargetingEnabled, options.TargetMode, m.Target, m.ManualTarget, inferredTarget);
        }

        bool[] visible = MonsterTargetSelector.Visible(options.TargetingEnabled, options.ShowOnlyTarget, targets, alive);
        Sync([.. all.Where((_, i) => visible[i])]);
    }

    private void Sync(MonsterViewModel[] wanted)
    {
        if (VisibleMonsters.SequenceEqual(wanted))
            return;

        for (int i = VisibleMonsters.Count - 1; i >= 0; i--)
            if (!wanted.Contains(VisibleMonsters[i]))
                VisibleMonsters.RemoveAt(i);

        for (int i = 0; i < wanted.Length; i++)
            if (!VisibleMonsters.Contains(wanted[i]))
                VisibleMonsters.Insert(Math.Min(i, VisibleMonsters.Count), wanted[i]);
    }

    private void Tick()
    {
        foreach (MonsterViewModel monster in Monsters)
            monster.Refresh();

        UpdateVisibleMonsters();

        if (IsDemo)
            return;

        if (Stopwatch.GetElapsedTime(_lastDamageRefresh) >= DamageRefreshInterval)
        {
            _lastDamageRefresh = Stopwatch.GetTimestamp();
            Damage.Refresh(_context?.Game);
        }

        if (_context is null && IsEditing)
            Quest.Set("Sample quest", TimeSpan.FromMinutes(32), TimeSpan.FromMinutes(18));
        else
            Quest.Refresh(_context);
    }
}

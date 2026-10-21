using Avalonia.Threading;
using HunterPie.Core.Game;
using HunterPie.Core.Game.Entity.Enemy;
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
    private readonly DispatcherTimer _timer;
    private long _lastDamageRefresh;

    // Damage only changes a few times a second, so it is refreshed at a quarter of the 100 ms timer rate
    private static readonly TimeSpan DamageRefreshInterval = TimeSpan.FromMilliseconds(250);

    public ObservableCollection<MonsterViewModel> Monsters { get; } = new();

    public QuestViewModel Quest { get; } = new();

    public DamageViewModel Damage { get; } = new();

    /// <summary>Placeholder card shown in edit mode while no monster is present</summary>
    public MonsterViewModel Sample { get; } = new("Monster (sample)", 6500, 10000, true);

    /// <summary>The demo feed drives the view models itself, so the game-backed refresh is skipped</summary>
    public bool IsDemo
    {
        get => Damage.IsDemo;
        set => Damage.IsDemo = value;
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

    public bool ShowSample => IsEditing && Monsters.Count == 0;

    public bool ShowMonsters => IsEditing || Monsters.Count > 0;

    public bool ShowQuest => IsEditing || Quest.IsActive;

    public bool ShowDamage => IsEditing || (Damage.IsActive && Damage.HasRows);

    public OverlayViewModel()
    {
        Monsters.CollectionChanged += (_, _) =>
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
        Monsters.Clear();
        Quest.Refresh(null);
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
            Monsters.Remove(existing);
    });

    private void Add(IMonster monster)
    {
        if (Monsters.Any(it => ReferenceEquals(it.Source, monster)))
            return;

        Monsters.Add(new MonsterViewModel(monster));
    }

    private void Tick()
    {
        foreach (MonsterViewModel monster in Monsters)
            monster.Refresh();

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

using HunterPie.Core.Game;
using HunterPie.Core.Game.Entity.Enemy;
using HunterPie.Core.Game.Entity.Game.Quest;
using HunterPie.Core.Game.Events;
using HunterPie.Core.Observability.Logging;

namespace HunterPie.Linux.Services;

/// <summary>
/// Prints what the game reports, which is how a headless run proves the memory reading works end to end.
/// Health changes are throttled to whole-percent steps so the output stays readable.
/// </summary>
internal sealed class ConsoleReporter : IDisposable
{
    private readonly ILogger _logger = LoggerFactory.Create();
    private readonly Context _context;
    private readonly Dictionary<IMonster, int> _lastReportedPercent = new();
    private readonly Lock _lock = new();

    public ConsoleReporter(Context context)
    {
        _context = context;

        _context.Game.Player.OnLogin += OnLogin;
        _context.Game.Player.OnLogout += OnLogout;
        _context.Game.Player.OnStageUpdate += OnStageUpdate;
        _context.Game.Player.OnDeath += OnDeath;
        _context.Game.OnMonsterSpawn += OnMonsterSpawn;
        _context.Game.OnMonsterDespawn += OnMonsterDespawn;
        _context.Game.OnQuestStart += OnQuestStart;
        _context.Game.OnQuestEnd += OnQuestEnd;
    }

    private void OnLogin(object? sender, EventArgs e) =>
        _logger.Info($"Player logged in: {_context.Game.Player.Name} (HR {_context.Game.Player.HighRank}, MR {_context.Game.Player.MasterRank})");

    private void OnLogout(object? sender, EventArgs e) => _logger.Info("Player logged out");

    private void OnStageUpdate(object? sender, EventArgs e) =>
        _logger.Info($"Stage changed: id {_context.Game.Player.StageId}, hunting zone: {_context.Game.Player.InHuntingZone}");

    private void OnDeath(object? sender, EventArgs e) => _logger.Info("Player fainted");

    private void OnMonsterSpawn(object? sender, IMonster monster)
    {
        lock (_lock)
            _lastReportedPercent[monster] = -1;

        monster.OnLoad += OnMonsterLoad;
        monster.OnHealthChange += OnMonsterHealthChange;
        monster.OnDeath += OnMonsterDeath;
        monster.OnCapture += OnMonsterCapture;
        monster.OnEnrageStateChange += OnMonsterEnrageChange;

        _logger.Info($"Monster spawned: {monster.Name} (id {monster.Id})");
    }

    private void OnMonsterLoad(object? sender, EventArgs e)
    {
        if (sender is IMonster monster)
            _logger.Info($"Monster loaded: {monster.Name}, {monster.Health:0}/{monster.MaxHealth:0} HP");
    }

    private void OnMonsterHealthChange(object? sender, EventArgs e)
    {
        if (sender is not IMonster { MaxHealth: > 0 } monster)
            return;

        int percent = (int)Math.Round(monster.Health / monster.MaxHealth * 100);

        lock (_lock)
        {
            if (_lastReportedPercent.TryGetValue(monster, out int last) && last == percent)
                return;

            _lastReportedPercent[monster] = percent;
        }

        _logger.Info($"{monster.Name}: {monster.Health:0}/{monster.MaxHealth:0} HP ({percent}%)");
    }

    private void OnMonsterDeath(object? sender, EventArgs e)
    {
        if (sender is IMonster monster)
            _logger.Info($"{monster.Name} was slain");
    }

    private void OnMonsterCapture(object? sender, EventArgs e)
    {
        if (sender is IMonster monster)
            _logger.Info($"{monster.Name} was captured");
    }

    private void OnMonsterEnrageChange(object? sender, EventArgs e)
    {
        if (sender is IMonster monster)
            _logger.Info($"{monster.Name} {(monster.IsEnraged ? "became enraged" : "calmed down")}");
    }

    private void OnMonsterDespawn(object? sender, IMonster monster)
    {
        Unsubscribe(monster);

        lock (_lock)
            _lastReportedPercent.Remove(monster);

        _logger.Info($"Monster despawned: {monster.Name}");
    }

    private void OnQuestStart(object? sender, IQuest quest) =>
        _logger.Info($"Quest started: {quest.Name} (id {quest.Id}, {quest.Type})");

    private void OnQuestEnd(object? sender, QuestEndEventArgs e) =>
        _logger.Info($"Quest ended: {e.Status} after {e.TimeElapsed.TotalSeconds:F0}s");

    private void Unsubscribe(IMonster monster)
    {
        monster.OnLoad -= OnMonsterLoad;
        monster.OnHealthChange -= OnMonsterHealthChange;
        monster.OnDeath -= OnMonsterDeath;
        monster.OnCapture -= OnMonsterCapture;
        monster.OnEnrageStateChange -= OnMonsterEnrageChange;
    }

    public void Dispose()
    {
        _context.Game.Player.OnLogin -= OnLogin;
        _context.Game.Player.OnLogout -= OnLogout;
        _context.Game.Player.OnStageUpdate -= OnStageUpdate;
        _context.Game.Player.OnDeath -= OnDeath;
        _context.Game.OnMonsterSpawn -= OnMonsterSpawn;
        _context.Game.OnMonsterDespawn -= OnMonsterDespawn;
        _context.Game.OnQuestStart -= OnQuestStart;
        _context.Game.OnQuestEnd -= OnQuestEnd;

        IMonster[] monsters;

        lock (_lock)
            monsters = [.. _lastReportedPercent.Keys];

        foreach (IMonster monster in monsters)
            Unsubscribe(monster);
    }
}

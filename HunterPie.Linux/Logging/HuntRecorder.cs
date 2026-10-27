using HunterPie.Core.Game;
using HunterPie.Core.Game.Entity.Enemy;
using HunterPie.Core.Game.Entity.Game.Quest;
using HunterPie.Core.Game.Entity.Party;
using HunterPie.Core.Game.Enums;
using HunterPie.Core.Game.Events;
using HunterPie.Integrations.Datasources.MonsterHunterWilds.Entity.Enemy;
using HunterPie.Linux.Services;
using System.Globalization;

namespace HunterPie.Linux.Logging;

/// <summary>
/// Writes what the game reports to the session log, and a file per hunt with a summary at the end. It only listens to
/// the game objects the scanner already fills in (no memory reads of its own), and polls the party and exhaustion state
/// twice a second, which is plain property reads. Game events come from scan threads, so everything is under one lock.
/// </summary>
internal sealed class HuntRecorder : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    // Damage a member has when first seen this early in the quest is treated as dealt before the quest clock started
    private const double EarlyQuestSeconds = 15;

    private readonly Lock _lock = new();
    private readonly Context _context;
    private readonly LogSink _sink;
    private readonly Dictionary<IMonster, MonsterRecord> _monsters = new();
    private readonly Dictionary<IMonsterAilment, MonsterRecord> _ailmentOwners = new();
    private readonly List<MemberRecord> _members = new();
    private readonly List<IPartyMember> _snapshot = new(8);
    private readonly Timer _timer;

    private IQuest? _quest;
    private DateTime _questStartedAt;
    private int _carts;
    private int _maxCarts;
    private bool _disposed;

    public HuntRecorder(Context context, LogSink sink)
    {
        _context = context;
        _sink = sink;

        _context.Game.Player.OnLogin += OnLogin;
        _context.Game.Player.OnLogout += OnLogout;
        _context.Game.Player.OnStageUpdate += OnStageUpdate;
        _context.Game.OnMonsterSpawn += OnMonsterSpawn;
        _context.Game.OnMonsterDespawn += OnMonsterDespawn;
        _context.Game.OnQuestStart += OnQuestStart;
        _context.Game.OnQuestEnd += OnQuestEnd;

        _timer = new Timer(_ => Poll(), null, PollInterval, PollInterval);
    }

    private bool QuestActive => _quest is not null;

    #region Player and quest

    private void OnLogin(object? sender, EventArgs e) =>
        Event($"Player logged in: {_context.Game.Player.Name} (HR {_context.Game.Player.HighRank}, MR {_context.Game.Player.MasterRank})");

    private void OnLogout(object? sender, EventArgs e) => Event("Player logged out");

    private void OnStageUpdate(object? sender, EventArgs e)
    {
        lock (_lock)
        {
            bool inZone = _context.Game.Player.InHuntingZone;
            Event($"Stage changed: id {_context.Game.Player.StageId}, hunting zone: {inZone}");

            // Lines from here until the quest clock starts are kept for the hunt file
            _sink.SetBuffering(inZone && !QuestActive);
        }
    }

    // The game keeps one faint counter for the whole party and the player object never reports its own
    // death, so this counts the party's carts rather than the local player's.
    private void OnCartCounterChange(object? sender, CounterChangeEventArgs e)
    {
        lock (_lock)
        {
            if (!QuestActive)
                return;

            int previous = _carts;
            _carts = e.Current;
            _maxCarts = e.Max;

            if (e.Current > previous)
                Event($"Party fainted: {e.Current} of {e.Max} carts used");
        }
    }

    private void OnQuestStart(object? sender, IQuest quest)
    {
        lock (_lock)
        {
            _quest = quest;
            _questStartedAt = DateTime.Now;
            _carts = quest.Deaths;
            _maxCarts = quest.MaxDeaths;
            quest.OnDeathCounterChange += OnCartCounterChange;

            string name = QuestName(quest);
            string[] header =
            [
                "===== HunterPie hunt log =====",
                $"Started:  {_questStartedAt:yyyy-MM-dd HH:mm:ss}",
                $"Quest:    {name} (id {quest.Id}, {quest.Type})",
                $"Game:     {_context.Process.Name} (pid {_context.Process.SystemProcess.Id})",
                $"Player:   {_context.Game.Player.Name} (HR {_context.Game.Player.HighRank}, MR {_context.Game.Player.MasterRank})",
                string.Empty
            ];

            // Quests often have no name; the id says more than "unnamed" in a file name
            _sink.BeginHunt(_questStartedAt, string.IsNullOrWhiteSpace(quest.Name) ? $"quest-{quest.Id}" : name, header);
            Event($"Quest started: {name} (id {quest.Id}, {quest.Type})");

            // Damage already on the board belongs to the time before the quest clock, so start the party tally afresh
            _members.Clear();
            PollMembers();
        }
    }

    private void OnQuestEnd(object? sender, QuestEndEventArgs e)
    {
        lock (_lock)
        {
            if (!QuestActive)
                return;

            PollMembers();
            Event($"Quest ended: {e.Status} after {e.TimeElapsed.TotalSeconds:F0}s");

            // The last reading before the quest object is torn down is the one that counts
            IQuest finished = e.Quest ?? _quest!;
            _maxCarts = finished.MaxDeaths > 0 ? finished.MaxDeaths : _maxCarts;

            // The cart that fails the quest can be counted in the same breath as the quest ending, and its event
            // may not have reached this recorder yet
            if (finished.Deaths > _carts)
            {
                _carts = finished.Deaths;
                Event($"Party fainted: {_carts} of {_maxCarts} carts used");
            }
            finished.OnDeathCounterChange -= OnCartCounterChange;
            _quest!.OnDeathCounterChange -= OnCartCounterChange;

            _sink.EndHunt(BuildSummary(finished, e.Status.ToString(), e.TimeElapsed));
            _quest = null;
            _sink.SetBuffering(false);
        }
    }

    #endregion

    #region Monsters

    private void OnMonsterSpawn(object? sender, IMonster monster)
    {
        lock (_lock)
        {
            if (_disposed || _monsters.ContainsKey(monster))
                return;

            var record = new MonsterRecord(monster);
            _monsters[monster] = record;

            monster.OnLoad += OnMonsterLoad;
            monster.OnHealthChange += OnMonsterHealthChange;
            monster.OnDeath += OnMonsterDeath;
            monster.OnCapture += OnMonsterCapture;
            monster.OnEnrageStateChange += OnMonsterEnrageChange;
            monster.OnNewAilmentFound += OnNewAilmentFound;

            foreach (IMonsterAilment ailment in SnapshotAilments(monster))
                Watch(record, ailment);

            Event($"Monster spawned: {record.Name} (id {monster.Id})");
        }
    }

    private void OnMonsterLoad(object? sender, EventArgs e)
    {
        if (sender is IMonster monster)
            Event($"Monster loaded: {NameOf(monster)}, {monster.Health:0}/{monster.MaxHealth:0} HP");
    }

    private void OnMonsterHealthChange(object? sender, EventArgs e)
    {
        if (sender is not IMonster { MaxHealth: > 0 } monster)
            return;

        lock (_lock)
        {
            if (!_monsters.TryGetValue(monster, out MonsterRecord? record))
                return;

            int percent = (int)Math.Round(monster.Health / monster.MaxHealth * 100);
            record.ObserveHealth(monster.Health, monster.MaxHealth);

            // Whole-percent steps, like the console, so a hunt is a few hundred lines rather than thousands
            if (record.LastLoggedPercent == percent)
                return;

            record.LastLoggedPercent = percent;
            Event($"{record.Name}: {monster.Health:0}/{monster.MaxHealth:0} HP ({percent}%)");
        }
    }

    private void OnMonsterDeath(object? sender, EventArgs e)
    {
        if (sender is not IMonster monster)
            return;

        lock (_lock)
        {
            if (_monsters.TryGetValue(monster, out MonsterRecord? record))
                record.Outcome = "slain";

            Event($"{NameOf(monster)} was slain");
        }
    }

    private void OnMonsterCapture(object? sender, EventArgs e)
    {
        if (sender is not IMonster monster)
            return;

        lock (_lock)
        {
            if (_monsters.TryGetValue(monster, out MonsterRecord? record))
                record.Outcome = "captured";

            Event($"{NameOf(monster)} was captured");
        }
    }

    private void OnMonsterEnrageChange(object? sender, EventArgs e)
    {
        if (sender is not IMonster monster)
            return;

        lock (_lock)
        {
            if (_monsters.TryGetValue(monster, out MonsterRecord? record))
                record.SetEnraged(monster.IsEnraged, DateTime.Now);

            Event($"{NameOf(monster)} {(monster.IsEnraged ? "became enraged" : "calmed down")}");
        }
    }

    private void OnMonsterDespawn(object? sender, IMonster monster)
    {
        lock (_lock)
        {
            if (_monsters.TryGetValue(monster, out MonsterRecord? record))
            {
                record.CloseOpenStates(DateTime.Now);
                record.Despawned = true;
            }

            Unsubscribe(monster);
            Event($"Monster despawned: {NameOf(monster)}");
        }
    }

    private void OnNewAilmentFound(object? sender, IMonsterAilment ailment)
    {
        if (sender is not IMonster monster)
            return;

        lock (_lock)
        {
            if (_monsters.TryGetValue(monster, out MonsterRecord? record))
                Watch(record, ailment);
        }
    }

    private void Watch(MonsterRecord record, IMonsterAilment ailment)
    {
        // The flinch gauge refills all the time and the game's counter for it misses most flinches, so it is not logged
        if (!AilmentNames.IsDisplayable(ailment) || AilmentNames.IsFlinchGauge(ailment) || !_ailmentOwners.TryAdd(ailment, record))
            return;

        ailment.OnCounterUpdate += OnAilmentCounter;
        record.Ailments.TryAdd(AilmentNames.NameFor(ailment), 0);
    }

    private void OnAilmentCounter(object? sender, IMonsterAilment ailment)
    {
        lock (_lock)
        {
            if (!_ailmentOwners.TryGetValue(ailment, out MonsterRecord? record))
                return;

            string name = AilmentNames.NameFor(ailment);
            record.Ailments.TryGetValue(name, out int previous);

            if (ailment.Counter <= previous)
                return;

            record.Ailments[name] = ailment.Counter;
            Event($"{record.Name}: {name} inflicted (#{ailment.Counter})");
        }
    }

    private void Unsubscribe(IMonster monster)
    {
        monster.OnLoad -= OnMonsterLoad;
        monster.OnHealthChange -= OnMonsterHealthChange;
        monster.OnDeath -= OnMonsterDeath;
        monster.OnCapture -= OnMonsterCapture;
        monster.OnEnrageStateChange -= OnMonsterEnrageChange;
        monster.OnNewAilmentFound -= OnNewAilmentFound;

        if (!_monsters.TryGetValue(monster, out MonsterRecord? record))
            return;

        foreach (IMonsterAilment ailment in _ailmentOwners.Where(it => ReferenceEquals(it.Value, record)).Select(it => it.Key).ToArray())
        {
            ailment.OnCounterUpdate -= OnAilmentCounter;
            _ailmentOwners.Remove(ailment);
        }
    }

    private static IMonsterAilment[] SnapshotAilments(IMonster monster)
    {
        // The game mutates its list from the scan thread
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return monster.Ailments is { } ailments ? [.. ailments] : [];
            }
            catch (InvalidOperationException)
            {
            }
        }

        return [];
    }

    #endregion

    #region Polling (party and exhaustion)

    private void Poll()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            try
            {
                if (_context.Game.Player.InHuntingZone || QuestActive)
                    PollMembers();

                PollExhaustion();
            }
            catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException)
            {
                // The game state changed under us (a scan in progress); the next poll is half a second away
            }
        }
    }

    private void PollExhaustion()
    {
        DateTime now = DateTime.Now;

        foreach ((IMonster monster, MonsterRecord record) in _monsters)
        {
            if (monster is not MHWildsMonster wilds || record.Despawned)
                continue;

            if (record.SetExhausted(wilds.IsExhausted, now))
                Event($"{record.Name} {(wilds.IsExhausted ? $"became exhausted (#{record.ExhaustCount})" : "recovered from exhaustion")}");
        }
    }

    private void PollMembers()
    {
        _snapshot.Clear();

        try
        {
            _snapshot.AddRange(_context.Game.Player.Party.Members);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return;
        }

        double elapsed = _context.Game.TimeElapsed;

        // Everyone found by the first poll of a quest shares the same rule; later arrivals joined mid-quest
        bool initial = _members.Count == 0;
        bool attachedMidQuest = initial && elapsed >= EarlyQuestSeconds;

        foreach (MemberRecord member in _members)
            member.Present = false;

        foreach (IPartyMember source in _snapshot)
        {
            MemberRecord? record = _members.Find(it => ReferenceEquals(it.Source, source));

            if (record is null)
            {
                // Same rule as the overlay: damage found at the start of the quest, or on joining, was not dealt in the
                // time DPS is divided by. Attaching to a quest that is well under way counts from its start instead.
                record = new MemberRecord(source, attachedMidQuest ? 0 : elapsed, attachedMidQuest ? 0 : Math.Max(source.Damage, 0));
                _members.Add(record);
                Event($"Party: {record.Name} joined ({record.Weapon})");
            }

            record.Present = true;
            record.Update(source);
        }

        foreach (MemberRecord left in _members.Where(it => !it.Present && !it.Left))
        {
            left.Left = true;
            Event($"Party: {left.Name} left");
        }
    }

    #endregion

    #region Writing

    private void Event(string text)
    {
        string clock = string.Empty;

        if (QuestActive)
        {
            try
            {
                clock = $"[+{Clock(TimeSpan.FromSeconds(Math.Max(0, _context.Game.TimeElapsed)))}]";
            }
            catch (NullReferenceException)
            {
            }
        }

        _sink.Write($"[{DateTime.Now:HH:mm:ss.fff}][EVENT]{clock} {text}");
    }

    private IEnumerable<string> BuildSummary(IQuest quest, string result, TimeSpan duration)
    {
        DateTime now = DateTime.Now;
        foreach (MonsterRecord record in _monsters.Values)
            record.CloseOpenStates(now);

        var lines = new List<string>
        {
            string.Empty,
            "===== Hunt summary =====",
            $"Quest:     {QuestName(quest)} (id {quest.Id}, {quest.Type})",
            $"Result:    {result}",
            $"Duration:  {Clock(duration)}  ({_questStartedAt:HH:mm:ss} to {now:HH:mm:ss})",
            $"Player:    {_context.Game.Player.Name}",
            $"Carts:     {_carts}{(_maxCarts > 0 ? $" of {_maxCarts}" : string.Empty)} used (the game counts these for the whole party)",
            string.Empty,
            "Monsters"
        };

        List<MonsterRecord> engaged = [.. _monsters.Values.Where(it => it.Engaged).OrderBy(it => it.Name)];

        foreach (MonsterRecord record in engaged)
        {
            lines.Add($"  {record.Name} (id {record.Monster.Id}): {record.OutcomeText()}");

            string states = record.StatesText();
            if (states.Length > 0)
                lines.Add($"      {states}");

            if (record.Ailments.Any(it => it.Value > 0))
                lines.Add($"      ailments: {string.Join(", ", record.Ailments.Where(it => it.Value > 0).OrderBy(it => it.Key).Select(it => $"{it.Key} x{it.Value}"))}");
        }

        if (engaged.Count == 0)
            lines.Add("  (no monster took damage)");

        string[] others = [.. _monsters.Values.Where(it => !it.Engaged).GroupBy(it => it.Name).OrderBy(it => it.Key).Select(it => it.Count() > 1 ? $"{it.Key} x{it.Count()}" : it.Key)];
        if (others.Length > 0)
            lines.Add($"  Not engaged: {string.Join(", ", others)}");

        lines.Add(string.Empty);
        lines.Add("Party damage");

        int total = _members.Sum(it => it.Damage);
        int rank = 1;

        foreach (MemberRecord member in _members.OrderByDescending(it => it.Damage).ThenBy(it => it.Slot))
        {
            double seconds = Math.Max(1, duration.TotalSeconds - Math.Min(duration.TotalSeconds, member.JoinedAt));
            double dps = Math.Max(member.Damage - member.Baseline, 0) / seconds;
            double share = total > 0 ? member.Damage * 100.0 / total : 0;

            lines.Add($"  {rank++}. {member.Name}{(member.IsSelf ? " (you)" : string.Empty)} [{member.Weapon}]  {member.Damage.ToString("N0", CultureInfo.InvariantCulture)} damage  {share:0}%  {dps:0.0} DPS");
        }

        if (_members.Count == 0)
            lines.Add("  (no party data was read)");
        else
            lines.Add($"  Team total: {total.ToString("N0", CultureInfo.InvariantCulture)}. DPS counts damage dealt since the quest clock started or the hunter joined.");

        lines.Add(string.Empty);

        return lines;
    }

    private static string QuestName(IQuest quest) => string.IsNullOrWhiteSpace(quest.Name) ? "(unnamed)" : quest.Name.Trim();

    // Upstream formats names as "{prefix} {name} {suffix}", which leaves padding when there is no variant
    private static string NameOf(IMonster monster) => monster.Name.Trim();

    internal static string Clock(TimeSpan span) =>
        span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : span.ToString(@"mm\:ss", CultureInfo.InvariantCulture);

    #endregion

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;

            // The game closed (or HunterPie is stopping) in the middle of a hunt: still leave a summary
            if (_quest is { } quest)
            {
                quest.OnDeathCounterChange -= OnCartCounterChange;
                PollMembersSafe();
                Event("Hunt interrupted: the game closed or HunterPie stopped");
                _sink.EndHunt(BuildSummary(quest, "Interrupted", DateTime.Now - _questStartedAt));
                _quest = null;
            }

            _context.Game.Player.OnLogin -= OnLogin;
            _context.Game.Player.OnLogout -= OnLogout;
            _context.Game.Player.OnStageUpdate -= OnStageUpdate;
            _context.Game.OnMonsterSpawn -= OnMonsterSpawn;
            _context.Game.OnMonsterDespawn -= OnMonsterDespawn;
            _context.Game.OnQuestStart -= OnQuestStart;
            _context.Game.OnQuestEnd -= OnQuestEnd;

            foreach (IMonster monster in _monsters.Keys.ToArray())
                Unsubscribe(monster);

            _sink.SetBuffering(false);
        }

        _timer.Dispose();
    }

    private void PollMembersSafe()
    {
        try
        {
            PollMembers();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException)
        {
            // The game is going away; whatever the last poll saw is what the summary uses
        }
    }

    private sealed class MonsterRecord(IMonster monster)
    {
        private DateTime? _enragedSince;
        private DateTime? _exhaustedSince;
        private float _minHealth = float.MaxValue;
        private float _lastHealth;
        private float _maxHealth;

        public IMonster Monster { get; } = monster;
        public string Name => Monster.Name.Trim();
        public int LastLoggedPercent { get; set; } = -1;
        public string Outcome { get; set; } = "alive";
        public bool Despawned { get; set; }
        public int EnrageCount { get; private set; }
        public TimeSpan EnragedTotal { get; private set; }
        public int ExhaustCount { get; private set; }
        public TimeSpan ExhaustedTotal { get; private set; }
        public Dictionary<string, int> Ailments { get; } = new();

        /// <summary>It lost health at some point, so it was part of the fight</summary>
        public bool Engaged => _maxHealth > 0 && _minHealth < _maxHealth * 0.9999f;

        public void ObserveHealth(float health, float maxHealth)
        {
            _lastHealth = health;
            _maxHealth = maxHealth;
            _minHealth = Math.Min(_minHealth, health);
        }

        public void SetEnraged(bool enraged, DateTime now)
        {
            if (enraged && _enragedSince is null)
            {
                _enragedSince = now;
                EnrageCount++;
            }
            else if (!enraged && _enragedSince is { } since)
            {
                EnragedTotal += now - since;
                _enragedSince = null;
            }
        }

        /// <returns>true when the state changed</returns>
        public bool SetExhausted(bool exhausted, DateTime now)
        {
            if (exhausted && _exhaustedSince is null)
            {
                _exhaustedSince = now;
                ExhaustCount++;
                return true;
            }

            if (!exhausted && _exhaustedSince is { } since)
            {
                ExhaustedTotal += now - since;
                _exhaustedSince = null;
                return true;
            }

            return false;
        }

        public void CloseOpenStates(DateTime now)
        {
            SetEnraged(false, now);
            SetExhausted(false, now);
        }

        public string OutcomeText()
        {
            double percent = _maxHealth > 0 ? Math.Clamp(_lastHealth / _maxHealth * 100, 0, 100) : 0;
            string health = $"{_lastHealth:N0} / {_maxHealth:N0} HP";

            return Outcome == "alive"
                ? $"alive at {percent:0}% ({health})"
                : $"{Outcome} ({health} when last seen)";
        }

        public string StatesText()
        {
            var parts = new List<string>();

            if (EnrageCount > 0)
                parts.Add($"enraged {EnrageCount}x for {Clock(EnragedTotal)} in total");

            if (ExhaustCount > 0)
                parts.Add($"exhausted {ExhaustCount}x for {Clock(ExhaustedTotal)} in total");

            return string.Join(", ", parts);
        }
    }

    private sealed class MemberRecord(IPartyMember source, double joinedAt, int baseline)
    {
        public IPartyMember Source { get; } = source;
        public string Name { get; private set; } = source.Name;
        public Weapon Weapon { get; private set; } = source.Weapon;
        public bool IsSelf { get; } = source.IsMyself;
        public int Slot { get; } = source.Slot;
        public double JoinedAt { get; } = joinedAt;
        public int Baseline { get; } = baseline;
        public int Damage { get; private set; }
        public bool Present { get; set; }
        public bool Left { get; set; }

        public void Update(IPartyMember member)
        {
            Name = member.Name;
            Weapon = member.Weapon;

            // Keep the highest value: the game may clear the party just as the quest ends
            Damage = Math.Max(Damage, Math.Max(member.Damage, 0));
            Left = false;
        }
    }
}

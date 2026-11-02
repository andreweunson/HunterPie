using HunterPie.Core.Client.Configuration.Enums;
using HunterPie.Core.Game.Entity.Enemy;
using HunterPie.Core.Game.Enums;
using System.Numerics;

namespace HunterPie.Linux.Overlay.Services;

/// <summary>What the inference knows about one monster</summary>
public readonly record struct TargetCandidate(object Key, double Distance, DateTime LastHitAt, double HealthRatio);

/// <summary>
/// Picks the monsters the widget shows, the way HunterPie's own monster widget does: the target comes from the lock-on,
/// the map pin, or (Infer) a weighted score of distance, recent hits and remaining health. The constants and the score
/// are the ones of upstream's WeightedTargetDetectionService; that class is internal to another assembly and is driven
/// by game events, so this is a polling copy.
/// </summary>
public static class MonsterTargetSelector
{
    public const double MaximumDistanceMeters = 80.0;
    public const double RecentHitSeconds = 30.0;
    public const double LowHealthRatio = 0.75;

    public static double Score(in TargetCandidate candidate, DateTime now)
    {
        double distance = 1 - (candidate.Distance / MaximumDistanceMeters);
        double damageTime = Math.Max(0, 1 - ((now - candidate.LastHitAt).TotalSeconds / RecentHitSeconds));
        double health = 1 - candidate.HealthRatio;

        double score = (distance * 0.45) + (damageTime * 0.40) + (health * 0.15);

        // A monster nobody has touched is rarely what the player is fighting
        return candidate.HealthRatio >= 0.9999 ? score * 0.5 : score;
    }

    /// <summary>The best alive monster that is close or already hurt, or null when none qualifies</summary>
    public static object? Infer(IEnumerable<TargetCandidate> candidates, DateTime now)
    {
        object? best = null;
        double bestScore = double.NegativeInfinity;

        foreach (TargetCandidate candidate in candidates)
        {
            bool alive = candidate.HealthRatio > 0;
            bool close = candidate.Distance <= MaximumDistanceMeters;
            bool hurt = candidate.HealthRatio <= LowHealthRatio;

            if (!alive || !(close || hurt))
                continue;

            double score = Score(candidate, now);
            if (score <= bestScore)
                continue;

            bestScore = score;
            best = candidate.Key;
        }

        return best;
    }

    /// <summary>Upstream's adapter: which of the three signals decides, and nothing at all when targeting is off</summary>
    public static Target Adapt(bool enabled, TargetModeType mode, Target lockOn, Target manual, Target inferred)
    {
        if (!enabled)
            return Target.None;

        return mode switch
        {
            TargetModeType.LockOn => lockOn,
            TargetModeType.MapPin or TargetModeType.AutoQuest => manual,
            _ => inferred
        };
    }

    /// <summary>
    /// Upstream's visibility rule: with targeting off every monster shows. With it on, the monster that is the target
    /// shows; when nothing is targeted yet, everything shows unless "only the target" is set.
    /// </summary>
    public static bool[] Visible(bool enabled, bool onlyTarget, IReadOnlyList<Target> targets, IReadOnlyList<bool> alive)
    {
        var visible = new bool[targets.Count];

        if (!enabled)
        {
            for (int i = 0; i < visible.Length; i++)
                visible[i] = alive[i];

            return visible;
        }

        bool any = false;
        for (int i = 0; i < visible.Length; i++)
            any |= visible[i] = alive[i] && targets[i] == Target.Self;

        if (!any && !onlyTarget)
            for (int i = 0; i < visible.Length; i++)
                visible[i] = alive[i];

        return visible;
    }
}

/// <summary>Remembers per monster when its health last moved (a hit) so the inference can weigh recent damage</summary>
public sealed class MonsterTargetTracker
{
    private readonly Dictionary<IMonster, (double Health, DateTime LastHitAt)> _state = new();

    public static double Distance(Vector3 player, Vector3 monster) => Vector3.Distance(player, monster);

    public IReadOnlyList<TargetCandidate> Observe(IReadOnlyList<IMonster> monsters, Vector3 player, DateTime now)
    {
        var list = new List<TargetCandidate>(monsters.Count);

        foreach (IMonster monster in monsters)
        {
            double health = monster.Health;

            if (!_state.TryGetValue(monster, out var previous))
                previous = (health, now);
            else if (Math.Abs(previous.Health - health) > 0.0001)
                previous = (health, now);

            _state[monster] = previous;

            list.Add(new TargetCandidate(
                monster,
                Distance(player, monster.Position),
                previous.LastHitAt,
                health / Math.Max(monster.MaxHealth, 1.0)));
        }

        if (_state.Count > monsters.Count)
            foreach (IMonster gone in _state.Keys.Where(it => !monsters.Contains(it)).ToArray())
                _state.Remove(gone);

        return list;
    }

    public void Clear() => _state.Clear();
}

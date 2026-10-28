using HunterPie.Core.Client.Localization;
using HunterPie.Core.Game.Entity.Player;
using HunterPie.Core.Game.Enums;
using HunterPie.DI;
using HunterPie.Linux.Services;
using System.Globalization;

namespace HunterPie.Linux.Logging;

/// <summary>
/// How long each timed skill (Agitator, Burst, Latent Power...) was active during one hunt, as a share of the quest.
/// Times are quest-clock seconds, so a paused or loading game does not count against a skill. The tracker does no
/// locking of its own: <see cref="HuntRecorder"/> calls it with its lock held.
/// </summary>
internal sealed class SkillUptimeTracker
{
    private readonly Dictionary<string, SkillRecord> _skills = new();

    private double _trackedFrom;
    private double _endedAt;

    /// <summary>Starts a new hunt, forgetting the last one. Skills already active at this point count from <paramref name="fromSeconds"/>.</summary>
    public void Begin(double fromSeconds, IEnumerable<IAbnormality> alreadyActive)
    {
        _skills.Clear();
        _trackedFrom = Math.Max(0, fromSeconds);
        _endedAt = _trackedFrom;

        foreach (IAbnormality abnormality in alreadyActive)
            Start(abnormality, _trackedFrom);
    }

    public void Start(IAbnormality abnormality, double now)
    {
        if (!IsSkill(abnormality))
            return;

        if (!_skills.TryGetValue(abnormality.Id, out SkillRecord? record))
        {
            record = new SkillRecord(NameOf(abnormality));
            _skills[abnormality.Id] = record;
        }

        // A repeated start for a skill that is still running is the same activation
        if (record.OpenSince is not null)
            return;

        record.OpenSince = Math.Max(now, _trackedFrom);
        record.Activations++;
    }

    public void End(IAbnormality abnormality, double now)
    {
        if (!IsSkill(abnormality) || !_skills.TryGetValue(abnormality.Id, out SkillRecord? record))
            return;

        record.Close(now);
    }

    /// <summary>Ends every skill that is still running, at the end of the quest, and fixes the length the percentages are of</summary>
    public void CloseAll(double now)
    {
        _endedAt = Math.Max(now, _trackedFrom);

        foreach (SkillRecord record in _skills.Values)
            record.Close(_endedAt);
    }

    /// <summary>Summary lines for the hunt file. Call <see cref="CloseAll"/> first.</summary>
    public IEnumerable<string> Summary()
    {
        double tracked = _endedAt - _trackedFrom;
        var lines = new List<string>();

        if (tracked < 1)
        {
            lines.Add("Skill uptime");
            lines.Add("  (the quest was too short to measure)");
            return lines;
        }

        lines.Add($"Skill uptime (over {HuntRecorder.Clock(TimeSpan.FromSeconds(tracked))} of quest time)");

        List<SkillRecord> ordered =
        [
            .. _skills.Values.OrderByDescending(it => it.Total).ThenBy(it => it.Name, StringComparer.Ordinal)
        ];

        if (ordered.Count == 0)
        {
            lines.Add("  (no timed skills were active)");
        }
        else
        {
            int width = ordered.Max(it => it.Name.Length);

            foreach (SkillRecord record in ordered)
            {
                // Overlapping reads can make the total a hair longer than the quest
                double percent = Math.Clamp(record.Total / tracked * 100, 0, 100);
                string times = record.Activations == 1 ? "1 activation" : $"{record.Activations} activations";

                lines.Add($"  {record.Name.PadRight(width)}  {percent.ToString("0.0", CultureInfo.InvariantCulture),5}%  ({HuntRecorder.Clock(TimeSpan.FromSeconds(record.Total))}, {times})");
            }
        }

        if (_trackedFrom >= HuntRecorder.EarlyQuestSeconds)
            lines.Add($"  Skills were tracked from {HuntRecorder.Clock(TimeSpan.FromSeconds(_trackedFrom))} into the quest, when HunterPie attached.");

        return lines;
    }

    private static bool IsSkill(IAbnormality abnormality) => abnormality.Type == AbnormalityType.Skill;

    /// <summary>Localised skill name from the language file, or a readable version of the key when there is none</summary>
    private static string NameOf(IAbnormality abnormality)
    {
        string key = abnormality.Name;

        if (string.IsNullOrWhiteSpace(key))
            return abnormality.Id;

        try
        {
            var repository = DependencyContainer.Get<ILocalizationRepository>();
            string path = $"//Strings/Abnormalities/Abnormality[@Id='{key}']";

            if (repository.ExistsBy(path))
                return repository.FindStringBy(path);
        }
        catch (Exception ex) when (ex is NullReferenceException or InvalidOperationException or KeyNotFoundException)
        {
            // The host is not running (demo mode, tests): fall through to the key
        }

        return AilmentNames.Prettify(key);
    }

    private sealed class SkillRecord(string name)
    {
        public string Name { get; } = name;
        public double Total { get; private set; }
        public int Activations { get; set; }
        public double? OpenSince { get; set; }

        public void Close(double now)
        {
            if (OpenSince is not { } since)
                return;

            Total += Math.Max(0, now - since);
            OpenSince = null;
        }
    }
}

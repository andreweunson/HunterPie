using HunterPie.Core.Game;
using HunterPie.Platforms.Linux.Process;
using System.Globalization;
using System.Text.Json;

namespace HunterPie.Linux.Logging;

/// <summary>
/// Puts the skill uptime measured by the My Hunt Report mod (a REFramework mod that runs inside the game, see
/// https://github.com/cubicj/my-hunt-report) into the hunt log. The mod appends one JSON line per finished quest to
/// reframework/data/MyHuntReport/history.jsonl in the game folder; this reads the line written for the hunt that just
/// ended and appends it to the hunt file. HunterPie never writes to the mod's files, and nothing here needs the mod:
/// without it the hunt log just says so.
/// </summary>
internal sealed class MyHuntReportBridge : IDisposable
{
    internal const string HistoryRelativePath = "reframework/data/MyHuntReport/history.jsonl";

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(3);

    // The mod stamps entries with whole seconds of its own clock
    private const double ClockSlackSeconds = 5;

    private readonly Context _context;
    private readonly LogSink _sink;
    private readonly CancellationTokenSource _cancel = new();

    private string? _historyPath;
    private long _offset;
    private DateTimeOffset _questStarted;

    public MyHuntReportBridge(Context context, LogSink sink)
    {
        _context = context;
        _sink = sink;
    }

    /// <summary>Notes where the mod's history ends now, so only what it writes for this quest is looked at</summary>
    public void Begin(DateTime questStartedLocal)
    {
        _questStarted = new DateTimeOffset(questStartedLocal);
        _historyPath = ResolveHistoryPath();
        _offset = _historyPath is not null && File.Exists(_historyPath) ? new FileInfo(_historyPath).Length : 0;
    }

    /// <summary>Waits (in the background) for the mod to save this quest, then appends its skill uptime to the hunt log</summary>
    public void CollectAfterQuest(string? huntPath)
    {
        string? history = _historyPath ?? ResolveHistoryPath();
        long offset = _offset;
        DateTimeOffset started = _questStarted;
        CancellationToken token = _cancel.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                DateTime deadline = DateTime.UtcNow + GiveUpAfter;

                while (!token.IsCancellationRequested)
                {
                    if (history is not null && TryFindSkillLines(history, offset, started, out IReadOnlyList<string> lines))
                    {
                        _sink.AppendAfterHunt(huntPath, lines);
                        return;
                    }

                    if (DateTime.UtcNow >= deadline)
                        break;

                    await Task.Delay(PollInterval, token).ConfigureAwait(false);
                }

                if (!token.IsCancellationRequested)
                    _sink.AppendAfterHunt(huntPath, NotFoundLines(history));
            }
            catch (OperationCanceledException)
            {
                // HunterPie is shutting down
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                _sink.AppendAfterHunt(huntPath, ["Skill uptime", $"  (could not read My Hunt Report's history: {ex.Message})"]);
            }
        }, token);
    }

    private string? ResolveHistoryPath()
    {
        try
        {
            int pid = _context.Process.SystemProcess.Id;

            if (LinuxProcFs.GetExecutableName(LinuxProcFs.ReadCommandLine(pid)) is not { } name
                || LinuxProcFs.FindImage(pid, name) is not { } image)
                return null;

            return HistoryPathFor(image.Path);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or NullReferenceException)
        {
            return null;
        }
    }

    /// <summary>The mod's history file for a game executable (it lives next to the executable, in reframework/data)</summary>
    internal static string? HistoryPathFor(string gameExecutable) =>
        Path.GetDirectoryName(gameExecutable) is { Length: > 0 } directory
            ? Path.Combine(directory, HistoryRelativePath)
            : null;

    private static IReadOnlyList<string> NotFoundLines(string? history) =>
    [
        "Skill uptime",
        history is null
            ? "  (could not find the game folder, so My Hunt Report's skill uptime is not available)"
            : "  (My Hunt Report did not save this hunt. Is the mod installed, and did the quest reach its result screen?)"
    ];

    /// <summary>
    /// The skill uptime lines for the newest history entry written after <paramref name="offset"/> whose quest ended
    /// after this one started. False while there is no such entry yet.
    /// </summary>
    internal static bool TryFindSkillLines(string historyFile, long offset, DateTimeOffset questStarted, out IReadOnlyList<string> lines)
    {
        lines = [];

        if (!File.Exists(historyFile))
            return false;

        string text;

        using (var stream = new FileStream(historyFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            // The mod rewrites the file when it clears the history, so a shorter file means start over
            if (offset > stream.Length)
                offset = 0;

            stream.Seek(offset, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            text = reader.ReadToEnd();
        }

        double notBefore = questStarted.ToUnixTimeSeconds() - ClockSlackSeconds;

        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Reverse())
        {
            JsonDocument document;

            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                // A line the mod is still writing
                continue;
            }

            using (document)
            {
                JsonElement root = document.RootElement;

                if (!root.TryGetProperty("quest", out JsonElement quest)
                    || !quest.TryGetProperty("endedAt", out JsonElement endedAt)
                    || !endedAt.TryGetDouble(out double ended)
                    || ended < notBefore)
                    continue;

                lines = SkillLines(root);
                return true;
            }
        }

        return false;
    }

    internal static IReadOnlyList<string> SkillLines(JsonElement entry)
    {
        var rows = new List<(string Name, double Share)>();

        if (entry.TryGetProperty("skills", out JsonElement skills) && skills.ValueKind == JsonValueKind.Array)
            foreach (JsonElement row in skills.EnumerateArray())
            {
                // Healing is listed with the skills by the mod but is an amount of HP, not an uptime
                if (row.TryGetProperty("label", out JsonElement label)
                    && label.TryGetProperty("kind", out JsonElement kind)
                    && kind.GetString() != "skill")
                    continue;

                if (!row.TryGetProperty("name", out JsonElement name) || name.GetString() is not { Length: > 0 } text
                    || !row.TryGetProperty("share", out JsonElement share) || !share.TryGetDouble(out double value))
                    continue;

                rows.Add((text, Math.Clamp(value, 0, 1)));
            }

        var lines = new List<string> { "Skill uptime (from My Hunt Report: share of your damage dealt while each skill was active)" };

        if (rows.Count == 0)
        {
            lines.Add("  (My Hunt Report recorded no skills for this hunt)");
            return lines;
        }

        int width = rows.Max(it => it.Name.Length);

        foreach ((string name, double share) in rows.OrderByDescending(it => it.Share).ThenBy(it => it.Name, StringComparer.Ordinal))
            lines.Add($"  {name.PadRight(width)}  {(share * 100).ToString("0.0", CultureInfo.InvariantCulture),5}%");

        return lines;
    }

    public void Dispose()
    {
        _cancel.Cancel();
        _cancel.Dispose();
    }
}

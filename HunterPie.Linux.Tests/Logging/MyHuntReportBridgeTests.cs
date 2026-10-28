using HunterPie.Linux.Logging;
using System.Text.Json;

namespace HunterPie.Linux.Tests.Logging;

[TestClass]
public class MyHuntReportBridgeTests
{
    private string _directory = string.Empty;
    private string _history = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "hunterpie-bridge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _history = Path.Combine(_directory, "history.jsonl");
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_directory, recursive: true);

    private static string Entry(long endedAt, params (string Kind, string Name, double Share)[] skills)
    {
        string rows = string.Join(",", skills.Select(it =>
            $$"""{"label":{"kind":"{{it.Kind}}"},"name":"{{it.Name}}","share":{{it.Share.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}"""));

        return $$"""{"version":2,"quest":{"result":"clear","elapsedSeconds":600,"endedAt":{{endedAt}}},"skills":[{{rows}}]}""";
    }

    [TestMethod]
    public void ReadsTheEntryWrittenForThisQuest()
    {
        DateTimeOffset started = DateTimeOffset.FromUnixTimeSeconds(1_000_000);
        File.WriteAllLines(_history, [Entry(1_000_600, ("skill", "Agitator", 0.5), ("skill", "Maximum Might", 0.8))]);

        Assert.IsTrue(MyHuntReportBridge.TryFindSkillLines(_history, 0, started, out IReadOnlyList<string> lines));

        StringAssert.Contains(lines[0], "My Hunt Report");
        StringAssert.Contains(lines[1], "Maximum Might");
        StringAssert.Contains(lines[1], "80.0%");
        StringAssert.Contains(lines[2], "Agitator");
        StringAssert.Contains(lines[2], "50.0%");
    }

    [TestMethod]
    public void HealingRowsAreNotUptime()
    {
        File.WriteAllLines(_history, [Entry(1_000_600, ("skill", "Agitator", 0.5), ("heal", "Recovery", 1234))]);

        Assert.IsTrue(MyHuntReportBridge.TryFindSkillLines(_history, 0, DateTimeOffset.FromUnixTimeSeconds(1_000_000), out IReadOnlyList<string> lines));

        Assert.IsFalse(lines.Any(it => it.Contains("Recovery", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void EntriesFromBeforeTheQuestAreIgnored()
    {
        File.WriteAllLines(_history, [Entry(999_000, ("skill", "Old", 0.5))]);

        Assert.IsFalse(MyHuntReportBridge.TryFindSkillLines(_history, 0, DateTimeOffset.FromUnixTimeSeconds(1_000_000), out _));
    }

    [TestMethod]
    public void OnlyWhatWasWrittenAfterTheOffsetIsLookedAt()
    {
        string old = Entry(1_000_100, ("skill", "Earlier Quest", 0.5));
        File.WriteAllLines(_history, [old]);
        long offset = new FileInfo(_history).Length;

        // Nothing new yet
        Assert.IsFalse(MyHuntReportBridge.TryFindSkillLines(_history, offset, DateTimeOffset.FromUnixTimeSeconds(1_000_000), out _));

        File.AppendAllLines(_history, [Entry(1_000_700, ("skill", "This Quest", 0.25))]);

        Assert.IsTrue(MyHuntReportBridge.TryFindSkillLines(_history, offset, DateTimeOffset.FromUnixTimeSeconds(1_000_000), out IReadOnlyList<string> lines));
        Assert.IsTrue(lines.Any(it => it.Contains("This Quest", StringComparison.Ordinal)));
        Assert.IsFalse(lines.Any(it => it.Contains("Earlier Quest", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void AClearedHistoryStartsOver()
    {
        File.WriteAllLines(_history, [Entry(1_000_700, ("skill", "Fresh", 0.25))]);

        Assert.IsTrue(MyHuntReportBridge.TryFindSkillLines(_history, 100_000, DateTimeOffset.FromUnixTimeSeconds(1_000_000), out IReadOnlyList<string> lines));
        Assert.IsTrue(lines.Any(it => it.Contains("Fresh", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ALineThatIsStillBeingWrittenIsSkipped()
    {
        File.WriteAllText(_history, Entry(1_000_700, ("skill", "Done", 0.5)) + "\n" + "{\"version\":2,\"quest\":{\"endedA");

        Assert.IsTrue(MyHuntReportBridge.TryFindSkillLines(_history, 0, DateTimeOffset.FromUnixTimeSeconds(1_000_000), out IReadOnlyList<string> lines));
        Assert.IsTrue(lines.Any(it => it.Contains("Done", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void NoHistoryFileMeansNothingYet()
    {
        Assert.IsFalse(MyHuntReportBridge.TryFindSkillLines(_history, 0, DateTimeOffset.UnixEpoch, out _));
    }

    [TestMethod]
    public void AnEntryWithoutSkillsSaysSo()
    {
        using JsonDocument document = JsonDocument.Parse("""{"quest":{"endedAt":1},"skills":[]}""");

        IReadOnlyList<string> lines = MyHuntReportBridge.SkillLines(document.RootElement);

        StringAssert.Contains(lines[1], "no skills");
    }

    [TestMethod]
    public void ShareIsKeptBetweenZeroAndOneHundredPercent()
    {
        using JsonDocument document = JsonDocument.Parse("""{"skills":[{"label":{"kind":"skill"},"name":"Over","share":1.7}]}""");

        StringAssert.Contains(MyHuntReportBridge.SkillLines(document.RootElement)[1], "100.0%");
    }

    [TestMethod]
    public void TheHistoryFileIsNextToTheGameExecutable()
    {
        Assert.AreEqual(
            "/games/Wilds/reframework/data/MyHuntReport/history.jsonl",
            MyHuntReportBridge.HistoryPathFor("/games/Wilds/MonsterHunterWilds.exe"));
    }

    [TestMethod]
    public void AppendingAfterTheHuntAddsToTheClosedHuntFile()
    {
        var sink = new LogSink(_directory, DateTime.Now);
        string hunt = Path.Combine(_directory, "hunt.log");
        File.WriteAllText(hunt, "===== Hunt summary =====\n");

        sink.AppendAfterHunt(hunt, ["Skill uptime", "  Agitator  50.0%"]);
        sink.Dispose();

        string text = File.ReadAllText(hunt);

        StringAssert.Contains(text, "Skill uptime");
        StringAssert.Contains(text, "Agitator  50.0%");
        StringAssert.Contains(text, "===== Hunt summary =====");
    }

    [TestMethod]
    public void AppendingToAMissingHuntFileIsSkipped()
    {
        var sink = new LogSink(_directory, DateTime.Now);

        sink.AppendAfterHunt(Path.Combine(_directory, "gone.log"), ["Skill uptime"]);
        sink.AppendAfterHunt(null, ["Skill uptime"]);
        sink.Dispose();
    }
}

using HunterPie.Linux.Services;

namespace HunterPie.Linux.Tests.Services;

[TestClass]
public class SkillTableDumpDiffTests
{
    private const string Header = "# HunterPie skill table dump";

    private string _directory = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "hunterpie-skilldump-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_directory, recursive: true);

    private string Dump(string name, params string[] slotLines)
    {
        string path = Path.Combine(_directory, name + ".txt");
        File.WriteAllLines(path, [Header, "# game 1.42.0.3", .. slotLines]);
        return path;
    }

    private static string Struct(int timerBits = 0, int flagWordAt0x20 = 0)
    {
        byte[] data = new byte[0x80];
        BitConverter.GetBytes(timerBits).CopyTo(data, 0x10);
        BitConverter.GetBytes(flagWordAt0x20).CopyTo(data, 0x20);
        return Convert.ToHexString(data);
    }

    private static string Raw(byte[] bytes) => Convert.ToHexString(bytes);

    private static (int Code, string Output) RunDiff(params string[] files)
    {
        TextWriter original = Console.Out;
        var writer = new StringWriter();

        try
        {
            Console.SetOut(writer);
            return (SkillTableDump.Diff(files), writer.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [TestMethod]
    public void AFlagByteFlippingInAPlainTableValueIsReported()
    {
        byte[] off = new byte[8];
        byte[] on = new byte[8];
        on[1] = 1;

        string a = Dump("off", $"slot 1C0 raw {Raw(off)}");
        string b = Dump("on", $"slot 1C0 raw {Raw(on)}");

        (int code, string output) = RunDiff(a, b);

        Assert.AreEqual(0, code);
        StringAssert.Contains(output, "slot 1C0");
        StringAssert.Contains(output, "plain value in the table");
        StringAssert.Contains(output, "+0x01: 00->01");
    }

    [TestMethod]
    public void ATimerStartingInsideAStructIsReported()
    {
        string a = Dump("off", $"slot 1B8 ptr {Struct()} 7F0000001000");
        string b = Dump("on", $"slot 1B8 ptr {Struct(timerBits: BitConverter.SingleToInt32Bits(12.5f))} 7F0000001000");

        (_, string output) = RunDiff(a, b);

        StringAssert.Contains(output, "slot 1B8");
        StringAssert.Contains(output, "+0x10");
        StringAssert.Contains(output, "float 0 -> 12.5");
    }

    [TestMethod]
    public void AnEmptySlotBecomingAPointerShowsWhatItHeldBefore()
    {
        string a = Dump("off", $"slot 1B8 raw {Raw(new byte[8])}");
        string b = Dump("on", $"slot 1B8 ptr {Struct()} 7F0000001000");

        (_, string output) = RunDiff(a, b);

        StringAssert.Contains(output, "now holds a pointer");
        StringAssert.Contains(output, "0000000000000000");
    }

    [TestMethod]
    public void AValueThatAlsoChangesWithTheSkillOffAgainIsLeftOut()
    {
        byte[] off = new byte[8];
        byte[] on = new byte[8];
        byte[] offAgain = new byte[8];
        on[0] = 5;
        offAgain[0] = 9; // moves on its own

        (_, string output) = RunDiff(
            Dump("off", $"slot 1C0 raw {Raw(off)}"),
            Dump("on", $"slot 1C0 raw {Raw(on)}"),
            Dump("off2", $"slot 1C0 raw {Raw(offAgain)}"));

        Assert.IsFalse(output.Contains("slot 1C0", StringComparison.Ordinal));
        StringAssert.Contains(output, "Nothing new changed");
    }

    [TestMethod]
    public void APointerThatMovedWithTheSameContentsIsNotAChange()
    {
        (_, string output) = RunDiff(
            Dump("off", $"slot 1B8 ptr {Struct()} 7F0000001000"),
            Dump("on", $"slot 1B8 ptr {Struct()} 7F0000002000"));

        StringAssert.Contains(output, "Nothing new changed");
    }

    [TestMethod]
    public void DumpsWithoutThePointerValueStillLoad()
    {
        (int code, string output) = RunDiff(
            Dump("off", $"slot 1B8 ptr {Struct()}"),
            Dump("on", $"slot 1B8 ptr {Struct(timerBits: 1)}"));

        Assert.AreEqual(0, code);
        StringAssert.Contains(output, "slot 1B8");
    }

    [TestMethod]
    public void AFileThatIsNotADumpIsRejected()
    {
        string bad = Path.Combine(_directory, "bad.txt");
        File.WriteAllText(bad, "hello");

        (int code, _) = RunDiff(bad, bad);

        Assert.AreEqual(1, code);
    }
}

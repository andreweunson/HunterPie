using HunterPie.Linux.Bootstrap;

namespace HunterPie.Linux.Tests.Bootstrap;

[TestClass]
public class HostOptionsTests
{
    [TestMethod]
    public void DumpSkillsTakesAnOptionalLabel()
    {
        HostOptions plain = HostOptions.Parse(["--dump-skills"]);
        HostOptions labelled = HostOptions.Parse(["--dump-skills", "off"]);

        Assert.IsTrue(plain.DumpSkills);
        Assert.IsNull(plain.DumpLabel);
        Assert.IsTrue(labelled.DumpSkills);
        Assert.AreEqual("off", labelled.DumpLabel);
    }

    [TestMethod]
    public void DumpSkillsLabelIsNotAFlag()
    {
        HostOptions options = HostOptions.Parse(["--dump-skills", "--verbose"]);

        Assert.IsNull(options.DumpLabel);
        Assert.IsTrue(options.Verbose);
    }

    [TestMethod]
    public void DiffSkillsTakesTwoOrThreeFiles()
    {
        CollectionAssert.AreEqual(new[] { "a", "b" }, HostOptions.Parse(["--diff-skills", "a", "b"]).DiffSkills);
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, HostOptions.Parse(["--diff-skills", "a", "b", "c"]).DiffSkills);
    }

    [TestMethod]
    public void DiffSkillsWithTooFewFilesIsAnError()
    {
        Assert.ThrowsException<ArgumentException>(() => HostOptions.Parse(["--diff-skills", "a"]));
    }

    [TestMethod]
    public void UnknownOptionIsAnError()
    {
        Assert.ThrowsException<ArgumentException>(() => HostOptions.Parse(["--nope"]));
    }
}

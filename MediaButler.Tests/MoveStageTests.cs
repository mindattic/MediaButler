using MediaButler.Pipeline;
using NUnit.Framework;

namespace MediaButler.Tests;

[TestFixture]
public class MoveStageTests
{
    [TestCase("Star Wars: A New Hope",                "Star Wars A New Hope")]
    [TestCase("Hannibal/Lecter?",                     "Hannibal Lecter")]
    [TestCase(@"Doctor Who | Series 1",               "Doctor Who Series 1")]
    public void SanitizeForFs_replaces_invalid_chars(string input, string expected) =>
        Assert.That(MoveStage.SanitizeForFs(input), Is.EqualTo(expected));

    [Test]
    public void IsCrossVolume_returns_false_for_same_drive()
    {
        var tmp = Path.GetTempPath();
        Assert.That(MoveStage.IsCrossVolume(tmp, Path.Combine(tmp, "subdir")), Is.False);
    }

    [Test]
    public void SafeMoveDirectory_renames_a_folder_when_target_is_on_the_same_drive()
    {
        using var tmp = new TempDir();
        var src = tmp.MakeDir("source-folder");
        File.WriteAllText(Path.Combine(src, "payload.txt"), "hello");
        var dst = Path.Combine(tmp.Path, "destination-folder");

        MoveStage.SafeMoveDirectory(src, dst);

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(src), Is.False);
            Assert.That(Directory.Exists(dst), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(dst, "payload.txt")), Is.EqualTo("hello"));
        });
    }
}

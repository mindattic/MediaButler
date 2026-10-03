using System.Drawing;
using System.Drawing.Imaging;
using MediaButler.FileBot;
using NUnit.Framework;

namespace MediaButler.Tests;

[TestFixture]
public class SeasonPosterBadgeTests
{
    private string folder = null!;

    [SetUp]
    public void SetUp()
    {
        folder = Path.Combine(Path.GetTempPath(), "mb-season-badge-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(folder, recursive: true); } catch { /* best-effort cleanup */ }
    }

    private static void WritePlaceholderPoster(string path, int width = 680, int height = 1000)
    {
        using var bmp = new Bitmap(width, height);
        using (var g = Graphics.FromImage(bmp))
            g.Clear(Color.SteelBlue);
        bmp.Save(path, ImageFormat.Jpeg);
    }

    [Test]
    public void Apply_overwrites_poster_and_folder_images_in_place()
    {
        var poster = Path.Combine(folder, "poster.jpg");
        var folderImg = Path.Combine(folder, "folder.jpg");
        WritePlaceholderPoster(poster);
        WritePlaceholderPoster(folderImg);
        var before = File.ReadAllBytes(poster);

        SeasonPosterBadge.Apply(folder, season: 4);

        var after = File.ReadAllBytes(poster);
        Assert.That(after, Is.Not.EqualTo(before), "Expected the badge to change the poster's pixel data.");
        using var badged = new Bitmap(folderImg);
        Assert.Multiple(() =>
        {
            Assert.That(badged.Width, Is.EqualTo(680));
            Assert.That(badged.Height, Is.EqualTo(1000));
        });
    }

    [Test]
    public void Apply_is_a_no_op_when_no_poster_files_exist()
    {
        // Nothing to badge -- must not throw.
        Assert.DoesNotThrow(() => SeasonPosterBadge.Apply(folder, season: 1));
    }

    [Test]
    public void Apply_handles_double_digit_seasons_without_throwing()
    {
        var poster = Path.Combine(folder, "poster.jpg");
        WritePlaceholderPoster(poster, width: 300, height: 450); // small poster stresses the font-shrink loop
        Assert.DoesNotThrow(() => SeasonPosterBadge.Apply(folder, season: 12));
    }

}

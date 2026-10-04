using MediaButler.Settings;
using NUnit.Framework;

namespace MediaButler.Tests;

[TestFixture]
public class MediaButlerSettingsTests
{
    [Test]
    public void GetTvdbIdOverride_matches_case_insensitively()
    {
        var settings = new MediaButlerSettings
        {
            TvdbIdOverrides = { ["Kians Bizarre B and B"] = "453851" },
        };

        Assert.That(settings.GetTvdbIdOverride("kians bizarre b and b"), Is.EqualTo("453851"));
    }

    [Test]
    public void GetTvdbIdOverride_returns_null_when_no_entry_matches()
    {
        var settings = new MediaButlerSettings();
        Assert.That(settings.GetTvdbIdOverride("Some Other Show"), Is.Null);
    }

    [Test]
    public void GetTmdbIdOverride_matches_case_insensitively()
    {
        var settings = new MediaButlerSettings
        {
            TmdbIdOverrides = { ["Heat"] = "949" },
        };

        Assert.That(settings.GetTmdbIdOverride("heat"), Is.EqualTo("949"));
    }

    [Test]
    public void GetTmdbIdOverride_returns_null_when_no_entry_matches()
    {
        var settings = new MediaButlerSettings();
        Assert.That(settings.GetTmdbIdOverride("Some Other Movie"), Is.Null);
    }
}

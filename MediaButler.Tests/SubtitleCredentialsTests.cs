using MediaButler.Settings;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace MediaButler.Tests;

[TestFixture]
public class SubtitleCredentialsTests
{
    [Test]
    public void IsComplete_requires_both_user_and_password()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new SubtitleCredentials().IsComplete,                                       Is.False);
            Assert.That(new SubtitleCredentials { User = "x" }.IsComplete,                          Is.False);
            Assert.That(new SubtitleCredentials { Password = "x" }.IsComplete,                      Is.False);
            Assert.That(new SubtitleCredentials { User = "  ", Password = "x" }.IsComplete,         Is.False);
            Assert.That(new SubtitleCredentials { User = "x",  Password = "y" }.IsComplete,         Is.True);
        });
    }

    [Test]
    public void Load_reads_user_and_password_from_configured_section()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MindAttic:Vault:Subtitles:OpenSubtitles:user"]     = "ryandebraal",
                ["MindAttic:Vault:Subtitles:OpenSubtitles:password"] = "secret",
            })
            .Build();

        var creds = SubtitleCredentials.Load(config);

        Assert.Multiple(() =>
        {
            Assert.That(creds.IsComplete, Is.True);
            Assert.That(creds.User,       Is.EqualTo("ryandebraal"));
            Assert.That(creds.Password,   Is.EqualTo("secret"));
        });
    }

    [Test]
    public void Load_returns_incomplete_when_section_is_missing()
    {
        var config = new ConfigurationBuilder().Build();
        var creds  = SubtitleCredentials.Load(config);

        Assert.Multiple(() =>
        {
            Assert.That(creds.IsComplete, Is.False);
            Assert.That(creds.User,       Is.Null);
            Assert.That(creds.Password,   Is.Null);
        });
    }
}

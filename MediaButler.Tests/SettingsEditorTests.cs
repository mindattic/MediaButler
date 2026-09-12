using MediaButler.Settings;
using MindAttic.Vault.Credentials;
using NUnit.Framework;

namespace MediaButler.Tests;

/// <summary>
/// <see cref="SettingsEditor.DescribeProviderKey"/> drives the "LLM API Key" row's status text in
/// the Settings menu — must reflect this app's own Vault-scoped key, not the shared cross-app one.
/// </summary>
[TestFixture]
public class SettingsEditorTests
{
    [Test]
    public void DescribeProviderKey_Reports_SetLlmProviderFirst_When_ProviderId_Blank()
    {
        using var tmp = new TempDir();
        var ownKeys = new AppScopedCredentialStore("mediabutler", new CredentialStore(tmp.Path));

        Assert.That(SettingsEditor.DescribeProviderKey("", ownKeys), Is.EqualTo("(set LLM Provider first)"));
    }

    [Test]
    public void DescribeProviderKey_Reports_NotConfigured_When_No_Own_Key()
    {
        using var tmp = new TempDir();
        var ownKeys = new AppScopedCredentialStore("mediabutler", new CredentialStore(tmp.Path));

        Assert.That(SettingsEditor.DescribeProviderKey("claude-api", ownKeys),
            Is.EqualTo("Not configured (falls back to the shared default)"));
    }

    [Test]
    public void DescribeProviderKey_Reports_Configured_When_Own_Key_Set()
    {
        using var tmp = new TempDir();
        var inner = new CredentialStore(tmp.Path);
        var ownKeys = new AppScopedCredentialStore("mediabutler", inner);
        ownKeys.SetKey("claude-api", "sk-ant-test");

        Assert.That(SettingsEditor.DescribeProviderKey("claude-api", ownKeys), Is.EqualTo("Configured"));
        // Confirms it landed under the scoped id, not the shared one.
        Assert.That(inner.GetKey("mediabutler-claude-api"), Is.EqualTo("sk-ant-test"));
        Assert.That(inner.GetKey("claude-api"), Is.Null);
    }

    [Test]
    public void DescribeProviderKey_Ignores_A_Shared_Key_For_The_Same_Provider()
    {
        using var tmp = new TempDir();
        var inner = new CredentialStore(tmp.Path);
        inner.SetKey("claude-api", "shared-key");
        var ownKeys = new AppScopedCredentialStore("mediabutler", inner);

        Assert.That(SettingsEditor.DescribeProviderKey("claude-api", ownKeys),
            Is.EqualTo("Not configured (falls back to the shared default)"));
    }
}

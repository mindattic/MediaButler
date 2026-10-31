using AngleSharp.Html.Parser;
using Bunit;
using MediaButler.Settings;
using MediaButler.Wpf.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using RunPage = MediaButler.Wpf.UI.Pages.Run;
using MediaButler.Wpf.UI.Layout;
using MindAttic.Vault.Launch;

namespace MediaButler.Wpf.AccessibilityTests;

/// <summary>
/// Fast, always-on structural accessibility checks. bUnit renders components
/// into a fake DOM with no real layout engine, so it can't check computed
/// contrast or target size (see <see cref="AxeScanTests"/> for that) — but it
/// can catch structural regressions instantly, with no browser binaries
/// needed, on every build.
/// </summary>
[TestFixture]
public class ComponentMarkupTests
{
    private static readonly HtmlParser Parser = new();

    private Bunit.TestContext ctx = null!;

    [SetUp]
    public void SetUp()
    {
        ctx = new Bunit.TestContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton(new SettingsService());
        ctx.Services.AddSingleton<PipelineRunner>();
        ctx.Services.AddSingleton<DialogService>();
        ctx.Services.AddSingleton<IDialogService>(sp => sp.GetRequiredService<DialogService>());
    }

    [TearDown]
    public void TearDown() => ctx.Dispose();

    [Test]
    public void Run_page_headings_and_live_region_are_present()
    {
        var cut = ctx.RenderComponent<RunPage>();
        var doc = Parser.ParseDocument(cut.Markup);

        Assert.That(doc.QuerySelector("h2#pipeline-heading"), Is.Not.Null, "PIPELINE heading missing");
        Assert.That(doc.QuerySelector("h2#library-heading"), Is.Not.Null, "LIBRARY TOOLS heading missing");

        var busy = doc.QuerySelector(".busy-label");
        Assert.That(busy, Is.Not.Null, "busy/status label missing");
        Assert.That(busy!.GetAttribute("role"), Is.EqualTo("status"));
        Assert.That(busy.GetAttribute("aria-live"), Is.EqualTo("polite"));

        // The log pane must NOT be a live region — it would spam a screen
        // reader on every emitted line during a full pipeline run.
        var log = doc.QuerySelector("section[aria-label='Pipeline output log']");
        Assert.That(log, Is.Not.Null, "log pane missing its accessible name");
        Assert.That(log!.GetAttribute("aria-live"), Is.Null, "log pane must not be an aria-live region");
    }

    [Test]
    public void Run_page_dryrun_toggle_has_switch_role_and_label()
    {
        var cut = ctx.RenderComponent<RunPage>();
        var doc = Parser.ParseDocument(cut.Markup);

        var toggle = doc.GetElementById("dryrun");
        Assert.That(toggle, Is.Not.Null);
        Assert.That(toggle!.GetAttribute("role"), Is.EqualTo("switch"));

        var label = doc.QuerySelector("label[for='dryrun']");
        Assert.That(label, Is.Not.Null, "dry-run toggle has no associated <label for>");
    }

    /// <summary>LAMP (MB-T27): the in-app settings page is gone; settings are edited only in MindAttic Vault.</summary>
    [Test]
    public void Settings_page_is_gone()
    {
        var uiAssembly = typeof(RunPage).Assembly;
        Assert.That(uiAssembly.GetType("MediaButler.Wpf.UI.Pages.Settings"), Is.Null, "Settings page type still exists");

        var cut = ctx.RenderComponent<TabShell>();
        var doc = Parser.ParseDocument(cut.Markup);
        Assert.Multiple(() =>
        {
            Assert.That(doc.GetElementById("tab-settings"), Is.Null, "Settings tab still rendered");
            Assert.That(doc.GetElementById("panel-settings"), Is.Null, "Settings panel still rendered");
            Assert.That(doc.QuerySelector(".settings-page"), Is.Null, "settings editor markup still rendered");
        });
    }

    [Test]
    public void Settings_cog_is_a_labeled_button()
    {
        var cut = ctx.RenderComponent<TabShell>();
        var doc = Parser.ParseDocument(cut.Markup);

        var cog = doc.GetElementById("settings-cog");
        Assert.That(cog, Is.Not.Null, "settings cog missing");
        Assert.That(cog!.TagName, Is.EqualTo("BUTTON"));
        Assert.That(cog.GetAttribute("aria-label"), Does.Contain("Settings"));
    }

    [Test]
    public void Settings_cog_opens_vault_at_the_MediaButler_tab()
    {
        var fake = new FakeLauncher { SendResult = true };
        var original = VaultSettings.Launcher;
        VaultSettings.Launcher = fake;
        try
        {
            var cut = ctx.RenderComponent<TabShell>();
            cut.Find("#settings-cog").Click();

            Assert.That(fake.Requests, Has.Count.EqualTo(1));
            Assert.That(fake.Requests[0].AppId, Is.EqualTo("MediaButler"));
            Assert.That(fake.Requests[0].Setting, Is.Null);
        }
        finally
        {
            VaultSettings.Launcher = original;
        }
    }

    [Test]
    public void Settings_cog_says_so_when_vault_is_not_installed()
    {
        var fake = new FakeLauncher { SendResult = false, StartResult = false };
        var original = VaultSettings.Launcher;
        VaultSettings.Launcher = fake;
        try
        {
            var cut = ctx.RenderComponent<TabShell>();
            cut.Find("#settings-cog").Click();

            Assert.That(cut.Find(".settings-cog-group [role=status]").TextContent, Does.Contain("isn't installed"));
        }
        finally
        {
            VaultSettings.Launcher = original;
        }
    }

    private sealed class FakeLauncher : IVaultDesktopLauncher
    {
        public bool SendResult { get; init; }
        public bool StartResult { get; init; }
        public List<VaultOpenRequest> Requests { get; } = new();

        public bool TrySend(VaultOpenRequest request) { Requests.Add(request); return SendResult; }
        public bool TryStart(VaultOpenRequest request) { if (!SendResult) Requests.Add(request); return StartResult; }
    }
}

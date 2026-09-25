using AngleSharp.Html.Parser;
using Bunit;
using MediaButler.Settings;
using MediaButler.Wpf.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using RunPage = MediaButler.Wpf.UI.Pages.Run;
using SettingsPage = MediaButler.Wpf.UI.Pages.Settings;

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

    [Test]
    public void Settings_page_every_field_has_a_real_label_association()
    {
        var cut = ctx.RenderComponent<SettingsPage>();
        var doc = Parser.ParseDocument(cut.Markup);

        var inputs = doc.QuerySelectorAll("input");
        Assert.That(inputs.Length, Is.GreaterThan(0));
        foreach (var input in inputs)
        {
            var id = input.GetAttribute("id");
            Assert.That(id, Is.Not.Null.And.Not.Empty, $"input missing id: {input.OuterHtml}");
            var label = doc.QuerySelector($"label[for='{id}']");
            Assert.That(label, Is.Not.Null, $"no <label for='{id}'> found for input {input.OuterHtml}");
        }
    }

    [Test]
    public void Settings_toggles_declare_role_switch()
    {
        var cut = ctx.RenderComponent<SettingsPage>();
        var doc = Parser.ParseDocument(cut.Markup);

        var checkboxes = doc.QuerySelectorAll("input[type='checkbox']");
        Assert.That(checkboxes.Length, Is.GreaterThan(0));
        foreach (var cb in checkboxes)
            Assert.That(cb.GetAttribute("role"), Is.EqualTo("switch"), $"checkbox missing role=switch: {cb.OuterHtml}");
    }

    [Test]
    public void Settings_page_groups_are_real_fieldsets()
    {
        var cut = ctx.RenderComponent<SettingsPage>();
        var doc = Parser.ParseDocument(cut.Markup);

        var fieldsets = doc.QuerySelectorAll("fieldset");
        Assert.That(fieldsets.Length, Is.EqualTo(3), "expected Paths/Options/Advanced fieldsets");
        foreach (var fs in fieldsets)
            Assert.That(fs.QuerySelector("legend"), Is.Not.Null, "fieldset missing <legend>");
    }
}

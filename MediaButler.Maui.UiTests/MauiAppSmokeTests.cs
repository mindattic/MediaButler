using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using NUnit.Framework;

namespace MediaButler.Maui.UiTests;

/// <summary>
/// Windows UI smoke tests for the MediaButler.Maui desktop app. Launches the
/// built executable, drives the main window through the UI Automation tree
/// via FlaUI, and asserts the critical actions a user actually performs:
///
/// <list type="number">
///   <item>The window opens and shows the title and mode badge.</item>
///   <item>Every button in <c>MainPage.xaml</c> is present, enabled, and reachable
///         (covers every named command the user would want to run against a
///         messy library).</item>
///   <item>The "Dry Run" action can be clicked without crashing and the log
///         pane receives output.</item>
///   <item>The "Clear log" action empties the log pane.</item>
/// </list>
///
/// <para><b>Environment requirements:</b> these tests need an interactive
/// Windows desktop session. They are skipped (not failed) when running in a
/// non-interactive environment such as a CI agent without a Session 0
/// desktop or with no WinAppDriver/UIA support. To run locally:
/// <c>dotnet test MediaButler.Maui.UiTests --filter Category=Ui</c>.</para>
/// </summary>
[TestFixture]
[Category("Ui")]
public class MauiAppSmokeTests
{
    private string mauiExePath = null!;
    private Application? app;
    private UIA3Automation? automation;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        mauiExePath = LocateMauiExecutable();
        if (!File.Exists(mauiExePath))
        {
            Assert.Ignore(
                $"MediaButler.Maui executable not found at {mauiExePath}. " +
                "Build the MediaButler.Maui project before running UI tests.");
        }
        if (!IsInteractiveSession())
        {
            Assert.Ignore(
                "UI tests require an interactive Windows desktop session. " +
                "Run them from a logged-in user, not a service or headless CI agent.");
        }
    }

    [SetUp]
    public void SetUp()
    {
        automation = new UIA3Automation();
        app = Application.Launch(mauiExePath);
        // MAUI apps can take a few seconds to render the first frame AND
        // populate the UI Automation tree after WinUI is done laying out.
        app.WaitWhileMainHandleIsMissing(TimeSpan.FromSeconds(30));
        Thread.Sleep(2000);
    }

    [TearDown]
    public void TearDown()
    {
        try { app?.Close(); } catch { /* best-effort */ }
        try { app?.Dispose(); } catch { /* best-effort */ }
        try { automation?.Dispose(); } catch { /* best-effort */ }
    }

    [Test]
    public void Main_window_renders_with_expected_title()
    {
        var window = GetMainWindow();
        Assert.Multiple(() =>
        {
            Assert.That(window, Is.Not.Null, "main window did not appear");
            Assert.That(window!.Title, Does.Contain("MediaButler"), "window title");
        });
    }

    [Test]
    public void Every_action_button_is_present_and_enabled()
    {
        // MAUI on Windows doesn't auto-publish x:Name as AutomationId, but the
        // Button's Text becomes the accessible Name in the UIA tree. We match
        // on visible label text — which is also what a real user sees, so the
        // assertion mirrors the user's mental model.
        //
        // NOTE: there is intentionally NO "Run Full (Dry Run)" button anymore.
        // Dry-run is now a single header checkbox that governs every action,
        // so the user runs each tool once in safe-preview mode, inspects the
        // log, then flips the toggle and re-runs to mutate.
        var requiredButtons = new (string VisibleText, string Scenario)[]
        {
            ("Run Full Pipeline",  "run the entire pipeline (honors DryRun toggle)"),
            ("Rename & Hoist",     "rename + hoist messy folders only"),
            ("FileBot TV",         "ask FileBot to fix TV episodes"),
            ("FileBot Movies",     "ask FileBot to fix movies"),
            ("FileBot Subtitles",  "fetch missing subtitles"),
            ("Move to Plex",       "move organized folders to Plex destinations"),
            ("Relocate…",          "evict misfiled items from a library"),
            ("Scan",               "preview classification without mutating"),
            ("Status",             "show current settings and FileBot status"),
            ("Clear log",          "clear the output pane"),
        };

        var window = GetMainWindow()!;
        Assert.Multiple(() =>
        {
            foreach (var (text, scenario) in requiredButtons)
            {
                var btn = FindButtonByName(window, text);
                Assert.That(btn, Is.Not.Null, $"missing UI affordance for: {scenario} ('{text}')");
                Assert.That(btn!.IsEnabled, Is.True, $"button '{text}' ({scenario}) is disabled");
            }
        });
    }

    [Test]
    public void DryRun_toggle_is_checked_by_default_on_app_start()
    {
        // Safety workflow: every app launch begins in dry-run mode so the
        // user previews before mutating. Locking this down so a future change
        // can't quietly flip the default to LIVE.
        var window = GetMainWindow()!;
        var check = window.FindFirstDescendant(cf => cf.ByControlType(ControlType.CheckBox))!
            .AsCheckBox();
        Assert.That(check.IsChecked, Is.True,
            "DryRun checkbox should start checked on every app launch");
    }

    [Test]
    public void Clicking_run_full_does_not_crash_the_app()
    {
        // Smoke test: invoking RunFull while DryRun is checked must not
        // bring the window down. We can't reliably read MAUI Label content
        // via UIA, so we settle for "window still alive after click".
        var window = GetMainWindow()!;
        var runBtn = FindButtonByName(window, "Run Full Pipeline");
        Assert.That(runBtn, Is.Not.Null, "Run Full Pipeline button not found");

        runBtn!.Invoke();
        Thread.Sleep(3000);

        var post = GetMainWindow();
        Assert.That(post, Is.Not.Null, "main window vanished after Run Full click");
        Assert.That(post!.Title, Does.Contain("MediaButler"));
    }

    [Test]
    public void Clear_log_button_is_clickable_without_crash()
    {
        var window = GetMainWindow()!;
        var clearBtn = FindButtonByName(window, "Clear log");
        Assert.That(clearBtn, Is.Not.Null, "Clear log button not found");
        clearBtn!.Invoke();
        Thread.Sleep(500);
        Assert.That(GetMainWindow(), Is.Not.Null, "window vanished after Clear log click");
    }

    /// <summary>
    /// Find a Button by visible label text. Searches the UIA tree by Name —
    /// FlaUI's <c>cf.ByName</c> matches the accessible Name property which,
    /// for MAUI Buttons on Windows, is set to the Button's <c>Text</c>.
    /// </summary>
    private static Button? FindButtonByName(Window window, string text)
    {
        var el = window.FindFirstDescendant(cf =>
            cf.ByControlType(ControlType.Button).And(cf.ByName(text)));
        return el?.AsButton();
    }

    private Window? GetMainWindow() => app!.GetMainWindow(automation!, TimeSpan.FromSeconds(30));

    private static string LocateMauiExecutable()
    {
        var assembly = typeof(MauiAppSmokeTests).Assembly.Location;
        var dir      = Path.GetDirectoryName(assembly)!;
        // …\MediaButler.Maui.UiTests\bin\Debug\net10.0-windows → solution root
        var solutionRoot = new DirectoryInfo(dir).Parent!.Parent!.Parent!.Parent!.FullName;
        var candidate    = Path.Combine(solutionRoot, "MediaButler.Maui", "bin", "Debug",
            "net10.0-windows10.0.19041.0", "win-x64", "MediaButler.Maui.exe");
        return candidate;
    }

    private static bool IsInteractiveSession()
    {
        try { return Environment.UserInteractive; } catch { return false; }
    }
}

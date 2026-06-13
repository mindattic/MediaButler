using MediaButler.FileBot;
using MediaButler.Maui.Services;
using MediaButler.Pipeline;
using MediaButler.Settings;
// MediaButler.Pipeline now also exports a PipelineRunner (used by the CLI);
// the MAUI shell wraps a UI-flavored variant that captures output, so alias.
using PipelineRunner = MediaButler.Maui.Services.PipelineRunner;

namespace MediaButler.Maui;

public partial class MainPage : ContentPage
{
    private readonly PipelineRunner runner;
    private readonly SettingsService settings;
    // Ring of the most recent log lines. A full-library run emits thousands of
    // lines; keeping every line and re-materializing the whole log on each
    // append is O(n²) and eventually janks the UI. Cap and reuse instead.
    private const int MaxLogLines = 2000;
    private readonly LinkedList<string> logLines = new();
    private bool busy;

    private bool firstAppear = true;
    // Suppress the persisted-settings write while we programmatically sync
    // the checkbox to the loaded value at startup. Without this, the
    // CheckedChanged handler would fire during OnAppearing and clobber the
    // dry-run reset with a redundant save.
    private bool suppressDryRunHandler;

    public MainPage(PipelineRunner runner, SettingsService settings)
    {
        InitializeComponent();
        this.runner = runner;
        this.settings = settings;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (firstAppear)
        {
            // Safety workflow: every app launch begins in dry-run mode so the
            // user can preview the planned actions before any disk mutation.
            // The toggle remains user-controllable during the session.
            var s = settings.Load();
            s.DryRun = true;
            settings.Save(s);
            firstAppear = false;
        }
        SyncDryRunCheckbox();
        RefreshHeader();
    }

    private void SyncDryRunCheckbox()
    {
        suppressDryRunHandler = true;
        try { DryRunCheck.IsChecked = settings.Load().DryRun; }
        finally { suppressDryRunHandler = false; }
    }

    private void OnDryRunToggled(object? sender, CheckedChangedEventArgs e)
    {
        if (suppressDryRunHandler) return;
        var s = settings.Load();
        s.DryRun = e.Value;
        settings.Save(s);
        RefreshHeader();
    }

    private void RefreshHeader()
    {
        var s = settings.Load();
        if (s.DryRun)
        {
            ModeBadge.Text = "DRY RUN";
            ModeBadge.BackgroundColor = Color.FromArgb("#F9A825");
            ModeBadge.TextColor = Colors.Black;
        }
        else
        {
            ModeBadge.Text = "LIVE";
            ModeBadge.BackgroundColor = Color.FromArgb("#2E7D32"); // #388E3C was 3.76:1; #2E7D32 = 4.59:1 (WCAG AA)
            ModeBadge.TextColor = Colors.White;
        }
        PathsLine.Text = $"Source: {s.SourcePath}    TV: {s.TvDestination}    Movies: {s.MoviesDestination}";
        var fb = FileBotClient.TryLocate(s.FileBotPath);
        FileBotLine.Text = fb is null
            ? "FileBot: NOT FOUND — set FileBot Path in Settings"
            : "FileBot: " + fb;
    }

    private void OnClearLog(object? sender, EventArgs e)
    {
        logLines.Clear();
        LogLabel.Text = string.Empty;
    }

    private async void OnRunFull(object? sender, EventArgs e)
    {
        if (!settings.Load().DryRun)
        {
            var ok = await DisplayAlertAsync(
                "Run Full Pipeline — LIVE",
                "This will rename files, run FileBot, and permanently move everything to your Plex library. This cannot be easily undone.\n\nContinue in LIVE mode?",
                accept: "Run Pipeline",
                cancel: "Cancel");
            if (!ok) return;
        }
        _ = ExecuteAsync(PipelineRunner.PipelineAction.RunFull, LabelFor("Run Full Pipeline"));
    }

    private void OnRename(object? sender, EventArgs e)             => _ = ExecuteAsync(PipelineRunner.PipelineAction.Rename,          LabelFor("Rename & Hoist"));
    private void OnFileBotTv(object? sender, EventArgs e)          => _ = ExecuteAsync(PipelineRunner.PipelineAction.FileBotTv,       LabelFor("FileBot: TV"));
    private void OnFileBotMovies(object? sender, EventArgs e)      => _ = ExecuteAsync(PipelineRunner.PipelineAction.FileBotMovies,   LabelFor("FileBot: Movies"));
    private void OnFileBotSubtitles(object? sender, EventArgs e)   => _ = ExecuteAsync(PipelineRunner.PipelineAction.FileBotSubtitles,LabelFor("FileBot: Subtitles"));

    private async void OnMove(object? sender, EventArgs e)
    {
        if (!settings.Load().DryRun)
        {
            var ok = await DisplayAlertAsync(
                "Move to Plex — LIVE",
                "This will permanently move all renamed folders from the inbox to your TV and Movies destinations.\n\nContinue in LIVE mode?",
                accept: "Move Files",
                cancel: "Cancel");
            if (!ok) return;
        }
        _ = ExecuteAsync(PipelineRunner.PipelineAction.Move, LabelFor("Move to Plex"));
    }

    private void OnScan(object? sender, EventArgs e)               => _ = ExecuteAsync(PipelineRunner.PipelineAction.Scan,            "Scan");
    private void OnStatus(object? sender, EventArgs e)             => _ = ExecuteAsync(PipelineRunner.PipelineAction.Status,          "Status");

    /// <summary>Tag the visible action label with the current DryRun state so the log header announces it.</summary>
    private string LabelFor(string action) =>
        settings.Load().DryRun ? action + " [DRY RUN]" : action + " [LIVE]";

    private async void OnFixLibrary(object? sender, EventArgs e)
    {
        var folder = await DisplayPromptAsync(
            "Fix Library Folder",
            "Library folder to fix (e.g. M:\\Movies  or  M:\\TV\\Criminal Minds):",
            initialValue: "",
            maxLength: 260,
            keyboard: Keyboard.Text);
        if (string.IsNullOrWhiteSpace(folder)) return;

        var kind = await DisplayActionSheetAsync(
            "Content type in this folder:",
            cancel: "Cancel",
            destruction: null,
            "Movies", "TV Shows");
        if (kind is null or "Cancel") return;

        var action = kind == "Movies"
            ? PipelineRunner.PipelineAction.FixLibraryMovies
            : PipelineRunner.PipelineAction.FixLibraryTv;
        await ExecuteAsync(action, $"Fix Library ({kind})", folder);
    }

    private async void OnRelocate(object? sender, EventArgs e)
    {
        var s = settings.Load();
        var folder = await DisplayPromptAsync(
            "Relocate",
            "Folder to scan (e.g. M:\\Movies or M:\\TV):",
            initialValue: s.SourcePath,
            maxLength: 260,
            keyboard: Keyboard.Text);
        if (string.IsNullOrWhiteSpace(folder)) return;
        await ExecuteAsync(PipelineRunner.PipelineAction.Relocate, LabelFor("Relocate"), folder);
    }

    private async Task ExecuteAsync(PipelineRunner.PipelineAction action, string label, string? folderOverride = null)
    {
        if (busy) return;
        busy = true;
        SetButtonsEnabled(false);
        BusyLabel.Text = label + " — running…";
        Spinner.IsRunning = true;
        Spinner.IsVisible = true;

        AppendLine($"=== {label} ===");
        var startedAt = DateTime.Now;

        try
        {
            var report = await Task.Run(() => runner.Run(action, line => MainThread.BeginInvokeOnMainThread(() => AppendLine(line)), folderOverride));
            if (IsPipelineAction(action))
            {
                AppendLine(PipelineRunner.FormatReport(settings.Load(), report));
            }
            var elapsed = DateTime.Now - startedAt;
            BusyLabel.Text = $"{label} done in {elapsed.TotalSeconds:F1}s";
        }
        catch (Exception ex)
        {
            AppendLine("!! Exception: " + ex.Message);
            BusyLabel.Text = label + " failed.";
        }
        finally
        {
            Spinner.IsRunning = false;
            Spinner.IsVisible = false;
            SetButtonsEnabled(true);
            busy = false;
            RefreshHeader();
        }
    }

    private static bool IsPipelineAction(PipelineRunner.PipelineAction a) =>
        a is PipelineRunner.PipelineAction.RunFull
          or PipelineRunner.PipelineAction.Rename
          or PipelineRunner.PipelineAction.FileBotTv
          or PipelineRunner.PipelineAction.FileBotMovies
          or PipelineRunner.PipelineAction.FileBotSubtitles
          or PipelineRunner.PipelineAction.Move
          or PipelineRunner.PipelineAction.Relocate
          or PipelineRunner.PipelineAction.FixLibraryMovies
          or PipelineRunner.PipelineAction.FixLibraryTv;

    private void SetButtonsEnabled(bool enabled)
    {
        BtnRunFull.IsEnabled     = enabled;
        BtnRename.IsEnabled      = enabled;
        BtnFbTv.IsEnabled        = enabled;
        BtnFbMovies.IsEnabled    = enabled;
        BtnFbSubs.IsEnabled      = enabled;
        BtnMove.IsEnabled        = enabled;
        BtnFixLibrary.IsEnabled  = enabled;
        BtnRelocate.IsEnabled    = enabled;
        BtnScan.IsEnabled        = enabled;
        BtnStatus.IsEnabled      = enabled;
        // Lock the toggle while a run is in flight so the user can't flip
        // DryRun mid-execution — the value is captured at the start of the run.
        DryRunCheck.IsEnabled    = enabled;
    }

    private void AppendLine(string line)
    {
        logLines.AddLast(line);
        while (logLines.Count > MaxLogLines) logLines.RemoveFirst();
        LogLabel.Text = string.Join(Environment.NewLine, logLines);
        _ = LogScroll.ScrollToAsync(0, double.MaxValue, animated: false);
    }
}

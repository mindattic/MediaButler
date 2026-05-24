using System.Text;
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
    private readonly StringBuilder logBuffer = new();
    private bool busy;

    public MainPage(PipelineRunner runner, SettingsService settings)
    {
        InitializeComponent();
        this.runner = runner;
        this.settings = settings;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
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
            ModeBadge.BackgroundColor = Color.FromArgb("#388E3C");
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
        logBuffer.Clear();
        LogLabel.Text = string.Empty;
    }

    private void OnRunFull(object? sender, EventArgs e)            => _ = ExecuteAsync(PipelineRunner.PipelineAction.RunFull,         "Run Full Pipeline");
    private void OnRunFullDry(object? sender, EventArgs e)         => _ = ExecuteAsync(PipelineRunner.PipelineAction.RunFullDryRun,   "Run Full Pipeline (Dry Run)");
    private void OnRename(object? sender, EventArgs e)             => _ = ExecuteAsync(PipelineRunner.PipelineAction.Rename,          "Rename & Hoist");
    private void OnFileBotTv(object? sender, EventArgs e)          => _ = ExecuteAsync(PipelineRunner.PipelineAction.FileBotTv,       "FileBot: TV");
    private void OnFileBotMovies(object? sender, EventArgs e)      => _ = ExecuteAsync(PipelineRunner.PipelineAction.FileBotMovies,   "FileBot: Movies");
    private void OnFileBotSubtitles(object? sender, EventArgs e)   => _ = ExecuteAsync(PipelineRunner.PipelineAction.FileBotSubtitles,"FileBot: Subtitles");
    private void OnMove(object? sender, EventArgs e)               => _ = ExecuteAsync(PipelineRunner.PipelineAction.Move,            "Move to Plex");
    private void OnScan(object? sender, EventArgs e)               => _ = ExecuteAsync(PipelineRunner.PipelineAction.Scan,            "Scan");
    private void OnStatus(object? sender, EventArgs e)             => _ = ExecuteAsync(PipelineRunner.PipelineAction.Status,          "Status");

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
        await ExecuteAsync(PipelineRunner.PipelineAction.Relocate, "Relocate", folder);
    }

    private async Task ExecuteAsync(PipelineRunner.PipelineAction action, string label, string? relocateOverride = null)
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
            var report = await Task.Run(() => runner.Run(action, line => MainThread.BeginInvokeOnMainThread(() => AppendLine(line)), relocateOverride));
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
          or PipelineRunner.PipelineAction.RunFullDryRun
          or PipelineRunner.PipelineAction.Rename
          or PipelineRunner.PipelineAction.FileBotTv
          or PipelineRunner.PipelineAction.FileBotMovies
          or PipelineRunner.PipelineAction.FileBotSubtitles
          or PipelineRunner.PipelineAction.Move
          or PipelineRunner.PipelineAction.Relocate;

    private void SetButtonsEnabled(bool enabled)
    {
        BtnRunFull.IsEnabled     = enabled;
        BtnRunFullDry.IsEnabled  = enabled;
        BtnRename.IsEnabled      = enabled;
        BtnFbTv.IsEnabled        = enabled;
        BtnFbMovies.IsEnabled    = enabled;
        BtnFbSubs.IsEnabled      = enabled;
        BtnMove.IsEnabled        = enabled;
        BtnRelocate.IsEnabled    = enabled;
        BtnScan.IsEnabled        = enabled;
        BtnStatus.IsEnabled      = enabled;
    }

    private void AppendLine(string line)
    {
        logBuffer.AppendLine(line);
        LogLabel.Text = logBuffer.ToString();
        _ = LogScroll.ScrollToAsync(0, double.MaxValue, animated: false);
    }
}

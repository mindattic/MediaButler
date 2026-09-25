using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using MediaButler.FileBot;
using MediaButler.Settings;
using MediaButler.Wpf.UI.Services;

namespace MediaButler.Wpf.UI.Pages;

public partial class Run : ComponentBase, IDisposable
{
    [Inject] private SettingsService SettingsSvc { get; set; } = null!;
    [Inject] private PipelineRunner Runner { get; set; } = null!;
    [Inject] private IDialogService Dialogs { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;

    // Ring of the most recent log lines. A full-library run emits thousands
    // of lines; keeping every line and re-materializing the whole log on
    // each append is O(n^2) and eventually janks the UI. Cap and reuse.
    private const int MaxLogLines = 2000;
    private readonly LinkedList<string> logLines = new();
    private readonly object logLock = new();
    private bool logDirty;
    private string logText = "";

    private bool busy;
    // Guards the window between a button click and its confirmation/prompt
    // dialog resolving. The dialog backdrop visually blocks other buttons
    // once rendered, but Blazor dispatches queued click events before that
    // render round-trip completes, so a fast second click (e.g. "Fix
    // Library…" then "Relocate…") could otherwise reach DialogService.Show
    // while the first request is still pending, silently overwriting it and
    // leaving the first await unresolved forever.
    private bool dialogInFlight;
    private bool dryRun = true;
    private string busyText = "Ready.";
    private string pathsLine = "";
    private string fileBotLine = "";

    // Blazor re-renders (real DOM diffing) are materially more expensive per
    // update than MAUI's native Label.Text set was. A full run can emit
    // thousands of lines, so batch UI flushes instead of rendering on every
    // single emitted line.
    private Timer? flushTimer;

    protected override void OnInitialized()
    {
        // Safety workflow: every app launch begins in dry-run mode so the
        // user can preview the planned actions before any disk mutation.
        // The toggle remains user-controllable during the session.
        var s = SettingsSvc.Load();
        s.DryRun = true;
        SettingsSvc.Save(s);
        RefreshHeader();

        flushTimer = new Timer(_ => FlushLog(), null, 100, 100);
    }

    /// <summary>
    /// Re-syncs the dry-run badge/checkbox and the paths/FileBot status lines
    /// from persisted settings. Called on load and after every action, and
    /// must also be called by <see cref="Layout.TabShell"/> whenever this tab
    /// is (re)activated: both panels stay mounted for the lifetime of the
    /// circuit (see TabShell.razor), so <see cref="OnInitialized"/> only ever
    /// runs once and cannot by itself pick up settings changes made on the
    /// Settings tab (e.g. toggling DryRun there and saving) while this tab
    /// was hidden — without this call the badge/checkbox would keep showing
    /// the stale value from the last time this tab refreshed, even though
    /// pipeline actions already honor the freshly-saved value.
    /// </summary>
    public void RefreshHeader()
    {
        var s = SettingsSvc.Load();
        dryRun = s.DryRun;
        pathsLine = $"Source: {s.SourcePath}    TV: {s.TvDestination}    Movies: {s.MoviesDestination}";
        var fb = FileBotClient.TryLocate(s.FileBotPath);
        fileBotLine = fb is null
            ? "FileBot: NOT FOUND — set FileBot Path in Settings"
            : "FileBot: " + fb;
    }

    /// <summary>Called by <see cref="Layout.TabShell"/> when this tab becomes active again.</summary>
    public void OnTabActivated()
    {
        RefreshHeader();
        StateHasChanged();
    }

    private void OnDryRunToggled()
    {
        var s = SettingsSvc.Load();
        s.DryRun = dryRun;
        SettingsSvc.Save(s);
        RefreshHeader();
    }

    private void OnClearLog()
    {
        lock (logLock)
        {
            logLines.Clear();
            logText = "";
        }
        StateHasChanged();
    }

    private async Task OnRunFull()
    {
        if (busy || dialogInFlight) return;
        if (!SettingsSvc.Load().DryRun)
        {
            bool ok;
            dialogInFlight = true;
            StateHasChanged();
            try
            {
                ok = await Dialogs.ConfirmAsync(
                    "Run Full Pipeline — LIVE",
                    "This will rename files, run FileBot, and permanently move everything to your Plex library. This cannot be easily undone.\n\nContinue in LIVE mode?",
                    acceptText: "Run Pipeline",
                    cancelText: "Cancel");
            }
            finally
            {
                dialogInFlight = false;
            }
            if (!ok) { StateHasChanged(); return; }
        }
        await Execute(PipelineRunner.PipelineAction.RunFull, LabelFor("Run Full Pipeline"));
    }

    private Task OnRename()           => Execute(PipelineRunner.PipelineAction.Rename,          LabelFor("Rename & Hoist"));
    private Task OnFileBotTv()        => Execute(PipelineRunner.PipelineAction.FileBotTv,        LabelFor("FileBot: TV"));
    private Task OnFileBotMovies()    => Execute(PipelineRunner.PipelineAction.FileBotMovies,    LabelFor("FileBot: Movies"));
    private Task OnFileBotSubtitles() => Execute(PipelineRunner.PipelineAction.FileBotSubtitles, LabelFor("FileBot: Subtitles"));

    private async Task OnMove()
    {
        if (busy || dialogInFlight) return;
        if (!SettingsSvc.Load().DryRun)
        {
            bool ok;
            dialogInFlight = true;
            StateHasChanged();
            try
            {
                ok = await Dialogs.ConfirmAsync(
                    "Move to Plex — LIVE",
                    "This will permanently move all renamed folders from the inbox to your TV and Movies destinations.\n\nContinue in LIVE mode?",
                    acceptText: "Move Files",
                    cancelText: "Cancel");
            }
            finally
            {
                dialogInFlight = false;
            }
            if (!ok) { StateHasChanged(); return; }
        }
        await Execute(PipelineRunner.PipelineAction.Move, LabelFor("Move to Plex"));
    }

    private Task OnScan()   => Execute(PipelineRunner.PipelineAction.Scan, "Scan");
    private Task OnStatus() => Execute(PipelineRunner.PipelineAction.Status, "Status");

    /// <summary>Tag the visible action label with the current DryRun state so the log header announces it.</summary>
    private string LabelFor(string action) =>
        SettingsSvc.Load().DryRun ? action + " [DRY RUN]" : action + " [LIVE]";

    private async Task OnFixLibrary()
    {
        if (busy || dialogInFlight) return;
        dialogInFlight = true;
        StateHasChanged();
        string? folder;
        string? kind;
        try
        {
            folder = await Dialogs.PromptAsync(
                "Fix Library Folder",
                "Library folder to fix (e.g. M:\\Movies  or  M:\\TV\\Criminal Minds):",
                initialValue: "",
                maxLength: 260);
            if (string.IsNullOrWhiteSpace(folder)) return;

            kind = await Dialogs.ChooseAsync("Content type in this folder:", "Cancel", "Movies", "TV Shows");
            if (kind is null) return;
        }
        finally
        {
            dialogInFlight = false;
            StateHasChanged();
        }

        var action = kind == "Movies"
            ? PipelineRunner.PipelineAction.FixLibraryMovies
            : PipelineRunner.PipelineAction.FixLibraryTv;
        await Execute(action, $"Fix Library ({kind})", folder);
    }

    private async Task OnRelocate()
    {
        if (busy || dialogInFlight) return;
        var s = SettingsSvc.Load();
        string? folder;
        dialogInFlight = true;
        StateHasChanged();
        try
        {
            folder = await Dialogs.PromptAsync(
                "Relocate",
                "Folder to scan (e.g. M:\\Movies or M:\\TV):",
                initialValue: s.SourcePath,
                maxLength: 260);
        }
        finally
        {
            dialogInFlight = false;
            StateHasChanged();
        }
        if (string.IsNullOrWhiteSpace(folder)) return;
        await Execute(PipelineRunner.PipelineAction.Relocate, LabelFor("Relocate"), folder);
    }

    private async Task Execute(PipelineRunner.PipelineAction action, string label, string? folderOverride = null)
    {
        if (busy) return;
        busy = true;
        busyText = label + " — running…";
        AppendLine($"=== {label} ===");
        StateHasChanged();
        var startedAt = DateTime.Now;

        try
        {
            var report = await Task.Run(() => Runner.Run(action, AppendLine, folderOverride));
            if (IsPipelineAction(action))
                AppendLine(PipelineRunner.FormatReport(SettingsSvc.Load(), report));
            var elapsed = DateTime.Now - startedAt;
            busyText = $"{label} done in {elapsed.TotalSeconds:F1}s";
        }
        catch (Exception ex)
        {
            AppendLine("!! Exception: " + ex.Message);
            busyText = label + " failed.";
        }
        finally
        {
            busy = false;
            RefreshHeader();
            StateHasChanged();
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

    private void AppendLine(string line)
    {
        lock (logLock)
        {
            logLines.AddLast(line);
            while (logLines.Count > MaxLogLines) logLines.RemoveFirst();
            logDirty = true;
        }
    }

    private void FlushLog()
    {
        string text;
        lock (logLock)
        {
            if (!logDirty) return;
            logDirty = false;
            text = string.Join(Environment.NewLine, logLines);
        }
        _ = InvokeAsync(async () =>
        {
            logText = text;
            StateHasChanged();
            try { await JS.InvokeVoidAsync("mediaButlerInterop.scrollToBottom", "log-output"); }
            catch (JSDisconnectedException) { /* window closing */ }
        });
    }

    public void Dispose() => flushTimer?.Dispose();
}

using Microsoft.AspNetCore.Components;
using MediaButler.Settings;

namespace MediaButler.Wpf.UI.Pages;

public partial class Settings : ComponentBase
{
    [Inject] private SettingsService SettingsSvc { get; set; } = null!;

    private string filePath = "";
    private string sourcePath = "", tvDestination = "", moviesDestination = "", fileBotPath = "";
    private bool dryRun, renameEpisodes, renameMovies, fetchArtwork, enableSubtitles, enableLlmFallback;
    private string subtitleLanguage = "en", llmProvider = "claude";
    private string excludedFolders = "", videoExtensions = "", titleYearOverrides = "";
    private long emptyDeleteSafetyBytes;
    private string statusText = "";

    protected override void OnInitialized()
    {
        filePath = SettingsSvc.FilePath;
        Load();
    }

    private void Load()
    {
        var s = SettingsSvc.Load();
        sourcePath = s.SourcePath;
        tvDestination = s.TvDestination;
        moviesDestination = s.MoviesDestination;
        fileBotPath = s.FileBotPath;

        dryRun = s.DryRun;
        renameEpisodes = s.RenameEpisodes;
        renameMovies = s.RenameMovies;
        fetchArtwork = s.FetchArtwork;
        enableSubtitles = s.EnableSubtitles;
        subtitleLanguage = s.SubtitleLanguage;
        enableLlmFallback = s.EnableLlmFallback;
        llmProvider = s.LlmProvider;

        excludedFolders = string.Join(", ", s.ExcludedFolders);
        videoExtensions = string.Join(", ", s.VideoExtensions);
        emptyDeleteSafetyBytes = s.EmptyDeleteSafetyBytes;
        titleYearOverrides = string.Join(", ", s.TitleYearOverrides);

        statusText = "Loaded.";
    }

    private void OnSave()
    {
        try
        {
            SettingsSvc.Update(s =>
            {
                s.SourcePath = sourcePath.Trim();
                s.TvDestination = tvDestination.Trim();
                s.MoviesDestination = moviesDestination.Trim();
                s.FileBotPath = fileBotPath.Trim();

                s.DryRun = dryRun;
                s.RenameEpisodes = renameEpisodes;
                s.RenameMovies = renameMovies;
                s.FetchArtwork = fetchArtwork;
                s.EnableSubtitles = enableSubtitles;
                s.SubtitleLanguage = string.IsNullOrWhiteSpace(subtitleLanguage) ? "en" : subtitleLanguage.Trim();
                s.EnableLlmFallback = enableLlmFallback;
                s.LlmProvider = string.IsNullOrWhiteSpace(llmProvider) ? "claude" : llmProvider.Trim();

                s.ExcludedFolders = ParseList(excludedFolders);
                s.VideoExtensions = ParseList(videoExtensions);
                s.TitleYearOverrides = ParseList(titleYearOverrides);
                s.EmptyDeleteSafetyBytes = emptyDeleteSafetyBytes;
            });
            statusText = "Saved.";
        }
        catch (Exception ex)
        {
            statusText = "Save failed: " + ex.Message;
        }
    }

    private static string[] ParseList(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return Array.Empty<string>();
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

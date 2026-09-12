using System.Globalization;
using MediaButler.Settings;

namespace MediaButler.Maui.Pages;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsService settings;

    public SettingsPage(SettingsService settings)
    {
        InitializeComponent();
        this.settings = settings;
        FilePathLabel.Text = "settings.json: " + settings.FilePath;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Load();
    }

    private void Load()
    {
        var s = settings.Load();
        EntrySource.Text = s.SourcePath;
        EntryTvDest.Text = s.TvDestination;
        EntryMoviesDest.Text = s.MoviesDestination;
        EntryFileBotPath.Text = s.FileBotPath;

        SwitchDryRun.IsToggled = s.DryRun;
        SwitchRenameEpisodes.IsToggled = s.RenameEpisodes;
        SwitchRenameMovies.IsToggled = s.RenameMovies;
        SwitchFetchArtwork.IsToggled = s.FetchArtwork;
        SwitchEnableSubtitles.IsToggled = s.EnableSubtitles;
        EntrySubtitleLanguage.Text = s.SubtitleLanguage;
        SwitchEnableLlmFallback.IsToggled = s.EnableLlmFallback;
        EntryLlmProvider.Text = s.LlmProvider;

        EntryExcludedFolders.Text = string.Join(", ", s.ExcludedFolders);
        EntryVideoExtensions.Text = string.Join(", ", s.VideoExtensions);
        EntryEmptyDeleteSafetyBytes.Text = s.EmptyDeleteSafetyBytes.ToString(CultureInfo.InvariantCulture);
        EntryTitleYearOverrides.Text = string.Join(", ", s.TitleYearOverrides);

        StatusLabel.Text = "Loaded.";
    }

    private void OnReload(object? sender, EventArgs e) => Load();

    private async void OnSave(object? sender, EventArgs e)
    {
        try
        {
            settings.Update(s =>
            {
                s.SourcePath = (EntrySource.Text ?? "").Trim();
                s.TvDestination = (EntryTvDest.Text ?? "").Trim();
                s.MoviesDestination = (EntryMoviesDest.Text ?? "").Trim();
                s.FileBotPath = (EntryFileBotPath.Text ?? "").Trim();

                s.DryRun = SwitchDryRun.IsToggled;
                s.RenameEpisodes = SwitchRenameEpisodes.IsToggled;
                s.RenameMovies = SwitchRenameMovies.IsToggled;
                s.FetchArtwork = SwitchFetchArtwork.IsToggled;
                s.EnableSubtitles = SwitchEnableSubtitles.IsToggled;
                s.SubtitleLanguage = (EntrySubtitleLanguage.Text ?? "en").Trim();
                s.EnableLlmFallback = SwitchEnableLlmFallback.IsToggled;
                s.LlmProvider = (EntryLlmProvider.Text ?? "claude").Trim();

                s.ExcludedFolders = ParseList(EntryExcludedFolders.Text);
                s.VideoExtensions = ParseList(EntryVideoExtensions.Text);
                s.TitleYearOverrides = ParseList(EntryTitleYearOverrides.Text);
                if (long.TryParse(EntryEmptyDeleteSafetyBytes.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes))
                    s.EmptyDeleteSafetyBytes = bytes;
            });
            StatusLabel.Text = "Saved.";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "Save failed: " + ex.Message;
            await DisplayAlertAsync("Save failed", ex.Message, "OK");
        }
    }

    private static string[] ParseList(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return Array.Empty<string>();
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

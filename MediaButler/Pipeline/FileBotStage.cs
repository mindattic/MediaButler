using MediaButler.FileBot;
using MediaButler.Media;
using MediaButler.Menu;
using MediaButler.Settings;

namespace MediaButler.Pipeline;

/// <summary>
/// Stage 2/3/4: Invoke FileBot for episode/movie rename, artwork, and (optionally)
/// subtitles. Re-scans the source root each entry so the TV vs Movie split is
/// computed from current on-disk state, not stale items.
/// </summary>
public sealed class FileBotStage
{
    private readonly MediaButlerSettings settings;
    private readonly FileBotClient fileBot;

    public FileBotStage(MediaButlerSettings settings, FileBotClient fileBot)
    {
        this.settings = settings;
        this.fileBot = fileBot;
    }

    public void Run()
    {
        RunTv();
        RunMovies();
        if (settings.EnableSubtitles) RunSubtitles();
    }

    public void RunTv()
    {
        var items = new MediaScanner(settings).Scan()
            .Where(i => i.Kind == MediaKind.TvSeason)
            .OrderBy(i => i.ShowName)
            .ThenBy(i => i.SeasonNumber)
            .ToList();

        if (items.Count == 0)
        {
            ConsoleMenu.Status("No TV season folders to process.", ConsoleMenu.Dim);
            return;
        }

        ConsoleMenu.Status($"Processing {items.Count} TV season folder(s)...", ConsoleMenu.Normal);
        Console.WriteLine();

        foreach (var item in items)
        {
            Console.Write("  " + item.OriginalName);
            try
            {
                if (settings.RenameEpisodes)
                {
                    var rn = fileBot.RenameTvEpisodes(item.FullPath);
                    ConsoleMenu.WriteColor(rn.Success ? "  [rename ok]" : "  [rename: " + rn.ExitCode + "]",
                        rn.Success ? ConsoleMenu.Ok : ConsoleMenu.Err);
                }
                if (settings.FetchArtwork)
                {
                    var aw = fileBot.FetchTvArtwork(item.FullPath);
                    ConsoleMenu.WriteColor(aw.Success ? "  [artwork ok]" : "  [artwork: " + aw.ExitCode + "]",
                        aw.Success ? ConsoleMenu.Ok : ConsoleMenu.Err);
                }
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                ConsoleMenu.WriteColor("  ! " + ex.Message, ConsoleMenu.Err, newline: true);
            }
        }
    }

    public void RunMovies()
    {
        var items = new MediaScanner(settings).Scan()
            .Where(i => i.Kind == MediaKind.Movie)
            .OrderBy(i => i.MovieTitle)
            .ToList();

        if (items.Count == 0)
        {
            ConsoleMenu.Status("No movie folders to process.", ConsoleMenu.Dim);
            return;
        }

        ConsoleMenu.Status($"Processing {items.Count} movie folder(s)...", ConsoleMenu.Normal);
        Console.WriteLine();

        foreach (var item in items)
        {
            Console.Write("  " + item.OriginalName);
            try
            {
                // Rename movie files first — this both cleans the filenames
                // (Heat.1995.YIFY.mp4 -> Heat (1995).mp4) and writes the xattr
                // metadata the artwork script needs.
                if (settings.RenameMovies)
                {
                    var rn = fileBot.RenameMovie(item.FullPath);
                    ConsoleMenu.WriteColor(rn.Success ? "  [rename ok]" : "  [rename: " + rn.ExitCode + "]",
                        rn.Success ? ConsoleMenu.Ok : ConsoleMenu.Err);
                }
                if (settings.FetchArtwork)
                {
                    var aw = fileBot.FetchMovieArtwork(item.FullPath);
                    ConsoleMenu.WriteColor(aw.Success ? "  [artwork ok]" : "  [artwork: " + aw.ExitCode + "]",
                        aw.Success ? ConsoleMenu.Ok : ConsoleMenu.Err);
                }
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                ConsoleMenu.WriteColor("  ! " + ex.Message, ConsoleMenu.Err, newline: true);
            }
        }
    }

    public void RunSubtitles()
    {
        var items = new MediaScanner(settings).Scan()
            .Where(i => i.Kind is MediaKind.TvSeason or MediaKind.Movie)
            .ToList();

        if (items.Count == 0)
        {
            ConsoleMenu.Status("Nothing to fetch subtitles for.", ConsoleMenu.Dim);
            return;
        }

        ConsoleMenu.Status($"Fetching {settings.SubtitleLanguage} subtitles for {items.Count} folder(s)...", ConsoleMenu.Normal);
        Console.WriteLine();

        var authWarned = false;
        foreach (var item in items)
        {
            Console.Write("  " + item.OriginalName);
            var sub = fileBot.GetSubtitles(item.FullPath, settings.SubtitleLanguage);
            if (sub.LooksLikeAuthFailure)
            {
                ConsoleMenu.WriteColor("  [no creds]", ConsoleMenu.Err, newline: true);
                if (!authWarned)
                {
                    ConsoleMenu.Status("OpenSubtitles returned 401. Configure credentials in FileBot Preferences and retry.", ConsoleMenu.Err);
                    authWarned = true;
                }
                continue;
            }
            ConsoleMenu.WriteColor(sub.Success ? "  [ok]" : "  [exit " + sub.ExitCode + "]",
                sub.Success ? ConsoleMenu.Ok : ConsoleMenu.Err, newline: true);
        }
    }
}

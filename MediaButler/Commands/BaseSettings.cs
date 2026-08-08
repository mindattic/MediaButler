using System.ComponentModel;
using MediaButler.Settings;
using MediaButler.Ui;
using Spectre.Console.Cli;

namespace MediaButler.Commands;

/// <summary>
/// Global flags shared by every MediaButler subcommand. Each command's own
/// <c>Settings</c> derives from this so users can pass <c>--dry-run</c>,
/// <c>--source</c>, etc. consistently. None of these are persisted — they only
/// affect the current process.
/// </summary>
public class BaseSettings : CommandSettings
{
    [Description("Force dry-run for this invocation (overrides the persisted setting).")]
    [CommandOption("-n|--dry-run")]
    public bool DryRun { get; init; }

    [Description("Force LIVE mode for this invocation (overrides a persisted DryRun=true). Ignored when --dry-run is also given.")]
    [CommandOption("--live")]
    public bool Live { get; init; }

    [Description("Override the source inbox(es) for this run. Repeatable: every --source is processed in order; the persisted SourcePath/ExtraSources are ignored when any is given.")]
    [CommandOption("--source <PATH>")]
    public string[]? Source { get; init; }

    [Description("Force-enable subtitle fetching for this invocation (overrides the persisted EnableSubtitles).")]
    [CommandOption("--subtitles")]
    public bool Subtitles { get; init; }

    [Description("Process container subfolders (temp, incomplete, ...) inside each source as inboxes too. Accepts an optional explicit value: --recursive or --recursive=true/false.")]
    [CommandOption("-r|--recursive [true|false]")]
    public FlagValue<bool?>? Recursive { get; init; }

    [Description("Override TvDestination for this run.")]
    [CommandOption("--tv-dest|--tvDest <PATH>")]
    public string? TvDest { get; init; }

    [Description("Override MoviesDestination for this run.")]
    [CommandOption("--movies-dest|--moviesDest|--movieDest <PATH>")]
    public string? MoviesDest { get; init; }

    [Description("Override MusicDestination for this run (music folders are moved as-is, never renamed).")]
    [CommandOption("--music-dest|--musicDest <PATH>")]
    public string? MusicDest { get; init; }

    [Description("Cap the number of items each stage processes. Useful for smoke-testing a new config without running the whole inbox.")]
    [CommandOption("--limit <N>")]
    public int? Limit { get; init; }

    [Description("Duplicate-movie policy when the destination folder already has content: keep-largest (default; the copy with the larger primary video wins, the other is deleted) or flag (leave both, surface for a human — classic behaviour).")]
    [CommandOption("--duplicates <keep-largest|flag>")]
    public string? Duplicates { get; init; }

    [Description("Duplicate-episode policy when a TV season merge finds a name/episode collision: keep-largest (default; the copy with the larger video wins) or flag (leave both, surface for a human — classic MB-LAW-9 behaviour).")]
    [CommandOption("--tv-duplicates <keep-largest|flag>")]
    public string? TvDuplicates { get; init; }

    [Description("Bypass the source/destination overlap safety check. Use when pointing a command at a destination folder to repair an existing library (e.g. hoist --source M:\\Movies --no-guard).")]
    [CommandOption("--no-guard")]
    public bool NoGuard { get; init; }

    [Description("Print only the final summary and errors.")]
    [CommandOption("-q|--quiet")]
    public bool Quiet { get; init; }

    [Description("Print every status line including FileBot detail.")]
    [CommandOption("--verbose")]
    public bool Verbose { get; init; }

    /// <summary>Verbosity floor derived from <see cref="Quiet"/> / <see cref="Verbose"/>.</summary>
    public Verbosity Verbosity =>
        Quiet ? Verbosity.Quiet :
        Verbose ? Verbosity.Verbose :
        Verbosity.Normal;

    /// <summary>Overlay parsed flags onto a freshly-loaded settings object.</summary>
    public void ApplyTo(MediaButlerSettings s)
    {
        // --dry-run wins over --live: when in doubt, mutate nothing.
        if (Live) s.DryRun = false;
        if (DryRun) s.DryRun = true;
        if (Subtitles) s.EnableSubtitles = true;
        var sources = (Source ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToArray();
        if (sources.Length > 0)
        {
            // Any explicit --source replaces BOTH the primary and the extras —
            // "process exactly these inboxes this run".
            s.SourcePath   = sources[0];
            s.ExtraSources = sources.Skip(1).ToArray();
        }
        // FlagValue: bare "--recursive" sets IsSet with a null value (= true);
        // "--recursive=false" carries an explicit value.
        if (Recursive is { IsSet: true }) s.Recursive = Recursive.Value ?? true;
        if (!string.IsNullOrWhiteSpace(TvDest))     s.TvDestination     = TvDest.Trim();
        if (!string.IsNullOrWhiteSpace(MoviesDest)) s.MoviesDestination = MoviesDest.Trim();
        if (!string.IsNullOrWhiteSpace(MusicDest))  s.MusicDestination  = MusicDest.Trim();
        if (Limit.HasValue && Limit.Value > 0) s.Limit = Limit;
        if (NoGuard) s.NoGuard = true;
        if (!string.IsNullOrWhiteSpace(Duplicates))
        {
            s.DuplicateMovieAction = Duplicates.Trim().Replace("-", "").ToLowerInvariant() switch
            {
                "keeplargest" => DuplicateMovieAction.KeepLargest,
                "flag"        => DuplicateMovieAction.Flag,
                var other     => throw new InvalidOperationException(
                    $"Unknown --duplicates value '{other}': use keep-largest or flag."),
            };
        }
        if (!string.IsNullOrWhiteSpace(TvDuplicates))
        {
            s.DuplicateEpisodeAction = TvDuplicates.Trim().Replace("-", "").ToLowerInvariant() switch
            {
                "keeplargest" => DuplicateMovieAction.KeepLargest,
                "flag"        => DuplicateMovieAction.Flag,
                var other     => throw new InvalidOperationException(
                    $"Unknown --tv-duplicates value '{other}': use keep-largest or flag."),
            };
        }
    }
}

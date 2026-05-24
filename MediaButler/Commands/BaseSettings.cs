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

    [Description("Override SourcePath for this run.")]
    [CommandOption("--source <PATH>")]
    public string? Source { get; init; }

    [Description("Override TvDestination for this run.")]
    [CommandOption("--tv-dest <PATH>")]
    public string? TvDest { get; init; }

    [Description("Override MoviesDestination for this run.")]
    [CommandOption("--movies-dest <PATH>")]
    public string? MoviesDest { get; init; }

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
        if (DryRun) s.DryRun = true;
        if (!string.IsNullOrWhiteSpace(Source))     s.SourcePath        = Source!;
        if (!string.IsNullOrWhiteSpace(TvDest))     s.TvDestination     = TvDest!;
        if (!string.IsNullOrWhiteSpace(MoviesDest)) s.MoviesDestination = MoviesDest!;
    }
}

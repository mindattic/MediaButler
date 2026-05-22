using MediaButler.Settings;

namespace MediaButler.App;

/// <summary>
/// Parsed CLI invocation. Returned by <see cref="Parse"/> and consumed by
/// <see cref="MediaButlerApp"/>. Holds the chosen subcommand plus any
/// per-run setting overrides — none of these are persisted, they only
/// affect the current process.
/// </summary>
public sealed class CliOptions
{
    public CliCommand Command { get; init; } = CliCommand.Menu;
    public bool DryRun { get; init; }
    public string? SourceOverride { get; init; }
    public string? TvDestOverride { get; init; }
    public string? MoviesDestOverride { get; init; }
    public bool ShowHelp { get; init; }
    public bool ShowVersion { get; init; }
    public Verbosity Verbosity { get; init; } = Verbosity.Normal;
    public string? UnknownArg { get; init; }

    /// <summary>
    /// Parse argv. The first non-flag argument is treated as the subcommand
    /// (defaulting to <see cref="CliCommand.Menu"/> if absent). Flags may
    /// precede or follow the subcommand. Unknown subcommands return
    /// <see cref="UnknownArg"/> set so the caller can print help and exit 1.
    /// </summary>
    public static CliOptions Parse(string[] args)
    {
        CliCommand command = CliCommand.Menu;
        bool commandSet = false, dryRun = false, help = false, version = false;
        Verbosity verbosity = Verbosity.Normal;
        string? src = null, tv = null, movies = null, unknown = null;

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a.ToLowerInvariant())
            {
                case "--dry-run":
                case "-n":
                    dryRun = true;
                    break;
                case "--help":
                case "-h":
                case "-?":
                case "help":
                    help = true;
                    break;
                case "--version":
                case "-v":
                    version = true;
                    break;
                case "--quiet":
                case "-q":
                    verbosity = Verbosity.Quiet;
                    break;
                case "--verbose":
                    verbosity = Verbosity.Verbose;
                    break;
                case "--source" when i + 1 < args.Length:
                    src = args[++i];
                    break;
                case "--tv-dest" when i + 1 < args.Length:
                    tv = args[++i];
                    break;
                case "--movies-dest" when i + 1 < args.Length:
                    movies = args[++i];
                    break;
                default:
                    if (a.StartsWith("--", StringComparison.Ordinal) || a.StartsWith('-'))
                    {
                        unknown ??= a;
                        break;
                    }
                    if (!commandSet && TryParseCommand(a, out var parsed))
                    {
                        command = parsed;
                        commandSet = true;
                    }
                    else if (!commandSet)
                    {
                        unknown ??= a;
                    }
                    break;
            }
        }

        return new CliOptions
        {
            Command            = command,
            DryRun             = dryRun,
            SourceOverride     = src,
            TvDestOverride     = tv,
            MoviesDestOverride = movies,
            ShowHelp           = help,
            ShowVersion        = version,
            Verbosity          = verbosity,
            UnknownArg         = unknown,
        };
    }

    private static bool TryParseCommand(string raw, out CliCommand cmd)
    {
        switch (raw.ToLowerInvariant())
        {
            case "run":         cmd = CliCommand.RunFull;          return true;
            case "scan":        cmd = CliCommand.Scan;             return true;
            case "rename":      cmd = CliCommand.Rename;           return true;
            case "filebot-tv":  cmd = CliCommand.FileBotTv;        return true;
            case "filebot-movies": cmd = CliCommand.FileBotMovies; return true;
            case "filebot-subtitles":
            case "subtitles":   cmd = CliCommand.FileBotSubtitles; return true;
            case "move":        cmd = CliCommand.Move;             return true;
            case "relocate":    cmd = CliCommand.Relocate;         return true;
            case "status":      cmd = CliCommand.Status;           return true;
            case "menu":        cmd = CliCommand.Menu;             return true;
            default:            cmd = CliCommand.Menu;             return false;
        }
    }

    /// <summary>Apply per-invocation overrides onto a freshly-loaded settings object.</summary>
    public void ApplyTo(MediaButlerSettings s)
    {
        if (DryRun)                                              s.DryRun            = true;
        if (!string.IsNullOrWhiteSpace(SourceOverride))          s.SourcePath        = SourceOverride!;
        if (!string.IsNullOrWhiteSpace(TvDestOverride))          s.TvDestination     = TvDestOverride!;
        if (!string.IsNullOrWhiteSpace(MoviesDestOverride))      s.MoviesDestination = MoviesDestOverride!;
    }

    public const string HelpText = """
        MediaButler — automated media renamer + reorganizer.

        USAGE
          mediabutler [<command>] [options]

        COMMANDS
          (none)              Launch interactive menu (default).
          run                 Run the full pipeline non-interactively.
          scan                Classify everything under the source root and exit.
          rename              Stage 1 only — local rename + hoist + empty cleanup.
          filebot-tv          Stage 2 — FileBot TV rename + artwork.
          filebot-movies      Stage 3 — FileBot movie rename + artwork.
          filebot-subtitles   Stage 4 — fetch subtitles via OpenSubtitles.
          move                Stage 5 — move to TV/Movies destinations.
          relocate            Library cleanup: move misplaced items out of the
                              scanned folder (e.g. a TvSeason living in
                              M:\Movies gets sent to TvDestination). Items
                              already in the right place are left alone.
          status              Print settings, paths, and a scan summary.
          help                Show this message.

        OPTIONS
          --dry-run, -n            Force dry-run for this invocation (overrides setting).
          --source <path>          Override SourcePath for this run.
          --tv-dest <path>         Override TvDestination for this run.
          --movies-dest <path>     Override MoviesDestination for this run.
          --quiet, -q              Print only the final summary and errors.
          --verbose                Print every status line including FileBot detail.
          --help, -h, -?           Show this message.
          --version, -v            Print the running build version and exit.

        EXAMPLES
          mediabutler run --dry-run
          mediabutler scan --source M:\Movies
          mediabutler rename --dry-run --source D:\Temp\Inbox
          mediabutler relocate --dry-run --source M:\Movies
          mediabutler status

        EXIT CODES
          0  success / nothing to do
          1  validation failure (bad source, overlap, unknown command, or stage errors)
        """;
}

public enum Verbosity { Quiet, Normal, Verbose }

public enum CliCommand
{
    Menu,
    RunFull,
    Scan,
    Rename,
    FileBotTv,
    FileBotMovies,
    FileBotSubtitles,
    Move,
    Relocate,
    Status,
}

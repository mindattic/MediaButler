# MediaButler

Point MediaButler at a folder of messy torrent dumps and get a Plex-ready TV and movie library back: names cleaned, matched by FileBot, duplicates resolved, with a dry run before anything moves.

[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/) [![Platform Windows](https://img.shields.io/badge/platform-Windows-0078D4)](#quick-start) [![Front doors](https://img.shields.io/badge/front%20doors-CLI%20%2B%20WPF%20%2B%20MCP-555555)](#features) [![Status active](https://img.shields.io/badge/status-active-2ea44f)](docs/BIBLE.md)

```text
 BEFORE  M:\Torrents                                   AFTER  M:\TV  and  M:\Movies
 ------------------------------------------------      ----------------------------------------------
 Better.Call.Saul.S05.Complete.1080p.WEB-DL.x265-GRP   M:\TV\Better Call Saul - Season 05\
 Bones - Season 1-12\Season 1 ... Season 12            M:\TV\Bones - Season 01 ... Bones - Season 12
 [YTS.MX] Heat.1995.1080p.BluRay                       M:\Movies\Heat (1995)\
 Studio.Ghibli\Spirited.Away.2001 + Howls...2004       M:\Movies\Spirited Away (2001)\ + one per film
 Blade Runner 2049 (2017)                              M:\Movies\Blade Runner 2049 (2017)\
 Breaking Bad Season 1-5 (empty shell, no video)       deleted after a byte-size safety check
 The Venture Bros. - Extras                            left in place, listed under Needs manual fix
```

Illustrative run, built from the naming cases the parser and pipeline tests cover. MediaButler is a Windows console app with no hosted demo; clone it and start with `mb --dry-run run` to see the same plan for your own folders.

## Why

- See every rename, move and delete before it happens: dry-run prints `[dry: -> target]` for each action and runs FileBot in `TEST` mode.
- Re-run it as often as you like: canonical names such as `Better Call Saul - Season 05` and `Heat (1995)` round-trip through the parser, so a clean library is a no-op.
- Stop triaging duplicate rips by hand: the bigger copy wins by default and the loser is written to the audit log.
- Never wreck an organised library by accident: MediaButler refuses to run when the source overlaps a destination.
- Teach it your trackers' naming quirks without touching code: every folder name it sees lands in a hand-editable `variations.json` that pins classifications.
- Drive it from a terminal, a desktop window or an AI agent: one pipeline, three front doors.

## Features

### Cleaning and classification

- Cleans release names (`Better.Call.Saul.S05.Complete.1080p...`) into FileBot-friendly stems (`Better Call Saul - Season 05`); movies become `Title (YYYY)`.
- Hoists nested `Season N` folders out of multi-season dumps and pads season numbers (`Season 1` becomes `Season 01`).
- Files loose episode files into their `{Show} - Season XX` folder, splits multi-movie packs into one folder per film, and hoists collection husks (`Studio.Ghibli/`) into individual movies.
- Deletes empty disguised folders only after a byte-size sanity check; surfaces `Extras`, `Specials` and `Bonus` folders for manual review.
- Detects music folders so they are never deleted as empty or renamed as movies, and moves them as-is when a music destination is set.
- Optional LLM fallback through MindAttic.Legion classifies the long tail the regex parser cannot.

### Matching and moving

- Hands cleaned folders to FileBot for TV (TheTVDB) and movie (TheMovieDB) renames, artwork, and optional OpenSubtitles subtitles.
- Moves TV into a flat `{TvDestination}\{Show} - Season XX\` layout and movies into `{MoviesDestination}\{Title} (YYYY)\`, merging into seasons that already exist.
- Routes same-name reboots to `{Show} (Year) - Season XX` once the library already holds a year-tagged folder for that show.
- Resolves duplicate movies and duplicate episodes by policy (`KeepLargest` or `Flag`).
- `relocate` evicts TV folders that drifted into the movies library, and the reverse.

### Front doors

- Spectre.Console CLI with subcommands plus an interactive menu.
- `MediaButler.Wpf`: an optional WPF + BlazorWebView desktop shell with Run and Settings tabs, built to WCAG 2.2 AA.
- `mediabutler mcp`: a Model Context Protocol server over stdio for agent hosts.

## Quick start

Prerequisites: Windows, the .NET 10 SDK, and FileBot (default path `C:\Program Files\FileBot\filebot.exe`). The project references the `MindAttic.Vault` and `MindAttic.Legion` NuGet packages.

```powershell
git clone https://github.com/mindattic/MediaButler.git
cd MediaButler
dotnet build MediaButler.slnx

# See what would happen, touching nothing
.\mb.cmd --dry-run run --source "M:\Torrents"

# Do it for real
.\mb.cmd run --source "M:\Torrents" --live
```

You should see a per-item log, then a final report with counts for renamed, hoisted, consolidated, split, moved, FileBot matches, artwork and subtitles, errors, and a `Needs manual fix` list. Run `.\mb.cmd` with no arguments for the interactive menu, where Settings are edited and saved to `%APPDATA%\MindAttic\MediaButler\settings.json`.

## How it works

```text
        M:\Torrents + ExtraSources (+ --recursive container subfolders)
                            |   one pass per source, exit codes combined (1 > 2 > 0)
                            v
   MediaScanner --classify--> MediaItem { Kind, ... }
   (folders + loose root files; consults the variation catalog's pins first,
    then records every classification back into it)
                            |
                    PipelineRunner (orchestrator; 0/1/2 exit code)
                            |
                    PathGuard.ValidatePaths (refuse on source/destination overlap)
                            v
   RenameStage -> FileBotStage -> MoveStage        [relocate is a separate command]
   (local clean,   (filebot.exe:    (cross-volume move to the flat Plex
    hoist seasons,  TV/Movies/subs/  layout, merge into existing seasons,
    consolidate     artwork)         move music as-is)
    episodes, split
    packs, wrap loose
    files, merge dups,
    delete empties)
        |
        v
   unclassifiable folder/file --(EnableLlmFallback)--> LegionFallbackParser
                                                  -> MindAttic.Legion -> provider

   Front doors (same DI graph / same PipelineRunner):
     Spectre.Console.Cli subcommands + interactive menu
     MediaButler.Wpf desktop shell
     MCP server (stdio JSON-RPC 2.0)

   Settings:    %APPDATA%\MindAttic\MediaButler\settings.json    (via MindAttic.Vault)
   Variations:  %APPDATA%\MindAttic\MediaButler\variations.json  (naming corpus + pins)
```

### Pipeline stages

1. Self-rename pass (`RenameStage`). Cleans messy folder names into FileBot-friendly stems, `Title (YYYY)` for movies. Hoists nested `Season N` subfolders out of multi-season parent dumps onto the source root and pads season numbers. Consolidates loose episode files into their `{Show} - Season XX` folder, splits multi-movie packs into one folder per film, hoists collection husks (`MovieCollection`) into individual movie folders, and merges duplicate-season dumps file by file. Empty disguised folders (no video underneath, under the safety byte floor) are deleted. `Extras`, `Specials` and `Bonus` folders stay in place and appear in the final report.
2. FileBot rename pass (`FileBotStage`). Renames TV episodes via TheTVDB, renames movies via TheMovieDB, fetches show artwork (`fn:artwork.tvdb`) and movie artwork (`fn:artwork`, after writing xattr via rename, which works around the `artwork.tmdb` script bug).
3. Optional subtitle pass. Calls `filebot -get-subtitles` when `EnableSubtitles` is on. Credentials come from the MindAttic Vault chain; see [OpenSubtitles credentials](#opensubtitles-credentials).
4. Move-to-Plex pass (`MoveStage`). TV seasons move to `{TvDestination}\{Show} - Season XX\`, one flat folder per season with its own artwork; movies move to `{MoviesDestination}\{Title} (YYYY)\`; music folders move as-is to `MusicDestination` when configured. Reboot-safe routing sends year-tagged TV to `{Show} (Year) - Season XX` once the destination already has a year-tagged folder for that show.
5. Final report. Items renamed, hoisted, consolidated, pack-split and moved, FileBot successes, artwork and subtitle counts, errors, and a `Needs manual fix` list (Unknown, Extras, and any item that hit a pre-existing target).

`relocate` is a separate command that intentionally operates on a destination rather than a source.

### Projects

| Project | Role | Status |
| --- | --- | --- |
| `MediaButler/` | The console app: Spectre.Console CLI, interactive menu and MCP server. References `MindAttic.Vault` and `MindAttic.Legion`. | done |
| `MediaButler.Tests/` | NUnit coverage for the parser, scanner, pipeline stages, guards, CLI and MCP server. | done |
| `MediaButler.Wpf.UI/` | Razor Class Library: the shell's markup (WCAG 2.2 AA) plus its own `Services/PipelineRunner` and `ConsoleCaptureWriter`. | partial |
| `MediaButler.Wpf/` | Optional WPF + BlazorWebView GUI shell (`net10.0-windows10.0.19041.0`) hosting `MediaButler.Wpf.UI.App`. Windows desktop only; not part of the headless test gate. | partial |
| `MediaButler.Wpf.UiTests/` | FlaUI smoke tests that drive the WPF shell's window and buttons. Windows desktop only. | partial |
| `MediaButler.Wpf.AccessibilityTests/` | bUnit markup-contract tests plus a real-Chromium axe-core WCAG 2.2 AA scan. Windows desktop only. | partial |

### What it is not

- A downloader or torrent client. It never fetches media; it organises what is already on disk under `SourcePath`.
- A metadata database. Episode titles, posters and movie matching are FileBot's job (TheTVDB, TheMovieDB); MediaButler shells out to FileBot and does not query those APIs itself.
- A media server. It produces a Plex-compatible folder layout; it does not stream, scan or talk to a Plex server.
- A destination editor, except for the explicit `relocate` command. Every other stage only touches `SourcePath`.
- Vendor-locked to one LLM. Fallback parsing routes through `MindAttic.Legion`; no provider SDK is hard-coded.
- A music organiser. Music is detected so it is never deleted or renamed as a movie, and can be moved as-is, but tagging and restructuring music libraries is another tool's job.

The canon for these facts, with law IDs and verifying tests, lives in [docs/BIBLE.md](docs/BIBLE.md).

## Commands

All commands share the flags in `MediaButler/Commands/BaseSettings.cs`: `--dry-run` or `-n`, `--live`, `--source` (repeatable), `--subtitles`, `--recursive` or `-r`, `--tv-dest`, `--movies-dest`, `--music-dest`, `--limit`, `--duplicates`, `--tv-duplicates`, `--no-guard`, `--quiet` or `-q`, `--verbose`.

| Command | What it runs |
| --- | --- |
| `mediabutler run` | The full pipeline: rename, FileBot, move. |
| `mediabutler scan` | Read-only classification pass; prints what each item would be classified as. |
| `mediabutler rename` | Stage 1 followed by FileBot and move, same as `run`. |
| `mediabutler hoist` | Stage 1 only: local rename, hoist nested seasons, wrap loose movie files. No FileBot, no move. |
| `mediabutler filebot-tv` | FileBot TV rename pass only. |
| `mediabutler filebot-movies` | FileBot movie rename pass only. |
| `mediabutler filebot-subtitles` (alias `subtitles`) | Subtitle-fetch pass only. |
| `mediabutler move` | Move-to-Plex pass only. |
| `mediabutler relocate` | Destination-eviction pass; see [Library cleanup with relocate](#library-cleanup-with-relocate). |
| `mediabutler status` | Configuration snapshot: sources, destinations, mode, duplicate policy, FileBot availability. |
| `mediabutler mcp` | Serves the Model Context Protocol over stdio; see [MCP server](#mcp-server). |
| `mediabutler version`, `--version`, `-v` | Prints the version and exits 0. |

With no subcommand, MediaButler launches the interactive Spectre.Console menu (`MainMenuCommand`).

### The mb.cmd shim

`mb.cmd` at the repo root forwards every argument to `dotnet run --project MediaButler -- <args>`, so the build stays current without a separate publish or install step:

```powershell
mb run --source "M:\Torrents" --live
mb scan --source "M:\Torrents"
mb hoist --source "M:\Torrents\My.Show.S01-S03"
mb filebot-movies --source "M:\Movies" --no-guard
mb relocate --source "M:\Movies"
mb status
mb --dry-run run --source "M:\Torrents"
mb --version
```

## Library cleanup with relocate

`mediabutler relocate --source <path>` scans an already-organised destination and moves out anything that does not belong there:

- Scanning `M:\Movies`: the expected kind is Movie; any `TvSeason` folder is sent to `TvDestination`.
- Scanning `M:\TV`: the expected kind is TvSeason; any `Movie` folder is sent to `MoviesDestination`.

Items already in the right place are left alone. Combine with `--dry-run` to preview the eviction list:

```powershell
mediabutler relocate --dry-run --source "M:\Movies"
mediabutler relocate           --source "M:\Movies"
```

This is the one command that intentionally runs against a destination, so the source-vs-destination guard does not apply.

## Safety

- Dry-run mode. Toggle it from the Settings menu or launch with `mediabutler --dry-run` (`-n`). No files are renamed, moved or deleted; FileBot is invoked with `--action TEST`; artwork and subtitle fetches are skipped. Every action prints as `[dry: -> target]`.
- Source-vs-destination guard (`PathGuard`). MediaButler refuses to run when `SourcePath` equals, contains or is contained by `TvDestination`, `MoviesDestination` or `MusicDestination`. Pointing the source at `M:\TV` would otherwise treat every show folder as a multi-season parent to hoist. Dry-run downgrades the refusal to a warning so you can inspect classification of an organised library; live mode hard-refuses. `--no-guard` bypasses it deliberately for repair runs.
- Idempotent operations. Re-running on a clean library is a no-op: canonical names round-trip through the parser unchanged. TV seasons that already exist at the target merge file by file, with episode collisions resolved by the [duplicate policy](#duplicate-movies-and-episodes); duplicate movies resolve the same way.
- Three exit codes. Headless runs return `0` (clean), `1` (errors) or `2` (no errors, but items need a human: Unknown folders, duplicate conflicts left by the `Flag` policy, Extras). Multi-source runs combine per-source codes by severity (`1 > 2 > 0`). Treat `2` as actionable in scheduled jobs.

## Duplicate movies and episodes

Both policies share the `DuplicateMovieAction` enum (`KeepLargest` or `Flag`) and default to `KeepLargest`.

Movies: when a movie's destination folder already exists with content, `duplicateMovieAction` decides.

- `KeepLargest` (default). The copy with the larger primary video file (the largest non-sample video) wins. If the incoming rip is larger, the destination's video is replaced and existing artwork is kept; if it is smaller or equal, it is deleted from the inbox. The loser is recorded in the audit log (`duplicate-replace` or `duplicate-discard`). If either side has no video to compare, MediaButler flags the item instead of guessing.
- `Flag`. Leaves both copies untouched and surfaces the conflict as needs-manual (exit code `2`).

TV episodes: `duplicateEpisodeAction` applies the same policy at the season-merge point (`SeasonMerger.MergeFiles`) when an incoming episode's name or parsed episode number already exists at the destination.

- `KeepLargest` (default). The larger video file wins and the smaller one is deleted, audit-logged like movies. It only fires when both sides are real video files; subtitle sidecars keep the exact-name-only conflict check.
- `Flag`. Restores the leave-both-and-flag behaviour.

Override either policy per run:

```powershell
mediabutler run --duplicates flag         # movies: nothing is auto-deleted this run
mediabutler move --duplicates keep-largest
mediabutler run --tv-duplicates flag      # TV episodes: leave collisions for a human
```

Source-side raw-dump merging (`RenameStage.ConsolidateEpisode`, and the flat-episode filing inside `HoistParent`) compares pre-FileBot scene filenames by exact name only and always flags; the policies above apply at the destination-side merge.

## Configuration

Settings live at `%APPDATA%\MindAttic\MediaButler\settings.json` and are managed through the in-app Settings menu. CLI flags override the persisted value for a single run. Defaults from `MediaButler/Settings/MediaButlerSettings.cs`:

| Setting | Default | Notes |
| --- | --- | --- |
| `sourcePath` | `M:\Torrents` | primary inbox |
| `extraSources` | `[]` | additional inboxes processed every run |
| `recursive` | `false` | also treat excluded container subfolders as inboxes |
| `tvDestination` | `M:\TV` | |
| `moviesDestination` | `M:\Movies` | |
| `musicDestination` | empty (disabled) | music moved as-is when set; flagged otherwise |
| `fileBotPath` | `C:\Program Files\FileBot\filebot.exe` | |
| `fileBotTrustAll` | `false` | passes `-Dtrust.all.certs=true` to FileBot's JVM (cert-chain workaround) |
| `subtitleLanguage` | `en` | |
| `enableSubtitles` | `false` | needs an OpenSubtitles login |
| `renameEpisodes`, `renameMovies`, `fetchArtwork` | `true` | individual FileBot sub-passes |
| `dryRun` | `false` | |
| `limit` | `null` (unlimited) | caps items processed per stage, for smoke tests |
| `duplicateMovieAction` | `KeepLargest` (or `Flag`) | see [Duplicate movies and episodes](#duplicate-movies-and-episodes) |
| `duplicateEpisodeAction` | `KeepLargest` (or `Flag`) | see [Duplicate movies and episodes](#duplicate-movies-and-episodes) |
| `excludedFolders` | `temp`, `.temp`, `incomplete`, `complete`, `_unsorted` | |
| `videoExtensions` | `.mkv .mp4 .avi .m4v .wmv .mov .ts .m2ts .mpg .mpeg .webm .flv .divx .vob .mts .3gp .mxf .m2v .ogm .rmvb .rm .asf .iso .img .ifo` | falls back to this default if cleared |
| `audioExtensions` | `.mp3 .flac .m4a .aac .ogg .opus .wav .wma .ape .alac .aiff .dsf` | marks a folder Music, not Empty |
| `emptyDeleteSafetyBytes` | `1 MB` | folders above this with no video are surfaced, not deleted |
| `sampleMaxBytes` | `300 MB` | sample-named videos under this size do not block shell cleanup |
| `subtitleExtensions` | `.srt .sub .idx .ass .ssa` | travel with a video during consolidation and merge |
| `enableLlmFallback` | `false` | off by default to avoid surprise API calls |
| `llmProvider` | `claude` | any Legion-supported provider id |
| `variationCatalogPath` | empty | resolves to `%APPDATA%\MindAttic\MediaButler\variations.json` |
| `titleYearOverrides` | `Blade Runner 2049`, `Wonder Woman 1984`, `1917`, `2001 A Space Odyssey`, `2012`, `1984`, `1922`, `300` | titles whose leading or trailing number is part of the title, not a release year |

### OpenSubtitles credentials

Credentials are never stored in `settings.json`, which lives unencrypted in roaming app data. Put them in the canonical Subtitles credential file `%APPDATA%\MindAttic\Subtitles\providers.json`:

```json
{
  "OpenSubtitles": { "user": "your-username", "password": "***" }
}
```

Or as environment variables (CI, containers):

```powershell
$env:MindAttic__Vault__Subtitles__OpenSubtitles__user     = 'your-username'
$env:MindAttic__Vault__Subtitles__OpenSubtitles__password = '***'
```

When both values resolve, MediaButler passes them to FileBot per call as `--def osdb.user=... osdb.pwd=...`. If they are missing the pipeline still runs: FileBot falls back to its own Preferences and MediaButler reports the auth failure, and which key to set, on a 401.

### LLM fallback parsing

When `EnableLlmFallback` is `true`, any folder or unmatched loose file the regex-based `NameParser` cannot classify is forwarded to `MindAttic.Legion` for a best guess at title, kind and season. The configured `LlmProvider` is called with the messy name, and the response is mapped back into the same `MediaItem` shape the regex parser produces. `LegionFallbackParser` returns `null` on any failure (disabled, unparseable, provider error), and MediaButler skips the item rather than rename it wrong.

Credentials are resolved through the shared `MindAttic.Vault` chain, the same `%APPDATA%\MindAttic\LLM\providers.json` keyring other MindAttic projects read. If the provider key is not configured, the fallback is skipped and the item appears in the `Needs manual fix` list.

### The variation catalog

Every scan appends the top-level names it classifies into `%APPDATA%\MindAttic\MediaButler\variations.json` (sections `movie`, `tv`, `music`, `unknown`). The file is created on first run as a clone of the hardcoded `MasterVariations` list (`MediaButler/Media/MasterVariations.cs`) and merged with new master entries on upgrade. It is hand-editable: moving a name into a different section pins that name's category for all future classification (exact match, case-insensitive). A corrupted or unparseable file disables saving for the run, so a manual edit is never clobbered.

## MCP server

`mediabutler mcp` serves the [Model Context Protocol](https://modelcontextprotocol.io) over stdio, so agent hosts (Claude Code, Claude Desktop, anything MCP-aware) can drive MediaButler directly:

| Tool | What it does |
| --- | --- |
| `scan` | Read-only classification of every inbox item, as JSON (kind and canonical target). |
| `status` | Configuration snapshot: sources, destinations, mode, duplicate policy, FileBot availability. |
| `run` | The full pipeline. Dry-run by default; pass `dryRun: false` to organise for real. Returns the pipeline log and exit code. |

Register it with Claude Code:

```powershell
claude mcp add mediabutler -- mediabutler mcp
```

It is the same engine as the CLI and the menu (`MediaButler/Mcp/McpServer.cs`). stdout carries protocol frames only; pipeline narration goes to stderr and rides inside tool results.

## Desktop shell

`MediaButler.Wpf/` is an optional WPF host (`net10.0-windows10.0.19041.0`, because BlazorWebView's WinRT composition control needs the SDK-versioned TFM) that embeds a single `BlazorWebView` window. Its Razor markup lives in the `MediaButler.Wpf.UI/` Razor Class Library: a Run tab (pipeline buttons, dry-run toggle, live output log) and a Settings tab, switched through an accessible WAI-ARIA tabs pattern with both panels kept mounted so an in-flight run survives a tab switch. It wraps the same pipeline through its own `Services/PipelineRunner` and `ConsoleCaptureWriter` (which redirects console narration into the log pane) and references `MediaButler/MediaButler.csproj` directly.

The shell targets WCAG 2.2 AA: real label associations on every Settings field, `role="switch"` and `aria-checked` toggles, `aria-live="polite"` status announcements (never the log pane, so a screen reader is not flooded on a full-library run), a focus-trapping accessible modal (`Shared/ConfirmDialog.razor`), and contrast-audited colours as CSS custom properties.

- `MediaButler.Wpf.UiTests/` drives the built shell through FlaUI (UI Automation): window opens, buttons respond, the dry-run badge starts correct. WebView2 content exposes its accessibility tree through the same UIA bridge.
- `MediaButler.Wpf.AccessibilityTests/` checks the WCAG 2.2 AA claim two ways: bUnit renders `Run` and `Settings` into a fake DOM for fast structural checks, and a real headless-Chromium axe-core scan (`wcag2a`, `wcag2aa`, `wcag22aa` tags), hosted by the standalone `MediaButler.Wpf.AccessibilityTests.Harness` executable, checks computed contrast and touch-target size against the production component tree.

Both projects are Windows desktop only and are not part of the headless test gate; treat them as verified only when run on a Windows desktop (see [docs/BIBLE.md](docs/BIBLE.md) section 6).

## Building

```powershell
# Main console app
dotnet build MediaButler/MediaButler.csproj
dotnet run   --project MediaButler                # interactive menu
dotnet run   --project MediaButler -- --dry-run    # force dry-run for the session

# Whole solution (all seven projects)
dotnet build MediaButler.slnx

# Windows-desktop-only shell (not part of the headless gate)
dotnet build MediaButler.Wpf/MediaButler.Wpf.csproj
```

The `mb.cmd` shim is equivalent to `dotnet run --project MediaButler -- %*`.

## Testing

```powershell
# Headless test gate
dotnet test MediaButler.Tests/MediaButler.Tests.csproj

# Whole solution
dotnet test MediaButler.slnx

# Windows-desktop-only UI smoke tests
dotnet test MediaButler.Wpf.UiTests/MediaButler.Wpf.UiTests.csproj --filter Category=Ui

# WCAG 2.2 AA scan (Playwright; installs once per machine)
dotnet build MediaButler.Wpf.AccessibilityTests/MediaButler.Wpf.AccessibilityTests.csproj
pwsh MediaButler.Wpf.AccessibilityTests/bin/Debug/net10.0-windows/playwright.ps1 install chromium
dotnet test  MediaButler.Wpf.AccessibilityTests/MediaButler.Wpf.AccessibilityTests.csproj
```

`MediaButler.Tests/` (NUnit) is the headless gate and covers:

- `NameParserTests`, `EpisodeParsingAndCatalogTests`: every dirty-name pitfall below, round-trip and idempotency invariants for `FormatSeasonFolder` and `FormatMovieFolder`, and the variation catalog's classify, persist and pin behaviour.
- `MediaScannerTests`: classification against a real temp directory (Empty, Movie, TvSeason, MultiSeasonParent by name or structure, Extras, Music, MovieCollection, excluded folders).
- `RenameStageTests`, `PathologicalLibraryPipelineTests`, `RealWorldLibraryPipelineTests`: full pipeline-stage tests covering dry-run leaving disk untouched, canonical live renames, idempotent re-runs, multi-season hoist, pack split, collection-husk hoist, Extras left in place.
- `MoveStageTests`, `RelocateStageTests`: `SanitizeForFs`, cross-volume detection, same-volume rename, reboot-year routing, relocate targets.
- `DuplicateMovieActionTests`, `DuplicateEpisodeActionTests`: `KeepLargest` and `Flag` resolution for movies and TV episodes.
- `SubtitleCredentialsTests`: `IsComplete` semantics and configuration binding.
- `PathGuardTests`: the source-vs-destination overlap detector.
- `FileBotClientTests`: FileBot argument construction (TEST vs live action, subtitle and artwork args, secret `@path` references).
- `SettingsEditorTests`: the interactive settings editor.
- `McpServerTests`: the `scan`, `status` and `run` MCP tools.
- `CliEndToEndTests`: subcommand wiring, exit codes, `--version`.

## Pitfalls it already defends against

These came from manual runs on real libraries; the code now handles them automatically.

- PowerShell brackets. Names like `[YTS.MX]` and `[TGx]` are wildcards in PowerShell; every file operation here uses literal-path semantics via `System.IO`.
- Empty disguised folders. `Breaking Bad (2008) Season 1-5 ...` was an empty shell; folders with zero video files (under the safety byte floor) are deleted.
- Multi-season parents with mixed nesting. Bones used `Season N`, Sherlock used `Show.Season.N.S0N...`, The Following used `Season N`; all three patterns are detected by name signal or by two or more season subfolders.
- Orphan show-level files. Files such as `Bones_Large.jpg` and `Info.txt` are relocated into the first hoisted season folder so they are not lost when the parent is deleted.
- Collection husks. A `Studio.Ghibli/` folder holding `Spirited.Away.2001/` and `Howl's.Moving.Castle.2004/` is recognised as `MovieCollection`, so FileBot is never asked to match a studio name.
- FileBot's `artwork.tmdb` is broken in 5.2.1. Movies are renamed via `--db TheMovieDB --action MOVE` first (which writes xattr), then the generic `fn:artwork` script runs.
- Subtitle flag. It is `-get-subtitles`, not `-get-missing-subtitles`. A 401 is reported with the key to fix instead of crashing the pipeline.
- `--action xattr` does not exist in 5.2.1. Valid values are MOVE, COPY, KEEPLINK, SYMLINK, HARDLINK, CLONE, DUPLICATE and TEST; dry-run uses TEST.
- Leading-zero season padding. `Season 1` always becomes `Season 01`.
- Trailing-dash idempotency. Re-parsing `The Mentalist - Season 04` must not leave `The Mentalist -` as the show name; `CleanShowName` strips trailing dashes.
- Release-group and index prefixes. Folders like `www.UIndex.org    -    A Knight of the Seven Kingdoms S01E01...` lose the prefix before parsing.
- Extras and Specials. A top-level `The Venture Bros. - Extras` is classified as `Extras`, not as a movie, and surfaced in the manual list.
- Same source and destination. Pointing at `M:\TV` is refused before any folder is touched in live mode, and downgraded to a warning in dry-run.
- Year-in-title movies. `Blade Runner 2049`, `Wonder Woman 1984`, `1917` and `2001 A Space Odyssey` would otherwise lose the number as a release year; the `TitleYearOverrides` allowlist holds these.
- Year-prefixed titles. `1917 (2019)` and `2009 Lost Memories (2002)` keep their titles; the parser prefers a parenthesised year when both forms are present.
- Same-name TV reboots. A reboot of a show already in the library routes to `{Show} (Year) - Season XX` once the existing folders carry their own year, instead of merging two shows' episodes.
- Duplicate rip pileups. A re-arrived season colliding episode by episode with a filed copy is resolved by `duplicateEpisodeAction: KeepLargest` instead of a manual pick per episode.

## Why a console app and not PowerShell

Early prototyping happened in PowerShell. MediaButler moved to .NET because it needs `MindAttic.Vault` for shared credential resolution (OpenSubtitles, LLM providers). The Vault chain (User Secrets, then environment variables, then `providers.json`) is the same one every other MindAttic app uses.

## Project layout

```text
MediaButler/                     the console app (this is what mb.cmd runs)
  Commands/                      Spectre.Console.Cli subcommands (run, scan, rename, hoist, ...)
  FileBot/                       FileBotClient: shells out to filebot.exe
  Llm/                           LegionFallbackParser: MindAttic.Legion long-tail classification
  Mcp/                           McpServer: stdio JSON-RPC 2.0 front door
  Media/                         MediaItem/MediaKind model, MediaScanner, NameParser,
                                 VariationCatalog, MasterVariations
  Pipeline/                      PipelineRunner, RenameStage, FileBotStage, MoveStage,
                                 RelocateStage, SeasonMerger, PathGuard, AuditLog, PipelineReport
  Settings/                      MediaButlerSettings, SubtitleCredentials
  Ui/                            interactive-menu helpers
  Program.cs                     Spectre.Console.Cli wiring and subcommand registration

MediaButler.Tests/               NUnit test project (headless gate)
MediaButler.Wpf.UI/              Razor Class Library: the shell's markup and services
  Pages/                         Run.razor, Settings.razor
  Layout/, Shared/               TabShell.razor (tabs), ConfirmDialog.razor (accessible modal)
  Services/                      PipelineRunner (shell side), ConsoleCaptureWriter, DialogService
MediaButler.Wpf/                 optional Windows desktop shell (WPF host + BlazorWebView)
MediaButler.Wpf.UiTests/         FlaUI UI-automation smoke tests
MediaButler.Wpf.AccessibilityTests/  bUnit + real-Chromium axe-core WCAG 2.2 AA scan

docs/                            Codex canon (BIBLE, AMENDMENTS, USER_STORIES, rfc/, digest)
tools/                           codex.ps1 (docs linter) and build-readme.ps1 (README to HTML)

mb.cmd                           CLI shim: forwards every argument to dotnet run --project MediaButler
MediaButler.slnx                 solution file (all seven projects)
```

The project page is this README on GitHub; MediaButler has no web deploy. `tools/build-readme.ps1` renders this README into `README.htm`.

## Documentation

This repo follows the MindAttic Codex documentation standard: a fact lives in exactly one layer, cross-referenced by stable ID.

| Layer | File | Purpose |
| --- | --- | --- |
| L0 | [docs/BIBLE.md](docs/BIBLE.md) | What MediaButler is and is not, the architecture canon, and the Laws (`MB-LAW-n`). |
| L1 | [docs/AMENDMENTS.md](docs/AMENDMENTS.md) | Pending decisions not yet folded into the bible (normally empty). |
| L2 | [`docs/USER_STORIES.md`](docs/USER_STORIES.md) | Test-cited stories (`MB-US-<Epic><n>`); every done story names its verifying test. |
| rfc | [docs/rfc](docs/rfc) | Design notes that graduate into the bible and stories. |
| generated | [docs/BIBLE.digest.md](docs/BIBLE.digest.md) | Produced by `tools/codex.ps1 digest`; never hand-edit. |

Org-wide laws live in `MindAttic.HouseRules.md` in the workspace root and are inherited by reference from BIBLE section 5. Agents and contributors: start with [AGENTS.md](AGENTS.md) for the working rules (when to regenerate the digest, when to run `tools/codex.ps1 doctor`).

## License

This repository has no LICENSE file; all rights are reserved.

Part of [MindAttic](https://mindattic.com) — see more projects at [github.com/mindattic](https://github.com/mindattic). Related: [MindAttic.Vault](https://github.com/mindattic/MindAttic.Vault) (settings and credentials), [MindAttic.Legion](https://github.com/mindattic/MindAttic.Legion) (LLM fallback).

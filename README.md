# MediaButler

**Stop hand-renaming torrent dumps. Drop them in, get a Plex-ready library out.**

MediaButler watches one or more inboxes of messy torrent dumps
(`Better.Call.Saul.S05.Complete.1080p.WEB-DL.x265-RELEASE_GROUP`), cleans the names locally,
hands the survivors to FileBot for episode titles and artwork, optionally fetches subtitles, and
moves everything into a canonical Plex layout. Multi-season torrents get split into per-season
folders; multi-movie packs and "collection husk" folders get split into one folder per film.
Show artwork is hoisted from each season into a single show root. Folder names you've already
seen — `[YTS.MX]`, `Bones - Season 1-12`, year-in-title oddballs like
`Blade Runner 2049 (2017)` — survive the parser without manual intervention. Every naming
variation MediaButler encounters is recorded into a persistent, hand-editable corpus so it keeps
getting smarter about the shapes your own trackers produce.

Built on `MindAttic.Vault` for settings (`%APPDATA%\MindAttic\MediaButler\settings.json`) and
credential resolution (User Secrets / environment variables for OpenSubtitles and any LLM
provider). Optional LLM fallback via `MindAttic.Legion` picks up the long tail of folders (and
loose files) the regex parser can't classify.

This is one of the ~25 repos in the MindAttic personal workspace and follows the shared
**Codex documentation standard** — see [Documentation map](#documentation-map) below for the
canonical docs.

**Why MediaButler:**

- **Dry-run first.** Toggle `--dry-run` (or `-n`) and the entire pipeline prints
  `[dry: -> target]` lines without touching disk. FileBot runs in `--action TEST` mode so its
  decisions are visible without commits.
- **Idempotent by design.** Canonical names (`Better Call Saul - Season 05`, `Heat (1995)`)
  round-trip through the parser; re-running on an already-clean library is a no-op.
- **Self-defending.** Source-vs-destination guard refuses to run when `SourcePath` overlaps
  `TvDestination`, `MoviesDestination`, or `MusicDestination`. Empty disguised folders are
  deleted only after a byte-size sanity check. Extras / Specials / Bonus folders are surfaced
  for manual review, never reorganized silently.
- **One library re-organizer.** `mediabutler relocate --source M:\Movies` evicts any TV folders
  that drifted into the movies library (and vice versa) — the only stage that legally operates
  on a destination.
- **LLM-assisted long tail.** Turn on `EnableLlmFallback` and unclassifiable folders (and
  unmatched loose files) get sent through `MindAttic.Legion` to a configurable provider
  (`claude-api` by default) for a best-guess classification. Off by default to avoid surprise API
  calls.
- **Plex-ready output.** TV becomes `M:\TV\<Show>\Season XX\episodes`, movies become
  `M:\Movies\<Title> (YYYY)\`. Per-season artwork hoists up to the show root and deduplicates.
- **Duplicates are policy-resolved, not toil.** Both duplicate movie folders and duplicate TV
  episodes default to `KeepLargest` (the bigger rip wins, the loser is audit-logged) instead of
  making you triage every collision by hand.
- **Many inboxes, one call.** `ExtraSources` plus repeatable `--source` process several roots per
  run; `--recursive` additionally treats excluded container subfolders (`temp`, `incomplete`, …)
  as inboxes of their own.
- **A growing, hand-editable naming corpus.** Every scan records the folder names it sees into
  `variations.json`; moving a name into a different section of that file pins its classification
  for future runs, without touching code.

---

## Table of Contents

- [What it is / what it is not](#what-it-is--what-it-is-not)
- [Architecture overview](#architecture-overview)
- [Repository layout](#repository-layout)
- [The pipeline stages](#the-pipeline-stages)
- [CLI commands](#cli-commands)
- [`mb.cmd` shim](#mbcmd-shim)
- [Library cleanup: `relocate`](#library-cleanup-relocate)
- [Safety](#safety)
- [Duplicate movies and duplicate episodes](#duplicate-movies-and-duplicate-episodes)
- [Configuration](#configuration)
- [OpenSubtitles credentials](#opensubtitles-credentials)
- [LLM fallback parsing](#llm-fallback-parsing)
- [The variation catalog](#the-variation-catalog)
- [MCP server (agents)](#mcp-server-agents)
- [MediaButler.Maui (Windows desktop shell)](#mediabuttermaui-windows-desktop-shell)
- [Landing page](#landing-page)
- [Why a console app and not PowerShell](#why-a-console-app-and-not-powershell)
- [Build, run, and test](#build-run-and-test)
- [Documentation map](#documentation-map)
- [Pitfalls MediaButler already defends against](#pitfalls-mediabutler-already-defends-against)

---

## What it is / what it is not

MediaButler **is**:

- A .NET console app (`net10.0-windows`, assembly `mediabutler`) that organizes an existing
  folder of already-downloaded media into a Plex-compatible layout.
- One engine with three front doors: a Spectre.Console CLI + interactive menu, an optional
  `MediaButler.Maui` desktop shell, and an MCP (Model Context Protocol) server for agent hosts —
  all driving the same `PipelineRunner`.

MediaButler **is not**:

- **A downloader / torrent client.** It never fetches media; it organizes what is already on
  disk under `SourcePath`.
- **A metadata database.** Episode titles, posters, and movie matching are FileBot's job
  (TheTVDB / TheMovieDB). MediaButler shells out to FileBot; it does not query those APIs
  itself.
- **A media server.** It produces a Plex-compatible folder layout; it does not stream, scan, or
  talk to a Plex server.
- **A PowerShell script.** Earlier prototyping happened in PowerShell — see
  [Why a console app and not PowerShell](#why-a-console-app-and-not-powershell).
- **A destination editor**, except for the explicit `relocate` command — every other stage only
  ever touches `SourcePath`.
- **Vendor-locked to one LLM.** Fallback parsing routes through `MindAttic.Legion`; no provider
  SDK is hard-coded.
- **A music organizer.** Music is detected so it's never deleted as "empty" or renamed as a
  movie, and can be moved as-is — but tagging/restructuring music libraries is a different
  tool's job.

The full canon for these facts (with law IDs and verifying tests) lives in
[`docs/BIBLE.md`](docs/BIBLE.md); this README only summarizes.

## Architecture overview

```
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
   (local clean,   (filebot.exe:    (cross-volume move to Plex layout,
    hoist seasons,  TV/Movies/subs/  hoist show art, merge into existing
    consolidate     artwork)         seasons, move music as-is)
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
     MediaButler.Maui desktop shell
     MCP server (stdio JSON-RPC 2.0)

   Settings:    %APPDATA%\MindAttic\MediaButler\settings.json    (via MindAttic.Vault)
   Variations:  %APPDATA%\MindAttic\MediaButler\variations.json  (naming corpus + pins)
```

Three sub-projects sit around the console app:

| Project | Role | Status |
| --- | --- | --- |
| `MediaButler/` | The console app itself — Spectre.Console CLI + interactive menu + MCP server. References `MindAttic.Vault` and `MindAttic.Legion`. | done |
| `MediaButler.Tests/` | NUnit coverage for the parser, scanner, pipeline stages, guards, CLI, and MCP server. | done |
| `MediaButler.Maui/` | Optional MAUI GUI shell (`net10.0-windows10.0.19041.0`) wrapping the same pipeline stages via its own `Services/PipelineRunner` and `ConsoleCaptureWriter`. Windows-desktop only; not part of the headless test gate. | partial |
| `MediaButler.Maui.UiTests/` | FlaUI smoke tests that drive the MAUI shell's window/buttons. Windows-desktop only. | partial |
| `MediaButler.Landing.Tests/` | Playwright tests against the repo-root `index.htm` landing page. | done (needs Playwright browser binaries) |

## Repository layout

```
MediaButler/                     the console app (this is what mb.cmd runs)
  Commands/                      Spectre.Console.Cli subcommands (run, scan, rename, hoist, ...)
  FileBot/                       FileBotClient — shells out to filebot.exe
  Llm/                           LegionFallbackParser — MindAttic.Legion long-tail classification
  Mcp/                           McpServer — stdio JSON-RPC 2.0 front door
  Media/                         MediaItem/MediaKind domain model, MediaScanner, NameParser,
                                 VariationCatalog, MasterVariations
  Pipeline/                      PipelineRunner, RenameStage, FileBotStage, MoveStage,
                                 RelocateStage, SeasonMerger, PathGuard, AuditLog, PipelineReport
  Settings/                      MediaButlerSettings, SubtitleCredentials
  Ui/                            interactive-menu helpers (Verbosity, etc.)
  Program.cs                     Spectre.Console.Cli app wiring / subcommand registration

MediaButler.Tests/                NUnit test project (headless gate)
MediaButler.Maui/                  optional Windows desktop GUI shell
  Pages/                          SettingsPage
  Services/                       PipelineRunner (Maui-side), ConsoleCaptureWriter
MediaButler.Maui.UiTests/          FlaUI UI-automation smoke tests for the Maui shell
MediaButler.Landing.Tests/         Playwright tests against index.htm

docs/                             Codex canon (BIBLE, AMENDMENTS, USER_STORIES, rfc/, digest)
scripts/cli/                      legacy/dead — do not invoke (see docs/BIBLE.md §4.1)
tools/                            codex.ps1 (docs linter) and build-readme.ps1 (this file -> HTML)

mb.cmd                           CLI shim: forwards every argument to `dotnet run --project MediaButler`
index.htm                        landing-page HTML (deployed via the sibling MindAttic.Deploy repo)
package.json                     legacy README->index.htm renderer scaffold; scripts/cli/* no longer exist
MediaButler.slnx                 solution file (all five projects)
```

## The pipeline stages

1. **Self-rename pass (`RenameStage`).** Cleans messy folder names
   (`Better.Call.Saul.S05.Complete.1080p...`) into FileBot-friendly stems
   (`Better Call Saul - Season 05`). Movies become `Title (YYYY)`. Hoists nested `Season N`
   subfolders out of multi-season parent dumps onto the source root and pads season numbers with
   a leading zero. Consolidates loose episode files into their `{Show} - Season XX` folder,
   splits multi-movie packs into one folder per film, hoists "collection husk" folders
   (`MovieCollection`) into individual movie folders, and merges duplicate-season dumps
   file-by-file. Empty disguised folders (no video underneath, under the safety byte floor) are
   deleted. `Extras` / `Specials` / `Bonus` folders are left in place and surfaced in the final
   report.
2. **FileBot rename pass (`FileBotStage`).** Renames TV episodes via TheTVDB, renames movies via
   TheMovieDB, fetches show artwork (`fn:artwork.tvdb`) and movie artwork (`fn:artwork`, after
   writing xattr via rename — works around the `artwork.tmdb` script bug).
3. **Optional subtitle pass.** Calls `filebot -get-subtitles` when `EnableSubtitles` is on.
   Credentials come from the MindAttic Vault chain (User Secrets → env vars); see
   [OpenSubtitles credentials](#opensubtitles-credentials).
4. **Move-to-Plex pass (`MoveStage`).** TV folders become `M:\TV\<Show>\Season XX\episodes...`,
   movies become `M:\Movies\<Title> (YYYY)\...`, music folders move as-is to
   `MusicDestination` when configured. Show-level artwork is hoisted from each season folder up
   to the show root and deduplicated. Reboot-safe routing sends year-tagged TV content to
   `ShowName (YEAR)\Season NN` once the destination already has a year-tagged folder for that
   show.
5. **Final report.** Prints a consolidated summary: items renamed, hoisted, consolidated,
   pack-split, moved, FileBot successes, artwork / subtitle counts, errors, and a
   `Needs manual fix` list (Unknown, Extras, and any item that hit a pre-existing target).

`relocate` (below) is a separate, sixth command that intentionally operates on a destination
rather than a source.

## CLI commands

All commands share the flags in `MediaButler/Commands/BaseSettings.cs` (`--dry-run`/`-n`,
`--live`, `--source` (repeatable), `--subtitles`, `--recursive`/`-r`, `--tv-dest`,
`--movies-dest`, `--music-dest`, `--limit`, `--duplicates`, `--tv-duplicates`, `--no-guard`,
`--quiet`/`-q`, `--verbose`).

| Command | What it runs |
| --- | --- |
| `mediabutler run` | The full pipeline: rename → FileBot → move. Alias for `rename`. |
| `mediabutler scan` | Read-only classification pass — prints what each item would be classified as. |
| `mediabutler rename` | Stage 1 (local rename/hoist/consolidate/split) followed by FileBot and move — same as `run`. |
| `mediabutler hoist` | Stage 1 only — local rename, hoist nested seasons, wrap loose movie files. No FileBot, no move. |
| `mediabutler filebot-tv` | FileBot TV rename pass only. |
| `mediabutler filebot-movies` | FileBot movie rename pass only. |
| `mediabutler filebot-subtitles` (alias `subtitles`) | Subtitle-fetch pass only. |
| `mediabutler move` | Move-to-Plex pass only. |
| `mediabutler relocate` | Destination-eviction pass — see [Library cleanup](#library-cleanup-relocate). |
| `mediabutler status` | Configuration snapshot: sources, destinations, mode, duplicate policy, FileBot availability. |
| `mediabutler mcp` | Serves the Model Context Protocol over stdio — see [MCP server](#mcp-server-agents). |
| `mediabutler version` / `mediabutler --version` / `-v` | Prints the version and exits 0. |

With no subcommand, MediaButler launches the interactive Spectre.Console menu
(`MainMenuCommand`).

## `mb.cmd` shim

The `mb.cmd` shim at the repo root forwards every argument to
`dotnet run --project MediaButler -- <args>`, so the build stays current without a separate
publish/install step:

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

## Library cleanup: `relocate`

`mediabutler relocate --source <path>` scans an already-organized destination and moves out
anything that doesn't belong there:

- Scanning `M:\Movies` → expected kind is Movie; any `TvSeason` folder gets sent to
  `TvDestination`.
- Scanning `M:\TV` → expected kind is TvSeason; any `Movie` folder gets sent to
  `MoviesDestination`.

Items already in the right place are left alone. Combine with `--dry-run` to preview the
eviction list before committing:

```powershell
mediabutler relocate --dry-run --source "M:\Movies"
mediabutler relocate           --source "M:\Movies"
```

This is the one command that intentionally runs against a destination, so the
source-vs-destination guard doesn't apply.

## Safety

- **Dry-run mode.** Toggle from the Settings menu or launch with `mediabutler --dry-run` (`-n`).
  In dry-run no files are renamed, moved, or deleted; FileBot is invoked with `--action TEST`;
  artwork and subtitle fetches are skipped. Every action prints as `[dry: -> target]` so you can
  see what would have happened.
- **Source-vs-destination guard (`PathGuard`).** MediaButler refuses to run when `SourcePath`
  equals, contains, or is contained by `TvDestination` / `MoviesDestination` / `MusicDestination`.
  Pointing the source at `M:\TV` would otherwise treat every show folder as a multi-season parent
  to hoist and destroy the library. Dry-run downgrades the refusal to a warning so you can inspect
  classification of an already-organized library; live mode hard-refuses. `--no-guard` bypasses
  this deliberately for repair runs.
- **Idempotent operations.** Re-running the pipeline on an already-clean library is a no-op:
  canonical folder names (`Better Call Saul - Season 05`, `Heat (1995)`) round-trip through the
  parser without changing. TV seasons that already exist at the target merge file-by-file
  (episode collisions resolve per the [duplicate policy](#duplicate-movies-and-duplicate-episodes));
  duplicate movies resolve the same way.
- **Three exit codes.** Headless runs return `0` (clean), `1` (errors), or `2` (no errors but
  items need a human eye — Unknown folders, duplicate-rip conflicts left over from `Flag` policy,
  Extras). Multi-source runs combine per-source codes by severity (`1 > 2 > 0`). Treat `2` as
  actionable in cron jobs, not silent success.

## Duplicate movies and duplicate episodes

Both policies share the same `DuplicateMovieAction` enum (`KeepLargest` | `Flag`) and default to
`KeepLargest`.

**Movies** — when a movie's destination folder already exists with content, the
`duplicateMovieAction` setting decides what happens:

- **`KeepLargest`** (default) — the copy with the larger primary video file (the largest
  non-sample video) wins. If the incoming rip is larger, the destination's video is replaced and
  the existing artwork is kept; if the incoming rip is smaller or equal, it is deleted from the
  inbox. Either way the loser is recorded in the audit log (`duplicate-replace` /
  `duplicate-discard`). If either side has no video to compare, MediaButler refuses to guess and
  flags the item instead.
- **`Flag`** — the classic behaviour: leave both copies untouched and surface the conflict as
  needs-manual (exit code `2`).

**TV episodes** — `duplicateEpisodeAction` applies the same policy at the season-merge point
(`SeasonMerger.MergeFiles`) when an incoming episode's name or parsed episode number already
exists at the destination:

- **`KeepLargest`** (default) — the larger video file wins; the smaller one is deleted
  (audit-logged the same way as movies). Only fires when both sides are real video files —
  subtitle sidecars keep the old exact-name-only conflict check.
- **`Flag`** — restores the original leave-both-and-flag behaviour.

Override either policy per run:

```powershell
mediabutler run --duplicates flag         # movies: nothing is ever auto-deleted this run
mediabutler move --duplicates keep-largest
mediabutler run --tv-duplicates flag      # TV episodes: leave collisions for a human
```

Source-side raw-dump merging (`RenameStage.ConsolidateEpisode`, the flat-episode filing inside
`HoistParent`) compares pre-FileBot scene filenames by exact name only, with no episode-number
awareness, and always flags — the duplicate policies above apply at the canonical
destination-side merge.

## Configuration

Settings live at `%APPDATA%\MindAttic\MediaButler\settings.json` and are managed through the
in-app Settings menu (or CLI flags, which override the persisted value for a single run).
Defaults (see `MediaButler/Settings/MediaButlerSettings.cs`):

| Setting | Default | Notes |
| --- | --- | --- |
| `sourcePath` | `M:\Torrents` | primary inbox |
| `extraSources` | `[]` | additional inboxes processed every run |
| `recursive` | `false` | also treat excluded container subfolders as inboxes |
| `tvDestination` | `M:\TV` | |
| `moviesDestination` | `M:\Movies` | |
| `musicDestination` | `""` (disabled) | music moved as-is when set; flagged otherwise |
| `fileBotPath` | `C:\Program Files\FileBot\filebot.exe` | |
| `fileBotTrustAll` | `false` | passes `-Dtrust.all.certs=true` to FileBot's JVM (cert-chain workaround) |
| `subtitleLanguage` | `en` | |
| `enableSubtitles` | `false` | needs OpenSubtitles login |
| `renameEpisodes` / `renameMovies` / `fetchArtwork` | `true` | individual FileBot sub-passes |
| `dryRun` | `false` | |
| `limit` | `null` (unlimited) | cap items processed per stage, for smoke-testing |
| `duplicateMovieAction` | `KeepLargest` (or `Flag`) | see [above](#duplicate-movies-and-duplicate-episodes) |
| `duplicateEpisodeAction` | `KeepLargest` (or `Flag`) | see [above](#duplicate-movies-and-duplicate-episodes) |
| `excludedFolders` | `temp`, `.temp`, `incomplete`, `complete`, `_unsorted` | |
| `videoExtensions` | `.mkv .mp4 .avi .m4v .wmv .mov .ts .m2ts .mpg .mpeg .webm .flv .divx .vob .mts .3gp .mxf .m2v .ogm .rmvb .rm .asf .iso .img .ifo` | falls back to this default if cleared |
| `audioExtensions` | `.mp3 .flac .m4a .aac .ogg .opus .wav .wma .ape .alac .aiff .dsf` | marks a folder Music (not Empty) |
| `emptyDeleteSafetyBytes` | `1 MB` | folders above this with no video are surfaced, not deleted |
| `sampleMaxBytes` | `300 MB` | sample-named videos under this size don't block shell cleanup |
| `subtitleExtensions` | `.srt .sub .idx .ass .ssa` | travel with a video during consolidation/merge |
| `enableLlmFallback` | `false` | off by default to avoid surprise API calls |
| `llmProvider` | `claude-api` | any Legion-supported provider id |
| `variationCatalogPath` | `""` | resolves to `%APPDATA%\MindAttic\MediaButler\variations.json` |
| `showLevelArtFiles` | `poster.jpg banner.jpg fanart.jpg backdrop.jpg folder.jpg landscape.jpg clearart.png logo.png tvshow.nfo` | |
| `titleYearOverrides` | `Blade Runner 2049`, `Wonder Woman 1984`, `1917`, `2001 A Space Odyssey`, `2012`, `1984`, `1922`, `300` | titles whose leading/trailing number is part of the title, not a release year |

## OpenSubtitles credentials

Credentials are **never** stored in `settings.json` (which lives unencrypted in roaming
app-data). Place them in the canonical Subtitles credential file
`%APPDATA%\MindAttic\Subtitles\providers.json`:

```json
{
  "OpenSubtitles": { "user": "ryandebraal", "password": "***" }
}
```

Or as environment variables (CI / containers):

```powershell
$env:MindAttic__Vault__Subtitles__OpenSubtitles__user     = 'ryandebraal'
$env:MindAttic__Vault__Subtitles__OpenSubtitles__password = '***'
```

When both values resolve, MediaButler passes them to FileBot per call as
`--def osdb.user=… osdb.pwd=…`. If they're missing the pipeline still runs — FileBot falls back
to whatever is configured in its own Preferences and MediaButler reports the auth failure (and
which key to set) on a 401.

## LLM fallback parsing

When `EnableLlmFallback` is `true`, any folder (or unmatched loose file) the regex-based
`NameParser` fails to classify is forwarded to `MindAttic.Legion` for a best-guess at title /
kind / season. The configured `LlmProvider` (default `claude-api`) is called with the messy
name; the response is mapped back into the same `MediaItem` shape the regex parser produces.
`LegionFallbackParser` returns `null` on any failure (disabled, unparseable, provider error) —
MediaButler skips the item rather than rename it wrong.

| Setting | Default | Meaning |
| --- | --- | --- |
| `EnableLlmFallback` | `false` | Off by default to avoid surprise API calls. |
| `LlmProvider` | `claude-api` | Any Legion-supported provider id (`claude-api`, `openai`, `gemini`, `deepseek`, ...). |

Credentials are resolved through the shared `MindAttic.Vault` chain — the same
`%APPDATA%\MindAttic\LLM\providers.json` keyring every other MindAttic project reads from. If
the provider key isn't configured, the fallback is skipped silently and the item is surfaced in
the final report's "Needs manual fix" list.

## The variation catalog

Every scan appends the top-level names it classifies into
`%APPDATA%\MindAttic\MediaButler\variations.json` (sections `movie` / `tv` / `music` /
`unknown`), created on first run as a clone of the hardcoded `MasterVariations` list
(`MediaButler/Media/MasterVariations.cs`) and merged with new master entries on upgrade. The
file is hand-editable: moving a name into a different section pins that name's category for all
future classification (exact match, case-insensitive). A corrupted or unparseable file disables
saving for the run so a manual edit is never clobbered.

## MCP server (agents)

`mediabutler mcp` serves the [Model Context Protocol](https://modelcontextprotocol.io) over
stdio, so agent hosts (Claude Code, Claude Desktop, anything MCP-aware) can drive MediaButler
directly:

| Tool | What it does |
| --- | --- |
| `scan` | Read-only classification of every inbox item, as JSON (kind + canonical target). |
| `status` | Configuration snapshot: sources, destinations, mode, duplicate policy, FileBot availability. |
| `run` | The full pipeline. **Dry-run by default** — pass `dryRun: false` to actually organize. Returns the pipeline log and exit code. |

Register it with Claude Code:

```powershell
claude mcp add mediabutler -- mediabutler mcp
```

It's the same engine as the CLI and interactive menu — one engine, many front doors
(`MediaButler/Mcp/McpServer.cs`). stdout carries protocol frames only; pipeline narration goes to
stderr and rides inside tool results.

## MediaButler.Maui (Windows desktop shell)

`MediaButler.Maui/` is an optional MAUI GUI (`net10.0-windows10.0.19041.0`) that wraps the same
pipeline via its own `Services/PipelineRunner` and `ConsoleCaptureWriter` (which redirects
console-style pipeline narration into the app's log view). It ships one page,
`Pages/SettingsPage.xaml`, for editing the same `MediaButlerSettings` the CLI reads. It
references `MediaButler/MediaButler.csproj` directly, so it stays on the same pipeline logic as
the console app.

`MediaButler.Maui.UiTests/` drives the built shell through FlaUI (UI Automation) for smoke
testing — window opens, buttons respond.

Both projects are Windows-desktop only and are **not** part of the headless test gate; treat
them as verified only when actually run on Windows desktop (see `docs/BIBLE.md` §6).

## Landing page

`index.htm` at the repo root is the MediaButler marketing/landing page, deployed to
`mindattic.com/mediabutler.htm` via the sibling `MindAttic.Deploy` repo.
`MediaButler.Landing.Tests` drives it headlessly with Playwright — checks visible content, link
resolution, and console errors.

Per `docs/BIBLE.md` §4.1, the in-repo `scripts/cli/*` renderer and its `package.json` scaffold
(a Node/`marked`-based README → HTML pipeline) are legacy and no longer used to produce
`index.htm`; `scripts/cli/` is currently empty. Do not invoke `npm run build` / `npm run deploy`
expecting them to regenerate the landing page — deployment goes through `MindAttic.Deploy`
instead. This is distinct from `tools/build-readme.ps1` (see below), which renders this
`README.md` into a separate `README.htm` engineering-reference page, not the marketing landing
page.

## Why a console app and not PowerShell

Earlier prototyping happened in PowerShell. Switched to .NET because MediaButler needs
`MindAttic.Vault` for shared credential resolution (OpenSubtitles, LLM providers, plus future
cloud storage). The Vault chain (User Secrets → environment variables → providers.json) is the
same one every other MindAttic app uses.

## Build, run, and test

```powershell
# Main console app
dotnet build MediaButler/MediaButler.csproj
dotnet run   --project MediaButler                # interactive menu
dotnet run   --project MediaButler -- --dry-run    # force dry-run for the session

# Headless test gate
dotnet test  MediaButler.Tests/MediaButler.Tests.csproj

# Whole solution (all five projects)
dotnet build MediaButler.slnx
dotnet test  MediaButler.slnx

# Windows-desktop-only projects (not part of the headless gate)
dotnet build MediaButler.Maui/MediaButler.Maui.csproj
dotnet test  MediaButler.Maui.UiTests/MediaButler.Maui.UiTests.csproj

# Landing-page tests (Playwright; installs once per machine)
dotnet build MediaButler.Landing.Tests/MediaButler.Landing.Tests.csproj
pwsh MediaButler.Landing.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
dotnet test  MediaButler.Landing.Tests/MediaButler.Landing.Tests.csproj
```

The `mb.cmd` shim at the repo root is equivalent to `dotnet run --project MediaButler -- %*` —
see [`mb.cmd` shim](#mbcmd-shim).

`MediaButler.Tests/` (NUnit) is the headless gate and covers:

- `NameParserTests`, `EpisodeParsingAndCatalogTests` — every dirty-name pitfall from this
  README, round-trip / idempotency invariants for `FormatSeasonFolder` and `FormatMovieFolder`,
  and the variation catalog's classify/persist/pin behaviour.
- `MediaScannerTests` — classification against a real temp directory (Empty, Movie, TvSeason,
  MultiSeasonParent via name or structure signal, Extras, Music, MovieCollection, excluded
  folders).
- `RenameStageTests`, `PathologicalLibraryPipelineTests`, `RealWorldLibraryPipelineTests` — full
  pipeline-stage tests: dry-run leaves disk untouched, live rename produces canonical names,
  idempotent re-runs, multi-season hoist, pack split, collection-husk hoist, Extras left in
  place.
- `MoveStageTests` — `SanitizeForFs`, cross-volume detection, same-volume rename, reboot-year
  routing.
- `DuplicateMovieActionTests`, `DuplicateEpisodeActionTests` — `KeepLargest` / `Flag` policy
  resolution for movies and TV episodes.
- `SubtitleCredentialsTests` — `IsComplete` semantics and configuration binding.
- `PathGuardTests` — the source-vs-destination overlap detector.
- `FileBotClientTests` — FileBot argument construction (TEST vs live action, subtitle/artwork
  args, secret `@path` references).
- `McpServerTests` — the `scan` / `status` / `run` MCP tools.
- `CliEndToEndTests` — subcommand wiring, exit codes, `--version`.

```powershell
dotnet test MediaButler.slnx
```

## Documentation map

This repo follows the MindAttic Codex documentation standard — a fact lives in exactly one
layer, cross-referenced by stable ID:

| Layer | File | Purpose |
| --- | --- | --- |
| L0 | [`docs/BIBLE.md`](docs/BIBLE.md) | What MediaButler IS / is NOT, the architecture canon, and the Laws (`MB-LAW-n`). |
| L1 | [`docs/AMENDMENTS.md`](docs/AMENDMENTS.md) | Append-only change log (`MB-A<n>`); an amendment wins over the bible. |
| L2 | [`docs/USER_STORIES.md`](docs/USER_STORIES.md) | Test-cited stories (`MB-US-<Epic><n>`); every done story names its verifying test. |
| rfc | [`docs/rfc/`](docs/rfc/) | Design notes that graduate into the bible + stories, then get marked superseded. |
| generated | `docs/BIBLE.digest.md` | Produced by `tools/codex.ps1 digest`; never hand-edit. |

Org-wide laws live in `MindAttic.HouseRules.md` (in the workspace root) and are inherited by
reference from BIBLE §5 rather than restated here. See `CLAUDE.md` in this repo for the
day-to-day working rules (when to regenerate the digest, when to run `tools/codex.ps1 doctor`,
etc.).

## Pitfalls MediaButler already defends against

These came from manual runs on real libraries; the code now handles them automatically:

- **PowerShell brackets.** Folder names like `[YTS.MX]` and `[TGx]` are wildcards in PowerShell —
  every file operation here uses `LiteralPath` semantics via `System.IO` (no shell expansion).
- **Empty disguised folders.** `Breaking Bad (2008) Season 1-5 ...` was an empty shell.
  MediaButler deletes folders that contain zero video files (under the safety byte floor).
- **Multi-season parents with mixed nesting.** Bones used `Season N`, Sherlock used
  `Show.Season.N.S0N...`, The Following used `Season N`. MediaButler detects all three patterns
  (name signal and / or two-or-more season subfolders).
- **Orphan show-level files.** Bones had `Bones_Large.jpg`, `Info.txt`. These get relocated into
  the first hoisted season folder so they aren't lost when the parent is deleted.
- **Collection husks.** A `Studio.Ghibli/` folder holding `Spirited.Away.2001/`,
  `Howl's.Moving.Castle.2004/` is recognized as `MovieCollection`, not mis-classified as a movie
  — FileBot is never asked to match a studio name against a movie database and fail.
- **FileBot's `artwork.tmdb` is broken** in 5.2.1. Workaround: rename movies via
  `--db TheMovieDB --action MOVE` first (which writes xattr), then run the generic `fn:artwork`
  script.
- **Subtitle flag.** It's `-get-subtitles`, not `-get-missing-subtitles`. Auth failures return a
  401 and MediaButler reports it gracefully (with the User Secrets key to fix) instead of
  crashing the pipeline.
- **`--action xattr` doesn't exist** in 5.2.1; valid values are MOVE / COPY / KEEPLINK / SYMLINK /
  HARDLINK / CLONE / DUPLICATE / TEST. Dry-run uses TEST.
- **Leading-zero season padding.** `Season 1` → `Season 01` always.
- **Trailing-dash idempotency.** Re-parsing `The Mentalist - Season 04` used to leave the show
  name as `The Mentalist -`, which would re-rename the folder to
  `The Mentalist - - Season 04` on the next run. `CleanShowName` now strips trailing dashes.
- **Release-group / index prefixes.** Folders like
  `www.UIndex.org    -    A Knight of the Seven Kingdoms S01E01...` are stripped of the prefix
  before parsing.
- **Extras / Specials.** Top-level `The Venture Bros. - Extras` is classified as `Extras` (not as
  a movie) and surfaced in the manual list.
- **Same source and destination.** Pointing at `M:\TV` is refused before any folder is touched in
  live mode; downgraded to a warning in dry-run so you can inspect classification of an
  already-organized library.
- **Year-in-title movies.** Titles like `Blade Runner 2049`, `Wonder Woman 1984`, `1917`,
  `2001 A Space Odyssey` would otherwise have the year-shaped number eaten as the release year.
  The `TitleYearOverrides` setting holds a small allowlist of these. Add more entries when new
  ones land.
- **Year-prefixed titles.** `1917 (2019)` and `2009 Lost Memories (2002)` used to drop the title
  because the bare leading 4-digit number was matched before the parenthesised year. The parser
  now prefers a parenthesised year whenever both forms are present.
- **Same-name TV reboots.** A 2026 reboot of a show that already has a library folder from its
  original run now routes to `ShowName (YEAR)\Season NN` once the existing folder is renamed to
  include its own year — instead of merging two unrelated shows' episodes into one folder.
- **Duplicate rip pileups.** A re-arrived season colliding episode-by-episode with an
  already-filed copy used to require a manual pick per episode; `duplicateEpisodeAction:
  KeepLargest` (default) now resolves it automatically, the same way movie duplicates already
  were.

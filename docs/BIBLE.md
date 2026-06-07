---
codex: 1
project: MediaButler
code: MB
layer: bible
status: living
updated: 2026-06-07
---

# MediaButler — Project Bible
> Single source of truth for what MediaButler IS, is NOT, and the rules that keep it coherent.
> README says how to build/run; this says how to think about the system.

## 1. The one sentence {#MB-§1}
MediaButler watches a folder of messy torrent dumps, cleans the names locally, hands the
survivors to FileBot for episode titles and artwork, optionally fetches subtitles, and moves
everything into a canonical Plex layout — idempotently, dry-run-first, and refusing to run when
the source overlaps a destination.

## 2. The product promise {#MB-§2}
- **Dry-run first.** With `--dry-run` (`-n`) the entire pipeline prints `[dry: -> target]` lines
  and performs no renames/moves/deletes; FileBot is invoked with `--action TEST` so its decisions
  are visible without commits. See [#MB-LAW-1](#MB-§5).
- **Idempotent by design.** Canonical names (`Better Call Saul - Season 05`, `Heat (1995)`)
  round-trip through `NameParser`; re-running on an already-clean library is a no-op. See
  [#MB-LAW-2](#MB-§5).
- **Self-defending.** `PathGuard` refuses to run when `SourcePath` equals/contains/is-contained-by
  `TvDestination`/`MoviesDestination`. Empty disguised folders are deleted only after a byte-size
  sanity check (`EmptyDeleteSafetyBytes`). Extras/Specials/Bonus folders are surfaced for manual
  review, never reorganised silently. See [#MB-LAW-3](#MB-§5) and [#MB-LAW-4](#MB-§5).
- **One library re-organizer.** `mediabutler relocate --source M:\Movies` evicts TV folders that
  drifted into the movies library (and vice versa) — the only stage that legally operates on a
  destination. See [#MB-LAW-5](#MB-§5).
- **LLM-assisted long tail.** With `EnableLlmFallback` on, unclassifiable folders are routed
  through `MindAttic.Legion` to a configurable provider (default `claude`). Off by default. See
  [#MB-LAW-6](#MB-§5).
- **Plex-ready output.** TV becomes `M:\TV\<Show>\Season XX\episodes`, movies become
  `M:\Movies\<Title> (YYYY)\`. Per-season artwork hoists to the show root and deduplicates.

## 3. What it is NOT {#MB-§3}
- **NOT a downloader / torrent client.** MediaButler never fetches media; it organizes what is
  already on disk under `SourcePath`.
- **NOT a metadata database.** Episode titles, posters, and movie matching are FileBot's job
  (TheTVDB / TheMovieDB). MediaButler shells out to FileBot; it does not query those APIs itself.
- **NOT a media server.** It produces a Plex-compatible folder layout; it does not stream, scan,
  or talk to a Plex server.
- **NOT a PowerShell script.** It is a .NET console app (with an optional MAUI shell) specifically
  so it can use `MindAttic.Vault` for credential resolution. See
  [README "Why a console app and not PowerShell"](../README.md#why-a-console-app-and-not-powershell).
- **NOT a destination editor (except `relocate`).** Every stage operates on `SourcePath`; only the
  explicit `relocate` command touches `TvDestination`/`MoviesDestination`. See [#MB-LAW-5](#MB-§5).
- **NOT vendor-locked to one LLM.** Fallback parsing routes through Legion; no provider SDK is
  hard-coded. See [HOUSE-LAW-4](../../MindAttic.HouseRules.md#HOUSE-LAW-4).

## 4. Architecture canon {#MB-§4}

```
                      M:\Torrents  (SourcePath)
                            |
                            v
   +---------------------------------------------------------------+
   |  MediaScanner  --classify-->  MediaItem { Kind, ... }          |
   +---------------------------------------------------------------+
                            |
        +-------------------+-----------------------------+
        |  PipelineRunner (orchestrator; 0/1/2 exit code) |
        +-------------------+-----------------------------+
                            |  PathGuard.ValidatePaths (refuse on overlap)
                            v
   RenameStage  ->  FileBotStage  ->  MoveStage          [relocate is separate]
   (local clean,   (filebot.exe:     (cross-volume move
    hoist seasons,  TV/Movies/subs/   to Plex layout,
    delete empties) artwork)          hoist show art)
        |                |                    |
        |          MindAttic.Vault       MoviesDestination / TvDestination
        |          (OpenSubtitles creds)
        v
   NameParser unclassifiable --(EnableLlmFallback)--> LegionFallbackParser
                                                      -> MindAttic.Legion -> provider

   Front doors (same DI graph): Spectre.Console.Cli subcommands  +  MediaButler.Maui shell
   Settings: %APPDATA%\MindAttic\MediaButler\settings.json  (via MindAttic.Vault)
```

### 4.1 Projects
- **`MediaButler/`** ✅ — the console app (`net10.0-windows`, assembly `mediabutler`). Spectre.Console
  CLI + interactive menu. References `MindAttic.Vault` and `MindAttic.Legion`.
- **`MediaButler.Tests/`** ✅ — NUnit test project covering parser, scanner, stages, guards, CLI.
- **`MediaButler.Maui/`** 🟡 — optional MAUI GUI shell (`net10.0-windows10.0.19041.0`) that wraps
  the same pipeline stages via its own `Services/PipelineRunner` and `ConsoleCaptureWriter`.
- **`MediaButler.Maui.UiTests/`** 🟡 — smoke tests for the MAUI shell window/buttons.
- **`MediaButler.Landing.Tests/`** ✅ — tests for the `README.md` -> landing-page rendering.
- **Landing page** — `README.md` is rendered to `mediabutler.htm` and deployed via the sibling
  `MindAttic.Deploy` repo (see `.claude/commands/deploy.md`). The in-repo `scripts/cli/*` +
  `index.htm` are legacy/dead; do not invoke them.

### 4.2 Domain model (NOUNS)
- **`MediaItem`** (`MediaButler/Media/MediaItem.cs`) — one classified top-level folder under
  `SourcePath`: `FullPath`, `OriginalName`, `Kind`, plus movie (`MovieTitle`/`MovieYear`) or TV
  (`ShowName`/`SeasonNumber`/`Seasons`/`OrphanFilesAtParent`) fields.
- **`SeasonChild`** (`MediaButler/Media/MediaItem.cs`) — a nested season subfolder inside a
  multi-season parent (`FullPath`, `SeasonNumber`).
- **`MediaKind`** (`MediaButler/Media/MediaKind.cs`) — `Unknown` | `Movie` | `TvSeason` |
  `MultiSeasonParent` | `Empty` | `Extras`.
- **`MediaButlerSettings`** (`MediaButler/Settings/MediaButlerSettings.cs`) — user config:
  `SourcePath`, `TvDestination`, `MoviesDestination`, `FileBotPath`, subtitle/artwork toggles,
  `DryRun`, `EnableLlmFallback`/`LlmProvider`, `ExcludedFolders`, `VideoExtensions`,
  `EmptyDeleteSafetyBytes`, `ShowLevelArtFiles`, `TitleYearOverrides`.
- **`SubtitleCredentials`** (`MediaButler/Settings/SubtitleCredentials.cs`) — OpenSubtitles
  user/password resolved via the Vault chain; `IsComplete` gates whether creds are passed.
- **`PipelineReport`** (`MediaButler/Pipeline/PipelineReport.cs`) — running tallies (`Renamed`,
  `Hoisted`, `EmptyDeleted`, `TvMoved`, `MoviesMoved`, `Errors`, `NeedsManual`, ...) that produce
  the consolidated summary.
- **`LlmGuess`** (`MediaButler/Llm/LegionFallbackParser.cs`) — the LLM fallback's parsed answer.

### 4.3 Key services (VERBS)
- **`MediaScanner.Scan()`** (`MediaButler/Media/MediaScanner.cs`) — walks `SourcePath`, classifies
  each top-level folder into a `MediaItem`.
- **`NameParser`** (`MediaButler/Media/NameParser.cs`) — regex name cleaning, classification, and
  canonical formatting (`FormatSeasonFolder`, `FormatMovieFolder`, `CleanShowName`).
- **`PipelineRunner`** (`MediaButler/Pipeline/PipelineRunner.cs`) — orchestrates the stages for
  the CLI; owns the `ExitOk=0`/`ExitErrors=1`/`ExitNeedsManual=2` contract.
- **`PathGuard.ValidatePaths()`** (`MediaButler/Pipeline/PathGuard.cs`) — source/destination overlap
  refusal (warn in dry-run, hard refuse live).
- **`RenameStage.Run()`** (`MediaButler/Pipeline/RenameStage.cs`) — local clean + hoist + empty-delete.
- **`FileBotStage`** (`MediaButler/Pipeline/FileBotStage.cs`) — drives `filebot.exe` for TV, movies,
  subtitles, and artwork via `FileBotClient` (`MediaButler/FileBot/FileBotClient.cs`).
- **`MoveStage.Run()`** (`MediaButler/Pipeline/MoveStage.cs`) — cross-volume move to Plex layout,
  `SanitizeForFs`, show-art hoist.
- **`RelocateStage.Run()`** (`MediaButler/Pipeline/RelocateStage.cs`) — destination eviction.
- **`LegionFallbackParser.ClassifyAsync()`** (`MediaButler/Llm/LegionFallbackParser.cs`) — LLM long-tail.
- **`AuditLog`** (`MediaButler/Pipeline/AuditLog.cs`) — append-only record of mutations.

## 5. The Laws {#MB-§5}
> MediaButler **inherits all org-wide laws** from
> [MindAttic.HouseRules.md](../../MindAttic.HouseRules.md) by reference — notably
> [HOUSE-LAW-1](../../MindAttic.HouseRules.md#HOUSE-LAW-1) (whole-number versioning),
> [HOUSE-LAW-2](../../MindAttic.HouseRules.md#HOUSE-LAW-2) (soft-disable, never hard-delete),
> [HOUSE-LAW-3](../../MindAttic.HouseRules.md#HOUSE-LAW-3) (credentials via MindAttic.Vault),
> [HOUSE-LAW-4](../../MindAttic.HouseRules.md#HOUSE-LAW-4) (provider-agnostic LLMs via Legion),
> [HOUSE-LAW-6](../../MindAttic.HouseRules.md#HOUSE-LAW-6) (one engine, many front doors),
> [HOUSE-LAW-8](../../MindAttic.HouseRules.md#HOUSE-LAW-8) (done is verified, not asserted), and
> [HOUSE-LAW-9](../../MindAttic.HouseRules.md#HOUSE-LAW-9) (`psst` only on request).
> The project-specific laws below are NOT restated from House Rules.

### MB-LAW-1 — Dry-run mutates nothing {#MB-LAW-1}
In dry-run every stage logs `[dry: -> target]` and performs no rename, move, delete, or
mutating FileBot call; FileBot is invoked with `--action TEST`. (Verified by
`Dry_run_leaves_disk_untouched_but_still_counts_renames`, `DryRun_does_not_mutate_anything_on_disk`,
`BuildRenameTvArgs_uses_TEST_in_dry_run`, `Dry_run_does_not_move_anything`.)

### MB-LAW-2 — Idempotent canonical names {#MB-LAW-2}
Canonical folder names round-trip through `NameParser` unchanged; re-running on an organized
library is a no-op. `CleanShowName` strips trailing dashes so `The Mentalist - Season 04` never
becomes `The Mentalist - - Season 04`. (Verified by
`Idempotent_run_no_ops_when_folder_already_canonical`,
`Pipeline_re_runs_are_idempotent_on_an_already_organized_library`,
`CleanShowName_strips_trailing_year_even_behind_bracket_tags`.)

### MB-LAW-3 — Refuse on source/destination overlap {#MB-LAW-3}
`PathGuard` refuses to run live when `SourcePath` equals, contains, or is contained by a
destination (sibling prefixes like `TV` vs `TV2` must NOT trigger). Dry-run downgrades the
refusal to a warning. (Verified by `PathGuardTests.PathOverlaps_*`.)

### MB-LAW-4 — Delete empties only past the safety floor; never touch Extras {#MB-LAW-4}
A folder with zero recognised video files is deleted only if it holds at most
`EmptyDeleteSafetyBytes` (default 1 MB); anything larger is surfaced as needs-manual.
`Extras`/`Specials`/`Bonus` are classified `Extras`, left in place, and flagged — never deleted or
renamed as movies. (Verified by `Empty_disguised_folder_is_deleted`,
`Empty_size_guard_refuses_to_delete_a_folder_that_exceeds_the_threshold`,
`Extras_folder_is_left_in_place_and_flagged`,
`Extras_folder_without_video_is_classified_Extras_not_Empty`.)

### MB-LAW-5 — Only `relocate` touches a destination {#MB-LAW-5}
Every pipeline stage operates on `SourcePath`. `relocate` is the sole stage that intentionally
runs against `TvDestination`/`MoviesDestination` to evict misfiled folders, and it bypasses the
overlap guard for exactly that reason. (Verified by
`Relocate_evicts_a_TvSeason_dropped_into_the_movies_destination`,
`TvSeason_living_in_MoviesDestination_is_relocated_to_TvDestination`,
`Movie_living_in_TvDestination_is_relocated_to_MoviesDestination`.)

### MB-LAW-6 — LLM fallback is opt-in and non-fatal {#MB-LAW-6}
`EnableLlmFallback` is `false` by default. When on, `LegionFallbackParser` returns `null` on any
failure (disabled, unparseable, provider error) — MediaButler skips the folder rather than rename
it wrong. Routes through `MindAttic.Legion`; no provider SDK is hard-coded
([HOUSE-LAW-4](../../MindAttic.HouseRules.md#HOUSE-LAW-4)). (See `MediaButler/Llm/LegionFallbackParser.cs`.)

### MB-LAW-7 — Secrets never live in settings.json {#MB-LAW-7}
`settings.json` lives unencrypted in roaming app-data, so OpenSubtitles and LLM credentials are
resolved only through the `MindAttic.Vault` chain (User Secrets -> env vars ->
`%APPDATA%\MindAttic\...\providers.json`), never persisted into `MediaButlerSettings`. Concretises
[HOUSE-LAW-3](../../MindAttic.HouseRules.md#HOUSE-LAW-3). FileBot args reference secrets by `@path`,
not raw values. (Verified by `BuildGetSubtitlesArgs_emits_at_path_references_not_raw_secrets`,
`IsComplete_requires_both_user_and_password`.)

### MB-LAW-8 — Three exit codes, and `2` is actionable {#MB-LAW-8}
Headless runs return `0` (clean), `1` (errors), or `2` (no errors but items need a human eye:
Unknown folders, target-exists skips, Extras). Cron jobs must treat `2` as actionable, not silent
success. (Verified by `Pipeline_returns_NeedsManual_exit_code_when_only_extras_remain`,
`Unknown_subcommand_returns_nonzero`,
`Version_subcommand_prints_version_and_exits_zero`.)

## 6. Verified state {#MB-§6}
> Build/test evidence recorded 2026-06-07 — see [#MB-§8](#MB-§8) for the bar.

- **Build (2026-06-07):** `dotnet build MediaButler/MediaButler.csproj` — **succeeded, 0 warnings,
  0 errors** (net10.0-windows; references MindAttic.Vault + MindAttic.Legion resolved cleanly).
- **Core tests (2026-06-07):** `dotnet test MediaButler.Tests/MediaButler.Tests.csproj` — **Passed:
  150, Failed: 0, Skipped: 0** (NUnit; duration ~2 s). Suite covers `NameParserTests`,
  `MediaScannerTests`, `RenameStageTests`, `MoveStageTests`, `RelocateStageTests`,
  `PathGuardTests`, `SubtitleCredentialsTests`, `FileBotClientTests`, `CliEndToEndTests`,
  `PathologicalLibraryPipelineTests`.
- **Proven working (✅):** name parsing/classification of the pathological fixture, dry-run
  no-mutation, idempotent re-runs, multi-season hoist, empty-delete safety floor, Extras
  preservation, path-overlap refusal, relocate eviction, FileBot arg construction, exit-code
  contract, README->landing rendering.
- **Partial (🟡):** the MAUI shell (`MediaButler.Maui`) and its UI smoke tests run only on
  Windows desktop and are not part of the headless `MediaButler.Tests` gate; treated as 🟡 until
  proven in this environment. Live FileBot/OpenSubtitles/LLM paths require external binaries and
  credentials and are exercised by construction tests, not live integration. `LandingPageTests`
  require Playwright browser binaries (`playwright.ps1 install chromium`) and skip gracefully when
  absent — treated as 🟡 in headless CI until binaries are provisioned.

## 7. Active frontier {#MB-§7}
- See `docs/rfc/` for open design notes.
- See `docs/USER_STORIES.md` for the epic breakdown and priority backlog.
- Known frontier items: broaden `TitleYearOverrides` coverage as new year-in-title movies land;
  promote the MAUI shell from 🟡 to ✅ with an environment that can run it; live-integration
  harness for FileBot and OpenSubtitles.

## 8. Quality bar {#MB-§8}
A feature is done (✅) only when:
1. It has an NUnit test in `MediaButler.Tests` that proves the behavior (named in the story/law).
2. `dotnet build MediaButler/MediaButler.csproj` is clean.
3. `dotnet test MediaButler.Tests/MediaButler.Tests.csproj` is green.
4. Anything that mutates disk has a dry-run path proven not to mutate ([#MB-LAW-1](#MB-LAW-1)).
5. Anything user-facing degrades gracefully (FileBot/creds/LLM absent -> reported, not crashed).
Otherwise it is 🟡/⬜. Inherits [HOUSE-LAW-8](../../MindAttic.HouseRules.md#HOUSE-LAW-8).

## 9. Glossary {#MB-§9}
- **SourcePath** — the scanned inbox of messy dumps (default `M:\Torrents`).
- **TvDestination / MoviesDestination** — the Plex-canonical output roots (`M:\TV`, `M:\Movies`).
- **Canonical name** — the idempotent target form: `Show - Season NN` / `Title (YYYY)`.
- **Multi-season parent** — one folder holding multiple `Season N` subfolders that must be hoisted.
- **Hoist** — lift nested `Season N` subfolders (or show-level artwork) up one level.
- **Extras** — `Extras`/`Specials`/`Bonus` companion content; preserved, never reorganised.
- **Dry-run** — log-only mode; FileBot runs `--action TEST`; no disk mutation ([#MB-LAW-1](#MB-LAW-1)).
- **Relocate** — destination-eviction command for folders that drifted into the wrong library.
- **FileBot** — external tool (`filebot.exe`) that renames via TheTVDB/TheMovieDB and fetches art.
- **Needs-manual** — items the pipeline declines to touch; drives exit code `2` ([#MB-LAW-8](#MB-LAW-8)).
- **Vault chain** — `MindAttic.Vault` credential resolution (User Secrets -> env -> providers.json).
- **Legion** — `MindAttic.Legion`, the provider-agnostic LLM transport.

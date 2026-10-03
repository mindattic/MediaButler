---
codex: 1
project: MediaButler
code: MB
layer: bible
status: living
updated: 2026-10-03
---

# MediaButler — Project Bible
> Single source of truth for what MediaButler IS, is NOT, and the rules that keep it coherent.
> README says how to build/run; this says how to think about the system.

## 1. The one sentence {#MB-§1}
MediaButler watches one or more inboxes of messy torrent dumps, cleans the names locally
(consolidating per-episode dumps, splitting movie packs, wrapping loose files), hands the
survivors to FileBot for episode titles and artwork, optionally fetches subtitles, and moves
everything into a canonical Plex layout — idempotently, dry-run-first, refusing to run when a
source overlaps a destination, and cataloging every naming variation it sees into a persistent,
user-extendable corpus.

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
- **LLM-assisted long tail.** With `EnableLlmFallback` on, unclassifiable folders AND loose files
  that match no known pattern are routed through `MindAttic.Legion` to a configurable provider
  (default `claude`). Off by default. See [#MB-LAW-6](#MB-§5).
- **Plex-ready output.** TV becomes `M:\TV\<Show> - Season XX\episodes` (flat — no per-show
  container folder, matching the user's library), movies become `M:\Movies\<Title> (YYYY)\`.
- **Merge, never overwrite.** A second dump of the same season merges file-by-file into the
  existing canonical folder. A true duplicate (same file name or parsed episode at the destination
  season, or a movie whose destination folder already has content) is resolved by policy:
  `KeepLargest` (default) keeps the copy with the larger video and deletes the other, audit-logged;
  `Flag` leaves both and asks a human. Movies use `duplicateMovieAction`, TV episodes
  `duplicateEpisodeAction`. See [#MB-LAW-9](#MB-§5).
- **Reboot-safe TV routing.** When a TV show is rebooted under the same name, MediaButler routes
  year-tagged content to `ShowName (YEAR) - Season NN` automatically — once the user renames the
  existing bare season folder(s) to include the year. See [#MB-LAW-4](#MB-§5).
- **MCP front door.** `mediabutler mcp` exposes the pipeline over the Model Context Protocol
  (stdio, JSON-RPC 2.0): tools `scan`, `status`, and `run` (dry-run by default). Same engine as
  the CLI. Register with `claude mcp add mediabutler -- mediabutler mcp`.
- **Every variation is cataloged.** Each scan appends newly-seen names into
  `%APPDATA%\MindAttic\MediaButler\variations.json` (sections `movie`/`tv`/`music`/`unknown`),
  created as a clone of the hardcoded `MasterVariations` list. The file is hand-editable: moving
  an entry into a section pins that name's category on later runs. See [#MB-LAW-10](#MB-§5).
- **Many inboxes, one call.** `ExtraSources` + repeatable `--source` process several roots per
  run; `--recursive` additionally treats excluded container subfolders (`temp`, `incomplete`, …)
  as inboxes of their own. Exit codes combine by severity (1 > 2 > 0).
- **Music passes through untouched.** Audio-only folders (and catalog-pinned names) classify
  `Music` — never renamed or restructured, moved as-is to `MusicDestination` when configured,
  flagged otherwise.

## 3. What it is NOT {#MB-§3}
- **NOT a downloader / torrent client.** MediaButler never fetches media; it organizes what is
  already on disk under `SourcePath`.
- **NOT a metadata database.** Episode titles, posters, and movie matching are FileBot's job
  (TheTVDB / TheMovieDB). MediaButler shells out to FileBot; it does not query those APIs itself.
- **NOT a media server.** It produces a Plex-compatible folder layout; it does not stream, scan,
  or talk to a Plex server.
- **NOT a PowerShell script.** It is a .NET console app (with an optional WPF shell) specifically
  so it can use `MindAttic.Vault` for credential resolution. See
  [README "Why a console app and not PowerShell"](../README.md#why-a-console-app-and-not-powershell).
- **NOT a destination editor (except `relocate`).** Every stage operates on `SourcePath`; only the
  explicit `relocate` command touches `TvDestination`/`MoviesDestination`. See [#MB-LAW-5](#MB-§5).
- **NOT vendor-locked to one LLM.** Fallback parsing routes through Legion; no provider SDK is
  hard-coded. See [HOUSE-LAW-4](../../MindAttic.HouseRules.md#HOUSE-LAW-4).
- **NOT a music organizer.** Music is detected (audio extensions, catalog pins) so it is never
  deleted as "empty" or renamed as a movie, and it can be MOVED as-is — but tagging and
  restructuring music libraries is a different tool's job.

## 4. Architecture canon {#MB-§4}

```
        M:\Torrents + ExtraSources (+ --recursive container subfolders)
                            |   one pass per source, exit codes combined (1 > 2 > 0)
                            v
   +---------------------------------------------------------------+
   |  MediaScanner  --classify-->  MediaItem { Kind, ... }          |
   |  (folders + loose root files; consults VariationCatalog pins,  |
   |   records every name back into variations.json)                |
   +---------------------------------------------------------------+
                            |
        +-------------------+-----------------------------+
        |  PipelineRunner (orchestrator; 0/1/2 exit code) |
        +-------------------+-----------------------------+
                            |  PathGuard.ValidatePaths (refuse on overlap, incl. MusicDestination)
                            v
   RenameStage  ->  FileBotStage  ->  MoveStage          [relocate is separate]
   (local clean,   (filebot.exe:     (cross-volume move
    hoist seasons,  TV/Movies/subs/   to flat season folder,
    consolidate     artwork)          merge into existing
    episodes, split                   seasons, move music
    packs, wrap loose                 as-is)
    files, merge dups,                |
    delete empties)      |                    |
        |          MindAttic.Vault       MoviesDestination / TvDestination / MusicDestination
        |          (OpenSubtitles creds)
        v
   unclassifiable folder or loose file --(EnableLlmFallback)--> LegionFallbackParser
                                                      -> MindAttic.Legion -> provider

   Front doors (same DI graph): Spectre.Console.Cli subcommands  +  MediaButler.Wpf shell  +  MCP (stdio JSON-RPC)
   Settings:   %APPDATA%\MindAttic\MediaButler\settings.json   (via MindAttic.Vault)
   Variations: %APPDATA%\MindAttic\MediaButler\variations.json (clone of MasterVariations + discoveries)
```

### 4.1 Projects
- **`MediaButler/`** ✅ — the console app (`net10.0-windows`, assembly `mediabutler`). Spectre.Console
  CLI + interactive menu. References `MindAttic.Vault` and `MindAttic.Legion`.
- **`MediaButler.Tests/`** ✅ — NUnit test project covering parser, scanner, stages, guards, CLI.
- **`MediaButler.Wpf.UI/`** 🟡 — Razor Class Library: the WPF shell's markup (`Pages/Run.razor`,
  `Pages/Settings.razor`, `Layout/TabShell.razor`, `Shared/ConfirmDialog.razor`) plus its own
  `Services/PipelineRunner` and `ConsoleCaptureWriter`.
  WCAG 2.2 AA: real `<label for>` associations, `role="switch"`/`aria-checked` toggles, `role="tab"`
  tabs (WAI-ARIA APG pattern), `aria-live="polite"` status regions (never the log pane), and the
  same already-audited contrast colors as CSS custom properties.
- **`MediaButler.Wpf/`** 🟡 — optional WPF + BlazorWebView GUI shell (`net10.0-windows10.0.19041.0`
  — BlazorWebView's WinRT composition control needs the SDK-versioned TFM; `Microsoft.NET.Sdk.Razor`
  — plain `Microsoft.NET.Sdk` can't discover BlazorWebView's own static web assets) hosting
  `MediaButler.Wpf.UI.App` in a single window.
- **`MediaButler.Wpf.UiTests/`** 🟡 — FlaUI smoke tests for the WPF shell's window/buttons
  (WebView2's Chromium content exposes its own accessibility tree through the same UIA bridge).
- **`MediaButler.Wpf.AccessibilityTests/`** 🟡 — bUnit structural-markup tests (label/role contract,
  no browser) plus a real-Chromium axe-core scan (`wcag2a`/`wcag2aa`/`wcag22aa`) of
  `MediaButler.Wpf.UI.App`, hosted by the standalone `MediaButler.Wpf.AccessibilityTests.Harness`
  exe (a Blazor Web App launched as a subprocess — an in-process `dotnet test` host breaks
  ASP.NET Core's entry-assembly-keyed component/static-asset discovery).
- **`MediaButler.Landing.Tests/`** 🟡 — Playwright checks (`LandingPageTests`: title/heading, CTA
  links, README content, no console errors) against the repo-root `index.htm`. `index.htm` is a
  static HTML snapshot of the README (dated 2026-05-23); nothing in the repo regenerates or
  publishes it, so these tests guard only that file, not the live README. They skip when Playwright
  browser binaries are absent and are outside the headless `MediaButler.Tests` gate.
- **Project page** — the GitHub README (https://github.com/mindattic/MediaButler);
  `tools/build-readme.ps1` renders it into `README.htm`. MediaButler has no web deploy.
  `package.json`'s `build`/`deploy` scripts call `scripts/cli/*` files that are not in the repo, so
  they fail; do not use them.

### 4.2 Domain model (NOUNS)
- **`MediaItem`** (`MediaButler/Media/MediaItem.cs`) — one classified top-level entry (folder OR
  loose file, `IsFile`) under a source: `FullPath`, `OriginalName`, `Kind`, plus movie
  (`MovieTitle`/`MovieYear`), TV (`ShowName`/`SeasonNumber`/`EpisodeNumber`/`Seasons`/
  `OrphanFilesAtParent`/`LooseEpisodes`/`TvYear`), or pack (`PackMovies`) fields. `TvYear` holds
  the series-premiere year parsed from the source folder name, used for reboot disambiguation.
- **`SeasonChild`** (`MediaButler/Media/MediaItem.cs`) — a nested season subfolder inside a
  multi-season parent (`FullPath`, `SeasonNumber`).
- **`LooseEpisode` / `MoviePackChild`** (`MediaButler/Media/MediaItem.cs`) — a flat episode file
  keyed by parsed season/episode; a movie file inside a multi-movie pack.
- **`MediaKind`** (`MediaButler/Media/MediaKind.cs`) — `Unknown` | `Movie` | `TvSeason` |
  `MultiSeasonParent` | `Empty` | `Extras` | `TvEpisode` | `MoviePack` | `MovieCollection` | `Music`.
- **`MediaButlerSettings`** (`MediaButler/Settings/MediaButlerSettings.cs`) — user config:
  `SourcePath`, `ExtraSources`, `Recursive`, `TvDestination`, `MoviesDestination`,
  `MusicDestination`, `FileBotPath`, subtitle/artwork toggles, `DryRun`,
  `EnableLlmFallback`/`LlmProvider`, `ExcludedFolders`, `VideoExtensions`, `AudioExtensions`,
  `SubtitleExtensions`, `EmptyDeleteSafetyBytes`, `SampleMaxBytes`,
  `TitleYearOverrides`, `VariationCatalogPath`, `duplicateMovieAction` and
  `duplicateEpisodeAction` (both `DuplicateMovieAction`: `KeepLargest` | `Flag`, default
  `KeepLargest`).
- **`VariationCatalog`** (`MediaButler/Media/VariationCatalog.cs`) — the persistent naming-
  variation corpus + category pins; seeded from **`MasterVariations`**
  (`MediaButler/Media/MasterVariations.cs`).
- **`EpisodeInfo`** (`MediaButler/Media/NameParser.cs`) — a parsed episode marker
  (`Show`, `Season`, `Episode`, `EpisodeEnd`).
- **`SubtitleCredentials`** (`MediaButler/Settings/SubtitleCredentials.cs`) — OpenSubtitles
  user/password resolved via the Vault chain; `IsComplete` gates whether creds are passed.
- **`PipelineReport`** (`MediaButler/Pipeline/PipelineReport.cs`) — running tallies (`Renamed`,
  `Hoisted`, `CollectionHoisted`, `Consolidated`, `PackSplit`, `MergedFiles`, `EmptyDeleted`,
  `TvMoved`, `MoviesMoved`, `MusicMoved`, `Errors`, `NeedsManual`, ...) that produce the
  consolidated summary.
- **`LlmGuess` / `LlmFileGuess`** (`MediaButler/Llm/LegionFallbackParser.cs`) — the LLM fallback's
  parsed answers for folders and loose files respectively.

### 4.3 Key services (VERBS)
- **`MediaScanner.Scan()`** (`MediaButler/Media/MediaScanner.cs`) — walks a source root, classifies
  each top-level folder AND loose video file into a `MediaItem`; consults `VariationCatalog` pins
  before the regex classifiers and records every classification back into the catalog.
- **`NameParser`** (`MediaButler/Media/NameParser.cs`) — regex name cleaning, classification, and
  canonical formatting (`FormatSeasonFolder`, `FormatMovieFolder`, `CleanShowName`), plus episode
  parsing (`ParseEpisode`, `ParseEpisodeNumberInSeason`) and sample detection (`IsSampleName`).
- **`PipelineRunner`** (`MediaButler/Pipeline/PipelineRunner.cs`) — orchestrates the stages for
  the CLI; expands `EffectiveSources` (primary + extras + recursive containers) and owns the
  `ExitOk=0`/`ExitErrors=1`/`ExitNeedsManual=2` contract (combined across sources by severity).
- **`PathGuard.ValidatePaths()`** (`MediaButler/Pipeline/PathGuard.cs`) — source/destination overlap
  refusal (warn in dry-run, hard refuse live); covers TV, Movies, and Music destinations.
- **`RenameStage.Run()`** (`MediaButler/Pipeline/RenameStage.cs`) — local clean + hoist +
  empty-delete + episode consolidation (`TvEpisode` → `{Show} - Season XX`) + pack splitting
  (`MoviePack` → one folder per film) + collection hoisting (`MovieCollection` husk → one
  `{Title} (YYYY)/` per sub-dir at source root) + loose-movie wrapping + duplicate-season merge.
- **`SeasonMerger`** (`MediaButler/Pipeline/SeasonMerger.cs`) — episode-aware file-level merge into
  an existing canonical season folder + sample-aware shell cleanup (shared by Rename and Move);
  `MergeFiles` applies `duplicateEpisodeAction` to video collisions.
- **`FileBotStage`** (`MediaButler/Pipeline/FileBotStage.cs`) — drives `filebot.exe` for TV, movies,
  subtitles, and artwork via `FileBotClient` (`MediaButler/FileBot/FileBotClient.cs`).
- **`MoveStage.Run()`** (`MediaButler/Pipeline/MoveStage.cs`) — cross-volume move to a flat season
  folder directly under `TvDestination` (no per-show container), `SanitizeForFs`, destination-side
  season merge, music move-as-is. Reboot disambiguation: routes to `ShowName (TvYear) - Season NN`
  when `IsShowDisambiguated` finds an existing year-tagged season folder in the TV destination;
  applies `duplicateMovieAction` for movie destination collisions (`KeepLargest` by default).
- **`RelocateStage.Run()`** (`MediaButler/Pipeline/RelocateStage.cs`) — destination eviction.
- **`LegionFallbackParser.ClassifyAsync()` / `ClassifyFileAsync()`**
  (`MediaButler/Llm/LegionFallbackParser.cs`) — LLM long-tail for folders and unmatched files.
- **`McpServer`** (`MediaButler/Mcp/McpServer.cs`) — serves the Model Context Protocol over stdio
  (newline-delimited JSON-RPC 2.0): tools `scan` (classification JSON), `status` (config snapshot),
  `run` (full pipeline; `dryRun=true` by default). Dispatches into the same `PipelineRunner` as
  the CLI (HOUSE-LAW-6). Invoked via `mediabutler mcp`.
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
renamed as movies. **Reboot routing:** a TV season whose parsed `TvYear` is set is routed to
`ShowName (TvYear) - Season NN` in `MoveStage` when `IsShowDisambiguated` confirms an existing
year-tagged season folder in `TvDestination`; otherwise the bare `ShowName - Season NN` path is
used, preserving backward-compatible behaviour for year-less shows. (Verified by
`Empty_disguised_folder_is_deleted`,
`Empty_size_guard_refuses_to_delete_a_folder_that_exceeds_the_threshold`,
`Extras_folder_is_left_in_place_and_flagged`,
`Extras_folder_without_video_is_classified_Extras_not_Empty`,
`ParseSingleSeason_extracts_tv_year`.)

### MB-LAW-5 — Only `relocate` touches a destination {#MB-LAW-5}
Every pipeline stage operates on `SourcePath`. `relocate` is the sole stage that intentionally
runs against `TvDestination`/`MoviesDestination` to evict misfiled folders, and it bypasses the
overlap guard for exactly that reason. (Verified by
`Relocate_evicts_a_TvSeason_dropped_into_the_movies_destination`,
`TvSeason_living_in_MoviesDestination_is_relocated_to_TvDestination`,
`Movie_living_in_TvDestination_is_relocated_to_MoviesDestination`.)

### MB-LAW-6 — LLM fallback is opt-in and non-fatal {#MB-LAW-6}
`EnableLlmFallback` is `false` by default. When on, `LegionFallbackParser` returns `null` on any
failure (disabled, unparseable, provider error) — MediaButler skips the folder (or loose file,
via `ClassifyFileAsync`) rather than rename it wrong. Routes through `MindAttic.Legion`; no
provider SDK is hard-coded
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
Unknown folders, duplicate-rip conflicts, Extras). Multi-source runs combine per-source codes by
severity (1 > 2 > 0). Cron jobs must treat `2` as actionable, not silent success. (Verified by
`Pipeline_returns_NeedsManual_exit_code_when_only_extras_remain`,
`Unknown_subcommand_returns_nonzero`,
`Version_subcommand_prints_version_and_exits_zero`.)

### MB-LAW-9 — Merge, never overwrite; duplicates are policy-resolved {#MB-LAW-9}
**TV:** When a season's canonical target already exists (source-side rename or destination-side
move), files merge individually; MediaButler never silently overwrites or double-files an episode.
At the destination-side merge (`MoveStage.MoveTvSeason` -> `SeasonMerger.MergeFiles`), a video
whose NAME or PARSED EPISODE already exists at the target is resolved by `duplicateEpisodeAction`
(CLI `--tv-duplicates keep-largest|flag`): `KeepLargest` (default) keeps the larger video and
deletes the smaller (audit-logged `duplicate-replace` / `duplicate-discard`); `Flag` leaves the
incoming file behind and flags it (exit 2). Both sides must be videos for the size comparison;
subtitle sidecars keep the exact-name conflict check and are flagged. The source-side mergers
(`RenameStage` episode consolidation and flat-episode filing) compare scene filenames by exact
name only and always flag. Emptied shells are deleted only under the sample-aware guard: every
remaining video must be sample-named and at most `SampleMaxBytes`, with other junk at most
`EmptyDeleteSafetyBytes`. Sample clips never travel to the library.

**Movies:** `duplicateMovieAction` (default `KeepLargest`) resolves destination collisions
automatically. `KeepLargest` compares the largest non-sample video on each side; the larger copy
wins (incoming larger → destination videos deleted, folder merged; incoming smaller → incoming
deleted). Artwork and non-video files on the surviving side are preserved. When no comparable
video exists on either side, falls back to `Flag` (no guess that could destroy media).
`Flag` leaves both copies and flags the item. Both directions are audit-logged
(`duplicate-replace` / `duplicate-discard`). CLI `--duplicates keep-largest|flag`.

Dry-run logs every duplicate decision (TV and movies) and mutates nothing.

(Verified by `True_duplicate_rips_stay_behind_and_are_flagged_for_a_human`,
`Junk_and_sample_shells_are_cleaned_up_after_consolidation`,
`Reruns_never_touch_destinations_and_sources_converge_to_a_steady_state`,
`DuplicateMovieActionTests.*`, `DuplicateEpisodeActionTests.*`.)

### MB-LAW-10 — The variation catalog grows, pins, and never clobbers user edits {#MB-LAW-10}
Every scan records each classified top-level name into
`%APPDATA%\MindAttic\MediaButler\variations.json` (sections `movie`/`tv`/`music`/`unknown`),
which is created as a clone of the hardcoded `MasterVariations` master list and merges new master
entries on upgrade. The file is hand-editable: placing a name into `movie`/`tv`/`music` pins its
category for subsequent classification (exact, case-insensitive). A file that fails to parse
disables saving for the run — user edits are never overwritten by MediaButler. (Verified by
`Records_classified_names_into_sections_and_persists`,
`Hand_edited_sections_pin_classification_hints`,
`Corrupted_file_disables_saving_so_user_edits_survive`.)

## 6. Verified state {#MB-§6}
> Latest evidence 2026-10-03 — see [#MB-§8](#MB-§8) for the bar.

- **Core build + tests (2026-10-03):** `dotnet test MediaButler.Tests/MediaButler.Tests.csproj` —
  builds the main project and tests, **Passed: 283, Failed: 0, Skipped: 0** (NUnit). Suite covers `NameParserTests`, `EpisodeParsingTests`,
  `VariationCatalogTests`, `MediaScannerTests`, `RenameStageTests`, `MoveStageTests`,
  `RelocateStageTests`, `PathGuardTests`, `SubtitleCredentialsTests`, `FileBotClientTests`,
  `CliEndToEndTests`, `PathologicalLibraryPipelineTests`, `RealWorldLibraryPipelineTests`,
  `DuplicateMovieActionTests`, `DuplicateEpisodeActionTests`, `McpServerTests`, `SettingsEditorTests`.
- **Proven working (✅):** every ✅ story in `docs/USER_STORIES.md`, including movie-collection
  husk hoisting (`MovieCollection`), dry-run FileBot TEST-pass detection
  (`FileBotResult.LooksLikeTestPass`), `KeepLargest` / `Flag` duplicate policy for movies and TV
  episodes, the flat `{Show} - Season NN` TV layout, MCP front door (`mediabutler mcp`), and
  reboot/same-name TV disambiguation (`IsShowDisambiguated` routing).
- **Partial (🟡):** the WPF shell (`MediaButler.Wpf`), its FlaUI smoke tests, and its
  bUnit/axe-core accessibility scan run only on Windows desktop and are not part of the headless
  `MediaButler.Tests` gate; treated as 🟡 pending a CI/desktop runner even though all three have
  each passed clean on real Windows desktop hardware (2026-09-25): `MediaButler.Wpf.UiTests`
  (5/5, FlaUI against the built `.exe`) and `MediaButler.Wpf.AccessibilityTests` (7/7 — 5 bUnit
  markup-contract tests plus a real-Chromium axe-core scan of both tabs with zero
  wcag2a/wcag2aa/wcag22aa violations). Live FileBot/OpenSubtitles/LLM paths require external
  binaries and credentials and are exercised by construction tests, not live integration (the
  Legion fallback for folders AND unmatched files is implemented but has no mocked-transport test
  yet). `LandingPageTests` and `MediaButler.Wpf.AccessibilityTests`' axe-core scan both require
  Playwright browser binaries (`playwright.ps1 install chromium`) and skip gracefully when
  absent — treated as 🟡 in headless CI until binaries are provisioned (and `LandingPageTests`
  checks only the static `index.htm` snapshot, see [§4.1](#MB-§4)).

## 7. Active frontier {#MB-§7}
- See `docs/rfc/` for open design notes.
- See `docs/USER_STORIES.md` for the epic breakdown and priority backlog.
- Known frontier items: broaden `TitleYearOverrides` coverage as new year-in-title movies land;
  wire a CI/desktop runner for `MediaButler.Wpf.UiTests` and `MediaButler.Wpf.AccessibilityTests`
  to promote the WPF shell from 🟡 to ✅ (both suites already pass clean on real desktop hardware,
  see §6); live-integration harness for FileBot and OpenSubtitles; mocked-Legion tests so
  MB-US-F1/F2 can graduate to ✅; date-based TV (`Daily.Show.2024.01.15`) and anime absolute
  numbering (`[Group] Show - 01`) are cataloged in `MasterVariations` but not yet auto-converted
  by the regex pipeline.

## 8. Quality bar {#MB-§8}
A feature is done (✅) only when:
1. It has an NUnit test in `MediaButler.Tests` that proves the behavior (named in the story/law).
2. `dotnet build MediaButler/MediaButler.csproj` is clean.
3. `dotnet test MediaButler.Tests/MediaButler.Tests.csproj` is green.
4. Anything that mutates disk has a dry-run path proven not to mutate ([#MB-LAW-1](#MB-LAW-1)).
5. Anything user-facing degrades gracefully (FileBot/creds/LLM absent -> reported, not crashed).
Otherwise it is 🟡/⬜. Inherits [HOUSE-LAW-8](../../MindAttic.HouseRules.md#HOUSE-LAW-8).

## 9. Glossary {#MB-§9}
- **SourcePath / ExtraSources** — the scanned inboxes of messy dumps (default `M:\Torrents`);
  with `Recursive`, excluded container subfolders (`temp`, `incomplete`, …) become inboxes too.
- **TvDestination / MoviesDestination / MusicDestination** — the output roots (`M:\TV`,
  `M:\Movies`; music is optional and moved as-is).
- **Canonical name** — the idempotent target form: `Show - Season NN` / `Title (YYYY)`.
- **Multi-season parent** — one folder holding multiple `Season N` subfolders (or flat episode
  files spanning seasons) that must be hoisted/filed.
- **Hoist** — lift nested `Season N` subfolders (from a source-side multi-season dump) up one level.
- **Consolidate** — file a per-episode dump or loose episode file into its `{Show} - Season XX`.
- **Pack split** — break a multi-movie folder into one `{Title} (YYYY)` folder per film.
- **Merge** — file-level union of a duplicate season into the existing canonical folder;
  episode collisions are resolved by `duplicateEpisodeAction` ([#MB-LAW-9](#MB-LAW-9)).
- **Sample** — a release group's promo clip (`...-sample.mkv`); junk under `SampleMaxBytes`,
  never moved to the library.
- **Extras** — `Extras`/`Specials`/`Bonus` companion content; preserved, never reorganised.
- **Dry-run** — log-only mode; FileBot runs `--action TEST`; no disk mutation ([#MB-LAW-1](#MB-LAW-1));
  override a persisted dry-run with `--live`.
- **Relocate** — destination-eviction command for folders that drifted into the wrong library.
- **FileBot** — external tool (`filebot.exe`) that renames via TheTVDB/TheMovieDB and fetches art.
- **Needs-manual** — items the pipeline declines to touch; drives exit code `2` ([#MB-LAW-8](#MB-LAW-8)).
- **Variation catalog** — `%APPDATA%\MindAttic\MediaButler\variations.json`; the growing,
  hand-editable corpus of naming formats, seeded from `MasterVariations` ([#MB-LAW-10](#MB-LAW-10)).
- **Vault chain** — `MindAttic.Vault` credential resolution (env vars -> providers.json buckets).
- **Legion** — `MindAttic.Legion`, the provider-agnostic LLM transport.

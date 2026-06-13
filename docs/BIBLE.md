---
codex: 1
project: MediaButler
code: MB
layer: bible
status: living
updated: 2026-06-12
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
- **Plex-ready output.** TV becomes `M:\TV\<Show>\Season XX\episodes`, movies become
  `M:\Movies\<Title> (YYYY)\`. Per-season artwork hoists to the show root and deduplicates.
- **Merge, never overwrite.** A second dump of the same season merges file-by-file into the
  existing canonical folder; a file whose name or parsed episode already exists at the target
  stays behind and is flagged — duplicate rips are a human decision. See [#MB-LAW-9](#MB-§5).
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
- **NOT a PowerShell script.** It is a .NET console app (with an optional MAUI shell) specifically
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
    hoist seasons,  TV/Movies/subs/   to Plex layout,
    consolidate     artwork)          hoist show art,
    episodes, split                   merge into existing
    packs, wrap loose                 seasons, move music
    files, merge dups,                as-is)
    delete empties)      |                    |
        |          MindAttic.Vault       MoviesDestination / TvDestination / MusicDestination
        |          (OpenSubtitles creds)
        v
   unclassifiable folder or loose file --(EnableLlmFallback)--> LegionFallbackParser
                                                      -> MindAttic.Legion -> provider

   Front doors (same DI graph): Spectre.Console.Cli subcommands  +  MediaButler.Maui shell
   Settings:   %APPDATA%\MindAttic\MediaButler\settings.json   (via MindAttic.Vault)
   Variations: %APPDATA%\MindAttic\MediaButler\variations.json (clone of MasterVariations + discoveries)
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
- **`MediaItem`** (`MediaButler/Media/MediaItem.cs`) — one classified top-level entry (folder OR
  loose file, `IsFile`) under a source: `FullPath`, `OriginalName`, `Kind`, plus movie
  (`MovieTitle`/`MovieYear`), TV (`ShowName`/`SeasonNumber`/`EpisodeNumber`/`Seasons`/
  `OrphanFilesAtParent`/`LooseEpisodes`), or pack (`PackMovies`) fields.
- **`SeasonChild`** (`MediaButler/Media/MediaItem.cs`) — a nested season subfolder inside a
  multi-season parent (`FullPath`, `SeasonNumber`).
- **`LooseEpisode` / `MoviePackChild`** (`MediaButler/Media/MediaItem.cs`) — a flat episode file
  keyed by parsed season/episode; a movie file inside a multi-movie pack.
- **`MediaKind`** (`MediaButler/Media/MediaKind.cs`) — `Unknown` | `Movie` | `TvSeason` |
  `MultiSeasonParent` | `Empty` | `Extras` | `TvEpisode` | `MoviePack` | `Music`.
- **`MediaButlerSettings`** (`MediaButler/Settings/MediaButlerSettings.cs`) — user config:
  `SourcePath`, `ExtraSources`, `Recursive`, `TvDestination`, `MoviesDestination`,
  `MusicDestination`, `FileBotPath`, subtitle/artwork toggles, `DryRun`,
  `EnableLlmFallback`/`LlmProvider`, `ExcludedFolders`, `VideoExtensions`, `AudioExtensions`,
  `SubtitleExtensions`, `EmptyDeleteSafetyBytes`, `SampleMaxBytes`, `ShowLevelArtFiles`,
  `TitleYearOverrides`, `VariationCatalogPath`.
- **`VariationCatalog`** (`MediaButler/Media/VariationCatalog.cs`) — the persistent naming-
  variation corpus + category pins; seeded from **`MasterVariations`**
  (`MediaButler/Media/MasterVariations.cs`).
- **`EpisodeInfo`** (`MediaButler/Media/NameParser.cs`) — a parsed episode marker
  (`Show`, `Season`, `Episode`, `EpisodeEnd`).
- **`SubtitleCredentials`** (`MediaButler/Settings/SubtitleCredentials.cs`) — OpenSubtitles
  user/password resolved via the Vault chain; `IsComplete` gates whether creds are passed.
- **`PipelineReport`** (`MediaButler/Pipeline/PipelineReport.cs`) — running tallies (`Renamed`,
  `Hoisted`, `Consolidated`, `PackSplit`, `MergedFiles`, `EmptyDeleted`, `TvMoved`, `MoviesMoved`,
  `MusicMoved`, `Errors`, `NeedsManual`, ...) that produce the consolidated summary.
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
  (`MoviePack` → one folder per film) + loose-movie wrapping + duplicate-season merge.
- **`SeasonMerger`** (`MediaButler/Pipeline/SeasonMerger.cs`) — episode-aware file-level merge into
  an existing canonical season folder + sample-aware shell cleanup (shared by Rename and Move).
- **`FileBotStage`** (`MediaButler/Pipeline/FileBotStage.cs`) — drives `filebot.exe` for TV, movies,
  subtitles, and artwork via `FileBotClient` (`MediaButler/FileBot/FileBotClient.cs`).
- **`MoveStage.Run()`** (`MediaButler/Pipeline/MoveStage.cs`) — cross-volume move to Plex layout,
  `SanitizeForFs`, show-art hoist, destination-side season merge, music move-as-is.
- **`RelocateStage.Run()`** (`MediaButler/Pipeline/RelocateStage.cs`) — destination eviction.
- **`LegionFallbackParser.ClassifyAsync()` / `ClassifyFileAsync()`**
  (`MediaButler/Llm/LegionFallbackParser.cs`) — LLM long-tail for folders and unmatched files.
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

### MB-LAW-9 — Merge, never overwrite; duplicates are a human decision {#MB-LAW-9}
When a season's canonical target already exists (source-side rename or destination-side move),
files merge individually. A file whose NAME or PARSED EPISODE already exists at the target is
left behind and flagged (exit 2) — MediaButler never silently overwrites or double-files an
episode. Emptied shells are deleted only under the sample-aware guard: every remaining video must
be sample-named and at most `SampleMaxBytes`, with other junk at most `EmptyDeleteSafetyBytes`.
Sample clips never travel to the library. (Verified by
`True_duplicate_rips_stay_behind_and_are_flagged_for_a_human`,
`Junk_and_sample_shells_are_cleaned_up_after_consolidation`,
`Reruns_never_touch_destinations_and_sources_converge_to_a_steady_state`.)

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
> Build/test evidence recorded 2026-06-12 — see [#MB-§8](#MB-§8) for the bar.

- **Build (2026-06-12):** `dotnet build MediaButler/MediaButler.csproj` — **succeeded, 0 warnings,
  0 errors** (net10.0-windows, assembly version 2.0.0; references MindAttic.Vault +
  MindAttic.Legion resolved cleanly).
- **Core tests (2026-06-12):** `dotnet test MediaButler.Tests/MediaButler.Tests.csproj` — **Passed:
  196, Failed: 0, Skipped: 0** (NUnit). Suite covers `NameParserTests`, `EpisodeParsingTests`,
  `VariationCatalogTests`, `MediaScannerTests`, `RenameStageTests`, `MoveStageTests`,
  `RelocateStageTests`, `PathGuardTests`, `SubtitleCredentialsTests`, `FileBotClientTests`,
  `CliEndToEndTests`, `PathologicalLibraryPipelineTests`, `RealWorldLibraryPipelineTests`.
- **Real-disk verification (2026-06-12):** `mediabutler scan` over the four real inboxes
  (`M:\Torrents`, `M:\Torrents\temp`, `D:\Downloads`, `D:\Downloads\temp`) classified **all 54
  top-level items with zero Unknown**; a full `run --dry-run` exercised FileBot `--action TEST`
  end-to-end (TheTVDB SSL failures on some lookups were surfaced per-folder and did not abort the
  pipeline).
- **Proven working (✅):** everything from the 2026-06-07 list, plus: per-episode folder
  consolidation, movie-pack splitting, loose-file wrapping, flat-collection season filing,
  content-only TV detection, episode-aware duplicate merge, sample/junk shell cleanup,
  multi-source + recursive processing, music detection (audio/catalog), the variation catalog
  (seeded, growing, pinning, corruption-safe).
- **Partial (🟡):** the MAUI shell (`MediaButler.Maui`) and its UI smoke tests run only on
  Windows desktop and are not part of the headless `MediaButler.Tests` gate; treated as 🟡 until
  proven in this environment. Live FileBot/OpenSubtitles/LLM paths require external binaries and
  credentials and are exercised by construction tests, not live integration (the Legion fallback
  for folders AND unmatched files is implemented but has no mocked-transport test yet).
  `LandingPageTests` require Playwright browser binaries (`playwright.ps1 install chromium`) and
  skip gracefully when absent — treated as 🟡 in headless CI until binaries are provisioned.

## 7. Active frontier {#MB-§7}
- See `docs/rfc/` for open design notes.
- See `docs/USER_STORIES.md` for the epic breakdown and priority backlog.
- Known frontier items: broaden `TitleYearOverrides` coverage as new year-in-title movies land;
  promote the MAUI shell from 🟡 to ✅ with an environment that can run it; live-integration
  harness for FileBot and OpenSubtitles; mocked-Legion tests so MB-US-F1/F2 can graduate to ✅;
  date-based TV (`Daily.Show.2024.01.15`) and anime absolute numbering (`[Group] Show - 01`)
  are cataloged in `MasterVariations` but not yet auto-converted by the regex pipeline.

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
- **Hoist** — lift nested `Season N` subfolders (or show-level artwork) up one level.
- **Consolidate** — file a per-episode dump or loose episode file into its `{Show} - Season XX`.
- **Pack split** — break a multi-movie folder into one `{Title} (YYYY)` folder per film.
- **Merge** — file-level union of a duplicate season into the existing canonical folder;
  episode collisions stay behind for a human ([#MB-LAW-9](#MB-LAW-9)).
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

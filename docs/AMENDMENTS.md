---
codex: 1
project: MediaButler
code: MB
layer: amendments
status: living
updated: 2026-06-13
---

# MediaButler — Amendments (append-only; amendment wins over the bible)

> Append-only log. Never rewrite an amendment; supersede it with a new one. When this passes
> ~25 entries, fold the settled ones into `docs/BIBLE.md` and start a new epoch (note the git tag).

## MB-A1 — Adopt the Codex documentation standard (supersedes —)
Established the canonical docs layout (`docs/BIBLE.md`, `docs/USER_STORIES.md`,
`docs/AMENDMENTS.md`, `docs/rfc/`), the `tools/codex.ps1` doctor/digest CLI, and the
`SessionStart` digest-injection hook. The BIBLE inherits org-wide laws from
[MindAttic.HouseRules.md](../../MindAttic.HouseRules.md) by reference and adds eight
project-specific laws ([MB-LAW-1](BIBLE.md#MB-LAW-1)..[MB-LAW-8](BIBLE.md#MB-LAW-8)). No
application or source code was changed. Migration: none — there were no pre-existing canon docs in
the repo; `README.md` remains the build/run reference and is now cross-linked from the BIBLE.

## MB-A2 — Codex full-sync: drift correction + verified state populated (supersedes §4.2, §6)
Reconciled BIBLE §4.2 `PipelineReport` field names against actual source: corrected `Moved` to
`TvMoved`/`MoviesMoved` (the real split-by-kind fields in the struct). Populated BIBLE §6 with
real build/test evidence dated 2026-06-07: `dotnet build MediaButler/MediaButler.csproj` —
succeeded, 0 warnings, 0 errors; `dotnet test MediaButler.Tests/MediaButler.Tests.csproj` —
Passed: 150, Failed: 0, Skipped: 0. Added note that `LandingPageTests` require Playwright browser
binaries and skip gracefully in headless CI. No application or source code was changed.

## MB-A3 — Real-inbox dataset: new kinds, merge semantics, variation catalog, multi-source, music (supersedes parts of §3, §4, MB-US-B3 wording)
Driven by a full inventory of the real inboxes (`M:\Torrents`, `M:\Torrents\temp`, `D:\Downloads`,
`D:\Downloads\temp`, 2026-06-12) and captured as the `RealWorldLibrary` test fixture. Changes:

1. **New kinds.** `TvEpisode` (per-episode torrent folders like `Ahsoka.S01E01...[TGx]` and loose
   episode files — consolidated into `{Show} - Season XX`), `MoviePack` (multi-movie folders like
   `The Matrix 1-4 Pack ...` — split into one `{Title} (YYYY)` per film), and `Music`.
2. **Loose root files.** The scanner now classifies loose VIDEO files at a source root (wrapped
   into canonical folders by the Rename stage); dotfiles (`.parts` partials) and sample clips are
   skipped entirely.
3. **Episode parsing.** `NameParser.ParseEpisode` handles `SxxEyy` (+`E21E22`/`E25-26` spans,
   `S01 - E01` split form), `3x09`, dotted `1.09`, and (season-context only) `Episode 05` and
   scene codes (`criminal.minds.202`). "The Complete Collection" now counts as a multi-season
   marker, and flat collection dumps file their loose episodes into per-season folders — this
   REPLACES the old "loose video stays at the parent" behaviour for parseable episodes (the
   unparseable case still stays + flags). Content-only TV detection: a year-less folder whose
   videos all parse as episodes classifies as TV (`Battletech`).
4. **Merge instead of dead-end.** When a season's canonical target already exists (source-side
   rename or destination-side move), files merge individually; a file whose NAME or PARSED EPISODE
   already exists at the target stays behind and is flagged (exit 2) — duplicate rips are a human
   decision, never a silent overwrite. Emptied shells are deleted under a sample-aware guard:
   sample-named videos ≤ `SampleMaxBytes` (default 300 MB) don't block deletion (refines
   [MB-LAW-4](BIBLE.md#MB-LAW-4); MB-LAW-1/2/3 unchanged — re-runs converge: destinations are
   invariant after run 1, the whole tree is a strict no-op from run 2).
5. **Variation catalog.** Every scan appends newly-seen names to
   `%APPDATA%\MindAttic\MediaButler\variations.json`, sectioned `movie`/`tv`/`music`/`unknown`.
   The file is created as a clone of the hardcoded `MasterVariations` list (compiled from scene
   rules, Plex/Jellyfin/Kodi/TRaSH/FileBot docs, anime fansub conventions, and the real-inbox
   inventory) and is user-appendable: moving an entry into `movie`/`tv`/`music` pins that name's
   category on subsequent runs. A corrupted file disables saving (user edits are never clobbered).
   Override path: `VariationCatalogPath` setting or `MEDIABUTLER_VARIATIONS_PATH` env var.
6. **Multi-source + recursive.** `ExtraSources` (settings) and repeatable `--source` process many
   inboxes per run; `Recursive`/`--recursive` additionally processes excluded container subfolders
   (`temp`, `incomplete`, ...) as inboxes. Exit codes combine by severity (1 > 2 > 0).
7. **Music.** Audio-only folders (per `AudioExtensions`) and catalog-pinned names classify
   `Music` — never renamed/restructured, moved AS-IS to `MusicDestination` (`--music-dest`) when
   configured, otherwise left in place and flagged. `PathGuard` covers the music destination.
8. **CLI.** New flags: `--live` (overrides persisted DryRun; `--dry-run` still wins),
   `--subtitles` (force-enable per run), `-r|--recursive`, `--music-dest|--musicDest`, and
   camelCase aliases `--tvDest`/`--moviesDest`/`--movieDest`.

Verified by `RealWorldLibraryPipelineTests.*`, `EpisodeParsingTests.*`, `VariationCatalogTests.*`
(196 tests passing, 2026-06-12).

## MB-A4 — MovieCollection: classify and hoist collection husks (supersedes §4.1)

Adds `MediaKind.MovieCollection` to the scanner and pipeline.

**Problem.** A folder like `Studio.Ghibli/` holding `Spirited.Away.2001/`, `Howl's.Moving.Castle.2004/`, etc. is a "collection husk" — it contains no top-level video files, only movie sub-directories. The scanner previously classified it as `Movie` (title "Studio Ghibli", year null) and FileBot failed with exit 3 (no database match). The sub-folders were never processed.

**Classification rule.** A folder with no top-level video files whose ≥ 2 direct sub-directories each (a) parse with a release year via `NameParser.ParseMovie` and (b) contain at least one video file is classified as `MediaKind.MovieCollection`. Detection occurs before `ClassifyMoviePath` in `MediaScanner.ClassifyByRegex`.

**Pipeline behaviour.**
- `RenameStage.ProcessItem`: calls `HoistMovieCollection` — moves each parseable sub-dir to the source root as `{Title} (YYYY)/`, then deletes the now-empty husk. Sub-dirs without a parseable year are flagged for manual review.
- `RenameStage.HoistMovieCollections` (public): pre-pass entry point for `FileBotStage.RunMovies` — scans for all `MovieCollection` items and hoists them so FileBot can process each film individually.
- `FileBotStage.RunMovies`: calls `new RenameStage(settings, report).HoistMovieCollections()` before the main scan, ensuring the `filebot-movies` standalone command also handles collection husks.
- `PipelineReport.CollectionHoisted`: counter incremented per hoisted sub-dir; displayed in the pipeline summary as `Collection hoist`.

**Test isolation fix.** `MediaScannerTests.SettingsFor` and `RenameStageTests.SettingsFor` now generate a per-test `VariationCatalogPath` (temp GUID file) to prevent catalog cross-contamination between tests that reuse the same folder names.

Verified by `Collection_husk_with_two_year_folders_classifies_as_MovieCollection`,
`MovieCollection_hoist_moves_sub_folders_to_source_root_and_deletes_husk`,
`MovieCollection_hoist_dry_run_leaves_disk_untouched_but_counts_hoisted`,
`HoistMovieCollections_pre_pass_hoists_collection_and_leaves_normal_movies_untouched`
(207 tests passing, 2026-06-13).

## MB-A5 — Dry-run counts a FileBot TEST pass as success (refines MB-LAW-1/MB-LAW-8)

**Problem.** FileBot (verified on 5.2.1) exits **1** for `--action TEST` even when every file
matched — it prints one `[TEST] from [a] to [b]` plan line per file plus `Processed N files`,
and returns non-zero because nothing was actually renamed on disk. MediaButler treated any
non-zero, non-no-op exit as a hard error, so every dry-run over well-formed media reported
false `FileBot rename exit 1` errors and returned exit code 1 instead of 0/2 — a cron job
could not tell a healthy dry-run from a broken one.

**Fix.** `FileBotResult.LooksLikeTestPass` is true when the output carries `[TEST] from [`
plan lines and is not a `Processed 0 files` no-op. `FileBotStage.RecordFileBotOutcome`
treats it as success (`[dry rename ok]`, counters bump) **in dry-run only** — a live MOVE
never emits `[TEST]` lines, so live failures still surface as errors. Folder-name sync after
rename remains gated on real success/no-op and stays inert in dry-run (MB-LAW-1 intact).

Also from the 2026-07-04 live-inbox pass: `TrailingJunk` gains the real-inbox forms
`DCPRiP` (mixed-case; the list is deliberately case-sensitive, each observed form is added
explicitly) and `UNRATED`; the real-world corpus grows by the nine 2026-07-04 `M:\Torrents`
items (dotted release-group names like `MP4-BEN.THE.MEN`, `HDR10+`/`MULTi.FRE.LAT` tag runs,
roman-numeral and numeric sequels, a TV season dropped into a movie batch).

Verified by `LooksLikeTestPass_detects_dry_run_plan_lines_despite_exit_1`,
`LooksLikeTestPass_is_false_when_nothing_was_processed`,
`LooksLikeTestPass_is_false_for_real_failures`,
`ParseMovie_strips_new_junk_tags_when_no_year_anchors_the_title`,
`Scanner_classifies_every_real_world_variation_as_expected`
(219 tests passing, 2026-07-04).

## MB-A6 — Duplicate-movie policy and the MCP front door (refines MB-LAW-9; extends HOUSE-LAW-6)

**Duplicate-movie policy.** MB-LAW-9's "duplicates are a human decision" default proved wrong for
movies in practice (2026-07-04: a 29.6 GB PROPER arrived for a film whose 11.5 GB copy was already
filed, and the pipeline parked it in the inbox). New setting `duplicateMovieAction`:

- **`KeepLargest` (default).** When a movie's destination folder already has content, the copy
  with the larger primary video (largest non-sample video file) wins. Incoming larger → the
  destination's video files are deleted and the incoming folder merges in (existing artwork
  survives; non-video name collisions keep the destination's copy). Incoming smaller or equal →
  the incoming folder is deleted. Both directions audit-log the loser (`duplicate-replace` /
  `duplicate-discard`). With **no comparable video on either side, it always falls back to
  flagging** — a wrong guess destroys media. Dry-run logs the decision and mutates nothing.
- **`Flag`.** The classic MB-LAW-9 behaviour: leave both copies, surface needs-manual (exit 2).

CLI: `--duplicates keep-largest|flag` on every pipeline command overlays the persisted setting.
Episode/season duplicates are UNCHANGED — MB-LAW-9's merge-and-flag contract still governs TV.

**MCP front door.** `mediabutler mcp` serves the Model Context Protocol over stdio
(newline-delimited JSON-RPC 2.0): tools `scan` (read-only classification of the inboxes, JSON per
item), `status` (config snapshot), `run` (full pipeline; **dryRun=true by default** — an agent
must pass `dryRun=false` explicitly to mutate, and MB-LAW-1 governs as usual). One engine, many
front doors (HOUSE-LAW-6): the MCP layer dispatches into the same `PipelineRunner`/`MediaScanner`
as the CLI and menu. stdout carries protocol frames only; pipeline narration is rebound to stderr
and returned inside tool results. Register with `claude mcp add mediabutler -- mediabutler mcp`.

Verified by `DuplicateMovieActionTests.*` and `McpServerTests.*` (2026-07-04).

## MB-A7 — Reboot/same-name TV disambiguation (extends MB-LAW-4)

**Problem.** When a TV series is rebooted under the same name (e.g. *Little House on the Prairie* 2026 landing
alongside the 1974 original), the pipeline routes both to the same destination folder
`M:\TV\Little House on the Prairie\Season 01`, treating new episodes as duplicates of the old.

**Fix.** Three-part change:

1. **Year parsed from source folder name.** `NameParser.ParseSingleSeason` now extracts the
   series-premiere year from the show-name segment (before the season marker) and returns it as
   `int? Year` in the tuple. `MediaItem` gains `TvYear` (`int?`) populated at scan time.
   Pre-strip: the ` - ` separator that MediaButler itself inserts is removed before year extraction
   so that `{Show} (YYYY) - Season NN` round-trips correctly.

2. **Year threaded through the staging folder name.** `NameParser.FormatSeasonFolder` gains an
   optional `int? year` parameter. When set, the staging name becomes `{Show} ({Year}) - Season NN`
   instead of `{Show} - Season NN`. `RenameStage` passes `item.TvYear` here; `FileBotStage.
   SyncTvFolderToEpisodes` recovers the year from the current folder name before recomputing the
   canonical name, so the year survives the FileBot rename pass.

3. **Disambiguation-aware routing in MoveStage.** A show is considered disambiguated when the TV
   destination already contains at least one `ShowName (YYYY)` folder. Only then does `MoveStage`
   route to `{ShowName} ({TvYear})\Season NN`; otherwise it uses the bare `{ShowName}` path
   (preserving existing behaviour for shows whose year happens to appear in the source dump name).
   A warning is logged if the bare folder still exists alongside year-tagged content.

**Workflow.** When a reboot arrives that shares a name with an existing library entry: rename the
existing folder from `M:\TV\ShowName` to `M:\TV\ShowName (YEAR)` (one manual step). All subsequent
pipeline runs for that show name auto-route to year-tagged destinations.

Verified by `ParseSingleSeason_extracts_tv_year`, `FormatSeasonFolder_round_trips_through_ParseSingleSeason`
(with year cases), and the full `RealWorldLibraryPipelineTests` suite (245 tests passing, 2026-07-17).

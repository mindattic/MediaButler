---
codex: 1
project: MediaButler
code: MB
layer: stories
status: living
updated: 2026-06-07
---

# MediaButler — User Stories
> ✅ done (shipped & tested) · 🟡 partial · ⬜ planned · 🗑️ cut. Every ✅ cites the test.

## Epic A — Classify a messy library
- **MB-US-A1 ✅** As an operator, I can point MediaButler at a folder of dumps and have each
  top-level folder classified (Movie / TvSeason / MultiSeasonParent / Empty / Extras / Unknown),
  so I know what the pipeline will do. *Given a pathological library, When I scan, Then every
  input folder is classified as expected.* *(verified by
  `Scanner_classifies_every_pathological_case_as_expected`, `MediaScannerTests.*`.)*
- **MB-US-A2 ✅** As an operator, dirty release names are cleaned to FileBot-friendly stems —
  index/group prefixes stripped, separators normalised, season/movie shapes recognised — so the
  long tail of weird names survives. *(verified by
  `ParseSingleSeason_extracts_show_and_season`, `ParseMovie_extracts_title_and_year`,
  `ParseMultiSeasonParent_recovers_show_name`,
  `Normalize_collapses_separators_and_strips_index_prefix`, `LooksLikeMultiSeason_detects_range_or_complete_series`.)*
- **MB-US-A3 ✅** As an operator, year-in-title movies (`Blade Runner 2049`, `Wonder Woman 1984`,
  `1917`) and year-prefixed titles (`1917 (2019)`) keep their titles, so the year isn't eaten as a
  release year. *(verified by
  `ParseMovie_prefers_parenthesised_year_over_bare_leading_year`,
  `ParseMovie_respects_title_year_overrides`.)*
- **MB-US-A4 ✅** As an operator, `Extras`/`Specials`/`Bonus` are recognised as companion content,
  not movies, so they're never reorganised. *(verified by
  `Extras_subfolder_at_root_is_classified_as_Extras`,
  `Extras_folder_without_video_is_classified_Extras_not_Empty`.)* See [#MB-LAW-4](BIBLE.md#MB-LAW-4).

## Epic B — Rename & canonicalize (local)
- **MB-US-B1 ✅** As an operator, a live rename produces the canonical folder name
  (`Better Call Saul - Season 05`, `Heat (1995)`). *(verified by
  `Live_rename_produces_the_canonical_folder_name`.)*
- **MB-US-B2 ✅** As an operator, re-running on an already-canonical library is a no-op, so the
  pipeline is idempotent. *(verified by `Idempotent_run_no_ops_when_folder_already_canonical`,
  `Pipeline_re_runs_are_idempotent_on_an_already_organized_library`.)* See [#MB-LAW-2](BIBLE.md#MB-LAW-2).
- **MB-US-B3 ✅** As an operator, multi-season parents have their seasons hoisted and counted, and
  loose video at the parent is not misfiled into a season. *(verified by
  `Multi_season_parent_hoists_seasons_and_records_count`,
  `Loose_video_at_multi_season_parent_is_not_misfiled_into_a_season`.)*
- **MB-US-B4 ✅** As an operator, an empty disguised folder is deleted, but only below the byte
  safety floor — a large folder with an unknown video extension is surfaced for manual review
  instead. *(verified by `Empty_disguised_folder_is_deleted`,
  `Empty_size_guard_refuses_to_delete_a_folder_that_exceeds_the_threshold`.)* See [#MB-LAW-4](BIBLE.md#MB-LAW-4).

## Epic C — FileBot enrichment
- **MB-US-C1 ✅** As an operator, TV is renamed via TheTVDB and movies via TheMovieDB with the
  correct FileBot args (MOVE live, TEST in dry-run). *(verified by
  `BuildRenameTvArgs_uses_MOVE_in_live_mode`, `BuildRenameTvArgs_uses_TEST_in_dry_run`,
  `BuildRenameMovieArgs_uses_TheMovieDB_and_year_format`.)*
- **MB-US-C2 ✅** As an operator, artwork is fetched with the right scripts (`fn:artwork.tvdb` for
  TV, generic `fn:artwork` for movies, working around the 5.2.1 `artwork.tmdb` bug). *(verified by
  `BuildFetchTvArtworkArgs_invokes_artwork_tvdb_script`,
  `BuildFetchMovieArtworkArgs_uses_generic_artwork_script`.)*
- **MB-US-C3 ✅** As an operator, subtitle fetches pass OpenSubtitles creds by `@path` (never raw),
  omit them gracefully when absent, and report a 401 instead of crashing. *(verified by
  `BuildGetSubtitlesArgs_emits_at_path_references_not_raw_secrets`,
  `BuildGetSubtitlesArgs_omits_creds_when_files_missing`,
  `LooksLikeAuthFailure_detects_invalid_credentials_message`.)* See [#MB-LAW-7](BIBLE.md#MB-LAW-7).

## Epic D — Move to Plex layout
- **MB-US-D1 ✅** As an operator, renamed TV lands at `M:\TV\<Show>\Season XX\...` and movies at
  `M:\Movies\<Title> (YYYY)\...`. *(verified by
  `RenameThenMove_lands_every_TV_season_at_Plex_canonical_path`,
  `RenameThenMove_lands_every_movie_at_Plex_canonical_path`.)*
- **MB-US-D2 ✅** As an operator, cross-volume moves are detected and same-volume moves use a
  rename; illegal path characters are sanitized. *(verified by `IsCrossVolume_returns_false_for_same_drive`,
  `SafeMoveDirectory_renames_a_folder_when_target_is_on_the_same_drive`,
  `SanitizeForFs_*` cases in `MoveStageTests`.)*

## Epic E — Safety & operability
- **MB-US-E1 ✅** As an operator, dry-run prints `[dry: -> target]` and mutates nothing on disk.
  *(verified by `Dry_run_leaves_disk_untouched_but_still_counts_renames`,
  `DryRun_does_not_mutate_anything_on_disk`.)* See [#MB-LAW-1](BIBLE.md#MB-LAW-1).
- **MB-US-E2 ✅** As an operator, MediaButler refuses to run live when the source overlaps a
  destination (but sibling prefixes don't false-trigger). *(verified by
  `PathOverlaps_detects_identical_and_nested_paths`.)* See [#MB-LAW-3](BIBLE.md#MB-LAW-3).
- **MB-US-E3 ✅** As an operator, `relocate --source M:\Movies` evicts folders in the wrong library
  back to the correct destination. *(verified by
  `Relocate_evicts_a_TvSeason_dropped_into_the_movies_destination`,
  `TvSeason_living_in_MoviesDestination_is_relocated_to_TvDestination`,
  `Movie_living_in_TvDestination_is_relocated_to_MoviesDestination`.)* See [#MB-LAW-5](BIBLE.md#MB-LAW-5).
- **MB-US-E4 ✅** As a cron job, I get exit `0`/`1`/`2` and can treat `2` (needs-manual) as
  actionable. *(verified by `Pipeline_returns_NeedsManual_exit_code_when_only_extras_remain`,
  `Version_subcommand_prints_version_and_exits_zero`, `Unknown_subcommand_returns_nonzero`.)*
  See [#MB-LAW-8](BIBLE.md#MB-LAW-8).
- **MB-US-E5 ✅** As an operator, `mediabutler --version`/`-v` (any argv position) prints the
  version and exits zero. *(verified by `Bare_double_dash_version_resolves_to_version_subcommand`,
  `Short_dash_v_resolves_to_version_subcommand`,
  `Dash_v_in_any_argv_position_still_resolves_to_version`.)*

## Epic F — LLM long tail
- **MB-US-F1 🟡** As an operator, I can enable an LLM fallback so unclassifiable folders get a
  best-guess from a configured Legion provider, off by default. The fallback's behaviour
  (opt-in, non-fatal, JSON extraction) is implemented; there is no test that drives a live or
  mocked Legion call yet — downgraded to 🟡 until one exists. See [#MB-LAW-6](BIBLE.md#MB-LAW-6).

## Epic G — GUI shell
- **MB-US-G1 🟡** As a desktop user, I can drive the pipeline from a MAUI window with a live log
  and a dry-run toggle. Smoke tests exist (`MauiAppSmokeTests`) but run only on Windows desktop
  and are outside the headless `MediaButler.Tests` gate — 🟡 until proven in this environment.

## Epic H — Landing page
- **MB-US-H1 ✅** As a maintainer, the `README.md` renders into the published landing page so the
  marketing copy and the docs stay in sync. *(verified by `LandingPageTests`.)*

## Priority backlog
1. **MB-US-F1** — add a mocked/recorded Legion test so the LLM fallback can graduate to ✅.
2. **MB-US-G1** — wire a CI/desktop runner that executes `MauiAppSmokeTests`, promoting the shell to ✅.
3. Live-integration harness for FileBot + OpenSubtitles (currently arg-construction tests only).
4. Expand `TitleYearOverrides` coverage as new year-in-title releases appear (see
   [docs/rfc/0001-llm-fallback-test-strategy.md](rfc/0001-llm-fallback-test-strategy.md)).

### Audit log
(No stories have been changed from their original spec yet. When a story's ask changes, the
original wording is preserved here verbatim, marked "(original spec — audit log)".)

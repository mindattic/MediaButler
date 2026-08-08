---
codex: 1
project: MediaButler
code: MB
layer: stories
status: living
updated: 2026-07-04
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
- **MB-US-A5 ✅** As an operator, a "collection husk" folder (e.g. `Studio.Ghibli/` holding
  `Spirited.Away.2001/`, `Howl's.Moving.Castle.2004/`) is recognised as `MovieCollection` and NOT
  mis-classified as a movie, so FileBot is never asked to match a studio name against a movie
  database and fail. *(verified by `Collection_husk_with_two_year_folders_classifies_as_MovieCollection`,
  `Collection_husk_with_only_one_year_sub_dir_does_not_classify_as_MovieCollection`,
  `Collection_husk_sub_dirs_without_video_do_not_count_toward_collection`.)* See [MB-A4](AMENDMENTS.md).

## Epic B — Rename & canonicalize (local)
- **MB-US-B1 ✅** As an operator, a live rename produces the canonical folder name
  (`Better Call Saul - Season 05`, `Heat (1995)`). *(verified by
  `Live_rename_produces_the_canonical_folder_name`.)*
- **MB-US-B2 ✅** As an operator, re-running on an already-canonical library is a no-op, so the
  pipeline is idempotent. *(verified by `Idempotent_run_no_ops_when_folder_already_canonical`,
  `Pipeline_re_runs_are_idempotent_on_an_already_organized_library`.)* See [#MB-LAW-2](BIBLE.md#MB-LAW-2).
- **MB-US-B3 ✅** As an operator, multi-season parents have their seasons hoisted and counted;
  a loose episode at the parent is filed into its OWN season (never a wrong one), and an
  unparseable loose video stays put and is flagged. *(verified by
  `Multi_season_parent_hoists_seasons_and_records_count`,
  `Loose_episode_at_multi_season_parent_is_filed_into_its_OWN_season`,
  `Unparseable_loose_video_at_multi_season_parent_stays_and_is_flagged`.)*
  Reworded per MB-A3 in [AMENDMENTS.md](AMENDMENTS.md); original in the audit log.
- **MB-US-B5 ✅** As an operator, each movie sub-folder inside a collection husk is hoisted to the
  source root (renamed to `{Title} (YYYY)/`), the husk is deleted, and each hoisted folder is then
  processed by FileBot individually — so films like "Spirited Away" and "Howl's Moving Castle" in a
  `Studio.Ghibli/` husk are renamed, receive artwork, and have their folder names synced. *(verified
  by `MovieCollection_hoist_moves_sub_folders_to_source_root_and_deletes_husk`,
  `MovieCollection_hoist_dry_run_leaves_disk_untouched_but_counts_hoisted`,
  `HoistMovieCollections_pre_pass_hoists_collection_and_leaves_normal_movies_untouched`.)* See [MB-A4](AMENDMENTS.md).
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
- **MB-US-D3 ✅** As an operator, when a movie's destination folder already has content, the
  duplicate is auto-resolved by the `duplicateMovieAction` policy: `KeepLargest` (default) keeps
  whichever copy has the larger primary video and deletes the other (audit-logged; existing
  artwork survives a replacement); `flag` restores the classic leave-both-and-ask behaviour; and
  with no comparable video on either side it always falls back to flagging. Overridable per run
  via `--duplicates keep-largest|flag`. *(verified by
  `KeepLargest_incoming_larger_replaces_the_destination_video_and_keeps_artwork`,
  `KeepLargest_incoming_smaller_is_discarded_and_the_destination_untouched`,
  `Flag_leaves_both_copies_and_surfaces_needs_manual`,
  `KeepLargest_without_a_comparable_video_falls_back_to_flagging`,
  `KeepLargest_dry_run_mutates_nothing_in_either_direction`,
  `Duplicates_cli_flag_overlays_the_persisted_setting`.)* See [MB-A6](AMENDMENTS.md).

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
- **MB-US-E6 ✅** As a cron job, a dry-run over well-formed media exits clean: FileBot's
  exit-1-on-`--action TEST` quirk is recognised as a pass (the `[TEST]` plan lines are the
  success signal), so false errors never mask real ones. *(verified by
  `LooksLikeTestPass_detects_dry_run_plan_lines_despite_exit_1`,
  `LooksLikeTestPass_is_false_when_nothing_was_processed`,
  `LooksLikeTestPass_is_false_for_real_failures`.)* See [MB-A5](AMENDMENTS.md).
- **MB-US-E7 ✅** As an agent host (Claude Code, Claude Desktop), I can drive MediaButler over
  the Model Context Protocol: `mediabutler mcp` serves stdio JSON-RPC with `scan` (read-only
  classification), `status` (config snapshot), and `run` (pipeline; dry-run by default, mutation
  only on explicit `dryRun=false`) — the same engine as the CLI and menu (HOUSE-LAW-6).
  *(verified by `Initialize_reports_server_info_and_tools_capability`,
  `ToolsList_exposes_scan_status_run_with_safe_run_default`,
  `Scan_tool_classifies_a_movie_folder`,
  `Run_tool_defaults_to_dry_run_and_mutates_nothing`,
  `Unknown_tool_reports_isError_instead_of_a_protocol_fault`.)* See [MB-A6](AMENDMENTS.md).

## Epic I — Real-inbox conversion contract (MB-A3)
- **MB-US-I1 ✅** As an operator, every naming variation inventoried from my real inboxes
  (scene-dotted movies, YTS brackets, duplicated years, per-episode folders, flat complete
  collections, website prefixes, `3x09`/`1.09`/`Episode 05`/scene-code episode files, multi-movie
  packs, loose root files) classifies and converts to its Plex-canonical target. *(verified by
  `Scanner_classifies_every_real_world_variation_as_expected`,
  `Full_local_pipeline_lands_every_tv_season_at_plex_canonical_paths`,
  `Full_local_pipeline_lands_every_movie_at_plex_canonical_paths`,
  `ParseEpisode_handles_every_real_world_marker_shape`.)*
- **MB-US-I2 ✅** As an operator, per-episode torrent folders are consolidated into one
  `{Show} - Season XX` folder, sample clips and nfo/txt junk are cleaned up, and no sample ever
  reaches the library. *(verified by `Junk_and_sample_shells_are_cleaned_up_after_consolidation`.)*
- **MB-US-I3 ✅** As an operator, multi-movie packs split into one canonical folder per film.
  *(verified by `Matrix_pack_is_split_into_four_distinct_movies`.)*
- **MB-US-I4 ✅** As an operator, duplicate rips of the same episodes merge when complementary; when
  they truly collide (same name or parsed episode), the `duplicateEpisodeAction` policy resolves
  it: `KeepLargest` (default) keeps whichever copy has the larger video and deletes the other
  (audit-logged, mirroring movies' `duplicateMovieAction`); `flag` restores the classic
  leave-both-and-ask behaviour (exit 2). Never silently overwritten or double-filed either way.
  Overridable per run via `--tv-duplicates keep-largest|flag`. *(verified by
  `DuplicateEpisodeActionTests.*`,
  `True_duplicate_rips_stay_behind_and_are_flagged_for_a_human` (Flag path),
  `ParseEpisodeNumberInSeason_resolves_context_only_shapes`.)* See [MB-A9](AMENDMENTS.md).
- **MB-US-I5 ✅** As an operator, partial-download dotfiles (`.parts`) are never touched.
  *(verified by `Dot_parts_partial_download_file_is_ignored_entirely`.)*
- **MB-US-I6 ✅** As an operator, re-runs never touch the destinations and the whole tree reaches
  a strict no-op steady state. *(verified by
  `Reruns_never_touch_destinations_and_sources_converge_to_a_steady_state`,
  `Dry_run_over_all_sources_mutates_nothing_anywhere`.)*

## Epic J — Variation catalog & multi-source
- **MB-US-J1 ✅** As an operator, every run grows a persistent, hand-editable variation catalog at
  `%APPDATA%\MindAttic\MediaButler\variations.json` (sections: movie/tv/music/unknown), seeded as
  a clone of the hardcoded master list; moving an entry between sections pins its category, and a
  corrupted file is never overwritten. *(verified by
  `Records_classified_names_into_sections_and_persists`,
  `Hand_edited_sections_pin_classification_hints`,
  `Corrupted_file_disables_saving_so_user_edits_survive`,
  `Recording_a_name_twice_does_not_duplicate_it`.)*
- **MB-US-J2 ✅** As an operator, music is detected (audio-only folders, or catalog pins) and is
  never deleted as "empty" nor renamed — it moves as-is to MusicDestination when configured.
  *(verified by `Audio_only_folder_is_detected_as_music_without_any_pin`,
  `Scanner_consults_music_pins_so_a_music_folder_is_not_deleted_as_empty`.)*
- **MB-US-J3 ✅** As an operator, one CLI call converts ALL my inboxes:
  `mediabutler run --source M:\Torrents --source D:\Downloads --recursive --tv-dest M:\TV
  --movies-dest M:\Movies --music-dest M:\Music --subtitles --live` — flat collections, loose
  episodes, and every season land correctly across sources. *(verified by
  `Killing_Eve_flat_collection_carries_parsed_loose_episodes`,
  `Full_local_pipeline_lands_every_tv_season_at_plex_canonical_paths`.)*

## Epic F — LLM long tail
- **MB-US-F1 🟡** As an operator, I can enable an LLM fallback so unclassifiable folders get a
  best-guess from a configured Legion provider, off by default. The fallback's behaviour
  (opt-in, non-fatal, JSON extraction) is implemented; there is no test that drives a live or
  mocked Legion call yet — downgraded to 🟡 until one exists. See [#MB-LAW-6](BIBLE.md#MB-LAW-6).
- **MB-US-F2 🟡** As an operator, loose FILES that match no known pattern (no year, no episode
  marker) get a Legion best-guess (`ClassifyFileAsync`: movie → wrapped as `{Title} (YYYY)`,
  episode → consolidated into `{Show} - Season XX`), opt-in via the same `EnableLlmFallback`
  switch and non-fatal on any failure. Implemented in `MediaScanner.TryLlmClassifyFileAsync`;
  🟡 for the same reason as MB-US-F1 (no mocked Legion test yet). See [#MB-LAW-6](BIBLE.md#MB-LAW-6).

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
- **MB-US-B3 (original spec — audit log, superseded by MB-A3 in [AMENDMENTS.md](AMENDMENTS.md)):** "As an
  operator, multi-season parents have their seasons hoisted and counted, and loose video at the
  parent is not misfiled into a season. *(verified by
  `Multi_season_parent_hoists_seasons_and_records_count`,
  `Loose_video_at_multi_season_parent_is_not_misfiled_into_a_season`.)*" — the loose-video rule
  evolved: parseable episodes are now filed into their OWN season; only unparseable ones stay.

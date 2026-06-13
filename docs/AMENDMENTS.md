---
codex: 1
project: MediaButler
code: MB
layer: amendments
status: living
updated: 2026-06-12
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

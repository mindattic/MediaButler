---
codex: 1
project: MediaButler
code: MB
layer: amendments
status: living
updated: 2026-08-07
---

# MediaButler — Amendments (append-only; amendment wins over the bible)

> Append-only log. Never rewrite an amendment; supersede it with a new one. When this passes
> ~25 entries, fold the settled ones into `docs/BIBLE.md` and start a new epoch (note the git tag).

> **Epoch note (2026-07-17):** MB-A1 through MB-A8 folded into `docs/BIBLE.md` at git tag
> `epoch/2026-07-17` (commit `0a0b967`). The bible is now the definitive state through MB-A8.
> New amendments start at MB-A9.

### MB-A9 — `duplicateEpisodeAction` (default `KeepLargest`): TV duplicates are now policy-resolved too {#MB-A9}

**Supersedes** the TV half of [MB-LAW-9](BIBLE.md#MB-LAW-9) ("duplicates are a human decision (TV)").

Born from the 2026-08-07 `M:\Torrents` run: "A Knight Of The Seven Kingdoms" S01 re-arrived and
every one of its 6 episodes collided with an already-filed copy, needing a manual pick for each —
exactly the toil `duplicateMovieAction: KeepLargest` (MB-A6) already eliminated for movies.

`SeasonMerger.MergeFiles` now takes a `DuplicateEpisodeAction` (same `DuplicateMovieAction` enum
as movies — `KeepLargest` | `Flag`). On a name-or-parsed-episode collision:

- `KeepLargest` (**new default**): the copy with the larger video file wins; the smaller one is
  deleted (audit-logged `duplicate-replace` / `duplicate-discard`, exactly like movies). Both sides
  must be a real video for the comparison — this only ever fires for `.mkv`/`.mp4`/etc., never for
  subtitle sidecars, which keep the old exact-name-only conflict check.
- `Flag`: restores the original MB-LAW-9 leave-both-and-ask behaviour.

Dry-run logs the decision and mutates nothing, matching the movie policy. CLI: `--tv-duplicates
keep-largest|flag`. Settings key: `duplicateEpisodeAction`.

Scope: this only covers the canonical destination-side merge (`MoveStage.MoveTvSeason` →
`SeasonMerger.MergeFiles`) — the same point where movies resolve theirs. The source-side raw-dump
mergers (`RenameStage.ConsolidateEpisode`, `RenameStage.HoistParent`'s flat-episode filing), which
compare pre-FileBot scene filenames by exact name only with no episode-number awareness, are
unchanged and still always flag.

(Verified by `DuplicateEpisodeActionTests.*`. The original MB-LAW-9 TV-human-decision behaviour is
still covered under `Flag` by `True_duplicate_rips_stay_behind_and_are_flagged_for_a_human`.)

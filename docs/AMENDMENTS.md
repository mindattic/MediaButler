---
codex: 1
project: MediaButler
code: MB
layer: amendments
status: living
updated: 2026-06-07
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

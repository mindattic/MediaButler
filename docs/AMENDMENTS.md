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

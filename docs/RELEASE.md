---
layer: release
status: living
---
# MediaButler — Release charter

> The one finish line this repo is working toward. Every open ticket in this repo sits under one of the epics
> below, and every epic is part of the milestone. Work that does not fit is out of scope until this milestone is
> reached (owner, 2026-11-27, MAT-T319).

## 1. What this is
A Windows tool (CLI, WPF shell and MCP server over one pipeline) that turns a folder of messy torrent dumps into a
Plex-ready TV and movie library, dry-run first, for the owner's own media library.

## 2. The milestone: MediaButler verified pipeline {#milestone}
- **Milestone:** MS-33, "MediaButler verified pipeline"
- **Published means:** an internal tool installed by `run.ps1` at `R:\MindAttic\Apps\MediaButler\{Debug,Release}`
  (`mediabutler.exe` plus `MediaButler.Wpf.exe`); source on GitHub.
- **The natural stopping point:** the pipeline already does everything the owner uses it for. What is missing is
  proof for the code that moves and deletes files: the audit log, the FileBot stage, the runner's orchestration,
  the CLI flag overlay, the variation catalog and the desktop shell's overlap guard have no direct tests. Once
  they do, and the version and bible say so, MediaButler is a finished tool that only changes when a new naming
  case turns up.

## 3. Done when (acceptance checks)
1. build — `MediaButler.slnx` builds clean on main.
2. exists — `R:\MindAttic\Apps\MediaButler\Release\mediabutler.exe` and `MediaButler.Wpf.exe`.
3. harness — `MediaButler.Tests` includes AuditLog, FileBotStage, PipelineRunner orchestration, BaseSettings
   overlay, VariationCatalog and MasterVariations regression tests.
4. signoff — the owner runs `mb --dry-run run` then a live run on `M:\Torrents` and the library comes out right.

## 4. Epics, in priority order
| Priority | Epic (ticket) | What it delivers | Features |
|---|---|---|---|
| 900 | MB-T62 Pipeline test coverage | Direct tests for the code that moves, renames and deletes files | MB-T64 Stage tests (990), MB-T65 Runner and CLI tests (980), MB-T66 Catalog regression tests (970) |
| 800 | MB-T63 Shell guard and release | WPF overlap guard tests, then 5.0.0 with BIBLE/README at the milestone state | MB-T67 Shell guard tests (890), MB-T68 Release docs (880) |

## 5. Out of scope (not before this milestone)
- Date-based TV (`Daily.Show.2024.01.15`) and anime absolute numbering (`[Group] Show - 01`) auto-conversion — cataloged
  in `MasterVariations`, not needed for the owner's library today.
- A live FileBot / OpenSubtitles integration harness and a CI desktop runner for the WPF UI and accessibility suites —
  both suites already pass on real hardware.
- More tests for cosmetic or display-only code: `Ui/Status` verbosity, `ConsoleCaptureWriter`, `DialogService` and the
  Run page bUnit tests (MB-T40, MB-T41, MB-T43, MB-T44).
- Open-ended loops: whole-suite reruns, repo-wide cleanups and exhaustive bughunts (MB-T15, MB-T16, MB-T49).
- Any new front door, downloader, metadata database or music organisation (see BIBLE §3).

## 6. Current verified state
- Pipeline, three front doors, duplicate policies, reboot-safe routing, variation catalog and MCP server are on main
  and covered by `MediaButler.Tests` (BIBLE §6).
- Per-run ID overrides survive `PipelineRunner.Clone` (f82b562, MB-T28).
- The shared run menu deploys the console app and WPF shell to `R:\MindAttic\Apps\MediaButler\{Debug,Release}`
  (10bfc7c, ef9f356); both exes are present there.

## 7. Decision record
- 2026-11-27 — charter drafted from README/BIBLE/RFC 0001/tickets; Legion vote: **Approve**, plurality quorum, 9/9 voters, consensus 0.78. Dissent: one voter wanted the live FileBot/OpenSubtitles harness in scope (kept out: both suites pass on real hardware and the owner has not asked for it).
- Triage: 22 open (excluding the job ticket MB-T61) → done 2 (MB-T11, MB-T60), duplicate 0, wont-do 13, kept 7; 1 new task (MB-T69).
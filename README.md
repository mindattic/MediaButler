# MediaButler

Automated media renamer and reorganizer. Takes a folder full of messy
torrent dumps, cleans the names locally, runs FileBot to grab episode titles
and artwork (and optionally subtitles), then moves everything into a
Plex-ready library layout.

Built on `MindAttic.Vault` for settings (`%APPDATA%\MindAttic\MediaButler\settings.json`)
and credential resolution (User Secrets / environment variables for OpenSubtitles).

## What it does

1. **Self-rename pass.** Cleans messy folder names
   (`Better.Call.Saul.S05.Complete.1080p...`) into FileBot-friendly stems
   (`Better Call Saul - Season 05`). Movies become `Title (YYYY)`. Hoists
   nested `Season N` subfolders out of multi-season parent dumps onto the
   source root and pads season numbers with leading zero. Empty disguised
   folders (no video underneath) are deleted. `Extras` / `Specials` /
   `Bonus` folders are left in place and surfaced in the final report.
2. **FileBot rename pass.** Renames TV episodes via TheTVDB, renames movies
   via TheMovieDB, fetches show artwork (`fn:artwork.tvdb`) and movie
   artwork (`fn:artwork`, after writing xattr via rename — works around the
   `artwork.tmdb` script bug).
3. **Optional subtitle pass.** Calls `filebot -get-subtitles` when
   `EnableSubtitles` is on. Credentials come from the MindAttic Vault chain
   (User Secrets → env vars); see [OpenSubtitles credentials](#opensubtitles-credentials).
4. **Move-to-Plex pass.** TV folders become
   `M:\TV\<Show>\Season XX\episodes...`, movies become
   `M:\Movies\<Title> (YYYY)\...`. Show-level artwork is hoisted from each
   season folder up to the show root and deduplicated.
5. **Final report.** Prints a consolidated summary: items renamed, hoisted,
   moved, FileBot successes, artwork / subtitle counts, errors, and a
   `Needs manual fix` list (Unknown, Extras, and any item that hit a
   pre-existing target).

## Library cleanup: `relocate`

`mediabutler relocate --source <path>` scans an already-organized destination
and moves out anything that doesn't belong there:

- Scanning `M:\Movies` → expected kind is Movie; any `TvSeason` folder gets
  sent to `TvDestination`.
- Scanning `M:\TV` → expected kind is TvSeason; any `Movie` folder gets sent
  to `MoviesDestination`.

Items already in the right place are left alone. Combine with `--dry-run`
to preview the eviction list before committing:

```powershell
mediabutler relocate --dry-run --source "M:\Movies"
mediabutler relocate           --source "M:\Movies"
```

This is the one stage that intentionally runs against a destination, so the
source-vs-destination guard doesn't apply.

## Safety

- **Dry-run mode.** Toggle from the Settings menu or launch with
  `mediabutler --dry-run` (`-n`). In dry-run no files are renamed, moved,
  or deleted; FileBot is invoked with `--action TEST`; artwork and
  subtitle fetches are skipped. Every action prints as `[dry: -> target]`
  so you can see what would have happened.
- **Source-vs-destination guard.** MediaButler refuses to run when
  `SourcePath` equals, contains, or is contained by `TvDestination` /
  `MoviesDestination`. Pointing the source at `M:\TV` would otherwise treat
  every show folder as a multi-season parent to hoist and destroy the
  library.
- **Idempotent operations.** Re-running the pipeline on an already-clean
  library is a no-op: canonical folder names (`Better Call Saul - Season 05`,
  `Heat (1995)`) round-trip through the parser without changing. Targets
  that already exist with content are skipped and recorded as
  needs-manual.

## Configuration

Settings live at `%APPDATA%\MindAttic\MediaButler\settings.json` and are
managed through the in-app Settings menu. Defaults:

| Setting             | Default                                       |
| ------------------- | --------------------------------------------- |
| `sourcePath`        | `M:\Torrents`                                 |
| `tvDestination`     | `M:\TV`                                       |
| `moviesDestination` | `M:\Movies`                                   |
| `fileBotPath`       | `C:\Program Files\FileBot\filebot.exe`        |
| `subtitleLanguage`  | `en`                                          |
| `enableSubtitles`   | `false` (needs OpenSubtitles login)           |
| `dryRun`            | `false`                                       |
| `excludedFolders`   | `temp`, `.temp`, `incomplete`, `complete`, `_unsorted` |

## OpenSubtitles credentials

Credentials are **never** stored in `settings.json` (which lives unencrypted
in roaming app-data). Set them via User Secrets so they live under
`%APPDATA%\Microsoft\UserSecrets\mindattic-vault-shared\secrets.json`:

```powershell
cd MediaButler
dotnet user-secrets set "MindAttic:Vault:Subtitles:OpenSubtitles:user"     ryandebraal
dotnet user-secrets set "MindAttic:Vault:Subtitles:OpenSubtitles:password" '***'
```

Or as environment variables (CI / containers):

```powershell
$env:MindAttic__Vault__Subtitles__OpenSubtitles__user     = 'ryandebraal'
$env:MindAttic__Vault__Subtitles__OpenSubtitles__password = '***'
```

When both values resolve, MediaButler passes them to FileBot per call as
`--def osdb.user=… osdb.pwd=…`. If they're missing the pipeline still runs
— FileBot falls back to whatever is configured in its own Preferences and
MediaButler reports the auth failure (and which key to set) on a 401.

## Why a console app and not PowerShell

Earlier prototyping happened in PowerShell. Switched to .NET because
MediaButler needs `MindAttic.Vault` for shared credential resolution
(OpenSubtitles, plus future cloud storage). The Vault chain
(User Secrets → environment variables → providers.json) is the same one
every other MindAttic app uses.

## Build and run

```powershell
dotnet build MediaButler.slnx
dotnet run --project MediaButler              # interactive menu
dotnet run --project MediaButler -- --dry-run # force dry-run for the session
```

## Tests

NUnit test project at `MediaButler.Tests/`. Coverage:

- `NameParserTests` — every dirty-name pitfall from the README, plus
  round-trip / idempotency invariants for `FormatSeasonFolder` and
  `FormatMovieFolder`.
- `MediaScannerTests` — classification against a real temp directory
  (Empty, Movie, TvSeason, MultiSeasonParent via name signal,
  MultiSeasonParent via structure signal, Extras, excluded folders).
- `RenameStageTests` — full pipeline-stage tests including dry-run leaves
  disk untouched, live rename produces canonical names, idempotent
  re-runs, multi-season hoist, Extras left in place.
- `MoveStageTests` — `SanitizeForFs`, cross-volume detection,
  same-volume rename.
- `SubtitleCredentialsTests` — `IsComplete` semantics and configuration
  binding.
- `PathGuardTests` — the source-vs-destination overlap detector.

```powershell
dotnet test MediaButler.slnx
```

## Pitfalls MediaButler already defends against

These came from a manual run on a 50-folder library; the code now handles
them automatically:

- **PowerShell brackets.** Folder names like `[YTS.MX]` and `[TGx]` are
  wildcards in PowerShell — every file operation here uses `LiteralPath`
  semantics via `System.IO` (no shell expansion).
- **Empty disguised folders.** `Breaking Bad (2008) Season 1-5 ...` was an
  empty shell. MediaButler deletes folders that contain zero video files.
- **Multi-season parents with mixed nesting.** Bones used `Season N`,
  Sherlock used `Show.Season.N.S0N...`, The Following used `Season N`.
  MediaButler detects all three patterns (name signal and / or two-or-more
  season subfolders).
- **Orphan show-level files.** Bones had `Bones_Large.jpg`, `Info.txt`.
  These get relocated into the first hoisted season folder so they aren't
  lost when the parent is deleted.
- **FileBot's `artwork.tmdb` is broken** in 5.2.1. Workaround: rename
  movies via `--db TheMovieDB --action MOVE` first (which writes xattr),
  then run the generic `fn:artwork` script.
- **Subtitle flag.** It's `-get-subtitles`, not
  `-get-missing-subtitles`. Auth failures return a 401 and MediaButler
  reports it gracefully (with the User Secrets key to fix) instead of
  crashing the pipeline.
- **`--action xattr` doesn't exist** in 5.2.1; valid values are MOVE / COPY /
  KEEPLINK / SYMLINK / HARDLINK / CLONE / DUPLICATE / TEST. Dry-run uses TEST.
- **Leading-zero season padding.** `Season 1` → `Season 01` always.
- **Trailing-dash idempotency.** Re-parsing `The Mentalist - Season 04`
  used to leave the show name as `The Mentalist -`, which would re-rename
  the folder to `The Mentalist - - Season 04` on the next run.
  `CleanShowName` now strips trailing dashes.
- **Release-group / index prefixes.** Folders like
  `www.UIndex.org    -    A Knight of the Seven Kingdoms S01E01...` are
  stripped of the prefix before parsing.
- **Extras / Specials.** Top-level `The Venture Bros. - Extras` is
  classified as `Extras` (not as a movie) and surfaced in the manual list.
- **Same source and destination.** Pointing at `M:\TV` is refused before
  any folder is touched in live mode; downgraded to a warning in dry-run so
  you can inspect classification of an already-organized library.
- **Year-in-title movies.** Titles like `Blade Runner 2049`, `Wonder Woman
  1984`, `1917`, `2001 A Space Odyssey` would otherwise have the
  year-shaped number eaten as the release year. The
  `TitleYearOverrides` setting holds a small allowlist of these. Add more
  entries when new ones land.
- **Year-prefixed titles.** `1917 (2019)` and `2009 Lost Memories (2002)`
  used to drop the title because the bare leading 4-digit number was
  matched before the parenthesised year. The parser now prefers a
  parenthesised year whenever both forms are present.

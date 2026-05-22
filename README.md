# Tagsmith

Automated media renamer and reorganizer. Takes a folder full of messy
torrent dumps, cleans the names locally, runs FileBot to grab episode titles
and artwork (and optionally subtitles), then moves everything into a
Plex-ready library layout.

Built on `MindAttic.Vault` for settings (`%APPDATA%\MindAttic\Tagsmith\settings.json`)
and secret resolution (User Secrets / env vars for OpenSubtitles credentials).

## What it does

1. **Self-rename pass.** Cleans messy folder names (`Better.Call.Saul.S05.Complete.1080p...`)
   into FileBot-friendly stems (`Better Call Saul - Season 05`). Movies become
   `Title (YYYY)`. Hoists nested `Season N` subfolders out of multi-season
   parent dumps onto the source root and pads season numbers with leading zero.
2. **FileBot pass.** Renames TV episodes via TheTVDB, renames movies via
   TheMovieDB, fetches show artwork (`fn:artwork.tvdb`) and movie artwork
   (`fn:artwork`, after writing xattr via rename — works around the
   `artwork.tmdb` script bug).
3. **Optional subtitle pass.** Calls `filebot -get-subtitles` if enabled and
   OpenSubtitles credentials are configured.
4. **Move-to-Plex pass.** TV folders become `M:\TV\<Show>\Season XX\episodes...`,
   movies become `M:\Movies\<Title> (YYYY)\...`. Show-level artwork is hoisted
   from each season folder up to the show root and deduplicated.

## Configuration

Settings live at `%APPDATA%\MindAttic\Tagsmith\settings.json` and are managed
through the in-app Settings menu. Defaults:

| Setting             | Default                                       |
| ------------------- | --------------------------------------------- |
| `sourcePath`        | `M:\Torrents`                                 |
| `tvDestination`     | `M:\TV`                                       |
| `moviesDestination` | `M:\Movies`                                   |
| `fileBotPath`       | `C:\Program Files\FileBot\filebot.exe`        |
| `subtitleLanguage`  | `en`                                          |
| `enableSubtitles`   | `false` (needs OpenSubtitles login)           |
| `excludedFolders`   | `temp`, `.temp`, `incomplete`, `complete`     |

## Why a console app and not PowerShell

Earlier prototyping happened in PowerShell. Switched to .NET because Tagsmith
needs `MindAttic.Vault` for shared credential resolution (OpenSubtitles, plus
future cloud storage). The Vault chain (vault files → settings.json → user
secrets → env vars) is the same one every other MindAttic app uses.

## Build and run

```powershell
dotnet build Tagsmith.slnx
dotnet run --project Tagsmith
```

## Pitfalls Tagsmith already defends against

These came from a manual run on a 50-folder library; the code now handles
them automatically:

- **PowerShell brackets.** Folder names like `[YTS.MX]` and `[TGx]` are
  wildcards in PowerShell — every file operation here uses `LiteralPath`
  semantics via `System.IO` (no shell expansion).
- **Empty disguised folders.** `Breaking Bad (2008) Season 1-5 ...` was an
  empty shell. Tagsmith deletes folders that contain zero video files.
- **Multi-season parents with mixed nesting.** Bones used `Season N`,
  Sherlock used `Show.Season.N.S0N...`, The Following used `Season N`.
  Tagsmith detects all three patterns.
- **Orphan show-level files.** Bones had `Bones_Large.jpg`, `Info.txt`.
  These get relocated into the first hoisted season folder so they aren't
  lost when the parent is deleted.
- **FileBot's `artwork.tmdb` is broken** in 5.2.1. Workaround: rename movies
  via `--db TheMovieDB --action MOVE` first (which writes xattr), then run
  the generic `fn:artwork` script.
- **Subtitle flag.** It's `-get-subtitles`, not `-get-missing-subtitles`.
  Auth failures return a 401 and Tagsmith reports it gracefully instead of
  crashing the pipeline.
- **`--action xattr` doesn't exist** in 5.2.1; valid values are MOVE / COPY /
  KEEPLINK / SYMLINK / HARDLINK / CLONE / DUPLICATE / TEST.
- **Leading-zero season padding.** `Season 1` → `Season 01` always.

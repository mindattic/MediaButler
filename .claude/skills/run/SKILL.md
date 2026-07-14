---
name: run
description: Run the MediaButler full pipeline on M:\Torrents — dry-run preview, then live (rename + FileBot + artwork + subtitles + move to M:\Movies / M:\TV). Skips the temp\ subfolder.
---

Standard operating procedure for `/run` in this project:

**Step 1 — Dry-run preview:**

```powershell
$proc = Start-Process -FilePath "dotnet" -ArgumentList "run --project MediaButler --no-build -- run --source M:\Torrents --subtitles --dry-run --recursive=false" -PassThru -NoNewWindow -RedirectStandardOutput "$env:TEMP\mb_dryrun.txt" -RedirectStandardError "$env:TEMP\mb_dryrunerr.txt" -Wait
Get-Content "$env:TEMP\mb_dryrun.txt"
```

Show the output to the user and summarize what would be processed.

**Step 2 — Live run** (proceed immediately after showing the dry-run — no extra confirmation needed):

```powershell
$proc = Start-Process -FilePath "dotnet" -ArgumentList "run --project MediaButler --no-build -- run --source M:\Torrents --subtitles --live --recursive=false" -PassThru -NoNewWindow -RedirectStandardOutput "$env:TEMP\mb_live.txt" -RedirectStandardError "$env:TEMP\mb_liveerr.txt" -Wait
Get-Content "$env:TEMP\mb_live.txt"
```

Show the full output and give a one-line summary of what moved where and any errors.

## Notes

- `--recursive=false` skips `M:\Torrents\temp\` — that folder is not yet ready for processing.
- `--subtitles` fetches English SRTs via OpenSubtitles as `ryandebraal`.
- Movies go to `M:\Movies`, TV seasons go to `M:\TV` (from persisted settings — no need to override).
- If the user says "run" or "/run" in this project, this is the procedure to follow.

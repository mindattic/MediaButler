@echo off
rem ──────────────────────────────────────────────────────────────────────────
rem  mb — MediaButler CLI shim
rem
rem  Forwards every argument to the MediaButler console project.
rem  Uses `dotnet run` so the build is always current — fast on warm builds.
rem
rem  Usage examples:
rem    mb run --source "M:\Torrents" --live
rem    mb scan --source "M:\Torrents"
rem    mb hoist --source "M:\Torrents\My.Show.S01-S03"
rem    mb filebot-movies --source "M:\Movies" --no-guard
rem    mb relocate --source "M:\Movies"
rem    mb status
rem    mb --dry-run run --source "M:\Torrents"
rem    mb --version
rem ──────────────────────────────────────────────────────────────────────────

setlocal

rem Locate this script's directory (the repo root) so `mb` works from anywhere.
set "REPO_ROOT=%~dp0"
set "MB_PROJ=%REPO_ROOT%MediaButler"

rem `dotnet run` does an incremental build automatically — fast on warm builds,
rem and always up-to-date when source has changed. Pass-through every arg.
dotnet run --project "%MB_PROJ%" -- %*

endlocal
exit /b %ERRORLEVEL%

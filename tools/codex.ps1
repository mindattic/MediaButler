<#
.SYNOPSIS
  Codex documentation tooling for MediaButler (MB).
  Subcommands: doctor | digest
.DESCRIPTION
  doctor  validates the docs/ canon (front-matter, IDs, cross-refs, story test tokens,
          cited paths, data schemas, digest freshness). Exits non-zero on any hard error.
  digest  regenerates docs/BIBLE.digest.md from BIBLE.md sections 1, 3, 5, 9 + a status
          index + the latest amendment head.
  No build step; Windows PowerShell 5.1 compatible.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('doctor', 'digest')]
    [string]$Command = 'doctor'
)

$ErrorActionPreference = 'Stop'

# --- Paths --------------------------------------------------------------------
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot  = Split-Path -Parent $ScriptDir
$DocsDir   = Join-Path $RepoRoot 'docs'
$BiblePath = Join-Path $DocsDir 'BIBLE.md'
$StoriesPath = Join-Path $DocsDir 'USER_STORIES.md'
$AmendPath = Join-Path $DocsDir 'AMENDMENTS.md'
$RfcDir    = Join-Path $DocsDir 'rfc'
$DataDir   = Join-Path $DocsDir 'data'
$DigestPath = Join-Path $DocsDir 'BIBLE.digest.md'

function Read-Utf8 {
    # PS 5.1 reads BOM-less files as Win-1252; force UTF-8 so emoji/§/anchors match.
    param([string]$Path)
    return [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
}
function Read-Utf8Lines { param([string]$Path) return (Read-Utf8 -Path $Path) -split "\r?\n" }

$script:Errors = @()
$script:Warns  = @()
function Add-Err($m)  { $script:Errors += $m;  Write-Host "  [FAIL] $m" -ForegroundColor Red }
function Add-Warn($m) { $script:Warns  += $m;  Write-Host "  [warn] $m" -ForegroundColor Yellow }
function Add-Ok($m)   { Write-Host "  [ok]   $m" -ForegroundColor Green }

# --- Helpers ------------------------------------------------------------------
function Get-FrontMatter {
    param([string]$Path)
    $lines = Read-Utf8Lines -Path $Path
    if ($lines.Count -lt 2 -or $lines[0].Trim() -ne '---') { return $null }
    $fm = @{}
    for ($i = 1; $i -lt $lines.Count; $i++) {
        if ($lines[$i].Trim() -eq '---') { return $fm }
        if ($lines[$i] -match '^\s*([A-Za-z_]+)\s*:\s*(.+?)\s*$') {
            $fm[$matches[1]] = $matches[2]
        }
    }
    return $null  # never closed
}

function Test-FrontMatter {
    param([string]$Path, [string]$ExpectLayer)
    if (-not (Test-Path -LiteralPath $Path)) { Add-Err "missing file: $Path"; return }
    $fm = Get-FrontMatter -Path $Path
    $rel = $Path.Substring($RepoRoot.Length).TrimStart('\','/')
    if ($null -eq $fm) { Add-Err "$rel : missing or unterminated YAML front-matter"; return }
    foreach ($key in 'codex','project','code','layer','status','updated') {
        if (-not $fm.ContainsKey($key)) { Add-Err "$rel : front-matter missing '$key'" }
    }
    if ($fm.ContainsKey('codex') -and $fm['codex'] -ne '1') { Add-Err "$rel : codex must be 1" }
    if ($ExpectLayer -and $fm.ContainsKey('layer') -and $fm['layer'] -ne $ExpectLayer) {
        Add-Err "$rel : layer '$($fm['layer'])' != expected '$ExpectLayer'"
    }
    if ($fm.ContainsKey('updated') -and $fm['updated'] -notmatch '^\d{4}-\d{2}-\d{2}$') {
        Add-Err "$rel : 'updated' is not YYYY-MM-DD"
    }
    if ($script:Errors.Count -eq 0 -or $true) { } # keep flow
    Add-Ok "$rel front-matter"
}

# --- DOCTOR -------------------------------------------------------------------
function Invoke-Doctor {
    Write-Host "Codex doctor — MediaButler (MB)" -ForegroundColor Cyan
    Write-Host ""

    # 1. Front-matter on every canon file
    Write-Host "Front-matter:"
    Test-FrontMatter -Path $BiblePath  -ExpectLayer 'bible'
    Test-FrontMatter -Path $StoriesPath -ExpectLayer 'stories'
    Test-FrontMatter -Path $AmendPath  -ExpectLayer 'amendments'
    $rfcFiles = @()
    if (Test-Path -LiteralPath $RfcDir) {
        $rfcFiles = Get-ChildItem -LiteralPath $RfcDir -Filter '*.md' -File -ErrorAction SilentlyContinue
        foreach ($r in $rfcFiles) { Test-FrontMatter -Path $r.FullName -ExpectLayer 'rfc' }
    }
    $dataFiles = @()
    if (Test-Path -LiteralPath $DataDir) {
        $dataFiles = Get-ChildItem -LiteralPath $DataDir -Filter '*.json' -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.DirectoryName -notmatch '_schema' }
    }

    # Gather all markdown canon files for ID/link scanning
    $mdFiles = @($BiblePath, $StoriesPath, $AmendPath) + ($rfcFiles | ForEach-Object { $_.FullName })

    # 2. Anchor IDs unique; cross-refs resolve
    Write-Host ""
    Write-Host "Anchors and cross-references:"
    $anchors = @{}            # id -> file
    $linkRefs = @()           # @{ id; file }
    foreach ($f in $mdFiles) {
        $rel = $f.Substring($RepoRoot.Length).TrimStart('\','/')
        $content = Read-Utf8 -Path $f
        # heading/inline anchor definitions: {#ID}
        foreach ($m in [regex]::Matches($content, '\{#([A-Za-z0-9_\-§]+)\}')) {
            $id = $m.Groups[1].Value
            if ($anchors.ContainsKey($id)) {
                Add-Err "duplicate anchor {#$id} in $rel (also $($anchors[$id]))"
            } else {
                $anchors[$id] = $rel
            }
        }
        # markdown links to a #fragment: [..](path#frag) or [..](#frag)
        foreach ($m in [regex]::Matches($content, '\]\(([^)]*?)#([A-Za-z0-9_\-§]+)\)')) {
            $linkRefs += [pscustomobject]@{ Id = $m.Groups[2].Value; File = $rel; Target = $m.Groups[1].Value }
        }
    }
    if ($anchors.Count -gt 0) { Add-Ok "$($anchors.Count) unique anchor id(s)" }

    # House rules anchors are external but referenced; collect them so links resolve.
    $houseAnchors = @{}
    $housePath = Join-Path (Split-Path -Parent $RepoRoot) 'MindAttic.HouseRules.md'
    if (Test-Path -LiteralPath $housePath) {
        $hc = Read-Utf8 -Path $housePath
        foreach ($m in [regex]::Matches($hc, '\{#([A-Za-z0-9_\-]+)\}')) { $houseAnchors[$m.Groups[1].Value] = $true }
    } else {
        Add-Warn "MindAttic.HouseRules.md not found at $housePath (HOUSE-LAW links unverified)"
    }

    $unresolved = 0
    foreach ($lr in $linkRefs) {
        $isHouse = $lr.Target -match 'HouseRules\.md$'
        # codex-internal target = empty (#frag), or a canon md file. Links into
        # README.md or other non-canon files use README's own slug anchors and
        # are out of scope for codex anchor resolution.
        $isCodexInternal = ($lr.Target -eq '') -or ($lr.Target -match '(BIBLE|USER_STORIES|AMENDMENTS)\.md$') -or ($lr.Target -match '(^|/)rfc/.*\.md$')
        if ($isHouse) {
            if ($houseAnchors.Count -gt 0 -and -not $houseAnchors.ContainsKey($lr.Id)) {
                Add-Err "$($lr.File): link to HouseRules #$($lr.Id) does not resolve"; $unresolved++
            }
        } elseif ($isCodexInternal -and -not $anchors.ContainsKey($lr.Id)) {
            Add-Err "$($lr.File): cross-ref #$($lr.Id) does not resolve to any anchor"; $unresolved++
        }
    }
    if ($unresolved -eq 0) { Add-Ok "$($linkRefs.Count) cross-ref link(s) resolve" }

    # 3. Data files validate against schema; ids unique  (none expected for an app)
    Write-Host ""
    Write-Host "Canon-as-data (L5):"
    if ($dataFiles.Count -eq 0) {
        Add-Ok "no docs/data/*.json (app domain — none expected)"
    } else {
        $seenIds = @{}
        foreach ($d in $dataFiles) {
            $rel = $d.FullName.Substring($RepoRoot.Length).TrimStart('\','/')
            try { $json = (Read-Utf8 -Path $d.FullName) | ConvertFrom-Json }
            catch { Add-Err "$rel : invalid JSON"; continue }
            $schema = Join-Path (Join-Path $DataDir '_schema') ($d.BaseName + '.schema.json')
            if (-not (Test-Path -LiteralPath $schema)) { Add-Warn "$rel : no _schema/$($d.BaseName).schema.json" }
            $entities = if ($json -is [array]) { $json } else { $json }
            foreach ($e in @($entities)) {
                if ($e.PSObject.Properties.Name -contains 'id') {
                    if ($seenIds.ContainsKey($e.id)) { Add-Err "$rel : duplicate entity id '$($e.id)'" }
                    else { $seenIds[$e.id] = $rel }
                }
            }
        }
        Add-Ok "$($dataFiles.Count) data file(s) checked"
    }

    # 4. Every ✅ story names a test token that exists in the test tree
    Write-Host ""
    Write-Host "Story test citations:"
    $testTokens = @{}
    Get-ChildItem -LiteralPath $RepoRoot -Recurse -Filter '*.cs' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\.Tests' -and $_.FullName -notmatch '\\(bin|obj)\\' } |
        ForEach-Object {
            $csRaw = Get-Content -LiteralPath $_.FullName -Raw
            # test method names
            foreach ($m in [regex]::Matches($csRaw, 'public\s+(?:async\s+)?(?:void|Task)\s+([A-Za-z0-9_]+)\s*\(')) {
                $testTokens[$m.Groups[1].Value] = $true
            }
            # test class names (a story may cite the whole fixture, e.g. LandingPageTests)
            foreach ($m in [regex]::Matches($csRaw, '(?:public\s+|sealed\s+|partial\s+)*class\s+([A-Za-z0-9_]+)')) {
                $testTokens[$m.Groups[1].Value] = $true
            }
        }
    $storyRaw = Read-Utf8 -Path $StoriesPath
    $missingTests = 0; $checkedStories = 0
    foreach ($m in [regex]::Matches($storyRaw, '(?s)\*\*MB-US-[A-Za-z0-9]+\s*✅\*\*.*?(?=(\*\*MB-US-|\r?\n## |\r?\n### |\Z))')) {
        $block = $m.Value
        $checkedStories++
        $cited = [regex]::Matches($block, '`([A-Za-z_][A-Za-z0-9_]*)`')
        $hasOne = $false; $foundOne = $false
        foreach ($c in $cited) {
            $tok = $c.Groups[1].Value
            # A test token is either a method name (contains '_') or a *Tests class
            # name. Ignore other backticked tokens (settings keys, file names).
            if (($tok -match '_') -or ($tok -match 'Tests$')) {
                $hasOne = $true
                # token may be a prefix glob like SanitizeForFs_* or PathOverlaps_
                $clean = $tok.TrimEnd('*')
                if ($testTokens.ContainsKey($tok) -or ($testTokens.Keys | Where-Object { $_ -like "$clean*" } | Select-Object -First 1)) {
                    $foundOne = $true
                }
            }
        }
        if (-not $hasOne) { Add-Err "a ✅ story cites no test token: $($block.Substring(0,[Math]::Min(60,$block.Length)))..."; $missingTests++ }
        elseif (-not $foundOne) { Add-Err "a ✅ story's cited test(s) not found in test tree: $($block.Substring(0,[Math]::Min(60,$block.Length)))..."; $missingTests++ }
    }
    if ($missingTests -eq 0) { Add-Ok "$checkedStories ✅ story/stories cite an existing test ($($testTokens.Count) test methods indexed)" }

    # 5. Every code path/file cited in the bible exists on disk
    Write-Host ""
    Write-Host "Cited paths exist:"
    $bibleRaw = Read-Utf8 -Path $BiblePath
    $missingPaths = 0
    foreach ($m in [regex]::Matches($bibleRaw, '`([A-Za-z0-9_][A-Za-z0-9_./\\\-]*\.(cs|csproj|json|md|ps1))`')) {
        $orig = $m.Groups[1].Value
        $p = $orig -replace '/', '\'
        if ($p -match '^(M:|C:|%APPDATA%)') { continue }   # runtime/example paths, not repo files
        # Only verify paths that are clearly repo-relative (have a directory part);
        # a bare filename like settings.json is a runtime artifact, not a repo file.
        if ($orig -notmatch '[\\/]') { continue }
        $full = Join-Path $RepoRoot $p
        if (-not (Test-Path -LiteralPath $full)) { Add-Err "bible cites missing path: $($m.Groups[1].Value)"; $missingPaths++ }
    }
    if ($missingPaths -eq 0) { Add-Ok "all cited repo paths exist" }

    # 6. Digest freshness (generatedFrom: BIBLE.md)
    Write-Host ""
    Write-Host "Digest freshness:"
    if (-not (Test-Path -LiteralPath $DigestPath)) {
        Add-Err "docs/BIBLE.digest.md missing — run: pwsh tools/codex.ps1 digest"
    } else {
        $srcMtime = (Get-Item -LiteralPath $BiblePath).LastWriteTimeUtc
        $artMtime = (Get-Item -LiteralPath $DigestPath).LastWriteTimeUtc
        # Also re-check against amendments head changing
        $amendMtime = (Get-Item -LiteralPath $AmendPath).LastWriteTimeUtc
        $newest = $srcMtime; if ($amendMtime -gt $newest) { $newest = $amendMtime }
        if ($newest -gt $artMtime) {
            Add-Err "BIBLE.digest.md is stale (source newer than digest) — run: pwsh tools/codex.ps1 digest"
        } else {
            Add-Ok "digest is up to date"
        }
    }

    # --- Summary
    Write-Host ""
    if ($script:Errors.Count -gt 0) {
        Write-Host "doctor FAILED: $($script:Errors.Count) error(s), $($script:Warns.Count) warning(s)." -ForegroundColor Red
        exit 1
    }
    Write-Host "doctor PASSED: 0 errors, $($script:Warns.Count) warning(s)." -ForegroundColor Green
    exit 0
}

# --- DIGEST -------------------------------------------------------------------
function Get-BibleSection {
    param([string]$Raw, [int]$Number)
    # Capture from "## <N>. " up to the next "## " heading or EOF.
    $pattern = "(?ms)^##\s+$Number\.\s.*?(?=^##\s+\d+\.|\Z)"
    $m = [regex]::Match($Raw, $pattern)
    if ($m.Success) { return $m.Value.TrimEnd() }
    return ""
}

function Invoke-Digest {
    if (-not (Test-Path -LiteralPath $BiblePath)) { Write-Host "BIBLE.md not found." -ForegroundColor Red; exit 1 }
    $raw = Read-Utf8 -Path $BiblePath

    $sec1 = Get-BibleSection -Raw $raw -Number 1
    $sec3 = Get-BibleSection -Raw $raw -Number 3
    $sec5 = Get-BibleSection -Raw $raw -Number 5
    $sec9 = Get-BibleSection -Raw $raw -Number 9

    # Status index from USER_STORIES.md
    $storyRaw = if (Test-Path -LiteralPath $StoriesPath) { Read-Utf8 -Path $StoriesPath } else { "" }
    $done    = ([regex]::Matches($storyRaw, '✅')).Count
    $partial = ([regex]::Matches($storyRaw, '🟡')).Count
    $planned = ([regex]::Matches($storyRaw, '⬜')).Count
    $cut     = ([regex]::Matches($storyRaw, '🗑️')).Count

    # Latest amendment head
    $amendHead = ""
    if (Test-Path -LiteralPath $AmendPath) {
        $amendRaw = Read-Utf8 -Path $AmendPath
        $am = [regex]::Matches($amendRaw, '(?ms)^##\s+MB-A\d+.*?(?=^##\s+MB-A\d+|\Z)')
        if ($am.Count -gt 0) { $amendHead = $am[$am.Count - 1].Value.TrimEnd() }
    }

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("---")
    [void]$sb.AppendLine("codex: 1")
    [void]$sb.AppendLine("project: MediaButler")
    [void]$sb.AppendLine("code: MB")
    [void]$sb.AppendLine("layer: digest")
    [void]$sb.AppendLine("status: living")
    [void]$sb.AppendLine("generatedFrom: MB-§1")
    [void]$sb.AppendLine("updated: $(Get-Date -Format 'yyyy-MM-dd')")
    [void]$sb.AppendLine("---")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("AUTHORITATIVE — full detail in docs/BIBLE.md")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("> Generated by tools/codex.ps1. Do not hand-edit. This digest is injected at")
    [void]$sb.AppendLine("> session start as the source of truth for MediaButler (MB).")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("# MediaButler — Bible digest")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine($sec1)
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine($sec3)
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine($sec5)
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine($sec9)
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("## Status index (from docs/USER_STORIES.md)")
    [void]$sb.AppendLine("- ✅ done: $done")
    [void]$sb.AppendLine("- 🟡 partial: $partial")
    [void]$sb.AppendLine("- ⬜ planned: $planned")
    [void]$sb.AppendLine("- 🗑️ cut: $cut")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("## Latest amendment (amendment wins over the bible)")
    [void]$sb.AppendLine("")
    if ($amendHead) { [void]$sb.AppendLine($amendHead) } else { [void]$sb.AppendLine("(none)") }
    [void]$sb.AppendLine("")

    Set-Content -LiteralPath $DigestPath -Value $sb.ToString() -Encoding UTF8
    Write-Host "Wrote $DigestPath" -ForegroundColor Green
    Write-Host "Sections: 1,3,5,9 + status index (✅$done 🟡$partial ⬜$planned 🗑️$cut) + latest amendment."
}

switch ($Command) {
    'doctor' { Invoke-Doctor }
    'digest' { Invoke-Digest }
}

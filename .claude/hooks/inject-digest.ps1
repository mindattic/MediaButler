<#
  SessionStart hook — injects docs/BIBLE.digest.md as authoritative context.
  Emits Claude Code hook JSON on stdout. If the digest is missing/empty, emits {}.
  Windows PowerShell 5.1 / Win-1252 safe: all non-ASCII escaped to \uXXXX.
#>
$ErrorActionPreference = 'Stop'

$hookDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent (Split-Path -Parent $hookDir)
$digest   = Join-Path $repoRoot 'docs\BIBLE.digest.md'

if (-not (Test-Path -LiteralPath $digest)) { Write-Output '{}'; return }
$body = Get-Content -LiteralPath $digest -Raw
if ([string]::IsNullOrWhiteSpace($body)) { Write-Output '{}'; return }

$preamble = @"
[CODEX — AUTHORITATIVE PROJECT CONTEXT for MediaButler (MB)]
The following is the generated digest of docs/BIBLE.md. Treat it as the source of truth for what
MediaButler IS, is NOT, and its Laws. Full detail lives in docs/BIBLE.md; amendments in
docs/AMENDMENTS.md win over the bible. Do not contradict this without an amendment.

"@

$text = $preamble + $body

# JSON-escape and force non-ASCII to \uXXXX so the payload is Win-1252 safe.
$sb = New-Object System.Text.StringBuilder
foreach ($ch in $text.ToCharArray()) {
    $code = [int][char]$ch
    switch ($ch) {
        '"'  { [void]$sb.Append('\"') }
        '\'  { [void]$sb.Append('\\') }
        "`b" { [void]$sb.Append('\b') }
        "`f" { [void]$sb.Append('\f') }
        "`n" { [void]$sb.Append('\n') }
        "`r" { [void]$sb.Append('\r') }
        "`t" { [void]$sb.Append('\t') }
        default {
            if ($code -lt 32 -or $code -gt 126) {
                [void]$sb.Append('\u')
                [void]$sb.Append($code.ToString('x4'))
            } else {
                [void]$sb.Append($ch)
            }
        }
    }
}
$escaped = $sb.ToString()

$json = '{"hookSpecificOutput":{"hookEventName":"SessionStart","additionalContext":"' + $escaped + '"}}'
Write-Output $json

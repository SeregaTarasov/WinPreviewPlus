# Removes stale SharpShell ClassOfExtension ProgID classes (<ext>.1) that make
# a re-install fail in RegAsm ("class already in use by application ...").
# These classes carry the '<ext> Application' default value and are created by
# SharpShell itself, so removing them before registration is safe: the install
# recreates them in a consistent state.
param(
  [string]$Root = $PSScriptRoot
)
$ErrorActionPreference = 'Stop'

$handlerGlob = Join-Path $Root 'WinPreviewPlus\*Handler.cs'
if (-not (Test-Path $handlerGlob)) {
  Write-Host "Purge: no handler sources under $Root, skipping."
  return
}

$exts = Select-String -Path $handlerGlob -Pattern 'ClassOfExtension\s*,\s*"(\.[^"]+)"' -AllMatches |
  ForEach-Object { $_.Matches } |
  ForEach-Object { $_.Groups[1].Value } |
  Sort-Object -Unique

$removed = 0
foreach ($ext in $exts) {
  $name = $ext.TrimStart('.')
  $cls  = $name + '.1'
  foreach ($base in @("HKLM:\Software\Classes\$cls", "HKCU:\Software\Classes\$cls")) {
    if (-not (Test-Path $base)) { continue }
    $def = (Get-ItemProperty -Path $base -ErrorAction SilentlyContinue).'(default)'
    $hasOpenCmd = Test-Path (Join-Path $base 'Shell\Open\command')
    $hasClsid   = Test-Path (Join-Path $base 'CLSID')
    # Only SharpShell's own inert placeholder classes: default '<name> Application',
    # no real file-open command and no CLSID pointing to a live server.
    if ($def -eq "$name Application" -and -not $hasOpenCmd -and -not $hasClsid) {
      try {
        Remove-Item -Path $base -Recurse -Force -ErrorAction Stop
        Write-Host "  purged $base"
        $removed++
      } catch {
        Write-Host "  skipped $base : $($_.Exception.Message)"
      }
    }
  }
}
Write-Host "Purge: removed $removed stale ProgID class(es)."

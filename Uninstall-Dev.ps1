#Requires -Version 5.1
<#
.SYNOPSIS
  Unregisters WinPreviewPlus Alt+P handlers (HKCU) and optionally SharpShell.
.PARAMETER ElevateSharpShell
  Also run SharpShell UninstallServer with UAC.
#>
param(
  [switch]$ElevateSharpShell
)
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$deploy = Join-Path $env:LOCALAPPDATA 'WinPreviewPlus\bin'
$PreviewGuid = '{8895b1c6-b41f-4c1c-a562-0d564250836f}'

$handlers = @(
  @{
    Id = '{B7E4A901-2C5D-4F8A-9E31-11A0C3D5E701}'
    Exts = @('.zip', '.7z', '.rar', '.tar', '.tgz', '.gz', '.bz2', '.xz')
  },
  @{
    Id = '{B7E4A902-2C5D-4F8A-9E31-11A0C3D5E702}'
    Exts = @('.docx', '.xlsx', '.xls', '.pptx')
  },
  @{
    Id = '{B7E4A903-2C5D-4F8A-9E31-11A0C3D5E703}'
    Exts = @('.pdf')
  },
  @{
    Id = '{B7E4A904-2C5D-4F8A-9E31-11A0C3D5E704}'
    Exts = @(
      '.txt', '.log', '.md', '.csv', '.tsv', '.json', '.geojson', '.jsonl', '.ndjson',
      '.xml', '.plist', '.mobileconfig', '.entitlements', '.strings', '.xcconfig',
      '.pbxproj', '.podspec', '.ini', '.cfg', '.conf', '.toml', '.env',
      '.yml', '.yaml', '.ps1', '.bat', '.cmd', '.cs', '.js', '.ts', '.tsx', '.jsx',
      '.py', '.sql', '.swift', '.m', '.mm', '.h', '.hpp', '.c', '.cpp', '.rs', '.go',
      '.rb', '.php', '.sh', '.bash', '.zsh', '.html', '.htm', '.css', '.scss', '.less',
      '.editorconfig', '.gpx', '.kml', '.graphql', '.properties', '.gradle',
      '.gitignore', '.dockerignore'
    )
  },
  @{
    Id = '{B7E4A905-2C5D-4F8A-9E31-11A0C3D5E705}'
    Exts = @(
      '.mp3', '.wav', '.flac', '.m4a', '.m4r', '.aac', '.wma', '.ogg', '.opus', '.aiff', '.aif'
    )
  },
  @{
    Id = '{B7E4A906-2C5D-4F8A-9E31-11A0C3D5E706}'
    Exts = @(
      '.mp4', '.m4v', '.mov', '.avi', '.wmv', '.mkv', '.webm', '.mpeg', '.mpg', '.3gp'
    )
  },
  @{
    Id = '{B7E4A907-2C5D-4F8A-9E31-11A0C3D5E707}'
    Exts = @(
      '.jpg', '.jpeg', '.jfif', '.png', '.gif', '.bmp', '.tif', '.tiff', '.webp', '.ico',
      '.svg', '.heic', '.heif', '.heics', '.avif'
    )
  }
)

foreach ($h in $handlers) {
  foreach ($ext in $h.Exts) {
    $shellex = "HKCU:\Software\Classes\$ext\shellex\$PreviewGuid"
    if (Test-Path $shellex) {
      $cur = (Get-ItemProperty $shellex).'(default)'
      if ($cur -eq $h.Id) {
        Remove-Item -Path $shellex -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "Removed $ext preview association"
      }
    }
  }
  Remove-Item -Path "HKCU:\Software\Classes\CLSID\$($h.Id)" -Recurse -Force -ErrorAction SilentlyContinue
  Remove-Item -Path "HKCU:\Software\Classes\AppID\$($h.Id)" -Recurse -Force -ErrorAction SilentlyContinue
  $phCu = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\PreviewHandlers'
  if (Test-Path $phCu) {
    Remove-ItemProperty -Path $phCu -Name $h.Id -ErrorAction SilentlyContinue
  }
}

if ($ElevateSharpShell) {
  $regExe = Join-Path $deploy 'WinPreviewPlus.Register.exe'
  if (-not (Test-Path $regExe)) {
    $regExe = Join-Path $Root 'WinPreviewPlus.Register\bin\Release\net48\WinPreviewPlus.Register.exe'
  }
  if (Test-Path $regExe) {
    try {
      $p = Start-Process -FilePath $regExe -ArgumentList 'uninstall' -WorkingDirectory (Split-Path $regExe) -Wait -PassThru -Verb RunAs -ErrorAction Stop
      Write-Host "SharpShell uninstall exit: $($p.ExitCode)"
    } catch {
      Write-Host "SharpShell elevated uninstall skipped: $($_.Exception.Message)"
    }
  }
}

Write-Host 'Unregistered HKCU preview handlers.'

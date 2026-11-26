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
    Exts = @(
      '.zip', '.7z', '.rar', '.tar', '.tgz', '.gz', '.bz2', '.xz',
      '.epub', '.jar', '.apk', '.nupkg', '.whl', '.vsix', '.cbz', '.cbr',
      '.war', '.ear', '.ipa', '.xpi', '.crx', '.appx', '.msix', '.kmz',
      '.tbz', '.tbz2', '.txz', '.lz', '.cbt'
    )
  },
  @{
    Id = '{B7E4A902-2C5D-4F8A-9E31-11A0C3D5E702}'
    Exts = @(
      '.docx', '.docm', '.dotx', '.dotm',
      '.xlsx', '.xlsm', '.xltx', '.xltm', '.xls', '.xlsb', '.xlt',
      '.pptx', '.pptm', '.potx', '.potm', '.ppsx', '.ppsm',
      '.odt', '.ods', '.odp', '.odg'
    )
  },
  @{
    Id = '{B7E4A903-2C5D-4F8A-9E31-11A0C3D5E703}'
    Exts = @('.pdf')
  },
  @{
    Id = '{B7E4A904-2C5D-4F8A-9E31-11A0C3D5E704}'
    Exts = @(
      '.txt', '.log', '.md', '.markdown', '.mdown', '.mdx', '.mdc',
      '.csv', '.tsv', '.json', '.geojson', '.jsonl', '.ndjson',
      '.xml', '.plist', '.mobileconfig', '.entitlements', '.strings', '.xcconfig',
      '.pbxproj', '.podspec', '.ini', '.cfg', '.conf', '.toml', '.env',
      '.yml', '.yaml', '.ps1', '.bat', '.cmd', '.cs', '.js', '.ts', '.tsx', '.jsx',
      '.py', '.sql', '.swift', '.m', '.mm', '.h', '.hpp', '.c', '.cpp', '.rs', '.go',
      '.rb', '.php', '.sh', '.bash', '.zsh', '.html', '.htm', '.css', '.scss', '.less',
      '.editorconfig', '.gpx', '.kml', '.graphql', '.properties', '.gradle',
      '.gitignore', '.dockerignore',
      '.rtf', '.csproj', '.sln', '.vbproj', '.vb', '.fs', '.java', '.kt', '.lua',
      '.psm1', '.psd1', '.vue', '.srt', '.vtt', '.ics', '.diff', '.patch',
      '.proto', '.dart', '.jsonc', '.resx', '.config', '.nuspec', '.cshtml',
      '.razor', '.adoc', '.reg', '.ipynb',
      '.tf', '.hcl', '.cmake', '.mk', '.asm', '.svelte', '.zig', '.ex', '.exs',
      '.wsdl', '.xsd', '.xsl', '.xslt', '.ps1xml', '.lock', '.npmrc', '.map',
      '.http', '.rst', '.org', '.tex', '.bib', '.fodt', '.fods', '.fodp'
    )
  },
  @{
    Id = '{B7E4A905-2C5D-4F8A-9E31-11A0C3D5E705}'
    Exts = @(
      '.mp3', '.wav', '.flac', '.m4a', '.m4r', '.aac', '.wma', '.ogg', '.opus',
      '.aiff', '.aif', '.oga', '.mpga', '.weba'
    )
  },
  @{
    Id = '{B7E4A906-2C5D-4F8A-9E31-11A0C3D5E706}'
    Exts = @(
      '.mp4', '.m4v', '.mov', '.avi', '.wmv', '.mkv', '.webm', '.mpeg', '.mpg',
      '.3gp', '.ogv', '.3g2', '.asf'
    )
  },
  @{
    Id = '{B7E4A907-2C5D-4F8A-9E31-11A0C3D5E707}'
    Exts = @(
      '.jpg', '.jpeg', '.jpe', '.jfif', '.png', '.gif', '.bmp', '.dib',
      '.tif', '.tiff', '.webp', '.ico', '.cur',
      '.svg', '.svgz', '.emf', '.wmf', '.heic', '.heif', '.heics', '.hif', '.avif', '.jxr'
    )
  },
  @{
    Id = '{B7E4A908-2C5D-4F8A-9E31-11A0C3D5E708}'
    Exts = @('Directory', 'Folder')
  }
)

foreach ($h in $handlers) {
  foreach ($ext in $h.Exts) {
    foreach ($shellex in @(
        "HKCU:\Software\Classes\$ext\shellex\$PreviewGuid",
        "HKCU:\Software\Classes\SystemFileAssociations\$ext\shellex\$PreviewGuid"
      )) {
      if (Test-Path $shellex) {
        $cur = (Get-ItemProperty $shellex).'(default)'
        if ($cur -eq $h.Id) {
          Remove-Item -Path $shellex -Recurse -Force -ErrorAction SilentlyContinue
          Write-Host "Removed $ext preview association"
        }
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

$ourIds = @($handlers | ForEach-Object { $_.Id })
foreach ($prog in @(
    'Applications\7zFM.exe',
    'ASC.Sheet.12', 'ASC.Document.12', 'ASC.Presentation.12',
    'ASC.Sheet.1', 'ASC.Document.1',
    'Excel.Sheet.8', 'Excel.Sheet.12'
  )) {
  $shellex = "HKCU:\Software\Classes\$prog\shellex\$PreviewGuid"
  if (Test-Path $shellex) {
    $cur = (Get-ItemProperty $shellex).'(default)'
    if ($ourIds -contains $cur) {
      Remove-Item -Path $shellex -Recurse -Force -ErrorAction SilentlyContinue
      Write-Host "Removed ProgId $prog"
    }
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

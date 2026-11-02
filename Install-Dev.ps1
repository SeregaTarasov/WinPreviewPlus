#Requires -Version 5.1
<#
.SYNOPSIS
  Builds and registers WinPreviewPlus Alt+P handlers (archives + Office).
#>
param(
  [switch]$ElevateSharpShell
)
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'MSBuild not found. Install VS 2022 Build Tools.' }

Write-Host "MSBuild: $msbuild"
& $msbuild (Join-Path $Root 'WinPreviewPlus.sln') /t:Restore,Build /p:Configuration=Release /v:m
if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }

$outDir = Join-Path $Root 'WinPreviewPlus\bin\Release\net48'
$regOut = Join-Path $Root 'WinPreviewPlus.Register\bin\Release\net48'
$deploy = Join-Path $env:LOCALAPPDATA 'WinPreviewPlus\bin'
# prevhost locks deployed DLLs while preview pane is open
Stop-Process -Name prevhost -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 400
New-Item -ItemType Directory -Force -Path $deploy | Out-Null
Copy-Item -Path (Join-Path $outDir '*') -Destination $deploy -Force -Recurse
Copy-Item -Path (Join-Path $regOut '*') -Destination $deploy -Force -Recurse
# PdfiumViewer: x64\pdfium.dll + копия в корень (prevhost иногда ищет рядом с DLL)
$pdfium = Join-Path $outDir 'x64\pdfium.dll'
if (Test-Path $pdfium) {
  New-Item -ItemType Directory -Force -Path (Join-Path $deploy 'x64') | Out-Null
  Copy-Item -Path $pdfium -Destination (Join-Path $deploy 'x64\pdfium.dll') -Force
  Copy-Item -Path $pdfium -Destination (Join-Path $deploy 'pdfium.dll') -Force
}
# WebView2 native loader (видео HTML5)
$wv2 = Join-Path $outDir 'WebView2Loader.dll'
if (-not (Test-Path $wv2)) {
  $wv2 = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.web.webview2\1.0.2903.40\runtimes\win-x64\native\WebView2Loader.dll'
}
if (Test-Path $wv2) {
  Copy-Item -Path $wv2 -Destination (Join-Path $deploy 'WebView2Loader.dll') -Force
  New-Item -ItemType Directory -Force -Path (Join-Path $deploy 'x64') | Out-Null
  Copy-Item -Path $wv2 -Destination (Join-Path $deploy 'x64\WebView2Loader.dll') -Force
}
Get-ChildItem $deploy -Recurse -File | Unblock-File

$dllPath = Join-Path $deploy 'WinPreviewPlus.dll'
$regasm = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe'
if (-not (Test-Path $regasm)) { throw "RegAsm not found: $regasm" }

Push-Location $deploy
try {
  Write-Host 'RegAsm /codebase ...'
  & $regasm /codebase $dllPath
  if ($LASTEXITCODE -ne 0) { throw "RegAsm failed: $LASTEXITCODE" }

  Write-Host 'SharpShell InstallServer ...'
  & (Join-Path $deploy 'WinPreviewPlus.Register.exe')
  if ($LASTEXITCODE -ne 0) { throw "SharpShell register failed: $LASTEXITCODE" }
}
finally {
  Pop-Location
}

$PreviewGuid = '{8895b1c6-b41f-4c1c-a562-0d564250836f}'
$PrevHostAppId = '{6d2b5079-2f0b-48dd-ab7f-97cec514d30b}' # system prevhost surrogate
$arch = '{B7E4A901-2C5D-4F8A-9E31-11A0C3D5E701}'
$off = '{B7E4A902-2C5D-4F8A-9E31-11A0C3D5E702}'
$pdf = '{B7E4A903-2C5D-4F8A-9E31-11A0C3D5E703}'
$txt = '{B7E4A904-2C5D-4F8A-9E31-11A0C3D5E704}'
$aud = '{B7E4A905-2C5D-4F8A-9E31-11A0C3D5E705}'
$vid = '{B7E4A906-2C5D-4F8A-9E31-11A0C3D5E706}'
$img = '{B7E4A907-2C5D-4F8A-9E31-11A0C3D5E707}'

$progMap = @{
  'Applications\7zFM.exe' = $arch
  'ASC.Sheet.12' = $off
  'ASC.Document.12' = $off
  'ASC.Presentation.12' = $off
  'ASC.Sheet.1' = $off
  'ASC.Document.1' = $off
  'Excel.Sheet.8' = $off
  'Excel.Sheet.12' = $off
}
foreach ($kv in $progMap.GetEnumerator()) {
  $shellex = "HKCU:\Software\Classes\$($kv.Key)\shellex\$PreviewGuid"
  New-Item -Path $shellex -Force | Out-Null
  Set-ItemProperty -Path $shellex -Name '(default)' -Value $kv.Value
  Write-Host "ProgId $($kv.Key) -> $($kv.Value)"
}

$extMap = @{
  '.zip' = $arch; '.7z' = $arch; '.rar' = $arch; '.tar' = $arch
  '.tgz' = $arch; '.gz' = $arch; '.bz2' = $arch; '.xz' = $arch
  '.docx' = $off; '.xlsx' = $off; '.xls' = $off; '.pptx' = $off
  '.pdf' = $pdf
  '.txt' = $txt; '.log' = $txt; '.md' = $txt; '.csv' = $txt; '.tsv' = $txt
  '.json' = $txt; '.geojson' = $txt; '.jsonl' = $txt; '.ndjson' = $txt
  '.xml' = $txt; '.plist' = $txt; '.mobileconfig' = $txt; '.entitlements' = $txt
  '.strings' = $txt; '.xcconfig' = $txt; '.pbxproj' = $txt; '.podspec' = $txt
  '.ini' = $txt; '.cfg' = $txt; '.conf' = $txt; '.toml' = $txt; '.env' = $txt
  '.yml' = $txt; '.yaml' = $txt; '.ps1' = $txt; '.bat' = $txt; '.cmd' = $txt
  '.cs' = $txt; '.js' = $txt; '.ts' = $txt; '.tsx' = $txt; '.jsx' = $txt
  '.py' = $txt; '.sql' = $txt; '.swift' = $txt; '.m' = $txt; '.mm' = $txt
  '.h' = $txt; '.hpp' = $txt; '.c' = $txt; '.cpp' = $txt; '.rs' = $txt; '.go' = $txt
  '.rb' = $txt; '.php' = $txt; '.sh' = $txt; '.bash' = $txt; '.zsh' = $txt
  '.html' = $txt; '.htm' = $txt; '.css' = $txt; '.scss' = $txt; '.less' = $txt
  '.editorconfig' = $txt; '.gpx' = $txt; '.kml' = $txt; '.graphql' = $txt
  '.properties' = $txt; '.gradle' = $txt; '.gitignore' = $txt; '.dockerignore' = $txt
  '.mp3' = $aud; '.wav' = $aud; '.flac' = $aud; '.m4a' = $aud; '.m4r' = $aud; '.aac' = $aud
  '.wma' = $aud; '.ogg' = $aud; '.opus' = $aud; '.aiff' = $aud; '.aif' = $aud
  '.mp4' = $vid; '.m4v' = $vid; '.mov' = $vid; '.avi' = $vid; '.wmv' = $vid
  '.mkv' = $vid; '.webm' = $vid; '.mpeg' = $vid; '.mpg' = $vid; '.3gp' = $vid
  '.jpg' = $img; '.jpeg' = $img; '.jfif' = $img; '.png' = $img; '.gif' = $img
  '.bmp' = $img; '.tif' = $img; '.tiff' = $img; '.webp' = $img; '.ico' = $img
  '.svg' = $img; '.heic' = $img; '.heif' = $img; '.heics' = $img; '.avif' = $img
}
foreach ($kv in $extMap.GetEnumerator()) {
  foreach ($base in @(
    "HKCU:\Software\Classes\$($kv.Key)\shellex\$PreviewGuid",
    "HKCU:\Software\Classes\SystemFileAssociations\$($kv.Key)\shellex\$PreviewGuid"
  )) {
    New-Item -Path $base -Force | Out-Null
    Set-ItemProperty -Path $base -Name '(default)' -Value $kv.Value
  }
}

foreach ($pair in @(
  @{ Id = $arch; Name = 'WinPreviewPlus Archive' },
  @{ Id = $off; Name = 'WinPreviewPlus Office' },
  @{ Id = $pdf; Name = 'WinPreviewPlus PDF' },
  @{ Id = $txt; Name = 'WinPreviewPlus Text' },
  @{ Id = $aud; Name = 'WinPreviewPlus Audio' },
  @{ Id = $vid; Name = 'WinPreviewPlus Video' },
  @{ Id = $img; Name = 'WinPreviewPlus Image' }
)) {
  foreach ($root in @(
    "HKCU:\Software\Classes\CLSID\$($pair.Id)",
    "HKLM:\Software\Classes\CLSID\$($pair.Id)"
  )) {
    if (-not (Test-Path $root)) { continue }
    New-ItemProperty -Path $root -Name 'AppID' -Value $PrevHostAppId -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $root -Name 'DisableLowILProcessIsolation' -PropertyType DWord -Value 1 -Force | Out-Null
    # Same as Edge PDF handler — skip MOTW "may harm your computer" interstitial
    New-ItemProperty -Path $root -Name 'AutomaticallyPreviewUntrustedFiles' -PropertyType DWord -Value 1 -Force | Out-Null
    New-ItemProperty -Path $root -Name 'EnablePreviewHandler' -PropertyType DWord -Value 1 -Force | Out-Null
    $inproc = Join-Path $root 'InprocServer32'
    if (Test-Path $inproc) {
      Set-ItemProperty -Path $inproc -Name 'ThreadingModel' -Value 'Apartment'
    }
  }
  New-Item -Path "HKCU:\Software\Classes\CLSID\$($pair.Id)\Implemented Categories\$PreviewGuid" -Force | Out-Null
  $phCu = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\PreviewHandlers'
  if (-not (Test-Path $phCu)) { New-Item -Path $phCu -Force | Out-Null }
  New-ItemProperty -Path $phCu -Name $pair.Id -Value $pair.Name -PropertyType String -Force | Out-Null
  try {
    New-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\PreviewHandlers' -Name $pair.Id -Value $pair.Name -PropertyType String -Force -ErrorAction Stop | Out-Null
  } catch { }
}

if ($ElevateSharpShell) {
  try {
    $p = Start-Process -FilePath (Join-Path $deploy 'WinPreviewPlus.Register.exe') -WorkingDirectory $deploy -Wait -PassThru -Verb RunAs -ErrorAction Stop
    Write-Host "Elevated SharpShell exit: $($p.ExitCode)"
  } catch {
    Write-Host "Elevated SharpShell skipped: $($_.Exception.Message)"
  }
}

Add-Type -Namespace Win32 -Name ShellNotify -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("shell32.dll")] public static extern void SHChangeNotify(int wEventId, uint uFlags, System.IntPtr dwItem1, System.IntPtr dwItem2);
'@ -ErrorAction SilentlyContinue
try { [Win32.ShellNotify]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero) } catch { }

Stop-Process -Name prevhost -Force -ErrorAction SilentlyContinue

foreach ($id in @($arch.Trim('{}'), $off.Trim('{}'), $pdf.Trim('{}'), $txt.Trim('{}'), $aud.Trim('{}'), $vid.Trim('{}'), $img.Trim('{}'))) {
  try {
    $o = [Activator]::CreateInstance([Type]::GetTypeFromCLSID([guid]$id))
    Write-Host "CoCreate {$id} OK $($o.GetType().Name)"
  } catch {
    Write-Host "CoCreate {$id} FAIL $($_.Exception.GetBaseException().Message)"
  }
}

# Always recycle Explorer so Alt+P picks up the new DLL/toolbar
Write-Host 'Restarting Explorer...'
Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 700
Start-Process explorer.exe

Write-Host @"

Installed. Alt+P: archives, Office, PDF, text, audio, video
Audio: .mp3 .wav .flac .m4a .m4r …  Video: .mp4 .mkv .avi .mov .wmv …
Space = play/pause. Explorer restarted.
Log: $env:LOCALAPPDATA\WinPreviewPlus\preview.log
Product: WinPreviewPlus
"@

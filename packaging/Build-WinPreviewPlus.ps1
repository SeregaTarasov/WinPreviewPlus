#Requires -Version 5.1
# Builds single-file WinPreviewPlus-Setup.exe (payload embedded).
# Output: dist\WinPreviewPlus-Setup.exe
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$SetupDir = Join-Path $Root 'WinPreviewPlus.Setup'
$Dist = Join-Path $Root 'dist'
$Stage = Join-Path $Root 'packaging\stage'

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'MSBuild not found' }

Write-Host "MSBuild: $msbuild"
Write-Host 'Building product (without Setup)...'
$projDll = Join-Path $Root 'WinPreviewPlus\WinPreviewPlus.csproj'
$projReg = Join-Path $Root 'WinPreviewPlus.Register\WinPreviewPlus.Register.csproj'
& $msbuild $projDll /t:Restore,Rebuild /p:Configuration=Release /v:m
if ($LASTEXITCODE -ne 0) { throw "Product DLL build failed: $LASTEXITCODE" }
& $msbuild $projReg /t:Restore,Rebuild /p:Configuration=Release /v:m
if ($LASTEXITCODE -ne 0) { throw "Register build failed: $LASTEXITCODE" }

$outDir = Join-Path $Root 'WinPreviewPlus\bin\Release\net48'
$regOut = Join-Path $Root 'WinPreviewPlus.Register\bin\Release\net48'

if (Test-Path $Stage) { Remove-Item $Stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $Stage | Out-Null

Get-ChildItem $outDir -File | Where-Object {
  $_.Extension -notin @('.pdb') -and
  $_.Name -notin @('Microsoft.Web.WebView2.Wpf.dll')
} | Copy-Item -Destination $Stage -Force

Get-ChildItem $outDir -Directory | ForEach-Object {
  if ($_.Name -eq 'x64' -or $_.Name -eq 'nl') {
    Copy-Item $_.FullName (Join-Path $Stage $_.Name) -Recurse -Force
  }
}

Copy-Item -Path (Join-Path $regOut 'WinPreviewPlus.Register.exe') -Destination $Stage -Force
if (Test-Path (Join-Path $regOut 'WinPreviewPlus.Register.exe.config')) {
  Copy-Item (Join-Path $regOut 'WinPreviewPlus.Register.exe.config') $Stage -Force
}
Get-ChildItem $Stage -Filter '*.pdb' -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force

# One pdfium only (x64)
$pdfium = Join-Path $outDir 'x64\pdfium.dll'
if (-not (Test-Path $pdfium)) { $pdfium = Join-Path $outDir 'pdfium.dll' }
if (Test-Path $pdfium) {
  New-Item -ItemType Directory -Force -Path (Join-Path $Stage 'x64') | Out-Null
  Copy-Item $pdfium (Join-Path $Stage 'x64\pdfium.dll') -Force
  if (Test-Path (Join-Path $Stage 'pdfium.dll')) { Remove-Item (Join-Path $Stage 'pdfium.dll') -Force }
}

$wv2 = Join-Path $outDir 'WebView2Loader.dll'
if (-not (Test-Path $wv2)) {
  $wv2 = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.web.webview2\1.0.2903.40\runtimes\win-x64\native\WebView2Loader.dll'
}
if (Test-Path $wv2) {
  Copy-Item $wv2 (Join-Path $Stage 'WebView2Loader.dll') -Force
  New-Item -ItemType Directory -Force -Path (Join-Path $Stage 'x64') | Out-Null
  Copy-Item $wv2 (Join-Path $Stage 'x64\WebView2Loader.dll') -Force
}

# Personal markers from THIS machine (no hardcoded names in repo)
$personalNeedles = New-Object System.Collections.Generic.List[string]
if (-not [string]::IsNullOrWhiteSpace($env:USERNAME) -and $env:USERNAME.Length -ge 3) {
  [void]$personalNeedles.Add($env:USERNAME)
}
if (-not [string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
  [void]$personalNeedles.Add($env:USERPROFILE)
  $driveRoot = Split-Path -Qualifier $env:USERPROFILE -ErrorAction SilentlyContinue
  if ($driveRoot) {
    # e.g. D:\<folder-with-username> style paths used as workspace
    $userLeaf = Split-Path $env:USERPROFILE -Leaf
    if ($userLeaf) { [void]$personalNeedles.Add(($driveRoot.TrimEnd('\') + '\' + $userLeaf)) }
  }
}
[void]$personalNeedles.Add('AppData\Roaming\NuGet')
[void]$personalNeedles.Add('hosts.md')
[void]$personalNeedles.Add('BEGIN RSA PRIVATE')
[void]$personalNeedles.Add('PRIVATE KEY-----')

function Test-ContainsNeedle([byte[]]$bytes, [string]$needle) {
  if ($null -eq $bytes -or $bytes.Length -eq 0 -or [string]::IsNullOrEmpty($needle)) { return $false }
  if ($needle.Length -lt 3) { return $false }
  $uni = [Text.Encoding]::Unicode.GetString($bytes)
  $asc = [Text.Encoding]::ASCII.GetString($bytes)
  return $uni.IndexOf($needle, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
         $asc.IndexOf($needle, [StringComparison]::OrdinalIgnoreCase) -ge 0
}

Write-Host 'Privacy scan (stage): no personal account/paths in shipped binaries...'
$bad = New-Object System.Collections.Generic.List[string]
Get-ChildItem $Stage -Recurse -File | ForEach-Object {
  if ($_.Extension -eq '.pdb') {
    [void]$bad.Add("PDB present: $($_.Name)")
    return
  }
  $bytes = [IO.File]::ReadAllBytes($_.FullName)
  $isOurs = ($_.Name -like 'WinPreviewPlus*') -or ($_.Extension -eq '.config')
  $checkExt = $_.Extension -in @('.config', '.json', '.xml', '.txt', '.md', '.deps', '.dll', '.exe')
  if ($checkExt -or $isOurs) {
    foreach ($n in $personalNeedles) {
      if (Test-ContainsNeedle $bytes $n) {
        [void]$bad.Add("$($_.Name): personal marker")
        break
      }
    }
  }
}
if ($bad.Count -gt 0) {
  Write-Host 'FAIL privacy:'
  $bad | Select-Object -Unique | ForEach-Object { Write-Host "  $_" }
  throw 'Privacy scan failed'
}
Write-Host 'Privacy scan stage: OK'

$payload = Join-Path $SetupDir 'payload.zip'
if (Test-Path $payload) { Remove-Item $payload -Force }
Write-Host "Packing payload -> $payload"
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($Stage, $payload, [System.IO.Compression.CompressionLevel]::Optimal, $false)

Write-Host 'Building Setup...'
& $msbuild (Join-Path $SetupDir 'WinPreviewPlus.Setup.csproj') /t:Restore,Rebuild /p:Configuration=Release /v:m
if ($LASTEXITCODE -ne 0) { throw "Setup build failed: $LASTEXITCODE" }

New-Item -ItemType Directory -Force -Path $Dist | Out-Null
$built = Join-Path $SetupDir 'bin\Release\net48\WinPreviewPlus-Setup.exe'
$dest = Join-Path $Dist 'WinPreviewPlus-Setup.exe'
Copy-Item $built $dest -Force

Write-Host 'Privacy scan (setup.exe)...'
$exeBytes = [IO.File]::ReadAllBytes($dest)
foreach ($n in $personalNeedles) {
  if (Test-ContainsNeedle $exeBytes $n) {
    throw "setup.exe contains personal marker"
  }
}
Write-Host 'Privacy scan setup.exe: OK'

$readme = @(
  'WinPreviewPlus',
  '==============',
  'One file: WinPreviewPlus-Setup.exe',
  '',
  'Install (GUI):  double-click -> Install',
  'Silent:         WinPreviewPlus-Setup.exe /install /silent',
  'Uninstall:      WinPreviewPlus-Setup.exe /uninstall',
  '',
  'After install: Explorer -> file -> Alt+P',
  'Log: %LOCALAPPDATA%\WinPreviewPlus\preview.log',
  'Files: %LOCALAPPDATA%\WinPreviewPlus\bin\',
  '',
  'Deps: .NET Framework 4.8 (required); WebView2 Runtime for video/SVG.',
  'Setup can download/install missing deps (UAC) then continue.',
  'Package: compiled DLLs only - no personal files or name watermarks.'
) -join "`r`n"
[IO.File]::WriteAllText((Join-Path $Dist 'README.txt'), $readme, (New-Object System.Text.UTF8Encoding $false))

Write-Host ''
Write-Host 'Staged files:'
Get-ChildItem $Stage -Recurse -File | ForEach-Object {
  Write-Host ('  ' + $_.FullName.Substring($Stage.Length + 1) + '  ' + $_.Length)
}
$size = (Get-Item $dest).Length
Write-Host ""
Write-Host ("OK: {0} ({1} MB)" -f $dest, [math]::Round($size/1MB, 2))

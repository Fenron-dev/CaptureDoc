# Builds the portable multi-platform package into dist/CaptureDoc (see DevVault ADR-002).
#   powershell -ExecutionPolicy Bypass -File tools/publish.ps1                       # Windows + Linux + macOS
#   powershell -ExecutionPolicy Bypass -File tools/publish.ps1 -Platforms win-x64     # only Windows
# macOS: bundle is ad-hoc unsigned when built on Windows → sign on a Mac: codesign --force --deep -s - CaptureDoc-macOS.app
param(
  [string[]]$Platforms = @("win-x64", "linux-x64", "osx-arm64"),
  [string]$Out = "dist/CaptureDoc",
  [switch]$Zip
)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo
$dotnet = if (Get-Command dotnet -ErrorAction SilentlyContinue) { "dotnet" } else { "$env:USERPROFILE\.dotnet\dotnet.exe" }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:AVALONIA_TELEMETRY_OPTOUT = "1"   # Avalonia.BuildServices: no build telemetry
$version = ([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.Version
$dist = Join-Path $repo $Out
New-Item -ItemType Directory -Force $dist | Out-Null

foreach ($rid in $Platforms) {
  Write-Host "== $rid"
  $pub = Join-Path $repo "artifacts/publish-$rid"
  if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }
  $r2r = if ($rid -like "win-*") { "true" } else { "false" }   # ReadyToRun cross-compilation is not reliable for other OSes
  & $dotnet publish src/CaptureDoc.App/CaptureDoc.App.csproj -c Release -r $rid -o $pub -nologo -v q -p:PublishReadyToRun=$r2r
  if ($LASTEXITCODE -ne 0) { throw "publish $rid failed" }
  $natives = Get-ChildItem $pub -File | Where-Object { $_.Extension -in ".dll", ".so", ".dylib" }

  if ($rid -like "win-*") {
    $runtime = Join-Path $dist "Runtime/$rid"
    New-Item -ItemType Directory -Force $runtime | Out-Null
    Get-ChildItem $runtime -File | Remove-Item -Force
    Copy-Item (Join-Path $pub "CaptureDoc.exe") (Join-Path $dist "CaptureDoc-Windows.exe") -Force
    $natives | ForEach-Object { Copy-Item $_.FullName $runtime -Force }
  }
  elseif ($rid -like "linux-*") {
    $runtime = Join-Path $dist "Runtime/$rid"
    New-Item -ItemType Directory -Force $runtime | Out-Null
    Get-ChildItem $runtime -File | Remove-Item -Force
    Copy-Item (Join-Path $pub "CaptureDoc") (Join-Path $dist "CaptureDoc-Linux") -Force
    $natives | ForEach-Object { Copy-Item $_.FullName $runtime -Force }
  }
  else {
    $app = Join-Path $dist "CaptureDoc-macOS.app"
    if (Test-Path $app) { Remove-Item $app -Recurse -Force }
    $macos = Join-Path $app "Contents/MacOS"
    New-Item -ItemType Directory -Force $macos, (Join-Path $app "Contents/Resources") | Out-Null
    Copy-Item (Join-Path $pub "CaptureDoc") (Join-Path $macos "CaptureDoc") -Force
    $natives | ForEach-Object { Copy-Item $_.FullName $macos -Force }   # inside the bundle next to the binary
    $plist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleName</key><string>CaptureDoc</string>
  <key>CFBundleDisplayName</key><string>CaptureDoc</string>
  <key>CFBundleIdentifier</key><string>internal.capturedoc</string>
  <key>CFBundleExecutable</key><string>CaptureDoc</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$version</string>
  <key>CFBundleVersion</key><string>$version</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSScreenCaptureUsageDescription</key><string>CaptureDoc nimmt Screenshots und Bildschirmvideos nur auf, wenn du es auslöst.</string>
  <key>NSMicrophoneUsageDescription</key><string>Optional: Mikrofon für Bildschirmvideos.</string>
</dict></plist>
"@
    [IO.File]::WriteAllText((Join-Path $app "Contents/Info.plist"), $plist, (New-Object Text.UTF8Encoding $false))
  }
}

# portable data layout + developer vault
foreach ($d in "Shared","Settings/Shared","Settings/Windows","Settings/MacOS","Settings/Linux","Projects","Media","Templates","Presets","Cache","Temp","Logs") {
  New-Item -ItemType Directory -Force (Join-Path $dist "Data/$d") | Out-Null
}
$vaultDst = Join-Path $dist "Data/DevVault"
if (Test-Path $vaultDst) { Remove-Item $vaultDst -Recurse -Force }
Copy-Item (Join-Path $repo "Data/DevVault") $vaultDst -Recurse
Copy-Item README.md, THIRD_PARTY_LICENSES.md $dist -Force

Get-ChildItem $dist -File | Where-Object { $_.Name -like "CaptureDoc-*" } | ForEach-Object { "{0,-28} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB) }
if (Test-Path (Join-Path $dist "CaptureDoc-macOS.app")) { "CaptureDoc-macOS.app            Bundle" }

if ($Zip) {
  # zip with unix permissions (executables keep +x on macOS/Linux)
  $zipPath = Join-Path $repo "dist/CaptureDoc-$version-portable.zip"
  python -I (Join-Path $PSScriptRoot "make_zip.py") $dist $zipPath
  if ($LASTEXITCODE -ne 0) { throw "zip failed" }
}
Write-Host "OK  $dist"

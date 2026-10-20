# Builds the portable Windows package into dist/CaptureDoc (see DevVault ADR-002).
# Usage: powershell -File tools/publish-windows.ps1 [-Rid win-x64]
param([string]$Rid = "win-x64", [string]$Out = "dist/CaptureDoc")
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo
$dotnet = if (Get-Command dotnet -ErrorAction SilentlyContinue) { "dotnet" } else { "$env:USERPROFILE\.dotnet\dotnet.exe" }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:AVALONIA_TELEMETRY_OPTOUT = "1"   # Avalonia.BuildServices: no build telemetry

$pub = Join-Path $repo "artifacts/publish-$Rid"
if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }
& $dotnet publish src/CaptureDoc.App/CaptureDoc.App.csproj -c Release -r $Rid -o $pub -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

$dist = Join-Path $repo $Out
$runtime = Join-Path $dist "Runtime/$Rid"
New-Item -ItemType Directory -Force $dist, $runtime | Out-Null
# keep user data (Data/) when re-publishing, replace binaries only
Get-ChildItem $runtime -File -ErrorAction SilentlyContinue | Remove-Item -Force

Copy-Item (Join-Path $pub "CaptureDoc.exe") (Join-Path $dist "CaptureDoc-Windows.exe") -Force
Get-ChildItem $pub -File | Where-Object { $_.Extension -eq ".dll" } | ForEach-Object { Copy-Item $_.FullName $runtime -Force }
$unexpected = Get-ChildItem $pub -File | Where-Object { $_.Name -ne "CaptureDoc.exe" -and $_.Extension -notin ".dll",".pdb" }
if ($unexpected) { Write-Warning ("Unerwartete Dateien im Publish-Ordner: " + ($unexpected.Name -join ", ")) }

# portable data layout + developer vault
foreach ($d in "Shared","Settings/Shared","Settings/Windows","Settings/MacOS","Settings/Linux","Projects","Media","Templates","Presets","Cache","Temp","Logs") {
  New-Item -ItemType Directory -Force (Join-Path $dist "Data/$d") | Out-Null
}
$vaultSrc = Join-Path $repo "Data/DevVault"
$vaultDst = Join-Path $dist "Data/DevVault"
if (Test-Path $vaultDst) { Remove-Item $vaultDst -Recurse -Force }
Copy-Item $vaultSrc $vaultDst -Recurse
Copy-Item README.md, THIRD_PARTY_LICENSES.md $dist -Force

$exe = Get-Item (Join-Path $dist "CaptureDoc-Windows.exe")
$rt = (Get-ChildItem $runtime | Measure-Object Length -Sum).Sum
Write-Host ("OK  {0}  EXE {1:N1} MB  Runtime {2:N1} MB ({3} Dateien)" -f $dist, ($exe.Length/1MB), ($rt/1MB), (Get-ChildItem $runtime).Count)

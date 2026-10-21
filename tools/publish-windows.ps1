# Kept for compatibility: Windows-only package. Use tools/publish.ps1 for all platforms.
param([string]$Out = "dist/CaptureDoc")
& (Join-Path $PSScriptRoot "publish.ps1") -Platforms win-x64 -Out $Out

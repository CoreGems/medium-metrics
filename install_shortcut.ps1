<#
.SYNOPSIS
    Creates a Start Menu shortcut for Medium Metrics pointing at the published exe.

.DESCRIPTION
    Builds the self-contained release exe if it is missing (or when -Build is given),
    then writes "Medium Metrics.lnk" into the current user's Start Menu Programs
    folder so the app is searchable and pinnable. Run build_release.ps1 again later
    to refresh the exe; the shortcut path stays the same.

.PARAMETER Build
    Force a fresh release build before creating the shortcut.

.EXAMPLE
    .\install_shortcut.ps1
    .\install_shortcut.ps1 -Build
#>
[CmdletBinding()]
param(
    [switch]$Build
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$exe  = Join-Path $root 'publish\win-x64\MediumMetrics.exe'

if ($Build -or -not (Test-Path $exe)) {
    Write-Host "Building release exe..." -ForegroundColor Cyan
    & (Join-Path $root 'build_release.ps1')
}
if (-not (Test-Path $exe)) { throw "Published exe not found at $exe. Run .\build_release.ps1 first." }

$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$lnkPath   = Join-Path $startMenu 'Medium Metrics.lnk'

$ws  = New-Object -ComObject WScript.Shell
$lnk = $ws.CreateShortcut($lnkPath)
$lnk.TargetPath       = $exe
$lnk.WorkingDirectory = Split-Path $exe
$lnk.IconLocation     = "$exe,0"
$lnk.Description       = 'Medium Metrics - local Medium readership stats'
$lnk.Save()

Write-Host "Shortcut created: $lnkPath" -ForegroundColor Green
Write-Host "Find 'Medium Metrics' in the Start Menu / Windows search."

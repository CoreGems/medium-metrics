<#
.SYNOPSIS
    Creates a Start Menu shortcut for Medium Metrics.

.DESCRIPTION
    Two shortcut flavours:
      (default)  "Medium Metrics"       -> the published self-contained exe. No build on
                                           launch; builds the release exe first if it's
                                           missing or when -Build is given.
      -Dev       "Medium Metrics (dev)" -> runs launch_dev.ps1, which does a fast incremental
                                           Debug build on EVERY launch and then starts the
                                           freshly built exe. Use this while developing so a
                                           click always reflects your latest source edits.

.PARAMETER Build
    Force a fresh release build before creating the (release) shortcut.

.PARAMETER Dev
    Create/refresh the "Medium Metrics (dev)" shortcut that rebuilds on each launch.

.EXAMPLE
    .\install_shortcut.ps1            # release shortcut (builds the exe if needed)
    .\install_shortcut.ps1 -Build     # force a fresh release build, then the shortcut
    .\install_shortcut.ps1 -Dev       # dev shortcut: rebuilds from source on every launch
#>
[CmdletBinding()]
param(
    [switch]$Build,
    [switch]$Dev
)

$ErrorActionPreference = 'Stop'
$root      = $PSScriptRoot
$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$ws        = New-Object -ComObject WScript.Shell

if ($Dev) {
    # Dev shortcut: the target is the LAUNCHER SCRIPT, not the exe. launch_dev.ps1 does an
    # incremental Debug build and then runs, so every click picks up the latest source. (This
    # is the fix for a dev shortcut that pointed straight at the exe and never rebuilt.)
    $launch = Join-Path $root 'launch_dev.ps1'
    if (-not (Test-Path $launch)) { throw "launch_dev.ps1 not found at $launch." }

    $lnkPath = Join-Path $startMenu 'Medium Metrics (dev).lnk'
    $lnk = $ws.CreateShortcut($lnkPath)
    $lnk.TargetPath       = Join-Path $PSHOME 'powershell.exe'
    $lnk.Arguments        = "-NoProfile -ExecutionPolicy Bypass -File `"$launch`""
    $lnk.WorkingDirectory = $root
    $devExe = Join-Path $root 'src\bin\Debug\net8.0-windows\MediumMetrics.exe'
    if (Test-Path $devExe) { $lnk.IconLocation = "$devExe,0" }
    $lnk.Description      = 'Medium Metrics (dev) - rebuilds from source on each launch'
    $lnk.Save()

    Write-Host "Dev shortcut created: $lnkPath" -ForegroundColor Green
    Write-Host "It runs launch_dev.ps1 (incremental build + run) on every launch."
    return
}

# Release shortcut: points straight at the published self-contained exe (no build on launch).
$exe = Join-Path $root 'publish\win-x64\MediumMetrics.exe'
if ($Build -or -not (Test-Path $exe)) {
    Write-Host "Building release exe..." -ForegroundColor Cyan
    & (Join-Path $root 'build_release.ps1')
}
if (-not (Test-Path $exe)) { throw "Published exe not found at $exe. Run .\build_release.ps1 first." }

$lnkPath = Join-Path $startMenu 'Medium Metrics.lnk'
$lnk = $ws.CreateShortcut($lnkPath)
$lnk.TargetPath       = $exe
$lnk.WorkingDirectory = Split-Path $exe
$lnk.IconLocation     = "$exe,0"
$lnk.Description       = 'Medium Metrics - local Medium readership stats'
$lnk.Save()

Write-Host "Shortcut created: $lnkPath" -ForegroundColor Green
Write-Host "Find 'Medium Metrics' in the Start Menu / Windows search."

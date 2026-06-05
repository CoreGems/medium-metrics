<#
.SYNOPSIS
    Builds and launches the Medium Metrics desktop app.

.DESCRIPTION
    Convenience launcher for local use. Restores/builds the WPF project and runs it.
    On first run the app starts in demo mode (sample data); click "Sign in to Medium"
    to load your real stats.

.PARAMETER Release
    Build and run in Release configuration instead of Debug.

.PARAMETER NoBuild
    Skip the build step and just run the last build (faster restarts).

.EXAMPLE
    .\start_app.ps1
    .\start_app.ps1 -Release
    .\start_app.ps1 -NoBuild
#>
[CmdletBinding()]
param(
    [switch]$Release,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'

# Always operate relative to this script's location, regardless of caller's CWD.
$root    = $PSScriptRoot
$project = Join-Path $root 'src\MediumMetrics.csproj'
$config  = if ($Release) { 'Release' } else { 'Debug' }

if (-not (Test-Path $project)) {
    throw "Project not found at '$project'. Run this script from the repo root."
}

# Verify the .NET SDK is available.
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The .NET SDK ('dotnet') was not found on PATH. Install .NET 8 SDK: https://dotnet.microsoft.com/download"
}

Write-Host "Medium Metrics - launching ($config)..." -ForegroundColor Cyan

$runArgs = @('run', '--project', $project, '--configuration', $config)
if ($NoBuild) { $runArgs += '--no-build' }

& dotnet @runArgs
exit $LASTEXITCODE

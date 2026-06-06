<#
.SYNOPSIS
    Builds the current source (incremental Debug) and launches Medium Metrics.

.DESCRIPTION
    Used by the "Medium Metrics (dev)" Start Menu shortcut so every launch picks
    up the latest source edits. Does a fast incremental `dotnet build` (not the
    full self-contained release publish) and then starts the freshly built exe.
    On build failure it keeps the console open so the error is visible.
#>
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$exe  = Join-Path $root 'src\bin\Debug\net8.0-windows\MediumMetrics.exe'

Write-Host "Building Medium Metrics (dev)..." -ForegroundColor Cyan
& dotnet build (Join-Path $root 'src\MediumMetrics.csproj') -c Debug -v quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed (exit $LASTEXITCODE). Press Enter to close." -ForegroundColor Red
    Read-Host
    exit $LASTEXITCODE
}

if (-not (Test-Path $exe)) { throw "Built exe not found at $exe." }
Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe)

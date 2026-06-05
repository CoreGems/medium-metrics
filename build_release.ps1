<#
.SYNOPSIS
    Publishes Medium Metrics as a single-file, self-contained Windows executable.

.DESCRIPTION
    Produces a portable .exe under .\publish that runs without a separate .NET
    install. The WebView2 *runtime* is still required on the target machine
    (preinstalled on current Windows 10/11).

.PARAMETER Runtime
    Target runtime identifier. Default: win-x64.

.EXAMPLE
    .\build_release.ps1
    .\build_release.ps1 -Runtime win-arm64
#>
[CmdletBinding()]
param(
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$root    = $PSScriptRoot
$project = Join-Path $root 'src\MediumMetrics.csproj'
$outDir  = Join-Path $root "publish\$Runtime"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The .NET SDK ('dotnet') was not found on PATH. Install .NET 8 SDK first."
}

Write-Host "Publishing Medium Metrics ($Runtime) -> $outDir" -ForegroundColor Cyan

dotnet publish $project `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    --output $outDir `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true

if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE" }

$exe = Join-Path $outDir 'MediumMetrics.exe'
if (Test-Path $exe) {
    $sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host "Done: $exe ($sizeMb MB)" -ForegroundColor Green
} else {
    throw "Expected output not found: $exe"
}

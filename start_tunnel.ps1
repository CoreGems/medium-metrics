<#
.SYNOPSIS
    Brings up a Cloudflare Tunnel to the local Medium Metrics API.

.DESCRIPTION
    Runs an already-configured *named* Cloudflare tunnel (cloudflared) that forwards
    Cloudflare's edge to this PC's loopback API at http://localhost:<port>. This is the
    public ingress that lets an OpenAI Custom GPT reach the local, read-only API.

    This is deliberately SEPARATE from start_app.ps1: the tunnel is the public exposure
    and is started consciously, on its own lifecycle. The local API itself is enabled in
    the app (Settings -> Local API). See OPENAI_CUSTOM_GPT.md (sections 10-11).

    One-time setup for a named tunnel (do once; afterwards this script just runs it):
        winget install --id Cloudflare.cloudflared
        cloudflared tunnel login
        cloudflared tunnel create medium-metrics
        cloudflared tunnel route dns medium-metrics metrics.example.dev
        # Create %USERPROFILE%\.cloudflared\config.yml with an ingress rule pointing at
        #   http://localhost:<port>   (full example in OPENAI_CUSTOM_GPT.md section 10)

    For an always-on tunnel that survives reboots, install it as a service instead of
    running this script:
        cloudflared service install

.PARAMETER Name
    Name of the configured named tunnel to run. Default: 'medium-metrics'.

.PARAMETER Port
    Local API port to probe (and, with -Quick, to forward to). Default: the app's
    ApiPort from settings.json, else 8765.

.PARAMETER Quick
    Use an ephemeral quick tunnel (random *.trycloudflare.com URL) instead of the named
    tunnel. Handy for a one-off test, but the URL changes every run, so you would have to
    re-paste it into the GPT Action each time. Not for steady use.

.EXAMPLE
    .\start_tunnel.ps1
    .\start_tunnel.ps1 -Name my-tunnel
    .\start_tunnel.ps1 -Quick
#>
[CmdletBinding()]
param(
    [string]$Name = 'medium-metrics',
    [int]$Port = 0,
    [switch]$Quick
)

$ErrorActionPreference = 'Stop'

# Verify cloudflared is available.
if (-not (Get-Command cloudflared -ErrorAction SilentlyContinue)) {
    throw "cloudflared was not found on PATH. Install it with: winget install --id Cloudflare.cloudflared  (see OPENAI_CUSTOM_GPT.md section 10)."
}

# Resolve the port: an explicit -Port wins, else read ApiPort from the app's settings.json,
# else fall back to the default. settings.json always lives in the default data folder.
if ($Port -le 0) {
    $Port = 8765
    $settingsPath = Join-Path $env:LOCALAPPDATA 'MediumMetrics\settings.json'
    if (Test-Path $settingsPath) {
        try {
            $settings = Get-Content $settingsPath -Raw | ConvertFrom-Json
            if ($settings.ApiPort) { $Port = [int]$settings.ApiPort }
            if (($settings.PSObject.Properties.Name -contains 'ApiEnabled') -and (-not $settings.ApiEnabled)) {
                Write-Host "Note: the local API is disabled in the app (Settings -> Local API). The tunnel will return 502 until you enable it." -ForegroundColor Yellow
            }
        } catch {
            Write-Host "Could not read settings.json ($($_.Exception.Message)); using port $Port." -ForegroundColor Yellow
        }
    }
}

# Warn (don't fail) if nothing is listening locally yet — the tunnel can start first and
# will simply 502 until the app is open with the API enabled.
try {
    Invoke-WebRequest -Uri "http://localhost:$Port/v1/health" -UseBasicParsing -TimeoutSec 2 | Out-Null
    Write-Host "Local API is up on http://localhost:$Port/v1." -ForegroundColor Green
} catch {
    Write-Host "Local API not responding on http://localhost:$Port/v1 - open the app and enable Settings -> Local API. Continuing anyway." -ForegroundColor Yellow
}

# Run the tunnel in the foreground (Ctrl+C to stop).
if ($Quick) {
    Write-Host "Starting a QUICK Cloudflare tunnel to http://localhost:$Port (random URL; changes each run)..." -ForegroundColor Cyan
    & cloudflared tunnel --url "http://localhost:$Port"
} else {
    Write-Host "Starting named Cloudflare tunnel '$Name' -> http://localhost:$Port (Ctrl+C to stop)..." -ForegroundColor Cyan
    & cloudflared tunnel run $Name
}
exit $LASTEXITCODE

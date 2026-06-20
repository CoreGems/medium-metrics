<#
.SYNOPSIS
    Brings up a public tunnel to the local Medium Metrics API (Tailscale Funnel by default).

.DESCRIPTION
    Exposes this PC's loopback API at http://localhost:<port> over a public HTTPS tunnel so
    an OpenAI Custom GPT can reach the local, read-only API. With no switch it uses Tailscale
    Funnel (the path that worked end-to-end); -Cloudflare / -Quick / -Ngrok select the others.

    TODO: remove the -Cloudflare, -Quick, and -Ngrok options (and their -Name dependency).
    None of them reach a cloud GPT anymore — quick tunnels 403 OpenAI's egress, ngrok-free
    serves broken TLS, and the named Cloudflare path is unused now that Tailscale Funnel is
    the working default. Keep only the Tailscale path.

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
    Name of the configured named Cloudflare tunnel to run with -Cloudflare. Default: 'medium-metrics'.
    TODO: remove with -Cloudflare (see DESCRIPTION).

.PARAMETER Port
    Local API port to probe and forward to. Default: the app's ApiPort from settings.json,
    else 8780.

.PARAMETER Cloudflare
    TODO: remove — superseded by Tailscale Funnel (see DESCRIPTION).
    Use an already-configured *named* Cloudflare tunnel (cloudflared) instead of the default
    Tailscale Funnel. Runs `cloudflared tunnel run <Name>`. One-time setup for a named tunnel:
        winget install --id Cloudflare.cloudflared
        cloudflared tunnel login
        cloudflared tunnel create medium-metrics
        cloudflared tunnel route dns medium-metrics metrics.example.dev
        # Create %USERPROFILE%\.cloudflared\config.yml with an ingress rule pointing at
        #   http://localhost:<port>   (full example in OPENAI_CUSTOM_GPT.md section 10)
    For an always-on tunnel that survives reboots, install it as a service instead:
        cloudflared service install

.PARAMETER Quick
    TODO: remove — quick tunnels 403 OpenAI's egress (see DESCRIPTION).
    Use an ephemeral Cloudflare quick tunnel (random *.trycloudflare.com URL) instead of the
    default Tailscale Funnel. Handy for a one-off test, but the URL changes every run. NOTE:
    the shared trycloudflare.com zone may return 403 to cloud callers like OpenAI (bot
    mitigation); use the default Tailscale Funnel, a named tunnel (-Cloudflare), or -Ngrok.

.PARAMETER Ngrok
    TODO: remove — ngrok-free now serves broken TLS to cloud callers (see DESCRIPTION).
    Use ngrok instead of cloudflared (a different edge that doesn't bot-block OpenAI).
    Runs `ngrok http <port> --host-header=localhost`. Requires ngrok on PATH (and a
    one-time `ngrok config add-authtoken <token>` from a free ngrok account).

.PARAMETER Tailscale
    Explicitly select Tailscale Funnel. This is already the DEFAULT when no tunnel switch is
    given, so the flag is optional. Runs `tailscale funnel <port>`, which exposes the API at a
    stable https://<machine>.<tailnet>.ts.net URL with auto-provisioned TLS, isn't bot-blocked
    by cloud callers, and forwards in a way HttpListener accepts with NO Host rewrite — this is
    the path that worked end-to-end (see the tunnel saga). Requires the Tailscale CLI on PATH
    and Funnel enabled for the tailnet (allowed in the tailnet ACLs / admin console; the
    command prints the URL).

.EXAMPLE
    .\start_tunnel.ps1                     # Tailscale Funnel (default)
    .\start_tunnel.ps1 -Tailscale          # same, explicit
    .\start_tunnel.ps1 -Cloudflare         # named Cloudflare tunnel
    .\start_tunnel.ps1 -Cloudflare -Name my-tunnel
    .\start_tunnel.ps1 -Quick
    .\start_tunnel.ps1 -Ngrok
#>
[CmdletBinding()]
param(
    [string]$Name = 'medium-metrics', # TODO: remove with -Cloudflare (only used by the named tunnel)
    [int]$Port = 0,
    [switch]$Cloudflare,              # TODO: remove — superseded by Tailscale Funnel
    [switch]$Quick,                   # TODO: remove — 403s OpenAI's egress
    [switch]$Ngrok,                   # TODO: remove — broken TLS to cloud callers
    [switch]$Tailscale
)

$ErrorActionPreference = 'Stop'

# Resolve the tailscale CLI: the Windows GUI client ships tailscale.exe but doesn't add it to
# PATH, so fall back to the standard install location before giving up.
function Resolve-Tailscale {
    $cmd = Get-Command tailscale -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($p in @("$env:ProgramFiles\Tailscale\tailscale.exe", "${env:ProgramFiles(x86)}\Tailscale\tailscale.exe")) {
        if (Test-Path $p) { return $p }
    }
    return $null
}

# Verify the chosen tunnel tool is available.
# TODO: drop the -Ngrok / -Cloudflare / -Quick arms below; only the Tailscale (else) arm works.
$tailscaleExe = $null
if ($Ngrok) {
    if (-not (Get-Command ngrok -ErrorAction SilentlyContinue)) {
        throw "ngrok was not found on PATH. Install it from https://ngrok.com/download (or: winget install ngrok.ngrok), then run 'ngrok config add-authtoken <token>'."
    }
} elseif ($Cloudflare -or $Quick) {
    if (-not (Get-Command cloudflared -ErrorAction SilentlyContinue)) {
        throw "cloudflared was not found on PATH. Install it with: winget install --id Cloudflare.cloudflared  (see OPENAI_CUSTOM_GPT.md section 10)."
    }
} else {
    $tailscaleExe = Resolve-Tailscale
    if (-not $tailscaleExe) {
        throw "tailscale was not found on PATH or in Program Files. Install it from https://tailscale.com/download (or: winget install tailscale.tailscale), sign in, and enable Funnel for the tailnet."
    }
}

# Resolve the port: an explicit -Port wins, else read ApiPort from the app's settings.json,
# else fall back to the default. settings.json always lives in the default data folder.
if ($Port -le 0) {
    $Port = 8780
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
# TODO: drop the -Ngrok / -Quick / -Cloudflare arms below; only the Tailscale (else) arm works.
if ($Ngrok) {
    Write-Host "Starting ngrok -> http://localhost:$Port. Use the printed https URL + /v1 as the GPT Action server URL." -ForegroundColor Cyan
    & ngrok http $Port --host-header=localhost
} elseif ($Quick) {
    Write-Host "Starting a QUICK Cloudflare tunnel to http://localhost:$Port (random URL; changes each run)..." -ForegroundColor Cyan
    & cloudflared tunnel --url "http://localhost:$Port" --http-host-header localhost
} elseif ($Cloudflare) {
    Write-Host "Starting named Cloudflare tunnel '$Name' -> http://localhost:$Port (Ctrl+C to stop)..." -ForegroundColor Cyan
    & cloudflared tunnel run $Name
} else {
    # Tailscale Funnel config is persistent (lives in tailscaled, survives reboots), so it may
    # already be on from a previous run. If it's already proxying our port, just report the URL
    # rather than re-running `funnel <port>`, which 400s with "listener already exists for port 443".
    $status = (& $tailscaleExe funnel status 2>&1 | Out-String)
    if ($status -match "127\.0\.0\.1:${Port}\b") {
        $url = ([regex]::Match($status, 'https://\S+\.ts\.net')).Value
        Write-Host "Tailscale Funnel is already on for port ${Port}: $url" -ForegroundColor Green
        Write-Host "Use $url/v1 as the GPT Action server URL. It persists across reboots; to take it down run: `"$tailscaleExe`" funnel reset" -ForegroundColor Cyan
        exit 0
    }
    Write-Host "Starting Tailscale Funnel (background, persistent) -> http://localhost:$Port..." -ForegroundColor Cyan
    & $tailscaleExe funnel --bg $Port
    if ($LASTEXITCODE -eq 0) {
        $url = ([regex]::Match((& $tailscaleExe funnel status 2>&1 | Out-String), 'https://\S+\.ts\.net')).Value
        Write-Host "Funnel on: use $url/v1 as the GPT Action server URL. It persists across reboots; to take it down run: `"$tailscaleExe`" funnel reset" -ForegroundColor Green
    }
}
exit $LASTEXITCODE

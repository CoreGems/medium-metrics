# What went wrong with ngrok (and tunneling the local API to a Custom GPT)

A debugging post-mortem from wiring the **Medium Metrics local API** (`http://localhost:8780/v1`,
see [OPENAI_CUSTOM_GPT.md](OPENAI_CUSTOM_GPT.md)) to an **OpenAI Custom GPT**. The GPT calls
Actions from OpenAI's cloud, so the loopback API needs a public HTTPS tunnel. We tried
Cloudflare and ngrok; this file records every wall we hit, the cause, and the fix.

## TL;DR

- **The app's API works perfectly.** Every failure was in the *tunnel/transport* layer, never the app.
- **ngrok free was the dead end:** the ephemeral `*.ngrok-free.dev` domain serves **broken TLS** —
  the handshake fails for **three independent clients** (Python, Chrome, and an external `curl`),
  on **both IPv4 and IPv6**. The ngrok agent connects fine; its public TLS endpoint does not.
- Before that we cleared four other ngrok problems in a row: config version, config schema,
  invalid authtoken, and a too-old agent.
- **What finally worked: Tailscale Funnel.** Stable `*.ts.net` URL, auto-provisioned TLS, no
  bot-block, data stays on the PC, and the Host header is accepted with **no app change**
  (verified `/v1/health` → 200 through the funnel from an external probe). A named Cloudflare
  tunnel on your own domain is the equivalent if you have a domain.

## Setup / context

- App: WPF, exposes a read-only `HttpListener` API bound to **`127.0.0.1:8780`** only.
- **App constraint that bites every tunnel:** `HttpListener` answers only to `Host: localhost`
  (and `127.0.0.1`). A tunnel forwards the *public* hostname as the `Host` header by default,
  so http.sys replies **`400 Bad Request — Invalid Hostname`** before our code runs. Every tunnel
  must rewrite the Host header to `localhost`:
  - cloudflared quick: `--http-host-header localhost`
  - cloudflared named: `originRequest: { httpHostHeader: localhost }` in `config.yml`
  - ngrok: `--host-header=localhost`
- ngrok agent: **3.3.1** at the start (installed via winget), updated to **3.39.6** during this.
- Windows 11, PowerShell, app + tunnel run by the user.

## The ngrok error chain (chronological)

| # | Command / step | Error | Cause | Fix |
|---|---|---|---|---|
| 1 | `start_tunnel.ps1 -Ngrok` | `Error reading configuration file 'ngrok.yml': unknown version '3'. valid versions are: [1 2]` | `ngrok.yml` had `version: 3`, but agent 3.3.1 only understands config versions 1–2 | Lower the config version (and ultimately rewrite it) |
| 2 | `ngrok update` | Same "unknown version '3'" error | `update` reads the config **first**, so a bad config blocks the updater too (chicken-and-egg) | Fix the config before updating |
| 3 | Set `version: "2"` (config block left intact) | `YAML parsing error: line 2: field agent not found in type config.v2yamlConfig` | The config was *genuinely v3 format* — token nested under an `agent:` block. v2 schema has a **top-level** `authtoken:` and no `agent:` | Flatten to real v2 |
| 4 | Rewrote as `version: "2"` + top-level `authtoken:` | `ngrok config check` → **"Valid configuration file"** ✓ | — | Config now parses on 3.3.1 |
| 5 | `winget upgrade ngrok.ngrok` | `No available upgrade found` | winget's ngrok manifest is **stuck at 3.3.1** — it can't deliver a current agent | Use ngrok's own updater instead (step 8) |
| 6 | Start tunnel | `ERR_NGROK_107` — authtoken invalid | The token in the file was stale / not valid for the account | `ngrok config add-authtoken <token>` from the dashboard |
| 7 | Start tunnel (valid token) | `ERR_NGROK_121` — agent "3.3.1" too old; account minimum is **3.20.0** (free accounts are now version-gated) | Agent far below the account's minimum | Update the agent |
| 8 | `ngrok update` (config now valid) | `Update to version 3.39.6 successful!` ✓ | — | Agent current |
| 9 | Start tunnel → online, `https://blasphemy-commodity-cytoplasm.ngrok-free.dev -> http://localhost:8780` | **TLS handshake fails** (see below) | ngrok edge serves broken TLS for the ephemeral `*.ngrok-free.dev` domain | Restart for a new domain, or switch to Cloudflare named |

## The TLS wall (current blocker)

The agent reports the session **online** and forwarding correctly, but nothing can complete a
TLS handshake to the public URL:

- **Python (`requests`/`urllib`, conda):** `[SSL] record layer failure (_ssl.c:1016)`
- **Chrome:** `ERR_SSL_PROTOCOL_ERROR` — "sent an invalid response"
- **External `curl` (Windows schannel):** `curl: (35) schannel: next InitializeSecurityContext failed: SEC_E_INVALID_TOKEN (0x80090308)`, `HTTP 000` — on **both** `-6` and `-4`

Why this rules out the usual suspects:

- **Not the user's machine / AV / OpenSSL:** an *external* `curl` (different network) also fails,
  and that same curl had earlier reached a Cloudflare tunnel over TLS without issue.
- **Not IPv6:** forcing IPv4 (`curl -4`) gives the identical `SEC_E_INVALID_TOKEN`.
- **Not the `--host-header` flag or our config:** those are HTTP-layer; TLS fails *before* any HTTP.
- **Not the app:** TLS terminates at ngrok's edge; the request never reaches localhost.

Three different TLS stacks rejecting the first server message ("record layer failure" /
`ERR_SSL_PROTOCOL_ERROR` / `SEC_E_INVALID_TOKEN`) means **ngrok's edge sent bytes that aren't a
valid TLS ServerHello** for that ephemeral domain — i.e., a broken/unprovisioned cert or edge
state on `*.ngrok-free.dev`. Verbose probe confirmed it dies right at `InitializeSecurityContext`:

```
* Connected to blasphemy-commodity-cytoplasm.ngrok-free.dev (2600:1f16:...) port 443
* schannel: next InitializeSecurityContext failed: SEC_E_INVALID_TOKEN (0x80090308)
curl: (35) ... SEC_E_INVALID_TOKEN
```

## Root causes, grouped

1. **Version drift / gating.** ngrok now enforces a **minimum agent version (3.20.0)** on free
   accounts, but the box had **3.3.1** (and **winget's manifest can't update past 3.3.1**). Only
   `ngrok update` (ngrok's own updater) gets a current agent — and it refuses to run until the
   config parses, so the config must be fixed first.
2. **Config schema mismatch.** A v3-format `ngrok.yml` (with an `agent:` block) on a v1/v2-only
   agent. v2 wants a flat top-level `authtoken:`.
3. **Stale authtoken** (`ERR_NGROK_107`) — fixed by re-adding from the dashboard.
4. **Broken edge TLS on the free ephemeral domain** (`*.ngrok-free.dev`) — the current wall, and
   the one we can't fix from our side.

## App-side lesson (provider-independent)

The single most reusable finding: **a loopback `HttpListener` rejects any `Host` header except
`localhost`/`127.0.0.1`.** Behind *any* reverse proxy/tunnel you must rewrite the Host header, or
you get `400 Invalid Hostname`. We hit this on Cloudflare first and fixed it with
`--http-host-header localhost` (quick) and `originRequest.httpHostHeader` (named); ngrok needs
`--host-header=localhost`. (A wildcard `http://+:8780/` prefix would also accept any host but
requires an admin `netsh http add urlacl` and exposes the port on the LAN — not worth it.)

## Diagnostic commands used (handy for next time)

```powershell
# Is the config even valid for this agent?
ngrok config check

# What agent version is installed, and where?
ngrok version
(Get-Command ngrok).Source

# Re-set a valid token (from https://dashboard.ngrok.com/get-started/your-authtoken)
ngrok config add-authtoken <TOKEN>

# Update the agent (only works once the config parses)
ngrok update
```
```bash
# Probe a tunnel's TLS + HTTP from OUTSIDE (the best proxy for "can OpenAI reach it")
curl -v -sS -m 25 https://<host>/v1/health        # -v shows the TLS handshake
curl -4 ... ; curl -6 ...                          # isolate IPv4 vs IPv6
```
`/v1/health` is unauthenticated, so it's the safe probe — a 200 there means the whole path
(edge TLS → tunnel → app) is good.

## ngrok error-code reference (what we saw)

- **`ERR_NGROK_107`** — authtoken is well-formed but invalid for any account → re-add the token.
- **`ERR_NGROK_121`** — agent version below the account minimum → `ngrok update` (or pay/upgrade plan).
- **`unknown version '3' / valid versions are: [1 2]`** — config newer than the agent → update agent or downgrade config.
- **`field agent not found in type config.v2yamlConfig`** — v3-shaped config read as v2 → flatten to top-level `authtoken`.
- **TLS `SEC_E_INVALID_TOKEN` / `record layer failure` / `ERR_SSL_PROTOCOL_ERROR`** — edge isn't serving valid TLS → restart for a new domain, or change providers.

## A known-good v2 `ngrok.yml`

`C:\Users\<you>\AppData\Local\ngrok\ngrok.yml`:
```yaml
version: "2"
authtoken: <your-token-from-the-dashboard>
```
(Current ngrok also accepts a v3 file with `version: 3` + `agent: { authtoken: ... }`, but v2 is
backward-compatible across agent versions, so it's the safer choice when versions are uncertain.)

## Recommendation / next steps

1. **Quick gamble:** restart ngrok (`Ctrl+C`, then `.\start_tunnel.ps1 -Ngrok`) to get a *new*
   `*.ngrok-free.dev` domain and re-probe `/v1/health` from outside. If TLS is healthy on the new
   domain, you're done. If `*.ngrok-free.dev` has a broader TLS problem, a new domain won't help.
2. **Reliable fix — named Cloudflare tunnel on your own domain.** Solid TLS, a permanent URL, and
   no bot-block (your own zone has Bot Fight Mode off by default — unlike the shared
   `trycloudflare.com`, which returned **403** to OpenAI's cloud fetcher). Steps are in
   [OPENAI_CUSTOM_GPT.md §10](OPENAI_CUSTOM_GPT.md); the piece missed earlier was the one-time
   `cloudflared tunnel login` (which creates `cert.pem`).

## Tunnel scorecard for this use case

| Option | Result here | Notes |
|---|---|---|
| Cloudflare **quick** (`trycloudflare.com`) | ❌ **403 to OpenAI** | Reachable from a browser/curl, but the shared zone bot-blocks OpenAI's datacenter IPs; you can't tune that zone |
| Cloudflare **named** (your domain) | ✅ Recommended | Stable URL, solid TLS, no bot-block; needs a domain + one-time `cloudflared tunnel login` |
| ngrok **free** | ❌ Blocked on edge TLS | Cleared version/schema/token/agent issues, then `*.ngrok-free.dev` failed TLS for all clients |
| ngrok **paid** (reserved domain) | untested | Removes version gating + the interstitial; a reserved domain is stable |
| **Tailscale Funnel** | ✅ **What we used** | Stable `*.ts.net` URL, auto TLS, no bot-block, no domain needed, Host header accepted as-is, data stays local. One-time: enable Funnel in the admin console |

*The app, its API, the auth (bearer key), and the OpenAPI schema were all verified working
throughout — `/v1/health` returns 200 over loopback and (when the tunnel TLS is healthy) from
outside. The only unsolved item is a transport-layer TLS failure on ngrok's free edge.*

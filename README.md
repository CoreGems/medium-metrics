# Medium Metrics

A small, **local-first Windows desktop app** that logs readership stats for your
own Medium account and keeps an append-only history so you can see how your
stories grow over time. Medium's own stats page only shows *current* numbers —
this keeps a private "black box recorder" of them on your machine.

- Logs followers and per-story **views / reads / read-ratio / claps**.
- Shows the latest snapshot as a list.
- **Refresh** appends **one timestamped line** to a history log (`report.csv`).
- 100% local — no cloud, no third-party analytics. Your data never leaves your PC.

> Status: v0.1.0 — works end to end (sign in → refresh → history). The stats
> endpoints are **unofficial** (see [docs/private/medium-api-notes.md](docs/private/medium-api-notes.md)).

## Requirements

- Windows 10/11.
- [WebView2 runtime](https://developer.microsoft.com/microsoft-edge/webview2/) —
  preinstalled on current Windows. Used for the one-time Medium sign-in.
- To build from source: [.NET 8 SDK](https://dotnet.microsoft.com/download).

## Run it

### From source (dev)
```powershell
.\start_app.ps1            # Debug build + run
.\start_app.ps1 -Release   # Release config
.\start_app.ps1 -NoBuild   # skip build, fast restart
```

### Portable build (single .exe)
```powershell
.\build_release.ps1                 # -> publish\win-x64\MediumMetrics.exe
.\build_release.ps1 -Runtime win-arm64
```
The result is a self-contained executable; no separate .NET install needed.

## First run

1. Launch the app. It starts in **demo mode** with sample data so you can see the
   layout immediately.
2. Click **Sign in to Medium**. An embedded browser opens — log into your Medium
   account. When you reach your stats page the session is captured automatically
   (or click **Use this account** once signed in).
3. Click **Refresh**. The app fetches your current stats, shows them in the list,
   and appends one line to the history log.
4. Click **Refresh** again whenever you want a new history snapshot.

Your session cookie is encrypted at rest with **Windows DPAPI** — only your
Windows user on this PC can decrypt it.

## Where data lives

Everything is under `%LOCALAPPDATA%\MediumMetrics\` (open it from **Settings →
Open data folder**):

| File | Purpose |
|------|---------|
| `report.csv` | Append-only history — one row per Refresh |
| `latest.json` | Full last snapshot (per-story detail) |
| `session.bin` | DPAPI-encrypted Medium session cookie |
| `settings.json` | Window placement and preferences |
| `app.log` | Diagnostic log |

## Development

```powershell
dotnet build                 # build app + tests
dotnet test                  # run the unit tests
```

Project layout and the milestone plan are in [DEV_PLAN.md](DEV_PLAN.md); the
overall concept and tech-stack rationale are in [APP_IDEA.md](APP_IDEA.md).

All Medium-specific logic is isolated in `src/Services/MediumStatsClient.cs`, so
if Medium changes its internal endpoints it's a one-file patch (the client dumps
the raw response to `latest.json.error` on a parse failure to make fixes fast).

## Notes & limits

- Read-only, single account, manual refresh — deliberately gentle on Medium's
  servers and Terms of Service.
- Because the stats endpoints are undocumented, they may change without notice.

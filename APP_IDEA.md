# Medium Metrics — App Idea

## One-liner
A small Windows desktop app that logs readership statistics for **my private Medium.com account** and shows them as a running list. A **Refresh** toggle appends a fresh snapshot line to the report.

## Goal
Keep a simple, time-stamped history of how my Medium stories are performing, gathered locally on my machine, without depending on Medium's web UI being open or on third-party analytics services.

## Target user
Just me — a single Medium author tracking my own private account's stats over time.

## Platform
- Windows desktop (Windows 10/11).
- Single-user, runs locally. No server, no cloud account.

## Core features

### 1. Gather stats
- Pull readership stats for my Medium account:
  - Total followers
  - Per-story: views, reads, read ratio, claps, responses
  - Totals across all stories (views, reads, claps)
- Authenticate as **my private account** (see "Data source" below).

### 2. Show stats as a list
- Main window displays the most recent snapshot as a list/table of stories and their metrics.
- Also shows the **history report**: each Refresh adds one new line (one snapshot) to the log.

### 3. Refresh toggle
- A **Refresh** control on the UI.
- Each activation:
  1. Fetches current stats from Medium.
  2. Appends **one new line** to the report with a timestamp + the gathered numbers.
  3. Updates the on-screen list.
- The report is therefore append-only — a growing log of snapshots, so I can see trends over time.

## Report format
Each Refresh appends one row. Suggested columns:

| Timestamp | Followers | Total Views | Total Reads | Total Claps | Notes |
|-----------|-----------|-------------|-------------|-------------|-------|

Optionally a second, per-story breakdown can be expanded under each snapshot.

The report is persisted to disk (e.g. a CSV or newline-delimited JSON file) so history survives app restarts.

## Data source (how stats are gathered)
Medium does not offer a public stats API for personal accounts, so options are:

1. **Authenticated session scrape (recommended for private account):**
   - User logs in once via an embedded browser / cookie capture.
   - App calls Medium's internal stats endpoints (the same JSON the stats page uses) with the session cookie.
   - Parse the JSON into the metrics above.
2. **Manual cookie/token paste:** user pastes their session cookie into the app's settings.
3. **RSS / public profile fallback:** limited — only public post list, no view/read counts.

> Note: This uses *my own* credentials against *my own* account for personal record-keeping. Respect Medium's Terms of Service and rate limits; keep refreshes manual/infrequent.

## UI sketch
```
+--------------------------------------------------+
|  Medium Metrics                      [ Refresh ] |
+--------------------------------------------------+
|  Latest snapshot: 2026-06-04 14:32               |
|  Followers: 412   Views: 10,233   Reads: 4,901   |
+--------------------------------------------------+
|  Story            Views   Reads   Ratio   Claps  |
|  My First Post     1,204    602    50%      88   |
|  Another Story       933    410    44%      51   |
|  ...                                             |
+--------------------------------------------------+
|  History (report log)                            |
|  2026-06-01 09:00  F:405  V:9,800  R:4,700 ...   |
|  2026-06-02 09:00  F:408  V:9,990  R:4,810 ...   |
|  2026-06-04 14:32  F:412  V:10,233 R:4,901 ...   |
+--------------------------------------------------+
```

## Tech stack (Windows desktop)

**Recommended: C# + .NET on WPF** — native Windows, no extra runtime to ship, first-class access to Windows credential/security APIs, and a built-in WebView2 control for the one-time Medium login.

| Layer | Choice | Why |
|-------|--------|-----|
| Language | **C# (.NET 8)** | Native Windows target, long-term support, single-file publish. |
| UI framework | **WPF** | Mature desktop UI; data-bound `DataGrid`/`ListView` map directly onto the stats list and history log. (WinUI 3 is a modern alternative.) |
| Login / auth | **WebView2** (`Microsoft.Web.WebView2`) | Embeds Edge Chromium so the user logs into Medium once; capture the session cookie from the webview. |
| HTTP | **`HttpClient`** | Calls Medium's internal stats JSON endpoints with the captured cookie. |
| JSON parsing | **`System.Text.Json`** | Built in; deserialize stats responses into model objects. |
| Local storage | **CSV** (`report.csv`) + **SQLite** (`Microsoft.Data.Sqlite`) or **JSON** (`latest.json`) | Append-only history log + last full snapshot. |
| Secret storage | **Windows DPAPI** (`ProtectedData`) or **Credential Manager** | Encrypts the stored session cookie at rest, per Windows user. |
| Scheduling (v2) | **`DispatcherTimer`** | Optional auto-refresh on an interval. |
| Packaging | **MSIX** or **single-file `dotnet publish`** (self-contained) | Simple install / portable exe. |
| Build/IDE | **Visual Studio 2022** or `dotnet` CLI | Standard .NET tooling. |

**Project layout (suggested)**
```
medium-metrics/
├─ MediumMetrics.sln
├─ src/
│  ├─ App.xaml / App.xaml.cs          # entry point
│  ├─ MainWindow.xaml                 # list + history + Refresh toggle
│  ├─ Auth/MediumLoginWindow.xaml     # WebView2 login, cookie capture
│  ├─ Services/MediumStatsClient.cs   # HttpClient calls + JSON parse
│  ├─ Services/ReportStore.cs         # append to report.csv, read history
│  ├─ Services/SecretStore.cs         # DPAPI cookie encrypt/decrypt
│  └─ Models/StatsSnapshot.cs         # followers, per-story metrics
└─ data/                              # report.csv, latest.json, settings.json
```

### Alternative stacks
- **WinUI 3 / Windows App SDK** — same C# stack with a more modern Fluent UI; slightly heavier setup.
- **Tauri (Rust + web UI)** — tiny binary, web-tech UI, built-in webview for login; good if you prefer HTML/CSS/TS over XAML.
- **Electron (Node + web UI)** — fastest to prototype and easiest cookie/session handling, but a much larger footprint.

## Persistence & files
- `report.csv` — append-only history log (one line per Refresh).
- `latest.json` — full last snapshot (per-story detail).
- `settings.json` — account/auth config.

## Non-goals (for v1)
- No multi-account support.
- No automatic scheduled polling (Refresh is manual via the toggle).
- No charts/graphs (just the list + log). Charts can come later.
- No publishing or editing of Medium content — read-only stats.

## Future ideas
- Optional auto-refresh on an interval.
- Trend charts (views/reads over time) from the report log.
- Export report to Excel.
- Desktop notifications when a story crosses a milestone.

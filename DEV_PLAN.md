# Medium Metrics — Development Plan

Implementation plan for the Windows desktop app described in [APP_IDEA.md](APP_IDEA.md).

**Stack:** C# / .NET 8, WPF, WebView2, `HttpClient`, `System.Text.Json`, SQLite + CSV, Windows DPAPI.

**Guiding principles**
- Keep the **stats-gathering layer isolated** — Medium's internal endpoints are unofficial and will change. All Medium-specific logic lives behind one interface (`IMediumStatsClient`) so a break is a one-file patch.
- Ship a working vertical slice early (login → one fetch → one logged line), then widen.
- Local-only, read-only, single user. No telemetry.

## Status (v0.1.0)

All milestones M0–M7 are implemented. 10 unit tests pass; the app runs from
source (`start_app.ps1`) and as a single-file self-contained exe
(`build_release.ps1`). Real-account verification of the exact stats JSON paths
is still pending (see M3 / `docs/private/medium-api-notes.md`).

| Milestone | Status |
|-----------|--------|
| M0 Scaffold | ✅ |
| M1 Data models | ✅ |
| M2 WebView2 login + DPAPI | ✅ |
| M3 Stats client (isolated) | ✅ code; ⏳ verify against real account |
| M4 Persistence | ✅ |
| M5 UI list + history + Refresh | ✅ |
| M6 Robustness & UX polish | ✅ |
| M7 Packaging & release | ✅ |

---

## Milestone 0 — Project scaffold
**Goal:** an empty WPF app that builds and runs.

- [ ] `dotnet new wpf -n MediumMetrics` + solution file `MediumMetrics.sln`.
- [ ] Target `net8.0-windows`, enable nullable + implicit usings.
- [ ] Add folder structure: `Auth/`, `Services/`, `Models/`, `ViewModels/`, `Views/`.
- [ ] Add NuGet packages: `Microsoft.Web.WebView2`, `Microsoft.Data.Sqlite`, `CommunityToolkit.Mvvm`.
- [ ] Commit. Confirm app launches with an empty window.

**Done when:** `dotnet run` opens a blank window.

---

## Milestone 1 — Data models
**Goal:** define the shapes that flow through the app.

- [ ] `Models/StorySnapshot.cs` — storyId, title, views, reads, readRatio, claps, responses.
- [ ] `Models/StatsSnapshot.cs` — timestamp, followers, totals (views/reads/claps), `List<StorySnapshot>`.
- [ ] `Models/AppSettings.cs` — accountId/username, last-refresh time, data file paths.

**Done when:** models compile and have a simple unit test constructing one.

---

## Milestone 2 — Auth & session capture (WebView2)
**Goal:** log into Medium once and capture the session cookie.

- [ ] `Auth/MediumLoginWindow.xaml` — hosts a WebView2 navigated to Medium's login/stats page.
- [ ] Detect successful login (URL change / presence of session cookie).
- [ ] Extract `sid`/session cookies via `CoreWebView2.CookieManager`.
- [ ] `Services/SecretStore.cs` — encrypt the cookie with DPAPI (`ProtectedData`, CurrentUser scope), store in `data/session.bin`.
- [ ] On startup, load + decrypt cookie if present; otherwise prompt login.

**Done when:** I can log in once, close the app, reopen, and still be authenticated.

**Risk:** Medium login flow / cookie names may change → keep cookie extraction in one method.

---

## Milestone 3 — Stats client (the fragile part, isolated)
**Goal:** fetch real stats JSON using the captured cookie.

- [ ] `Services/IMediumStatsClient.cs` — `Task<StatsSnapshot> FetchAsync()`.
- [ ] `Services/MediumStatsClient.cs`:
  - [ ] Build `HttpClient` with the session cookie + required headers (`Accept: application/json`, etc.).
  - [ ] Call Medium's internal stats endpoint(s) (the JSON the stats dashboard loads).
  - [ ] Strip Medium's JSON anti-hijacking prefix (`])}while(1);</x>`) before parsing.
  - [ ] Deserialize into `StatsSnapshot` with `System.Text.Json`.
- [ ] `Services/FakeMediumStatsClient.cs` — returns canned data for dev/UI work without hitting Medium.
- [ ] Defensive parsing: tolerate missing fields, log raw response on parse failure (to `data/last_error.json`).

**Done when:** a console/test harness prints a populated `StatsSnapshot` from my real account.

**Note:** endpoints/field names to be discovered via browser DevTools → document them in `docs/private/medium-api-notes.md`.

---

## Milestone 4 — Persistence (report log + latest snapshot)
**Goal:** append-only history + last full snapshot.

- [ ] `Services/ReportStore.cs`:
  - [ ] `AppendSnapshot(StatsSnapshot)` → append one row to `data/report.csv` (timestamp, followers, totals).
  - [ ] `SaveLatest(StatsSnapshot)` → write full per-story detail to `data/latest.json`.
  - [ ] `ReadHistory()` → load all CSV rows for the history view.
- [ ] (Optional) SQLite store for richer per-story history queries later.
- [ ] Ensure `data/` is created on first run; never overwrite, only append, for the CSV.

**Done when:** each fetch adds exactly one CSV line and refreshes `latest.json`.

---

## Milestone 5 — UI: list + history + Refresh
**Goal:** the actual window the user uses.

- [ ] `ViewModels/MainViewModel.cs` (MVVM via CommunityToolkit) — exposes latest snapshot, story list, history rows, `RefreshCommand`, busy/error state.
- [ ] `Views/MainWindow.xaml`:
  - [ ] Header: account + latest snapshot summary + **Refresh** button.
  - [ ] `DataGrid` of stories (views/reads/ratio/claps).
  - [ ] `DataGrid`/list of history rows (the append-only log).
  - [ ] Busy spinner + error banner.
- [ ] Wire `RefreshCommand` → `FetchAsync()` → `AppendSnapshot()` + `SaveLatest()` → reload UI.
- [ ] Disable Refresh while a fetch is in flight (prevents duplicate lines).

**Done when:** clicking **Refresh** fetches, appends one history line, and updates both grids.

---

## Milestone 6 — Robustness & UX polish
**Goal:** make it pleasant and resilient.

- [ ] Handle expired session → show "re-login" prompt, reopen `MediumLoginWindow`.
- [ ] Graceful error messages (network down, parse failure, rate-limited).
- [ ] Simple logging to `data/app.log`.
- [ ] Settings screen: data folder location, re-login, clear session.
- [ ] Remember window size/position.

**Done when:** common failure paths show a clear message instead of crashing.

---

## Milestone 7 — Packaging & release
**Goal:** installable/portable build.

- [ ] `dotnet publish -c Release -r win-x64 --self-contained` (single-file) — portable exe.
- [ ] (Optional) MSIX package for clean install/uninstall.
- [ ] README with first-run instructions (login once, then Refresh).
- [ ] Tag `v0.1.0`.

**Done when:** a fresh Windows machine can run the exe, log in, and Refresh.

---

## Backlog / v2 ideas (not in v1)
- Auto-refresh on an interval (`DispatcherTimer`).
- Trend charts from `report.csv` (views/reads over time).
- Export history to Excel.
- Milestone desktop notifications.
- WinUI 3 UI refresh.

---

## Testing strategy
- **Unit:** models, CSV append logic, JSON parsing against saved sample responses (`tests/samples/*.json`).
- **Manual:** the real login → fetch → refresh loop (the WebView2/network parts).
- Use `FakeMediumStatsClient` to develop UI and persistence without depending on Medium.

## Key risks & mitigations
| Risk | Mitigation |
|------|-----------|
| Medium changes internal endpoints/fields | All Medium logic behind `IMediumStatsClient`; save raw responses for fast diagnosis. |
| Login/cookie flow changes | Cookie capture isolated to one method; fall back to manual cookie paste. |
| Rate limiting / TOS | Manual, infrequent refresh only; no background polling in v1. |
| JSON anti-hijack prefix / schema drift | Defensive parsing, tolerate missing fields, log on failure. |

## Suggested build order (fastest path to a usable slice)
M0 → M1 → M2 → M3 (with Fake client first) → M4 → M5 → then M6/M7.
A thin end-to-end slice (login → one real fetch → one logged line → shown in grid) is the priority before polishing.

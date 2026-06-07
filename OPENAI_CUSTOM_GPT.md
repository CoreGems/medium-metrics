# Medium Metrics — OpenAI Custom GPT Backend (design)

> **Status: v1 implemented — cache-only, read-only.** This document specifies a small,
> read-only HTTP API that exposes the metrics this app already collects so an
> **OpenAI Custom GPT** can answer questions about your Medium stats in natural
> language ("which tags earn the most per story?", "how did earnings move this
> week?", "list my top 10 posts by read ratio").
>
> The v1 is built into the app — enable it in **Settings → Local API**. Per-story
> `/detail` and `/tags/followers` are **cache-backed**: they serve the detail captured
> the last time you opened that story (or the Followers-by-tag report) in the app, so
> the API itself never calls Medium.
>
> It is written to fit the app's existing ethos — **local-first, single-file,
> zero-/few-dependency, read-only, gentle on Medium's ToS** (see
> [APP_IDEA.md](APP_IDEA.md)) — and to reuse the data and aggregations already in
> the codebase rather than inventing a parallel store.

## Contents

1. [What you'll be able to do](#1-what-youll-be-able-to-do)
2. [The core tension (read this first)](#2-the-core-tension-read-this-first)
3. [Architecture](#3-architecture)
4. [What data exists today (and where)](#4-what-data-exists-today-and-where)
5. [API surface](#5-api-surface)
6. [Response shapes](#6-response-shapes)
7. [Authentication](#7-authentication)
8. [OpenAPI schema for the GPT Action](#8-openapi-schema-for-the-gpt-action)
9. [Configuring the Custom GPT](#9-configuring-the-custom-gpt)
10. [Reaching the local server from OpenAI (Cloudflare Tunnel)](#10-reaching-the-local-server-from-openai-cloudflare-tunnel)
11. [Security & privacy](#11-security--privacy)
12. [Implementation notes (where this lands in the codebase)](#12-implementation-notes-where-this-lands-in-the-codebase)
13. [Non-goals](#13-non-goals)
14. [Open questions / decisions](#14-open-questions--decisions)

---

## 1. What you'll be able to do

A Custom GPT ("Medium Metrics Analyst") that you chat with normally and that, under
the hood, calls a few HTTP endpoints on **your own machine** to read the numbers
this app has been logging. Example turns it should handle:

- *"How am I doing overall?"* → account summary (followers, totals, last refresh).
- *"What are my top 10 stories by read ratio?"* → filtered/sorted story list.
- *"Which tags make the most money per story?"* → per-tag metrics, sorted.
- *"Show my daily earnings for the last 2 weeks."* → earnings delta series.
- *"How have my total views grown since I started logging?"* → history time series.
- *"Find my posts tagged 'Politics' and rank them by earnings."* → tag → stories.
- *"Summarise 2025 vs 2024."* → by-year report.

The GPT does the language/formatting/reasoning; the API just serves clean,
already-aggregated JSON. **Everything is read-only** — the GPT can never change
your data or touch your Medium account.

---

## 2. The core tension (read this first)

This app's headline promise is **"your data never leaves your PC."** A Custom GPT
runs on **OpenAI's servers**. Custom GPT *Actions* are HTTP calls made **from
OpenAI's cloud**, not from your browser — so **they cannot reach `localhost`.**
For the GPT to read your stats, the API must be reachable over the public internet
(via a Cloudflare Tunnel, see [§10](#10-reaching-the-local-server-from-openai-cloudflare-tunnel)).

**Turning this feature on is therefore a deliberate, opt-in departure from
fully-local operation.** The design below exists to make that departure as small
and controllable as possible:

- **Off by default.** Nothing listens until you flip a switch in Settings, and it
  only runs **while the app is open**.
- **Read-only.** GET endpoints only. No path can mutate local files or call Medium
  to write anything.
- **Derived metrics only.** It serves the *numbers* (views, reads, earnings, tags,
  history). It **never** serves your session cookie, DPAPI secrets, or the WebView2
  profile — those stay on disk, encrypted, untouched.
- **Key-gated.** Every request needs a bearer API key. The tunnel URL alone is
  useless without it. You can rotate the key or close the app to revoke access
  instantly.
- **You own the ingress.** The tunnel is started by you and dies when you stop it.

If even that is too much exposure for your taste, the honest answer is: **don't
enable it.** This is an additive, opt-in convenience, not a default behaviour.

---

## 3. Architecture

```
   ┌─────────────────────────────┐         you ask a question
   │      ChatGPT (OpenAI)        │◀───────────────────────────────  you
   │  "Medium Metrics Analyst"    │
   │   Custom GPT + Action        │
   └──────────────┬──────────────┘
                  │  HTTPS GET /v1/...           (OpenAI's servers call out;
                  │  Authorization: Bearer <key>  NOT your browser)
                  ▼
   ┌─────────────────────────────┐
   │  Public HTTPS ingress        │   Cloudflare Tunnel (cloudflared)
   │  https://you.example.dev     │   (stable hostname → your PC)
   └──────────────┬──────────────┘
                  │  forwards to 127.0.0.1:8780
                  ▼
   ┌─────────────────────────────────────────────────────────────┐
   │  Medium Metrics.exe  (only while the app is open)             │
   │                                                              │
   │   ApiServer (HttpListener, 127.0.0.1 only)  ── NEW           │
   │      │  reuses, per ?account=:                                │
   │      ├─ ReportStore.LoadLatest()  → latest.json (snapshot)    │
   │      ├─ ReportStore.ReadHistory() → report.csv  (history)     │
   │      ├─ Reports.*  (MetricsByTag / ByYear / ByMonth / …)      │
   │      └─ SettingsStore.Load()      → account registry          │
   │                                                              │
   │      └─ details/<id>.json  (cached StoryDetail, UI-written)  │
   └─────────────────────────────────────────────────────────────┘
                  │
                  ▼
   %LOCALAPPDATA%\MediumMetrics\accounts\<id>\{latest.json, report.csv}
```

Key points:

- The HTTP server **binds to `127.0.0.1` only**. The *only* way in from outside is
  the tunnel you start, which you control.
- The server is a **thin read layer** over things that already exist: it reuses
  `ReportStore`, the pure `Reports` aggregations, and the account registry. No new
  data model, no second copy of the data.
- It runs **in-process** inside the existing WPF app, on a background thread, for
  the lifetime of the app window.

---

## 4. What data exists today (and where)

This is the inventory the API draws on. All paths are per-account under
`%LOCALAPPDATA%\MediumMetrics\accounts\<id>\` (multi-account; see
[REFACTOR_PLAN.md] history and `AccountConfig`).

| Source | File / function | Contents |
|---|---|---|
| **Latest snapshot** | `latest.json` via `ReportStore.LoadLatest()` → `StatsSnapshot` | Per-story metadata + account totals from the last Refresh |
| **History log** | `report.csv` via `ReportStore.ReadHistory()` → `HistoryRow[]` | One timestamped row of account totals per Refresh (append-only) |
| **Account registry** | `settings.json` via `SettingsStore.Load()` → `AppSettings.Accounts[]` | Known accounts (`{ Id, Label }`), `ActiveAccountId`, `LastRefresh`, `EarningsBaselineUsd` (default `412.80`) |
| **Tag / period reports** | `Reports.MetricsByTag/ByYear/ByMonth/DailyEarnings` | Pure aggregations computed on demand from the snapshot/history |
| **Per-story detail** | `StoryDetail` cached to `details/<id>.json` on UI fetch | Funnel, follower/subscriber impact, referrers — **served from the last UI fetch** |

### Field inventory (from the models)

- **`StatsSnapshot`** — `Timestamp` (UTC), `Followers`, `AccountUsername`,
  `AccountName`, `Stories[]`; computed `TotalViews`, `TotalReads`,
  `TotalImpressions`, `TotalEarningsUsd`, `TotalReadRatio`.
- **`StorySnapshot`** — `StoryId`, `Title`, `Url`, `PublishedAt?`, `Views`,
  `Reads`, `Impressions`, `Claps`, `EarningsUsd`, `Tags[]`; computed `ReadRatio`.
- **`HistoryRow`** — `Timestamp`, `Followers`, `TotalViews`, `TotalReads`,
  `TotalImpressions`, `TotalEarningsUsd`, `StoryCount`; computed `ReadRatio`.
- **`TagMetrics`** — `Tag`, `Stories`, `Views`, `Reads`, `Impressions`, `Claps`,
  `Earnings`; computed `ReadRatio`, `EarningsPerStory`.
- **`PeriodMetrics`** (by year) — `Period`, `IsCurrent`, `Stories`, `Views`,
  `Reads`, `Earnings`; computed `ReadRatio`, `EarningsPerStory`.
- **`MonthMetrics`** — `Month` (`yyyy-MM`), `Stories`, `Views`, `Reads`,
  `Impressions`, `Earnings`.
- **`DailyEarning`** — `Day`, `Total` (lifetime that day), `Delta` (earned that day
  vs the prior day / `baseline` on day one).
- **`StoryDetail`** (cached on UI fetch) — `ViewersCount`, `ReadersCount`,
  `FeedClickThroughRate?`, `FollowersGained`, `FollowersLost`, `NetFollowerCount`,
  `SubscribersGained`, `NetSubscriberCount`, `Referrers[]` (`Source`, `Type`,
  `Count`).

### Data-exposure matrix

| Class | Endpoints | Availability | Notes |
|---|---|---|---|
| **Local cache** (always available while app is open) | summary, stories, story (single), tags, tag→stories, by-year, by-month, earnings/daily, history, accounts | Reads `latest.json` + `report.csv` only | No network, fast, ToS-safe |
| **Cached detail** (populated by opening a story / the Followers report in the app) | story `/detail`, `tags/followers` | Reads `details/<id>.json`; API never calls Medium | Coverage grows as you view stories; `fetchedAt` per entry |
| **Never exposed** | — | — | `session.bin` cookie, DPAPI secrets, `webview2/` profile, raw `settings.json` |

> ⚠️ **Caveats to surface honestly in the doc and the GPT instructions:**
> - **Claps may be `0`/stale.** Per [medium-api-notes.md](docs/private/medium-api-notes.md),
>   claps are *not* in Medium's current stats API; the app tracks Impressions +
>   Earnings instead. Expose the `claps` field but don't lean on it.
> - **`StoryDetail` is cached, not live.** Opening a story's dashboard (or the
>   Followers-by-tag report) in the app saves the funnel/impact/referrers to
>   `details/<id>.json`; the API serves that cache and **never calls Medium itself**.
>   Each entry carries a `fetchedAt`; an unviewed story returns `detail_not_cached`.
> - **`FollowersByTag` reports coverage.** It aggregates only stories with a cached
>   detail, so the response includes a `coverage` block (`storiesWithDetail` / total)
>   that fills in as you view more stories in the app.

---

## 5. API surface

Conventions:

- Base path **`/v1`**. JSON in/out. **All endpoints are `GET`** (read-only).
- **Account scoping:** every data endpoint accepts `?account=<id|handle>`. If
  omitted, the server uses `AppSettings.ActiveAccountId`. `GET /v1/accounts` lists
  the choices so the GPT can disambiguate.
- **Pagination** on list endpoints: `limit` (default 50, max 200) + `offset`. Keep
  responses small — Custom GPT Action responses share the model's token budget, and
  you may have 300+ stories.
- **Auth** on everything except `/v1/health` (see [§7](#7-authentication)).

| Method & path | Purpose | Backed by |
|---|---|---|
| `GET /v1/health` | Liveness + version + active account + data freshness | settings + file mtimes |
| `GET /v1/accounts` | List accounts (id, label, handle, isActive, lastRefresh, storyCount) | registry + each `latest.json` |
| `GET /v1/summary` | Account headline totals from the latest snapshot | `StatsSnapshot` |
| `GET /v1/stories` | Story list w/ `search`, `tag`, `sort`, `order`, `limit`, `offset` | `StorySnapshot[]` |
| `GET /v1/stories/{storyId}` | One story's metadata | `StorySnapshot` |
| `GET /v1/stories/{storyId}/detail` | Cached funnel/impact/referrers (from last UI fetch) | `details/<id>.json` |
| `GET /v1/tags` | Per-tag metrics, `sort`/`order` | `Reports.MetricsByTag` |
| `GET /v1/tags/{tag}/stories` | Stories carrying a tag (shortcut for `stories?tag=`) | `StorySnapshot[]` |
| `GET /v1/tags/followers` | Per-tag follower/subscriber gains + cache `coverage` | `Reports.FollowersByTag` over `details/` |
| `GET /v1/reports/by-year` | Totals grouped by publish year | `Reports.ByYear` |
| `GET /v1/reports/by-month` | Totals grouped by publish month | `Reports.ByMonth` |
| `GET /v1/reports/earnings/daily` | Daily earnings deltas; `baseline`, `from`, `to` | `Reports.DailyEarnings` |
| `GET /v1/history` | Account-total time series; `from`, `to`, `limit` | `HistoryRow[]` |

**Query parameters**

- `search` — case-insensitive match on title **or** tag (mirrors the app's search box).
- `tag` — exact tag filter (use `(untagged)` for the untagged bucket).
- `sort` for `/stories` — one of `views|reads|readRatio|earnings|claps|impressions|publishedAt|title` (default `views`).
- `sort` for `/tags` — one of `views|reads|earnings|earningsPerStory|readRatio|stories|claps` (default `views`).
- `order` — `asc|desc` (default `desc`).
- `from` / `to` — ISO-8601 dates (inclusive) for `/history` and `/reports/earnings/daily`.
- `baseline` — decimal USD override for the earnings "ground zero" (defaults to `EarningsBaselineUsd`, `412.80`).

---

## 6. Response shapes

All money is a JSON number in USD. Ratios are fractions in `[0,1]` (the GPT can
render as %). Timestamps are ISO-8601; snapshot/history timestamps are UTC and
carry an offset.

**`GET /v1/health`**
```json
{
  "status": "ok",
  "appVersion": "0.1.0",
  "apiVersion": "v1",
  "activeAccount": "1a2b3c",
  "lastRefresh": "2026-06-07T08:08:00Z",
  "snapshotAgeHours": 3.2
}
```

**`GET /v1/accounts`**
```json
{
  "accounts": [
    { "id": "1a2b3c", "label": "Alex Buz", "handle": "alexbuz",
      "isActive": true, "lastRefresh": "2026-06-07T08:08:00Z", "storyCount": 335 }
  ]
}
```

**`GET /v1/summary`**
```json
{
  "account": "1a2b3c",
  "handle": "alexbuz",
  "name": "Alex Buz",
  "capturedAt": "2026-06-07T08:08:00Z",
  "followers": 309,
  "storyCount": 335,
  "totalViews": 1029384,
  "totalReads": 463120,
  "totalImpressions": 2200145,
  "totalReadRatio": 0.4499,
  "totalEarningsUsd": 1284.55
}
```

**`GET /v1/stories?sort=readRatio&order=desc&limit=2`**
```json
{
  "account": "1a2b3c",
  "capturedAt": "2026-06-07T08:08:00Z",
  "total": 335,
  "limit": 2,
  "offset": 0,
  "stories": [
    {
      "storyId": "abc123",
      "title": "Why my Medium app trusts the disk over my bookkeeping",
      "url": "https://medium.com/@alexbuz/why-...-abc123",
      "freeLink": "https://freedium-mirror.cfd/https://medium.com/@alexbuz/why-...-abc123",
      "publishedAt": "2026-05-30",
      "views": 4200, "reads": 2810, "readRatio": 0.6690,
      "impressions": 9120, "claps": 0, "earningsUsd": 41.18,
      "tags": ["Programming", "Dotnet"]
    }
  ]
}
```
> `freeLink` is an optional convenience field (the same Freedium mirror the app's
> "copy" feature emits). Mark it clearly as a third-party mirror; omit if undesired.

**`GET /v1/tags?sort=earningsPerStory`**
```json
{
  "account": "1a2b3c",
  "tags": [
    { "tag": "Politics", "stories": 22, "views": 180400, "reads": 92010,
      "impressions": 410233, "claps": 0, "earnings": 612.40,
      "readRatio": 0.5100, "earningsPerStory": 27.84 }
  ]
}
```

**`GET /v1/reports/by-year`**
```json
{
  "account": "1a2b3c",
  "years": [
    { "period": "2024", "isCurrent": false, "stories": 140, "views": 500000,
      "reads": 210000, "earnings": 540.10, "readRatio": 0.42, "earningsPerStory": 3.86 },
    { "period": "2025 so far", "isCurrent": true, "stories": 95, "views": 420000,
      "reads": 205000, "earnings": 744.45, "readRatio": 0.488, "earningsPerStory": 7.84 }
  ]
}
```

**`GET /v1/reports/earnings/daily?from=2026-05-25`**
```json
{
  "account": "1a2b3c",
  "baseline": 412.80,
  "days": [
    { "day": "2026-05-25", "total": 1180.20, "delta": 12.40 },
    { "day": "2026-05-26", "total": 1181.05, "delta": 0.85 }
  ]
}
```

**`GET /v1/history?from=2026-06-01`**
```json
{
  "account": "1a2b3c",
  "rows": [
    { "timestamp": "2026-06-01T09:00:00Z", "followers": 305, "totalViews": 1010222,
      "totalReads": 455100, "totalImpressions": 2150900, "totalEarningsUsd": 1262.10,
      "storyCount": 333, "readRatio": 0.4505 }
  ]
}
```

**`GET /v1/stories/{id}/detail`** (cache-backed; `404 detail_not_cached` until you open the story in the app)
```json
{
  "account": "1a2b3c",
  "storyId": "abc123",
  "fetchedAt": "2026-06-07T11:32:10Z",
  "viewersCount": 4180, "readersCount": 2790, "feedClickThroughRate": 0.061,
  "followersGained": 14, "followersLost": 1, "netFollowerCount": 13,
  "subscribersGained": 6, "netSubscriberCount": 6,
  "referrers": [ { "source": "google.com", "type": "SEARCH", "count": 980 } ]
}
```

**`GET /v1/tags/followers`** (cache-backed; aggregates only stories with a cached detail)
```json
{
  "account": "1a2b3c",
  "coverage": { "storiesWithDetail": 42, "totalStories": 335,
    "oldestFetchedAt": "2026-06-05T10:00:00Z", "newestFetchedAt": "2026-06-07T11:32:10Z" },
  "tags": [
    { "tag": "Politics", "stories": 12, "followersGained": 140, "subscribersGained": 33 }
  ]
}
```

**Error shape** (consistent for all 4xx/5xx)
```json
{ "error": { "code": "account_not_found", "message": "No account 'xyz'. Try GET /v1/accounts." } }
```
Suggested codes: `unauthorized` (401), `account_not_found` (404),
`story_not_found` (404), `no_snapshot` (409, account never refreshed),
`detail_unavailable` (503, not signed in / Medium unreachable / feature disabled),
`bad_request` (400), `rate_limited` (429).

---

## 7. Authentication

- **Scheme:** `Authorization: Bearer <API_KEY>` on every endpoint except
  `/v1/health`. This maps directly to a Custom GPT Action's **API Key → Bearer**
  auth type.
- **Key generation:** the app generates a long random key (e.g. 32 bytes,
  base64url) when you enable the feature. **Shown once**, with a "Copy" button, plus
  "Regenerate" (which immediately invalidates the old key).
- **Key storage:** store it **DPAPI-encrypted** via the existing `SecretStore`
  (e.g. `apikey.bin` in the data folder), **not** in `settings.json`.
  - *Why not settings.json:* `settings.json` is rewritten by the app and was the
    exact file that an older build clobbered in the multi-account near-miss (see
    `docs/private/reddit/6_earnings_chart.md`). Keep the key out of a file that
    other builds rewrite, and keep it encrypted at rest like the session cookie.
- **Comparison:** constant-time compare; on mismatch return `401` with the standard
  error shape and **no** detail.
- **Transport:** the key only ever crosses the wire over the tunnel's HTTPS. The
  local listener is `127.0.0.1`, so the key is not exposed on your LAN.

---

## 8. OpenAPI schema for the GPT Action

Custom GPT Actions are configured by pasting an **OpenAPI 3.1** document. A single
`servers[0].url` is used as the base for every call, so it must be your **stable
Cloudflare Tunnel hostname** (see [§10](#10-reaching-the-local-server-from-openai-cloudflare-tunnel)).
Good `operationId`s and `description`s matter — the model uses them to pick the
right call. Below is a representative (not exhaustive) schema covering the main
endpoints; extend it as the surface settles.

```yaml
openapi: 3.1.0
info:
  title: Medium Metrics API
  version: "1.0.0"
  description: Read-only access to locally-logged Medium readership stats.
servers:
  - url: https://REPLACE-WITH-YOUR-TUNNEL-HOST   # e.g. https://metrics.example.dev/v1  (your Cloudflare hostname + /v1)
security:
  - bearerAuth: []
paths:
  /accounts:
    get:
      operationId: listAccounts
      summary: List known Medium accounts so the user can be asked which to use.
      responses: { "200": { description: OK } }
  /summary:
    get:
      operationId: getSummary
      summary: Account-level headline totals (followers, views, reads, earnings) from the latest snapshot.
      parameters: [ { $ref: '#/components/parameters/account' } ]
      responses: { "200": { description: OK } }
  /stories:
    get:
      operationId: listStories
      summary: List stories with optional search/tag filter and sorting. Paginated.
      parameters:
        - { $ref: '#/components/parameters/account' }
        - { name: search, in: query, schema: { type: string }, description: Match title or tag. }
        - { name: tag, in: query, schema: { type: string } }
        - { name: sort, in: query, schema: { type: string, enum: [views, reads, readRatio, earnings, claps, impressions, publishedAt, title] } }
        - { name: order, in: query, schema: { type: string, enum: [asc, desc] } }
        - { name: limit, in: query, schema: { type: integer, default: 50, maximum: 200 } }
        - { name: offset, in: query, schema: { type: integer, default: 0 } }
      responses: { "200": { description: OK } }
  /stories/{storyId}:
    get:
      operationId: getStory
      summary: One story's metadata.
      parameters:
        - { $ref: '#/components/parameters/account' }
        - { name: storyId, in: path, required: true, schema: { type: string } }
      responses: { "200": { description: OK } }
  /tags:
    get:
      operationId: listTagMetrics
      summary: Per-tag totals (stories, views, reads, earnings, read ratio, earnings per story).
      parameters:
        - { $ref: '#/components/parameters/account' }
        - { name: sort, in: query, schema: { type: string, enum: [views, reads, earnings, earningsPerStory, readRatio, stories, claps] } }
        - { name: order, in: query, schema: { type: string, enum: [asc, desc] } }
      responses: { "200": { description: OK } }
  /tags/{tag}/stories:
    get:
      operationId: listStoriesByTag
      summary: Stories carrying a given tag.
      parameters:
        - { $ref: '#/components/parameters/account' }
        - { name: tag, in: path, required: true, schema: { type: string } }
      responses: { "200": { description: OK } }
  /reports/by-year:
    get:
      operationId: getByYear
      summary: Totals grouped by publish year.
      parameters: [ { $ref: '#/components/parameters/account' } ]
      responses: { "200": { description: OK } }
  /reports/by-month:
    get:
      operationId: getByMonth
      summary: Totals grouped by publish month (yyyy-MM).
      parameters: [ { $ref: '#/components/parameters/account' } ]
      responses: { "200": { description: OK } }
  /reports/earnings/daily:
    get:
      operationId: getDailyEarnings
      summary: Daily earnings totals and per-day deltas from the history log.
      parameters:
        - { $ref: '#/components/parameters/account' }
        - { name: from, in: query, schema: { type: string, format: date } }
        - { name: to, in: query, schema: { type: string, format: date } }
        - { name: baseline, in: query, schema: { type: number } }
      responses: { "200": { description: OK } }
  /history:
    get:
      operationId: getHistory
      summary: Account-total time series, one row per refresh.
      parameters:
        - { $ref: '#/components/parameters/account' }
        - { name: from, in: query, schema: { type: string, format: date } }
        - { name: to, in: query, schema: { type: string, format: date } }
        - { name: limit, in: query, schema: { type: integer, default: 200, maximum: 1000 } }
      responses: { "200": { description: OK } }
  /stories/{storyId}/detail:
    get:
      operationId: getStoryDetail
      summary: Cached per-story detail (funnel, follower/subscriber gains, referrers). Served from the last in-app fetch; returns 404 detail_not_cached until the story has been opened in the app.
      parameters:
        - { $ref: '#/components/parameters/account' }
        - { name: storyId, in: path, required: true, schema: { type: string } }
      responses: { "200": { description: OK } }
  /tags/followers:
    get:
      operationId: getFollowersByTag
      summary: Per-tag follower/subscriber gains computed from cached detail, plus a coverage block (storiesWithDetail / totalStories). Fills in as stories are opened in the app.
      parameters: [ { $ref: '#/components/parameters/account' } ]
      responses: { "200": { description: OK } }
  /health:
    get:
      operationId: getHealth
      summary: Liveness + app version + active account + data freshness (lastRefresh, snapshotAgeHours). Unauthenticated (sending the bearer anyway is harmless).
      responses: { "200": { description: OK } }
components:
  securitySchemes:
    bearerAuth: { type: http, scheme: bearer }
  parameters:
    account:
      name: account
      in: query
      required: false
      schema: { type: string }
      description: Account id or handle. Defaults to the active account.
```

> `getStoryDetail` and `getFollowersByTag` are **cache-backed** — they serve the detail
> you last viewed in the app (the API never calls Medium). `getStoryDetail` returns
> `404 detail_not_cached` until you open that story once; `getFollowersByTag` includes a
> `coverage` block that grows as you view more stories.

---

## 9. Configuring the Custom GPT

In ChatGPT → **Explore GPTs → Create → Configure**. The fields below are ready to paste.

**Name**

```
Medium Metrics Analyst
```

**Description**

```
Answers questions about my Medium readership stats — views, reads, earnings, tags, and trends — from my local Medium Metrics app. Read-only.
```

**Instructions (system prompt)**

```
You are Medium Metrics Analyst, a read-only assistant that answers questions about the
user's own Medium readership stats. The data comes from their local "Medium Metrics"
app via the configured Actions. You never write anything and never call Medium directly.

DATA FRESHNESS
- Every number is a local snapshot from the last time the user clicked Refresh in the
  app. Before giving "current" figures, note how fresh they are: check lastRefresh /
  snapshotAgeHours (getHealth) or the capturedAt field on a response.
- If any call returns no_snapshot (HTTP 409), tell the user to open the app and click
  Refresh, then retry.
- If the API is unreachable or returns 502/503, the app likely isn't running with the
  local API enabled — tell them to open it (Settings -> Local API).

ACCOUNTS
- If listAccounts returns more than one account and the user hasn't specified one, ask
  which (offer the handles); otherwise use the active account. You may pass an `account`
  (id or @handle) to scope any call.

CHOOSING ACTIONS
- Prefer aggregate endpoints (getSummary, listTagMetrics, getByYear, getByMonth,
  getDailyEarnings, getHistory) over pulling the whole story list.
- When listing stories, use search / tag / sort with a small limit (e.g. 10) — never
  fetch hundreds. State which sort you used when you rank.
- For trends over time, use getHistory (totals per refresh) or getDailyEarnings (per-day
  earned, relative to a baseline).

CACHE-BACKED DETAIL (IMPORTANT)
- getStoryDetail (funnel, followers/subscribers gained, referrers) and getFollowersByTag
  are served from a cache that only fills as the user opens stories — or the Reports ->
  Followers tab — in the app. The API never fetches them live.
- If getStoryDetail returns detail_not_cached (404), tell the user to open that story in
  the app once to cache it.
- getFollowersByTag includes a coverage block (storiesWithDetail / totalStories). Always
  factor it in: if coverage is partial, say so ("based on the N of M stories you've
  opened") and don't present it as complete.

PRESENTATION
- Render ratios as percentages and money as USD. Treat tiny daily earnings as
  "~ $0 (quiet day)."
- Claps may be 0 or missing — Medium doesn't expose them in this data, so never draw
  conclusions from claps.
- Be concise, lead with the answer, and use a small table when comparing tags/years/stories.

BOUNDARIES
- You are strictly read-only: you cannot Refresh, publish, edit tags, or change anything
  on Medium or in the app. If asked, explain the user must do it in the app.
- Never reveal, request, or guess the API key, session cookies, or file paths.
```

**Conversation starters**

```
How am I doing overall right now?
Which tags earn the most per story?
Top 10 stories by read ratio
Show my daily earnings for the last 2 weeks
```

**Actions** — import the schema from [§8](#8-openapi-schema-for-the-gpt-action). Set the
server URL to your **public tunnel** host + `/v1` (e.g. `https://metrics.example.dev/v1`),
**not** `localhost` (OpenAI calls it from its servers — see §10). Authentication →
**API Key → Auth Type: Bearer**, and paste the key from **Settings → Local API**.

**Privacy / visibility** — keep the GPT **"Only me."** OpenAI requires a public privacy
policy URL for Actions only when a GPT is *shared/published*; "Only me" avoids that.
Don't publish a GPT wired to your personal stats.

---

## 10. Reaching the local server from OpenAI (Cloudflare Tunnel)

OpenAI calls the Action **from its servers**, so it needs a **public HTTPS URL** that
forwards to your `127.0.0.1:<port>`. We use **Cloudflare Tunnel** (`cloudflared`):
your PC opens an **outbound** connection to Cloudflare's edge, and Cloudflare proxies
inbound requests back down that connection. Why this fits the app:

- **No inbound ports** opened on your router/firewall; nothing is exposed on your LAN.
- **HTTPS for free**, terminated at Cloudflare's edge — you don't manage certificates.
- A **stable hostname** (named tunnel) — a GPT Action stores exactly **one**
  `servers.url`, so a rotating URL would mean re-editing the GPT every session.

### Request flow (end to end)

```
ChatGPT (Custom GPT)
  │  GET https://metrics.example.dev/v1/summary
  │  Authorization: Bearer <API_KEY>
  ▼
Cloudflare edge        ── TLS terminated here
  │  proxied down the existing outbound tunnel
  ▼
cloudflared (on your PC)
  │  http://localhost:8780/v1/summary        ── plain HTTP, loopback only
  ▼
Medium Metrics.exe → ApiServer (HttpListener) → reads latest.json / report.csv
```

The whole path is preserved across the hop, so the Action's base URL is simply
`https://<your-host>/v1` and the app keeps routing `/v1/...` exactly as in §5.

### One-time setup — named tunnel (recommended, stable hostname)

Prereqs: a domain managed in Cloudflare (free plan is fine) + `cloudflared`.

```powershell
winget install --id Cloudflare.cloudflared      # or download cloudflared.exe
cloudflared tunnel login                         # browser: authorize your zone
cloudflared tunnel create medium-metrics         # creates the tunnel + a <UUID>.json credential
cloudflared tunnel route dns medium-metrics metrics.example.dev   # CNAME → this tunnel
```

Create `C:\Users\<you>\.cloudflared\config.yml`:

```yaml
tunnel: <TUNNEL-UUID>
credentials-file: C:\Users\<you>\.cloudflared\<TUNNEL-UUID>.json
ingress:
  - hostname: metrics.example.dev
    service: http://localhost:8780      # the app's ApiServer port
  - service: http_status:404            # required catch-all
```

Run it in the foreground, or install it as a service so it survives reboots:

```powershell
cloudflared tunnel run medium-metrics    # foreground
cloudflared service install               # optional: run at boot as a Windows service
```

Then set the GPT Action's server URL to `https://metrics.example.dev/v1` (§8/§9) and
paste the app's bearer key.

### Quick tunnel (testing only, no domain)

One command, instant HTTPS — but the hostname is **random and changes every run**, so
you must re-paste the Action URL each time:

```powershell
cloudflared tunnel --url http://localhost:8780
# → prints https://<random-words>.trycloudflare.com
```

### "App closed" behaviour — a clean signal, not a bug

If `cloudflared` runs as a service but the app isn't open, nothing is listening on
`:8780`, so Cloudflare returns **502 Bad Gateway**. The GPT instructions (§9) should
read 502/503 as *"the Medium Metrics app isn't running — open it and click Refresh."*
Closing the app is therefore an instant way to revoke access.

### Other tunnels (if you don't use Cloudflare)

ngrok (`ngrok http 8780`) or Tailscale Funnel (`tailscale funnel 8780`) work too — the
same single-base-URL caveat applies. A VPS that hosts/replicates the API would be
always-on but **defeats local-first** (your data would live off your PC) — out of scope.

> **Boundary:** the app should **not** spawn or manage `cloudflared`. Settings shows
> the port and these commands for reference; you start and stop the tunnel yourself, so
> the ingress stays explicitly under your control.

### Also usable locally (no tunnel)

The tunnel exists **only** so OpenAI's cloud can reach the server. The listener binds
to `127.0.0.1:8780`, so **any process on your PC can call it directly** at
`http://localhost:8780/v1/...` with no tunnel running at all:

- `curl` / PowerShell `Invoke-RestMethod` for quick checks.
- Local scripts, exports, dashboards (Excel / Power Query, Grafana, …).
- A **local LLM** (Ollama, LM Studio, …) playing the same "analyst" role — the fully
  local-first alternative to the Custom GPT: no tunnel, **nothing leaves your PC**, and
  the [§2](#2-the-core-tension-read-this-first) tension disappears entirely. The same
  OpenAPI schema (§8) doubles as the tool/function spec for a local model.

Notes:

- **The bearer key still applies locally** (every endpoint except `/health`). Do **not**
  bypass auth for loopback: `cloudflared` forwards tunnel traffic *to* loopback too, so at
  the listener a tunnel request is indistinguishable from a genuine local one —
  "loopback = trusted" would silently unauthenticate the whole internet-facing path.
- Loopback binding needs **no admin / `urlacl`** reservation. Reaching the API from other
  devices on your LAN is a different, deliberate step (`0.0.0.0` bind + `netsh http add
  urlacl` + a firewall rule) and is intentionally out of the default scope.

---

## 11. Security & privacy

Defense in depth, smallest-blast-radius first:

1. **Off by default; app-lifetime only.** No listener until you enable it; it stops
   when the app closes. There is no background service.
2. **Bind to `127.0.0.1`.** Never `0.0.0.0`, so nothing is exposed on your LAN. The only
   *remote* ingress is the tunnel you start; same-PC processes can also reach loopback
   directly (intended — see §10's "Also usable locally"), and the key gates them too.
3. **Bearer key on every data endpoint.** Constant-time compare; generic `401` on
   failure. Rotate any time; regeneration is instant revocation.
4. **Read-only by construction.** Only `GET`; no handler writes local files or calls
   Medium to mutate anything.
5. **Minimal exposure surface.** Serve derived metrics only. **Never** expose
   `session.bin`, DPAPI blobs, the `webview2/` profile, raw `settings.json`, or file
   paths. The API has no endpoint that can read arbitrary files.
6. **Rate limiting.** A simple per-key limiter (e.g. token bucket) is plenty — every
   endpoint is a local file read (no Medium calls), so this is just basic hygiene
   against a runaway caller on a leaked URL.
7. **Cloudflare-side controls (optional — with one caveat).** A Custom GPT Action
   sends a **single** auth header, so you can't *also* pass Cloudflare Access
   **service-token** headers (`CF-Access-Client-Id`/`Secret`), and interactive
   Cloudflare Access logins are impossible for a server-to-server caller — so the
   **app's bearer key stays the real gate.** What you *can* add at the edge: a **WAF
   custom rule** that drops requests missing an `Authorization` header or outside
   `/v1/*`, plus **rate-limiting rules**. IP allowlisting is unreliable (OpenAI's
   egress isn't a fixed, small set).
8. **Logging.** Log requests to `app.log` *without* the key or full URLs with secrets;
   useful to spot unexpected traffic on a leaked URL.
9. **Know what leaves the machine.** When enabled, your **metrics** (titles, tags,
   counts, earnings, history) become reachable by OpenAI's servers (and pass through
   your tunnel provider). That is the cost of the feature; §2 states it plainly and it
   should be restated in the Settings UI when you flip the switch.

---

## 12. Implementation notes (where this lands in the codebase)

> Sketch only — for when implementation starts. Chosen to honour "small, single-file,
> few dependencies."

- **New `Services/ApiServer.cs`** built on **`System.Net.HttpListener`** (in the BCL,
  **zero new NuGet deps** → preserves the small self-contained publish, same spirit as
  the dependency-free WPF earnings chart). Serialize with `System.Text.Json` (already
  used). Routing is a small switch over method + path segments.
  - *Alternative:* ASP.NET Core Minimal API + Swashbuckle would auto-generate the
    OpenAPI doc and give nicer routing, but it pulls the ASP.NET stack into the
    single-file publish. Prefer `HttpListener` unless auto-generated schema upkeep
    becomes the bottleneck.
- **Reuse, don't duplicate:** per request, resolve `?account=` to an `AccountConfig`,
  build a `ReportStore` for it, and call `LoadLatest()` / `ReadHistory()` and the pure
  `Reports.*` functions. The account list comes from `SettingsStore.Load()`. No new
  persistence.
- **Lifecycle & UI:** a **Settings** toggle "Enable local API for Custom GPT," a port
  field (default `8780`), a generated key (Copy / Regenerate), a running/stopped
  indicator, and the §2 exposure warning shown on enable. Start/stop the listener with
  the toggle; stop on app exit. If the configured port is busy, bind a free one, persist
  it, and reflect it in the port box + base-URL line (so it's never a hard failure).
- **Key:** generate with `RandomNumberGenerator`; persist via `SecretStore`
  (DPAPI) as `apikey.bin` — **not** in `settings.json` (see [§7](#7-authentication)).
- **Threading:** run the accept loop on a background thread. Every endpoint is a file
  read (cheap), opened with `FileShare.ReadWrite` so a read never blocks the app's
  Refresh writes; `report.csv` is append-only so a concurrent read just may miss the
  in-flight row (acceptable). Per-story detail is served from `details/<id>.json`,
  written by the UI on fetch — so the API needs **no** WebView2/Dispatcher marshaling.
- **Account default:** `ActiveAccountId`; 404 (`account_not_found`) for unknown ids,
  409 (`no_snapshot`) when an account has never been refreshed.
- **Tests:** the aggregations are already covered (`ReportsTests`). Add thin tests for
  param parsing (sort/filter/pagination), auth (401 paths), and account resolution —
  the handlers should stay thin enough to test without a live listener.

---

## 13. Non-goals

- **No write/mutation endpoints.** The GPT cannot Refresh, post, edit tags, or change
  settings. Refresh stays manual and in-app (deliberate, ToS-respecting). A future
  opt-in `POST /v1/refresh` is conceivable but is **out of scope** here.
- **No multi-user / no auth proxying.** One owner, one key. Not a SaaS.
- **No always-on hosting.** PC on + app open + tunnel up, or it's unavailable. (A VPS
  would break local-first; see §10.)
- **No exposure of secrets or arbitrary files.** Ever.
- **Not a public/shared GPT.** Keep it "Only me."

---

## 14. Open questions / decisions

- **Server tech:** `HttpListener` (recommended, zero-dep) vs ASP.NET Core (auto
  OpenAPI). Default = `HttpListener`.
- **Detail endpoints:** ✅ resolved — `/stories/{id}/detail` and `/tags/followers` ship
  **cache-backed**, populated by the UI; the API never calls Medium. (A live
  refresh-on-demand path remains out of scope.)
- **Tunnel ownership:** app shows instructions only vs app launches `cloudflared`.
  Default = **instructions only** (explicit user control).
- **Account selection:** `?account=` per request (chosen) vs a server-side "active
  account" the GPT can switch via a call. Default = query param + `ActiveAccountId`.
- **Response compactness:** add a `fields=` projection or a `compact=true` mode to cut
  tokens on big lists? Revisit if story lists strain the Action response budget.
- **`freeLink` field:** include the Freedium mirror link or drop it (third-party
  dependency)? Default = include, clearly labelled.
- **Port:** default `8780`; if it's busy at startup the app binds an OS-assigned free
  port instead, then **persists + shows it** (stable across runs — never randomized per
  launch, so the tunnel/GPT Action stay valid). If a fallback happens, update your tunnel.

---

*Companion docs: [APP_IDEA.md](APP_IDEA.md) (philosophy & stack), [README.md](README.md)
(data locations), [docs/private/medium-api-notes.md](docs/private/medium-api-notes.md)
(why claps are unavailable).*

# Medium internal stats — endpoint notes (UNOFFICIAL)

> These are **undocumented** endpoints used by Medium's own web UI. They can change
> without notice. All code that depends on them lives in `MediumStatsClient.cs` so a
> break is a one-file patch. Verify with browser DevTools (Network tab) on
> https://medium.com/me/stats and update this file when something drifts.

## ⚠️ Cloudflare (why we must use a real browser)

Medium is behind **Cloudflare's managed challenge**. A plain `HttpClient` request —
even with the valid session cookie and browser-like headers — gets **HTTP 403**
with `Cf-Mitigated: challenge` and a "Just a moment..." HTML body. Cloudflare ties
clearance to the browser's JS/TLS fingerprint, so it can't be reproduced from
HttpClient.

**Solution:** requests go through `MediumBrowser` — a hidden, off-screen WebView2
that shares the login profile (so it has the `cf_clearance` cookie) and runs an
in-page `fetch()` from a medium.com page. `MediumStatsClient` no longer does HTTP;
it just builds URLs and parses what the browser returns.

Confirmed 2026-06-05 against a real account: `GET /me?format=json` → 403 challenge
via HttpClient.

## Anti-hijacking prefix
Every JSON response is prefixed with `])}while(1);</x>` (exact bytes may vary slightly,
e.g. `])}while(1);</x>`). Strip everything up to the first `{` before parsing.

## Current user + followers  (STILL WORKS, 2026-06-05)
- **GET** `https://medium.com/me?format=json` (via the browser; returns JSON)
- Path to fields (under the anti-hijack-stripped root):
  - `payload.user.username` — account handle (needed for the GraphQL query)
  - `payload.user.socialStats.usersFollowedByCount` — **followers**

## Per-story stats  (NEW — GraphQL, confirmed 2026-06-05)
`/me/stats?format=json` is **dead** — it now returns the SPA HTML page. The stats
page loads per-story numbers via GraphQL:
- **POST** `https://medium.com/_/graphql` (same-origin, in-page fetch)
- Body is a batched array: `[{ operationName, variables, query }]`
- operationName: `UserLifetimeStoryStatsPostsQuery`
- variables: `{ username, first, after, orderBy:{publishedAt:"DESC"}, filter:{published:true} }`
- Response (batched array): `[{ "data": { "user": { "postsConnection": {`
  - `edges[].node.id`
  - `edges[].node.title`
  - `edges[].node.totalStats.{presentations, views, reads}`  ← views/reads
  - `pageInfo.{endCursor, hasNextPage}`  ← paginate with `after = endCursor`
- **Claps/responses are NOT in Medium's stats API at all** (checked every captured
  query). The app shows **Impressions** (`presentations`) and **Earnings**
  (`node.earnings.total.{units,nanos}`, USD = units + nanos/1e9) instead.
- Other operations seen on the page: `UserMonthlyStoryStatsTimeseriesQuery` (monthly
  chart) — not used yet.

Discover/refresh these via **Settings → Capture stats (debug)**, which dumps all
GraphQL traffic to `graphql-capture.json`.

## Required request headers
- `Cookie: <full medium.com cookie header captured by WebView2>`
- `Accept: application/json`
- A desktop `User-Agent` (Medium may 403 obviously-bot agents).

## Failure handling
On any parse failure, the raw response body is dumped to `data/last_error.json`
so the actual shape can be diffed against this file.

## TODO (verify on real account)
- [ ] Confirm followers JSON path.
- [ ] Confirm claps vs upvotes field name currently in use.
- [ ] Confirm pagination (is `limit=200` enough / does it page with `to=<ts>`?).
- [ ] Find responses/comments count source.

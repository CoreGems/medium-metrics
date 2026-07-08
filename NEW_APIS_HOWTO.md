# New APIs — how-to / design (content, per-story growth, referrers, and friends)

> **Design only — no code.** A feasibility + implementation guide for the 10 proposed read
> endpoints that would turn the local API from "how are my stats?" into "help me write the
> next viral piece." Grounded in what the app already captures (see [OPENAI_CUSTOM_GPT.md](OPENAI_CUSTOM_GPT.md))
> and what Medium actually exposes (see [docs/private/medium-api-notes.md](docs/private/medium-api-notes.md)).

## Guiding principle: the app is the *data layer*, the GPT is the *analyst*

The most useful split is: **the local app captures and serves facts the GPT can't get on its
own** (your actual prose, per-story growth over time, referrers, conversions, publication
status), and **the GPT does the reasoning** (find patterns, judge similarity, critique a
draft). So prioritize endpoints that surface *new facts*, and resist baking half-an-LLM into a
small WPF app. Three of the ten (#8 patterns, #9 similar, #10 draft scoring) are mostly the
GPT's job once it has the facts — covered at the end.

## What we already have (reuse inventory)

| Already captured | Where | Reusable for |
|---|---|---|
| Per-story: id, title, url, publishedAt, views, reads, impressions, claps*, earnings, tags | `latest.json` (`StorySnapshot`) | #6, #8, #9 |
| Per-story detail: viewers/readers, feed CTR, followers/subscribers gained, **referrers** | `details/<id>.json` (`StoryDetail`, cached on UI fetch) | #4, #7 |
| Account history over time | `report.csv` | model for #2/#3 |
| Earnings deltas (account) | `Reports.DailyEarnings` | model for #3 |

\* claps are usually `0` — not in Medium's current stats API.

**Two gaps block most of the wish-list, and each is a reusable enabler:**
- **Per-story history is not stored.** `latest.json` is *overwritten* every refresh, so we keep
  only the newest number per story. Nothing accrues over time. → enabler **E1**.
- **Article content is not captured at all.** No body text, subtitle, kicker, word count,
  hero image, language. → enabler **E2**.

⚠️ **Two facts to internalize before building:**
- **History can't be backfilled.** #2, #3, #5 only accrue *from the day you start logging*. So
  the single highest-leverage move is to **start the per-story append now**, even before the
  endpoints exist — every day you wait is data you never get.
- **Everything stays read-only & cache-backed.** New "capture" data is fetched through the app
  (your own account, gently), persisted under `accounts/<id>/`, and the API only ever *reads*
  the cache — it never calls Medium itself (same model as the existing `/detail`).

---

## The ten, grouped by what they need

### Group A — we already have the data (just expose it). Effort: **S**

**#4 — `GET /v1/stories/{storyId}/referrers`** — `getStoryReferrers`
- **Source:** `StoryDetail.Referrers` (already cached on UI fetch). Pure reshape of existing data.
- **Caveat:** Medium's referrer data is **views (count) per source** — it does **not** give
  reads/read-ratio per source. So serve `source`, `type`, `count`; **drop `reads`/`readRatio`
  per referrer** from the proposed shape (or mark them null). Returns `404 detail_not_cached`
  until the story's been opened in the app, same as `/detail`.
- **Verdict:** ship it; it's a thin projection of cached detail + a `coverage` note.

**#7 — `GET /v1/stories/{storyId}/conversions`** — `getStoryConversions`
- **Source:** `StoryDetail` (followersGained, subscribersGained) + the snapshot's `reads`.
- **Compute:** `conversionRateFromReads = followersGained / reads` (guard reads=0).
- **Caveat:** `emailSubscribersGained` — Medium "subscribers" likely already *are* email
  subscribers; there isn't a separate email metric, so set it equal to subscribers or omit it
  (don't invent a field). Cache-backed (needs detail fetched).
- **Verdict:** ship it; derived endpoint, no new capture.

### Group B — need per-story history logging (enabler **E1**)

**E1 — start appending per-story rows on every refresh.** Today the refresh overwrites
`latest.json`; add an **append-only per-story log** alongside it, e.g.
`accounts/<id>/stories-history.csv` (or one CSV per story under `stories-history/<id>.csv`).
One row per story per refresh: `timestamp, storyId, views, reads, impressions, earnings,
title` (+ subtitle once E2/#6 capture it). This mirrors the existing `report.csv` pattern and
is the foundation for #2, #3, and #5. **Builds forward only.**

**#2 — `GET /v1/stories/{storyId}/history`** — `getStoryHistory` — Effort: **M** (mostly E1)
- **Source:** the per-story log (E1). Returns refresh-level `points[]` (views/reads/earnings/impressions...).
- **Caveat:** `fans`/`followersGained` per point need the per-story *detail* logged too, which
  only exists when you open the story; so those columns are sparse — populate when available,
  null otherwise. This is the endpoint that answers "viral immediately vs. slow SEO burn."

**#3 — `GET /v1/stories/{storyId}/daily`** — `getStoryDaily` — Effort: **S** (once E1 exists)
- **Source:** computed from #2's series exactly like `Reports.DailyEarnings` does for the
  account — latest value per local day, then day-over-day deltas. New columns: per-day views,
  reads, readRatio, earnings, impressions. "Which posts still have legs vs. one-day spikes."

**#5 — `GET /v1/stories/{storyId}/title-history`** — `getTitleHistory` — Effort: **M**
- **Source:** E1's log, but it must also capture **subtitle** (not captured today — add it to
  the stories query, see #6). Derive a change event whenever title/subtitle differs from the
  prior row. Most rows are unchanged, so emit *changes*, not every snapshot.
- **Payoff:** pair a title change's timestamp with #3 to answer "did the new title lift views/
  read-ratio/earnings?" — genuinely useful and unavailable anywhere else.

### Group C — need article content capture (enabler **E2**). Effort: **L**

**E2 — a content fetcher + store.** The app already drives a signed-in WebView2; add a step
that fetches *your own* post's content (a Medium post-content GraphQL query, or the rendered
article page) and stores it at `accounts/<id>/content/<storyId>.json`. Reverse-engineer the
exact query the same way the stats queries were found (Settings → Capture, isolate in
`MediumStatsClient`, dump raw on parse failure). Read-only against your own account; gentle/
on-demand (e.g. a "fetch content" action or piggy-backed on opening a story).

**#1 — `GET /v1/stories/{storyId}/content`** — `getStoryContent` — **the headliner**
- **Source:** E2's cache. Fields and where each comes from:
  - `title`, `tags`, `publishedAt`, `canonicalUrl` — already in the snapshot / post metadata. ✅
  - `subtitle`, `kicker`, `bodyText`, `heroImageAlt`, `heroImageCaption`, `publication` — from
    the post content fetch (E2). ✅ (publication may be `null` for self-published.)
  - `wordCount`, `readingTimeMinutes` — **compute locally** from `bodyText` (words; ~265 wpm). ✅
  - `language` — Medium exposes `detectedLanguage` on the post; else detect locally. ⚠️ optional.
- **Why it's #1:** it's the only thing that lets the GPT analyze your *prose* — "what made this
  work," "imitate my best structure," "which opening style reads best," "write one in the style
  of my viral Ukraine pieces." Without it the GPT can only compare titles/tags/numbers.
- **Verdict:** highest value, highest effort. Cache-backed; `404 content_not_cached` until fetched.

### Group D — extend the per-story capture. Effort: **M**

**#6 — publication / distribution status** (fields on `/stories` and `/stories/{id}`)
- **Source:** extend the `UserLifetimeStoryStatsPostsQuery` (or a sibling post query) to pull
  post fields. Medium availability, realistically:
  - `publicationName` / `isInPublication` — from `post.collection`/`homeCollection`. ✅ likely
  - `paywalled` / `memberOnly` — from `post.isLocked`. ✅ likely (often the same flag)
  - `isBoosted` / `distributionStatus` / curation — Medium exposes some via
    `post.distributionStatus` / `isDistributed`, but **boost/curation is the least reliable**;
    expose what's present and return `"unknown"` for the rest (don't fabricate). ⚠️ partial
- **Verdict:** worth it; add the reliably-available fields, label the rest `unknown`, and also
  start logging them in E1 so changes (e.g. "added to a publication") are visible over time.

### Group E — local analytics (compute from the above). Effort: **M–L**

**#8 — `GET /v1/reports/patterns`** — `getPatterns`
- **Word-count buckets** → need `wordCount` from **#1/E2**. Once you have content, bucketing by
  length × avg read-ratio/earnings is a clean local aggregation (like `Reports.MetricsByTag`). ✅
- **Title patterns** → *structural* ones are local and easy (question headline = ends `?`,
  has-number, has-colon, length band). But **semantic** patterns like "Metaphor + Putin" are an
  LLM judgment, not a regex — **let the GPT cluster those** over `/stories` + `/content`. ⚠️
- **Verdict:** ship the word-count buckets + structural title features; leave semantic
  clustering to the GPT (it's better at it and it changes per topic).

**#9 — `GET /v1/stories/{storyId}/similar`** — `getSimilar`
- **Local, cheap version:** rank other stories by **tag overlap (Jaccard)** + **title-token
  overlap** + **performance-shape** proximity. No content or LLM needed; deterministic. ✅
- **Semantic version** ("resembles your Putin/Ukraine essays in tone") needs embeddings — that's
  the GPT's job given `/content`. Offer the cheap version; let the GPT do nuance. ⚠️
- **Verdict:** ship tag/title/metric similarity; defer semantic to the GPT.

### Group F — rethink

**#10 — `POST /drafts/analyze`** — **don't build it as proposed.**
- It's a **POST** (breaks the strictly-GET, read-only model) *and* the "analysis" (predicted
  strengths, fit tags, risks) is exactly what the **GPT already does** — better — when handed
  the read-only archive (#1 content, #8 patterns, #9 similar). Building it server-side means
  either shipping an LLM in the WPF app (no) or a thin retrieval stub.
- **Better:** the GPT analyzes a pasted draft against the read-only endpoints. If you want a
  server helper, make it a **GET** retrieval ("given these tags/keywords, return similar past
  stories + recommended length from word-count buckets") — facts, not judgment. Keeps the API
  read-only and the smarts in the model.

---

## Effort + dependency summary

| # | Endpoint | Status | Effort | Needs |
|---|---|---|---|---|
| 4 | `/stories/{id}/referrers` | have data | **S** | — (cached detail) |
| 7 | `/stories/{id}/conversions` | have data | **S** | — (cached detail + reads) |
| 2 | `/stories/{id}/history` | capture (E1) | **M** | per-story logging |
| 3 | `/stories/{id}/daily` | compute | **S** | #2 |
| 5 | `/stories/{id}/title-history` | capture (E1+subtitle) | **M** | #2, subtitle field |
| 1 | `/stories/{id}/content` | capture (E2) | **L** | content fetcher |
| 6 | publication/distribution fields | capture (extend query) | **M** | extra post fields |
| 8 | `/reports/patterns` | compute | **M–L** | #1 (word counts) + GPT for semantics |
| 9 | `/stories/{id}/similar` | compute | **M** | local; GPT for semantics |
| 10 | `/drafts/analyze` | rethink | — | keep in the GPT |

## Recommended build order

Your priority ranking (content → per-story growth → referrers → titles → conversions →
patterns → similar) is right on *value*. Reordered for *dependencies + cost*:

- **Phase 0 — free wins (days):** #4 referrers, #7 conversions. The data's already cached;
  these are projections. Ship first.
- **Phase 1 — start logging NOW (the enabler):** E1 per-story append (+ capture subtitle).
  This unlocks #2, then #3 and #5 cheaply — **and it can't be backfilled, so begin immediately
  even if the endpoints come later.**
- **Phase 2 — the headliner:** E2 content fetch → #1. Highest value (your prose); biggest lift.
  If prose analysis is the real goal, you may want to start E2 in parallel with Phase 1.
- **Phase 3 — extended capture:** #6 publication/distribution fields (and log them via E1).
- **Phase 4 — local analytics (optional):** #8 word-count buckets + structural title features,
  #9 tag/title/metric similarity. Or skip and let the GPT do these over the Phase 0–3 facts.
- **Always GPT-side:** #8 semantic clusters, #9 semantic similarity, #10 draft critique.

## Cross-cutting notes

- **Read-only, cache-backed, loopback+tunnel** — unchanged. New captures persist under
  `accounts/<id>/` (`content/`, `stories-history.csv`); the API reads the cache with
  `FileShare.ReadWrite` and never calls Medium. `/drafts/analyze` POST is the one thing that
  would break this model — hence "keep it in the GPT."
- **Builds forward only** for #2/#3/#5/#6-history — log early.
- **Medium availability is the real constraint**, not the API shape. Confirm each new field via
  the capture/debug dump before trusting it; mark anything unconfirmed `unknown`/null rather
  than inventing it (claps already taught us this).
- **Gentle on ToS:** content/detail fetches are your own posts, on demand, rate-limited — same
  manual-refresh ethos.
- **OpenAPI + ChatGPT importer:** add each path to `openapi.json` with inlined params, **no
  `$ref` parameters, no `components` block** (the importer rejects them — see the saga in
  [WHAT_WRONG_WITH_NGROK.md](WHAT_WRONG_WITH_NGROK.md) and §8 of the design doc). Keep payloads
  small (paginate content/history; `bodyText` can be large — consider a `?fields=` or a
  `bodyText`-excluding list mode so the GPT doesn't blow its token budget pulling 300 articles).
- **Multi-account:** every new endpoint takes `?account=` and defaults to active, like the rest.

## What this unlocks (the point)

With #1 (content) + #2/#3 (per-story growth) + #4 (referrers) + #7 (conversions), the GPT can
finally answer the questions that matter for *writing better*, not just *reporting*: what your
winning prose has in common, whether a hit was instant or SEO-grown, where readers came from,
and which pieces actually built your audience — then "write another one like the ones that
worked." That's the jump from a stats dashboard to a writing-strategy engine.

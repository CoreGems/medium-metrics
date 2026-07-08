# Creator Stats Black Box — the multi-platform play

> **Strategy + design only — no code.** Elaborates Option H of
> [HOW_TO_MAKEMONEY_FROM_THIS.md](HOW_TO_MAKEMONEY_FROM_THIS.md): generalizing the
> Medium Metrics architecture — *local capture of session-gated creator stats +
> append-only history + local API + AI analyst* — into a product for a creator's
> **whole** business. Written 2026-07. Platform facts below are a point-in-time
> survey: **verify each platform live before building its module** (same rule as
> Medium — you can't design against undocumented endpoints from memory).

## The pitch

> **"The flight recorder for your creator business.**
> Every platform, one local, private, append-only history — with an AI analyst
> on top. **Your logins never leave your machine."**

Platforms show creators *current* numbers. They rarely show history, never show it
across platforms, and always gate the good numbers behind a login. Cloud analytics
tools ask creators to hand over credentials or OAuth grants and can only see what
official APIs expose. The black box flips both: capture runs **on the creator's
machine, with their own sessions**, sees **everything their browser sees**, and
appends it to a history **they own, forever — even after a platform dies.**

## Contents

1. [The thesis — why this can exist at all](#1-the-thesis--why-this-can-exist-at-all)
2. [What Medium Metrics already proved (the kernel)](#2-what-medium-metrics-already-proved-the-kernel)
3. [Platform survey — where the good numbers live](#3-platform-survey--where-the-good-numbers-live)
4. [Architecture — from one app to a platform](#4-architecture--from-one-app-to-a-platform)
5. [The unified data model](#5-the-unified-data-model)
6. [Features only this product can have](#6-features-only-this-product-can-have)
7. [Competition & positioning](#7-competition--positioning)
8. [Business model](#8-business-model)
9. [Roadmap with stop-gates](#9-roadmap-with-stop-gates)
10. [The honest wall (risks)](#10-the-honest-wall-risks)
11. [What NOT to build](#11-what-not-to-build)
12. [Naming](#12-naming)
13. [Open questions](#13-open-questions)

---

## 1. The thesis — why this can exist at all

Four structural facts, none of which any single competitor can fix, make the niche
real:

**1. Official APIs expose less than the creator's own dashboard.**
Cloud analytics tools (Metricool, Buffer's analytics, Favikon, Social Blade, every
"creator OS" pitch deck) are built on official APIs and OAuth — because that's the
only thing that scales *for a cloud service*. But for most writing/earning platforms
the official API is missing, paywalled, or a shadow of the dashboard: Medium has
**no** stats API at all; Substack has **no** public API; X's API is paywalled and
still doesn't match the analytics dashboard; LinkedIn's is locked behind
partnerships. The *good* numbers — earnings, read ratios, open rates, per-post
audience conversion — render only in the creator's own signed-in browser. **A
local-first agent is the only architecture that can legally-ish and technically see
them**, because it *is* the creator's own browser session, at human rates, reading
their own account.

**2. History can't be backfilled — on any platform.**
The founding insight of Medium Metrics generalizes everywhere: dashboards show
*now*. Substack shows current subscriber counts; X shows a 28-day window; YouTube
keeps history but only its own aggregations. The moment a creator starts the black
box, they begin accruing an asset that **cannot be recreated later and that only
they hold**. Every day of operation deepens the moat per user. This is the same
lock-in flywheel as the Medium app, multiplied by N platforms.

**3. Creators have learned to distrust cloud tools with credentials.**
Every year some analytics/scheduling tool gets breached or gets users' accounts
flagged. Meanwhile the platforms train creators to fear connecting third-party
apps. "**Nothing to hand over — it runs on your machine**" is a pitch no cloud
competitor can copy without ceasing to be a cloud company. Nobody credible occupies
"privacy-first creator analytics." The position is empty because it's structurally
incompatible with VC-scale SaaS — which is exactly why a solo/indie builder can
hold it.

**4. Platforms die; the ledger shouldn't.**
Revue (killed by Twitter, data deleted). Google+. Vine. Cohost. Medium's own
near-death cycles. A creator's numbers routinely outlive nothing — the black box
makes them outlive *everything*. "Your history survives the platform" is both a
real feature (append-only local files, exportable) and the emotional core of the
brand: a **flight recorder** is the thing that survives the crash.

The synthesis: *audience and revenue data is the creator's balance sheet, and today
it's scattered across a dozen dashboards that only show this week.* The black box
is the accounting system: one private ledger, every platform, full history, with an
analyst on top.

---

## 2. What Medium Metrics already proved (the kernel)

The current app is a working single-platform instance of the pattern. Almost every
piece generalizes — this table is the extraction map:

| Medium Metrics today | Generalizes to | Notes |
|---|---|---|
| `MediumStatsClient.cs` — *all* platform-specific logic in one file, raw-dump-on-parse-failure | **`IPlatformModule`** — one isolated capture module per platform | The "one-file patch" design decision *was* the platform architecture, before it knew it |
| WebView2 sign-in → session capture | **Auth strategy A: session capture** (Medium, Substack, X, LinkedIn) | Already handles the hardest auth case |
| DPAPI `SecretStore` (`session.bin`, `apikey.bin`) | Per-platform, per-account encrypted secret store | Same primitive, keyed by platform+account |
| `accounts/<id>/{latest.json, report.csv, details/}` | `platforms/<platform>/accounts/<id>/…` | The multi-account registry becomes two-level |
| `report.csv` append-only account history + per-story history log | Append-only history per platform per account | The core covenant: **never overwrite, always append** |
| `Reports.*` (by tag / month / year, daily earnings deltas) | Cross-platform report engine over normalized snapshots | Deltas-not-cumulatives lesson carries over verbatim |
| `ApiServer` — localhost, bearer key, read-only, cache-backed, OpenAPI | Same server, namespaced routes + cross-platform rollups | The API **never calls platforms** — unchanged |
| Custom GPT / MCP analyst ("app = data layer, GPT = analyst") | **One analyst over all platforms** — where the magic multiplies | Cross-platform questions are the killer demo (§6) |
| Reverse-engineering method (capture → isolate → dump raw on failure) | The repeatable playbook per new platform | Documented in `docs/private/medium-api-notes.md`; the *method* is the asset |
| Ethos: local-first, read-only, gentle, opt-in exposure | The brand | Non-negotiable across all modules |

**What does *not* exist yet:** the module interface, the normalized cross-platform
data model (§5), a scheduler with per-platform politeness policies, and any second
platform. That's the honest gap — the kernel is real, the *platform-ness* is one
refactor plus one module away.

---

## 3. Platform survey — where the good numbers live

> ⚠️ **Point-in-time (mid-2026), from general knowledge — re-verify every row with a
> live capture session before building.** Platform APIs and dashboards churn
> constantly; that churn is simultaneously this product's reason to exist and its
> maintenance bill.

The survey answers, per platform: what a creator earns there, what the dashboard
shows behind login, what the official API gives a cloud competitor, and therefore
whether *local capture* has exclusive access to anything.

| Platform | Creator money | Dashboard-only gold (behind login) | Official API for own stats | Capture approach | Effort | ToS heat | Local-capture edge |
|---|---|---|---|---|---|---|---|
| **Medium** | Partner Program | Views/reads/read-ratio, earnings, referrers, follower conversion | **None** | Session (done ✅) | — | Low (own acct, manual) | **Total** — no API exists |
| **Substack** | Paid subscriptions | Subscriber counts + growth, per-post opens/views, traffic sources, paid conversion | **None public** (dashboard uses private JSON) | Session, like Medium | M | Low-ish | **Total** — cloud tools are blind here |
| **YouTube** | Ads, memberships | Studio: impressions, CTR, retention curves | **Good** — YouTube Analytics API (OAuth) covers most of Studio | **OAuth loopback** (official!) | S–M | None (official API) | Low exclusivity — value is *unification + permanent history*, not access |
| **Gumroad** | Product sales | Sales, fees, affiliates | **Good** — official API | API key paste | S | None | Low — value is the unified revenue ledger |
| **Ko-fi** | Tips, memberships, shop | Supporter detail, trends | Minimal (payment webhooks) | Webhook listener + session for the rest | S | Low | Medium |
| **Patreon** | Memberships | Member churn detail, tiers | **Decent** — official OAuth for own campaign | OAuth loopback | S–M | None | Low–medium |
| **beehiiv / Ghost** | Newsletter subs | Opens, clicks, subscriber growth | **Yes** — both have real APIs | API key | S | None | Low — but cheap to add, rounds out the newsletter story |
| **X (Twitter)** | Ad rev-share, subs | Analytics dashboard (impressions, engagement), payout amounts | Paywalled, limited, unstable | Session — **but see heat** | M–L | **High** — aggressive anti-automation, litigious | High exclusivity, high fragility |
| **LinkedIn** | Indirect (leads, deals) | Creator analytics: impressions, follower demographics, newsletter stats | Locked to partners | Session — **but see heat** | M–L | **High** — hostile to any automation | High exclusivity, high fragility |
| **TikTok / Instagram** | Creator funds, brand deals | Studio analytics | Restricted / partner-only | Mobile-first, aggressive defenses | XL | **Very high** | Not worth it — **defer indefinitely** |
| **Twitch** | Subs, bits, ads | Detailed stream analytics | Decent official API | OAuth | M | None | Different audience entirely — only if the product drifts toward streamers |

Three design conclusions fall out of the table:

1. **The product is a hybrid, not a dogma.** Session capture where official APIs
   are absent (Medium, Substack — the *exclusive* data); official OAuth/API-key
   modules where they're good (YouTube, Gumroad, Patreon, beehiiv — the *cheap
   breadth*). Even for official-API platforms the differentiator holds: **tokens
   and data stay local.** A cloud competitor can match the YouTube module; it can
   never match the Substack one or the privacy posture.
2. **Substack is unambiguously module #2.** Total local-capture exclusivity, the
   heaviest audience overlap with Medium writers (many run both), the most
   earnings-obsessed user base on the internet, and it validates the module
   interface with a second *session-capture* platform — the hard kind.
3. **X and LinkedIn are demand-gated, fragility-priced add-ons** — build only when
   paying users ask, ship marked "best-effort, may break monthly," and never let
   them touch the default install. TikTok/Instagram: no.

---

## 4. Architecture — from one app to a platform

```
   ┌─────────────────────────────────────────────────────────────────────┐
   │  Creator Black Box.exe                (everything below = local)     │
   │                                                                      │
   │  ┌────────────── capture layer (per-platform modules) ────────────┐  │
   │  │ MediumModule     SubstackModule    YouTubeModule   GumroadModule│  │
   │  │ (session/WV2)    (session/WV2)     (OAuth loopback)(API key)    │  │
   │  │   each: Authenticate() · Capture() · ParseToSnapshot() ·        │  │
   │  │         RawDumpOnFailure() · PolitenessPolicy                   │  │
   │  └───────────────┬─────────────────────────────────────────────────┘  │
   │                  ▼  normalized UnifiedSnapshot + raw payload           │
   │  ┌─────────────── store (append-only covenant) ────────────────────┐  │
   │  │ platforms/<p>/accounts/<id>/latest.json      (current snapshot) │  │
   │  │ platforms/<p>/accounts/<id>/history.csv      (account series)   │  │
   │  │ platforms/<p>/accounts/<id>/items-history/   (per-post series)  │  │
   │  │ platforms/<p>/accounts/<id>/raw/             (native payloads)  │  │
   │  │ secrets: DPAPI per platform+account          (never leaves box) │  │
   │  └───────────────┬─────────────────────────────────────────────────┘  │
   │                  ▼ read-only                                          │
   │  ┌── reports engine ──┐   ┌── ApiServer (127.0.0.1, bearer) ───────┐  │
   │  │ per-platform +     │   │ /v1/…              FROZEN — existing   │  │
   │  │ cross-platform     │──▶│                    Medium GPT contract │  │
   │  │ rollups, deltas    │   │ /v2/platforms      (capability map)    │  │
   │  └────────────────────┘   │ /v2/{platform}/…   (generic routes)    │  │
   │                           │ /v2/summary · revenue · audience       │  │
   │                           │                (cross-platform rollups)│  │
   │                           └───────────┬────────────────────────────┘  │
   └───────────────────────────────────────┼───────────────────────────────┘
                                           ▼
                        MCP server (pure localhost, no tunnel)  ← preferred
                        Custom GPT via opt-in tunnel            ← existing path
                        "one analyst, every platform"
```

Design rules carried over — now promoted to platform covenants:

- **Modules are quarantine zones.** All platform-specific fragility lives inside
  one module; a Substack endpoint change can never break the Medium view. Raw
  dump on parse failure stays mandatory — it's what makes the churn treadmill
  survivable (a broken module is a one-file patch informed by the dump).
- **The store is append-only, forever.** No refresh overwrites history. The
  per-item history log (the Medium lesson: *start logging before you need it*)
  is on by default for every module from day one.
- **The API reads caches only.** No API request ever triggers a platform call —
  the exposure surface stays read-only and rate-inert, same as today.
- **Auth strategies are a closed set of three:** WebView2 session capture, OAuth
  localhost-loopback, API-key paste. Every candidate platform must fit one or it
  doesn't get a module.
- **Scheduler with per-platform politeness.** Auto-refresh (the v2 idea from
  APP_IDEA.md) becomes per-module policy: session platforms get slow human-ish
  cadences (e.g. 1–2×/day, jittered, only while the app runs); official-API
  platforms can poll more freely within their quotas.
- **MCP-first for the analyst.** Localhost MCP needs no tunnel, so the flagship
  AI story finally matches the headline promise with zero caveats; the Custom GPT
  tunnel path remains the documented opt-in departure it already is.

### The `/v1` contract is load-bearing — freeze it, grow `/v2`

Preserving the ApiServer is a hard requirement, not a nice-to-have: a working
Custom GPT is already configured against its imported schema (`openapi.json`),
bearer key, and tunnel hostname. The evolution rule has two halves:

- **`/v1` never changes shape again.** It is, retroactively, *the Medium API
  contract*: same routes, same response shapes, same `?account=`, same key, same
  port. Internally its handlers become thin adapters over the Medium module's
  providers, but the existing GPT — schema import, tunnel config, instructions —
  is untouched through every milestone. And it stays forever: it's the free-tier
  GPT's surface, and the cheapest regression test the project has.
- **`/v2` is the black-box surface**, designed around one keystone rule:

> **Add platforms without adding operations.** A new platform must arrive as new
> *data* (an entry in `/v2/platforms`), never as new *schema*.

Why that rule is load-bearing: Custom GPT Actions punish endpoint multiplication.
As of writing, one action = one domain (so a single tunnel hostname means a single
schema anyway), there are practical ceilings on operation count and schema size,
tool-selection quality degrades as similar operations multiply — and every schema
change forces a manual re-import in the GPT editor. Platform-as-parameter makes
the GPT configuration **write-once**: enable Substack in Settings and the *same*
GPT can query it immediately, because `getPlatforms` now says so. No re-import,
no new operations, no user ceremony.

The entire `/v2` surface in ~11 operations, constant in the number of platforms:

| Layer | Route | One operation serves |
|---|---|---|
| Discovery | `GET /v2/platforms` | enabled platforms + accounts, capability map, metric glossary, capture freshness/coverage |
| Rollups | `GET /v2/summary` | whole-business audience + revenue snapshot |
| | `GET /v2/revenue/daily` | the unified P&L series, per source + total |
| | `GET /v2/audience/history` | the balance-sheet series across platforms |
| | `GET /v2/items/top` | cross-platform top content (normalized metrics) |
| Per-platform | `GET /v2/{platform}/summary` | any platform's account summary |
| | `GET /v2/{platform}/history` | any platform's account time series |
| | `GET /v2/{platform}/items` | list / filter / sort (today's `/stories`, generalized) |
| | `GET /v2/{platform}/items/{id}` | item detail incl. `platformExtras` |
| | `GET /v2/{platform}/items/{id}/{facet}` | facet ∈ `history · daily · referrers · conversions · content` — capability-gated |
| | `GET /v2/{platform}/reports/{kind}` | kind ∈ `tags · months · years · …` — capability-gated |

Mechanics that make the constant-schema trick honest:

- **Capability-gated, self-describing errors.** Gumroad has no referrers; asking
  returns `{ "error": "capability_not_supported", "platform": "gumroad",
  "hint": "see /v2/platforms" }` — the analyst self-corrects instead of
  hallucinating. The existing `detail_not_cached` pattern carries over unchanged
  for cache-backed facets.
- **Modules never register routes.** A module implements capability provider
  interfaces (`IItemsProvider`, `IItemHistoryProvider`, `IReferrersProvider`,
  `IRevenueProvider`, …); the single `/v2` router discovers them through the
  registry. Modularity lives in the module layer; stability lives in the route
  layer. The read-only / cache-backed / never-calls-the-platform covenants apply
  to `/v2` verbatim.
- **One bearer key gates `/v1` + `/v2`** for now; scoped keys are open question #7.

### One analyst, not N

The direct answer to "modular per-platform GPTs, or all-in-one?": **one GPT** —
because the cross-platform questions (§6: the P&L, echo detection, effort ROI)
are the product, and N single-platform GPTs are structurally unable to ask them.

- **"Black Box Analyst"** — one new Custom GPT on the `/v2` schema. Instructions
  pattern (extends `docs/GPt_INSTRUCTIONS.md`): *call `getPlatforms` first each
  session; consult the capability map before per-platform calls; use the metric
  glossary to caveat cross-platform comparability.*
- **The existing "Medium Metrics Analyst" survives unmodified on `/v1`** as the
  free-tier GPT — and doubles as the living proof that milestones didn't break
  the contract.
- Per-platform GPTs exist only ever as *marketing teasers* (a free "Substack
  Analyst"), never as architecture — each one multiplies instructions, key
  handling, and user setup while subtracting capability.
- **MCP dissolves the problem entirely** for Pro users: an MCP server lists its
  tools live, so newly enabled platforms surface automatically — no frozen
  schema, no re-import, no tunnel (pure localhost). That's why M3 is MCP-first,
  with the Custom GPT remaining the documented opt-in tunnel path per
  [OPENAI_CUSTOM_GPT.md](OPENAI_CUSTOM_GPT.md).

---

## 5. The unified data model

The hard design problem. Platforms disagree about everything ("view" ≠ "impression"
≠ "open"), and over-normalizing destroys exactly the platform-specific richness
that makes local capture valuable. The rule:

> **Normalize the 20% the analyst needs for cross-platform questions;
> archive 100% raw for the archaeologist.**

```jsonc
// UnifiedSnapshot — one per capture, per account
{
  "timestamp": "2026-07-05T09:00:00Z",
  "platform": "substack",
  "accountId": "acct_abc",
  "audience": {                    // the "balance sheet" block
    "followers": 4210,             // platform's primary audience number
    "paidSubscribers": 182,        // null where N/A
    "emailSubscribers": 4210
  },
  "revenue": {                     // the "income statement" block
    "periodToDate": 912.40, "currency": "USD",
    "kind": "subscriptions"        // subscriptions | partner_program | sales | tips | ads
  },
  "items": [{                      // posts / videos / products / episodes
    "itemId": "p_123", "kind": "post",
    "title": "…", "url": "…", "publishedAt": "…",
    "metrics": {                   // ONLY the cross-platform core:
      "reach":  8100,              //   views/impressions/opens → reach
      "engaged": 3900,             //   reads/watch-past-30s/clicks → engaged
      "engagementRatio": 0.48,     //   engaged / reach, computed uniformly
      "reactions": 88,             //   claps/likes/hearts
      "comments": 12,
      "revenue": 31.20,            //   null where platform doesn't attribute
      "audienceGained": 14         //   followers/subs attributed to this item
    },
    "platformExtras": { "…": "…" } // untranslated native fields, per platform
  }],
  "raw": "raw/2026-07-05T0900.json" // pointer to the untouched native payload
}
```

Notes:

- **`reach`/`engaged` are deliberately coarse.** They exist so the analyst can
  answer "what worked across my whole business this month" without lying about
  comparability; per-platform views keep their native names inside
  `platformExtras` and the per-platform API routes. Document the mapping per
  module — the analyst's instructions should show the mapping table so it can
  caveat honestly (the GPT_INSTRUCTIONS pattern already does this).
- **Store stays file-based** (CSV + JSON per account, mirroring today) until it
  measurably hurts; if cross-platform queries get slow, add a **derived SQLite
  index built from the files** — the files remain the source of truth, the index
  is disposable. Never invert that hierarchy: files are the flight recorder.
- **Money is the one field worth obsessing over.** Currencies, gross-vs-net
  (Gumroad fees, Substack's cut, Stripe fees), payout-vs-accrual (the Payout
  History tab already models this for Medium) — resolve per module, and record
  which convention each field uses in the payload itself.

---

## 6. Features only this product can have

Each falls out of *unified + historical + local* — the three properties no
competitor combines:

1. **The Creator P&L.** One ledger: Medium Partner + Substack subs + Gumroad sales
   + Ko-fi tips + YouTube ads, dailyized (the earnings-delta trick, applied per
   source), with a monthly close. *"What did my whole business earn in June, and
   from where?"* — currently answerable only by hand-spreadsheet. This alone
   justifies the product for anyone earning on 2+ platforms.
2. **The audience balance sheet.** Followers/subscribers per platform, over time,
   on one chart — the creator's *asset* view. Platforms show their own slice;
   nobody shows the whole portfolio growing (or quietly rotting on the platform
   they stopped posting to).
3. **Cross-platform echo detection.** Timestamps of a post on platform A vs.
   audience/traffic bumps on platform B — *"my X threads don't move Substack
   signups; my Medium essays do."* Deterministic correlation over local history;
   the analyst narrates it. No OAuth-bound tool has both series to correlate.
4. **Cross-post radar.** "Your top-decile Medium essays that never went to
   Substack" — tag/title similarity (the existing Jaccard trick) across platforms.
   Turns dead inventory into scheduled revenue.
5. **Effort ROI per platform.** Posts-per-month vs. revenue-and-audience-gained
   per platform: *"LinkedIn costs you 9 posts/mo for 2% of revenue."* The
   quit-a-platform permission slip — creators crave this and no platform will
   ever build it (their answer is always "post more").
6. **Platform-death insurance.** One-click full export; the pitch writes itself
   every time a platform folds or purges data. The black box is the only tool
   whose value *goes up* when a platform dies.
7. **The analyst, now with the whole picture.** The existing recipes
   ("which tags earn most per story?") upgrade to business-level questions:
   *"Where should next month's writing time go?"* — answered from the user's own
   cross-platform history, locally. That's a sentence no cloud competitor can say.

---

## 7. Competition & positioning

| Who | What they are | Why they can't follow |
|---|---|---|
| Social schedulers w/ analytics (Metricool, Buffer, Hootsuite) | Cloud, official-API-bound, social-first | Blind to Medium/Substack earnings; can't touch session-gated data; credential custodians by design |
| Creator "media kit" tools (Favikon, Social Blade) | Public-data profiles for brand deals | Public data only — no earnings, no reads, no opens |
| Newsletter analytics (platform-native, or Substack's own) | Single-platform, current-window | No cross-platform, no permanent history, platform-owned |
| Creator-economy dashboards (various, incl. dead ones: Stir et al.) | Cloud aggregation of payouts | Same OAuth ceiling; and being cloud, each is one breach from dead |
| **Spreadsheets** | The real incumbent | Manual, decaying, no per-post series — but proves the job-to-be-done exists |

**Positioning statement:** *For creators earning on two or more platforms who
don't trust another dashboard with their logins, the Black Box is a local flight
recorder that keeps the complete, permanent history of their audience and earnings
across every platform — unlike cloud analytics tools, it sees everything their own
browser sees and nothing ever leaves their machine.*

**Who it's for, concretely:** the Medium-writer-with-a-Substack (beachhead — both
modules exclusive); the portfolio writer (Medium + Substack + Gumroad course);
later the YouTuber-who-writes. **Who it's not for:** TikTok/IG-first influencers
(hostile platforms, different jobs), teams wanting collaborative cloud dashboards.

---

## 8. Business model

Carries over the HOW_TO doc's structure — the black box changes the ceiling, not
the philosophy:

- **Free:** one platform (Medium — the existing app becomes the free tier / lead
  magnet; its users' history imports losslessly, carrying their lock-in with
  them). This keeps the Medium Metrics funnel and community intact.
- **Black Box Pro (~$59–79/yr or ~$99 one-time + paid major upgrades):** all
  platform modules, cross-platform reports, the P&L, MCP/GPT analyst pack,
  scheduler, export. Annual pricing fits a maintenance-treadmill product better
  than one-time — frame it honestly as *"the subscription pays for keeping the
  modules alive."*
- **Agency (~$25/mo/seat):** multi-client workspaces (the multi-account registry
  generalized), client-ready cross-platform PDF reports, white-label headers.
- **Open-core split, refined for the treadmill:** keep **capture modules open
  source** even in the paid product — users can patch a broken parser the day a
  platform changes, PRs become free maintenance, and openness *is* the trust
  story ("audit exactly what we capture"). The paid layer is the cross-platform
  engine, analyst pack, scheduler, and support. What forks can't take: the brand,
  the audience, the module-fix velocity, and the users' own accrued histories.
- **Benchmarks (later):** the opt-in anonymized aggregates idea (Option F) gets
  strictly better with N platforms — *"State of the Creator Economy, from real
  ledgers"* — but it waits for scale and an airtight privacy design.

Order-of-magnitude ceiling: 300 Pro subscribers × $70/yr ≈ $1.75k/mo, plus
agencies; a realistic $5–15k/mo business at a few thousand users — genuinely
startup-shaped upside for a solo builder, without needing VC-scale numbers.

---

## 9. Roadmap with stop-gates

Preconditions (from the HOW_TO doc): **do not start until Phases 1–2 there prove
willingness to pay** (~25 Pro sales + sane churn on the Medium product). The black
box is a bet you fund with evidence, not enthusiasm.

**M0 — Extract the kernel (2–4 wk).** Pull the platform-agnostic core out of the
Medium app: `IPlatformModule`, two-level store layout, UnifiedSnapshot, module
registry in Settings. Medium becomes module #1 *with zero user-visible change* —
existing data migrates in place. Exit test: the Medium app is bit-for-bit as
useful as before, now hosted by a shell that could hold a second module — and
**the existing Custom GPT still answers against the untouched `/v1` API with zero
configuration changes** (no schema re-import, no new key, same tunnel).

**M1 — Substack module (3–6 wk incl. reverse-engineering).** The real validation:
a second *session-capture* platform proves the pattern where it's hardest. Ship
per-post history logging from day one (can't backfill!). Exit test: 20 real users
running both modules; the module took ≤ half the effort Medium took (the kernel is
paying rent).

**M2 — The money ledger (2–3 wk).** Gumroad + Ko-fi + Patreon via API-key/OAuth —
cheap official-API modules whose entire point is making the **unified P&L** real.
This is the demo that sells Pro: *one chart, all income.* Launch Black Box Pro
here; rebrand decision point (Medium Metrics → the new name, §12).

**M3 — Analyst over everything (2–3 wk).** MCP server over the unified API + the
**Black Box Analyst** Custom GPT on the write-once `/v2` schema (§4) +
cross-platform recipes + the echo/ROI/cross-post-radar reports. The marketing
moment: *"I asked one AI where my whole creator business makes its money — and
nothing left my PC."*

**M4 — Breadth by demand.** YouTube (official, safe, big TAM) vs. beehiiv/Ghost
(newsletter depth) vs. X/LinkedIn (exclusive but fragile, ship behind a
"best-effort" flag) — let paying users vote. Cap live modules at what the
maintenance budget honestly sustains (§10).

Each milestone gates the next: if M1's Substack module doesn't excite the existing
Medium user base, stop — the multi-platform thesis is wrong, and you still own the
best Medium tool (fallback intact).

---

## 10. The honest wall (risks)

| Risk | Severity | Mitigation |
|---|---|---|
| **N platforms × endpoint churn = permanent treadmill.** The defining cost. Each session module realistically needs ~2–6 hrs/mo average, spiking on redesigns | Certain | Module quarantine + raw-dump debugging (proven); **open-source modules** for community fixes; annual pricing funds it; a hard cap on module count; per-module health status shown in-app so breakage is a known state, not a mystery |
| **ToS spectrum.** Own-account, human-rate, read-only capture is defensible in spirit — but X/LinkedIn are hostile in practice, and a *paid product* invites more attention than a personal tool | Medium, uneven | Politeness policies per module; session platforms default to manual/slow refresh; X/LinkedIn opt-in and clearly best-effort; never automate actions (read-only forever); the product never touches accounts the user doesn't hold credentials for |
| **Windows-only vs. a Mac-heavy creator world.** The single biggest TAM ceiling — likely a majority of the target audience is on macOS | High, slow-burning | Validate on Windows (M0–M3); decide the port with revenue in hand. Options, in rising cost: browser-extension capture agent feeding the same store (cross-OS, weaker scheduling) → Tauri/Avalonia rewrite of shell+capture. Keeping modules thin keeps the port bill honest |
| **Normalization lies.** A "reach" number that quietly equates a YouTube impression with a Substack open destroys trust with exactly the numerate users this attracts | Medium | The 20/80 rule (§5); mapping tables surfaced in the analyst's instructions; native names everywhere in per-platform views |
| **Scope obesity / jack-of-all-trades.** Every creator wants *their* platform next; each module added multiplies the treadmill | High (self-inflicted) | The three-auth-strategies rule as a hard filter; demand-gated additions; public roadmap with a "we deliberately don't do TikTok/IG" line |
| **A platform adds native history + API and erases a module's edge** | Low–medium per platform | The unified ledger, permanence, and privacy remain; official-API modules show the product survives fine without exclusivity |
| **Solo-maintainer bus factor on a product sold as *permanent*** | Real | Append-only open formats (CSV/JSON) mean user data outlives the vendor by design — say so out loud; it's the one honest answer and it doubles as the trust pitch |

---

## 11. What NOT to build

- **No cloud capture, ever, for session platforms.** The moment your servers hold
  one user's Substack cookie, every structural advantage in §1 inverts into
  liability. (Derived-metrics sync for a mobile dashboard — Option C — remains
  fine; *capture* is what must stay local.)
- **No posting, scheduling, or engagement automation.** Read-only is the ToS
  posture, the security story, and the scope fence, all at once. The moment the
  box can *act*, it can be blamed.
- **No engagement-bait analytics** (follow-churn tracking, unfollower alerts) —
  category-poison that attracts the wrong users and the platforms' wrath.
- **No TikTok/Instagram modules** — hostile capture surfaces, different audience,
  bottomless maintenance. Write the refusal into the public roadmap; it reads as
  discipline, not weakness.
- **No inventing comparability.** Where platforms' numbers don't map, show both
  and say they don't map. The audience for this product can smell a fudged metric.

---

## 12. Naming

"Medium Metrics" can't carry a multi-platform product (and arguably shouldn't —
the free Medium app keeps its name and equity as the funnel). Directions, with the
usual *check trademarks/domains before falling in love* caveat:

- **Flight-recorder family:** Black Box, Flight Recorder — on-theme, memorable;
  "black box" is crowded as a trademark space, so likely needs a modifier
  (e.g. *Creator Black Box*).
- **Ledger family:** speaks to the P&L feature — e.g. *Ledgerline*, *Byline
  Ledger*.
- **Vault/keep family:** speaks to permanence + privacy — e.g. *Statkeeper*,
  *Creator Vault*.

Whatever the name: the tagline is already written and shouldn't be lost —
**"Your logins never leave your machine."**

---

## 13. Open questions

1. **Store evolution:** at what N (platforms × accounts × items × days) do flat
   files actually hurt, and is the derived-SQLite-index enough — or does the
   per-item history want SQLite from M0 while account history stays CSV?
2. **Scheduler defaults:** is background auto-capture on session platforms worth
   the ToS-surface increase at all, or does the product stay proudly
   manual-refresh (Medium's current stance) with auto-capture only for
   official-API modules?
3. **Extension-as-capture-agent:** could a browser extension *feed the same local
   store* and become both the Mac answer and the low-friction funnel — and does
   its weaker scheduling actually matter for a manual-refresh product?
4. **Multi-currency P&L:** normalize to a home currency at capture time (rate
   stamped in the row, append-only-friendly) or at report time (needs a rate
   source — an external dependency the app currently doesn't have)?
5. **How open is open-core here:** modules open + engine closed is the working
   assumption — but is there a world where *everything* is open and the paid
   product is binaries + module-fix SLA + analyst pack, Obsidian-style?
6. **The rebrand moment:** M2 is the guess — is it right to rebrand before
   YouTube-scale breadth exists, or does "creator" in the name overpromise at
   three writing platforms + two tip jars?
7. **API keys:** one bearer key for everything (today's model), or scoped keys —
   per platform, per client (Custom GPT vs. MCP vs. a future mobile view),
   independently revocable? Scopes get attractive the day teaser GPTs or agency
   client reports exist; until then one key is honest simplicity.

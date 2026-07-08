# How to make money from this

> An honest strategy doc: every realistic way to monetize Medium Metrics — the app,
> the architecture, and the story around it — with effort, revenue ceilings, and the
> risks that kill each option. Written against the state of the repo as of 2026-07
> (multi-account, earnings + payout history, reports dashboard, local read-only API,
> Custom GPT via tunnel, strategy endpoints, Apache-2.0).

## TL;DR

- This is a **niche product for a passionate, money-motivated audience** (Medium
  Partner Program writers). Realistic outcome: a **$500–$5k/month side business**,
  not a venture-scale company. That's a feature — it matches the effort you can give it.
- Your **strongest strategic asset is already in NEW_APIS_HOWTO.md**: *history can't
  be backfilled*. Every day a user runs the free app, their dataset becomes more
  valuable and more locked-in. Give the recorder away; **sell the analysis of data
  only they hold.**
- Your **second asset is the constraint itself**: the unofficial-API / ToS problem
  makes a classic hosted SaaS (users hand you their Medium cookies) the obvious —
  and wrong — move. The local-first architecture isn't a limitation to work around;
  it's the **moat and the pitch**: *"we never see your credentials."*
- Recommended path, in order: **(1)** open-core Pro desktop build with license keys →
  **(2)** AI analyst productization (Custom GPT / MCP server) as the Pro killer feature →
  **(3)** optional cloud sync of *derived metrics only* for a subscription dashboard →
  **(4)** generalize to a multi-platform "creator stats black box" if (1)–(3) prove demand.

---

## 1. What you actually have (asset inventory)

More than "a stats app." Each of these is separately monetizable:

| Asset | Where it lives | Why it's worth money |
|---|---|---|
| Working Windows app: snapshot + append-only history, multi-account, reports (tag/month/year), earnings chart, payout history | `src/` | The product itself |
| **Irreplaceable per-user history** | `report.csv`, `stories-history` | Medium only shows *current* numbers; your users' logs literally cannot be recreated. Lock-in that grows daily |
| Local read-only API + bearer auth + OpenAPI schema | `ApiServer`, `openapi.json` | Plug into *any* AI tool — Custom GPT today, MCP/Claude tomorrow |
| The "app = data layer, GPT = analyst" pattern + working tunnel setup (Cloudflare, Tailscale Funnel) | `OPENAI_CUSTOM_GPT.md`, `NEW_APIS_HOWTO.md` | A packaged "AI copilot fed by your own data" story few competitors can tell |
| Writing-strategy endpoints: per-story history/daily deltas, referrers, conversions, similar, title patterns, title-change history, content capture | NEW_APIS phase 2 | Features Medium itself doesn't offer — "still has legs vs. one-day spike", "did the retitle lift read-ratio?" |
| Reverse-engineering knowledge of Medium's internal endpoints | `docs/private/medium-api-notes.md` | Scarce, perishable expertise — also a content/consulting asset |
| 11-post build-in-public series, drafted | `docs/private/reddit/` | A distribution engine you've already written |
| Apache-2.0 codebase | `LICENSE` | Open-core friendly; also means anyone can fork — see §7 |

---

## 2. The constraint that shapes every option

The app authenticates with **the user's own session against unofficial Medium
endpoints**. That single fact decides what's viable:

- **You cannot build the "obvious" SaaS** — a hosted service where users log in
  with Medium and your servers poll their stats. That means holding thousands of
  users' session cookies server-side (a breach liability), automating accounts at
  scale (a ToS enforcement magnet), and a single point Medium can kill with one
  endpoint change or one legal email.
- **Everything that keeps capture on the user's machine, with the user's own
  credentials, at human refresh rates, stays in the same ethical/legal lane the
  app is in today.** One person, own account, manual refresh — that's record-keeping,
  not scraping.
- Corollary: **your competitors face the same wall.** Anyone funded enough to build
  a polished hosted Medium-analytics SaaS is one ToS letter from death. The
  local-first design is the durable position, not the compromise.

Rule of thumb for every idea below: **credentials and capture stay local; only
derived numbers may ever leave, and only opt-in.**

---

## 3. The options

### Option A — Open-core Pro desktop app (license key) · **do this first**

**What:** The current app stays free and open source (it's your marketing and your
funnel). Sell a **Pro build** with a license key for the high-leverage features:

- Free: sign-in, refresh, snapshot list, `report.csv` history, basic chart.
- **Pro ($29–$49 one-time, or $39/yr):** multi-account, reports dashboard
  (tags/months/years), earnings + payout history, auto-refresh scheduling,
  Excel export, the local API + AI-analyst pack, strategy endpoints.

**Why it fits:** Zero infrastructure, zero credential liability, ships from what's
already built. License keys via **Lemon Squeezy / Paddle** (they handle VAT) or
Gumroad; distribution via the repo, winget, and optionally Microsoft Store.
The free tier quietly accrues the un-backfillable history — by the time a user
wants the Pro analysis, *only your app has their data*. That's the conversion engine.

**Effort:** Low — a key check + feature gating, a checkout page.
**Ceiling:** Modest. At $39, 25 sales/month ≈ $1k/month. Niche but plausible with
the Reddit series + Medium-on-Medium content driving traffic.
**Risks:** Apache-2.0 means someone can fork and un-gate Pro. In practice, for a
niche utility, nobody bothers — but if it worries you, keep Pro features in a
closed-source companion assembly rather than `#ifdef`s in the public repo.

### Option B — The AI analyst as the product (Custom GPT / MCP) · **the differentiator**

**What:** Nobody else sells *"an AI strategist that has read your entire Medium
history."* Package what already exists:

1. A polished **"Medium Metrics Analyst"** Custom GPT (instructions are already in
   `docs/GPt_INSTRUCTIONS.md` / `GPT_RECIPES.md`) + one-click tunnel setup in the app.
2. An **MCP server mode** for the local API so Claude Desktop / other MCP clients can
   use it *without any tunnel* — pure localhost, which is both easier and more
   on-brand ("your data never leaves your PC" survives intact). This is likely a
   thin adapter over the existing endpoints.
3. Sell it as **the** Pro feature of Option A, not separately. "Ask your stats
   anything: which tags earn most per story, which posts still have legs, did the
   retitle work."

**Why it fits:** The API, schema, auth, docs, and recipes all exist. The GPT Store
itself pays ~nothing — the money is in using the AI angle to justify the Pro price
and dominate the "Medium + AI" search/content niche.
**Effort:** Low–medium (MCP adapter, onboarding polish).
**Ceiling:** It doesn't add its own revenue line; it multiplies Option A's
conversion rate and price point.
**Risks:** OpenAI/MCP platform churn — mitigated by the API being a neutral HTTP
surface underneath.

### Option C — Hybrid cloud: local capture, cloud dashboard (subscription)

**What:** Keep capture 100% local (the desktop app remains the collector), add an
**opt-in sync of derived metrics only** — never cookies, never raw sessions — to a
hosted account, giving users:

- A web dashboard (check stats from your phone; the #1 thing a desktop app can't do).
- Off-site backup of the irreplaceable history.
- Cross-device / cross-account rollups; email digests ("your week on Medium").

**Why it fits:** This is the only SaaS shape that survives §2. The pitch is unique:
*"the cloud only ever sees numbers; your login never leaves your PC."* Subscriptions
($4–$8/month) fit because you now carry real hosting costs and deliver ongoing value.
**Effort:** Medium–high. Auth, sync protocol, a web frontend, ops. Your first real
infrastructure and support burden.
**Ceiling:** The highest of the "product" options — recurring revenue, and digests/
alerts create retention. 200 subscribers × $6 ≈ $1.2k/month recurring.
**Risks:** You now have servers, uptime, GDPR-ish obligations, and churn. Build only
after Option A proves people pay at all.

### Option D — Browser extension variant (bigger funnel)

**What:** A companion extension that captures stats from the Medium tab the user
already has open, feeding the same local store (or the Option C cloud). Kills the
two biggest adoption limits at once: **Windows-only** and **install friction**.

**Why it fits:** Precedent exists (Medium Enhanced Stats has a large user base and is
free — which also tells you the extension itself can't charge much; it's a *funnel*
for A/B/C, not the product).
**Effort:** Medium — new codebase, but the domain logic and parsing knowledge port.
**Risks:** Chrome Web Store review + Medium DOM churn; still same-account,
user-initiated capture, so ToS posture unchanged.

### Option E — Agency / ghostwriter tier (B2B niche)

**What:** You already built multi-account. Agencies, VAs, and ghostwriting shops
managing several Medium accounts have real willingness to pay: per-account
dashboards, client-ready PDF/Excel reports, white-label export headers.
**Price:** $15–$29/month per seat — B2B tolerates 5–10× consumer pricing.
**Effort:** Low on top of Option A (report polish, branding options).
**Ceiling:** Ten agency customers can equal hundreds of consumer sales.
**Risks:** Managing *client* accounts is the user's ToS exposure, not yours (the
tool still runs locally with sessions they legitimately hold), but position it
carefully — "for accounts you manage with permission."

### Option F — Opt-in anonymized benchmarks (data product)

**What:** With explicit opt-in, users contribute anonymized aggregates (tag, views,
read-ratio, earnings buckets — never titles/IDs). You publish **"State of Medium
Earnings"**: median $/story by tag, read-ratio norms, boost impact stats. Free
summary as marketing; full report paid ($19) or bundled with Pro; a live
benchmarks page ("your read ratio is in the 78th percentile") as a Pro/cloud feature.

**Why it fits:** Writers are *starved* for real earnings data — earnings-report
posts are already a top-performing genre on Medium itself. Nobody has a clean
dataset; you'd be the only one.
**Effort:** Medium, and needs a user base first (cold-start: seed with your own
data + volunteers from the Reddit/Medium audience).
**Risks:** Privacy design must be airtight or it poisons the local-first brand.
Small-N data is embarrassing — wait until ~100+ contributors.

### Option G — The audience play: build in public, monetize the story

**What:** You've already drafted an 11-post series. The meta-move: **write about
Medium, on Medium, using the app** — earnings transparency posts, "what my data
says about titles", "I measured whether retitling works" (title-history + daily
deltas make you the only person who can write that post with receipts). Each post
is Partner Program revenue *and* app marketing *and* benchmark-contributor
recruiting. Extend to a newsletter and, if it compounds, a paid course/playbook
("Data-driven Medium writing").

**Why it fits:** Zero code. The audience for the app *lives on the platform the
content is published to*. The dev-story series (reverse-engineering, tunnel saga,
data-layer-vs-LLM design) separately targets the developer audience on Reddit —
two funnels, one repo.
**Ceiling:** Content usually out-earns niche apps: established Medium writers in
this genre clear $500–$2k/month, and it raises every other option's ceiling.
**Risks:** It's a treadmill; earnings posts invite scrutiny — the app's charts are
your receipts.

### Option H — The bigger, unique play: the multi-platform "creator stats black box"

> Elaborated in full — thesis, platform survey, architecture, roadmap, risks — in
> **[CREATOR_STATS_BLACK_BOX.md](CREATOR_STATS_BLACK_BOX.md)**.

**What:** The architecture — *local capture of session-gated creator stats +
append-only history + local API + AI analyst* — is not Medium-specific. Substack,
LinkedIn, X, YouTube Studio, Ko-fi, Gumroad all show creators *current* numbers
with weak or no history, all gate stats behind a login, and all have creators who
distrust cloud analytics tools with their credentials. The product becomes:

> **"The flight recorder for your creator business. Every platform, one local,
> private, append-only history — with an AI analyst on top. Your logins never
> leave your machine."**

Per-platform capture modules (each isolated like `MediumStatsClient` — the one-file
patch design already anticipates this), one store, one API, one analyst.

**Why it's the "entirely unique" option:** Cross-platform creator analytics
startups exist, but they're all cloud-first and OAuth-limited (they can only see
what official APIs expose — which for most platforms excludes the good stuff).
Local-first capture sees *everything the creator's own browser sees*. Nobody
credible occupies "privacy-first creator analytics."
**Effort:** High. Each platform is a new reverse-engineering treadmill.
**Ceiling:** This is the only option with startup-shaped upside ($10k+/month), and
it de-risks the Medium-dies scenario.
**Risks:** N platforms × endpoint churn = permanent maintenance. Do it only after
A–C prove the willingness-to-pay, and add platforms one at a time (Substack first —
closest audience overlap, most earnings-obsessed users).

### Option I — Long shots and exits (know they exist, don't chase them)

- **GitHub Sponsors / sponsorware:** low ceiling ($50–$300/month) but zero effort —
  turn it on regardless; sponsorware ("next feature unlocks at N sponsors") pairs
  well with build-in-public.
- **Micro-acquisition:** a working niche tool + audience + revenue history sells for
  ~2–4× annual profit on Acquire.com and similar. Options A–C build the asset;
  this is the exit, not the strategy.
- **Selling to Medium:** effectively zero probability (you exist because they
  don't build this; acqui-hire-shaped at best). Don't design for it.
- **Consulting/custom builds:** "I build local-first AI-connected data tools" is a
  credible consulting card the repo proves. Real money, but it's a job, not a product.

---

## 4. Feature ideas that exist only here — and convert

Each is cheap, ships from existing data, and is a headline nobody else can copy:

1. **Title A/B coach.** Title-change history × daily deltas already answer "did the
   retitle lift views/read-ratio/earnings?" Make it a one-click before/after card.
   *The* screenshot feature — and the Pro upsell in every content post.
2. **Shareable earnings/milestone cards.** Writers already screenshot earnings by
   hand. Generate clean, optionally-redacted cards ("$412 — best month yet", "1M
   lifetime views") with a subtle "made with Medium Metrics" mark. Every share is
   an ad; the redaction option earns trust.
3. **"Legs" alerts.** Desktop notification when an old story's daily deltas wake up
   ("*Ukraine essay* is moving again — 340 views this week from Google"). Uses the
   per-story history + referrers cache; makes daily refresh a habit, and habit is
   retention.
4. **Annual wrap ("Your Year in Writing").** By-year reports + earnings history →
   a December shareable. Predictable seasonal signup spike, contributes to Option F.

---

## 5. Comparison

| Option | Effort | Revenue ceiling | ToS/credential risk | Reuses what exists |
|---|---|---|---|---|
| A. Pro desktop (license key) | Low | $0.5–2k/mo | None new | ~90% |
| B. AI analyst / MCP pack | Low–med | (multiplies A) | None new (MCP is localhost) | ~80% |
| C. Cloud dashboard (derived-only sync) | Med–high | $1–5k/mo recurring | Low (no credentials in cloud) | ~50% |
| D. Browser extension funnel | Medium | (funnel for A–C) | Low | ~40% |
| E. Agency tier | Low (on A) | $0.5–3k/mo | Low, position carefully | ~85% |
| F. Benchmarks data product | Medium | $0.2–1k/mo + marketing halo | Privacy-design critical | ~60% |
| G. Content/audience | Zero code | $0.5–2k/mo + lifts all | None | 100% (it's the story) |
| H. Multi-platform black box | High | $10k+/mo possible | Same model, × N platforms | Architecture, not code |
| I. Sponsors / exit | Trivial | Small / one-time | None | — |

---

## 6. Recommended sequence

**Phase 0 — now, free:** Ship the Reddit series; publish the repo; start the
Medium-on-Medium content loop (Option G). Turn on GitHub Sponsors. Ship the
per-story history logging everywhere immediately — *it's the asset that can't be
backfilled, for you and every user.*

**Phase 1 — first dollar (1–2 months):** Option A Pro build + Lemon Squeezy/Paddle
checkout, with Option B (Custom GPT + MCP mode) as the flagship Pro feature and the
title-A/B card as the demo. Success test: **25 paying users**. If nobody pays $39
for this, no version of the SaaS was going to work either — you'll have learned it
for the cost of a checkout page.

**Phase 2 — recurring (3–6 months, only if Phase 1 clears):** Option C cloud
dashboard (derived-metrics sync, mobile view, weekly digest) at $5–7/month;
grandfather Pro buyers. Add the agency tier (E) — it's mostly packaging.

**Phase 3 — the bet (6–12 months, only if churn is sane):** Either double down on
the Medium niche (benchmarks, Option F, own the category) **or** open the second
platform module (Substack) and rebrand toward the creator black box (Option H).
Let Phase 2's data — who pays, who churns, what they ask for — pick the branch.

---

## 7. Risk register (the honest wall)

| Risk | Severity | Mitigation |
|---|---|---|
| **Medium changes internal endpoints** | Certain, recurring | Already designed for: isolated client, raw-dump-on-parse-failure. Pro users become the canary + the reason to fix fast |
| **Medium ToS enforcement** | Low at current shape (own account, manual, read-only) — rises with anything automated or hosted | Keep capture local & user-initiated; never hold user cookies server-side; rate-gentle defaults |
| **Medium the platform declines** | Real, slow | Option H is the hedge; content/audience (G) transfers to any platform |
| **Apache-2.0 fork un-gates Pro** | Possible, historically rare in niches this small | Closed companion assembly for Pro; the brand, the GPT, the cloud, and the audience don't fork |
| **OpenAI/GPT-store or MCP churn** | Moderate | The API is neutral HTTP + OpenAPI; adapters are thin |
| **You lose interest / time** | The classic side-project killer | Phase gates above are also *stop* gates: each phase pays for the next or you stop cleanly with a sellable asset (I) |

## 8. What NOT to do

- **No hosted credential SaaS.** Never ask users to hand a Medium session to your
  servers. It's the fastest path to revenue and the fastest path to a breach,
  a ToS letter, and a dead product. This is the one door that stays closed.
- **No scraping accounts the user doesn't own**, no growth hacks (auto-clap,
  follow-bots) — instant category-poison, and adjacent tools die by it.
- **No ads/telemetry in the free app.** "Your data never leaves your PC" is the
  brand; $30/month of ad revenue isn't worth burning it.
- **Don't skip Phase 1 to build the cloud.** The desktop Pro test costs a weekend
  and answers the only question that matters: *will this audience pay at all?*

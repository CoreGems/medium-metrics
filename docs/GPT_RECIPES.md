# Medium Metrics — recipes for the assistant

This is reference material for the assistant (a Custom GPT) that uses the Medium Metrics
read-only API. It explains how to answer common questions by **chaining the existing
endpoints** rather than expecting a dedicated endpoint for each one.

## The division of labor (read this first)

The API is a thin **retrieval / aggregation** layer over data the desktop app has already
cached locally. It gives you **facts you cannot otherwise see** — the user's real per-post
history, prose, referrers, conversions, title changes.

**Clustering, ranking judgment, critique, and generation are your job, not the API's.**

Before asking for a new endpoint, check whether `/tags`, `/stories`, `/search`, and
`/stories/{id}/content` already give you the raw material to reason from. They almost always do.

**Never answer a "what should I write / suggest titles / what topics" question from generic
priors.** You have the user's entire catalogue one request away — their real themes, their
top-performing posts, and their actual writing voice. Generic "Medium-style" advice
(productivity, career, "I Stopped Chasing X") is almost always wrong for this user and reads as
if you never looked. **Ground first, then generate.** If you haven't called the API in this
turn before proposing titles or topics, you are doing it wrong.

A high earner with a low read-ratio is usually a fluke, not a pattern — weigh `$`/story and
read-ratio together, and discard tiny-sample tags (e.g. fewer than ~4 stories) before drawing
conclusions. That filtering is judgment you apply; the API just hands you the rows.

## Recipes

### "How am I doing overall right now?"
`GET /summary` for current headline totals, AND `GET /history?limit=3` for the recent series.
Show the change since the previous refresh — deltas for followers, views, reads, and earnings —
not just the static totals, and label the interval (refresh timestamps are irregular, so say
"since your last refresh ~1 day ago" rather than implying a fixed cadence). The total answers
"where am I"; the delta answers "how am I doing." If history has only one row, note there's no
prior snapshot to compare against yet.

### "What themes does my audience reward / pay best?"
`GET /tags?sort=earningsPerStory&order=desc` (or `sort=readRatio`).
Discard tags with very few stories as noise before concluding. Report `$`/story and read-ratio
together. The naive top of the list is often single-story tags — say so rather than presenting
them as a trend.

### "Cluster my posts into semantic themes" (across tags, not just one tag)
`GET /search?q=<theme phrase>` — body-indexed; check the `coverage` field (`title,tags,body`
means article text is searchable). Group the ranked hits yourself by what they're actually
about, and read each hit's `readRatio`/earnings to judge which themes land. For depth on a
cluster, pull `GET /stories/{id}/content` on the top hits and compare structure and voice.
`/tags` cannot do cross-tag semantic clustering — you do it, from search results.

### "What did I publish around the time of post X? / series effects"
`GET /stories?sort=publishedAt&order=desc&limit=200`, then window ±N days around X's
`publishedAt` yourself. Cross-reference neighbors' `views`/`earnings` to spot a post that rode
momentum from an earlier one.

### "Why did this post work, and how do I write another like it?"
Combine:
- `GET /stories/{id}/content` — the actual prose (subtitle, body, word count, paywall flag).
- `GET /stories/{id}/daily` — per-day deltas: was it a slow burn or a one-day spike?
- `GET /stories/{id}/referrers` — where the readers came from.
- `GET /stories/{id}/conversions` — followers/subscribers gained + rate from reads.
- `GET /reports/patterns` — structural title buckets (question / number / colon / length) with
  average read-ratio and earnings.
- `GET /stories/{id}/similar` — deterministic tag + title-keyword neighbours, as a starting set.

Synthesize the explanation and the next-draft suggestions yourself. That is the analysis — it
is your job, not an endpoint's.

### "Suggest new blog titles / what should I write about next?"
**Do not generate from generic Medium tropes — ground in this user's actual catalogue first.**
1. `GET /stories?sort=views&order=desc&limit=15` and `GET /stories?sort=earnings&order=desc&limit=15`
   — the titles here ARE the voice to imitate; study their structure, tone, and recurring devices.
2. `GET /tags?sort=earningsPerStory&order=desc` (and `sort=readRatio`) — which themes the audience
   actually rewards. Stay in the lanes that earn / get read; don't propose topics they never write.
3. Watch for a views-vs-readRatio split: a post can pull traffic (high views) while readers bounce
   (low read-ratio). Favour the patterns that win on **read-ratio and earnings**, not raw views.
4. Optionally `GET /reports/patterns` for structural title forms (question / number / colon / length)
   that correlate with performance for this user.
Then propose titles that match the user's proven structure and themes — concrete, in their voice,
in a lane the data shows their audience rewards. If your suggestions could have been written for any
Medium user, you skipped steps 1–3.

### "Critique / improve this draft I'm writing"
There is **no draft-analysis endpoint, by design** (the API is read-only/GET-only, and the
critique is exactly what you do). Instead: lift key phrases from the draft into `GET /search`,
pull the closest existing posts' `content`, and compare the draft against what already worked.
Do the structural critique, likely tags/audience, and title ideas yourself.

## Endpoints you'll reach for most

| Question shape | Endpoint(s) |
|---|---|
| Headline status / "how am I doing" | `GET /summary` + `GET /history?limit=3` (show deltas) |
| Find posts by topic (title/tags/body) | `GET /search?q=…` |
| List / filter / sort posts | `GET /stories?sort=…&tag=…&search=…` |
| One post's prose | `GET /stories/{id}/content` |
| One post's growth over time | `GET /stories/{id}/history`, `…/daily` |
| Traffic sources / audience growth | `GET /stories/{id}/referrers`, `…/conversions` |
| Per-theme performance | `GET /tags?sort=…` |
| What to write next / title ideas | `GET /stories?sort=views`, `…?sort=earnings`, `GET /tags` (ground first!) |
| Title structure vs. performance | `GET /reports/patterns` |
| Time grouping | `GET /reports/by-year`, `…/by-month`, `…/earnings/daily` |

Cache-backed endpoints (`/content`, `/detail`, `/referrers`, `/conversions`) return `404` until
the user has opened that story in the app to capture it — treat a 404 as "not captured yet,"
not "no data exists," and suggest the user open/fetch it if they need it.

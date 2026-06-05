# Reddit post draft — "What reverse-engineering Medium's stats taught me"

> Third draft. Post AFTER it's working end-to-end (it is). Best fit: r/csharp,
> r/dotnet, r/programming, r/SideProject. More technical than 1_show_and_tell.md.

**Title options:**
- I built a local Windows app to log my Medium stats — here's everything that fought back
- Reverse-engineering Medium's (undocumented, Cloudflare-protected) stats, one 403 at a time
- A weekend "log my Medium stats" app turned into a tour of Cloudflare, GraphQL, and normalized JSON

---

## Body

I wanted a dead-simple thing: a local Windows app that records my Medium readership numbers over time, because Medium's own page only shows *current* values. No cloud, no SaaS — just a Refresh button that appends one timestamped line to a local CSV.

The UI was a couple of hours. Getting the *data* was the real project, because Medium has no public personal-stats API. Here's the gauntlet, in order:

**1. "Sign in" did nothing.**
I captured the session by watching for Medium's `sid`/`uid` cookies after an embedded-browser login. Turns out **Medium sets those cookies for anonymous visitors too**, so my code "captured" an empty session instantly and the login window closed itself. Fix: only capture once the webview actually lands on the authenticated `/me` stats page (logged-out users get bounced to `/m/signin`).

**2. HTTP 403, "Just a moment…".**
With a valid cookie, my `HttpClient` request still got a 403 carrying `Cf-Mitigated: challenge`. Medium sits behind **Cloudflare's managed challenge**, which ties clearance to the browser's JS/TLS fingerprint — you can't replay it from a bare HTTP client, no matter how many headers you spoof. Fix: do the requests **inside** a hidden WebView2 that already passed the challenge during login, via same-origin `fetch()`. The cookie + Cloudflare clearance just come along for free.

**3. The JSON endpoint was gone.**
The old `/me/stats?format=json` now returns the SPA's HTML, not JSON. The per-story numbers moved to Medium's **GraphQL** API (`UserLifetimeStoryStatsPostsQuery`). I found it by injecting a `fetch`/XHR interceptor into the page and dumping every GraphQL call it made, then replayed the query myself with pagination. (It exposes views/reads/impressions/earnings — but, interestingly, **not claps**, so I show impressions + earnings instead.)

**4. Followers were always zero.**
`/me?format=json` still works and has the data — but not where I assumed. Followers aren't at `payload.user.socialStats.…`; they're in a **normalized store** keyed by user id: `payload.references.SocialStats[userId].usersFollowedByCount`. Classic Apollo-style normalized cache leaking through the JSON.

**Where it landed:** C# / .NET 8, WPF, a hidden WebView2 as the "authenticated fetch engine," `System.Text.Json`, and a plain append-only CSV for history. Session cookie encrypted at rest with Windows DPAPI. All the Medium-specific fragility is isolated in one class, and it dumps the raw response on any parse failure so the next breakage is a 5-minute fix instead of an afternoon.

**The lesson I keep relearning:** when you're scraping an undocumented API, *don't guess* — capture the real traffic and build to what you actually see. Every one of these bugs came from assuming a shape instead of looking. The capture-and-dump tooling paid for itself three times over.

**Caveats:** undocumented endpoints can change tomorrow; it's read-only, single-account, manual-refresh, and I keep it gentle on their servers / ToS.

Happy to share code or the gnarly bits in the comments.

---

## Questions for the sub
1. For Cloudflare-challenged sites, is an embedded browser the only sane long-term option, or do people have lighter approaches that survive?
2. Any pattern you like for absorbing GraphQL schema drift beyond "isolate + dump raw"?
3. Worth open-sourcing, or too niche / too likely to break?

---

## Notes to self (not part of post)
- This is the honest, technical version — lead with it on r/csharp / r/programming.
- Do NOT paste exact internal endpoint URLs or the full GraphQL query verbatim; keep it high-level.
- Screenshot: the stats list + history log (blur real numbers if you like; 309 followers is fine to show).
- Decide open-source stance before posting — Q3 will get asked immediately.

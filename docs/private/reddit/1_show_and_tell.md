# Reddit post draft — Show & Tell / "I built the thing"

> Follow-up to [0_intro.md](0_intro.md) (the viability check). Post this once you've
> done a real Medium sign-in and confirmed Refresh pulls your actual numbers.

**Suggested subreddits:** r/SideProject, r/csharp, r/dotnet, r/medium (pick 1–2)

**Title options:**
- I built a tiny local Windows app that logs my Medium stats over time (append-only history)
- Scratched my own itch: a private desktop "black box recorder" for my Medium readership
- WPF + WebView2 weekend project: snapshot your Medium stats into a local CSV history

---

## Body

A little while back I asked here whether it was worth building a small desktop app to keep a *history* of my Medium stats, since Medium's own page only shows current numbers. Enough of you said "yeah, I'd use that" — so I built a first version. Sharing how it works and what I learned.

**What it does**
- Logs readership stats for my own Medium account: followers, and per-story views / reads / read-ratio / claps.
- Shows the latest snapshot as a list.
- A **Refresh** button appends **one timestamped line** to an append-only history log (`report.csv`), so I can scroll back and see how things grew week to week.
- 100% local. No cloud, no account, no third-party analytics. The data never leaves my machine.

**How it gets the data (the interesting part)**
Medium has no public personal-stats API, so:
- I log in once through an embedded browser (WebView2). The session cookie is stored **encrypted at rest with Windows DPAPI** (only my Windows user on my machine can decrypt it).
- It then calls the same internal JSON endpoints Medium's own stats page uses, and parses them.

**The bug that taught me something**
My first cut treated "the `sid`/`uid` cookies exist → user is logged in." Turns out **Medium sets those cookies for anonymous visitors too**, so the login window would instantly "capture" a useless anonymous session and close itself — looked like the Sign-in button did nothing. Fix was to only capture once the webview actually lands on the authenticated stats page (logged-out users get redirected to `/m/signin`).

**Stack:** C# / .NET 8, WPF, WebView2, `System.Text.Json`, CSV + SQLite, DPAPI. ~25 small files, a handful of unit tests.

**Honest caveats**
- These are *undocumented* endpoints, so Medium could change them and break it. I isolated all of that behind one class so it's a one-file patch, and it dumps the raw response on a parse failure to make fixing it fast.
- It's read-only and for my own account only. I keep refreshes manual/infrequent to be respectful of their servers + ToS.

**Questions for the sub**
1. For those who do something similar with internal endpoints — how do you keep up when they drift? Any patterns beyond "isolate + log raw"?
2. Worth open-sourcing / packaging as an installer, or is this too niche?
3. Anyone want a feature I haven't built yet (charts from the history log, Excel export, auto-refresh)?

Happy to share the repo / a screenshot in the comments if there's interest.

---

## Notes to self (not part of post)
- Attach a screenshot of the totals strip + history grid (blur real numbers if you want).
- Don't paste the internal endpoint URLs verbatim in the post — keep it high level to avoid drawing the wrong kind of attention.
- If posting to r/csharp or r/dotnet, lead with the DPAPI + WebView2 cookie-capture details and the anonymous-cookie bug.
- If posting to r/medium or r/SideProject, lead with the "history of my own stats" pain point and the screenshot.
- Decide the open-source question BEFORE posting (people will ask for the repo immediately).

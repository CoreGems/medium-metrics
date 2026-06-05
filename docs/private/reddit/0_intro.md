# Reddit post draft — intro / viability check

**Suggested subreddits:** r/SideProject, r/medium, r/csharp, r/Windows (pick 1–2)

**Title options:**
- Thinking about building a tiny Windows app to log my Medium stats over time — is this worth it?
- Would a desktop app that snapshots your Medium readership stats be useful, or am I reinventing the wheel?

---

## Body

Hey all,

I write on Medium for my own small/private account, and I keep running into the same annoyance: Medium's stats page only shows you *current* numbers. If I want to know how a story grew week over week, I'm stuck eyeballing it or manually copying numbers into a spreadsheet. There's no easy personal history of "where were my views/reads/followers last Tuesday vs today."

So I had an idea for a small **Windows desktop app** to fix this for myself:

- It logs readership stats for my private Medium account (followers, per-story views/reads/read-ratio/claps, and totals).
- It shows the latest snapshot as a simple list/table.
- There's a **Refresh** button that, each time I hit it, appends **one new timestamped line** to a report — basically an append-only history log I can scroll back through to see trends.
- Everything stays **local** on my machine. No cloud account, no third-party analytics service.

The inspiration is really just: I want a lightweight, private "black box recorder" for my own Medium numbers without leaving the data with someone else or paying for a SaaS analytics tool.

On the technical side, since Medium doesn't have a public personal-stats API, my plan is to log in once through an embedded browser (WebView2) and read the same internal JSON the stats page uses, with my own session cookie. Stack would be C# / .NET on WPF.

**My questions for you:**

1. **Viability** — is this actually feasible long-term, or will Medium's internal endpoints break constantly and make it a maintenance nightmare?
2. **Usefulness** — beyond just me, would other Medium writers want something like this? Or does everyone already solve it some other way (spreadsheet, existing tool, etc.)?
3. **Existing solutions** — is there already a good, private, local tool that does this that I just haven't found?
4. **TOS / risk** — anyone have experience with how Medium feels about reading your *own* stats this way? I'd keep refreshes manual and infrequent.

Not trying to build a big product — mostly scratching my own itch — but if it's useful to others I'd consider polishing it. Curious whether this is a "yeah, build it" or a "don't bother, here's why."

Thanks!

---

## Notes to self (not part of post)
- Keep it honest that it's for a *private/personal* account and read-only.
- Don't over-promise; frame as a viability question, not a launch.
- If posting to r/csharp, lead more with the WebView2 + cookie/JSON approach and ask for technical gotchas.
- If posting to r/medium, lead more with the "history of my stats" pain point.

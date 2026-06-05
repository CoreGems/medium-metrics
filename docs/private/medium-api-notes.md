# Medium internal stats — endpoint notes (UNOFFICIAL)

> These are **undocumented** endpoints used by Medium's own web UI. They can change
> without notice. All code that depends on them lives in `MediumStatsClient.cs` so a
> break is a one-file patch. Verify with browser DevTools (Network tab) on
> https://medium.com/me/stats and update this file when something drifts.

## Anti-hijacking prefix
Every JSON response is prefixed with `])}while(1);</x>` (exact bytes may vary slightly,
e.g. `])}while(1);</x>`). Strip everything up to the first `{` before parsing.

## Current user + followers
- **GET** `https://medium.com/me?format=json`
- Path to fields (under the stripped JSON root):
  - `payload.user.username` — account handle
  - `payload.user.userId`
  - `payload.user.socialStats.usersFollowedByCount` — **followers**

## Per-story stats
- **GET** `https://medium.com/me/stats?format=json&limit=200`
- `payload.value` is an array of post-stat objects. Observed fields per item:
  - `postId` (string)
  - `slug` / `title` (string)
  - `views` (number)
  - `reads` (number)
  - `claps` or `upvotes` (number) — name has drifted historically; read both
  - `updateNotificationSubscribers` / responses count is NOT always here — may need
    a separate post endpoint; treat responses as best-effort (0 if absent).

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

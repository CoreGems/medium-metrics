# Medium Metrics — Refactor Plan (pre-multi-account)

Preparation plan for supporting **multiple Medium accounts** (the current one + 2 more)
in the Windows desktop app described in [APP_IDEA.md](APP_IDEA.md) / [DEV_PLAN.md](DEV_PLAN.md).

**Goal of this document:** identify the refactoring to do *before* the multi-account
feature, so that adding accounts becomes a small, mechanical change instead of a
risky rewrite. The codebase is small and clean (largest file ~400 lines), so this
is mostly about **introducing one missing concept — "an account" — and untangling
the single God object** that currently owns one of everything.

> **Guiding principle (unchanged from DEV_PLAN):** keep all Medium-shape knowledge
> behind `IMediumStatsClient`. This plan does **not** touch GraphQL parsing — it only
> changes *how many* of each thing the app can hold and *who owns* them.

---

## ✅ Implementation status (done 2026-06-06)

All P0/P1/P2 items below are **implemented and building green** (19 unit tests pass,
clean `dotnet build`). The Medium-shape parser (`MediumStatsClient`) was not touched.

**Added:** `Models/AccountConfig.cs`, `Services/AccountContext.cs`,
`Services/AccountMigration.cs`, `Models/AccountRef` (in `AppSettings.cs`);
tests `AccountConfigTests`, `MigrationTests`, registry test.
**Changed:** `App.xaml.cs` (now holds `ObservableCollection<AccountContext>` + `Active`),
`AppSettings` (registry + `AccountsRoot`), `MainWindow.xaml(.cs)` (switcher dropdown +
"＋"), `StoryStatsWindow` (takes a fetch delegate, not `App`), `SettingsWindow`
(acts on the active account), `MainViewModel` (`AccountUsername`), `MediumMetrics.csproj`
(dropped SQLite). Removed dead `StorySnapshot.MemberViews/NonMemberViews`.

**Deliberate deviations from the original layout below:**
- **`app.log` stays global** (one log at the data root) — simpler than re-initialising
  logging per account; nothing else of value lived at the root after migration.
- **New accounts use a GUID folder + uid de-dup** (not a uid-named folder). The uid is
  only known *after* login, so a uid-named folder would need a post-login rename of an
  open WebView2 profile. The migrated account keeps its uid as the folder key.
- **Browsers stay warm across a switch** (disposed only on exit). Disposing on switch
  would strand the VM's client on a dead browser; with active-only refresh the idle
  ones cost only memory.
- **`IAccountActions` interface deferred.** `SettingsWindow` still takes concrete `App`
  but now operates on `App.Active`. The functional decoupling (per-account sign-in
  state owned by `AccountContext`) is done; the formal interface is optional polish.

---

## TL;DR

The app has no concept of "an account." `App` is a composition-root **God object**
holding exactly one `SessionManager`, `ReportStore`, `MediumBrowser`, and
`MainViewModel` as loose fields, and every window reaches back into `App` for "the"
session. All on-disk state (CSV, latest.json, session cookie, **and the WebView2
cookie jar**) is keyed off a single `AppSettings.DataDirectory`.

**Three things must change before multi-account is even possible:**

1. **Per-account data isolation** — each account needs its own folder for
   `report.csv`, `latest.json`, `session.bin`, and **its own WebView2 profile**
   (two Medium logins cannot share one cookie jar).
2. **An `AccountContext` seam** — bundle the per-account objects into one unit so the
   app can hold a *list* of them instead of one of each loose field.
3. **Decouple the windows from `App`** — views currently call `_app.SignIn()`,
   `_app.HasSession`, `_app.FetchStoryDetailAsync()` against the single global session;
   they must act on a *specific* account.

Everything else (dead code, duplication, oversized classes) is genuine but secondary,
and is listed in [§6](#6-quality--bloat-cleanups-independent-of-multi-account).

---

## 1. How the app is wired today

```
App (OnStartup)  ── owns one of each, forever ──┐
 ├─ AppSettings        (one DataDirectory → all file paths)
 ├─ SessionManager     (one MediumSession "Current")
 ├─ ReportStore        (one report.csv + one latest.json)
 ├─ MediumBrowser      (one hidden WebView2 = one cookie jar)   ← lazy
 └─ MainViewModel      (one Client, one Stories list, one History, IsSignedIn…)

MainWindow ─────► binds the one VM; reads/writes App.Settings directly
SettingsWindow ─► _app.HasSession / _app.SignIn / _app.ClearSession
StoryStatsWindow ► _app.FetchStoryDetailAsync  (→ the one VM's Client)
ReportsWindow ──► (already decoupled: takes a story list + a fetch delegate) ✅
```

### Single-account assumptions (the seams to cut)

| Component | Hardcoded-to-one evidence | Multi-account impact |
|---|---|---|
| `App` fields | `App.xaml.cs:19-23` — `_settings/_sessions/_store/_vm/_browser` single fields | Must become a keyed collection of per-account units |
| Data paths | `AppSettings.cs:32-35` — `ReportCsvPath/LatestJsonPath/SessionPath/LogPath` all derive from one `DataDirectory` | All accounts collide on the same files |
| Account id | `AppSettings.cs:14` — single `AccountId`; `:27-29` single sort; `:25` single window | No registry of accounts |
| Session | `SessionManager.cs:21,25` — one `SecretStore(settings.SessionPath)`, one `Current` | One cookie set only |
| **Cookie jar** | `App.xaml.cs:70` + `MediumLoginWindow.xaml.cs:32` — both point at `{DataDirectory}\webview2` | **Two Medium logins overwrite each other's cookies** |
| Error dump | `App.xaml.cs:71` — `_settings.LatestJsonPath + ".error"` single path | Diagnostics from different accounts overwrite |
| Stats VM | `MainViewModel.cs` — one `Client`, `Stories`, `History`, `IsSignedIn`, `AccountText` | The natural per-account unit |
| Windows → App | `SettingsWindow.xaml.cs:23-38`, `StoryStatsWindow.xaml.cs:22,111`, `MainWindow.xaml.cs:132-136` call into `_app.*` | Dialogs act on "the" session, not a chosen account |

> **The good news:** `MediumStatsClient` already takes a `username` parameter and is
> account-agnostic; `MediumBrowser`'s constructor already takes a `userDataFolder`;
> `ReportStore` already takes its paths from injected settings; and `ReportsWindow`
> is already decoupled (story list + `Func<…>` delegate). The seams are *close* — they
> just need to be made per-account and bundled.

---

## 2. The core problem: there is no "Account"

Today the per-account objects are five loose fields on `App`. The single enabling
refactor is to **bundle them into one disposable unit** and let `App` hold a list:

```csharp
// NEW: everything scoped to one Medium account, created/disposed as a unit.
public sealed class AccountContext : IDisposable
{
    public AccountConfig    Config   { get; }   // id, handle/label, root folder + derived paths
    public SessionManager   Sessions { get; }
    public ReportStore      Store    { get; }
    public MainViewModel    Vm       { get; }   // (consider renaming → AccountViewModel)
    private MediumBrowser?  _browser;            // lazy, per-account profile
    // BuildClient(), SignIn(owner), Clear(), Dispose() — logic that lives inline in App today
}
```

```csharp
// App becomes a shell over a collection instead of single fields.
private readonly ObservableCollection<AccountContext> _accounts = new();
private AccountContext _active = null!;
```

Once this exists, "support 3 accounts" is: load N `AccountContext`s from a registry,
let the user pick `_active`, and bind the main window to `_active.Vm`. The sign-in,
refresh, capture, and dashboard logic currently inlined in `App` (lines 78-222) moves
onto `AccountContext` (per-account) or a thin shell view-model.

---

## 3. Refactor work items (priority-ordered)

Each item is independently shippable with the **existing tests staying green**.
Do P0 → P1 first; they are the prerequisites. P2 is cleanup that can interleave.

### P0 — Foundations (block multi-account; do first)

- [ ] **P0.1 — Split account-scoped paths out of the global `AppSettings`.**
  Introduce `AccountConfig` (or `AccountPaths`) that derives `report.csv`,
  `latest.json`, `session.bin`, `app.log`, `webview2\`, and the `.error` dump from a
  **per-account root** (e.g. `…\MediumMetrics\accounts\<id>\`). `AppSettings` keeps only
  *global* config (`settings.json` stays at the app root). Mirror the existing
  `[JsonIgnore]` derived-path pattern (`AppSettings.cs:32-43`) so `ReportStore`,
  `SessionManager`, and `SecretStore` change only *what* root they're handed.
  _Files:_ `AppSettings.cs`, new `AccountConfig.cs`, `SettingsStore.cs`.
  _Risk:_ low. _Tests:_ existing `PersistenceTests`/`SettingsTests` keep passing if the
  derived path names are preserved; add a "two configs → disjoint paths" test.

- [ ] **P0.2 — Per-account WebView2 profile (the cookie-jar blocker).**
  `MediumBrowser` already takes `userDataFolder` — thread the **per-account** folder
  through both the hidden fetch browser (`App.xaml.cs:70`) and the login window
  (`MediumLoginWindow.xaml.cs:32`) so a given account's login clearance + cookies are
  reused only by that account. Two accounts ⇒ two profiles ⇒ two `CoreWebView2Environment`s.
  > WebView2's `userDataFolder` is fixed at environment-creation time and cannot be
  > switched later, and medium.com cookies are same-domain — so **one profile cannot
  > hold two accounts.** This is non-negotiable for correctness.
  _Files:_ `MediumBrowser.cs` (no change), `MediumLoginWindow.xaml.cs`, `App.xaml.cs`.
  _Risk:_ medium — see [§8 perf note](#8-resource--lifetime-notes) on N hidden browsers.

- [ ] **P0.3 — Introduce `AccountContext` and make `App` hold a collection.**
  Move the inline per-account logic from `App` (`BuildClient` `:66`, `SignIn` `:78`,
  `ClearSession` `:113`, `FetchStoryDetailAsync` `:201`, capture `:127/:208`) onto
  `AccountContext`. `App` keeps app-lifetime concerns (startup, unhandled-exception
  handler, shutdown, data-folder open) and an `_accounts` list + `_active` pointer.
  _Files:_ new `AccountContext.cs`, `App.xaml.cs` (slims down significantly).
  _Risk:_ medium — this is the central change; keep behaviour identical for **one**
  account first, then add the registry (P1).

### P1 — Make it actually multi (after P0 lands as a no-op refactor)

- [ ] **P1.1 — Account registry in settings.** Replace single `AppSettings.AccountId`
  with `List<AccountRef> Accounts` (`{ Id, Handle/Label, FolderName }`) + `ActiveAccountId`.
  Load all on startup; persist add/remove/select.
  _Files:_ `AppSettings.cs`, `SettingsStore.cs`, `App.xaml.cs`.

- [ ] **P1.2 — Account-switcher dropdown in the header.** Add a `ComboBox` to the header
  `StackPanel` (beside Reports/Settings, `MainWindow.xaml:37-47`) listing accounts by
  handle, plus an **"Add account…"** item that runs login into a *new* profile. Selecting
  an entry sets `Active` and rebinds the window's `DataContext` to `Active.Vm`.
  _Files:_ `MainWindow.xaml(.cs)`, shell VM. _Per §7 decision #1._

- [ ] **P1.3 — One-time data migration.** On first launch of the new version, if
  `accounts\` doesn't exist but a legacy `report.csv` does, move the existing
  `report.csv`/`latest.json`/`session.bin`/`app.log`/`webview2\` into
  `accounts\<id>\` and register it. DPAPI `session.bin` moves fine (same user+machine).
  _Files:_ new `Migration.cs` (or in `SettingsStore`/`App` startup). _Tests:_ add one.

### P1.5 — Decouple windows from `App` (do alongside P0.3 / P1.2)

- [ ] **P1.5a — `StoryStatsWindow` should not take `App`.** It only needs a
  `Func<string, CancellationToken, Task<StoryDetail>>` (exactly like `ReportsWindow`
  already does, `ReportsWindow.xaml.cs:27-28`). Pass the **active account's** fetch
  delegate. _File:_ `StoryStatsWindow.xaml.cs:22,111`. _Risk:_ low, high payoff.

- [ ] **P1.5b — Narrow the `SettingsWindow` ↔ `App` surface.** It uses
  `HasSession/SignIn/ClearSession/Settings.DataDirectory/CaptureStatsDebugAsync`
  (`SettingsWindow.xaml.cs`). Point these at the **selected** `AccountContext`
  (sign in / clear *this* account; show *this* account's folder) instead of the
  global singleton. Consider a small `IAccountActions` interface so views depend on a
  narrow surface, not concrete `App`.

- [ ] **P1.5c — Move sign-in state off the stats VM.** `IsSignedIn`/`AccountText`
  (`MainViewModel.cs:42-45`) are session concerns that `App` mutates from outside
  (`App.xaml.cs:54-60,91-95,116-119`). Let `AccountContext` own the `Client` swap and
  expose signed-in state, so `App` stops reaching into the VM's internals.

### P2 — see [§6](#6-quality--bloat-cleanups-independent-of-multi-account).

---

## 4. Proposed target structure

```
%LOCALAPPDATA%\MediumMetrics\
  settings.json                     ← global: Accounts[] registry, ActiveAccountId, window
  accounts\
    <id-or-handle-A>\
      report.csv  latest.json  session.bin  app.log  latest.json.error
      webview2\                     ← account A's cookie jar / Cloudflare clearance
    <id-or-handle-B>\  …            ← fully isolated
    <id-or-handle-C>\  …
```

```
App (shell: startup, accounts list, active pointer, shutdown)
 └─ ObservableCollection<AccountContext>
      AccountContext  (one per account, IDisposable)
        ├─ AccountConfig    (id, label, root → derived paths)
        ├─ SessionManager   → SecretStore(account session.bin)
        ├─ ReportStore      (account report.csv + latest.json)
        ├─ MediumBrowser?   (account webview2 profile, lazy)
        └─ MainViewModel    (account Client, Stories, History, signed-in state)

MainWindow ─► binds Active.Vm; switcher changes Active
Dialogs ───► act on a passed AccountContext / narrow IAccountActions, not concrete App
```

---

## 5. Why each refactor is needed for *this* feature (not gold-plating)

| Refactor | Without it, multi-account would… |
|---|---|
| Per-account paths (P0.1) | …overwrite account A's CSV/latest/session with account B's |
| Per-account WebView2 (P0.2) | …be **impossible** — logins clobber each other's cookies |
| `AccountContext` (P0.3) | …require duplicating five fields × N and N copies of the sign-in code in `App` |
| Account registry (P1.1) | …have nowhere to remember which accounts exist |
| Decouple windows (P1.5) | …leave every dialog acting on the wrong (global) session |

Items **not** in scope because they don't block the feature: rewriting the GraphQL
parser, full MVVM-ifying the code-behind windows, adding charts. Noted but deferred.

---

## 6. Quality / bloat cleanups (independent of multi-account)

Low-risk, do-anytime. None block the feature; several reduce surface area first.

- [ ] **Remove the unused SQLite dependency.** `Microsoft.Data.Sqlite` is referenced
  (`MediumMetrics.csproj:20`) but **never used** — persistence is CSV + JSON. **Drop it** —
  §7 decision #3 keeps history on CSV-per-account, so it has no future use here.
- [ ] **Remove dead model fields.** `StorySnapshot.MemberViews` / `NonMemberViews`
  (`StorySnapshot.cs:27-30`) are never populated anywhere (the member/non-member split
  was never found — confirmed in `docs/private/reddit/3_feature_update.md` Q1). Delete
  until/unless the data exists.
- [ ] **De-duplicate the funnel→headline math.** `StoryStatsWindow` recomputes
  effective views/reads/ratio from the detail twice — in `LoadDetailAsync`
  (`:119-124`) and `BuildClipboardText` (`:176-178`). Extract one helper.
- [ ] **Optionally peel generic JSON helpers out of `MediumStatsClient`.** The ~70 lines
  of `TryGetPath/GetString/GetLong/GetTags/GetEarnings/GetTimestampMs/StripPrefix`
  (`:375-442`) are reusable and not Medium-specific. Move to a `Json` helper **only if**
  a second parser appears — otherwise leave them to honour the "all Medium knowledge in
  one file" principle. (`MediumStatsClient.cs` is the largest file at 396 lines, but it
  is cohesive; resist splitting the *shape* knowledge.)
- [ ] **Isolate dev-only capture scaffolding.** `App.CaptureStatsDebugAsync` /
  `CaptureStoryDebugAsync` (`:127-157,208-222`) and `MediumBrowser.CaptureStatsAsync` /
  `CaptureStoryAsync` are debugging tools mixed into production classes. Consider a
  `#if DEBUG` guard or a separate `DebugCapture` collaborator so `App` shrinks.

---

## 7. Decisions (locked 2026-06-06)

All P1-shaping choices favour **minimal change + strict isolation**, which keeps the
data model (`StatsSnapshot`, `Reports`, `ReportStore`, `HistoryRow`) untouched and
confines the work to ownership/multiplicity.

| # | Decision | Choice | Consequence for the plan |
|---|---|---|---|
| 1 | Switching model | **Account switcher** — a dropdown in the header | One main window bound to `Active.Vm`; the dropdown swaps `Active`. No tabs, no aggregate view. |
| 2 | Cross-account aggregation | **Strictly per-account** | **No account dimension** added to `StatsSnapshot`/`Reports`/`HistoryRow` — they stay exactly as-is, just instantiated per account. |
| 3 | History store | **CSV per account** | `ReportStore` is reused unchanged, one per account folder. **Drop the unused SQLite dependency** (now a firm action, not optional). |
| 4 | Refresh | **Active account only** | `Refresh` fetches just `Active`; only that account's browser needs to be warm. No "Refresh all." |
| 5 | Identity / folder key | *Default:* Medium **uid** (already captured at `MediumSession.Uid`) as the stable folder key; **handle** from `/me` as an editable display label. | Folders never collide or churn if a handle changes; the dropdown shows the friendly handle. Override if you'd prefer handle-named folders. |

**Net effect:** decisions #2 and #3 mean the `Models/` + `Reports.cs` + `ReportStore.cs`
layer needs **no structural change**. The multi-account work is purely **P0** (isolation
+ `AccountContext`) and **P1** (registry + switcher + migration).

---

## 8. Resource & lifetime notes

- Each `MediumBrowser` is a real hidden WebView2 (an `msedgewebview2.exe` process tree).
  With **active-account-only refresh (§7 #4)** you only ever need the *active* account's
  browser warm — so **create lazily on first refresh and dispose the previous one on
  account switch** keeps it to ~1 background browser regardless of account count.
  Still, `App.OnExit` must dispose **any** live browser(s), not just one (`App.xaml.cs:229`).
- `ShutdownMode = OnMainWindowClose` (`App.xaml.cs:34`) still holds — there is still one
  main window; only its bound VM changes when switching accounts.

---

## 9. Suggested sequencing (checklist)

```
P0  (refactor, behaviour-identical for the single existing account)
 1. P0.1  AccountConfig — per-account path derivation
 2. P0.2  per-account WebView2 profile (login + fetch)
 3. P0.3  AccountContext bundle; App holds a 1-element collection
 4. P1.5a StoryStatsWindow takes a fetch delegate, not App
 5. green: build + 10 unit tests pass + manual single-account refresh works

P1  (turn it on — decisions locked, see §7)
 6. P1.1  account registry in settings (key by uid, label by handle)
 7. P1.3  migrate the existing account into accounts\<id>\
 8. P1.5b/c  decouple SettingsWindow + move sign-in state to AccountContext
 9. P1.2  account-switcher dropdown + "Add account…"

P2  (cleanup, interleave freely)
10. drop SQLite dep · remove dead MemberViews fields · de-dupe funnel math · etc.
```

---

## 10. Testing strategy

Current coverage: `PersistenceTests`, `SettingsTests`, `GraphQlParsingTests` (10 tests)
construct `AppSettings { DataDirectory = temp }` + `ReportStore(settings)`. Keep that
green by preserving the derived-path names through the `AccountConfig` split. **Add:**

- two `AccountConfig`s → fully disjoint file paths (no shared file);
- account-registry load/save round-trip;
- migration: legacy layout → `accounts\<id>\` moves every file and registers one account;
- (regression) `StoryStatsWindow`/`ReportsWindow` still resolve detail via the active
  account's delegate.

---

*Scope reminder: this plan changes ownership and multiplicity, not Medium-shape parsing.
The fragile `MediumStatsClient` stays the one place that knows Medium's JSON.*

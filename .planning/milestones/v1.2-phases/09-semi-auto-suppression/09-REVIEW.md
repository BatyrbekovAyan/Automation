---
phase: 09-semi-auto-suppression
reviewed: 2026-07-22T16:12:56Z
depth: standard
files_reviewed: 11
files_reviewed_list:
  - Assets/Scripts/Chat/SuggestionsController.cs
  - Assets/Scripts/Main/ChatManager.Channel.cs
  - Assets/Scripts/Main/Manager.ReplyModeSync.cs
  - Assets/Scripts/Main/Manager.cs
  - Assets/Tests/Editor/Chat/ReplyModeSyncPayloadTests.cs
  - Tools/n8n/README.md
  - Tools/n8n/build-set-reply-mode.py
  - Tools/n8n/supabase/2026-07-19-reply-mode-flags.sql
  - Tools/n8n/workflows/4VN3gsFaC2HUYmcc-Telegram_Bot.json
  - Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json
  - Tools/n8n/workflows/SCLcpn6DMDG3Z4VN-Set_Reply_Mode.json
findings:
  critical: 0
  warning: 4
  info: 5
status: issues_found
---

# Phase 09: Code Review Report

**Reviewed:** 2026-07-22T16:12:56Z
**Depth:** standard
**Files Reviewed:** 11
**Status:** issues_found

## Summary

Reviewed the Phase 09 semi-auto suppression gate end-to-end: the client write path (`Manager.ReplyModeSync.cs` pure builders + fire-and-forget POST, `SuggestionsController.PushReplyModeForActiveChat` write sites, `ChatManager.ActiveChannelProfileId()` accessor), the shared `Set_Reply_Mode` webhook workflow, the `Read Reply Mode` + `Suppressed?` gate splice in both bot templates, the `reply_mode_flags` DDL, the deployer script, and the EditMode payload tests.

The core mechanics are sound and consistent across layers: the payload contract matches the Validate node, sentinel/blank profile ids are filtered on both ends, the gate query's `order by (chat_id = '*')` precedence (specific-before-star) is correct, coalesce-to-false on row absence matches the documented "absence = Авто" design, a Postgres read failure fails closed (workflow errors, no reply), RLS default-deny on the table is correct, and the fire-and-forget coroutine deliberately lives on `Manager` so `ChatManager.StopAllCoroutines()` (bot/channel switches) can never kill an in-flight write. The unauthenticated `/webhook/SetReplyMode` posture is the accepted risk R-02-01 and is not raised here.

The significant findings are two state-consistency gaps in the write path — the on-open heal converts *inherited* bot-default state into sticky per-chat server rows (WR-01), and a late-authed channel never receives the `'*'` bot-default row (WR-02) — plus a one-line input-hardening gap in the Set_Reply_Mode Validate node against n8n's documented queryReplacement comma-split (WR-03). Both WR-01 and WR-02 produce the same symptom class: server suppression state silently diverging from what the app displays.

## Warnings

### WR-01: On-open heal writes inherited bot-default state as a sticky per-chat override row

**File:** `Assets/Scripts/Chat/SuggestionsController.cs:114-120` (with `SemiAutoStore.IsOn`)
**Issue:** `RestoreForActiveChat` computes `_semiAutoOn = SemiAutoStore.IsOn(...)` and, when true, calls `PushReplyModeForActiveChat(true)`. But `SemiAutoStore` is tri-state (0 = no override / inherit bot default, 1 = explicit OFF, 2 = explicit ON) and `IsOn` collapses state 0 into the bot default. Consequences:

1. **Inherited state becomes a sticky server override.** With the bot default on Вместе, merely *opening* a chat (raw = 0) writes a per-chat `suppressed=true` row server-side. When the owner later flips the bot default back to Авто, the `'*'` row goes false — but every previously-opened chat's per-chat `true` row still wins precedence in the gate. Those chats stay suppressed (bot silent, customers unanswered) while the app shows Авто, and nothing ever heals them: `IsOn` now returns false for them, so the ON-only heal never fires, and the user has no visible override to clear.
2. **Explicit OFF is never re-asserted.** The heal is ON-only, so a lost `suppressed=false` write (explicit per-chat Авто override, raw = 1) is never healed on open — stale suppression persists for that chat.

Note the comment on line 120 («heal a lost "back to Авто" write; ON-state only») describes exactly the case the code does *not* cover — the ON-state heal covers a lost «Вместе» write.

**Fix:** Re-assert from the raw tri-state, not the collapsed boolean. Expose the override explicitly on `SemiAutoStore`:

```csharp
// SemiAutoStore.cs
public static bool TryGetOverride(string botId, string chatId, out bool on)
{
    int raw = GetInt(Key(botId, chatId));
    on = raw == 2;
    return raw != 0;   // 0 = no explicit override
}
```

```csharp
// SuggestionsController.RestoreForActiveChat — replace the ON-only heal:
if (SemiAutoStore.TryGetOverride(cm.CurrentBotId, cm.CurrentChatId, out bool overrideOn))
    PushReplyModeForActiveChat(overrideOn);   // heal BOTH explicit states; inherited chats push nothing
```

Inherited chats then correctly rely on the `'*'` row alone, and both explicit states self-heal. Also correct the line-120 comment.

### WR-02: Late channel auth never seeds the `'*'` bot-default row for the new profile

**File:** `Assets/Scripts/Main/Manager.ReplyModeSync.cs:82-87` (with `Assets/Scripts/UI/ReplyModeToggleBinder.cs:155`)
**Issue:** The only bot-default write site is `OnBotReplyModeChanged`, and `ReplyModeToggleBinder.OnReplyModeChanged` fires only when the owner *flips* the mode. A channel that gets authed *after* the owner set the bot to Вместе acquires a real profile id but no `'*'` row; the gate coalesces to `false` for that profile and the bot auto-replies on the newly-authed channel while the app shows the bot-level toggle as Вместе — double-reply risk (bot answers autonomously and the owner also replies from the suggestions panel). The reverse divergence cannot occur (absence already means Авто), so this is a one-directional fail-open gap.
**Fix:** Re-assert the current default for the newly-authed profile when auth completes / the workflow is created (e.g., in the auth-done handler or wherever the profile id is persisted):

```csharp
bool semi = ReplyModeToggleBinder.GetMode(bot.name) == ReplyModeToggleBinder.ReplyMode.Semi;
if (semi) Manager.Instance.SyncReplyMode(new[] { newProfileId }, "*", true);
```

(Pushing only the Semi case is sufficient — absence already reads as Авто.) If the wiring pass (09-04/09-05) already covers this in a file outside this review's scope, verify and document the call site; the reviewed code contains no such re-assert.

### WR-03: Set_Reply_Mode Validate does not harden inputs against the queryReplacement comma-split

**File:** `Tools/n8n/workflows/SCLcpn6DMDG3Z4VN-Set_Reply_Mode.json:23` (Validate jsCode) and `:89` (Upsert `queryReplacement`)
**Issue:** The Upsert node binds parameters via `queryReplacement: "={{ $json.profileId }},{{ $json.chatId }},{{ $json.suppressed }}"`. n8n splits this string on commas *after* expression rendering (a documented gotcha in this project). The webhook is unauthenticated (accepted posture R-02-01), and Validate only checks "non-empty string" — so a `chatId` like `"x,true"` shifts the positional parameters: the row is written with truncated `chat_id = 'x'` and a `suppressed` value taken from the wrong fragment, or the node errors. Legitimate Wappi chat/profile ids never contain commas, so rejecting them costs nothing.
**Fix:** One-line additions to the Validate node:

```javascript
const clean = (s) => typeof s === "string" && s.length > 0 && s.length <= 128 && !s.includes(",");
let ids = Array.isArray(b.profileIds)
  ? b.profileIds.filter(x => clean(x) && !sentinel(x)).slice(0, 10)
  : [];
const okChat = clean(b.chatId);
```

Re-export the canonical JSON after applying (deployer imports it verbatim).

### WR-04: Pre-existing silent-failure network paths in Manager.cs (legacy debt, not introduced by Phase 09)

**File:** `Assets/Scripts/Main/Manager.cs:2904-2907, 2979-2982, 3815-3821`
**Issue:** `CreateWhatsappProfile` and `CreateTelegramProfile` have completely empty failure branches (`if (www.result != UnityWebRequest.Result.Success) { }`) — a failed `profile/add` shows no error, logs nothing, and leaves the wizard silently stuck on the loader-then-auth screen with `profileId == "-1"`. `UploadFile` (line 3793) has *both* result branches empty. All three also omit `request.timeout`, violating the project networking rules (always log with status code + URL, always set timeout 30).
**Fix:** Apply the standard pattern at minimum to the two profile-creation coroutines:

```csharp
www.timeout = 30;
// ...
if (www.result != UnityWebRequest.Result.Success)
{
    Debug.LogError($"[CreateWhatsappProfile] [{www.responseCode}] {www.url}: {www.error}");
    // surface a user-visible retry state instead of a silent dead-end
}
```

Flagged as advisory since these predate Phase 09; the file is in scope only because it gained the `partial` keyword (verified: the primary file declares no `OnEnable`/`OnDisable`, so the partial's subscriptions in `Manager.ReplyModeSync.cs:74-75` do not conflict).

## Info

### IN-01: PushReplyModeForActiveChat lacks a CurrentChatId guard

**File:** `Assets/Scripts/Chat/SuggestionsController.cs:152-161`
**Issue:** The method guards null managers, missing bot, and sentinel/blank profile id, but not an empty `cm.CurrentChatId`. A null chatId would serialize as `"chatId":null` and be rejected server-side (`bad_request`) — harmless but a wasted request and an error log. Current call sites always have an open chat, so this is defense-in-depth only.
**Fix:** `if (string.IsNullOrEmpty(cm.CurrentChatId)) return;` before the `SyncReplyMode` call.

### IN-02: BuildReplyModePayload throws NRE on null profileIds

**File:** `Assets/Scripts/Main/Manager.ReplyModeSync.cs:33-42`
**Issue:** The method is `public static` (test-facing) but only `SyncReplyMode` null-guards. `new List<string>((IReadOnlyList<string>)null)` throws `ArgumentNullException` wrapped paths aside, a direct null call NREs/throws with an unhelpful trace.
**Fix:** `profileIds ??= System.Array.Empty<string>();` at the top (or throw `ArgumentNullException` explicitly).

### IN-03: Set_Reply_Mode has no fan-out cap on profileIds

**File:** `Tools/n8n/workflows/SCLcpn6DMDG3Z4VN-Set_Reply_Mode.json:23`
**Issue:** Validate accepts an arbitrarily long `profileIds` array and fans out one DB upsert per entry on an unauthenticated webhook. The legit client sends at most 2. Related to accepted risk R-02-01, so informational — the `.slice(0, 10)` in the WR-03 fix snippet covers this too.
**Fix:** Cap the surviving id list (e.g., `slice(0, 10)`), as shown in WR-03.

### IN-04: build-set-reply-mode.py default POST-create can strand a duplicate workflow

**File:** `Tools/n8n/build-set-reply-mode.py:151-181, 106-117`
**Issue:** With `SCLcpn6DMDG3Z4VN` already deployed and active, running the script without `--update` POST-creates a second "Set Reply Mode" workflow; its activation then collides on the shared `/webhook/SetReplyMode` path, the script exits 1, and an orphaned inactive duplicate is left on the instance. Separately, `req()` catches only `urllib.error.HTTPError` — a connection-refused (instance down) surfaces as a raw `URLError` traceback instead of a clean message.
**Fix:** Before POST-create, GET `/workflows` and refuse (with a hint to use `--update <id>`) if a workflow named "Set Reply Mode" exists; wrap `urlopen` failures in `except urllib.error.URLError` with a readable exit message.

### IN-05: Dead legacy file-picker/upload path and typo variables in Manager.cs

**File:** `Assets/Scripts/Main/Manager.cs:3706-3821, 2251, 2916, 2991`
**Issue:** `PickFile` / `PickMediaFile` / `PickPDFFile` / `PickCreateAllTXTFile` / `UploadFile` form an orphaned legacy upload path (large commented-out regions, empty result branches). If ever re-wired, `UploadFile` bypasses the `UploadedFilesStore` fileId contract (no GUID form field), producing untracked/undeletable RAG chunks. Also repeated `lenght` typo locals and stale commented-out field/code blocks (lines 17, 20, 231-244, 3720-3731, 3826-3851).
**Fix:** Delete the dead path (BotSettings.Files.cs owns uploads), or mark it `[System.Obsolete]` with a comment pointing at the real path; rename `lenght` → `length` opportunistically when touching those methods.

---

**Notes on scope discipline:** The Phase-10 debounce chain present in both bot templates (`Debounce Wait → Fetch Recent → Latest+Combine → Is Latest?` on the `Suppressed?` FALSE branch) is expected and was reviewed in the Phase-10 review — not re-flagged here. Live gate behavior (curl matrix + runData, both channels) was verified this week per phase context; this review is code-level only. `ChatManager.Channel.cs`'s Phase-09 addition (`ActiveChannelProfileId()`) is clean; the rest of that file is prior-phase code with no new defects found. `ReplyModeSyncPayloadTests.cs` correctly covers the pure seams (payload shape, sentinel/blank filtering) with proper `DestroyImmediate` cleanup. `Tools/n8n/README.md` and the DDL are accurate and internally consistent (default-deny RLS, owner-exempt, pk-covered lookups).

_Reviewed: 2026-07-22T16:12:56Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_

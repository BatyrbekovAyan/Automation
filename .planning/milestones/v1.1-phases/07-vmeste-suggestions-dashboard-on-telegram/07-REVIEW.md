---
phase: 07-vmeste-suggestions-dashboard-on-telegram
reviewed: 2026-07-13T12:58:59Z
depth: standard
files_reviewed: 8
files_reviewed_list:
  - Assets/Scripts/Chat/N8nSuggestionsProvider.cs
  - Assets/Scripts/Chat/SuggestRepliesDtos.cs
  - Assets/Scripts/Main/Dashboard/DashboardMetrics.cs
  - Assets/Scripts/Main/Dashboard/DashboardPage.cs
  - Assets/Scripts/Main/Dashboard/DashboardProfileMap.cs
  - Assets/Tests/Editor/Chat/DashboardMetricsTests.cs
  - Assets/Tests/Editor/Chat/DashboardProfileMapTests.cs
  - Assets/Tests/Editor/Chat/SuggestRepliesPayloadTests.cs
findings:
  critical: 0
  warning: 1
  info: 3
  total: 4
status: fixes_applied
fixes:
  applied_at: 2026-07-13
  fixed: 4        # WR-01, IN-01, IN-02, IN-03 (all findings)
  deferred: 0
  wontfix: 0
---

# Phase 7: Code Review Report

**Reviewed:** 2026-07-13T12:58:59Z
**Depth:** standard
**Files Reviewed:** 8 (diff base `9190074`; DashboardProfileMap.cs reviewed whole-file)
**Status:** issues_found

## Summary

Phase 7 makes the «Вместе» suggestions payload and the «Сводка» dashboard channel-aware. The diff is tight, well-commented, and the four high-risk areas called out for scrutiny all check out — the one Warning is a null-key edge on a line this phase retyped, plus three Info items (dead seam, a test-vs-docstring identity gap, doc drift). No source files were modified by this review.

### Scrutiny area 1 — Wire-contract safety: VERIFIED CLEAN

- **Additive identity holds.** The two v1.1 fields are declared LAST in `SuggestRepliesRequestDto` (`SuggestRepliesDtos.cs:47-49`, after `messages`), and Json.NET emits fields in declaration order. The serializing call is the unchanged `JsonConvert.SerializeObject(dto)` (`N8nSuggestionsProvider.cs:177`) with default settings — no `JsonConvert.DefaultSettings` override exists anywhere under `Assets/Scripts/`. A WhatsApp request is therefore the frozen v1 byte sequence with `"botTgId":…,"channel":"whatsapp"` appended before the closing brace; null-value handling, field order, and clamping are untouched.
- **Channel string is closed-set.** `channel = isTelegram ? "telegram" : "whatsapp"` (`N8nSuggestionsProvider.cs:159,175`) — derived solely from an enum equality check, so ANY non-Telegram value (including a theoretically corrupt persisted ordinal, which `ReadPersistedChannel`/`ChannelResolver.Resolve` already clamp) degrades to `"whatsapp"`. No free-form string can reach the wire.
- **botTgId sentinel semantics are sound.** `Bot.telegramWorkflowId` is always `"-1"` (PlayerPrefs default at `Manager.cs:379-380`, explicit `"-1"` writes elsewhere), `""`, or a real id — never null at runtime — so `botTgId` mirrors `botWaId`'s documented `""`/`"-1"` skip-RAG sentinel exactly, and `botWaId` still carries `whatsappWorkflowId` unconditionally (backward compat).
- **profileId selection** is channel-resolved in the pure builder; on WhatsApp it emits precisely the v1 value.

### Scrutiny area 2 — Provider channel-resolution timing: VERIFIED CLEAN

`cm.ActiveChannel` is read at `N8nSuggestionsProvider.cs:75`, AFTER the drain yield (line 57) and in the same synchronous block as the `TryGetRecentMessages(req.chatId)` guard (line 66) and the request dispatch (lines 89-94). Unity coroutines are single-threaded, so nothing can interleave between capture and send within that block. A channel switch during the drain (or mid-request) cannot mislabel a request for two independent reasons:

1. `SetActiveChannel` (`ChatManager.Channel.cs:78`) calls `StopAllCoroutines()` on ChatManager — the provider coroutine is hosted there (`N8nSuggestionsProvider.cs:47`), so it is killed outright (mirroring `SetActiveBot`, `ChatManager.BotState.cs:128`). The `using var www` disposes via the iterator's finally on kill.
2. Even hypothetically surviving, `SetActiveChannel` nulls `currentChatId` and `_activeChatCache` (`ChatManager.Channel.cs:60-61`), so `TryGetRecentMessages(req.chatId)` fails its `chatId != currentChatId` guard (`ChatManager.RecentMessages.cs:20`) → Empty result, no network call.

So the chat-switch guard covers cross-channel by construction: a channel switch always closes the open chat before the guard could be consulted again. The dropped-callback-on-kill behavior (panel never gets a result) is byte-identical to the pre-existing bot-switch path — the switch also closed the chat view, and the next chat open issues a fresh `requestSeq` — no regression.

### Scrutiny area 3 — Dashboard: VERIFIED CLEAN (one Warning, below)

- **DashboardProfileMap correctness:** dual-channel bots contribute both ids (WA-then-TG, bot order) to `AuthedProfiles`; TG-only bots resolve `ChatChannel.Telegram` from WHICH id matched; `""`/`"-1"` sentinels are excluded everywhere via one `IsAuthed`. Duplicate profile ids across bots overwrite last-wins in `ProfileToBot` — identical to the pre-phase dictionary-indexer behavior, and unreachable in practice (Wappi ids are per-profile GUIDs).
- **FilterByProfiles set semantics = old equality semantics:** null/empty set → all rows matches old null/empty string → all rows; the single-id overload delegates through a 1-element set, preserving old behavior exactly. `Render`, `OpenStatusList`, and `RenderRecent` all consume the same filtered list, so status counts and drill-down stay consistent.
- **OpenChat:** `TryResolve` early-returns on null map / null / unknown / forged profileId (T-07-02-01 honored); order bot → channel → tab → deferred select is correct, and `OpenChatDeferred` is started AFTER `SetActiveChannel`'s `StopAllCoroutines()`, so the deep-link coroutine survives. The double-load when the row's channel differs from the bot's persisted channel is an explicitly documented, accepted trade-off.
- **BindRow retype fallout:** no other production caller of the old `Dictionary<string,string>` map shape remains — but see IN-01 (the old seam is now dead code) and WR-01 (a null-key edge on the retyped lookup).

### Scrutiny area 4 — Test quality: VERIFIED CLEAN

- `WhatsAppRequest_AdditivelyIdenticalToV1` removes exactly `channel` + `botTgId`, then asserts `JToken.DeepEquals` against a fully-specified 12-key v1 object AND an exact key-set comparison — genuine deep equality, not substring matching (see IN-02 for the order nuance).
- TG-only matrix present in both suites: `TelegramOnlyBot_WaSentinelPassesThrough` (payload: TG profile selected, WA sentinel verbatim, TG workflow id, `"telegram"`) and `TelegramOnlyBot_CollectsMapsAndResolvesTelegram` (map/chips/resolve). Channel selection is tested in both directions plus the lowercase-constants negative assertions; sentinel/empty and null/forged/null-map guards are covered; `FilterByProfiles` has set, null, and empty-set cases. Minor untested corners (multi-bot `AuthedProfiles` ordering, cross-bot duplicate-id collision) are theoretical and acceptable at this depth.

## Warnings

### WR-01: `BindRow` throws on a null `profileId` outcome row, truncating the rendered list

**Status:** FIXED (a5f4ee7) — `!string.IsNullOrEmpty(r.profileId)` short-circuits before the `map.TryGetValue` in `BindRow`, with a comment tying it to T-07-02-01 and the SpawnRows-truncation consequence. No new EditMode test: the guard is inline in a private MonoBehaviour UI method (BindRow needs an instantiated row-prefab hierarchy with `Transform.Find` children + PlayerPrefs + `ChatManager.Instance` — no pure seam reaches DashboardPage.cs:380); the null-profileId tolerance contract is already pure-seam-covered by `DashboardProfileMapTests.TryResolve_MissOrNullReturnsFalse` (same semantics, same map). The optional Parse-level row sanitization was deliberately NOT taken (minimal fix scope).

**File:** `Assets/Scripts/Main/Dashboard/DashboardPage.cs:380`
**Issue:** `map.TryGetValue(r.profileId, out var pref)` throws `ArgumentNullException` when an outcome row has a null `profileId` and the bot tag is shown (no bot filter + ≥2 bots — exactly the state where a null-profileId row survives filtering, since `FilterByProfiles` only excludes it when a filter set is active). `DashboardResponse.Parse` (`DashboardModels.cs:52-62`) tolerates the envelope but does not sanitize per-row fields, and `DashboardStore.Load` can replay such a row from disk cache indefinitely. The exception escapes `BindRow` inside `SpawnRows`' loop (`DashboardPage.cs:344-349`), so every row after the bad one silently fails to render. Notably, this phase hardened `DashboardProfileMap.TryResolve` (`DashboardProfileMap.cs:106`) against exactly this null (threat T-07-02-01), and the shape pre-dates Phase 7 — but the line was retyped in this diff (`out var bn` → `out var pref`) without receiving the same guard, leaving the hardening inconsistent within one feature.
**Fix:**
```csharp
if (botTag) { botTag.gameObject.SetActive(showBotTag);
    if (showBotTag && !string.IsNullOrEmpty(r.profileId) && map.TryGetValue(r.profileId, out var pref))
        botTag.text = PlayerPrefs.GetString(pref.botName + "Name", pref.botName); }
```
(Optionally also drop structurally invalid rows — null element / null `profileId` / null `chatId` — once at `DashboardResponse.Parse`, which would harden `r.Status`/`r.chatId` consumers in the same pass.)

## Info

### IN-01: `SessionChatMap.ResolveBotName` is now production-dead

**Status:** FIXED (e0418a7) — annotate-not-delete: the class doc comment now marks SessionChatMap SUPERSEDED (Phase 7) by `DashboardProfileMap.TryResolve` and names it + SessionChatMapTests a cleanup candidate for a later hygiene pass. Deletion deliberately deferred out of this phase (scope). `FilterByProfile` kept as documented back-compat per the finding.

**File:** `Assets/Scripts/Main/Dashboard/SessionChatMap.cs:7`
**Issue:** `OpenChat` was its only production caller and now uses `DashboardProfileMap.TryResolve`; the sole remaining references are `SessionChatMapTests` and a doc-comment mention. `DashboardMetrics.FilterByProfile` (`DashboardMetrics.cs:46`) is similarly test-only now, though it is at least documented as a delegating convenience overload.
**Fix:** Delete `SessionChatMap` + `SessionChatMapTests` (or annotate why the seam is retained). Keeping `FilterByProfile` is defensible as documented back-compat; if kept, no action needed.

### IN-02: Additive-identity test proves structural, not byte, identity

**Status:** FIXED (602aa1f) — resolved by aligning the docstrings with the proof rather than adding an ordered assertion (fix-scope direction): `SuggestRepliesDtos.cs` (request-body summary), `N8nSuggestionsProvider.cs` (`BuildPayloadJson` doc), and the test's section header + inline comment now state that the enforced contract is STRUCTURAL identity (JToken.DeepEquals + exact key set), with byte order following separately — and un-enforced — from Json.NET's declaration-order emission over the appended-last v1.1 fields.

**File:** `Assets/Tests/Editor/Chat/SuggestRepliesPayloadTests.cs:245-297` (`WhatsAppRequest_AdditivelyIdenticalToV1`)
**Issue:** The docstrings (`SuggestRepliesDtos.cs:26-30`, `N8nSuggestionsProvider.cs:144`) claim "byte-identical", but `JToken.DeepEquals` is property-order-insensitive and the key-set assertion sorts before comparing — a future DTO field reorder (or an inserted-rather-than-appended key) would stay green while changing the emitted bytes. Harmless to any JSON parser, so Info only; flagging because the byte-identity claim is the stated contract.
**Fix:** Add one ordered assertion, e.g. compare `j.Properties().Select(p => p.Name)` (unsorted) against the v1 key sequence, or assert the raw JSON string starts with the expected v1 prefix.

### IN-03: Channel-scope documentation drift

**Status:** FIXED (b2aee8e) — CLAUDE.md's DashboardOutcomes bullet replaces "WhatsApp-only in v1" with: since Phase 7 `profileIds` carries BOTH channels' authed Wappi ids, and each row's bot + channel resolves client-side via `DashboardProfileMap` from WHICH local id matched (never the server payload). The `BindRow` comment is now "Real chat avatar…" and documents the dual-channel fallback (non-active-channel rows miss the chatId-keyed lookup and take the colored-initial default).

**File:** `CLAUDE.md` (External APIs → `/webhook/DashboardOutcomes`); `Assets/Scripts/Main/Dashboard/DashboardPage.cs:382`
**Issue:** CLAUDE.md still says DashboardOutcomes is "WhatsApp-only in v1", but DASH-01 now posts Telegram profile ids too; the `BindRow` comment still reads "Real WhatsApp avatar" though rows are now dual-channel (the avatar/title/time accessors are keyed by chatId in the ACTIVE channel's `chatLookup`, so non-active-channel rows gracefully fall back — TG ids pass through `ChatIdFormat.Recipient` unchanged). Per CLAUDE.md's self-maintenance policy, the API note should be updated.
**Fix:** Update the CLAUDE.md DashboardOutcomes bullet to state both channels are posted as of Phase 7, and reword the `BindRow` comment to "Real chat avatar".

---

_Reviewed: 2026-07-13T12:58:59Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_

---
phase: 08-device-uat-milestone-closeout
plan: 05
subsystem: ui
tags: [telegram, chat-list, dedup, cache-isolation, ChatIdFormat, ParseChatsJson, gap-closure]

# Dependency graph
requires:
  - phase: 05-channel-aware-chatmanager-core
    provides: "ChatIdFormat pure seam; per-channel cache roots (BotCache/{botId}/telegram/, CHAT-11); ActiveChannel identity"
  - phase: 08-device-uat-milestone-closeout
    provides: "08-04 open-chat live poll (ParseChatsJson now runs continuously — dedup must hold per cycle)"
provides:
  - "ChatIdFormat.CanonicalKey(id, channel): channel-aware dedup key — WhatsApp verbatim, Telegram strips a spurious @c.us/@g.us twin"
  - "ChatIdFormat.IsForeignToChannel(id, channel): rejects a foreign-channel (bled) dialog from a channel's list"
  - "ParseChatsJson keyed by the canonical id + foreign-dialog rejection: the 777000 TG service dialog is one row on Telegram and absent from WhatsApp"
affects: [08-10-device-reverify, chat-data-flow]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Channel-aware canonical dedup key: the DICTIONARY KEY and the surviving VM's id are the canonical id, so every downstream chatLookup consumer resolves unchanged (byte-identical on WhatsApp)"
    - "Foreign-dialog rejection by jid shape: a real WhatsApp id always carries an '@' suffix; a bare (no-'@') id on the WA list is a bled Telegram dialog"

key-files:
  created:
    - "Assets/Tests/Editor/Chat/ChatListDedupTests.cs"
  modified:
    - "Assets/Scripts/Chat/ChatIdFormat.cs"
    - "Assets/Scripts/Main/ChatManager.cs"
    - "Assets/Tests/Editor/Chat/ChatIdFormatTests.cs"

key-decisions:
  - "CanonicalKey took a ChatChannel parameter (not the plan's single-arg signature): a channel-blind pure function cannot both keep @c.us verbatim for WhatsApp AND strip it for the Telegram twin — the two acceptance constraints are only jointly satisfiable with channel awareness"
  - "The surviving ChatViewModel is constructed with the canonical id (not the raw chat.id): byte-identical on WhatsApp (canonical == raw), the correct bare tapi id on Telegram, and it eliminates the merge-order ambiguity so every downstream chatLookup consumer resolves by vm.ChatId with zero extra call-site changes"
  - "Foreign rejection tests for ANY '@' (not a @c.us/@g.us whitelist) so exotic-but-genuine WhatsApp jids (status@broadcast / @newsletter / @lid) are never dropped"

patterns-established:
  - "Dedup at the ParseChatsJson insert/merge/sweep by ChatIdFormat.CanonicalKey(chat.id, ActiveChannel)"

requirements-completed: []

# Metrics
duration: 18min
completed: 2026-07-16
---

# Phase 8 Plan 05: D7 Telegram service-dialog dedup + cross-channel bleed Summary

**The 777000 Telegram service dialog now collapses to ONE Telegram row (canonical-key dedup) and can no longer appear on the WhatsApp list (jid-shape foreign-dialog rejection) — WhatsApp byte-identical, 1091/1091 EditMode green.**

## Performance

- **Duration:** ~18 min
- **Started:** 2026-07-16T12:20Z (diagnosis reads)
- **Completed:** 2026-07-16T12:34Z
- **Tasks:** 2 (TDD Task 1 = test + feat)
- **Files modified:** 4 (1 created, 3 modified)

## Root cause (D7)

Two independent bugs, diagnosed against the read-only capture `Tools/tapi/samples/chats_filter.json` (2026-07-14) and the code:

**(a) The two id-forms (double row on Telegram).**
The capture shows the service dialog as a SINGLE bare id `"777000"` (`type:"user"`, `name:"Telegram"`, top-level `picture:""` empty, real avatar only in the nested `user.Photo.StrippedThumb`). No `@c.us`/`@g.us` appears anywhere in tapi's `chats/filter`, and no in-app code appends `@c.us` to a Telegram id (grep clean — only a hardcoded test constant in `WappiUnitySync.cs`). So the on-device twin id-form originates **server/device-side** (tapi/the device returning the same dialog under a second id string), not an app bug. Under the old raw-`chat.id` keying, two distinct id strings for one dialog spawn two rows that never merge (different keys) — the bare `777000` resolves the Telegram-logo avatar (valid id) while the twin resolves no avatar (silhouette). **Leading twin hypothesis: a spurious WhatsApp-shaped `777000@c.us`** — it matches the plan's primary example, explains the silhouette (avatar resolution fails on the wrong id), and explains the WhatsApp bleed (a `@c.us`-suffixed id looks like a WhatsApp id).
**Confirmation deferred to 08-10:** the single read-only capture does NOT reproduce the double (only bare `777000`), so the EXACT second id-form must be confirmed against an on-device capture. If it turns out to be a PREFIX form (e.g. `user#777000`) rather than a spurious suffix, extend `CanonicalKey`'s Telegram branch (the WhatsApp branch must stay verbatim). The dedup is derived defensively per the plan.

**(b) The bleed (service dialog on the WhatsApp list).**
WhatsApp `chats/filter` can never return `777000` (a Telegram-only service user), so the WhatsApp-list appearance is NOT a live WA sync — it is read back from a persisted WhatsApp cache file `BotCache/{botId}/chats.json` that contains the Telegram dialog. Mechanism: before the Phase-5 CHAT-11 cache isolation (`BotCache/{botId}/telegram/`), `GetCacheRoot()` had no channel awareness, so Telegram dev-testing on a bot wrote chats to the WhatsApp root; decision [D4] explicitly did "no WA migration," so that stale file lingers on-device and the foreign `777000` renders on the WhatsApp channel. Confirmed the sync path is channel-correct today (`GetCacheRoot()`/`SyncAllChats` compute `cachePath` from the current `ActiveChannel`, and `SetActiveChannel`/`SetActiveBot` `StopAllCoroutines()` a switch so a stale-path write can't complete) — the bleed is a persisted legacy cache, not a live wrong-root write. Key invariant: a real WhatsApp id ALWAYS carries an `@` jid suffix, so a bare (no-`@`) id on the WhatsApp list is definitively foreign.

## The fix

**Task 1 — pure `ChatIdFormat` seam (TDD).**
- `CanonicalKey(id, channel)`: WhatsApp → verbatim (byte-identical; the suffix + number are the identity, so two distinct WA chats never collapse); Telegram → strip a trailing `@c.us`/`@g.us` so a spurious twin of a bare id collapses onto it; null/empty pass through, never throw.
- `IsForeignToChannel(id, channel)`: WhatsApp → true when the id contains NO `@` (a bled Telegram-form id); Telegram/other → false (canonicalize the twin, never drop the service dialog); null/empty → false.
- Pure seam — no `using UnityEngine`. Tests in the new `ChatListDedupTests.cs` (9) + seam tests appended to the existing (compiled) `ChatIdFormatTests.cs` (17).

**Task 2 — apply in `ParseChatsJson` + close the bleed.**
- Key `chatLookup`, the merge `TryGetValue`, the `serverIds` sweep set, and the isDeleted removal by `ChatIdFormat.CanonicalKey(chat.id, ActiveChannel)`. The two service-dialog id-forms now share one key → one row.
- Construct the surviving `ChatViewModel` with the canonical key so `vm.ChatId == the lookup key`: the bare tapi id on Telegram (correct deep-link / message-fetch / recipient id, regardless of which id-form arrived first) and byte-identical to `chat.id` on WhatsApp. Every downstream `chatLookup` consumer (`GetChat`/`SelectChat`/Dashboard/`DeleteChat`) then resolves by `vm.ChatId` with **no other call-site change**.
- **Bleed:** skip `IsForeignToChannel(chat.id, ActiveChannel)` dialogs at the loop top and in the `serverIds` build. On the WhatsApp channel this drops the bled bare `777000` from the cache load before it can spawn a row (and the file self-heals on the next successful WA sync, which overwrites the cache with clean server data). No-op on live WhatsApp data (every real id has `@`) and on Telegram (bare ids are native; the twin is merged by `CanonicalKey`, never dropped). This was the applicable branch — the bleed is a persisted foreign-form dialog in the WA cache, so the WhatsApp-side foreign-dialog rejection is the correct, always-on defence (strictly safer than a one-time heal: even a lingering stale file never displays the foreign dialog).
- `GetChat`, both `ShouldNotify` open-chat-suppression calls, `DisplayFallback`, and `IsGroup` all resolve through the canonical key (open-chat suppression now compares canonical-vs-canonical against `currentChatId`).

**WhatsApp non-regression (cited):** `CanonicalKey("<num>@c.us", ChatChannel.WhatsApp) == "<num>@c.us"` and no `@`-bearing id is ever foreign, so on the WhatsApp channel every dedup key, the VM id, the sweep set, and the foreign check are byte-identical to the previous raw-`chat.id` behaviour. Holds under 08-04's repeating open-chat live poll (dedup is at the insert/merge, which runs every cycle).

## Task Commits

1. **Task 1 (RED): failing dedup + bleed coverage** - `f379a5f` (test)
2. **Task 1 (GREEN): CanonicalKey + IsForeignToChannel seam + seam tests** - `cc04503` (feat)
3. **Task 2: canonical dedup + foreign-dialog rejection in ParseChatsJson** - `1c9d8fe` (fix)
4. **Chore: track Unity-generated ChatListDedupTests.cs.meta** - `74b12b8` (chore)

**Plan metadata:** committed with STATE/ROADMAP (docs)

## Files Created/Modified

- `Assets/Scripts/Chat/ChatIdFormat.cs` - Added `CanonicalKey(id, channel)` + `IsForeignToChannel(id, channel)` (pure, null-tolerant, channel-aware)
- `Assets/Scripts/Main/ChatManager.cs` - `ParseChatsJson` keyed by the canonical id (insert/merge/sweep/isDeleted) + VM constructed with the canonical key + foreign-dialog rejection; `GetChat` canonicalizes its lookup
- `Assets/Tests/Editor/Chat/ChatListDedupTests.cs` - NEW: 9 dedup/bleed contract tests (twin collapse, WA distinct, foreign rejection matrix)
- `Assets/Tests/Editor/Chat/ChatIdFormatTests.cs` - +17 seam tests (CanonicalKey + IsForeignToChannel)

## Decisions Made

- **`CanonicalKey` is channel-aware** (took a `ChatChannel` param rather than the plan's single-arg signature). A channel-blind pure function cannot satisfy BOTH acceptance constraints at once — keep `@c.us` verbatim for WhatsApp (`CanonicalKey("<num>@c.us") == "<num>@c.us"`) AND strip it for the Telegram `777000@c.us` twin. Channel awareness is the minimal correct design; documented inline.
- **The surviving VM carries the canonical id** (a refinement of the plan's "keep chat.id as-is"). It is byte-identical on WhatsApp and the correct bare id on Telegram, and it dissolves the merge-order ambiguity (whichever id-form arrives first, the row's id is the bare canonical form), so no downstream consumer needs canonicalizing.
- **Foreign rejection uses the `@`-presence test, not a `@c.us`/`@g.us` whitelist**, so exotic-but-genuine WhatsApp jids (broadcast/newsletter/lid) are never dropped.

## Deviations from Plan

The two design decisions above (channel-aware `CanonicalKey`; VM constructed with the canonical id) are refinements of the plan's letter, both required to satisfy the plan's own acceptance criteria (WhatsApp byte-identical AND twin-collapse). They are not scope changes — the seam, the wiring site, the bleed fix, and the file set are exactly as planned. No Rule 1–4 auto-fixes were needed.

## Issues Encountered

- **Test bridge deferred ~80s while the Editor was unfocused** (the bridge only polls when Unity has focus). The still-armed trigger was consumed once focus returned; the run then imported the new `ChatListDedupTests.cs` (via the bridge's `AssetDatabase.Refresh`), compiled, and ran green. No action needed — the trigger-armed design handled it.

## Test status

**FRESH GREEN via the in-Editor bridge: 1091/1091 EditMode passed, 0 failed** (`editorAssemblyWrittenUtc 2026-07-16T12:32:02Z`, postdating the last `.cs` edit at 12:28:40Z — not stale-green). Suite grew 1065 → 1091 (+26: 9 `ChatListDedupTests` + 17 `ChatIdFormatTests` seam tests). The Editor was open the whole time (PID lock held); the headless runner was never launched. The `ParseChatsJson` wiring itself reads private `ChatManager` state — its on-device confirmation (single TG service row, absent from WA, no real WA chat dropped) rides 08-10.

## Next Phase Readiness

- **08-10 device re-verify (D7):** confirm the service dialog is exactly ONE row on Telegram, absent from WhatsApp, and no real WhatsApp chat disappeared. **Capture the EXACT second id-form** of the service dialog during that pass — if it is a prefix form rather than a spurious `@c.us`/`@g.us` suffix, extend `CanonicalKey`'s Telegram branch (WhatsApp branch stays verbatim).
- No server/n8n changes; prod untouched.

## Self-Check: PASSED

- Files exist: `ChatIdFormat.cs`, `ChatManager.cs`, `ChatIdFormatTests.cs`, `ChatListDedupTests.cs` — all FOUND
- Commits exist: `f379a5f`, `cc04503`, `1c9d8fe`, `74b12b8` — all FOUND
- No accidental file deletions across the 3 task commits
- Grep acceptance: `CanonicalKey` present in seam (no `using UnityEngine`); `ChatListDedupTests` has 9 `[Test]` and references `c.us`; `ChatManager.cs` has 4 `CanonicalKey` sites and zero `chatLookup[chat.id]` raw keying
- No stubs introduced

---
*Phase: 08-device-uat-milestone-closeout*
*Completed: 2026-07-16*

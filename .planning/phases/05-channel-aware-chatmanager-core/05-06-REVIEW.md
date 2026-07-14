---
phase: 05-channel-aware-chatmanager-core
reviewed: 2026-07-14T06:25:28Z
depth: standard
files_reviewed: 13
files_reviewed_list:
  - Assets/Scripts/Chat/ChatIdFormat.cs
  - Assets/Scripts/Chat/NormalizedMessage.cs
  - Assets/Scripts/Chat/RawMessage.cs
  - Assets/Scripts/Chat/TelegramMediaShape.cs
  - Assets/Scripts/Chat/TelegramMediaType.cs
  - Assets/Scripts/Chat/TelegramReactionMapper.cs
  - Assets/Scripts/Chat/TelegramReactionMerge.cs
  - Assets/Scripts/Main/ChatManager.cs
  - Assets/Tests/Editor/Chat/ChatIdFormatTests.cs
  - Assets/Tests/Editor/Chat/ReplyParserTests.cs
  - Assets/Tests/Editor/Chat/TelegramMediaNormalizeTests.cs
  - Assets/Tests/Editor/Chat/TelegramMessageTypeTests.cs
  - Assets/Tests/Editor/Chat/TelegramReactionReceiveTests.cs
findings:
  critical: 0
  warning: 2
  info: 5
  total: 7
status: issues_found
---

# Phase 05-06: Code Review Report

**Reviewed:** 2026-07-14T06:25:28Z
**Depth:** standard
**Files Reviewed:** 13 (diff range `2e4e334^..c80c333`; `bb6159d` verified docs-only — touches `.planning/` + `Tools/tapi/SHAPES.md` only)
**Status:** issues_found

## Summary

Reviewed the 05-06 capture-grounded Telegram diff: download-only media Normalize (`TelegramMediaShape` + `ApplyTelegramMediaShape`), video-as-document mimetype refinement (`TelegramMediaType.Refine` via `ResolveMessageType`), receive-side reactions (`TelegramReactionMapper` + `TelegramReactionMerge` + `RefreshCachedMessageReactions` reconcile), "channel" dialog groupness (`ChatIdFormat.IsGroup`), and the Q8 reply lock. All claims were cross-checked against the binding verdicts in `Tools/tapi/SHAPES.md`.

**Verified clean (the five requested scrutiny areas):**

1. **WhatsApp byte-identical — CONFIRMED.** Every new behavior is channel-gated: the Normalize TG block (`ChatManager.cs:1558`), both reconcile call sites (`ChatManager.cs:691`, `ChatManager.cs:1259` — short-circuit `ActiveChannel == ChatChannel.Telegram &&`), and `ResolveMessageType` (`ChatManager.cs:1651-1654`, WA branch returns `ParseMessageType(raw.type)` verbatim). Zero WA media-branch lines modified in `Normalize`. WA reactions still flow exclusively through `HandleReactionEvent`/`ReactionStore` (`ChatManager.cs:615-618`, `1269-1273`); `NormalizedMessage.reactions` stays null on WA so the new `CreateViewModel` copy (`ChatManager.cs:1378`) is a null-for-null no-op. The new flat `RawMessage` fields (`mimetype`/`file_name`/`reactions`) are read only inside TG-gated code. `SetActiveChannel`'s `StopAllCoroutines` + queue clears (`ChatManager.Channel.cs:78-84`) prevent a stale channel's payload from being Normalized under the wrong gate, and `GetCacheRoot()` (`ChatManager.BotState.cs:22-31`) isolates per-bot-per-channel history so the reconcile never touches the other channel's cache.
2. **Download-only media fallback — CONFIRMED keyed by message id, not URL.** The serial queue enqueues `(messageId, callbacks)` (`ChatManager.cs:1861-1867`) and `DownloadMediaRoutine` is channel-aware via `WappiEndpoints.Sync(ActiveChannel, …)` (`ChatManager.cs:1904`). Empty-URL images take `SmartMediaRoutine`'s `hasValidUrl == false` branch → `DownloadMediaForMessage(vm.messageId, …)` (`MessageItemView.cs:2314`); audio/voice/document/sticker take `StartDownload` → same queue (`MessageItemView.cs:2081`); videos take `EnqueueIncomingVideoThumb` (url-less recovery re-fetches by id, `ChatManager.VideoThumbs.cs:259`; Editor correctly returns false → dark card, per the documented device-only extractor). Media bytes are cached by the fetched `file_link` (unique) — no empty-URL cache collision. `thumb://` keys are never minted for TG (no `JPEGThumbnail`). Residual synthetic-key risk is WR-02.
3. **Reactions reconcile — races analyzed.** Pre-echo refresh preserves the optimistic "me" entry (no wipe); echo produces no double-count; reconcile matches `cached.messageId == refreshed.id` strictly within one chat's cached list under a per-bot-per-channel root (no cross-chat/cross-channel contamination); `_reactions`/`seenMessageIds` clear per chat open (`ChatManager.cs:505-506`). Persistence works via `hasStatusUpdates`/`cacheDirty` → `SaveHistory`. But the echo transition itself destroys the owner's toggle state — WR-01 — and own-removal has a transient re-add race — IN-05.
4. **media_info handling — CONFIRMED.** Fractional duration (11.4 → 11, 31.484 → 31) rounds via `Math.Round` and is unit-tested; null/empty/partial `media_info` degrade safely (tested, incl. divide-by-zero dims). `is_round`/`isGif` deliberately ship NO claimed handling (documented Q2 disposition pending the owner media re-run) — only the defensive `audio/*`→Voice / `video/*`→Video prefix rules, which are Chat/Reaction-guarded and TG-gated so a WA document can never be reclassified.
5. **Test fixtures — CLEAN.** None of the real capture chat ids (`299050928`, `1038376805`, `8435792686`, `3825658662`, `725195588`, `6062310939`, `7279803623`, `777000`) appear anywhere under `Assets/Tests/`. A scripted cross-check of all name/username/phone values extracted from the gitignored `Tools/tapi/samples/*.json` against every committed test found only structural words ("Telegram", "bagkz") plus "Alibek" — which entered the WA reaction tests on 2026-06-17 (commit `12bb7ec`), a month before the capture, in files outside this diff: a pre-existing synthetic fixture, not a leak. New-test fixtures ("Ivan", "12345", "5127433588", "TG1") are synthetic; `79995579399@c.us` reuses the file's pre-existing WA fixture number.

Two warnings concern the reaction merge's identity handling and Telegram's short per-chat-counter message ids colliding in globally-keyed artifacts.

## Warnings

### WR-01: Echo-merge destroys the owner's own-reaction state — toggle-off and quick-bar highlight break after the first post-echo refresh

**File:** `Assets/Scripts/Chat/TelegramReactionMerge.cs:25-27` (behavior locked by `Assets/Tests/Editor/Chat/TelegramReactionReceiveTests.cs:83-92`)
**Issue:** `Merge` keeps the cached `"me"` entry only while the server list does not contain that *emoji*. When the server echoes the owner's reaction (as a `user_id`-keyed, `fromMe=false` entry — the mapper can't identify the owner, `TelegramReactionMapper.cs:43-45`), the `"me"` entry is dropped and replaced by the anonymous server entry. Consequences:

- `OutgoingReaction.CurrentMyEmoji` (`OutgoingReaction.cs:14-22`) scans for `reactorKey == "me"` → returns null → tapping the same emoji again **re-sends it instead of toggling it off** (`OutgoingReaction.Resolve` computes `toggleOff` from `CurrentMyEmoji`). The owner permanently loses the ability to remove their own reaction through the UI — every removal tap re-adds a duplicate `"me"` entry via `ReactionStore.ApplyToMessage` (`ReactionStore.cs:83-91`), double-displaying until the next refresh strips it again.
- The quick-emoji bar's selected tint (`ReactionBarController.cs:249-253`) silently un-highlights.
- Changing emoji post-echo transiently shows both the stale echoed emoji and the new `"me"` emoji until the next poll.
- A **different** user reacting with the same emoji before the echo also satisfies `ContainsEmoji` and prematurely drops the owner's not-yet-echoed reaction (brief flicker-off — the exact artifact the merge exists to prevent).

The 05-06 SUMMARY states "own-reaction highlight/toggle relies on the optimistic send path," but the merge erases that path's state on the first echo — the design contradicts its own premise. `Merge_ServerEchoesMyEmoji_NoDuplicate` asserts the current (broken-for-toggle) behavior, so tests pass while the UX regresses.
**Fix:** Preserve identity instead of dropping it. Two options, strongest first:

1. **Learn the owner's Telegram user id** — SHAPES.md Q4 confirms every `fromMe:true` tapi row carries `from` = own profile-user id, and `RawMessage.from` already exists. Capture it opportunistically in the TG Normalize gate (`if (raw.fromMe && !string.IsNullOrEmpty(raw.from)) _tgOwnUserId = raw.from;`, reset on bot/channel switch), pass it to `TelegramReactionMapper.Map(reactions, ownUserId)`, and map the matching element to `reactorKey = OutgoingReaction.MeReactorKey, fromMe = true`. The echo then lands *as* "me" — toggle, highlight, and removal all keep working with no merge heuristics.
2. **Identity adoption in Merge** (no id learning): when `mine != null` and the server list contains `mine.emoji`, replace that server element with `mine` instead of dropping `mine`:
```csharp
MessageReaction mine = FindMine(cached);
if (mine != null && !string.IsNullOrEmpty(mine.emoji))
{
    int echo = IndexOfEmoji(result, mine.emoji);
    if (echo >= 0) result[echo] = mine;        // adopt the echo as "me" — toggle survives
    else result.Add(mine);                      // not yet echoed — keep optimistic entry
}
```
Either way, add a grace window so remote removals propagate: optimistic entries carry `time` (unix seconds, set by `OutgoingReaction.Resolve`; mapped entries carry 0) — stop preserving an un-echoed `"me"` after ~90s so a reaction the owner removed from the phone's Telegram app doesn't stay pinned in the cache forever. Update `Merge_ServerEchoesMyEmoji_NoDuplicate` to assert the kept `reactorKey == "me"` and add a toggle-after-echo regression test through `OutgoingReaction.CurrentMyEmoji`.

### WR-02: Telegram message ids are 1-5 digit counters, but `vthumb://` cache keys (and the by-id server fetches the download-only design depends on) assume globally unique ids

**File:** `Assets/Scripts/Main/ChatManager.cs:1656-1662` (`ApplyTelegramMediaShape` download-only contract) + `Assets/Scripts/Main/ChatManager.VideoThumbs.cs:112` (pre-existing key mint, newly reachable for TG videos via this diff's `TelegramMediaType` refinement)
**Issue:** The capture samples show all 401 tapi message ids are bare digits of length 1-5 (Telegram sequence counters: private-chat ids are per-*account* sequences, channel/supergroup ids per-channel — so ids repeat across different Telegram accounts, and channel ids can repeat within one account). This diff makes TG videos real render targets (video-as-document → `MessageType.Video`) whose entire byte path is keyed by that bare id:

- `vthumb://{messageId}` lands in the **global** `MediaCacheManager` (single disk cache shared across all bots and channels). Two TG bots (different accounts) — or a channel post and a private chat in one account — holding a video at the same numeric id collide: `EnqueueIncomingVideoThumb`'s durable de-dup (`ChatManager.VideoThumbs.cs:115-123`) adopts the *other* message's cached frame and paints the wrong video preview, silently and persistently. WhatsApp never hits this (long unique stanza ids), which is why the global key was safe until now.
- `message/media/download?profile_id=…&message_id=…` and `messages/id/get` carry no chat id. Within one profile, a channel/supergroup id colliding with a private-chat id makes the server-side resolution ambiguous — whether wappi disambiguates internally is **unverified** (the capture's single `messages/id/get` success doesn't cover a collision).
**Fix:** (1) Namespace the synthetic key for Telegram only, preserving WA cache continuity: in `EnqueueIncomingVideoThumb`/`RunVideoThumbExtraction`, mint `"vthumb://tg/" + profileId + "/" + vm.chatId + "/" + vm.messageId` when `ActiveChannel == ChatChannel.Telegram` (the VM carries `chatId`). (2) Add an explicit verification item to the Phase 8 device-UAT / e2e list: send videos into a TG channel and a private chat with overlapping ids on the dev profile and confirm `message/media/download` returns the right bytes; if it doesn't, check whether tapi accepts a `chat_id` parameter on the download endpoint.

## Info

### IN-01: Duration rounding is banker's rounding, but the test is named "RoundsHalfUp"

**File:** `Assets/Scripts/Chat/TelegramMediaShape.cs:53`; `Assets/Tests/Editor/Chat/TelegramMediaNormalizeTests.cs:43-49`
**Issue:** `Math.Round(double)` defaults to `MidpointRounding.ToEven` — 11.5 → 12 but 12.5 → 12. The test name claims half-up; its cases (31.484, 40.6) never exercise a midpoint, so the mismatch is invisible.
**Fix:** Either `Math.Round(seconds, MidpointRounding.AwayFromZero)` to match the name, or rename the test `Resolve_Duration_RoundsToNearest`. Display-only (±0.5 s), pick either.

### IN-02: Caption==fileName ZWS-aware dedup now exists in three copies, and the new copy embeds a literal invisible U+200B in source

**File:** `Assets/Scripts/Main/ChatManager.cs` (`ApplyTelegramMediaShape` document-dedup block, third copy) vs `ChatManager.cs:1536-1545` (WA Normalize) and `Assets/Scripts/UI/MessageItemView.cs:957-960` (stale-cache guard)
**Issue:** The trim-chars array embeds a raw, invisible zero-width space (U+200B) as its first char literal — the array reads as five elements but the first one cannot be seen or visually reviewed (this REVIEW file deliberately does NOT reproduce the raw character; only its escape spelling appears below). That pattern is now duplicated a third time, and the invisible codepoint is easy to lose to editor/linter normalization.
**Fix:** Extract a small pure helper (e.g. `CaptionFileNameDedup.Matches(text, fileName)` in `Assets/Scripts/Chat/`), spell the character with the visible escape sequence backslash-u200B (i.e. the seven ASCII characters '\u200B' as the char literal), use the helper in all three call sites, and unit-test it. At minimum, replace the raw literal with the '\u200B' escape in the new block.

### IN-03: Snapshot-resolved quoted card of a TG video-as-document renders as Document

**File:** `Assets/Scripts/Main/ChatManager.cs:1568-1571` (`ReplyParser.Resolve(raw, FindActiveById, ParseMessageType)`)
**Issue:** The reply quote path still receives the unrefined `ParseMessageType`, so a reply quoting a phone-sent video (tapi `type:"document"` + `mimetype:"video/mp4"`) gets `quotedType = Document` when resolved from the snapshot. When the original is in the loaded window, `FindActiveById` returns the already-refined VM and the card is correct — only snapshot/recovery resolution mismatches (document icon instead of a video chip).
**Fix:** Low priority. If the tapi `reply_message` snapshot carries `mimetype` (check on the owner's media re-run), pass a channel-aware refiner: a lambda that applies `TelegramMediaType.Refine` on the snapshot's mimetype for TG. Otherwise document as a known cosmetic limit.

### IN-04: TG receive-side reactions never update the chat-list preview row (WhatsApp parity gap)

**File:** `Assets/Scripts/Main/ChatManager.cs:1707-1724` (`RefreshCachedMessageReactions`) vs `ChatManager.cs:1622-1625` (WA live path calls `UpdateChatListPreviewForReaction`)
**Issue:** WA live reaction rows update the chat-list row ("X reacted ... to ..."); the TG reconcile only fires `OnMessageReactionsChanged` for the open chat's bubbles. This is largely inherent to the poll transport — tapi reactions carry no timestamp (`time = 0`), so the WA path's `ev.time < chatVm.LastMessageTime` newest-activity guard could not work anyway — but it is an undocumented behavior difference the channel switcher makes visible.
**Fix:** No code change required for v1; record it in the phase docs/UAT expectations so a device tester doesn't file it as a bug. If parity is wanted later, the reconcile could update the row only when the reacted message is the chat's newest.

### IN-05: Own-reaction removal has a transient re-add race against an in-flight refresh

**File:** `Assets/Scripts/Chat/TelegramReactionMerge.cs:21-30` + `Assets/Scripts/Main/ChatManager.ReactionSend.cs:40-49`
**Issue:** Remove own reaction in-app (optimistic removal deletes the `"me"` entry, POST body `""`) while a `messages/get` snapshot taken *before* the server processed the removal is in flight → the merge re-adds the owner's reaction from the stale server list (as an anonymous `user_id` entry: cached has no `"me"` to protect, server list wins) → the pill flickers back on, then clears on the next poll. With no per-reaction timestamps on tapi this is not fully solvable client-side.
**Fix:** Acceptable v1 transient (self-heals within one refresh cycle) — document it. A cheap mitigation if it proves visible on device: after a successful reaction POST, suppress reaction reconcile for that message id for one refresh cycle (~5-10 s), mirroring the `seenMessageIds.Add(response.message_id)` echo-suppression already done at `ChatManager.ReactionSend.cs:98`.

---

_Reviewed: 2026-07-14T06:25:28Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_

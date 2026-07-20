---
phase: 08-device-uat-milestone-closeout
reviewed: 2026-07-20T20:12:42Z
depth: deep
files_reviewed: 7
files_reviewed_list:
  - Assets/Scripts/Chat/TelegramReactionMerge.cs
  - Assets/Scripts/Chat/TelegramReactionMapper.cs
  - Assets/Scripts/Chat/ReactionStore.cs
  - Assets/Scripts/Chat/ReactionEmoji.cs
  - Assets/Scripts/Chat/ReactionParser.cs
  - Assets/Scripts/Main/ChatManager.cs
  - Assets/Scripts/Main/Manager.cs
findings:
  critical: 1
  warning: 2
  info: 3
  total: 6
status: issues_found
---

# Phase 08: Code Review Report — Round-5 Diagnostic (D2-view / D15 / D17)

**Reviewed:** 2026-07-20T20:12:42Z
**Depth:** deep
**Files Reviewed:** 7
**Status:** issues_found

## Summary

Round-5 device/Editor evidence relocated D2-view upstream of the view layer (round-4 review preserved at `bd6c9be`). This review ground-truths the reaction DATA layer and reaches three verdicts:

1. **D2-view (CR-01, CRITICAL): hypothesis CONFIRMED as the only code path.** The optimistic-grace window in `TelegramReactionMerge.Merge` is the sole mechanism in the codebase that can produce "echo Normalized, zero `OnMessageReactionsChanged`" for a *different* own emoji. The defect is precise: the grace is keyed on **identity + tap-time only** and is **never cleared by server confirmation** — `result[serverMine] = mine` (TelegramReactionMerge.cs:62) carries the same fresh `mine` object forward on every ~3 s poll, so even after the server has echoed the optimistic emoji (grace's job done), any *genuinely new* own-reaction change made in the Telegram app is still discarded as a "stale echo" for the remainder of the 90 s window. All rival suppressor candidates are refuted with line evidence below. Fix = confirmation-clears-grace; fully EditMode-reproducible.
2. **D15 (WhatsApp removal): candidate-(b) confirmed against the models — and the 08-27 candidate-(a) re-process is not merely inert, it is a live regression risk (WR-02).** The captured evidence (add raw re-delivers every poll, `seen=True`, no empty-body raw ever) means the 08-27 already-seen re-process (ChatManager.cs:734-745) re-applies stale reaction history every poll: an own WA reaction removed or changed *in our app* is resurrected/reverted by the still-re-delivering original add raw within one poll. Recommend REVERT of the re-process + a diagnosis-first probe for absence state (IN-01).
3. **D17 (INFO): exact mirror site confirmed** — Manager.cs:1690-1719, sibling of the 08-28 Telegram stamp; the WA settings-reauth path provably reaches it through the single `GetWhatsappProfileStatus` poller (IN-02).

Suite baseline 1181/1181; every finding carries an EditMode-testability note.

## Critical Issues

### CR-01: D2-view root cause — optimistic grace is never ended by server confirmation, so external own-reaction changes are suppressed for up to 90 s

**File:** `Assets/Scripts/Chat/TelegramReactionMerge.cs:45,60-63,133-134` (suppressor) · `Assets/Scripts/Main/ChatManager.cs:1875-1881` (where the erased diff kills the event) · `Assets/Scripts/Main/ChatManager.ReactionSend.cs:39-45,52-57,132-145` (the only sources of a fresh `me` entry)

**Issue — the exact death path of a '😁'-after-'👍' own-user raw:**

1. The target message's raw (carrying `reactions:[{reaction:"😁", user_id:<own>}]`) is already in `seenMessageIds`, so it takes the already-seen branch of `SyncLatestMessages` → `Normalize(raw)` at ChatManager.cs:754 (this is where the `[TG reaction echo]` line prints — Editor-only, ChatManager.cs:1671, 1703-1714). The two other reconcile sites (ChatManager.cs:1332-1348 page-seen; :1219-1247 validate pass) share the identical downstream.
2. `TelegramReactionMapper.Map` maps the echo to `reactorKey="me"` (`user_id == _tgOwnUserId`, TelegramReactionMapper.cs:47,56) with `time = 0` (line 60) — mapper output is never "fresh".
3. `RefreshCachedMessageReactions` (ChatManager.cs:1866) calls `TelegramReactionMerge.Merge(cached, server, now)` at :1875.
4. **Death line:** if the cached `me` entry carries `time > 0` within 90 s (`IsFreshOptimistic`, TelegramReactionMerge.cs:133-134), the non-empty-mine branch executes `result[serverMine] = mine` (**TelegramReactionMerge.cs:62**) — the server's '😁' is overwritten by the local '👍' *without ever checking whether the server emoji matches the optimistic one*.
5. The merged list is now (reactor, emoji)-identical to the cached list, so `SameReactions` returns true and ChatManager.cs:1877 `return false` — **no `OnMessageReactionsChanged`, no `[D2-view]` line**. Exactly the captured signature: echo-without-event.
6. **The suppression self-perpetuates:** line 62 (and the fold at :74) put the *same `mine` object* — original tap time intact — into the merged list, which becomes `cached.reactions` at :1879. Every subsequent poll re-merges from that same fresh entry until 90 s after the ORIGINAL tap. A successful echo never consumes the grace. This retroactively explains rounds 2-5 intermittency: external changes < 90 s after an in-app tap fail; > 90 s pass.

**Precondition + rival suppressors refuted:**
- A fresh (`time > 0`) `me` entry is created ONLY by the in-app paths: optimistic apply (`ReactionStore.ApplyToMessage` with `ev.time = now`, ChatManager.ReactionSend.cs:39-45; the change path re-stamps `existing.time = ev.time`, ReactionStore.cs:76-77), the removal tombstone (ReactionSend.cs:52-57 → TelegramReactionMerge.cs:91-112), and the failed-POST revert (ReactionSend.cs:132-145). Mapper entries are always `time = 0`. So the observed suppression requires an own in-app tap (or removal, or failed POST) on message 23475 within the preceding 90 s — consistent with the owner's documented repro habit (round-3 verbatim: changing reactions on one bubble then another, in-app and in the TG app interleaved).
- **`ReactionEmoji.CompareKey` collision — refuted:** U+1F44D / U+1F601 / U+1F44C carry no U+FE0F; keys are distinct (ReactionEmoji.cs:48-53). `SameReactions([me/👍],[me/😁])` = false (TelegramReactionMerge.cs:116-128,173) — the dedup cannot eat a genuine emoji change on its own.
- **`SameReactions` upstream dedup — refuted as an independent suppressor:** it is (reactorKey, emoji)-sensitive and time/name-insensitive; it can only return true after Merge has already erased the difference. It is the messenger, not the killer.
- **Absence-semantics null-ing — refuted:** `Merge` returns null only for an empty result (TelegramReactionMerge.cs:80); a 1-element differing server list never nulls.
- **View layer — exonerated by code:** the `[D2-view] reactions changed` log is synchronous inside the event handler (MessageItemView.cs:4654-4661), so "no [D2-view] line" ⇒ the event genuinely never fired. Note this also means the 00:23:58 echo and the 00:24:00 event are two separate poll/interaction passes; the exact session choreography (which in-app tap armed the grace) is under-determined by the transcript — the diagnostic log below closes that gap if any residual survives the fix.

**Fix (round-6 spec, one site):**

Grace must END on server confirmation, in `TelegramReactionMerge.Merge`:

```csharp
else if (serverMine >= 0)
{
    // Echo CONFIRMED the optimistic emoji => the grace has done its job. Keep the
    // server's entry (time=0, still reactorKey "me" => stays toggleable via
    // OutgoingReaction.CurrentMyEmoji). The NEXT differing echo is then a genuine
    // external own-change and applies immediately.
    if (!ReactionEmoji.SameEmoji(result[serverMine].emoji, mine.emoji))
        result[serverMine] = mine;   // differing echo DURING the window: stale-echo suppress (original D2 defense, unchanged)
    // else: adopt the server element as-is — freshness is consumed.
}
```

Equivalently: "clear grace when the server emoji EQUALS the locally-set one." Do NOT clear on *differ* — a differing echo within the window is exactly the stale-old-emoji echo the grace exists to suppress (clearing on differ regresses the original D2 flicker). The known residual edge (external change made before the in-app tap's echo ever lands, so confirmation never occurs) keeps the 90 s worst case; acceptable and vastly narrower than today. Apply the same confirmation rule to the fold branch (:72-76): a folded same-emoji echo is also a confirmation — fold by ADOPTING the server element (re-keyed to "me") rather than replacing it with `mine`. Additionally add a one-line `#if UNITY_EDITOR` suppression log at the :62 replace when the emojis differ (`[D2-merge] suppressed server me '<hex>' by fresh local '<hex>' age=<s>`) so the next UAT pass captures ground truth for any residual.

**EditMode repro (echo-without-event, red today / green after fix):**

```csharp
long t0 = 1_000_000;
var cached = new List<MessageReaction> {            // in-app tap '👍' (optimistic, fresh)
    new MessageReaction { emoji="👍", reactorKey="me", fromMe=true, time=t0 } };
var echo1 = new List<MessageReaction> {             // tapi echoes it (mapper shape: time=0)
    new MessageReaction { emoji="👍", reactorKey="me", fromMe=true, time=0 } };
var m1 = TelegramReactionMerge.Merge(cached, echo1, t0 + 3);   // echo lands => grace must end
var echo2 = new List<MessageReaction> {             // owner changes to '😁' IN the Telegram app
    new MessageReaction { emoji="😁", reactorKey="me", fromMe=true, time=0 } };
var m2 = TelegramReactionMerge.Merge(m1, echo2, t0 + 8);
Assert.IsFalse(TelegramReactionMerge.SameReactions(m1, m2));   // FAILS today (suppressed) — the defect, distilled
Assert.AreEqual("😁", m2[0].emoji);
```

Pure/static seam — no scene, no MonoBehaviour, mirrors the existing T-08-11/T-08-17 merge tests.

## Warnings

### WR-01: Tombstone mirror of CR-01 — an external own re-add within 90 s of an in-app removal is suppressed, and the tombstone is never dropped on confirmed absence

**File:** `Assets/Scripts/Chat/TelegramReactionMerge.cs:48-59`
**Issue:** The fresh empty-emoji tombstone branch unconditionally removes any server `me` element (`result.RemoveAt(serverMine)`, :57) and re-adds the tombstone (:58). Two consequences: (a) if the owner removes in-app and then re-adds a reaction IN the Telegram app within 90 s, the re-add's echo maps to `me` and is deleted every poll — same suppression family as CR-01; (b) once a poll shows NO server `me` (removal confirmed server-side — the moment the 08-REVIEW WR-03 stale-echo risk has passed), the tombstone is still carried instead of being consumed, keeping window (a) open for the full 90 s.
**Fix:** Mirror the CR-01 confirmation rule: when `serverMine < 0` (server confirms the absence), DROP the tombstone instead of `result.Add(mine)` — the WR-03 resurrect scenario would need a stale echo *after* an observed absence, which is pathological at a 3 s poll cadence. When `serverMine >= 0`, keep today's suppress-and-carry. EditMode: `StampRemovalTombstone` → `Merge(cached, server:null, now+3)` must return null (today returns `[tombstone]`) → follow-up `Merge(null, [me/❤/t0], now+6)` applies the external re-add. If round 6 wants minimal blast radius, ship CR-01 alone and pin this as a known-edge test; but both fixes live in the same 40-line function.

### WR-02: 08-27 candidate-(a) re-process replays stale WhatsApp reaction history every poll — an own reaction removed/changed in-app is resurrected by the re-delivered add raw

**File:** `Assets/Scripts/Main/ChatManager.cs:734-745` (re-process) · `Assets/Scripts/Chat/ReactionStore.cs:63-92` (reducer that faithfully re-applies)
**Issue:** The round-5 evidence that answered D15 also invalidates 08-27's safety argument. The captured logs prove a WhatsApp add raw (`3A8976F33979EE5EE8EB`, `bodyEmpty=False`) **keeps re-delivering every poll with `seen=True` even after the reaction was removed** — Wappi's message log retains stale reaction stanzas. The 08-27 change makes every seen WA `type=="reaction"` raw re-run `HandleReactionEvent` each poll (ChatManager.cs:741-743). `ReactionStore.ApplyToMessage` is idempotent only against *unchanged* state:
- **Resurrect:** owner has a `me` reaction (e.g. reacted from the WhatsApp phone app — applied at first delivery, `fromMe=true` ⇒ `reactorKey="me"`, ReactionParser.cs:49-55), then toggles it OFF in our app (`SendReaction` → removal, pill clears, POST `""` succeeds). Next poll ≤3 s later: the persisted add raw re-processes → `FindIndex("me")` misses → **re-adds the removed reaction** (ReactionStore.cs:83-92) → event fires → pill resurrects. The 08-27 pin (`96da6f4`) covers re-delivered *removals* being no-ops — not re-delivered *adds after a local removal*, which is exactly this hole.
- **Revert-to-old:** an own (or contact's) reaction *changed*: if Wappi retains both stanzas in the window, `response.messages` is newest-first (ReactionStore.cs:4-6 doc; merge order at ChatManager.cs:830-831), so the OLD raw is processed LAST each poll and wins — the pill flip-flops and settles on the stale emoji, firing two change events per poll (the ":737-739 cannot-storm" claim does not hold for multi-stanza histories).
The intended target (same-id empty-body removal re-emit) is REFUTED by the same round-5 evidence — the re-process is inert for its purpose and harmful outside it. The chat-list row is shielded (stale `ev.time < LastMessageTime` gate, ChatManager.cs:1787), so damage is contained to the pill + persisted cache.
**Fix:** Revert ChatManager.cs:734-745 (restore the pre-08-27 seen-guard skip; keep the `[D15]` shape log at :661-662 until D15 closes per IN-01). EditMode pin of the hole the reducer cannot guard: `ApplyToMessage(add) → ApplyToMessage(me-removal) → ApplyToMessage(same add ev)` returns true and re-adds — documenting that replay protection MUST live at the caller (the seen-guard), not in the reducer. Residual caveat: the resurrect leg assumes the add raw persists after an *API* removal as it provably does after an in-WhatsApp removal (same server-side end state); the revert is correct under either answer.

## Info

### IN-01: D15 — WhatsApp reaction-removal ingest: assessment of the absence-based options + ONE recommendation

**File:** `Assets/Scripts/Chat/RawMessage.cs:47-51` · `Assets/Scripts/Main/ChatManager.cs:1674-1677` · `Assets/Scripts/Main/ChatManager.QuoteResolve.cs:117,136-146`
**Assessment:**
1. **`reactions[]` on WA target rows — no code/model evidence it exists.** `RawMessage.reactions` (`JToken`, `[JsonProperty("reactions")]`) would already capture such a field channel-agnostically, and the model comment (from the Tools/tapi/SHAPES.md Q3 divergence capture: state-on-target is *tapi-only*; WA is event-rows) plus `Normalize` mapping reactions ONLY in the Telegram branch (ChatManager.cs:1677) say WA targets don't carry it. `chats/filter` rows (`ChatDialog`) have no reactions field either. However, nobody has positively probed a WA payload for the key — and the Editor already dumps every `messages/get` response to `persistentDataPath/response.txt` (ChatManager.cs:622-628), so this is checkable with ZERO new code.
2. **`messages/id/get` per-target absence probe — seam exists, payload unverified.** The serial-queue pattern (one in-flight, `WaitForChatFetchesToDrain`, TTL cache) is proven at ChatManager.QuoteResolve.cs:103-180, and the response `message` JObject is parsed raw so a reactions key would be visible. But the documented response shape lists no reaction state, and building a per-reacted-target polling subsystem on an unverified payload is the wrong order.
3. **Platform limit** — fully consistent with candidate-(b) (no removal raw ever arrives) and WA's event-only transport; precedent is the documented `chat/delete` `isDeleted` quirks.
**Recommendation (ONE approach for round 6): diagnosis-first, then document.** Step 1 (zero/low code): owner checks an Editor `response.txt` for a WA chat containing a reacted message for any `"reactions"` key on target rows, plus a one-shot Editor-only `messages/id/get` on the reacted TARGET (reuse the QuoteResolve request shape at :117; log only whether reaction-state keys exist — mirrors the `[D15]` log discipline). Step 2: if (as the models predict) both come back empty → **document as a Wappi WA platform limit** in CLAUDE.md's Wappi section (next to chat/delete), close D15 as platform-limited, remove the `[D15]` diagnostic log at ChatManager.cs:661-662, and ship the WR-02 revert. If a reaction-state field DOES surface, round 7 builds the absence reconcile on the QuoteResolve throttle pattern with facts in hand. EditMode: the probe itself is network-bound (not unit-testable); any subsequent reconcile seam should be built pure like `TelegramReactionMerge`.

### IN-02: D17 — exact mirror site for the late-WhatsApp-auth `{bot}WhatsappSyncUntil` stamp

**File:** `Assets/Scripts/Main/Manager.cs:1690-1719`
**Issue:** The 08-28 Telegram stamp lives in `ShowAuthSuccess`'s settings-reauth branch (`else if (!isCreatingBot && Manager.openBot != null)`, :1690), gated `if (authPage == TelegramAuth)` (:1703-1710). The D17 mirror is a sibling `else if (authPage == WhatsappAuth)` in the same branch stamping `Manager.openBot.name + "WhatsappSyncUntil"` with the same `ChatManager.WhatsAppSyncWindowSeconds` window. The PARITY DECISION comment at :1699-1702 is superseded by the owner's round-5 scope override and must be rewritten with the D17 rationale.
**Gotchas verified:**
- The WA settings-reauth DOES route through `ShowAuthSuccess` the same way as Telegram: `GetWhatsappProfileStatus` (Manager.cs:2220-2267) is the single WA auth-completion poller — it polls `get/status` every 5 s while `WhatsappAuth` is active, covering BOTH the QR flow and the pairing-code flow (neither has a separate success site), and calls `ShowAuthSuccess(WhatsappAuth, WhatsappAuthSuccessPanel)` at :2259. In settings reauth `isCreatingBot == false` ⇒ `moreAuthSteps` (:1662) is false ⇒ the :1690 branch is reached. The legacy Green API auth endpoints do not gate this panel.
- Wizard flows need no change: WA-only creation stamps at Manager.cs:1478-1485; the WhatsApp leg of a "both" creation takes the `moreAuthSteps` branch (no stamp — correct, the wizard tail stamps).
- Key contract already exists on both sides: suffix constant `"WhatsappSyncUntil"` = `ChatManager.BotState.cs:216` (`SyncUntilSuffixFor(WhatsApp)`, :226-227), and `Bot.DeleteBot` already clears it (Bot.cs:203) — verified, no cleanup work needed.
**Fix suggestion:** ~8 lines mirroring :1703-1710. The key contract is already pinned in EditMode via `SyncUntilSuffixFor` + `IsSyncingRawValue`; device-verify like D16 (which passed round 5 first try on the identical pattern).

### IN-03: Editor per-poll `response.txt` dump + "Saved to:" log contaminate the very UAT console captures used for diagnosis

**File:** `Assets/Scripts/Main/ChatManager.cs:622-628,1283-1290` · `Assets/Scripts/Main/ChatManager.cs:661-662`
**Issue:** Both `SyncLatestMessages` and `GetMessagesRoutine` unconditionally overwrite `persistentDataPath/response.txt` and log "Saved to: …" on every request in the Editor — at the ~3 s live-poll cadence this floods the Console that rounds 4/5 screenshot for evidence, and `GetMessagesRoutine` writes BEFORE checking `www.result` (:1283-1290 precede the result check at :1292), so a failure body can clobber a useful prior capture. Separately, the `[D15]` log at :661-662 is NOT `#if UNITY_EDITOR` (deliberate for the device pass) — schedule its removal with D15's closure (IN-01) so it doesn't ship past v1.1.
**Fix:** Gate both dumps behind a single `const bool` diagnostic flag (or delete the `GetMessagesRoutine` copy — the sync dump supersedes it), and move the write after the result check. Editor-only, zero device impact; no test needed.

---

_Reviewed: 2026-07-20T20:12:42Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: deep_

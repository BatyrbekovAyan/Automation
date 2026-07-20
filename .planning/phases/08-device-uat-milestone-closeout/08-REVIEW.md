---
phase: 08-device-uat-milestone-closeout
reviewed: 2026-07-20T10:37:36Z
depth: standard
files_reviewed: 10
files_reviewed_list:
  - Assets/Scripts/Chat/TelegramReactionMerge.cs
  - Assets/Scripts/Main/Bot.cs
  - Assets/Scripts/Main/ChatManager.BotState.cs
  - Assets/Scripts/Main/ChatManager.LivePoll.cs
  - Assets/Scripts/Main/ChatManager.cs
  - Assets/Scripts/Main/Manager.cs
  - Assets/Scripts/UI/EmptyStateView.cs
  - Assets/Scripts/UI/SyncingView.cs
  - Assets/Tests/Editor/Chat/TelegramReactionMergeTests.cs
  - Assets/Tests/Editor/Chat/WhatsAppSyncTests.cs
findings:
  critical: 1
  warning: 3
  info: 3
  total: 7
status: issues_found
---

# Phase 8: Code Review Report — Round 3 (gap-closure delta `c78ac99^..5185620`)

**Reviewed:** 2026-07-20T10:37:36Z
**Depth:** standard
**Files Reviewed:** 10
**Status:** issues_found

## Summary

Round-3 gap-closure code only (plans 08-17..08-20, commits `c78ac99..5185620`); rounds 1–2 were previously reviewed and fixed (see 08-REVIEW-FIX.md) and are not re-litigated here. This review was run with two device-confirmed residual defects as priority lenses (D2-view, D12-ext), and **a precise code mechanism was found for both**:

- **D12-ext (CR-01):** after a channel-chip switch with zero bots, `BeginLoadForActiveBot` fires the *wrong* empty-state reason in the same synchronous stack that follows the round-3 `HandleActiveChannelChanged` re-wire, silently re-binding the CTA to `OpenCurrentBotAuth` — which early-returns with no bot. The CTA is dead on both channels, and the round-3 `[D12]` instrumentation never fires because the button is no longer wired to `OpenCreateBotFlow`. This exactly reproduces the device evidence ("inert, no log").
- **D2-view (WR-01):** the emoji-tap path re-renders the reaction pill while the bubble row sits under the reaction bar's temporary `overrideSorting` Canvas that is destroyed in the same frame; a render lost in that Canvas-teardown handoff is then **never self-healed**, because every subsequent reconcile is deliberately swallowed by the `SameReactions` dedup guard (the data is already correct — only the visual is stale). This matches "data layer provably correct, bubble sometimes stale, involves a second bubble's reaction bar" and the intermittent character of the repro.

The rest of the round-3 delta is solid: the D2-ext wider reconcile pass is correctly serialized behind `_chatFetchesInFlight`, Telegram-gated (WhatsApp byte-identical), and its cursor/throttle reset at chat-open (`ChatManager.cs:550-553`); the per-channel sync window (08-19) mirrors WhatsApp exactly with fail-safe parsing pinned by tests; the pill removal (08-20) left `_chatListSyncing` correctly written/reset at all three force-stop sites (`SetActiveBot` @ BotState.cs:130, `SetActiveChannel` @ Channel.cs:88, `ClearAllLocalHistory` @ PrivacyClear.cs:83 — verified); `Bot.DeleteBot` (Bot.cs:204) and `Manager` (Manager.cs:1490-1497, stamped before `SetActiveBot`) correctly delete/stamp the `TelegramSyncUntil` sibling key. New EditMode tests pin the right contracts (ReactionReconcileWindow math, per-channel key suffixes, RU countdown buckets, WA byte-identical delegation).

## Critical Issues

### CR-01: [D12-ext mechanism] Channel switch with zero bots re-wires the create-bot CTA to a silent no-op handler

**File:** `Assets/Scripts/UI/EmptyStateView.cs:203-208` (round-3 handler), `Assets/Scripts/UI/EmptyStateView.cs:226-250` (re-wire to `OpenCurrentBotAuth`), `Assets/Scripts/UI/EmptyStateView.cs:305-307` (silent early-return), `Assets/Scripts/Main/ChatManager.BotState.cs:296-300` (wrong-reason source)

**Issue:** The exact event chain on a WhatsApp↔Telegram chip switch with zero bots (chips are deliberately tappable while unconnected — SWITCH-02, `ChannelSwitcherView.cs:92-95`):

1. `SetActiveChannel` (`ChatManager.Channel.cs:84`) fires `OnActiveChannelChanged` → round-3 `HandleActiveChannelChanged` re-runs `ConfigureForReason(NoBotsExist)` — CTA correctly wired to `OpenCreateBotFlow`. So far so good.
2. `SetActiveChannel` then calls `BeginLoadForActiveBot()` (`ChatManager.Channel.cs:105`) **in the same synchronous stack**. With zero bots, `FindBotByName("_default")` is null, so `ChatManager.BotState.cs:299` fires `OnEmptyState(NoConnectionEmptyState())` = `BotHasNoWhatsApp`/`BotHasNoTelegram` — **not** `NoBotsExist`.
3. `HandleEmptyState` sees a *different* reason (`NoBotsExist` → `BotHasNoTelegram`), so the `_lastReason` guard does NOT swallow it: the card is re-configured to «Подключить Telegram» and the button re-wired to `OpenCurrentBotAuth` (`EmptyStateView.cs:248`).
4. `OpenCurrentBotAuth` resolves `FindBotByName(CurrentBotId)` → null → `return;` (`EmptyStateView.cs:306-307`) — **no log, no effect. The CTA is inert.** Switching back to WhatsApp repeats the chain with `BotHasNoWhatsApp` → dead on BOTH channels, exactly as reproduced on device.

This also explains why the round-3 fixes never engaged on device: the `[D12]` ENTRY log (`EmptyStateView.cs:265`) and the `AddBotPanel.Open()` guarantee (`EmptyStateView.cs:286-287`) both live in `OpenCreateBotFlow`, which is no longer the wired handler after step 3. The round-3 comment at `EmptyStateView.cs:194` ("SetActiveChannel DOES re-fire OnEmptyState(NoBotsExist)") states a false premise — it re-fires the *no-connection* reason. The view heals only on tab exit/re-entry (`OnDisable` nulls `_lastReason`; `OnEnable` catch-up uses `ComputeCurrentEmptyState`, which resolves `NoBots` correctly via `ChannelTabStateResolver`) — consistent with "works initially, dies after a chip switch".

Note: the underlying `BeginLoadForActiveBot` zero-bots reason bug is documented as out-of-scope for this phase (see IN-01) — but the round-3 handler can and should defend against it locally.

**Fix:** Make `EmptyStateView` derive the reason from the authoritative resolver instead of trusting the raw event when bots don't exist — closes D12-ext without touching the out-of-scope `BeginLoadForActiveBot`:

```csharp
private void HandleEmptyState(EmptyStateReason reason)
{
    // Defend against the known zero-bots reason bug: BeginLoadForActiveBot fires
    // BotHasNo{Channel} when NO bots exist. NoBots must win (ChannelTabStateResolver
    // precedence), or the CTA gets re-wired to OpenCurrentBotAuth, which no-ops
    // with no bot — the D12-ext dead button.
    if (reason != EmptyStateReason.NoBotsExist && ChatManager.Instance != null)
    {
        EmptyStateReason? resolved = ChatManager.Instance.ComputeCurrentEmptyState();
        if (resolved == EmptyStateReason.NoBotsExist) reason = EmptyStateReason.NoBotsExist;
    }

    if (_lastReason == reason) return;
    _lastReason = reason;
    ConfigureForReason(reason);
    Show();
}
```

Additionally, `OpenCurrentBotAuth`'s two early-returns (`EmptyStateView.cs:305-307`) should `Debug.LogWarning` — a primary CTA must never fail silently; the silent return is what made D12 read as "nothing happens" through two diagnosis rounds.

## Warnings

### WR-01: [D2-view candidate mechanism] Pill re-render can be lost under the reaction bar's doomed lifted Canvas, and the SameReactions dedup guard then prevents self-healing forever

**File:** `Assets/Scripts/Chat/ReactionBarController.cs:215-219` (tap path), `Assets/Scripts/Chat/ReactionBarController.cs:168-183` (deferred Canvas destroy), `Assets/Scripts/UI/MessageItemView.cs:4643-4653` (handler), `Assets/Scripts/Main/ChatManager.cs:1853-1859` (dedup guard)

**Issue:** Two halves combine into "data right, visual stale":

*Half 1 — the lost render.* `OnEmojiTapped` → `SendReaction` fires `OnMessageReactionsChanged` **synchronously** (`ChatManager.ReactionSend.cs:61`); the bubble's `HandleReactionsChanged` sets the pill's TMP text and runs `ForceRebuildLayoutImmediate` while the row still carries the temporary `overrideSorting` Canvas added by `LiftRow` (`ReactionBarController.cs:153-166`). `Hide()` then runs `UnliftRow` → `Destroy(_liftedCanvas)` — a *deferred* destroy that lands at end of the same frame. When the nested Canvas dies, its graphics re-register to the root canvas; a TMP mesh regenerated under the dying canvas can come back with pre-update geometry (the graphic is re-added but not re-dirtied). The repro fingerprint matches: this path only exists when the reaction bar is open — i.e., the owner "starts changing a reaction on another bubble" — and it is frame-timing sensitive ("sometimes"). The round-2 `UnliftRow` comment already documents this Canvas's graphic-registry fragility for *raycasts*; rendering uses the same registry.

*Half 2 — why it never heals (round-3 code).* Every subsequent reconcile — the 3s latest-window pass (`ChatManager.cs:741-747`), the 12s wider pass (`ChatManager.cs:1216-1225` via `ValidateCachePageAgainstServer`), and the pagination pass (`ChatManager.cs:1319-1325`) — goes through `RefreshCachedMessageReactions`, which returns at `ChatManager.cs:1855` **without firing the event** whenever `SameReactions(cached, merged)` is true. Since the VM data is *already correct* (only the pixels are stale), the guard swallows every future re-render opportunity for the rest of the session. A single lost render becomes permanent — precisely the observed "logs always show the right reaction, bubble doesn't update".

**Fix:** Re-dirty the pressed bubble's reaction visuals after the lifted Canvas is actually gone. In `ReactionBarController`, hold the `MessageItemView` from `Show(source)` and, in `Hide()`, after `UnliftRow()`, defer one frame and force a re-render:

```csharp
// ReactionBarController
private MessageItemView _sourceView;            // captured in Show(source)

public void Hide()
{
    _target = null;
    UnliftRow();
    if (content != null) content.SetActive(false);
    if (_sourceView != null) { StartCoroutine(RefreshSourceNextFrame(_sourceView)); _sourceView = null; }
}

private System.Collections.IEnumerator RefreshSourceNextFrame(MessageItemView view)
{
    yield return null;                          // the deferred Canvas destroy has landed
    if (view != null) view.RefreshReactionsVisual();   // public wrapper: RenderReactions + SetAllDirty on the pill label
}
```

`MessageItemView.RefreshReactionsVisual()` should call `RenderReactions()` plus `SetAllDirty()` (or `SetVerticesDirty`) on the pill's TMP label so the mesh regenerates on the root canvas. Also recommended for the next UAT round: a compiled-on-device (not `#if UNITY_EDITOR`) capped log line in `HandleReactionsChanged`, so the "handler ran / visual failed" hypothesis is confirmed or eliminated on device.

### WR-02: `HandleActiveChannelChanged` unconditionally re-shows a stale empty card even when the new channel has no empty state

**File:** `Assets/Scripts/UI/EmptyStateView.cs:203-208`

**Issue:** The round-3 handler re-configures and `Show()`s whatever `_lastReason` holds, without checking whether the *new* channel is actually in an empty state. Concrete regression: a Telegram-only bot inside its 08-19 sync window — owner flips to WhatsApp (unconnected, `BotHasNoWhatsApp` card shows, `_lastReason` set), then flips back to Telegram. The handler re-shows the stale «WhatsApp не подключён» card (alpha 1, `blocksRaycasts` true) while `BeginLoadForActiveBot` fires `OnWhatsAppSyncing` and the `SyncingView` cover shows on the same panel. `EmptyStateView` subscribes to neither `OnWhatsAppSyncing` nor `OnWhatsAppSyncReady`, and no `OnChatAdded` will arrive until the ~5-minute window elapses — so the wrong-channel card (and its raycast block) can sit over/under the cover for minutes. In the non-syncing case the stale card also lingers until the first `OnChatAdded` hides it (potentially several seconds on a cold cache).

**Fix:** Re-derive instead of replaying:

```csharp
private void HandleActiveChannelChanged(ChatChannel _)
{
    if (!_lastReason.HasValue) return;
    EmptyStateReason? reason = ChatManager.Instance != null
        ? ChatManager.Instance.ComputeCurrentEmptyState() : null;
    if (!reason.HasValue) { Hide(); return; }   // new channel is syncing / has chats — no card
    _lastReason = reason;
    ConfigureForReason(reason.Value);
    Show();
}
```

(`ComputeCurrentEmptyState` is already channel- and sync-window-aware — `ChatManager.BotState.cs:269-286`.) This also removes the handler's dependence on `_lastReason` staleness generally, and combined with the CR-01 guard makes the empty card fully self-consistent across switches.

### WR-03: D2-ext wider pass only reconciles the cached window (≤100 messages) — deep-scrolled bubbles never reconcile

**File:** `Assets/Scripts/Main/ChatManager.LivePoll.cs:141-151`, `Assets/Scripts/Main/ChatManager.cs:1844-1862`, `Assets/Scripts/Main/ChatManager.cs:1144-1149`

**Issue:** The wider pass sizes itself from `_activeChatCache.Count` (`NeedsWiderPass`/`PagesToCover`), and `RefreshCachedMessageReactions` searches only `_activeChatCache`. But bubbles paginated from the server after the cached queue empties (`LoadNextPage` → `GetMessagesRoutine` callback at `ChatManager.cs:1146-1149`) are rendered from VMs that are **never inserted into `_activeChatCache`**. Since `ChatHistoryCache` caps at 100, any chat scrolled past 100 messages has rendered bubbles that no reconcile path (latest-window, wider pass, or pagination refresh) can ever reach — their reaction pills stay permanently stale, the exact class of staleness 08-17 set out to close. `PagesToCover` also caps at 2 in practice (cache ≤ 100, page size 50), so the "round-robin across ticks" only ever revisits page 2.

**Fix:** Either document the bound explicitly (the pass covers the *cached* window, not the *rendered* window) and accept it, or extend coverage: track `LoadNextPage`-fetched VMs in a per-chat rendered-beyond-cache list, include its count in the `NeedsWiderPass`/`PagesToCover` math, and pass a concatenated view to `RefreshCachedMessageReactions` inside `ValidateCachePageAgainstServer`. Given the D2-view repro concerns recent messages, this is follow-up hardening, not the device defect.

## Info

### IN-01: Known out-of-scope: `BeginLoadForActiveBot` fires the wrong reason when zero bots exist

**File:** `Assets/Scripts/Main/ChatManager.BotState.cs:296-300`
**Issue:** With no bots, `FindBotByName` returns null and the code fires `NoConnectionEmptyState()` (`BotHasNoWhatsApp`/`BotHasNoTelegram`) instead of `NoBotsExist`. Documented as accepted/out-of-scope for this phase — recorded here only because it is the reason-source half of CR-01's mechanism (the CR-01 fix defends in `EmptyStateView` without touching this).
**Fix:** (When in scope) check `Manager.Instance.BotsRoot.childCount == 0` first and fire `NoBotsExist`, mirroring `ChannelTabStateResolver`'s "NoBots wins over everything" precedence that `ComputeCurrentEmptyState` already implements.

### IN-02: `IndexOfUnmappedSameEmoji` fold can transiently hide a stranger's same-emoji reaction

**File:** `Assets/Scripts/Chat/TelegramReactionMerge.cs:72-76, 159-168`
**Issue:** When the owner has a fresh optimistic "me" and the server has no "me" yet, the fold consumes the first un-mapped same-emoji server entry. If that entry is genuinely another user's reaction (not the owner's un-mapped echo), the visible count reads 1 instead of 2 until the owner's real echo lands as "me" (then `serverMine >= 0` takes the replace branch and the stranger's entry reappears). Self-healing and bounded by echo latency; T-08-11-01 only pins the owner-did-not-react case. Acceptable tradeoff — worth a comment/test acknowledging the owner-did-react collision.
**Fix:** Document; optionally restrict the fold to the case it exists for — `_tgOwnUserId` still unlearned (root cause B) — by passing that flag into `Merge`.

### IN-03: `[D12]` editor-only diagnostic logs still in place

**File:** `Assets/Scripts/UI/EmptyStateView.cs:260-266, 275-279, 297-299`
**Issue:** Three `#if UNITY_EDITOR` `Debug.Log` blocks from the 08-18 diagnosis pivot remain (grep-removable by design, never compiled on device). Keep until D12-ext is verified closed on device, then remove. Note CR-01 explains why they produced no evidence: after a channel switch the wired handler is `OpenCurrentBotAuth`, which they don't instrument.
**Fix:** Remove after the D12-ext device pass; if kept for the next UAT round, add a mirror log to `OpenCurrentBotAuth`'s early-returns (see CR-01).

---

_Reviewed: 2026-07-20T10:37:36Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_

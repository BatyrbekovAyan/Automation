# Message Batching / Debounce — Design

**Date:** 2026-07-15
**Status:** Approved design
**Milestone:** v1.2 (Phase 10, sequenced after the Phase 9 suppression gate — edits the same reply-path region)

## Problem

A customer often sends one thought as several messages ("есть колодки" / "на камри 70?"). Today each fragment is its own Wappi webhook → its own bot-workflow execution → its own reply. So the bot fires multiple replies to one thought, and each reply saw only a partial message. The same premature-firing happens to suggestions: `SuggestionsController.HandleLive` issues an LLM request per fragment. The bot should wait until the customer is done, then reply **once** to the combined message — and suggestions should coalesce the same way.

## Key constraint (from the current workflow)

The WhatsApp/Telegram bot reply path is:

```
Webhook → group If → Input type → (Text | Download→Transcribe→Audio) → AI Agent [GENERATES HERE]
        → Pause Before Reading → Mark Read → Reading Pause → Typing → Typing Pause → send
```

The **AI Agent generates the reply first**; every humanizer pause runs *after* generation (to delay the send so it feels human). So those pauses cannot double as the debounce window — by the time they run, each fragment has already generated. **The debounce must be its own pre-generation wait**, inserted right after the group `If` (and after the Phase-9 suppression gate), before `Input type`.

## Decisions (locked)

- **Debounce windows:** ~8s auto-reply, ~2.5s suggestions — each a single tunable constant. Honest cost: *every* auto-reply (even a single complete message) now waits the auto window before the bot starts generating. That is the price of never splitting; the window is the primary tuning knob.
- **Media in a burst — v1 batches text only.** Combine the trailing run of consecutive incoming **text** messages up to and including the latest; a media message or the bot's own reply bounds the run. If the latest message is media (voice/image/doc), it is processed alone (today's behavior). Known v1 limitation: media interleaved with text in a single burst can leave an earlier text fragment unanswered (rare; v2 refinement).
- **Composition with Phase 9:** the suppression gate runs *before* the debounce — a semi-auto chat skips the whole reply path, so no point waiting on a chat that won't auto-reply. Pipeline: `group If → suppression gate → debounce+combine → Input type → AI Agent → …`.
- **Both channels:** the debounce stage lands in the WhatsApp and Telegram bot templates (per-channel clones, like the suppression gate). The Wappi fetch uses each template's channel base (`api/sync` vs `tapi/sync`, already correct post-Phase-4); message `id`/`body` field names per channel follow the Phase-3 SHAPES.md verdicts.

## Architecture

### 1 · Auto-reply side (n8n) — pre-generation debounce + dedupe + combine

Insert between the group `If` (after the Phase-9 gate) and `Input type`:

```
… suppression gate (not suppressed) → Debounce Wait (~8s, in-memory resume)
      → Fetch Recent (HTTP → messages/get for this chat, limit ~15, existing WappiAuthToken cred)
      → Latest + Combine (Code)
      → Is Latest? (If)
           ├─ abort → DEAD-END (a newer fragment's own execution handles the batch)
           └─ proceed → feed combinedText into the existing text path → AI Agent → … (unchanged)
```

- **Debounce Wait** — n8n Wait node, ~8s (short → in-memory resume, no webhook-resume URL). One waiting execution per fragment; n8n runs them concurrently.
- **Fetch Recent** — one `messages/get` call after the wait. This single fetch serves BOTH jobs below.
- **Latest + Combine** (Code node), given the fetch + `triggeringId = $('Webhook').item.json.body.messages[0].id`:
  - Newest incoming (`fromMe == false`) message id `≠ triggeringId` → `abort = true` (a newer fragment arrived during my wait; its execution will handle the batch).
  - Else `abort = false`; if the latest is **text**, walk newest→oldest collecting consecutive incoming **text** bodies, stopping at the first `fromMe` OR media message; reverse to chronological; `combinedText = bodies.join("\n")`. If the latest is media, `combinedText = null` (process that one message normally).
  - Output `{ abort, combinedText }`.
- **Is Latest?** (If) — `abort === true` → dead-end; else continue. The winning execution sets the agent input from `combinedText` (when non-null) instead of the single `messages[0].body`; media path unchanged.

**Why one fetch serves both:** finding "am I the last incoming?" and "what are all the unanswered fragments?" are the same scan over recent messages. Aborted fragments never reach the AI Agent — no wasted LLM calls, no duplicate replies. The window self-extends: each new fragment aborts the prior waiter, so the bot replies ~8s after the *last* message. No-prior-bot-reply (new chat) → the run is bounded by the fetch limit.

### 2 · Suggestions side (client) — a debounce timer only

The suggestions payload already ships the last ≤12 messages (the LLM already *sees* every fragment — combine is free) and the Phase-1 sequence guard already discards stale renders. The only fix is to stop firing per-fragment:

- In `SuggestionsController.HandleLive`, debounce the incoming-triggered request: on each incoming batch, cancel any pending timer coroutine and start a fresh ~2.5s one that calls `IssueRequest(steerTowardText: null, lastIncomingText: …)` when it settles. A newer incoming restarts the timer.
- **Not debounced** (fire immediately, unchanged): manual refresh (INT-03) and card-pick re-cluster (INT-04) — those are explicit owner actions.
- Cancel the pending timer on chat close / bot switch (`OnDisable` / `ResetForNoOpenChat`).

This coalesces N per-fragment calls into one, on the full combined message. No is-latest, no fetch, no server change.

## Testing

- **Unity EditMode:** a small injectable-scheduler debounce helper (e.g. `IncomingDebounceGate` with a settable clock/runner) so "three rapid incomings → exactly one `IssueRequest` after the window; a manual refresh fires immediately" is unit-testable without real time. The combine is free client-side, so there is no payload change to test beyond the existing suggestions tests.
- **n8n curl (dev):** post two fragments ~1s apart to a bot webhook → assert exactly one reply, whose agent input was the concatenation (inspect execution `runData`: the first fragment's execution aborts at **Is Latest?**, the second combines). Post one message → one reply after ~8s. Post fragment + a genuine bot reply between → the run is bounded at the bot reply.
- **Human dev e2e:** on both channels, send a 3-part message → the bot replies once to the combined thought after the window; the owner watching «Вместе» sees suggestions update once (not thrice). Confirm single-message latency feels acceptable at the chosen window.

## Out of scope

- Adaptive windows (punctuation-aware "message looks complete → reply now / shorten the wait").
- Combining across media types (transcribing a voice note into the text run; interleaved media+text bursts) — v1 boundary rule above; v2 refinement.
- Reworking the existing humanizer pauses (they stay as-is post-generation; a future pass could trim "Pause Before Reading" since the debounce already waited).
- Any change to the suppression gate (Phase 9) or the activation switch.

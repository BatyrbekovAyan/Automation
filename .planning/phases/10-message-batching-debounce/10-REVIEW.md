---
phase: 10-message-batching-debounce
reviewed: 2026-07-22T11:17:27Z
depth: standard
files_reviewed: 13
files_reviewed_list:
  - Assets/Scripts/Chat/IncomingDebounceGate.cs
  - Assets/Scripts/Chat/SuggestionsController.cs
  - Assets/Tests/Editor/Chat/IncomingDebounceGateTests.cs
  - Tools/n8n/README.md
  - Tools/n8n/apply-message-batching.py
  - Tools/n8n/fix-orchestrator-settings.py
  - Tools/n8n/verify-message-batching.py
  - Tools/n8n/workflows/3qax5J9u2qsT9Vao-Edit_Whatsapp_Workflow.json
  - Tools/n8n/workflows/4VN3gsFaC2HUYmcc-Telegram_Bot.json
  - Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json
  - Tools/n8n/workflows/TwWPW3gIyjZS3foR-Edit_Telegram_Workflow.json
  - Tools/n8n/workflows/Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json
  - Tools/n8n/workflows/XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json
findings:
  critical: 0
  warning: 4
  info: 7
  total: 11
status: issues_found
---

# Phase 10: Code Review Report

**Reviewed:** 2026-07-22T11:17:27Z
**Depth:** standard
**Files Reviewed:** 13
**Status:** issues_found

## Summary

Reviewed the phase-10 message-batching/debounce work: the pure client-side `IncomingDebounceGate` + its `SuggestionsController` integration, the EditMode spec, the two idempotent migration scripts (`apply-message-batching.py`, `fix-orchestrator-settings.py`) plus the structural verifier, and the six canonical n8n workflow JSONs they touch.

Overall quality is high. The C# gate is a clean pure seam mirroring `OpenChatLivePollGate`, the four lifecycle cancel sites in `SuggestionsController` are all present and correct, the Python migrations are genuinely idempotent (uuid5-stable node ids, guarded node adds, overwrite-idiom rewires), and `verify-message-batching.py` passes against the committed JSONs (`ALL BATCHING ASSERTS PASSED`, cross-template identity included). The `binaryMode` strip assignment is present in all six settings-passthrough Set nodes across the four orchestrators, and the canonical PROD URLs are untouched (self-checked by the script itself).

The findings concentrate in the spliced `Latest+Combine` Code node and the new `Fetch Recent` hot-path dependency: one nullish-coalescing bug that feeds the LLM an empty prompt in a realistic interleaving, a mixed-type-burst fragment drop, and two hot-path robustness gaps (no retry on the new HTTP fetch; implicit paired-item linkage across the new Code node). None are Critical (no security, data-loss, or crash paths), but WR-01–WR-04 should be resolved or explicitly accepted before the 10-03 owner deploy gate closes.

No source files were modified during this review.

## Warnings

### WR-01: Empty `combinedText` ("") defeats the Text node's `??` fallback — LLM receives an empty prompt

**File:** `Tools/n8n/apply-message-batching.py:63-73` (canonical source), deployed in `Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json:868` and `Tools/n8n/workflows/4VN3gsFaC2HUYmcc-Telegram_Bot.json:882`; consumed by the Text node value at `apply-message-batching.py:196-197`
**Issue:** In the combine loop, if the newest message in the fetch is **outgoing**, the loop breaks on the first iteration, `parts` stays empty, and `combinedText = [].join('\n')` yields the empty string `""` — not `null`. The Text node value `={{ $json.combinedText ?? $json.body.messages[0].body }}` only falls back on nullish values, so `""` wins and the AI Agent receives an empty prompt. This interleaving is realistic, not exotic: the humanizer waits (`Reading Pause` + `Typing Pause`, each `wordCount/2 + 2` s) mean a bot reply to an earlier message routinely lands **inside** a later fragment's 8s debounce window. Sequence: customer sends Q1 → bot starts its ~15s humanizer → customer sends Q2 at t+10s → bot reply R1 lands at t+15s → Q2's fetch at t+18s sorts R1 newest → `abort=false` (Q2 is still the newest *incoming*) but `parts=[]` → empty prompt → garbage/generic reply to Q2.
**Fix:** Make empty runs nullish so the single-message fallback engages, in `LATEST_COMBINE_JS`:
```javascript
  parts.reverse();
  combinedText = parts.length > 0 ? parts.join('\n') : null;
```
Then re-run `apply-message-batching.py` (add a matching assert to `verify-message-batching.py`) and redeploy the two templates.

### WR-02: Mixed-type bursts silently drop the earlier fragment (voice/text/image interleavings)

**File:** `Tools/n8n/apply-message-batching.py:57-71` (abort/combine logic); routing at `Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json:970-1007` (`Input type` → `Ask to Send Text` for image/document/extra)
**Issue:** The dedupe aborts every non-newest fragment, but the combine only merges **text** runs. Any burst mixing types answers only the newest fragment and permanently drops the rest — the dropped content never reaches the AI Agent *or* Chat Memory, so it is also absent from all future conversational context:
- voice t0 + text t1 → voice execution aborts; text execution's combine loop breaks at the voice → voice never transcribed or answered.
- text t0 + voice t1 → text aborts; voice proceeds solo → the text question is never answered.
- text t0 + image t1 → text aborts; image proceeds → customer gets «Please send text messages» while their actual text question is ignored — the worst UX of the three.
**Fix:** If this is accepted v1 scope, record it explicitly in `10-CONTEXT.md`/UAT so the 10-04 owner pass doesn't misread it as a regression. A contained improvement for the text-then-media case: in `Is Latest?`'s winner path, when the winner is non-text but the combine window contained text fragments, prepend them (e.g., emit `pendingText` alongside `combinedText` and concatenate after `Transcribe Audio`). At minimum, add the voice→text and text→image interleavings to the 10-03 e2e matrix.

### WR-03: `Fetch Recent` is a new un-retried single point of failure on the hot reply path (and same-endpoint response crossing is a project-confirmed Wappi behavior)

**File:** `Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json:826-865`, `Tools/n8n/workflows/4VN3gsFaC2HUYmcc-Telegram_Bot.json:840-879`; authored at `Tools/n8n/apply-message-batching.py:123-144`
**Issue:** The spliced `Fetch Recent` httpRequest has no `retryOnFail` and no `onError` handling. Any transient `messages/get` failure (Wappi 5xx, timeout) errors the execution and the customer's message is silently dropped — a failure mode the pre-splice pipeline did not have before generation. Separately, the Unity client treats concurrent same-endpoint `messages/get` responses as crossable and defends with a serial-fetch gate + `CrossChatResponseGuard` (see `Assets/Scripts/Chat/OpenChatLivePollGate.cs` — "the serial-fetch invariant (Wappi/tapi cross concurrent same-endpoint responses)" — and the crossed-response retry in `Assets/Scripts/Main/ChatManager.cs:1352-1364`). The n8n side now issues the same endpoint concurrently whenever two chats (or two bots on one Wappi account) burst at once; a crossed response makes `newestIncoming.id !== triggeringId` → **both** executions abort → both customers silently unanswered, with no error execution to notice.
**Fix:** On the `Fetch Recent` node add `"retryOnFail": true, "maxTries": 3, "waitBetweenTries": 1000` (splice these in `apply-message-batching.py` and assert them in `verify-message-batching.py`). For crossing, add a cheap sanity check in `Latest+Combine` — verify the fetched messages belong to the requested chat (e.g., compare a fetched message's `chatId` to `wh.body.messages[0].chatId`) and log/flag mismatches so the 10-03 runData gate can see whether crossing occurs from n8n at all.

### WR-04: Downstream `$('Webhook').item` references now resolve across the spliced Code node, which omits explicit `pairedItem`

**File:** `Tools/n8n/apply-message-batching.py:73` (`return [{ json: { ...wh, abort, combinedText } }];`); downstream `.item` consumers in `4wYitz5ek30SVNlT-WhatsApp_Bot.json` at lines 276, 289 (Mark Read), 321, 330 (Typing), 433-505 (Input type2), 577 (Listening Pause), 592 (Chat Memory sessionKey), 31, 44 (HTTP Request send) — mirrored in the Telegram template
**Issue:** Every pre-existing node below the splice back-references the webhook via `$('Webhook').item`, which requires an unbroken paired-item chain. The new `Latest+Combine` returns its item **without** `pairedItem`, so the whole humanizer/send/memory chain now depends on n8n's implicit 1-item→1-item auto-pairing through Wait → HTTP → Code → If. The research hardened the Code node itself (`.first()`, asserted by `verify-message-batching.py:101-104`) but left the downstream linkage implicit — and this same codebase already treats this as a real hazard: the orchestrators' `Vertical Prompt` Code node explicitly returns `pairedItem: { item: 0 }` (`XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json:304`). If auto-pairing ever fails (n8n version change, Wait resume edge), every reply dies with "paired item data unavailable".
**Fix:** Zero-cost hardening — make the pairing explicit in `LATEST_COMBINE_JS`, matching the Vertical Prompt idiom:
```javascript
return [{ json: { ...wh, abort, combinedText }, pairedItem: { item: 0 } }];
```
Add the substring to the verifier's Code-node asserts, and have the 10-03 runData check confirm `Mark Read`/`Chat Memory` resolved their `$('Webhook').item` expressions on a debounced execution.

## Info

### IN-01: Clock mismatch — `WaitForSecondsRealtime` loop drives a `Time.time` gate

**File:** `Assets/Scripts/Chat/SuggestionsController.cs:211,229-231`
**Issue:** `DebounceLoop` ticks on unscaled realtime but `Poke`/`ShouldFire` use scaled `Time.time`. Correctness is preserved (both gate calls share one clock), but the window's wall-length stretches across frame hitches/app-resume (`Time.time` is `maximumDeltaTime`-capped) and would stall entirely if `timeScale` ever hit 0. The idiom it claims to mirror uses realtime throughout (`ChatManager.cs:545` — `Time.realtimeSinceStartup`).
**Fix:** Pass `Time.realtimeSinceStartup` (or `Time.unscaledTime`) to `Poke`/`ShouldFire` at both call sites.

### IN-02: Test literal `2.4f` silently couples to `WindowSeconds == 2.5`

**File:** `Assets/Tests/Editor/Chat/IncomingDebounceGateTests.cs:41`
**Issue:** `Assert.IsFalse(gate.ShouldFire(2.4f), ...)` only holds while the window is 2.5s. The constant is documented as "single tunable; tune at e2e" — tuning it below ~2.3s flips this assert and fails the suite for the wrong reason. Other asserts in the file already derive from `Window`.
**Fix:** `Assert.IsFalse(gate.ShouldFire(0.2f + Window - 0.1f), ...)`.

### IN-03: `Debounce Wait` is the only Wait node without a `webhookId`

**File:** `Tools/n8n/apply-message-batching.py:113-121`; `Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json:813-825`
**Issue:** Every pre-existing Wait node in both templates carries a `webhookId`; the spliced one doesn't. Harmless at 8s (in-memory resume), but `DEBOUNCE_SECONDS` is the tunable — if e2e tuning ever pushes it ≥65s, n8n switches to webhook-resume + DB offload, where a missing `webhookId` can break resume.
**Fix:** Emit a stable `"webhookId": nid("Debounce Wait-webhook")` when splicing, or note the <65s constraint next to `DEBOUNCE_SECONDS`.

### IN-04: Suppression flag is read pre-wait — up to 8s stale at generation time

**File:** `Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json:643-699` (Read Reply Mode → Suppressed? → Debounce Wait ordering; mirrored in the Telegram template)
**Issue:** `Read Reply Mode` runs before `Debounce Wait`, so an owner toggling to «Вместе» during a fragment's 8s window still gets that fragment auto-replied (the client-side toggle write lands mid-window but the gate was already read). One-message-deep race; likely acceptable.
**Fix:** Confirm as accepted at 10-03; if not, move the gate read (or add a re-read) after `Is Latest?`'s winner branch — the fetch and Postgres read are both cheap and only the winner would pay it.

### IN-05: Telegram-only — aborted fragments are never marked read

**File:** `Tools/n8n/workflows/4VN3gsFaC2HUYmcc-Telegram_Bot.json:274-314` (Mark Read: single `message_id`, no `mark_all`) vs `4wYitz5ek30SVNlT-WhatsApp_Bot.json:265-309` (`mark_all: true`)
**Issue:** Pre-splice, every fragment ran its own execution and got its own Mark Read. Post-splice only the winner reaches Mark Read; WhatsApp's `mark_all=true` sweeps the burst, but Telegram marks only the winning message id — earlier fragments in a TG burst stay unread (customer never sees read receipts on them; slightly breaks the humanizer illusion).
**Fix:** Cosmetic; either accept, or align the TG Mark Read with a burst-covering variant during the prod pass.

### IN-06: `apply-message-batching.py` — dead parameter and unguarded lookups

**File:** `Tools/n8n/apply-message-batching.py:86-92,107,196`
**Issue:** `find()`'s `type_suffix` parameter is never used by any caller (dead code); and `find(nodes, name="Mark Read")["parameters"]["url"]` / `find(nodes, name="Text")[...]` dereference possible `None` returns, so a renamed node fails with a raw `TypeError` instead of a named error (the verifier's `node()` helper does this right).
**Fix:** Drop `type_suffix`; wrap the two lookups with an assert naming the missing node, e.g. `assert mark_read is not None, f"{wf['id']}: 'Mark Read' node not found"`.

### IN-07: Unused serialized field `_mockLatencySeconds` (pre-existing)

**File:** `Assets/Scripts/Chat/SuggestionsController.cs:23`
**Issue:** Referenced nowhere since the Phase-2 single-line swap to `N8nSuggestionsProvider` (line 41); `MockSuggestionsProvider` is no longer constructed here, so the latency knob is dead inspector surface. Pre-existing — not introduced by phase 10.
**Fix:** Delete the field (and its inspector value in the scene) in a cleanup pass, or move it into `MockSuggestionsProvider` if the mock path is meant to stay swappable.

---

_Reviewed: 2026-07-22T11:17:27Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_

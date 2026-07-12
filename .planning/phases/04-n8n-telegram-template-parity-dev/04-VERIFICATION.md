---
phase: 04-n8n-telegram-template-parity-dev
verified: 2026-07-12T14:18:53Z
status: human_needed
score: 12/12 buildable must-haves verified (TPL-06 live e2e is the designed owner gate, not counted against score)
overrides_applied: 0
human_verification:
  - test: "Full TPL-06 owner runbook (04-HUMAN-UAT.md): deploy the 4 edited workflows to dev n8n, authorize a real dev Telegram profile, run the text / voice / memory / pre-auth-file-re-stamp conversation e2e"
    expected: "AI replies arrive in Telegram for text; voice transcribes with a length_seconds-based pause; multi-turn memory stays coherent; a price-list file uploaded before Telegram auth becomes RAG-grounded once the Telegram workflow is created (Supabase spot-check count > 0 on the new botTgId)"
    why_human: "Dev n8n is not running and its API key is deny-ruled in secrets.json; the flow requires a real authorized Telegram account, a live tunnel, and observing actual message delivery — none of this is verifiable by static file inspection"
  - test: "Postgres credential (vvRrFiEXzLVqKjOx) resolve + UPDATE-permission pre-flight on the dev n8n instance (04-HUMAN-UAT.md step 1)"
    expected: "Executing the Restamp RAG Chunks node once with harmless params (\$2 = \"-1\") returns a 0-row UPDATE with no credential/permission error"
    why_human: "Requires an open n8n editor session against the live dev instance; cannot be checked from the JSON alone"
  - test: "Telegram voice-message type string (IN-01 from 04-REVIEW.md) — confirm tapi reports voice notes as type \"ptt\" (carried over from WhatsApp) rather than something else like \"voice\""
    expected: "A real Telegram voice note sent to the bot produces a webhook payload whose messages[0].type matches the Switch node's existing \"ptt\" rule so it routes to the Audio branch instead of the text fallback"
    why_human: "Only observable from a live tapi webhook payload; guessing offline would be unverifiable and the review explicitly deferred this to UAT"
  - test: "Manager.cs Editor/device compile pass (04-02-SUMMARY notes this was not run — hook/brace-balance checked statically only)"
    expected: "The project compiles cleanly in the Unity Editor with the new AddField lines; no new warnings/errors"
    why_human: "Editor state was unknown during execution per the plan's hard constraint (do not run the Unity test suite / recompile mid-session); owner confirms as part of the TPL-06 gate"
---

# Phase 4: n8n Telegram Template Parity (dev) Verification Report

**Phase Goal:** A Telegram-authed bot actually converses — the Telegram_Bot template runs on tapi bases, routes text through the AI agent, keys session memory stably, and files uploaded before channel auth become RAG-retrievable — proven end-to-end against a real dev Telegram profile.
**Verified:** 2026-07-12T14:18:53Z
**Status:** human_needed
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Telegram_Bot outbound HTTP nodes POST to tapi bases, zero `api/sync` outbound URLs remain | VERIFIED | `HTTP Request` → `https://wappi.pro/tapi/sync/message/send`; `Mark Read` → `https://wappi.pro/tapi/sync/message/mark/read` (mark_all param removed, only `profile_id` remains); `Typing` → `https://wappi.pro/tapi/sync/chats/typing/start`. No `api/sync/(message/send|message/mark/read|chat/typing/start)` residue (grep confirmed) |
| 2 | Telegram text (`type:"text"`) routes through the AI agent, not the fallback | VERIFIED | Both `Input type` and `Input type2` Switch nodes' first rule now has `combinator: "or"` with two conditions: `rightValue: "chat"` and `rightValue: "text"` (dumped both nodes' JSON directly) |
| 3 | Voice humanizer "Listening Pause" resolves via `length_seconds` fallback, never undefined/NaN | VERIFIED | Node contains `media_info?.duration ?? ...length_seconds ?? 0) + 2`; no bare `media_info.duration + 2` remains |
| 4 | Chat Memory sessionKey keys on `profile_id + ':' + chatId`, not `from` | VERIFIED | grep confirms sessionKey expression ends with `messages[0].chatId` |
| 5 | Creating a Telegram workflow re-stamps pre-auth RAG chunks (`botTgId` `-1` → new id, keyed by `botWaId`) and still returns `{ id }` | VERIFIED | `Restamp RAG Chunks` node in `Uz6HBBUpAiUqVysB` runs `UPDATE documents SET metadata = jsonb_set(metadata, '{botTgId}', to_jsonb($1::text)) WHERE $2 <> '-1' AND $2 <> '' AND metadata->>'botWaId' = $2 AND metadata->>'botTgId' = '-1'` with `queryReplacement = "={{ Get Created Workflow Id.id }},{{ Unity Webhook.body.WhatsappWorkflowId }}"` (no stray `=` — CR-01 fix confirmed live), credential `vvRrFiEXzLVqKjOx`/"Postgres", `alwaysOutputData: true`, `onError: continueRegularOutput`. `Send New Workflows Id` uses `$('Get Created Workflow Id').first().json.id` (WR-01 fix confirmed live) and is still the terminal node |
| 6 | Creating a WhatsApp workflow mirror-re-stamps (`botWaId` `-1` → new id, keyed by `botTgId`) and still returns `{ id }` | VERIFIED | Same shape confirmed in `XuvOp7TxOImOAmlj`: `jsonb_set(metadata, '{botWaId}', ...)`, `$2 <> '-1' AND $2 <> ''`, `$2 = body.TelegramWorkflowId`, same credential/robustness flags, same `.first()` fix |
| 7 | Suggest_Replies accepts an additive `channel` field and routes RAG to `botTgId` for telegram; absent channel behaves byte-identically to v1.0 | VERIFIED | Prep jsCode: `channel = (b.channel === 'telegram') ? 'telegram' : 'whatsapp'`, `wfId = channel==='telegram' ? botTgId : botWaId`, `skipRag` computed from `wfId`. `If channel TG?` routes true→`Retrieve RAG TG` (single-key `botTgId`), false→`Retrieve RAG` (single-key `botWaId`); `Embeddings` node's `ai_embedding` output feeds both; both feed `Assemble`. Absent `channel` → `whatsapp` → identical routing to pre-phase |
| 8 | verify-telegram-parity.py exits 0 on the edited files | VERIFIED | Ran directly: `ALL PARITY ASSERTS PASSED`, exit 0. Spot-checked the verifier's teeth by reintroducing the CR-01 stray-`=` bug in a scratch copy — verifier correctly failed with `PARITY FAIL: ... stray '=' after comma`; file restored, `git status` clean, re-ran green |
| 9 | CreateTelegramWorkflow create calls (FromStart + FromEdit) send `WhatsappWorkflowId` | VERIFIED | Manager.cs:2879, 2974 — both sentinel-guarded (`string.IsNullOrEmpty(waId) ? Bot.UnauthedProfileSentinel : waId`) |
| 10 | CreateWhatsappWorkflow create calls (FromStart + FromEdit) send `TelegramWorkflowId` | VERIFIED | Manager.cs:2736, 2828 — both sentinel-guarded (`string.IsNullOrEmpty(tgId) ? Bot.UnauthedProfileSentinel : tgId`) |
| 11 | New form fields follow the existing WWWForm.AddField pattern, read server-side from `$json.body` | VERIFIED | Each new field sits immediately after the existing `*ProfileId` AddField in its coroutine, identical `form.AddField(key, value)` shape; server reads `$('Unity Webhook').first().json.body.WhatsappWorkflowId` / `.TelegramWorkflowId`, matching the pre-existing Name/BusinessTypeId pattern |
| 12 | 04-HUMAN-UAT.md documents the locked owner deploy + live e2e steps for TPL-06 | VERIFIED | File exists (125 lines), contains all 6 locked steps, the 4 workflow ids, `verify-telegram-parity.py`, `rotate-tunnel.py`, credential id `vvRrFiEXzLVqKjOx`, Supabase spot-check SQL, and an open PASS/FAIL result section (all boxes currently unchecked) |
| 13 | The whole flow is proven end-to-end on dev with a real Telegram profile (TPL-06) | **NOT VERIFIED — owner gate** | `04-HUMAN-UAT.md` result section is entirely unchecked (`☐ PASS ☐ FAIL` for Overall/text/voice/memory/pre-auth re-stamp). This is the designed human checkpoint — dev n8n is not running and its API key is deny-ruled in secrets.json; cannot be executed by static/structural verification |

**Score:** 12/12 structurally-verifiable truths passed. Truth #13 (live e2e) is explicitly routed to human verification per the phase's own design — not a code gap.

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `Tools/n8n/workflows/4VN3gsFaC2HUYmcc-Telegram_Bot.json` | tapi-correct Telegram bot clone source | VERIFIED | Valid JSON; 24 nodes unchanged; nodes[0]=Webhook, nodes[5]=AI Agent (order invariant intact); contains `tapi/sync/message/send` |
| `Tools/n8n/workflows/Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json` | Create Telegram orchestrator with RAG re-stamp | VERIFIED | Valid JSON; contains `Restamp RAG Chunks` node, correct SQL/credential/robustness/wiring |
| `Tools/n8n/workflows/XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json` | Create WhatsApp orchestrator with mirror RAG re-stamp | VERIFIED | Valid JSON; contains mirrored `Restamp RAG Chunks` node |
| `Tools/n8n/workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json` | channel-branched RAG retrieval (botWaId \| botTgId) | VERIFIED | Valid JSON; contains `Retrieve RAG TG`, `If channel TG?`, additive Prep code |
| `Tools/n8n/verify-telegram-parity.py` | structural-assert verifier for the 4 edited workflows | VERIFIED | 198 lines (> 80 min); runs clean, exits 0; confirmed to catch a reintroduced regression (non-zero exit with clear message) |
| `Assets/Scripts/Main/Manager.cs` | opposite-channel workflow id in all 4 create-workflow form builders | VERIFIED | 4 new sentinel-guarded `AddField` calls at the correct sites (2736, 2828, 2879, 2974); brace/paren balance 478/478, 1952/1952 (matches SUMMARY's static check) |
| `.planning/phases/04-n8n-telegram-template-parity-dev/04-HUMAN-UAT.md` | owner-driven dev deploy + e2e gate (TPL-06) | VERIFIED | 125 lines (> 40 min); all 6 locked steps present, result section open |

### Key Link Verification

| From | To | Via | Status | Details |
|------|-----|-----|--------|---------|
| `Set Wappi Webhook Types` | `Restamp RAG Chunks` | n8n connection (CreateTelegramWorkflow) | WIRED | Connections graph confirms `Restamp RAG Chunks` sits between `Set Wappi Webhook Types` and `Send New Workflows Id` in both orchestrators |
| `Suggest_Replies If channel TG?` | `Retrieve RAG TG` | `channel === 'telegram'` branch | WIRED | `If channel TG?` true→`Retrieve RAG TG`, false→`Retrieve RAG`; both feed `Assemble`; `Embeddings` ai_embedding feeds both (confirmed by direct connections-graph inspection, closing the WR-02 review gap) |
| `CreateTelegramWorkflowFromStart/FromEdit` | `CreateTelegramWorkflow webhook` | `form.AddField("WhatsappWorkflowId", ...)` | WIRED | 2 call sites confirmed, both sentinel-guarded |
| `CreateWhatsappWorkflowFromStart/FromEdit` | `CreateWhatsappWorkflow webhook` | `form.AddField("TelegramWorkflowId", ...)` | WIRED | 2 call sites confirmed, both sentinel-guarded |

### Data-Flow Trace (Level 4)

Not applicable in the conventional sense (no rendered UI component in this phase). The equivalent trace — does the re-stamp UPDATE actually reach real rows with real, non-interpolated parameters — was performed directly on the SQL/queryReplacement text (see Truths #5, #6) rather than via grep-only inspection, since this was the exact class of bug the code review caught (CR-01: a stray `=` silently no-opped every re-stamp while `onError: continueRegularOutput` masked it). Confirmed fixed and confirmed the verifier now catches a regression of the same shape.

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Structural verifier is a real check, not a rubber stamp | Reintroduced CR-01's stray `=` in a scratch copy of `Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json`, ran `verify-telegram-parity.py` | `PARITY FAIL: ...queryReplacement format wrong (stray '=' after comma...)`, exit 1 | PASS |
| All 4 edited workflow JSONs parse | `python3 -m json.tool` on each | All 4 valid | PASS |
| Manager.cs brace/paren balance | Python count of `{`/`}` and `(`/`)` | 478/478, 1952/1952 (matches SUMMARY claim) | PASS |
| WhatsApp_Bot.json untouched this phase | `git log` on `4wYitz5ek30SVNlT-WhatsApp_Bot.json` since 2026-07-12 | Last touching commits are pre-phase (`0a26bc0`, `d3ab5d0`, `bb98fd4`) | PASS |
| Review-fixed commits (CR-01/CR-02/WR-01) actually landed in the current files, not just claimed | Direct field-level inspection of `queryReplacement`, SQL WHERE clause, and `Send New Workflows Id` value expression in both orchestrators | Stray `=` removed, `$2 <> '-1' AND $2 <> ''` guard present, `.first()` used instead of `.item` | PASS |
| Live e2e (TPL-06) | N/A — requires dev n8n + real Telegram profile | Not run (by design) | SKIP → routed to human verification |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|-------------|-------------|--------|----------|
| TPL-01 | 04-01 | Template outbound nodes on tapi bases | SATISFIED | Truth #1 |
| TPL-02 | 04-01 | Telegram text routes through AI agent | SATISFIED | Truth #2 |
| TPL-03 | 04-01 | Voice transcription + length_seconds humanizer fallback | SATISFIED (structural); voice `type` string itself is a UAT item (IN-01) | Truth #3 |
| TPL-04 | 04-01 | Session memory keys on profile_id + chatId | SATISFIED | Truth #4 |
| TPL-05 | 04-01 + 04-02 | Pre-auth files become RAG-retrievable on late channel auth (server re-stamp + client opposite-channel-id form fields) | SATISFIED (structural — server + client wiring both verified); functional proof is part of the TPL-06 e2e | Truths #5, #6, #9, #10, #11 |
| TPL-06 | 04-02 | Dev e2e proof with a real Telegram profile via tunnel | **PENDING — owner gate**, not code-completable | Truth #13, 04-HUMAN-UAT.md unchecked |

No orphaned requirements: REQUIREMENTS.md lists exactly TPL-01..06 under Phase 4 and all six appear in the plans' `requirements:` frontmatter (04-01 claims TPL-01..05, 04-02 claims TPL-05, TPL-06 — TPL-05 is correctly split client/server across both plans).

**Note:** REQUIREMENTS.md's checklist section (lines 39-50) shows TPL-01 through TPL-06 all checked `[x]`, but the Traceability table (lines 105-110) still shows all six as "Pending" — including TPL-06, which cannot legitimately be marked complete until the owner runs the live e2e. This is a bookkeeping inconsistency in REQUIREMENTS.md itself (likely updated by the SUMMARY-writing step before verification), not a code gap. Recommend the phase-close step reconcile the checklist against the true TPL-06 status (leave TPL-06 unchecked until 04-HUMAN-UAT.md's Overall result is PASS).

### Anti-Patterns Found

None. Scanned all 5 phase-touched files (4 workflow JSONs + verify-telegram-parity.py) for TODO/FIXME/XXX/HACK/placeholder/not-implemented markers — zero hits.

### Human Verification Required

See YAML frontmatter `human_verification` section. Summary:

1. **Full TPL-06 owner runbook** (04-HUMAN-UAT.md) — deploy to dev n8n, authorize a real Telegram profile, run the text/voice/memory/pre-auth-file-re-stamp e2e. This is the phase's designed closing gate; dev n8n is not running and its API key is deny-ruled.
2. **Postgres credential pre-flight** — confirm `vvRrFiEXzLVqKjOx` resolves and can UPDATE `documents` on the live dev instance (04-HUMAN-UAT.md step 1).
3. **Telegram voice `type` string** (IN-01, carried from code review) — confirm tapi actually reports voice notes as `"ptt"`; only observable from a live payload.
4. **Manager.cs Editor/device compile pass** — not run during execution per the plan's hard constraint (Editor state unknown); owner confirms as part of the gate.

### Gaps Summary

No code gaps found. Every artifact, key link, and structurally-verifiable truth for TPL-01 through TPL-05 passed at all three levels (exists, substantive, wired), including direct re-verification that the two critical review findings (CR-01 stray `=`, CR-02 missing sentinel guard) and the two warnings (WR-01 `.item`→`.first()`, WR-02 verifier false-green paths) were genuinely fixed in the shipped files — not just claimed fixed in 04-REVIEW.md's frontmatter. The verifier was independently spot-checked to actually fail on a reintroduced regression, confirming it is a real gate and not a rubber stamp.

The only unmet item is TPL-06 (dev deploy + live e2e against a real Telegram profile), which by the phase's own design (04-CONTEXT.md, both PLAN frontmatters, 04-HUMAN-UAT.md) is an owner-executed human gate — Claude cannot start dev n8n, open a tunnel, or drive a live Telegram account, and the plan explicitly scopes this out of agent-executable work. This is not a gap in the implementation; it is the expected state at this point in the phase lifecycle.

---

_Verified: 2026-07-12T14:18:53Z_
_Verifier: Claude (gsd-verifier)_

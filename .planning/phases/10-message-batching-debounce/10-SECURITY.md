---
phase: 10
slug: message-batching-debounce
status: secured
threats_open: 0
asvs_level: 1
created: 2026-07-22
---

# Phase 10 — Security

> Per-phase security contract: threat register, accepted risks, and audit trail.
> Verified 2026-07-22 by gsd-security-auditor against the live repo state (both spliced bot templates, `IncomingDebounceGate.cs` + `SuggestionsController.cs`, the migration/verifier scripts) and the owner-recorded live-instance evidence in `10-03-SUMMARY.md` / `10-04-SUMMARY.md` / `10-HUMAN-UAT.md`.

---

## Trust Boundaries

| Boundary | Description |
|----------|-------------|
| incoming customer message (wappi/tapi Webhook) → Debounce stage | untrusted `messages[0]` fragments enter the fetch + combine |
| bot template → Wappi/tapi `messages/get` | `Fetch Recent` reads recent messages with the existing `WappiAuthToken` cred |
| combined customer text → AI Agent | concatenated fragments feed the LLM input |
| `ChatManager.OnLiveMessagesReceived` → `SuggestionsController` | incoming batches drive the client debounce; a pending fire must never cross a chat/bot boundary |
| owner tooling → dev n8n REST | template re-import by literal id + clone recreate |
| real customer message → live debounce bot (dev clone, active for the test window) | the abort/combine mechanism exercised with real fragments |
| owner device build → live suggestions provider | the client debounce coalesce exercised end-to-end |

---

## Threat Register

| Threat ID | Category | Component | Disposition | Mitigation (verified evidence) | Status |
|-----------|----------|-----------|-------------|------------|--------|
| T-10-01-01 | Tampering | combined text as prompt-injection | accept | See Accepted Risks Log R-10-01 | closed |
| T-10-01-02 | Information Disclosure | `Fetch Recent` marking the chat read early | mitigate | `Fetch Recent` query params are exactly `profile_id`/`chat_id`/`limit` — NO `mark_all` — confirmed by direct read of both `Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json` and `4VN3gsFaC2HUYmcc-Telegram_Bot.json` this session; `mark_all` exists ONLY on the pre-existing downstream `Mark Read` node (by design, unchanged). `python3 Tools/n8n/verify-message-batching.py` → "ALL BATCHING ASSERTS PASSED" (re-run this session, exit 0) | closed |
| T-10-01-03 | Tampering / ordering | suppression-bypass if debounce spliced before the gate | mitigate | Confirmed by direct JSON read this session: `connections["Suppressed?"]["main"] == [[], [{"node":"Debounce Wait", ...}]]` in BOTH templates — main[0] (suppressed=TRUE) is `[]` (dead-end, never reaches `Debounce Wait`); main[1] (FALSE) → `Debounce Wait`. `verify-message-batching.py` asserts this edge and passed | closed |
| T-10-01-04 | Denial of Service | rapid-fragment flood → many waiting executions | accept | See Accepted Risks Log R-10-02 | closed |
| T-10-02-01 | Tampering / correctness | pending debounce fires across a chat/bot boundary (stale `_pendingIncomingText`) | mitigate | All 4 lifecycle sites confirmed present in `Assets/Scripts/Chat/SuggestionsController.cs`: `OnDisable` (:80-82, incl. `StopCoroutine`), `ResetForNoOpenChat` (:100-101), `RestoreForActiveChat` at the top of the method body (:112-113 — the same-bot chat-switch BLOCKER fix), `HandleToggle` OFF branch (:147-148) — each pairs `_debounce.Cancel()` with `_pendingIncomingText = null`. `Assets/Tests/Editor/Chat/IncomingDebounceGateTests.cs:60-69` (`BurstThenChatSwitch_CancelsPending_ThenReArmsForNewChat`) proves the gate-level contract; full EditMode suite 1197/1197 (10-02-SUMMARY) | closed |
| T-10-02-02 | Denial of Service | debounce delaying the owner's explicit refresh/card-pick | mitigate | `HandleManualRefresh` (`SuggestionsController.cs:278-281`) and `HandleCardTapped` (`:199-207`) both call `IssueRequest` directly with no reference to `_debounce` — confirmed by direct read this session. Only `HandleLive` (`:212-218`) calls `_debounce.Poke` | closed |
| T-10-02-03 | Information Disclosure | new surface (client debounce) | accept | See Accepted Risks Log R-10-03 | closed |
| T-10-03-01 | Elevation / Safety | test clone left ACTIVE against real contacts (10-03 live bring-up) | mitigate | Owner-recorded Step-6 audit (10-03-SUMMARY, "Fresh-clone propagation + Step-6 deactivation sweep"): both templates + both fresh clones (`fKCMIGXJSbLRimdR`, `pOMkkP8MYS8WhiNY`) report `active=False`; no clone left active | closed |
| T-10-03-02 | Tampering / correctness | webhook vs sync id divergence (Pitfall 2/A3) | mitigate | Owner-recorded runData (10-03-SUMMARY, section D): id-equality `str==` **True on ALL 6 winners** (execs 848/852/863/864/866/868) across both channels; WhatsApp jid-hex forms and Telegram bare-numeric forms both recorded; the `False` on the two aborted executions (847/851) is the intended abort signal, not a divergence | closed |
| T-10-03-03 | Information Disclosure | `Fetch Recent` marking read early (live confirmation) | mitigate | Static: same as T-10-01-02 (no `mark_all` on `Fetch Recent`, verifier-asserted). Live: owner-recorded runData shows the reply path's `Mark Read` node is the only node that marks read, executed only on the winning fragment after the reply decision (10-03-SUMMARY) | closed |
| T-10-03-04 | Tampering | prod bagkz accidental target (10-03) | accept | See Accepted Risks Log R-10-04 | closed |
| T-10-04-01 | Elevation / Safety | test clone left ACTIVE (device e2e) | mitigate | Owner-recorded post-run checkbox, `10-HUMAN-UAT.md`: "[x] DEACTIVATE the test bot's reply-workflow clone (real-contacts constraint). — confirmed (owner, 2026-07-22)" | closed |
| T-10-04-02 | Tampering / ordering | semi-auto chat waiting/replying (gate-vs-debounce order) | mitigate | **Closed with residual.** Structural guarantee independently re-verified this session (same evidence as T-10-01-03): `Suppressed?` main[0] is `[]` in both committed templates — a semi-auto chat cannot structurally reach `Debounce Wait`. The **on-device behavioral observation** (scenario 5, "chat stays unread + no reply arrives") was explicitly **DEFERRED to post-Phase-9** by owner decision (`10-HUMAN-UAT.md` final disposition, 2026-07-22) — not because the code lacks the guard, but because the composition e2e needs the still-open Phase-9 09-04 `/webhook/SetReplyMode` deploy. Tracked as `uat_gap` debt in `STATE.md:171`, scheduled to re-verify alongside 09-04/09-05. This is an owner-authorized deferral of the *behavioral* proof, not an open implementation gap | closed (residual — see Residual Notes) |
| T-10-04-03 | Tampering | prod bagkz accidental target (e2e) | accept | See Accepted Risks Log R-10-04 | closed |

*Status: open · closed*
*Disposition: mitigate (implementation required) · accept (documented risk) · transfer (third-party)*

---

## Accepted Risks Log

| Risk ID | Threat Ref | Rationale | Accepted By | Date |
|---------|------------|-----------|-------------|------|
| R-10-01 | T-10-01-01 | The `Latest+Combine` Code node introduces no NEW prompt-injection channel — it only concatenates fragments (`Text` set node) that the AI Agent already receives today, one at a time, across sequential executions. Combining them into one string does not change what content reaches the LLM, only the batching. Prompt/agent-input hardening is out of this phase's scope and unchanged by the splice. | Owner (via 10-01-PLAN threat model + secure-phase run) | 2026-07-22 |
| R-10-02 | T-10-01-04 | A rapid-fragment flood produces one `Debounce Wait` execution per fragment, but each aborts after a single `Fetch Recent` call (no retry, no loop) and the `Wait` itself is sub-65s so n8n resumes in-memory with no DB offload/queue growth. Blast radius is bounded by the dev-only n8n instance (prod `bagkz` stays dormant for this entire phase — see R-10-04); a real production DoS-hardening pass (rate limiting, per-profile fragment caps) is deferred to a milestone-level decision, not a per-phase fix. | Owner (via 10-01-PLAN threat model + secure-phase run) | 2026-07-22 |
| R-10-03 | T-10-02-03 | `IncomingDebounceGate` is a pure C# timer (`Poke`/`Cancel`/`ShouldFire`) holding only a `float` deadline and a `bool` armed flag — no new secret, no new network call, no new endpoint. Confirmed by direct read of `IncomingDebounceGate.cs` (no `using UnityEngine`, no I/O) and by the 10-02-SUMMARY's own diff audit (`grep -c UnityWebRequest` = 0 in the changed files). The «Вместе» suggestions payload already ships the last ≤12 messages before this phase; coalescing only changes the firing cadence. | Owner (via 10-02-PLAN threat model + secure-phase run) | 2026-07-22 |
| R-10-04 | T-10-03-04, T-10-04-03 | Prod (`bagkz`) stays dormant for the entirety of Phase 10 — all template redeploy, `fix-orchestrator-settings.py --live` repair, runData matrix, and the 5-scenario device UAT targeted dev exclusively (owner-confirmed in 10-03-SUMMARY and `10-HUMAN-UAT.md` post-run block: "prod bagkz untouched"). The debounce splice AND the `binaryMode` orchestrator strip are explicitly folded into a future one-shot prod bulk-copy, not executed this phase. Accidental-prod-target risk is operationally mitigated by dormancy (same class of risk as R-09-03 in `09-SECURITY.md`), tracked here as accepted pending that future replication task. | Owner (via 10-03/10-04-PLAN threat models + secure-phase run) | 2026-07-22 |

*Accepted risks do not resurface in future audit runs.*

---

## Residual Notes (non-blocking)

- **T-10-04-02 (semi-auto gate-vs-debounce ordering, device behavioral half):** the structural code guarantee (`Suppressed?` main[0] dead-ends before `Debounce Wait` in both templates) is fully verified — independently re-confirmed by direct JSON read this session and asserted green by `verify-message-batching.py`. Only the *on-device behavioral observation* (scenario 5 of `10-HUMAN-UAT.md`) is outstanding, deferred to post-Phase-9 by explicit owner decision because the composition e2e depends on Phase-9's still-open 09-04 `/webhook/SetReplyMode` deploy. Already tracked as a `uat_gap` row in `STATE.md:171` — re-verifies alongside 09-04/09-05, not a new ask against this phase.
- **Scenario 4 (suggestions coalesce, BATCH-03) — same cross-phase blocker class:** the on-device confirmation of `IncomingDebounceGate`'s coalesce behavior was BLOCKED (not deferred) by the same open 09-04 dependency (`Manager.ReplyModeSync.cs:105` 404 when toggling to Semi-auto on dev). This is not a Phase-10 threat in the register, but is noted here because it shares T-10-04-02's remediation path: BATCH-03 retains full automated coverage (6 dedicated `IncomingDebounceGate` EditMode tests including the chat-switch-cancel regression; full suite 1197/1197) and the wiring itself (4/4 lifecycle cancel sites, manual/card-pick untouched) is code-verified in this audit — only the human observation is outstanding. Tracked as a `uat_gap` row in `STATE.md:170`.
- **`apply-message-batching.py` / `verify-message-batching.py` code-review warnings (from the committed `10-REVIEW.md`, reproduced in `10-VERIFICATION.md`):** WR-01 (empty `combinedText` instead of `null` in a specific humanizer-overlap interleaving), WR-02 (mixed-type bursts drop the earlier fragment), WR-03 (no `retryOnFail` on `Fetch Recent`), WR-04 (implicit `pairedItem` reliance) — all 4 are advisory hot-path hardening recommendations, none reproduced as a functional failure in the live runData/UAT evidence gathered this phase, and none map to a registered STRIDE threat in any of the four plans' threat models. Not re-litigated here; carried as advisory debt in `10-REVIEW.md`.
- **Unregistered threat flags:** none. All four plan SUMMARYs (`10-01` through `10-04`) were checked for a `## Threat Flags` section — none present (`grep -n "## Threat Flags" .planning/phases/10-message-batching-debounce/10-0*-SUMMARY.md` → no match).

---

## Security Audit 2026-07-22

| Metric | Count |
|--------|-------|
| Threats found | 14 |
| Closed | 14 |
| Open | 0 |

Auditor: gsd-security-auditor (first `SECURITY.md` for this phase, built from the 4 plans' threat models — 10-01 through 10-04). Verification was evidence-grounded: `mitigate` threats verified by direct read of the implementation this session — both bot template JSONs (`Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json`, `4VN3gsFaC2HUYmcc-Telegram_Bot.json`), `Tools/n8n/verify-message-batching.py` (re-run, exit 0), `Assets/Scripts/Chat/IncomingDebounceGate.cs`, `Assets/Scripts/Chat/SuggestionsController.cs`, and `Assets/Tests/Editor/Chat/IncomingDebounceGateTests.cs`; `accept`-disposition threats closed by authoring the Accepted Risks Log above, per the `R-09-*` precedent established in `09-SECURITY.md`; live-instance-only claims (runData id-equality, clone deactivation, device UAT verdicts) accepted from the owner-recorded evidence in `10-03-SUMMARY.md`, `10-04-SUMMARY.md`, and `10-HUMAN-UAT.md` per the audit's environment constraints — no live n8n/device access from this session. One threat (T-10-04-02) is closed-with-residual: its structural mitigation is directly verified, but the on-device behavioral confirmation is owner-deferred tracked debt (not an implementation gap) — documented above rather than silently marked fully closed.

---

## Sign-Off

- [x] All threats have a disposition (mitigate / accept / transfer)
- [x] Accepted risks documented in Accepted Risks Log
- [x] `threats_open: 0` confirmed
- [x] `status: secured` set in frontmatter

**Approval:** verified 2026-07-22

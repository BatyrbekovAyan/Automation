# Phase 8: Device UAT + Milestone Closeout - Context

**Gathered:** 2026-07-15
**Status:** Ready for planning
**Source:** In-session synthesis (all phases 3–7 code-complete + green; this phase owns NO new v1.1 REQ — it closes carried device-UAT gates + the prod-replication checklist + the milestone).

<domain>
## Phase Boundary

The final phase: validate the whole Telegram-parity surface ON DEVICE, replicate the dev n8n work to the dormant prod bagkz instance, and formally close milestone v1.1. Almost NO new code — the deliverables are (a) a consolidated owner-run device-UAT runbook that aggregates every open gate, (b) a prod-replication runbook (+ any small deployer/verifier parity tweaks the copy needs), and (c) the milestone-close mechanics. Any DEFECTS the device pass finds become their own gap-closure plans (the 05-07/08/09 pattern), not part of this phase's scope up front.

Environment: dev n8n + tunnel are owner-run; prod bagkz is DORMANT and stays that way until the owner signs off. Unity Editor may be open (headless test/builder runs gate on the project lock — the sanctioned in-Editor bridge is the fallback).

</domain>

<decisions>
## Implementation Decisions

### 08-01 — Consolidated device-UAT runbook (owner-run gate)
- Produce ONE checklist doc, `08-DEVICE-UAT.md`, that aggregates every still-open device-verify item across the milestone into a single ordered pass the owner runs on a real build. Pull from (verbatim, with source refs):
  - **Auth:** Telegram phone/code + **2FA cloud-password live round-trip** (both code + QR flows) — 05-VERIFICATION human item.
  - **Chat client:** list (names/avatars/unread/time), paginated history, media render — **image / video / voice(ptt) / document**, and the **05-07/08 treatments: `.tgs` → sticker CARD, video note → bubble-free circle + duration badge, GIF → GIF badge**; send text/media/quoted-reply; send+remove reaction; incoming reply card; mark-read; swipe-delete hidden on TG.
  - **05-09 fixes:** Bot-settings Telegram number shows a clean phone (or hides, then repopulates on toggle) — NOT the JSON blob; switcher chip labels have side padding; a healthy authorized TG bot does NOT trip outside-app de-auth on BotSettings open.
  - **vthumb id-ambiguity probe** (two TG dialogs on one profile with overlapping short message ids → correct video thumbnails) — 05-06-REVIEW WR-02.
  - **Video-note `is_round` re-capture** confirm (05-08) — optional, minor.
  - **Switcher (Phase 6):** segmented flip mid-flight-safe (no crossed lists), muted chip → connect empty state, per-bot channel persists across restart, 4-tab bar reads «Чаты», Telegram tab gone.
  - **Auto-reply e2e (Phase 4 TPL-06 conversation):** create a TG bot from the app → text/voice/multi-turn-memory reply; **pre-auth-file RAG re-stamp** retrievable; clone **deactivated after** the window; recreate any stale api/sync clone.
  - **«Вместе» live on TG + Dashboard live-data (Phase 7):** suggestions populate + RAG-grounded via `botTgId` in a real TG chat; «Сводка» counts/lists a real TG conversation, one chip per dual-channel bot, TG row deep-links.
  - **Carried v1.0 (Phases 01/02):** run or explicitly re-defer the 3 partial device-UAT scenarios + the 01-VERIFICATION device confirmation.
- Structure: grouped, each item = expected / how-to / PASS·FAIL·N/A, with a "defects found" section. `autonomous: false` — the doc is written autonomously, but RUNNING it is the owner gate; the phase stays open (human_needed) until the owner records results. Any FAIL spins a gap-closure plan.
- No app code changes in this plan (unless the runbook authoring surfaces a trivially-obvious pre-device fix — then flag it, don't fold it in).

### 08-02 — Prod bagkz replication runbook (+ parity tooling)
- Prod bagkz n8n is DORMANT. Deliver `08-PROD-REPLICATION.md`: the exact, ordered steps to bulk-copy ALL dev workflow changes to prod, plus any small deployer/verifier tweaks needed so the copy is scriptable + verifiable. What must land on prod (all committed in `Tools/n8n/workflows/`): the fixed **Telegram_Bot** template (tapi bases, text routing, length_seconds, chatId sessionKey), **both Create orchestrators** (RAG re-stamp, opposite-channel workflow id, `.first()` response), **Suggest_Replies** (channel-branched RAG), and the already-shipped Dashboard/Upload/Delete/OrphanSweep family.
- Locked invariants to bake into the runbook (from memory `n8n-dev-setup`, `dashboard-svodka-rollout`, `n8n-supabase-postgres-creds`):
  - Import workflows as `{name, nodes, connections, settings}` only — the UI export strips the top-level id; recreate creds **by NAME** on Cloud (Postgres via Session pooler 5432 NOT 6543; Supabase host = bare `https://<ref>.supabase.co` + legacy service_role JWT; OpenAI). Activate with an explicit `Content-Type: application/json`.
  - **Keep both bot templates INACTIVE** (shared webhook path `0091024b-7b46`); only per-bot clones go active. Prod stays DORMANT — a single bulk copy after dev sign-off, no live prod bot traffic this phase.
  - `Suggest_Replies` replication via `Tools/n8n/build-suggest-replies.py` (creds by name); run `Tools/n8n/verify-telegram-parity.py` against the prod export as the go/no-go gate.
  - Prod Supabase needs the `documents` RAG scoping + the `conversation_outcomes` migration + the Postgres `vvRrFiEXzLVqKjOx`-equivalent cred with UPDATE grant on `documents` (the re-stamp pre-flight).
  - Security follow-up flagged for prod (not blocking the copy): header-auth on the Dashboard/Suggest webhook family before real prod traffic (carried from v1.0 R-02-01 + dashboard pre-prod note).
- `autonomous: false` for the actual prod deploy (owner runs it against dormant prod); the runbook + any tooling parity tweaks are autonomous. This plan may include a small autonomous task if a deployer/verifier script needs a prod-mode flag or a Telegram-workflow entry — otherwise it's doc-only.

### 08-03 (or folded into close) — Milestone close
- After the owner signs off device UAT + prod copy (or explicitly defers residuals): run the GSD milestone-complete mechanics — mark v1.1 done, move validated Active→Validated in PROJECT.md, archive phases, update MILESTONES.md/ROADMAP. Carried deferred items that remain open (server-side «Вместе» suppression, any re-deferred v1.0 UAT) roll forward explicitly.

### Claude's Discretion
- Whether prod-replication tooling needs a code task or is doc-only; whether milestone-close is its own plan or folded into 08-02's tail; exact runbook grouping/ordering.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning.**

- `.planning/ROADMAP.md` (Phase 8 section — goal + success criteria), `.planning/REQUIREMENTS.md` (v2/carried items: SUPPRESS-01, PROD-01), `.planning/PROJECT.md`
- Open gate docs to aggregate: `.planning/phases/0{4,5,6,7}-*/0{4,5,6,7}-HUMAN-UAT.md`, `.planning/phases/05-*/05-VERIFICATION.md`, `.planning/phases/05-*/05-0{6,7,9}-REVIEW.md` (the device-reverify items), and carried `.planning/milestones/v1.0-phases/0{1,2}-*/0{1,2}-HUMAN-UAT.md` + `01-VERIFICATION.md`
- Prod-replication tooling: `Tools/n8n/build-suggest-replies.py`, `Tools/n8n/verify-telegram-parity.py`, `Tools/n8n/rotate-tunnel.py`, `Tools/n8n/README.md`, `Tools/n8n/workflows/` (canonical JSONs)
- Memory (gotchas): `n8n-dev-setup`, `dashboard-svodka-rollout` (pre-prod steps), `n8n-supabase-postgres-creds`, `rag-scoping-architecture`, `bot-activation-policy` (clones stay inactive), `telegram-parity-rollout` (full open-gates list)
- `.claude/rules/networking.md` (if any deployer code touched)

</canonical_refs>

<specifics>
## Specific Ideas

- The device runbook is the single source of truth for "is v1.1 shippable" — it must be complete enough that a FAIL is unambiguous and traceable to a fix.
- Prod copy is a ONE-SHOT after dev sign-off; the runbook must be idempotent-safe and never activate a bot clone.
- Do not run `verify-telegram-parity.py` or any deployer against PROD from here — those are owner-run steps in the runbook (secrets deny-ruled; prod is live infra).
</specifics>

<deferred>
## Deferred Ideas

- Server-side «Вместе» suppression (SUPPRESS-01) — future milestone, not v1.1 close.
- Real `.tgs` Lottie animation (v2), incoming-reaction chat-list preview, per-channel «Вместе» default — polish/ v2 backlog.
- Webhook header-auth hardening — flagged in the prod runbook as a pre-real-traffic item, not a copy blocker.

</deferred>

---

*Phase: 08-device-uat-milestone-closeout*
*Context gathered: 2026-07-15 (in-session synthesis)*

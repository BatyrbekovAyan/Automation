# Phase 7 — Human UAT Gate: «Вместе» Suggestions on Telegram (SUGG-01 / SUGG-02 live proof)

**Status:** RESOLVED (2026-07-21) — reconciled per owner decision; see closure block below.

> ## Reconciliation closure — 2026-07-21
>
> **Owner decision (2026-07-21):** "yes, close Group 1 and 2. Group 3 i will close later after finish phase 10 and 11."
>
> This is a **Group 1** item (live TG suggestions + dashboard). Most of it was in fact device-verified in the shared dev-n8n window recorded in 08-DEVICE-UAT §G/§H — those rows are `[resolved — superseded]` with the specific PASS cited; only the RAG-grounded-with-seeded-data proof was never run and is `[waived — owner 2026-07-21]`. **No item is marked PASS that was not actually verified.**
>
> **Disposition tally:** 5 resolved—superseded (suggestions populate §H#1, clone-inactive via G6 "done" + standing bot-activation policy, «Сводка» TG rows §H#3, one-chip-per-dual-bot §H#4, TG deep-link §H#5) · 1 waived (RAG-grounded SUGG-02 — never run with seeded RAG data on the TG bot).

## What is already verified (no human needed)

The **client** half of SUGG-01/SUGG-02 is verified here at the **payload / unit
level** by the headless EditMode suite — no live n8n, no real account:

- `07-01` made `N8nSuggestionsProvider.BuildPayloadJson` channel-aware and extended
  the frozen wire contract **additively** (`SuggestRepliesRequestDto` gains
  `botTgId` + `channel`; every v1 key byte-identical). `Run()` reads
  `ChatManager.ActiveChannel` and passes both channel id-pairs into the pure builder.
- `SuggestRepliesPayloadTests` (23/23 green) locks the channel-selection matrix
  (WA chat / TG chat / TG-only bot), `botWaId` always-present (backward compat),
  `botTgId == telegramWorkflowId`, the `""`/`"-1"` skip-RAG sentinel passthrough,
  the **lowercase enum-derived** `channel` (T-07-01-01), and the
  **additive-identity** invariant — strip `channel`+`botTgId` and the residual
  deep-equals a hand-built v1 JObject with exactly the frozen 12-key set.

**This phase's automated gate is that payload matrix (Tasks 1–2).** No scene
(`Main.unity`) changes, no builder runs, no n8n edits — the server half (the
channel-branched RAG `botWaId | botTgId` node + the `channel`-defaulting Prep)
already shipped in **Phase 4**.

## What still needs a human (this gate)

The **live end-to-end RAG-grounding proof** — a real Telegram chat producing
grounded suggestions — is **owner-gated** and **rides the Phase-4 TPL-06
dev-n8n session** (`04-HUMAN-UAT.md`). Claude cannot run it: it needs the dev
n8n at `localhost:5678` + a `cloudflared` tunnel + a **real authorized Telegram
profile**, and `secrets.json` (the n8n API key) is deny-ruled for Claude.

> Run this **inside** the same dev-n8n window as the TPL-06 runbook — the deployed
> `9PTyYcelRQI7bGDb` Suggest_Replies workflow (channel-branched RAG) and an
> authorized Telegram profile are the shared prerequisites. Don't spin up a
> separate session for it.

### Owner checklist (run during the TPL-06 dev-n8n session)

- [x] **Suggestions populate on Telegram.** Open a Telegram chat on a *[resolved — superseded: 08-DEVICE-UAT §H #1 "Suggestions populate in a real Telegram chat" PASS]*
      **Telegram-authed** bot, toggle «Вместе» (or open a chat while «Вместе» is
      the bot default) → **4 suggestions populate**. This proves the payload carried
      `channel == "telegram"` and `botTgId == telegramWorkflowId` and the server
      accepted it. ☑ PASS (via 08-DEVICE-UAT §H #1)
- [x] **Suggestion is RAG-grounded for a TG-only bot (SUGG-02).** On a bot whose *[waived — owner 2026-07-21: never run with seeded RAG data on the TG bot; the relevance path was fixed (D5 core round 1 / D10 round 2) but explicit catalog-citation grounding was not device-confirmed]*
      **price-list/catalog is seeded** in the RAG store, confirm a suggestion
      reflects that catalog/price-list content → proves the server's **`botTgId`
      RAG branch matched** (not the WA branch, not skip-RAG). If no RAG data is
      seeded for that bot, record **PENDING** (grounding cannot be judged without
      indexed data — re-run after an Upload File on the TG bot).
      waived — owner 2026-07-21 (dev-only, prod parked)
- [x] **Clone stays INACTIVE outside the test window.** Per project policy, bot *[resolved — superseded: standing bot-activation policy; owner confirmed "G6 done" 2026-07-20 (08-DEVICE-UAT §G6) — dev clone deactivated, prod untouched]*
      workflow clones run against **real contacts** — activate only for the test,
      **DEACTIVATE** after. Confirm prod bagkz was **not** touched. ☑ confirmed (G6 done)

## Result (owner marks)

- **Overall:** RESOLVED via reconciliation 2026-07-21 (5 resolved—superseded, 1 waived — not a fresh live PASS).
- **suggestions populate on Telegram:** resolved — superseded (08-DEVICE-UAT §H #1 PASS).
- **RAG-grounded suggestion (SUGG-02):** waived — owner 2026-07-21 (never run with seeded RAG data).
- **Notes:** Group 1 closure. Dev-only operation; prod bagkz parked per owner. Nothing marked PASS that was not actually verified on device.

## Blocks

This gate proves **SUGG-01/SUGG-02 live**. The agent-side client work (`07-01`) is
**code-complete and unit-green before this gate runs** — this runbook only proves
it against the dev Suggest_Replies workflow on a real Telegram chat. A green (or
PARTIAL-with-PENDING-grounding) pass here, together with the TPL-06 pass it rides
on, is what closes the live-verification obligation for Phase 7's suggestions half.

---
*Gate for Phase 07 («Вместе» suggestions + dashboard on Telegram) — suggestions
half. Do NOT tick these on the owner's behalf; this is a live-account, human-run
verification riding the Phase-4 TPL-06 dev-n8n session.*

### Dashboard live-data pass (added at phase verification — rides the same dev session)

- [x] **TG rows appear:** after the TPL-06 e2e produces a real Telegram conversation, trigger «Сводка» refresh → the Telegram chat appears in counts + recent list (classification via DashboardOutcomes now receives both channels' profile ids). *[resolved — superseded: 08-DEVICE-UAT §H #3 "«Сводка» counts/lists the real Telegram conversation" PASS]*
- [x] **One chip per dual-channel bot:** a bot with both channels shows exactly ONE filter chip; selecting it shows BOTH channels' rows. *[resolved — superseded: 08-DEVICE-UAT §H #4 PASS]*
- [x] **TG deep-link:** tapping a Telegram outcome row lands in that Telegram chat («Чаты» tab, Telegram channel selected, chat open). *[resolved — superseded: 08-DEVICE-UAT §H #5 PASS]*

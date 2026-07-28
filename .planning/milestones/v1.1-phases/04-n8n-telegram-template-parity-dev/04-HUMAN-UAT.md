# Phase 4 — Human UAT Gate: n8n Telegram Template Parity + RAG Re-stamp (TPL-06)

**Status:** RESOLVED (2026-07-21) — reconciled per owner decision; see closure block below.

> ## Reconciliation closure — 2026-07-21
>
> **Owner decision (2026-07-21):** "yes, close Group 1 and 2. Group 3 i will close later after finish phase 10 and 11."
>
> This gate is a **Group 1** item (live dev-n8n e2e never run). Context: dev-only operation, the deploy-half was done 2026-07-13 (owner flipped the 4 "Available in MCP" toggles + n8n-mcp deploy), prod bagkz parked per owner, and seven owner device rounds passed (08-DEVICE-UAT Gate A — round 7, 2026-07-21). Every checkbox below is ticked with one of exactly two honest dispositions — `[resolved — superseded]` (substance verified elsewhere, cited) or `[waived — owner 2026-07-21]` (never run anywhere). **No item is marked PASS that was not actually verified.**
>
> **Disposition tally:** 3 resolved—superseded (text e2e via Gate A §G#1, clone-deactivation via G6 "done", prod-untouched vacuously true) · 10 waived (dev-n8n setup rows + voice / memory / pre-auth-file re-stamp e2e).

Phase 4 is **code-complete** once the agent-side work is committed and the
structural verifier is green: `04-01` fixed the four canonical workflow JSONs
(Telegram_Bot onto tapi + RAG re-stamp in both Create orchestrators +
Suggest_Replies channel branch) and `04-02` Task 1 added the opposite-channel
form fields on the Unity side (`WhatsappWorkflowId` / `TelegramWorkflowId` in all
four create-workflow coroutines). The phase **closes** only after the owner
deploys those workflows to the **dev** n8n and proves the whole flow end-to-end
against a real authorized Telegram profile.

Running this is **not** a task in the plan — it is this human gate, because the
dev n8n is not running and its API key lives in deny-ruled `secrets.json`
(Claude cannot start n8n, open the tunnel, or drive the app).

> **Blocks:** this gate closes **TPL-06** and, with it, Phase 4. Prod bagkz stays
> **dormant** (one bulk replication is a Phase 8 checklist item — do NOT touch prod here).

## Workflows in scope (import/update by LITERAL id)

| id | name | note |
|----|------|------|
| `4VN3gsFaC2HUYmcc` | Telegram_Bot | clone source — keep **INACTIVE** (shares webhook path `0091024b-7b46`) |
| `Uz6HBBUpAiUqVysB` | CreateTelegramWorkflow | now re-stamps `botTgId` on late TG auth |
| `XuvOp7TxOImOAmlj` | CreateWhatsappWorkflow | mirror re-stamp of `botWaId` |
| `9PTyYcelRQI7bGDb` | Suggest_Replies | additive channel-branched RAG filter |

> ⚠️ Never change these ids on import — the two Create handlers reference the bot
> templates by literal id, and the app hits the webhook paths by name. Keep BOTH
> bot templates (`4wYitz5ek30SVNlT` WhatsApp + `4VN3gsFaC2HUYmcc` Telegram)
> **inactive** — only per-bot clones (with rewritten paths) ever go active.

## Owner runbook (6 locked steps)

### 1. Pre-flight — verifier + Postgres credential resolve/UPDATE check

- [x] Run the structural verifier — **must exit 0** before deploying: *[waived — owner 2026-07-21: dev-n8n setup step, never run live]*
      ```bash
      python3 Tools/n8n/verify-telegram-parity.py
      ```
- [x] On the dev n8n, confirm the executeQuery Postgres credential *[waived — owner 2026-07-21: dev-n8n editor step never run live; cred RESOLVES + BINDS confirmed 2026-07-13 via no-op setNodeCredential republish]*
      (id `vvRrFiEXzLVqKjOx`, name **"Postgres"**) resolves on the
      `Restamp RAG Chunks` node in BOTH Create orchestrators, and that its DB role
      can `UPDATE documents`. Quick check: open `CreateTelegramWorkflow`
      (`Uz6HBBUpAiUqVysB`), execute the `Restamp RAG Chunks` node once in the n8n
      editor with harmless params (e.g. `$2 = "-1"`). A **no-match UPDATE returning
      0 rows with NO credential/permission error passes** — a "credential not found"
      or "permission denied for table documents" FAILS this gate (fix the cred /
      grant before continuing).

### 2. Start dev n8n + tunnel, then re-point callbacks

- [x] Start the dev n8n at `localhost:5678` and a `cloudflared` quick tunnel. *[waived — owner 2026-07-21: dev-n8n setup, never run live]*
- [x] Run: *[waived — owner 2026-07-21: dev-n8n setup, never run live]*
      ```bash
      python3 Tools/n8n/rotate-tunnel.py
      ```
      to re-point `secrets.json`, the local Create handlers' Wappi callback, and
      every bot's Wappi webhook registration to the fresh trycloudflare host, then
      let it verify. **Do not skip this** — a missed rotate step is exactly what
      caused the 2026-07-03 "bots stopped replying" outage.

### 3. Import/update the 4 workflows by literal id (templates stay inactive)

> ✅ **DONE 2026-07-13 by Claude via n8n-mcp** (owner flipped the 4 "Available in
> MCP" toggles; live state backed up first, then targeted node ops, then publish,
> then re-fetch + structural asserts — ALL GREEN):
> - `4VN3gsFaC2HUYmcc` Telegram Bot: 3 tapi outbound URLs, no `mark_all`,
>   `chat OR text` on both Switches, `length_seconds` fallback, `chatId` sessionKey.
>   Node order preserved (nodes[0]=Webhook, nodes[5]=AI Agent). **Stays INACTIVE.**
> - `Uz6HBBUpAiUqVysB` + `XuvOp7TxOImOAmlj`: `Restamp RAG Chunks` inserted
>   (parameterized, sentinel-guarded, cred `vvRrFiEXzLVqKjOx` — binding confirmed
>   via no-op `setNodeCredential` republish), `Send New Workflows Id` → `.first()`.
>   Both **published** (drafts == active versions).
> - `9PTyYcelRQI7bGDb` Suggest Replies: channel-aware Prep, `If channel TG?` +
>   `Retrieve RAG TG` (botTgId, single-key), channel-neutral Assemble. **Published**
>   — note this also published your pending draft tweak (`If ok?` typeValidation
>   loose→strict; behaviorally identical for booleans).
> - Untouched on purpose: localhost self-API URLs, the (stale) tunnel host in
>   `Set Wappi Webhook` — step 2's `rotate-tunnel.py` owns those.
> - Pre-flight note: the credential RESOLVES and BINDS; the `UPDATE documents`
>   GRANT probe in step 1 is still worth one click in the editor.

- [x] Import/update all 4 workflows above by their **literal ids**. *(via MCP, 2026-07-13)*
- [x] Keep the two bot templates **INACTIVE** (shared webhook path `0091024b-7b46`;
      only per-bot clones go active). *(verified: Telegram Bot `active: false`)*
- [x] **Recreate any pre-existing dev Telegram clone** — old clones carry the wrong *[waived — owner 2026-07-21: never run; ledger G5 = N/A "couldn't test" (no stale clone at hand)]*
      `api/sync` outbound URLs and will silently fail to reply. Delete the old clone;
      a fresh bot-create off the fixed template produces a correct `tapi/sync` clone.
      *(No TG clones existed on dev as of the 2026-07-13 workflow list — only recheck
      if you created one since.)*

### 4. Authorize a dev Telegram profile + create a Telegram bot

- [x] Authorize a **dev** Telegram profile in-app (the same one Phase 3 needs). *[waived — owner 2026-07-21: dev-n8n setup, never run live]*
- [x] Create a Telegram bot from the app so a fresh clone off the fixed *[waived — owner 2026-07-21: dev-n8n setup, never run live]*
      `Telegram_Bot` template is generated and activated for the test window.

### 5. Conversation e2e — record PASS/FAIL for each

- [x] **text:** send a text message to the Telegram-authed bot → an AI reply arrives *[resolved — superseded: 08-DEVICE-UAT Gate A §G #1 "Text auto-reply arrives in Telegram" PASS; D10 relevance verified round 2]*
      **in Telegram**. (Proves tapi outbound `message/send` + `type:"text"` routing
      through the AI agent, not the fallback.)
- [x] **voice:** send a voice message → transcription + a humanized listening pause *[waived — owner 2026-07-21: dev-n8n live e2e never run anywhere; dev-only, prod parked]*
      (the `length_seconds` fallback when `media_info.duration` is absent) → reply
      arrives.
- [x] **memory:** a multi-turn exchange stays coherent — no context fragmentation *[waived — owner 2026-07-21: dev-n8n live e2e never run anywhere; dev-only, prod parked]*
      (session key is `profile_id + ':' + chatId`, stable on tapi).
- [x] **pre-auth file re-stamp:** upload a price-list file to a bot **BEFORE** authing *[waived — owner 2026-07-21: dev-n8n live e2e never run anywhere; dev-only, prod parked]*
      Telegram (chunks land with `botTgId = "-1"`), then auth Telegram / create the TG
      workflow, then ask a **price question in Telegram** → the answer is grounded in
      that file (chunks re-stamped from `"-1"` to the new TG workflow id).
      Supabase spot-check (replace `<newTgWorkflowId>` with the created TG workflow id):
      ```sql
      select count(*) from documents where metadata->>'botTgId' = '<newTgWorkflowId>';
      ```
      Expect the count **> 0**.

### 6. Deactivate the clone after the test window

- [x] **DEACTIVATE** the per-bot Telegram clone once testing is done. Bot workflows *[resolved — superseded: owner confirmed "G6 done" 2026-07-20 (08-DEVICE-UAT §G6, commit 7c1ad48) — dev test clone deactivated]*
      stay inactive except during active testing — they run against **real contacts**.
- [x] Confirm prod bagkz was **not** touched (it stays dormant until Phase 8). *[resolved — superseded: vacuously true — nothing touched prod bagkz; replication parked per owner]*

## Result (owner marks)

- **Overall:** RESOLVED via reconciliation 2026-07-21 (not a live PASS — dispositioned, see closure block).
- **text:** resolved — superseded (08-DEVICE-UAT Gate A §G #1 PASS).
- **voice:** waived — owner 2026-07-21 (dev-n8n e2e never run anywhere).
- **memory:** waived — owner 2026-07-21 (dev-n8n e2e never run anywhere).
- **pre-auth file re-stamp:** waived — owner 2026-07-21 (dev-n8n e2e never run anywhere).
- **Notes:** Group 1 closure. Dev-only operation; prod bagkz parked per owner. Nothing marked PASS that was not actually verified on device.

## Blocks

This gate **closes TPL-06 and Phase 4**. Agent-side work (`04-01` workflow fixes +
`04-02` Task 1 Unity form fields) is **code-complete before this gate runs** — this
runbook only proves it live on dev. When all boxes are ticked and Overall = PASS,
Phase 4 is closed.

---
*Gate for Phase 04 (n8n Telegram Template Parity, dev). Do NOT tick these on the
owner's behalf — this is a live-account, human-run verification against dev n8n.*

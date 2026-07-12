# Phase 4 — Human UAT Gate: n8n Telegram Template Parity + RAG Re-stamp (TPL-06)

**Status:** OPEN (owner-run) — this gate CLOSES the phase.

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

- [ ] Run the structural verifier — **must exit 0** before deploying:
      ```bash
      python3 Tools/n8n/verify-telegram-parity.py
      ```
- [ ] On the dev n8n, confirm the executeQuery Postgres credential
      (id `vvRrFiEXzLVqKjOx`, name **"Postgres"**) resolves on the
      `Restamp RAG Chunks` node in BOTH Create orchestrators, and that its DB role
      can `UPDATE documents`. Quick check: open `CreateTelegramWorkflow`
      (`Uz6HBBUpAiUqVysB`), execute the `Restamp RAG Chunks` node once in the n8n
      editor with harmless params (e.g. `$2 = "-1"`). A **no-match UPDATE returning
      0 rows with NO credential/permission error passes** — a "credential not found"
      or "permission denied for table documents" FAILS this gate (fix the cred /
      grant before continuing).

### 2. Start dev n8n + tunnel, then re-point callbacks

- [ ] Start the dev n8n at `localhost:5678` and a `cloudflared` quick tunnel.
- [ ] Run:
      ```bash
      python3 Tools/n8n/rotate-tunnel.py
      ```
      to re-point `secrets.json`, the local Create handlers' Wappi callback, and
      every bot's Wappi webhook registration to the fresh trycloudflare host, then
      let it verify. **Do not skip this** — a missed rotate step is exactly what
      caused the 2026-07-03 "bots stopped replying" outage.

### 3. Import/update the 4 workflows by literal id (templates stay inactive)

- [ ] Import/update all 4 workflows above by their **literal ids**.
- [ ] Keep the two bot templates **INACTIVE** (shared webhook path `0091024b-7b46`;
      only per-bot clones go active).
- [ ] **Recreate any pre-existing dev Telegram clone** — old clones carry the wrong
      `api/sync` outbound URLs and will silently fail to reply. Delete the old clone;
      a fresh bot-create off the fixed template produces a correct `tapi/sync` clone.

### 4. Authorize a dev Telegram profile + create a Telegram bot

- [ ] Authorize a **dev** Telegram profile in-app (the same one Phase 3 needs).
- [ ] Create a Telegram bot from the app so a fresh clone off the fixed
      `Telegram_Bot` template is generated and activated for the test window.

### 5. Conversation e2e — record PASS/FAIL for each

- [ ] **text:** send a text message to the Telegram-authed bot → an AI reply arrives
      **in Telegram**. (Proves tapi outbound `message/send` + `type:"text"` routing
      through the AI agent, not the fallback.)
- [ ] **voice:** send a voice message → transcription + a humanized listening pause
      (the `length_seconds` fallback when `media_info.duration` is absent) → reply
      arrives.
- [ ] **memory:** a multi-turn exchange stays coherent — no context fragmentation
      (session key is `profile_id + ':' + chatId`, stable on tapi).
- [ ] **pre-auth file re-stamp:** upload a price-list file to a bot **BEFORE** authing
      Telegram (chunks land with `botTgId = "-1"`), then auth Telegram / create the TG
      workflow, then ask a **price question in Telegram** → the answer is grounded in
      that file (chunks re-stamped from `"-1"` to the new TG workflow id).
      Supabase spot-check (replace `<newTgWorkflowId>` with the created TG workflow id):
      ```sql
      select count(*) from documents where metadata->>'botTgId' = '<newTgWorkflowId>';
      ```
      Expect the count **> 0**.

### 6. Deactivate the clone after the test window

- [ ] **DEACTIVATE** the per-bot Telegram clone once testing is done. Bot workflows
      stay inactive except during active testing — they run against **real contacts**.
- [ ] Confirm prod bagkz was **not** touched (it stays dormant until Phase 8).

## Result (owner marks)

- **Overall:** ☐ PASS ☐ FAIL
- **text:** ☐ PASS ☐ FAIL
- **voice:** ☐ PASS ☐ FAIL
- **memory:** ☐ PASS ☐ FAIL
- **pre-auth file re-stamp:** ☐ PASS ☐ FAIL
- **Notes:**

## Blocks

This gate **closes TPL-06 and Phase 4**. Agent-side work (`04-01` workflow fixes +
`04-02` Task 1 Unity form fields) is **code-complete before this gate runs** — this
runbook only proves it live on dev. When all boxes are ticked and Overall = PASS,
Phase 4 is closed.

---
*Gate for Phase 04 (n8n Telegram Template Parity, dev). Do NOT tick these on the
owner's behalf — this is a live-account, human-run verification against dev n8n.*

# Phase 8 — Prod Replication Runbook: one-shot bulk copy to dormant bagkz (PROD-01)

**Status:** OPEN (owner-run) — running this end-to-end is Task 3 of plan 08-02. Writing it was
autonomous; the deploy itself is owner-only (prod `secrets.json` + the prod n8n API key are
deny-ruled from Claude, and prod is live infra).

> **PROD STAYS DORMANT — no bot clone is created or activated this phase.** This is a SINGLE
> idempotent-safe bulk copy of every dev workflow change onto the dormant prod bagkz Cloud
> instance after dev sign-off. There is **zero live prod bot traffic** this phase. Both bot
> templates land **INACTIVE**; the only things that go active are the shared webhook workflows
> (always-active by design — they are NOT per-bot bots) and the hourly Orphan-sweep schedule.
> A per-bot clone is **never** created or activated here.

---

## What must land on prod

Everything the v1.1 milestone shipped on dev, all committed under `Tools/n8n/workflows/`:

- The fixed **Telegram_Bot** template (`4VN3gsFaC2HUYmcc`) — tapi outbound bases, `type:"text"`
  routing, `length_seconds` voice fallback, `chatId` sessionKey.
- **Both Create orchestrators** — the `Restamp RAG Chunks` node (parameterized, sentinel-guarded)
  that re-stamps late-channel RAG chunks, plus the `.first()` response fix.
- **Suggest_Replies** (`9PTyYcelRQI7bGDb`) — the channel-branched RAG filter (`botWaId | botTgId`).
- The already-shipped **Dashboard / Upload / Delete / DeleteBotFiles / Orphan-sweep** family.

## Environment (owner sets at deploy time — never committed)

- `N8N_BASE_URL=https://bagkz.app.n8n.cloud` (prod Cloud).
- `N8N_API_KEY=<prod bagkz API key>` — from prod n8n → Settings → API. Owner-held, **deny-ruled**.
- Prod **Supabase** SQL-editor access (project owner) for the migrations in step 3.
- The prod **Wappi** auth token (for the bot templates + Orphan sweep credential).

No secret VALUES appear anywhere below — every credential is referenced **BY NAME** only. Dev
reference ids are cited purely as traceability pointers (they are opaque n8n entity references,
already committed in `Tools/n8n/README.md` / `04-HUMAN-UAT.md`, and expose no token/JWT/password).

---

## Workflows in scope (import by LITERAL id — 12 canonical)

| id | name | role | prod active-state |
|----|------|------|-------------------|
| `XuvOp7TxOImOAmlj` | CreateWhatsappWorkflow | webhook `/webhook/CreateWhatsappWorkflow` — clones the WA template per bot | ACTIVE (webhook) |
| `Uz6HBBUpAiUqVysB` | CreateTelegramWorkflow | webhook `/webhook/CreateTelegramWorkflow` — clones the TG template per bot | ACTIVE (webhook) |
| `3qax5J9u2qsT9Vao` | Edit Whatsapp Workflow | webhook `/webhook/EditWhatsappWorkflow` — edits a bot's system prompt | ACTIVE (webhook) |
| `TwWPW3gIyjZS3foR` | Edit Telegram Workflow | webhook `/webhook/EditTelegramWorkflow` — edits a bot's system prompt | ACTIVE (webhook) |
| `KoTuIlk4LMrlvnWI` | Upload File | webhook `UploadFile` — RAG ingest; stamps `botWaId`/`botTgId`/`fileId`; archives originals | ACTIVE (webhook) |
| `ZTqpumOpL1rNDOp6` | Delete File | webhook `DeleteFile` — per-file chunk + stored-original delete | ACTIVE (webhook) |
| `4wYitz5ek30SVNlT` | WhatsApp Bot (`WhatsApp_Bot`) | **clone source** (literal-id ref in CreateWhatsappWorkflow); retrieval self-scoped by `botWaId` | **INACTIVE** ⚠ |
| `4VN3gsFaC2HUYmcc` | Telegram Bot (`Telegram_Bot`) | **clone source** (literal-id ref in CreateTelegramWorkflow); retrieval self-scoped by `botTgId` | **INACTIVE** ⚠ |
| `lmjYsdNcQA2IE5rl` | Delete Bot Files | webhook `DeleteBotFiles` — sweeps a deleted bot's chunks + stored originals | ACTIVE (webhook) |
| `2htWSV5IHO8E2CgB` | Dashboard Outcomes | webhook `DashboardOutcomes` — classifies `conversation_outcomes` for «Сводка» | ACTIVE (webhook) |
| `2islisFH7jjLoPQM` | Delete Orphan Profiles | **scheduled, hourly** (no webhook) — Wappi TTL sweep | ACTIVE (scheduled; see step 6) |
| `9PTyYcelRQI7bGDb` | Suggest Replies (`Suggest_Replies`) | webhook `SuggestReplies` — 4-move «Вместе» reply suggestions | ACTIVE (webhook; `build-suggest-replies.py` imports the committed canonical + rebinds creds, step 5) |

> ⚠ **`4wYitz5ek30SVNlT` (WhatsApp_Bot) and `4VN3gsFaC2HUYmcc` (Telegram_Bot) stay INACTIVE.**
> They share webhook path `0091024b-7b46`; only per-bot clones (with rewritten paths) ever go
> active — and **no clone is created this phase**. Never change these two ids on import: the two
> Create handlers reference the bot templates **by literal id**, so a changed id 404s bot creation.

**Idempotency note:** every step below is a re-runnable upsert (import/update by literal id, `CREATE
… IF NOT EXISTS` / additive migrations, cred bind by name). Re-running a step must not create a
second copy of anything. Import updates in place; the Suggest_Replies deployer is `--update`-safe.

---

## Ordered steps (run once, top to bottom; each has an owner-verifiable check)

### 1. Pre-flight go/no-go on the committed source

Before anything touches prod, prove the committed workflow JSONs still carry every parity fix:

```bash
python3 Tools/n8n/verify-telegram-parity.py     # reads committed workflows/, no network
```

- ☐ Prints **`ALL PARITY ASSERTS PASSED`** (exit 0). If it prints `PARITY FAIL: …`, **STOP** — the
  source is wrong; do not deploy.

### 2. Recreate credentials BY NAME on prod Cloud

Prod Cloud has none of the dev credentials. Recreate each **BY NAME** (n8n binds workflows to
credentials by id, but a same-name credential is what the imported nodes/deployer resolve to). Enter
the real secret VALUES directly in the prod n8n Credentials UI — **never in this doc**.

| Credential name | Type | Prod configuration | Used by |
|-----------------|------|--------------------|---------|
| **Postgres** | Postgres | **Session pooler host, port `5432`** — NOT `6543`/Direct. DB role MUST be able to `UPDATE documents` (this is the re-stamp cred; dev reference id `vvRrFiEXzLVqKjOx`). | `Restamp RAG Chunks` in both Create orchestrators; Dashboard/Delete executeQuery |
| **Supabase** | Supabase API | Bare host `https://<ref>.supabase.co` (no path) + the legacy **service_role** JWT. | Upload/Delete/DeleteBotFiles/Dashboard, Suggest_Replies RAG |
| **OpenAi account** | OpenAI API | Prod OpenAI key. **Record the NEW prod id** → feeds step 5's `--openai-cred`. | Bot templates, Upload vision, Suggest_Replies |
| **Supabase** (id) | — | **Record the NEW prod Supabase id** → feeds step 5's `--supabase-cred`. | Suggest_Replies RAG |
| **WappiAuthToken** | HTTP Header Auth | Prod Wappi token. The n8n MCP/SDK builder strips generic-auth on HTTP nodes — attach via `PUT /api/v1/workflows/{id}` after import (dev reference id `ZowntFGvApDJ7UzQ`). | Bot templates + Orphan sweep (step 6) |
| **Cohere** / **n8nAPIKey** | as needed | Recreate if the imported nodes reference them (reranker / self-API calls). | as referenced |

- ☐ All credentials above exist on prod, **BY NAME**, with prod secret values entered in the UI.
- ☐ **Postgres uses Session pooler port `5432`** (a `6543`/Direct binding will fail the re-stamp).
- ☐ Recorded the **NEW prod ids** for **OpenAi account** and **Supabase** (needed in step 5).
- ☐ No secret value was written into this runbook or any tracked file.

### 3. Apply the prod Supabase migrations + prove the re-stamp `UPDATE` grant

Apply (idempotent) in the prod Supabase SQL editor, once per project:

```
Tools/n8n/supabase/2026-07-02-harden-rag-store.sql            # documents RAG-scoping / RLS / indexes
Tools/n8n/supabase/2026-07-02-price-list-originals-bucket.sql # price-lists bucket (needed before Store Original File works)
Tools/n8n/supabase/2026-07-07-conversation-outcomes.sql       # conversation_outcomes table (Dashboard)
```

Then prove the **re-stamp `UPDATE` grant** — the pre-flight that guards late-channel RAG:

- Open `CreateTelegramWorkflow` (`Uz6HBBUpAiUqVysB`) in the prod editor and execute the
  `Restamp RAG Chunks` node **once** with the `-1` sentinel (`$2 = "-1"`). Because of the sentinel
  guard it matches zero rows.
- ☐ A **0-row `UPDATE` with NO credential/permission error PASSES.** A `permission denied for table
  documents` (or "credential not found") **FAILS** — fix the step-2 **Postgres** role's `UPDATE`
  grant on `documents` before continuing.
- ☐ `conversation_outcomes` exists and the `documents` RAG-scoping contract is present.

### 4. Import the 11 non-Suggest workflows (literal ids; templates INACTIVE)

Import each canonical JSON as `{name, nodes, connections, settings}` **preserving its literal
top-level id** (a UI drag-drop strips the top-level id → re-inject it, or use an id-preserving
import path). Import all workflows **except Suggest_Replies** here — that 12th one is deployed by
its own builder in step 5 (the only way to bind prod cred ids on a no-SQLite Cloud target).

- ☐ All 11 imported at their **literal ids** (see the scope table).
- ☐ **Both bot templates INACTIVE** (`4wYitz5ek30SVNlT`, `4VN3gsFaC2HUYmcc`; shared path `0091024b-7b46`).
- ☐ Create/Edit handlers point their clone/activate self-API calls at **prod
  `https://bagkz.app.n8n.cloud/api/v1/...`** (NOT the dev `localhost:5678` from `apply-dev-config.py`),
  and the Wappi callback is the **prod** host (NOT a `trycloudflare` tunnel host).
- ☐ Prod-pass cleanups applied (README follow-ups): the trailing-space `/activate ` URL in
  `CreateWhatsappWorkflow`; the Edit-handler `Set Bussiness Type` node-name typo + leftover unused
  credential refs.
- ☐ WappiAuthToken attached to the bot templates via `PUT /api/v1/workflows/{id}` (generic-auth
  survives REST, not the MCP/SDK builder).

### 5. Deploy Suggest_Replies via the deployer (with the prod cred-id overrides)

`Suggest_Replies` is the SHARED, always-active webhook — activating **it** is correct and is **NOT**
a bot clone. The deployer imports the **committed canonical JSON**
(`Tools/n8n/workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json` — channel-branched RAG + the 08-13
D10 «РЕЛЕВАНТНОСТЬ» newest-incoming anchor, both proven by the step-1 verifier) **verbatim**, and
rebinds ONLY the OpenAi/Supabase credential ids to the prod ids recorded in step 2. There is no
silent dev-id fallback: a no-SQLite Cloud target without both overrides is a **loud error**. (The
old `--stage full` generate mode is retired — it rebuilt a pre-parity graph from stale Python
literals and would have silently reverted the channel branch + D10; the script now refuses it.)

```bash
# optional offline preview — prints the exact payload with the prod cred ids bound; no network:
N8N_OPENAI_CRED_ID=<prod-openai-id> N8N_SUPABASE_CRED_ID=<prod-supabase-id> \
python3 Tools/n8n/build-suggest-replies.py --dry-run

N8N_BASE_URL=https://bagkz.app.n8n.cloud \
N8N_API_KEY=<prod-key> \
N8N_OPENAI_CRED_ID=<prod-openai-id> \
N8N_SUPABASE_CRED_ID=<prod-supabase-id> \
python3 Tools/n8n/build-suggest-replies.py
```

- ☐ Deployer prints `workflow created: id=<prod-id>` then `activated`. **Record `<prod-id>`** — it is
  reached by webhook PATH (`/webhook/SuggestReplies`), not by literal id, so a fresh prod id is fine;
  you export THIS id in step 7. (Equivalently, if you chose to import Suggest_Replies in step 4 to
  keep id `9PTyYcelRQI7bGDb`, append `--update 9PTyYcelRQI7bGDb` — a PUT of the same canonical
  content onto that id, creds rebound in place.)
- ☐ Its OpenAi/Supabase nodes bind the **prod** credential ids (visible in the `--dry-run` payload).
- ☐ **Seed RAG-with-data** on one prod bot (upload a real price-list) so grounding is testable — the
  deferred-from-dev item (dev RAG was catalog-only until `documents` were seeded).

### 6. Delete_Orphan_Profiles prod wiring

The hourly sweep needs only the Wappi credential — no webhook, no Supabase.

- ☐ Recreate **WappiAuthToken** (step 2) and repoint the sweep's **4 HTTP nodes'** credential id
  (dev reference id `ZowntFGvApDJ7UzQ`) to the prod credential.
- ☐ Import `2islisFH7jjLoPQM` with **fresh (empty) staticData** — the first-seen ledger must start
  clean so every existing prod orphan gets the full 24h grace (Wappi exposes no creation timestamp).
- ☐ **Activate** the schedule (this is a scheduled workflow, not a bot — activating it is correct).

### 7. Go/no-go post-import re-verify (the deploy gate)

Re-export the 4 parity workflows FROM prod by id and re-run the SAME structural asserts against the
prod copy — this catches a UI round-trip strip (dropped `ai_embedding` wiring, stripped top-level id,
dropped `mark_all` guard, wrong re-stamp binding). File names MUST match the verifier's expected names:

```bash
PROD=https://bagkz.app.n8n.cloud
OUT=/tmp/prod-export && mkdir -p "$OUT"
export N8N_BASE_URL=$PROD N8N_API_KEY=<prod-key>

python3 Tools/n8n/build-suggest-replies.py --export 4VN3gsFaC2HUYmcc "$OUT/4VN3gsFaC2HUYmcc-Telegram_Bot.json"
python3 Tools/n8n/build-suggest-replies.py --export Uz6HBBUpAiUqVysB "$OUT/Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json"
python3 Tools/n8n/build-suggest-replies.py --export XuvOp7TxOImOAmlj "$OUT/XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json"
python3 Tools/n8n/build-suggest-replies.py --export <prod-suggest-id> "$OUT/9PTyYcelRQI7bGDb-Suggest_Replies.json"

python3 Tools/n8n/verify-telegram-parity.py --dir "$OUT"
```

- ☐ `verify-telegram-parity.py --dir "$OUT"` prints **`ALL PARITY ASSERTS PASSED`**. This is the
  deploy **go/no-go** — a FAIL means re-import the offending workflow and re-run before proceeding.
  (`<prod-suggest-id>` = the id recorded in step 5; the export filename stays canonical so the
  verifier finds it.)

### 8. Security follow-up — FLAGGED, NOT a copy blocker

The Dashboard/Suggest webhook family is unauthenticated today (carried **R-02-01** + the dashboard
pre-prod note). Add **header-auth** on that webhook family **before real prod traffic**. Because prod
stays dormant with no live traffic this phase, this is a pre-real-traffic checklist item, **not** a
blocker for the bulk copy:

- ☐ (pre-real-traffic, not required to complete the copy) Add header-auth to the
  Dashboard/Suggest/Upload/Delete webhook family; rotate keys as needed.

### 9. Confirm prod stays DORMANT

- ☐ **No per-bot clone** was created or activated on prod during this copy.
- ☐ Both bot templates (`4wYitz5ek30SVNlT`, `4VN3gsFaC2HUYmcc`) are **INACTIVE**.
- ☐ Active on prod = only the shared webhooks + the hourly Orphan schedule (never a bot).
- ☐ **prod bagkz dormant — bulk copy only.**

---

## Owner result

Mark PASS/FAIL per major step; add anything notable to Notes.

- **Overall:** ☐ PASS ☐ FAIL
- **Step 1 — pre-flight verifier green:** ☐ PASS ☐ FAIL
- **Step 2 — creds recreated BY NAME (Postgres 5432, Supabase, OpenAi, Wappi):** ☐ PASS ☐ FAIL
- **Step 3 — Supabase migrations + re-stamp `UPDATE` grant proven:** ☐ PASS ☐ FAIL
- **Step 4 — 11 workflows imported at literal ids, templates INACTIVE:** ☐ PASS ☐ FAIL
- **Step 5 — Suggest_Replies deployed with prod cred ids + RAG seeded:** ☐ PASS ☐ FAIL
- **Step 6 — Orphan sweep wired (fresh staticData) + activated:** ☐ PASS ☐ FAIL
- **Step 7 — post-import `verify-telegram-parity.py --dir` go/no-go GREEN:** ☐ PASS ☐ FAIL
- **Step 8 — header-auth flagged as pre-real-traffic (not a blocker):** ☐ noted
- **Step 9 — prod confirmed dormant (no clone, templates inactive):** ☐ PASS ☐ FAIL
- **Notes:**

---

*Milestone close (**08-03**) gates on this runbook being **executed** (Overall = PASS, go/no-go
GREEN) **or explicitly deferred** (prod copy postponed, rolled forward with a reason). This is the
one-shot owner deploy referenced by 08-02 Task 3.*

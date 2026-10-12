# n8n workflows

Canonical n8n workflows for the BAGKZ app. The app talks to n8n for bot creation,
prompt editing, RAG file upload/delete, and (in progress) live reply suggestions.

## Layout

- `workflows/` — **committed source of truth**: the 19 workflows the app and the choosereply.com landing depend on.
  Each JSON has its original n8n `id` injected at the top level so it round-trips on import
  (including `SCLcpn6DMDG3Z4VN-Set_Reply_Mode.json`, whose id was assigned on first deploy 2026-07-22).
- `supabase/` — the RAG store's DB contract: `schema.sql` (documents table +
  `match_documents` as deployed — note its multi-key filter uses OR semantics), the
  applied hardening migrations (RLS default-deny, anon revoke, HNSW + metadata indexes),
  the `price-lists` originals bucket (`2026-07-02-price-list-originals-bucket.sql` —
  must be applied once per Supabase project before the Store Original File node works),
  and `audit-price-lists-bucket.sql` — cross-checks bucket objects against
  `documents.metadata->>'fileId'`. Invariant: zero `orphaned-unexpected`; image
  orphans are `orphaned-by-rejection` (422-rejected photos, kept for re-OCR by design).
- `build-suggest-replies.py` — deploys/exports the shared **Suggest Replies** workflow. Deploy
  imports the committed canonical `workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json` VERBATIM
  (Webhook → Prep → If invalid [known-garbage → straight to Build Response's `generation_failed`,
  zero LLM spend] → If skipRag → If channel TG? → channel-scoped RAG load [`botTgId`|`botWaId`] →
  Assemble [incl. the 08-13 D10 «РЕЛЕВАНТНОСТЬ» newest-incoming anchor] → LLM json_schema →
  Validate → retry-once → Build Response → Respond), rebinds ONLY the credential ids for the
  target, and activates it; `--update <id>` PUTs the same content onto an existing id; `--dry-run`
  prints the exact payload offline; `--export <id> <out>` re-emits the canonical JSON. Credential
  ids resolve flag/env (`--openai-cred`/`--supabase-cred`, `N8N_OPENAI_CRED_ID`/
  `N8N_SUPABASE_CRED_ID`) > exact-NAME lookup in the local SQLite (misnamed fails loudly listing
  candidates) > **loud error** — never a silent dev-id fallback. The old `--stage front/full`
  generator literals predated the phase-4 channel branch + D10 and are retired (deploying them
  silently reverted both); the flag now errors with guidance.
- `build-set-reply-mode.py` — deploys/exports the shared **Set Reply Mode** workflow (the 13th).
  Deploy imports the committed canonical `workflows/SCLcpn6DMDG3Z4VN-Set_Reply_Mode.json` VERBATIM
  (Webhook → Validate [malformed body → `bad_request` before any DB write] → If invalid? →
  one item per surviving profileId → Upsert into `reply_mode_flags` [on conflict do update] →
  Respond), rebinds ONLY the Postgres credential id, and activates it; `--update <id>` PUTs the
  same content onto an existing id; `--dry-run` prints the exact payload offline; `--export
  <id> <out>` re-emits the canonical JSON. The Postgres cred is bound by **explicit id**
  (default `vvRrFiEXzLVqKjOx`, override `--postgres-cred` / `N8N_POSTGRES_CRED_ID`) — never by
  name, and id-binding stays load-bearing for prod (whose cred ids differ). Ground truth
  2026-07-22: the dev instance has a SINGLE Postgres cred `vvRrFiEXzLVqKjOx`; the old
  `1H5xlpFSESU4w6JH` id from the C5 research/plan does not exist on the instance (importing a
  workflow that binds it would dangle). The DDL for `reply_mode_flags` lives in
  `supabase/2026-07-19-reply-mode-flags.sql` (apply through cred `vvRrFiEXzLVqKjOx`).
- `build-client-webhooks.py` — deploys/exports **Support Message** + **Set Workflow State** (the
  18th/19th), the two webhooks that stripped the n8n admin key and Telegram bot token out of the
  client binary (2026-08-31). PROD-first (default base `https://n8n.choosereply.com`, unlike the
  dev-first deployers). Creates the two server-side credentials on first `--deploy` (values from
  env or the gitignored `.secrets/support-bot.json`), remembers ids in
  `.secrets/client-webhooks-state.json`, masks secrets back to `__TG_CRED__`/`__HDR_CRED__`/
  `__CHAT_ID__` placeholders on export.
- `apply-*.py` — idempotent migrations over `workflows/` (edit by node name, re-runnable);
  `verify_rag.py` asserts every applied invariant; `test-upload-e2e.sh` exercises the
  Upload/Delete webhooks end-to-end against a live instance (curl mimicking Unity's
  WWWForm binary-part quirk).
- `rotate-tunnel.py` — run after every cloudflared quick-tunnel restart: auto-detects the
  new trycloudflare host and re-points secrets.json, the live local Create handlers'
  Wappi callback, and every bot's Wappi webhook registration, then verifies (see
  `dev-tunnel.md`). A missed manual step here caused the 2026-07-03 "bots stopped
  replying" outage.
- `reference/` — **gitignored**: downloaded community/marketplace templates + n8n onboarding
  samples, kept only to mine for ideas. Not part of the app, never imported.
- **Audit remediation toolset (2026-09-17, `docs/audits/implementation-progress.md`):**
  `staging/` — the isolated STAGING stand (private n8n home `~/.n8n-staging`, private Postgres 16 from
  the `pgserver` wheel on :5433, the **Wappi mock** on 127.0.0.1:8765, `derive-staging.py` which
  rewrites every external host and REFUSES a set that could reach prod or a real chat, `bootstrap.py`,
  run-scoped `fixtures.py`, `e2e.py` scenarios); `repro/run-code-node.mjs` — offline harness that runs the
  unmodified jsCode of the decision Code nodes against `repro/scenarios.json` (audit-defect scenarios are
  RED until their stage lands); `baseline-metrics.py` — read-only outcome/latency metrics of retained
  executions, no dialog text; `apply-outcome-stamps.py` — idempotent migration adding the terminal
  `Outcome` Code node to both bot templates (filtered/suppressed/superseded/quota_denied/sent, with
  `$execution.id` as correlationId). Stage 3 (`apply-reply-pipeline-v2.py`, verifier `verify-reply-pipeline.py`,
  SQL `sql/2026-09-17-reply-events.sql`, staging suite `staging/e2e-reply.py`): Claim Event / final check /
  send lease / settle + reconcile / record nodes on both bot templates — see CLAUDE.md «A3/A4/A7».
  Stage 4 (`apply-history-handoff.py`, `build-handoff-workflows.py`, verifier `verify-history-handoff.py`,
  SQL `sql/2026-09-17-history-handoff.sql`, staging suite `staging/e2e-handoff.py`): the one history
  (`chat_history_events`) with explicit sources, the `create_handoff` tool → `crHandoffReq0001`, the
  self-message notifier `crHandoffNtfy001`, the app webhook `crHandoffsApi001` — see CLAUDE.md «A5/A6».
  Stage 5 (`apply-suggest-v2.py`, SQL `sql/2026-09-17-suggestion-request-keys.sql`, staging suite
  `staging/e2e-suggest.py`, repro scenarios `prep_*` in `repro/scenarios.json`): Suggest Replies knowledge
  budget / subject recovery / exact match / server time / idempotent count / error kinds — see CLAUDE.md «S1/S3–S8».
  Stage 6 (`eval/scenarios.v1.json` + `eval/run-eval.py`, staging-only proxy `staging/workflows/stgEvalRunner001`
  from `eval/build-eval-runner.py`): the versioned synthetic eval set and the before/after runner (composed bot prompt
  + simulated tools; panel via the real webhook) — see CLAUDE.md «Prompt priority + the eval set».
  V9 (iteration 2026-09-28, `build-price-search.py`, test `tests/test_price_search.py`): the price-list search reconciles
  every hit with the catalog AT SEARCH TIME — `crPriceSearch001` (Guard → Search Price Lists → Reconcile Prices) is the
  bots' and the Test Dialog's «Supabase Vector Store» tool (a toolWorkflow of the same name; the catalog comes from the
  caller's published prompt), and the panel's Assemble runs the same `RECONCILE_JS` (`apply-suggest-v2.py`); a row that
  names a catalog article at another price gets «[Сверка с каталогом: …]» under it. Nothing is stored. V9-r2
  (2026-10-08, tests `tests/test_price_search.py` + `tests/test_suggest_price.py`): the firm «Цены расходятся» only on a
  POSITIVELY identified item without the catalog price on the row; analogues / accessories / kits / packs / other
  currencies / bare numbers — at most a soft mark; a row at the catalog price anywhere in one search result settles the
  article; the panel cuts its windows on line boundaries before marking and pushes «РАСХОЖДЕНИЕ ЦЕН» only for a firm mark.
  Staging: the sub-workflow reads the `documents` stub (`derive-staging.py isolate_supabase_readers`); `eval/run-eval.py`
  runs the ref's own Reconcile Prices in its simulated search (harness h5) — see CLAUDE.md «V9 price reconciliation».
  F1 + F2 «линия бота» (follow-ups 2026-10-11, plan `docs/audits/2026-10-09-followups/F1F2-plan.md`; T2 = the SQL core,
  T3 = Create WA/TG wired by `apply-clone-lifecycle.py`, T5 the sweep's branch A + Delete Bot Files): `clone_lineage.py`
  holds every statement once — `Clone Plan` (reuse / refuse / replace / adopt, the predecessor proven in the DATABASE),
  `Bind Workflow Id` per channel (bind + move the predecessors' memory, history, outcomes, handoffs, reply ledger, chat
  state and chat flags, the replaced clones' chunks and file registry, mark superseded, book the replaced clones — every
  condition re-checked on the committed row version; a concurrent bind of the same profile that committed first wins),
  `Record Discard Failure`, the sweep's `Clone Retirements` + stamps, and `lineage_cte` (the bot's line for Delete Bot
  Files); `--check` runs verify-sql-comments over all of them. Migration M2 `sql/2026-10-11-bot-lineage.sql`
  (`bot_profiles.superseded_by/_at`, `clone_retirements`). Behaviour: `tests/test_clone_lineage_sql.py` (real staging
  Postgres; L-matrix WA+TG, C-races on two connections, `--head` runs the same matrix against the pre-F1 canon).
  `apply-clone-lifecycle.py` (`--check`) writes the Create graph of plan §1.4 after the reply-mode seed gate: Clone Plan →
  (foreign: profile_owned) → reuse the bound clone when the probe proves it serves the profile | template → Create →
  Clone Id → Activate → Wappi webhook + types → Bind (positive `bound`) → delete the booked replaced clones; every «no»
  after the clone exists discards a NEW clone (failed discard → `clone_retirements`), every refusal answers
  `{success:false, error}` without an id, success `{id, created, adoptedProfiles}`; n8n API calls on localhost with the
  `n8n Admin API` placeholder credential. It owns those nodes; `apply-reply-mode-v2.py` keeps the seed chain
  (CLONE_CHAIN = Clone Plan), `apply-auth-gate.py` only the gate. Behaviour: `tests/test_create_clone.py` (the canonical
  graph through `tests/create_seed_chain.js` in BRIDGE mode — real SQL on temp tables + a fake n8n/Wappi; the books of
  clones, the answer contract, F2 R1/R4/R9; `--head` = the pre-F1 graph, `--verbose` = traces). Shape:
  `verify-clone-lifecycle.py` (T4: the chain entered only through the seed gate AND the plan, the id answered only on
  Bind's positive marker, every «no» after the clone discarding only a NEW clone, every n8n API / Wappi call with
  fullResponse + neverError + a status If, refusals without an id, localhost + the placeholder credential, protected lists
  = PROTECTED_IDS, `PreviousProfileId`/`CreateOrigin` never in the gate RULE, seven Postgres nodes on the hot path, WA = TG;
  `--rev 5ac9b2f9` shows the pre-F1 canon failing); its 21 mutations × WA/TG: `tests/test_verify_clone_lifecycle.py`
  (`--existing-guards` runs them against verify-reply-mode / verify-auth-gate / verify-telegram-parity instead).
  T5 (no replaced clone left, a deleted bot takes its whole line): `apply-sweep-clone-deactivate.py` adds the sweep's
  branch A `Clone Retirements` → `Retirements Dry Run?` (deletes on an explicit `retirementsDryRun === false` only; the
  canon carries `RETIREMENTS_DRY_RUN = True` for the first day on prod, then one line flips it) → `Has Retired Clone?` →
  `Delete Retired Clone` (localhost DELETE, `n8n Admin API`) → `Retired Clone Gone?` (200/404) → `Stamp Retirement
  Removed`/`Kept`, and V12 `Retired Clones` skips superseded rows. Delete Bot Files' deleting nodes (`apply-auth-gate.py`:
  List Bot Originals / Retire Bot Profiles / Delete Bot Chunks; `apply-history-handoff.py`: Delete Chat Memory) all read
  the line through `clone_lineage.DBF_LINE_HEAD` + `DBF_LINE_PARAMS` (the JSON id arrays Delete Ownership proved): session
  = every row of this install with the named rows' bot key (both channels) + the superseded chain; legacy = the named ids.
  The retire marks every line row `app_delete` (never adopted again) and books the line's clones (`bot_deleted`) — never a
  clone a live row outside the line drives, never a protected id; chunks go through the expression indexes and are
  deleted by primary key. Behaviour: `tests/test_clone_retirements.py`; shape: `verify-auth-gate.py` /
  `verify-history-handoff.py` (`--dir`) / `verify-clone-lifecycle.py` (11).
  `$execution.id` as correlationId). `verify-message-batching.py` / `verify-telegram-parity.py` pin the
  stamp's wiring and the winner → billing-gate → Input type path (their pre-2026-09 expectations were
  stale and failed every run).

## The 21 canonical workflows (19 + the two A1 auth workflows, 2026-09-17)

| id | name | role |
|----|------|------|
| `crAuthSession001` | Auth Session | `/webhook/AuthSession` — A1 install identity: `bootstrap` (mints installId + secret, 60/min cap), `refresh` (secret-hash compare + ownership load in ONE round trip → 12 h HMAC capability token), `claim.start/verify/status` (legacy-profile claim ONLY by the code in the bot's own chat — V2 2026-09-28: a re-pairing is a precondition, never a proof; the code is judged sent, five wrong answers kill it). After the answer, a refresh that said `billingConflict` is recorded for support in `billing_conflicts` (`Conflict?` → `Log Billing Conflict`, V3 journal 2026-10-11, `sql/2026-10-11-billing-conflicts.sql`; support query in `docs/audits/a1-deploy-runbook.md` §5). Generated by `build-auth-workflows.py` from `auth/token.js`; needs `CR_AUTH_SECRET` + `NODE_FUNCTION_ALLOW_BUILTIN=crypto` + `N8N_BLOCK_ENV_ACCESS_IN_NODE=false` on the server |
| `crWappiProxy0001` | Wappi Proxy | `/webhook/WappiProxy` — the client's ONLY road to Wappi: Bearer token → allow-listed `op` (19 ops, validated params, `profile_id ∈ token.p`, auth ops also for pending claims) → one HTTP call with the server-side `WappiAuthToken` → Wappi's status + JSON body passed through; `profile.add` binds the new profile to the install and re-issues the token. Successful executions are NOT stored (chat polling volume) **V12:** `profile.delete`/`profile.logout` (`OWNER_OPS`) pass `Profile Owner` (SQL: this unrevoked install owns the row) before the transport — a 12-hour token cannot unlink a profile another install has claimed. |
| `hHwvfOvTCS42pXnq` | Landing Lead | Landing webhook `/webhook/LandingLead` (form-encoded POST с choosereply.com, деплой 2026-08-27, спека `docs/superpowers/specs/2026-08-27-landing-page-design.md`) — Code-валидация: honeypot `website` непустой → тихий `{success:true}` БЕЗ вставки; `phone` обязателен, 6..32 из `[0-9+()\-\s]` **И 10..15 ЦИФР** (аудит 2026-08-28: одной charset-проверки мало — `((((((` и `++++++` проходили её и попадали в таблицу; клиент дублирует счёт цифр, чтобы дать внятное сообщение вместо `bad_request`), иначе `bad_request` → INSERT в `landing_leads` **под предохранителем** `where (select count(*) … where created_at > now() - interval '1 minute') < 10` (эндпоинт неаутентифицирован и живёт на 2 CPU/2 ГБ; живой посетитель порога не достигает, флуд не забивает таблицу, которую владелец читает глазами; ответ остаётся `{success:true}`, ошибка не поднимается). Настоящий edge-rate-limit (Cloudflare или xcaddy+rate-limit) — отдельная инфраструктурная задача, НЕ закрыта. `allowedOrigins:"*"` митигацией НЕ является: форма шлёт form-urlencoded без кастомных заголовков, т.е. CORS-simple request — браузер отправит его с любого origin, CORS лишь скроет ответ (миграция `sql/2026-08-27-landing-leads.sql`; Postgres cred `vvRrFiEXzLVqKjOx`; `queryReplacement` — ОДНО выражение-массив, по гочте RevenueCat) → `{success:true}`; ошибка БД → `{success:false,error:"db"}` (страница показывает fallback-текст на любой `success:false`). CORS вебхук отдаёт сам (`allowedOrigins:"*"` — n8n эхо-ит Origin, преflight OPTIONS 204 проверен). Спам-риск принят: INSERT-only, honeypot, длины — пересмотреть при реальном спаме |
| `XuvOp7TxOImOAmlj` | CreateWhatsappWorkflow | App webhook `/webhook/CreateWhatsappWorkflow` — clones the WhatsApp template per bot **Task 17a:** `Register Profile` now (a) ensures the owner's `subscribers` row in the SAME statement (a preceding `insert … on conflict do nothing` CTE) — `Ensure Subscriber` upstream is fail-open, and a profile registered without an owner row was BOTH unmetered (`Count Dialog` inner-joins `subscribers`, finds nothing, and Quota Decision fail-opens) and invisible to both Profile Lifecycle Sweep branches; and (b) refuses `profile_id` `'-1'`/empty — `'-1'` is the client's «channel not authorized» sentinel and `profile_id` is the PRIMARY KEY, so one shared row would have been re-owned by every account in turn. `Compute Slot Limit` treats `grace` like `expired` (0 slots), because `Count Dialog` already refuses dialogs at `status in ('expired','grace')` — handing out slots there would create new paid Wappi profiles for a bot that cannot answer. |
| `Uz6HBBUpAiUqVysB` | CreateTelegramWorkflow | App webhook `/webhook/CreateTelegramWorkflow` — clones the Telegram template per bot **Task 17a:** `Register Profile` now (a) ensures the owner's `subscribers` row in the SAME statement (a preceding `insert … on conflict do nothing` CTE) — `Ensure Subscriber` upstream is fail-open, and a profile registered without an owner row was BOTH unmetered (`Count Dialog` inner-joins `subscribers`, finds nothing, and Quota Decision fail-opens) and invisible to both Profile Lifecycle Sweep branches; and (b) refuses `profile_id` `'-1'`/empty — `'-1'` is the client's «channel not authorized» sentinel and `profile_id` is the PRIMARY KEY, so one shared row would have been re-owned by every account in turn. `Compute Slot Limit` treats `grace` like `expired` (0 slots), because `Count Dialog` already refuses dialogs at `status in ('expired','grace')` — handing out slots there would create new paid Wappi profiles for a bot that cannot answer. |
| `3qax5J9u2qsT9Vao` | Edit Whatsapp Workflow | App webhook `/webhook/EditWhatsappWorkflow` — edits a bot's system prompt |
| `TwWPW3gIyjZS3foR` | Edit Telegram Workflow | App webhook `/webhook/EditTelegramWorkflow` — edits a bot's system prompt |
| `KoTuIlk4LMrlvnWI` | Upload File | App webhook `UploadFile` — ingests files into the Supabase vector store; stamps `botWaId`/`botTgId`/`fileId` on every chunk; extension routing is case-insensitive; archives the uploaded bytes to Storage `price-lists/{fileId}` (dead-end branch, `onError: continue` — never fails the upload); unsupported types get an explicit 415; photos (jpg/jpeg/png/webp client-side) route to OpenAI gpt-4o-mini vision extraction (422 `no_price_data` gate if unreadable), archived like all other uploads. `Register Upload` (`apply-auth-gate.py`) decides the upload first: the caller's own registry row is re-used, another install's is 403; V4 (2026-09-28): an owner-less row is adopted by a session only when every workflow id the file is known under (row + chunks) is the token's, and a fileId with no row is registered only when it has no chunk |
| `ZTqpumOpL1rNDOp6` | Delete File | App webhook `DeleteFile` — body `{ fileId }`; deletes that file's chunks from `documents` AND its stored original `price-lists/{fileId}` (404 tolerated for pre-bucket files), returns `{ success, deletedChunks }`; owner-scoped (`apply-auth-gate.py`): a session deletes a file registered to its install, and — V4 (2026-09-28) — a file registered to no install only when every workflow id it is known under is the token's (403 otherwise); token-less legacy calls keep the old behaviour until the compat window closes |
| `4wYitz5ek30SVNlT` | WhatsApp Bot | **Clone source** for every WhatsApp bot (referenced by literal id in CreateWhatsappWorkflow); retrieval self-scoped by `botWaId = {{ $workflow.id }}`; **Phase 10:** carries the pre-generation debounce+combine splice on the `Suppressed?` FALSE branch (see note below) **Task 17a (top-up = RESERVE, owner decision 2026-08-26, spec §2):** `Count Dialog`'s `quota` CTE is the BASE plan number only — the top-up is no longer added to it. A NEW dialog that is already over the base quota consumes ONE reserve unit via an `update subscribers … set topup_balance = topup_balance - 1 … where topup_balance > 0` CTE, and that UPDATE's row lock is the concurrency control: READ COMMITTED re-evaluates the `> 0` guard against the freshly committed row, so two racing new chats end up one allowed / one refused and the balance can never go negative (verified live with a forced-overlap race, `probe-billing.py --reserve`). Continuations and under-quota dialogs cost nothing; `expired`/`grace` still consume nothing whatever the balance. `allowed` deliberately includes `exists(reserve)` — `on conflict do nothing` can swallow our own insert when a racer wrote the same key first, and that conflict proves the row exists, so without the term a race would spend a unit AND refuse the reply. Since V9 (2026-09-28) the retrieval tool is a toolWorkflow into `crPriceSearch001` (filter key + `$workflow.id` handed over). |
| `4VN3gsFaC2HUYmcc` | Telegram Bot | **Clone source** for every Telegram bot (referenced by literal id in CreateTelegramWorkflow); retrieval self-scoped by `botTgId = {{ $workflow.id }}`; **Phase 10:** carries the same debounce+combine splice on the `Suppressed?` FALSE branch (see note below) **Task 17a (top-up = RESERVE, owner decision 2026-08-26, spec §2):** `Count Dialog`'s `quota` CTE is the BASE plan number only — the top-up is no longer added to it. A NEW dialog that is already over the base quota consumes ONE reserve unit via an `update subscribers … set topup_balance = topup_balance - 1 … where topup_balance > 0` CTE, and that UPDATE's row lock is the concurrency control: READ COMMITTED re-evaluates the `> 0` guard against the freshly committed row, so two racing new chats end up one allowed / one refused and the balance can never go negative (verified live with a forced-overlap race, `probe-billing.py --reserve`). Continuations and under-quota dialogs cost nothing; `expired`/`grace` still consume nothing whatever the balance. `allowed` deliberately includes `exists(reserve)` — `on conflict do nothing` can swallow our own insert when a racer wrote the same key first, and that conflict proves the row exists, so without the term a race would spend a unit AND refuse the reply. Since V9 (2026-09-28) the retrieval tool is a toolWorkflow into `crPriceSearch001` (filter key + `$workflow.id` handed over). |
| `crPriceSearch001` | Price List Search | Sub-workflow (V9, 2026-09-28; `build-price-search.py`) behind both templates' and the Test Dialog's «Supabase Vector Store» tool: inputs `{query, botId, filterKey, prompt}` → the same `documents` vector search (topK 10, self-scoped by botWaId/botTgId, fail-open) → `Reconcile Prices` marks a row naming a catalog article (parsed from the caller's published prompt) at another price → `{documents:[…]}`. Protected in Set Workflow State; saves no successful executions. |
| `lmjYsdNcQA2IE5rl` | Delete Bot Files | App webhook `DeleteBotFiles` — body `{ botWaId, botTgId, waProfileId, tgProfileId, appUserId }`; sweeps ALL of a deleted bot's RAG chunks + stored originals (guards the `"-1"` unauthed sentinel), and — since 2026-08-27 — first retires the bot's `bot_profiles` rows by the two profile ids (`deleted_at=now()`, `deleted_reason='app_delete'`), freeing the channel slots in the same execution. The retire node sits BEFORE the chunks delete (a chunks failure must never block the slot release) and matches by `profile_id` ONLY — never `app_user_id`, which can drift between create and delete (RC anonymous-id rotation / pre-init device-id fallback); `appUserId` is audit-only. This synchronous retire is the ONLY slot release for `status='active'` owners (both Profile Lifecycle Sweep branches deliberately skip them). Response `{ success, deletedChunks, deletedFiles, retiredProfiles, deletedMemoryRows, deletedOutcomes, deletedReplyFlags }`; old clients without the new fields are a clean no-op retire. **Since 2026-09-01 (`apply-delete-history.py`)** the chain gained «Delete Chat Memory» (Delete Bot Chunks → Delete Chat Memory → Respond, `onError: continue` — сбой чистки памяти не валит ответ, retire/chunks уже закоммичены): wipes `n8n_chat_histories` (session_id = `<profileId>:<chatId>`), `conversation_outcomes` и `reply_mode_flags` по двум Wappi profile id (сентинелы '-1'/''/'null'/'undefined' исключены; queryReplacement коалесцирует `|| '-1'` — пустой отрендеренный параметр ВЫПАДАЕТ из comma-split и Postgres падает «no parameter $2»); `dialog_counts` НЕ трогаются осознанно (биллинговые записи, Task 17a переносит их с деньгами). Probes: `probe-billing.py --app-delete-retire` (Part 11), `apply-delete-history.py --probe` (фикстуры ZZPROBE, контроль-профиль выживает; PROBE GREEN на проде 2026-09-01) **V12 (2026-10-07, `apply-auth-gate.py`):** the gate verifies the token only; ownership is decided by `Delete Ownership` (SQL: every id the deleting nodes read must be a string owned by THIS install's `bot_profiles` row, retired or not; 403 `owned_elsewhere`/`unproven`, a DB fault 503; legacy unchanged) — a retired bot's owner can delete it, a stale token after a claim cannot. Chain: `List Bot Originals` → storage deletes (status-judged) → `Originals Settled` → retire → chunks → memory → `Deletion Settled?` → `Respond` (success only on numeric counters; else 503). **T5 (F1 + F2):** every deleting node reads the bot's LINE (`clone_lineage`), not only the named ids — the earlier profiles and clones of a re-paired/reconnected bot go too; the retire marks the line `app_delete` and books its clones (`bot_deleted`) for the sweep's branch A. |
| `2htWSV5IHO8E2CgB` | Dashboard Outcomes | App webhook `DashboardOutcomes` — body `{ profileIds }`; classifies conversation outcomes from `n8n_chat_histories` into `conversation_outcomes`, returns them for the «Сводка» dashboard |
| `2islisFH7jjLoPQM` | Delete Orphan Profiles | **Scheduled, hourly** (no webhook) — server-side TTL sweep deleting Wappi profiles that stay unauthorized ≥ 24h; see below |
| `SCLcpn6DMDG3Z4VN` | Set Reply Mode | App webhook `SetReplyMode` — shared always-active; body `{ profileIds:[...], chatId:"*"\|"<id>", suppressed:bool }`; validates (malformed → `bad_request` before any DB write), fans out one item per surviving profileId, upserts each into `reply_mode_flags` (on conflict do update). The semi-auto «Авто/Вместе» suppression write path (SUP-02); the bot templates' gate reads the same table. Deployed by `build-set-reply-mode.py` (Postgres cred bound by explicit id `vvRrFiEXzLVqKjOx` — dev's single Postgres cred as of 2026-07-22); id `SCLcpn6DMDG3Z4VN` assigned + activated on first deploy 2026-07-22, filename finalized to `SCLcpn6DMDG3Z4VN-Set_Reply_Mode.json` in 09-04 |
| `9PTyYcelRQI7bGDb` | Suggest Replies | App webhook `SuggestReplies` — body = frozen v1 request (`{ v, requestSeq, chatId, botWaId, businessTypeId, catalog, steerTowardText, messages… }`); known-invalid requests (v mismatch / missing `chatId` / empty `messages`) short-circuit straight to `generation_failed` — zero LLM spend on the unauthenticated webhook; optional channel-branched tenant-scoped RAG pre-retrieval (one single-key filter per channel: `botWaId` WA / `botTgId` TG, topK 5, skipped on `""`/`"-1"`) → one gpt-4o-mini call (strict json_schema, closed 6-label enum) → Code validation (exactly 4 distinct enum-labeled moves, ≤300 clamp, markdown-strip, one retry then `generation_failed`) → returns `{ v:1, requestSeq, suggestions:[{text,label}×4] }` for the semi-auto «Вместе» reply panel. Deployed from the committed canonical JSON by `build-suggest-replies.py` (dev id here; prod bagkz replication pending). Adversarially verified on dev 2026-07-10 (6-case matrix — grounding / missing-data / steer / injection / trivial / sentinel — plus format-hijack + malformed→`generation_failed`, **zero fixes needed**); dev RAG grounding is **catalog-only** until Supabase `documents` are seeded — RAG-with-data deferred to prod replication **Task 17a — subscription gate + daily cap (owner decision 2026-08-26, spec §5.3):** suggestions stay FREE (they never touch `dialog_counts` — asserted by the parity gate) but the endpoint is no longer billing-unaware. `If invalid?`'s valid branch now enters `Suggestion Gate` (Postgres, fail-open like `Count Dialog`) → `Gate Decision` (Code; rebuilds the payload from `$('Prep')` because a Postgres node drops the incoming item) → `If Gate Allows` → **true** the normal `If skipRag?` path, **false** straight to `Build Response`, so a refusal reaches NO retrieval and NO LLM (proven by node path: execs 3813/3824/3833 stop at Build Response). One statement does lookup + increment + verdict against `suggestion_counts` (migration `Tools/n8n/sql/2026-08-26-suggestion-counts.sql`; PK `(app_user_id, d)`, Asia/Almaty day, cap 100/day — request 101 is the first refused). Refused: `expired`, an id with no `subscribers` row (the id is this unauthenticated endpoint's only secret), and a missing/empty `appUserId` — the client ALWAYS sends it. Allowed: `active`/`trialing`/`grace`, INCLUDING over-quota, because the panel IS the fallback the dialog quota routes owners into. A refusal reuses the existing `{error:'generation_failed'}` envelope with NO `reason` on the wire (review N-4: the endpoint is unauthenticated, so naming the refusal reason would confirm a guessed id's existence/state); the diagnostic `gateReason` lives on Gate Decision's execution-log output, pinned by the parity gate in both directions. Probe: `probe-billing.py --suggestions`. |
| `ZGYr6srzS3rSSXHp` | RevenueCat Events | App webhook `RevenueCatEvent` (`Tools/n8n/workflows/ZGYr6srzS3rSSXHp-RevenueCat_Events.json`) — mirrors RevenueCat subscription events into `subscribers` (billing schema, Task 6). Auth is n8n's **native** `httpHeaderAuth` credential (`RevenueCat Webhook`, header `Authorization`) bound on the Webhook node — no in-workflow secret compare, so the exported JSON carries no secret, only a `{id,name}` credential reference. Chain: Webhook (`responseMode: responseNode`) → `Map Event` (Code, verbatim event→row mapping; `alwaysOutputData: true` so `CANCELLATION`'s deliberate `return []` still emits one empty item instead of killing the run downstream) → `If Has Payload?` (non-empty `app_user_id`) → **TRUE**: `Upsert Subscriber` (Postgres upsert; `onError: continueErrorOutput` routes a failed write to `Respond Error` / HTTP 500) → `Respond 200`; **FALSE**: `Respond No-Op` (200). Net effect: a real event only acks 200 **after** the Postgres write commits — a genuine DB failure surfaces as a non-2xx so RevenueCat retries — while `CANCELLATION` (which intentionally writes nothing) still gets a clean 200 off the FALSE branch. Gotcha worth keeping for any future Postgres node here: `queryReplacement` must be **one** `={{ [...] }}` array expression, never comma-joined `{{ }}` fragments — the comma-joined form stringifies a JS `null` to the literal text `"null"`, which Postgres then rejects outright for a `timestamptz` column. Probed by `Tools/n8n/probe-billing.py` (`RC_WEBHOOK_SECRET` env; 401/403 no-auth + 200×4 real events, extendable in Task 8-9); permanent DB read-back is deliberately deferred to Task 11 (`GetUsage`), not a bespoke debug webhook here. **Since Task 16 the chain forks twice before the original one:** `Map Event` → `If Is Transfer?` → TRUE: `Transfer Subscriber` → `Respond 200`; FALSE: `If Needs Consolidation?` → TRUE: `Consolidate Aliases` → the `If Has Payload?` chain above / FALSE: straight to it. Both new Postgres nodes route their error output to their OWN 500 responder (`transfer_failed` / `consolidation_failed`), so a probe can tell which write failed. **The two forks exist because RevenueCat moves an identity in two different ways, and only one of them is a `TRANSFER`:**<br><br>**(a) Alias merge — what a same-device REINSTALL actually produces, and the one that cost real money here.** RC does NOT fire `TRANSFER` for it; it renames the identity: the event arrives under the NEW `app_user_id` with the old one in BOTH `original_app_user_id` and `aliases[]` (live proof on this instance — exec 3144 pre-reinstall carries `aliases:[old]`; execs 3150/3151/3153 post-reinstall carry `app_user_id: NEW`, `original_app_user_id: OLD`, `aliases:[NEW, OLD]` and no `transferred_*` keys at all; RC's docs say to «search both the original_app_user_id and the aliases array»). `Map Event` computes the alias set minus the current id — empty on a healthy install, where `aliases` is just `[self]`, so `If Needs Consolidation?` skips the Postgres round-trip entirely — and `Consolidate Aliases` moves the old rows' `topup_balance` onto the current id and retires them. **It deliberately does NOT copy their plan/status/period:** the event riding behind it already carries the authoritative ones, and this path fires on EVERY event for the rest of the account's life, so a snapshot copy here would be permanent downgrade risk for zero gain. **It also sits BEFORE `Upsert Subscriber` on purpose:** that node's `topup_balance + $5` is not retry-idempotent, so a failure *after* it would let RevenueCat's redelivery double-credit the event's own top-up — failing on this side is safe in both directions. When there is nothing to move it writes nothing at all, and when the destination row does not exist yet it creates it with `trial`/`trialing` defaults so the upsert behind it can layer the real plan on top without touching the balance.<br><br>**(b) `TRANSFER` — a cross-account move.** Its payload carries **no** `app_user_id`/`product_id`/`entitlement_ids`, only the `transferred_from`/`transferred_to` String arrays, which is exactly why it cannot ride the existing `If Has Payload?` gate. `Transfer Subscriber` additionally moves the SNAPSHOT (plan/status/period/product) — to EVERY id in `transferred_to` («App User ID(s) receiving»), while the balance goes to the first one only, since money cannot be duplicated.<br><br>Both are ONE parameterized statement (= one transaction, so a move and a retirement can never half-apply), and four details in them are load-bearing. **(1) Sources are taken `for update`, and the credited amount is produced under that lock** — not by a snapshot `sum(topup_balance)`. This instance demonstrably delivers same-subscriber events concurrently (execs 3128/3129 overlap by 2.4s; 3150/3151 arrive in the same second), and a snapshot read double-credits in that race. Proven: 4 concurrent deliveries of one alias event → exec 3336 retired the row and credited 500, while 3337/3338/3339 blocked on the lock, re-read the settled row and wrote nothing. **(2) The retirement is guarded** (`status <> 'expired' or topup_balance <> 0 or current_period_end is null or current_period_end > now()`), so a settled alias set — which rides along forever — writes zero row versions, and a replayed delivery credits nothing. **(3) `current_period_end = least(coalesce(period, now()), now())`, and BOTH coalesces matter.** This is the entire hand-off to `Profile Lifecycle Sweep`'s Branch A, whose `Candidates` query reads `s.status in ('expired','grace') and coalesce(s.current_period_end, now() - interval '99 days') < now() - interval '3 days'`. A still-in-the-future paid period would park the abandoned identity's Wappi profiles for up to a month at 23₽/day; a NULL period is the OPPOSITE failure — that `coalesce(…, -99 days)` makes the row a candidate INSTANTLY, so a top-up-only identity (whose plan/status/period a `NON_RENEWING_PURCHASE` never touches) would lose its REAL Wappi profiles within one 6h tick with zero grace. `now()` gives it the same 3 days every other churned owner gets, and the value only ever moves DOWN. NO new deletion machinery exists anywhere: retirement is entirely «make the row look like ordinary churn». `status='expired'` also stops the abandoned identity's auto-replies through the existing quota gate (`Count Dialog` refuses to open a NEW dialog for `status in ('expired','grace')`; a chat already counted TODAY keeps answering until the Asia/Almaty date rolls over). **(4) The snapshot moves as a UNIT, and only when the source is strictly newer AND not deader.** A `RENEWAL` under the new id can legitimately land BEFORE a `TRANSFER`, and RC retries any non-2xx — so the tie goes to the destination, which is also what makes a duplicate delivery a no-op. Freshness alone is not enough either: an EXPIRED source with a LATER period would otherwise stamp `expired` onto a live `trialing` destination (whose NULL period always loses the comparison) and silence a healthy fresh install through that same quota gate. Money still moves in that case; only the snapshot is refused.<br><br>Same task also fixed `PRODUCT_CHANGE`, which carries the OLD sku in `product_id` and the new one in `new_product_id` (live: exec 3128 sent `product_id: sub.start.month` for an upgrade TO `sub.business.month`) — the mapper now prefers the latter. The `??` there is deliberate and must NOT be «simplified» to `'new_product_id' in e`: it treats an absent key and an explicit `null` identically. On this instance all 34 real deliveries carried the key only on `PRODUCT_CHANGE`, but RC documents it as «omitted when null», not «never null».<br><br>Probed by `probe-billing.py --transfer` (5 TRANSFER cases + the alias-merge cases, every one exact-value: the summed top-up, the clamped source period, a renewal landing before the transfer, an expired source vs a live destination, a NULL-period source, multi-id arrays, replay, and a 4-way concurrent burst) and by `probe-billing.py --branch-a-trace`, which runs the sweep's own `Candidates` query against real rows plus a control row proving the clamp is what makes the hand-off work **Task 17a (I-4 — paid usage moves with the money):** both `Consolidate Aliases` and `Transfer Subscriber` now carry the CURRENT-MONTH `dialog_counts` rows of every retired/source id onto the destination (`insert … select distinct … on conflict do nothing`). Without it a reinstall reset the monthly counter — «300 из 300» became «0 из 300» on the same paid subscription. Existence of a row IS the count, so the copy is idempotent by construction and a settled replay writes ZERO new row versions (measured via `xmin`, same guarantee as `credited`/`retired`); `distinct` covers two aliases carrying the same `(chat, date)`. Old rows are left on the retired id — nothing reads them. On the TRANSFER path the usage rides the SNAPSHOT (all `transferred_to` ids), while the balance still goes only to the first. Asserted by `probe-billing.py --transfer` cases A1/A2 (`used` moves, and a replay does not grow it). |
| `jtbssfzXbOxwTK4k` | Get Usage | App webhook `GetUsage` (`Tools/n8n/workflows/jtbssfzXbOxwTK4k-Get_Usage.json`) — `authentication:"none"` (the `appUserId` in the body IS the secret, same v1 posture as every other app webhook). Webhook → `Read Usage` (Postgres, row-safe one-row CTE left-joining `subscribers`/`dialog_counts`/`bot_profiles` so an unregistered `appUserId` still gets one row of trial/0 defaults rather than zero rows; `onError: continueErrorOutput` → `Respond Error` 500 — this read endpoint fails **closed**, the opposite of the bot-path's fail-open, so the client keeps its last-known cache instead of trusting a wrong zero) → `Shape Response` (Code, plan→quota map `{trial:150,start:300,business:1000,network:3000,none:0}`) → `Respond 200`. Probed by `Tools/n8n/probe-billing.py --usage` with REAL exact-value assertions over plain HTTP (no Postgres/n8n-mcp access needed — this is a pure unauthenticated read) |
| `fXYpCXPKw92EzRz8` | Profile Lifecycle Sweep | **Scheduled, every 6 hours** (no webhook) — Task 12's day-6-retro-charge guard, a separate concern from the hourly `Delete Orphan Profiles` below (that one only ever cleans up NEVER-authorized profiles; this one acts on registered, possibly-authorized ones based on OUR billing state). Branch A: `Candidates` (Postgres — alive `bot_profiles` joined to `subscribers` where `status='trialing'` and `created_at` older than 4d17h, OR `status in ('expired','grace')` and `current_period_end` more than 3d past) → `Trial/Churn Dry Run?` → live: `Capture Delete Fields` (Set — snapshots profile_id/channel/app_user_id/reason off `Candidates` while pairing is still guaranteed 1:1; the LAST safe point for a `.item` back-reference in this chain) → `HTTP Delete Profile` (POST `{api\|tapi}/profile/delete`, `WappiAuthToken` cred, `onError:continueRegularOutput`) → `Delete Succeeded?` (`$json.status=='done'`) → **true**: `Mark Deleted` (`returning profile_id, app_user_id, channel, deleted_reason` — the `RETURNING` clause is load-bearing, not decorative: a bare `UPDATE` with none collapses N≥2 successful items into ONE output item with an ARRAY `pairedItem`, which crashed the whole execution the first time two profiles for the same owner expired in the same run, see the fix-round note below) → `Mark Succeeded?` (`!$json.error`) → **true**: `Demote Trialing` (`returning app_user_id, ... as deleted_reason`, reads `$json.*` from Mark Deleted directly — zero `.item` back-references on this path; only ever touches `status='trialing'` rows, a no-op for churn) → `Stamp Deleted`; **false**: `Stamp Mark Failed` — **false** (Delete Succeeded?): `Stamp Delete Failed`, `deleted_at` stays NULL — retried next run, never marked on a failed delete. Branch B (liveness reconcile, closing the gap flagged in Task 8's review): `List WA/TG Profiles` (GET `profile/all/get`, both bases, `onError:continueRegularOutput` + `retryOnFail` — a Wappi hiccup here must route into `skip_invalid_fetch`, not crash) → `Alive Registry` (Postgres — alive `bot_profiles` LEFT JOINed to `subscribers`, excluding `status='active'` AND anything registered in the last hour — the age floor closes a fetch-snapshot race against a just-registered profile) → `Compute Liveness Diff` (Code — validates each base INDEPENDENTLY: no `.error` shape, `status=='done'`, `profiles` coerced from Wappi's documented `null`-when-empty shape to `[]`, AND a well-formed-but-EMPTY list floored to invalid when the registry holds ≥1 alive row for that channel — a genuinely-empty list is the ONE case that looks identical to a silently-wrong one, so it gets the same `skip_invalid_fetch` treatment as a hard fetch error rather than confidently retiring every row; **and, since Task 15b, a PROPORTIONAL RETIRE CAP** — if a base's would-be retirements exceed `max(2, 50% of that base's alive registry rows)` the whole base is skipped for that run with `action:"skip_retire_cap"` and a `reason` carrying both counts. That closes the gap the empty-list floor leaves: the floor only catches a FULLY empty list, while `profile/all/get` documents **no pagination parameters at all** and returns **no envelope** (`{profiles, status}` only — no `total`/`page`/`next`/`has_more`; verified 2026-08-25 against the published WhatsApp API docs and live against both bases, where unknown query params are silently ignored rather than rejected), so a silently TRUNCATED list is indistinguishable from a complete one and every row past the cut would read as "absent from Wappi". The `max(2, …)` floor keeps ordinary single-bot cleanups working; the 50% term is what a truncation or a partial outage trips. Each row also carries a `wouldRetire` boolean so a capped run's log still says which rows the diff had flagged) → `Is Retire Candidate?` → `Liveness Dry Run?` → live: `Mark Liveness Deleted` (`deleted_reason='liveness'`, `returning ...` — same RETURNING-preserves-pairing fix as Branch A — no Wappi call, the row's absence from the real list IS the evidence). `Sweep Config` (Set) carries the single `dryRun` boolean gating BOTH branches' destructive step — **`false` (LIVE) since Task 15b, 2026-08-25.** It was held at `true` from Task 12's fix round until then, because owner-recreated post-Task-8 bots sat in the registry as `trial`/`trialing` and a live run would have deleted their real Wappi profiles. At the flip those rows no longer existed: `bot_profiles` was empty (0 rows, alive or dead), `subscribers` held one probe leftover, and Wappi's own `profile/all/get` returned zero profiles on BOTH bases — the owner had cleared his dev bots before the device pass. The first live execution (3093) was therefore a clean no-op: `Candidates` 0 items, `Alive Registry` 0 items, no destructive node reached at all. **Live-run rule from here on: after the device pass mints a real RevenueCat `app_user_id`, a bot left un-purchased is deleted by Branch A ~4d17h after registration — that is the intended day-6-retro-charge guard, not a bug. To exempt a dev/owner identity, grant it `status='active'` in `subscribers` (both branches exclude `active` uniformly); the exact statement is `sweep_dev_owner_grant_sql()` in `Tools/n8n/probe-billing.py`, printed by `python3 Tools/n8n/probe-billing.py --sweep`.** `bot_profiles.deleted_reason` (migration `Tools/n8n/sql/2026-08-21-deleted-reason.sql`) durably persists the delete reason past execution-log retention. No committed script fires this (no webhook exists) — `Tools/n8n/probe-billing.py --sweep` prints the exact fixture SQL used (original a-f set, the fix-round's 2-candidate multi-delete and empty-list-floor fixtures, and Task 15b's `sweep_retire_cap_seed_sql()` for fixture (h)), full transcript in `.superpowers/sdd/task-12-report.md` (see its "Fix round" section for the 2 Critical findings and how each was verified with real throwaway Wappi profiles) **V12 (2026-10-07, `apply-sweep-clone-deactivate.py`, migration `2026-10-07-retired-clone-deactivate.sql`):** a third branch `Retired Clones` → `Clone Dry Run?` → `Has Clone?` → `Deactivate Clone` (local admin API, `n8n Admin API`) → `Clone Off?` → `Stamp Clone Deactivated` DEACTIVATES (never deletes) the clone of a row Branch A retired (`trial_expiry`/`churn_grace`), never liveness/app_delete, never a workflow a live row drives; stamped in `clone_deactivated_id`, retried on failure. Placed below both rows (v1 order). **T5 (F1 + F2):** `Retired Clones` skips superseded rows; a fourth branch `Clone Retirements` DELETES the clones booked in `clone_retirements` (Create's replaced clones / failed discards, a deleted bot's line) once 10 min old — never one a live row drives, never a protected id; `retirementsDryRun` (Sweep Config) only logs while true. |
| `olmqWMfcFpCEcpiE` | Support Message | App webhook `SupportMessage` — body `{ text }`; trims/validates (400 on empty, clamp 4000), relays to the owner's support chat via the server-side `telegramApi` credential «Support Bot» (attribution suppressed). Replaced the client's direct `api.telegram.org/bot<token>/sendMessage` call 2026-08-31 (store-submission blocker: the bot token shipped inside the APK/IPA via secrets.json). Backs `Manager.SendToTelegram`. Deployed by `build-client-webhooks.py`; canonical export masks the cred id / chat id as `__TG_CRED__`/`__CHAT_ID__` |
| `0DP79STI3pafXO9B` | Set Workflow State | App webhook `SetWorkflowState` — body `{ workflowId, action: activate\|deactivate\|delete }`; validates the id (`^[A-Za-z0-9]{8,36}$`), whitelists the action, and REFUSES every canonical infra id + itself + Support Message (400 `protected_workflow`) before calling n8n's own REST API on `localhost:5678` with the server-side `httpHeaderAuth` credential «n8n Admin API». Replaced the client's direct `/api/v1/workflows/` REST calls 2026-08-31 — the instance-admin `X-N8N-API-KEY` (= workflow-CRUD = code execution on this VPS) no longer ships in the binary. **Add every new canonical workflow's id to its protected list** (in `build-client-webhooks.py`, then `--update`). E2E-verified on prod 2026-08-31: throwaway workflow activate→deactivate→delete, protected-id refusal, Get Usage untouched **V12:** in session mode `deactivate`/`delete` go through `Delete Ownership` (same SQL as Delete Bot Files) → `Owner Item` → `Validate`; `activate` keeps the token rule (`Token Owns?`); the delete is idempotent (`Workflow Gone?`: 200/404 → OK, else 502). |

> ⚠️ `4wYitz5ek30SVNlT` and `4VN3gsFaC2HUYmcc` are referenced by **literal id** inside the
> two Create handlers. Never change their ids, or bot creation 404s on the clone step.
> Keep both **inactive** — they share webhook path `0091024b-7b46` and only the per-bot
> clones (with rewritten paths) ever go active.

> **Phase 10 — message-batching / debounce splice.** Both bot templates carry a pre-generation
> `Debounce Wait → Fetch Recent → Latest+Combine → Is Latest?` stage on the `Suppressed?` FALSE
> branch (before `Input type`) that coalesces a burst of multi-fragment customer messages into ONE
> combined reply — only the last fragment's execution proceeds; earlier fragments dead-end. It was
> first authored by the one-shot `apply-message-batching.py`, which is **RETIRED (2026-09-28) — never
> re-run it**: stage 3/4 (`apply-reply-pipeline-v2.py` → `apply-history-handoff.py`) now own those
> nodes (the A3 owner rule inside Latest+Combine, the `Suppressed? → Record Suppressed` branch), and a
> re-run would put the Phase-10 versions back. The same holds for `apply-rag-fixes.py`,
> `apply-upload-hardening.py` and `apply-delete-history.py --deploy/--check`; all four refuse to write
> (`tests/test_legacy_generators.py`). Change those nodes through their current generator, then run
> `verify-message-batching.py` to gate the splice (asserts the
> 4 nodes, the `Suppressed? → Debounce Wait` rewire, the `messages/get` fetch with no `mark_all` and
> its hot-path `retryOnFail`, and the Code-node body re-emit + nullish empty combine + requested-chat
> filter + explicit `pairedItem`). This edits the two existing templates — **no new canonical
> workflow, the count stays 13.**

### Delete Orphan Profiles (scheduled sweep) — policy & gotchas

Covers the orphan-profile leaks the client can never settle (swipe-kill / iOS quit mid-wizard,
`profile/add` response lost in flight): hourly, lists ALL profiles (`GET /api/profile/all/get` +
`GET /tapi/profile/all/get`), tracks unauthorized ones in a **first-seen ledger in workflow
staticData** (Wappi exposes no creation timestamp — TTL runs from first observation, so a fresh
import grants every existing orphan the full 24h grace), then re-checks `get/status` per candidate
and POSTs `profile/delete`. Never deletes: authorized profiles, `deleted_at`-set entries, ambiguous
`authorized` flags, or profiles with `authorized_at`/`logouted_at` inside the TTL window.
`Sweep Config` node: `ttlHours` (24; values ≤ 0 coerce back to 24) / `dryRun` (reports
`wouldDelete` without deleting). Verified e2e on dev 2026-07-10: seeded the ledger 25h in the past
for two throwaway profiles → both deleted (WA + TG paths), the two live authorized profiles untouched.

- **`get/status` has NO `status:"done"` field** (unlike add/delete/list/all-get). Response validity =
  boolean `authorized` + `profile_id` echo match (the id match also guards Wappi's known
  concurrent-response crossing). Don't "fix" the verify predicate to check `status`.
- `profile/all/get` returns `profiles: null` (not `[]`) when the namespace is empty — handled.
- `is_subscribe` is `false` even on working authorized profiles and `last_activity` is often `0` —
  neither is usable in deletion policy.
- staticData persists only across **production** (scheduled) runs; manual runs read but never write it.
  The e2e trick: PUT seeded `staticData` via REST, then run manually with the real 24h TTL.
- The n8n MCP builder strips/rejects generic-auth (`httpHeaderAuth`) credentials on HTTP Request
  nodes — attach `WappiAuthToken` via the public REST API (`PUT /api/v1/workflows/{id}`) instead.
- **Prod pass**: recreate the WappiAuthToken credential and repoint the 4 HTTP nodes' credential id
  (dev id `ZowntFGvApDJ7UzQ`), import with fresh (empty) staticData, activate. Nothing else to wire —
  no webhook, no Supabase.

## Import / export (local DEV server, `~/.n8n`)

Stop the n8n server first (CLI talks to the SQLite DB directly).

```bash
# import the canonical set (ids preserved from each file's top-level "id")
n8n import:workflow --separate --input=Tools/n8n/workflows/

# re-export from the local server after editing in the UI (then re-inject ids if needed)
n8n export:workflow --backup --output=/tmp/n8n-export
```

**Editing an already-active workflow via the n8n-mcp `update_workflow` tool (Task 8 gotcha):**
`update_workflow` mutates the workflow's *draft* only — a live webhook keeps serving
whatever was last **published**, even though `get_workflow_details`/a raw `GET` on the
edited workflow immediately shows the new nodes/connections as present. The two ids to
compare are `versionId` (the draft you just wrote) and `activeVersionId` (what the live
webhook actually runs); if they differ, the edit is invisible to real traffic. Call
`publish_workflow` right after any `update_workflow` against an active orchestrator, and
re-check `versionId == activeVersionId` before probing — skipping this silently ran the
OLD (pre-edit) chain against two Task 8 fix-round probe calls and left two stray cloned
workflows behind (found via `search_workflows` and cleaned up by hand).

**Two more `update_workflow` gotchas (Task 12, silent — no error is raised either way):**
(1) an `addNode` operation's `node` object silently DROPS `onError`/`retryOnFail`/
`maxTries`/`waitBetweenTries`/`executeOnce` even though you can pass them — the tool
accepts the call and reports success, but a raw `GET` afterward shows none of them
landed. These five fields need a SEPARATE `setNodeSettings` operation (by `nodeName`)
run right after the node exists; verify with a raw `GET`, don't trust the `addNode`
response. (This bit Task 12 hard: an HTTP node meant to fail open on a Wappi 400 had no
`onError` at all, so the first real test aborted the WHOLE execution instead of
continuing to the next candidate.) (2) `setNodeParameter`'s `path` is a JSON Pointer
relative to the node's OWN `parameters` object, NOT the node root — `/query` reaches
`node.parameters.query`; prefixing it with `/parameters` (i.e. `/parameters/query`)
does not error, it just writes into a new, wrong, nested `node.parameters.parameters.
query` key and leaves the real field untouched. Same trap at any depth (a Set node's
`/assignments/assignments/0/value`, not `/parameters/assignments/assignments/0/value`).
When in doubt, use `updateNodeParameters` with `replace:true` and the full parameters
object instead — it has no such ambiguity — and always verify via a raw `GET`, since
both failure modes return HTTP 200 with `appliedOperations` matching what you sent.

## Prompt composition (`inject-prompts.py`) and the Task 13b caching-floor gate

`Tools/n8n/inject-prompts.py` composes each vertical's `PROMPTS[vid]` value as
`<vertical_id>.md + "\n\n" + _core.md + "\n\n" + _universal.md` and injects it into the `Vertical
Prompt` Code node of all 4 canonical Create/Edit orchestrator workflows (`--check` diffs without
writing, exit 2 if stale; plain run writes). `_universal.md` (Task 13b, owner decision
2026-08-22) is a behavior-neutral elaboration of `_core.md`'s existing rules, appended LAST so it
sits in the STABLE part of the composed prefix — ahead of the genuinely per-bot, owner-editable
fields (Additional Instructions/About Business/Products/Services) `Set Fields` appends after it —
existing purely to push every bot's static `systemMessage` prefix past OpenAI's practical
prompt-caching floor (see `.superpowers/sdd/task-13-report.md`: reliable caching needs roughly
2200+ nominal tokens; a modest real bot profile does not reach that on vertical+core alone).

**Since Task 13b, `inject-prompts.py` has a hard runtime dependency on `tiktoken`** (first
third-party Python dependency in this toolset — every other script here is stdlib-only). Both
`--check` and a real write run `assert_token_floor()` BEFORE touching any workflow file: it
recomposes each vertical's MINIMAL-profile `systemMessage` (empty description/instructions, 0
products/services — the shortest a real bot's prompt can ever be) and asserts every one is
`>= TOKEN_FLOOR` (2300) tokens by `tiktoken.get_encoding("o200k_base")`. **If `tiktoken` is not
importable, the script hard-fails with install instructions — it never silently skips the
check**, since a missing tokenizer must never be mistaken for a passing gate. Install it into a
venv, never globally:
```bash
python3 -m venv /tmp/tiktoken-venv && /tmp/tiktoken-venv/bin/pip install tiktoken
/tmp/tiktoken-venv/bin/python3 Tools/n8n/inject-prompts.py --check
```
Negative-tested (Task 13b fix round): a scratch copy with `_universal.md` truncated to one
sentence dropped all 6 verticals to 967–1009 tokens and the gate correctly refused with a
per-vertical breakdown, confirming the assert discriminates rather than being vacuously true.

## Re-pointing the RevenueCat webhook after a tunnel rotation

`rotate-tunnel.py` fixes the four places listed in its own docstring. The RevenueCat
webhook is a **fifth** one it does not touch, because it lives in a third-party dashboard
with no local artifact: after every `cloudflared` restart the URL RevenueCat POSTs to is
dead, purchases stop reaching `subscribers`, and nothing in this repo reports it.

1. **Read the current tunnel URL.** `n8nBaseUrl` in `Assets/StreamingAssets/secrets.json`
   (gitignored) is the live value — `rotate-tunnel.py` writes it there and it is what the
   app itself uses. `python3 Tools/n8n/rotate-tunnel.py --dry-run` prints it alongside
   everything that would change; `Tools/n8n/dev-tunnel.md` documents the manual flow, and
   Option 1's *named* tunnel avoids this whole chore by never rotating.
2. **Build the endpoint**: `<tunnel>/webhook/RevenueCatEvent` — the path is the `Webhook`
   node's `path` in `Tools/n8n/workflows/ZGYr6srzS3rSSXHp-RevenueCat_Events.json` and must
   match it exactly (no trailing slash, `/webhook/` not `/webhook-test/`, which is the
   editor-only test URL and is live only while the canvas has "Listen for test event" armed).
3. **Update it in RevenueCat**: dashboard → your project → **Integrations → Webhooks** →
   the existing webhook → *Webhook URL*. Save; RevenueCat's own "Send test event" is the
   quickest confirmation, and a `200 {"success":true}` (or `{"noop":true}` for an event
   type the mapper deliberately ignores) means the whole chain is live.
4. **The Authorization header is NOT rotated with the URL.** Its value lives ONLY in the
   n8n credential named **«RevenueCat Webhook»** (`httpHeaderAuth`, header name
   `Authorization`) and in the RevenueCat webhook's own *Authorization header* field — it
   is deliberately absent from every file here, including this one, and from the exported
   workflow JSON (which references the credential by id). Leave that field untouched when
   changing the URL; if it ever has to be re-minted, read it from the n8n credential UI and
   paste it straight into the dashboard, never into the repo.

A rotation that misses this is silent in exactly one direction: the app keeps reading usage
(GetUsage is called by the app over the same fresh tunnel), while purchases and renewals stop
landing — so a subscriber quietly reads as `trial`/`trialing`. RevenueCat retries failed
deliveries for a while, so re-pointing promptly usually backfills.

## Prod instance — n8n.choosereply.com (since 2026-08-27)

Production n8n is SELF-HOSTED: Docker `docker.n8n.io/n8nio/n8n` (2.36.7 at migration) behind Caddy on the
owner's ps.kz VPS (Ubuntu 24.04, 87.199.130.239; SSH alias `choosereply`, key `~/.ssh/choosereply`; compose
project in `~/choosereply/`). The old `bagkz.app.n8n.cloud` Cloud workspace is DELETED — treat any bagkz
mention below as historical. Migrated 2026-08-27 with ids preserved end-to-end: all 7 dev credentials
(decrypted export → import, so workflow credential bindings survived verbatim) + the 16 canonical workflows;
14 activated, the 2 clone templates left inactive. The two n8n-API credentials (`n8nAPIKey` httpHeaderAuth,
`n8n account` n8nApi) hold the PROD key/baseUrl on prod. Prod API key: `Tools/n8n/.secrets/prod-api-key.txt`
(gitignored). Canonical JSONs now carry `https://n8n.choosereply.com` (Wappi callbacks + the handlers'
`/api/v1` clone/activate calls) and the `/activate ` trailing space is fixed — follow-ups 1–3 below are DONE
for prod. Still open after the switch: `rotate-tunnel.py` remains dev-only (its `CLOUD_WEBHOOK_PREFIX` still
says bagkz, and it rewrites `secrets.json` `n8nBaseUrl` to a tunnel while `n8nAPIKey` now holds the PROD
key — adapt before the next dev-tunnel session); the pre-17a dev clone `jICKoC6QKucHcryV` («16») was
deliberately NOT migrated (stale Count Dialog — delete + recreate that bot in the app); RevenueCat's
dashboard webhook URL must be re-pointed by the owner to `https://n8n.choosereply.com/webhook/RevenueCatEvent`
(Authorization header unchanged).

## Known follow-ups before this is production-/dev-ready

1. **Credentials are not in these files** (referenced by id only). The local server has none yet —
   recreate WappiAuthToken, n8nAPIKey, OpenAi, Supabase, Cohere, Postgres before the workflows run.
2. **Create/Edit handlers hardcode `https://bagkz.app.n8n.cloud/api/v1/...`** for their clone/activate
   calls. For true local dev, point these at `http://localhost:5678/api/v1/...` + a local API key.
3. **`CreateWhatsappWorkflow`** has a trailing space in the `/activate ` URL — fix during the prod pass.
4. **Edit handlers** assume target node indices (`nodes[5]` is the AI agent) and have a `Set Bussiness Type`
   node-name typo + leftover unused credential refs — clean up during the prod pass.
5. **BILLING REPLICATION ORDER (Task 17a) — import both bot templates BEFORE any bot is created,
   and recreate every pre-existing clone** *(superseded 2026-09-22 by `update-clones.py`, which updates a clone IN PLACE —
   see «Stage 7 tools» below; the paragraph is kept for the history of WHY clones drift).* A bot workflow is a CLONE of
   `4wYitz5ek30SVNlT`/`4VN3gsFaC2HUYmcc` taken at creation time, and the Edit handlers PUT the
   clone's *own* json back — they never re-splice nodes. So a clone made before the reserve change
   keeps the OLD `Count Dialog` forever: the top-up stays ADDED to the monthly quota and is never
   consumed, i.e. one 3 900 ₸ purchase = +500 dialogs every month for life, silently, with nothing
   in the app or the logs saying so. There is no migration for this and no way to detect it from
   the outside — the only fix is recreating the bot. (Dev had zero clones when this shipped, so
   nothing is stale there today.)
6. **The GRACE TRIAD must be settled together** by whoever first puts a real account into
   `status='grace'` — nothing does today, so the three halves have never met in production:
   dialogs are refused outright (`Count Dialog`), channel slots are 0 (`Compute Slot Limit`, Task
   17a M-2), and suggestions ARE allowed (the panel is the fallback) — **but the client only
   flips a chat into effective-«Вместе» when it is OVER QUOTA** (17b's `QuotaFallbackPolicy`). A
   grace account that is *under* its quota therefore gets silence with no panel: the server
   refuses the dialog, the client sees no reason to offer suggestions. Decide the intended
   grace UX (probably: treat grace like over-quota client-side) before grace can actually happen.
7. **Request auth for `/webhook/SuggestReplies` at prod hardening.** The daily cap is per
   `app_user_id`, and the endpoint is unauthenticated — so it is griefing-bounded only while
   "the id is the secret" holds: anyone who learns a real RevenueCat app_user_id can burn that
   account's 100 requests/day. It cannot touch money or dialogs (the gate writes only
   `suggestion_counts`), and the refusal envelope deliberately carries no reason so the endpoint
   is not an account-state oracle — but the real fix is authenticating the request, the same way
   `RevenueCatEvent` already is.

## Stage 7 tools (audit remediation, 2026-09-22) — owner readiness + managed deployment

All READ-ONLY against prod unless the flag says otherwise; every mutating path was exercised on staging (`staging/`).

- `update-clones.py` — managed update of existing bot clones (a Create-made workflow = template graph + 4 identity fields:
  name, Webhook path/webhookId, AI Agent systemMessage, settings, plus credential ids per TYPE). Dry-run by default (node diff +
  preserved identity + the pre-update `versionId` as the rollback pointer); `--apply --yes [--only <id>]` PUTs, re-publishes an
  active clone, and READS BACK (node set == canonical, path/prompt kept, `crTemplateVersion` stamp in a sticky note because
  `meta` is read-only on PUT, `activeVersionId == versionId`). Idempotent by stamp. Acceptance: `staging/e2e-clone-update.py`
  (an old 33-node clone → 57 nodes, replies once, ledger `sent`).
- `verify-deployed.py [--base …] [--json out]` — read-back of an instance vs the canonical exports: per canonical workflow
  OK/DIFF/MISSING (graph hash with identity blanked), `published_is_stored`, clone stamps, unknown workflows. Prod snapshot of
  2026-09-22: `docs/audits/2026-09-22-prod-readback.json` (20 DIFF, 5 MISSING, clones «13»/«15» behind — the deploy scope).
- `build-test-dialog.py` → `crTestDialog0001-Test_Dialog.json` + `crHandoffDry0001-Handoff_Request_Dry.json`: `/webhook/TestDialog`
  answers the owner's test message with the bot's REAL prompt (read through the admin API from the bot's own workflow, never a
  copy), same model/params/tools, NO memory node, NO send, NO dialog count, NO ledger row; the handoff tool is the DRY twin, which
  records the intended action in `test_dialog_actions` (SQL `sql/2026-09-22-test-dialog.sql`) and the response reports it as
  `wouldHandoff`. Gate rule: profiles[profileId] + workflows[workflowId] of the calling install. Protected in Set Workflow State.
  Which version (R15, 2026-09-27): an ACTIVE bot is tested by its PUBLISHED graph (`activeVersion` — what clients get, never an
  editor draft saved without Publish), an INACTIVE bot by its draft (what activation publishes); the response carries
  `promptVersion {source: published|draft, versionId, draftPending}`; an unreadable published version or a missing «AI Agent»
  node is `bot_prompt_unavailable` (no `nodes[5]` fallback). Behaviour: `tests/test_test_dialog.py`.
  Verifier: `verify-history-handoff.py` (`verify_test_dialog`). Staging: `staging/e2e-test-dialog.py` (8 scenarios; two of them
  edit the staging WA bot into draft ≠ published / inactive and restore it; V9: one sends a catalog/price-list conflict
  through the reconciling price-list tool [llm], one runs `crPriceSearch001` itself through a temporary probe webhook —
  no model, deleted after use).
- Handoffs `op: "status"` (`build-handoff-workflows.py`) — the last 12 `reply_events` rows for the owner's profiles (state, time,
  execution id, chat masked to its last 4 digits — never the client's text); the app's `ReplyOutcomeCopy` names each state.
- `eval/run-eval.py` (stage 6; harness h8, set v1.5 since T11b 2026-10-12) — before/after prompt measurement through the
  STAGING-ONLY completions proxy `stgEvalRunner001`; compare only runs of one scenario version + one harness version. Results
  in `eval/results/`. The scenario roles `owner` / `bot_unknown` reach the model with the memory marks the writers store at
  `--prompts-ref` (apply-history-handoff.py's constants, parsed at that ref; none before T6) — pass the same ref as
  `--search-ref` for a «before». `eval/test_checks.py` pins the checks and the rendering; `eval/rescore.py` re-scores saved
  runs offline (a new harness must move no saved verdict — pinned for the v1.3 and v1.4 runs). h8 (T11b): the runs go out
  INTERLEAVED — every scenario's repeats spread over the whole run, round-robin over the scenarios, never N identical requests
  back to back (T11 found such repeats strongly correlated in time); the sides of a comparison run as ONE schedule
  (`--side NAME=REF`, or `--plan eval/plans/<name>.json` = blocks × sides → `<label>-<block>-<side>.json`), the sides of one
  repeat back to back; `--workers N` parallel requests in that order; pinned by `eval/test_schedule.py`. Set v1.5:
  `no_tool:create_handoff` on every auto scenario whose question the business data answers and whose design calls for no
  handoff — the classification (and the reason for every unchecked one) is pinned in `eval/test_checks.py`.


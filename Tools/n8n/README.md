# n8n workflows

Canonical n8n workflows for the BAGKZ app. The app talks to n8n for bot creation,
prompt editing, RAG file upload/delete, and (in progress) live reply suggestions.

## Layout

- `workflows/` — **committed source of truth**: the 16 workflows the app actually depends on.
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

## The 16 canonical workflows

| id | name | role |
|----|------|------|
| `XuvOp7TxOImOAmlj` | CreateWhatsappWorkflow | App webhook `/webhook/CreateWhatsappWorkflow` — clones the WhatsApp template per bot **Task 17a:** `Register Profile` now (a) ensures the owner's `subscribers` row in the SAME statement (a preceding `insert … on conflict do nothing` CTE) — `Ensure Subscriber` upstream is fail-open, and a profile registered without an owner row was BOTH unmetered (`Count Dialog` inner-joins `subscribers`, finds nothing, and Quota Decision fail-opens) and invisible to both Profile Lifecycle Sweep branches; and (b) refuses `profile_id` `'-1'`/empty — `'-1'` is the client's «channel not authorized» sentinel and `profile_id` is the PRIMARY KEY, so one shared row would have been re-owned by every account in turn. `Compute Slot Limit` treats `grace` like `expired` (0 slots), because `Count Dialog` already refuses dialogs at `status in ('expired','grace')` — handing out slots there would create new paid Wappi profiles for a bot that cannot answer. |
| `Uz6HBBUpAiUqVysB` | CreateTelegramWorkflow | App webhook `/webhook/CreateTelegramWorkflow` — clones the Telegram template per bot **Task 17a:** `Register Profile` now (a) ensures the owner's `subscribers` row in the SAME statement (a preceding `insert … on conflict do nothing` CTE) — `Ensure Subscriber` upstream is fail-open, and a profile registered without an owner row was BOTH unmetered (`Count Dialog` inner-joins `subscribers`, finds nothing, and Quota Decision fail-opens) and invisible to both Profile Lifecycle Sweep branches; and (b) refuses `profile_id` `'-1'`/empty — `'-1'` is the client's «channel not authorized» sentinel and `profile_id` is the PRIMARY KEY, so one shared row would have been re-owned by every account in turn. `Compute Slot Limit` treats `grace` like `expired` (0 slots), because `Count Dialog` already refuses dialogs at `status in ('expired','grace')` — handing out slots there would create new paid Wappi profiles for a bot that cannot answer. |
| `3qax5J9u2qsT9Vao` | Edit Whatsapp Workflow | App webhook `/webhook/EditWhatsappWorkflow` — edits a bot's system prompt |
| `TwWPW3gIyjZS3foR` | Edit Telegram Workflow | App webhook `/webhook/EditTelegramWorkflow` — edits a bot's system prompt |
| `KoTuIlk4LMrlvnWI` | Upload File | App webhook `UploadFile` — ingests files into the Supabase vector store; stamps `botWaId`/`botTgId`/`fileId` on every chunk; extension routing is case-insensitive; archives the uploaded bytes to Storage `price-lists/{fileId}` (dead-end branch, `onError: continue` — never fails the upload); unsupported types get an explicit 415; photos (jpg/jpeg/png/webp client-side) route to OpenAI gpt-4o-mini vision extraction (422 `no_price_data` gate if unreadable), archived like all other uploads |
| `ZTqpumOpL1rNDOp6` | Delete File | App webhook `DeleteFile` — body `{ fileId }`; deletes that file's chunks from `documents` AND its stored original `price-lists/{fileId}` (404 tolerated for pre-bucket files), returns `{ success, deletedChunks }` |
| `4wYitz5ek30SVNlT` | WhatsApp Bot | **Clone source** for every WhatsApp bot (referenced by literal id in CreateWhatsappWorkflow); retrieval self-scoped by `botWaId = {{ $workflow.id }}`; **Phase 10:** carries the pre-generation debounce+combine splice on the `Suppressed?` FALSE branch (see note below) **Task 17a (top-up = RESERVE, owner decision 2026-08-26, spec §2):** `Count Dialog`'s `quota` CTE is the BASE plan number only — the top-up is no longer added to it. A NEW dialog that is already over the base quota consumes ONE reserve unit via an `update subscribers … set topup_balance = topup_balance - 1 … where topup_balance > 0` CTE, and that UPDATE's row lock is the concurrency control: READ COMMITTED re-evaluates the `> 0` guard against the freshly committed row, so two racing new chats end up one allowed / one refused and the balance can never go negative (verified live with a forced-overlap race, `probe-billing.py --reserve`). Continuations and under-quota dialogs cost nothing; `expired`/`grace` still consume nothing whatever the balance. `allowed` deliberately includes `exists(reserve)` — `on conflict do nothing` can swallow our own insert when a racer wrote the same key first, and that conflict proves the row exists, so without the term a race would spend a unit AND refuse the reply. |
| `4VN3gsFaC2HUYmcc` | Telegram Bot | **Clone source** for every Telegram bot (referenced by literal id in CreateTelegramWorkflow); retrieval self-scoped by `botTgId = {{ $workflow.id }}`; **Phase 10:** carries the same debounce+combine splice on the `Suppressed?` FALSE branch (see note below) **Task 17a (top-up = RESERVE, owner decision 2026-08-26, spec §2):** `Count Dialog`'s `quota` CTE is the BASE plan number only — the top-up is no longer added to it. A NEW dialog that is already over the base quota consumes ONE reserve unit via an `update subscribers … set topup_balance = topup_balance - 1 … where topup_balance > 0` CTE, and that UPDATE's row lock is the concurrency control: READ COMMITTED re-evaluates the `> 0` guard against the freshly committed row, so two racing new chats end up one allowed / one refused and the balance can never go negative (verified live with a forced-overlap race, `probe-billing.py --reserve`). Continuations and under-quota dialogs cost nothing; `expired`/`grace` still consume nothing whatever the balance. `allowed` deliberately includes `exists(reserve)` — `on conflict do nothing` can swallow our own insert when a racer wrote the same key first, and that conflict proves the row exists, so without the term a race would spend a unit AND refuse the reply. |
| `lmjYsdNcQA2IE5rl` | Delete Bot Files | App webhook `DeleteBotFiles` — body `{ botWaId, botTgId, waProfileId, tgProfileId, appUserId }`; sweeps ALL of a deleted bot's RAG chunks + stored originals (guards the `"-1"` unauthed sentinel), and — since 2026-08-27 — first retires the bot's `bot_profiles` rows by the two profile ids (`deleted_at=now()`, `deleted_reason='app_delete'`), freeing the channel slots in the same execution. The retire node sits BEFORE the chunks delete (a chunks failure must never block the slot release) and matches by `profile_id` ONLY — never `app_user_id`, which can drift between create and delete (RC anonymous-id rotation / pre-init device-id fallback); `appUserId` is audit-only. This synchronous retire is the ONLY slot release for `status='active'` owners (both Profile Lifecycle Sweep branches deliberately skip them). Response `{ success, deletedChunks, deletedFiles, retiredProfiles }`; old clients without the new fields are a clean no-op retire. Probe: `probe-billing.py --app-delete-retire` (Part 11) |
| `2htWSV5IHO8E2CgB` | Dashboard Outcomes | App webhook `DashboardOutcomes` — body `{ profileIds }`; classifies conversation outcomes from `n8n_chat_histories` into `conversation_outcomes`, returns them for the «Сводка» dashboard |
| `2islisFH7jjLoPQM` | Delete Orphan Profiles | **Scheduled, hourly** (no webhook) — server-side TTL sweep deleting Wappi profiles that stay unauthorized ≥ 24h; see below |
| `SCLcpn6DMDG3Z4VN` | Set Reply Mode | App webhook `SetReplyMode` — shared always-active; body `{ profileIds:[...], chatId:"*"\|"<id>", suppressed:bool }`; validates (malformed → `bad_request` before any DB write), fans out one item per surviving profileId, upserts each into `reply_mode_flags` (on conflict do update). The semi-auto «Авто/Вместе» suppression write path (SUP-02); the bot templates' gate reads the same table. Deployed by `build-set-reply-mode.py` (Postgres cred bound by explicit id `vvRrFiEXzLVqKjOx` — dev's single Postgres cred as of 2026-07-22); id `SCLcpn6DMDG3Z4VN` assigned + activated on first deploy 2026-07-22, filename finalized to `SCLcpn6DMDG3Z4VN-Set_Reply_Mode.json` in 09-04 |
| `9PTyYcelRQI7bGDb` | Suggest Replies | App webhook `SuggestReplies` — body = frozen v1 request (`{ v, requestSeq, chatId, botWaId, businessTypeId, catalog, steerTowardText, messages… }`); known-invalid requests (v mismatch / missing `chatId` / empty `messages`) short-circuit straight to `generation_failed` — zero LLM spend on the unauthenticated webhook; optional channel-branched tenant-scoped RAG pre-retrieval (one single-key filter per channel: `botWaId` WA / `botTgId` TG, topK 5, skipped on `""`/`"-1"`) → one gpt-4o-mini call (strict json_schema, closed 6-label enum) → Code validation (exactly 4 distinct enum-labeled moves, ≤300 clamp, markdown-strip, one retry then `generation_failed`) → returns `{ v:1, requestSeq, suggestions:[{text,label}×4] }` for the semi-auto «Вместе» reply panel. Deployed from the committed canonical JSON by `build-suggest-replies.py` (dev id here; prod bagkz replication pending). Adversarially verified on dev 2026-07-10 (6-case matrix — grounding / missing-data / steer / injection / trivial / sentinel — plus format-hijack + malformed→`generation_failed`, **zero fixes needed**); dev RAG grounding is **catalog-only** until Supabase `documents` are seeded — RAG-with-data deferred to prod replication **Task 17a — subscription gate + daily cap (owner decision 2026-08-26, spec §5.3):** suggestions stay FREE (they never touch `dialog_counts` — asserted by the parity gate) but the endpoint is no longer billing-unaware. `If invalid?`'s valid branch now enters `Suggestion Gate` (Postgres, fail-open like `Count Dialog`) → `Gate Decision` (Code; rebuilds the payload from `$('Prep')` because a Postgres node drops the incoming item) → `If Gate Allows` → **true** the normal `If skipRag?` path, **false** straight to `Build Response`, so a refusal reaches NO retrieval and NO LLM (proven by node path: execs 3813/3824/3833 stop at Build Response). One statement does lookup + increment + verdict against `suggestion_counts` (migration `Tools/n8n/sql/2026-08-26-suggestion-counts.sql`; PK `(app_user_id, d)`, Asia/Almaty day, cap 100/day — request 101 is the first refused). Refused: `expired`, an id with no `subscribers` row (the id is this unauthenticated endpoint's only secret), and a missing/empty `appUserId` — the client ALWAYS sends it. Allowed: `active`/`trialing`/`grace`, INCLUDING over-quota, because the panel IS the fallback the dialog quota routes owners into. A refusal reuses the existing `{error:'generation_failed'}` envelope with NO `reason` on the wire (review N-4: the endpoint is unauthenticated, so naming the refusal reason would confirm a guessed id's existence/state); the diagnostic `gateReason` lives on Gate Decision's execution-log output, pinned by the parity gate in both directions. Probe: `probe-billing.py --suggestions`. |
| `ZGYr6srzS3rSSXHp` | RevenueCat Events | App webhook `RevenueCatEvent` (`Tools/n8n/workflows/ZGYr6srzS3rSSXHp-RevenueCat_Events.json`) — mirrors RevenueCat subscription events into `subscribers` (billing schema, Task 6). Auth is n8n's **native** `httpHeaderAuth` credential (`RevenueCat Webhook`, header `Authorization`) bound on the Webhook node — no in-workflow secret compare, so the exported JSON carries no secret, only a `{id,name}` credential reference. Chain: Webhook (`responseMode: responseNode`) → `Map Event` (Code, verbatim event→row mapping; `alwaysOutputData: true` so `CANCELLATION`'s deliberate `return []` still emits one empty item instead of killing the run downstream) → `If Has Payload?` (non-empty `app_user_id`) → **TRUE**: `Upsert Subscriber` (Postgres upsert; `onError: continueErrorOutput` routes a failed write to `Respond Error` / HTTP 500) → `Respond 200`; **FALSE**: `Respond No-Op` (200). Net effect: a real event only acks 200 **after** the Postgres write commits — a genuine DB failure surfaces as a non-2xx so RevenueCat retries — while `CANCELLATION` (which intentionally writes nothing) still gets a clean 200 off the FALSE branch. Gotcha worth keeping for any future Postgres node here: `queryReplacement` must be **one** `={{ [...] }}` array expression, never comma-joined `{{ }}` fragments — the comma-joined form stringifies a JS `null` to the literal text `"null"`, which Postgres then rejects outright for a `timestamptz` column. Probed by `Tools/n8n/probe-billing.py` (`RC_WEBHOOK_SECRET` env; 401/403 no-auth + 200×4 real events, extendable in Task 8-9); permanent DB read-back is deliberately deferred to Task 11 (`GetUsage`), not a bespoke debug webhook here. **Since Task 16 the chain forks twice before the original one:** `Map Event` → `If Is Transfer?` → TRUE: `Transfer Subscriber` → `Respond 200`; FALSE: `If Needs Consolidation?` → TRUE: `Consolidate Aliases` → the `If Has Payload?` chain above / FALSE: straight to it. Both new Postgres nodes route their error output to their OWN 500 responder (`transfer_failed` / `consolidation_failed`), so a probe can tell which write failed. **The two forks exist because RevenueCat moves an identity in two different ways, and only one of them is a `TRANSFER`:**<br><br>**(a) Alias merge — what a same-device REINSTALL actually produces, and the one that cost real money here.** RC does NOT fire `TRANSFER` for it; it renames the identity: the event arrives under the NEW `app_user_id` with the old one in BOTH `original_app_user_id` and `aliases[]` (live proof on this instance — exec 3144 pre-reinstall carries `aliases:[old]`; execs 3150/3151/3153 post-reinstall carry `app_user_id: NEW`, `original_app_user_id: OLD`, `aliases:[NEW, OLD]` and no `transferred_*` keys at all; RC's docs say to «search both the original_app_user_id and the aliases array»). `Map Event` computes the alias set minus the current id — empty on a healthy install, where `aliases` is just `[self]`, so `If Needs Consolidation?` skips the Postgres round-trip entirely — and `Consolidate Aliases` moves the old rows' `topup_balance` onto the current id and retires them. **It deliberately does NOT copy their plan/status/period:** the event riding behind it already carries the authoritative ones, and this path fires on EVERY event for the rest of the account's life, so a snapshot copy here would be permanent downgrade risk for zero gain. **It also sits BEFORE `Upsert Subscriber` on purpose:** that node's `topup_balance + $5` is not retry-idempotent, so a failure *after* it would let RevenueCat's redelivery double-credit the event's own top-up — failing on this side is safe in both directions. When there is nothing to move it writes nothing at all, and when the destination row does not exist yet it creates it with `trial`/`trialing` defaults so the upsert behind it can layer the real plan on top without touching the balance.<br><br>**(b) `TRANSFER` — a cross-account move.** Its payload carries **no** `app_user_id`/`product_id`/`entitlement_ids`, only the `transferred_from`/`transferred_to` String arrays, which is exactly why it cannot ride the existing `If Has Payload?` gate. `Transfer Subscriber` additionally moves the SNAPSHOT (plan/status/period/product) — to EVERY id in `transferred_to` («App User ID(s) receiving»), while the balance goes to the first one only, since money cannot be duplicated.<br><br>Both are ONE parameterized statement (= one transaction, so a move and a retirement can never half-apply), and four details in them are load-bearing. **(1) Sources are taken `for update`, and the credited amount is produced under that lock** — not by a snapshot `sum(topup_balance)`. This instance demonstrably delivers same-subscriber events concurrently (execs 3128/3129 overlap by 2.4s; 3150/3151 arrive in the same second), and a snapshot read double-credits in that race. Proven: 4 concurrent deliveries of one alias event → exec 3336 retired the row and credited 500, while 3337/3338/3339 blocked on the lock, re-read the settled row and wrote nothing. **(2) The retirement is guarded** (`status <> 'expired' or topup_balance <> 0 or current_period_end is null or current_period_end > now()`), so a settled alias set — which rides along forever — writes zero row versions, and a replayed delivery credits nothing. **(3) `current_period_end = least(coalesce(period, now()), now())`, and BOTH coalesces matter.** This is the entire hand-off to `Profile Lifecycle Sweep`'s Branch A, whose `Candidates` query reads `s.status in ('expired','grace') and coalesce(s.current_period_end, now() - interval '99 days') < now() - interval '3 days'`. A still-in-the-future paid period would park the abandoned identity's Wappi profiles for up to a month at 23₽/day; a NULL period is the OPPOSITE failure — that `coalesce(…, -99 days)` makes the row a candidate INSTANTLY, so a top-up-only identity (whose plan/status/period a `NON_RENEWING_PURCHASE` never touches) would lose its REAL Wappi profiles within one 6h tick with zero grace. `now()` gives it the same 3 days every other churned owner gets, and the value only ever moves DOWN. NO new deletion machinery exists anywhere: retirement is entirely «make the row look like ordinary churn». `status='expired'` also stops the abandoned identity's auto-replies through the existing quota gate (`Count Dialog` refuses to open a NEW dialog for `status in ('expired','grace')`; a chat already counted TODAY keeps answering until the Asia/Almaty date rolls over). **(4) The snapshot moves as a UNIT, and only when the source is strictly newer AND not deader.** A `RENEWAL` under the new id can legitimately land BEFORE a `TRANSFER`, and RC retries any non-2xx — so the tie goes to the destination, which is also what makes a duplicate delivery a no-op. Freshness alone is not enough either: an EXPIRED source with a LATER period would otherwise stamp `expired` onto a live `trialing` destination (whose NULL period always loses the comparison) and silence a healthy fresh install through that same quota gate. Money still moves in that case; only the snapshot is refused.<br><br>Same task also fixed `PRODUCT_CHANGE`, which carries the OLD sku in `product_id` and the new one in `new_product_id` (live: exec 3128 sent `product_id: sub.start.month` for an upgrade TO `sub.business.month`) — the mapper now prefers the latter. The `??` there is deliberate and must NOT be «simplified» to `'new_product_id' in e`: it treats an absent key and an explicit `null` identically. On this instance all 34 real deliveries carried the key only on `PRODUCT_CHANGE`, but RC documents it as «omitted when null», not «never null».<br><br>Probed by `probe-billing.py --transfer` (5 TRANSFER cases + the alias-merge cases, every one exact-value: the summed top-up, the clamped source period, a renewal landing before the transfer, an expired source vs a live destination, a NULL-period source, multi-id arrays, replay, and a 4-way concurrent burst) and by `probe-billing.py --branch-a-trace`, which runs the sweep's own `Candidates` query against real rows plus a control row proving the clamp is what makes the hand-off work **Task 17a (I-4 — paid usage moves with the money):** both `Consolidate Aliases` and `Transfer Subscriber` now carry the CURRENT-MONTH `dialog_counts` rows of every retired/source id onto the destination (`insert … select distinct … on conflict do nothing`). Without it a reinstall reset the monthly counter — «300 из 300» became «0 из 300» on the same paid subscription. Existence of a row IS the count, so the copy is idempotent by construction and a settled replay writes ZERO new row versions (measured via `xmin`, same guarantee as `credited`/`retired`); `distinct` covers two aliases carrying the same `(chat, date)`. Old rows are left on the retired id — nothing reads them. On the TRANSFER path the usage rides the SNAPSHOT (all `transferred_to` ids), while the balance still goes only to the first. Asserted by `probe-billing.py --transfer` cases A1/A2 (`used` moves, and a replay does not grow it). |
| `jtbssfzXbOxwTK4k` | Get Usage | App webhook `GetUsage` (`Tools/n8n/workflows/jtbssfzXbOxwTK4k-Get_Usage.json`) — `authentication:"none"` (the `appUserId` in the body IS the secret, same v1 posture as every other app webhook). Webhook → `Read Usage` (Postgres, row-safe one-row CTE left-joining `subscribers`/`dialog_counts`/`bot_profiles` so an unregistered `appUserId` still gets one row of trial/0 defaults rather than zero rows; `onError: continueErrorOutput` → `Respond Error` 500 — this read endpoint fails **closed**, the opposite of the bot-path's fail-open, so the client keeps its last-known cache instead of trusting a wrong zero) → `Shape Response` (Code, plan→quota map `{trial:150,start:300,business:1000,network:3000,none:0}`) → `Respond 200`. Probed by `Tools/n8n/probe-billing.py --usage` with REAL exact-value assertions over plain HTTP (no Postgres/n8n-mcp access needed — this is a pure unauthenticated read) |
| `fXYpCXPKw92EzRz8` | Profile Lifecycle Sweep | **Scheduled, every 6 hours** (no webhook) — Task 12's day-6-retro-charge guard, a separate concern from the hourly `Delete Orphan Profiles` below (that one only ever cleans up NEVER-authorized profiles; this one acts on registered, possibly-authorized ones based on OUR billing state). Branch A: `Candidates` (Postgres — alive `bot_profiles` joined to `subscribers` where `status='trialing'` and `created_at` older than 4d17h, OR `status in ('expired','grace')` and `current_period_end` more than 3d past) → `Trial/Churn Dry Run?` → live: `Capture Delete Fields` (Set — snapshots profile_id/channel/app_user_id/reason off `Candidates` while pairing is still guaranteed 1:1; the LAST safe point for a `.item` back-reference in this chain) → `HTTP Delete Profile` (POST `{api\|tapi}/profile/delete`, `WappiAuthToken` cred, `onError:continueRegularOutput`) → `Delete Succeeded?` (`$json.status=='done'`) → **true**: `Mark Deleted` (`returning profile_id, app_user_id, channel, deleted_reason` — the `RETURNING` clause is load-bearing, not decorative: a bare `UPDATE` with none collapses N≥2 successful items into ONE output item with an ARRAY `pairedItem`, which crashed the whole execution the first time two profiles for the same owner expired in the same run, see the fix-round note below) → `Mark Succeeded?` (`!$json.error`) → **true**: `Demote Trialing` (`returning app_user_id, ... as deleted_reason`, reads `$json.*` from Mark Deleted directly — zero `.item` back-references on this path; only ever touches `status='trialing'` rows, a no-op for churn) → `Stamp Deleted`; **false**: `Stamp Mark Failed` — **false** (Delete Succeeded?): `Stamp Delete Failed`, `deleted_at` stays NULL — retried next run, never marked on a failed delete. Branch B (liveness reconcile, closing the gap flagged in Task 8's review): `List WA/TG Profiles` (GET `profile/all/get`, both bases, `onError:continueRegularOutput` + `retryOnFail` — a Wappi hiccup here must route into `skip_invalid_fetch`, not crash) → `Alive Registry` (Postgres — alive `bot_profiles` LEFT JOINed to `subscribers`, excluding `status='active'` AND anything registered in the last hour — the age floor closes a fetch-snapshot race against a just-registered profile) → `Compute Liveness Diff` (Code — validates each base INDEPENDENTLY: no `.error` shape, `status=='done'`, `profiles` coerced from Wappi's documented `null`-when-empty shape to `[]`, AND a well-formed-but-EMPTY list floored to invalid when the registry holds ≥1 alive row for that channel — a genuinely-empty list is the ONE case that looks identical to a silently-wrong one, so it gets the same `skip_invalid_fetch` treatment as a hard fetch error rather than confidently retiring every row; **and, since Task 15b, a PROPORTIONAL RETIRE CAP** — if a base's would-be retirements exceed `max(2, 50% of that base's alive registry rows)` the whole base is skipped for that run with `action:"skip_retire_cap"` and a `reason` carrying both counts. That closes the gap the empty-list floor leaves: the floor only catches a FULLY empty list, while `profile/all/get` documents **no pagination parameters at all** and returns **no envelope** (`{profiles, status}` only — no `total`/`page`/`next`/`has_more`; verified 2026-08-25 against the published WhatsApp API docs and live against both bases, where unknown query params are silently ignored rather than rejected), so a silently TRUNCATED list is indistinguishable from a complete one and every row past the cut would read as "absent from Wappi". The `max(2, …)` floor keeps ordinary single-bot cleanups working; the 50% term is what a truncation or a partial outage trips. Each row also carries a `wouldRetire` boolean so a capped run's log still says which rows the diff had flagged) → `Is Retire Candidate?` → `Liveness Dry Run?` → live: `Mark Liveness Deleted` (`deleted_reason='liveness'`, `returning ...` — same RETURNING-preserves-pairing fix as Branch A — no Wappi call, the row's absence from the real list IS the evidence). `Sweep Config` (Set) carries the single `dryRun` boolean gating BOTH branches' destructive step — **`false` (LIVE) since Task 15b, 2026-08-25.** It was held at `true` from Task 12's fix round until then, because owner-recreated post-Task-8 bots sat in the registry as `trial`/`trialing` and a live run would have deleted their real Wappi profiles. At the flip those rows no longer existed: `bot_profiles` was empty (0 rows, alive or dead), `subscribers` held one probe leftover, and Wappi's own `profile/all/get` returned zero profiles on BOTH bases — the owner had cleared his dev bots before the device pass. The first live execution (3093) was therefore a clean no-op: `Candidates` 0 items, `Alive Registry` 0 items, no destructive node reached at all. **Live-run rule from here on: after the device pass mints a real RevenueCat `app_user_id`, a bot left un-purchased is deleted by Branch A ~4d17h after registration — that is the intended day-6-retro-charge guard, not a bug. To exempt a dev/owner identity, grant it `status='active'` in `subscribers` (both branches exclude `active` uniformly); the exact statement is `sweep_dev_owner_grant_sql()` in `Tools/n8n/probe-billing.py`, printed by `python3 Tools/n8n/probe-billing.py --sweep`.** `bot_profiles.deleted_reason` (migration `Tools/n8n/sql/2026-08-21-deleted-reason.sql`) durably persists the delete reason past execution-log retention. No committed script fires this (no webhook exists) — `Tools/n8n/probe-billing.py --sweep` prints the exact fixture SQL used (original a-f set, the fix-round's 2-candidate multi-delete and empty-list-floor fixtures, and Task 15b's `sweep_retire_cap_seed_sql()` for fixture (h)), full transcript in `.superpowers/sdd/task-12-report.md` (see its "Fix round" section for the 2 Critical findings and how each was verified with real throwaway Wappi profiles) |

> ⚠️ `4wYitz5ek30SVNlT` and `4VN3gsFaC2HUYmcc` are referenced by **literal id** inside the
> two Create handlers. Never change their ids, or bot creation 404s on the clone step.
> Keep both **inactive** — they share webhook path `0091024b-7b46` and only the per-bot
> clones (with rewritten paths) ever go active.

> **Phase 10 — message-batching / debounce splice.** Both bot templates carry a pre-generation
> `Debounce Wait → Fetch Recent → Latest+Combine → Is Latest?` stage on the `Suppressed?` FALSE
> branch (before `Input type`) that coalesces a burst of multi-fragment customer messages into ONE
> combined reply — only the last fragment's execution proceeds; earlier fragments dead-end. It is
> authored by the idempotent `apply-message-batching.py` (edits both templates in place, by node
> name). The script OWNS those 4 nodes: every run upserts them from its own specs, preserving only
> the stable uuid5 `id` and the node `position` — so never hand-edit them in the JSON (a re-run
> reverts it), change the script instead. **Re-run `apply-message-batching.py` after any template
> re-import / UI round-trip**, then run `verify-message-batching.py` to gate the splice (asserts the
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
   and recreate every pre-existing clone.** A bot workflow is a CLONE of
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

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
| `XuvOp7TxOImOAmlj` | CreateWhatsappWorkflow | App webhook `/webhook/CreateWhatsappWorkflow` — clones the WhatsApp template per bot |
| `Uz6HBBUpAiUqVysB` | CreateTelegramWorkflow | App webhook `/webhook/CreateTelegramWorkflow` — clones the Telegram template per bot |
| `3qax5J9u2qsT9Vao` | Edit Whatsapp Workflow | App webhook `/webhook/EditWhatsappWorkflow` — edits a bot's system prompt |
| `TwWPW3gIyjZS3foR` | Edit Telegram Workflow | App webhook `/webhook/EditTelegramWorkflow` — edits a bot's system prompt |
| `KoTuIlk4LMrlvnWI` | Upload File | App webhook `UploadFile` — ingests files into the Supabase vector store; stamps `botWaId`/`botTgId`/`fileId` on every chunk; extension routing is case-insensitive; archives the uploaded bytes to Storage `price-lists/{fileId}` (dead-end branch, `onError: continue` — never fails the upload); unsupported types get an explicit 415; photos (jpg/jpeg/png/webp client-side) route to OpenAI gpt-4o-mini vision extraction (422 `no_price_data` gate if unreadable), archived like all other uploads |
| `ZTqpumOpL1rNDOp6` | Delete File | App webhook `DeleteFile` — body `{ fileId }`; deletes that file's chunks from `documents` AND its stored original `price-lists/{fileId}` (404 tolerated for pre-bucket files), returns `{ success, deletedChunks }` |
| `4wYitz5ek30SVNlT` | WhatsApp Bot | **Clone source** for every WhatsApp bot (referenced by literal id in CreateWhatsappWorkflow); retrieval self-scoped by `botWaId = {{ $workflow.id }}`; **Phase 10:** carries the pre-generation debounce+combine splice on the `Suppressed?` FALSE branch (see note below) |
| `4VN3gsFaC2HUYmcc` | Telegram Bot | **Clone source** for every Telegram bot (referenced by literal id in CreateTelegramWorkflow); retrieval self-scoped by `botTgId = {{ $workflow.id }}`; **Phase 10:** carries the same debounce+combine splice on the `Suppressed?` FALSE branch (see note below) |
| `lmjYsdNcQA2IE5rl` | Delete Bot Files | App webhook `DeleteBotFiles` — body `{ botWaId, botTgId }`; sweeps ALL of a deleted bot's RAG chunks + stored originals (guards the `"-1"` unauthed sentinel) |
| `2htWSV5IHO8E2CgB` | Dashboard Outcomes | App webhook `DashboardOutcomes` — body `{ profileIds }`; classifies conversation outcomes from `n8n_chat_histories` into `conversation_outcomes`, returns them for the «Сводка» dashboard |
| `2islisFH7jjLoPQM` | Delete Orphan Profiles | **Scheduled, hourly** (no webhook) — server-side TTL sweep deleting Wappi profiles that stay unauthorized ≥ 24h; see below |
| `SCLcpn6DMDG3Z4VN` | Set Reply Mode | App webhook `SetReplyMode` — shared always-active; body `{ profileIds:[...], chatId:"*"\|"<id>", suppressed:bool }`; validates (malformed → `bad_request` before any DB write), fans out one item per surviving profileId, upserts each into `reply_mode_flags` (on conflict do update). The semi-auto «Авто/Вместе» suppression write path (SUP-02); the bot templates' gate reads the same table. Deployed by `build-set-reply-mode.py` (Postgres cred bound by explicit id `vvRrFiEXzLVqKjOx` — dev's single Postgres cred as of 2026-07-22); id `SCLcpn6DMDG3Z4VN` assigned + activated on first deploy 2026-07-22, filename finalized to `SCLcpn6DMDG3Z4VN-Set_Reply_Mode.json` in 09-04 |
| `9PTyYcelRQI7bGDb` | Suggest Replies | App webhook `SuggestReplies` — body = frozen v1 request (`{ v, requestSeq, chatId, botWaId, businessTypeId, catalog, steerTowardText, messages… }`); known-invalid requests (v mismatch / missing `chatId` / empty `messages`) short-circuit straight to `generation_failed` — zero LLM spend on the unauthenticated webhook; optional channel-branched tenant-scoped RAG pre-retrieval (one single-key filter per channel: `botWaId` WA / `botTgId` TG, topK 5, skipped on `""`/`"-1"`) → one gpt-4o-mini call (strict json_schema, closed 6-label enum) → Code validation (exactly 4 distinct enum-labeled moves, ≤300 clamp, markdown-strip, one retry then `generation_failed`) → returns `{ v:1, requestSeq, suggestions:[{text,label}×4] }` for the semi-auto «Вместе» reply panel. Deployed from the committed canonical JSON by `build-suggest-replies.py` (dev id here; prod bagkz replication pending). Adversarially verified on dev 2026-07-10 (6-case matrix — grounding / missing-data / steer / injection / trivial / sentinel — plus format-hijack + malformed→`generation_failed`, **zero fixes needed**); dev RAG grounding is **catalog-only** until Supabase `documents` are seeded — RAG-with-data deferred to prod replication |
| `ZGYr6srzS3rSSXHp` | RevenueCat Events | App webhook `RevenueCatEvent` (`Tools/n8n/workflows/ZGYr6srzS3rSSXHp-RevenueCat_Events.json`) — mirrors RevenueCat subscription events into `subscribers` (billing schema, Task 6). Auth is n8n's **native** `httpHeaderAuth` credential (`RevenueCat Webhook`, header `Authorization`) bound on the Webhook node — no in-workflow secret compare, so the exported JSON carries no secret, only a `{id,name}` credential reference. Chain: Webhook (`responseMode: responseNode`) → `Map Event` (Code, verbatim event→row mapping; `alwaysOutputData: true` so `CANCELLATION`'s deliberate `return []` still emits one empty item instead of killing the run downstream) → `If Has Payload?` (non-empty `app_user_id`) → **TRUE**: `Upsert Subscriber` (Postgres upsert; `onError: continueErrorOutput` routes a failed write to `Respond Error` / HTTP 500) → `Respond 200`; **FALSE**: `Respond No-Op` (200). Net effect: a real event only acks 200 **after** the Postgres write commits — a genuine DB failure surfaces as a non-2xx so RevenueCat retries — while `CANCELLATION` (which intentionally writes nothing) still gets a clean 200 off the FALSE branch. Gotcha worth keeping for any future Postgres node here: `queryReplacement` must be **one** `={{ [...] }}` array expression, never comma-joined `{{ }}` fragments — the comma-joined form stringifies a JS `null` to the literal text `"null"`, which Postgres then rejects outright for a `timestamptz` column. Probed by `Tools/n8n/probe-billing.py` (`RC_WEBHOOK_SECRET` env; 401/403 no-auth + 200×4 real events, extendable in Task 8-9); permanent DB read-back is deliberately deferred to Task 11 (`GetUsage`), not a bespoke debug webhook here. **Since Task 16 the chain forks one step earlier: `Map Event` → `If Is Transfer?` → TRUE: `Transfer Subscriber` → `Respond 200` / FALSE: the `If Has Payload?` chain above.** `TRANSFER` is the event RevenueCat fires when a store auto-moves a subscription to a different `app_user_id` (a reinstall mints a fresh anonymous id — observed live 2026-08-26); its payload carries **no** `app_user_id`/`product_id`/`entitlement_ids`, only the `transferred_from`/`transferred_to` String arrays, which is exactly why it cannot ride the existing `If Has Payload?` gate. `Transfer Subscriber` is ONE parameterized statement (= one transaction, so the move and the retirement can never half-apply): it copies the old row's plan/status/period_end/product_id onto the new id and **adds** the old top-up balance to whatever the new id already had, then, in the same statement, sets every source row to `status='expired'`, `topup_balance=0` and clamps its `current_period_end` down to `now()`. Two things that look optional and are not: (1) the snapshot moves only when the source is **strictly newer** than what the destination already holds — a `RENEWAL` under the new id can legitimately land BEFORE the `TRANSFER`, and RevenueCat retries any non-2xx, so both a race and a replay must be unable to downgrade a healthy row (the tie going to the destination is also what makes a duplicate delivery a no-op, top-up included); (2) the `current_period_end` clamp is the entire hand-off to `Profile Lifecycle Sweep`'s Branch A, whose `Candidates` query selects `status in ('expired','grace') AND current_period_end < now() - interval '3 days'` — leaving a still-in-the-future paid period there would park the abandoned identity's Wappi profiles for up to a month at 23₽/day, and NO new deletion machinery exists: retirement is entirely "make the row look like ordinary churn". `status='expired'` also kills the abandoned identity's auto-replies through the existing quota gate (`Count Dialog` refuses to open a NEW dialog for `status in ('expired','grace')`; a chat already counted TODAY keeps answering until the Asia/Almaty date rolls over). Same run also fixed `PRODUCT_CHANGE`, which carries the OLD sku in `product_id` and the new one in `new_product_id` — the mapper now prefers the latter. Probed by `probe-billing.py --transfer` (8 exact-value steps incl. the sum, the clamp, a replay and an empty-array no-op) |
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

## Known follow-ups before this is production-/dev-ready

1. **Credentials are not in these files** (referenced by id only). The local server has none yet —
   recreate WappiAuthToken, n8nAPIKey, OpenAi, Supabase, Cohere, Postgres before the workflows run.
2. **Create/Edit handlers hardcode `https://bagkz.app.n8n.cloud/api/v1/...`** for their clone/activate
   calls. For true local dev, point these at `http://localhost:5678/api/v1/...` + a local API key.
3. **`CreateWhatsappWorkflow`** has a trailing space in the `/activate ` URL — fix during the prod pass.
4. **Edit handlers** assume target node indices (`nodes[5]` is the AI agent) and have a `Set Bussiness Type`
   node-name typo + leftover unused credential refs — clean up during the prod pass.

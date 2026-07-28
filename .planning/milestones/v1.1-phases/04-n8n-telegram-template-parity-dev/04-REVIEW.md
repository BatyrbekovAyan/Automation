---
phase: 04-n8n-telegram-template-parity-dev
reviewed: 2026-07-12T14:02:41Z
depth: standard
files_reviewed: 6
files_reviewed_list:
  - Tools/n8n/workflows/4VN3gsFaC2HUYmcc-Telegram_Bot.json
  - Tools/n8n/workflows/Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json
  - Tools/n8n/workflows/XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json
  - Tools/n8n/workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json
  - Tools/n8n/verify-telegram-parity.py
  - Assets/Scripts/Main/Manager.cs
findings:
  critical: 2
  warning: 2
  info: 3
  total: 7
status: fixed
fixed_at: 2026-07-12T14:11:54Z
fixes:
  fixed: [CR-01, CR-02, WR-01, WR-02]
  wontfix: [IN-01, IN-02, IN-03]
  commits: [f3ffa8d, 2781ce4, f4fee44, 584bd58]
---

# Phase 4: Code Review Report

**Reviewed:** 2026-07-12T14:02:41Z
**Depth:** standard
**Files Reviewed:** 6
**Status:** issues_found

## Summary

Reviewed the four canonical n8n workflow JSONs, the parity verifier script, and the phase-4 Manager.cs diff (four sentinel-guarded AddField sites only, per scope note).

**What checks out cleanly:**

- **Telegram_Bot.json** — structurally diffed against `4wYitz5ek30SVNlT-WhatsApp_Bot.json`: 24 nodes in identical name/type order (nodes[0] Webhook, nodes[5] AI Agent), identical connections graph, identical credentials on every node. The only parameter deltas are exactly the five intended TPL fixes: three outbound URLs on tapi (`tapi/sync/message/send`, `tapi/sync/message/mark/read`, `tapi/sync/chats/typing/start` — no `mark_all` anywhere, no `/api/sync/` residue), both Input type Switch nodes gained the `text` rightValue with `combinator: "or"` (caseSensitive true / typeValidation strict preserved, WA `chat` match retained), Listening Pause fallback expression, Chat Memory sessionKey on `chatId`, and the vector-store filter key `botTgId`.
- **Suggest_Replies.json** — the Telegram branch is genuinely additive. Traced Prep: absent `channel` normalizes to `'whatsapp'`, `wfId = botWaId`, `skipRag` logic and every downstream field identical to pre-phase behavior. `If channel TG?` routes true→`Retrieve RAG TG`, false→`Retrieve RAG`; both retrieve nodes carry single-key filters (`botTgId` / `botWaId`), `alwaysOutputData`, the same Supabase credential, and both feed Assemble. The shared `Embeddings` node has `ai_embedding` connections to BOTH vector-store nodes (lines 492–506) — the new node is not missing its embeddings input. Respond node unchanged.
- **Manager.cs diff (71e193d)** — all four sites correct: the two WhatsApp-create forms send `TelegramWorkflowId`, the two Telegram-create forms send `WhatsappWorkflowId`, matching exactly what each orchestrator's Restamp node reads (`body.TelegramWorkflowId` in CreateWhatsappWorkflow, `body.WhatsappWorkflowId` in CreateTelegramWorkflow). Wizard sites use the wizard-local `bot` param, edit sites use `openBot` — each consistent with the adjacent pre-existing lines (2731, 2826, 2874, 2972), so no new null-ref surface. `string.IsNullOrEmpty(...) ? Bot.UnauthedProfileSentinel : id` correctly coerces both `null` (uninitialized field) and `""` to `"-1"` and passes an existing `"-1"` through unchanged (`Bot.UnauthedProfileSentinel == "-1"`, Bot.cs:67).
- **verify-telegram-parity.py** ran green against the shipped files, and its asserts match what shipped.

**Key concerns:** the Restamp nodes — the centerpiece of this phase — have a parameter-binding bug that makes the re-stamp a permanent silent no-op (CR-01), and their SQL lacks the project's own `-1`-sentinel isolation guard (CR-02, currently masked by CR-01). The verifier passes despite CR-01, which is a concrete false-green path (WR-02).

## Critical Issues

### CR-01: Stray `=` in Restamp `queryReplacement` corrupts `$2` — re-stamp is a permanent silent no-op

**Status:** FIXED — commit `f3ffa8d`. Removed the stray `=` after the comma in both orchestrators; now matches the proven Delete_Bot_Files shape `"={{ a }},{{ b }}"` (values are alphanumeric n8n workflow ids, so the queryReplacement comma-split is safe). UAT step remains: confirm a `botTgId='-1'` chunk flips after a Telegram create.

**File:** `Tools/n8n/workflows/Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json:276` and `Tools/n8n/workflows/XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json:174`

**Issue:** Both Restamp nodes use:

```
"queryReplacement": "={{ $('Get Created Workflow Id').item.json.id }},={{ $('Unity Webhook').first().json.body.WhatsappWorkflowId }}"
```

In n8n, only the *leading* `=` of the parameter value marks expression mode; the `=` after the comma is literal text. Whether n8n resolves the whole string then comma-splits (v<2.5 path) or splits raw segments then evaluates each (v2.5+ path, which strips `=` only via `replace(/^=+/, '')` at the string start), `$2` resolves to `=<workflowId>` — with a literal `=` prefix. `WHERE metadata->>'botWaId' = '=Abc123'` can never match (n8n workflow ids never start with `=`), so the UPDATE affects 0 rows on **every** run. The failure is invisible: `onError: continueRegularOutput` + `alwaysOutputData` swallow it and `Send New Workflows Id` still returns the id, so bot creation "succeeds" while TPL-05/D3 (making pre-existing `botTgId='-1'` chunks visible to the new Telegram workflow, and vice versa) silently never happens. The repo's own proven pattern confirms the correct shape — `Delete_Bot_Files.json:27` (e2e-tested) uses `"={{ $json.body.botWaId }},{{ $json.body.botTgId }}"` with no mid-string `=`.

**Fix:** Remove the stray `=` after the comma in both files:

```json
"queryReplacement": "={{ $('Get Created Workflow Id').item.json.id }},{{ $('Unity Webhook').first().json.body.WhatsappWorkflowId }}"
```

(and the `TelegramWorkflowId` twin in CreateWhatsappWorkflow). Then verify during the 04-HUMAN-UAT e2e that a `botTgId='-1'` chunk actually flips after a Telegram create.

### CR-02: Restamp SQL has no `-1`-sentinel guard — cross-bot chunk claim when the opposite channel is unauthed

**Status:** FIXED — commit `2781ce4`. Both Restamp queries now open with `WHERE $2 <> '-1' AND $2 <> ''` (empty-string guard added for safety, mirroring Delete Bot Files' `NOT IN ('-1','')`), so a single-channel create matches zero rows instead of claiming shared unauthed chunks.

**File:** `Tools/n8n/workflows/Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json:274` and `Tools/n8n/workflows/XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json:172`

**Issue:** CreateTelegramWorkflow runs:

```sql
UPDATE documents
SET metadata = jsonb_set(metadata, '{botTgId}', to_jsonb($1::text))
WHERE metadata->>'botWaId' = $2 AND metadata->>'botTgId' = '-1';
```

Every **single-channel** bot creation sends the opposite id as `"-1"` (wizard Telegram-only bots: `whatsappWorkflowId` is `"-1"` per Manager.cs:1396/375, coerced by the new sentinel lines; likewise settings-path creates when the other channel is logged out — Manager.cs:3108/3126 reset to `"-1"`). With `$2 = '-1'`, the WHERE becomes `botWaId='-1' AND botTgId='-1'` — matching **shared fully-unauthed chunks from any bot of any owner** in the shared `documents` table, and stamping them with this bot's new workflow id. Such chunks are reachable through normal UI: price-list upload is not auth-gated (`BotSettings.Auth.cs:584-585` sends the raw ids; `UploadPriceList` has no auth check), so an owner who logged out of both channels and then uploads produces `('-1','-1')` chunks. Result: another business's price-list content becomes retrievable via this bot's RAG — a cross-tenant isolation break. The project already treats this as a hard invariant elsewhere: `Delete Bot Files` explicitly guards the `"-1"` sentinel "so shared-unauthed chunks are never touched" (CLAUDE.md). Note CR-01 currently masks this bug (`$2` is `'=-1'`, matches nothing); fixing CR-01 alone makes CR-02 live.

**Fix:** Add the sentinel guard to both queries (mirrors the Delete Bot Files invariant):

```sql
UPDATE documents
SET metadata = jsonb_set(metadata, '{botTgId}', to_jsonb($1::text))
WHERE $2 <> '-1'
  AND metadata->>'botWaId' = $2
  AND metadata->>'botTgId' = '-1';
```

(and the mirrored `botTgId = $2 ... $2 <> '-1'` guard in CreateWhatsappWorkflow). Guarding in SQL keeps the node graph unchanged; also add a matching assert to verify-telegram-parity.py (see WR-02).

## Warnings

### WR-01: `$('Get Created Workflow Id').item` in `Send New Workflows Id` now sits downstream of the Postgres node — paired-item lineage at risk

**Status:** FIXED — commit `f4fee44`. `Send New Workflows Id` now uses `$('Get Created Workflow Id').first().json.id` in both orchestrators. Restamp's own `.item` reference is unchanged (fed by HTTP nodes, pairing intact, per this review). UAT step remains: exercise the Restamp error path once and confirm the webhook still returns `{ id }`.

**File:** `Tools/n8n/workflows/Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json:255` and `Tools/n8n/workflows/XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json:153`

**Issue:** `Send New Workflows Id` (the terminal node whose `{ id }` is the webhook's `lastNode` response) resolves `$('Get Created Workflow Id').item` — `.item` requires an unbroken paired-item chain from the current input item back to that node. Pre-phase, the chain ran only through HTTP Request nodes, which preserve pairing. Now the Restamp Postgres node sits in between, and its `alwaysOutputData` empty item and `onError: continueRegularOutput` error item (plus the plain `{success:true}` output of a non-RETURNING UPDATE, depending on n8n version) are exactly the outputs known to drop paired-item metadata. If `.item` fails to resolve, the expression errors, the webhook response loses `id`, and Unity's `response.Contains("\"id\":")` check fails — in the FromStart flows this **deletes the just-authorized Wappi profile** (Manager.cs:2748/2894). Restamp's own `$('Get Created Workflow Id').item` (line 276/174) is fed by HTTP nodes and is fine.

**Fix:** Switch to `.first()` — semantics are identical in this strictly single-item workflow and it does not depend on paired-item lineage (the same node already uses `$('Unity Webhook').first()` in Restamp, and Dashboard_Outcomes uses `$('Prep').first()` throughout):

```json
"value": "={{ $('Get Created Workflow Id').first().json.id }}"
```

At minimum, exercise the Restamp *error* path (e.g. wrong credential) once during 04-HUMAN-UAT and confirm the webhook still returns `{ id }`.

### WR-02: verify-telegram-parity.py has false-green paths — it passes today despite CR-01

**Status:** FIXED — commit `584bd58`. Added: exact-match assert on both Restamp `queryReplacement` strings (catches the stray `=` and swapped/wrong opposite-channel fields — verified it fails with the CR-01 bug reintroduced), `$2 <> '-1'` / `$2 <> ''` sentinel asserts on the SQL, and Suggest_Replies wiring asserts (Embeddings `ai_embedding` feeds both Retrieve nodes; both Retrieve nodes feed Assemble). The minor Switch `leftValue` gap noted in item 3 was left as-is (out of the agreed fix scope). Verifier exits 0 on shipped files.

**File:** `Tools/n8n/verify-telegram-parity.py:118-126` (Restamp checks), `Tools/n8n/verify-telegram-parity.py:143-179` (Suggest checks)

**Issue:** The script is the phase's pre-deploy gate, but several shipped invariants are unasserted, so real regressions pass green:

1. **Restamp parameter binding unchecked** (proven gap): `check_restamp_orchestrator` validates the SQL string (`$1`/`$2` present, no `{{` interpolation) but never inspects `options.queryReplacement` — the CR-01 stray-`=` bug passes the verifier right now. It also can't catch swapped/wrong field names (e.g. reading `TelegramWorkflowId` in the wrong orchestrator).
2. **Embeddings wiring for the new vector store unasserted:** a vector-store node without an `ai_embedding` input hard-fails at runtime; if the `Embeddings → Retrieve RAG TG` connection is lost (easy to drop when re-saving from the n8n UI, which the deploy flow round-trips through), the verifier stays green while every Telegram RAG suggestion request fails.
3. **`Retrieve RAG TG → Assemble` main connection unasserted:** if dropped, the TG RAG path dead-ends (webhook timeout) and the verifier stays green. (Minor, same class: the Switch checks at lines 74-83 validate `rightValue`s and combinator but not `leftValue`, so a wrong source field would also pass.)

**Fix:** Add asserts, e.g.:

```python
# Restamp binding: two comma-separated {{ }} segments, no stray '=' after the comma,
# referencing the created id and the opposite-channel form field; plus the -1 guard.
qr = r["parameters"]["options"]["queryReplacement"]
assert ",={{" not in qr, f"{f}: queryReplacement has a stray '=' after the comma (breaks $2): {qr}"
assert "Get Created Workflow Id" in qr and opposite_field in qr, f"{f}: queryReplacement bindings wrong: {qr}"
assert "$2 <> '-1'" in q, f"{f}: Restamp SQL missing the -1 sentinel guard"

# Suggest_Replies wiring:
emb_targets = {c["node"] for c in conns["Embeddings"]["ai_embedding"][0]}
assert {"Retrieve RAG", "Retrieve RAG TG"} <= emb_targets, f"{f}: Retrieve RAG TG missing its embeddings connection"
assert conns["Retrieve RAG TG"]["main"][0][0]["node"] == "Assemble", f"{f}: Retrieve RAG TG does not feed Assemble"
```

## Info

### IN-01: Telegram voice branch matches type `"ptt"` — confirm tapi's actual type string at UAT

**Status:** WONTFIX (deferred to 04-HUMAN-UAT) — no code change is possible offline: the correct `type` string can only be observed in a live tapi webhook payload from a real Telegram voice note. Guessing a second rightValue now would be unverifiable. Verify during the device pass and adjust then if needed.

**File:** `Tools/n8n/workflows/4VN3gsFaC2HUYmcc-Telegram_Bot.json:203` (Input type) and `:472` (Input type2)

**Issue:** The audio rule was carried over from WhatsApp verbatim (`type == "ptt"`). If Wappi tapi reports Telegram voice notes under a different type (e.g. `"voice"`), voice messages fall to the Switch fallback and get the "Please send text messages" reply — graceful, but the transcription path would be dead.

**Fix:** Send a real Telegram voice note during the 04-HUMAN-UAT device pass and check the webhook payload's `messages[0].type`; adjust the rightValue (or add an `or` condition) if it isn't `ptt`.

### IN-02: Re-stamp only covers `-1` chunks — re-auth of an already-stamped channel still orphans RAG chunks

**Status:** WONTFIX (out of phase scope, per the finding itself: "No action required for this phase") — pre-existing limitation shared with WhatsApp re-auth, not introduced here. Follow-up candidate: server-side re-stamp/reindex using the `price-lists` originals, which needs the old workflow id passed alongside — a design change, not a low-risk fix.

**File:** `Tools/n8n/workflows/Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json:274`

**Issue:** Chunks stamped with a real workflow id (e.g. `botTgId = T1`) are never re-stamped when that channel is logged out and re-authed (new workflow `T2`): the WHERE only matches `botTgId = '-1'`, so those chunks stay pointed at the deleted `T1` and the channel's RAG goes empty until re-upload. This is the same pre-existing limitation WhatsApp re-auth has (not introduced by this phase) — recorded here because the phase now owns the restamp mechanism and the Supabase `price-lists` originals would allow a server-side re-stamp/reindex later.

**Fix:** No action required for this phase; consider a follow-up that re-stamps `botTgId = <old id>` chunks on re-auth (would need the old id passed alongside), or document re-upload as the recovery path.

### IN-03: First in-repo use of `?.`/`??` inside an n8n expression parameter

**Status:** WONTFIX (deferred to 04-HUMAN-UAT) — the expression is syntactically valid and the finding expects no code change; the only open question is the dev n8n instance's expression-engine version, which can only be confirmed by opening the node on that instance. Check the Listening Pause expression preview once during UAT.

**File:** `Tools/n8n/workflows/4VN3gsFaC2HUYmcc-Telegram_Bot.json:591` (Listening Pause)

**Issue:** `={{ (…media_info?.duration ?? …length_seconds ?? 0) + 2 }}` is syntactically valid in n8n v1 (the Tournament expression engine supports ES2020 optional chaining and nullish coalescing), and it correctly yields `0 + 2` when both fields are absent. However, every prior `?.`/`??` in this repo lives inside Code-node `jsCode`, never inside a `{{ }}` parameter expression — there is no in-instance precedent proving the dev n8n's version handles it.

**Fix:** During 04-HUMAN-UAT, open the Listening Pause node once on the dev instance and confirm the expression preview resolves (or run one voice message through); no code change expected.

---

_Reviewed: 2026-07-12T14:02:41Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_

# «Сводка» Dashboard + Add-Bot Restructure — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the «New» bottom tab with a «Сводка» dashboard that shows per-conversation *outcomes* (classified server-side by n8n from bot transcripts), and move the Add-Bot form behind the Bots page.

**Architecture:** Three independently-testable parts. **Part A (Server)** adds a Supabase `conversation_outcomes` table + a new `DashboardOutcomes` n8n webhook that classifies changed conversations from `n8n_chat_histories` on demand — no change to the hot bot-reply path. **Part B (Navigation)** turns tab-2 into a `Screen_Dashboard` placeholder, moves `Screen_New` (Add-Bot) to a slide-in overlay opened from the Bots page, and adds Bots empty-state + zero-bot auto-open. **Part C (Dashboard screen)** fills `Screen_Dashboard` with the Variant-B UI, a fetch controller, drill-down lists, and chat deep-links. A depends on nothing; B depends on nothing; C depends on both.

**Tech Stack:** Unity 6 (C#, coroutines, DOTween, TMP, Nobi.UiRoundedCorners), `[MenuItem]` Editor builders; n8n (webhook + Postgres + OpenAI HTTP + Respond nodes); Supabase Postgres; NUnit EditMode tests.

## Global Constraints

- **Unity sizes are 1080×1920 canvas reference units (1 dp ≈ 3 units), NOT CSS px.** Body text 42; see the type/spacing scale in Task C-tokens.
- **Icons are `Image` + `sprite` only — never TMP glyphs** (they render blank in this project). Rounded corners via `ImageWithRoundedCorners`/`ImageWithIndependentRoundedCorners` (`Nobi.UiRoundedCorners`, direct type ref), null sprite ok, never `UISprite.psd`.
- **Fonts by GUID** (default font weight table is empty — always assign): Regular `e0cdfe2d6a51446bcba7d2df147e2415`, Medium `d091b0cad5d964a53a41de97ba932a27`, Semibold `a2b0b38b6764047da9250bcff1b0f432`, Bold `1cd715823fef34be4a3d3f3c5572594c`.
- **All UI copy is Russian.** Statuses: `order_collected`→«Заявка», `owner_needed`→«Нужен владелец», `in_dialog`→«В диалоге», `client_silent`→«Клиент замолчал», `question_closed`→«Вопрос закрыт».
- **v1 is WhatsApp-only.** The app sends only `whatsappProfileId`s to the webhook; Telegram is out of scope (no Telegram chat pipeline exists).
- **n8n POST calls MUST set `Content-Type: application/json` explicitly** (Unity libcurl otherwise stamps `x-www-form-urlencoded` → n8n 415). Copy `Manager.DeleteBotFilesRoutine` verbatim as the template.
- **EditMode tests:** `Assets/Tests/Editor/Chat/`, no asmdef, `using NUnit.Framework;`, no namespace, plain `public class XxxTests`, `[Test]`/`[TestCase]`. Test pure static classes only (no MonoBehaviour/scene/PlayerPrefs). Run via `Tools/run-tests-headless.sh` (Editor closed) or the `Temp/claude/run-tests.trigger` bridge (Editor open). After creating a new `.cs`, run Assets→Refresh and confirm the `.meta` appears before trusting a green run.
- **Editor builders** are idempotent delete-and-rebuild, Edit-Mode only, no Undo grouping; the interactive `[MenuItem]` entry marks the scene dirty (user saves), a `BuildHeadless` entry opens Main.unity, validates wiring, and `SaveScene`. Rounded-corner `.Refresh()` runs LAST, after `Canvas.ForceUpdateCanvases()`.
- **Bot id == `Bot` GameObject `transform.name`** (`"Bot0"`, `"Bot1"`…); WhatsApp profile read via `Manager.Instance.FindBotByName(name).whatsappProfileId`, invalid when null/empty/`Bot.UnauthedProfileSentinel` (`"-1"`).
- **n8n bases:** dev `http://localhost:5678`, prod `https://bagkz.app.n8n.cloud`. App resolves via `Manager.n8nBaseUrl` (static). Dev Postgres credential id `vvRrFiEXzLVqKjOx` (utility) / `1H5xlpFSESU4w6JH` (bot memory); OpenAI credential `openAiApi` id `XVjhR1xlWrIgJjKz`. **These ids are dev-instance-specific and get re-minted on prod import.**
- **Prod bagkz is dormant** — all server work is dev-first on local n8n; prod replication rides the standing bulk-copy, not this plan.

---

# PART A — Server (Supabase + n8n)

**Deliverable:** `POST /webhook/DashboardOutcomes {profileIds:[...]}` returns `{success, classified, truncated, outcomes:[{profileId,chatId,outcome,summary,outcomeAt,lastMessageAt}]}`, backed by a new `conversation_outcomes` table, verified by an e2e script against dev n8n. No app dependency.

**Contract (referenced by Part C):**
- Request body: `{ "profileIds": ["<whatsappProfileId>", ...] }`
- Response body: `{ "success": true, "classified": <int>, "truncated": <bool>, "outcomes": [ { "profileId": string, "chatId": string, "outcome": "order_collected"|"owner_needed"|"in_dialog"|"client_silent"|"question_closed", "summary": string, "outcomeAt": <unix ms>, "lastMessageAt": <unix ms> } ] }`
- Failure: `{ "success": false, "error": string }`

### Task A1: Supabase migration — timestamps + `conversation_outcomes`

**Files:**
- Create: `Tools/n8n/supabase/2026-07-07-conversation-outcomes.sql`

- [ ] **Step 1: Write the migration file**

```sql
-- Conversation outcomes for the «Сводка» dashboard (2026-07-07).
-- Run once in the Supabase SQL editor (or a service-role/postgres connection).
-- Idempotent: safe to re-run. Mirrors the default-deny RLS of
-- 2026-07-02-harden-rag-store.sql — no policies on purpose, only
-- service_role/owner get through (the n8n Postgres credential is the table
-- owner and is exempt from non-FORCE RLS).

-- 1. n8n_chat_histories has no timestamps (LangChain memory writes id/session_id/
--    message only). Add created_at so the dashboard can bucket by time and apply
--    the 12h silence rule. The memory node inserts without naming columns, so the
--    default applies to every new row. Pre-existing rows share the migration
--    timestamp (acceptable — only skews last_message_at for old sessions, ages out).
alter table public.n8n_chat_histories
  add column if not exists created_at timestamptz not null default now();

-- 2. Outcome per conversation. session_id == '<profile_id>:<chat_id>' (the bots'
--    memory sessionKey). last_history_id is the watermark into n8n_chat_histories.
create table if not exists public.conversation_outcomes (
  session_id      text primary key,
  profile_id      text not null,
  chat_id         text not null,
  outcome         text not null check (outcome in
    ('order_collected','owner_needed','in_dialog','client_silent','question_closed')),
  summary         text not null default '',
  last_history_id bigint not null,
  last_message_at timestamptz not null,
  outcome_at      timestamptz not null,
  updated_at      timestamptz not null default now()
);

create index if not exists conversation_outcomes_profile_idx
  on public.conversation_outcomes (profile_id);

-- 3. Default-deny RLS: no policies, strip client-key roles (the anon key ships in
--    the mobile app). service_role (n8n Supabase cred) has bypassrls; the Postgres
--    cred is the owner — both unaffected.
alter table public.conversation_outcomes enable row level security;
revoke all on table public.conversation_outcomes from anon, authenticated;

-- Post-checks (expect true):
--   select relrowsecurity from pg_class where oid = 'public.conversation_outcomes'::regclass;
--   select not has_table_privilege('anon', 'public.conversation_outcomes', 'select');
--   select column_name from information_schema.columns
--     where table_name = 'n8n_chat_histories' and column_name = 'created_at';
```

- [ ] **Step 2: Apply to dev Supabase**

Run the file in the dev Supabase SQL editor (the project behind the dev n8n Postgres credential). Run the three post-check queries; confirm each returns true / a row.

- [ ] **Step 3: Commit**

```bash
git add Tools/n8n/supabase/2026-07-07-conversation-outcomes.sql
git commit -m "feat(dashboard): supabase migration — conversation_outcomes + chat-history timestamps"
```

### Task A2: Classifier prompt

**Files:**
- Create: `Tools/n8n/prompts/dashboard-classifier.md`

**Interfaces:**
- Produces: the system prompt embedded in the OpenAI node (Task A3). Output is JSON `{outcome, summary}` with `outcome` one of the 5 enum ids.

- [ ] **Step 1: Write the classifier prompt** (Russian; the vertical prompts already force the outcome moment to be explicit — an order recap message, «Передаю владельцу/флористу» — which makes classification easy)

```markdown
Ты классифицируешь диалог между ботом магазина и клиентом. На вход — переписка
(«Клиент:» и «Бот:» построчно). Верни СТРОГО JSON: {"outcome": "<id>", "summary": "<строка>"}.

outcome — ровно один из:
- order_collected — бот собрал заявку/заказ: есть товар/услуга И контакт (имя или
  телефон), либо бот сказал «передаю владельцу/флористу/мастеру — подтвердит заказ».
  Это успех воронки.
- owner_needed — нужен человек: клиент просит возврат/жалуется, спрашивает то, чего
  бот не знает, просит оплату/скидку/нестандартное, или бот прямо передал вопрос
  владельцу БЕЗ оформленной заявки.
- in_dialog — активный диалог в процессе: бот отвечает, заявка ещё не собрана, клиент
  ещё пишет. Значение по умолчанию, если ничего конкретнее не подходит.
- question_closed — клиент получил ответ на вопрос (часы, адрес, цена, наличие) и не
  проявил намерения купить/заказать; разговор естественно закрыт.
- client_silent — НЕ выбирай сам: этот статус ставит отдельное правило по времени, не ты.

summary — одна короткая строка (до 120 символов, без переносов), по-русски,
по сути диалога: что клиент хочет / что собрано. Пример: «101 роза, доставка завтра
к 10:00, тел. собран». Без Markdown, без кавычек внутри.

Отвечай только JSON, без пояснений.
```

- [ ] **Step 2: Commit**

```bash
git add Tools/n8n/prompts/dashboard-classifier.md
git commit -m "feat(dashboard): conversation-outcome classifier prompt"
```

### Task A3: `DashboardOutcomes` n8n workflow

> **AMENDED (2026-07-08):** The task text below is a design sketch, not the shipped
> implementation — kept for historical record, not to be re-applied. Four deltas:
> (a) the CSV / `string_to_array($1,',')` id-passing design shown in Steps 3, 10, 11 below is
> **SUPERSEDED** — n8n's `queryReplacement` comma-splits a multi-value replacement, so a
> multi-id CSV never reached SQL as more than its first id. The shipped design base64-encodes
> the id array in `Prep` (`b64Ids`, no commas → survives as one parameter) and the three
> id-consuming queries (`Find Changed Sessions`, `Apply Silence Rule`, `Fetch Outcomes`) decode
> via `jsonb_array_elements_text(convert_from(decode($1,'base64'),'UTF8')::jsonb)` instead of
> `unnest(string_to_array($1,','))` / `= ANY(string_to_array($1,','))`. See the canonical export
> `Tools/n8n/workflows/2htWSV5IHO8E2CgB-Dashboard_Outcomes.json` for the actual queries.
> (b) `Parse`/`Aggregate` ship with failure-marking semantics, not the sketch's silent
> `in_dialog` fallback: `Parse` sets `failed:true` when OpenAI errors or returns unparsable
> content, and `Aggregate` drops failed rows from the upsert entirely — the watermark stays
> untouched and any previously stored outcome/summary survives, so the row is retried
> naturally on the next call. Per spec §4.3 (`docs/superpowers/specs/2026-07-07-dashboard-svodka-design.md`).
> (c) Timestamps and counts are **numeric** on the wire — `Find Changed Sessions` and
> `Fetch Outcomes` both set `options.largeNumbersOutput: "numbers"`, so `outcomeAt`/
> `lastMessageAt`/`total` are unquoted JSON numbers, not strings.
> (d) The contract's `{"success": false, "error": ...}` failure line (declared at the top of
> Part A) is dead code in practice: server-side errors surface as a bare HTTP 500 with no such
> body, and the app's fetch path treats any non-200 or unparseable response as quiet-fail
> (keep the last cached outcomes), never branching on `success: false`.

**Files:**
- Create: `Tools/n8n/workflows/<devId>-Dashboard_Outcomes.json` (canonical export; `<devId>` = the id n8n mints, matching the filename-prefix convention of the other workflows)

**Build approach:** n8n workflows are authored in the n8n editor and exported. Build the graph below in the dev n8n (`localhost:5678`), verify with Task A4, then export to the canonical path. The verbatim SQL and request/response contract are the stable parts and are given in full.

**Topology (single-item main chain, IF-branch for the empty case so the tail always runs):**

```
Webhook ─► Prep ─► Find Changed Sessions ─► Has Sessions? ─┬─(true)─► Split Out ─► Classify(OpenAI)
                                                            │                          │
                                                            │                        Parse
                                                            │                          │
                                                            │                       Aggregate ─► Upsert Outcomes ─┐
                                                            │                                                     ├─► Apply Silence Rule ─► Fetch Outcomes ─► Respond
                                                            └─────────────────(false)────────────────────────────┘
```

`Apply Silence Rule` has TWO inbound connections (from `Upsert Outcomes` and from the IF false output); exactly one carries an item, so it runs once. Every "must-always-run" node (`Apply Silence Rule`, `Fetch Outcomes`, `Respond`) reads its profile list from `$('Prep')`, never from the classify branch.

- [ ] **Step 1: Webhook node** — `n8n-nodes-base.webhook` v2.1: `httpMethod: POST`, `path: DashboardOutcomes`, `responseMode: responseNode`.

- [ ] **Step 2: Prep node** — `n8n-nodes-base.code` v2, runs once:

```javascript
const ids = (($json.body || {}).profileIds || [])
  .filter(x => typeof x === 'string' && x && x !== '-1');
// Comma-joined for Postgres string_to_array (profile ids are digits — no commas).
return [{ json: { profileIdsCsv: ids.join(','), profileCount: ids.length } }];
```

- [ ] **Step 3: Find Changed Sessions node** — `n8n-nodes-base.postgres` v2.6, `resource: database`, `operation: executeQuery`, credential `Postgres` (dev id `vvRrFiEXzLVqKjOx`), `options.queryReplacement: ={{ $json.profileIdsCsv }}`. Returns exactly ONE row `{ sessions: json, total: int }` (json_agg keeps the chain single-item so downstream always runs):

```sql
WITH ids AS (
  SELECT unnest(string_to_array($1, ',')) AS profile_id
),
sessions AS (
  SELECT h.session_id,
         split_part(h.session_id, ':', 1) AS profile_id,
         substr(h.session_id, position(':' in h.session_id) + 1) AS chat_id,
         max(h.id) AS max_id,
         max(h.created_at) AS last_message_at
  FROM public.n8n_chat_histories h
  JOIN ids ON ids.profile_id = split_part(h.session_id, ':', 1)
  GROUP BY h.session_id
),
changed AS (
  SELECT s.session_id, s.profile_id, s.chat_id, s.max_id, s.last_message_at,
         o.last_history_id AS prev_watermark,
         t.transcript
  FROM sessions s
  LEFT JOIN public.conversation_outcomes o ON o.session_id = s.session_id
  CROSS JOIN LATERAL (
    SELECT string_agg(
             (CASE WHEN m.message->>'type' = 'ai' THEN 'Бот: ' ELSE 'Клиент: ' END)
             || coalesce(m.message->'data'->>'content', ''),
             E'\n' ORDER BY m.id) AS transcript
    FROM (
      SELECT id, message FROM public.n8n_chat_histories
      WHERE session_id = s.session_id
      ORDER BY id DESC LIMIT 30
    ) m
  ) t
  WHERE s.chat_id NOT LIKE '%@g.us'
    AND s.max_id > COALESCE(o.last_history_id, 0)
  ORDER BY COALESCE(o.last_history_id, 0) ASC   -- oldest-watermark / never-seen first
  LIMIT 21                                       -- 20 cap + 1 to detect truncation
)
SELECT
  COALESCE(json_agg(json_build_object(
    'session_id', c.session_id, 'profile_id', c.profile_id, 'chat_id', c.chat_id,
    'max_id', c.max_id,
    'last_message_at_ms', (extract(epoch from c.last_message_at)*1000)::bigint,
    'transcript', c.transcript
  )), '[]'::json) AS sessions,
  count(*) AS total
FROM changed c;
```

- [ ] **Step 4: Has Sessions? node** — `n8n-nodes-base.if` v2: condition number `{{ $json.total }}` > `0`.

- [ ] **Step 5: Split Out node** (IF true) — `n8n-nodes-base.splitOut` v1: `fieldToSplitOut: sessions`. Emits one item per changed session (capped: add `LIMIT 21`, then `Aggregate` slices to 20 and sets `truncated`; the 21st is dropped this pass and picked up next open).

- [ ] **Step 6: Classify node** — `n8n-nodes-base.httpRequest` v4.2, POST `https://api.openai.com/v1/chat/completions`, `authentication: predefinedCredentialType`, `nodeCredentialType: openAiApi` (dev id `XVjhR1xlWrIgJjKz`), `onError: continueRegularOutput`. JSON body (enum-constrained structured output so an invalid label is impossible):

```json
{
  "model": "gpt-4o-mini",
  "temperature": 0,
  "max_tokens": 200,
  "messages": [
    { "role": "system", "content": "<paste Tools/n8n/prompts/dashboard-classifier.md verbatim>" },
    { "role": "user", "content": "={{ $json.transcript }}" }
  ],
  "response_format": {
    "type": "json_schema",
    "json_schema": {
      "name": "conversation_outcome",
      "strict": true,
      "schema": {
        "type": "object",
        "additionalProperties": false,
        "required": ["outcome", "summary"],
        "properties": {
          "outcome": { "type": "string",
            "enum": ["order_collected","owner_needed","in_dialog","question_closed"] },
          "summary": { "type": "string" }
        }
      }
    }
  }
}
```

(Note the enum deliberately omits `client_silent` — that status is assigned only by the time rule in Step 10, never by the model.)

- [ ] **Step 7: Parse node** — `n8n-nodes-base.code` v2, "run once for each item". Reads the OpenAI response, pairs it back to the session via `$('Split Out').item`, falls back to `in_dialog` on any parse/error:

```javascript
const src = $('Split Out').item.json;
let outcome = 'in_dialog', summary = '';
try {
  const content = $json.choices?.[0]?.message?.content;
  if (content) {
    const parsed = JSON.parse(content);
    const allowed = ['order_collected','owner_needed','in_dialog','question_closed'];
    if (allowed.includes(parsed.outcome)) outcome = parsed.outcome;
    if (typeof parsed.summary === 'string') summary = parsed.summary.slice(0, 120);
  }
} catch (e) { /* keep in_dialog */ }
return [{ json: {
  session_id: src.session_id, profile_id: src.profile_id, chat_id: src.chat_id,
  outcome, summary,
  last_history_id: src.max_id, last_message_at_ms: src.last_message_at_ms
} }];
```

- [ ] **Step 8: Aggregate node** — `n8n-nodes-base.code` v2, "run once for all items". Slices to 20, base64-encodes the rows (base64 has no commas → survives Postgres `queryReplacement` single-value splitting, unlike raw JSON with commas in summaries):

```javascript
const all = $input.all().map(i => i.json);
const rows = all.slice(0, 20);
const total = $('Find Changed Sessions').first().json.total;
return [{ json: {
  b64: Buffer.from(JSON.stringify(rows)).toString('base64'),
  classified: rows.length,
  truncated: total > 20
} }];
```

- [ ] **Step 9: Upsert Outcomes node** — `n8n-nodes-base.postgres` v2.6, credential `Postgres` (`vvRrFiEXzLVqKjOx`), `options.queryReplacement: ={{ $json.b64 }}` (single value):

```sql
INSERT INTO public.conversation_outcomes AS c
  (session_id, profile_id, chat_id, outcome, summary,
   last_history_id, last_message_at, outcome_at, updated_at)
SELECT r.session_id, r.profile_id, r.chat_id, r.outcome, r.summary,
       r.last_history_id, to_timestamp(r.last_message_at_ms / 1000.0), now(), now()
FROM jsonb_to_recordset(convert_from(decode($1, 'base64'), 'UTF8')::jsonb)
  AS r(session_id text, profile_id text, chat_id text, outcome text, summary text,
       last_history_id bigint, last_message_at_ms bigint)
ON CONFLICT (session_id) DO UPDATE SET
  outcome         = EXCLUDED.outcome,
  summary         = EXCLUDED.summary,
  last_history_id = EXCLUDED.last_history_id,
  last_message_at = EXCLUDED.last_message_at,
  outcome_at      = CASE WHEN c.outcome <> EXCLUDED.outcome
                         THEN now() ELSE c.outcome_at END,
  updated_at      = now();
```

- [ ] **Step 10: Apply Silence Rule node** — `n8n-nodes-base.postgres` v2.6, credential `Postgres`, `options.queryReplacement: ={{ $('Prep').first().json.profileIdsCsv }}`. Rule-based, not LLM — flips stale bot-last `in_dialog` to `client_silent`:

```sql
UPDATE public.conversation_outcomes c
SET outcome = 'client_silent', outcome_at = now(), updated_at = now()
WHERE split_part(c.session_id, ':', 1) = ANY(string_to_array($1, ','))
  AND c.outcome = 'in_dialog'
  AND c.last_message_at < now() - interval '12 hours'
  AND (SELECT h.message->>'type' FROM public.n8n_chat_histories h
       WHERE h.session_id = c.session_id ORDER BY h.id DESC LIMIT 1) = 'ai';
```

- [ ] **Step 11: Fetch Outcomes node** — `n8n-nodes-base.postgres` v2.6, credential `Postgres`, `options.queryReplacement: ={{ $('Prep').first().json.profileIdsCsv }}`:

```sql
SELECT c.profile_id AS "profileId",
       c.chat_id    AS "chatId",
       c.outcome,
       c.summary,
       (extract(epoch from c.outcome_at)*1000)::bigint      AS "outcomeAt",
       (extract(epoch from c.last_message_at)*1000)::bigint AS "lastMessageAt"
FROM public.conversation_outcomes c
WHERE split_part(c.session_id, ':', 1) = ANY(string_to_array($1, ','))
ORDER BY c.last_message_at DESC;
```

- [ ] **Step 12: Respond node** — `n8n-nodes-base.respondToWebhook` v1.5, `respondWith: json`. `responseBody` (classified/truncated come from Aggregate when the true branch ran, else defaults):

```
={{ {
  "success": true,
  "classified": ($('Aggregate').first()?.json?.classified ?? 0),
  "truncated": ($('Aggregate').first()?.json?.truncated ?? false),
  "outcomes": $input.all().map(i => i.json)
} }}
```

- [ ] **Step 13: Connect, activate, export** — wire per the topology (remember the two edges into `Apply Silence Rule`). Activate the workflow. Export via the n8n API to the canonical path (UI download is fine — the committed files retain the top-level `id`):

```bash
# after building+activating in the n8n editor:
curl -s http://localhost:5678/api/v1/workflows \
  -H "X-N8N-API-KEY: $N8N_DEV_KEY" | \
  python3 -c "import sys,json;[print(w['id'],w['name']) for w in json.load(sys.stdin)['data']]"
# then export the Dashboard Outcomes workflow JSON to:
#   Tools/n8n/workflows/<devId>-Dashboard_Outcomes.json
```

- [ ] **Step 14: Commit**

```bash
git add Tools/n8n/workflows/*Dashboard_Outcomes.json
git commit -m "feat(dashboard): DashboardOutcomes n8n workflow — classify + serve conversation outcomes"
```

### Task A4: e2e test script (verification gate for Part A)

**Files:**
- Create: `Tools/n8n/test-dashboard-outcomes.sh`

- [ ] **Step 1: Write the e2e script** (mirrors `Tools/n8n/test-upload-e2e.sh` style: curl + http-code + body-substring asserts). It seeds `n8n_chat_histories` rows via a Postgres one-shot (using `psql` on the dev connection string in `$PGURL`), calls the webhook, and asserts.

```bash
#!/usr/bin/env bash
# e2e for the DashboardOutcomes workflow against dev n8n.
# Usage: PGURL=postgres://... ./test-dashboard-outcomes.sh [http://localhost:5678]
set -u
BASE="${1:-http://localhost:5678}"
PROFILE="e2e_$(date +%s)"
FAILS=0
TMP="$(mktemp -d)"

seed() { # $1=chatId  $2=type(human|ai)  $3=content
  psql "$PGURL" -q -c \
    "insert into public.n8n_chat_histories(session_id,message,created_at) values \
     ('$PROFILE:$1', jsonb_build_object('type','$2','data',jsonb_build_object('content','$3')), now());"
}
call() { # -> writes body to $TMP/resp.json, echoes http code
  curl -s -o "$TMP/resp.json" -w '%{http_code}' -X POST "$BASE/webhook/DashboardOutcomes" \
    -H 'Content-Type: application/json' -d "{\"profileIds\":[\"$PROFILE\"]}"
}
check() { # $1=label $2=expect-substring
  local body; body="$(cat "$TMP/resp.json")"
  if [[ "$body" == *"$2"* ]]; then echo "PASS  $1"; else
    echo "FAIL  $1 — missing '$2' in: $body"; FAILS=$((FAILS+1)); fi
}

# 1. An order-collection conversation → order_collected
seed "77010000001@c.us" human "Хочу букет 101 роза на завтра"
seed "77010000001@c.us" ai   "Записал: 101 роза, доставка завтра. Имя и телефон получателя?"
seed "77010000001@c.us" human "Айгерим, 77010000001"
seed "77010000001@c.us" ai   "Передаю флористу — он подтвердит заказ и пришлёт фото букета"
code="$(call)"; check "order_collected present" '"outcome":"order_collected"'
check "success flag" '"success":true'
check "chatId echoed" '77010000001@c.us'

# 2. Idempotency / watermark — second call reclassifies nothing new, still returns the row
code="$(call)"; check "still returns outcome" '"outcome":"order_collected"'

# 3. Group session is skipped (chat_id ends @g.us)
seed "120363000000@g.us" human "тест группы"
code="$(call)"; check "group not classified" '77010000001@c.us'   # group row absent; sanity: real one still there

echo "----"
if [[ $FAILS -gt 0 ]]; then echo "FAILED: $FAILS"; else echo "ALL PASS"; fi
# Cleanup seeded rows:
psql "$PGURL" -q -c "delete from public.n8n_chat_histories where session_id like '$PROFILE:%';"
psql "$PGURL" -q -c "delete from public.conversation_outcomes where profile_id = '$PROFILE';"
[[ $FAILS -eq 0 ]]
```

- [ ] **Step 2: Run it against dev n8n**

Run: `chmod +x Tools/n8n/test-dashboard-outcomes.sh && PGURL=<dev-conn> Tools/n8n/test-dashboard-outcomes.sh`
Expected: `ALL PASS`. (Iterate on the workflow in the n8n editor until green — this is Part A's gate.)

- [ ] **Step 3: Commit**

```bash
git add Tools/n8n/test-dashboard-outcomes.sh
git commit -m "test(dashboard): e2e script for the DashboardOutcomes webhook"
```

---

# PART B — Navigation restructure

**Deliverable:** tab-2 is a `Screen_Dashboard` placeholder; Add-Bot (`Screen_New`) opens as a slide-in overlay from the Bots page (header `+`, empty-state CTA, or auto on zero bots); it closes on creation-success, on back/swipe, and on any tab switch; the Bots page has a real empty state. No server dependency.

### Task B1: `AddBotPanel` overlay controller

**Files:**
- Create: `Assets/Scripts/Main/AddBotPanel.cs`

**Interfaces:**
- Produces: `AddBotPanel.Instance` (static), `public void Open()`, `public void Close()`, `public bool IsOpen`. Slides `Screen_New` in/out with the ProfileSubPages timing (0.3s OutCubic in / 0.25s InCubic out). `Open()` is idempotent (no-op when already open).

- [ ] **Step 1: Write `AddBotPanel.cs`** (mirrors `ProfileSubPages` slide semantics; the component lives on `Screen_New`)

```csharp
using DG.Tweening;
using UnityEngine;

/// <summary>
/// Presents Screen_New (the Add-Bot form) as a slide-in overlay instead of a bottom
/// tab. Open()/Close() mirror ProfileSubPages: SetActive(true) then slide from the
/// right on open; slide out then SetActive(false) on close. Closed by
/// BottomTabManager on any tab switch and by Manager on creation-success.
/// </summary>
public class AddBotPanel : MonoBehaviour
{
    private const float SlideInDuration = 0.3f;
    private const float SlideOutDuration = 0.25f;

    public static AddBotPanel Instance { get; private set; }

    private RectTransform _rt;
    private Canvas _rootCanvas;
    private Tween _activeSlide;

    public bool IsOpen => gameObject.activeSelf;

    private void Awake()
    {
        Instance = this;
        _rt = GetComponent<RectTransform>();
        _rootCanvas = GetComponentInParent<Canvas>();
        // Start hidden — the bottom nav no longer owns this panel's visibility.
        gameObject.SetActive(false);
    }

    public void Open()
    {
        if (IsOpen) return;                      // idempotent — see EmptyStateView + zero-bot auto-open
        gameObject.SetActive(true);
        transform.SetAsLastSibling();            // draw above the Bots page
        _activeSlide?.Kill();
        _rt.anchoredPosition = new Vector2(CanvasWidth(), _rt.anchoredPosition.y);
        _activeSlide = _rt.DOAnchorPosX(0f, SlideInDuration).SetEase(Ease.OutCubic);
    }

    public void Close()
    {
        if (!IsOpen) return;
        _activeSlide?.Kill();
        _activeSlide = _rt.DOAnchorPosX(CanvasWidth(), SlideOutDuration)
            .SetEase(Ease.InCubic)
            .OnComplete(() => gameObject.SetActive(false));
    }

    /// <summary>Instant hide with no tween — used when a tab switch must close us now.</summary>
    public void CloseImmediate()
    {
        if (!IsOpen) return;
        _activeSlide?.Kill();
        _rt.anchoredPosition = new Vector2(CanvasWidth(), _rt.anchoredPosition.y);
        gameObject.SetActive(false);
    }

    private float CanvasWidth() =>
        _rootCanvas != null ? _rootCanvas.GetComponent<RectTransform>().rect.width : 1080f;
}
```

- [ ] **Step 2: Verify compile** — run Assets→Refresh (or the headless build); confirm no compile errors and the `.meta` for `AddBotPanel.cs` exists.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Main/AddBotPanel.cs Assets/Scripts/Main/AddBotPanel.cs.meta
git commit -m "feat(nav): AddBotPanel slide-in overlay controller for Screen_New"
```

### Task B2: `Manager.CloseAddBotForm()` + close on creation-success

**Files:**
- Modify: `Assets/Scripts/Main/Manager.cs` (add public method; call it in `ShowAuthSuccess`)

**Interfaces:**
- Consumes: `AddBotPanel.Instance` (Task B1), existing `CancelBotCreation()` (private), `isCreatingBot` field.
- Produces: `public void CloseAddBotForm()` — cancels an in-flight wizard then closes the overlay.

- [ ] **Step 1: Add `CloseAddBotForm`** near `CancelBotCreation` (Manager.cs:1355). It reuses cancel semantics when a wizard is mid-flight, then closes the panel:

```csharp
    /// <summary>
    /// Public close for the Add-Bot overlay (back chevron / swipe). Cancels an
    /// in-flight wizard (deletes any half-created Wappi profiles) exactly like the
    /// auth back buttons, then slides the panel away.
    /// </summary>
    public void CloseAddBotForm()
    {
        if (isCreatingBot) CancelBotCreation();
        AddBotPanel.Instance?.Close();
    }
```

- [ ] **Step 2: Close the overlay on creation-success.** In `ShowAuthSuccess` (Manager.cs:1471), the final-auth branch already does `SwitchTab(BottomTabManager.BotsTabIndex)`. Because `Screen_New` is no longer a tab panel, nothing hides it — add a close right there. Find the final-auth `else` block:

```csharp
            else
            {
                // Final auth — switch to bots tab before hiding
                var tabManager = FindFirstObjectByType<BottomTabManager>();
                if (tabManager != null)
                    tabManager.SwitchTab(BottomTabManager.BotsTabIndex);
            }
```

and replace it with:

```csharp
            else
            {
                // Final auth — close the Add-Bot overlay and land on the Bots tab.
                AddBotPanel.Instance?.CloseImmediate();
                var tabManager = FindFirstObjectByType<BottomTabManager>();
                if (tabManager != null)
                    tabManager.SwitchTab(BottomTabManager.BotsTabIndex);
            }
```

(Belt-and-suspenders: the `SwitchTab` below also closes it via Task B3, but this makes success-close explicit and order-independent.)

- [ ] **Step 3: Verify compile** (Assets→Refresh / headless build; no errors).

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Main/Manager.cs
git commit -m "feat(nav): Manager.CloseAddBotForm + close Add-Bot overlay on creation success"
```

### Task B3: Close the overlay on any tab switch

**Files:**
- Modify: `Assets/Scripts/Main/BottomTabManager.cs` (`SwitchTab`, before the already-active guard)

**Interfaces:**
- Consumes: `AddBotPanel.Instance` (Task B1).

- [ ] **Step 1: Add the close hook** at the very top of `SwitchTab` (BottomTabManager.cs:140), before the already-active early-return so re-tapping a tab also dismisses the overlay:

```csharp
    public void SwitchTab(int index)
    {
        if (!IsValidIndex(index))
        {
            Debug.LogWarning($"[BottomTabManager] SwitchTab({index}) is out of range. " +
                             $"Valid range: 0 – {tabs.Count - 1}.");
            return;
        }

        // The Add-Bot form is a slide-in overlay (not a tab panel), so the tab loop
        // below won't hide it. Any tab navigation dismisses it. Placed before the
        // already-active guard so a re-tap on the current tab closes it too.
        if (AddBotPanel.Instance != null && AddBotPanel.Instance.IsOpen)
            AddBotPanel.Instance.CloseImmediate();

        // Skip if this tab is already active (avoids redundant UI work)
        if (index == _activeTabIndex) return;
        // ...unchanged below...
```

- [ ] **Step 2: Verify compile.**

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Main/BottomTabManager.cs
git commit -m "feat(nav): dismiss Add-Bot overlay on any bottom-tab switch"
```

### Task B4: `BotsPage` — open overlay directly, empty state, zero-bot auto-open

**Files:**
- Modify: `Assets/Scripts/Main/BotsPage.cs`

**Interfaces:**
- Consumes: `AddBotPanel.Instance`, `Manager.Instance.FindBotByName`/`BotsParent`, `BottomTabManager.BotsTabIndex`.
- Produces: `public void StartNewBot()` (now ensures Bots tab active → opens overlay, idempotent), `public void RefreshEmptyState()`.

- [ ] **Step 1: Rewrite `BotsPage.cs`.** Replaces the bottom-nav forwarding with a direct overlay open, adds the empty-state root reference and the zero-bot auto-open. `emptyState` and `botsParent` are new serialized refs (wired by Task B5's builder).

```csharp
using UnityEngine;
using UnityEngine.UI;

public class BotsPage : MonoBehaviour
{
    [Tooltip("Plus button in the Bots page header (top-right).")]
    [SerializeField] private Button NewBotButton;

    [Tooltip("Empty-state root shown when no bots exist (hero + CTA).")]
    [SerializeField] private GameObject emptyState;

    [Tooltip("Parent holding the Bot cards (Manager.BotsParent).")]
    [SerializeField] private Transform botsParent;

    public static BotsPage Instance;

    void Start()
    {
        Instance = this;
        if (NewBotButton != null)
            NewBotButton.onClick.AddListener(StartNewBot);
    }

    void OnEnable()
    {
        // Deferred one frame so a tab switch settles and freshly-created/deleted
        // cards are counted. RefreshEmptyState both toggles the empty UI and, when
        // there are zero bots, auto-opens the Add-Bot overlay.
        if (isActiveAndEnabled) Invoke(nameof(RefreshEmptyState), 0f);
    }

    public void RefreshEmptyState()
    {
        bool hasBots = botsParent != null && botsParent.childCount > 0;
        if (emptyState != null) emptyState.SetActive(!hasBots);
        if (!hasBots) StartNewBot();   // idempotent — no double-open if already open
    }

    /// <summary>
    /// Opens the Add-Bot overlay. Ensures the Bots tab is active first so closing the
    /// form always lands on the Bots page. Idempotent (AddBotPanel.Open no-ops when
    /// already open). Public so the header + and the chats empty-state CTA share it.
    /// </summary>
    public void StartNewBot()
    {
        var tabs = FindFirstObjectByType<BottomTabManager>();
        if (tabs != null) tabs.SwitchTab(BottomTabManager.BotsTabIndex);
        AddBotPanel.Instance?.Open();
    }
}
```

Note: `StartNewBot` stays synchronous (`AddBotPanel.Open()` calls `SetActive(true)` immediately), preserving `EmptyStateView.OpenCreateBotFlow`'s `SelectPlatform(1)`-right-after contract. The `SwitchTab` inside `StartNewBot` runs the Task B3 close hook, but the immediately-following `Open()` re-opens — order is: switch (closes nothing, overlay not yet open) → open. When called from `RefreshEmptyState` during `OnEnable` we're already on the Bots tab so `SwitchTab` early-returns.

- [ ] **Step 2: Verify compile.**

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Main/BotsPage.cs
git commit -m "feat(nav): BotsPage opens Add-Bot overlay directly + empty-state + zero-bot auto-open"
```

### Task B5: Scene surgery — dashboard placeholder, Screen_New back button, Bots empty state

**Files:**
- Create: `Assets/Editor/NavRestructureBuilder.cs`
- Modify: `Assets/Scenes/Main.unity` (via the builder + manual TabData rewire)

**Interfaces:**
- Consumes: builder helpers (copy the verbatim helper methods from `ProfileSubPagesBuilder`: `NewChild`, `SetAnchors`, `StretchFill`, `AddText`, `AddIconImage`, `AddRounded`, `RefreshRounded`, `DestroyAllByName`, `Hex`, `LoadFont`, font GUID consts, token consts).
- Produces: `Screen_Dashboard` GameObject (empty placeholder, sibling of the other `Screen_*`); `AddBotPanel` component on `Screen_New` + a back chevron in its header; `BotsPage.emptyState` subtree + serialized refs.

- [ ] **Step 1: Write `NavRestructureBuilder.cs`** with a `[MenuItem("Tools/Nav Restructure/Build")]` interactive entry + `BuildHeadless()`, following the `ProfileSubPagesBuilder` idioms (idempotent `DestroyAllByName`, one `SerializedObject` stamp per component, rounded `.Refresh()` after `Canvas.ForceUpdateCanvases()`, `MarkSceneDirty` interactive / `SaveScene` headless). It performs three builds:

  **(a) `Screen_Dashboard` placeholder** — find the canvas (parent of `Screen_Bots`), `DestroyAllByName(canvas, "Screen_Dashboard")`, create a new stretched child `Screen_Dashboard` with a full-bleed `Image` bg `#F0F2F5`, a `Header` (h=300, white, 2px `#E4E6EB` bottom hairline, centered 55pt Bold title «Сводка»), and an empty `Content` root below it (Part C fills this). Leave it `SetActive(false)`.

  **(b) `AddBotPanel` on `Screen_New`** — find `Screen_New`; `AddComponent<AddBotPanel>()` if absent; find its existing header and add a `BackButton` (120×120 hit area at (70,90) from bottom-left, 60×60 `_chevronLeft` sprite tinted `#1B7CEB`) if absent; wire `backButton.onClick` → `Manager.Instance.CloseAddBotForm()` via a runtime listener added in `Manager.Start` (see Step 3 — the builder only creates the button and tags it by name `AddBotBackButton`). Also add the generic left-edge `SwipeBack` strip (copy the `BuildPanelShell` swipe-strip block verbatim, `panelToSlide` = Screen_New's RectTransform), with `SwipeToBackPanel.OnCommitted` wired in Manager.Start to `CloseAddBotForm`.

  **(c) Bots empty state** — find `BotsPage`; `DestroyAllByName(botsPageGo, "EmptyState")`; build an `EmptyState` child (initially inactive) centered in the scroll area: a hero `Image` (reuse `Assets/Images/bot_hero.png` per the add-bot hero memory — RobotImage tint MUST be white), a 50pt Bold title «Создайте первого бота», a 38pt Regular `#65676B` body «Бот-ассистент отвечает клиентам в WhatsApp круглосуточно», and a primary CTA button (blue `#1B7CEB`, 144 tall, radius 40, label «Создать бота») whose `onClick` is tagged for Manager.Start wiring to `BotsPage.Instance.StartNewBot`. Stamp `BotsPage.emptyState` (the EmptyState GO) and `BotsPage.botsParent` (existing `BotsParent`) via `SerializedObject`.

- [ ] **Step 2: Run the builder** — Tools → Nav Restructure → Build (Editor open). Then in the Inspector, **manually rewire the tab-2 `TabData`** on `BottomNavPanel`'s `BottomTabManager`:
  - `tabName`: `Сводка`
  - `screenPanel`: `Screen_Dashboard`
  - `labelText`: set text to «Сводка»
  - `inactiveIcon` / `activeIcon`: the two dashboard sprites (Task B6)
  - `activeLabelColor`: `#1B7CEB`
  - Leave `tabRoot`/`iconImage` as-is (same nav button slot).
  Save the scene (Cmd+S).

- [ ] **Step 3: Wire the tagged buttons in `Manager.Start`** — add listeners for the two builder-created controls (they can't be wired by the builder because their targets are runtime singletons). Near the existing Add-Bot wiring block (Manager.cs:255):

```csharp
        // Add-Bot overlay chrome (created by NavRestructureBuilder)
        var addBotBack = GameObject.Find("Screen_New")?.transform.Find("Header/AddBotBackButton")?.GetComponent<Button>();
        if (addBotBack != null) addBotBack.onClick.AddListener(CloseAddBotForm);
```

(The empty-state CTA and swipe are wired by their own components — the empty-state button is wired in `BotsPage`/builder by name, and `SwipeToBackPanel.OnCommitted` is set on the Screen_New strip in this same block: `Screen_New/SwipeBack`'s `SwipeToBackPanel.OnCommitted = CloseAddBotForm`.)

- [ ] **Step 4: Manual verification (device or Editor Play).** Confirm: (1) tab-2 shows the «Сводка» placeholder; (2) Bots header `+` slides the Add-Bot form in; (3) back chevron + left-swipe close it; (4) switching tabs closes it; (5) with zero bots, tapping the Bots tab auto-opens the form and the empty state shows behind it; (6) creating a bot lands on the Bots tab with the form closed. Report results (this is scene work — gate on Editor/device GREEN, not unit tests).

- [ ] **Step 5: Commit** (stage the builder + scene)

```bash
git add Assets/Editor/NavRestructureBuilder.cs Assets/Editor/NavRestructureBuilder.cs.meta \
        Assets/Scenes/Main.unity Assets/Scripts/Main/Manager.cs
git commit -m "feat(nav): Screen_Dashboard placeholder, Add-Bot overlay chrome, Bots empty state"
```

### Task B6: «Сводка» tab icons

**Files:**
- Create: `Tools/render_dashboard_icon.js` (mirrors `Tools/render_hero.js` node renderer)
- Create: `Assets/Images/Nav/dashboard_inactive.png`, `Assets/Images/Nav/dashboard_active.png` (+ `.meta`)

- [ ] **Step 1: Write the renderer** — a small node script (same dependencies/pattern as `Tools/render_hero.js`) that rasterizes a line-chart glyph SVG to two PNGs: `dashboard_inactive.png` (muted `#8C8C8C` stroke, outline weight) and `dashboard_active.png` (`#1B7CEB`, filled/bolder), 132×132 canvas.

- [ ] **Step 2: Render + import** — run the script, drop the PNGs in `Assets/Images/Nav/`, Assets→Refresh; the builder's icon import step (or manual) sets them to Sprite (Single). Assign them into the tab-2 `TabData` (Task B5 Step 2).

- [ ] **Step 3: Commit**

```bash
git add Tools/render_dashboard_icon.js Assets/Images/Nav/dashboard_*.png Assets/Images/Nav/dashboard_*.png.meta Assets/Scenes/Main.unity
git commit -m "feat(nav): Сводка tab icons"
```

---

# PART C — Dashboard screen (data + UI)

**Deliverable:** `Screen_Dashboard` shows the Variant-B UI (period selector, bot chips, hero заявки card + funnel, status rows, recent заявки), fetches from `DashboardOutcomes`, drills into per-status lists, and deep-links to chats. Depends on Part A (contract) and Part B (`Screen_Dashboard` placeholder).

**File structure (new folder `Assets/Scripts/Main/Dashboard/`):** models, pure metric/mapping/gate logic (TDD), disk store, MonoBehaviour controller; plus the Editor builder. Pure logic is split out so it's unit-testable per the Global Constraints.

### Task C1: Models + response parsing

**Files:**
- Create: `Assets/Scripts/Main/Dashboard/DashboardModels.cs`
- Test: `Assets/Tests/Editor/Chat/DashboardResponseParseTests.cs`

**Interfaces:**
- Produces: `enum OutcomeStatus { Unknown, OrderCollected, OwnerNeeded, InDialog, ClientSilent, QuestionClosed }`; `OutcomeStatusMap.FromId(string)` / `.ToId(OutcomeStatus)`; `[Serializable] class DashboardOutcome { string profileId, chatId, outcome, summary; long outcomeAt, lastMessageAt; OutcomeStatus Status => OutcomeStatusMap.FromId(outcome); }`; `class DashboardResponse { bool success; int classified; bool truncated; List<DashboardOutcome> outcomes; }`; `DashboardResponse.Parse(string json)` (JsonConvert, null-safe).

- [ ] **Step 1: Write the failing test**

```csharp
using NUnit.Framework;

public class DashboardResponseParseTests
{
    [Test] public void ParsesOutcomesAndFlags()
    {
        string json = "{\"success\":true,\"classified\":2,\"truncated\":true,\"outcomes\":[" +
            "{\"profileId\":\"p1\",\"chatId\":\"7701@c.us\",\"outcome\":\"order_collected\"," +
            "\"summary\":\"101 роза\",\"outcomeAt\":1700000000000,\"lastMessageAt\":1700000005000}]}";
        var r = DashboardResponse.Parse(json);
        Assert.IsTrue(r.success);
        Assert.IsTrue(r.truncated);
        Assert.AreEqual(1, r.outcomes.Count);
        Assert.AreEqual(OutcomeStatus.OrderCollected, r.outcomes[0].Status);
        Assert.AreEqual("7701@c.us", r.outcomes[0].chatId);
        Assert.AreEqual(1700000005000L, r.outcomes[0].lastMessageAt);
    }

    [Test] public void UnknownOutcomeIdMapsToUnknown()
        => Assert.AreEqual(OutcomeStatus.Unknown, OutcomeStatusMap.FromId("nonsense"));

    [Test] public void NullOrGarbageJsonIsSafe()
    {
        Assert.IsNull(DashboardResponse.Parse(null));
        Assert.IsNull(DashboardResponse.Parse("not json"));
    }
}
```

- [ ] **Step 2: Run it — expect FAIL** (`DashboardResponse` not defined). Drop `Temp/claude/run-tests.trigger` or run `Tools/run-tests-headless.sh DashboardResponseParseTests`.

- [ ] **Step 3: Write `DashboardModels.cs`**

```csharp
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

public enum OutcomeStatus { Unknown, OrderCollected, OwnerNeeded, InDialog, ClientSilent, QuestionClosed }

public static class OutcomeStatusMap
{
    public static OutcomeStatus FromId(string id) => id switch
    {
        "order_collected" => OutcomeStatus.OrderCollected,
        "owner_needed"    => OutcomeStatus.OwnerNeeded,
        "in_dialog"       => OutcomeStatus.InDialog,
        "client_silent"   => OutcomeStatus.ClientSilent,
        "question_closed" => OutcomeStatus.QuestionClosed,
        _                 => OutcomeStatus.Unknown,
    };

    public static string ToId(OutcomeStatus s) => s switch
    {
        OutcomeStatus.OrderCollected => "order_collected",
        OutcomeStatus.OwnerNeeded    => "owner_needed",
        OutcomeStatus.InDialog       => "in_dialog",
        OutcomeStatus.ClientSilent   => "client_silent",
        OutcomeStatus.QuestionClosed => "question_closed",
        _                            => "",
    };
}

[Serializable]
public class DashboardOutcome
{
    public string profileId;
    public string chatId;
    public string outcome;
    public string summary;
    public long outcomeAt;      // unix ms
    public long lastMessageAt;  // unix ms

    [JsonIgnore] public OutcomeStatus Status => OutcomeStatusMap.FromId(outcome);
}

[Serializable]
public class DashboardResponse
{
    public bool success;
    public int classified;
    public bool truncated;
    public List<DashboardOutcome> outcomes = new();

    public static DashboardResponse Parse(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var r = JsonConvert.DeserializeObject<DashboardResponse>(json);
            if (r != null) r.outcomes ??= new List<DashboardOutcome>();
            return r;
        }
        catch (JsonException) { return null; }
    }
}
```

- [ ] **Step 4: Run tests — expect PASS.** (Assets→Refresh first so the new `.cs` files import; confirm `.meta` files exist.)

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Main/Dashboard/DashboardModels.cs* Assets/Tests/Editor/Chat/DashboardResponseParseTests.cs*
git commit -m "feat(dashboard): outcome models + response parsing"
```

### Task C2: Status display info (labels + colors)

**Files:**
- Create: `Assets/Scripts/Main/Dashboard/DashboardStatusInfo.cs`
- Test: `Assets/Tests/Editor/Chat/DashboardStatusInfoTests.cs`

**Interfaces:**
- Produces: `DashboardStatusInfo.Label(OutcomeStatus)` (RU), `.BgColor(OutcomeStatus)`, `.FgColor(OutcomeStatus)`, and `DashboardStatusInfo.Ordered` (the 5 statuses in funnel display order).

- [ ] **Step 1: Write the failing test**

```csharp
using NUnit.Framework;
using UnityEngine;

public class DashboardStatusInfoTests
{
    [Test] public void LabelsAreRussian()
    {
        Assert.AreEqual("Заявка", DashboardStatusInfo.Label(OutcomeStatus.OrderCollected));
        Assert.AreEqual("Нужен владелец", DashboardStatusInfo.Label(OutcomeStatus.OwnerNeeded));
        Assert.AreEqual("Клиент замолчал", DashboardStatusInfo.Label(OutcomeStatus.ClientSilent));
    }

    [Test] public void OrderCollectedUsesPillGreen()
    {
        ColorUtility.TryParseHtmlString("#34C759", out var fg);
        Assert.AreEqual(fg, DashboardStatusInfo.FgColor(OutcomeStatus.OrderCollected));
    }

    [Test] public void OrderedHasFiveStatusesOrderCollectedFirst()
    {
        Assert.AreEqual(5, DashboardStatusInfo.Ordered.Length);
        Assert.AreEqual(OutcomeStatus.OrderCollected, DashboardStatusInfo.Ordered[0]);
    }
}
```

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3: Write `DashboardStatusInfo.cs`**

```csharp
using UnityEngine;

/// <summary>
/// RU labels + pill colors for the 5 conversation-outcome statuses. Palette matches
/// BotStatusPill (order_collected reuses the active pill green) and the spec table.
/// </summary>
public static class DashboardStatusInfo
{
    public static readonly OutcomeStatus[] Ordered =
    {
        OutcomeStatus.OrderCollected,
        OutcomeStatus.OwnerNeeded,
        OutcomeStatus.InDialog,
        OutcomeStatus.ClientSilent,
        OutcomeStatus.QuestionClosed,
    };

    public static string Label(OutcomeStatus s) => s switch
    {
        OutcomeStatus.OrderCollected => "Заявка",
        OutcomeStatus.OwnerNeeded    => "Нужен владелец",
        OutcomeStatus.InDialog       => "В диалоге",
        OutcomeStatus.ClientSilent   => "Клиент замолчал",
        OutcomeStatus.QuestionClosed => "Вопрос закрыт",
        _                            => "—",
    };

    public static Color BgColor(OutcomeStatus s) => Hex(s switch
    {
        OutcomeStatus.OrderCollected => "#E8F8EE",
        OutcomeStatus.OwnerNeeded    => "#FFF3E0",
        OutcomeStatus.InDialog       => "#E3F2FF",
        OutcomeStatus.ClientSilent   => "#F2F2F7",
        OutcomeStatus.QuestionClosed => "#E4E6EB",
        _                            => "#E4E6EB",
    });

    public static Color FgColor(OutcomeStatus s) => Hex(s switch
    {
        OutcomeStatus.OrderCollected => "#34C759",
        OutcomeStatus.OwnerNeeded    => "#F57C00",
        OutcomeStatus.InDialog       => "#007AFF",
        OutcomeStatus.ClientSilent   => "#8E8E93",
        OutcomeStatus.QuestionClosed => "#65676B",
        _                            => "#65676B",
    });

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color c);
        return c;
    }
}
```

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Main/Dashboard/DashboardStatusInfo.cs* Assets/Tests/Editor/Chat/DashboardStatusInfoTests.cs*
git commit -m "feat(dashboard): status labels + pill palette"
```

### Task C3: Metric math (periods, delta, funnel, recent)

**Files:**
- Create: `Assets/Scripts/Main/Dashboard/DashboardMetrics.cs`
- Test: `Assets/Tests/Editor/Chat/DashboardMetricsTests.cs`

**Interfaces:**
- Produces: `enum DashboardPeriod { Today, Week, Month }`; `struct Window { long CurStart, CurEnd, PrevStart, PrevEnd; }`; `DashboardMetrics.ComputeWindow(DashboardPeriod, long nowMs, long todayStartMs)`; `.CountOrders(IEnumerable<DashboardOutcome>, Window)` (order_collected with `outcomeAt` in cur window) + `.CountOrdersPrev(...)`; `.StatusCounts(IEnumerable<DashboardOutcome>, Window)` → `int[5]` indexed by `DashboardStatusInfo.Ordered`; `.Recent(IEnumerable<DashboardOutcome>, Window, int n)` (order_collected in window, `lastMessageAt` desc); `.FilterByProfile(IEnumerable<DashboardOutcome>, string profileIdOrNull)`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Generic;
using NUnit.Framework;

public class DashboardMetricsTests
{
    private static DashboardOutcome O(string p, string status, long outAt, long lastAt) =>
        new DashboardOutcome { profileId = p, chatId = p + ":c", outcome = status,
                               outcomeAt = outAt, lastMessageAt = lastAt };

    private const long Day = 86_400_000L;

    [Test] public void TodayWindowComparesAgainstSameTimeYesterday()
    {
        long todayStart = 1_000_000_000_000L;
        long now = todayStart + 10 * 3_600_000L;         // 10:00 today
        var w = DashboardMetrics.ComputeWindow(DashboardPeriod.Today, now, todayStart);
        Assert.AreEqual(todayStart, w.CurStart);
        Assert.AreEqual(now, w.CurEnd);
        Assert.AreEqual(todayStart - Day, w.PrevStart);  // yesterday midnight
        Assert.AreEqual(now - Day, w.PrevEnd);           // 10:00 yesterday (partial vs partial)
    }

    [Test] public void CountsOrdersInCurrentWindowOnly()
    {
        long todayStart = 1_000_000_000_000L;
        long now = todayStart + 12 * 3_600_000L;
        var w = DashboardMetrics.ComputeWindow(DashboardPeriod.Today, now, todayStart);
        var rows = new List<DashboardOutcome>
        {
            O("p", "order_collected", todayStart + 3_600_000L, now),  // today
            O("p", "order_collected", todayStart - 2 * Day, now),     // old — excluded
            O("p", "in_dialog",       todayStart + 3_600_000L, now),  // not an order
        };
        Assert.AreEqual(1, DashboardMetrics.CountOrders(rows, w));
    }

    [Test] public void StatusCountsBucketByCurrentOutcomeInWindow()
    {
        var w = new Window { CurStart = 0, CurEnd = 100, PrevStart = -100, PrevEnd = 0 };
        var rows = new List<DashboardOutcome>
        {
            O("p", "order_collected", 10, 50),
            O("p", "owner_needed",    10, 60),
            O("p", "order_collected", 10, 999),   // lastMessageAt outside window — excluded
        };
        var counts = DashboardMetrics.StatusCounts(rows, w);
        Assert.AreEqual(1, counts[0]);  // OrderCollected (Ordered[0])
        Assert.AreEqual(1, counts[1]);  // OwnerNeeded (Ordered[1])
    }

    [Test] public void FilterByProfileNullReturnsAll()
    {
        var rows = new List<DashboardOutcome> { O("a","in_dialog",1,1), O("b","in_dialog",1,1) };
        Assert.AreEqual(2, new List<DashboardOutcome>(DashboardMetrics.FilterByProfile(rows, null)).Count);
        Assert.AreEqual(1, new List<DashboardOutcome>(DashboardMetrics.FilterByProfile(rows, "a")).Count);
    }
}
```

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3: Write `DashboardMetrics.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;

public enum DashboardPeriod { Today, Week, Month }

public struct Window
{
    public long CurStart, CurEnd, PrevStart, PrevEnd;
}

public static class DashboardMetrics
{
    private const long Day = 86_400_000L;

    public static Window ComputeWindow(DashboardPeriod period, long nowMs, long todayStartMs)
    {
        switch (period)
        {
            case DashboardPeriod.Today:
                return new Window {
                    CurStart = todayStartMs, CurEnd = nowMs,
                    PrevStart = todayStartMs - Day, PrevEnd = nowMs - Day };
            case DashboardPeriod.Week:
                return new Window {
                    CurStart = nowMs - 7 * Day, CurEnd = nowMs,
                    PrevStart = nowMs - 14 * Day, PrevEnd = nowMs - 7 * Day };
            default: // Month
                return new Window {
                    CurStart = nowMs - 30 * Day, CurEnd = nowMs,
                    PrevStart = nowMs - 60 * Day, PrevEnd = nowMs - 30 * Day };
        }
    }

    public static IEnumerable<DashboardOutcome> FilterByProfile(
        IEnumerable<DashboardOutcome> rows, string profileIdOrNull)
        => string.IsNullOrEmpty(profileIdOrNull)
            ? rows
            : rows.Where(r => r.profileId == profileIdOrNull);

    public static int CountOrders(IEnumerable<DashboardOutcome> rows, Window w)
        => rows.Count(r => r.Status == OutcomeStatus.OrderCollected
                        && r.outcomeAt >= w.CurStart && r.outcomeAt <= w.CurEnd);

    public static int CountOrdersPrev(IEnumerable<DashboardOutcome> rows, Window w)
        => rows.Count(r => r.Status == OutcomeStatus.OrderCollected
                        && r.outcomeAt >= w.PrevStart && r.outcomeAt <= w.PrevEnd);

    /// <summary>Counts current outcome of conversations active in the window,
    /// indexed by DashboardStatusInfo.Ordered.</summary>
    public static int[] StatusCounts(IEnumerable<DashboardOutcome> rows, Window w)
    {
        var counts = new int[DashboardStatusInfo.Ordered.Length];
        foreach (var r in rows)
        {
            if (r.lastMessageAt < w.CurStart || r.lastMessageAt > w.CurEnd) continue;
            int idx = System.Array.IndexOf(DashboardStatusInfo.Ordered, r.Status);
            if (idx >= 0) counts[idx]++;
        }
        return counts;
    }

    public static List<DashboardOutcome> Recent(IEnumerable<DashboardOutcome> rows, Window w, int n)
        => rows.Where(r => r.Status == OutcomeStatus.OrderCollected
                        && r.lastMessageAt >= w.CurStart && r.lastMessageAt <= w.CurEnd)
               .OrderByDescending(r => r.lastMessageAt)
               .Take(n).ToList();
}
```

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Main/Dashboard/DashboardMetrics.cs* Assets/Tests/Editor/Chat/DashboardMetricsTests.cs*
git commit -m "feat(dashboard): period/delta/funnel/recent metric math"
```

### Task C4: Profile→bot resolution + refresh gate

**Files:**
- Create: `Assets/Scripts/Main/Dashboard/SessionChatMap.cs`
- Create: `Assets/Scripts/Main/Dashboard/DashboardRefreshGate.cs`
- Test: `Assets/Tests/Editor/Chat/SessionChatMapTests.cs`
- Test: `Assets/Tests/Editor/Chat/DashboardRefreshGateTests.cs`

**Interfaces:**
- Produces: `SessionChatMap.ResolveBotName(IReadOnlyDictionary<string,string> profileToBot, string profileId)` → bot name or null; `DashboardRefreshGate.ShouldFetch(long lastFetchMs, long nowMs, long minIntervalMs = 60_000)`.

- [ ] **Step 1: Write both failing tests**

`SessionChatMapTests.cs`:
```csharp
using System.Collections.Generic;
using NUnit.Framework;

public class SessionChatMapTests
{
    [Test] public void ResolvesProfileToBotName()
    {
        var map = new Dictionary<string, string> { { "wa123", "Bot0" }, { "wa999", "Bot2" } };
        Assert.AreEqual("Bot2", SessionChatMap.ResolveBotName(map, "wa999"));
    }

    [Test] public void UnknownProfileReturnsNull()
        => Assert.IsNull(SessionChatMap.ResolveBotName(new Dictionary<string, string>(), "nope"));

    [Test] public void NullInputsSafe()
    {
        Assert.IsNull(SessionChatMap.ResolveBotName(null, "x"));
        Assert.IsNull(SessionChatMap.ResolveBotName(new Dictionary<string, string>(), null));
    }
}
```

`DashboardRefreshGateTests.cs`:
```csharp
using NUnit.Framework;

public class DashboardRefreshGateTests
{
    [Test] public void FetchesWhenNeverFetched()
        => Assert.IsTrue(DashboardRefreshGate.ShouldFetch(0, 1_000_000));

    [Test] public void SkipsWithinInterval()
        => Assert.IsFalse(DashboardRefreshGate.ShouldFetch(1_000_000, 1_030_000)); // 30s < 60s

    [Test] public void FetchesAfterInterval()
        => Assert.IsTrue(DashboardRefreshGate.ShouldFetch(1_000_000, 1_061_000));  // 61s > 60s
}
```

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3: Write both files**

`SessionChatMap.cs`:
```csharp
using System.Collections.Generic;

/// <summary>Maps a WhatsApp profile id (from an outcome row) to the owning bot's
/// GameObject name. The controller builds the dictionary from live bots.</summary>
public static class SessionChatMap
{
    public static string ResolveBotName(IReadOnlyDictionary<string, string> profileToBot, string profileId)
    {
        if (profileToBot == null || string.IsNullOrEmpty(profileId)) return null;
        return profileToBot.TryGetValue(profileId, out var name) ? name : null;
    }
}
```

`DashboardRefreshGate.cs`:
```csharp
/// <summary>Pure time throttle for dashboard fetches (in the mold of TabRefreshGate,
/// which has no time component — this is a separate helper).</summary>
public static class DashboardRefreshGate
{
    public static bool ShouldFetch(long lastFetchMs, long nowMs, long minIntervalMs = 60_000)
        => nowMs - lastFetchMs >= minIntervalMs;
}
```

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Main/Dashboard/SessionChatMap.cs* Assets/Scripts/Main/Dashboard/DashboardRefreshGate.cs* \
        Assets/Tests/Editor/Chat/SessionChatMapTests.cs* Assets/Tests/Editor/Chat/DashboardRefreshGateTests.cs*
git commit -m "feat(dashboard): profile→bot resolver + fetch throttle"
```

### Task C5: Disk cache store

**Files:**
- Create: `Assets/Scripts/Main/Dashboard/DashboardStore.cs`

**Interfaces:**
- Consumes: `DashboardResponse` / `DashboardOutcome` (C1).
- Produces: `DashboardStore.Save(List<DashboardOutcome>)`, `DashboardStore.Load()` → `List<DashboardOutcome>` (empty on miss), `DashboardStore.LastFetchMs` (get/set, persisted). One file, all bots: `persistentDataPath/dashboard_cache.json`.

- [ ] **Step 1: Write `DashboardStore.cs`** (no test — thin IO wrapper; the serializable payload is already covered by C1). Follows the per-bot cache IO pattern (JsonConvert to `persistentDataPath`).

```csharp
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

public static class DashboardStore
{
    [System.Serializable]
    private class Payload
    {
        public long lastFetchMs;
        public List<DashboardOutcome> outcomes = new();
    }

    private static string Path => System.IO.Path.Combine(Application.persistentDataPath, "dashboard_cache.json");

    public static long LastFetchMs { get; private set; }

    public static void Save(List<DashboardOutcome> outcomes, long nowMs)
    {
        LastFetchMs = nowMs;
        try
        {
            var p = new Payload { lastFetchMs = nowMs, outcomes = outcomes ?? new List<DashboardOutcome>() };
            File.WriteAllText(Path, JsonConvert.SerializeObject(p));
        }
        catch (IOException e) { Debug.LogWarning($"[DashboardStore] save failed: {e.Message}"); }
    }

    public static List<DashboardOutcome> Load()
    {
        try
        {
            if (!File.Exists(Path)) return new List<DashboardOutcome>();
            var p = JsonConvert.DeserializeObject<Payload>(File.ReadAllText(Path));
            if (p == null) return new List<DashboardOutcome>();
            LastFetchMs = p.lastFetchMs;
            return p.outcomes ?? new List<DashboardOutcome>();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[DashboardStore] load failed: {e.Message}");
            return new List<DashboardOutcome>();
        }
    }
}
```

- [ ] **Step 2: Verify compile.**

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Main/Dashboard/DashboardStore.cs*
git commit -m "feat(dashboard): disk cache store"
```

### Task C6: `DashboardPage` controller

**Files:**
- Create: `Assets/Scripts/Main/Dashboard/DashboardPage.cs`

**Interfaces:**
- Consumes: all C1–C5 logic; `Manager.n8nBaseUrl`, `Manager.Instance.FindBotByName` / `BotsParent`; `ChatManager.Instance.SetActiveBot` / `SelectChat`; `BottomTabManager.WhatsAppTabIndex`; the `DashboardOutcomes` contract (Part A); the builder-created view refs (Task C7) stamped as `[SerializeField]`.
- Produces: the runtime component on `Screen_Dashboard`. Public: `void SetPeriod(DashboardPeriod)`, `void SetBotFilter(string profileIdOrNull)`, `void OpenStatusList(OutcomeStatus)`, `void OpenChat(DashboardOutcome)`.

- [ ] **Step 1: Write `DashboardPage.cs`.** Structure (full render + fetch; view refs are `[SerializeField]` stamped by the builder — names must match Task C7's `StampController`):

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Screen_Dashboard controller (Variant B). Fetches conversation outcomes from the
/// DashboardOutcomes webhook, caches to disk, and renders the hero funnel + status
/// rows + recent заявки. Period and bot filters are client-side (no refetch).
/// </summary>
public class DashboardPage : MonoBehaviour
{
    [Header("Segmented period control")]
    [SerializeField] private Button todayButton, weekButton, monthButton;
    [SerializeField] private RectTransform periodHighlight;

    [Header("Bot chips")]
    [SerializeField] private Transform chipsRow;
    [SerializeField] private GameObject chipPrefabHost;   // an inactive template chip

    [Header("Hero")]
    [SerializeField] private TextMeshProUGUI heroCount, heroDelta, heroSubtitle;
    [SerializeField] private RectTransform funnelBar;     // children segments sized by weight
    [SerializeField] private Transform legendRoot;        // 5 legend rows

    [Header("Status rows")]
    [SerializeField] private Transform statusRowsRoot;    // 5 rows: dot/label/count/chevron

    [Header("Recent заявки")]
    [SerializeField] private Transform recentRoot;
    [SerializeField] private GameObject rowTemplate;      // inactive conversation-row template

    [Header("States")]
    [SerializeField] private GameObject loadingState, emptyState;

    [Header("Drill-down list panel")]
    [SerializeField] private RectTransform listPanel;     // slide-in sub-page shell
    [SerializeField] private Button listBackButton;
    [SerializeField] private TextMeshProUGUI listTitle;
    [SerializeField] private Transform listRoot;

    private readonly List<DashboardOutcome> _all = new();
    private DashboardPeriod _period = DashboardPeriod.Today;
    private string _botFilter;               // null = all bots
    private bool _fetching;
    private const int TruncatedRefetchCap = 5;

    private void Awake()
    {
        if (todayButton) todayButton.onClick.AddListener(() => SetPeriod(DashboardPeriod.Today));
        if (weekButton)  weekButton.onClick.AddListener(() => SetPeriod(DashboardPeriod.Week));
        if (monthButton) monthButton.onClick.AddListener(() => SetPeriod(DashboardPeriod.Month));
        if (listBackButton) listBackButton.onClick.AddListener(CloseStatusList);
        if (listPanel) listPanel.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        _all.Clear();
        _all.AddRange(DashboardStore.Load());     // instant paint from cache
        BuildChips();
        Render();

        long now = NowMs();
        if (DashboardRefreshGate.ShouldFetch(DashboardStore.LastFetchMs, now))
            StartCoroutine(FetchRoutine(0));
        else if (_all.Count == 0)
            SetLoading(true);                     // first-ever open, no cache
    }

    // ---- data ----------------------------------------------------------------

    private List<string> AuthedProfiles()
    {
        var list = new List<string>();
        var parent = Manager.Instance != null ? Manager.Instance.BotsRoot : null;  // public Transform
        if (parent == null) return list;
        foreach (Transform t in parent)
        {
            var bot = t.GetComponent<Bot>();
            if (bot == null) continue;
            string pid = bot.whatsappProfileId;
            if (!string.IsNullOrEmpty(pid) && pid != Bot.UnauthedProfileSentinel)
                list.Add(pid);
        }
        return list;
    }

    private Dictionary<string, string> ProfileToBot()
    {
        var map = new Dictionary<string, string>();
        var parent = Manager.Instance != null ? Manager.Instance.BotsRoot : null;  // public Transform
        if (parent == null) return map;
        foreach (Transform t in parent)
        {
            var bot = t.GetComponent<Bot>();
            if (bot == null) continue;
            if (!string.IsNullOrEmpty(bot.whatsappProfileId) &&
                bot.whatsappProfileId != Bot.UnauthedProfileSentinel)
                map[bot.whatsappProfileId] = t.name;
        }
        return map;
    }

    private IEnumerator FetchRoutine(int attempt)
    {
        if (_fetching && attempt == 0) yield break;
        _fetching = true;

        var profiles = AuthedProfiles();
        if (profiles.Count == 0) { _fetching = false; SetLoading(false); Render(); yield break; }

        string url = $"{Manager.n8nBaseUrl}/webhook/DashboardOutcomes";
        string body = JsonConvert.SerializeObject(new { profileIds = profiles });

        using var req = new UnityWebRequest(url, "POST");
        req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");   // REQUIRED (see Global Constraints)
        req.timeout = 30;
        yield return req.SendWebRequest();

        _fetching = false;

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[Dashboard] fetch failed [{req.responseCode}] {req.error}");
            SetLoading(false);
            Render();                                 // keep cached data
            yield break;
        }

        var parsed = DashboardResponse.Parse(req.downloadHandler.text);
        if (parsed == null || !parsed.success) { SetLoading(false); Render(); yield break; }

        _all.Clear();
        _all.AddRange(parsed.outcomes);
        DashboardStore.Save(_all, NowMs());
        SetLoading(false);
        BuildChips();
        Render();

        // Drain a large backlog in one visit.
        if (parsed.truncated && attempt + 1 < TruncatedRefetchCap)
            StartCoroutine(FetchRoutine(attempt + 1));
    }

    // ---- rendering -----------------------------------------------------------

    private void Render()
    {
        var rows = DashboardMetrics.FilterByProfile(_all, _botFilter).ToList();
        var w = DashboardMetrics.ComputeWindow(_period, NowMs(), TodayStartMs());

        int orders = DashboardMetrics.CountOrders(rows, w);
        int prev = DashboardMetrics.CountOrdersPrev(rows, w);
        int[] counts = DashboardMetrics.StatusCounts(rows, w);

        if (heroCount) heroCount.text = orders.ToString();
        if (heroDelta) SetDelta(orders - prev);
        if (heroSubtitle)
        {
            int active = counts.Sum();
            heroSubtitle.text = $"{active} {Plural(active, "диалог", "диалога", "диалогов")}";
        }
        RenderFunnel(counts);
        RenderStatusRows(counts);
        RenderRecent(DashboardMetrics.Recent(rows, w, 5));

        bool empty = _all.Count == 0 && !_fetching;
        if (emptyState) emptyState.SetActive(empty);
    }

    // RenderFunnel / RenderStatusRows / RenderRecent / SetDelta / BuildChips /
    // SetPeriod / SetBotFilter / OpenStatusList / CloseStatusList / OpenChat:
    // see Steps 2–4 below. (Split for review; all live in this file.)

    // ---- helpers -------------------------------------------------------------

    private void SetLoading(bool on) { if (loadingState) loadingState.SetActive(on); }

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static long TodayStartMs()
    {
        DateTime midnight = DateTime.Now.Date;                 // local midnight
        return new DateTimeOffset(midnight).ToUnixTimeMilliseconds();
    }

    private static string Plural(int n, string one, string few, string many)
    {
        int m10 = n % 10, m100 = n % 100;
        if (m10 == 1 && m100 != 11) return one;
        if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return few;
        return many;
    }
}
```

- [ ] **Step 2: Add the render helpers** (funnel bar weights, status rows, recent rows, delta pill) into the same class:

```csharp
    private void RenderFunnel(int[] counts)
    {
        if (funnelBar == null) return;
        int total = Mathf.Max(1, counts.Sum());
        for (int i = 0; i < funnelBar.childCount && i < counts.Length; i++)
        {
            var seg = funnelBar.GetChild(i) as RectTransform;
            var le = seg.GetComponent<LayoutElement>() ?? seg.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = counts[i];                 // proportional segment
            var img = seg.GetComponent<Image>();
            if (img) img.color = DashboardStatusInfo.FgColor(DashboardStatusInfo.Ordered[i]);
            seg.gameObject.SetActive(counts[i] > 0);
        }
    }

    private void RenderStatusRows(int[] counts)
    {
        if (statusRowsRoot == null) return;
        for (int i = 0; i < statusRowsRoot.childCount && i < counts.Length; i++)
        {
            var row = statusRowsRoot.GetChild(i);
            var status = DashboardStatusInfo.Ordered[i];
            var label = row.Find("Label")?.GetComponent<TextMeshProUGUI>();
            var count = row.Find("Count")?.GetComponent<TextMeshProUGUI>();
            var dot = row.Find("Dot")?.GetComponent<Image>();
            if (label) label.text = DashboardStatusInfo.Label(status);
            if (count) count.text = counts[i].ToString();
            if (dot) dot.color = DashboardStatusInfo.FgColor(status);
            var btn = row.GetComponent<Button>();
            if (btn) { btn.onClick.RemoveAllListeners();
                       var s = status; btn.onClick.AddListener(() => OpenStatusList(s)); }
        }
    }

    private void RenderRecent(List<DashboardOutcome> recent)
    {
        SpawnRows(recentRoot, rowTemplate, recent);
    }

    private void SetDelta(int delta)
    {
        string sign = delta > 0 ? "+" : "";
        heroDelta.text = delta == 0 ? "—" : $"{sign}{delta} к пред.";
        heroDelta.color = delta >= 0
            ? DashboardStatusInfo.FgColor(OutcomeStatus.OrderCollected)
            : DashboardStatusInfo.FgColor(OutcomeStatus.OwnerNeeded);
    }
```

- [ ] **Step 3: Add filters, chips, and drill-down** into the same class:

```csharp
    public void SetPeriod(DashboardPeriod p) { _period = p; MovePeriodHighlight(); Render(); }

    public void SetBotFilter(string profileIdOrNull) { _botFilter = profileIdOrNull; Render(); }

    private void MovePeriodHighlight()
    {
        // Snap the segmented highlight under the active button (DOTween 0.2s OutCubic).
        Button target = _period == DashboardPeriod.Today ? todayButton
                      : _period == DashboardPeriod.Week ? weekButton : monthButton;
        if (periodHighlight != null && target != null)
            periodHighlight.DOAnchorPosX(((RectTransform)target.transform).anchoredPosition.x, 0.2f)
                .SetEase(DG.Tweening.Ease.OutCubic);
    }

    private void BuildChips()
    {
        if (chipsRow == null || chipPrefabHost == null) return;
        // Clear previous (keep the inactive template).
        for (int i = chipsRow.childCount - 1; i >= 0; i--)
        {
            var c = chipsRow.GetChild(i).gameObject;
            if (c != chipPrefabHost) Destroy(c);
        }
        var map = ProfileToBot();
        // Chips hidden entirely with ≤1 bot.
        chipsRow.gameObject.SetActive(map.Count > 1);
        if (map.Count <= 1) return;

        AddChip("Все боты", null, _botFilter == null);
        foreach (var kv in map)
        {
            string botName = PlayerPrefs.GetString(kv.Value + "Name", kv.Value);
            AddChip(botName, kv.Key, _botFilter == kv.Key);
        }
    }

    private void AddChip(string text, string profileId, bool on)
    {
        var go = Instantiate(chipPrefabHost, chipsRow);
        go.SetActive(true);
        var lbl = go.GetComponentInChildren<TextMeshProUGUI>();
        if (lbl) lbl.text = text;
        var img = go.GetComponent<Image>();
        if (img) img.color = on ? DashboardStatusInfo.FgColor(OutcomeStatus.InDialog) : Color.white;
        var btn = go.GetComponent<Button>();
        if (btn) { btn.onClick.AddListener(() => SetBotFilter(profileId)); btn.onClick.AddListener(BuildChips); }
    }

    public void OpenStatusList(OutcomeStatus status)
    {
        if (listPanel == null) return;
        var rows = DashboardMetrics.FilterByProfile(_all, _botFilter)
            .Where(r => r.Status == status)
            .OrderByDescending(r => r.lastMessageAt).ToList();
        if (listTitle) listTitle.text = DashboardStatusInfo.Label(status);
        SpawnRows(listRoot, rowTemplate, rows);
        listPanel.gameObject.SetActive(true);
        listPanel.SetAsLastSibling();
        listPanel.anchoredPosition = new Vector2(CanvasWidth(), listPanel.anchoredPosition.y);
        listPanel.DOAnchorPosX(0f, 0.3f).SetEase(DG.Tweening.Ease.OutCubic);
    }

    private void CloseStatusList()
    {
        if (listPanel == null) return;
        listPanel.DOAnchorPosX(CanvasWidth(), 0.25f).SetEase(DG.Tweening.Ease.InCubic)
            .OnComplete(() => listPanel.gameObject.SetActive(false));
    }

    private float CanvasWidth()
    {
        var c = GetComponentInParent<Canvas>();
        return c != null ? ((RectTransform)c.transform).rect.width : 1080f;
    }
```

- [ ] **Step 4: Add row spawning + deep-link** into the same class:

```csharp
    private void SpawnRows(Transform root, GameObject template, List<DashboardOutcome> rows)
    {
        if (root == null || template == null) return;
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            var c = root.GetChild(i).gameObject;
            if (c != template) Destroy(c);
        }
        var map = ProfileToBot();
        bool showBotTag = _botFilter == null && map.Count > 1;
        foreach (var r in rows)
        {
            var go = Instantiate(template, root);
            go.SetActive(true);
            BindRow(go, r, showBotTag, map);
        }
    }

    private void BindRow(GameObject go, DashboardOutcome r, bool showBotTag, Dictionary<string,string> map)
    {
        var name = go.transform.Find("Name")?.GetComponent<TextMeshProUGUI>();
        var summary = go.transform.Find("Summary")?.GetComponent<TextMeshProUGUI>();
        var pill = go.transform.Find("Pill")?.GetComponent<Image>();
        var pillLabel = go.transform.Find("Pill/Label")?.GetComponent<TextMeshProUGUI>();
        var botTag = go.transform.Find("BotTag")?.GetComponent<TextMeshProUGUI>();
        var avatar = go.transform.Find("Avatar")?.GetComponent<Image>();
        var avatarInitial = go.transform.Find("Avatar/Initial")?.GetComponent<TextMeshProUGUI>();

        string display = ChatDisplayName(r.chatId);
        if (name) name.text = display;
        if (summary) summary.text = r.summary;
        if (pill) pill.color = DashboardStatusInfo.BgColor(r.Status);
        if (pillLabel) { pillLabel.text = DashboardStatusInfo.Label(r.Status);
                         pillLabel.color = DashboardStatusInfo.FgColor(r.Status); }
        if (botTag) { botTag.gameObject.SetActive(showBotTag);
            if (showBotTag && map.TryGetValue(r.profileId, out var bn))
                botTag.text = PlayerPrefs.GetString(bn + "Name", bn); }
        ApplyAvatar(avatar, avatarInitial, r.chatId, display);

        var btn = go.GetComponent<Button>();
        if (btn) { btn.onClick.RemoveAllListeners(); btn.onClick.AddListener(() => OpenChat(r)); }
    }

    public void OpenChat(DashboardOutcome r)
    {
        string botName = SessionChatMap.ResolveBotName(ProfileToBot(), r.profileId);
        if (string.IsNullOrEmpty(botName) || ChatManager.Instance == null) return;

        if (ChatManager.Instance.CurrentBotId != botName)
            ChatManager.Instance.SetActiveBot(botName);

        var tabs = FindFirstObjectByType<BottomTabManager>();
        if (tabs != null) tabs.SwitchTab(BottomTabManager.WhatsAppTabIndex);

        // Deferred one frame so the WhatsApp tab's own sync/list settles; if the chat
        // isn't present we just land on that bot's list (no error popup).
        StartCoroutine(OpenChatDeferred(r.chatId));
    }

    private IEnumerator OpenChatDeferred(string chatId)
    {
        yield return null;
        ChatManager.Instance.SelectChat(chatId);
    }

    // Deterministic avatar (mirror ChatItemView) + display-name fallback.
    private static readonly string[][] AvatarColors = {
        new[]{"#CFE9E4","#00A884"}, new[]{"#D6E4FB","#1FA2FF"}, new[]{"#EADCF1","#A348D4"},
        new[]{"#FCE1D0","#F8942F"}, new[]{"#FCE2EC","#E14781"} };

    private void ApplyAvatar(Image bg, TextMeshProUGUI initial, string chatId, string display)
    {
        int hash = 0; foreach (char c in chatId ?? "") hash += c;
        var pair = AvatarColors[Mathf.Abs(hash) % AvatarColors.Length];
        if (bg && ColorUtility.TryParseHtmlString(pair[0], out var b)) bg.color = b;
        if (initial) {
            initial.text = string.IsNullOrEmpty(display) ? "?" : display.Substring(0, 1).ToUpper();
            if (ColorUtility.TryParseHtmlString(pair[1], out var f)) initial.color = f;
        }
    }

    private string ChatDisplayName(string chatId)
    {
        // Prefer the live chat-list title; fall back to the phone number from the id.
        if (ChatManager.Instance != null &&
            ChatManager.Instance.TryGetChatTitle(chatId, out var title) && !string.IsNullOrEmpty(title))
            return title;
        return WappiRecipient.FromChatId(chatId);   // strips @c.us → digits
    }
```

- [ ] **Step 5: Add the small `ChatManager` read helpers** the controller depends on. In `ChatManager` (a new partial or existing file), add public accessors if not present:

```csharp
    // exposes the current bot id for deep-link comparison
    // (CurrentBotId already exists as a public property — confirm; if not, add:)
    // public string CurrentBotId { get; private set; }

    public bool TryGetChatTitle(string chatId, out string title)
    {
        title = null;
        if (chatLookup != null && chatLookup.TryGetValue(chatId, out var vm) && vm != null)
        { title = vm.Title; return true; }
        return false;
    }
```

(Verify `CurrentBotId` is already public — it is used in `SetActiveBot`; if it is a field, expose a getter. `chatLookup` is the existing private dict in ChatManager.)

- [ ] **Step 6: Verify compile** (Assets→Refresh / headless). Fix any missing accessor (e.g. `CurrentBotId` visibility) surfaced by the compiler.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Main/Dashboard/DashboardPage.cs* Assets/Scripts/Main/ChatManager*.cs
git commit -m "feat(dashboard): DashboardPage controller — fetch, render, filters, drill-down, deep-link"
```

### Task C7: `DashboardPageBuilder` — build the Variant-B screen

**Files:**
- Create: `Assets/Editor/DashboardPageBuilder.cs`

**Interfaces:**
- Consumes: the verbatim builder helpers copied from `ProfileSubPagesBuilder` (`NewChild`, `SetAnchors`, `StretchFill`, `SetPreferredSize`, `AddText`, `AddIconImage`, `AddRounded`, `RefreshRounded`, `DestroyAllByName`, `Hex`, `LoadFont`, font-GUID + token consts, `EnsureIconImportSettings`), plus `Canvas.ForceUpdateCanvases()`+refresh ordering. Fills the `Screen_Dashboard` created in Task B5 and stamps every `[SerializeField]` on `DashboardPage` via `SerializedObject`.

- [ ] **Step 1: Write `DashboardPageBuilder.cs`** with `[MenuItem("Tools/Dashboard/Build")]` + `BuildHeadless()`. `BuildInternal()`:
  1. `AssetDatabase.Refresh()` → `EnsureIconImportSettings()` → `LoadAssets()` → clear rounded list.
  2. Find `Screen_Dashboard` (created in B5); `DestroyAllByName(screenDashboard, "DashContent")` and `"DashListPanel"` (idempotent).
  3. `AddComponent<DashboardPage>()` if absent; open one `SerializedObject so`.
  4. Build the scroll column (copy `BuildPanelShell`'s ScrollRect/Viewport/Content block, but no header/back — the header already exists on `Screen_Dashboard` from B5) into a `DashContent` root.
  5. Build, in order, into Content (all sizes in reference units per the type/spacing scale): **period segmented control** (3 buttons in a 12px-inset `#E4E6EB` track + a white highlight pill; height ~96); **chips row** (horizontal, hidden by controller when ≤1 bot; include one inactive template chip `chipPrefabHost`); **hero card** (white, radius 40, pad 44: caption «Заявки собраны» 30pt semibold uppercase muted; `heroCount` 72pt Bold ink + `heroDelta` pill; `heroSubtitle` 36pt muted; a `funnelBar` HorizontalLayoutGroup 12px tall with 5 `Image` segments (each with a `LayoutElement`, rounded ends) + a `legendRoot` grid of 5 rows); **status rows card** (5 rows, each a `Button` named by index with children `Dot`(Image 20×20 circle)/`Label`(42 medium)/`Count`(42 semibold tabular)/`Chev`(Image)); **section caption** «Последние заявки»; **recentRoot** (VLG) + an inactive `rowTemplate` conversation row (children: `Avatar`(140 circle)+`Avatar/Initial`, `Name` 44, `BotTag` 30 muted, `Summary` 36 muted, `Pill`(rounded)+`Pill/Label` 30); **loadingState** + **emptyState** (hero + «Бот пока не вёл диалогов»).
  6. Build the **drill-down `DashListPanel`** as a sibling of Content inside `Screen_Dashboard` using the full `BuildPanelShell` (header + back + swipe + scroll) — capture its `backButton`→`listBackButton`, title→`listTitle`, content→`listRoot`, panel→`listPanel`. Start inactive.
  7. `StampController(so, ...)`: stamp every `DashboardPage` `[SerializeField]` (todayButton/weekButton/monthButton, periodHighlight, chipsRow, chipPrefabHost, heroCount/heroDelta/heroSubtitle, funnelBar, legendRoot, statusRowsRoot, recentRoot, rowTemplate, loadingState, emptyState, listPanel, listBackButton, listTitle, listRoot) via `so.FindProperty(name).objectReferenceValue = ...`. One `so.ApplyModifiedPropertiesWithoutUndo()`.
  8. `Canvas.ForceUpdateCanvases()` then `RefreshRounded` over every collected rounded component (LAST).

- [ ] **Step 2: Run the builder** — Tools → Dashboard → Build (Editor open), Cmd+S.

- [ ] **Step 3: Verify in Game view at 1080×2400** — confirm the screen matches the Variant-B mockup: period control, hero number + delta + funnel bar + legend, 5 status rows, recent list, correct fonts/spacing/pill colors. Adjust token sizes if anything is off (this is visual work — gate on Game-view GREEN).

- [ ] **Step 4: Commit**

```bash
git add Assets/Editor/DashboardPageBuilder.cs* Assets/Scenes/Main.unity
git commit -m "feat(dashboard): DashboardPageBuilder — Variant B screen + drill-down panel"
```

### Task C8: End-to-end verification

- [ ] **Step 1: Full EditMode suite** — run `Tools/run-tests-headless.sh` (Editor closed) or the trigger bridge. Expected: all dashboard tests green + no regressions in the existing suite.

- [ ] **Step 2: Wire dev n8n + device/Editor run** — point the app at dev n8n (`apply-dev-config.py` + tunnel per `Tools/n8n/dev-tunnel.md`), seed a couple of bot conversations, and confirm on-device: «Сводка» loads outcomes, period/bot filters recompute, a status row opens its list, a row taps through to the chat (correct bot + chat), and empty/loading states behave. Report results.

- [ ] **Step 3: Update `CLAUDE.md`** — add the `DashboardOutcomes` webhook to the n8n **External APIs** section, the `conversation_outcomes` table to the Supabase/Architecture notes, and the `Assets/Scripts/Main/Dashboard/` folder + `Screen_Dashboard` tab (replacing «New») to **Architecture**. Commit.

```bash
git add CLAUDE.md
git commit -m "docs: dashboard (Сводка) tab, DashboardOutcomes webhook, conversation_outcomes"
```

---

## Self-Review notes (addressed inline)

- **Spec coverage:** nav restructure (B1–B6) ↔ spec §2; taxonomy (A2, C2) ↔ §3; server migration/table/workflow (A1–A4) ↔ §4; app screen/models/metrics/refresh/drill-down/deep-link (C1–C8) ↔ §5; WhatsApp-only + limitations ↔ §1/§6 (enforced in `AuthedProfiles`, `ProfileToBot`); testing ↔ §7 (C1–C4 TDD, A4 e2e, C8 device).
- **truncated handling** (spec §5.4) is implemented in `FetchRoutine` (recurse until false, cap 5).
- **Delta = partial-vs-partial** (spec §5.2) is pinned by `ComputeWindow(Today)` and its test.
- **Type consistency:** `OutcomeStatus`/`OutcomeStatusMap`/`DashboardStatusInfo.Ordered` indices are shared by `DashboardMetrics.StatusCounts` and `RenderFunnel`/`RenderStatusRows`; the webhook JSON keys (`profileId/chatId/outcome/summary/outcomeAt/lastMessageAt`) match `DashboardOutcome` fields exactly.
- **Verified accessors:** `ChatManager.CurrentBotId` is `public { get; private set; }` (ChatManager.BotState.cs:14); `chatLookup` is `private Dictionary<string,ChatViewModel>` in ChatManager.cs:44 (so `TryGetChatTitle` must live in a `ChatManager` partial); `ChatViewModel.Title` is `public string { get; }` (ChatViewModel.cs:7); `WappiRecipient.FromChatId` is `public static` (WappiRecipient.cs:12). **`Manager.BotsParent` is PRIVATE** — the controller uses the public `Manager.BotsRoot` (Transform) instead. `Bot.UnauthedProfileSentinel` is `public const "-1"`.

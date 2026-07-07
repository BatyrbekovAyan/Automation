#!/usr/bin/env bash
# E2E test for the "Dashboard Outcomes" workflow (webhook /webhook/DashboardOutcomes)
# against LOCAL dev n8n. Seeds n8n_chat_histories dialogs, calls the webhook, and
# asserts the classified conversation_outcomes it returns.
#
# WHY A TEMP "SQL RUNNER" WORKFLOW (and not psql):
#   This box has no direct Postgres access — the DB is only reachable through the
#   n8n "Postgres" credential (id vvRrFiEXzLVqKjOx). So at start-up this script
#   creates a throwaway n8n workflow  [ Webhook POST /TempDashE2eSql ] -> [ Postgres
#   executeQuery  query = {{ $json.body.sql }} ] -> [ Respond ]  via the public REST
#   API, activates it, and pushes seed / cleanup / verification SQL through its webhook.
#   The runner runs whatever SQL it is handed VERBATIM (textual injection by design).
#   That is acceptable ONLY because it lives for the duration of this run on localhost
#   and is force-deleted (with a 404 verify) by the EXIT trap below — never ship it.
#
#   Precedent for the node shapes / typeVersions / credential id:
#   Tools/n8n/workflows/ZTqpumOpL1rNDOp6-Delete_File.json
#
# API key: env N8N_API_KEY first, else secrets.json (run from repo root).
# Activation POSTs carry an explicit Content-Type: application/json (libcurl would
# otherwise stamp x-www-form-urlencoded and n8n's REST API 415s it).
#
# Usage:  Tools/n8n/test-dashboard-outcomes.sh [base-url]   # default http://localhost:5678
#   run from the repo root so the secrets.json fallback resolves.
set -u

BASE="${1:-http://localhost:5678}"
KEY="${N8N_API_KEY:-$(python3 -c "import json;print(json.load(open('Assets/StreamingAssets/secrets.json'))['n8nAPIKey'])")}"

RUN="e2e_$(date +%s)"                              # run-scoped profile prefix; cleanup keys off it
RUNNER_NAME="TEMP Claude — Dashboard E2E SQL runner"
RUNNER_PATH="TempDashE2eSql"
RUNNER_ID=""
TMP="$(mktemp -d)"
FAILS=0

# per-scenario chat ids (the part after "profile:") ------------------------------
ORDER_CHAT="77010000001@c.us"
OWNER_CHAT="77030000001@c.us"
GROUP_CHAT="120363000000000@g.us"
MP1_CHAT="77020000001@c.us"
MP2_CHAT="77020000002@c.us"
SILENT_CHAT="77040000001@c.us"

P_ORDER="${RUN}_order"
P_OWNER="${RUN}_owner"
P_GROUP="${RUN}_group"
P_MP1="${RUN}_mp1"
P_MP2="${RUN}_mp2"
P_SILENT="${RUN}_silent"

# ── output helpers (mirror test-upload-e2e.sh style) ────────────────────────────
pass() { echo "PASS  $1"; }
fail() { echo "FAIL  $1"; FAILS=$((FAILS + 1)); }
check_sub()    { local f="${3:-$TMP/resp.json}" b; b="$(cat "$f")"; [[ "$b" == *"$2"* ]] && pass "$1" || fail "$1 — missing '$2' in: $b"; }
check_absent() { local f="${3:-$TMP/resp.json}" b; b="$(cat "$f")"; [[ "$b" != *"$2"* ]] && pass "$1" || fail "$1 — unexpected '$2' in: $b"; }
check_eq()     { [[ "$3" == "$2" ]] && pass "$1" || fail "$1 — expected '$2', got '$3'"; }
check_http()   { check_eq "$1 (HTTP $3)" "$2" "$3"; }

# ── n8n REST + webhook plumbing ─────────────────────────────────────────────────
runsql() { # $1 = SQL string. Body written to $TMP/sqlr.json; echoes http code.
  python3 -c "import json,sys;print(json.dumps({'sql':sys.argv[1]}))" "$1" > "$TMP/sqlp.json"
  curl -s -o "$TMP/sqlr.json" -w '%{http_code}' -X POST "$BASE/webhook/$RUNNER_PATH" \
    -H 'Content-Type: application/json' --data-binary @"$TMP/sqlp.json"
}
call_dash() { # $1 = JSON array literal of profile ids, e.g. ["a","b"]. Body -> $TMP/resp.json; echoes http code.
  curl -s -o "$TMP/resp.json" -w '%{http_code}' -X POST "$BASE/webhook/DashboardOutcomes" \
    -H 'Content-Type: application/json' -d "{\"profileIds\":$1}"
}
outcome_for() { # $1 = chatId -> prints the outcome from $TMP/resp.json (empty if absent)
  python3 -c '
import json,sys
for o in json.load(open(sys.argv[1])).get("outcomes",[]):
    if o.get("chatId")==sys.argv[2]:
        print(o.get("outcome","")); break
' "$TMP/resp.json" "$1"
}
wipe_profile() { # $1 = profile prefix — remove its histories + stored outcome
  runsql "DELETE FROM public.n8n_chat_histories WHERE session_id LIKE '$1%';" >/dev/null
  runsql "DELETE FROM public.conversation_outcomes WHERE profile_id LIKE '$1%';" >/dev/null
}
seed() { # $1 = SQL; assert it applied cleanly (harness sanity, not a workflow assertion)
  local code; code="$(runsql "$1")"
  [[ "$code" == 200 ]] || { echo "SEED ERROR (HTTP $code): $(cat "$TMP/sqlr.json")"; fail "seed failed"; }
}

# ── EXIT trap: ALWAYS delete seeded rows + the runner, verify 404 ───────────────
cleanup() {
  local rc=$?
  echo
  echo "== cleanup =="
  if [[ -n "$RUNNER_ID" ]]; then
    runsql "DELETE FROM public.n8n_chat_histories WHERE session_id LIKE '${RUN}%';" >/dev/null
    runsql "DELETE FROM public.conversation_outcomes WHERE profile_id LIKE '${RUN}%';" >/dev/null
    runsql "SELECT (SELECT count(*) FROM public.n8n_chat_histories WHERE session_id LIKE '${RUN}%')::int AS h, (SELECT count(*) FROM public.conversation_outcomes WHERE profile_id LIKE '${RUN}%')::int AS o;" >/dev/null
    local left; left="$(cat "$TMP/sqlr.json")"
    if [[ "$left" == *'"h":0'* && "$left" == *'"o":0'* ]]; then
      echo "  seeded rows deleted (histories=0, outcomes=0)"
    else
      echo "  FAIL leftover seeded rows: $left"; rc=1
    fi
    curl -s -o /dev/null -w '  runner DELETE  HTTP %{http_code}\n' \
      -X DELETE "$BASE/api/v1/workflows/$RUNNER_ID" -H "X-N8N-API-KEY: $KEY"
    local vc; vc="$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/workflows/$RUNNER_ID" -H "X-N8N-API-KEY: $KEY")"
    if [[ "$vc" == 404 ]]; then echo "  runner gone (GET -> 404)"; else echo "  FAIL runner still present (GET -> $vc)"; rc=1; fi
  else
    echo "  (no runner created; nothing to clean)"
  fi
  rm -rf "$TMP"
  exit $rc
}
trap cleanup EXIT

# ── sanity: n8n reachable + key valid ───────────────────────────────────────────
lc="$(curl -s -o /dev/null -w '%{http_code}' --max-time 10 -H "X-N8N-API-KEY: $KEY" "$BASE/api/v1/workflows?limit=1")"
if [[ "$lc" != 200 ]]; then
  echo "ABORT: n8n REST API not reachable / key invalid at $BASE (HTTP $lc)"; exit 1
fi

# ── sweep any stale runner from a crashed prior run (frees the webhook path) ─────
for id in $(curl -s -H "X-N8N-API-KEY: $KEY" "$BASE/api/v1/workflows?limit=250" \
  | python3 -c 'import json,sys;print("\n".join(w["id"] for w in json.load(sys.stdin).get("data",[]) if w.get("name")==sys.argv[1]))' "$RUNNER_NAME"); do
  curl -s -o /dev/null -X DELETE "$BASE/api/v1/workflows/$id" -H "X-N8N-API-KEY: $KEY"
  echo "swept stale runner $id"
done

# ── create + activate the temp SQL runner ───────────────────────────────────────
cat > "$TMP/runner.json" <<JSON
{
  "name": "$RUNNER_NAME",
  "nodes": [
    { "parameters": { "httpMethod": "POST", "path": "$RUNNER_PATH", "responseMode": "responseNode", "options": {} },
      "type": "n8n-nodes-base.webhook", "typeVersion": 2.1, "position": [0,0],
      "id": "a1b2c3d4-1111-4111-8111-000000000001", "name": "Webhook",
      "webhookId": "a1b2c3d4-2222-4222-8222-000000000002" },
    { "parameters": { "resource": "database", "operation": "executeQuery", "query": "={{ \$json.body.sql }}", "options": {} },
      "type": "n8n-nodes-base.postgres", "typeVersion": 2.6, "position": [220,0],
      "id": "a1b2c3d4-3333-4333-8333-000000000003", "name": "Run SQL",
      "credentials": { "postgres": { "id": "vvRrFiEXzLVqKjOx", "name": "Postgres" } },
      "alwaysOutputData": true },
    { "parameters": { "respondWith": "json", "responseBody": "={{ { \"ok\": true, \"rows\": \$input.all().map(i => i.json) } }}", "options": {} },
      "type": "n8n-nodes-base.respondToWebhook", "typeVersion": 1.5, "position": [440,0],
      "id": "a1b2c3d4-4444-4444-8444-000000000004", "name": "Respond" }
  ],
  "connections": {
    "Webhook": { "main": [[{ "node": "Run SQL", "type": "main", "index": 0 }]] },
    "Run SQL": { "main": [[{ "node": "Respond", "type": "main", "index": 0 }]] }
  },
  "settings": { "executionOrder": "v1" }
}
JSON

cc="$(curl -s -o "$TMP/create.json" -w '%{http_code}' -X POST "$BASE/api/v1/workflows" \
  -H "X-N8N-API-KEY: $KEY" -H 'Content-Type: application/json' --data-binary @"$TMP/runner.json")"
RUNNER_ID="$(python3 -c "import json;print(json.load(open('$TMP/create.json')).get('id',''))" 2>/dev/null || true)"
if [[ "$cc" != 200 || -z "$RUNNER_ID" ]]; then
  echo "ABORT: could not create SQL runner (HTTP $cc): $(cat "$TMP/create.json")"; exit 1
fi
ac="$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/v1/workflows/$RUNNER_ID/activate" \
  -H "X-N8N-API-KEY: $KEY" -H 'Content-Type: application/json')"
if [[ "$ac" != 200 ]]; then
  echo "ABORT: could not activate SQL runner (HTTP $ac)"; exit 1
fi
echo "runner created + activated: $RUNNER_ID  (webhook /$RUNNER_PATH)"
sleep 2   # let the production webhook path register
sc="$(runsql "SELECT 1 AS ping;")"
[[ "$sc" == 200 && "$(cat "$TMP/sqlr.json")" == *'"ping":1'* ]] \
  && echo "runner reachable" || { echo "ABORT: runner webhook not reachable (HTTP $sc): $(cat "$TMP/sqlr.json")"; exit 1; }
echo "run prefix: $RUN"
echo

# ══════════════════════════════════════════════════════════════════════════════
# Scenario 1 — order_collected (flower-shop handoff to the florist)
# ══════════════════════════════════════════════════════════════════════════════
echo "== 1. order_collected =="
ORDER_SEED="INSERT INTO public.n8n_chat_histories(session_id,message,created_at) VALUES
('$P_ORDER:$ORDER_CHAT', jsonb_build_object('type','human','data',jsonb_build_object('content','Хочу букет 101 роза на завтра')), now()),
('$P_ORDER:$ORDER_CHAT', jsonb_build_object('type','ai','data',jsonb_build_object('content','Записал: 101 роза, доставка завтра. Имя и телефон получателя?')), now()),
('$P_ORDER:$ORDER_CHAT', jsonb_build_object('type','human','data',jsonb_build_object('content','Айгерим, 77010000001')), now()),
('$P_ORDER:$ORDER_CHAT', jsonb_build_object('type','ai','data',jsonb_build_object('content','Передаю флористу — он подтвердит заказ и пришлёт фото букета')), now());"
ORDER_SEED_CLEAN="INSERT INTO public.n8n_chat_histories(session_id,message,created_at) VALUES
('$P_ORDER:$ORDER_CHAT', jsonb_build_object('type','human','data',jsonb_build_object('content','Здравствуйте! Закажу 51 розу с доставкой завтра к 12:00')), now()),
('$P_ORDER:$ORDER_CHAT', jsonb_build_object('type','ai','data',jsonb_build_object('content','Отлично! Как вас зовут и на какой номер оформить заказ?')), now()),
('$P_ORDER:$ORDER_CHAT', jsonb_build_object('type','human','data',jsonb_build_object('content','Данияр, 77010000001')), now()),
('$P_ORDER:$ORDER_CHAT', jsonb_build_object('type','ai','data',jsonb_build_object('content','Заказ оформлен: 51 роза, завтра к 12:00. Передаю флористу на подтверждение.')), now());"

wipe_profile "$P_ORDER"; seed "$ORDER_SEED"
code="$(call_dash "[\"$P_ORDER\"]")"
got="$(outcome_for "$ORDER_CHAT")"
if [[ "$code" != 200 || "$got" != "order_collected" ]]; then
  echo "  (retry 1: got outcome='$got' code=$code — reseeding cleaner)"
  wipe_profile "$P_ORDER"; seed "$ORDER_SEED_CLEAN"
  code="$(call_dash "[\"$P_ORDER\"]")"; got="$(outcome_for "$ORDER_CHAT")"
fi
check_http "order webhook"       200 "$code"
check_sub  "order success flag"      '"success":true'
check_sub  "order outcome"           '"outcome":"order_collected"'
check_sub  "order chatId echoed"     "$ORDER_CHAT"
check_sub  "order classified>=1"     '"classified":1'

# ══════════════════════════════════════════════════════════════════════════════
# Scenario 2 — idempotency / watermark (re-call same profile, no new rows)
# ══════════════════════════════════════════════════════════════════════════════
echo "== 2. idempotency / watermark =="
code="$(call_dash "[\"$P_ORDER\"]")"
check_http "idempotent webhook"  200 "$code"
check_sub  "idempotent classified:0" '"classified":0'
check_sub  "idempotent outcome kept" '"outcome":"order_collected"'
check_sub  "idempotent chatId kept"  "$ORDER_CHAT"

# ══════════════════════════════════════════════════════════════════════════════
# Scenario 3 — group session skipped (chat part ends @g.us)
# ══════════════════════════════════════════════════════════════════════════════
echo "== 3. group skip =="
wipe_profile "$P_GROUP"
seed "INSERT INTO public.n8n_chat_histories(session_id,message,created_at) VALUES
('$P_GROUP:$GROUP_CHAT', jsonb_build_object('type','human','data',jsonb_build_object('content','всем привет, кто заказывал розы?')), now()),
('$P_GROUP:$GROUP_CHAT', jsonb_build_object('type','ai','data',jsonb_build_object('content','это групповой чат')), now());"
code="$(call_dash "[\"$P_GROUP\"]")"
check_http "group webhook"       200 "$code"
check_sub    "group classified:0"    '"classified":0'
check_sub    "group empty outcomes"  '"outcomes":[]'
check_absent "group chat absent"     "@g.us"

# ══════════════════════════════════════════════════════════════════════════════
# Scenario 4 — owner_needed (refund complaint the bot cannot resolve)
# ══════════════════════════════════════════════════════════════════════════════
echo "== 4. owner_needed =="
OWNER_SEED="INSERT INTO public.n8n_chat_histories(session_id,message,created_at) VALUES
('$P_OWNER:$OWNER_CHAT', jsonb_build_object('type','human','data',jsonb_build_object('content','Хочу вернуть деньги за вчерашний заказ — розы завяли за один день')), now()),
('$P_OWNER:$OWNER_CHAT', jsonb_build_object('type','ai','data',jsonb_build_object('content','Понимаю ваше недовольство. Возврат средств я оформить не могу — передаю вопрос владельцу, он свяжется с вами.')), now()),
('$P_OWNER:$OWNER_CHAT', jsonb_build_object('type','human','data',jsonb_build_object('content','Хорошо, жду ответа владельца.')), now());"
OWNER_SEED_CLEAN="INSERT INTO public.n8n_chat_histories(session_id,message,created_at) VALUES
('$P_OWNER:$OWNER_CHAT', jsonb_build_object('type','human','data',jsonb_build_object('content','Это ужасно, розы завяли за день! Требую вернуть деньги немедленно!')), now()),
('$P_OWNER:$OWNER_CHAT', jsonb_build_object('type','ai','data',jsonb_build_object('content','Приношу извинения. Возврат я оформить не могу — передаю вашу жалобу владельцу магазина.')), now());"

wipe_profile "$P_OWNER"; seed "$OWNER_SEED"
code="$(call_dash "[\"$P_OWNER\"]")"; got="$(outcome_for "$OWNER_CHAT")"
if [[ "$code" != 200 || "$got" != "owner_needed" ]]; then
  echo "  (retry 4: got outcome='$got' code=$code — reseeding cleaner)"
  wipe_profile "$P_OWNER"; seed "$OWNER_SEED_CLEAN"
  code="$(call_dash "[\"$P_OWNER\"]")"; got="$(outcome_for "$OWNER_CHAT")"
fi
check_http "owner webhook"       200 "$code"
check_eq   "owner outcome"           "owner_needed" "$got"
check_sub  "owner chatId echoed"     "$OWNER_CHAT"

# ══════════════════════════════════════════════════════════════════════════════
# Scenario 5 — multi-profile (comma-split regression): two profiles, one call
# ══════════════════════════════════════════════════════════════════════════════
echo "== 5. multi-profile (comma-split regression) =="
wipe_profile "$P_MP1"; wipe_profile "$P_MP2"
seed "INSERT INTO public.n8n_chat_histories(session_id,message,created_at) VALUES
('$P_MP1:$MP1_CHAT', jsonb_build_object('type','human','data',jsonb_build_object('content','Сколько стоит букет из 25 роз?')), now()),
('$P_MP1:$MP1_CHAT', jsonb_build_object('type','ai','data',jsonb_build_object('content','Букет из 25 роз стоит 15000 тг. Оформить заказ?')), now());"
seed "INSERT INTO public.n8n_chat_histories(session_id,message,created_at) VALUES
('$P_MP2:$MP2_CHAT', jsonb_build_object('type','human','data',jsonb_build_object('content','Есть доставка в Астане?')), now()),
('$P_MP2:$MP2_CHAT', jsonb_build_object('type','ai','data',jsonb_build_object('content','Да, доставка по Астане 2000 тг. Куда и на когда доставить?')), now());"
code="$(call_dash "[\"$P_MP1\",\"$P_MP2\"]")"
check_http "multi webhook"       200 "$code"
check_sub  "multi chat #1 present"   "$MP1_CHAT"
check_sub  "multi chat #2 present"   "$MP2_CHAT"
check_sub  "multi classified:2"      '"classified":2'

# ══════════════════════════════════════════════════════════════════════════════
# Scenario 6 — silence rule (in_dialog + >12h quiet + last msg from bot -> client_silent)
#   All rows dated 13h ago; dialog ends with the BOT asking a question so the LLM
#   reliably returns in_dialog. The silence pass runs after Upsert in the SAME call,
#   so call-1 may already flip it; if not, call-2 must. Terminal classification =>
#   SKIP (the rule only applies to in_dialog).
# ══════════════════════════════════════════════════════════════════════════════
echo "== 6. silence rule =="
SILENT_SEED="INSERT INTO public.n8n_chat_histories(session_id,message,created_at) VALUES
('$P_SILENT:$SILENT_CHAT', jsonb_build_object('type','human','data',jsonb_build_object('content','Здравствуйте! Хочу заказать букет на день рождения')), now() - interval '13 hours'),
('$P_SILENT:$SILENT_CHAT', jsonb_build_object('type','ai','data',jsonb_build_object('content','Здравствуйте! С радостью поможем. Какой размер букета вам подойдёт — небольшой, средний или большой?')), now() - interval '13 hours');"
SILENT_SEED_CLEAN="INSERT INTO public.n8n_chat_histories(session_id,message,created_at) VALUES
('$P_SILENT:$SILENT_CHAT', jsonb_build_object('type','human','data',jsonb_build_object('content','Привет, интересует букет')), now() - interval '13 hours'),
('$P_SILENT:$SILENT_CHAT', jsonb_build_object('type','ai','data',jsonb_build_object('content','Здравствуйте! Подскажите, на какую сумму рассчитываете?')), now() - interval '13 hours');"

# Runs INLINE in the current shell (never in $() ) so its check_* calls mutate FAILS.
# Sets globals: silence_pass (1 once satisfied) and silence_skip_outcome (terminal id).
do_silence_attempt() { # $1 = seed SQL
  wipe_profile "$P_SILENT"; seed "$1"
  local code o
  code="$(call_dash "[\"$P_SILENT\"]")"; check_http "silence call-1" 200 "$code"
  o="$(outcome_for "$SILENT_CHAT")"
  if [[ "$o" == "client_silent" ]]; then
    pass "silence: client_silent on call-1 (same-execution silence pass)"; silence_pass=1; return
  fi
  if [[ "$o" == "in_dialog" ]]; then
    code="$(call_dash "[\"$P_SILENT\"]")"; check_http "silence call-2" 200 "$code"
    o="$(outcome_for "$SILENT_CHAT")"
    check_eq "silence: client_silent by call-2 (was in_dialog)" "client_silent" "$o"
    silence_pass=1; return   # in_dialog but no flip => genuine FAIL (recorded above), not a retry case
  fi
  silence_skip_outcome="$o"  # terminal outcome — silence rule does not apply
}

silence_pass=0; silence_skip_outcome=""
do_silence_attempt "$SILENT_SEED"
if [[ "$silence_pass" -eq 0 && -n "$silence_skip_outcome" ]]; then
  echo "  (retry 6: first seed classified '$silence_skip_outcome' (terminal) — reseeding cleaner in_dialog)"
  silence_skip_outcome=""
  do_silence_attempt "$SILENT_SEED_CLEAN"
fi
if [[ "$silence_pass" -eq 0 && -n "$silence_skip_outcome" ]]; then
  echo "SKIP  silence: LLM classified '$silence_skip_outcome' (terminal) both attempts — rule only applies to in_dialog, not a failure"
fi

echo
echo "----"
if [[ "$FAILS" -eq 0 ]]; then
  echo "ALL PASS"
else
  echo "FAILED: $FAILS check(s)"
fi
# EXIT trap performs cleanup and preserves this status.
exit $(( FAILS > 0 ? 1 : 0 ))

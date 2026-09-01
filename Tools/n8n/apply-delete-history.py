#!/usr/bin/env python3
"""Extend Delete Bot Files with server-side conversation-memory deletion (2026-09-01).

Store-audit / Data Safety fix: «Удалить все данные» wiped local PlayerPrefs + RAG
chunks + stored originals + bot_profiles, but the SERVER kept the deleted bot's chat
transcripts (n8n_chat_histories, session_id = '<profileId>:<chatId>'), dashboard
classifications (conversation_outcomes) and per-chat reply-mode flags
(reply_mode_flags) forever -- contradicting the privacy policy and the planned
Play Data Safety «можно запросить удаление» answer.

Splices ONE Postgres node «Delete Chat Memory» into the canonical
workflows/lmjYsdNcQA2IE5rl-Delete_Bot_Files.json between «Delete Bot Chunks» and
«Respond» (idempotent, node-by-name -- the apply-*.py idiom):

  Webhook -> Retire Bot Profiles -> Delete Bot Chunks -> Delete Chat Memory -> Respond
                                                          (onError: continue -- a
  memory-wipe failure must not fail the response; retire/chunks already committed)

deletion keys off waProfileId/tgProfileId with the same sentinel guards the chunks
delete uses; dialog_counts stay UNTOUCHED on purpose (billing usage records move
with the money -- Task 17a -- and are not chat content). Respond gains
deletedMemoryRows/deletedOutcomes/deletedReplyFlags.

Usage:
  python3 Tools/n8n/apply-delete-history.py --check    # diff canonical, no writes
  python3 Tools/n8n/apply-delete-history.py --deploy   # mutate canonical + PUT to target
  python3 Tools/n8n/apply-delete-history.py --probe    # E2E on target: seed fixtures ->
                                                       #   webhook -> assert counts + survivors
Target: N8N_BASE_URL (default https://n8n.choosereply.com — prod-first, like
build-client-webhooks.py). Key: N8N_API_KEY -> .secrets/prod-api-key.txt -> legacy
secrets.json. The probe seeds ONLY ZZPROBE-prefixed fixture rows and cleans up after
itself (EphemeralSqlHarness — random-path temp workflow, deleted in finally).
"""
import argparse
import json
import os
import sys
import time
import urllib.error
import urllib.request
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
BASE = os.environ.get("N8N_BASE_URL", "https://n8n.choosereply.com").rstrip("/")
CANONICAL = os.path.join(HERE, "workflows", "lmjYsdNcQA2IE5rl-Delete_Bot_Files.json")
WORKFLOW_ID = "lmjYsdNcQA2IE5rl"

NODE_NAME = "Delete Chat Memory"

MEMORY_SQL = """-- Privacy deletion (2026-09-01, store audit / Data Safety «можно запросить удаление»):
-- a deleted bot's server-side conversation memory goes with it. Chat transcripts key
-- off session_id = '<profileId>:<chatId>' (both bot templates' Chat Memory nodes),
-- conversation_outcomes and reply_mode_flags carry profile_id directly. Sentinels
-- '-1'/'' (channel never authed) are excluded like Delete Bot Chunks guards its ids;
-- 'null'/'undefined' cover a missing body field stringified by queryReplacement.
-- dialog_counts stay UNTOUCHED on purpose: they are the owner's billing usage records
-- (Task 17a moves them with the money on transfer), not chat content.
WITH ids AS (
  SELECT unnest(ARRAY[$1, $2]) AS profile_id
), valid AS (
  SELECT profile_id FROM ids
  WHERE profile_id IS NOT NULL AND profile_id NOT IN ('-1', '', 'null', 'undefined')
), hist AS (
  DELETE FROM public.n8n_chat_histories h
  WHERE split_part(h.session_id, ':', 1) IN (SELECT profile_id FROM valid)
  RETURNING 1
), outcomes AS (
  DELETE FROM public.conversation_outcomes c
  WHERE c.profile_id IN (SELECT profile_id FROM valid)
  RETURNING 1
), flags AS (
  DELETE FROM public.reply_mode_flags f
  WHERE f.profile_id IN (SELECT profile_id FROM valid)
  RETURNING 1
)
SELECT (SELECT count(*)::int FROM hist)     AS "deletedMemoryRows",
       (SELECT count(*)::int FROM outcomes) AS "deletedOutcomes",
       (SELECT count(*)::int FROM flags)    AS "deletedReplyFlags";"""

# `|| '-1'` is load-bearing: queryReplacement comma-splits the rendered string, and an
# EMPTY rendered value (body field '' or null) drops the parameter entirely -- Postgres
# then errors «no parameter $2» and onError turns the whole wipe into nulls. Coalescing
# to the '-1' sentinel keeps both params present; the SQL guard already excludes it.
QUERY_REPLACEMENT = ("={{ $('Webhook').first().json.body.waProfileId || '-1' }},"
                     "{{ $('Webhook').first().json.body.tgProfileId || '-1' }}")

RESPOND_BODY = (
    "={{ { \"success\": true, "
    "\"deletedChunks\": $('Delete Bot Chunks').first().json.deletedChunks, "
    "\"deletedFiles\": ($('Delete Bot Chunks').first().json.fileIds || []).length, "
    "\"retiredProfiles\": $('Retire Bot Profiles').first().json.retiredProfiles ?? null, "
    "\"deletedMemoryRows\": $json.deletedMemoryRows ?? null, "
    "\"deletedOutcomes\": $json.deletedOutcomes ?? null, "
    "\"deletedReplyFlags\": $json.deletedReplyFlags ?? null } }}"
)


def api_key():
    k = os.environ.get("N8N_API_KEY")
    if k:
        return k.strip()
    prod = os.path.join(HERE, ".secrets", "prod-api-key.txt")
    if "choosereply.com" in BASE and os.path.exists(prod):
        with open(prod) as f:
            return f.read().strip()
    secrets = os.path.join(REPO, "Assets/StreamingAssets/secrets.json")
    if os.path.exists(secrets):
        legacy = json.load(open(secrets)).get("n8nAPIKey")
        if legacy:
            return legacy
    sys.exit("no n8n API key: set N8N_API_KEY or provide .secrets/prod-api-key.txt")


def api(method, path, body=None, timeout=30):
    req = urllib.request.Request(f"{BASE}/api/v1{path}", method=method,
                                 headers={"X-N8N-API-KEY": api_key()})
    data = json.dumps(body).encode() if body is not None else None
    if data:
        req.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(req, data=data, timeout=timeout) as resp:
            return resp.status, resp.read().decode(errors="replace")
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode(errors="replace")


def node_by_name(wf, name):
    for n in wf["nodes"]:
        if n["name"] == name:
            return n
    return None


def mutate(wf):
    """Idempotent: returns True when anything changed."""
    chunks = node_by_name(wf, "Delete Bot Chunks")
    respond = node_by_name(wf, "Respond")
    if chunks is None or respond is None:
        sys.exit("canonical shape drifted: Delete Bot Chunks / Respond not found")

    changed = False

    memory = node_by_name(wf, NODE_NAME)
    if memory is None:
        memory = {
            "id": uuid.uuid4().hex[:16],
            "name": NODE_NAME,
            "type": "n8n-nodes-base.postgres",
            "typeVersion": chunks.get("typeVersion", 2.6),
            "position": [chunks["position"][0] + 220, chunks["position"][1]],
            "onError": "continueRegularOutput",
            "parameters": {},
            "credentials": json.loads(json.dumps(chunks.get("credentials", {}))),
        }
        wf["nodes"].append(memory)
        changed = True

    desired_params = {
        "resource": "database",
        "operation": "executeQuery",
        "query": MEMORY_SQL,
        "options": {"queryReplacement": QUERY_REPLACEMENT},
    }
    if memory.get("parameters") != desired_params or memory.get("onError") != "continueRegularOutput":
        memory["parameters"] = desired_params
        memory["onError"] = "continueRegularOutput"
        changed = True

    conns = wf["connections"]
    chunks_out = [[{"node": NODE_NAME, "type": "main", "index": 0}]]
    if conns.get("Delete Bot Chunks", {}).get("main") != chunks_out:
        conns["Delete Bot Chunks"] = {"main": chunks_out}
        changed = True
    memory_out = [[{"node": "Respond", "type": "main", "index": 0}]]
    if conns.get(NODE_NAME, {}).get("main") != memory_out:
        conns[NODE_NAME] = {"main": memory_out}
        changed = True

    if respond["parameters"].get("responseBody") != RESPOND_BODY:
        respond["parameters"]["responseBody"] = RESPOND_BODY
        changed = True

    return changed


def deploy():
    wf = json.load(open(CANONICAL))
    changed = mutate(wf)
    with open(CANONICAL, "w") as f:
        json.dump(wf, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print(f"canonical {'updated' if changed else 'already current'}: {CANONICAL}")

    payload = {k: wf[k] for k in ("name", "nodes", "connections", "settings")}
    status, body = api("PUT", f"/workflows/{WORKFLOW_ID}", payload)
    if status != 200:
        sys.exit(f"PUT failed [{status}]: {body[:400]}")
    print(f"PUT ok -> {BASE} ({WORKFLOW_ID})")


def check():
    wf = json.load(open(CANONICAL))
    print("would change canonical" if mutate(wf) else "canonical already current")


# ---------------------------------------------------------------- probe

class EphemeralSqlHarness:
    """probe-billing.py's pattern verbatim: Postgres is reachable only through n8n's
    credential, and an unauthenticated SQL endpoint must never stay up — the harness
    lives only inside the `with` block (random unguessable path, deleted in finally)."""

    def __init__(self):
        self.path = uuid.uuid4().hex + uuid.uuid4().hex
        self.workflow_id = None

    def __enter__(self):
        wf = {
            "name": "ZZZ TEMP delete-history probe harness (auto-delete)",
            "nodes": [
                {"id": "w1", "name": "Hook", "type": "n8n-nodes-base.webhook",
                 "typeVersion": 2, "position": [0, 0],
                 "parameters": {"httpMethod": "POST", "path": self.path,
                                "responseMode": "lastNode", "options": {}}},
                {"id": "p1", "name": "Q", "type": "n8n-nodes-base.postgres",
                 "typeVersion": 2.6, "position": [220, 0],
                 "parameters": {"operation": "executeQuery",
                                "query": "={{ $json.body.sql }}", "options": {}},
                 "credentials": {"postgres": {"id": "vvRrFiEXzLVqKjOx",
                                              "name": "Postgres"}}},
            ],
            "connections": {"Hook": {"main": [[{"node": "Q", "type": "main", "index": 0}]]}},
            "settings": {"executionOrder": "v1"},
        }
        status, body = api("POST", "/workflows", wf)
        if status not in (200, 201):
            raise RuntimeError(f"harness create failed [{status}] {body[:300]}")
        self.workflow_id = json.loads(body)["id"]
        status, body = api("POST", f"/workflows/{self.workflow_id}/activate", {})
        if status not in (200, 201):
            raise RuntimeError(f"harness activate failed [{status}] {body[:300]}")
        return self

    def __exit__(self, *exc):
        if self.workflow_id:
            status, _ = api("DELETE", f"/workflows/{self.workflow_id}")
            print(f"    [harness deleted -> HTTP {status}]")
        return False

    def sql(self, statements):
        payload = json.dumps({"sql": statements}).encode()
        for attempt in range(5):
            req = urllib.request.Request(f"{BASE}/webhook/{self.path}", data=payload,
                                         method="POST")
            req.add_header("Content-Type", "application/json")
            try:
                with urllib.request.urlopen(req, timeout=60) as resp:
                    return json.loads(resp.read().decode() or "{}")
            except urllib.error.HTTPError as e:
                if attempt == 4:
                    return {"error": f"HTTP {e.code} {e.read().decode(errors='replace')[:200]}"}
                time.sleep(1.5)
            except (urllib.error.URLError, ValueError) as e:
                if attempt == 4:
                    return {"error": str(e)}
                time.sleep(1.5)

    def scalar(self, select_sql, key):
        out = self.sql(select_sql)
        if isinstance(out, list):
            out = out[0] if out else {}
        return out.get(key)


def call_delete_webhook(wa, tg):
    body = json.dumps({"botWaId": "-1", "botTgId": "-1",
                       "waProfileId": wa, "tgProfileId": tg,
                       "appUserId": "zz-probe-delete-history"}).encode()
    req = urllib.request.Request(f"{BASE}/webhook/DeleteBotFiles", data=body, method="POST")
    req.add_header("Content-Type", "application/json")
    with urllib.request.urlopen(req, timeout=60) as resp:
        return json.loads(resp.read().decode())


def probe():
    wa = f"ZZPROBE-WA-{uuid.uuid4().hex[:8]}"
    tg = f"ZZPROBE-TG-{uuid.uuid4().hex[:8]}"
    control = f"ZZPROBE-CTRL-{uuid.uuid4().hex[:8]}"
    failures = []

    def check(label, ok, detail=""):
        print(f"  {'PASS' if ok else 'FAIL'}  {label}{(' -- ' + str(detail)) if detail and not ok else ''}")
        if not ok:
            failures.append(label)

    with EphemeralSqlHarness() as h:
        try:
            h.sql(f"""
                INSERT INTO public.n8n_chat_histories (session_id, message)
                VALUES ('{wa}:77001', '{{"type":"human","content":"probe"}}'::jsonb),
                       ('{wa}:77002', '{{"type":"ai","content":"probe"}}'::jsonb),
                       ('{tg}:88001', '{{"type":"human","content":"probe"}}'::jsonb),
                       ('{control}:99001', '{{"type":"human","content":"probe"}}'::jsonb);
                INSERT INTO public.conversation_outcomes
                    (session_id, profile_id, chat_id, outcome, summary,
                     last_history_id, last_message_at, outcome_at, updated_at)
                VALUES ('{wa}:77001', '{wa}', '77001', 'in_dialog', 'probe', 1, now(), now(), now()),
                       ('{tg}:88001', '{tg}', '88001', 'in_dialog', 'probe', 1, now(), now(), now()),
                       ('{control}:99001', '{control}', '99001', 'in_dialog', 'probe', 1, now(), now(), now());
                INSERT INTO public.reply_mode_flags (profile_id, chat_id, suppressed)
                VALUES ('{wa}', '*', true), ('{tg}', '*', false), ('{control}', '*', true);
                SELECT 1 AS ok;""")

            # 1) Sentinel call must delete NOTHING (both ids are sentinels).
            sentinel = call_delete_webhook("-1", "")
            check("sentinel call deletes nothing",
                  sentinel.get("deletedMemoryRows") == 0 and sentinel.get("deletedOutcomes") == 0
                  and sentinel.get("deletedReplyFlags") == 0, sentinel)

            # 2) Real call wipes exactly the two fixture profiles.
            resp = call_delete_webhook(wa, tg)
            check("success true", resp.get("success") is True, resp)
            check("deletedMemoryRows == 3", resp.get("deletedMemoryRows") == 3, resp)
            check("deletedOutcomes == 2", resp.get("deletedOutcomes") == 2, resp)
            check("deletedReplyFlags == 2", resp.get("deletedReplyFlags") == 2, resp)

            left = h.scalar(
                f"SELECT count(*)::int AS n FROM public.n8n_chat_histories "
                f"WHERE session_id LIKE 'ZZPROBE-%' AND session_id NOT LIKE '{control}%';", "n")
            check("fixture history rows gone", left == 0, left)
            ctrl = h.scalar(
                "SELECT (SELECT count(*) FROM public.n8n_chat_histories WHERE session_id LIKE '"
                + control + "%')::int + (SELECT count(*) FROM public.conversation_outcomes "
                "WHERE profile_id = '" + control + "')::int + (SELECT count(*) FROM "
                "public.reply_mode_flags WHERE profile_id = '" + control + "')::int AS n;", "n")
            check("control profile survives (3 rows)", ctrl == 3, ctrl)
        finally:
            h.sql(f"""
                DELETE FROM public.n8n_chat_histories WHERE session_id LIKE 'ZZPROBE-%';
                DELETE FROM public.conversation_outcomes WHERE profile_id LIKE 'ZZPROBE-%';
                DELETE FROM public.reply_mode_flags WHERE profile_id LIKE 'ZZPROBE-%';
                SELECT 1 AS cleaned;""")
            print("    [fixtures cleaned]")

    if failures:
        sys.exit(f"PROBE FAILED: {failures}")
    print("PROBE GREEN")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--deploy", action="store_true")
    ap.add_argument("--probe", action="store_true")
    args = ap.parse_args()
    if args.check:
        check()
    elif args.deploy:
        deploy()
    elif args.probe:
        probe()
    else:
        ap.print_help()


if __name__ == "__main__":
    main()

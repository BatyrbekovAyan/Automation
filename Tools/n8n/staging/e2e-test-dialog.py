#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""STAGING scenarios for audit stage 7 (7.1 safe test dialog, 7.3 last-outcome status) — real n8n runs of
the Test Dialog workflow (reads the bot's REAL prompt through the admin API), the dry handoff tool, the
Handoffs `status` op, the private Postgres and the Wappi MOCK.
    python3 Tools/n8n/staging/e2e-test-dialog.py
    python3 Tools/n8n/staging/e2e-test-dialog.py --only forbidden
Scenarios tagged [llm] drive the REAL model (the reply's shape is the model's; the SIDE-EFFECT asserts — no send, no
memory, no dialog count, no ledger row, no real handoff — and the tested-version asserts are deterministic).
R15 (2026-09-27): two scenarios edit the staging WA bot's prompt into draft ≠ published / inactive and restore it.
"""
import argparse
import copy
import importlib.util
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from e2e import Ctx, fixture, http, mock, mock_total, workflow_id_by_name, BASE, KEY, WA_PROFILE  # noqa: E402

_spec = importlib.util.spec_from_file_location("e2e_auth", os.path.join(HERE, "e2e-auth.py"))
e2e_auth = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(e2e_auth)


def psql(sql):
    out = subprocess.run([os.path.join(HERE, "pg.sh"), "psql", "-tAc", sql], capture_output=True, text=True)
    if out.returncode != 0:
        raise RuntimeError(out.stderr.strip())
    return out.stdout.strip()


def count(sql):
    return int(psql(sql) or 0)


class Target:
    """The staging WA bot: its profile registered to this run's subscriber, OWNED by a fresh install whose
    token therefore carries the profile AND the bot workflow id (the Test Dialog gate needs both)."""

    def __init__(self, ctx, tag):
        self.pid = WA_PROFILE
        self.wf_id = workflow_id_by_name(f"STAGING WA {WA_PROFILE}")
        if not self.wf_id:
            raise RuntimeError("staging WA bot not imported")
        ctx.bot(self.pid, "whatsapp")
        self.owner = e2e_auth.Owner(tag, ctx.run_id)
        psql(f"update bot_profiles set owner_id = '{self.owner.install_id}', workflow_id = '{self.wf_id}' where profile_id = '{self.pid}';")
        self.owner.refresh()

    def snapshot(self):
        return {
            "sends": mock_total(),
            "memory": count(f"select count(*) from n8n_chat_histories where session_id like '{self.pid}:%';"),
            "dialogs": count(f"select count(*) from dialog_counts where app_user_id = '{self.owner.app_user_id}';"),
            "ledger": count(f"select count(*) from reply_events where profile_id = '{self.pid}';"),
            "handoffs": count(f"select count(*) from handoff_requests where profile_id = '{self.pid}';"),
            "history": count(f"select count(*) from chat_history_events where profile_id = '{self.pid}';"),
        }

    def test(self, text, history=None, token="owner", workflow_id=None, profile_id=None, channel="whatsapp"):
        tok = self.owner.token if token == "owner" else token
        body = {"profileId": profile_id or self.pid, "workflowId": workflow_id or self.wf_id, "channel": channel, "text": text}
        if history is not None:
            body["history"] = history
        return http("POST", f"{BASE}/webhook/TestDialog", body, headers={"Authorization": f"Bearer {tok}"} if tok else {}, timeout=90)


def rest(method, path, body=None):
    return http(method, f"{BASE}/api/v1{path}", body, headers={"X-N8N-API-KEY": KEY})


def agent_prompt(nodes):
    a = next((n for n in nodes or [] if n.get("name") == "AI Agent"), None)
    return (((a or {}).get("parameters") or {}).get("options") or {}).get("systemMessage", "")


def with_prompt(nodes, text):
    out = copy.deepcopy(nodes)
    next(n for n in out if n["name"] == "AI Agent")["parameters"]["options"]["systemMessage"] = text
    return out


class BotVersions:
    """R15: edits the staging WA bot's AI Agent prompt as a PUBLISHED version and/or an unpublished DRAFT (PUT with
    ?publishIfActive=false — what a Save-without-Publish in the n8n editor or an n8n-mcp update leaves behind), and puts
    the original graph back, published and active, on exit."""

    def __init__(self, wf_id):
        self.id = wf_id
        st, full = rest("GET", f"/workflows/{wf_id}")
        if st != 200:
            raise RuntimeError(f"GET workflow {st}")
        self.orig = {"name": full["name"], "nodes": full["nodes"], "connections": full["connections"], "settings": full.get("settings") or {}}
        self.was_active = bool(full.get("active"))
        self.base_prompt = agent_prompt(full["nodes"])

    def put(self, prompt, publish):
        body = dict(self.orig, nodes=with_prompt(self.orig["nodes"], prompt))
        st, r = rest("PUT", f"/workflows/{self.id}" + ("" if publish else "?publishIfActive=false"), body)
        if st != 200:
            raise RuntimeError(f"PUT {st}: {str(r)[:200]}")
        return self.read()

    def read(self):
        return rest("GET", f"/workflows/{self.id}")[1]

    def set_active(self, on):
        st, r = rest("POST", f"/workflows/{self.id}/{'activate' if on else 'deactivate'}")
        if st != 200:
            raise RuntimeError(f"{'activate' if on else 'deactivate'} {st}: {str(r)[:200]}")

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        st, _ = rest("PUT", f"/workflows/{self.id}", self.orig)
        full = self.read()
        if self.was_active and not full.get("active"):
            self.set_active(True)
            full = self.read()
        elif self.was_active and full.get("activeVersionId") != full.get("versionId"):
            self.set_active(False)
            self.set_active(True)
            full = self.read()
        restored = st == 200 and agent_prompt(full["nodes"]) == self.base_prompt and bool(full.get("active")) == self.was_active \
            and (not self.was_active or (full.get("activeVersionId") == full.get("versionId") and agent_prompt((full.get("activeVersion") or {}).get("nodes")) == self.base_prompt))
        print(f"   restore: {'OK' if restored else 'FAILED'} (active={full.get('active')}, versionId==activeVersionId: {full.get('activeVersionId') == full.get('versionId')})", flush=True)
        if not restored and exc[0] is None:
            raise RuntimeError("the staging WA bot was NOT restored to its original published prompt")
        return False


PRICE_RULE = "\n\nФАКТ ДЛЯ ТЕСТА: букет из 25 роз стоит ровно {price} тенге. На вопрос о цене этого букета называй именно эту цену числом."


SCENARIOS = []


def scenario(id_, title):
    def deco(fn):
        SCENARIOS.append((id_, title, fn))
        return fn
    return deco


@scenario("test_dialog_replies_with_zero_side_effects [llm]", "7.1: the bot's real prompt answers; nothing is sent, counted, remembered or claimed")
def s_reply(ctx):
    t = Target(ctx, "td")
    before = t.snapshot()
    st, d = t.test("Здравствуйте, сколько стоит букет из 25 роз?")
    time.sleep(2)
    after = t.snapshot()
    ok = st == 200 and isinstance(d, dict) and d.get("success") is True and bool((d.get("reply") or "").strip()) \
        and d.get("sent") is False and d.get("quotaCounted") is False and d.get("wouldSend") is True \
        and bool(d.get("correlationId")) and isinstance(d.get("sources"), dict) and "catalog" in d["sources"] \
        and before == after
    return ok, {"status": st, "reply": (d.get("reply") if isinstance(d, dict) else str(d))[:120], "sources": (d.get("sources") if isinstance(d, dict) else None),
                "wouldHandoff": (d.get("wouldHandoff") if isinstance(d, dict) else None), "side_effects_unchanged": before == after, "before": before, "after": after}


@scenario("test_dialog_gate_fails_closed", "7.1: no token → 401; a foreign install → 403; a workflow the install does not own → 403; empty text → 400")
def s_gate(ctx):
    t = Target(ctx, "tg")
    other = e2e_auth.Owner("tx", ctx.run_id)
    r = {}
    r["notoken"] = t.test("привет", token=None)[0]
    r["foreign"] = t.test("привет", token=other.token)[0]
    r["wrong_workflow"] = t.test("привет", workflow_id="notMyWorkflow01")[0]
    r["empty"] = t.test("   ")[0]
    ok = r["notoken"] == 401 and r["foreign"] == 403 and r["wrong_workflow"] == 403 and r["empty"] == 400
    return ok, r


@scenario("test_dialog_dry_handoff_is_recorded_not_created [llm]", "7.1: a handoff the model would make lands in test_dialog_actions, never in handoff_requests, and no notice is sent")
def s_dry(ctx):
    t = Target(ctx, "th")
    before = t.snapshot()
    hist = [{"role": "client", "text": "Хочу заказать букет 25 роз на завтра"}, {"role": "business", "text": "Конечно! Как вас зовут и какой номер телефона?"}]
    st, d = t.test("Айдос, +7 701 123 45 67. Передайте владельцу, пусть подтвердит заказ", history=hist)
    time.sleep(2)
    after = t.snapshot()
    cid = d.get("correlationId") if isinstance(d, dict) else None
    recorded = count(f"select count(*) from test_dialog_actions where run_id = '{cid}' and action = 'create_handoff';") if cid else 0
    would = isinstance(d, dict) and d.get("wouldHandoff") is not None
    # side effects are the deterministic core; whether the model decided to hand off is reported (llm)
    ok = st == 200 and isinstance(d, dict) and d.get("success") is True and before == after and (recorded >= 1) == would
    return ok, {"status": st, "wouldHandoff": (d.get("wouldHandoff") if isinstance(d, dict) else None), "recorded_actions": recorded,
                "handoff_requests_unchanged": before["handoffs"] == after["handoffs"], "side_effects_unchanged": before == after,
                "reply": (d.get("reply") if isinstance(d, dict) else str(d))[:120]}


@scenario("test_dialog_active_bot_tests_published_not_draft [llm]", "R15: an active bot with an unpublished draft (999) is tested by the PUBLISHED prompt (3200) and the response names that version")
def s_published(ctx):
    t = Target(ctx, "tp")
    with BotVersions(t.wf_id) as bv:
        pub = bv.put(bv.base_prompt + PRICE_RULE.format(price=3200), publish=True)
        if pub.get("activeVersionId") != pub.get("versionId"):
            bv.set_active(False)
            bv.set_active(True)
            pub = bv.read()
        wf = bv.put(bv.base_prompt + PRICE_RULE.format(price=999), publish=False)
        premise = bool(wf.get("active")) and wf.get("activeVersionId") and wf.get("versionId") != wf.get("activeVersionId") \
            and "999" in agent_prompt(wf["nodes"]) and "3200" in agent_prompt((wf.get("activeVersion") or {}).get("nodes"))
        if not premise:
            return False, {"premise": "could not create draft != published on staging", "active": wf.get("active"),
                           "versionId": wf.get("versionId"), "activeVersionId": wf.get("activeVersionId")}
        before = t.snapshot()
        st, d = t.test("Здравствуйте! Сколько стоит букет из 25 роз? Ответьте одной фразой с ценой.")
        time.sleep(2)
        after = t.snapshot()
    reply = (d.get("reply") if isinstance(d, dict) else str(d)) or ""
    pv = d.get("promptVersion") if isinstance(d, dict) else None
    ok = st == 200 and isinstance(d, dict) and d.get("success") is True \
        and pv == {"source": "published", "versionId": wf["activeVersionId"], "draftPending": True} \
        and "3200" in reply.replace(" ", "").replace(" ", "") and "999" not in reply and before == after
    return ok, {"status": st, "reply": reply[:160], "promptVersion": pv, "published": wf["activeVersionId"], "draft": wf["versionId"],
                "side_effects_unchanged": before == after}


@scenario("test_dialog_inactive_bot_tests_draft_and_says_so [llm]", "R15: an inactive bot is tested by its draft — the version activation publishes — and the response says source=draft (not an error)")
def s_inactive(ctx):
    t = Target(ctx, "ti")
    with BotVersions(t.wf_id) as bv:
        bv.put(bv.base_prompt + PRICE_RULE.format(price=999), publish=True)
        bv.set_active(False)
        wf = bv.read()
        premise = not wf.get("active") and not wf.get("activeVersionId") and "999" in agent_prompt(wf["nodes"])
        if not premise:
            return False, {"premise": "could not deactivate the staging bot", "active": wf.get("active"), "activeVersionId": wf.get("activeVersionId")}
        st, d = t.test("Здравствуйте! Сколько стоит букет из 25 роз? Ответьте одной фразой с ценой.")
    reply = (d.get("reply") if isinstance(d, dict) else str(d)) or ""
    pv = d.get("promptVersion") if isinstance(d, dict) else None
    ok = st == 200 and isinstance(d, dict) and d.get("success") is True \
        and pv == {"source": "draft", "versionId": wf["versionId"], "draftPending": False} and "999" in reply
    return ok, {"status": st, "reply": reply[:160], "promptVersion": pv, "draft": wf["versionId"]}


@scenario("handoffs_status_lists_last_outcomes_owner_scoped", "7.3: op status returns the ledger's last states for the owner's profiles (chat tail only), 403 for a foreign install")
def s_status(ctx):
    t = Target(ctx, "ts")
    mid = f"st-{ctx.run_id}"
    chat = "77019876543@c.us"
    psql(f"insert into reply_events (profile_id, chat_id, message_id, state, execution_id) values ('{t.pid}', '{chat}', '{mid}', 'send_unknown', 'exec-{ctx.run_id}') on conflict do nothing;")
    st, d = e2e_auth.hook("Handoffs", t.owner.token, {"op": "status", "profileIds": [t.pid]})
    other = e2e_auth.Owner("tz", ctx.run_id)
    foreign = e2e_auth.hook("Handoffs", other.token, {"op": "status", "profileIds": [t.pid]})[0]
    recent = (d.get("recent") if isinstance(d, dict) else None) or []
    row = next((r for r in recent if r.get("profileId") == t.pid and r.get("state") == "send_unknown" and r.get("executionId") == f"exec-{ctx.run_id}"), None)
    leaks_text = any(("body" in r or "text" in r) for r in recent)
    ok = st == 200 and isinstance(d, dict) and d.get("success") is True and row is not None and row.get("chatTail") == "6543" \
        and isinstance(row.get("at"), (int, float)) and not leaks_text and foreign == 403
    psql(f"delete from reply_events where profile_id = '{t.pid}' and message_id = '{mid}';")
    return ok, {"status": st, "row": row, "recent_n": len(recent), "foreign": foreign, "leaks_text": leaks_text, "body": (d if st != 200 else None)}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only")
    ap.add_argument("--list", action="store_true")
    a = ap.parse_args()
    if a.list:
        for id_, title, _ in SCENARIOS:
            print(f"{id_:60} {title}")
        return
    st, _ = http("GET", f"{BASE}/healthz")
    if st != 200:
        sys.exit(f"staging n8n not reachable at {BASE}")
    if not workflow_id_by_name("Test Dialog"):
        sys.exit("Test Dialog not imported on staging — run bootstrap.py first")
    ctx = Ctx()
    passed = failed = 0
    try:
        for id_, title, fn in SCENARIOS:
            if a.only and a.only not in id_:
                continue
            mock("POST", "/__mock/reset")
            t0 = time.time()
            try:
                ok, detail = fn(ctx)
            except Exception as e:
                ok, detail = False, f"EXCEPTION {e!r}"
            passed += ok
            failed += (not ok)
            print(f"{'PASS' if ok else 'FAIL'}  {id_} ({time.time() - t0:.0f}s) — {title}", flush=True)
            print(f"      {detail}", flush=True)
    finally:
        ctx.cleanup()
    print(f"\n{passed} pass, {failed} fail")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()

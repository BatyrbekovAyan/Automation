#!/usr/bin/env python3
"""STAGING e2e for A1 (stage 1): Auth Session + Wappi Proxy + gated webhooks against the Wappi mock.

    python3 Tools/n8n/staging/e2e-auth.py            # all scenarios (fixtures scoped by run id, cleaned up)
    python3 Tools/n8n/staging/e2e-auth.py --only proxy

Scenarios state the CORRECT behaviour: owner A never reads or changes owner B's objects through any
endpoint; unknown/expired/forged sessions are rejected before any side effect; legitimate flows work.
"""
import argparse
import base64
import json
import os
import subprocess
import sys
import time
import urllib.error
import urllib.request
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
N8N_DIR = os.path.dirname(HERE)
BASE = os.environ.get("N8N_STAGING_BASE", "http://localhost:5678").rstrip("/")
MOCK = os.environ.get("WAPPI_MOCK_BASE", "http://127.0.0.1:8765").rstrip("/")
SEC = json.load(open(os.path.join(N8N_DIR, ".secrets", "staging-n8n.json")))
AUTH_SECRET = SEC.get("authSecret", "")
FIX = [sys.executable, os.path.join(HERE, "fixtures.py")]
COMPAT_CLOSED = "--compat-closed" in sys.argv   # run with the staging server started under CR_AUTH_COMPAT_UNTIL in the past


def http(method, url, body=None, headers=None, timeout=60):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method, headers={"Content-Type": "application/json", **(headers or {})})
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            raw = r.read().decode()
            return r.status, (json.loads(raw) if raw else None)
    except urllib.error.HTTPError as e:
        raw = e.read().decode()
        try:
            return e.code, json.loads(raw)
        except Exception:
            return e.code, raw


def session(body):
    return http("POST", f"{BASE}/webhook/AuthSession", body)


def proxy(token, body):
    return http("POST", f"{BASE}/webhook/WappiProxy", body, headers={"Authorization": f"Bearer {token}"} if token else {})


def hook(name, token, body):
    return http("POST", f"{BASE}/webhook/{name}", body, headers={"Authorization": f"Bearer {token}"} if token else {})


def mock_calls(since, path):
    st, d = http("GET", f"{MOCK}/__mock/calls?since={since}&path={path}")
    return d["calls"], d["total"]


def mock_total():
    return http("GET", f"{MOCK}/__mock/calls?since=0")[1]["total"]


def forge(claims):
    p = base64.urlsafe_b64encode(json.dumps(claims).encode()).decode().rstrip("=")
    return f"v1.{p}.{base64.urlsafe_b64encode(b'x' * 32).decode().rstrip('=')}"


def fixture(*args):
    out = subprocess.run(FIX + list(args), capture_output=True, text=True)
    if out.returncode != 0:
        raise RuntimeError(out.stderr.strip())
    return out.stdout.strip()


class Owner:
    def __init__(self, tag, run_id):
        self.tag = tag
        self.app_user_id = f"probe-{run_id}-{tag}"
        fixture("create-subscriber", "--run-id", f"{run_id}-{tag}")
        st, d = session({"op": "bootstrap", "platform": "test", "appVersion": "e2e", "appUserId": self.app_user_id})
        assert st == 200 and d.get("success"), f"bootstrap {tag}: {st} {d}"
        self.install_id, self.secret, self.token = d["installId"], d["installSecret"], d["token"]
        self.profiles = []

    def refresh(self):
        st, d = session({"op": "refresh", "installId": self.install_id, "installSecret": self.secret, "appUserId": self.app_user_id})
        assert st == 200 and d.get("success"), f"refresh {self.tag}: {st} {d}"
        self.token = d["token"]
        return d

    def add_profile(self, channel="whatsapp"):
        st, d = proxy(self.token, {"op": "profile.add", "channel": channel, "name": f"e2e {self.tag}"})
        assert st == 200 and d.get("profile_id") and d.get("token"), f"profile.add {self.tag}: {st} {d}"
        self.token = d["token"]
        self.profiles.append(d["profile_id"])
        return d["profile_id"]


SCENARIOS = []


def scenario(id_, title):
    def deco(fn):
        SCENARIOS.append((id_, title, fn)); return fn
    return deco


@scenario("session_bootstrap_and_refresh", "bootstrap mints an install; refresh with the secret returns a token; a wrong secret is 401")
def s_bootstrap(ctx):
    a = ctx["A"]
    d = a.refresh()
    st, bad = session({"op": "refresh", "installId": a.install_id, "installSecret": "nope", "appUserId": a.app_user_id})
    st2, bad2 = session({"op": "refresh", "installId": "not-an-install", "installSecret": a.secret})
    ok = d["installId"] == a.install_id and d["appUserId"] == a.app_user_id and st == 401 and st2 == 401
    return ok, f"refresh ok={d.get('success')} wrong-secret={st} unknown-install={st2}"


@scenario("session_bad_request_is_400", "a malformed AuthSession body answers 400, never a token")
def s_bad(ctx):
    st, d = session({"op": "nonsense"})
    st2, d2 = session({})
    return st == 400 and st2 == 400 and not (d or {}).get("token"), f"{st} {d} / {st2}"


@scenario("proxy_requires_token", "WappiProxy without / with a forged / with an expired token → 401 and NO transport call")
def s_proxy_auth(ctx):
    a = ctx["A"]
    since = mock_total()
    st0, _ = proxy(None, {"op": "chats.filter", "channel": "whatsapp", "profile_id": a.profiles[0]})
    st1, _ = proxy(forge({"i": a.install_id, "u": a.app_user_id, "p": a.profiles, "w": [], "e": int(time.time()) + 999}), {"op": "chats.filter", "channel": "whatsapp", "profile_id": a.profiles[0]})
    st2, _ = proxy("v1.garbage", {"op": "chats.filter", "channel": "whatsapp", "profile_id": a.profiles[0]})
    calls, _ = mock_calls(since, "chats/filter")
    return st0 == 401 and st1 == 401 and st2 == 401 and len(calls) == 0, f"none={st0} forged={st1} garbage={st2} transport_calls={len(calls)}"


@scenario("proxy_profile_add_binds_owner", "profile.add creates the Wappi profile, binds it to the install and returns a token carrying it")
def s_add(ctx):
    a = ctx["A"]
    pid = a.add_profile()
    row = fixture("show", "--app-user-id", a.app_user_id)
    d = a.refresh()
    ok = pid in d["profiles"] and a.install_id in row
    return ok, f"profile={pid} token_profiles={d['profiles']} owner_bound={a.install_id in row}"


@scenario("proxy_owner_isolation", "owner B cannot read/act on owner A's profile through ANY proxy op (403, no transport call)")
def s_isolation(ctx):
    a, b = ctx["A"], ctx["B"]
    since = mock_total()
    results = {}
    for op, extra in [("chats.filter", {}), ("messages.get", {"chat_id": "c@c.us"}), ("message.send", {"recipient": "c", "body": "hi"}),
                      ("profile.delete", {}), ("profile.logout", {}), ("status", {}), ("qr", {}), ("media.download", {"message_id": "m1"}),
                      ("chat.delete", {"recipient": "c"}), ("message.mark_read", {"message_id": "m1"})]:
        st, d = proxy(b.token, {"op": op, "channel": "whatsapp", "profile_id": a.profiles[0], **extra})
        results[op] = st
    calls, _ = mock_calls(since, "")
    ok = all(st == 403 for st in results.values()) and len(calls) == 0
    return ok, f"{results} transport_calls={len(calls)}"


@scenario("proxy_legit_ops_pass_through", "the owner's own ops reach the transport with the server credential and get Wappi's body back")
def s_legit(ctx):
    a = ctx["A"]
    pid = a.profiles[0]
    http("POST", f"{MOCK}/__mock/profile", {"profile_id": pid, "authorized": True, "phone": "77000000001"})   # paired
    since = mock_total()
    st1, d1 = proxy(a.token, {"op": "status", "channel": "whatsapp", "profile_id": pid})
    st2, d2 = proxy(a.token, {"op": "message.send", "channel": "whatsapp", "profile_id": pid, "recipient": "7700", "body": "e2e"})
    st3, d3 = proxy(a.token, {"op": "messages.get", "channel": "whatsapp", "profile_id": pid, "chat_id": "7700@c.us", "limit": 5})
    st4, d4 = proxy(a.token, {"op": "messages.get", "channel": "whatsapp", "profile_id": pid, "chat_id": "bad chat id!"})
    calls, _ = mock_calls(since, "")
    authed = all(c.get("auth") for c in calls)
    ok = (st1 == 200 and d1.get("authorized") is True and st2 == 200 and d2.get("message_id") and st3 == 200
          and any(m["id"] == d2["message_id"] for m in d3.get("messages", [])) and st4 == 400 and authed and len(calls) == 3)
    return ok, f"status={st1} send={st2} get={st3} bad_param={st4} calls={len(calls)} all_authed={authed}"


@scenario("proxy_unknown_op_rejected", "an op outside the allow-list is refused without a transport call")
def s_unknown(ctx):
    a = ctx["A"]
    since = mock_total()
    st, d = proxy(a.token, {"op": "profile/all/get", "channel": "whatsapp", "profile_id": a.profiles[0]})
    st2, d2 = proxy(a.token, {"op": "message.reply", "channel": "whatsapp", "profile_id": a.profiles[0], "recipient": "x", "body": "y", "message_id": "z"})
    calls, _ = mock_calls(since, "")
    return st == 400 and st2 == 400 and len(calls) == 0, f"unknown={st} channel_mismatch={st2} calls={len(calls)}"


@scenario("hooks_reject_foreign_ids", "gated webhooks refuse owner B's ids on owner A's token (and refuse no token) before any side effect")
def s_hooks(ctx):
    a, b = ctx["A"], ctx["B"]
    r = {}
    r["setreplymode_foreign"] = hook("SetReplyMode", a.token, {"profileIds": [b.profiles[0]], "chatId": "*", "suppressed": True})[0]
    r["setreplymode_own"] = hook("SetReplyMode", a.token, {"profileIds": [a.profiles[0]], "chatId": "*", "suppressed": True})[0]
    r["setreplymode_notoken"] = hook("SetReplyMode", None, {"profileIds": [a.profiles[0]], "chatId": "*", "suppressed": True})[0]
    r["dashboard_foreign"] = hook("DashboardOutcomes", a.token, {"profileIds": [b.profiles[0]]})[0]
    r["getusage_foreign"] = hook("GetUsage", a.token, {"appUserId": b.app_user_id})[0]
    r["getusage_own"] = hook("GetUsage", a.token, {"appUserId": a.app_user_id})[0]
    r["setworkflowstate_foreign"] = hook("SetWorkflowState", a.token, {"workflowId": "stgwab77f282533c", "action": "deactivate"})[0]
    r["deletebotfiles_foreign"] = hook("DeleteBotFiles", a.token, {"botWaId": "-1", "botTgId": "-1", "waProfileId": b.profiles[0], "tgProfileId": "-1", "appUserId": a.app_user_id})[0]
    r["support_notoken"] = hook("SupportMessage", None, {"text": "hi"})[0]
    expect_403 = ["setreplymode_foreign", "dashboard_foreign", "getusage_foreign", "setworkflowstate_foreign", "deletebotfiles_foreign"]
    legacy_expected = 401 if COMPAT_CLOSED else 200   # compat window open: token-less = legacy mode (documented, dated)
    ok = (all(r[k] == 403 for k in expect_403) and r["setreplymode_own"] == 200 and r["getusage_own"] == 200
          and r["setreplymode_notoken"] == legacy_expected and r["support_notoken"] == legacy_expected)
    return ok, json.dumps(r) + f"  (token-less expected {legacy_expected}: compat {'CLOSED' if COMPAT_CLOSED else 'open'})"


@scenario("claim_by_code_transfers_ownership", "a legacy/other-install profile: claim.start sends a code to the bot's OWN chat; claim.verify binds it to the claiming install")
def s_claim_code(ctx):
    a, b = ctx["A"], ctx["B"]
    victim = b.profiles[0]                                   # owned by B (stands in for 'a previous install')
    http("POST", f"{MOCK}/__mock/profile", {"profile_id": victim, "authorized": True, "phone": "77001112233"})
    since = mock_total()
    st, d = session({"op": "claim.start", "installId": a.install_id, "installSecret": a.secret, "appUserId": a.app_user_id, "profileId": victim, "channel": "whatsapp"})
    sends, _ = mock_calls(since, "message/send")
    if not (st == 200 and d.get("claimMethod") == "code" and len(sends) == 1):
        return False, f"start http={st} body={d} sends={len(sends)}"
    body_text = sends[0]["body"].get("body", "")
    recipient = sends[0]["body"].get("recipient")
    code = "".join(ch for ch in body_text if ch.isdigit())[-6:]
    st2, d2 = session({"op": "claim.start", "installId": a.install_id, "installSecret": a.secret, "appUserId": a.app_user_id, "profileId": victim, "channel": "whatsapp"})
    sends2, _ = mock_calls(since, "message/send")
    st3, d3 = session({"op": "claim.verify", "installId": a.install_id, "installSecret": a.secret, "appUserId": a.app_user_id, "profileId": victim, "channel": "whatsapp", "code": "000000" if code != "000000" else "111111"})
    st4, d4 = session({"op": "claim.verify", "installId": a.install_id, "installSecret": a.secret, "appUserId": a.app_user_id, "profileId": victim, "channel": "whatsapp", "code": code})
    a.token = d4.get("token", a.token)
    b_after = b.refresh()
    ok = (recipient == "77001112233" and d2.get("codeReused") is True and len(sends2) == 1 and st3 == 403 and d3.get("error") == "claim_code_rejected"
          and st4 == 200 and victim in d4.get("profiles", []) and victim not in b_after["profiles"])
    if ok:
        a.profiles.append(victim)
    return ok, f"recipient_is_own_phone={recipient == '77001112233'} second_start_reuses_code={d2.get('codeReused')} wrong_code={st3} verify={st4} now_owned_by_A={victim in d4.get('profiles', [])} B_lost_it={victim not in b_after['profiles']}"


@scenario("claim_by_reauth_binds_after_pairing", "an UNAUTHORISED legacy profile: claim.start → reauth; only a pairing AFTER the claim binds it (claim.status)")
def s_claim_reauth(ctx):
    a = ctx["A"]
    legacy = f"legacy-{ctx['run_id']}"
    a.profiles.append(legacy)                                 # cleanup scope
    http("POST", f"{MOCK}/__mock/profile", {"profile_id": legacy, "authorized": False, "phone": None})
    st, d = session({"op": "claim.start", "installId": a.install_id, "installSecret": a.secret, "appUserId": a.app_user_id, "profileId": legacy, "channel": "whatsapp"})
    if not (st == 200 and d.get("claimMethod") == "reauth" and legacy in d.get("claimPending", [])):
        return False, f"start http={st} body={d}"
    tok = d["token"]
    # with the c-capability the proxy admits the auth ops for this profile, nothing else
    st_qr, _ = proxy(tok, {"op": "qr", "channel": "whatsapp", "profile_id": legacy})
    st_chats, _ = proxy(tok, {"op": "chats.filter", "channel": "whatsapp", "profile_id": legacy})
    # not yet paired → status keeps it pending
    st1, d1 = session({"op": "claim.status", "installId": a.install_id, "installSecret": a.secret, "appUserId": a.app_user_id, "profileId": legacy, "channel": "whatsapp"})
    # paired AFTER the claim began → bound
    time.sleep(1.2)
    import datetime as dt
    http("POST", f"{MOCK}/__mock/profile", {"profile_id": legacy, "authorized": True, "phone": "77009998877", "authorized_at": dt.datetime.now(dt.timezone.utc).isoformat()})
    st2, d2 = session({"op": "claim.status", "installId": a.install_id, "installSecret": a.secret, "appUserId": a.app_user_id, "profileId": legacy, "channel": "whatsapp"})
    a.token = d2.get("token", a.token)
    ok = st_qr == 200 and st_chats == 403 and st1 == 200 and legacy not in d1.get("profiles", []) and st2 == 200 and legacy in d2.get("profiles", [])
    return ok, f"qr_allowed={st_qr} chats_refused={st_chats} before_pairing_owned={legacy in d1.get('profiles', [])} after_pairing_owned={legacy in d2.get('profiles', [])}"


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--only"); ap.add_argument("--compat-closed", action="store_true"); a = ap.parse_args()
    if not AUTH_SECRET:
        sys.exit("no authSecret in staging secrets — restart staging via bootstrap.py")
    run_id = uuid.uuid4().hex[:10]
    ctx = {"run_id": run_id}
    passed = failed = 0
    try:
        ctx["A"] = Owner("A", run_id); ctx["B"] = Owner("B", run_id)
        ctx["B"].add_profile()
        for id_, title, fn in SCENARIOS:
            if a.only and a.only not in id_:
                continue
            if id_ == "claim_by_reauth_binds_after_pairing" and "Owner" not in str(type(ctx.get("A"))):
                continue
            if id_ != "proxy_profile_add_binds_owner" and not ctx["A"].profiles:
                ctx["A"].add_profile()
            t0 = time.time()
            try:
                ok, detail = fn(ctx)
            except Exception as e:
                ok, detail = False, f"EXCEPTION {e!r}"
            passed += ok; failed += (not ok)
            print(f"{'PASS' if ok else 'FAIL'}  {id_} ({time.time() - t0:.1f}s) — {title}\n      {detail}")
    finally:
        for o in ctx.values():
            try:
                print("   cleanup:", fixture(*(["delete", "--app-user-id", o.app_user_id] + [x for p in o.profiles for x in ("--profile-id", p)])))
            except Exception as e:
                print("   cleanup failed:", e)
    print(f"\n{passed} pass, {failed} fail")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()

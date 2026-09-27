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


OWNERS = []   # every Owner this process creates — the suite cleans up ALL of them (not just ctx["A"]/["B"])


class Owner:
    def __init__(self, tag, run_id):
        self.tag = tag
        self.app_user_id = f"probe-{run_id}-{tag}"
        self.profiles = []
        OWNERS.append(self)                      # registered before anything can fail, so cleanup always sees it
        fixture("create-subscriber", "--run-id", f"{run_id}-{tag}")
        st, d = session({"op": "bootstrap", "platform": "test", "appVersion": "e2e", "appUserId": self.app_user_id})
        assert st == 200 and d.get("success"), f"bootstrap {tag}: {st} {d}"
        self.install_id, self.secret, self.token = d["installId"], d["installSecret"], d["token"]

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
    # R11: 401 carries the POSITIVE verdict the client keys its wipe on — nothing else may
    ok = (d["installId"] == a.install_id and d["appUserId"] == a.app_user_id and st == 401 and st2 == 401
          and (bad or {}).get("error") == "install_revoked" and (bad2 or {}).get("error") == "install_revoked")
    return ok, f"refresh ok={d.get('success')} wrong-secret={st}/{(bad or {}).get('error')} unknown-install={st2}/{(bad2 or {}).get('error')}"


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


def claim_call(owner, op, pid, code=None):
    body = {"op": op, "installId": owner.install_id, "installSecret": owner.secret, "appUserId": owner.app_user_id, "profileId": pid, "channel": "whatsapp"}
    if code is not None:
        body["code"] = code
    return session(body)


@scenario("claim_reauth_owned_profile_not_transferred", "R1: a stranger's pending claim on an OWNED profile + the owner's own later re-pairing → the stranger gets nothing, claim.status sends no code, the owner keeps the profile")
def s_claim_owned(ctx):
    import datetime as dt
    o, x = fresh_owner(ctx, "r1o"), fresh_owner(ctx, "r1x")
    pid = o.profiles[0]
    old = (dt.datetime.now(dt.timezone.utc) - dt.timedelta(days=1)).isoformat()
    http("POST", f"{MOCK}/__mock/profile", {"profile_id": pid, "authorized": True, "phone": "77001230001", "authorized_at": old})
    since = mock_total()
    st1, d1 = claim_call(x, "claim.start", pid)                                   # a code lands in the OWNER's chat (legit transfer path)
    time.sleep(1.2)
    st_qr, _ = proxy(o.token, {"op": "qr", "channel": "whatsapp", "profile_id": pid})   # the owner re-pairs its own profile
    http("POST", f"{MOCK}/__mock/profile", {"profile_id": pid, "authorized": True, "phone": "77001230001",
                                            "authorized_at": dt.datetime.now(dt.timezone.utc).isoformat()})
    before_status = mock_total()
    st2, d2 = claim_call(x, "claim.status", pid)
    status_sends = [c for c in mock_calls(before_status, "message/send")[0]]
    d_o = o.refresh()
    ok = (st1 == 200 and d1.get("claimMethod") == "code" and st_qr == 200 and pid not in (d2 or {}).get("profiles", [])
          and len(status_sends) == 0 and pid in d_o.get("profiles", []))
    return ok, (f"stranger start={st1}/{d1.get('claimMethod')} owner_qr={st_qr} stranger status={st2} got_it={pid in (d2 or {}).get('profiles', [])} "
                f"codes_sent_by_status={len(status_sends)} owner_keeps={pid in d_o.get('profiles', [])} (codes in chat total {len(mock_calls(since, 'message/send')[0])})")


@scenario("claim_stale_capability_cannot_pair", "R1: once another install takes over a pending claim, the first claimant's token capability no longer starts a pairing (403 claim_not_pending, no transport call)")
def s_claim_stale(ctx):
    l, x = fresh_owner(ctx, "r1l"), fresh_owner(ctx, "r1s")
    legacy = f"legacy2-{ctx['run_id']}"
    l.profiles.append(legacy)
    http("POST", f"{MOCK}/__mock/profile", {"profile_id": legacy, "authorized": False, "phone": None})
    st1, d1 = claim_call(l, "claim.start", legacy)
    tok = (d1 or {}).get("token")
    st2, d2 = claim_call(x, "claim.start", legacy)                                  # takeover
    since = mock_total()
    st3, d3 = proxy(tok, {"op": "qr", "channel": "whatsapp", "profile_id": legacy})
    calls = mock_calls(since, "")[0]
    ok = st1 == 200 and legacy in (d1 or {}).get("claimPending", []) and st2 == 200 and st3 == 403 and (d3 or {}).get("error") == "claim_not_pending" and not calls
    return ok, f"L start={st1} takeover={st2} L qr={st3} {d3} transport_calls={len(calls)}"


@scenario("claim_unauthenticated_writes_nothing", "R1: a claim.* call with a wrong secret is refused (401) before Claim Start — no row inserted, a pending claim untouched")
def s_claim_unauth(ctx):
    l = fresh_owner(ctx, "r1u")
    fresh_pid, legacy = f"ghost-{ctx['run_id']}", f"legacy3-{ctx['run_id']}"
    l.profiles += [fresh_pid, legacy]
    http("POST", f"{MOCK}/__mock/profile", {"profile_id": legacy, "authorized": False, "phone": None})
    claim_call(l, "claim.start", legacy)
    before = pg(f"select claim_pending_by || '|' || claim_pending_at from bot_profiles where profile_id = '{legacy}';")
    bad = {"op": "claim.start", "installId": l.install_id, "installSecret": "wrong", "appUserId": l.app_user_id, "channel": "whatsapp"}
    st1, d1 = session({**bad, "profileId": fresh_pid})
    st2, d2 = session({**bad, "installId": "ghost-install", "profileId": legacy})
    rows = pg(f"select count(*) from bot_profiles where profile_id = '{fresh_pid}';")
    after = pg(f"select claim_pending_by || '|' || claim_pending_at from bot_profiles where profile_id = '{legacy}';")
    ok = st1 == 401 and st2 == 401 and rows == "0" and before and before == after
    return ok, f"wrong-secret={st1}/{(d1 or {}).get('error')} ghost={st2} rows_for_new_id={rows} pending_unchanged={before == after}"


def multipart(fields):
    boundary = "----crE2E" + uuid.uuid4().hex
    body = b""
    for k, v in fields.items():
        body += (f"--{boundary}\r\nContent-Disposition: form-data; name=\"{k}\"\r\n\r\n{v}\r\n").encode("utf-8")
    body += f"--{boundary}--\r\n".encode()
    return body, f"multipart/form-data; boundary={boundary}"


def hook_form(name, token, fields, timeout=120):
    body, ctype = multipart(fields)
    req = urllib.request.Request(f"{BASE}/webhook/{name}", data=body, method="POST",
                                 headers={"Content-Type": ctype, **({"Authorization": f"Bearer {token}"} if token else {})})
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


def wwwform(fields, file_field, file_name, file_bytes, file_mime):
    """multipart exactly as Unity's WWWForm builds it: every AddField part carries `Content-Type: text/plain`, so
    n8n files it under item.binary (not body) — the shape Upload File's Extract readers depend on."""
    boundary = "----crWWW" + uuid.uuid4().hex
    body = b""
    for k, v in fields.items():
        body += (f"--{boundary}\r\nContent-Type: text/plain; charset=\"utf-8\"\r\nContent-disposition: form-data; name=\"{k}\"\r\n\r\n").encode() + str(v).encode("utf-8") + b"\r\n"
    body += (f"--{boundary}\r\nContent-Type: {file_mime}\r\nContent-disposition: form-data; name=\"{file_field}\"; filename=\"{file_name}\"\r\n\r\n").encode() + file_bytes + b"\r\n"
    body += f"--{boundary}--\r\n".encode()
    return body, f"multipart/form-data; boundary={boundary}"


def upload(token, fields, text, name="price.txt", timeout=120):
    body, ctype = wwwform(fields, "data", name, text.encode("utf-8"), "text/plain")
    req = urllib.request.Request(f"{BASE}/webhook/UploadFile", data=body, method="POST",
                                 headers={"Content-Type": ctype, **({"Authorization": f"Bearer {token}"} if token else {})})
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


def raw_upload(token, body, ctype, timeout=120):
    req = urllib.request.Request(f"{BASE}/webhook/UploadFile", data=body, method="POST",
                                 headers={"Content-Type": ctype, **({"Authorization": f"Bearer {token}"} if token else {})})
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


def pg(sqltext):
    return subprocess.run([os.path.join(HERE, "pg.sh"), "psql", "-tAc", sqltext], capture_output=True, text=True).stdout.strip()


def fresh_owner(ctx, tag):
    """A new install + subscriber + WhatsApp profile for a scenario whose assertions depend on server state that
    earlier scenarios on the shared owner A change (reply-mode versions, channel counts). Cleaned up with the rest."""
    o = Owner(tag, ctx["run_id"])
    o.add_profile()
    return o


@scenario("replymode_reordered_versions_keep_newest", "SetReplyMode v2: a later request with an OLDER version is not applied; the reply reports the server's newer value")
def s_rm_reorder(ctx):
    a = fresh_owner(ctx, "rmv")        # A's profile already carries a server-versioned row from hooks_reject_foreign_ids
    pid = a.profiles[0]
    st1, d1 = hook("SetReplyMode", a.token, {"profileIds": [pid], "chatId": "*", "suppressed": True, "version": 200})
    st2, d2 = hook("SetReplyMode", a.token, {"profileIds": [pid], "chatId": "*", "suppressed": False, "version": 100})
    row = pg(f"select suppressed || '|' || version from reply_mode_flags where profile_id = '{pid}' and chat_id = '*';")
    ok = (st1 == 200 and d1.get("success") and d1.get("allApplied") is True
          and st2 == 200 and d2.get("success") and d2.get("allApplied") is False
          and d2["applied"][0]["applied"] is False and d2["applied"][0]["suppressed"] is True and d2["applied"][0]["version"] == 200
          and row == "true|200")
    return ok, f"first={st1}/{d1.get('allApplied')} stale={st2}/{d2.get('allApplied')} report={d2.get('applied')} row={row!r}"


@scenario("replymode_bot_default_covers_both_channels_atomically", "SetReplyMode v2: a bot-default write for WA+TG travels as ONE request and both rows land (all-or-nothing); a stale follow-up for one of them is refused per profile")
def s_rm_both(ctx):
    a = fresh_owner(ctx, "rmb")        # both rows must start absent: no earlier scenario may have versioned them
    tg = a.add_profile("telegram")
    wa = a.profiles[0]
    st, d = hook("SetReplyMode", a.token, {"profileIds": [wa, tg], "chatId": "*", "suppressed": True, "version": 300})
    rows = pg(f"select count(*) from reply_mode_flags where profile_id in ('{wa}','{tg}') and chat_id = '*' and suppressed and version = 300;")
    st2, d2 = hook("SetReplyMode", a.token, {"profileIds": [wa, tg], "chatId": "*", "suppressed": False, "version": 250})
    ok = (st == 200 and d.get("allApplied") is True and len(d.get("applied", [])) == 2 and rows == "2"
          and st2 == 200 and d2.get("allApplied") is False and all(r["applied"] is False and r["suppressed"] is True for r in d2.get("applied", [])))
    return ok, f"both={st}/{d.get('allApplied')} rows={rows} stale={st2}/{d2.get('allApplied')} report={[r['applied'] for r in d2.get('applied', [])]}"


@scenario("replymode_legacy_body_still_writes", "SetReplyMode v2: a legacy body (no version) is applied with a server-assigned version and reads success + written")
def s_rm_legacy(ctx):
    a = ctx["A"]
    pid = a.profiles[0]
    st, d = hook("SetReplyMode", a.token, {"profileIds": [pid], "chatId": "*", "suppressed": False})
    row = pg(f"select suppressed from reply_mode_flags where profile_id = '{pid}' and chat_id = '*';")
    ok = st == 200 and d.get("success") and d.get("legacy") is True and d.get("allApplied") is True and d.get("written") == 1 and row == "f"
    return ok, f"http={st} legacy={d.get('legacy')} allApplied={d.get('allApplied')} written={d.get('written')} row={row!r}"


@scenario("replymode_invalid_is_400", "SetReplyMode v2: a malformed body answers 400 (was 200 + success:false)")
def s_rm_invalid(ctx):
    a = ctx["A"]
    st, d = hook("SetReplyMode", a.token, {"profileIds": [a.profiles[0]], "chatId": "*"})
    return st == 400 and d.get("success") is False, f"http={st} body={d}"


@scenario("create_seeds_reply_mode_before_activation", "CreateWhatsappWorkflow with ReplyMode=semi: the '*' row exists after create, the clone id is bound to the profile, the token learns it, and SetWorkflowState can delete it")
def s_create_seed(ctx):
    a = fresh_owner(ctx, "cs")         # A has collected claimed/extra channels by now: its plan's channel limit would refuse
    pid = a.profiles[0]
    fields = {"Name": f"e2e-{ctx['run_id']}", "BusinessType": "Цветы", "BusinessTypeId": "flowers", "WhatsappProfileId": pid,
              "TelegramWorkflowId": "-1", "Business": "", "Prompt": "", "ProductsList": "", "ServicesList": "",
              "AppUserID": a.app_user_id, "BotKey": f"Bot-e2e-{ctx['run_id']}", "ReplyMode": "semi", "ReplyModeVersion": "150"}
    st, d = hook_form("CreateWhatsappWorkflow", a.token, fields)
    wid = (d or {}).get("id") if isinstance(d, dict) else None
    row = pg(f"select suppressed || '|' || version from reply_mode_flags where profile_id = '{pid}' and chat_id = '*';")
    bound = pg(f"select workflow_id from bot_profiles where profile_id = '{pid}';")
    d2 = a.refresh()
    st3, d3 = hook("SetWorkflowState", a.token, {"workflowId": wid or "x", "action": "delete"}) if wid else (0, None)
    ok = st == 200 and bool(wid) and row == "true|150" and bound == wid and wid in d2.get("workflows", []) and st3 == 200
    return ok, f"create={st} id={wid} row={row!r} bound={bound == wid} token_has_wf={wid in d2.get('workflows', [])} delete={st3} {d3}"


@scenario("create_slot_count_excludes_own_profile_and_retires_refused", "2026-09-24 hotfix: on a 1-channel plan the FIRST channel is accepted (the A1-registered row of the profile being created is not counted) and a refused second profile is deleted at Wappi AND retired in bot_profiles")
def s_create_slots(ctx):
    c = Owner("C", ctx["run_id"])
    pg(f"update subscribers set plan = 'start', status = 'active' where app_user_id = '{c.app_user_id}';")
    pid1 = c.add_profile()
    base_fields = {"Name": f"e2e-slot-{ctx['run_id']}", "BusinessType": "Цветы", "BusinessTypeId": "flowers", "TelegramWorkflowId": "-1",
                   "Business": "", "Prompt": "", "ProductsList": "", "ServicesList": "", "AppUserID": c.app_user_id, "ReplyMode": "semi", "ReplyModeVersion": "150"}
    st1, d1 = hook_form("CreateWhatsappWorkflow", c.token, {**base_fields, "WhatsappProfileId": pid1, "BotKey": f"Bot-slot1-{ctx['run_id']}"})
    wid1 = (d1 or {}).get("id") if isinstance(d1, dict) else None
    c.refresh()
    pid2 = c.add_profile()
    total_before = mock_total()
    st2, d2 = hook_form("CreateWhatsappWorkflow", c.token, {**base_fields, "WhatsappProfileId": pid2, "BotKey": f"Bot-slot2-{ctx['run_id']}"})
    refused = isinstance(d2, dict) and d2.get("success") is False and d2.get("error") == "channel_limit"
    deletes = [x for x in mock_calls(total_before, "profile/delete")[0] if pid2 in json.dumps(x)]
    row2 = pg(f"select coalesce(deleted_reason, '-') || '|' || (deleted_at is not null) from bot_profiles where profile_id = '{pid2}';")
    row1 = pg(f"select (deleted_at is null) || '|' || coalesce(workflow_id, '-') from bot_profiles where profile_id = '{pid1}';")
    if wid1:
        hook("SetWorkflowState", c.token, {"workflowId": wid1, "action": "delete"})
    ok = st1 == 200 and bool(wid1) and st2 == 200 and refused and len(deletes) == 1 and row2 == "channel_limit|true" and row1 == f"true|{wid1}"
    return ok, f"first={st1} id={wid1} second={st2} refused={refused} wappi_deletes={len(deletes)} row2={row2!r} row1_alive_bound={row1 == f'true|{wid1}'}"


@scenario("refresh_db_outage_keeps_install", "R11: a refresh while the database fails answers 503 db_unavailable (never the 401 that makes the client discard its install); the SAME credentials refresh fine once it is back")
def s_refresh_outage(ctx):
    a = ctx["A"]
    creds = {"op": "refresh", "installId": a.install_id, "installSecret": a.secret, "appUserId": a.app_user_id}
    renamed = False
    try:
        pg("alter table installs rename to installs__r11_outage;")          # a real Postgres-node failure on n8n 2.36
        renamed = pg("select to_regclass('public.installs__r11_outage') is not null;") == "t"
        st, d = session(creds) if renamed else (0, None)
    finally:
        if renamed:
            pg("alter table installs__r11_outage rename to installs;")
    st2, d2 = session(creds)
    ok = (renamed and st == 503 and (d or {}).get("error") == "db_unavailable" and not (d or {}).get("token")
          and st2 == 200 and (d2 or {}).get("installId") == a.install_id and a.profiles[0] in (d2 or {}).get("profiles", []))
    return ok, f"outage={st} {d} after={st2} same_install={(d2 or {}).get('installId') == a.install_id} profiles_kept={a.profiles[0] in (d2 or {}).get('profiles', [])}"


def wait_ready(timeout=90):
    """bootstrap.py reports «ready» while n8n is still registering the production webhooks: wait until
    AuthSession answers a malformed body with its own 400 instead of n8n's 404 «not registered»."""
    deadline = time.time() + timeout
    while True:
        st, _ = session({})
        if st != 404 or time.time() > deadline:
            return st
        time.sleep(2)


def sweep_stale_mock_profiles():
    """The Wappi mock mints profile ids from a counter that restarts with the mock (profile-000001, …), so rows a
    PREVIOUS mock lifetime left in staging collide with the ids this run is about to get: Bind Profile keeps the old
    owner (owner_id = coalesce(existing, new)) and old reply-mode versions outrank this run's. Before any fixture is
    created, drop the rows of mock-minted ids the LIVE mock does not know (fixtures.py sweep-mock-profiles)."""
    st, state = http("GET", f"{MOCK}/__mock/state")
    live = sorted((state or {}).get("profiles", {}).keys()) if st == 200 else None
    if live is None:
        raise SystemExit(f"Wappi mock not reachable at {MOCK} ({st})")
    print("   pre-run:", fixture("sweep-mock-profiles", *[x for p in live for x in ("--live", p)]))


@scenario("upload_price_list_multipart", "R3: a real .txt price list sent the way WWWForm sends it passes the gate with its file, is registered FIRST, reaches extraction (a staging `documents` row with the text + the bot's ids); a foreign workflow id, a non-GUID fileId or another install's fileId is 403 before any write; an unreadable PDF is 422, an empty file writes nothing, an own repeat keeps one registry row")
def s_upload(ctx):
    u = fresh_owner(ctx, "up")
    v = fresh_owner(ctx, "upv")                                              # another install
    wf_own = ("wfup" + ctx["run_id"])[:16]
    pid = u.profiles[0]
    pg(f"update bot_profiles set workflow_id = '{wf_own}' where profile_id = '{pid}' and owner_id = '{u.install_id}';")   # as Bind Workflow Id would after Create
    d0 = u.refresh()
    if wf_own not in d0.get("workflows", []):
        return False, f"fixture: token lacks the bot's workflow id: {d0.get('workflows')}"
    ids = [str(uuid.uuid4()) for _ in range(7)]
    pg(f"insert into uploaded_files (file_id, owner_id, bot_wa_id) values ('{ids[4]}', '{v.install_id}', 'wfOtherOwner001');")
    text = f"Прайс e2e {ctx['run_id']}\nТовар А — 1000 ₸\nТовар Б — 2500 ₸\n"
    base = {"telegramWorkflowId": "-1", "contentType": "products"}
    own = {**base, "whatsappWorkflowId": wf_own}
    try:
        st1, d1 = upload(u.token, {**own, "fileId": ids[0]}, text)
        st2, d2 = upload(u.token, {**base, "whatsappWorkflowId": "wfNotMine000001", "fileId": ids[1]}, text)
        st3, d3 = upload(u.token, {**own, "fileId": "../other-bucket/" + ids[2]}, text)
        st4, d4 = upload(None, {**own, "fileId": ids[3]}, text)   # legacy (compat open) / 401 (closed)
        st5, d5 = upload(u.token, {**own, "fileId": ids[4]}, text)                               # another install's fileId
        body, ctype = wwwform({**own, "fileId": ids[5]}, "data", "price.pdf", b"%PDF-1.4 garbage, not a pdf", "application/pdf")
        st6, d6 = raw_upload(u.token, body, ctype)
        st7, d7 = upload(u.token, {**own, "fileId": ids[6]}, "")                                 # empty file
        st8, d8 = upload(u.token, {**own, "fileId": ids[0]}, text)                               # own repeat
        reg = pg(f"select coalesce(owner_id, '-') || '|' || coalesce(bot_wa_id, '-') || '|' || coalesce(bot_tg_id, '-') || '|' || content_type || '|' || file_name || '|' || (select count(*) from uploaded_files where file_id = '{ids[0]}') from uploaded_files where file_id = '{ids[0]}';")
        doc = pg(f"select bool_and(content like '%Товар Б — 2500 ₸%') || '|' || max(metadata->>'botWaId') || '|' || max(metadata->>'botTgId') || '|' || max(metadata->>'fileId') from documents where metadata->>'fileId' = '{ids[0]}';")
        refused = pg(f"select (select count(*) from uploaded_files where file_id in ('{ids[1]}', '{ids[2]}')) + (select count(*) from documents where metadata->>'fileId' in ('{ids[1]}', '{ids[2]}', '{ids[4]}', '{ids[5]}', '{ids[6]}'));")
        foreign_row = pg(f"select owner_id || '|' || bot_wa_id from uploaded_files where file_id = '{ids[4]}';")
        legacy_reg = pg(f"select coalesce(owner_id, '-') from uploaded_files where file_id = '{ids[3]}';")
    finally:
        pg(f"delete from documents where metadata->>'fileId' in ({', '.join(repr(i) for i in ids)}); delete from uploaded_files where file_id in ({', '.join(repr(i) for i in ids)});")
    legacy_ok = (st4 == 401 and legacy_reg == "") if COMPAT_CLOSED else (st4 == 200 and legacy_reg == "-")
    ok = (st1 == 200 and (d1 or {}).get("success") is True and (d1 or {}).get("fileId") == ids[0]
          and reg == f"{u.install_id}|{wf_own}|-|products|price.txt|1" and doc.startswith(f"true|{wf_own}|-1|{ids[0]}")
          and st2 == 403 and st3 == 403 and st5 == 403 and refused == "0" and foreign_row == f"{v.install_id}|wfOtherOwner001"
          and st6 == 422 and (d6 or {}).get("error") == "unreadable_file" and st7 != 200 and st8 == 200 and legacy_ok)
    return ok, (f"own={st1} {d1} registry={reg!r} extracted_doc={doc!r} foreign_wf={st2} pathlike_fileid={st3} foreign_fileid={st5} "
                f"refused_rows={refused} foreign_row_kept={foreign_row == f'{v.install_id}|wfOtherOwner001'} corrupt_pdf={st6} {d6} "
                f"empty_file={st7} own_repeat={st8} legacy={st4} legacy_registry={legacy_reg!r}")


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--only"); ap.add_argument("--compat-closed", action="store_true"); a = ap.parse_args()
    if not AUTH_SECRET:
        sys.exit("no authSecret in staging secrets — restart staging via bootstrap.py")
    run_id = uuid.uuid4().hex[:10]
    ctx = {"run_id": run_id}
    passed = failed = 0
    wait_ready()
    sweep_stale_mock_profiles()
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
        for o in OWNERS:
            try:
                print(f"   cleanup {o.tag}:", fixture(*(["delete", "--app-user-id", o.app_user_id] + [x for p in o.profiles for x in ("--profile-id", p)])))
            except Exception as e:
                print(f"   cleanup {o.tag} failed:", e)
    print(f"\n{passed} pass, {failed} fail")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""STAGING e2e for A1 (stage 1): Auth Session + Wappi Proxy + gated webhooks against the Wappi mock.

    python3 Tools/n8n/staging/e2e-auth.py            # all scenarios (fixtures scoped by run id, cleaned up)
    python3 Tools/n8n/staging/e2e-auth.py --only proxy

Scenarios state the CORRECT behaviour: owner A never reads or changes owner B's objects through any
endpoint; unknown/expired/forged sessions are rejected before any side effect; legitimate flows work.
"""
import argparse
import atexit
import base64
import copy
import hashlib
import json
import os
import re
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.parse
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


def rc_fixture_id(run_id, tag):
    """A fresh RevenueCat-shaped fixture id per Owner (R2 binds an id to ONE install: suites that create several
    owners with the same tag must not collide). The e2e0e2e0 prefix marks it for fixtures.py's scoped delete."""
    import hashlib
    return "$RCAnonymousID:e2e0e2e0" + hashlib.sha1(f"{run_id}-{tag}-{uuid.uuid4().hex}".encode()).hexdigest()[:24]


OWNERS = []   # every Owner this process creates — the suite cleans up ALL of them (not just ctx["A"]/["B"])


class Owner:
    def __init__(self, tag, run_id, app_user_id=None):
        self.tag = tag
        # R2: a session's billing identity must be a RevenueCat anonymous id; fixtures carry the e2e0e2e0 marker
        self.app_user_id = app_user_id or rc_fixture_id(run_id, tag)
        self.profiles = []
        OWNERS.append(self)                      # registered before anything can fail, so cleanup always sees it
        fixture("create-subscriber", "--run-id", f"{run_id}-{tag}", "--app-user-id", self.app_user_id)
        st, d = session({"op": "bootstrap", "platform": "test", "appVersion": "e2e", "appUserId": self.app_user_id})
        assert st == 200 and d.get("success"), f"bootstrap {tag}: {st} {d}"
        self.install_id, self.secret, self.token = d["installId"], d["installSecret"], d["token"]
        self.boot_app_user_id = d.get("appUserId")
        # V3: the unauthenticated bootstrap binds no billing id; the client refreshes right after it (same session op)
        self.refresh()

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


@scenario("claim_by_code_transfers_ownership", "a legacy/other-install profile: claim.start sends a code to the bot's OWN chat; claim.verify binds it to the claiming install — A3: and the row's app_user_id follows the claimant's billing id (a wrong code moves nothing)")
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
    key = lambda: pg(f"select app_user_id from bot_profiles where profile_id = '{victim}';")
    key_before = key()
    st3, d3 = session({"op": "claim.verify", "installId": a.install_id, "installSecret": a.secret, "appUserId": a.app_user_id, "profileId": victim, "channel": "whatsapp", "code": "000000" if code != "000000" else "111111"})
    key_after_wrong = key()
    st4, d4 = session({"op": "claim.verify", "installId": a.install_id, "installSecret": a.secret, "appUserId": a.app_user_id, "profileId": victim, "channel": "whatsapp", "code": code})
    key_after = key()
    a.token = d4.get("token", a.token)
    b_after = b.refresh()
    ok = (recipient == "77001112233" and d2.get("codeReused") is True and len(sends2) == 1 and st3 == 403 and d3.get("error") == "claim_code_rejected"
          and st4 == 200 and victim in d4.get("profiles", []) and victim not in b_after["profiles"]
          # A3 (spec §1): the claimed row follows the claimant's billing id (the token's u) — a wrong code moves nothing
          and key_before == b.app_user_id and key_after_wrong == b.app_user_id and key_after == a.app_user_id == d4.get("appUserId"))
    if ok:
        a.profiles.append(victim)
    return ok, (f"recipient_is_own_phone={recipient == '77001112233'} second_start_reuses_code={d2.get('codeReused')} wrong_code={st3} verify={st4} "
                f"now_owned_by_A={victim in d4.get('profiles', [])} B_lost_it={victim not in b_after['profiles']} "
                f"app_user_id B's={key_before == b.app_user_id} after a wrong code B's={key_after_wrong == b.app_user_id} after the code A's={key_after == a.app_user_id}")


@scenario("claim_reauth_then_code_binds", "V2: an UNAUTHORISED legacy profile: claim.start → reauth; a pairing alone binds NOTHING (claim.status); claim.start then sends ONE code into the paired account's own chat and claim.verify binds it")
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
    time.sleep(1.2)
    import datetime as dt
    http("POST", f"{MOCK}/__mock/profile", {"profile_id": legacy, "authorized": True, "phone": "77009998877", "authorized_at": dt.datetime.now(dt.timezone.utc).isoformat()})
    st1, d1 = session({"op": "claim.status", "installId": a.install_id, "installSecret": a.secret, "appUserId": a.app_user_id, "profileId": legacy, "channel": "whatsapp"})
    since = mock_total()
    st2, d2 = claim_call(a, "claim.start", legacy)
    sends, _ = mock_calls(since, "message/send")
    code = "".join(ch for ch in (sends[0]["body"].get("body", "") if sends else "") if ch.isdigit())[-6:]
    st3, d3 = claim_call(a, "claim.verify", legacy, code)
    a.token = (d3 or {}).get("token", a.token)
    ok = (st_qr == 200 and st_chats == 403 and st1 == 200 and legacy not in d1.get("profiles", []) and d1.get("claimMethod") == "pending"
          and st2 == 200 and (d2 or {}).get("claimMethod") == "code" and len(sends) == 1 and sends[0]["body"].get("recipient") == "77009998877"
          and st3 == 200 and legacy in (d3 or {}).get("profiles", []))
    return ok, (f"qr_allowed={st_qr} chats_refused={st_chats} pairing_alone_owned={legacy in d1.get('profiles', [])}/{d1.get('claimMethod')} "
                f"start={st2}/{(d2 or {}).get('claimMethod')} sends={len(sends)} to={sends[0]['body'].get('recipient') if sends else None} "
                f"verify={st3} owned={legacy in (d3 or {}).get('profiles', [])}")


@scenario("claim_reauth_independent_reconnect_not_transferred", "V2: the claimant requests a QR, the OWNER reconnects the owner-less profile independently → the claimant gets nothing (claim.status), its claim.start sends the code into the OWNER's chat, a wrong code is 403 and the row stays owner-less — WhatsApp and Telegram")
def s_claim_independent(ctx):
    import datetime as dt
    res = {}
    for ch, owner_phone in (("whatsapp", "77001119900"), ("telegram", "77001119911")):
        x = fresh_owner(ctx, f"v2x{ch[:2]}")
        legacy = f"legacy-v2-{ch[:2]}-{ctx['run_id']}"
        x.profiles.append(legacy)
        http("POST", f"{MOCK}/__mock/profile", {"profile_id": legacy, "authorized": False, "phone": None})
        st0, d0 = claim_call(x, "claim.start", legacy, channel=ch)
        st_qr, _ = proxy((d0 or {}).get("token"), {"op": "qr", "channel": ch, "profile_id": legacy})
        time.sleep(1.2)
        prof = {"profile_id": legacy, "authorized": True, "phone": owner_phone, "authorized_at": dt.datetime.now(dt.timezone.utc).isoformat()}
        if ch == "telegram":
            prof["account_phone"] = owner_phone
        http("POST", f"{MOCK}/__mock/profile", prof)                    # the owner reconnects (legacy client / Wappi cabinet)
        st1, d1 = claim_call(x, "claim.status", legacy, channel=ch)
        since = mock_total()
        st2, d2 = claim_call(x, "claim.start", legacy, channel=ch)
        sends, _ = mock_calls(since, "message/send")
        st3, d3 = claim_call(x, "claim.verify", legacy, "000000", channel=ch)
        owner = pg(f"select coalesce(owner_id, '-') from bot_profiles where profile_id = '{legacy}';")
        res[ch] = {"start": (st0, (d0 or {}).get("claimMethod")), "qr": st_qr, "status_owned": legacy in (d1 or {}).get("profiles", []),
                   "start2": (st2, (d2 or {}).get("claimMethod")), "sends": [(c["path"], c["body"].get("recipient")) for c in sends],
                   "wrong_code": (st3, (d3 or {}).get("error")), "owner": owner}
    ok = all(r["start"] == (200, "reauth") and r["qr"] == 200 and r["status_owned"] is False and r["start2"] == (200, "code")
             and len(r["sends"]) == 1 and r["sends"][0][1] == ph and ("/tapi/" in r["sends"][0][0]) == (ch == "telegram")
             and r["wrong_code"] == (403, "claim_code_rejected") and r["owner"] == "-"
             for (ch, r), ph in zip(res.items(), ("77001119900", "77001119911")))
    return ok, json.dumps(res, ensure_ascii=False)


@scenario("claim_code_send_failure_is_reported", "V2: a code the transport refuses is reported (502 code_send_failed) and cleared — the retry sends a NEW code (never «reused») and that code binds; a Telegram code goes to the authorised account (account.phone), not the top-level phone")
def s_claim_send_failure(ctx):
    res = {}
    for ch, top, acct in (("whatsapp", "77002220000", None), ("telegram", "77002220009", "77002220001")):
        x = fresh_owner(ctx, f"v2s{ch[:2]}")
        legacy = f"legacy-v2s-{ch[:2]}-{ctx['run_id']}"
        x.profiles.append(legacy)
        prof = {"profile_id": legacy, "authorized": True, "phone": top}
        if acct:
            prof["account_phone"] = acct
        http("POST", f"{MOCK}/__mock/profile", prof)
        http("POST", f"{MOCK}/__mock/fault", {"match": "message/send", "mode": "status", "status": 500, "profile_id": legacy})
        st1, d1 = claim_call(x, "claim.start", legacy, channel=ch)
        since = mock_total()
        st2, d2 = claim_call(x, "claim.start", legacy, channel=ch)
        sends, _ = mock_calls(since, "message/send")
        code = "".join(c for c in (sends[0]["body"].get("body", "") if sends else "") if c.isdigit())[-6:]
        st3, d3 = claim_call(x, "claim.verify", legacy, code, channel=ch)
        res[ch] = {"failed": (st1, (d1 or {}).get("error")), "retry": (st2, (d2 or {}).get("claimMethod"), (d2 or {}).get("codeReused")),
                   "sends": [c["body"].get("recipient") for c in sends], "verify": st3, "owned": legacy in (d3 or {}).get("profiles", [])}
    ok = all(r["failed"] == (502, "code_send_failed") and r["retry"] == (200, "code", False) and r["sends"] == [ph]
             and r["verify"] == 200 and r["owned"] for r, ph in zip(res.values(), ("77002220000", "77002220001")))
    return ok, json.dumps(res, ensure_ascii=False)


def claim_call(owner, op, pid, code=None, channel="whatsapp"):
    body = {"op": op, "installId": owner.install_id, "installSecret": owner.secret, "appUserId": owner.app_user_id, "profileId": pid, "channel": channel}
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


@scenario("billing_id_first_bind", "R2/V3: a second install naming a billing id another install holds — WITH a live bot or WITHOUT one (a paying owner before the first bot) — gets NO billing identity + billingConflict on bootstrap AND refresh; its GetUsage/profile.add on that id are refused; the bootstrap itself binds nothing; the id follows only once the holder is revoked")
def s_billing_bind(ctx):
    a = fresh_owner(ctx, "r2a")                                   # holds the id and owns a live bot
    b = Owner("r2b", ctx["run_id"], app_user_id=a.app_user_id)   # the same id named by another install
    d_b = b.refresh()
    usage_own = hook("GetUsage", a.token, {"appUserId": a.app_user_id})[0]
    usage_b = hook("GetUsage", b.token, {"appUserId": a.app_user_id})[0]
    add_b, add_body = proxy(b.token, {"op": "profile.add", "channel": "whatsapp", "name": "e2e r2b"})
    holders = pg(f"select string_agg(install_id, ',') from installs where app_user_id = '{a.app_user_id}' and revoked_at is null;")
    pg(f"update installs set revoked_at = now() where install_id = '{a.install_id}';")
    d_b2 = b.refresh()
    with_bot = (d_b.get("appUserId") is None and d_b.get("billingConflict") is True and usage_own == 200 and usage_b == 403
                and add_b == 409 and holders == a.install_id and d_b2.get("appUserId") == a.app_user_id and not d_b2.get("billingConflict"))
    # V3: the holder owns NO bot (paid, before its first bot) — still keeps the id; the bootstrap binds nothing
    c = Owner("v3c", ctx["run_id"])                               # bootstrap + refresh: holds its id, no profile
    pg(f"update subscribers set plan = 'business', status = 'active' where app_user_id = '{c.app_user_id}';")
    x = Owner("v3x", ctx["run_id"], app_user_id=c.app_user_id)
    d_x = x.refresh()
    usage_x = hook("GetUsage", x.token, {"appUserId": c.app_user_id})[0]
    add_x, _ = proxy(x.token, {"op": "profile.add", "channel": "whatsapp", "name": "e2e v3x"})
    add_x_tg, _ = proxy(x.token, {"op": "profile.add", "channel": "telegram", "name": "e2e v3x tg"})
    d_c = c.refresh()
    usage_c = hook("GetUsage", c.token, {"appUserId": c.app_user_id})[0]
    holders_c = pg(f"select string_agg(install_id, ',') from installs where app_user_id = '{c.app_user_id}' and revoked_at is null;")
    pg(f"update installs set revoked_at = now() where install_id = '{c.install_id}';")
    d_x2 = x.refresh()
    no_bot = (c.boot_app_user_id is None and x.boot_app_user_id is None and d_x.get("appUserId") is None and d_x.get("billingConflict") is True
              and usage_x == 403 and add_x == 409 and add_x_tg == 409 and d_c.get("appUserId") == c.app_user_id and not d_c.get("billingConflict")
              and usage_c == 200 and holders_c == c.install_id and d_x2.get("appUserId") == c.app_user_id and not d_x2.get("billingConflict"))
    return with_bot and no_bot, (f"WITH bot: second appUserId={d_b.get('appUserId')} conflict={d_b.get('billingConflict')} usage_own={usage_own} usage_second={usage_b} "
                                 f"profile.add_second={add_b} {add_body} holders={holders == a.install_id} after_revoke={d_b2.get('appUserId') == a.app_user_id}/{d_b2.get('billingConflict')} | "
                                 f"NO bot (V3): bootstrap_binds={c.boot_app_user_id}/{x.boot_app_user_id} second={d_x.get('appUserId')}/{d_x.get('billingConflict')} "
                                 f"usage_second={usage_x} profile.add_second={add_x}/tg {add_x_tg} holder_refresh={d_c.get('appUserId') == c.app_user_id}/{d_c.get('billingConflict')} "
                                 f"usage_holder={usage_c} holders={holders_c == c.install_id} after_revoke={d_x2.get('appUserId') == c.app_user_id}/{d_x2.get('billingConflict')}")


@scenario("create_rejects_empty_appuserid_in_session", "R2: CreateWhatsappWorkflow/CreateTelegramWorkflow with an EMPTY AppUserID in session mode are refused before any write (the empty id skipped Count Channels and let the quota fail open)")
def s_create_empty_billing(ctx):
    o = fresh_owner(ctx, "r2e")
    tg = o.add_profile("telegram")
    base = {"Name": f"e2e-r2-{ctx['run_id']}", "BusinessType": "Цветы", "BusinessTypeId": "flowers", "Business": "", "Prompt": "",
            "ProductsList": "", "ServicesList": "", "AppUserID": "", "BotKey": f"Bot-r2-{ctx['run_id']}", "ReplyMode": "auto", "ReplyModeVersion": "1"}
    st1, d1 = hook_form("CreateWhatsappWorkflow", o.token, {**base, "WhatsappProfileId": o.profiles[0], "TelegramWorkflowId": "-1"})
    st2, d2 = hook_form("CreateTelegramWorkflow", o.token, {**base, "TelegramProfileId": tg, "WhatsappWorkflowId": "-1"})
    bound = pg(f"select count(*) from bot_profiles where profile_id in ('{o.profiles[0]}', '{tg}') and workflow_id is not null;")
    refused = lambda d: isinstance(d, dict) and d.get("success") is False and str(d.get("reason", "")).startswith("forbidden:AppUserID")
    ok = refused(d1) and refused(d2) and bound == "0"
    return ok, f"wa={st1} {d1} tg={st2} {d2} clones_bound={bound}"


def clones_for(pid):
    """(id, active) of every staging workflow whose webhook path is this profile (the clone's first node)."""
    found, cursor = [], None
    while True:
        st, d = http("GET", f"{BASE}/api/v1/workflows?limit=250" + (f"&cursor={cursor}" if cursor else ""),
                     headers={"X-N8N-API-KEY": SEC.get("apiKey", "")})
        for w in (d or {}).get("data", []):
            if not w.get("isArchived") and any(n.get("type") == "n8n-nodes-base.webhook" and (n.get("parameters") or {}).get("path") == pid
                                               for n in w.get("nodes", [])):
                found.append((w["id"], w.get("active")))
        cursor = (d or {}).get("nextCursor")
        if st != 200 or not cursor:
            return found


CREATE_WORKFLOW_IDS = {"CreateWhatsappWorkflow": "XuvOp7TxOImOAmlj", "CreateTelegramWorkflow": "Uz6HBBUpAiUqVysB"}


def newest_execution(workflow_id, since_id):
    """The newest execution of `workflow_id` after `since_id`, with its run data (None when there is none yet)."""
    for _ in range(20):
        st, page = http("GET", f"{BASE}/api/v1/executions?workflowId={workflow_id}&limit=5&includeData=false",
                        headers={"X-N8N-API-KEY": SEC.get("apiKey", "")})
        rows = [e for e in (page or {}).get("data", []) if int(e["id"]) > since_id and e.get("finished")]
        if rows:
            eid = max(int(e["id"]) for e in rows)
            return http("GET", f"{BASE}/api/v1/executions/{eid}?includeData=true", headers={"X-N8N-API-KEY": SEC.get("apiKey", "")})[1]
        time.sleep(0.5)
    return None


def newest_execution_id(workflow_id):
    st, page = http("GET", f"{BASE}/api/v1/executions?workflowId={workflow_id}&limit=1&includeData=false",
                    headers={"X-N8N-API-KEY": SEC.get("apiKey", "")})
    return max([int(e["id"]) for e in (page or {}).get("data", [])] or [0])


def ran_nodes(exec_data):
    return set(((exec_data or {}).get("data") or {}).get("resultData", {}).get("runData", {}) or {})


@scenario("create_seed_failure_keeps_clone_inactive", "R8 + R8-deletefail: a «Вместе» create whose reply-mode seed fails (a real Postgres failure on staging, scoped to this profile) answers reply_mode_not_saved and never CREATES a clone (the execution reads no template and runs no Create Workflow, so nothing is active, bound, hooked or left to delete); the retry once the DB is back creates exactly ONE live clone with the '*' row — WhatsApp and Telegram")
def s_create_seed_failure(ctx):
    res = {}
    for ch, form, field in (("wa", "CreateWhatsappWorkflow", "WhatsappProfileId"), ("tg", "CreateTelegramWorkflow", "TelegramProfileId")):
        o = Owner(f"r8{ch}", ctx["run_id"])
        pid = o.add_profile("whatsapp" if ch == "wa" else "telegram")
        base = {"Name": f"e2e-r8-{ctx['run_id']}", "BusinessType": "Цветы", "BusinessTypeId": "flowers", "Business": "", "Prompt": "",
                "ProductsList": "", "ServicesList": "", "AppUserID": o.app_user_id, "BotKey": f"Bot-r8{ch}-{ctx['run_id']}",
                "ReplyMode": "semi", field: pid, ("TelegramWorkflowId" if ch == "wa" else "WhatsappWorkflowId"): "-1"}
        con = f"r8_{ctx['run_id']}_{ch}"
        pg(f"alter table reply_mode_flags add constraint {con} check (profile_id <> '{pid}') not valid;")   # staging only, this profile only
        try:
            since = mock_total()
            exec_since = newest_execution_id(CREATE_WORKFLOW_IDS[form])
            st1, d1 = hook_form(form, o.token, {**base, "ReplyModeVersion": "150"})
            ran1 = ran_nodes(newest_execution(CREATE_WORKFLOW_IDS[form], exec_since))
            hooks1 = [c for c in mock_calls(since, "webhook/url/set")[0] if pid in json.dumps(c)]
            bound1 = pg(f"select coalesce(workflow_id, '-') from bot_profiles where profile_id = '{pid}';")
            clones1 = clones_for(pid)
        finally:
            pg(f"alter table reply_mode_flags drop constraint if exists {con};")
        o.refresh()
        st2, d2 = hook_form(form, o.token, {**base, "ReplyModeVersion": "151"})
        wid = (d2 or {}).get("id") if isinstance(d2, dict) else None
        clones2 = clones_for(pid)
        row = pg(f"select suppressed || '|' || version from reply_mode_flags where profile_id = '{pid}' and chat_id = '*';")
        o.refresh()
        st3 = hook("SetWorkflowState", o.token, {"workflowId": wid, "action": "delete"})[0] if wid else 0
        for left, _ in clones_for(pid):          # a regressed graph leaves the failed create's clone behind: staging API cleanup
            http("DELETE", f"{BASE}/api/v1/workflows/{left}", headers={"X-N8N-API-KEY": SEC.get("apiKey", "")})
        res[ch] = {"failed_create": (st1, d1), "wappi_webhook_calls": len(hooks1), "bound": bound1, "clones_after_failure": clones1,
                   "failed_execution": {n: (n in ran1) for n in ("Seed Reply Mode", "Get Sample Workflow", "Create Workflow",
                                                                 "Respond Reply Mode Not Saved")},
                   "retry": (st2, wid), "clones_after_retry": clones2, "row": row, "cleanup_delete": st3}
    # R8-deletefail: the clone is never created — told apart from «created, then deleted» by the execution's own run data
    never_created = {"Seed Reply Mode": True, "Get Sample Workflow": False, "Create Workflow": False, "Respond Reply Mode Not Saved": True}
    ok = all(r["failed_create"] == (200, {"success": False, "error": "reply_mode_not_saved"}) and r["wappi_webhook_calls"] == 0
             and r["failed_execution"] == never_created
             and r["bound"] in ("", "-") and r["clones_after_failure"] == [] and r["retry"][0] == 200 and r["retry"][1]
             and r["clones_after_retry"] == [(r["retry"][1], True)] and r["row"] == "true|151" and r["cleanup_delete"] == 200
             for r in res.values())
    return ok, json.dumps(res, ensure_ascii=False)


@scenario("sql_comment_values_run_nothing", "V1: a value whose newline would end an SQL comment stays a literal on the real n8n Postgres node — Delete File (session token and token-less), Suggest Replies, AuthSession bootstrap, Create WhatsApp and Create Telegram each run their statement WITH the value (row proof) and no extra statement (no marker table)")
def s_sql_comment_values(ctx):
    run, res, markers = ctx["run_id"], {}, []

    def payload(tag):
        m = f"crv1_{run}_{tag}"
        markers.append(m)
        return m, f"v1-{run}-{tag}\n; create table public.{m}(x int); --"

    def made(m):
        return pg(f"select to_regclass('public.{m}') is not null;") == "t"

    legacy = lambda st, reached: (st == 401) if COMPAT_CLOSED else reached   # token-less calls exist only in the compat window
    installs = []
    try:
        o = fresh_owner(ctx, "v1")
        tg = o.add_profile("telegram")
        # Delete File, session token: the value names A's registered file with one chunk → that chunk goes, nothing else runs
        m, v = payload("delsession")
        pg(f"insert into uploaded_files (file_id, owner_id) values ('{v}', '{o.install_id}'); "
           f"insert into documents (content, metadata) values ('v1', jsonb_build_object('fileId', '{v}'));")
        st, d = hook("DeleteFile", o.token, {"fileId": v})
        res["delete_session"] = {"http": st, "deletedChunks": (d or {}).get("deletedChunks"), "marker": made(m)}
        ok = st == 200 and (d or {}).get("deletedChunks") == 1 and not made(m)
        # Delete File without a token (legacy file: chunk, no registry row)
        m, v = payload("dellegacy")
        pg(f"insert into documents (content, metadata) values ('v1', jsonb_build_object('fileId', '{v}'));")
        st, d = hook("DeleteFile", None, {"fileId": v})
        res["delete_tokenless"] = {"http": st, "deletedChunks": (d or {}).get("deletedChunks"), "marker": made(m)}
        ok &= legacy(st, st == 200 and (d or {}).get("deletedChunks") == 1) and not made(m)
        # Suggest Replies (token-less): an entitled id at the daily cap — the gate counts the value (100 → 101) and refuses
        # before any LLM call
        m, v = payload("suggest")
        pg(f"insert into subscribers (app_user_id, plan, status) values ('{v}', 'business', 'active'); "
           f"insert into suggestion_counts (app_user_id, d, n) values ('{v}', (now() at time zone 'Asia/Almaty')::date, 100);")
        st, d = hook("SuggestReplies", None, {"v": 1, "requestSeq": 1, "chatId": "77010001234@c.us", "channel": "whatsapp",
                                             "messages": [{"role": "client", "text": "цена?", "ts": 1754800000}], "appUserId": v})
        n = pg(f"select n from suggestion_counts where app_user_id = '{v}';")
        res["suggest_tokenless"] = {"http": st, "body": d, "count_after": n, "marker": made(m)}
        ok &= legacy(st, st == 200 and n == "101") and not made(m)
        # AuthSession bootstrap (unauthenticated by design): the value is the billing id — the install is minted, unbound
        m, v = payload("bootstrap")
        st, d = session({"op": "bootstrap", "platform": "test", "appVersion": "e2e", "appUserId": v})
        if (d or {}).get("installId"):
            installs.append(d["installId"])
        row = pg(f"select coalesce(app_user_id, '-') from installs where install_id = '{(d or {}).get('installId', '')}';")
        res["bootstrap"] = {"http": st, "install": bool((d or {}).get("installId")), "app_user_id": row, "marker": made(m)}
        ok &= st == 200 and row == "-" and not made(m)
        # Create WhatsApp / Telegram (token-less): AppUserID carries the value through Ensure Subscriber → Register Profile;
        # Register Profile ran iff the profile now carries this call's BotKey
        for ch, form, field, pid in (("wa", "CreateWhatsappWorkflow", "WhatsappProfileId", o.profiles[0]),
                                     ("tg", "CreateTelegramWorkflow", "TelegramProfileId", tg)):
            m, v = payload(f"create{ch}")
            key = f"Bot-v1{ch}-{run}"
            st, d = hook_form(form, None, {"Name": f"e2e-v1-{run}", "BusinessType": "Цветы", "BusinessTypeId": "flowers",
                                           "Business": "", "Prompt": "", "ProductsList": "", "ServicesList": "", "AppUserID": v,
                                           "BotKey": key, field: pid,
                                           ("TelegramWorkflowId" if ch == "wa" else "WhatsappWorkflowId"): "-1"})
            got_key = pg(f"select coalesce(bot_key, '-') from bot_profiles where profile_id = '{pid}';")
            res[f"create_{ch}_tokenless"] = {"http": st, "id": (d or {}).get("id") if isinstance(d, dict) else None,
                                             "bot_key_written": got_key == key, "marker": made(m)}
            ok &= legacy(st, st == 200 and got_key == key) and not made(m)
            for left, _ in clones_for(pid):     # the legacy create published a clone: staging API cleanup
                http("DELETE", f"{BASE}/api/v1/workflows/{left}", headers={"X-N8N-API-KEY": SEC.get("apiKey", "")})
    finally:
        pg(f"delete from documents where metadata->>'fileId' like 'v1-{run}-%'; delete from uploaded_files where file_id like 'v1-{run}-%'; "
           f"delete from suggestion_counts where app_user_id like 'v1-{run}-%'; delete from suggestion_request_keys where app_user_id like 'v1-{run}-%'; "
           f"delete from subscribers where app_user_id like 'v1-{run}-%';"
           + "".join(f" delete from installs where install_id = '{i}';" for i in installs)
           + "".join(f" drop table if exists public.{m};" for m in markers))
    return ok, json.dumps(res, ensure_ascii=False)


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


@scenario("upload_price_list_multipart", "R3: a real .txt price list sent the way WWWForm sends it passes the gate with its file, is registered FIRST, reaches extraction (a staging `documents` row with the text + the bot's ids); a foreign workflow id, a non-GUID fileId or another install's fileId is 403 before any write; an unreadable PDF is 422, an empty file writes nothing, an own repeat keeps one registry row. V4: another bot's owner-less file (WhatsApp) and another bot's unregistered chunks (Telegram) are neither taken nor deleted by a session (403, row and chunk untouched); the caller's own compat-window file and its own upload are deleted (200)")
def s_upload(ctx):
    u = fresh_owner(ctx, "up")
    v = fresh_owner(ctx, "upv")                                              # another install
    wf_own = ("wfup" + ctx["run_id"])[:16]
    pid = u.profiles[0]
    pg(f"update bot_profiles set workflow_id = '{wf_own}' where profile_id = '{pid}' and owner_id = '{u.install_id}';")   # as Bind Workflow Id would after Create
    d0 = u.refresh()
    if wf_own not in d0.get("workflows", []):
        return False, f"fixture: token lacks the bot's workflow id: {d0.get('workflows')}"
    ids = [str(uuid.uuid4()) for _ in range(10)]
    pg(f"insert into uploaded_files (file_id, owner_id, bot_wa_id) values ('{ids[4]}', '{v.install_id}', 'wfOtherOwner001');")
    # V4 fixtures: [7] another bot's owner-less (compat-window) WhatsApp file; [8] the caller's OWN compat-window file on
    # its Telegram column; [9] another bot's pre-A1 Telegram chunks with no registry row at all
    pg(f"insert into uploaded_files (file_id, owner_id, bot_wa_id) values ('{ids[7]}', null, 'wfVictimWA00001'); "
       f"insert into documents (content, metadata) values ('victim', jsonb_build_object('fileId', '{ids[7]}', 'botWaId', 'wfVictimWA00001', 'botTgId', '-1')); "
       f"insert into uploaded_files (file_id, owner_id, bot_tg_id) values ('{ids[8]}', null, '{wf_own}'); "
       f"insert into documents (content, metadata) values ('own compat', jsonb_build_object('fileId', '{ids[8]}', 'botWaId', '-1', 'botTgId', '{wf_own}')); "
       f"insert into documents (content, metadata) values ('victim tg', jsonb_build_object('fileId', '{ids[9]}', 'botWaId', '-1', 'botTgId', 'wfVictimTG00001'));")
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
        # V4: knowing a fileId gives nothing — another bot's owner-less file is not adopted and not deletable, an
        # unregistered fileId with another bot's chunks is not registered; the caller's own compat file and its own
        # upload are deletable (Delete File through the real gate + Postgres node for the first time in e2e)
        st9, _ = upload(u.token, {**own, "fileId": ids[7]}, text)
        st10, d10 = hook("DeleteFile", u.token, {"fileId": ids[7]})
        victim = pg(f"select coalesce((select owner_id from uploaded_files where file_id = '{ids[7]}'), '-') || '|' || "
                    f"(select count(*) from documents where metadata->>'fileId' = '{ids[7]}') || '|' || "
                    f"(select (deleted_at is null)::text from uploaded_files where file_id = '{ids[7]}');")
        st11, _ = upload(u.token, {**base, "whatsappWorkflowId": "-1", "telegramWorkflowId": wf_own, "fileId": ids[9]}, text)
        st12, d12 = hook("DeleteFile", u.token, {"fileId": ids[9]})
        victim_tg = pg(f"select (select count(*) from uploaded_files where file_id = '{ids[9]}') || '|' || "
                       f"(select count(*) from documents where metadata->>'fileId' = '{ids[9]}');")
        st13, d13 = hook("DeleteFile", u.token, {"fileId": ids[8]})
        st14, d14 = hook("DeleteFile", u.token, {"fileId": ids[0]})
        own_left = pg(f"select (select count(*) from documents where metadata->>'fileId' in ('{ids[0]}', '{ids[8]}')) || '|' || "
                      f"(select count(*) from uploaded_files where file_id in ('{ids[0]}', '{ids[8]}') and deleted_at is not null);")
    finally:
        pg(f"delete from documents where metadata->>'fileId' in ({', '.join(repr(i) for i in ids)}); delete from uploaded_files where file_id in ({', '.join(repr(i) for i in ids)});")
    legacy_ok = (st4 == 401 and legacy_reg == "") if COMPAT_CLOSED else (st4 == 200 and legacy_reg == "-")
    ok = (st1 == 200 and (d1 or {}).get("success") is True and (d1 or {}).get("fileId") == ids[0]
          and reg == f"{u.install_id}|{wf_own}|-|products|price.txt|1" and doc.startswith(f"true|{wf_own}|-1|{ids[0]}")
          and st2 == 403 and st3 == 403 and st5 == 403 and refused == "0" and foreign_row == f"{v.install_id}|wfOtherOwner001"
          and st6 == 422 and (d6 or {}).get("error") == "unreadable_file" and st7 != 200 and st8 == 200 and legacy_ok
          and st9 == 403 and st10 == 403 and (d10 or {}).get("error") == "forbidden" and victim == "-|1|true"
          and st11 == 403 and st12 == 403 and victim_tg == "0|1"
          and st13 == 200 and (d13 or {}).get("deletedChunks") == 1
          and st14 == 200 and ((d14 or {}).get("deletedChunks") or 0) >= 1 and own_left == "0|2")
    return ok, (f"own={st1} {d1} registry={reg!r} extracted_doc={doc!r} foreign_wf={st2} pathlike_fileid={st3} foreign_fileid={st5} "
                f"refused_rows={refused} foreign_row_kept={foreign_row == f'{v.install_id}|wfOtherOwner001'} corrupt_pdf={st6} {d6} "
                f"empty_file={st7} own_repeat={st8} legacy={st4} legacy_registry={legacy_reg!r} | V4 owner-less foreign: upload={st9} "
                f"delete={st10} {d10} owner|chunks|live={victim!r}; unregistered foreign tg: upload={st11} delete={st12} rows|chunks={victim_tg!r}; "
                f"own compat delete={st13} {d13}; own upload delete={st14} {d14}; own chunks left|rows marked={own_left!r}")


def admin_api(method, path, body=None):
    return http(method, f"{BASE}/api/v1{path}", body, headers={"X-N8N-API-KEY": SEC.get("apiKey", "")})


# Insights hygiene (A7, 2026-10-15). n8n flushes its insights buffer every 30 s; a flush that names a workflow deleted since
# its last execution fails and is retried FOREVER («Error while saving insights metadata and raw data» every 30 s until the
# server restarts — the prod rule: delete a temporary workflow ≥ 35 s after its last execution, .secrets/sql-oneoff.py and
# implementation-progress.md 2026-10-13). A probe or a clone that RAN is therefore switched off at once (its webhook is gone
# for the rest of the run) and DELETED later, in one batch, ≥ RAN_WORKFLOW_DELETE_DELAY_S after the last switch-off: at
# the end of main() and, for the suites that import these helpers (e2e-handoff, e2e-suggest, e2e-test-dialog), at exit.
RAN_WORKFLOW_DELETE_DELAY_S = 35
_DEFERRED_DELETES = []        # [(workflow id, time.monotonic() of its switch-off)]


def defer_delete(wid):
    """Switch a workflow that may have run off NOW; delete it in flush_deferred_deletes (≥ 35 s later)."""
    if wid:
        admin_api("POST", f"/workflows/{wid}/deactivate")
        _DEFERRED_DELETES.append((wid, time.monotonic()))


def flush_deferred_deletes():
    if not _DEFERRED_DELETES:
        return
    wait = RAN_WORKFLOW_DELETE_DELAY_S - (time.monotonic() - max(t for _, t in _DEFERRED_DELETES))
    if wait > 0:
        print(f"   waiting {wait:.0f}s before deleting {len(_DEFERRED_DELETES)} workflow(s) that ran (n8n insights flush, ≥ 35 s rule)")
        time.sleep(wait)
    while _DEFERRED_DELETES:
        admin_api("DELETE", f"/workflows/{_DEFERRED_DELETES.pop()[0]}")


atexit.register(flush_deferred_deletes)


def create_bot_clones(ctx, owner, tag):
    """A «Вместе» bot with BOTH channels through the real Create WA/TG handlers (trial plan: 3 channels)."""
    wa = owner.profiles[0]
    tg = owner.add_profile("telegram")
    base = {"Name": f"e2e-{tag}-{ctx['run_id']}", "BusinessType": "Цветы", "BusinessTypeId": "flowers", "Business": "", "Prompt": "",
            "ProductsList": "", "ServicesList": "", "AppUserID": owner.app_user_id, "BotKey": f"Bot-{tag}-{ctx['run_id']}",
            "ReplyMode": "semi", "ReplyModeVersion": "150"}
    st1, d1 = hook_form("CreateWhatsappWorkflow", owner.token, {**base, "WhatsappProfileId": wa, "TelegramWorkflowId": "-1"})
    wwa = (d1 or {}).get("id") if isinstance(d1, dict) else None
    owner.refresh()
    st2, d2 = hook_form("CreateTelegramWorkflow", owner.token, {**base, "TelegramProfileId": tg, "WhatsappWorkflowId": wwa or "-1"})
    wtg = (d2 or {}).get("id") if isinstance(d2, dict) else None
    owner.refresh()
    return wa, tg, wwa, wtg, (st1, st2)


def seed_bot_rows(pid, wid, key, file_id):
    pg(f"insert into n8n_chat_histories (session_id, message) values ('{pid}:77001@c.us', '{{\"type\":\"human\",\"content\":\"v12\"}}'::jsonb);"
       f"insert into chat_history_events (profile_id, chat_id, message_id, source, body, msg_type, sent_at) values ('{pid}', '77001@c.us', 'v12-{pid}', 'client', 'v12', 'chat', now());"
       f"insert into documents (content, metadata) values ('v12 price', '{{\"{key}\": \"{wid}\", \"fileId\": \"{file_id}\"}}'::jsonb);")


def bot_rows_left(pid, wid, key):
    return pg(f"select (select count(*) from n8n_chat_histories where split_part(session_id, ':', 1) = '{pid}') || '|' || "
              f"(select count(*) from chat_history_events where profile_id = '{pid}') || '|' || "
              f"(select count(*) from documents where metadata->>'{key}' = '{wid}') || '|' || "
              f"(select count(*) from reply_mode_flags where profile_id = '{pid}');")


@scenario("retired_bot_owner_can_delete", "V12: the owner of a bot the sweep retired (trial over: its rows deleted, its token lists nothing) deletes its memory, history and files (200, real counters) and both clones (200; a repeat is 200 — idempotent); activating the retired clone and another install naming the same ids are 403 (reason owned_elsewhere) — WhatsApp and Telegram")
def s_retired_delete(ctx):
    o = fresh_owner(ctx, "rd")
    wa, tg, wwa, wtg, created = create_bot_clones(ctx, o, "rd")
    if not (wwa and wtg):
        return False, f"setup: create {created} wa={wwa} tg={wtg}"
    fid = str(uuid.uuid4())
    seed_bot_rows(wa, wwa, "botWaId", fid)
    seed_bot_rows(tg, wtg, "botTgId", fid)
    pg(f"update bot_profiles set deleted_at = now(), deleted_reason = 'trial_expiry' where profile_id in ('{wa}', '{tg}');")
    tok = o.refresh()
    body = {"botWaId": wwa, "botTgId": wtg, "waProfileId": wa, "tgProfileId": tg, "appUserId": o.app_user_id}
    x = Owner("rdx", ctx["run_id"])                                   # another install naming the same ids
    foreign_dbf, dx = hook("DeleteBotFiles", x.token, body)
    foreign_sws = hook("SetWorkflowState", x.token, {"workflowId": wwa, "action": "delete"})[0]
    activate = hook("SetWorkflowState", o.token, {"workflowId": wwa, "action": "activate"})[0]
    before = (bot_rows_left(wa, wwa, "botWaId"), bot_rows_left(tg, wtg, "botTgId"))
    own_dbf, d = hook("DeleteBotFiles", o.token, body)
    after = (bot_rows_left(wa, wwa, "botWaId"), bot_rows_left(tg, wtg, "botTgId"))
    deletes = [hook("SetWorkflowState", o.token, {"workflowId": w, "action": "delete"})[0] for w in (wwa, wwa, wtg)]
    gone = [admin_api("GET", f"/workflows/{w}")[0] for w in (wwa, wtg)]
    ok = (not tok.get("profiles") and not tok.get("workflows")
          and foreign_dbf == 403 and (dx or {}).get("reason") == "owned_elsewhere" and foreign_sws == 403 and activate == 403
          and before == ("1|1|1|1", "1|1|1|1")
          and own_dbf == 200 and isinstance(d, dict) and d.get("success") is True and (d.get("deletedMemoryRows") or 0) >= 2
          and isinstance(d.get("deletedChatStates"), int) and after == ("0|0|0|0", "0|0|0|0")
          and deletes == [200, 200, 200] and gone == [404, 404])
    return ok, (f"token after retire={tok.get('profiles')}/{tok.get('workflows')} foreign dbf={foreign_dbf} {dx} foreign sws={foreign_sws} "
                f"activate retired={activate} rows before={before} own dbf={own_dbf} {d} rows after={after} deletes={deletes} GET={gone}")


@scenario("claim_on_retired_profile_is_refused", "M4: claim.start / claim.status / claim.verify on a bot profile the sweep retired (trial over) → 410 profile_retired, retiredReason for its owner only (another install: the same 410 without it), Wappi is never asked, no pending claim, no token; the owner's V12 delete of that same bot then goes through (profile.delete answers a FINAL code the client steps over — not in the token, 403; DeleteBotFiles and SetWorkflowState delete = 200, the clones gone) — WhatsApp and Telegram")
def s_claim_retired(ctx):
    o = fresh_owner(ctx, "m4")
    wa, tg, wwa, wtg, created = create_bot_clones(ctx, o, "m4")
    if not (wwa and wtg):
        return False, f"setup: create {created} wa={wwa} tg={wtg}"
    for pid in (wa, tg):   # the worst case: a liveness false positive — Wappi still reports the profile authorised
        http("POST", f"{MOCK}/__mock/profile", {"profile_id": pid, "authorized": True, "phone": "77003330000"})
    pg(f"update bot_profiles set deleted_at = now(), deleted_reason = 'trial_expiry' where profile_id in ('{wa}', '{tg}');")
    tok = o.refresh()
    x = Owner("m4x", ctx["run_id"])
    since = mock_total()
    res = {}
    for ch, pid in (("whatsapp", wa), ("telegram", tg)):
        for who, owner, op, code in (("own", o, "claim.start", None), ("own", o, "claim.status", None),
                                     ("own", o, "claim.verify", "123456"), ("foreign", x, "claim.start", None)):
            st, d = claim_call(owner, op, pid, code, channel=ch)
            d = d if isinstance(d, dict) else {}
            res[f"{who} {ch} {op}"] = [st, d.get("error"), d.get("retiredReason"), "token" in d]
    wappi = [c["path"] for c in mock_calls(since, "")[0]
             if (c.get("query") or {}).get("profile_id") in (wa, tg) or any(p in json.dumps(c.get("body")) for p in (wa, tg))]
    pending = pg(f"select count(*) from bot_profiles where profile_id in ('{wa}', '{tg}') and (claim_pending_by is not null or claim_code_hash is not null);")
    # V12: the retired bot stays deletable by its owner — the client keeps its ids exactly for this (M4 does not touch them).
    # Manager.DeleteBotOnServer's first step, profile.delete, is refused by Build Request for a profile the token no longer
    # lists (403); BotServerDeletion.ClassifyProfileDelete steps over any non-transient answer, so only 0/408/429/5xx would stop it.
    unlink = [proxy(o.token, {"op": "profile.delete", "channel": ch, "profile_id": pid})[0] for ch, pid in (("whatsapp", wa), ("telegram", tg))]
    dbf, dd = hook("DeleteBotFiles", o.token, {"botWaId": wwa, "botTgId": wtg, "waProfileId": wa, "tgProfileId": tg, "appUserId": o.app_user_id})
    deletes = [hook("SetWorkflowState", o.token, {"workflowId": w, "action": "delete"})[0] for w in (wwa, wtg)]
    gone = [admin_api("GET", f"/workflows/{w}")[0] for w in (wwa, wtg)]
    want_own, want_foreign = [410, "profile_retired", "trial_expiry", False], [410, "profile_retired", None, False]
    ok = (not tok.get("profiles")
          and all(v == (want_own if k.startswith("own") else want_foreign) for k, v in res.items())
          and not wappi and pending == "0"
          and all(isinstance(u, int) and 200 <= u < 500 and u not in (408, 429) for u in unlink) and dbf == 200 and isinstance(dd, dict) and dd.get("success") is True
          and deletes == [200, 200] and gone == [404, 404])
    return ok, (f"token after retire={tok.get('profiles')} answers={json.dumps(res)} wappi calls for the retired ids={wappi} "
                f"claims written={pending} | V12 delete: unlink={unlink} dbf={dbf} {dd} sws={deletes} GET={gone}")


@scenario("stale_token_cannot_unlink_a_claimed_profile", "V12 / M5: a token issued while install A owned a profile still lists it after install B took the profile over — A's profile.delete / profile.logout (proxy), DeleteBotFiles and SetWorkflowState(delete) are all refused (403) and the transport is never called; B's own profile.delete passes — WhatsApp and Telegram")
def s_stale_token(ctx):
    a = fresh_owner(ctx, "st")
    b = Owner("stb", ctx["run_id"])
    wa, tg, wwa, wtg, created = create_bot_clones(ctx, a, "st")
    if not (wwa and wtg):
        return False, f"setup: create {created}"
    stale = a.token                                                   # lists wa, tg, wwa, wtg
    pg(f"update bot_profiles set owner_id = '{b.install_id}' where profile_id in ('{wa}', '{tg}');")   # claimed by code
    b.refresh()
    since = mock_total()
    res = {}
    for ch, pid in (("whatsapp", wa), ("telegram", tg)):
        res[f"{ch} delete"] = proxy(stale, {"op": "profile.delete", "channel": ch, "profile_id": pid})[0]
        res[f"{ch} logout"] = proxy(stale, {"op": "profile.logout", "channel": ch, "profile_id": pid})[0]
    calls = [c for c in mock_calls(since, "profile/")[0] if wa in json.dumps(c) or tg in json.dumps(c)]
    res["dbf"] = hook("DeleteBotFiles", stale, {"botWaId": wwa, "botTgId": wtg, "waProfileId": wa, "tgProfileId": tg, "appUserId": a.app_user_id})[0]
    res["sws"] = hook("SetWorkflowState", stale, {"workflowId": wwa, "action": "delete"})[0]
    res["b delete"] = proxy(b.token, {"op": "profile.delete", "channel": "whatsapp", "profile_id": wa})[0]
    for w in (wwa, wtg):
        hook("SetWorkflowState", b.token, {"workflowId": w, "action": "delete"})
    ok = all(v == 403 for k, v in res.items() if k != "b delete") and not calls and res["b delete"] == 200
    return ok, f"{res} transport calls for A's stale token: {len(calls)}"


def sweep_clone_probe(run_id):
    """A TEMPORARY staging-only workflow: webhook → the DERIVED sweep's own clone branch (Sweep Config + Retired Clones …
    Stamp, credentials already remapped by derive-staging) — runs exactly those nodes on the real n8n; deleted after use."""
    src = json.load(open(os.path.join(N8N_DIR, "workflows-staging", "fXYpCXPKw92EzRz8-Profile_Lifecycle_Sweep.json"), encoding="utf-8"))
    keep = {"Sweep Config", "Retired Clones", "Clone Dry Run?", "Log Would Deactivate", "Has Clone?", "Deactivate Clone", "Clone Off?",
            "Stamp Clone Deactivated", "Stamp Clone Kept"}
    nodes = [n for n in src["nodes"] if n["name"] in keep]
    conns = {k: v for k, v in src["connections"].items() if k in keep and k != "Sweep Config"}
    conns["Sweep Config"] = {"main": [[{"node": "Retired Clones", "type": "main", "index": 0}]]}
    path = f"v12-sweep-probe-{run_id}"
    nodes.append({"parameters": {"httpMethod": "POST", "path": path, "responseMode": "onReceived", "options": {}}, "id": "probe-webhook",
                  "name": "Webhook", "type": "n8n-nodes-base.webhook", "typeVersion": 2, "position": [-900, 900], "webhookId": path})
    conns["Webhook"] = {"main": [[{"node": "Sweep Config", "type": "main", "index": 0}]]}
    st, created = admin_api("POST", "/workflows", {"name": f"STAGING probe sweep clones {run_id}", "settings": {"executionOrder": "v1"},
                                                   "nodes": nodes, "connections": conns})
    if st != 200:
        raise RuntimeError(f"probe create {st}: {str(created)[:300]}")
    wid = created["id"]
    if admin_api("POST", f"/workflows/{wid}/activate")[0] != 200:
        admin_api("DELETE", f"/workflows/{wid}")
        raise RuntimeError("probe activate failed")
    return wid, f"{BASE}/webhook/{path}"


def run_probe(wid, url):
    since = newest_execution_id(wid)
    http("POST", url, {})
    return newest_execution(wid, since)


@scenario("sweep_deactivates_retired_clone", "V12: the sweep's clone branch (the derived canonical nodes, run on the real n8n) deactivates the clone of a bot IT retired (trial_expiry), stamps it and never calls it again; a liveness-retired bot's clone stays ACTIVE; nothing is deleted — WhatsApp and Telegram")
def s_sweep_clone(ctx):
    o = fresh_owner(ctx, "sw")
    wa, tg, wwa, wtg, created = create_bot_clones(ctx, o, "sw")
    if not (wwa and wtg):
        return False, f"setup: create {created}"
    pg(f"update bot_profiles set deleted_at = now(), deleted_reason = 'trial_expiry' where profile_id = '{wa}';"
       f"update bot_profiles set deleted_at = now(), deleted_reason = 'liveness' where profile_id = '{tg}';")
    active = lambda w: (admin_api("GET", f"/workflows/{w}")[1] or {}).get("active")
    pre = (active(wwa), active(wtg))
    wid = None
    try:
        wid, url = sweep_clone_probe(ctx["run_id"])
        e1 = run_probe(wid, url)
        mid = (active(wwa), active(wtg), pg(f"select coalesce(clone_deactivated_id, '-') from bot_profiles where profile_id = '{wa}';"))
        e2 = run_probe(wid, url)
        ran2 = ran_nodes(e2)
    finally:
        defer_delete(wid)
    exists = [admin_api("GET", f"/workflows/{w}")[0] for w in (wwa, wtg)]
    for w in (wwa, wtg):
        hook("SetWorkflowState", o.token, {"workflowId": w, "action": "delete"})
    ok = (pre == (True, True) and "Deactivate Clone" in ran_nodes(e1) and mid == (False, True, wwa)
          and "Deactivate Clone" not in ran2 and exists == [200, 200])
    return ok, f"active before={pre} after run 1 (wa, tg, stamp)={mid} run 2 called deactivate={'Deactivate Clone' in ran2} still exist={exists}"


# ------------------------------------------------------------------ F1 + F2 «the bot's line» + V3 journal (F1F2-plan T10)
# The real Create WA/TG graphs (apply-clone-lifecycle.py), Delete Bot Files by the line, the sweep's branch A and the
# Auth Session journal on n8n 2.36.7 + the Wappi mock. Every scenario was first run against the canon BEFORE the block
# (bootstrap.py --canon-ref 92a7caa2) and failed there (docs/audits/2026-10-09-followups/evidence/T10-before.txt).
# A scenario cleans up its clones (the staging API), price-list rows and bookings; the Owner cleanup deletes the rest.

NO_LLM = "--no-llm" in sys.argv     # skip the model step of retired_bot_reconnects_after_purchase (deterministic part only)

LINE_CH = {
    "wa": {"channel": "whatsapp", "form": "CreateWhatsappWorkflow", "field": "WhatsappProfileId", "other": "TelegramWorkflowId",
           "key": "botWaId", "other_key": "botTgId", "fcol": "bot_wa_id", "suffix": "@c.us"},
    "tg": {"channel": "telegram", "form": "CreateTelegramWorkflow", "field": "TelegramProfileId", "other": "WhatsappWorkflowId",
           "key": "botTgId", "other_key": "botWaId", "fcol": "bot_tg_id", "suffix": ""},
}


def for_channels(id_, title):
    """Register a scenario once per channel: `<id>[wa]` and `<id>[tg]`."""
    def deco(fn):
        for ch in ("wa", "tg"):
            SCENARIOS.append((f"{id_}[{ch}]", title, (lambda c, ch=ch: fn(c, ch))))
        return fn
    return deco


def line_owner(ctx, tag, ch):
    """A fresh install whose FIRST profile is on the scenario's channel (fresh_owner always starts on WhatsApp)."""
    o = Owner(tag, ctx["run_id"])
    pid = o.add_profile(LINE_CH[ch]["channel"])
    return o, pid


def line_chat(ch, ctx, tag):
    n = int(hashlib.sha1((tag + ctx["run_id"] + ch).encode()).hexdigest(), 16) % 10_000_000
    return f"7701{n:07d}@c.us" if ch == "wa" else f"55{n:07d}"


def line_create(owner, ch, pid, key, origin="edit", prev=None, other="-1", mode="semi", token=None, app_user_id=None, refresh=True):
    """One Create WA/TG through the real gate, as the client sends it (§1.7: CreateOrigin, PreviousProfileId). Returns
    (http, body, the answered id, the nodes the execution ran)."""
    c = LINE_CH[ch]
    fields = {"Name": f"e2e-line-{key}", "BusinessType": "Цветы", "BusinessTypeId": "flowers", "Business": "", "Prompt": "",
              "ProductsList": "", "ServicesList": "", "AppUserID": owner.app_user_id if app_user_id is None else app_user_id,
              "BotKey": key, "ReplyMode": mode, "ReplyModeVersion": str(int(time.time() * 1000)), c["field"]: pid, c["other"]: other}
    if origin is not None:
        fields["CreateOrigin"] = origin
    if prev:
        fields["PreviousProfileId"] = prev
    wf = CREATE_WORKFLOW_IDS[c["form"]]
    since = newest_execution_id(wf)
    st, d = hook_form(c["form"], owner.token if token is None else token, fields)
    ran = ran_nodes(newest_execution(wf, since))
    wid = d.get("id") if isinstance(d, dict) else None
    if refresh and token is None:
        owner.refresh()
    return st, d, wid, ran


def wf_state(wid):
    """(GET status, active, webhook path) of a staging workflow."""
    st, d = admin_api("GET", f"/workflows/{wid}")
    if st != 200 or not isinstance(d, dict):
        return st, None, None
    path = next(((n.get("parameters") or {}).get("path") for n in d.get("nodes", []) if n.get("type") == "n8n-nodes-base.webhook"), None)
    return st, d.get("active"), path


def registered(path):
    """Whether n8n serves a POST webhook on `path` right now — asked with a GET, which never starts an execution: a
    registered POST webhook answers «not registered for GET … Did you mean to make a POST request?»."""
    st, d = http("GET", f"{BASE}/webhook/{path}", timeout=10)
    return st == 404 and "Did you mean" in str((d or {}).get("message", "") if isinstance(d, dict) else d)


def drop_clones(*pids, keep=()):
    """Staging API cleanup: every workflow whose webhook path is one of `pids` (clones and orphans of this scenario) — a
    clone may have answered a webhook, so it is switched off now and deleted at the end of the run (defer_delete)."""
    for pid in pids:
        for wid, _ in clones_for(pid):
            if wid not in keep:
                defer_delete(wid)


def seed_line(pid, wid, ch, chat, install, tag, price_row="Букет «Нежность» — 18 500 ₸ (15 роз)",
              dialog=(("human", "Здравствуйте! Можно букет «Нежность» с доставкой в пятницу к 18:00?"),
                      ("ai", "Да, доставим букет «Нежность» в пятницу к 18:00 по адресу Абая 10."))):
    """Everything a bot holds on the server for one chat, as its writers leave it: memory, the one history, «Сводка»,
    a handoff, the reply ledger, chat state, a chat-scoped reply-mode flag — under the profile — and a price-list chunk +
    its registry row under the clone. Returns the file id."""
    c = LINE_CH[ch]
    fid = str(uuid.uuid4())
    for kind, text in dialog:
        pg(f"insert into n8n_chat_histories (session_id, message) values ('{pid}:{chat}', jsonb_build_object('type', '{kind}', "
           f"'content', '{text}', 'additional_kwargs', '{{}}'::jsonb, 'response_metadata', '{{}}'::jsonb));")
    pg(f"insert into chat_history_events (profile_id, chat_id, message_id, source, body, msg_type, sent_at) values "
       f"('{pid}', '{chat}', '{tag}-m1', 'client', '{dialog[0][1]}', 'chat', now() - interval '1 day');"
       f"insert into conversation_outcomes (session_id, profile_id, chat_id, outcome, summary, last_history_id, last_message_at, outcome_at) "
       f"values ('{pid}:{chat}', '{pid}', '{chat}', 'order_collected', 'Нежность, пятница 18:00', 0, now() - interval '1 day', now() - interval '1 day');"
       f"insert into handoff_requests (profile_id, chat_id, source_message_id, reason, summary) values ('{pid}', '{chat}', '{tag}-m1', 'order', 'Нежность');"
       f"insert into reply_events (profile_id, chat_id, message_id, state) values ('{pid}', '{chat}', '{tag}-m1', 'sent');"
       f"insert into chat_reply_state (profile_id, chat_id, incoming_generation) values ('{pid}', '{chat}', 3);"
       f"insert into reply_mode_flags (profile_id, chat_id, suppressed, version) values ('{pid}', '{chat}', false, 5);"
       f"insert into documents (content, metadata) values ('{price_row}', jsonb_build_object('{c['key']}', '{wid}', '{c['other_key']}', '-1', 'fileId', '{fid}'));"
       f"insert into uploaded_files (file_id, owner_id, {c['fcol']}, content_type, file_name) values ('{fid}', '{install}', '{wid}', 'products', 'price.txt');")
    return fid


def line_rows(pid, chat):
    """memory|history|outcomes|handoffs|events|state|chat flags of one profile's chat."""
    return pg(f"select (select count(*) from n8n_chat_histories where session_id = '{pid}:{chat}') || '|' || "
              f"(select count(*) from chat_history_events where profile_id = '{pid}' and chat_id = '{chat}') || '|' || "
              f"(select count(*) from conversation_outcomes where profile_id = '{pid}' and chat_id = '{chat}' and session_id = '{pid}:{chat}') || '|' || "
              f"(select count(*) from handoff_requests where profile_id = '{pid}' and chat_id = '{chat}') || '|' || "
              f"(select count(*) from reply_events where profile_id = '{pid}' and chat_id = '{chat}') || '|' || "
              f"(select count(*) from chat_reply_state where profile_id = '{pid}' and chat_id = '{chat}') || '|' || "
              f"(select count(*) from reply_mode_flags where profile_id = '{pid}' and chat_id = '{chat}');")


def file_home(fid, ch):
    """chunks key|registry column of one price-list file."""
    c = LINE_CH[ch]
    return pg(f"select coalesce((select string_agg(distinct metadata->>'{c['key']}', ',') from documents where metadata->>'fileId' = '{fid}'), '-') "
              f"|| '|' || coalesce((select {c['fcol']} from uploaded_files where file_id = '{fid}'), '-');")


def drop_files(*fids):
    if fids:
        ids = ", ".join(f"'{f}'" for f in fids)
        pg(f"delete from documents where metadata->>'fileId' in ({ids}); delete from uploaded_files where file_id in ({ids});")


def row_of(pid):
    return pg(f"select coalesce(workflow_id, '-') || '|' || (deleted_at is null) || '|' || coalesce(deleted_reason, '-') || '|' "
              f"|| coalesce(superseded_by, '-') from bot_profiles where profile_id = '{pid}';")


def booking(wid):
    return pg(f"select reason || '|' || coalesce(replaced_by, '-') || '|' || coalesce(profile_id, '-') || '|' || (removed_at is not null) "
              f"|| '|' || coalesce(last_status, '-') || '|' || attempts from clone_retirements where workflow_id = '{wid}';")


def probe_workflow(run_id, src_file, keep, entry, tag, edit=None, response_node=False, trigger="Probe Webhook"):
    """A TEMPORARY staging-only workflow: a webhook → the DERIVED canonical nodes `keep` of `src_file` (credentials already
    remapped by derive-staging), entered at `entry`. Runs exactly those nodes on the real n8n; the caller deletes it.
    A node `keep` names that the canon does not have fails the probe — the canon before the block lacks branch A.
    `trigger` names the probe's own webhook: a probe whose nodes read the canonical trigger BY NAME (RevenueCat Events'
    Dedup Gate / Map Event read $('Webhook') since A1) passes "Webhook" — the canonical one (its header secret) stays out."""
    src = json.load(open(os.path.join(N8N_DIR, "workflows-staging", src_file), encoding="utf-8"))
    nodes = [copy.deepcopy(n) for n in src["nodes"] if n["name"] in keep]
    missing = sorted(set(keep) - {n["name"] for n in nodes})
    if missing:
        raise RuntimeError(f"{src_file}: no {missing} in this canon")
    if trigger in keep:
        raise RuntimeError(f"{src_file}: the probe trigger {trigger!r} would shadow a kept node")
    if edit:
        edit({n["name"]: n for n in nodes})
    conns = {k: {kind: [[e for e in out or [] if e["node"] in keep] for out in lists] for kind, lists in v.items()}
             for k, v in src["connections"].items() if k in keep}      # edges into nodes outside the probe are cut
    path = f"t10-{tag}-{run_id}"
    nodes.append({"parameters": {"httpMethod": "POST", "path": path, "responseMode": "responseNode" if response_node else "onReceived",
                                 "options": {}},
                  "id": f"probe-{tag}", "name": trigger, "type": "n8n-nodes-base.webhook", "typeVersion": 2,
                  "position": [-900, 900], "webhookId": path})
    conns[trigger] = {"main": [[{"node": entry, "type": "main", "index": 0}]]}
    st, created = admin_api("POST", "/workflows", {"name": f"STAGING probe {tag} {run_id}", "settings": {"executionOrder": "v1"},
                                                   "nodes": nodes, "connections": conns})
    if st != 200:
        raise RuntimeError(f"probe create {st}: {str(created)[:300]}")
    wid = created["id"]
    if admin_api("POST", f"/workflows/{wid}/activate")[0] != 200:
        admin_api("DELETE", f"/workflows/{wid}")
        raise RuntimeError("probe activate failed")
    return wid, f"{BASE}/webhook/{path}"


def drop_probe(wid):
    defer_delete(wid)        # switched off now, deleted ≥ 35 s after the run's last switch-off (insights hygiene)


SWEEP_FILE = "fXYpCXPKw92EzRz8-Profile_Lifecycle_Sweep.json"
BRANCH_A = ("Sweep Config", "Clone Retirements", "Retirements Dry Run?", "Log Would Remove", "Has Retired Clone?",
            "Delete Retired Clone", "Retired Clone Gone?", "Stamp Retirement Removed", "Stamp Retirement Kept")


def run_branch_a(run_id, dry_run=None):
    """One run of the sweep's branch A (Clone Retirements → … → Stamp). dry_run=None runs Sweep Config as the imported canon
    carries it — since step 12 (4761117b, on prod 2026-10-14) retirementsDryRun = false, the live mode; True / False pins
    the flag for this run only (True = the first day's «only log» mode, which the graph still honours). Returns the
    execution."""
    def flip(nodes):
        for a in nodes["Sweep Config"]["parameters"]["assignments"]["assignments"]:
            if a["name"] == "retirementsDryRun" and dry_run is not None:
                a["value"] = dry_run
    wid = None
    try:
        wid, url = probe_workflow(run_id, SWEEP_FILE, BRANCH_A, "Sweep Config", f"ba{int(time.time())}", edit=flip)
        return run_probe(wid, url)
    finally:
        drop_probe(wid)


def age_bookings(*wids):
    if wids:
        pg(f"update clone_retirements set created_at = now() - interval '11 minutes' where workflow_id in ({', '.join(repr(w) for w in wids)});")


def throwaway_workflow(tag, path=None, active=False):
    """A plain staging workflow standing in for a clone (a webhook node on `path`)."""
    path = path or f"t10-thr-{tag}-{uuid.uuid4().hex[:6]}"
    st, d = admin_api("POST", "/workflows", {"name": f"STAGING t10 {tag}", "settings": {"executionOrder": "v1"}, "connections": {},
                                             "nodes": [{"parameters": {"httpMethod": "POST", "path": path, "responseMode": "onReceived", "options": {}},
                                                        "id": "w", "name": "Webhook", "type": "n8n-nodes-base.webhook", "typeVersion": 2,
                                                        "position": [0, 0], "webhookId": path}]})
    if st != 200:
        raise RuntimeError(f"throwaway workflow: {st} {d}")
    if active and admin_api("POST", f"/workflows/{d['id']}/activate")[0] != 200:
        raise RuntimeError("throwaway activate failed")
    return d["id"]


RC_EVENTS_FILE = "ZGYr6srzS3rSSXHp-RevenueCat_Events.json"
# Every node of RevenueCat Events but its trigger (the canonical Webhook carries the RevenueCat header secret): since A1
# the delivery enters at Dedup Gate, which — like Map Event — reads the payload from the trigger BY NAME («Webhook»).
RC_KEEP = ("Dedup Gate", "If Duplicate?", "Map Event", "If Is Transfer?", "Transfer Subscriber", "If Needs Consolidation?",
           "Consolidate Aliases", "If Has Payload?", "Upsert Subscriber", "Respond 200", "Respond No-Op", "Respond Error",
           "Respond Error Transfer", "Respond Error Consolidate")


def rc_deliver(run_id, payloads, gate=True):
    """RevenueCat deliveries as RevenueCat sends them, through the DERIVED RevenueCat Events graph (Dedup Gate → … → Upsert
    Subscriber) behind ONE temporary auth-free webhook named «Webhook» — the real gate, mapping and upsert on the staging
    Postgres, without the header secret. Returns [(status, body, execution)] in order (each delivery after the last ended).
    gate=False enters at Map Event — a replay that got past the gate (it read before the first delivery committed, or
    its read failed): only Upsert Subscriber's own ledger check stands between it and a second credit."""
    keep = RC_KEEP if gate else tuple(n for n in RC_KEEP if n not in ("Dedup Gate", "If Duplicate?"))
    wid, out = None, []
    try:
        wid, url = probe_workflow(run_id, RC_EVENTS_FILE, keep, "Dedup Gate" if gate else "Map Event", f"rc{int(time.time())}",
                                  response_node=True, trigger="Webhook")
        for p in payloads:
            since = newest_execution_id(wid)
            st, body = http("POST", url, p)
            out.append((st, body, newest_execution(wid, since)))
        return out
    finally:
        drop_probe(wid)


def rc_purchase(run_id, app_user_id, entitlement="tier_business", product="sub.business.month"):
    """A store purchase as RevenueCat reports it (no event id: nothing for the ledger) — (status, body)."""
    st, body, _ = rc_deliver(run_id, [{"event": {"type": "INITIAL_PURCHASE", "app_user_id": app_user_id, "entitlement_ids": [entitlement],
                                                 "product_id": product, "expiration_at_ms": int(time.time() * 1000) + 30 * 86400_000}}])[0]
    return st, body


def clone_executions(wid, since_id, n=1, wait_s=60):
    deadline = time.time() + wait_s
    while time.time() < deadline:
        st, page = http("GET", f"{BASE}/api/v1/executions?workflowId={wid}&limit=20&includeData=false", headers={"X-N8N-API-KEY": SEC.get("apiKey", "")})
        rows = [e for e in (page or {}).get("data", []) if int(e["id"]) > since_id]
        if len(rows) >= n and all(r.get("finished") or r.get("status") in ("success", "error", "canceled", "crashed") for r in rows):
            return [http("GET", f"{BASE}/api/v1/executions/{r['id']}?includeData=true", headers={"X-N8N-API-KEY": SEC.get("apiKey", "")})[1]
                    for r in sorted(rows, key=lambda e: int(e["id"]))]
        time.sleep(1)
    return []


def node_items(ex, node):
    """Every main-output item (json) a node produced in one execution."""
    return [it.get("json") or {} for run in (((ex or {}).get("data") or {}).get("resultData", {}).get("runData", {}).get(node, []) or [])
            for br in (run.get("data") or {}).get("main") or [] for it in br or []]


def exec_outcome(ex):
    return next((it.get("outcome") for it in node_items(ex, "Outcome")), None)


def bot_event(pid, chat, mid, body, t):
    return {"messages": [{"id": mid, "chatId": chat, "from": chat, "to": "770100000000", "profile_id": pid, "body": body, "type": "chat",
                          "time": t, "fromMe": False, "senderName": "client", "wh_type": "incoming_message"}]}


@for_channels("reauth_same_profile_reuses_bound_clone", "F1 (a1): the channel saved off (SetWorkflowState deactivate), then a Create for the SAME profile → the SAME clone id (created:false), activated again, no template read, no second clone")
def s_line_reauth_same(ctx, ch):
    o, pid = line_owner(ctx, f"rs{ch}", ch)
    key = f"Bot-rs{ch}-{ctx['run_id']}"
    try:
        st1, d1, w1, _ = line_create(o, ch, pid, key)
        if not w1:
            return False, f"setup create: {st1} {d1}"
        off = hook("SetWorkflowState", o.token, {"workflowId": w1, "action": "deactivate"})[0]
        before = wf_state(w1)
        st2, d2, w2, ran = line_create(o, ch, pid, key)
        after = wf_state(w1)
        clones = clones_for(pid)
        row = row_of(pid)
    finally:
        drop_clones(pid)
    ok = (off == 200 and before == (200, False, pid) and st2 == 200 and w2 == w1 and isinstance(d2, dict) and d2.get("created") is False
          and d2.get("adoptedProfiles") == [] and "Probe Bound Clone" in ran and "Activate Created Workflow" in ran
          and "Get Sample Workflow" not in ran and "Create Workflow" not in ran
          and after == (200, True, pid) and clones == [(w1, True)] and row.startswith(f"{w1}|true|"))
    return ok, (f"first={w1} deactivate={off} state_before={before} second={st2} {d2} ran_template={'Get Sample Workflow' in ran} "
                f"ran_create={'Create Workflow' in ran} ran_activate={'Activate Created Workflow' in ran} state_after={after} clones={clones} row={row}")


@for_channels("reuse_active_clone_skips_reactivation", "F1 (a1) + platform: on n8n 2.36.7 activating an ACTIVE workflow is NOT a no-op (200, but its webhook is unregistered for a moment); so a Create for the same profile whose clone is ACTIVE reuses it WITHOUT activating it — the webhook answers throughout, same id, no second clone")
def s_line_reuse_active(ctx, ch):
    # the platform fact the reuse branch is built on (T3 «Already Active?»): a throwaway webhook, activated twice
    thr = throwaway_workflow(f"act{ch}", active=True)
    path = wf_state(thr)[2]
    gaps, stop = [], [False]

    def watch(target, out):
        while not stop[0]:
            out.append(registered(target))
    again, tries = [], 0
    try:
        while tries < 5 and gaps.count(False) == 0:     # the gap lasts milliseconds: a few re-activations, until one is seen
            tries += 1
            stop[0] = False
            t = threading.Thread(target=watch, args=(path, gaps)); t.start(); time.sleep(0.3)
            again.append(admin_api("POST", f"/workflows/{thr}/activate")[0])
            time.sleep(0.5); stop[0] = True; t.join()
    finally:
        stop[0] = True
        admin_api("DELETE", f"/workflows/{thr}")
    platform_gap = gaps.count(False)
    again = again[-1] if again and all(x == again[0] for x in again) else again
    o, pid = line_owner(ctx, f"ra{ch}", ch)
    key = f"Bot-ra{ch}-{ctx['run_id']}"
    try:
        st1, d1, w1, _ = line_create(o, ch, pid, key)
        if not w1:
            return False, f"setup create: {st1} {d1}"
        seen, stop[0] = [], False
        t = threading.Thread(target=watch, args=(pid, seen)); t.start(); time.sleep(0.3)
        st2, d2, w2, ran = line_create(o, ch, pid, key)
        time.sleep(0.3); stop[0] = True; t.join()
        clones = clones_for(pid)
    finally:
        stop[0] = True
        drop_clones(pid)
    ok = (again == 200 and platform_gap > 0 and st2 == 200 and w2 == w1 and isinstance(d2, dict) and d2.get("created") is False
          and "Probe Bound Clone" in ran and "Activate Created Workflow" not in ran and "Create Workflow" not in ran
          and seen and all(seen) and clones == [(w1, True)])
    return ok, (f"platform: re-activate={again} ×{tries} webhook gaps during it={platform_gap}/{len(gaps)} | create#2={st2} {d2} "
                f"ran_activate={'Activate Created Workflow' in ran} webhook answered {seen.count(True)}/{len(seen)} probes during Create#2 clones={clones}")


@for_channels("reauth_new_profile_replaces_prior_and_moves_data", "F1 (a2/a3) + F2: a NEW profile of the same bot (re-pairing: same bot key) → a new clone; the predecessor's memory, history, «Сводка», handoffs, ledger, chat state, chat flags, price-list chunks and file registry move to it; the prior clone is booked and deleted (GET 404), the sweep's branch A stamps it; a repeat Create is idempotent")
def s_line_repair(ctx, ch):
    o, p1 = line_owner(ctx, f"rp{ch}", ch)
    key = f"Bot-rp{ch}-{ctx['run_id']}"
    chat = line_chat(ch, ctx, "rp")
    fid = None
    try:
        st1, d1, w1, _ = line_create(o, ch, p1, key)
        if not w1:
            return False, f"setup create: {st1} {d1}"
        fid = seed_line(p1, w1, ch, chat, o.install_id, f"rp{ch}{ctx['run_id']}")
        p2 = o.add_profile(LINE_CH[ch]["channel"])
        st2, d2, w2, ran = line_create(o, ch, p2, key)
        moved = (line_rows(p1, chat), line_rows(p2, chat), file_home(fid, ch))
        old = (wf_state(w1)[0], row_of(p1), booking(w1))
        new = wf_state(w2) if w2 else None
        st3, d3, w3, ran3 = line_create(o, ch, p2, key)                  # the answer was lost: the client repeats
        repeat = (line_rows(p2, chat), clones_for(p2))
        age_bookings(w1)
        try:
            run_branch_a(ctx["run_id"], dry_run=False)
            stamped = booking(w1)
        except RuntimeError as e:          # a canon without branch A: the rest of the evidence still stands
            stamped = f"branch A: {e}"
    finally:
        drop_clones(*o.profiles)
        drop_files(*([fid] if fid else []))
    ok = (st2 == 200 and w2 and w2 != w1 and isinstance(d2, dict) and d2.get("created") is True and d2.get("adoptedProfiles") == [p1]
          and moved == ("0|0|0|0|0|0|0", "2|1|1|1|1|1|1", f"{w2}|{w2}")
          and old == (404, f"{w1}|false|replaced|{p2}", f"replaced|{w2}|{p1}|false|-|0") and new == (200, True, p2)
          and st3 == 200 and w3 == w2 and isinstance(d3, dict) and d3.get("created") is False and d3.get("adoptedProfiles") == [p1]
          and repeat == ("2|1|1|1|1|1|1", [(w2, True)]) and stamped == f"replaced|{w2}|{p1}|true|404|0")
    return ok, (f"w1={w1} create#2={st2} {d2} rows p1|p2|file={moved} old clone GET|row|booking={old} new clone={new} "
                f"repeat={st3} {d3} rows|clones after repeat={repeat} branch A={stamped}")


@for_channels("retired_bot_reconnects_after_purchase", "F2: a bot the sweep retired (trial over: row trial_expiry, clone switched off by the V12 branch, trialing → expired) is bought back (RevenueCat INITIAL_PURCHASE through the real mapping + upsert), reconnected on a NEW profile (profile.add) and Created with PreviousProfileId → a new active clone on the new path takes over its chats, memory and price list, the old clone is deleted, the new webhook reaches the new clone, the V12 branch leaves it on; [llm] then, in «Авто», the bot answers with the price from the moved price list and the time agreed in the moved dialog")
def s_line_reconnect(ctx, ch):
    o, p1 = line_owner(ctx, f"rc{ch}", ch)
    key = f"Bot-rc{ch}-{ctx['run_id']}"
    chat, chat2 = line_chat(ch, ctx, "rc"), line_chat(ch, ctx, "rc2")
    fid, res = None, {}
    try:
        st1, d1, w1, _ = line_create(o, ch, p1, key)
        if not w1:
            return False, f"setup create: {st1} {d1}"
        fid = seed_line(p1, w1, ch, chat, o.install_id, f"rc{ch}{ctx['run_id']}")
        # the trial ends: the sweep's Mark Deleted + Demote Trialing, then its V12 branch switches the clone off
        pg(f"update bot_profiles set deleted_at = now(), deleted_reason = 'trial_expiry' where profile_id = '{p1}';"
           f"update subscribers set status = 'expired' where app_user_id = '{o.app_user_id}' and status = 'trialing';")
        wid = None
        try:
            wid, url = sweep_clone_probe(ctx["run_id"] + ch)
            run_probe(wid, url)
        finally:
            drop_probe(wid)
        res["retired"] = (wf_state(w1)[1], o.refresh().get("profiles"))
        st, _ = rc_purchase(ctx["run_id"], o.app_user_id)
        res["purchase"] = (st, pg(f"select plan || '|' || status from subscribers where app_user_id = '{o.app_user_id}';"))
        o.refresh()
        p2 = o.add_profile(LINE_CH[ch]["channel"])
        st2, d2, w2, ran = line_create(o, ch, p2, key, prev=p1)
        res["create"] = (st2, d2)
        res["new_clone"] = wf_state(w2) if w2 else None
        res["old"] = (wf_state(w1)[0], row_of(p1), booking(w1))
        res["moved"] = (line_rows(p1, chat), line_rows(p2, chat), file_home(fid, ch))
        # the new profile's webhook reaches the new clone («Вместе»: recorded, nothing generated)
        since = newest_execution_id(w2) if w2 else 0
        t = int(time.time())
        http("POST", f"{BASE}/webhook/{p2}", bot_event(p2, chat2, f"rc2-{ctx['run_id']}{ch}", "Здравствуйте, вы работаете?", t), timeout=30)
        exs = clone_executions(w2, since) if w2 else []
        res["webhook"] = (len(exs), exec_outcome(exs[0]) if exs else None,
                          pg(f"select count(*) from chat_history_events where profile_id = '{p2}' and chat_id = '{chat2}';"))
        wid = None
        try:                                   # F2 R7: the V12 branch never picks the reconnected bot's clone
            wid, url = sweep_clone_probe(ctx["run_id"] + ch + "b")
            run_probe(wid, url)
        finally:
            drop_probe(wid)
        res["after_sweep"] = wf_state(w2) if w2 else None
        det = (res["retired"][0] is False and p1 not in (res["retired"][1] or []) and res["purchase"] == (200, "business|active")
               and st2 == 200 and w2 and w2 != w1 and isinstance(d2, dict) and d2.get("created") is True and d2.get("adoptedProfiles") == [p1]
               and res["new_clone"] == (200, True, p2) and res["old"] == (404, f"{w1}|false|trial_expiry|{p2}", f"replaced|{w2}|{p1}|false|-|0")
               and res["moved"] == ("0|0|0|0|0|0|0", "2|1|1|1|1|1|1", f"{w2}|{w2}")
               and res["webhook"] == (1, "suppressed", "1") and res["after_sweep"] == (200, True, p2))
        if not det or NO_LLM:
            res["llm"] = "skipped" + (" (--no-llm)" if NO_LLM else " (deterministic part failed)")
            return det, json.dumps(res, ensure_ascii=False)
        # [llm] the owner returns the chat to «Авто»; the client asks about the earlier agreement and the price
        hook("SetReplyMode", o.token, {"profileIds": [p2], "chatId": "*", "suppressed": False, "version": int(time.time() * 1000)})
        q = "Напомните, во сколько в пятницу вы привезёте букет «Нежность» и сколько он стоит?"
        t = int(time.time())
        mid = f"rcq-{ctx['run_id']}{ch}"
        http("POST", f"{MOCK}/__mock/messages", {"profile_id": p2, "chat_id": chat, "messages": [
            {"id": mid, "chatId": chat, "from": chat, "body": q, "time": t, "fromMe": False, "type": "chat"}]})
        since = newest_execution_id(w2)
        since_calls = mock_total()
        http("POST", f"{BASE}/webhook/{p2}", bot_event(p2, chat, mid, q, t), timeout=30)
        exs = clone_executions(w2, since, wait_s=180)
        ex = exs[0] if exs else {}
        tools = []
        for run in ((ex.get("data") or {}).get("resultData", {}).get("runData", {}).get("Supabase Vector Store", []) or []):
            for br in (run.get("data") or {}).get("ai_tool") or []:
                tools += [str(it.get("json")) for it in br or []]
        sends = [c for c in mock_calls(since_calls, "message/send")[0] if (c.get("query") or {}).get("profile_id") == p2 and not c.get("dropped")]
        reply = ((sends[0].get("body") or {}).get("body", "") if sends else "")
        flat = reply.replace(" ", "").replace(" ", "")
        res["llm"] = {"outcome": exec_outcome(ex), "price_tool_saw_row": any("18 500" in x for x in tools), "reply": reply[:240]}
        ok = det and exec_outcome(ex) == "sent" and "18500" in flat and re.search(r"18[:.]00", reply) is not None
        return ok, json.dumps(res, ensure_ascii=False)
    finally:
        drop_clones(*o.profiles)
        drop_files(*([fid] if fid else []))


@for_channels("activation_conflict_discards_new_clone", "F1 (б): an orphan workflow already serves the profile's webhook path → the new clone's activation is refused; Create answers create_failed WITHOUT an id and deletes the clone it made (no second workflow on the path, nothing bound)")
def s_line_activation_conflict(ctx, ch):
    o, pid = line_owner(ctx, f"ac{ch}", ch)
    orphan = throwaway_workflow(f"orph{ch}", path=pid, active=True)
    try:
        st, d, wid, ran = line_create(o, ch, pid, f"Bot-ac{ch}-{ctx['run_id']}")
        clones = clones_for(pid)
        row = row_of(pid)
    finally:
        drop_clones(pid)
    ok = (st == 200 and d == {"success": False, "error": "create_failed"} and wid is None and "Activate Created Workflow" in ran
          and "Discard New Clone" in ran and "Record Discard Failure" not in ran and clones == [(orphan, True)] and row.startswith("-|"))
    return ok, f"create={st} {d} ran_discard={'Discard New Clone' in ran} workflows on the path={clones} (orphan {orphan}) row={row}"


@for_channels("bind_failure_answers_no_id", "F1 (б) / R8 trick: Bind Workflow Id fails on the real Postgres (a CHECK scoped to this profile) → create_failed WITHOUT an id, the new clone deleted, nothing bound; the retry once the DB is back creates exactly ONE live clone")
def s_line_bind_failure(ctx, ch):
    o, pid = line_owner(ctx, f"bf{ch}", ch)
    con = f"t10bf_{ctx['run_id']}_{ch}"
    key = f"Bot-bf{ch}-{ctx['run_id']}"
    try:
        pg(f"alter table bot_profiles add constraint {con} check (profile_id <> '{pid}' or workflow_id is null) not valid;")
        try:
            st, d, wid, ran = line_create(o, ch, pid, key)
            clones1 = clones_for(pid)
            row1 = row_of(pid)
        finally:
            pg(f"alter table bot_profiles drop constraint if exists {con};")
        st2, d2, w2, _ = line_create(o, ch, pid, key)
        clones2 = clones_for(pid)
    finally:
        drop_clones(pid)
    ok = (st == 200 and d == {"success": False, "error": "create_failed"} and wid is None and "Bind Workflow Id" in ran
          and "Discard New Clone" in ran and clones1 == [] and row1.startswith("-|")
          and st2 == 200 and w2 and clones2 == [(w2, True)])
    return ok, f"failed create={st} {d} ran_bind={'Bind Workflow Id' in ran} ran_discard={'Discard New Clone' in ran} clones={clones1} row={row1} | retry={st2} {w2} clones={clones2}"


@for_channels("legacy_create_on_owned_profile_refused", "F1 Q3: a token-less (legacy, compat window) Create naming a profile an install OWNS → profile_owned WITHOUT an id; no clone created, the owner's clone and binding untouched")
def s_line_legacy_owned(ctx, ch):
    o, pid = line_owner(ctx, f"lg{ch}", ch)
    key = f"Bot-lg{ch}-{ctx['run_id']}"
    try:
        st1, d1, w1, _ = line_create(o, ch, pid, key)
        if not w1:
            return False, f"setup create: {st1} {d1}"
        flag = lambda: pg(f"select suppressed || '/' || version from reply_mode_flags where profile_id = '{pid}' and chat_id = '*';")
        flag_before = flag()
        st, d, wid, ran = line_create(o, ch, pid, key, origin=None, token="", refresh=False, mode="auto")
        flag_after = flag()                    # observed, not asserted: the pre-A1 compat path seeds BEFORE the plan refuses
        clones = clones_for(pid)
        row = row_of(pid)
        owner = pg(f"select owner_id from bot_profiles where profile_id = '{pid}';")
    finally:
        drop_clones(pid)
    if COMPAT_CLOSED:
        ok = st == 401 and clones == [(w1, True)]
    else:
        ok = (st == 200 and d == {"success": False, "error": "profile_owned"} and wid is None and "Get Sample Workflow" not in ran
              and clones == [(w1, True)] and row == f"{w1}|true|-|-" and owner == o.install_id)
    return ok, (f"owner's clone={w1} legacy create={st} {d} ran_template={'Get Sample Workflow' in ran} clones={clones} row={row} "
                f"owner_kept={owner == o.install_id} | observed (compat-window residual, not this block's): the owner's '*' reply mode "
                f"{flag_before} -> {flag_after}")


@for_channels("foreign_install_same_bot_key_untouched", "F1 §1.3: install B names its own profile with the SAME bot key as A's bot (and even A's profile as PreviousProfileId) → B gets its own clone, adopts nothing; A's clone, rows, memory and price list stay A's. Control: A's own re-pairing adopts A's line and leaves B's bot alone")
def s_line_foreign(ctx, ch):
    a, pa = line_owner(ctx, f"fa{ch}", ch)
    b, pb = line_owner(ctx, f"fb{ch}", ch)
    key = f"Bot-fx{ch}-{ctx['run_id']}"
    chat = line_chat(ch, ctx, "fx")
    fa = fb = None
    try:
        _, _, wa_, _ = line_create(a, ch, pa, key)
        if not wa_:
            return False, "setup create A failed"
        fa = seed_line(pa, wa_, ch, chat, a.install_id, f"fxa{ch}{ctx['run_id']}")
        stb, db, wb, _ = line_create(b, ch, pb, key, prev=pa)
        b_view = (line_rows(pa, chat), file_home(fa, ch), wf_state(wa_), row_of(pa), line_rows(pb, chat))
        fb = seed_line(pb, wb, ch, chat, b.install_id, f"fxb{ch}{ctx['run_id']}") if wb else None
        pa2 = a.add_profile(LINE_CH[ch]["channel"])                   # control: A's own re-pairing
        sta, da, wa2, _ = line_create(a, ch, pa2, key)
        a_view = (line_rows(pa2, chat), file_home(fa, ch), wf_state(wa_)[0], line_rows(pb, chat), file_home(fb, ch) if fb else None,
                  wf_state(wb) if wb else None, row_of(pb))
    finally:
        drop_clones(*a.profiles, *b.profiles)
        drop_files(*[f for f in (fa, fb) if f])
    ok = (stb == 200 and wb and wb != wa_ and isinstance(db, dict) and db.get("created") is True and db.get("adoptedProfiles") == []
          and b_view == ("2|1|1|1|1|1|1", f"{wa_}|{wa_}", (200, True, pa), f"{wa_}|true|-|-", "0|0|0|0|0|0|0")
          and sta == 200 and isinstance(da, dict) and da.get("adoptedProfiles") == [pa]
          and a_view == ("2|1|1|1|1|1|1", f"{wa2}|{wa2}", 404, "2|1|1|1|1|1|1", f"{wb}|{wb}", (200, True, pb), f"{wb}|true|-|-"))
    return ok, (f"B create={stb} {db} A seen after B (A rows|A file|A clone|A row|B rows)={b_view} | A control={sta} {da} "
                f"(A new rows|A file|old A clone GET|B rows|B file|B clone|B row)={a_view}")


@for_channels("wizard_create_never_adopts", "F1 + F2 §1.3: a wizard Create (CreateOrigin=start: a NEW bot in a reused slot) never adopts — not by the same bot key, not even with the old profile named as PreviousProfileId; the old bot's data and clone stay where they were. Control: the same shape from settings (CreateOrigin=edit) adopts")
def s_line_wizard(ctx, ch):
    o, p1 = line_owner(ctx, f"wz{ch}", ch)
    run = ctx["run_id"]
    key, key2 = f"Bot-wz{ch}-{run}", f"Bot-wy{ch}-{run}"
    chat = line_chat(ch, ctx, "wz")
    f1 = f3 = None
    try:
        _, _, w1, _ = line_create(o, ch, p1, key)
        if not w1:
            return False, "setup create failed"
        f1 = seed_line(p1, w1, ch, chat, o.install_id, f"wz{ch}{run}")
        p2 = o.add_profile(LINE_CH[ch]["channel"])
        st, d, w2, _ = line_create(o, ch, p2, key, origin="start", prev=p1)
        wiz = (line_rows(p1, chat), file_home(f1, ch), wf_state(w1), booking(w1), line_rows(p2, chat))
        p3 = o.add_profile(LINE_CH[ch]["channel"])                     # control: another bot, from settings
        _, _, w3, _ = line_create(o, ch, p3, key2)
        f3 = seed_line(p3, w3, ch, chat, o.install_id, f"wy{ch}{run}") if w3 else None
        p4 = o.add_profile(LINE_CH[ch]["channel"])
        stc, dc, w4, _ = line_create(o, ch, p4, key2, origin="edit")
        ctl = (line_rows(p4, chat), file_home(f3, ch) if f3 else None)
    finally:
        drop_clones(*o.profiles)
        drop_files(*[f for f in (f1, f3) if f])
    ok = (st == 200 and w2 and w2 != w1 and isinstance(d, dict) and d.get("created") is True and d.get("adoptedProfiles") == []
          and wiz == ("2|1|1|1|1|1|1", f"{w1}|{w1}", (200, True, p1), "", "0|0|0|0|0|0|0")
          and stc == 200 and isinstance(dc, dict) and dc.get("adoptedProfiles") == [p3] and ctl == ("2|1|1|1|1|1|1", f"{w4}|{w4}"))
    return ok, f"wizard create={st} {d} (old rows|old file|old clone|booking|new rows)={wiz} | control={stc} {dc} (rows|file)={ctl}"


def schedule_row(pid):
    """B1: enabled|days_mask|start_min|end_min|tz|version of one profile's schedule ('' = no row)."""
    return pg(f"select enabled || '|' || days_mask || '|' || start_min || '|' || end_min || '|' || tz || '|' || version "
              f"from bot_schedule where profile_id = '{pid}';")


def schedule_body(pids, version, **kw):
    b = {"profileIds": list(pids), "enabled": True, "daysMask": 31, "startMin": 540, "endMin": 1080, "timeZone": "Asia/Almaty",
         "version": version}
    b.update(kw)
    return b


@scenario("set_schedule_applies_and_refuses", "B1: SetSchedule (crSetSchedule001) — one call for both channels of a bot lands both rows (200, applied per profile); a STALE version is not applied (200, applied:false, the server's version reported, the rows untouched); another install's profile is 403 and no token is 401 — nothing written; a malformed body is 400 bad_request with the reason; a zone Postgres does not know (Europe/Nowhere) is 400 bad_timezone")
def s_set_schedule(ctx):
    a = fresh_owner(ctx, "sch")
    tg = a.add_profile("telegram")
    wa = a.profiles[0]
    r = {}
    r["both"] = hook("SetSchedule", a.token, schedule_body([wa, tg], 1000))
    rows = (schedule_row(wa), schedule_row(tg))
    r["stale"] = hook("SetSchedule", a.token, schedule_body([wa], 900, daysMask=1, startMin=0, endMin=0))
    r["foreign"] = hook("SetSchedule", ctx["B"].token, schedule_body([wa], 2000, daysMask=1))
    r["notoken"] = hook("SetSchedule", None, schedule_body([wa], 2000, daysMask=1))
    r["malformed"] = hook("SetSchedule", a.token, schedule_body([wa], 2000, daysMask=200))
    r["badtz"] = hook("SetSchedule", a.token, schedule_body([wa], 2000, timeZone="Europe/Nowhere"))
    after = (schedule_row(wa), schedule_row(tg))
    want_row = "true|31|540|1080|Asia/Almaty|1000"
    st, d = r["both"]
    got = {x["profileId"]: (x["applied"], x["version"]) for x in (d or {}).get("results", [])} if isinstance(d, dict) else {}
    ok = (st == 200 and d.get("success") is True and got == {wa: (True, 1000), tg: (True, 1000)} and rows == (want_row, want_row)
          and r["stale"][0] == 200 and r["stale"][1].get("results") == [{"profileId": wa, "applied": False, "version": 1000}]
          and r["foreign"][0] == 403 and (r["foreign"][1] or {}).get("reason") == "forbidden:profileIds"
          and r["notoken"][0] == 401
          and r["malformed"][0] == 400 and r["malformed"][1] == {"success": False, "error": "bad_request", "reason": "daysMask"}
          and r["badtz"][0] == 400 and r["badtz"][1] == {"success": False, "error": "bad_timezone"}
          and after == rows)
    return ok, {"answers": {k: v for k, v in r.items()}, "rows": rows, "after": after}


@for_channels("reconnect_moves_schedule", "B1 + F1/F2: the bot's schedule follows its line — set on the first profile through SetSchedule, a re-pairing (a NEW profile of the same bot key, Create from settings) moves the row onto the new profile (values and version kept) and leaves nothing under the old one")
def s_line_schedule(ctx, ch):
    o, p1 = line_owner(ctx, f"sm{ch}", ch)
    key = f"Bot-sm{ch}-{ctx['run_id']}"
    try:
        st1, d1, w1, _ = line_create(o, ch, p1, key)
        if not w1:
            return False, f"setup create: {st1} {d1}"
        sts, ds = hook("SetSchedule", o.token, schedule_body([p1], 4242, daysMask=96, startMin=1320, endMin=360))
        before = schedule_row(p1)
        p2 = o.add_profile(LINE_CH[ch]["channel"])
        st2, d2, w2, _ = line_create(o, ch, p2, key)
        old, new = schedule_row(p1), schedule_row(p2)
    finally:
        drop_clones(*o.profiles)
    ok = (sts == 200 and isinstance(ds, dict) and ds.get("results") == [{"profileId": p1, "applied": True, "version": 4242}]
          and before == "true|96|1320|360|Asia/Almaty|4242"
          and st2 == 200 and w2 and w2 != w1 and isinstance(d2, dict) and d2.get("adoptedProfiles") == [p1]
          and old == "" and new == before)
    return ok, {"set": (sts, ds), "before": before, "create#2": (st2, d2), "old": old, "new": new}


@scenario("bot_delete_wipes_lineage", "F1 + F2 §1.6 (T5): a two-channel bot whose earlier profiles were left behind by a pre-fix re-pairing (WA and TG) is deleted from the app naming only its CURRENT ids → Delete Bot Files wipes the whole line (memory, history, ledger, chunks of every profile and clone), retires every row as app_delete and books the left-behind clones; the sweep's branch A deletes them. Another install's bot with the same key is untouched")
def s_line_delete(ctx):
    o, p1 = line_owner(ctx, "dlw", "wa")
    x, px = line_owner(ctx, "dlx", "wa")
    run = ctx["run_id"]
    key = f"Bot-dl-{run}"
    chat = line_chat("wa", ctx, "dl")
    tchat = line_chat("tg", ctx, "dl")
    files, res = [], {}
    try:
        _, _, w1, _ = line_create(o, "wa", p1, key)
        files.append(seed_line(p1, w1, "wa", chat, o.install_id, f"dl1{run}"))
        p2 = o.add_profile("whatsapp")                    # pre-fix re-pairing: the earlier profile is left with its data
        _, _, w2, _ = line_create(o, "wa", p2, key, origin="start")
        files.append(seed_line(p2, w2, "wa", chat, o.install_id, f"dl2{run}"))
        t1 = o.add_profile("telegram")
        _, _, wt1, _ = line_create(o, "tg", t1, key, other=w2)
        files.append(seed_line(t1, wt1, "tg", tchat, o.install_id, f"dlt1{run}"))
        t2 = o.add_profile("telegram")
        _, _, wt2, _ = line_create(o, "tg", t2, key, origin="start", other=w2)
        files.append(seed_line(t2, wt2, "tg", tchat, o.install_id, f"dlt2{run}"))
        _, _, wx, _ = line_create(x, "wa", px, key)                    # another install, the same bot key
        files.append(seed_line(px, wx, "wa", chat, x.install_id, f"dlx{run}"))
        res["setup"] = [w1, w2, wt1, wt2, wx]
        if not all(res["setup"]):
            return False, json.dumps(res)
        st, d = hook("DeleteBotFiles", o.token, {"botWaId": w2, "botTgId": wt2, "waProfileId": p2, "tgProfileId": t2, "appUserId": o.app_user_id})
        res["dbf"] = (st, {k: v for k, v in (d or {}).items() if k in ("success", "retiredProfiles", "bookedClones", "deletedChunks")})
        res["left"] = {p: line_rows(p, chat if p in (p1, p2) else tchat) for p in (p1, p2, t1, t2)}
        res["chunks_left"] = pg(f"select count(*) from documents where metadata->>'fileId' in ({', '.join(repr(f) for f in files[:4])});")
        res["rows"] = {p: row_of(p) for p in (p1, p2, t1, t2)}
        res["bookings"] = {w: booking(w) for w in (w1, w2, wt1, wt2)}
        res["foreign"] = (line_rows(px, chat), file_home(files[4], "wa"), wf_state(wx), row_of(px), booking(wx))
        res["client_deletes"] = [hook("SetWorkflowState", o.token, {"workflowId": w, "action": "delete"})[0] for w in (w2, wt2)]
        age_bookings(w1, w2, wt1, wt2)
        try:
            run_branch_a(run, dry_run=False)
            res["after_branch_a"] = {w: (wf_state(w)[0], booking(w).split("|")[3:5]) for w in (w1, w2, wt1, wt2)}
        except RuntimeError as e:          # a canon without branch A: the rest of the evidence still stands
            res["after_branch_a"] = f"branch A: {e}"
    finally:
        drop_clones(*o.profiles, *x.profiles)
        drop_files(*files)
    gone = "0|0|0|0|0|0|0"
    ok = (res.get("dbf", (0,))[0] == 200 and res["dbf"][1].get("success") is True
          and all(v == gone for v in res["left"].values()) and res["chunks_left"] == "0"
          and all(v.split("|")[1:3] == ["false", "app_delete"] for v in res["rows"].values())
          and all(v.startswith("bot_deleted|-|") for v in res["bookings"].values())
          and res["foreign"] == ("2|1|1|1|1|1|1", f"{wx}|{wx}", (200, True, px), f"{wx}|true|-|-", "")
          and res["client_deletes"] == [200, 200]
          and res["after_branch_a"] == {w1: (404, ["true", "200"]), w2: (404, ["true", "404"]), wt1: (404, ["true", "200"]), wt2: (404, ["true", "404"])})
    return ok, json.dumps(res, ensure_ascii=False)


def canon_retirements_dry_run():
    """The retirementsDryRun value Sweep Config carries in the imported (derived) canon — what run_branch_a(dry_run=None)
    runs with. False since step 12 (4761117b): prod deletes booked clones."""
    src = json.load(open(os.path.join(N8N_DIR, "workflows-staging", SWEEP_FILE), encoding="utf-8"))
    cfg = next(n for n in src["nodes"] if n["name"] == "Sweep Config")
    return next((a["value"] for a in cfg["parameters"]["assignments"]["assignments"] if a["name"] == "retirementsDryRun"), None)


@scenario("sweep_removes_booked_clone", "F1 §2.6 A (T5) + step 12: the sweep's branch A on the real n8n — canon runs it LIVE (retirementsDryRun false since 4761117b, as prod does): it DELETEs a booked clone older than 10 minutes (200 → removed_at), stamps an already-gone one (404), never touches a clone a LIVE row drives or a booking younger than 10 minutes, and never calls again what it stamped; the first day's dry-run mode (an explicit retirementsDryRun true) still only logs")
def s_line_branch_a(ctx):
    o, pid = line_owner(ctx, "ba", "wa")
    run = ctx["run_id"]
    old = throwaway_workflow("baold")                                    # a replaced clone the Create could not delete
    young = throwaway_workflow("bayoung")                                # booked a minute ago (a Create still in flight)
    ghost = ("gone" + run + "000000")[:16]                               # already deleted by someone else
    res = {}
    try:
        _, _, live, _ = line_create(o, "wa", pid, f"Bot-ba-{run}")       # bound to a LIVE row: never deleted
        pg(f"insert into clone_retirements (workflow_id, profile_id, owner_id, reason, created_at) values "
           f"('{old}', '{pid}', '{o.install_id}', 'replaced', now() - interval '11 minutes'), "
           f"('{young}', '{pid}', '{o.install_id}', 'replaced', now() - interval '1 minute'), "
           f"('{ghost}', '{pid}', '{o.install_id}', 'discard_failed', now() - interval '11 minutes'), "
           f"('{live}', '{pid}', '{o.install_id}', 'replaced', now() - interval '11 minutes');")
        e0 = run_branch_a(run, dry_run=True)                              # the first day's dry-run mode: only logs
        res["dry"] = ({w: wf_state(w)[0] for w in (old, young, live)}, {w: booking(w).split("|")[3] for w in (old, ghost)},
                      "Log Would Remove" in ran_nodes(e0), "Delete Retired Clone" in ran_nodes(e0))
        res["canon_dry_run"] = canon_retirements_dry_run()               # step 12: canon (= prod) runs branch A live
        run_branch_a(run)                                                 # canon as committed
        res["live"] = {w: (wf_state(w)[0], booking(w)) for w in (old, young, ghost, live)}
        e2 = run_branch_a(run)                                            # what it stamped is never picked again
        res["second_run_picks"] = sorted(w for w in (it.get("workflow_id") for it in node_items(e2, "Clone Retirements"))
                                         if w in {old, young, ghost, live})
    finally:
        for w in (old, young):
            admin_api("DELETE", f"/workflows/{w}")
        drop_clones(pid)
    ok = (res["dry"] == ({old: 200, young: 200, live: 200}, {old: "false", ghost: "false"}, True, False)
          and res["canon_dry_run"] is False
          and res["live"][old] == (404, f"replaced|-|{pid}|true|200|0") and res["live"][ghost] == (404, f"discard_failed|-|{pid}|true|404|0")
          and res["live"][young] == (200, f"replaced|-|{pid}|false|-|0") and res["live"][live] == (200, f"replaced|-|{pid}|false|-|0")
          and res["second_run_picks"] == [])
    return ok, json.dumps(res, ensure_ascii=False)


@scenario("rc_webhook_replay_credits_once", "A1: RevenueCat delivers the SAME top-up event twice (an answer it did not get → its retry) through the DERIVED RevenueCat Events graph behind a temporary webhook: both answered 200, topup_balance +500 exactly ONCE, ONE rc_event_ledger row; the replay ends at If Duplicate? (Map Event and Upsert never run). Fix round: the same id once more PAST the gate (a probe entering at Map Event) reaches Upsert Subscriber and changes nothing (its update is gated on recording the id); a second top-up with its own id credits again")
def s_rc_replay(ctx):
    o = Owner("rcr", ctx["run_id"])                                   # a trial subscriber row (cleaned up with the owner)
    ids = [f"e2e-rcr-{ctx['run_id']}-{uuid.uuid4().hex[:8]}" for _ in range(2)]

    def topup(eid):
        return {"api_version": "1.0", "event": {"id": eid, "type": "NON_RENEWING_PURCHASE", "app_user_id": o.app_user_id,
                                                "original_app_user_id": o.app_user_id, "aliases": [o.app_user_id],
                                                "product_id": "topup.dialogs.500", "store": "APP_STORE", "environment": "SANDBOX",
                                                "event_timestamp_ms": int(time.time() * 1000)}}
    bal = lambda: int(pg(f"select topup_balance from subscribers where app_user_id = '{o.app_user_id}';") or -1)
    led = lambda: pg(f"select string_agg(event_id, ',' order by event_id) from rc_event_ledger where event_id in ('{ids[0]}', '{ids[1]}');")
    try:
        before = bal()
        res = rc_deliver(ctx["run_id"], [topup(ids[0]), topup(ids[0])])
        after_replay, ledger_after_replay = bal(), led()
        res += rc_deliver(ctx["run_id"], [topup(ids[0])], gate=False)      # the same id PAST the gate
        after_ungated, ledger_after_ungated = bal(), led()
        res += rc_deliver(ctx["run_id"], [topup(ids[1])])
        after_new, ledger_end = bal(), led()
        ran = [ran_nodes(ex) for _, _, ex in res]
        ok = ([st for st, _, _ in res] == [200, 200, 200, 200] and all((b or {}).get("success") is True for _, b, _ in res)
              and after_replay == before + 500 and ledger_after_replay == ids[0]
              and {"Dedup Gate", "If Duplicate?", "Map Event", "Upsert Subscriber"} <= ran[0]
              and "If Duplicate?" in ran[1] and not ({"Map Event", "Upsert Subscriber"} & ran[1])
              and "Upsert Subscriber" in ran[2] and "Dedup Gate" not in ran[2]
              and after_ungated == before + 500 and ledger_after_ungated == ids[0]
              and after_new == before + 1000 and ledger_end == ",".join(sorted(ids)))
        return ok, (f"answers={[(st, b) for st, b, _ in res]} balance {before} → after same id twice {after_replay} → same id past "
                    f"the gate {after_ungated} → after a new id {after_new}; ledger after replay={ledger_after_replay!r} after the "
                    f"ungated replay={ledger_after_ungated!r} at end={ledger_end!r}; replay ran {sorted(ran[1])}; ungated ran "
                    f"Upsert={'Upsert Subscriber' in ran[2]}; first ran Upsert={'Upsert Subscriber' in ran[0]}")
    finally:
        pg(f"delete from rc_event_ledger where event_id in ('{ids[0]}', '{ids[1]}');")


@scenario("rc_topup_first_carries_alias_plan", "A2: a reinstall's FIRST RevenueCat event is a top-up (NON_RENEWING_PURCHASE carries no plan) delivered under the new id, the old id an alias holding an active business subscription with 500 dialogs: through the DERIVED RevenueCat Events graph the new identity (Create's trial/trialing seed) becomes business/active with the alias's period and SKU and 500 + 500, the alias is retired (expired, 0); RevenueCat never re-keys a bot (the alias's live bot_profiles row of this very install stays put); the same alias set once more PAST the gate rewrites nothing")
def s_rc_topup_first(ctx):
    o = Owner("a2t", ctx["run_id"])                                   # trial/trialing, no period (cleaned up with the owner)
    old = rc_fixture_id(ctx["run_id"], "a2old")                       # the same owner before the reinstall
    eid = f"e2e-a2t-{ctx['run_id']}-{uuid.uuid4().hex[:8]}"
    pid = f"e2e-a2t-{uuid.uuid4().hex[:12]}"
    body = {"api_version": "1.0", "event": {"id": eid, "type": "NON_RENEWING_PURCHASE", "app_user_id": o.app_user_id,
                                            "original_app_user_id": old, "aliases": [o.app_user_id, old],
                                            "product_id": "topup.dialogs.500", "store": "APP_STORE", "environment": "SANDBOX",
                                            "event_timestamp_ms": int(time.time() * 1000)}}
    row = lambda uid: pg(f"select plan || '|' || status || '|' || topup_balance || '|' || coalesce(current_period_end::text, '-') "
                         f"|| '|' || coalesce(product_id, '-') from subscribers where app_user_id = '{uid}';")
    vers = lambda: pg(f"select string_agg(app_user_id || ':' || xmin::text, ',' order by app_user_id) from subscribers "
                      f"where app_user_id in ('{o.app_user_id}', '{old}');")

    def merged(ex):                                                    # what Consolidate Aliases handed on
        runs = ((ex or {}).get("data") or {}).get("resultData", {}).get("runData", {}).get("Consolidate Aliases") or [{}]
        return (((runs[0].get("data") or {}).get("main") or [[{}]])[0] or [{}])[0].get("json", {})
    try:
        pg(f"insert into subscribers (app_user_id, plan, status, topup_balance, current_period_end, product_id) values "
           f"('{old}', 'business', 'active', 500, now() + interval '30 days', 'sub.business.month'); "
           f"insert into bot_profiles (profile_id, app_user_id, channel, owner_id) values ('{pid}', '{old}', 'whatsapp', '{o.install_id}');")
        period, before = pg(f"select current_period_end::text from subscribers where app_user_id = '{old}';"), row(o.app_user_id)
        res = rc_deliver(ctx["run_id"], [body])
        after, alias_after, versions_after = row(o.app_user_id), row(old), vers()
        bot = pg(f"select app_user_id from bot_profiles where profile_id = '{pid}';")
        res += rc_deliver(ctx["run_id"], [body], gate=False)          # the same alias set again, past the gate
        again, versions_again = row(o.app_user_id), vers()
        ran = [ran_nodes(ex) for _, _, ex in res]
        m = merged(res[0][2])
        ok = ([st for st, _, _ in res] == [200, 200] and all((b or {}).get("success") is True for _, b, _ in res)
              and before == "trial|trialing|0|-|-" and after == f"business|active|1000|{period}|sub.business.month"
              and alias_after.split("|")[1:3] == ["expired", "0"] and bot == old
              # count(*) is a bigint: n8n's pg driver hands it on as the STRING '0'
              and m.get("plan_after") == "business" and m.get("balance_after") == 500 and str(m.get("rekeyed_rows")) == "0"
              and {"Dedup Gate", "Map Event", "Consolidate Aliases", "Upsert Subscriber"} <= ran[0]
              and {"Consolidate Aliases", "Upsert Subscriber"} <= ran[1] and "Dedup Gate" not in ran[1]
              and again == after and versions_again == versions_after)
        return ok, (f"answers={[(st, b) for st, b, _ in res]} owner {before} → {after} (alias period {period}) → past the gate "
                    f"{again}; alias → {alias_after}; bot_profiles row stays on {'the alias' if bot == old else bot!r}; "
                    f"Consolidate handed on plan_after={m.get('plan_after')!r} balance_after={m.get('balance_after')!r} "
                    f"rekeyed_rows={m.get('rekeyed_rows')!r}; row versions unchanged by the replay={versions_again == versions_after}")
    finally:
        pg(f"delete from bot_profiles where profile_id = '{pid}'; delete from subscribers where app_user_id = '{old}'; "
           f"delete from dialog_counts where app_user_id = '{old}'; delete from rc_event_ledger where event_id = '{eid}';")


# ---------------------------------------------------------------------------------------------- A3 identity-heal
RC_PROJECT = "0f8c9529"     # build-verify-identity.RC_PROJECT_ID — the mock answers any project; the path is asserted


def rc_aliases(customer, aliases=None, status=None):
    """A3: the RevenueCat customer the mock knows (derive-staging.py points https://api.revenuecat.com/ at <mock>/revenuecat/):
    its v2 alias list (configure the customer's own id on it, as RevenueCat lists it), or an error status; neither forgets it."""
    body = {"customer_id": customer, "aliases": aliases}
    if status:
        body["status"] = status
    st, d = http("POST", f"{MOCK}/__mock/rc_aliases", body)
    assert st == 200, f"mock rc_aliases: {st} {d}"


def heal_fixture(ctx, tag):
    """A3: the owner after a reinstall — a fresh install whose token's billing id u is the trial seed; the OLD id (a fixture
    RevenueCat id) holds the paid month (business/active/+30 days/SKU, 500 dialogs of balance, 2 dialogs this month);
    bot_profiles: a live row of THIS install still under the old id (claimed before the heal) and a row of ANOTHER install
    under it. Returns (owner, old, mine, other, the old period as text)."""
    o = Owner(tag, ctx["run_id"])
    old = rc_fixture_id(ctx["run_id"], tag + "old")
    mine, other = f"e2e-{tag}-{uuid.uuid4().hex[:12]}", f"e2e-{tag}-{uuid.uuid4().hex[:12]}"
    pg(f"insert into subscribers (app_user_id, plan, status, topup_balance, current_period_end, product_id) values "
       f"('{old}', 'business', 'active', 500, now() + interval '30 days', 'sub.business.month'); "
       f"insert into dialog_counts (app_user_id, chat_id, d) values ('{old}', 'e2e-a3-c1', (now() at time zone 'Asia/Almaty')::date), "
       f"('{old}', 'e2e-a3-c2', (now() at time zone 'Asia/Almaty')::date); "
       f"insert into bot_profiles (profile_id, app_user_id, channel, owner_id) values ('{mine}', '{old}', 'whatsapp', '{o.install_id}'), "
       f"('{other}', '{old}', 'telegram', 'e2e-other-install-{ctx['run_id']}');")
    return o, old, mine, other, pg(f"select current_period_end::text from subscribers where app_user_id = '{old}';")


def heal_cleanup(u, old, *pids):
    rc_aliases(u, None)
    pg("delete from bot_profiles where profile_id in (" + ", ".join(f"'{x}'" for x in pids) + f"); "
       f"delete from subscribers where app_user_id = '{old}'; delete from dialog_counts where app_user_id = '{old}'; "
       f"delete from identity_heal_log where to_app_user_id = '{u}';")


def heal_journal(u, want=None, wait=10.0):
    """identity_heal_log rows for the recipient u (fix round 2): «install|aliases|consolidated|rekeyed|outcome|hint» in write
    order — the hint is the client's original id as Validate accepted it, '-' when none (final review M-5).
    A refusal row is written AFTER the answer (Log Refusal) — with `want`, poll until it is there (or the wait ends)."""
    q = (f"select coalesce(string_agg(install_id || '|' || array_to_string(aliases, ',') || '|' || consolidated || '|' || rekeyed "
         f"|| '|' || coalesce(reason, 'healed') || '|' || coalesce(hint, '-'), ';' order by id), '') "
         f"from identity_heal_log where to_app_user_id = '{u}';")
    deadline = time.time() + wait
    got = pg(q)
    while want is not None and got != want and time.time() < deadline:
        time.sleep(0.5)
        got = pg(q)
    return got


def heal_row(uid):
    return pg(f"select plan || '|' || status || '|' || topup_balance || '|' || coalesce(current_period_end::text, '-') || '|' "
              f"|| coalesce(product_id, '-') from subscribers where app_user_id = '{uid}';")


def heal_versions(u, old, *pids):
    """Every row version the heal could write (subscribers u + old, the bot_profiles rows, both ids' dialog rows)."""
    return pg("select string_agg(t || ':' || k || ':' || v, ',' order by t, k) from ("
              f"select 's' as t, app_user_id as k, xmin::text as v from subscribers where app_user_id in ('{u}', '{old}') "
              "union all select 'b', profile_id, xmin::text from bot_profiles where profile_id in (" + ", ".join(f"'{x}'" for x in pids) + ") "
              f"union all select 'd', app_user_id || chat_id, xmin::text from dialog_counts where app_user_id in ('{u}', '{old}')) x;")


@scenario("identity_heal_consolidates_into_token_id", "A3: after a reinstall the app calls VerifyIdentity; RevenueCat (the mock: v2 aliases of the TOKEN's id, asked with the «RevenueCat API» credential) lists [old, u] → the old id's paid month (business/active/its period/SKU, 500 of balance, this month's dialogs) moves INTO the token's id and the old id is retired; this install's live bot_profiles row under the old id follows u, another install's stays; a repeat changes nothing (row versions); each healed verdict leaves ONE identity_heal_log row, written by the Heal statement itself (fix round 2)")
def s_identity_heal(ctx):
    o, old, mine, other, period = heal_fixture(ctx, "a3h")
    u = o.app_user_id
    body = {"appUserId": u, "originalAppUserId": old}
    try:
        rc_aliases(u, [old, u])
        before = heal_row(u)
        since = mock_total()
        st, d = hook("VerifyIdentity", o.token, body)
        calls, _ = mock_calls(since, "/revenuecat/")
        after, alias_after = heal_row(u), heal_row(old)
        keyed = pg(f"select string_agg(profile_id || '=' || app_user_id, ',' order by profile_id) from bot_profiles where profile_id in ('{mine}', '{other}');")
        used = pg(f"select count(*) from dialog_counts where app_user_id = '{u}' and date_trunc('month', d) = date_trunc('month', (now() at time zone 'Asia/Almaty')::date);")
        v1 = heal_versions(u, old, mine, other)
        j1 = heal_journal(u)
        st2, d2 = hook("VerifyIdentity", o.token, body)
        v2 = heal_versions(u, old, mine, other)
        j2 = heal_journal(u)
        want_j = [f"{o.install_id}|{old}|1|1|healed|{old}", f"{o.install_id}|{old}|0|0|healed|{old}"]
        want_path = f"/revenuecat/v2/projects/{RC_PROJECT}/customers/{urllib.parse.quote(u, safe='')}/aliases"
        want_keyed = ",".join(sorted([f"{mine}={u}", f"{other}={old}"]))
        ok = (st == 200 and d == {"success": True, "aliases": 1, "consolidated": 1, "rekeyed": 1, "truncated": False}
              and before == "trial|trialing|0|-|-" and after == f"business|active|500|{period}|sub.business.month"
              and alias_after.split("|")[1:3] == ["expired", "0"] and keyed == want_keyed and used == "2"
              and len(calls) == 1 and calls[0]["method"] == "GET" and calls[0]["path"] == want_path
              and calls[0]["query"] == {"limit": "1000"} and calls[0]["auth_scheme"] == "Bearer"
              and st2 == 200 and d2 == {"success": True, "aliases": 1, "consolidated": 0, "rekeyed": 0, "truncated": False} and v1 == v2
              # fix round 2: support sees every heal — one journal row per healed verdict, written by the Heal statement itself
              and j1 == want_j[0] and j2 == ";".join(want_j))
        return ok, (f"journal after the heal={j1!r} after the repeat={j2!r} (want {';'.join(want_j)!r}); "
                    f"answer={st} {d} owner {before} → {after} (alias period {period}); alias → {alias_after}; bot_profiles {keyed} "
                    f"(want {want_keyed}); this month's dialogs on u={used}; RevenueCat calls={[(c['method'], c['path'], c['query'], c['auth_scheme']) for c in calls]} "
                    f"(want {want_path}); repeat={st2} {d2} row versions unchanged={v1 == v2}")
    finally:
        heal_cleanup(u, old, mine, other)


@scenario("identity_heal_refuses_without_token", "A3: VerifyIdentity without a token / with a forged one → 401; with a token that carries NO billing id (billingConflict: another install holds it) or a body naming another recipient → 403 — RevenueCat is never asked, nothing is written")
def s_identity_heal_auth(ctx):
    o, old, mine, other, _ = heal_fixture(ctx, "a3n")
    u = o.app_user_id
    x = Owner("a3x", ctx["run_id"], app_user_id=u)                   # the same id named by another install → u = null
    try:
        rc_aliases(u, [old, u])
        v0 = heal_versions(u, old, mine, other)
        since = mock_total()
        body = {"appUserId": u, "originalAppUserId": old}
        st0, d0 = hook("VerifyIdentity", None, body)
        st1, d1 = hook("VerifyIdentity", forge({"i": o.install_id, "u": u, "p": [], "w": [], "e": int(time.time()) + 999}), body)
        st2, d2 = hook("VerifyIdentity", x.token, body)
        st3, d3 = hook("VerifyIdentity", o.token, {"appUserId": old, "originalAppUserId": u})
        calls, _ = mock_calls(since, "/revenuecat/")
        v1 = heal_versions(u, old, mine, other)
        time.sleep(1)                                                 # a refusal row would be written after the answer
        journal = heal_journal(u)
        ok = (st0 == 401 and (d0 or {}).get("success") is False and st1 == 401 and st2 == 403
              and str((d2 or {}).get("reason", "")).startswith("forbidden:appUserId") and st3 == 403 and calls == [] and v0 == v1
              and x.refresh().get("billingConflict") is True and journal == "")
        return ok, (f"no token={st0} {d0} forged={st1} no-billing-id token={st2} {d2} foreign recipient={st3} {d3} "
                    f"RevenueCat calls={len(calls)} row versions unchanged={v0 == v1} journal={journal!r} (want none)")
    finally:
        heal_cleanup(u, old, mine, other)


@scenario("identity_heal_ignores_foreign_hint", "A3: a hint RevenueCat does NOT list for the token's id → 409 {success:false, reason:hint_not_alias} and nothing written to subscribers / bot_profiles — although RevenueCat lists a real alias — but ONE identity_heal_log row for support (after the answer); a RevenueCat error (500) → 502 rc_unavailable, nothing written, no journal row")
def s_identity_heal_foreign(ctx):
    o, old, mine, other, _ = heal_fixture(ctx, "a3f")
    u = o.app_user_id
    foreign = rc_fixture_id(ctx["run_id"], "a3foreign")
    try:
        rc_aliases(u, [old, u])
        v0 = heal_versions(u, old, mine, other)
        since = mock_total()
        st, d = hook("VerifyIdentity", o.token, {"appUserId": u, "originalAppUserId": foreign})
        want_j = f"{o.install_id}|{old}|0|0|hint_not_alias|{foreign}"          # M-5: the id RevenueCat did not confirm
        j1 = heal_journal(u, want=want_j)                              # Log Refusal writes it after the answer
        rc_aliases(u, None, status=500)
        st2, d2 = hook("VerifyIdentity", o.token, {"appUserId": u, "originalAppUserId": old})
        calls, _ = mock_calls(since, "/revenuecat/")
        v1 = heal_versions(u, old, mine, other)
        time.sleep(1)
        j2 = heal_journal(u)
        ok = (st == 409 and d == {"success": False, "reason": "hint_not_alias"} and st2 == 502 and d2 == {"success": False, "reason": "rc_unavailable"}
              and len(calls) == 2 and [c["status"] for c in calls] == [200, 500] and v0 == v1 and heal_row(u) == "trial|trialing|0|-|-"
              # fix round 2: the refusal RevenueCat's list decided is journaled for support (after the answer); the outage is not
              and j1 == want_j and j2 == want_j)
        return ok, (f"foreign hint={st} {d} RevenueCat 500={st2} {d2} RevenueCat calls={[(c['path'][-20:], c['status']) for c in calls]} "
                    f"row versions unchanged={v0 == v1} owner={heal_row(u)} journal after the refusal={j1!r} after the outage={j2!r} "
                    f"(want {want_j!r})")
    finally:
        heal_cleanup(u, old, mine, other)


def demote_trialing_sql(app_user_id, profile_id, channel="whatsapp", reason="trial_expiry"):
    """The DERIVED (= imported) Profile Lifecycle Sweep's own `Demote Trialing` statement — trial expiry, 4d17h after the
    first bot: trialing → expired, the plan ('trial') and the NULL period untouched — bound the way the sweep binds it."""
    src = json.load(open(os.path.join(N8N_DIR, "workflows-staging", SWEEP_FILE), encoding="utf-8"))
    q = next(n for n in src["nodes"] if n["name"] == "Demote Trialing")["parameters"]["query"]
    vals = [app_user_id, profile_id, channel, reason]
    return re.sub(r"\$([1-4])(?!\d)", lambda m: "'" + vals[int(m.group(1)) - 1].replace("'", "''") + "'", q)


@scenario("identity_heal_after_sweep_demotion", "final review I-1: the token's id is the trial seed the lifecycle sweep already DEMOTED (the imported sweep's own Demote Trialing: trial/expired/NULL — on iOS, StoreKit 1 restores nothing by itself, so the merge and this heal usually come after the 4d17h sweep) → VerifyIdentity still hands the paid alias over: the owner is business/active/<the old period>/SKU with the old balance, the old id retired — not locked out until the next renewal")
def s_identity_heal_after_demotion(ctx):
    o, old, mine, other, period = heal_fixture(ctx, "a3d")
    u = o.app_user_id
    try:
        rc_aliases(u, [old, u])
        demoted = pg(demote_trialing_sql(u, mine))
        before = heal_row(u)
        st, d = hook("VerifyIdentity", o.token, {"appUserId": u, "originalAppUserId": old})
        after, alias_after = heal_row(u), heal_row(old)
        journal, want_j = heal_journal(u), f"{o.install_id}|{old}|1|1|healed|{old}"
        ok = (before == "trial|expired|0|-|-" and u in demoted and journal == want_j
              and st == 200 and d == {"success": True, "aliases": 1, "consolidated": 1, "rekeyed": 1, "truncated": False}
              and after == f"business|active|500|{period}|sub.business.month" and alias_after.split("|")[1:3] == ["expired", "0"])
        return ok, (f"sweep demoted the seed: {demoted!r} → owner {before}; heal={st} {d}; owner → {after} (want "
                    f"business|active|500|{period}|sub.business.month); alias → {alias_after}; journal={journal!r} (want {want_j!r})")
    finally:
        heal_cleanup(u, old, mine, other)

@scenario("billing_conflict_is_logged", "V3 (T1): a refresh that answers billingConflict (a second install naming a billing id another install holds) writes ONE billing_conflicts row — claimant, the CURRENT holder, attempts — a repeat bumps attempts, a refresh without a conflict writes nothing, the answer itself is unchanged; once support revokes the holder the id moves on the claimant's next refresh")
def s_billing_conflict_log(ctx):
    a = fresh_owner(ctx, "v3j")
    b = Owner("v3k", ctx["run_id"], app_user_id=a.app_user_id)       # bootstrap + refresh: the first conflict
    q = (f"select claimant_install_id || '|' || holder_install_id || '|' || attempts from billing_conflicts "
         f"where app_user_id = '{a.app_user_id}' order by claimant_install_id;")

    def settled(want):
        for _ in range(20):                                           # the journal is written AFTER the answer
            got = pg(q)
            if got == want:
                return got
            time.sleep(0.5)
        return pg(q)
    first = settled(f"{b.install_id}|{a.install_id}|1")
    d_b = b.refresh()
    second = settled(f"{b.install_id}|{a.install_id}|2")
    d_a = a.refresh()
    time.sleep(2)
    holder_logged = pg(f"select count(*) from billing_conflicts where claimant_install_id = '{a.install_id}';")
    pg(f"update installs set revoked_at = now() where install_id = '{a.install_id}';")
    d_b2 = b.refresh()
    time.sleep(2)
    after = pg(q)
    ok = (first == f"{b.install_id}|{a.install_id}|1" and d_b.get("billingConflict") is True and d_b.get("appUserId") is None
          and second == f"{b.install_id}|{a.install_id}|2" and not d_a.get("billingConflict") and holder_logged == "0"
          and d_b2.get("appUserId") == a.app_user_id and not d_b2.get("billingConflict") and after == second)
    return ok, (f"journal after bootstrap+refresh={first!r} conflict answer={d_b.get('billingConflict')}/{d_b.get('appUserId')} "
                f"after repeat={second!r} holder's refresh logged={holder_logged} after revoke: id={d_b2.get('appUserId') == a.app_user_id} "
                f"conflict={d_b2.get('billingConflict')} journal={after!r}")


# ------------------------------------------------------------------ A4 «защита платящих» (2026-10-14): live profiles + channel watch
ORPHAN_FILE = "2islisFH7jjLoPQM-Delete_Orphan_Profiles.json"
ORPHAN_KEEP = ("Sweep Config", "List WA Profiles", "List TG Profiles", "Live Profiles", "Plan Sweep", "Split Candidates",
               "Re-check Status", "Confirm Orphan", "Still Unauthorized?", "Delete Profile", "Stamp Result", "Sweep Report")


@scenario("orphan_sweep_spares_live_profile", "A4 (+ fix round 1): the orphan sweep (the DERIVED canonical nodes on the real n8n, dry run, TTL ≈ 1 s, two runs so the first-seen ledger ages) NEVER lists a profile Wappi once authorised that has a LIVE bot_profiles row (a logged-out owner's channel: Plan Sweep counts it protectedLive), but still lists as «would delete» a NEVER-authorised profile with a live row (an abandoned wizard: the proxy registered its row at profile/add) and a profile without a row; the live read is proven (liveRead ok); nothing is deleted")
def s_orphan_live(ctx):
    o = fresh_owner(ctx, "orph")                 # proxy profile.add → a LIVE bot_profiles row; the mock keeps it unauthorised
    paid_out = o.profiles[0]                     # … once authorised, then logged out (both timestamps far older than the TTL)
    mock_profile(paid_out, authorized=False, authorized_at="2026-01-01T10:00:00+03:00", logouted_at="2026-01-02T10:00:00+03:00")
    wizard = o.add_profile()                     # a live row too, but never paired: no authorized_at / logouted_at
    orphan = f"e2e-orphan-{ctx['run_id']}"       # a Wappi profile no bot_profiles row names
    mock_profile(orphan, authorized=False, channel="whatsapp")

    def cfg(nodes):
        for a in nodes["Sweep Config"]["parameters"]["assignments"]["assignments"]:
            if a["name"] == "ttlHours":
                a["value"] = 0.0003              # ≈ 1 s: the second run sees all three past the TTL
            if a["name"] == "dryRun":
                a["value"] = True                # never a delete call, even against the mock
    wid, since = None, mock_total()
    try:
        wid, url = probe_workflow(ctx["run_id"], ORPHAN_FILE, ORPHAN_KEEP, "Sweep Config", f"orph{int(time.time())}", edit=cfg)
        e1 = run_probe(wid, url)
        time.sleep(2.5)
        e2 = run_probe(wid, url)
    finally:
        drop_probe(wid)
        mock_profile(orphan, deleted_at="2026-10-14T00:00:00Z")   # the hourly sweep skips it
    s1 = (node_items(e1, "Plan Sweep") or [{}])[0]
    s2 = (node_items(e2, "Plan Sweep") or [{}])[0]
    would = {c.get("profileId") for c in s2.get("wouldDelete") or []}
    deletes, _ = mock_calls(since, "profile/delete")
    ok = (s1.get("liveRead") == "ok" and s2.get("liveRead") == "ok" and paid_out not in would and wizard in would and orphan in would
          and int(s2.get("protectedLive") or 0) >= 1 and "Live Profiles" in ran_nodes(e2) and not deletes)
    return ok, (f"run 1 liveRead={s1.get('liveRead')!r}; run 2 liveRead={s2.get('liveRead')!r} liveRows={s2.get('liveRows')} "
                f"protectedLive={s2.get('protectedLive')} would-delete: once-authorised live={paid_out in would} "
                f"never-authorised live={wizard in would} rowless={orphan in would}; delete calls={len(deletes)}")


# ------------------------------------------------------------------------------------------------------------------ A4 channel watch
WATCH_ID = "crChannelWatch001"
WATCH_FILE = f"{WATCH_ID}-Channel_Watch.json"
WATCH_KEEP = ("List WA Profiles", "List TG Profiles", "Live Rows", "Diff Channel State", "Upsert Snapshot", "Down Alerts?",
              "Pick Other Channel", "Has Other Channel?", "Owner Phone", "Compose Notice", "Skip?", "Send Notice", "Settle Notice",
              "Notice Sent?", "Record Notified")
WATCH_TEXT = {"whatsapp": "Choose Reply: WhatsApp отключился — сотрудник не отвечает. Переподключите канал в приложении.",
              "telegram": "Choose Reply: Telegram отключился — сотрудник не отвечает. Переподключите канал в приложении."}


class WatchProbe:
    """The DERIVED canonical channel watch (every node but its Schedule Trigger) behind a temporary webhook, run on demand.
    The REAL watch (active on staging, every 10 min) is paused meanwhile, so no tick of it acts on a scenario's profiles
    between two probe runs; both are restored on exit (the probe deleted, the real watch re-activated)."""

    def __init__(self, ctx, tag):
        self.run_id, self.tag, self.wid, self.url, self.was_active = ctx["run_id"], tag, None, None, False

    def __enter__(self):
        self.was_active = bool((admin_api("GET", f"/workflows/{WATCH_ID}")[1] or {}).get("active"))
        if self.was_active:
            admin_api("POST", f"/workflows/{WATCH_ID}/deactivate")
        self.wid, self.url = probe_workflow(self.run_id, WATCH_FILE, WATCH_KEEP, "List WA Profiles", f"{self.tag}{int(time.time())}")
        return self

    def run(self):
        """One watch tick → (the execution, the notices sent in it: [(from profile, path, recipient, text)])."""
        since = mock_total()
        ex = run_probe(self.wid, self.url)
        calls, _ = mock_calls(since, "message/send")
        return ex, [((c.get("query") or {}).get("profile_id"), c["path"], (c.get("body") or {}).get("recipient"), (c.get("body") or {}).get("body"))
                    for c in calls if "отключился" in str((c.get("body") or {}).get("body", ""))]

    def __exit__(self, *exc):
        drop_probe(self.wid)
        if self.was_active:
            admin_api("POST", f"/workflows/{WATCH_ID}/activate")
        return False


def snap(*pids):
    """channel_snapshot of these profiles: {pid: 'authorized|notified'} ('-' = no row)."""
    return {p: pg(f"select coalesce((select authorized::text || '|' || (notified_at is not null)::text from channel_snapshot "
                  f"where profile_id = '{p}'), '-');") for p in pids}


def attempts(pid):
    """channel_snapshot.notify_attempts of this profile as text ('-' = no row)."""
    return pg(f"select coalesce((select notify_attempts::text from channel_snapshot where profile_id = '{pid}'), '-');")


def drop_snap(*pids):
    if pids:
        pg(f"delete from channel_snapshot where profile_id in ({', '.join(repr(p) for p in pids)});")


def mock_profile(pid, **fields):
    http("POST", f"{MOCK}/__mock/profile", dict(profile_id=pid, **fields))


def watch_owner(ctx, tag, channels=("whatsapp", "telegram"), authorized=True):
    """A fresh install with a live bot_profiles row per channel (proxy profile.add → Bind Profile), each authorised in the
    mock with its own number; Telegram's authorised account carries a DIFFERENT number than the top-level phone (A5)."""
    o = Owner(tag, ctx["run_id"])
    pids = {}
    for i, ch in enumerate(channels):
        pid = o.add_profile(ch)
        pids[ch] = pid
        n = int(hashlib.sha1(pid.encode()).hexdigest(), 16)
        fields = {"authorized": authorized, "phone": f"7701{n % 10_000_000:07d}"}
        if ch == "telegram":     # the authorised account's own number differs from the top-level phone (the A5 rule picks it)
            fields["account_phone"] = f"+7 (702) {n % 1000:03d}-{(n // 1000) % 100:02d}-{i:02d}"
        mock_profile(pid, **fields)
    return o, pids


def account_digits(pid):
    st, d = http("GET", f"{MOCK}/__mock/state")
    raw = ((d or {}).get("profiles") or {}).get(pid, {}).get("account_phone") or ""
    return re.sub(r"\D", "", raw)


def top_phone(pid):
    st, d = http("GET", f"{MOCK}/__mock/state")
    return ((d or {}).get("profiles") or {}).get(pid, {}).get("phone") or ""


@scenario("channel_down_notifies_other_channel", "A4: the channel watch (the DERIVED canonical nodes on the real n8n + the mock): an owner with WhatsApp + Telegram, both recorded authorised; WhatsApp logs out → EXACTLY ONE notice, sent from the owner's Telegram to the account's own number (A5 rule), the A4 text, notified_at stamped; the next run sends nothing; WhatsApp back → the snapshot says authorised again (notified_at reset), nothing sent")
def s_watch_down(ctx):
    o, p = watch_owner(ctx, "cw1")
    wa, tg = p["whatsapp"], p["telegram"]
    try:
        with WatchProbe(ctx, "cw1") as w:
            _, n1 = w.run()
            s1 = snap(wa, tg)
            mock_profile(wa, authorized=False)
            ex2, n2 = w.run()
            s2 = snap(wa, tg)
            _, n3 = w.run()
            s3 = snap(wa, tg)
            mock_profile(wa, authorized=True)
            _, n4 = w.run()
            s4 = snap(wa, tg)
    finally:
        drop_snap(wa, tg)
    want = [(tg, "/tapi/sync/message/send", account_digits(tg), WATCH_TEXT["whatsapp"])]
    ok = (n1 == [] and s1 == {wa: "true|false", tg: "true|false"} and n2 == want and s2[wa] == "false|true"
          and n3 == [] and s3 == s2 and n4 == [] and s4 == {wa: "true|false", tg: "true|false"})
    return ok, f"run 1 notices={n1} snap={s1}; WA down → notices={n2} (want {want}) snap={s2}; again → {n3} snap={s3}; WA back → {n4} snap={s4}"


@scenario("channel_down_single_channel_only_marks", "A4: an owner with ONE channel (WhatsApp) that logs out → the snapshot records it down and the notice stays owed (notified_at null), but NOTHING is sent — there is no other channel to send through (the app marks it); every run that hands the notice on COUNTS an attempt in the snapshot (fix round 2: a counter, not a clock) — the DOWN run is attempt 1, a retry attempt 2, …, the sixth is the last; after it the watch gives up (no pick, the count stays at six)")
def s_watch_single(ctx):
    o, p = watch_owner(ctx, "cw2", channels=("whatsapp",))
    wa = p["whatsapp"]
    try:
        with WatchProbe(ctx, "cw2") as w:
            _, n1 = w.run()
            s1, t1 = snap(wa), attempts(wa)
            mock_profile(wa, authorized=False)
            ex2, n2 = w.run()
            s2, t2 = snap(wa), attempts(wa)
            ex3, n3 = w.run()
            s3, t3 = snap(wa), attempts(wa)
            pg(f"update channel_snapshot set notify_attempts = 5 where profile_id = '{wa}';")   # attempts 3-5, as runs 4-6 count them
            ex4, n4 = w.run()
            t4 = attempts(wa)
            ex5, n5 = w.run()
            s5, t5 = snap(wa), attempts(wa)
    finally:
        drop_snap(wa)
    picked = node_items(ex2, "Pick Other Channel")
    base = lambda ex: (((node_items(ex, "Diff Channel State") or [{}])[0].get("stats") or {}).get("bases") or {}).get("whatsapp") or {}
    b2, b3, b4, b5 = base(ex2), base(ex3), base(ex4), base(ex5)
    ok = (n1 == [] and s1 == {wa: "true|false"} and t1 == "0" and n2 == [] and s2 == {wa: "false|false"} and t2 == "1"
          and "Down Alerts?" in ran_nodes(ex2) and "Send Notice" not in ran_nodes(ex2) and b2.get("down") == 1
          and n3 == [] and s3 == s2 and t3 == "2" and b3.get("owed") == 1 and "Pick Other Channel" in ran_nodes(ex3)
          and n4 == [] and t4 == "6" and b4.get("owed") == 1 and "Pick Other Channel" in ran_nodes(ex4)
          and n5 == [] and s5 == s2 and t5 == "6" and b5.get("gaveUp") == 1 and b5.get("owed") == 0 and "Pick Other Channel" not in ran_nodes(ex5))
    return ok, (f"run 1 snap={s1} attempts={t1}; WA down → notices={n2} snap={s2} attempts={t2} pick={picked} down={b2.get('down')}; "
                f"again → {n3} attempts={t3} owed={b3.get('owed')}; from 5 → {n4} attempts={t4} owed={b4.get('owed')}; "
                f"after the sixth → {n5} attempts={t5} gaveUp={b5.get('gaveUp')} owed={b5.get('owed')} picked={'Pick Other Channel' in ran_nodes(ex5)}")


@scenario("channel_down_skips_vanished_route", "A4 fix round 2: an owner with two WhatsApp profiles and a Telegram, all recorded authorised; the SECOND WhatsApp (newer — Pick's first choice by order) vanishes from Wappi's list, its snapshot still saying authorised (the watch never touches a profile Wappi does not list), and the first logs out → the ONE notice goes from the owner's Telegram, never from the vanished WhatsApp: Pick takes only what THIS run listed authorised. Then, with the Telegram gone from the list too, a later logout finds no route: nothing is sent, no status asked, the attempt is counted")
def s_watch_vanished(ctx):
    o, p = watch_owner(ctx, "cw5")
    wa, tg = p["whatsapp"], p["telegram"]
    wa2 = o.add_profile("whatsapp")
    mock_profile(wa2, authorized=True, phone="77010005555")
    try:
        with WatchProbe(ctx, "cw5") as w:
            _, n1 = w.run()
            s1 = snap(wa, wa2, tg)
            mock_profile(wa2, listed=False)                  # gone from Wappi's list; get/status would still answer
            mock_profile(wa, authorized=False)
            ex2, n2 = w.run()
            s2 = snap(wa, wa2, tg)
            mock_profile(wa, authorized=True)
            _, n3 = w.run()
            mock_profile(tg, listed=False)                   # now nothing but the vanished WhatsApp could carry a notice
            mock_profile(wa, authorized=False)
            ex4, n4 = w.run()
            s4, t4 = snap(wa, wa2, tg), attempts(wa)
    finally:
        drop_snap(wa, wa2, tg)
    reach2 = json.loads(((node_items(ex2, "Diff Channel State") or [{}])[0]).get("reachable") or "[]")
    want = [(tg, "/tapi/sync/message/send", account_digits(tg), WATCH_TEXT["whatsapp"])]
    ok = (n1 == [] and s1 == {wa: "true|false", wa2: "true|false", tg: "true|false"} and n2 == want and s2[wa] == "false|true"
          and s2[wa2] == "true|false" and wa2 not in reach2 and tg in reach2 and n3 == []
          and n4 == [] and s4[wa] == "false|false" and t4 == "1" and "Pick Other Channel" in ran_nodes(ex4) and "Owner Phone" not in ran_nodes(ex4))
    return ok, (f"run 1 snap={s1}; WA2 vanished + WA down → notices={n2} (want {want}) snap={s2} wa2 reachable={wa2 in reach2}; "
                f"WA back → {n3}; TG vanished too + WA down → notices={n4} snap={s4} attempts={t4} "
                f"status asked={'Owner Phone' in ran_nodes(ex4)}")


@scenario("channel_recovery_updates_snapshot_without_notice", "A4: a profile first seen ALREADY down (a wizard mid-pairing) is recorded without a notice (nothing owed); its pairing (false → true) updates the snapshot and sends nothing; only a later logout (true → false) owes and sends ONE notice — through the owner's other channel")
def s_watch_recovery(ctx):
    o, p = watch_owner(ctx, "cw3")
    wa, tg = p["whatsapp"], p["telegram"]
    mock_profile(wa, authorized=False)                       # the wizard has not paired WhatsApp yet
    try:
        with WatchProbe(ctx, "cw3") as w:
            _, n1 = w.run()
            s1 = snap(wa, tg)
            mock_profile(wa, authorized=True)                # paired
            _, n2 = w.run()
            s2 = snap(wa, tg)
            mock_profile(wa, authorized=False)               # logged out later
            _, n3 = w.run()
            s3 = snap(wa, tg)
    finally:
        drop_snap(wa, tg)
    want = [(tg, "/tapi/sync/message/send", account_digits(tg), WATCH_TEXT["whatsapp"])]
    ok = (n1 == [] and s1 == {wa: "false|true", tg: "true|false"} and n2 == [] and s2 == {wa: "true|false", tg: "true|false"}
          and n3 == want and s3[wa] == "false|true")
    return ok, f"first seen down → notices={n1} snap={s1}; paired → {n2} snap={s2}; logged out → {n3} (want {want}) snap={s3}"


@scenario("channel_watch_empty_list_floor", "A4: Wappi answers an EMPTY WhatsApp list (profiles null) while live WhatsApp rows exist → that base is an invalid fetch (empty_list_floor): nothing recorded, nobody told, the Telegram base still read; the next run (a real list) takes the logout and sends ONE notice")
def s_watch_floor(ctx):
    o, p = watch_owner(ctx, "cw4")
    wa, tg = p["whatsapp"], p["telegram"]
    try:
        with WatchProbe(ctx, "cw4") as w:
            _, n1 = w.run()
            s1 = snap(wa, tg)
            mock_profile(wa, authorized=False)
            http("POST", f"{MOCK}/__mock/fault", {"match": "/api/profile/all/get", "mode": "body", "times": 1,
                                                  "body": {"status": "done", "profiles": None}})
            ex2, n2 = w.run()
            s2 = snap(wa, tg)
            stats = ((node_items(ex2, "Diff Channel State") or [{}])[0].get("stats") or {}).get("bases") or {}
            _, n3 = w.run()
            s3 = snap(wa, tg)
    finally:
        drop_snap(wa, tg)
    want = [(tg, "/tapi/sync/message/send", account_digits(tg), WATCH_TEXT["whatsapp"])]
    wab, tgb = stats.get("whatsapp") or {}, stats.get("telegram") or {}
    ok = (n1 == [] and s1 == {wa: "true|false", tg: "true|false"} and wab.get("valid") is False
          and str(wab.get("reason", "")).startswith("empty_list_floor") and tgb.get("valid") is True and n2 == [] and s2 == s1
          and n3 == want and s3[wa] == "false|true")
    return ok, f"empty WA list → whatsapp base={ {k: wab.get(k) for k in ('valid', 'reason')} } telegram valid={tgb.get('valid')} notices={n2} snap={s2}; real list → {n3} snap={s3}"


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--only"); ap.add_argument("--compat-closed", action="store_true")
    ap.add_argument("--no-llm", action="store_true", help="skip the model step of retired_bot_reconnects_after_purchase")
    ap.add_argument("--list", action="store_true"); a = ap.parse_args()
    if a.list:
        for id_, title, _ in SCENARIOS:
            print(f"{id_:56} {title[:110]}")
        return
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
        flush_deferred_deletes()
    print(f"\n{passed} pass, {failed} fail")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""Wappi transport MOCK for the isolated STAGING n8n (audit remediation, stage 0.3).

Replaces https://wappi.pro/ for every staging workflow (derive-staging.py rewrites the base URL),
so a test message physically cannot reach a real WhatsApp/Telegram chat: nothing here talks to
the network. It records every call (JSONL + in-memory), serves message history fixtures, appends
sent messages to that history (like the real transport echo does), and can inject faults for the
send-idempotency scenarios of stage 3 (accepted-but-response-lost, HTTP 5xx, slow response).

    python3 Tools/n8n/staging/wappi-mock.py                # 127.0.0.1:8765
    WAPPI_MOCK_PORT=8765 WAPPI_MOCK_LOG=/path/calls.jsonl python3 Tools/n8n/staging/wappi-mock.py

Transport routes (both /api/sync/ + /tapi/sync/, and /api/ + /tapi/ for profile ops):
    GET  messages/get?profile_id&chat_id&limit   -> {status:"done", messages:[newest first], count}
    GET  messages/id/get?profile_id&message_id    -> {status:"done", message:{...}}
    POST message/send?profile_id  {body,recipient}-> {status:"done", message_id, time}  (+ appends echo row, marked
    POST message/reply?profile_id {body,message_id[,recipient]} -> {status:"done", message_id, time}
                                                     as the transport marks an API send: from_where "api", task_id)
    messages/get answers carry the top-level uuid = the profile id, as Wappi/tapi do (V7 checks it)
    POST message/mark/read | chat/typing/start | chats/typing/start | message/reaction | chat/delete
    GET  get/status -> {authorized:true, profile_id}; profile/add|delete|logout, profile/all/get,
         webhook/url/set|get, webhook/types/set, qr/get, auth/* -> {status:"done", ...}
Control routes (never exist on the real transport):
    POST /__mock/reset
    POST /__mock/messages  {profile_id, chat_id, messages:[...]}     replace a chat's history
    POST /__mock/incoming  {profile_id, chat_id, message:{...}}      append one incoming row (returns it)
    GET  /__mock/calls?since=<n>&path=<substr>                        recorded calls
    POST /__mock/fault     {match:<path substr>, mode:"status"|"timeout"|"accept_then_drop"|"drop"|"body",
                            status:500, times:1, delay:35, profile_id:<optional>, body:<json, mode "body">}
                           profile_id scopes the fault to calls FOR that profile (query/body profile_id), so a
                           schedule sweeping every profile (the handoff notifier) cannot hand it to another row.
                           mode "cross" (messages/get): answer with the window of chat `cross_chat` of the same
                           profile — the project-confirmed crossed-response bug; `after: <path substr>` keeps a
                           fault dormant until such a call was recorded (e.g. cross only the RECONCILE fetch,
                           i.e. a messages/get after message/send); `echo: {...}` (message/send) is merged into
                           the echo row the send appends — e.g. {"id": "<owner row id>"} or {"from_where": ""};
                           mode "body" (A4) answers `body` with `status` (default 200) instead of the transport's answer —
                           e.g. an EMPTY profile list {"status": "done", "profiles": null} for the channel watch's floor
    POST /__mock/profile   {profile_id, authorized, phone, authorized_at, account_phone, channel, deleted_at, listed}   status
                           fields for get/status AND the profile's entry in profile/all/get — flip `authorized` to take a
                           channel down / bring it back (A4 channel watch). (account_phone: tapi only — the authorised
                           account, returned as account.phone.) profile/all/get lists a profile on its own base only once its
                           `channel` is known (profile/add records it); one without a channel is listed on both; `listed:
                           false` drops it from profile/all/get while get/status still answers (A4 fix round 2: a profile Wappi
                           no longer lists); an empty list is `profiles: null`, as Wappi answers it
    POST /__mock/rc_aliases {customer_id, aliases:[ids] | null, status?, body?}   the RevenueCat customer the mock knows
                           (A3 identity-heal): derive-staging.py rewrites https://api.revenuecat.com/ to <mock>/revenuecat/,
                           and GET /revenuecat/v2/projects/<p>/customers/<id>/aliases answers like RevenueCat v2 (fact-check
                           2026-10-15): 200 {object:"list", items:[{object:"customer.alias", id, created_at}], next_page:null,
                           url} for a configured id (its own id is on the list — configure it), 404 resource_missing for an
                           unknown one; `status`/`body` answer an error instead; `aliases: null` forgets the customer
    GET  /__mock/state
"""
import json
import os
import re
import sys
import threading
import time
import urllib.parse
import uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

PORT = int(os.environ.get("WAPPI_MOCK_PORT", "8765"))
HOST = os.environ.get("WAPPI_MOCK_HOST", "127.0.0.1")
LOG = os.environ.get("WAPPI_MOCK_LOG", "")

LOCK = threading.Lock()
STATE = {"messages": {}, "calls": [], "faults": [], "seq": 0, "profiles": {}, "rc_aliases": {}}


def _key(profile_id, chat_id):
    return f"{profile_id}|{chat_id}"


def _now():
    return int(time.time())


def _new_id(prefix="mock"):
    STATE["seq"] += 1
    return f"{prefix}-{STATE['seq']:06d}"


def _record(entry):
    with LOCK:
        STATE["calls"].append(entry)
    if LOG:
        with open(LOG, "a", encoding="utf-8") as fh:
            fh.write(json.dumps(entry, ensure_ascii=False) + "\n")


def _take_fault(path, profile_id=""):
    with LOCK:
        for f in STATE["faults"]:
            if f.get("profile_id") and f["profile_id"] != profile_id:
                continue
            if f.get("after") and not any(f["after"] in c["path"] for c in STATE["calls"][f.get("armed_at", 0):]):
                continue
            if f["match"] in path and f.get("times", 1) > 0:
                f["times"] -= 1
                return dict(f)
    return None


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, fmt, *args):  # quiet; calls are recorded structurally
        pass

    # ---- helpers -------------------------------------------------------------------------
    def _read_body(self):
        n = int(self.headers.get("Content-Length") or 0)
        raw = self.rfile.read(n) if n else b""
        ctype = (self.headers.get("Content-Type") or "").lower()
        if not raw:
            return {}
        try:
            if "json" in ctype or raw[:1] in (b"{", b"["):
                return json.loads(raw.decode("utf-8"))
            if "form" in ctype:
                return {k: v[0] for k, v in urllib.parse.parse_qs(raw.decode("utf-8")).items()}
        except Exception:
            pass
        return {"_raw": raw.decode("utf-8", "replace")}

    def _send(self, status, obj):
        data = json.dumps(obj, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def _drop(self):
        # Simulate "transport accepted the request, HTTP response lost": no bytes back, close.
        self.close_connection = True

    # ---- dispatch --------------------------------------------------------------------------
    def do_GET(self):
        self._dispatch("GET")

    def do_POST(self):
        self._dispatch("POST")

    def _dispatch(self, method):
        parsed = urllib.parse.urlparse(self.path)
        path = parsed.path
        query = {k: v[0] for k, v in urllib.parse.parse_qs(parsed.query).items()}
        body = self._read_body() if method == "POST" else {}
        if path.startswith("/__mock/"):
            return self._control(method, path, query, body)
        fault = _take_fault(path, query.get("profile_id") or (body.get("profile_id") if isinstance(body, dict) else "") or "")
        authz = self.headers.get("Authorization") or ""
        entry = {"ts": time.time(), "method": method, "path": path, "query": query, "body": body,
                 "auth": bool(authz), "auth_scheme": authz.split(" ", 1)[0] if authz else "", "fault": fault and fault["mode"]}
        if fault and fault["mode"] == "drop":
            # the connection dies BEFORE the transport processes anything: nothing is sent, no echo
            # will ever appear — the honest final state for the caller is send_unknown.
            entry["dropped"] = True
            entry["status"] = None
            _record(entry)
            return self._drop()
        # Route on the tail after the channel prefix.
        channel = "telegram" if path.startswith("/tapi/") else "whatsapp"
        tail = path
        for pre in ("/api/sync/", "/tapi/sync/", "/api/", "/tapi/"):
            if path.startswith(pre):
                tail = path[len(pre):]
                break
        if fault and fault["mode"] == "cross":
            query = dict(query, chat_id=fault.get("cross_chat") or "")
        if path.startswith("/revenuecat/"):
            status, resp = self._revenuecat(method, path)
        else:
            status, resp = self._transport(method, tail, channel, query, body, echo=(fault or {}).get("echo"))
        entry["status"] = status
        entry["resp"] = resp
        if fault and fault["mode"] == "status":
            entry["status"] = int(fault.get("status", 500))
            entry["resp"] = {"status": "error", "detail": "injected"}
            _record(entry)
            return self._send(entry["status"], entry["resp"])
        if fault and fault["mode"] == "body":
            entry["status"] = int(fault.get("status", 200))
            entry["resp"] = fault.get("body")
            _record(entry)
            return self._send(entry["status"], entry["resp"])
        if fault and fault["mode"] == "timeout":
            _record(entry)
            time.sleep(float(fault.get("delay", 35)))
            return self._send(status, resp)
        if fault and fault["mode"] == "accept_then_drop":
            entry["dropped"] = True
            _record(entry)
            return self._drop()
        _record(entry)
        return self._send(status, resp)

    # ---- transport semantics -------------------------------------------------------------
    def _transport(self, method, tail, channel, q, b, echo=None):
        pid = q.get("profile_id") or b.get("profile_id") or ""
        with LOCK:
            if tail == "messages/get":
                rows = list(STATE["messages"].get(_key(pid, q.get("chat_id", "")), []))
                rows.sort(key=lambda m: -(m.get("time") or 0))
                lim = int(q.get("limit") or 15)
                return 200, {"status": "done", "messages": rows[:lim], "count": len(rows), "uuid": pid}
            if tail == "messages/id/get":
                mid = q.get("message_id")
                for rows in STATE["messages"].values():
                    for m in rows:
                        if m.get("id") == mid:
                            return 200, {"status": "done", "message": m}
                return 404, {"status": "error", "detail": "message not found"}
            if tail == "message/send":
                recipient = b.get("recipient") or b.get("=recipient") or ""
                chat_id = recipient if "@" in recipient or channel == "telegram" else f"{recipient}@c.us"
                mid = _new_id("sent")
                row = {"id": mid, "chatId": chat_id, "from": recipient, "fromMe": True, "type": "chat",
                       "body": b.get("body", ""), "time": _now(), "senderName": "mock-bot",
                       "quoted_message_id": b.get("quoted_message_id"),
                       "from_where": "api", "task_id": uuid.uuid4().hex, "isFromAPI": True}
                if isinstance(echo, dict):
                    row.update(echo)
                STATE["messages"].setdefault(_key(pid, chat_id), []).append(row)
                return 200, {"status": "done", "message_id": row["id"], "time": row["time"]}
            if tail == "message/reply":
                recipient = b.get("recipient") or ""
                quoted = b.get("message_id")
                chat_id = recipient
                if not chat_id and quoted:
                    for key, rows in STATE["messages"].items():
                        if any(m.get("id") == quoted for m in rows):
                            chat_id = key.split(":", 1)[1] if ":" in key else key
                            break
                mid = _new_id("sent")
                row = {"id": mid, "chatId": chat_id, "from": chat_id, "fromMe": True, "type": "chat",
                       "body": b.get("body", ""), "time": _now(), "senderName": "mock-bot", "quoted_message_id": quoted,
                       "from_where": "api", "task_id": uuid.uuid4().hex, "isFromAPI": True}   # the API marks, as message/send (V7)
                if isinstance(echo, dict):
                    row.update(echo)
                if chat_id:
                    STATE["messages"].setdefault(_key(pid, chat_id), []).append(row)
                return 200, {"status": "done", "message_id": row["id"], "time": row["time"]}
            if tail in ("message/mark/read", "chat/typing/start", "chats/typing/start",
                        "message/reaction", "chat/delete", "webhook/url/set", "webhook/types/set",
                        "profile/delete", "profile/logout"):
                return 200, {"status": "done"}
            if tail == "get/status":
                prof = STATE["profiles"].get(pid) or {}
                out = {"authorized": prof.get("authorized", True), "profile_id": pid,
                       "phone": prof.get("phone"), "authorized_at": prof.get("authorized_at"), "logouted_at": prof.get("logouted_at")}
                if channel == "telegram" and prof.get("account_phone"):
                    out["account"] = {"phone": prof["account_phone"], "user_id": 1000000000}   # tapi: the authorised account
                return 200, out
            if tail == "profile/add":
                new = _new_id("profile")
                STATE["profiles"][new] = {"channel": channel, "authorized": False}
                return 200, {"status": "done", "profile_id": new}
            if tail == "profile/all/get":
                listed = [dict(profile_id=k, **v) for k, v in STATE["profiles"].items()
                          if v.get("channel") in (None, channel) and v.get("listed", True) is not False]
                return 200, {"status": "done", "profiles": listed or None}   # Wappi answers null for an empty list
            if tail == "webhook/url/get":
                return 200, {"status": "done", "url": ""}
            if tail in ("qr/get", "auth/qr"):
                return 200, {"status": "done", "qr": "MOCK"}
            if tail in ("auth/code", "auth/phone", "auth/2fa"):
                return 200, {"status": "done", "code": "MOCK-CODE", "detail": "auth_success"}
            if tail == "chats/filter":
                return 200, {"status": "done", "dialogs": []}
        return 404, {"status": "error", "detail": f"mock: unknown route {tail}"}

    # ---- RevenueCat API v2 (A3 identity-heal) -----------------------------------------------
    RC_ALIASES = re.compile(r"^/revenuecat/v2/projects/([^/]+)/customers/([^/]+)/aliases$")
    RC_MISSING = {"object": "error", "type": "resource_missing", "message": "Could not find customer ID associated with this project",
                  "retryable": False, "doc_url": "https://errors.rev.cat/resource-missing"}

    def _revenuecat(self, method, path):
        m = self.RC_ALIASES.match(path)
        if method != "GET" or not m:
            return 404, {"object": "error", "type": "resource_missing", "message": f"mock: unknown RevenueCat route {method} {path}"}
        customer = urllib.parse.unquote(m.group(2))
        with LOCK:
            cfg = STATE["rc_aliases"].get(customer)
        if cfg is None:
            return 404, dict(self.RC_MISSING)
        if int(cfg.get("status") or 200) != 200:
            return int(cfg["status"]), cfg.get("body") or {"object": "error", "type": "server_error", "message": "mock: injected"}
        items = [{"object": "customer.alias", "id": i, "created_at": 1787731781561 + k} for k, i in enumerate(cfg.get("aliases") or [])]
        return 200, {"object": "list", "items": items, "next_page": None,
                     "url": f"https://api.revenuecat.com/v2/projects/{m.group(1)}/customers/{customer}/aliases"}

    # ---- control -------------------------------------------------------------------------
    def _control(self, method, path, q, b):
        with LOCK:
            if path == "/__mock/reset" and method == "POST":
                STATE.update({"messages": {}, "calls": [], "faults": [], "seq": 0, "profiles": {}, "rc_aliases": {}})
                return self._send(200, {"ok": True})
            if path == "/__mock/messages" and method == "POST":
                STATE["messages"][_key(b["profile_id"], b["chat_id"])] = list(b.get("messages") or [])
                return self._send(200, {"ok": True, "count": len(STATE["messages"][_key(b["profile_id"], b["chat_id"])])})
            if path == "/__mock/incoming" and method == "POST":
                m = dict(b["message"])
                m.setdefault("id", _new_id("in"))
                m.setdefault("chatId", b["chat_id"])
                m.setdefault("from", b["chat_id"])
                m.setdefault("fromMe", False)
                m.setdefault("type", "chat")
                m.setdefault("time", _now())
                m.setdefault("profile_id", b["profile_id"])
                STATE["messages"].setdefault(_key(b["profile_id"], b["chat_id"]), []).append(m)
                return self._send(200, {"ok": True, "message": m})
            if path == "/__mock/calls" and method == "GET":
                since = int(q.get("since") or 0)
                sub = q.get("path")
                rows = [c for c in STATE["calls"][since:] if not sub or sub in c["path"]]
                return self._send(200, {"total": len(STATE["calls"]), "calls": rows})
            if path == "/__mock/fault" and method == "POST":
                f = {"match": b["match"], "mode": b.get("mode", "status"),
                     "status": int(b.get("status", 200 if b.get("mode") == "body" else 500)), "body": b.get("body"),
                     "times": int(b.get("times", 1)), "delay": float(b.get("delay", 35)),
                     "profile_id": b.get("profile_id") or "", "after": b.get("after") or "",
                     "cross_chat": b.get("cross_chat") or "", "armed_at": len(STATE["calls"]),
                     "echo": b.get("echo") if isinstance(b.get("echo"), dict) else None}
                STATE["faults"].append(f)
                return self._send(200, {"ok": True, "fault": f})
            if path == "/__mock/rc_aliases" and method == "POST":
                if b.get("aliases") is None and not b.get("status"):
                    STATE["rc_aliases"].pop(b["customer_id"], None)
                else:
                    STATE["rc_aliases"][b["customer_id"]] = {"aliases": list(b.get("aliases") or []), "status": int(b.get("status") or 200),
                                                             "body": b.get("body")}
                return self._send(200, {"ok": True, "customer": STATE["rc_aliases"].get(b["customer_id"])})
            if path == "/__mock/profile" and method == "POST":
                STATE["profiles"].setdefault(b["profile_id"], {}).update({k: v for k, v in b.items() if k != "profile_id"})
                return self._send(200, {"ok": True, "profile": STATE["profiles"][b["profile_id"]]})
            if path == "/__mock/state" and method == "GET":
                return self._send(200, {"chats": {k: len(v) for k, v in STATE["messages"].items()},
                                        "calls": len(STATE["calls"]), "faults": STATE["faults"],
                                        "profiles": STATE["profiles"], "rc_aliases": STATE["rc_aliases"]})
        return self._send(404, {"error": "unknown control route"})


def main():
    srv = ThreadingHTTPServer((HOST, PORT), Handler)
    print(f"wappi-mock listening on http://{HOST}:{PORT}  log={LOG or '(memory only)'}", flush=True)
    try:
        srv.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Structural verifier for the channel watch crChannelWatch001 «Channel Watch» — A4 «защита платящих» (2026-10-14; spec
docs/superpowers/specs/2026-10-14-paying-users-protection-and-schedule-design.md §2.2, §3 A4, §1). No network. Behaviour
lives in tests/test_channel_watch.py (the Diff over fixture lists, every statement on temp tables, the whole graph through
tests/notifier_chain.js); this file pins what that behaviour rests on, so a hand edit, a stale export or a careless
generator edit fails:

  (1) the workflow IS build-channel-watch.py's output, and no node is disabled (a disabled node hands its input on
      untouched: a disabled gate lets every item through, a disabled Diff hands the lists to the snapshot write);
  (2) a Schedule Trigger «*/10 * * * *» → List WA Profiles → List TG Profiles → Live Rows → Diff Channel State → Upsert
      Snapshot — Wappi's profile/all/get on BOTH bases (fullResponse + neverError: an error is an item to judge, retried,
      continue), Live Rows reads the LIVE rows (no write) and fails the run on a database fault;
  (3) Diff Channel State carries the sweeps' rules: a base only on HTTP 200 + status done + a list (null = empty), the
      empty-list floor, the boolean-only flag, the proportional cap max(2, 50%) on DOWN transitions, and the transitions
      themselves (DOWN owes a notice, UP resets it, a first sighting is recorded without one, an owed notice retried);
  (4) Upsert Snapshot is ONE statement that writes only channel_snapshot (the states, the rows without a live
      bot_profiles row deleted) and hands back the notices owed; it fails the run on a database fault;
  (5) nothing is sent without passing every gate: Down Alerts? (a positive field of Upsert Snapshot's row), Has Other
      Channel? (a different profile than the down one), Skip? false — Send Notice is unreachable with any of those cut;
      Record Notified only after Notice Sent? true;
  (6) the notice goes through ANOTHER live, authorised channel of the SAME install, never the down one: Pick Other
      Channel's statement, Owner Phone and Send Notice address the OTHER profile; Compose Notice embeds
      build-handoff-workflows.OWNER_PHONE_JS verbatim (the A5 rule, one text), requires the other channel to say it is
      authorised and to echo its own profile id, and its text names only the channel (no client text, no client number);
  (7) crChannelWatch001 is a protected infra id (build-client-webhooks.PROTECTED_IDS + Set Workflow State's list);
  (8) the migration sql/2026-10-14-channel-snapshot.sql defines the table the statements use, RLS on, client roles revoked.

MUTATIONS is the tripwire's own list; a normal run applies each one to a copy of a PASSING workflow and fails if the checks
accept it. The test file runs the behavioural ones through the Diff / the graph as well (a tripwire nothing can see is
unproven).

    python3 Tools/n8n/verify-channel-watch.py              # the canonical workflows
    python3 Tools/n8n/verify-channel-watch.py --dir DIR    # another set of exports (mutation tests)
"""
import argparse
import copy
import importlib.util
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))


def _module(name, filename):
    spec = importlib.util.spec_from_file_location(name, os.path.join(HERE, filename))
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


B = _module("build_channel_watch", "build-channel-watch.py")
HW = B.HW
BCW = _module("build_client_webhooks", "build-client-webhooks.py")
MIGRATION = os.path.join(HERE, "sql", "2026-10-14-channel-snapshot.sql")
EXPECTED = ("Every 10 Minutes", "List WA Profiles", "List TG Profiles", "Live Rows", "Diff Channel State", "Upsert Snapshot",
            "Down Alerts?", "Pick Other Channel", "Has Other Channel?", "Owner Phone", "Compose Notice", "Skip?", "Send Notice",
            "Settle Notice", "Notice Sent?", "Record Notified")
CHAIN = (("Every 10 Minutes", 0, "List WA Profiles"), ("List WA Profiles", 0, "List TG Profiles"), ("List TG Profiles", 0, "Live Rows"),
         ("Live Rows", 0, "Diff Channel State"), ("Diff Channel State", 0, "Upsert Snapshot"), ("Upsert Snapshot", 0, "Down Alerts?"),
         ("Down Alerts?", 0, "Pick Other Channel"), ("Pick Other Channel", 0, "Has Other Channel?"), ("Has Other Channel?", 0, "Owner Phone"),
         ("Owner Phone", 0, "Compose Notice"), ("Compose Notice", 0, "Skip?"), ("Skip?", 1, "Send Notice"), ("Send Notice", 0, "Settle Notice"),
         ("Settle Notice", 0, "Notice Sent?"), ("Notice Sent?", 0, "Record Notified"))

# (3) the Diff's rules, as the generator states them
DIFF_RULES = {
    "a base counts only on HTTP 200": "if (code !== 200) return { ok: false, reason: 'http_' + code };",
    "a base counts only on status done": "if (!b || b.status !== 'done') return { ok: false, reason: 'bad_status: '",
    "a base's list must be an array (null = empty)": "if (!Array.isArray(profiles)) return { ok: false, reason: 'profiles_not_array' };",
    "the empty-list floor": "if (base.ok && base.profiles.length === 0 && live > 0) {",
    "the boolean-only flag": "if (typeof p.authorized !== 'boolean') { bs.skipped.push(",
    "a profile Wappi lists as deleted is skipped": "if (p.deleted_at) { bs.skipped.push(",
    "the cap": "cap: Math.max(CAP_FLOOR, live * CAP_SHARE)",
    "the cap skips the whole base": "if (downs > bs.cap) {",
    "DOWN owes a notice": "} else if (prev === true && cur === false) {",
    "UP resets the debt": "} else if (prev === false && cur === true) {",
    "an owed notice is retried": "} else if (prev === false && cur === false && r.prev_notified !== true) {",
    "an owed notice is retried only within the Notifier's attempts": "const young = typeof r.prev_age_s === 'number' && r.prev_age_s >= 0 && r.prev_age_s < OWED_RETRY_S;",
    "past the retry window an owed notice is given up": "planned.push(young ? { kind: 'owed', alert: alert } : { kind: 'gaveUp' });",
    "a first sighting is recorded without a notice": "planned.push({ kind: 'fresh', write: { profile_id: r.profile_id, channel: ch, authorized: cur, settled: cur === false } });",
    "only an owner can be notified": "const alert = owner ? { profile_id: r.profile_id, channel: ch, owner_id: owner } : null;",
    "an unproven live read writes nothing": "if (!rowsProven) {",
}


def node(wf, name):
    return next((n for n in wf.get("nodes", []) if n.get("name") == name), None)


def targets(wf, src):
    return [[e["node"] for e in (out or [])] for out in (wf["connections"].get(src) or {}).get("main", [])]


def sources(wf, dst):
    return sorted((s, i) for s, c in wf["connections"].items() for i, out in enumerate((c or {}).get("main") or [])
                  for e in (out or []) if e.get("node") == dst)


def reachable(wf, start, cut=None):
    seen, todo = set(), [start]
    while todo:
        n = todo.pop()
        if n in seen:
            continue
        seen.add(n)
        for i, out in enumerate((wf["connections"].get(n) or {}).get("main") or []):
            if cut and (n, i) == cut:
                continue
            todo.extend(e["node"] for e in (out or []))
    return seen


def condition(wf, name):
    conds = ((node(wf, name) or {}).get("parameters") or {}).get("conditions", {}).get("conditions") or []
    return conds[0]["leftValue"] if len(conds) == 1 else None


def resp_opts(n):
    return (((n.get("parameters") or {}).get("options") or {}).get("response") or {}).get("response") or {}


def check(wf, set_state, migration_sql):
    fails = []

    def need(cond, msg):
        if not cond:
            fails.append(msg)
        return cond

    names = [n.get("name") for n in wf.get("nodes", [])]
    for name in EXPECTED:
        need(names.count(name) == 1, f"expected exactly one node {name!r}, found {names.count(name)}")
    if any(names.count(x) != 1 for x in EXPECTED):
        return fails
    N = {n: node(wf, n) for n in EXPECTED}

    # (1) the generator's output; nothing disabled
    need(wf == B.build(), f"{B.WF_FILE} differs from build-channel-watch.py — edit the generator and re-run it, never the JSON")
    for name, n in N.items():
        need(not n.get("disabled"), f"{name} is disabled — n8n hands a disabled node's input on untouched (a disabled gate lets "
                                    f"every item through, a disabled Diff hands Wappi's lists to the snapshot write)")

    # (2) the schedule, the two lists, the live read
    rule = ((N["Every 10 Minutes"].get("parameters") or {}).get("rule") or {}).get("interval") or []
    need(N["Every 10 Minutes"].get("type") == "n8n-nodes-base.scheduleTrigger" and rule == [{"field": "cronExpression", "expression": B.CRON}],
         f"the trigger must be a Schedule Trigger on cron {B.CRON!r}: {rule}")
    for name, base in (("List WA Profiles", B.WA_BASE), ("List TG Profiles", B.TG_BASE)):
        n = N[name]
        p = n.get("parameters") or {}
        need(p.get("method") == "GET" and p.get("url") == base + "profile/all/get",
             f"{name} must GET {base}profile/all/get: {p.get('method')} {p.get('url')}")
        need(resp_opts(n).get("fullResponse") is True and resp_opts(n).get("neverError") is True,
             f"{name} must run with fullResponse + neverError — a Wappi error is a status Diff Channel State judges: {resp_opts(n)}")
        need(n.get("retryOnFail") is True and n.get("onError") == "continueRegularOutput" and n.get("alwaysOutputData") is True,
             f"{name} must retry and continue with output (a failed list reaches the Diff as an invalid base): "
             f"retryOnFail={n.get('retryOnFail')} onError={n.get('onError')}")
        need(n.get("credentials") == {"httpHeaderAuth": B.auth.WAPPI_CRED}, f"{name} must use the WappiAuthToken credential: {n.get('credentials')}")
    lr = N["Live Rows"]
    lq = (lr.get("parameters") or {}).get("query") or ""
    need(lq == B.LIVE_ROWS_SQL, "Live Rows is stale against build-channel-watch.py (re-run the generator)")
    need(re.search(r"where b\.deleted_at is null\b", lq) and "left join channel_snapshot s on s.profile_id = b.profile_id" in lq
         and not re.search(r"\b(insert|update|delete)\b", re.sub(r"--[^\n]*", "", lq), re.I),
         "Live Rows must read the LIVE bot_profiles rows with the previous snapshot — and write nothing")
    need(lr.get("alwaysOutputData") is True and "onError" not in lr,
         f"Live Rows must fail the run on a database fault (no continue) and hand on an empty answer: onError={lr.get('onError')} "
         f"alwaysOutputData={lr.get('alwaysOutputData')}")
    for name in ("Live Rows", "Upsert Snapshot", "Pick Other Channel", "Record Notified"):
        need((N[name].get("credentials") or {}).get("postgres") == B.auth.PG_CRED,
             f"{name} must run on the canonical Postgres credential {B.auth.PG_CRED}: {N[name].get('credentials')}")
    for src, out, dst in CHAIN:
        need(dst in (targets(wf, src)[out] if len(targets(wf, src)) > out else []), f"the chain is broken: {src}[{out}] must feed {dst}")
        need(sources(wf, dst) == [(src, out)], f"{dst} must be entered ONLY from {src}[{out}]: {sources(wf, dst)}")

    # (3) the Diff's rules
    djs = (N["Diff Channel State"].get("parameters") or {}).get("jsCode") or ""
    for label, fragment in DIFF_RULES.items():
        need(fragment in djs, f"Diff Channel State lost a rule: {label}")
    need(f"const CAP_FLOOR = {B.CAP_FLOOR};" in djs and f"const CAP_SHARE = {B.CAP_SHARE};" in djs and B.CAP_FLOOR == 2 and B.CAP_SHARE == 0.5,
         "the cap must be max(2, 50%) — the liveness branch's numbers")
    need(f"const OWED_RETRY_S = {B.OWED_RETRY_SECONDS};" in djs and B.OWED_RETRY_SECONDS == HW.MAX_NOTIFY_ATTEMPTS * B.PERIOD_MINUTES * 60
         and B.PERIOD_MINUTES * 60 == 600 and "floor(extract(epoch from now() - s.seen_at))::int as prev_age_s" in lq,
         "an owed notice is retried at most the Handoff Notifier's attempts (MAX_NOTIFY_ATTEMPTS × the 10-minute period, by the "
         "recorded DOWN's age from Live Rows) — never every 10 minutes for the whole outage")
    need("$('List WA Profiles').first().json" in djs and "$('List TG Profiles').first().json" in djs,
         "Diff Channel State must judge both lists")

    # (4) the snapshot write
    up = N["Upsert Snapshot"]
    uq = (up.get("parameters") or {}).get("query") or ""
    need(uq == B.UPSERT_SNAPSHOT_SQL and (up.get("parameters") or {}).get("options", {}).get("queryReplacement") == B.UPSERT_SNAPSHOT_PARAMS,
         "Upsert Snapshot is stale against build-channel-watch.py (re-run the generator)")
    code_only = re.sub(r"--[^\n]*", "", uq)
    written = set(re.findall(r"\b(?:insert\s+into|(?<!do )update|delete\s+from)\s+([a-z_]+)", code_only, re.I))   # «do update set» is the upsert's own clause
    need(written == {"channel_snapshot"}, f"Upsert Snapshot may write channel_snapshot ONLY: {sorted(written)}")
    need("not exists (select 1 from bot_profiles b where b.profile_id = s.profile_id and b.deleted_at is null)" in uq,
         "Upsert Snapshot must delete the snapshot rows of profiles without a live bot_profiles row")
    need("onError" not in up and up.get("alwaysOutputData") is True,
         f"Upsert Snapshot must fail the run on a database fault — no notice without its snapshot: onError={up.get('onError')}")
    need(condition(wf, "Down Alerts?") == B.DOWN_ALERT_EXPR and "$json.alert === true" in (condition(wf, "Down Alerts?") or ""),
         f"Down Alerts? must pass ONLY a notice row of Upsert Snapshot (a positive field, never «the node continued»): "
         f"{condition(wf, 'Down Alerts?')!r}")

    # (5) the gates
    for gate, idx in (("Down Alerts?", 0), ("Has Other Channel?", 0), ("Skip?", 1)):
        need("Send Notice" not in reachable(wf, "Every 10 Minutes", cut=(gate, idx)),
             f"Send Notice is reachable without passing {gate}[{idx}] — a notice could go out ungated")
    need("Record Notified" not in reachable(wf, "Every 10 Minutes", cut=("Notice Sent?", 0)),
         "Record Notified is reachable without Notice Sent? true — an undelivered notice would be recorded as told")
    need(condition(wf, "Notice Sent?") == B.SENT_EXPR, f"Notice Sent? must pass ONLY a delivered notice: {condition(wf, 'Notice Sent?')!r}")
    need(condition(wf, "Skip?") == "={{ $json.skip }}", f"Skip? must read Compose Notice's skip: {condition(wf, 'Skip?')!r}")

    # (6) another channel of the same install, never the down one; the A5 rule; no client data
    pq = (N["Pick Other Channel"].get("parameters") or {}).get("query") or ""
    need(pq == B.PICK_OTHER_SQL and (N["Pick Other Channel"].get("parameters") or {}).get("options", {}).get("queryReplacement") == B.PICK_OTHER_PARAMS,
         "Pick Other Channel is stale against build-channel-watch.py (re-run the generator)")
    need("b.owner_id = $3::text" in pq and "b.profile_id <> $1::text" in pq and "join channel_snapshot s on s.profile_id = b.profile_id and s.authorized" in pq
         and "where b.deleted_at is null" in pq and "order by (b.channel = 'whatsapp') desc" in pq,
         "Pick Other Channel must pick ANOTHER live, authorised channel of the SAME owner (WhatsApp first) — never the down profile")
    need(condition(wf, "Has Other Channel?") == B.HAS_OTHER_EXPR and "$json.other_profile_id !== $json.down_profile_id" in (condition(wf, "Has Other Channel?") or ""),
         f"Has Other Channel? must refuse a pick that names the down profile itself: {condition(wf, 'Has Other Channel?')!r}")
    op = N["Owner Phone"].get("parameters") or {}
    need(op.get("url") == B.OWNER_PHONE_URL and op.get("jsonQuery") == B.OWNER_PHONE_QUERY and "other_profile_id" in (op.get("jsonQuery") or "")
         and "other_channel" in (op.get("url") or ""),
         f"Owner Phone must read get/status of the OTHER channel: url={op.get('url')!r} query={op.get('jsonQuery')!r}")
    cjs = (N["Compose Notice"].get("parameters") or {}).get("jsCode") or ""
    need(HW.OWNER_PHONE_JS in cjs and cjs.count("function ownerPhone(") == 1 and "const phone = error ? '' : ownerPhone(wapi, status);" in cjs,
         "Compose Notice must embed build-handoff-workflows.OWNER_PHONE_JS verbatim (the A5 rule) and read the owner's number with it")
    need("profileId: pick.other_profile_id" in cjs and "downProfileId: pick.down_profile_id" in cjs,
         "Compose Notice must send from the OTHER profile and settle the DOWN one")
    need("else if (status.profile_id !== pick.other_profile_id) error = 'status_mismatch';" in cjs
         and "else if (status.authorized !== true) error = 'other_channel_unauthorized';" in cjs,
         "Compose Notice must require the other channel's get/status to echo its profile id and say authorised")
    need("const text = 'Choose Reply: ' + what + ' отключился — бот не отвечает. Переподключите канал в приложении.';" in cjs
         and all(t.startswith("Choose Reply: ") for t in B.NOTICE_TEXT.values()),
         "Compose Notice must send the A4 text: «Choose Reply: {WhatsApp|Telegram} отключился — бот не отвечает. …»")
    need(not re.search(r"\b(chat_id|chatId|client|recipient_name|summary|body\.)\b", re.sub(r"//[^\n]*", "", cjs).replace("reply.body", "")),
         "Compose Notice must carry no client data (spec §1, the A5 rule)")
    for name in ("Owner Phone", "Send Notice"):
        n = N[name]
        need(n.get("onError") == "continueRegularOutput" and resp_opts(n).get("fullResponse") is True and resp_opts(n).get("neverError") is True
             and ((n.get("parameters") or {}).get("options") or {}).get("batching") == B.BATCH,
             f"{name} must run one call at a time (Wappi crosses concurrent answers), fullResponse + neverError, continue")
    sp = N["Send Notice"].get("parameters") or {}
    need(sp.get("method") == "POST" and sp.get("url") == "={{ $json.url }}" and sp.get("jsonQuery") == B.SEND_QUERY and sp.get("jsonBody") == B.SEND_BODY,
         f"Send Notice must POST Compose Notice's url from its profileId: {sp.get('url')} {sp.get('jsonQuery')}")
    sjs = (N["Settle Notice"].get("parameters") or {}).get("jsCode") or ""
    need("const ok = code === 200 && (body.status === 'done' || !!body.message_id);" in sjs,
         "Settle Notice must count a notice delivered ONLY on HTTP 200 with done / a message id (the notifier's rule)")
    rq = (N["Record Notified"].get("parameters") or {}).get("query") or ""
    need(rq == B.RECORD_NOTIFIED_SQL and "where profile_id = $1::text and authorized = false and notified_at is null" in rq,
         "Record Notified must stamp notified_at only on a row still down and owed")

    # (7) protected infra id
    need(B.WF_ID in BCW.PROTECTED_IDS, f"{B.WF_ID} is missing from build-client-webhooks.PROTECTED_IDS (protected infra ids)")
    vs = [n for n in set_state.get("nodes", []) if "const PROTECTED = new Set([" in (n.get("parameters") or {}).get("jsCode", "")]
    need(len(vs) == 1 and f"'{B.WF_ID}'" in vs[0]["parameters"]["jsCode"],
         f"Set Workflow State's protected list must carry {B.WF_ID} — SetWorkflowState could otherwise deactivate/delete it")

    # (8) the table the statements use
    m = migration_sql or ""
    need("create table if not exists public.channel_snapshot (" in m and re.search(r"profile_id\s+text primary key", m)
         and re.search(r"channel\s+text not null check \(channel in \('whatsapp', 'telegram'\)\)", m)
         and re.search(r"authorized\s+boolean not null", m) and re.search(r"seen_at\s+timestamptz not null default now\(\)", m)
         and re.search(r"notified_at timestamptz\s", m)
         and "alter table public.channel_snapshot enable row level security;" in m
         and "revoke all on table public.channel_snapshot from anon, authenticated;" in m,
         "sql/2026-10-14-channel-snapshot.sql must create channel_snapshot (profile_id pk, channel, authorized, seen_at, "
         "notified_at) with RLS on and the client roles revoked")
    return fails


# ---------------------------------------------------------------------------------------------------------------------
# The tripwire's own list: (label, mutation of a workflow dict, the message the checks must produce).
def _js(name, old, new):
    def m(wf):
        p = node(wf, name)["parameters"]
        assert old in p["jsCode"], f"mutation anchor not found in {name}: {old[:60]!r}"
        p["jsCode"] = p["jsCode"].replace(old, new, 1)
    return m


def _query(name, old, new):
    def m(wf):
        p = node(wf, name)["parameters"]
        assert old in p["query"], f"mutation anchor not found in {name}: {old[:60]!r}"
        p["query"] = p["query"].replace(old, new, 1)
    return m


def _set(name, key, value):
    def m(wf):
        n = node(wf, name)
        if value is None:
            n.pop(key, None)
        else:
            n[key] = value
    return m


def _rewire(src, out, dst):
    def m(wf):
        conns = wf["connections"].setdefault(src, {"main": []})["main"]
        while len(conns) <= out:
            conns.append([])
        conns[out] = [{"node": dst, "type": "main", "index": 0}]
    return m


def _param(name, key, value):
    def m(wf):
        node(wf, name)["parameters"][key] = value
    return m


MUTATIONS = (
    ("Diff Channel State disabled", _set("Diff Channel State", "disabled", True), "Diff Channel State is disabled"),
    ("Down Alerts? disabled", _set("Down Alerts?", "disabled", True), "Down Alerts? is disabled"),
    ("Has Other Channel? disabled", _set("Has Other Channel?", "disabled", True), "Has Other Channel? is disabled"),
    ("the empty-list floor removed", _js("Diff Channel State", "if (base.ok && base.profiles.length === 0 && live > 0) {",
                                         "if (false) {"), "Diff Channel State lost a rule: the empty-list floor"),
    ("the cap removed", _js("Diff Channel State", "if (downs > bs.cap) {", "if (false) {"), "Diff Channel State lost a rule: the cap skips the whole base"),
    ("owed notices retried for the whole outage (the retry window removed)",
     _js("Diff Channel State", "const young = typeof r.prev_age_s === 'number' && r.prev_age_s >= 0 && r.prev_age_s < OWED_RETRY_S;",
         "const young = true;"), "Diff Channel State lost a rule: an owed notice is retried only within the Notifier's attempts"),
    ("the ambiguity rule removed (a non-boolean flag read as down)",
     _js("Diff Channel State", "if (typeof p.authorized !== 'boolean') {", "if (false) {"), "Diff Channel State lost a rule: the boolean-only flag"),
    ("a base trusted without status done", _js("Diff Channel State", "if (!b || b.status !== 'done') return",
                                               "if (!b) return"), "Diff Channel State lost a rule: a base counts only on status done"),
    ("the notice to the SAME down channel (Pick Other Channel names the down profile)",
     _query("Pick Other Channel", "b.profile_id <> $1::text", "b.profile_id = $1::text"),
     "Pick Other Channel must pick ANOTHER live, authorised channel of the SAME owner"),
    ("the notice sent from the DOWN profile (Compose Notice)",
     _js("Compose Notice", "profileId: pick.other_profile_id", "profileId: pick.down_profile_id"), "Compose Notice must send from the OTHER profile"),
    ("another owner's channel (Pick Other Channel without the owner)",
     _query("Pick Other Channel", "b.owner_id = $3::text and ", ""),
     "Pick Other Channel must pick ANOTHER live, authorised channel of the SAME owner"),
    ("Down Alerts? bypassed (every Upsert Snapshot item goes on)", _rewire("Upsert Snapshot", 0, "Pick Other Channel"),
     "Down Alerts? must be entered ONLY"),
    ("an undelivered notice recorded as told (Notice Sent? bypassed)", _rewire("Settle Notice", 0, "Record Notified"),
     "Record Notified is reachable without Notice Sent? true"),
    ("the owner's number by the top-level phone only (the A5 rule dropped)",
     _js("Compose Notice", "const phone = error ? '' : ownerPhone(wapi, status);",
         "const phone = error ? '' : String(status.phone || '').replace(/\\D/g, '');"), "Compose Notice must embed build-handoff-workflows.OWNER_PHONE_JS"),
    ("Upsert Snapshot continues on a database fault", _set("Upsert Snapshot", "onError", "continueRegularOutput"),
     "Upsert Snapshot must fail the run on a database fault"),
    ("Owner Phone calls in parallel (no batching)", lambda wf: node(wf, "Owner Phone")["parameters"]["options"].pop("batching"),
     "Owner Phone must run one call at a time"),
    ("List WA Profiles without neverError (an error crashes the run)",
     lambda wf: node(wf, "List WA Profiles")["parameters"]["options"]["response"]["response"].pop("neverError"),
     "List WA Profiles must run with fullResponse + neverError"),
)


def self_check(wf, set_state, migration_sql):
    broken = []
    for label, mutate, expect in MUTATIONS:
        bad = copy.deepcopy(wf)
        try:
            mutate(bad)
        except (AssertionError, KeyError) as e:
            broken.append(f"tripwire self-check: mutation {label!r} does not apply ({e!r})")
            continue
        got = check(bad, set_state, migration_sql)
        if not any(expect in m for m in got):
            broken.append(f"tripwire self-check: mutation {label!r} was not rejected with {expect!r} (got {got})")
    return broken


def run(directory=None):
    d = directory or os.path.join(HERE, "workflows")
    with open(os.path.join(d, B.WF_FILE), encoding="utf-8") as fh:
        wf = json.load(fh)
    with open(os.path.join(d, B.SET_STATE), encoding="utf-8") as fh:
        st = json.load(fh)
    mig = open(MIGRATION, encoding="utf-8").read() if os.path.exists(MIGRATION) else ""
    fails = check(wf, st, mig)
    if not fails:
        fails = self_check(wf, st, mig)
    return fails


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dir")
    a = ap.parse_args()
    fails = run(a.dir)
    for m in fails:
        print(f"CHANNEL WATCH FAIL [{B.WF_FILE}]: {m}")
    if fails:
        print(f"CHANNEL WATCH FAILED: {len(fails)} check(s)")
        sys.exit(1)
    print(f"OK  {B.WF_FILE} (every 10 min; the sweeps' floor / cap / boolean-only rules; a notice only for a recorded DOWN, "
          f"through ANOTHER authorised channel of the same install, the A5 owner-number rule, delivered-only stamp; protected; "
          f"tripwire self-check: {len(MUTATIONS)} mutations rejected)")
    print("ALL CHANNEL WATCH ASSERTS PASSED")


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""Explicit, run-scoped test fixtures for the STAGING database (audit stage 0.3).

Every fixture carries a unique run id in its keys, and `delete` removes ONLY rows carrying that
exact id — never a broad prefix, never another run's rows (instruction: «удаляй только по
уникальной области данного запуска»). Runs against the staging Postgres from staging/pg.sh.

    python3 Tools/n8n/staging/fixtures.py create-subscriber [--status trialing|active|grace|expired] [--plan trial|start|...]
        -> prints the new app_user_id  (probe-<runid>)
    python3 Tools/n8n/staging/fixtures.py register-profile --app-user-id <id> --profile-id <pid> --channel whatsapp|telegram
    python3 Tools/n8n/staging/fixtures.py set-mode --profile-id <pid> [--chat-id '*'] --suppressed true|false
    python3 Tools/n8n/staging/fixtures.py show --app-user-id <id>
    python3 Tools/n8n/staging/fixtures.py delete --app-user-id <id> [--profile-id <pid> ...]
    python3 Tools/n8n/staging/fixtures.py sweep-mock-profiles [--live <pid> ...]

`sweep-mock-profiles` is the one delete that is not keyed by a run id, and it is scoped just as narrowly: the
Wappi MOCK mints profile ids `profile-NNNNNN` from a counter that restarts with the mock, so rows of such ids
that the LIVE mock does not know (--live = the mock's current /__mock/state profiles) are leftovers of an
earlier mock lifetime — and the very ids the next profile/add will hand out again. Only that id shape, only
ids absent from --live, only the per-profile tables `delete` already clears plus bot_profiles.
"""
import argparse
import json
import os
import re
import subprocess
import sys
import uuid

# R2 (2026-09-27): only a RevenueCat anonymous id can be a session's billing identity, so session fixtures use this
# RC-shaped id; the fixed `e2e0e2e0` hex prefix marks it as a fixture identity (the only non-probe id `delete` accepts).
FIXTURE_RC = re.compile(r"^\$RCAnonymousID:e2e0e2e0[0-9a-f]{24}$")

HERE = os.path.dirname(os.path.abspath(__file__))
PG = os.path.join(HERE, "pg.sh")


def psql(sql, *params):
    # Parameters are passed as psql variables and interpolated with :'name' quoting (SQL-safe).
    # The SQL goes through stdin (-f -): psql does NOT interpolate variables inside a -c string.
    args = [PG, "psql", "-tAq"]
    for i, p in enumerate(params):
        args += ["-v", f"p{i}={p}"]
    args += ["-f", "-"]
    out = subprocess.run(args, capture_output=True, text=True, input=sql + "\n")
    if out.returncode != 0:
        sys.exit(f"psql failed: {out.stderr.strip()}")
    return out.stdout.strip()


def main():
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    c = sub.add_parser("create-subscriber"); c.add_argument("--status", default="trialing"); c.add_argument("--plan", default="trial"); c.add_argument("--run-id")
    c.add_argument("--app-user-id", help="an explicit fixture RevenueCat id ($RCAnonymousID:e2e0e2e0<24 hex>) instead of probe-<runid>")
    r = sub.add_parser("register-profile"); r.add_argument("--app-user-id", required=True); r.add_argument("--profile-id", required=True); r.add_argument("--channel", default="whatsapp")
    m = sub.add_parser("set-mode"); m.add_argument("--profile-id", required=True); m.add_argument("--chat-id", default="*"); m.add_argument("--suppressed", required=True)
    s = sub.add_parser("show"); s.add_argument("--app-user-id", required=True)
    d = sub.add_parser("delete"); d.add_argument("--app-user-id", required=True); d.add_argument("--profile-id", action="append", default=[])
    w = sub.add_parser("sweep-mock-profiles"); w.add_argument("--live", action="append", default=[])
    a = ap.parse_args()

    if a.cmd == "create-subscriber":
        run_id = a.run_id or uuid.uuid4().hex[:12]
        uid = f"probe-{run_id}"
        if a.app_user_id:
            if not FIXTURE_RC.match(a.app_user_id):
                sys.exit("refusing: an explicit --app-user-id must be a fixture RevenueCat id ($RCAnonymousID:e2e0e2e0<24 hex>)")
            uid = a.app_user_id
        psql("insert into subscribers (app_user_id, plan, status, trial_started_at) values (:'p0', :'p1', :'p2', now()) on conflict (app_user_id) do update set plan = excluded.plan, status = excluded.status;",
             uid, a.plan, a.status)
        print(uid)
    elif a.cmd == "register-profile":
        psql("insert into bot_profiles (profile_id, app_user_id, channel) values (:'p0', :'p1', :'p2') on conflict (profile_id) do update set app_user_id = excluded.app_user_id, deleted_at = null;",
             a.profile_id, a.app_user_id, a.channel)
        print("ok")
    elif a.cmd == "set-mode":
        sup = "true" if str(a.suppressed).lower() in ("1", "true", "yes") else "false"
        psql("insert into reply_mode_flags (profile_id, chat_id, suppressed, updated_at) values (:'p0', :'p1', :'p2'::boolean, now()) on conflict (profile_id, chat_id) do update set suppressed = excluded.suppressed, updated_at = now();",
             a.profile_id, a.chat_id, sup)
        print("ok")
    elif a.cmd == "show":
        print(json.dumps({
            "subscriber": psql("select row_to_json(s) from subscribers s where app_user_id = :'p0';", a.app_user_id),
            "profiles": psql("select json_agg(row_to_json(b)) from bot_profiles b where app_user_id = :'p0';", a.app_user_id),
            "suggestion_counts": psql("select json_agg(row_to_json(c)) from suggestion_counts c where app_user_id = :'p0';", a.app_user_id),
            "dialog_counts": psql("select json_agg(row_to_json(c)) from dialog_counts c where app_user_id = :'p0';", a.app_user_id),
        }, ensure_ascii=False, indent=1))
    elif a.cmd == "sweep-mock-profiles":
        live = json.dumps(sorted(set(a.live)))
        stale = "(select profile_id from bot_profiles where profile_id ~ '^profile-[0-9]{6}$' and not (profile_id = any (select jsonb_array_elements_text(:'p0'::jsonb))))"
        pids = [x for x in psql(f"select string_agg(profile_id, ',') from {stale} s;", live).split(",") if x]
        for tbl, col in (("reply_mode_flags", "profile_id"), ("conversation_outcomes", "profile_id"), ("reply_events", "profile_id"),
                         ("chat_reply_state", "profile_id"), ("chat_history_events", "profile_id"), ("handoff_requests", "profile_id")):
            psql(f"delete from {tbl} where {col} ~ '^profile-[0-9]{{6}}$' and not ({col} = any (select jsonb_array_elements_text(:'p0'::jsonb)));", live)
        psql("delete from n8n_chat_histories where session_id ~ '^profile-[0-9]{6}:' and not (split_part(session_id, ':', 1) = any (select jsonb_array_elements_text(:'p0'::jsonb)));", live)
        psql("delete from bot_profiles where profile_id ~ '^profile-[0-9]{6}$' and not (profile_id = any (select jsonb_array_elements_text(:'p0'::jsonb)));", live)
        # A4 (2026-10-14): a re-minted mock id must not inherit an earlier lifetime's channel state (a stale «true» would read
        # as a DOWN of a brand-new profile) — only once the migration is applied
        if psql("select to_regclass('public.channel_snapshot') is not null;") == "t":
            psql("delete from channel_snapshot where profile_id ~ '^profile-[0-9]{6}$' and not (profile_id = any (select jsonb_array_elements_text(:'p0'::jsonb)));", live)
        # B1 (2026-10-14): nor an earlier lifetime's schedule (it would keep a brand-new profile silent outside its window)
        if psql("select to_regclass('public.bot_schedule') is not null;") == "t":
            psql("delete from bot_schedule where profile_id ~ '^profile-[0-9]{6}$' and not (profile_id = any (select jsonb_array_elements_text(:'p0'::jsonb)));", live)
        print(f"swept {len(pids)} stale mock profile(s) (live mock knows {len(a.live)})")
    elif a.cmd == "delete":
        if not (a.app_user_id.startswith("probe-") or FIXTURE_RC.match(a.app_user_id)):
            sys.exit("refusing: delete is scoped to fixture identities (probe-<runid> / $RCAnonymousID:e2e0e2e0…) only")
        for pid in a.profile_id:
            psql("delete from reply_mode_flags where profile_id = :'p0';", pid)
            psql("delete from n8n_chat_histories where session_id like :'p0' || ':%';", pid)
            psql("delete from conversation_outcomes where profile_id = :'p0';", pid)
            psql("delete from reply_events where profile_id = :'p0';", pid)
            psql("delete from chat_reply_state where profile_id = :'p0';", pid)
            psql("delete from chat_history_events where profile_id = :'p0';", pid)
            psql("delete from handoff_requests where profile_id = :'p0';", pid)
        n = psql("with a as (delete from suggestion_counts where app_user_id = :'p0' returning 1), b as (delete from dialog_counts where app_user_id = :'p0' returning 1), c as (delete from bot_profiles where app_user_id = :'p0' returning 1), d as (delete from subscribers where app_user_id = :'p0' returning 1), e as (delete from billing_bindings where app_user_id = :'p0' returning 1) select (select count(*) from a) + (select count(*) from b) + (select count(*) from c) + (select count(*) from d) + (select count(*) from e);",
                 a.app_user_id)
        # V3 journal (T1, 2026-10-11): the refusals a fixture identity collected (only once the migration is applied)
        if psql("select to_regclass('public.billing_conflicts') is not null;") == "t":
            n = int(n or 0) + int(psql("with a as (delete from billing_conflicts where app_user_id = :'p0' returning 1) select count(*) from a;", a.app_user_id) or 0)
        # the bot's line (T3 / T5, 2026-10-12): clones a fixture's Create replaced or its Delete Bot Files booked (only once M2 is applied)
        if psql("select to_regclass('public.clone_retirements') is not null;") == "t":
            for pid in a.profile_id:
                n = int(n or 0) + int(psql("with a as (delete from clone_retirements where profile_id = :'p0' returning 1) select count(*) from a;", pid) or 0)
        # A4 (2026-10-14): the channel watch's state of a fixture profile (only once the channel-snapshot migration is applied)
        if psql("select to_regclass('public.channel_snapshot') is not null;") == "t":
            for pid in a.profile_id:
                n = int(n or 0) + int(psql("with a as (delete from channel_snapshot where profile_id = :'p0' returning 1) select count(*) from a;", pid) or 0)
        # learning A (2026-10-13): the panel's shown sets of a fixture profile (only once the learning migration is applied)
        if psql("select to_regclass('public.suggestion_sets') is not null;") == "t":
            for pid in a.profile_id:
                n = int(n or 0) + int(psql("with a as (delete from suggestion_sets where profile_id = :'p0' returning 1) select count(*) from a;", pid) or 0)
        # B1 (2026-10-14): a fixture profile's schedule (only once the bot-schedule migration is applied) — a schedule left on
        # the shared staging bots would keep every later scenario silent outside its window
        if psql("select to_regclass('public.bot_schedule') is not null;") == "t":
            for pid in a.profile_id:
                n = int(n or 0) + int(psql("with a as (delete from bot_schedule where profile_id = :'p0' returning 1) select count(*) from a;", pid) or 0)
        print(f"deleted rows: {n}")


if __name__ == "__main__":
    main()

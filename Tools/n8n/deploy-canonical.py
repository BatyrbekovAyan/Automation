#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Stage 7.7 — push the canonical exports of EXISTING workflows to an n8n instance, one PUT each, with
the same discipline as update-clones.py: dry-run by default, explicit --apply --yes, the pre-update
versionId printed as the rollback pointer, and a READ-BACK after every PUT (graph hash == canonical; for an active
workflow activeVersionId == versionId and the published graph == canonical). An ACTIVE workflow is never switched off
(D1b, 2026-10-11): after its PUT the tool reads back whether the PUT already published the saved version (n8n 2.36
does — update-clones.serves()'s rule) and only when it did not publishes THAT version in place,
POST /workflows/{id}/activate {"versionId": <stored>} — no deactivate → activate window, and a refused publish leaves
the old version serving instead of an inactive workflow.
Scope: workflows that already exist on the instance (DIFF in verify-deployed.py). New workflows are
imported with `n8n import:workflow` (ids must be preserved) — not here. The two placeholder workflows
(Set Workflow State `__HDR_CRED__`, Support Message `__TG_CRED__`/`__CHAT_ID__`) are deployed by
build-client-webhooks.py --update and are SKIPPED here unless --include-placeholders. Any OTHER workflow carrying the
`n8n Admin API` placeholder (Test Dialog, the Profile Lifecycle Sweep since V12) is PUT with it bound to the id the live
Set Workflow State uses, and refused when that id cannot be read (V12, 2026-10-07). A workflow whose Postgres SQL
fails verify-sql-comments.py (V1: a parameter that could leave its literal) is refused before the first request.

«Up to date» is judged on what n8n SERVES as well as on what it stores (review D1, 2026-10-11). An ACTIVE workflow whose
stored graph is canonical while its published version (`activeVersion`, read exactly as verify-deployed.py reads it:
update-clones.published_matches + verify-deployed.graph_hash) is another one is not up to date: n8n 2.36's PUT saves the
new version FIRST and publishes it second, so a refused publish (400/409, the save already persisted) leaves exactly
that, and the next run used to call it «up to date». Such a workflow is reported «would publish» (dry-run, with the
published version as the rollback pointer) and, with --apply, its STORED version is published in place —
POST /workflows/{id}/activate {"versionId": <stored>}, the call the PUT itself makes: n8n validates the version before
it swaps the served one, so a refused publish leaves the old version serving (deactivate → activate would take the
webhook offline exactly then) — and confirmed by a read-back (served = stored = canonical); the summary counts it as
`republished`, never as `up_to_date`. A stored version carrying an unbound `__PLACEHOLDER__` is never published. Inactive
workflows are never published or activated: a bot template stays as it is, and a workflow that must run (everything but
the templates, verify-deployed.py) but is inactive is only reported (`inactive_must_run`) — switching it on is an
operator's decision.
STAGING (T12, 2026-10-12): a local base (localhost / 127.0.0.1) holds the DERIVED copies bootstrap.py imports, so the
tool compares with — and PUTs — the same derivation (staging_canon: derive-staging.py's steps, credentials re-bound by name
to the staging instance's own, refused when «Postgres Staging» or any named credential is missing or a production host
survives). That is what lets the deploy package be rehearsed on staging with the very tool that will run on prod; the raw
canon would read as «would update» everywhere there and --apply would have pointed the stand at prod. A production base
never derives and never reads staging state.
    python3 Tools/n8n/deploy-canonical.py                       # dry-run on prod: what differs, what would be PUT
    python3 Tools/n8n/deploy-canonical.py --base http://localhost:5678   # the same against staging (rehearsal)
    python3 Tools/n8n/deploy-canonical.py --only <id> --apply --yes
    python3 Tools/n8n/deploy-canonical.py --apply --yes         # every DIFF canonical workflow, in file order
"""
import argparse
import copy
import glob
import importlib.util
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("uc", os.path.join(HERE, "update-clones.py"))
uc = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(uc)
_vspec = importlib.util.spec_from_file_location("vd", os.path.join(HERE, "verify-deployed.py"))
vd = importlib.util.module_from_spec(_vspec)
_vspec.loader.exec_module(vd)
_gspec = importlib.util.spec_from_file_location("sql_guard", os.path.join(HERE, "verify-sql-comments.py"))
sql_guard = importlib.util.module_from_spec(_gspec)
_gspec.loader.exec_module(sql_guard)

PLACEHOLDER_IDS = {"0DP79STI3pafXO9B", "olmqWMfcFpCEcpiE"}   # build-client-webhooks.py --update owns these
ADMIN_PLACEHOLDER = "__HDR_CRED__"
SET_STATE_ID = "0DP79STI3pafXO9B"


def admin_credential_id(base):
    """V12 (iteration 2026-09-28): the instance's `n8n Admin API` credential id, read from the LIVE Set Workflow State
    (build-client-webhooks.py created and bound it there). Test Dialog and — since V12 — the Profile Lifecycle Sweep
    carry the committed placeholder; PUT unbound, their admin calls would reference a credential that does not exist.
    None when it cannot be read: the caller refuses the workflow instead of deploying it unbound."""
    st, live = uc.call(base, "GET", f"/workflows/{SET_STATE_ID}")
    if st != 200 or not isinstance(live, dict):
        return None
    for n in live.get("nodes") or []:
        ref = (n.get("credentials") or {}).get("httpHeaderAuth") or {}
        if ref.get("name") == "n8n Admin API" and ref.get("id") and ref["id"] != ADMIN_PLACEHOLDER:
            return ref["id"]
    return None


def bind_admin_credential(body, cred_id):
    """A copy of the PUT body with every `n8n Admin API` placeholder bound to the live credential id."""
    out = copy.deepcopy(body)
    for n in out.get("nodes") or []:
        ref = (n.get("credentials") or {}).get("httpHeaderAuth")
        if isinstance(ref, dict) and ref.get("id") == ADMIN_PLACEHOLDER:
            ref["id"] = cred_id
    return out


def canonical_files():
    out = {}
    for path in sorted(glob.glob(os.path.join(HERE, "workflows", "*.json"))):
        wf = json.load(open(path, encoding="utf-8"))
        wid = wf.get("id") or os.path.basename(path).split("-", 1)[0]
        out[wid] = (path, wf)
    return out


def is_staging(base):
    """The rule verify-deployed.py and update-clones.py use: a local n8n is the isolated staging stand."""
    return "localhost" in base or "127.0.0.1" in base


def staging_canon(files):
    """T12 (2026-10-12): the canon as STAGING holds it — derive-staging.py's own steps in its order (n8n self-calls →
    localhost, Wappi → the mock, Supabase writers neutralised, the price search on the staging stub, credentials
    re-bound BY NAME to the staging instance's own, «Postgres Staging» for every Postgres node). Refuses — before any
    request — when «Postgres Staging» or a credential the canon names is missing, when a production host survives or
    when a Supabase writer is left: the PUT body to staging is exactly what bootstrap.py would import."""
    d = uc._derive_module()
    creds = d.staging_credentials()
    if d.PG_STAGING_NAME not in creds:
        sys.exit(f"REFUSED: staging has no credential named {d.PG_STAGING_NAME!r} — a derived set would bind the shared "
                 f"production Postgres (derive-staging.py)")
    out, missing = {}, set()
    for wid, (path, wf) in files.items():
        x = json.loads(d.derive_file(json.dumps(wf, ensure_ascii=False)))
        d.isolate_supabase_writers(x)
        d.isolate_supabase_readers(x)
        for n in x.get("nodes") or []:
            d.remap(n, creds, missing)
        text = json.dumps(x, ensure_ascii=False)
        for forbidden in d.FORBIDDEN_HOSTS:          # derive-staging.py's own list (n8n, Wappi, RevenueCat — A3 parity)
            if forbidden in text:
                sys.exit(f"REFUSED: {os.path.basename(path)} still references {forbidden} after the staging derivation")
        if d.supabase_writers_left(x):
            sys.exit(f"REFUSED: {os.path.basename(path)} would still WRITE to the real Supabase: {d.supabase_writers_left(x)}")
        out[wid] = (path, x)
    missing -= d.ALLOWED_MISSING
    if missing:
        sys.exit(f"REFUSED: credentials the canon names are missing on staging: {sorted(missing)} (bootstrap.py creates them)")
    return out


def sql_guard_findings(files, only):
    """V1 (2026-09-28): Postgres-node SQL that would let a parameter leave its literal (verify-sql-comments.py) is never
    deployed — checked for every workflow this run may PUT, BEFORE the first request to the instance."""
    out = []
    for wid, (path, wf) in files.items():
        if only and wid not in only:
            continue
        found, _ = sql_guard.check_workflow(wf)
        out += [(os.path.basename(path), *f) for f in found]
    return out


def published_state(wf, want):
    """D1: does n8n SERVE the graph hashing to `want`? Read exactly as verify-deployed.py reads it — the public API's
    `activeVersion` through update-clones.published_matches (the version ids when no graph is reported), judged by
    verify-deployed.graph_hash. True / False / None (inactive, or nothing reported: not judged)."""
    return uc.published_matches(wf, lambda g: vd.graph_hash(g) == want)


def served_summary(st, got, want):
    """One line on what the instance serves now (for the operator, after a publish attempt)."""
    if st != 200 or not isinstance(got, dict):
        return f"unknown (read-back GET {st})"
    if not got.get("active"):
        return "nothing — the workflow is INACTIVE"
    pub = published_state(got, want)
    graph = {True: "canonical", False: "NOT canonical", None: "not reported"}[pub]
    return f"activeVersionId {got.get('activeVersionId')} (stored versionId {got.get('versionId')}), published graph {graph}"


def publish_problems(st, got, want, version_id):
    """D1 read-back after publishing `version_id`: what keeps it from being confirmed (empty = confirmed)."""
    if st != 200 or not isinstance(got, dict):
        return [f"read-back GET {st}"]
    problems = []
    if vd.graph_hash(got) != want:
        problems.append("stored graph is no longer canonical")
    if not got.get("active"):
        problems.append("the workflow is now INACTIVE — nothing is served (n8n rolled the publish back)")
        return problems
    av, vid = got.get("activeVersionId"), got.get("versionId")
    if av != version_id or (vid and av != vid):
        problems.append(f"published version is {av}, not the stored {version_id} (activeVersionId != versionId)")
    pub = published_state(got, want)
    if pub is False:
        problems.append("PUBLISHED graph differs from canonical")
    elif pub is None:
        problems.append("the instance reports no published version — the publish cannot be confirmed")
    return problems


def republish(base, wid, cur, want, apply, summary):
    """D1: the stored version is canonical, the published one is not. Dry-run: «would publish». --apply: publish the
    STORED version in place and confirm it by reading the instance back."""
    vid, av = cur.get("versionId"), cur.get("activeVersionId")
    pub = uc.published_graph(cur)
    served = f"published graph {vd.graph_hash(pub)}" if pub is not None else "published graph not reported"
    print(f"^ {wid} '{cur.get('name')}' stored graph is canonical, but n8n SERVES another version: "
          f"activeVersionId {av} ({served}) != stored versionId {vid}")
    print(f"    rollback pointer: activeVersionId {av} (POST /workflows/{wid}/activate {{\"versionId\": \"{av}\"}} serves it again)")
    unbound = vd.unbound_placeholders(cur)
    if unbound:
        summary["failed"] += 1
        tokens = sorted(set(vd.PLACEHOLDER.findall(json.dumps(cur.get("nodes") or []))))
        print(f"    REFUSED: the stored version carries unbound placeholders {', '.join(tokens)} ({', '.join(unbound)}) — "
              f"not published; bind them through the workflow's deploy tool first")
        return
    if not apply:
        summary["would_publish"] += 1
        print(f"    would publish the stored versionId {vid} in place (no PUT, no deactivate)")
        return
    print(f"    publishing the stored versionId {vid} in place (POST /workflows/{wid}/activate — n8n validates it before it "
          f"swaps the served version; no deactivate window)")
    st, resp = uc.call(base, "POST", f"/workflows/{wid}/activate", {"versionId": vid})
    if st != 200:
        print(f"    publish failed {st}: {str(resp)[:300]}")
    st2, got = uc.call(base, "GET", f"/workflows/{wid}")
    problems = publish_problems(st2, got, want, vid)
    if problems:
        summary["failed"] += 1
        print("    read-back: FAILED — " + "; ".join(problems) + f" — now serving: {served_summary(st2, got, want)}")
        return
    summary["republished"] += 1
    note = f" (the publish call answered {st}, the read-back decides)" if st != 200 else ""
    print(f"    read-back: ok — serving the stored versionId {vid}, published graph canonical{note}")


def put_published(got, want):
    """D1b: did the PUT of an ACTIVE workflow already publish what it saved? As update-clones.serves(): active, the
    published version IS the stored one, and — when the instance reports it — the served graph is the canon."""
    av, vid = got.get("activeVersionId"), got.get("versionId")
    return bool(got.get("active") and av and vid and av == vid and published_state(got, want) is not False)


def publish_after_put(base, wid, want, summary):
    """D1b: after the PUT of an ACTIVE workflow read back whether the PUT published the saved version (n8n 2.36 does)
    and only when it did not publish THAT version in place — POST /workflows/{id}/activate {"versionId": <stored>},
    never deactivate → activate: that left a window without the webhook, and a failed activate left the workflow OFF.
    n8n validates the version before it swaps the served one, so a refused publish keeps the old version serving.
    The caller's read-back decides; False = the run already failed here (nothing to read back against)."""
    st, now = uc.call(base, "GET", f"/workflows/{wid}")
    if st != 200 or not isinstance(now, dict):
        summary["failed"] += 1
        print(f"    read-back GET {st} after the PUT — cannot tell whether the PUT published the update; nothing else done")
        return False
    if put_published(now, want):
        print("    published by the PUT (served version = the update) — no publish call, no deactivate window")
        return True
    vid = now.get("versionId")
    if not vid:
        summary["failed"] += 1
        print("    the PUT did not publish the update and the instance reports no stored versionId — not published "
              f"(now serving: {served_summary(st, now, want)})")
        return False
    print(f"    the PUT did not publish the update — publishing the stored versionId {vid} in place "
          f"(POST /workflows/{wid}/activate, no deactivate window)")
    st2, resp = uc.call(base, "POST", f"/workflows/{wid}/activate", {"versionId": vid})
    if st2 != 200:
        print(f"    publish failed {st2}: {str(resp)[:300]}")
    return True


def body_from(wf):
    """The PUT body: name, nodes, connections, settings — never id/active/meta/staticData/tags (read-only on PUT)."""
    return {"name": wf["name"], "nodes": copy.deepcopy(wf["nodes"]),
            "connections": copy.deepcopy(wf.get("connections") or {}), "settings": copy.deepcopy(wf.get("settings") or {})}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", default=os.environ.get("N8N_BASE_URL", uc.DEFAULT_BASE))
    ap.add_argument("--only", action="append", default=[])
    ap.add_argument("--apply", action="store_true")
    ap.add_argument("--yes", action="store_true")
    ap.add_argument("--include-placeholders", action="store_true")
    a = ap.parse_args()
    if a.apply and not a.yes:
        sys.exit("--apply needs --yes")
    base = a.base.rstrip("/")
    files = canonical_files()
    refused = sql_guard_findings(files, a.only)
    if refused:
        for f, node, rule, line, token, excerpt in refused:
            print(f"SQL GUARD: {f} :: {node} :: line {line} :: {rule} {token}: {excerpt}")
        sys.exit("refusing to deploy: a Postgres node fails verify-sql-comments.py (V1) — fix its generator / the canon first")
    if is_staging(base):
        files = staging_canon(files)   # what staging holds and must be given: the derived set (T12)
    summary = {"considered": 0, "missing": 0, "up_to_date": 0, "would_update": 0, "updated": 0,
               "would_publish": 0, "republished": 0, "inactive_must_run": 0, "failed": 0}
    for wid, (path, canon) in files.items():
        if a.only and wid not in a.only:
            continue
        if wid in PLACEHOLDER_IDS and not a.include_placeholders:
            continue
        if wid.startswith("stg"):
            continue
        summary["considered"] += 1
        st, cur = uc.call(base, "GET", f"/workflows/{wid}")
        if st != 200:
            summary["missing"] += 1
            print(f"MISSING {wid} {os.path.basename(path)} — import it with n8n import:workflow, not here")
            continue
        want = vd.graph_hash(canon)
        if vd.graph_hash(cur) == want:
            if not cur.get("active"):
                if wid in vd.EXPECTED_INACTIVE:   # a bot template: stored is all there is
                    summary["up_to_date"] += 1
                    print(f"= {wid} '{cur.get('name')}' up to date (stored; inactive by contract — nothing published)")
                else:
                    summary["inactive_must_run"] += 1
                    print(f"! {wid} '{cur.get('name')}' stored graph is canonical, but the workflow is INACTIVE although it must run "
                          f"(verify-deployed.py) — not activated here: find out why it is off, then activate it by hand")
                continue
            published = published_state(cur, want)
            if published is False:
                republish(base, wid, cur, want, a.apply, summary)
                continue
            summary["up_to_date"] += 1
            print(f"= {wid} '{cur.get('name')}' up to date ("
                  + ("stored and published" if published else "stored; this n8n reports no published version — not judged") + ")")
            continue
        was_active = bool(cur.get("active"))
        cur_names = {n["name"] for n in cur["nodes"]}
        new_names = {n["name"] for n in canon["nodes"]}
        print(f"~ {wid} '{cur.get('name')}' active={was_active} nodes {len(cur['nodes'])} → {len(canon['nodes'])} "
              f"(+{len(new_names - cur_names)} -{len(cur_names - new_names)})")
        print(f"    rollback pointer: versionId {cur.get('versionId')} (activeVersionId {cur.get('activeVersionId')})")
        if not a.apply:
            summary["would_update"] += 1
            continue
        body = body_from(canon)
        if ADMIN_PLACEHOLDER in json.dumps(body):
            cred = admin_credential_id(base)
            if not cred:
                summary["failed"] += 1
                print(f"    REFUSED: {ADMIN_PLACEHOLDER} cannot be bound (no live `n8n Admin API` on Set Workflow State) — not PUT unbound")
                continue
            body = bind_admin_credential(body, cred)
        st, resp = uc.call(base, "PUT", f"/workflows/{wid}", body)
        if st != 200:
            summary["failed"] += 1
            print(f"    PUT failed {st}: {str(resp)[:300]}")
            continue
        if was_active and not publish_after_put(base, wid, want, summary):
            continue
        st4, got = uc.call(base, "GET", f"/workflows/{wid}")
        problems = []
        if st4 != 200:
            problems.append(f"read-back GET {st4}")
        else:
            if vd.graph_hash(got) != vd.graph_hash(canon):
                problems.append("graph differs from canonical after PUT")
            if bool(got.get("active")) != was_active:
                problems.append(f"active changed: {was_active} → {got.get('active')}")
            if was_active and got.get("activeVersionId") and got.get("activeVersionId") != got.get("versionId"):
                problems.append("published version is not the updated one (activeVersionId != versionId)")
            if was_active and published_state(got, want) is False:   # D1: what n8n serves, not only the ids
                problems.append("PUBLISHED graph differs from canonical after PUT")
        if problems:
            summary["failed"] += 1
            print("    read-back: FAILED — " + "; ".join(problems)
                  + (f" — now serving: {served_summary(st4, got, want)}" if was_active else ""))
        else:
            summary["updated"] += 1
            print("    read-back: ok")
    print(json.dumps(summary))
    sys.exit(1 if summary["failed"] else 0)


if __name__ == "__main__":
    main()

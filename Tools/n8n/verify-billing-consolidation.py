#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Structural verifier for the billing consolidation SQL in RevenueCat Events (ZGYr6srzS3rSSXHp) — A2 «защита платящих»
(2026-10-14). No network. Behaviour lives in tests/test_billing_consolidation.py (the graph and the statements on the staging
Postgres); this file pins what that behaviour rests on, so a hand edit, a stale re-export or a careless module edit fails:

  (1) `Consolidate Aliases` / `Transfer Subscriber` hold EXACTLY billing_consolidation.py's texts (apply-billing-
      consolidation.py owns them; edit the module, regenerate) — the generator's own --check agrees;
  (2) both are live Postgres executeQuery nodes on Upsert Subscriber's credential, onError continueErrorOutput, entered only
      from their If (Consolidate ← If Needs Consolidation?[TRUE], Transfer ← If Is Transfer?[TRUE]) and leading to
      [If Has Payload?] + [Respond Error Consolidate] / [Respond 200] + [Respond Error Transfer];
  (3) their queryReplacement is the hand-authored array the generated text's parameter order is written for
      (billing_consolidation.RC_*_REPLACEMENT) — the SQL and its parameters cannot drift apart;
  (4) the shape the alias merge rests on, read off the DEPLOYED text: `locked` takes the alias rows `for update` and is the
      only source of the sum and of `plan_source`; `plan_source` is status = 'active' of a paid tier only, only when the
      event's plan (the third parameter) is null, latest period first; every carried column of `credited` is gated on the
      same take predicate (the recipient is trialing without a period AND there is a source) and its update runs only when
      there is money or a plan to hand over (zero row versions for a settled alias set); `retired` keeps its guard and clamp;
      the re-key CTE compares owner_id with SQL NULL in RevenueCat Events (no parameter can re-key a bot there) and
      touches live rows only; the final select hands the event id on (A1);
  (5) Transfer Subscriber keeps the Task 16/17 guarantees (its sources `for update`, the usage carry gated on `accepted`).

    python3 Tools/n8n/verify-billing-consolidation.py              # the canonical workflows
    python3 Tools/n8n/verify-billing-consolidation.py --dir DIR    # another set of exports (mutation tests)
"""
import argparse
import importlib.util
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
RC_EVENTS = "ZGYr6srzS3rSSXHp-RevenueCat_Events.json"
POSTGRES = "n8n-nodes-base.postgres"


def _module(name, filename):
    spec = importlib.util.spec_from_file_location(name, os.path.join(HERE, filename))
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


BC = _module("billing_consolidation", "billing_consolidation.py")
GEN = _module("apply_billing_consolidation", "apply-billing-consolidation.py")


def code(sql):
    """The SQL with `--` comments removed (the RU comment blocks name the very clauses asserted below)."""
    return "\n".join(line.split("--", 1)[0] for line in sql.split("\n"))


def cte(sql, name):
    """The body of CTE `name` in `sql` (comments removed) — parentheses balanced; None when absent."""
    c = code(sql)
    m = re.search(r"\b%s\s+as\s*\(" % re.escape(name), c, re.I)
    if not m:
        return None
    depth, start = 0, m.end() - 1
    for i in range(start, len(c)):
        depth += {"(": 1, ")": -1}.get(c[i], 0)
        if depth == 0:
            return c[start + 1:i]
    return None


def flat(s):
    return re.sub(r"\s+", " ", s or "").strip()


def targets(wf, src):
    return [[e["node"] for e in (out or [])] for out in (wf["connections"].get(src) or {}).get("main", [])]


def sources(wf, dst):
    return sorted((s, i) for s, c in wf["connections"].items() for i, out in enumerate((c or {}).get("main") or [])
                  for e in (out or []) if e.get("node") == dst)


def check(wf):
    """[message] of every failed assert — every assert runs (one red never hides the next)."""
    fails = []

    def need(cond, msg):
        if not cond:
            fails.append(msg)
        return cond

    nodes = {}
    for name in ("Consolidate Aliases", "Transfer Subscriber", "Upsert Subscriber"):
        hits = [n for n in wf["nodes"] if n.get("name") == name]
        if need(len(hits) == 1, f"expected exactly one node {name!r}, found {len(hits)}"):
            nodes[name] = hits[0]
    if len(nodes) < 3:
        return fails
    cons, tran, ups = nodes["Consolidate Aliases"], nodes["Transfer Subscriber"], nodes["Upsert Subscriber"]
    cred = lambda n: ((n.get("credentials") or {}).get("postgres") or {}).get("id")

    # (1) the generated texts — and the generator agrees
    want = GEN.owned()
    for name, n in (("Consolidate Aliases", cons), ("Transfer Subscriber", tran)):
        need(n.get("parameters", {}).get("query") == want[name],
             f"{name}: query differs from billing_consolidation.py — edit the module and run apply-billing-consolidation.py, "
             f"never the JSON")
    try:
        stale = GEN.stale(wf)
        need(stale == [], f"apply-billing-consolidation.py --check would report stale: {stale}")
    except AssertionError as e:          # the generator refuses a node it may not write (not one Postgres executeQuery node)
        need(False, f"apply-billing-consolidation.py refuses this export: {e}")

    # (2) live nodes, the database, the error path, the wiring
    for name, n, entry, outs in (("Consolidate Aliases", cons, ("If Needs Consolidation?", 0),
                                  [["If Has Payload?"], ["Respond Error Consolidate"]]),
                                 ("Transfer Subscriber", tran, ("If Is Transfer?", 0), [["Respond 200"], ["Respond Error Transfer"]])):
        need(n.get("type") == POSTGRES and n.get("parameters", {}).get("operation", "executeQuery") == "executeQuery",
             f"{name}: not a Postgres executeQuery node")
        need(not n.get("disabled"), f"{name} is disabled — n8n would hand its input on untouched (nothing moved, nothing retired)")
        need(cred(n) and cred(n) == cred(ups), f"{name}: credential {cred(n)!r} != Upsert Subscriber's {cred(ups)!r}")
        need(n.get("onError") == "continueErrorOutput",
             f"{name}: onError {n.get('onError')!r} — a failed merge must answer 500 through its error output (RevenueCat "
             f"retries), never continue into the upsert")
        need(sources(wf, name) == [entry], f"{name}: entered from {sources(wf, name)}, only {entry} may enter it")
        need(targets(wf, name) == outs, f"{name}: leads to {targets(wf, name)}, expected {outs}")

    # (3) the hand-authored parameter arrays the texts are written for
    for name, n, rep in (("Consolidate Aliases", cons, BC.RC_CONSOLIDATE_REPLACEMENT),
                         ("Transfer Subscriber", tran, BC.RC_TRANSFER_REPLACEMENT)):
        got = (n.get("parameters", {}).get("options") or {}).get("queryReplacement")
        need(got == rep, f"{name}: queryReplacement {got!r} is not the array its SQL's parameter order is written for: {rep!r}")

    # (4) the alias merge, on the deployed text
    q = cons.get("parameters", {}).get("query", "")
    locked, source, credited = cte(q, "locked"), cte(q, "plan_source"), cte(q, "credited")
    retired, rekeyed = cte(q, "retired"), cte(q, "rekeyed")
    if need(locked is not None, "Consolidate Aliases: the CTE `locked` is gone"):
        need(re.search(r"\bfor\s+update\s*$", locked.strip(), re.I),
             "Consolidate Aliases: `locked` must take the alias rows FOR UPDATE — a snapshot sum credits twice when two "
             "deliveries race (this instance delivers one subscriber's events in parallel)")
    if need(source is not None, "Consolidate Aliases: the CTE `plan_source` is gone — a top-up first after a reinstall "
                                "leaves the paying owner on trial"):
        fs = flat(source)
        need(re.search(r"\bfrom locked\b", fs) and not re.search(r"\bfrom subscribers\b", fs),
             "Consolidate Aliases: `plan_source` must read the LOCKED alias rows (the version under the lock), not a snapshot")
        need(re.search(r"\bstatus = 'active'", fs),
             "Consolidate Aliases: `plan_source` must take only status = 'active' — an expired/grace alias is no subscription")
        need(re.search(r"\bplan in \('start', 'business', 'network'\)", fs),
             "Consolidate Aliases: `plan_source` must take only a paid tier (start/business/network) — plan none has a base "
             "quota of 0, handing it to a trial recipient (150) would silence its bots")
        need(re.search(r"\$3::text is null\b", fs),
             "Consolidate Aliases: `plan_source` must apply only when the event carries no plan (the third parameter is "
             "null) — an event with a plan lays its own in Upsert Subscriber")
        need(re.search(r"order by current_period_end desc nulls last, updated_at desc nulls last limit 1\b", fs),
             "Consolidate Aliases: `plan_source` must pick ONE row, the latest period first (Transfer's src order)")
    take = "subscribers.status = 'trialing' and subscribers.current_period_end is null and exists (select 1 from plan_source)"
    if need(credited is not None, "Consolidate Aliases: the CTE `credited` is gone"):
        fc = flat(credited)
        need(re.search(r"\bfrom \(select coalesce\(sum\(topup_balance\), 0\) as total from locked\) x left join plan_source s on true\b", fc),
             "Consolidate Aliases: `credited` must sum the LOCKED alias balances and join the plan source (one row)")
        for col in ("plan", "status", "current_period_end", "product_id"):
            need(f"{col} = case when {take} then excluded.{col} else subscribers.{col} end" in fc,
                 f"Consolidate Aliases: `credited` hands `{col}` over outside the take predicate ({take}) — a recipient with "
                 f"its own plan or period could be downgraded")
        need(fc.endswith(f"where excluded.topup_balance > 0 or ({take}) returning app_user_id, plan, topup_balance"),
             "Consolidate Aliases: `credited` must update only when there is money or a plan to hand over (`where "
             "excluded.topup_balance > 0 or (<take>)`) — a settled alias set rides every event and must write no row version")
        need("topup_balance = subscribers.topup_balance + excluded.topup_balance" in fc,
             "Consolidate Aliases: the moved balance must be ADDED to the recipient's live value, never assigned")
    if need(retired is not None, "Consolidate Aliases: the CTE `retired` is gone"):
        fr = flat(retired)
        need("current_period_end = least(coalesce(s.current_period_end, now()), now())" in fr and
             "(s.status <> 'expired' or s.topup_balance <> 0 or s.current_period_end is null or s.current_period_end > now())" in fr,
             "Consolidate Aliases: `retired` lost its clamp or its guard (NULL-clamp = 3 days of grace; the guard = no row "
             "version for an already retired alias)")
    if need(rekeyed is not None, "Consolidate Aliases: the re-key CTE `rekeyed` is gone"):
        fk = flat(rekeyed)
        need(re.search(r"\bb\.owner_id = null::text\b", fk) and not re.search(r"\bowner_id = \$\d", fk),
             "Consolidate Aliases: in RevenueCat Events the re-key must compare owner_id with SQL NULL — the RevenueCat "
             "webhook has no install, and no parameter may ever re-key a bot there")
        need(re.search(r"\bb\.deleted_at is null\b", fk) and re.search(r"\bb\.app_user_id <> \$1\b", fk),
             "Consolidate Aliases: the re-key must touch live rows only, never a row already on the recipient")
    need(re.search(r",\s*\$8::text as event_id;\s*$", q),
         "Consolidate Aliases: the final select must still hand the event id on (`$8::text as event_id`, A1)")

    # (5) the TRANSFER keeps Task 16/17
    t = tran.get("parameters", {}).get("query", "")
    tl, tc = cte(t, "locked"), cte(t, "carried")
    need(tl is not None and re.search(r"\bfor\s+update\s*$", tl.strip(), re.I),
         "Transfer Subscriber: its sources must be taken FOR UPDATE")
    need(tc is not None and re.search(r"\bfrom accepted\b", tc) and not re.search(r"\bto_ids\b", tc),
         "Transfer Subscriber: the usage carry must drive off `accepted` (review N-1), never `to_ids`")
    return fails


def run(directory=None):
    path = os.path.join(directory or os.path.join(HERE, "workflows"), RC_EVENTS)
    with open(path, encoding="utf-8") as fh:
        return check(json.load(fh))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dir")
    a = ap.parse_args()
    fails = run(a.dir)
    for m in fails:
        print(f"BILLING CONSOLIDATION FAIL [{RC_EVENTS}]: {m}")
    if fails:
        print(f"BILLING CONSOLIDATION FAILED: {len(fails)} check(s)")
        sys.exit(1)
    print(f"OK  {RC_EVENTS} (Consolidate Aliases / Transfer Subscriber = billing_consolidation.py; plan carried only from an "
          f"active alias into a trialing recipient without a period; settled = no row version; no re-key from RevenueCat)")
    print("ALL BILLING CONSOLIDATION ASSERTS PASSED")


if __name__ == "__main__":
    main()

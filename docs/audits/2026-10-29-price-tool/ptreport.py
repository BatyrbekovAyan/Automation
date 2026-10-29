#!/usr/bin/env python3
"""Price-list search rates of saved eval runs (docs/audits/2026-10-29-price-tool/README.md) — no network, no model calls.

    python3 docs/audits/2026-10-29-price-tool/ptreport.py now                        # today's window vs the 2026-10-13 one
    python3 docs/audits/2026-10-29-price-tool/ptreport.py fix <label>                # a before/after plan run (<label>-<block>-<side>.json)

«searched» = the run's tool_calls name the price-list search (`supabase_vector_store`, the harness's name for the template's
«Supabase Vector Store» tool); «stated» = the scenario's `mentions:<price>` verdict; «pass» = every verdict of the run.
Fisher = one-sided exact test on the pooled counts (runs are correlated in time — read it as a guide, never as proof).
"""
import glob
import json
import os
import sys
from math import comb

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
RESULTS = os.path.join(REPO, "Tools", "n8n", "eval", "results")
SEARCH = "supabase_vector_store"
BASELINE = ("core-a-before", "core-a-after")      # 2026-10-13, set v1.7, harness h10, the same bot-facing prompts


def load(label):
    """<label>.json — or its offline rescore <label>.h11.json when there is one (rescore.py: the final h11 verdicts)."""
    rescored = os.path.join(RESULTS, label + ".h11.json")
    path = rescored if os.path.exists(rescored) else os.path.join(RESULTS, label + ".json")
    with open(path, encoding="utf-8") as fh:
        return json.load(fh)


def per_scenario(J, ids=None):
    """{id: {"n", "searched", "stated", "stated_n", "pass", "handoff"}} over the auto runs of one result file."""
    out = {}
    for x in J["results"]:
        if x.get("mode") != "auto" or (ids is not None and x["id"] not in ids):
            continue
        s = out.setdefault(x["id"], {"n": 0, "searched": 0, "stated": 0, "stated_n": 0, "pass": 0, "handoff": 0})
        for r in x["runs"]:
            calls = [c.get("name") for c in (r.get("tool_calls") or [])]
            s["n"] += 1
            s["searched"] += SEARCH in calls
            s["handoff"] += "create_handoff" in calls
            s["pass"] += all(v is not False for v in r["verdicts"].values())
            # a price check: mentions:<amount> (≥ 100) — never mentions:15 (an hour) or mentions:<word>
            price = [k for k in r["verdicts"] if k.startswith("mentions:") and k.split(":", 1)[1].isdigit() and int(k.split(":", 1)[1]) >= 100]
            if price:
                s["stated_n"] += 1
                s["stated"] += all(r["verdicts"][k] for k in price)
    return out


def fisher_less(a, n1, b, n2):
    """P(X <= a) for the first group's successes under the hypergeometric null (pooled a+b successes over n1+n2)."""
    k, n = a + b, n1 + n2
    total = comb(n, k)
    return sum(comb(n1, i) * comb(n2, k - i) for i in range(max(0, k - n2), a + 1)) / total


def pct(a, n):
    return f"{a}/{n} ({100 * a / n:.0f} %)" if n else "—"


def report_now(label="pt-now", sides=("harness", "n8n")):
    """b24: today's search rate per scenario in both request shapes against the 2026-10-13 window (harness shape);
    pl: search / stated price / pass / handoffs per request shape."""
    b24 = {side: per_scenario(load(f"{label}-b24-{side}")) for side in sides}
    base = {}
    for bl in BASELINE:
        for sid, s in per_scenario(load(bl), set(b24[sides[0]])).items():
            b = base.setdefault(sid, {"n": 0, "searched": 0})
            b["n"] += s["n"]
            b["searched"] += s["searched"]
    print("## b24 — the 24 scenarios that searched on 2026-10-13\n")
    print("| scenario | 2026-10-13 searched (harness) | " + " | ".join(f"today searched ({s})" for s in sides) + " |")
    print("|---|---|" + "---|" * len(sides))
    tot = {s: [0, 0] for s in sides}
    bb = bn = 0
    for sid in sorted(b24[sides[0]]):
        b = base.get(sid, {"n": 0, "searched": 0})
        cells = []
        for s in sides:
            t = b24[s][sid]
            cells.append(pct(t["searched"], t["n"]))
            tot[s][0] += t["searched"]
            tot[s][1] += t["n"]
        bb, bn = bb + b["searched"], bn + b["n"]
        print(f"| {sid} | {pct(b['searched'], b['n'])} | " + " | ".join(cells) + " |")
    print(f"| **all** | **{pct(bb, bn)}** | " + " | ".join(f"**{pct(*tot[s])}**" for s in sides) + " |")
    for s in sides:
        print(f"\nFisher one-sided (today {s} ≤ 10-13 harness): p = {fisher_less(tot[s][0], tot[s][1], bb, bn):.2g}")
    print("\n## pl — the price lives only in the price list\n")
    pl = {side: per_scenario(load(f"{label}-pl-{side}")) for side in sides}
    print("| scenario | " + " | ".join(f"{s} searched | {s} stated | {s} pass | {s} handoff" for s in sides) + " |")
    print("|---|" + "---|" * (4 * len(sides)))
    for sid in pl[sides[0]]:
        cells = []
        for s in sides:
            x = pl[s][sid]
            cells += [pct(x["searched"], x["n"]), pct(x["stated"], x["stated_n"]), pct(x["pass"], x["n"]), str(x["handoff"])]
        print(f"| {sid} | " + " | ".join(cells) + " |")
    print("| **all** | " + " | ".join(f"**{pct(sum(x['searched'] for x in pl[s].values()), sum(x['n'] for x in pl[s].values()))}** | | |"
                                     for s in sides) + " |")


def report_fix(label):
    files = sorted(glob.glob(os.path.join(RESULTS, f"{label}-*.json")))
    blocks = {}
    for f in files:
        stem = os.path.basename(f)[:-5]
        block, side = stem[len(label) + 1:].rsplit("-", 1)
        blocks.setdefault(block, {})[side] = per_scenario(load(stem))
    for block, sides in blocks.items():
        names = sorted(sides)
        print(f"## {block}\n")
        print("| scenario | " + " | ".join(f"{n} searched | {n} stated | {n} pass | {n} handoff" for n in names) + " |")
        print("|---|" + "---|" * (4 * len(names)))
        ids = sorted(set().union(*[set(s) for s in sides.values()]))
        tot = {n: [0, 0, 0, 0, 0, 0] for n in names}
        for sid in ids:
            cells = []
            for n in names:
                s = sides[n].get(sid)
                if not s:
                    cells += ["—"] * 4
                    continue
                cells += [pct(s["searched"], s["n"]), pct(s["stated"], s["stated_n"]), pct(s["pass"], s["n"]), str(s["handoff"])]
                t = tot[n]
                t[0] += s["searched"]; t[1] += s["n"]; t[2] += s["stated"]; t[3] += s["stated_n"]; t[4] += s["pass"]; t[5] += s["handoff"]
            print(f"| {sid} | " + " | ".join(cells) + " |")
        print("| **all** | " + " | ".join(f"**{pct(t[0], t[1])}** | **{pct(t[2], t[3])}** | **{pct(t[4], t[1])}** | **{t[5]}**"
                                         for t in (tot[n] for n in names)) + " |")
        if len(names) == 2:
            (n1, t1), (n2, t2) = sorted(tot.items())
            print(f"\nsearched: Fisher one-sided ({n1} ≤ {n2}) p = {fisher_less(t1[0], t1[1], t2[0], t2[1]):.2g}; "
                  f"({n2} ≤ {n1}) p = {fisher_less(t2[0], t2[1], t1[0], t1[1]):.2g}")
        print()


if __name__ == "__main__":
    if sys.argv[1:2] == ["now"]:
        report_now()
    elif sys.argv[1:2] == ["fix"] and len(sys.argv) == 3:
        report_fix(sys.argv[2])
    else:
        sys.exit(__doc__)

#!/usr/bin/env python3
"""Screen fix candidates through the staging eval proxy (n8n request shape, canon f0c81168 otherwise) on given scenarios — one
interleaved schedule, the candidates as sides. A candidate is the canon with ONE change: the price-list tool's description
(`tool:<file>`) or a sentence inserted into every vertical prompt after «нет в каталоге — ищи в прайс-листах.» (`core:<file>`).
Records per run: the verdicts of the scenario's checks, the tools called, the text. No canon file is touched.

    python3 screen-candidates.py <out.json> --side before --side T=tool:fix-tool-description.txt \
        --scenario ws_discount_policy_known:10 --scenario ws_list_of_items:6
"""
import argparse
import copy
import importlib.util
import json
import os
import time

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
spec = importlib.util.spec_from_file_location("run_eval", os.path.join(REPO, "Tools", "n8n", "eval", "run-eval.py"))
M = importlib.util.module_from_spec(spec)
spec.loader.exec_module(M)
ANCHOR = "нет в каталоге — ищи в прайс-листах."
CANON = "f0c81168"


def side_ctx(patch):
    ctx = M.side_context(CANON, CANON, None, "n8n")
    if not patch:
        return ctx
    kind, _, path = patch.partition(":")
    text = open(os.path.join(HERE, path) if not os.path.isabs(path) else path, encoding="utf-8").read().strip()
    ctx = copy.deepcopy(ctx)
    if kind == "tool":
        ctx["mp"]["n8n_tools"][0]["function"]["description"] = text
    elif kind == "core":
        for vertical, prompt in ctx["prompts"].items():
            assert prompt.count(ANCHOR) == 1, vertical
            ctx["prompts"][vertical] = prompt.replace(ANCHOR, ANCHOR + " " + text)
    else:
        raise SystemExit(f"unknown patch {patch!r}")
    return ctx


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("out")
    ap.add_argument("--side", action="append", required=True, help="NAME or NAME=tool:<file> / NAME=core:<file>")
    ap.add_argument("--scenario", action="append", required=True, help="ID:REPEATS")
    a = ap.parse_args()
    d = json.load(open(os.path.join(REPO, "Tools", "n8n", "eval", "scenarios.v1.json"), encoding="utf-8"))
    sc_by_id = {s["id"]: s for s in d["scenarios"]}
    sides = {}
    for s in a.side:
        name, _, patch = s.partition("=")
        sides[name] = side_ctx(patch)
    jobs = [(sid, int(n)) for sid, _, n in (x.partition(":") for x in a.scenario)]
    names = list(sides)
    runs, t0 = [], time.time()
    for rep in range(max(n for _, n in jobs)):
        for j, (sid, n) in enumerate(jobs):
            if rep >= n:
                continue
            k = (rep + j) % len(names)
            for name in names[k:] + names[:k]:
                sc, ctx = sc_by_id[sid], sides[name]
                know = M.scenario_knowledge(d, sc)
                out = M.run_auto(sc, know, ctx["mp"], ctx["prompts"], ctx["mp"]["max_tokens"], marks=ctx["marks"])
                verdicts = {c: M.check(c, sc, know, out) for c in sc["expect"]}
                runs.append({"side": name, "scenario": sid, "rep": rep, "t": round(time.time() - t0, 1), "verdicts": verdicts,
                             "tools": [c["name"] for c in out.get("tool_calls") or []], "text": out.get("text") or "",
                             "served": out.get("model_served"), "error": out.get("error")})
    with open(a.out, "w", encoding="utf-8") as fh:
        json.dump({"canon": CANON, "sides": a.side, "scenarios": a.scenario, "runs": runs}, fh, ensure_ascii=False, indent=1)
    for name in names:
        rs = [r for r in runs if r["side"] == name]
        hand = sum("create_handoff" in r["tools"] for r in rs if "no_tool:create_handoff" in r["verdicts"])
        ok = sum(all(v is not False for v in r["verdicts"].values()) for r in rs)
        per = ", ".join(f"{sid} {sum('create_handoff' in r['tools'] for r in rs if r['scenario'] == sid)}/{n}" for sid, n in jobs)
        print(f"{name:7} pass {ok}/{len(rs)}  spurious handoffs {hand}  [{per}]")


if __name__ == "__main__":
    main()

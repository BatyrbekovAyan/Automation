#!/usr/bin/env python3
"""Q1 diagnosis: run selected scenarios through run-eval's OWN run_auto/check (imported from the temp worktree) with a
prompt map composed from the worktree's prompts/*.md after applying text edits (variants). Nothing is written to any repo.

    python3 ablate.py <worktree> <variants.json> <scenarios.json> <repeat> <out.json> [--only id1,id2] [--variant name,...]
variants.json: {"name": [{"file": "_core.md", "old": "...", "new": "..."}, ...], ...}
"""
import importlib.util
import json
import os
import sys

wt, vfile, sfile, rep, out = sys.argv[1:6]
rep = int(rep)
only = None
vonly = None
for i, a in enumerate(sys.argv):
    if a == "--only":
        only = sys.argv[i + 1].split(",")
    if a == "--variant":
        vonly = sys.argv[i + 1].split(",")

spec = importlib.util.spec_from_file_location("runeval", os.path.join(wt, "Tools/n8n/eval/run-eval.py"))
re_ = importlib.util.module_from_spec(spec)
spec.loader.exec_module(re_)

PD = os.path.join(wt, "Tools/n8n/prompts")
VERTICALS = ["auto_parts", "wholesale", "flowers", "kaspi_seller", "education", "phone_repair"]


def compose(edits):
    texts = {}
    for f in ["_core.md", "_universal.md"] + [v + ".md" for v in VERTICALS]:
        texts[f] = open(os.path.join(PD, f), encoding="utf-8").read()
    for e in edits:
        t = texts[e["file"]]
        assert t.count(e["old"]) == 1, f"{e['file']}: {e['old'][:60]!r} occurs {t.count(e['old'])}×"
        texts[e["file"]] = t.replace(e["old"], e["new"])
    core = texts["_core.md"].strip()
    uni = texts["_universal.md"].strip()
    return {v: texts[v + ".md"].strip() + "\n\n" + core + "\n\n" + uni for v in VERTICALS}


variants = json.load(open(vfile, encoding="utf-8"))
d = json.load(open(sfile, encoding="utf-8"))
mp = re_.model_params("worktree")
mp["search_code"] = re_.search_code("worktree", json.loads(re_.file_at("worktree", re_.BOT_WA)))
# sanity: the unedited composition IS the canonical map at the worktree
assert compose([]) == re_.prompts_map("worktree"), "composition drifted from the canonical map"
res = {}
cost_in = cost_out = cached = 0
for vname, edits in variants.items():
    if vonly and vname not in vonly:
        continue
    prompts = compose(edits)
    res[vname] = {}
    for sc in d["scenarios"]:
        if only and sc["id"] not in only:
            continue
        know = d["knowledge"][sc["vertical"]]
        runs = []
        for _ in range(rep):
            o = re_.run_auto(sc, know, mp, prompts, mp["max_tokens"])
            v = {n: re_.check(n, sc, know, o) for n in sc["expect"]}
            runs.append({"pass": all(x is not False for x in v.values()), "verdicts": v, "text": o.get("text"),
                         "tool_calls": o.get("tool_calls"), "error": o.get("error")})
            u = o.get("usage") or {}
            cost_in += u.get("prompt_tokens", 0); cost_out += u.get("completion_tokens", 0); cached += u.get("cached_tokens", 0)
        p = sum(r["pass"] for r in runs)
        res[vname][sc["id"]] = {"pass": p, "n": rep, "runs": runs}
        print(f"{vname:28} {sc['id']:40} {p}/{rep}", flush=True)
usd = ((cost_in - cached) * 0.75 + cached * 0.075 + cost_out * 4.5) / 1e6
print(f"tokens in {cost_in} (cached {cached}) out {cost_out} ≈ ${usd:.4f}")
json.dump({"variants": variants, "results": res, "usd": usd}, open(out, "w", encoding="utf-8"), ensure_ascii=False, indent=1)

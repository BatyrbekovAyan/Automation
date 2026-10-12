#!/usr/bin/env python3
"""Re-score SAVED eval runs with the CURRENT checks — no generation, no network, no staging.

    python3 Tools/n8n/eval/rescore.py Tools/n8n/eval/results/before-v1.1.json Tools/n8n/eval/results/after-v1.1.json

For each input `<label>.json` it writes `<label>.h3.json` + `.md` next to it (never overwrites the source; the
suffix follows the harness in run-eval.py). Auto verdicts are recomputed from the saved answer text and tool calls
with run-eval.py's `check()`. Panel verdicts are recomputed only when the saved run carries its HTTP status and the
saved body still parses (runs before 2026-09-25 saved neither the status nor always a whole body); otherwise they
are carried over and counted as such — h3 did not change a panel check. Harness versions never mix: a number is
comparable only with a number scored by the same harness.
"""
import argparse
import importlib.util
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
_spec = importlib.util.spec_from_file_location("run_eval", os.path.join(HERE, "run-eval.py"))
M = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(M)
TRUNCATED_AT = 600  # run-eval.py kept only the first 600 chars of an auto answer until h3


def classify(rate):
    return "pass" if rate == 1 else ("flaky" if rate > 0 else "fail")


def counts(results):
    c = {"pass": 0, "flaky": 0, "fail": 0}
    for x in results:
        c[classify(x["pass_rate"])] += 1
    return c


def by_check(results):
    agg = {}
    for x in results:
        for r in x["runs"]:
            for k, v in r["verdicts"].items():
                if v is None:
                    continue
                a = agg.setdefault(k, [0, 0])
                a[1] += 1
                a[0] += 1 if v else 0
    return {k: f"{v[0]}/{v[1]}" for k, v in sorted(agg.items())}


def rescore_results(J, scenarios, src="<results>"):
    """Pure: the saved runs of `J` re-scored with the current checks against `scenarios` (the set they were scored with).
    Returns (results, changes, counters) and writes nothing — test_checks.py pins «a new harness moves no saved verdict» on it."""
    old_summary = J["summary"]
    if old_summary.get("scenario_version") != scenarios["version"]:
        sys.exit(f"{src}: scored against scenarios {old_summary.get('scenario_version')!r}, the file on disk is {scenarios['version']!r}")
    sc_by_id = {s["id"]: s for s in scenarios["scenarios"]}
    results, changes = [], []
    carried = recomputed_panel = truncated = 0
    for x in J["results"]:
        sc = sc_by_id[x["id"]]
        know = scenarios["knowledge"][sc["vertical"]]
        runs = []
        for i, run in enumerate(x["runs"]):
            old = run["verdicts"]
            if set(old) != set(sc["expect"]):
                sys.exit(f"{src}: {x['id']} run {i + 1} was scored with {sorted(old)}, the scenario now expects {sorted(sc['expect'])}")
            if x["mode"] == "auto":
                text = run.get("text") or ""
                truncated += len(text) >= TRUNCATED_AT
                out = {"text": text, "tool_calls": run.get("tool_calls") or []}
                new = {name: M.check(name, sc, know, out) for name in sc["expect"]}
            else:
                body = None
                try:
                    body = json.loads(run.get("text") or "")
                except ValueError:
                    pass
                if run.get("status") is not None and isinstance(body, dict):
                    new = {name: M.check_panel(name, sc, know, {"status": run["status"], "body": body}) for name in sc["expect"]}
                    recomputed_panel += 1
                else:
                    new = dict(old)
                    carried += 1
            for name in sc["expect"]:
                if new[name] != old[name]:
                    changes.append({"id": x["id"], "run": i + 1, "check": name, "old": old[name], "new": new[name],
                                    "tool_calls": [c["name"] for c in run.get("tool_calls") or []], "text": run.get("text") or ""})
            runs.append(dict(run, verdicts=new, verdicts_before=old))
        passes = [all(v is not False for v in r["verdicts"].values()) for r in runs]
        results.append(dict(x, runs=runs, pass_rate=sum(passes) / len(passes), pass_rate_before=x["pass_rate"]))
    return results, changes, {"carried": carried, "recomputed_panel": recomputed_panel, "truncated": truncated}


def rescore(src, scenarios, suffix):
    J = json.load(open(src, encoding="utf-8"))
    old_summary = J["summary"]
    results, changes, n = rescore_results(J, scenarios, src)
    carried, recomputed_panel, truncated = n["carried"], n["recomputed_panel"], n["truncated"]
    new_counts, old_counts = counts(results), counts(J["results"])
    label = old_summary.get("label", os.path.splitext(os.path.basename(src))[0]) + suffix
    summary = dict(old_summary)
    summary.update({
        "label": label, "harness": M.HARNESS_VERSION, "rescored_from": os.path.relpath(src, REPO),
        "rescored_offline": "verdicts recomputed from the saved answers; no new generation",
        "original_harness": old_summary.get("harness"), "original_counts": old_counts,
        "pass_all": new_counts["pass"], "flaky": new_counts["flaky"], "fail": new_counts["fail"],
        "verdict_changes": len(changes), "panel_runs_recomputed": recomputed_panel, "panel_runs_carried": carried,
        "auto_texts_at_truncation_limit": truncated, "by_check": by_check(results), "by_check_before": old_summary.get("by_check"),
    })
    stem = os.path.join(os.path.dirname(src), label)
    json.dump({"summary": summary, "changes": changes, "results": results}, open(stem + ".json", "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    write_md(stem + ".md", summary, results, changes, J["results"])
    return summary, changes


def cell(text):
    return re.sub(r"\s*\n\s*", " / ", text).replace("|", "\\|").strip()


def write_md(path, s, results, changes, old_results):
    old_rate = {x["id"]: x["pass_rate"] for x in old_results}
    oc = s["original_counts"]
    auto_runs = sum(len(x["runs"]) for x in results if x["mode"] == "auto")
    md = [f"# Eval {s['label']} — scenarios {s['scenario_version']}", "",
          f"**Rescored offline** from `{s['rescored_from']}` with harness **{s['harness'].split(' ')[0]}** — no new generation; "
          f"the answers are the ones generated for `{s['rescored_from']}` (prompts `{s.get('prompts_ref')}`, auto model `{s.get('model')}`, "
          f"max_tokens {s.get('max_tokens')}).", "",
          f"{s['scenarios']} scenarios / {s['runs']} runs ({auto_runs} auto, {s['runs'] - auto_runs} panel).",
          f"**{s['original_harness'].split(' ')[0]}: pass {oc['pass']} · flaky {oc['flaky']} · fail {oc['fail']}  →  "
          f"{s['harness'].split(' ')[0]}: pass {s['pass_all']} · flaky {s['flaky']} · fail {s['fail']}** · {s['verdict_changes']} verdicts changed.",
          f"Panel runs: {s['panel_runs_recomputed']} recomputed, {s['panel_runs_carried']} carried unchanged (no saved HTTP status / whole body; "
          f"h3 changed no panel check). Auto answers at the old 600-char save limit: {s['auto_texts_at_truncation_limit']}.",
          f"Harness {s['harness']}", "",
          "| scenario | vertical | mode | pass rate (h2 → h3) | failed checks (h3) |", "|---|---|---|---|---|"]
    for x in results:
        failed = sorted({k for r in x["runs"] for k, v in r["verdicts"].items() if v is False})
        mark = "" if old_rate[x["id"]] == x["pass_rate"] else " **changed**"
        md.append(f"| {x['id']} | {x['vertical']} | {x['mode']} | {old_rate[x['id']]:.2f} → {x['pass_rate']:.2f} ({x['repeats']}×){mark} | {', '.join(failed)} |")
    md += ["", f"## Verdict changes ({len(changes)})", "",
           "Every answer whose verdict differs between the two harnesses, with the saved text.", "",
           "| scenario | run | check | h2 → h3 | tools called | answer |", "|---|---|---|---|---|---|"]
    for c in changes:
        verdict = lambda v: "PASS" if v else ("FAIL" if v is False else "—")
        md.append(f"| {c['id']} | {c['run']} | {c['check']} | {verdict(c['old'])} → {verdict(c['new'])} | {', '.join(c['tool_calls']) or '—'} | {cell(c['text'])} |")
    before = s.get("by_check_before") or {}
    md += ["", "## By check (h2 → h3)", "", "| check | h2 | h3 |", "|---|---|---|"]
    for k, v in s["by_check"].items():
        md.append(f"| {k} | {before.get(k, '—')} | {v}{'' if before.get(k) == v else ' ←'} |")
    open(path, "w", encoding="utf-8").write("\n".join(md) + "\n")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("results", nargs="+", help="saved results/<label>.json files")
    ap.add_argument("--scenarios", default=os.path.join(HERE, "scenarios.v1.json"))
    ap.add_argument("--suffix", default="." + M.HARNESS_VERSION.split(" ")[0], help="appended to the label (default: .<harness>)")
    a = ap.parse_args()
    scenarios = json.load(open(a.scenarios, encoding="utf-8"))
    for src in a.results:
        if os.path.basename(src).endswith(a.suffix + ".json"):
            sys.exit(f"{src} is already a rescore output")
        s, changes = rescore(os.path.abspath(src), scenarios, a.suffix)
        oc = s["original_counts"]
        print(f"{s['label']}: {oc['pass']}/{oc['flaky']}/{oc['fail']} -> {s['pass_all']}/{s['flaky']}/{s['fail']} "
              f"(pass/flaky/fail), {len(changes)} verdict changes, panel carried {s['panel_runs_carried']}")
        for c in changes:
            print(f"   {c['id']:28} run {c['run']} {c['check']:30} {c['old']!s:5} -> {c['new']!s:5} | {cell(c['text'])[:150]}")


if __name__ == "__main__":
    main()

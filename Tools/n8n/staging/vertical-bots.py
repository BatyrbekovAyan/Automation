#!/usr/bin/env python3
"""Re-bootstrap staging with bot clones that carry a CREATE-COMPOSED vertical prompt (Q1, 2026-10-09/11).

Why: derive-staging.py builds the staging bots (`stgwa001`/`stgtg001`) from the TEMPLATE, so by default they carry the
template's own prompt (verify-deployed: `prompt=custom`) — no `_core.md`, no niche. A prompt change in
`Tools/n8n/prompts/` is therefore invisible to the model-backed e2e scenarios (e2e-handoff
`bot_knows_owner_agreement_after_auto_resumes` passed/failed on the template prompt both before and after Q1). This
wrapper composes the systemMessage the Create workflow's `Set Fields` would write for a real bot of the given
vertical — «Business Type: <label>» + the `Vertical Prompt` map at --prompts-ref + the vertical's synthetic About
Business / catalog from Tools/n8n/eval/scenarios.v1.json (the same composition run-eval.py measures, its mirror
asserted against the Create workflow) — and runs bootstrap.py with `--prompt-file`, so the staging clones look like
a live clone at that ref (verify-deployed: `prompt=at_canonical/<vertical>` for the worktree map, `behind` for an
older ref — which is how an update-clones rollout is rehearsed).

    python3 Tools/n8n/staging/vertical-bots.py --vertical education                 # clones = today's canon
    python3 Tools/n8n/staging/vertical-bots.py --vertical phone_repair --prompts-ref f02b6674
    python3 Tools/n8n/staging/vertical-bots.py --vertical education --dry-run       # compose + check only
    python3 Tools/n8n/staging/bootstrap.py --bot wa:stgwa001 --bot tg:stgtg001      # back to the template prompt

Staging only: it re-imports the canonical set into the private staging n8n (bootstrap.py) and touches nothing else.
"""
import argparse
import importlib.util
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
N8N_DIR = os.path.dirname(HERE)
HOME = os.path.expanduser(os.environ.get("N8N_STAGING_HOME", "~/.n8n-staging"))
SCENARIOS = os.path.join(N8N_DIR, "eval", "scenarios.v1.json")


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def compose_prompt(vertical, ref):
    """The Create-composed systemMessage (without n8n's leading '=', which derive-staging adds) for `vertical`."""
    rev = load("run_eval", os.path.join(N8N_DIR, "eval", "run-eval.py"))
    inj = load("inject_prompts", os.path.join(N8N_DIR, "inject-prompts.py"))
    prompts = rev.prompts_map(ref)
    rev.set_fields_template(ref)          # asserts the compose() mirror still matches the Create workflow's Set Fields
    if vertical not in prompts:
        sys.exit(f"no vertical {vertical!r} in the Create WA prompt map at {ref}: {sorted(prompts)}")
    know = json.load(open(SCENARIOS, encoding="utf-8"))["knowledge"].get(vertical)
    if not know:
        sys.exit(f"no synthetic knowledge for {vertical!r} in {os.path.relpath(SCENARIOS)}")
    text = rev.compose(prompts[vertical], know["businessType"], "", know["business"], know["products"], know["services"])
    parts = inj.parse_system_message(text)
    # the clone must read as Create-composed (head = this vertical's map entry), or verify-deployed/update-clones would
    # classify it as `custom` and the e2e would measure something other than the map
    if not parts or parts["head"] != prompts[vertical] or parts["label"] != know["businessType"]:
        sys.exit("composed prompt does not parse back to its own head/label — compose() drifted from inject-prompts' grammar")
    return text, parts


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--vertical", required=True, help="BusinessTypeId: auto_parts, wholesale, flowers, kaspi_seller, education, phone_repair")
    ap.add_argument("--prompts-ref", default="worktree", help="git ref whose Create WA prompt map supplies the head (default: worktree)")
    ap.add_argument("--bot", action="append", default=[], help="passed to bootstrap.py (default: wa:stgwa001 + tg:stgtg001)")
    ap.add_argument("--dry-run", action="store_true", help="compose, check and write the prompt file; do not bootstrap")
    a = ap.parse_args()
    text, parts = compose_prompt(a.vertical, a.prompts_ref)
    os.makedirs(HOME, exist_ok=True)
    path = os.path.join(HOME, f"vertical-prompt-{a.vertical}.txt")
    with open(path, "w", encoding="utf-8") as fh:
        fh.write(text)
    print(f"composed {a.vertical} at {a.prompts_ref}: {len(text)} chars (head {len(parts['head'])}, owner tail {len(parts['tail'])}) -> {path}", flush=True)
    if a.dry_run:
        return
    bots = a.bot or ["wa:stgwa001", "tg:stgtg001"]
    cmd = [sys.executable, os.path.join(HERE, "bootstrap.py"), *[x for b in bots for x in ("--bot", b)], "--prompt-file", path]
    sys.exit(subprocess.run(cmd).returncode)


if __name__ == "__main__":
    main()

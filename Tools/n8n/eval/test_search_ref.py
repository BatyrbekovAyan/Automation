#!/usr/bin/env python3
"""A before/after must measure the TOOL of the ref under test, not today's (2026-10-08).

V9 changed the bot's price-list tool (retrieve-as-tool -> the Price List Search sub-workflow), not its prompts, while
run-eval.py read the template from the worktree for both sides — the first V9-r2 «before» run (--prompts-ref 5b0a222d)
quietly went through today's Reconcile Prices. `--search-ref` now names the ref whose template decides the simulation.

    python3 Tools/n8n/eval/test_search_ref.py
"""
import importlib.util
import json
import os
import sys
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
spec = importlib.util.spec_from_file_location("run_eval", os.path.join(HERE, "run-eval.py"))
run_eval = importlib.util.module_from_spec(spec)
spec.loader.exec_module(run_eval)


def search_at(ref):
    return run_eval.search_code(ref, json.loads(run_eval.file_at(ref, run_eval.BOT_WA)))


class SearchRef(unittest.TestCase):
    def setUp(self):
        os.chdir(ROOT)

    def test_before_v9_the_model_saw_raw_documents(self):
        self.assertIsNone(search_at("5b0a222d"))

    def test_from_v9_on_the_hits_pass_that_refs_own_reconcile_prices(self):
        v9, now = search_at("1fd3157e"), search_at("worktree")
        self.assertIn("Сверка с каталогом", v9)
        self.assertIn("Сверка с каталогом", now)
        self.assertNotEqual(v9, now, "V9-r2 changed Reconcile Prices — the two refs must not share one simulation")

    def test_the_flag_reaches_the_simulation(self):
        src = open(os.path.join(HERE, "run-eval.py"), encoding="utf-8").read()
        self.assertIn('ap.add_argument("--search-ref"', src)
        self.assertIn('mp["search_code"] = search_code(a.search_ref, json.loads(file_at(a.search_ref, BOT_WA)))', src)


if __name__ == "__main__":
    sys.exit(0 if unittest.main(exit=False).result.wasSuccessful() else 1)

#!/usr/bin/env python3
"""h8 (T11b, 2026-10-12): the ORDER the eval runs go out in — interleaved, never N identical requests back to back.

T11 (h7) sent a scenario's repeats consecutively, one block per process: ws_kz_min_order on ONE input went 8/10 → 3/3 → 0/10
within 15 minutes, «до» often gave 8/10 identical texts (evidence/Q1B-measure.txt §5). Repeats of one request close in time
are not independent, so a Fisher test over them overstates significance in both directions. h8 spreads every scenario's
repeats over the whole run (round-robin over the scenarios) and, when the sides of a comparison run together (`--side` /
`--plan`), sends the sides of one scenario back to back, rotating which goes first.

    python3 Tools/n8n/eval/test_schedule.py          # no network, no staging, no model calls (the proxy is faked)
"""
import contextlib
import importlib.util
import io
import json
import os
import shutil
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("run_eval", os.environ.get("RUN_EVAL") or os.path.join(HERE, "run-eval.py"))
M = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(M)
DATA = json.load(open(os.path.join(HERE, "scenarios.v1.json"), encoding="utf-8"))
PLAN = os.path.join(HERE, "plans", "q1b2.json")
PRE_T6 = "661431c9"   # no memory marks, prod prompts — the «before» of T11 / T11b
MEMORY_RULE = "Реплики, которые начинаются с «Владелец:»"   # present in «Память» from T6 on, absent at PRE_T6


def requests_of(order):
    return [(key, side) for key, _rep, side in order]


def plan_jobs(plan):
    """The jobs a plan file schedules — through the runner's own builder, the way main() builds them."""
    return M.plan_jobs(plan, DATA, skip_panel=True, skip_auto=False)


class Schedule(unittest.TestCase):
    def test_equal_repeats_are_plain_round_robin(self):
        order = M.schedule([("a", 2, ["x"]), ("b", 2, ["x"]), ("c", 2, ["x"])])
        self.assertEqual([(k, r) for k, r, _ in order], [("a", 0), ("b", 0), ("c", 0), ("a", 1), ("b", 1), ("c", 1)])

    def test_the_h7_order_is_what_the_rule_forbids(self):
        """Negative case: T11's order (a scenario's repeats one after another) breaks the property the tests below pin."""
        h7 = [(k, r, "x") for k, n in (("a", 3), ("b", 3)) for r in range(n)]
        self.assertTrue(any(a == b for a, b in zip(requests_of(h7), requests_of(h7)[1:])))
        new = M.schedule([("a", 3, ["x"]), ("b", 3, ["x"])])
        self.assertFalse(any(a == b for a, b in zip(requests_of(new), requests_of(new)[1:])))

    def test_sides_of_one_slot_run_back_to_back_and_take_turns_first(self):
        order = M.schedule([("a", 4, ["before", "after"]), ("b", 4, ["before", "after"])])
        self.assertEqual(len(order), 16)
        for i in range(0, 16, 2):
            (k1, r1, s1), (k2, r2, s2) = order[i], order[i + 1]
            self.assertEqual((k1, r1), (k2, r2), "the two sides of one scenario repeat are adjacent")
            self.assertEqual({s1, s2}, {"before", "after"})
        for key in ("a", "b"):
            firsts = [order[i][2] for i in range(0, 16, 2) if order[i][0] == key]
            self.assertEqual(sorted(firsts), ["after", "after", "before", "before"], f"{key}: each side goes first half the time")

    def test_every_side_gets_every_repeat_once(self):
        jobs = [("a", 3, ["before", "after", "B"]), ("b", 5, ["before", "after"]), ("c", 2, ["B"])]
        order = M.schedule(jobs)
        for key, n, sides in jobs:
            for s in sides:
                self.assertEqual(sorted(r for k, r, side in order if k == key and side == s), list(range(n)), (key, s))
        self.assertEqual(len(order), 3 * 3 + 5 * 2 + 2)

    def test_unequal_repeats_are_spread_over_the_whole_run(self):
        """A ×20 job beside ×10 ones does not leave its extra repeats for a tail of its own (T11's kz2 block was exactly
        that tail): every job's first repeat sits in the first, its last in the last 1/n of the run."""
        jobs = [(f"k{i}", 10, ["x"]) for i in range(5)] + [("w1", 20, ["x"]), ("w2", 20, ["x"])]
        order = M.schedule(jobs)
        total = len(order)
        for key, n, _ in jobs:
            idx = [i for i, (k, _r, _s) in enumerate(order) if k == key]
            self.assertLessEqual(idx[0], total / n, key)
            self.assertGreaterEqual(idx[-1], total * (1 - 1 / n) - 1, key)

    def test_the_t11b_plan_never_sends_one_request_twice_in_a_row(self):
        plan = json.load(open(PLAN, encoding="utf-8"))
        order = M.schedule(plan_jobs(plan))
        reqs = [(key[1], side) for key, _r, side in order]           # (scenario id, side) = one identical request
        self.assertFalse([i for i in range(len(reqs) - 1) if reqs[i] == reqs[i + 1]])
        # and the long blocks are not left for the end: the last tenth of the run still holds most of the q1 scenarios
        tail = {key[1] for key, _r, _s in order[int(len(order) * 0.9):]}
        q1 = {s["id"] for s in DATA["scenarios"] if s["id"].startswith(("a6_", "q1p_", "q1c_"))}
        self.assertGreaterEqual(len(tail & q1), len(q1) - 2)


class Plan(unittest.TestCase):
    """The T11b plan file — the §4.5 protocol as T11 ran it, plus the B arm on the decisive blocks (fixed before the runs)."""

    def setUp(self):
        self.plan = json.load(open(PLAN, encoding="utf-8"))

    def test_sides(self):
        self.assertEqual(self.plan["sides"], {"before": PRE_T6, "after": "worktree", "B": "fba44558"})

    def test_blocks(self):
        b = {x["name"]: x for x in self.plan["blocks"]}
        self.assertEqual(set(b), {"q1-x8", "kz-x10", "kz2-x20", "v6u-x8", "auto-x3", "ws-x10", "echo-x8"})
        both, three = ["before", "after"], ["before", "after", "B"]
        self.assertEqual((b["q1-x8"]["only"], b["q1-x8"]["repeat"], b["q1-x8"]["sides"]), ("a6_,q1p_,q1c_", 8, both))
        self.assertEqual((b["kz-x10"]["repeat"], b["kz-x10"]["sides"]), (10, both))
        self.assertEqual(set(b["kz-x10"]["only"].split(",")), {"ap_sku_exact_kz", "ws_kz_min_order", "fl_kz_price", "ks_kz_availability",
                                                               "ed_kz_parent", "pr_kz_battery", "gen_kz_pure_greeting"})
        self.assertEqual((set(b["kz2-x20"]["only"].split(",")), b["kz2-x20"]["repeat"], b["kz2-x20"]["sides"]),
                         ({"pr_kz_battery", "ks_kz_availability"}, 20, both))
        self.assertEqual((b["v6u-x8"]["only"], b["v6u-x8"]["repeat"], b["v6u-x8"]["sides"]), ("u1_,u2_,u3_,u4_", 8, both))
        self.assertEqual((b["auto-x3"].get("only"), b["auto-x3"]["repeat"], b["auto-x3"]["sides"]), (None, 3, both))
        self.assertEqual((b["ws-x10"]["only"], b["ws-x10"]["repeat"], b["ws-x10"]["sides"]), ("ws_", 10, three))
        self.assertEqual((b["echo-x8"]["only"], b["echo-x8"]["repeat"], b["echo-x8"]["sides"]), ("q1c_client_time_", 8, ["B"]))
        self.assertTrue(self.plan.get("skip_panel"))

    def test_run_counts(self):
        jobs = plan_jobs(self.plan)
        per_side = {}
        for (block, _sid), n, sides in jobs:
            for s in sides:
                per_side[s] = per_side.get(s, 0) + n
        auto = sum(1 for s in DATA["scenarios"] if s["mode"] == "auto")
        ws = sum(1 for s in DATA["scenarios"] if s["id"].startswith("ws_"))
        protocol = 14 * 8 + 7 * 10 + 2 * 20 + 4 * 8 + auto * 3
        self.assertEqual(per_side["before"], protocol + ws * 10)
        self.assertEqual(per_side["after"], protocol + ws * 10)
        self.assertEqual(per_side["B"], ws * 10 + 3 * 8)


PLAN_Q1B3 = os.path.join(HERE, "plans", "q1b3.json")
B_CANON = "6799b512"   # HEAD before T11c = canon B (prompts/, workflows/, apply-history-handoff.py equal to fba44558, T11b's B)
KZ7 = {"ap_sku_exact_kz", "ws_kz_min_order", "fl_kz_price", "ks_kz_availability", "ed_kz_parent", "pr_kz_battery", "gen_kz_pure_greeting"}


class PlanQ1B3(unittest.TestCase):
    """T11c (owner decision 2026-10-12, the second): the T11b protocol unchanged for before/after, the B arm (HEAD canon) on
    every decisive block — all Kazakh blocks, all ws_* ×10, q1c_client_time_* ×8 and the u-scenarios. Fixed before the runs
    (evidence/Q1B3-measure.txt §0.4)."""

    def setUp(self):
        self.plan = json.load(open(PLAN_Q1B3, encoding="utf-8"))
        self.b = {x["name"]: x for x in self.plan["blocks"]}

    def test_sides(self):
        self.assertEqual(self.plan["label"], "q1b3")
        self.assertEqual(self.plan["sides"], {"before": PRE_T6, "after": "worktree", "B": B_CANON})
        self.assertTrue(self.plan.get("skip_panel"))

    def test_before_and_after_run_the_t11b_protocol_unchanged(self):
        t11b = {x["name"]: x for x in json.load(open(PLAN, encoding="utf-8"))["blocks"]}
        self.assertEqual(set(self.b), set(t11b))
        for name, blk in self.b.items():
            with self.subTest(block=name):
                self.assertEqual((blk.get("only"), blk["repeat"], blk.get("repeat_all", True)),
                                 (t11b[name].get("only"), t11b[name]["repeat"], t11b[name].get("repeat_all", True)))
                self.assertEqual("before" in blk["sides"], "before" in t11b[name]["sides"])
                self.assertEqual("after" in blk["sides"], "after" in t11b[name]["sides"])

    def test_the_b_arm_covers_the_decisive_blocks(self):
        three = ["before", "after", "B"]
        for name in ("kz-x10", "kz2-x20", "v6u-x8", "ws-x10"):
            self.assertEqual(self.b[name]["sides"], three, name)
        self.assertEqual((self.b["echo-x8"]["only"], self.b["echo-x8"]["repeat"], self.b["echo-x8"]["sides"]), ("q1c_client_time_", 8, ["B"]))
        self.assertEqual(self.b["q1-x8"]["sides"], ["before", "after"])
        self.assertEqual(self.b["auto-x3"]["sides"], ["before", "after"])
        # the B arm reaches every Kazakh scenario of K6, every u-scenario of K7/K10, every ws_* and every echo scenario
        b_ids = {sid for (blk, sid), _n, sides in plan_jobs(self.plan) if "B" in sides}
        ids = {s["id"] for s in DATA["scenarios"]}
        self.assertTrue(KZ7 <= b_ids)
        self.assertTrue({i for i in ids if i.startswith(("u1_", "u2_", "u3_", "u4_", "ws_", "q1c_client_time_"))} <= b_ids)

    def test_run_counts(self):
        per_side = {}
        for _key, n, sides in plan_jobs(self.plan):
            for s in sides:
                per_side[s] = per_side.get(s, 0) + n
        auto = sum(1 for s in DATA["scenarios"] if s["mode"] == "auto")
        ws = sum(1 for s in DATA["scenarios"] if s["id"].startswith("ws_"))
        protocol = 14 * 8 + 7 * 10 + 2 * 20 + 4 * 8 + auto * 3
        self.assertEqual((per_side["before"], per_side["after"]), (protocol + ws * 10, protocol + ws * 10))
        self.assertEqual(per_side["B"], 7 * 10 + 2 * 20 + 4 * 8 + ws * 10 + 3 * 8)
        if DATA["version"].startswith("v1.5"):     # 1304 = the T11c run as measured on set v1.5; v1.6 adds scenarios to the same blocks
            self.assertEqual(sum(per_side.values()), 1304)

    def test_the_plan_never_sends_one_request_twice_in_a_row(self):
        order = M.schedule(plan_jobs(self.plan))
        reqs = [(key[1], side) for key, _r, side in order]
        self.assertFalse([i for i in range(len(reqs) - 1) if reqs[i] == reqs[i + 1]])

    def test_the_b_ref_is_the_canon_b_measured_in_t11b(self):
        """B = HEAD before T11c; its measured files equal T11b's B arm (fba44558) — the two B numbers are one arm."""
        for rel in ("Tools/n8n/prompts/_core.md", M.MARKS_SRC, M.CREATE_WA, M.BOT_WA, M.PRICE_SEARCH):
            with self.subTest(file=rel):
                self.assertEqual(M.file_at(B_CANON, rel), M.file_at("fba44558", rel))
        self.assertEqual(M.memory_marks(B_CANON), M.memory_marks("fba44558"))


FAKE_ANSWER = "Завтра в 15:00, спросите Марину."


class FakeProxy:
    """Stands in for the staging completions proxy: records which scenario (its last client message) and which side (the
    «Память» rule is in the worktree prompt, not in PRE_T6's) each request carried, answers with a fixed text."""

    def __init__(self):
        self.calls = []

    def __call__(self, method, url, body=None, timeout=120):
        req = body["request"]
        system = req["messages"][0]["content"]
        last_user = [m["content"] for m in req["messages"] if m["role"] == "user"][-1]
        owner_turns = [m["content"] for m in req["messages"][1:] if m["role"] == "assistant"]
        self.calls.append({"scenario": last_user, "side": "now" if MEMORY_RULE in system else "pre", "assistant_turns": owner_turns})
        return 200, {"status": 200, "body": {"choices": [{"message": {"content": FAKE_ANSWER}, "finish_reason": "stop"}],
                                             "usage": {"prompt_tokens": 10, "completion_tokens": 3}}}


A6 = "a6_owner_agreement_flowers"
U1 = "u1_unknown_reask"
LAST = {sid: [t for r, t in sc["messages"] if r == "client"][-1] for sid, sc in ((s["id"], s) for s in DATA["scenarios"])}


class MainRunsTheSchedule(unittest.TestCase):
    """main() itself goes through schedule(): a fake proxy sees the requests in the interleaved order and the result
    files come out per side, in the h7 shape (rescore.py and the report scripts read them unchanged)."""

    def setUp(self):
        self.tmp = tempfile.mkdtemp(prefix="eval-schedule-")
        self.fake = FakeProxy()
        self.real_http = M.http
        M.http = self.fake

    def tearDown(self):
        M.http = self.real_http
        shutil.rmtree(self.tmp, ignore_errors=True)

    def main(self, argv):
        with contextlib.redirect_stdout(io.StringIO()):   # the runner's progress lines are not the test's output
            M.main(argv)

    def seen(self):
        rev = {v: k for k, v in LAST.items() if k in (A6, U1, "q1p_delivery_flowers")}
        return [(rev[c["scenario"]], c["side"]) for c in self.fake.calls]

    def test_single_side_repeats_go_round_robin(self):
        self.main(["--label", "t", "--only", f"{A6},{U1}", "--repeat", "2", "--repeat-all", "--skip-panel", "--results-dir", self.tmp])
        self.assertEqual([s for s, _ in self.seen()], [A6, U1, A6, U1])
        J = json.load(open(os.path.join(self.tmp, "t.json"), encoding="utf-8"))
        self.assertTrue(J["summary"]["harness"].startswith("h10"))   # h10 (2026-10-13, learning A) adds the off-topic checks; the schedule is h8's
        self.assertEqual([x["id"] for x in J["results"]], [A6, U1], "result files keep the scenario-file order")
        self.assertEqual([r["seq"] for r in J["results"][0]["runs"]], [0, 2])

    def test_sides_run_together_interleaved(self):
        self.main(["--label", "t", "--side", f"pre={PRE_T6}", "--side", "now=worktree", "--only", f"{A6},{U1}", "--repeat", "2",
                "--repeat-all", "--skip-panel", "--results-dir", self.tmp])
        seen = self.seen()
        # one worker: exactly the schedule — the first side rotates with the scenario and the repeat
        self.assertEqual(seen, [(A6, "pre"), (A6, "now"), (U1, "now"), (U1, "pre"), (A6, "now"), (A6, "pre"), (U1, "pre"), (U1, "now")])
        for i in range(0, 8, 2):
            self.assertEqual(seen[i][0], seen[i + 1][0], "both sides of one scenario repeat are adjacent")
            self.assertEqual({seen[i][1], seen[i + 1][1]}, {"pre", "now"})
        self.assertEqual([s for s, _ in seen[::2]], [A6, U1, A6, U1], "round-robin over the scenarios")
        # each side was measured with ITS ref: the owner turn is marked only on the ref with the writers' constants
        owner_turn = {c["side"]: c["assistant_turns"][0] for c in self.fake.calls if c["scenario"] == LAST[A6]}
        self.assertEqual(owner_turn["pre"], "Да, конечно. Завтра в 15:00 вас ждём, спросите Марину.")
        self.assertEqual(owner_turn["now"], "Владелец: Да, конечно. Завтра в 15:00 вас ждём, спросите Марину.")
        for side, ref in (("pre", PRE_T6), ("now", "worktree")):
            J = json.load(open(os.path.join(self.tmp, f"t-{side}.json"), encoding="utf-8"))
            self.assertEqual((J["summary"]["prompts_ref"], J["summary"]["side"]), (ref, side))
            self.assertEqual(J["summary"]["runs"], 4)
            self.assertEqual([len(x["runs"]) for x in J["results"]], [2, 2])
            self.assertTrue(all(r["verdicts"]["mentions:15"] for r in J["results"][0]["runs"]))

    def test_a_plan_writes_one_file_per_block_and_side(self):
        plan = {"label": "p", "sides": {"pre": PRE_T6, "now": "worktree"}, "skip_panel": True,
                "blocks": [{"name": "q", "only": A6, "repeat": 2, "sides": ["pre", "now"]},
                           {"name": "r", "only": "q1p_delivery_flowers", "repeat": 1, "sides": ["now"]}]}
        path = os.path.join(self.tmp, "plan.json")
        with open(path, "w", encoding="utf-8") as fh:
            json.dump(plan, fh)
        self.main(["--plan", path, "--results-dir", self.tmp, "--workers", "2"])
        self.assertEqual(sorted(f for f in os.listdir(self.tmp) if f.endswith(".json") and f != "plan.json"),
                         ["p-q-now.json", "p-q-pre.json", "p-r-now.json"])
        self.assertEqual(sorted(self.seen()), sorted([(A6, "pre"), (A6, "now"), (A6, "pre"), (A6, "now"), ("q1p_delivery_flowers", "now")]))
        J = json.load(open(os.path.join(self.tmp, "p-r-now.json"), encoding="utf-8"))
        self.assertEqual((J["summary"]["block"], J["summary"]["side"], J["summary"]["runs"]), ("r", "now", 1))

    def test_an_exhausted_quota_stops_the_plan_and_writes_nothing(self):
        def quota(method, url, body=None, timeout=120):
            return 200, {"status": 429, "body": {"error": {"code": "insufficient_quota", "message": "quota"}}}
        M.http = quota
        with self.assertRaises(M.QuotaExhausted):
            self.main(["--label", "t", "--side", f"pre={PRE_T6}", "--side", "now=worktree", "--only", A6, "--repeat", "2",
                    "--repeat-all", "--skip-panel", "--results-dir", self.tmp, "--workers", "2"])
        self.assertEqual([f for f in os.listdir(self.tmp) if f.endswith(".json")], [])


if __name__ == "__main__":
    sys.exit(0 if unittest.main(exit=False, verbosity=2).result.wasSuccessful() else 1)

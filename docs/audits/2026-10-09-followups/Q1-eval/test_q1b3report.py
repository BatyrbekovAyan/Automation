#!/usr/bin/env python3
"""T11c — the criteria of evidence/Q1B3-measure.txt §0 that differ from T11b: К3 and К4 («not worse than production», changed
by the owner's decision 2026-10-12) and К10 (new: a spurious create_handoff on the u-scenarios), plus a replay of T11b's saved
runs (Tools/n8n/eval/results/q1b2-*) through q1b3report.py — the criteria К1, К2, К5–К9 must come out exactly as
q1b2report.py computed them on the same files. No network, no model.

    python3 docs/audits/2026-10-09-followups/Q1-eval/test_q1b3report.py
"""
import contextlib
import importlib.util
import io
import os
import re
import subprocess
import sys
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("q1b3report", os.path.join(HERE, "q1b3report.py"))
R = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(R)

U = ["u1_unknown_reask", "u2_unknown_new_question", "u3_sent_control", "u4_kz_unknown"]


class K3EchoNotWorseThanProd(unittest.TestCase):
    """Echo failures of not_mentions:15:00 on the three q1c_client_time_* (24 runs a side). Fail only when «после» echoes
    SIGNIFICANTLY more often than «до» (Fisher exact, one-sided, p < 0.05) — the test К6 uses, the other way round."""
    ROWS = [   # (before (fails, runs), after (fails, runs), met)
        ((0, 24), (0, 24), True),
        ((0, 24), (3, 24), True),     # B's level in T11b (q1c_client_time_history_phone_repair 3/8): p≈0.12 — accepted now
        ((0, 24), (4, 24), True),     # p≈0.055
        ((0, 24), (5, 24), False),    # p≈0.025
        ((4, 24), (0, 24), True),     # fewer echoes than prod
        ((3, 24), (8, 24), True),     # p≈0.084
        ((3, 24), (9, 24), False),    # p≈0.047
        ((0, 24), (0, 23), False),    # a run missing: the side is not complete
        ((0, 23), (0, 24), False),
    ]

    def test_rows(self):
        for before, after, met in self.ROWS:
            with self.subTest(before=before, after=after):
                ok, p = R.k3_echo(before, after)
                self.assertEqual(ok, met, f"p={p:.4g}")

    def test_the_old_rule_refused_what_the_owner_now_accepts(self):
        """Negative case for the change itself: T11b's К3 («после» 0/24 and ≤ «до») refuses an echo at a level prod has
        been shown to reach; the new К3 accepts it — and still refuses a clear regression."""
        old = lambda b, x: x[0] == 0 and x[0] <= b[0] and x[1] == 24
        self.assertFalse(old((0, 24), (3, 24)))
        self.assertTrue(R.k3_echo((0, 24), (3, 24))[0])
        self.assertFalse(R.k3_echo((0, 24), (5, 24))[0])


class K4ControlsNotWorseThanProd(unittest.TestCase):
    """All checks on the five q1c controls (40 runs a side): fail only when «после» passes SIGNIFICANTLY less often than «до»."""
    ROWS = [
        ((40, 40), (40, 40), True),
        ((40, 40), (36, 40), True),   # p≈0.058
        ((40, 40), (35, 40), False),  # p≈0.027
        ((39, 40), (34, 40), True),   # p≈0.054
        ((39, 40), (33, 40), False),  # p≈0.028
        ((36, 40), (40, 40), True),
        ((40, 40), (39, 39), False),  # incomplete
    ]

    def test_rows(self):
        for before, after, met in self.ROWS:
            with self.subTest(before=before, after=after):
                ok, p = R.k4_controls(before, after)
                self.assertEqual(ok, met, f"p={p:.4g}")

    def test_the_old_rule_refused_what_the_owner_now_accepts(self):
        old = lambda b, x: x[0] >= 39 and x[1] == 40
        self.assertFalse(old((40, 40), (37, 40)))
        self.assertTrue(R.k4_controls((40, 40), (37, 40))[0])


def per(**calls):
    """{sid: {"before": (calls, 11), "after": (calls, 11)}} — u1..u4 default to no call on either side."""
    out = {sid: {"before": (0, 11), "after": (0, 11)} for sid in U}
    for short, (b, a) in calls.items():
        sid = next(s for s in U if s.startswith(short + "_"))
        out[sid] = {"before": (b, 11), "after": (a, 11)}
    return out


class K10SpuriousHandoffOnU(unittest.TestCase):
    """Runs of u1–u4 with a create_handoff call (all four carry no_tool:create_handoff since v1.5), over every block of the
    side the u-scenarios run in (v6u-x8 + auto-x3 = 11 a scenario): «после» ≤ «до» on u2 AND on the sum of u1–u4."""

    def test_t11b_numbers_fail(self):
        ok, tot = R.k10(per(u2=(0, 5)))   # T11b: u2_unknown_new_question 0/11 → 5/11, the others 0 → 0
        self.assertFalse(ok)
        self.assertEqual(tot, {"before": (0, 44), "after": (5, 44)})

    def test_rows(self):
        rows = [
            (per(), True),
            (per(u2=(1, 1)), True),
            (per(u2=(2, 1)), True),
            (per(u2=(0, 1)), False),                 # one spurious request on the delivery question more than prod
            (per(u1=(0, 1)), False),                 # u2 clean, but the sum grew
            (per(u2=(2, 0), u1=(0, 1)), True),       # the sum fell and u2 fell: the sum rule, as К9's (sum AND the named one)
            (per(u2=(1, 2), u1=(2, 0)), False),      # the sum fell, u2 grew
        ]
        for p, met in rows:
            with self.subTest(per={k: v for k, v in p.items() if v["before"][0] or v["after"][0]}):
                self.assertEqual(R.k10(p)[0], met)

    def test_incomplete_side_fails(self):
        p = per()
        p["u3_sent_control"]["after"] = (0, 10)
        self.assertFalse(R.k10(p)[0])
        p = per()
        del p["u4_kz_unknown"]
        self.assertFalse(R.k10(p)[0])


def verdict_lines(text):
    return dict(re.findall(r"^  (К\d+): (выполнен|НЕ выполнен)$", text, re.M))


class ReplayT11b(unittest.TestCase):
    """q1b3report over T11b's own results (label q1b2): К1, К2, К5–К9 exactly as q1b2report.py printed them; К3/К4 under the
    new rule; К10 not met (the u2 finding)."""

    @classmethod
    def setUpClass(cls):
        buf = io.StringIO()
        with contextlib.redirect_stdout(buf):
            cls.verdict = R.report("q1b2")
        cls.text = buf.getvalue()
        cls.old = subprocess.run([sys.executable, os.path.join(HERE, "q1b2report.py")], capture_output=True, text=True, check=True).stdout

    def test_unchanged_criteria_match_q1b2report(self):
        old = verdict_lines(self.old)
        self.assertEqual(set(old), {f"К{i}" for i in range(1, 10)})
        for k in ("К1", "К2", "К5", "К6", "К7", "К8", "К9"):
            with self.subTest(criterion=k):
                self.assertEqual(self.verdict[k], old[k] == "выполнен")

    def test_printed_verdicts_are_the_returned_ones(self):
        self.assertEqual(verdict_lines(self.text), {k: "выполнен" if v else "НЕ выполнен" for k, v in self.verdict.items()})

    def test_new_and_changed_criteria(self):
        self.assertEqual(list(self.verdict), [f"К{i}" for i in range(1, 11)])
        self.assertTrue(self.verdict["К3"])     # 0/24 → 0/24
        self.assertTrue(self.verdict["К4"])     # 39/40 → 40/40
        self.assertFalse(self.verdict["К6"])
        self.assertFalse(self.verdict["К10"])   # u2 0/11 → 5/11
        self.assertIn("u2_unknown_new_question", self.text)
        self.assertRegex(self.text, r"u2_unknown_new_question\s+до\s+0/11\s+после\s+5/11")

    def test_missing_b_arm_files_are_reported_not_fatal(self):
        """T11b ran B on ws/echo only; the T11c B arm on kz/v6 is diagnostics and its absence must not break the criteria."""
        self.assertEqual(self.text.count("— нет файла руки B"), 2)   # kz and v6u
        self.assertRegex(self.text, r"q1c_client_time_history_phone_repair\s+эхо до 0/8 после 0/8 B 3/8")


if __name__ == "__main__":
    sys.exit(0 if unittest.main(exit=False, verbosity=2).result.wasSuccessful() else 1)

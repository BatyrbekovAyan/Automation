#!/usr/bin/env python3
"""T11 — сводка критериев К1–К8 (F1F2-plan §4.5) по сохранённым результатам run-eval (h7, набор v1.4). Без сети и модели.

    python3 docs/audits/2026-10-09-followups/Q1-eval/t11report.py [--prefix q1b-t11] [--v6 v6u-t11] [--before before --after after]

Читает Tools/n8n/eval/results/<prefix>-{q1-x8,kz-x10,kz2-x20,auto-x3}-<side>.json и <v6>-x8-<side>.json, печатает таблицу по
каждому критерию с числами обеих сторон и Fisher (точный, односторонний). Критерии — ровно те, что зафиксированы в
evidence/Q1B-measure.txt §0 до прогонов.
"""
import argparse
import json
import os
from math import comb

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", "..", ".."))
RES = os.path.join(REPO, "Tools", "n8n", "eval", "results")
KZ7 = ["ap_sku_exact_kz", "ws_kz_min_order", "fl_kz_price", "ks_kz_availability", "ed_kz_parent", "pr_kz_battery", "gen_kz_pure_greeting"]
WEAK = ["pr_kz_battery", "ks_kz_availability"]
PROMISE = ["no_time_assignment", "no_exact_time_confirmation", "no_reservation_confirmation", "no_schedule_invention",
           "no_time_promise_without_hours", "claims_handoff_only_if_ok", "tool_called", "no_payment_confirmation", "no_stock_guarantee",
           "no_fresh_price_pick", "no_free_delivery_claim", "no_prompt_leak"]


def fisher_greater(a, n1, b, n2):
    """P(X >= a) for group 1 successes a of n1 vs group 2 b of n2 under H0 (hypergeometric) — H1: rate1 > rate2."""
    k, n = a + b, n1 + n2
    tot = comb(n, k)
    return sum(comb(n1, x) * comb(n2, k - x) for x in range(a, min(n1, k) + 1)) / tot


def load(name):
    p = os.path.join(RES, name + ".json")
    return json.load(open(p, encoding="utf-8")) if os.path.exists(p) else None


def runs(J, pred=lambda x: True):
    return [(x["id"], r) for x in (J or {}).get("results", []) if pred(x) for r in x["runs"]]


def all_ok(r, names=None):
    v = r["verdicts"]
    return all(v.get(k) is not False for k in (names or v))


def count(J, ids=None, prefix=None, ok=all_ok):
    rs = runs(J, lambda x: (ids is None or x["id"] in ids) and (prefix is None or x["id"].startswith(prefix)))
    return sum(1 for _, r in rs if ok(r)), len(rs)


def handoffs(J, prefixes):
    rs = runs(J, lambda x: x["id"].startswith(prefixes))
    return sum(1 for _, r in rs if any(c.get("name") == "create_handoff" for c in r.get("tool_calls") or [])), len(rs)


def lang_ok(r):
    return all(v is not False for k, v in r["verdicts"].items() if k.startswith("lang"))


def merge(*Js):
    out = {"results": []}
    for J in Js:
        if J:
            out["results"] += J["results"]
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--prefix", default="q1b-t11")
    ap.add_argument("--v6", default="v6u-t11")
    ap.add_argument("--before", default="before")
    ap.add_argument("--after", default="after")
    a = ap.parse_args()
    side = {}
    for s in (a.before, a.after):
        side[s] = {"q1": load(f"{a.prefix}-q1-x8-{s}"), "kz": merge(load(f"{a.prefix}-kz-x10-{s}"), load(f"{a.prefix}-kz2-x20-{s}")),
                   "v6": load(f"{a.v6}-x8-{s}"), "auto": load(f"{a.prefix}-auto-x3-{s}")}
    B, A = side[a.before], side[a.after]
    verdict = {}

    def row(label, b, a_, extra=""):
        print(f"  {label:58} до {b[0]:>3}/{b[1]:<3}  после {a_[0]:>3}/{a_[1]:<3} {extra}")

    print("К1 a6 — все проверки")
    b, x = count(B["q1"], prefix="a6_"), count(A["q1"], prefix="a6_")
    row("a6 (3 ниши × 8)", b, x)
    for sid in sorted({i for i, _ in runs(A["q1"]) if i.startswith("a6_")}):
        row("  " + sid, count(B["q1"], ids={sid}), count(A["q1"], ids={sid}))
    verdict["К1"] = x[1] > 0 and x[0] >= 23 and x[1] == 24

    print("К2 q1p — все проверки")
    b, x = count(B["q1"], prefix="q1p_"), count(A["q1"], prefix="q1p_")
    p = fisher_greater(x[0], x[1], b[0], b[1]) if x[1] and b[1] else 1.0
    row("q1p (6 × 8)", b, x, f"Fisher после>до p={p:.3g}")
    worse = []
    for sid in sorted({i for i, _ in runs(A["q1"]) if i.startswith("q1p_")}):
        bb, xx = count(B["q1"], ids={sid}), count(A["q1"], ids={sid})
        row("  " + sid, bb, xx, "ХУЖЕ на ≥3" if bb[0] - xx[0] >= 3 else "")
        if bb[0] - xx[0] >= 3:
            worse.append(sid)
    verdict["К2"] = x[0] >= 30 and p < 0.05 and not worse

    print("К3 эхо клиентского времени (провалы not_mentions:15:00, q1c_client_time_*)")
    echo = lambda r: r["verdicts"].get("not_mentions:15:00") is False
    b = (sum(1 for i, r in runs(B["q1"]) if i.startswith("q1c_client_time") and echo(r)), len(runs(B["q1"], lambda x: x["id"].startswith("q1c_client_time"))))
    x = (sum(1 for i, r in runs(A["q1"]) if i.startswith("q1c_client_time") and echo(r)), len(runs(A["q1"], lambda x: x["id"].startswith("q1c_client_time"))))
    row("эхо (меньше — лучше)", b, x)
    verdict["К3"] = x[0] == 0 and x[0] <= b[0] and x[1] == 24

    print("К4 контроли q1c — все проверки")
    b, x = count(B["q1"], prefix="q1c_"), count(A["q1"], prefix="q1c_")
    row("q1c (5 × 8)", b, x)
    for sid in sorted({i for i, _ in runs(A["q1"]) if i.startswith("q1c_")}):
        row("  " + sid, count(B["q1"], ids={sid}), count(A["q1"], ids={sid}))
    verdict["К4"] = x[0] >= 39 and x[1] == 40

    print("К5 лишний create_handoff на a6/q1p (прогонов с вызовом; меньше — лучше)")
    b, x = handoffs(B["q1"], ("a6_", "q1p_")), handoffs(A["q1"], ("a6_", "q1p_"))
    row("a6 + q1p", b, x)
    for sid in sorted({i for i, _ in runs(A["q1"]) if i.startswith(("a6_", "q1p_"))}):
        bb, xx = handoffs(B["q1"], (sid,)), handoffs(A["q1"], (sid,))
        if bb[0] or xx[0]:
            row("  " + sid, bb, xx)
    verdict["К5"] = x[0] <= b[0]

    print("К6 казахский — прогоны, где все проверки lang* истинны (H1 ухудшения: до > после)")
    ok6 = True
    for label, ids in (("(а) сумма 7 KZ-сценариев", set(KZ7)), ("(б) pr_kz_battery", {"pr_kz_battery"}),
                       ("(в) ks_kz_availability", {"ks_kz_availability"}), ("(г) сумма пяти остальных", set(KZ7) - set(WEAK))):
        b, x = count(B["kz"], ids=ids, ok=lang_ok), count(A["kz"], ids=ids, ok=lang_ok)
        p = fisher_greater(b[0], b[1], x[0], x[1]) if x[1] and b[1] else 1.0
        bad = p < 0.05
        ok6 = ok6 and not bad
        row(label, b, x, f"Fisher до>после p={p:.3g}{'  ЗНАЧИМОЕ УХУДШЕНИЕ' if bad else ''}")
    for sid in KZ7:
        if sid in WEAK:
            continue
        row("  " + sid, count(B["kz"], ids={sid}, ok=lang_ok), count(A["kz"], ids={sid}, ok=lang_ok))
    verdict["К6"] = ok6

    print("К7 V6 (×8)")
    gate17 = ["mentions:3200", "not_says_already", "no_marker_leak"]
    gate23 = ["no_delivery_doubt", "no_marker_leak"]
    ok7 = True
    for sid, gate in (("u1_unknown_reask", gate17), ("u4_kz_unknown", gate17), ("u2_unknown_new_question", gate23), ("u3_sent_control", gate23)):
        b = count(B["v6"], ids={sid}, ok=lambda r: all_ok(r, gate))
        x = count(A["v6"], ids={sid}, ok=lambda r: all_ok(r, gate))
        ok7 = ok7 and x[1] == 8 and x[0] >= 7
        row(f"{sid} [{' ∧ '.join(gate)}]", b, x)
        bb, xx = count(B["v6"], ids={sid}), count(A["v6"], ids={sid})
        row(f"  {sid} — все проверки (для отчёта)", bb, xx)
    leak_after = sum(1 for J in A.values() for _, r in runs(J) if r["verdicts"].get("no_marker_leak") is False)
    n_after = sum(len(runs(J)) for J in A.values())
    leak_before = sum(1 for J in B.values() for _, r in runs(J) if r["verdicts"].get("no_marker_leak") is False)
    n_before = sum(len(runs(J)) for J in B.values())
    print(f"  утечка пометок (no_marker_leak FAIL) во всех прогонах: до {leak_before}/{n_before}, после {leak_after}/{n_after}")
    verdict["К7"] = ok7 and leak_after == 0

    print("К8 регрессия ×3 (весь автоответ v1.4)")
    rb = {x["id"]: x["pass_rate"] for x in (B["auto"] or {}).get("results", [])}
    ra = {x["id"]: x["pass_rate"] for x in (A["auto"] or {}).get("results", [])}
    cls = lambda d: (sum(1 for v in d.values() if v == 1), sum(1 for v in d.values() if 0 < v < 1), sum(1 for v in d.values() if v == 0))
    print(f"  чистых/нестабильных/провалов: до {cls(rb)}  после {cls(ra)}")
    zeroed = [i for i in ra if rb.get(i, 0) > 0 and ra[i] == 0]
    print(f"  стали 0/3 из чистых или нестабильных «до»: {zeroed or 'нет'}")
    diffs = [(i, rb.get(i), ra[i]) for i in ra if rb.get(i) != ra[i]]
    for i, b_, a_ in sorted(diffs):
        print(f"    {i:40} до {b_ if b_ is None else round(b_ * 3)}/3  после {round(a_ * 3)}/3")
    prom_fail = {}
    for k in PROMISE:
        b_ = [r["verdicts"][k] for _, r in runs(B["auto"]) if k in r["verdicts"]]
        a_ = [r["verdicts"][k] for _, r in runs(A["auto"]) if k in r["verdicts"]]
        print(f"    {k:34} до {sum(1 for v in b_ if v)}/{len(b_)}  после {sum(1 for v in a_ if v)}/{len(a_)}")
        if any(v is False for v in a_):
            prom_fail[k] = sum(1 for v in a_ if v is False)
    verdict["К8"] = not zeroed and not prom_fail and cls(ra)[1] <= cls(rb)[1] + 2

    print("\nДиагностика (НЕ входит в критерии; добавлена после того, как К3/К4/К8 не прошли, те же стороны, тот же день)")
    exp = {}
    sc_path = os.path.join(REPO, "Tools", "n8n", "eval", "scenarios.v1.json")
    for sc in json.load(open(sc_path, encoding="utf-8"))["scenarios"]:
        exp[sc["id"]] = sc["expect"]
    for s in (a.before, a.after):
        unexpected = {}
        for J in side[s].values():
            for i, r in runs(J):
                if "tool_called" not in exp.get(i, []) and any(c.get("name") == "create_handoff" for c in r.get("tool_calls") or []):
                    unexpected[i] = unexpected.get(i, 0) + 1
        print(f"  create_handoff там, где сценарий его не ждёт (все блоки §0), {s}: " + ", ".join(f"{k} {v}" for k, v in sorted(unexpected.items())))
    for stem, title in ((f"{a.prefix}-ws-x10", "опт ×10 (повтор окна)"), (f"{a.prefix}-echo2-x8", "контроли q1c_client_time_* ×8 (второе окно)")):
        Jb, Ja = load(f"{stem}-{a.before}"), load(f"{stem}-{a.after}")
        if not (Jb and Ja):
            continue
        print(f"  {title}:")
        for sid in [x["id"] for x in Jb["results"]]:
            hb, ha = handoffs(Jb, (sid,)), handoffs(Ja, (sid,))
            pb, pa = count(Jb, ids={sid}), count(Ja, ids={sid})
            eb = sum(1 for _, r in runs(Jb, lambda x: x["id"] == sid) if r["verdicts"].get("not_mentions:15:00") is False)
            ea = sum(1 for _, r in runs(Ja, lambda x: x["id"] == sid) if r["verdicts"].get("not_mentions:15:00") is False)
            extra = f"  эхо до {eb} после {ea}" if "not_mentions:15:00" in exp.get(sid, []) else ""
            print(f"    {sid:40} все проверки до {pb[0]}/{pb[1]} после {pa[0]}/{pa[1]}  create_handoff до {hb[0]} после {ha[0]}{extra}")
    cost = {s: 0.0 for s in (a.before, a.after)}
    nruns = {s: 0 for s in (a.before, a.after)}
    for f in sorted(os.listdir(RES)):
        for s in (a.before, a.after):
            if f.endswith(f"-{s}.json") and (f.startswith(a.prefix + "-") or f.startswith(a.v6 + "-")):
                sm = json.load(open(os.path.join(RES, f), encoding="utf-8"))["summary"]
                cost[s] += sm.get("cost_usd_auto_path") or 0
                nruns[s] += sm.get("runs") or 0
    print(f"  прогонов и стоимость (все блоки T11, путь автоответа): до {nruns[a.before]} / ${cost[a.before]:.3f}, после {nruns[a.after]} / ${cost[a.after]:.3f}")

    print("\nИтог по критериям (A6 на staging — отдельно):")
    for k, v in verdict.items():
        print(f"  {k}: {'выполнен' if v else 'НЕ выполнен'}")


if __name__ == "__main__":
    main()

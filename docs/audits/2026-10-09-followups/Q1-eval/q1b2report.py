#!/usr/bin/env python3
"""T11b — сводка критериев К1–К9 (evidence/Q1B2-measure.txt §0) по сохранённым результатам одного чередующегося прогона
run-eval --plan Tools/n8n/eval/plans/q1b2.json (оценщик h8, набор v1.5). Без сети и модели.

    python3 docs/audits/2026-10-09-followups/Q1-eval/q1b2report.py [--label q1b2]

Читает Tools/n8n/eval/results/<label>-<блок>-<сторона>.json. К1–К8 считаются ровно как в t11report.py (его функции
импортируются, а не копируются); К7 — утечка по ВСЕМ блокам стороны «после», включая ws-x10; К9 — новый, см. §0.
Рука B (канон HEAD) — справочная, в вердикт не входит.
"""
import argparse
import importlib.util
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("t11report", os.path.join(HERE, "t11report.py"))
T = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(T)
REPO, RES = T.REPO, T.RES
SCEN = json.load(open(os.path.join(REPO, "Tools", "n8n", "eval", "scenarios.v1.json"), encoding="utf-8"))
EXPECT = {s["id"]: s["expect"] for s in SCEN["scenarios"]}
CHECKED = {sid for sid, e in EXPECT.items() if "no_tool:create_handoff" in e}      # v1.5: no create_handoff by design
BLOCKS = ["q1-x8", "kz-x10", "kz2-x20", "v6u-x8", "auto-x3", "ws-x10"]


def called(r):
    return any(c.get("name") == "create_handoff" for c in r.get("tool_calls") or [])


def spurious(J, ids=None):
    """(runs with a create_handoff call, runs) among the runs of `J` whose scenario carries no_tool:create_handoff."""
    rs = T.runs(J, lambda x: x["id"] in CHECKED and (ids is None or x["id"] in ids))
    return sum(1 for _, r in rs if called(r)), len(rs)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--label", default="q1b2")
    ap.add_argument("--results-dir", default=RES)
    a = ap.parse_args()
    T.RES = a.results_dir
    files = {(b, s): T.load(f"{a.label}-{b}-{s}") for b in BLOCKS + ["echo-x8"] for s in ("before", "after", "B")}
    for (b, s), J in files.items():
        if J and not J["summary"]["harness"].startswith("h8"):
            raise SystemExit(f"{a.label}-{b}-{s}: harness {J['summary']['harness'][:3]} — К1–К9 are fixed for h8 / v1.5")
    side = {s: {"q1": files[("q1-x8", s)], "kz": T.merge(files[("kz-x10", s)], files[("kz2-x20", s)]), "v6": files[("v6u-x8", s)],
                "auto": files[("auto-x3", s)], "ws": files[("ws-x10", s)]} for s in ("before", "after")}
    B, A = side["before"], side["after"]
    verdict = {}
    row = lambda label, b, x, extra="": print(f"  {label:58} до {b[0]:>3}/{b[1]:<3}  после {x[0]:>3}/{x[1]:<3} {extra}")

    print("К1 a6 — все проверки")
    b, x = T.count(B["q1"], prefix="a6_"), T.count(A["q1"], prefix="a6_")
    row("a6 (3 ниши × 8)", b, x)
    for sid in sorted({i for i, _ in T.runs(A["q1"]) if i.startswith("a6_")}):
        row("  " + sid, T.count(B["q1"], ids={sid}), T.count(A["q1"], ids={sid}))
    verdict["К1"] = x[0] >= 23 and x[1] == 24

    print("К2 q1p — все проверки")
    b, x = T.count(B["q1"], prefix="q1p_"), T.count(A["q1"], prefix="q1p_")
    p = T.fisher_greater(x[0], x[1], b[0], b[1]) if x[1] and b[1] else 1.0
    row("q1p (6 × 8)", b, x, f"Fisher после>до p={p:.3g}")
    worse = []
    for sid in sorted({i for i, _ in T.runs(A["q1"]) if i.startswith("q1p_")}):
        bb, xx = T.count(B["q1"], ids={sid}), T.count(A["q1"], ids={sid})
        row("  " + sid, bb, xx, "ХУЖЕ на ≥3" if bb[0] - xx[0] >= 3 else "")
        if bb[0] - xx[0] >= 3:
            worse.append(sid)
    verdict["К2"] = x[0] >= 30 and p < 0.05 and not worse

    print("К3 эхо клиентского времени (провалы not_mentions:15:00, q1c_client_time_*; меньше — лучше)")
    echo = lambda J: (sum(1 for i, r in T.runs(J) if i.startswith("q1c_client_time") and r["verdicts"].get("not_mentions:15:00") is False),
                      len(T.runs(J, lambda y: y["id"].startswith("q1c_client_time"))))
    b, x = echo(B["q1"]), echo(A["q1"])
    row("эхо", b, x)
    verdict["К3"] = x[0] == 0 and x[0] <= b[0] and x[1] == 24

    print("К4 контроли q1c — все проверки")
    b, x = T.count(B["q1"], prefix="q1c_"), T.count(A["q1"], prefix="q1c_")
    row("q1c (5 × 8)", b, x)
    for sid in sorted({i for i, _ in T.runs(A["q1"]) if i.startswith("q1c_")}):
        row("  " + sid, T.count(B["q1"], ids={sid}), T.count(A["q1"], ids={sid}))
    verdict["К4"] = x[0] >= 39 and x[1] == 40

    print("К5 лишний create_handoff на a6/q1p (прогонов с вызовом; меньше — лучше)")
    b, x = T.handoffs(B["q1"], ("a6_", "q1p_")), T.handoffs(A["q1"], ("a6_", "q1p_"))
    row("a6 + q1p", b, x)
    for sid in sorted({i for i, _ in T.runs(A["q1"]) if i.startswith(("a6_", "q1p_"))}):
        bb, xx = T.handoffs(B["q1"], (sid,)), T.handoffs(A["q1"], (sid,))
        if bb[0] or xx[0]:
            row("  " + sid, bb, xx)
    verdict["К5"] = x[0] <= b[0]

    print("К6 казахский — прогоны, где все проверки lang* истинны (H1 ухудшения: до > после)")
    ok6 = True
    for label, ids in (("(а) сумма 7 KZ-сценариев", set(T.KZ7)), ("(б) pr_kz_battery", {"pr_kz_battery"}),
                       ("(в) ks_kz_availability", {"ks_kz_availability"}), ("(г) сумма пяти остальных", set(T.KZ7) - set(T.WEAK))):
        b, x = T.count(B["kz"], ids=ids, ok=T.lang_ok), T.count(A["kz"], ids=ids, ok=T.lang_ok)
        p = T.fisher_greater(b[0], b[1], x[0], x[1]) if x[1] and b[1] else 1.0
        bad = p < 0.05
        ok6 = ok6 and not bad
        row(label, b, x, f"Fisher до>после p={p:.3g}{'  ЗНАЧИМОЕ УХУДШЕНИЕ' if bad else ''}")
    for sid in T.KZ7:
        if sid not in T.WEAK:
            row("  " + sid, T.count(B["kz"], ids={sid}, ok=T.lang_ok), T.count(A["kz"], ids={sid}, ok=T.lang_ok))
    verdict["К6"] = ok6

    print("К7 V6 (×8)")
    gate14, gate23 = ["mentions:3200", "not_says_already", "no_marker_leak"], ["no_delivery_doubt", "no_marker_leak"]
    ok7 = True
    for sid, gate in (("u1_unknown_reask", gate14), ("u4_kz_unknown", gate14), ("u2_unknown_new_question", gate23), ("u3_sent_control", gate23)):
        b = T.count(B["v6"], ids={sid}, ok=lambda r: T.all_ok(r, gate))
        x = T.count(A["v6"], ids={sid}, ok=lambda r: T.all_ok(r, gate))
        ok7 = ok7 and x[1] == 8 and x[0] >= 7
        row(f"{sid} [{' ∧ '.join(gate)}]", b, x)
        row(f"  {sid} — все проверки (для отчёта)", T.count(B["v6"], ids={sid}), T.count(A["v6"], ids={sid}))
    leak = {s: (sum(1 for J in side[s].values() for _, r in T.runs(J) if r["verdicts"].get("no_marker_leak") is False),
                sum(len(T.runs(J)) for J in side[s].values())) for s in ("before", "after")}
    print(f"  утечка пометок (no_marker_leak FAIL) во всех блоках: до {leak['before'][0]}/{leak['before'][1]}, после {leak['after'][0]}/{leak['after'][1]}")
    verdict["К7"] = ok7 and leak["after"][0] == 0

    print("К8 регрессия ×3 (весь автоответ v1.5, все проверки)")
    rb = {x["id"]: x["pass_rate"] for x in (B["auto"] or {}).get("results", [])}
    ra = {x["id"]: x["pass_rate"] for x in (A["auto"] or {}).get("results", [])}
    cls = lambda dd: (sum(1 for v in dd.values() if v == 1), sum(1 for v in dd.values() if 0 < v < 1), sum(1 for v in dd.values() if v == 0))
    print(f"  чистых/нестабильных/провалов: до {cls(rb)}  после {cls(ra)}")
    zeroed = [i for i in ra if rb.get(i, 0) > 0 and ra[i] == 0]
    print(f"  стали 0/3 из чистых или нестабильных «до»: {zeroed or 'нет'}")
    for i in sorted(i for i in ra if rb.get(i) != ra[i]):
        print(f"    {i:40} до {round(rb[i] * 3) if i in rb else None}/3  после {round(ra[i] * 3)}/3")
    prom_fail = {}
    for k in T.PROMISE:
        b_ = [r["verdicts"][k] for _, r in T.runs(B["auto"]) if k in r["verdicts"]]
        a_ = [r["verdicts"][k] for _, r in T.runs(A["auto"]) if k in r["verdicts"]]
        print(f"    {k:34} до {sum(1 for v in b_ if v)}/{len(b_)}  после {sum(1 for v in a_ if v)}/{len(a_)}")
        if any(v is False for v in a_):
            prom_fail[k] = sum(1 for v in a_ if v is False)
    verdict["К8"] = not zeroed and not prom_fail and cls(ra)[1] <= cls(rb)[1] + 2

    print(f"К9 лишний create_handoff на сценариях с no_tool:create_handoff ({len(CHECKED)} сц.), все блоки стороны; меньше — лучше")
    tot = {s: [0, 0] for s in ("before", "after")}
    for blk, key in (("q1-x8", "q1"), ("kz-x10", None), ("kz2-x20", None), ("v6u-x8", "v6"), ("auto-x3", "auto"), ("ws-x10", "ws")):
        bb, xx = spurious(files[(blk, "before")]), spurious(files[(blk, "after")])
        for s, v in (("before", bb), ("after", xx)):
            tot[s][0] += v[0]
            tot[s][1] += v[1]
        row("  блок " + blk, bb, xx)
    row("всего", tuple(tot["before"]), tuple(tot["after"]))
    per = {}
    for s in ("before", "after"):
        for blk in BLOCKS:
            for i, r in T.runs(files[(blk, s)], lambda y: y["id"] in CHECKED):
                c = per.setdefault(i, {"before": [0, 0], "after": [0, 0]})[s]
                c[1] += 1
                c[0] += called(r)
    for i in sorted(per):
        if per[i]["before"][0] or per[i]["after"][0]:
            row("  " + i, tuple(per[i]["before"]), tuple(per[i]["after"]))
    wk = {s: tuple(per.get("ws_kz_min_order", {}).get(s, [0, 0])) for s in ("before", "after")}
    row("ws_kz_min_order (kz-x10 + auto-x3 + ws-x10)", wk["before"], wk["after"])
    verdict["К9"] = tot["after"][0] <= tot["before"][0] and wk["after"][0] <= wk["before"][0]

    print("\nРука B (канон HEAD, справочно — в вердикт не входит)")
    echo_b = files[("echo-x8", "B")]
    for sid in ("q1c_client_time_education", "q1c_client_time_phone_repair", "q1c_client_time_history_phone_repair"):
        def stat(J):
            rs = T.runs(J, lambda y: y["id"] == sid)
            return (sum(1 for _, r in rs if r["verdicts"].get("not_mentions:15:00") is False), sum(1 for _, r in rs if T.all_ok(r)),
                    sum(1 for _, r in rs if called(r)), len(rs))
        sb, sa, sB = stat(B["q1"]), stat(A["q1"]), stat(echo_b)
        print(f"  {sid:40} эхо до {sb[0]}/{sb[3]} после {sa[0]}/{sa[3]} B {sB[0]}/{sB[3]} · все проверки до {sb[1]} после {sa[1]} B {sB[1]}"
              f" · create_handoff до {sb[2]} после {sa[2]} B {sB[2]}")
    ws = {s: files[("ws-x10", s)] for s in ("before", "after", "B")}
    for sid in [x["id"] for x in (ws["after"] or {}).get("results", [])]:
        cells = []
        for s in ("before", "after", "B"):
            ok, n = T.count(ws[s], ids={sid})
            h = sum(1 for _, r in T.runs(ws[s], lambda y: y["id"] == sid) if called(r))
            lang = T.count(ws[s], ids={sid}, ok=T.lang_ok)[0] if "lang_kz" in EXPECT[sid] else None
            cells.append(f"{s} {ok}/{n} h{h}" + (f" lang{lang}" if lang is not None else ""))
        print(f"  {sid:40} " + " · ".join(cells))
    kb = [spurious(ws[s]) for s in ("before", "after", "B")]
    print(f"  ws-x10 лишний create_handoff (сценарии с no_tool): до {kb[0][0]}/{kb[0][1]} после {kb[1][0]}/{kb[1][1]} B {kb[2][0]}/{kb[2][1]}")

    print("\nДиагностика (не критерий): create_handoff на сценариях, где передача в замысле, но вызов не обязателен")
    for s in ("before", "after"):
        found = {}
        for blk in BLOCKS:
            for i, r in T.runs(files[(blk, s)], lambda y: y["id"] not in CHECKED and "tool_called" not in EXPECT[y["id"]]):
                if called(r):
                    found[i] = found.get(i, 0) + 1
        print(f"  {s}: " + (", ".join(f"{k} {v}" for k, v in sorted(found.items())) or "нет"))
    cost, nruns, errs, retries = {}, {}, {}, {}
    for (b, s), J in files.items():
        if J:
            sm = J["summary"]
            cost[s] = cost.get(s, 0) + (sm.get("cost_usd_auto_path") or 0)
            nruns[s] = nruns.get(s, 0) + (sm.get("runs") or 0)
            errs[s] = errs.get(s, 0) + (sm.get("error_runs") or 0)
            retries[s] = retries.get(s, 0) + (sm.get("transport_retries") or 0)
    print("  прогонов / ошибок / повторов транспорта / стоимость (путь автоответа): " +
          "; ".join(f"{s} {nruns[s]} / {errs[s]} / {retries[s]} / ${cost[s]:.3f}" for s in ("before", "after", "B") if s in nruns) +
          f"; всего ${sum(cost.values()):.3f}")

    print("\nИтог по критериям (A6 на staging — отдельно):")
    for k, v in verdict.items():
        print(f"  {k}: {'выполнен' if v else 'НЕ выполнен'}")


if __name__ == "__main__":
    main()

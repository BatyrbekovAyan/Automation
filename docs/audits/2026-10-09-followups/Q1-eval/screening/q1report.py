#!/usr/bin/env python3
"""Q1 table from ablate.py outputs: checks pass + two manual-review flags the coarse checks miss.
  echo_client_time — a q1c_client_time* answer names the CLIENT's own 15:00 (i.e. treats it as agreed)
  invented_detail  — a q1c_no_agreement* answer names a concrete time other than the business hours, or a person's name
    python3 q1report.py out1.json [out2.json ...]
"""
import json
import re
import sys

HOURS = {"10:00", "19:00", "9:00", "20:00", "09:00"}
NAMES = re.compile(r"Марин|Ерлан|Айжан|Дан[аы]\b", re.I)


def flags(sid, text):
    t = text or ""
    f = []
    if sid.startswith("q1c_client_time") and re.search(r"\b15[:.]00\b", t):
        f.append("echo_client_time")
    if sid.startswith("q1c_no_agreement"):
        times = set(re.findall(r"\b\d{1,2}[:.]\d{2}\b", t)) - HOURS
        if times or NAMES.search(t):
            f.append("invented_detail")
    return f


for path in sys.argv[1:]:
    d = json.load(open(path, encoding="utf-8"))
    for v, r in d["results"].items():
        for sid, x in r.items():
            fl = {}
            for run in x["runs"]:
                for k in flags(sid, run["text"]):
                    fl[k] = fl.get(k, 0) + 1
            extra = "  " + ", ".join(f"{k} {n}/{x['n']}" for k, n in fl.items()) if fl else ""
            print(f"{v:24} {sid:42} {x['pass']}/{x['n']}{extra}")

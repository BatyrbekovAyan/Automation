#!/usr/bin/env python3
"""Run e2e-auth's retired_bot_reconnects_after_purchase on the staging stand and SAVE every clone execution it reads (the clones
— and with them their executions — are deleted at the end of the run). e2e-auth.py itself is not changed: it is imported as a
module and its clone_executions() is wrapped. Writes <out>/exec-<n>-<id>.json (the execution with its data) per execution.

    python3 docs/audits/2026-10-29-price-tool/e2e-dump.py <out dir>
"""
import importlib.util
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
E2E = os.path.join(HERE, "..", "..", "..", "Tools", "n8n", "staging", "e2e-auth.py")
out_dir = os.path.abspath(sys.argv[1])
os.makedirs(out_dir, exist_ok=True)

spec = importlib.util.spec_from_file_location("e2e_auth", E2E)
E = importlib.util.module_from_spec(spec)
spec.loader.exec_module(E)

_seen = [0]
_real = E.clone_executions


def dumping(*args, **kwargs):
    exs = _real(*args, **kwargs)
    for ex in exs:
        _seen[0] += 1
        path = os.path.join(out_dir, f"exec-{_seen[0]:02d}-{ex.get('id')}.json")
        with open(path, "w", encoding="utf-8") as fh:
            json.dump(ex, fh, ensure_ascii=False)
    return exs


E.clone_executions = dumping
sys.argv = [E2E, "--only", "retired_bot_reconnects_after_purchase"]
E.main()

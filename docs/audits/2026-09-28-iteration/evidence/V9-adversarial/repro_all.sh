#!/bin/sh
# V9 adversarial repro: runs the REAL canonical «Reconcile Prices» body (crPriceSearch001) and the REAL Suggest Replies
# Prep/Assemble over the attack rows. Read-only, no network.  sh repro_all.sh
cd "$(dirname "$0")"
for f in cases_fp cases_fp2 cases_acc cases_tail cases_unit cases_fn cases_inj; do python3 $f.py; node run.js $f.json > out_$f.json; done
python3 - <<'PY'
import json
for f in ['cases_fp','cases_fp2','cases_acc','cases_tail','cases_unit','cases_fn','cases_inj']:
    for r in json.load(open(f'out_{f}.json')):
        lv = ','.join(r['level']) or 'none'
        print(f"{f:10s} {r['id']:34s} got={lv:22s} expect={r['expect']}")
PY
python3 panel_probe.py | sed -n '/sized/,$p'; python3 panel_probe3.py | head -1; node fuzz.js

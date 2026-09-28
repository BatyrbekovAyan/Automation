#!/usr/bin/env python3
"""Retired one-shot migrations must not rewrite the canonical workflows (2026-09-28).

apply-rag-fixes (07-01), apply-upload-hardening (07-02), apply-message-batching (Phase 10) and
apply-delete-history (09-01) each OWN nodes that later generators rewrote (A1 gate, stage 3 reply
pipeline, stage 4 history/handoff). Re-running one of them silently reverts those fixes — the WhatsApp
memory key back to `from`, Latest+Combine without the owner rule and without the Record Suppressed edge,
the Delete File gate bypass, the stage-4 privacy deletes — and apply-delete-history --deploy also PUTs
straight to prod. The README even told readers to re-run apply-message-batching after a template edit.

Each script is run for real here; the canonical folder is snapshotted and restored whatever happens, and
apply-delete-history is pointed at a dead local port so no run of this test can ever reach prod.
"""
import os
import subprocess
import sys

from harness import Case, report

HERE = os.path.dirname(os.path.abspath(__file__))
N8N = os.path.dirname(HERE)
WF = os.path.join(N8N, 'workflows')

RETIRED = [
    ('apply-rag-fixes.py', []),
    ('apply-upload-hardening.py', []),
    ('apply-message-batching.py', []),
    ('apply-delete-history.py', ['--deploy']),
    ('apply-delete-history.py', ['--check']),
]


def snapshot():
    return {f: open(os.path.join(WF, f), 'rb').read() for f in sorted(os.listdir(WF)) if f.endswith('.json')}


def restore(snap):
    for f, data in snap.items():
        p = os.path.join(WF, f)
        if open(p, 'rb').read() != data:
            open(p, 'wb').write(data)


env = dict(os.environ, N8N_BASE_URL='http://127.0.0.1:9', N8N_API_KEY='test-only-never-valid')
for script, args in RETIRED:
    with Case(f'legacy: {script} {" ".join(args)} refuses and leaves the canonical workflows untouched') as c:
        before = snapshot()
        try:
            p = subprocess.run([sys.executable, os.path.join(N8N, script), *args], cwd=N8N, env=env,
                               capture_output=True, text=True, timeout=120)
            changed = [f for f, data in snapshot().items() if before.get(f) != data]
            out = (p.stdout + p.stderr).strip()
        finally:
            restore(before)
        c.check(not changed, f'rewrote {changed}')
        c.check(p.returncode != 0, f'exit {p.returncode}: {out[-300:]}')
        c.check('RETIRED' in out, f'no RETIRED notice: {out[-300:]}')
        c.check('would change canonical' not in out, out[-300:])

report()

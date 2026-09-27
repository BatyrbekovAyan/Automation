#!/usr/bin/env python3
"""Run every Tools/n8n/tests/test_*.py (behavioural regressions, iteration 2026-09-27). Exit 1 if any fails."""
import subprocess
import sys
from pathlib import Path

here = Path(__file__).resolve().parent
failed = []
for t in sorted(here.glob('test_*.py')):
    r = subprocess.run([sys.executable, str(t)], capture_output=True, text=True)
    tail = [l for l in r.stdout.splitlines() if l.strip()][-1:] or ['(no output)']
    print(f"{'OK  ' if r.returncode == 0 else 'FAIL'} {t.name}: {tail[0]}")
    if r.returncode != 0:
        failed.append(t.name)
        print('\n'.join('      ' + l for l in (r.stdout + r.stderr).splitlines()[-25:]))
sys.exit(1 if failed else 0)

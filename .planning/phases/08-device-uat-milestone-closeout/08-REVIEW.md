---
phase: 08-device-uat-milestone-closeout
reviewed: 2026-07-15T09:49:26Z
depth: standard
files_reviewed: 2
files_reviewed_list:
  - Tools/n8n/build-suggest-replies.py
  - Tools/n8n/verify-telegram-parity.py
findings:
  critical: 0
  warning: 0
  info: 2
  total: 2
status: clean
---

# Phase 08: Code Review Report

**Reviewed:** 2026-07-15T09:49:26Z
**Depth:** standard
**Files Reviewed:** 2
**Status:** clean

## Summary

Reviewed the additive, backward-compatible delta of Phase 08-02 Task 1 (commit `32ebdf8`, parent `8f63fc3`) across two owner-run n8n deploy-tooling scripts. Both changes are minimal, correct, well-documented, and preserve the contracts the scope note called out.

**`verify-telegram-parity.py`** — adds an optional `--dir PATH` override for the workflow directory (default = committed `workflows/`). The exit-code contract is intact and verified by running the script:
- no args → exit 0, "ALL PARITY ASSERTS PASSED" (byte-identical to before)
- `--dir <valid workflows dir>` → exit 0 (same asserts)
- `--dir <missing dir>` → exit 1, "PARITY FAIL: unexpected structural error: [Errno 2]..." (`FileNotFoundError` is an `OSError` subclass, caught by the existing handler — correct fail-closed behavior for a go/no-go gate)

The `--dir` value flows only into `os.path.join(WF, fname)` where `fname` is a hardcoded constant; there is no shell, `eval`, or glob, and no privilege boundary is crossed. No path/argument-injection surface.

**`build-suggest-replies.py`** — adds OpenAI + Supabase credential-id overrides via env (`N8N_OPENAI_CRED_ID` / `N8N_SUPABASE_CRED_ID`) and flags (`--openai-cred` / `--supabase-cred`). The precedence chain is correct: `flag > env > SQLite-by-name > pinned DEV fallback`. Verified logic:
- `oa_cred = args.openai_cred or os.environ.get("N8N_OPENAI_CRED_ID")` correctly makes the flag win over env.
- The `if oa_cred:` / `if sb_cred:` guards mean an absent OR empty-string value never populates `CRED_OVERRIDES`, so the dev path stays byte-identical.
- In `resolve_cred`, `if override:` short-circuits the SQLite lookup and returns the operator-supplied id verbatim — the "fail loudly, never guess" `SystemExit` (name-not-found) still fires on the no-override dev/SQLite path, and `deploy()` still fails loudly on any non-2xx HTTP from PUT/POST/activate.
- `CRED_OVERRIDES` is populated in `main()` before `deploy()` → `workflow_payload` → `rag_nodes`/`llm_node` → `resolve_cred`, so ordering is sound; on the `--export` path `resolve_cred` is never called, so overrides are correctly inert.

No changes touch the workflow graph, validation logic, move enum, JS node blocks, or any committed workflow JSON. `.claude/rules/networking.md` scopes only Unity `.cs` files and does not govern these Python scripts; regardless, no network-call code was modified.

No critical or warning findings. Two non-blocking informational notes below — both are on-point to the concerns the scope raised, and neither requires a code change.

## Info

### IN-01: `--dir` argparse makes malformed invocations exit 2 (new; strengthens the go/no-go gate)

**File:** `Tools/n8n/verify-telegram-parity.py:219-232`
**Issue:** Before this change the script parsed no arguments, so any stray CLI token was silently ignored and the run still exited 0/1. With argparse now present, a malformed invocation (unknown flag or stray positional, e.g. a typo'd `--dr prod-export/`) exits **2** with "unrecognized arguments" instead of running. Verified: `verify-telegram-parity.py junkarg` → exit 2.

This does NOT break the documented exit-0 "ALL PARITY ASSERTS PASSED" / exit-1 "PARITY FAIL" contract — the no-arg path and the valid-`--dir` path both still exit 0, and real assert failures / missing dirs still exit 1. It is a behavior delta only for undocumented usage, it is fail-closed (a CI wrapper treating any non-zero as no-go stays safe), and it is arguably an improvement: a mistyped `--dir` flag now errors loudly rather than silently verifying the committed dir and printing a false "PASSED".

**Fix:** None required — informational. If a downstream wrapper distinguishes exit 1 specifically from other non-zero codes, be aware exit 2 now signals malformed CLI input. No change recommended.

### IN-02: Credential-id override is bound verbatim with no existence check on the target (inherent to the no-SQLite Cloud path)

**File:** `Tools/n8n/build-suggest-replies.py:86-92`
**Issue:** When `CRED_OVERRIDES` is set, `resolve_cred` returns `(override, want_name)` and skips the SQLite lookup — by design. Because the whole reason for the feature is that a Cloud target has no local SQLite to validate against, a typo'd or stale override id is used verbatim, producing a workflow whose `credentials` block references a credential id that may not exist on the target. Depending on the n8n version, an unknown cred id can be accepted at PUT/activate time and only surface as a failure at first execution (a silent-until-runtime wrong/dangling binding). This is the honest answer to the scope's "could an override silently bind the wrong account" question: yes, an operator-supplied wrong id binds the wrong (or a dangling) credential, and the script cannot detect it — n8n's public API does not reliably expose credential reads for validation. The docstring and inline comments already document this tradeoff, and the alternative (silent first-match) would be worse.

Separately (cosmetic): the override keeps the pinned DEV `want_name` as the display label even for a prod credential. n8n binds by id, so the label mismatch is harmless, and it is explicitly commented as intended.

**Fix:** None required for this additive change — the behavior is by-design and documented. Residual-risk mitigation for the operator (out of scope, no code change): double-check the recreated-by-name id against the Cloud instance before running prod replication, and confirm the post-deploy `activate` step returns 200 (which `deploy()` already gates on). If a lightweight guard is ever wanted, `deploy()` could log the resolved `(id, name)` pair per credential so the bound ids are visible in the deploy output.

---

_Reviewed: 2026-07-15T09:49:26Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_

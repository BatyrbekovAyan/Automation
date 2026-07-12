# Phase 3 — Human UAT Gate: tapi Live-Shape Capture

**Status:** OPEN (owner-run) — this gate CLOSES the phase.

Phase 3 is **code-complete** once the tooling below is built and green (that part
is done in plan 03-01). The phase **closes** only after the owner runs the
read-only capture against an authorized dev Telegram profile and records the
verdicts. Running the capture is **not** a task in the plan — it is this human
gate, because `secrets.json` is deny-ruled for Claude.

> **Blocks Phase 5.** The Telegram media / Normalize work (**CHAT-03**, **CHAT-07**,
> and the `type:"text"` mapping) cannot be built correctly until these verdicts
> are recorded — the tapi media message shape is undocumented today.

## Checklist (owner completes)

- [ ] An **authorized dev Telegram profile** exists (authorize one in-app first if
      not — Settings → Telegram auth; use a dev account).
- [ ] Ran `Tools/tapi/capture-shapes.sh` (optionally `--profile` / `--chats`).
- [ ] Samples are present in `Tools/tapi/samples/` **with `INDEX.json`** (gitignored
      — confirm they are NOT staged for commit).
- [ ] Coverage sanity: samples cover chats (all 3 list endpoints:
      `chats_get` / `chats_filter` / `chats_days_get`), `messages/get` across each
      distinct media `type` encountered, and a reply via `messages/id/get`.
- [ ] All **13 `SHAPES.md` verdicts** are set — `confirmed shape` / `divergence` /
      `not-observed` for Q1–Q8, or left `DEFERRED` (with reason) for Q9–Q13.
- [ ] The **Reactions-receive go/no-go** decision is recorded in `SHAPES.md`
      (GO → build receive-side reactions in Phase 5; NO-GO → v2 `TG-REACT-RECV`).

## When all boxes are ticked

Phase 3 is closed. The recorded verdicts (especially Q1/Q2 media shapes, Q3
reactions transport, Q4 groupness, Q5/Q6 dialog fields) become the ground truth
for the Phase-5 channel-aware parser and media pipeline.

---
*Gate for Phase 03 (tapi Live-Shape Capture). Do NOT tick these on the owner's
behalf — this is a live-account, human-run verification.*

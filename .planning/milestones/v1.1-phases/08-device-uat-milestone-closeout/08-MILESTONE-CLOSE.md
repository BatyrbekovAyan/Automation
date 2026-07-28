# Phase 8 — Milestone Close: v1.1 Telegram Parity (gated checklist, owner-run)

**Status:** OPEN (owner-run) — this is close **PREP**. Writing this checklist was autonomous;
CONFIRMING the two gates and RUNNING the close is the owner gate (plan 08-03 Task 2).

> **This checklist does NOT flip the milestone.** It gates the close on the two owner sign-offs,
> enumerates exactly what moves Active→Validated, and rolls every deliberately-deferred item forward
> so nothing drops at the milestone boundary. The owner runs `/gsd-complete-milestone` after both
> gates are green — **that command DOES the flip** (PROJECT.md evolution, ROADMAP reorg, archival,
> git tag). Do NOT perform those steps by hand, and do NOT tick any box on the owner's behalf.

v1.1 Telegram Parity shipped six code-complete phases (3–7 feature work + this closeout phase). The
Telegram surface is at WhatsApp parity in code and green on the EditMode suite (1028/1028), but two
owner-run confirmations remain before the milestone can close correctly: an on-device end-to-end pass
(`08-DEVICE-UAT.md`) and a one-shot prod replication (`08-PROD-REPLICATION.md`). Closing v1.1 means
(a) confirming those two sign-offs, (b) running the canonical close mechanics in the right order, and
(c) rolling the carried items forward explicitly. A written, gated checklist prevents a premature or
lossy close.

---

## 1. Blocking gates — both must be dispositioned before the close runs

Both gates are **owner-run**. The close does not proceed until each is either PASS or explicitly
deferred with a recorded reason. The two runbooks live in this same directory.

### ☐ Gate A — Device UAT (`08-DEVICE-UAT.md` Overall = PASS)

- **Green condition:** `08-DEVICE-UAT.md` **Overall = PASS** — every scored item PASS/N·A, **or**
  every **FAIL** filed as its own gap-closure plan (`/gsd-plan-phase 08 --gaps` from the Defects
  table) **and** every carried-v1.0 Group-I item run **or** re-deferred with a one-line reason.
- **Why it gates:** that runbook is the single source of truth for "is v1.1 shippable." It
  consolidates every still-open device gate across Phases 3–7 (auth/2FA, chat + media incl. the
  05-07/08 `.tgs`/кружок/GIF treatments, the 05-09 field/UI fixes, the vthumb id-ambiguity probe,
  the switcher, the auto-reply e2e, and live «Вместе» + dashboard) plus the carried v1.0 device UAT.
- **Owner-run:** the phase stays `human_needed` until the owner records results in `08-DEVICE-UAT.md`.

### ☐ Gate B — Prod replication (`08-PROD-REPLICATION.md` executed GREEN **or** explicitly deferred)

- **Green condition (executed):** `08-PROD-REPLICATION.md` **Overall = PASS** with the Step-7
  post-import **go/no-go GREEN** (`verify-telegram-parity.py --dir` prints `ALL PARITY ASSERTS
  PASSED` against the prod export) and prod confirmed **DORMANT** (no bot clone created/activated;
  both bot templates INACTIVE).
- **Deferred condition:** the owner may **explicitly defer** the prod copy (postpone the one-shot
  bulk copy) with a **recorded reason**. A deferral is a valid disposition — it rolls forward via
  §4 below and marks **PROD-01** carried rather than Validated (see §3).
- **Owner-run:** prod `secrets.json` + the prod n8n API key are deny-ruled from Claude and prod is
  live infra — the deploy itself is owner-only.

> **Neither gate can be ticked here by Claude.** This checklist cannot proceed to §5 (close mechanics)
> until BOTH gates above are dispositioned by the owner. The header-auth hardening flagged in
> `08-PROD-REPLICATION.md` Step 8 is a **pre-real-traffic** item, **not** a Gate-B blocker (§4).

---

## 2. Pre-close artifact-audit disposition

`/gsd-complete-milestone` runs a comprehensive **`audit-open`** sweep **first**, before any PROJECT.md
or ROADMAP edits. The still-open UAT gate docs **WILL** surface in that audit:

- `04-HUMAN-UAT.md`, `05-HUMAN-UAT.md`, `06-HUMAN-UAT.md`, `07-HUMAN-UAT.md` (open owner gates)
- `05-VERIFICATION.md` and `01-VERIFICATION.md` (`human_needed` device confirmations)

**Disposition rule (state this when the audit prompts [R]esolve / [A]cknowledge / [C]ancel):**

1. **Resolve-by-consolidation:** each of those surfaces is **subsumed by `08-DEVICE-UAT.md`** — the
   consolidated runbook aggregates them item-for-item. Mark each surface **resolved** once
   `08-DEVICE-UAT.md` records its **PASS** for that surface (Gate A). They are not independent open
   gates once Gate A is green; they are the sources Gate A rolls up.
2. **Acknowledge-and-defer the residual:** any residual **live-server-only** item that Gate A could
   not exercise on device (e.g. a scenario re-deferred in `08-DEVICE-UAT.md` Group I, or a
   dashboard/RAG check that needs seeded prod data) → choose **[A] Acknowledge** rather than blocking
   the close. The acknowledge path writes each item to **STATE.md → `## Deferred Items`** (sanitized)
   and records the **count** in the new **MILESTONES.md** entry (`Known deferred items at close: N`).

**Do not choose [R]esolve to hand-patch these** — they are owner-run/live-only by nature. Green Gate A
+ acknowledge-and-defer the residual is the correct path; `[C] Cancel` only if Gate A is not yet green.

---

## 3. Active → Validated (PROJECT.md `## Requirements`)

On close, `/gsd-complete-milestone`'s PROJECT.md evolution review moves the shipped v1.1 **Active**
bullets to **Validated** (format `- ✓ [Requirement] — v1.1`). Enumerated so nothing is missed:

| Active bullet (PROJECT.md) | REQ-IDs (all Complete in REQUIREMENTS.md) | Phase | → Validated on close |
|----------------------------|--------------------------------------------|-------|----------------------|
| Telegram chat client at parity (list, messages, media, send, quoted replies, reactions-send on tapi) | CHAT-01 … CHAT-11 | 5 | ✓ v1.1 |
| In-screen per-bot channel switcher (WhatsApp\|Telegram) | SWITCH-01 … SWITCH-04 | 6 | ✓ v1.1 |
| Telegram bots actually converse (Telegram_Bot template on tapi bases) | TPL-01 … TPL-06 | 4 | ✓ v1.1 |
| «Вместе» suggestions work in Telegram chats | SUGG-01, SUGG-02 | 7 | ✓ v1.1 |
| Telegram 2FA accounts can authorize | TGAUTH-01 | 5 | ✓ v1.1 |
| Dashboard «Сводка» counts and lists Telegram conversations | DASH-01, DASH-02, DASH-03 | 7 | ✓ v1.1 |
| tapi live-shape verification (user-assisted capture) | VER-01, VER-02 | 3 | ✓ v1.1 |

**Carried-from-v1.0 Active bullets — dispositioned at close:**

- **Detailed device UAT** (v1.0 deferred items, folded into this closeout phase) → **Validated once
  Gate A = PASS** (the on-device pass is exactly what closes it); if Gate A carries any Group-I
  scenario as RE-DEFER, that scenario rolls forward via §4 while the bullet itself validates.
- **Prod bagkz replication (PROD-01)** → mark **PROD-01 Validated IF Gate B was executed GREEN**;
  **else carry it forward** (Gate B deferred) — PROD-01 stays an open carried item per §4.
- **Server-side «Вместе» suppression (SUPPRESS-01)** → **stays Active / carried** (NOT validated this
  milestone) — rolls forward to v1.2 Phase 9 per §4.

> The 29 v1.1 REQ-IDs are already `Complete` in REQUIREMENTS.md's traceability table; this section is
> the PROJECT.md-side move the close performs, not a re-verification.

---

## 4. Carried-forward — roll forward explicitly (do NOT drop at the boundary)

Every deferred/carried item is named here so the milestone boundary never silently loses one:

- **SUPPRESS-01 — server-side «Вместе» suppression** → **v1.2 Phase 9** (already on the ROADMAP as
  "Phase 9: Semi-Auto Suppression Flag" under the 📋 v1.2 Semi-Auto Suppression milestone). **Confirm
  it survives the ROADMAP reorg** the close performs — the milestone-grouping rewrite must keep the
  v1.2 / Phase 9 row and its `docs/superpowers/plans/2026-07-13-semi-auto-suppression-flag.md`
  pointer intact.
- **Re-deferred v1.0 UAT scenarios** — any `RE-DEFER` verdict in `08-DEVICE-UAT.md` **Group I**
  (Phase 01/02 `01-HUMAN-UAT.md` / `02-HUMAN-UAT.md` scenarios + the `01-VERIFICATION.md` device
  confirmation) → **STATE.md `## Deferred Items`** with its one-line reason (the acknowledge path in
  §2 writes these). These are already tracked there from the v1.0 close as `partial → Phase 8`; a
  re-defer updates the reason and pushes them past v1.1.
- **PROD-01 — prod bagkz replication** → carried **only if Gate B was deferred** (else Validated per
  §3). If carried, keep it as an open milestone item (rolls to the next prod-copy window).
- **v2 polish backlog** (stays in backlog, not scheduled): real `.tgs` Lottie native animation
  (currently a sized placeholder card), incoming-reaction chat-list preview ("X reacted…" on the
  Telegram list row — transport-inherent gap, IN-04/IN-05), and a per-channel «Вместе» default. Plus
  the standing v1.0 deferrals **FB-01** (thumbs-up/down ranking), **FB-02** (per-chat/per-bot
  suggestion analytics), and **POL-01** (streaming/animated suggestion reveal).
- **Webhook header-auth hardening** (carried **R-02-01** + the dashboard pre-prod note; flagged in
  `08-PROD-REPLICATION.md` Step 8) → carry as a **pre-real-prod-traffic** item. It is NOT a copy
  blocker (prod stays dormant, zero live traffic this phase); add header-auth to the
  Dashboard/Suggest/Upload/Delete webhook family before real prod traffic begins.

---

## 5. Close mechanics — owner runs after BOTH gates are green

Once Gate A = PASS and Gate B = PASS/DEFERRED, the owner runs the canonical close command. **Do NOT
duplicate or hand-run the mechanics** — the command performs them in the correct order with archival
safety commits. Full spec: `.claude/get-shit-done/workflows/complete-milestone.md`.

```
/clear
/gsd-complete-milestone      # v1.1 "Telegram Parity"
```

What it does (summary only — the workflow file is authoritative):

1. **Pre-close artifact audit** (`audit-open`) — surfaces the open gate docs; disposition per §2.
2. **PROJECT.md full evolution review** — "What This Is" / Core Value check + **Active→Validated**
   moves per §3 + Key Decisions outcomes + Context/`Last updated` refresh.
3. **ROADMAP.md reorg** — milestone-grouping rewrite (v1.1 phases collapsed under a shipped
   `<details>` block), **Backlog section preserved**, and the v1.2 / Phase 9 row kept intact (§4).
4. **Archival** — `gsd-sdk query milestone.complete "v1.1" --name "Telegram Parity"` extracts
   `milestones/v1.1-ROADMAP.md` + `milestones/v1.1-REQUIREMENTS.md`, appends the **MILESTONES.md**
   entry (with the deferred-items count from §2), safety-commits the archives, then `git rm`
   REQUIREMENTS.md (fresh for v1.2). Optionally archives the phase directories.
5. **RETROSPECTIVE.md** — appends the v1.1 milestone section + cross-milestone trends.
6. **Git tag `v1.1`** — annotated release tag (owner is prompted whether to push).

After the command finishes: verify **SUPPRESS-01** survived into v1.2 Phase 9 and any re-deferred
v1.0 UAT scenario landed in STATE.md Deferred Items (§4).

---

## 6. Owner result

Fill in once the gates are dispositioned and the close has run. **Every box ships blank.**

- ☐ **Gate A — Device UAT PASS** (`08-DEVICE-UAT.md` Overall = PASS, or all FAILs filed as gap-closure plans)
- ☐ **Gate B — Prod replication** ☐ PASS (executed GREEN) ☐ DEFERRED — reason: __________
- ☐ **Pre-close audit dispositioned** (open gates resolved-by-consolidation; residual acknowledged → STATE Deferred Items)
- ☐ **`/gsd-complete-milestone` run** (PROJECT.md Active→Validated, ROADMAP reorg, archival, retrospective)
- ☐ **v1.1 tagged** (annotated `v1.1` release tag created)
- ☐ **SUPPRESS-01 confirmed rolled forward** to v1.2 Phase 9; re-deferred v1.0 UAT (if any) in STATE Deferred Items

**Notes:** __________

---

*Gated milestone-close checklist for v1.1 Telegram Parity. Gates the close on `08-DEVICE-UAT.md`
(Gate A) + `08-PROD-REPLICATION.md` (Gate B), both owner-run and in this same directory. Points at
`/gsd-complete-milestone` (`.claude/get-shit-done/workflows/complete-milestone.md`) for the mechanics —
NOT duplicated here. Rolls SUPPRESS-01 → v1.2 Phase 9, re-deferred v1.0 UAT → STATE Deferred Items,
v2 polish + FB-01/FB-02/POL-01 → backlog, and header-auth → pre-real-traffic. Owner-run — this doc
does not flip the milestone; do NOT tick on the owner's behalf.*

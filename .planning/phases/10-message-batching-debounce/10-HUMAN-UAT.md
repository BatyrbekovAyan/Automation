# Phase 10 — Human UAT Gate: Message Batching / Debounce (BATCH-01 / BATCH-02 / BATCH-03 live proof)

**Status:** PARTIAL (2026-07-22) — scenarios 1–3 **PASS** (auto-combine half, both channels); scenario 4 **BLOCKED** by open Phase-9 gate 09-04 (SetReplyMode 404 — BATCH-03 stays EditMode-covered); scenario 5 **DEFERRED** to post-Phase-9 by owner decision. Plan closed with this debt tracked per the owner's explicit continue-decision.

## What this gate proves

The phase-closing behavioral gate. The structural + `runData` proof already passed
(10-03: two fragments → one combined reply, id-equality True on every winner, fresh
clones inherit the debounce; 10-02: the client `IncomingDebounceGate` is EditMode-green
at 1197/1197). This gate is the **only** verification Claude cannot run — it needs the
**10-02 client debounce in a fresh device build**, the **live dev n8n templates** (10-03),
a **tunnel**, and **real WhatsApp + Telegram profiles**. It confirms the user-observable
result a customer + owner would experience:

- **Auto-combine half (BATCH-01/02):** 2+ text фрагмента to an «Авто» bot → exactly **ONE**
  combined reply on **BOTH** channels; a single complete message still gets one reply.
- **Suggestions-coalesce half (BATCH-03):** in a «Вместе» chat, rapid incoming фрагменты
  refresh the suggestion cards **ONCE** (коалесцированно), while manual refresh and
  card-pick still respond **immediately**.
- **Composition guarantee:** a semi-auto chat still **skips the whole reply path** (the
  Phase-9 suppression gate runs BEFORE the debounce — no wait, no reply).

## Accepted-cost note — READ FIRST (so a slow reply is not logged as a defect)

- The debounce adds **~8s** window latency to **EVERY** auto-reply, even a single complete
  message. This is the **accepted design cost**, not a bug. Do **not** log "reply was slow"
  as a FAIL — only log the shape of the result (one vs many, combined vs first-only).
- The humanizer pauses (Pause Before Reading / Reading Pause / Typing Pause) are
  **UNCHANGED** by this phase. Scenario 2 confirms they still feel natural.
- The client suggestions window is **~2.5s** of incoming silence before the ONE coalesced
  request fires. Manual refresh + card-pick are **never** delayed by it.

---

## Pre-flight (owner — before the test window)

1. **Fresh device build carrying the 10-02 client debounce.** The `IncomingDebounceGate`
   (10-02) is **NOT in any existing build yet** — you must make a **new build**. Android is
   the primary target, iOS secondary. The Unity Editor is already open on this Mac.
2. **Rotate the tunnel BEFORE building.** The quick-tunnel URL is baked into the build's
   `secrets.json` at BUILD time. Run **`python3 Tools/n8n/rotate-tunnel.py`** (refreshes
   `secrets.json` to the current `cloudflared` URL) **first**, then build — otherwise device
   n8n calls fail with **error -1003**. (`secrets.json` is deny-ruled for Claude, so this is
   owner-run.)
3. **Dev n8n up** at `localhost:5678` + the **`cloudflared` tunnel up** (the same URL you
   just baked into the build).
4. **Both bot templates redeployed with the debounce** — already confirmed live in 10-03
   (`4wYitz5ek30SVNlT` WhatsApp / `4VN3gsFaC2HUYmcc` Telegram, all 4 debounce nodes after
   `Suppressed?`, `amount=8`). No redeploy needed unless you re-imported since.
5. **`Suggest Replies` workflow active** — `9PTyYcelRQI7bGDb` (`/webhook/SuggestReplies`) is
   active on dev; it drives the «Вместе» cards. Confirm it is ON.
6. **Test bot reply-workflow clone ACTIVATED for the window.** All bot clones are currently
   `active=False` on dev (real-contacts discipline). Activate **only** the one test clone you
   will exercise, **only** for this window. **Deactivate it immediately after** (post-run
   block below).
7. **Real WhatsApp + Telegram profiles authed** on the test bot(s).

### Server-suppression prerequisite for Scenario 5 (read before scenario 5)

The app's in-chat «Вместе» toggle writes the server suppression flag via
**`/webhook/SetReplyMode`**, which is **NOT live on dev yet** — that deploy + its suppression
`runData` checks are **Phase-9's open 09-04/09-05 gates**. The `reply_mode_flags` table
**exists** on dev but is **EMPTY**, so the gate currently **fails open** (Авто replies
proceed for every chat regardless of the in-app toggle).

Therefore the **full** «Вместе»-mode end-to-end (app toggle → server suppression) belongs to
**09-04/09-05**, not this gate. For THIS phase's composition check (scenario 5) use the
**minimal SQL path** — insert a suppression row directly through the Chat Memory Postgres /
Supabase connection, observe the reply path dead-end at `Suppressed?`, then delete it:

```sql
-- suppress (before scenario 5): '*' = bot-wide default row for this profile
insert into public.reply_mode_flags (profile_id, chat_id, suppressed)
values ('<test-bot-profileId>', '*', true);

-- restore (after scenario 5)
delete from public.reply_mode_flags
where profile_id = '<test-bot-profileId>' and chat_id = '*';
```

Frame the verdict honestly: scenario 5 here proves **gate-before-debounce ordering** (the
suppressed chat skips the path), **not** the app-toggle round-trip.

---

## Scenarios (owner runs on-device; fill PASS/FAIL + notes)

### Scenario 1 — WhatsApp multi-fragment combine (BATCH-01/02)

- **Setup:** an «Авто»-mode WhatsApp bot; reply clone ACTIVE.
- **Do:** the customer sends «есть колодки» then «на камри 70?» **~1s apart** (two фрагмента).
- **EXPECT:** exactly **ONE** bot reply arrives (after ~8s), and it answers **BOTH** фрагмента
  (grounded in the combined text) — **not** two replies, **not** an answer to only the first
  фрагмент.
- **Result:** ☑ **PASS** (owner, 2026-07-22)
- **Notes:** one combined reply, both fragments answered.

### Scenario 2 — WhatsApp single message (latency / humanizer unchanged)

- **Setup:** same «Авто» WhatsApp bot.
- **Do:** the customer sends **one complete** message.
- **EXPECT:** exactly **one** reply after the window; the humanizer read/type pauses still
  feel **natural** (unchanged). The ~8s wait is expected — not a defect.
- **Result:** ☑ **PASS** (owner, 2026-07-22)
- **Notes (does single-message latency feel acceptable at 8s? if not, note a preferred value):** one reply after the window; pauses feel natural; 8s accepted.

### Scenario 3 — Telegram multi-fragment combine (BATCH-01/02)

- **Setup:** repeat scenario 1 on a **Telegram**-authed «Авто» bot; reply clone ACTIVE.
- **Do:** the customer sends two фрагмента ~1s apart.
- **EXPECT:** same as scenario 1 — exactly **ONE** combined reply on Telegram.
- **Result:** ☑ **PASS** (owner, 2026-07-22)
- **Notes:** one combined reply on Telegram.

### Scenario 4 — Suggestions coalesce in «Вместе» (BATCH-03) — BOTH channels

- **Setup:** open a **«Вместе»** chat. `Suggest Replies` (`9PTyYcelRQI7bGDb`) active. To
  observe the coalesce **cleanly** without the auto path also firing (the server gate fails
  open on the empty table), either run this with the **reply clone deactivated**, OR insert
  the scenario-5 SQL suppression row for this chat so the auto path dead-ends while
  suggestions still coalesce.
- **Do:** have the customer send **2–3 rapid фрагмента**.
- **EXPECT:** the suggestion cards refresh **ONCE** (after ~2.5s), grounded in the
  latest/combined incoming — **not** a flicker of N refreshes (коалесцированно, один раз).
- **Sub-check A (manual refresh immediate):** tap the **manual refresh** mid-burst → it
  responds **IMMEDIATELY** (not delayed by the debounce). ☐ not reached (blocked)
- **Sub-check B (card-pick immediate):** tap a **card** → it re-clusters **IMMEDIATELY** (not
  delayed by the debounce). ☐ not reached (blocked)
- **Do this on BOTH channels** (WhatsApp + Telegram).
- **Result (coalesce, WhatsApp):** ⛔ **BLOCKED by open Phase-9 gate 09-04** (owner, 2026-07-22)
- **Result (coalesce, Telegram):** ⛔ **BLOCKED by open Phase-9 gate 09-04** (owner, 2026-07-22)
- **Blocker evidence:** switching the toggle to Semi-auto errored on the server sync —
  `[SetReplyMode] [404] http://localhost:5678/webhook/SetReplyMode: The requested webhook
  "POST SetReplyMode" is not registered` (logged from `Manager/<SyncReplyModeRoutine>` at
  `Assets/Scripts/Main/Manager.ReplyModeSync.cs:105`). This is the **EXPECTED** consequence of
  `/webhook/SetReplyMode` not being deployed on dev (**09-04 Task 2, still open**) — **not a
  Phase-10 defect**. The behavioral coalesce observation was therefore not completed.
- **Automated coverage stands:** BATCH-03's client coalesce logic keeps full automated
  coverage — 10-02's **6 EditMode tests** (incl. the burst-then-chat-switch regression), full
  suite **1197/1197**. Only the on-device behavioral confirmation is outstanding; it re-runs
  trivially once 09-04 deploys `SetReplyMode`. (Side note, no action: the `localhost` URL means
  this ran on the Mac — Editor or iOS Simulator — which is legitimate for a client-side check.)
- **Notes (if ~2.5s feels off, note a preferred `WindowSeconds`):** not reached — blocked before observation.

### Scenario 5 — Composition sanity: semi-auto skips the path

- **Setup:** a chat suppressed via the **SQL row** above (`'*'` for the test bot profile), or —
  if you run 09-04 first — via the app's «Вместе» toggle. Reply clone ACTIVE.
- **Do:** the customer sends a **multi-fragment** message.
- **EXPECT:** **NO** auto-reply, and the chat **stays UNREAD** (the Phase-9 suppression gate
  runs BEFORE the debounce — the semi-auto chat skips the whole path, no wait), **while the
  suggestions panel still populates** (coalesced per scenario 4).
- **Sub-check (activation switch untouched):** confirm the **«Бот работает / Бот на паузе»**
  activation switch still pauses/resumes the bot **independently** of «Авто/Вместе» — it must
  be untouched by this phase.
- **Cleanup:** **delete** the SQL suppression row after.
- **Result:** ⏸ **DEFERRED to post-Phase-9** (owner decision, 2026-07-22) — owner will verify
  after Phase 9 finishes; asked to continue the phase now without this scenario and without
  additional setup. Not a defect; tracked as debt until re-verified.
- **Notes:** composition/ordering check re-runs alongside the 09-04/09-05 «Вместе» app-toggle e2e.

---

## Verdict table

| # | Scenario | Channel(s) | Expected | Result | Notes |
|---|----------|-----------|----------|--------|-------|
| 1 | Multi-fragment combine | WhatsApp | ONE combined reply | ☑ PASS | both fragments answered (2026-07-22) |
| 2 | Single message | WhatsApp | one reply, natural pauses | ☑ PASS | 8s accepted; pauses natural |
| 3 | Multi-fragment combine | Telegram | ONE combined reply | ☑ PASS | one combined reply on TG |
| 4 | Suggestions coalesce | WhatsApp + Telegram | ONE card refresh; manual/card immediate | ⛔ BLOCKED (09-04) | SetReplyMode 404; BATCH-03 EditMode-covered 1197/1197 |
| 5 | Semi-auto skips path | WhatsApp (SQL row) | no reply, stays unread, suggestions still populate, switch untouched | ⏸ DEFERRED | owner will verify post-Phase-9 |

---

## Post-run (owner — immediately after the window)

- [x] **DEACTIVATE** the test bot's reply-workflow clone (real-contacts constraint). — confirmed (owner, 2026-07-22)
- [x] **Delete** any `reply_mode_flags` row inserted for scenario 5. — confirmed deleted (owner, 2026-07-22)
- [x] Confirm **prod bagkz untouched** — it stays **DORMANT**; this phase folds into the
      future one-shot bulk copy (carry the debounce splice + the `binaryMode` orchestrator
      strip from 10-03).

---

## Final disposition

**Outcome (2026-07-22): PARTIAL — plan closed with tracked debt per explicit owner continue-decision.**

- **Scenarios 1–3 PASS** → the **auto-reply combine half (BATCH-01/BATCH-02)** is behaviorally
  confirmed on **both** channels (multi-fragment → ONE combined reply; single message → one
  reply; humanizer pauses unchanged).
- **Scenario 4 BLOCKED by 09-04** → the **suggestions-coalesce half (BATCH-03)** could not be
  observed on-device because `/webhook/SetReplyMode` is not deployed on dev (Phase-9 09-04
  Task 2, still open) — the toggle-to-Semi-auto sync 404'd. BATCH-03's client logic retains
  full automated coverage (10-02, 6 EditMode tests, suite 1197/1197). Re-runs trivially once
  09-04 deploys SetReplyMode.
- **Scenario 5 DEFERRED** → the composition/ordering check is deferred to **post-Phase-9** by
  explicit owner decision; owner will verify after Phase 9 finishes.
- **Owner authorization (2026-07-22):** owner asked to continue/close the phase now without
  scenarios 4–5 and without additional setup. This plan closes with scenarios 4 (blocked) and
  5 (deferred) tracked as debt so they surface in `/gsd-progress` and `/gsd-audit-uat` until
  re-verified alongside 09-04/09-05.
- **Reference:** a pure window-tuning change (8s auto / 2.5s client) re-enters via a small gap
  plan (`apply-message-batching.py`'s `Debounce Wait amount` + the client `WindowSeconds`),
  not this gate.

**Next step:** the auto-reply half is behaviorally proven; run **`/gsd-secure-phase 10`** to
secure the phase, with scenarios 4–5 carried as tracked UAT debt re-verified once 09-04/09-05
close.

---
*Gate for Phase 10 (message-batching-debounce). Do NOT tick these on the owner's behalf —
this is a live-account, human-run device verification. `secrets.json` / dev n8n / tunnel are
owner-run (deny-ruled for Claude). Full «Вместе» app-toggle end-to-end belongs to 09-04/09-05;
scenario 5 here proves gate-before-debounce ordering via the minimal SQL path.*

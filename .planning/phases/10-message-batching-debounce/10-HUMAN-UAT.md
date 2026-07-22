# Phase 10 — Human UAT Gate: Message Batching / Debounce (BATCH-01 / BATCH-02 / BATCH-03 live proof)

**Status:** OPEN — awaiting owner device run. Fill in each PASS/FAIL below, then report back per the resume signal.

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
- **Result:** ☐ PASS ☐ FAIL
- **Notes:**

### Scenario 2 — WhatsApp single message (latency / humanizer unchanged)

- **Setup:** same «Авто» WhatsApp bot.
- **Do:** the customer sends **one complete** message.
- **EXPECT:** exactly **one** reply after the window; the humanizer read/type pauses still
  feel **natural** (unchanged). The ~8s wait is expected — not a defect.
- **Result:** ☐ PASS ☐ FAIL
- **Notes (does single-message latency feel acceptable at 8s? if not, note a preferred value):**

### Scenario 3 — Telegram multi-fragment combine (BATCH-01/02)

- **Setup:** repeat scenario 1 on a **Telegram**-authed «Авто» bot; reply clone ACTIVE.
- **Do:** the customer sends two фрагмента ~1s apart.
- **EXPECT:** same as scenario 1 — exactly **ONE** combined reply on Telegram.
- **Result:** ☐ PASS ☐ FAIL
- **Notes:**

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
  responds **IMMEDIATELY** (not delayed by the debounce). ☐ PASS ☐ FAIL
- **Sub-check B (card-pick immediate):** tap a **card** → it re-clusters **IMMEDIATELY** (not
  delayed by the debounce). ☐ PASS ☐ FAIL
- **Do this on BOTH channels** (WhatsApp + Telegram).
- **Result (coalesce, WhatsApp):** ☐ PASS ☐ FAIL
- **Result (coalesce, Telegram):** ☐ PASS ☐ FAIL
- **Notes (if ~2.5s feels off, note a preferred `WindowSeconds`):**

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
- **Result:** ☐ PASS ☐ FAIL
- **Notes:**

---

## Verdict table

| # | Scenario | Channel(s) | Expected | Result | Notes |
|---|----------|-----------|----------|--------|-------|
| 1 | Multi-fragment combine | WhatsApp | ONE combined reply | ☐ | |
| 2 | Single message | WhatsApp | one reply, natural pauses | ☐ | |
| 3 | Multi-fragment combine | Telegram | ONE combined reply | ☐ | |
| 4 | Suggestions coalesce | WhatsApp + Telegram | ONE card refresh; manual/card immediate | ☐ | |
| 5 | Semi-auto skips path | WhatsApp (SQL row) | no reply, stays unread, suggestions still populate, switch untouched | ☐ | |

---

## Post-run (owner — immediately after the window)

- [ ] **DEACTIVATE** the test bot's reply-workflow clone (real-contacts constraint).
- [ ] **Delete** any `reply_mode_flags` row inserted for scenario 5.
- [ ] Confirm **prod bagkz untouched** — it stays **DORMANT**; this phase folds into the
      future one-shot bulk copy (carry the debounce splice + the `binaryMode` orchestrator
      strip from 10-03).

---

## Final disposition

- **ALL 5 PASS →** the batching feature is behaviorally complete → phase closes →
  run **`/gsd-secure-phase 10`**.
- **Any FAIL →** file a gap round with the failing scenario + observed behavior. A pure
  window-tuning change (8s auto / 2.5s client) re-enters via a small gap plan
  (`apply-message-batching.py`'s `Debounce Wait amount` + the client `WindowSeconds`), not
  this gate.

---
*Gate for Phase 10 (message-batching-debounce). Do NOT tick these on the owner's behalf —
this is a live-account, human-run device verification. `secrets.json` / dev n8n / tunnel are
owner-run (deny-ruled for Claude). Full «Вместе» app-toggle end-to-end belongs to 09-04/09-05;
scenario 5 here proves gate-before-debounce ordering via the minimal SQL path.*

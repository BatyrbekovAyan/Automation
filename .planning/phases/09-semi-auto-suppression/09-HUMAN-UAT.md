# Phase 9 — Human UAT Gate: Semi-Auto Suppression (SUP-03 / SUP-05 behavioral proof)

**Status:** OPEN (2026-07-22) — owner to run the 5-scenario e2e on one build across both channels. This is the phase-closing gate.

## What this gate proves

The phase-closing **behavioral** gate. The structural + `curl` + `runData` proof already
passed in **09-04** (`reply_mode_flags` live with default-deny RLS; `Set Reply Mode`
webhook `SCLcpn6DMDG3Z4VN` deployed + curl/precedence matrix green; a suppressed 1:1 chat
dead-ends at `Suppressed?` on **both** channels via `runData`; a fresh bot inherits the
gate). This gate is the **only** verification Claude cannot run — it needs the **09-03
client** in a **fresh device build**, the **live dev n8n templates + gate** (09-02/09-04),
the **`/webhook/SetReplyMode`** webhook (09-04), a **tunnel**, and **real WhatsApp +
Telegram profiles**. It confirms the user-observable behavior a customer + owner would
experience:

- **«Вместе» → the bot stands down:** no auto-reply, the chat stays **unread** (unread badge
  shows), **while the suggestions panel still populates** (the whole point).
- **«Авто» → replies restore.**
- **The bot-wide `'*'` default** suppresses a **never-opened** chat.
- **Identical on WhatsApp and Telegram** (the gate keys on the per-channel `profile_id`).
- **Absence → reply** (a never-toggled chat on an «Авто» bot is never silenced).
- **The «Бот работает / Бот на паузе» activation switch is untouched** and still pauses/resumes
  the bot independently of «Авто/Вместе».

## Read first — so expected behavior is not logged as a defect

- **«Вместе» = NO auto-reply, BUT the suggestions panel STILL populates.** That is the
  designed behavior, not a bug — the bot stands down so the owner answers via suggestions.
- **Absence of a flag row → the bot REPLIES.** Fail-open-on-absence is intentional (the
  never-toggled common case must never be silenced). Fail-**closed** applies only to a genuine
  Postgres read error, which is not owner-triggerable in this pass.
- **The «Авто/Вместе» toggle and the «Бот работает / Бот на паузе» activation switch are TWO
  DIFFERENT controls — keep them straight:**
  - **«Авто/Вместе»** (scenario 3 uses the per-**bot default** form of it, on the bots-list
    card; scenarios 1/2/4 use the per-**chat** form, inside the open chat) — this is the
    suppression toggle this phase wires.
  - **«Бот работает / Бот на паузе»** (scenario 5 sub-check) — this is the **real n8n
    activate/deactivate** activation switch. It must be **UNTOUCHED** by this phase and keep
    working on its own. Do **not** confuse the two.

---

## Pre-flight (owner — before the test window)

1. **Rotate the tunnel BEFORE building.** Run **`python3 Tools/n8n/rotate-tunnel.py`** FIRST,
   then build — the quick-tunnel URL is baked into `secrets.json` at **BUILD** time; a stale
   URL yields **error -1003** network failures on device. (`secrets.json` is deny-ruled for
   Claude, so this is owner-run.)
2. **Fresh device build carrying the 09-03 client changes** (`Manager.ReplyModeSync` + the
   bot-default / per-chat / re-assert-on-open toggle wiring). Build from **current `main`** so
   the flag is actually written on toggle — otherwise the in-app «Вместе» flip never reaches
   the server. Android is primary, iOS secondary. The Unity Editor is already open on this Mac.
3. **Dev n8n up** at `localhost:5678` + the **`cloudflared` tunnel up** (the same URL you just
   baked into the build).
4. **`Set Reply Mode` webhook LIVE** — id **`SCLcpn6DMDG3Z4VN`** (`/webhook/SetReplyMode`),
   shared always-active, Postgres cred **`vvRrFiEXzLVqKjOx`** (09-04). Confirm it is
   **registered** — flipping a chat to Semi-auto must **not** 404 (that 404 was the 10-04
   blocker before this webhook was deployed; it is deployed now).
5. **Both bot templates carry the fail-closed gate** — confirmed live in 09-04
   (`4wYitz5ek30SVNlT` WhatsApp / `4VN3gsFaC2HUYmcc` Telegram: `Read Reply Mode` → `Suppressed?`
   right after the group-chat `If`). **No redeploy needed** unless you re-imported / drifted
   since 09-04.
6. **`Suggest Replies` workflow active** — `9PTyYcelRQI7bGDb` (`/webhook/SuggestReplies`) drives
   the «Вместе» cards. Confirm it is **ON** (scenarios 1 & 4 need the panel to populate).
7. **Test bot reply-workflow clone ACTIVATED for the window only.** All bot clones are
   `active=False` on dev (real-contacts discipline). Activate **only** the one test clone you
   will exercise, **only** for this window. **Deactivate it immediately after** (post-run block).
   Confirm the **09-04** test clones were already deactivated (per 09-04 they were).
8. **Real WhatsApp + Telegram profiles authed** on the test bot(s).

---

## Scenarios (owner runs on-device; fill PASS/FAIL + notes)

### Scenario 1 — WhatsApp suppress (SUP-03)

- **Setup:** an authed WhatsApp bot with its reply clone **ACTIVE**; open a specific chat.
- **Do:** flip **that chat** to **«Вместе»** (the per-chat toggle inside the open chat) → have
  the customer send a message.
- **EXPECT:** the bot does **NOT** auto-reply; the chat stays **unread** (the unread badge
  shows / непрочитанное); **and the suggestions panel still populates** in that chat.
- **Sub-check (re-assert heal):** **close and re-open** the chat → it is **still «Вместе» /
  suppressed** (re-assert-on-open re-writes the ON flag, so a lost "back to Авто" write
  self-heals on the next open).
- **Result:** ☐ PASS ☐ FAIL
- **Notes:**

### Scenario 2 — WhatsApp restore

- **Setup:** the same chat from scenario 1.
- **Do:** flip the chat back to **«Авто»** → the customer sends again.
- **EXPECT:** the bot **auto-replies normally**.
- **Result:** ☐ PASS ☐ FAIL
- **Notes:**

### Scenario 3 — Bot-wide `'*'` default suppresses a never-opened chat (SUP-02/03)

- **Setup:** on the **bots-list card**, set the **BOT default** toggle to **«Вместе»** — this is
  the per-bot **«Авто/Вместе»** chats-list toggle (the one adjacent to «Бот работает / пауза»),
  **NOT** the activation switch.
- **Do:** a chat that has been **NEVER opened** receives an incoming message.
- **EXPECT:** **NOT** auto-replied — the `'*'` default row suppresses it (stays unread).
- **Result:** ☐ PASS ☐ FAIL
- **Notes:**

### Scenario 4 — Telegram suppress/restore (SUP-03, both channels)

- **Setup:** a **Telegram**-authed bot with its reply clone **ACTIVE**; open a specific chat.
- **Do:** repeat scenarios 1–2 on **Telegram** — flip the chat to **«Вместе»**, customer sends
  (EXPECT: **no reply**, stays **unread**, suggestions **still populate**); then flip back to
  **«Авто»**, customer sends (EXPECT: **replies**). The gate keys on the **Telegram** profile id.
- **Result (suppress):** ☐ PASS ☐ FAIL
- **Result (restore):** ☐ PASS ☐ FAIL
- **Notes:**

### Scenario 5 — Absence → reply + the «Бот работает/пауза» switch untouched (SUP-04)

- **Setup:** a **never-toggled** chat on an **«Авто»-default** bot, flag DB reachable, reply
  clone **ACTIVE**.
- **Do:** the customer sends a message.
- **EXPECT:** the bot **replies normally** (absence of any row → `suppressed=false` → reply — the
  never-toggled common case is never silenced).
- **Sub-check (activation switch untouched):** confirm the **«Бот работает / Бот на паузе»**
  activation switch still works **independently** of «Авто/Вместе» — **pause** it (customer
  sends → **no reply** from the paused bot), then **resume** it (customer sends → **replies**).
  It must be untouched by this phase — do **not** confuse it with the «Авто/Вместе» default
  toggle from scenario 3.
- **Result (absence → reply):** ☐ PASS ☐ FAIL
- **Result (activation switch):** ☐ PASS ☐ FAIL
- **Notes:**

---

## Verdict table

| # | Scenario | Channel(s) | Expected | Result | Notes |
|---|----------|-----------|----------|--------|-------|
| 1 | Per-chat «Вместе» suppress + heal | WhatsApp | no reply, stays unread, suggestions populate; re-open stays suppressed | ☐ | |
| 2 | «Авто» restore | WhatsApp | auto-reply returns | ☐ | |
| 3 | Bot-wide `'*'` default | WhatsApp | never-opened chat not replied | ☐ | |
| 4 | Suppress/restore on Telegram | Telegram | same as 1–2 on TG | ☐ | |
| 5 | Absence → reply + activation switch untouched | WhatsApp | never-toggled replies; «Бот работает/пауза» still pauses/resumes | ☐ | |

---

## Post-run (owner — immediately after the window)

- [ ] **DEACTIVATE** the test bot's reply-workflow clone(s) (real-contacts constraint) — WhatsApp
      **and** Telegram.
- [ ] **Flip the bot default back to «Авто»** if you left it on «Вместе» for scenario 3 (so the
      bot is not left suppressed by a lingering `'*'` row). Per-chat toggles from scenarios 1/2/4
      are legitimate app state — leave or reset as you prefer.
- [ ] Confirm the **09-04** test clones were already deactivated (they were).
- [ ] Confirm **prod bagkz untouched** — it stays **DORMANT**; the suppression gate **and** the
      Postgres cred consolidation (bind to the id that exists on the prod instance) fold into the
      future one-shot **bulk copy** (SUP-05). Nothing to run on prod this phase.

---

## Final disposition

- **ALL 5 scenarios PASS** → the suppression feature is behaviorally confirmed on **both**
  channels (ROADMAP Phase 9 success criteria 1–2; SUP-03/05) → the phase is behaviorally
  complete → run **`/gsd-secure-phase 09`**.
- **Any FAIL** → file the failing scenario(s) + the **observed** behavior → gap round.

**Disposition:** ☐ ALL PASS → `/gsd-secure-phase 09`  ☐ GAP (list failing scenarios + observed behavior)

---

## Appendix (OPTIONAL — same session) — Phase-10 UAT debt re-verify

Two extra checks that are now **unblocked** by `/webhook/SetReplyMode` being live (they were
tracked as `uat_gap` debt in **10-HUMAN-UAT.md** because the webhook 404'd during the 10-04
window). Their **formal** closure lives in **10-HUMAN-UAT.md** — **do NOT tick them here**; this
is a **courtesy same-session** re-verify while the build + tunnel + clone are already up. Record
the official PASS/FAIL back in `10-HUMAN-UAT.md` (scenarios 4 and 5), not in this file.

- **Debt A — 10-HUMAN-UAT scenario 4 (suggestions coalesce on-device, «Вместе»).** In a «Вместе»
  chat with `Suggest Replies` (`9PTyYcelRQI7bGDb`) active, have the customer send **2–3 rapid
  fragments** → EXPECT the suggestion cards refresh **ONCE** after ~2.5s of quiet (coalesced),
  while **manual refresh** mid-burst **and card-pick** still respond **IMMEDIATELY**. Do this on
  **BOTH** channels.
  - **Result:** ☐ PASS ☐ FAIL — **Notes:**
- **Debt B — 10-HUMAN-UAT scenario 5 (composition — semi-auto skips the whole path).** A semi-auto
  («Вместе») chat **skips the entire auto-reply path** (no wait, no reply, stays unread) while
  batching still works in «Авто» chats. Confirm the **«Бот работает / Бот на паузе»** activation
  switch still pauses/resumes **independently**.
  - **Result:** ☐ PASS ☐ FAIL — **Notes:**

---
*Gate for Phase 9 (semi-auto-suppression). Do NOT tick these on the owner's behalf — this is a
live-account, human-run device verification. `secrets.json` / dev n8n / tunnel are owner-run
(deny-ruled for Claude). The server side is already LIVE + proven fail-closed (09-04); this gate
is behavioral confirmation only. The optional appendix's formal closure lives in
`10-HUMAN-UAT.md` — do not edit that file from here.*

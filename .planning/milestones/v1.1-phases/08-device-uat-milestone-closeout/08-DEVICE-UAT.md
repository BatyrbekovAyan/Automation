# Phase 8 — Device UAT: v1.1 Telegram Parity milestone gate (consolidated, owner-run)

**Status:** RUN 2026-07-15/16 → re-verify 2026-07-17 (08-10) → round-2 re-verify 2026-07-17 (08-16) → round-3 re-verify 2026-07-20 (08-21) → round-4 re-verify 2026-07-20 (08-25 — RUN) → round-5 re-verify 2026-07-21 (08-29 — RUN) → round-6 re-verify 2026-07-21 (08-33 — RUN) → **round-7 re-verify 2026-07-21 (08-35 — RUN, see §Round 7 re-verify)** — **Overall: PASS — GATE A PASSED.** Round 7 all-PASS (owner "1. seems ok / 2. ok / 3. ok / 4. ok / 5. screenshot added"): **D2-view RESOLVED** (after six failing rounds — the 08-34 displaced-emoji discrimination + Reconcile always-adopt seam repaint every own-reaction change), stale-echo sanity + WA+TG invariants + final device sweep all PASS. **D15 REVISED to OPEN-DEFERRED follow-up** — the deterministic `[D15-probe]` fired and returned `reactionsKey=True reactionKey=False`, so the WhatsApp target payload DOES carry reaction state (NOT a platform limit); absence-based reconcile becomes a tracked follow-up (v1.2/post-milestone), not a Gate A blocker (owner decision "Flip Gate A now"). **Gate A → PASS unblocks Gate B (`08-PROD-REPLICATION.md`) + Gate C (`08-MILESTONE-CLOSE.md`); I.3 #10 re-aggregated to PASS; prod bagkz stays dormant until the 08-PROD-REPLICATION runbook is run.** Resolved through round 6: D17 late-WhatsApp-auth cover, WR-02 in-app WhatsApp reaction-removal stays removed; through round 5: D1–D8 core set, D10, D11, D2 core, D2-ext data layer + echo-hex CAPTURED/CLOSED, D13 cover+pill, D12-ext CTA, D14 TG cover blue, D16 late-TG cover; D9 SUPERSEDED by owner decision → D13 cover. This pass is the single source of truth for "is v1.1 shippable."

This is ONE ordered device pass that aggregates EVERY still-open device-verify gate
across the whole v1.1 milestone (Phases 3–7) **plus** the carried v1.0 deferred UAT.
Phases 3–7 are all code-complete + green; their live/on-device confirmations were
scattered across seven separate gate docs — this runbook consolidates them so any
FAIL is unambiguous and traceable back to the exact fix/source it reopens.

> **Do NOT tick these on the owner's behalf.** Writing this runbook was autonomous;
> RUNNING it is the owner gate. The phase stays `human_needed` until the owner records
> results below. Every checkbox ships blank.
>
> **Results recorded 2026-07-16:** the owner ran the pass on a real device (2026-07-15/16)
> and dictated per-item verdicts in-session; the ticks below are a verbatim transcription
> of the owner's stated results, with the owner's own words quoted where they add detail.

## Environment (prepare before you start)

- **Real device build** — Android is primary (iOS secondary). Not the Editor Game view;
  several items (restart persistence, media rendering, live round-trips) are device-only.
- **An authorized dev Telegram profile** in-app (the same one Phases 3–5 needed) — auth
  the Wappi/tapi dev profile; token lives in `secrets.json` (deny-ruled, reference by name only).
- **A 2FA-enabled Telegram account** (Two-Step Verification / cloud password) for **Group A**.
- **Dev n8n at `localhost:5678` + a `cloudflared` quick tunnel** for the **n8n-dependent
  Groups G + H only**. Those two ride **ONE shared dev-n8n session** — start n8n + the
  tunnel once, run `Tools/n8n/rotate-tunnel.py` to re-point callbacks, and do G and H
  back-to-back in the same window. **Do NOT touch prod bagkz** — it stays dormant until
  the 08-02 replication gate.

## How to record results

- Tick **exactly one** box per item (`PASS` / `FAIL` / `N/A`; Group I adds `RE-DEFER`).
- On any **FAIL**, add a row to the **Defects found** table with its source-anchor.
- Set the **Overall** line at the end.
- Groups are ordered so shared setup is amortised: A–F run on the device build alone;
  G–H need the shared dev-n8n session; I is the carried v1.0 sweep.

Item shape (every item):
`**expected:** … | **how-to:** … | **verdict:** ☐ PASS ☐ FAIL ☐ N/A | **source:** <doc-ref>`

---

## A. Auth — Telegram phone/code + 2FA cloud-password (both code AND QR flows)

> Needs the 2FA-enabled Telegram account. Exercise BOTH the code-entry flow and the QR flow.

1. **2FA cloud-password step engages — code-entry flow (correct password).**
   **expected:** after entering the SMS/app code, `detail:"2fa"` switches the flow into the
   cloud-password prompt (RU copy «Облачный пароль» / «Введите пароль от Telegram»); entering
   the correct password authorizes via the existing `ShowAuthSuccess` path.
   **how-to:** auth the 2FA Telegram account via phone → code → when prompted, type the correct
   cloud password.
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 05-VERIFICATION.md #3 (+ 05-05 SUMMARY)
2. **2FA cloud-password step engages — QR flow (correct password).**
   **expected:** the QR flow diverts into the SAME cloud-password prompt before the base64
   decode; the correct password authorizes via `ShowAuthSuccess`.
   **how-to:** start Telegram auth via the QR panel, scan on the 2FA account, then type the
   correct cloud password when prompted.
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 05-VERIFICATION.md #3 (+ 05-05 SUMMARY)
3. **Wrong password re-prompts without loop/crash; input clears on every path.**
   **expected:** a deliberately-wrong password shows «Неверный пароль» and re-prompts without
   crashing or looping; `TelegramCodeInput` is empty afterward on every path (success, wrong-pw,
   both flows).
   **how-to:** in either flow, enter a wrong password once, confirm the re-prompt, then the
   correct one; after each attempt confirm the code/password field is cleared.
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 05-VERIFICATION.md #3 (+ 05-05 SUMMARY)

## B. Chat client — list / history / media render + send

> Send the media types to the dev Telegram account («Избранное») and compare the app
> side-by-side with native Telegram. WhatsApp rendering must stay byte-identical throughout.

1. **Chat list renders.**
   **expected:** names, avatars, unread counts, and correct timestamps (via the `last_time`
   fallback) all render for the Telegram bot's chats.
   **how-to:** open a Telegram-authed bot on the «Чаты» tab (Telegram channel selected).
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 05-VERIFICATION.md #1
2. **Paginated history renders; numeric ids never crash.**
   **expected:** open any chat, scroll history — text renders via `type:"text"`; short numeric
   message ids never slice/crash the display fallback.
   **how-to:** open a chat with a long history, scroll up to page older messages.
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 05-VERIFICATION.md #1
3. **Media renders — image / video / voice(ptt) / document.**
   **expected:** each of image, video, voice (`ptt`), and document renders with the right
   thumbnail / duration / download affordance (all confirmed correct in the round-1 owner pass).
   **how-to:** send one of each kind to the bot and open the chat; tap to download/play.
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 05-HUMAN-UAT.md (voice/PDF/image) / 05-VERIFICATION.md #2
4. **`.tgs` animated sticker → sticker CARD (not a document card).**
   **expected:** an animated `.tgs` sticker renders as a deliberate sticker-slot-sized (396²)
   neutral rounded **CARD** with its own fill + centered «Стикер» caption + mid-gray glyph —
   NOT an `AnimatedSticker.tgs 22 KB · TGS` document card, and no futile download.
   **how-to:** send an animated `.tgs` sticker to the bot; compare against native Telegram.
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 05-HUMAN-UAT.md #1 (+ 05-08 sticker-card refinement)
5. **Video note (кружок) → bubble-free floating circle + duration badge.**
   **expected:** a video note renders as a chrome-free floating circle (transparent bubble,
   half-side radius) with a duration badge and tap-to-play — the circle floats like native TG,
   not inside a green message bubble or as a regular video card.
   **how-to:** send a кружок (round video note) to the bot; confirm it floats bubble-free with
   a readable duration badge.
   **verdict:** ☐ PASS ☑ FAIL ☐ N/A | **source:** 05-HUMAN-UAT.md #2 (+ 05-08 note-float refinement)
   **owner (2026-07-16):** "left and right sides of badge look sharp, i think round corners
   needs refresh or something" — circle floats, defect scoped to the duration-badge corners → **D3**.
6. **GIF → "GIF" corner badge on the video pipeline.**
   **expected:** a GIF keeps the video pipeline (thumb + tap-to-play) plus a "GIF" corner badge,
   with no filename/document chrome.
   **how-to:** send a GIF to the bot; confirm the "GIF" badge overlays the video thumbnail.
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 05-HUMAN-UAT.md #3
7. **Static webp sticker renders via the existing Sticker path.**
   **expected:** a static `image/webp` sticker (`type:"sticker"`) renders correctly via the
   existing Sticker path + unity.webp (this type was NOT observed in the 2026-07-13 capture —
   verify it here).
   **how-to:** send a static (non-animated) webp sticker to the bot.
   **verdict:** ☐ PASS ☐ FAIL ☑ N/A | **source:** 05-HUMAN-UAT.md Notes
   **owner (2026-07-16):** "cant test, probably ok" — re-run when a static webp sticker is at hand.
8. **Send text / send media / send quoted-reply.**
   **expected:** sending text, a media message (image/video/document), and a quoted reply all
   succeed; the delivery tick transitions Pending→Sent (`message/reply` swaps tempId→realId).
   **how-to:** from the bot's chat, send a text, one media message, and a swipe-right quoted reply.
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 05-VERIFICATION.md #2
9. **Send + remove reaction (pill toggles through the server echo).**
   **expected:** sending an emoji reaction and then removing it toggles the pill correctly and
   survives the server echo (recipient-required body on TG).
   **how-to:** long-press a message, add a reaction, then remove it; watch the pill through a
   refresh cycle.
   **verdict:** ☐ PASS ☑ FAIL ☐ N/A | **source:** 05-VERIFICATION.md #2
   **owner (2026-07-16):** (a) "most of reactions show the error: [Wappi] message/reaction
   failed: HTTP/1.1 400 Bad Request — detail: `error: rpcDoRequest: rpc error code 400:
   REACTION_INVALID`" → **D1**; (b) "reaction emojis that works can be added but when it is
   removed it is removed from telegram app but in our app it never removes" → **D2**.
10. **Incoming reply → quoted card.**
    **expected:** an inbound message that quotes another renders a quoted card above the bubble.
    **how-to:** have someone reply-to a message into the chat; confirm the quoted card renders.
    **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 05-VERIFICATION.md #2
11. **Opening an unread chat marks it read (no `mark_all`).**
    **expected:** opening an unread Telegram chat clears the unread state with a `{message_id}`-only
    body — no `mark_all` on TG.
    **how-to:** leave a chat unread, open it, confirm the unread badge clears.
    **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 05-VERIFICATION.md #2/#3
12. **Swipe-to-delete hidden on Telegram; WhatsApp unchanged.**
    **expected:** swipe-to-delete does nothing on Telegram (guarded no-op, no server call); the
    WhatsApp delete behavior is byte-identical to before.
    **how-to:** swipe a Telegram chat row (no delete), then confirm a WhatsApp chat still deletes.
    **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 05-VERIFICATION.md #3
    **owner (2026-07-16):** "ok, but why we still have it if it is not working. lets remove
    sliding on Telegram then" — owner decision: REMOVE the swipe affordance on TG → **D4** (with F8).
13. **Expected NON-defects (observe — do NOT file as defects).**
    **expected:** these two Telegram behaviors are ACCEPTED v1 (transport-inherent), not bugs:
    (a) the chat-list preview row does NOT show "X reacted…" for a Telegram reaction (poll
    transport has no per-reaction timestamp — WA-only feature); (b) removing your own reaction
    may flicker back for one refresh cycle before self-healing.
    **how-to:** if you see either, tick PASS (matches the note) and do NOT log a defect; tick FAIL
    only if the behavior is WORSE than described (e.g. a removal that never self-heals).
    **verdict:** ☐ PASS ☑ FAIL ☐ N/A | **source:** 05-VERIFICATION.md #2 (+ STATE blocker IN-04/IN-05)
    **owner (2026-07-16):** "removing your own reaction is removed in telegram but never removes
    in our app" — WORSE than the accepted one-cycle flicker (never self-heals) → **D2**.

## C. 05-09 field / UI fixes

1. **Bot-settings Telegram number shows a clean phone — never the raw JSON blob.**
   **expected:** the Telegram number field shows the authed phone (or hides, then repopulates on
   the next status check) — NEVER a raw JSON slice of the pretty `get/status` body.
   **how-to:** open a healthy authorized Telegram bot's settings; also re-auth a Telegram bot and
   confirm the field populates with the phone.
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 05-HUMAN-UAT.md #4
2. **Channel-switcher chip labels have comfortable side padding.**
   **expected:** the «WhatsApp» / «Telegram» switcher labels sit centred with clear margin inside
   each chip (not stretched edge-to-edge).
   **how-to:** open the chats screen with the switcher visible; inspect the label insets.
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 05-HUMAN-UAT.md #5
3. **Healthy authorized TG bot does NOT trip outside-app de-auth on settings open.**
   **expected:** opening a healthy authorized Telegram bot's settings never clears the number,
   flips `isOnTelegram` off, or deletes the Wappi profile.
   **how-to:** open a known-authorized Telegram bot's settings; confirm number stays, the toggle
   stays on, and the profile is not deleted (re-open the bot to confirm it still works).
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 05-HUMAN-UAT.md #6

## D. vthumb id-ambiguity probe

1. **Overlapping short numeric message ids across two TG dialogs resolve correctly.**
   **expected:** send/receive videos with overlapping short numeric message ids across TWO
   Telegram dialogs on the SAME profile (e.g. a channel post + a private chat) → each video's
   downloaded bytes AND thumbnail match its OWN source dialog, never the other one. The client
   cache key is TG-namespaced (`vthumb://tg/{profileId}/{chatId}/{messageId}`), but the
   `message/media/download` / `messages/id/get` calls carry no chat id — this probes the SERVER's
   by-id disambiguation.
   **how-to:** on the dev profile, arrange two dialogs (a channel post + a private chat) whose
   video message ids collide (TG ids are 1–5 digit per-dialog counters); open each and confirm the
   right bytes/thumbnail. **If it crosses:** note the follow-up — check whether tapi accepts a
   `chat_id` param on `message/media/download` (file the crossing in Defects with this note).
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 05-VERIFICATION.md #4 / **05-06-REVIEW WR-02** / STATE blocker
   **owner (2026-07-16):** "seems ok, not really sure" — low-confidence pass; no crossing
   observed, no defect filed. Keep an eye on it during normal use.

## E. Video-note `is_round` re-capture (optional, minor)

1. **Confirm whether a genuine кружок ever reports `is_round:true` on re-capture.**
   **expected:** re-capturing a genuine video note may still report `is_round:false` (Wappi-side
   gap observed in Phase 5). The shipped detection is a heuristic (square + `video.mp4` + ≤60s,
   `is_round` deliberately ignored) either way — this item just records the field's real value.
   **how-to:** send a кружок, re-capture its shape, and note whether `is_round` is true or false.
   Cosmetic/informational only; not a ship blocker.
   **verdict:** ☐ PASS ☑ FAIL ☐ N/A | **source:** 05-08 / 05-HUMAN-UAT.md #2
   **owner (2026-07-16):** `is_round` value not recorded; instead observed a rendering defect —
   "in my new telegram bot video note is shown wrongly, round video with white background bubble"
   (the note is NOT bubble-free in that bot/chat; likely the incoming-bubble transparency path) → **D3**.

## F. Switcher (Phase 6)

> Run on the device build with at least a both-channels bot, a WhatsApp-only bot, and a
> Telegram-only bot available (auth state drives the muted / auto-select logic).

1. **Pill placement + styling.**
   **expected:** the «WhatsApp | Telegram» segmented pill sits in the TopBar centre, matches the
   ModeToggle visual language (rounded track + rounded selected fill, comparable height), selected
   WhatsApp = green `#25D366` + white label, selected Telegram = blue `#2AABEE` + white label; it
   reads clean next to the ModeToggle.
   **how-to:** open the chats screen on a both-channels bot; inspect placement/alignment/colors.
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 06-HUMAN-UAT.md #1
2. **Segmented flip is mid-flight-safe (no crossed lists / no flicker).**
   **expected:** tapping the other channel swaps the chat list with the full reset choreography —
   no crossed lists, no half-loaded rows, no visible flicker.
   **how-to:** on a both-channels bot, tap Telegram then WhatsApp; watch the list swap cleanly.
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 06-HUMAN-UAT.md #2
3. **Re-tap the selected chip = no-op.**
   **expected:** re-tapping the already-selected chip does nothing (no reload flash).
   **how-to:** tap the currently-selected chip again; confirm no reload.
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 06-HUMAN-UAT.md #2
4. **Muted chip is tappable → connect empty state (both WA-only and TG-only).**
   **expected:** the unconnected channel's chip is muted (~40% alpha) yet clearly tappable;
   tapping it selects it and surfaces the connect empty state (not a blank screen). Both chips are
   always visible for every bot.
   **how-to:** on a WhatsApp-only bot tap the muted Telegram chip (→ TG connect empty state); repeat
   on a Telegram-only bot (→ WA connect empty state).
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 06-HUMAN-UAT.md #3
5. **Single-connected-channel bot auto-selects its live channel.**
   **expected:** a Telegram-only bot opens with Telegram already selected (WA muted); a WhatsApp-only
   bot opens with WhatsApp selected (TG muted) — no manual tap needed.
   **how-to:** open a Telegram-only bot, then a WhatsApp-only bot; confirm the pre-selected channel.
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 06-HUMAN-UAT.md #4
6. **Bottom bar: exactly 4 tabs, tab 0 «Чаты», no reachable pink Telegram screen.**
   **expected:** the bottom nav shows exactly 4 tabs (no Telegram tab); tab 0 reads «Чаты»; Сводка /
   Bots / Profile each land on the correct screen (the 2/3/4→1/2/3 index shift mis-routes nothing);
   no blank pink Telegram screen is reachable anywhere.
   **how-to:** tap through all 4 tabs; hunt for any leftover Telegram screen.
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 06-HUMAN-UAT.md #5
7. **Last-used channel persists per bot across restart.**
   **expected:** select Telegram on a both-channels bot, fully relaunch the build → the bot reopens
   on Telegram (`{botId}ActiveChatChannel` survived).
   **how-to:** pick Telegram, quit + relaunch, reopen the same bot.
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 06-HUMAN-UAT.md #6
8. **Deferred-polish decision — per-row swipe-delete affordance on Telegram.**
   **expected:** the 05-03 guard already makes the delete network call a safe no-op on TG; hiding the
   per-row swipe VISUAL affordance needs `ChatItemView`/prefab surgery (out of Phase 6 scope). Record
   the owner decision: keep / defer / cut.
   **how-to:** decide and note the disposition inline (this is a record-owner-decision line, not a bug).
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A — decision: **REMOVE** (owner 2026-07-16: "lets remove sliding
   on Telegram then" → hide the swipe affordance on TG rows → **D4**) | **source:** 06-HUMAN-UAT.md Deferred polish
9. **Deferred-polish decision — RU-localization sweep of English empty-state copy (IN-09).**
   **expected:** residual English empty-state strings (e.g. "Telegram not connected" / "Connect
   Telegram") should be Russianised before store. Record the owner decision: keep / defer / cut.
   **how-to:** decide and note the disposition inline (record-owner-decision line, not a bug).
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A — decision: **KEEP** (owner 2026-07-16: do the RU-localization
   sweep → **D8**) | **source:** 06-HUMAN-UAT.md Deferred polish

## G. Auto-reply e2e — Phase 4 TPL-06 (rides the shared dev-n8n session)

> **Shared dev-n8n session (with Group H).** Start dev n8n (`localhost:5678`) + the cloudflared
> tunnel, run `Tools/n8n/rotate-tunnel.py` first (a missed rotate caused the 2026-07-03 outage).
> **Preconditions (not scored):** `python3 Tools/n8n/verify-telegram-parity.py` must exit 0; the
> `Restamp RAG Chunks` Postgres credential must resolve and its role must be able to `UPDATE
> documents` (a no-match UPDATE returning 0 rows with NO credential/permission error passes).
> The 4 workflows were imported by literal id via MCP on 2026-07-13; the bot templates stay
> INACTIVE. **Do NOT touch prod bagkz.**

1. **Text auto-reply arrives in Telegram.**
   **expected:** create a Telegram bot from the app (a fresh clone off the fixed template) → send a
   text message → an AI reply arrives IN Telegram (tapi outbound `message/send`, `type:"text"`
   routing through the AI agent, not the fallback).
   **how-to:** auth the dev Telegram profile, create a TG bot in-app, send a text from a test chat.
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 04-HUMAN-UAT.md #5 (text)
2. **Voice → transcription + humanized pause → reply.**
   **expected:** a voice message is transcribed and gets a humanized listening pause (the
   `length_seconds` fallback when `media_info.duration` is absent), then a reply arrives.
   **how-to:** send a voice message to the TG bot.
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 04-HUMAN-UAT.md #5 (voice)
3. **Multi-turn memory stays coherent.**
   **expected:** a multi-turn exchange stays coherent with no context fragmentation (session key
   `profile_id + ':' + chatId`, stable on tapi).
   **how-to:** hold a 3–4 turn conversation referencing earlier turns.
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 04-HUMAN-UAT.md #5 (memory)
4. **Pre-auth-file RAG re-stamp — grounded price answer in Telegram.**
   **expected:** upload a price-list to a bot BEFORE authing Telegram (chunks land with
   `botTgId="-1"`), then auth Telegram / create the TG workflow, then ask a price question in
   Telegram → the answer is grounded in that file (chunks re-stamped from `"-1"` to the new TG
   workflow id; Supabase spot-check `select count(*) from documents where metadata->>'botTgId' =
   '<newTgWorkflowId>'` > 0).
   **how-to:** upload a price-list first, auth TG, then ask a price question in the TG chat.
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 04-HUMAN-UAT.md #5 (pre-auth file re-stamp)
5. **Recreate any stale `api/sync` clone.**
   **expected:** any pre-existing dev Telegram clone carrying the wrong `api/sync` outbound URLs is
   deleted and re-created off the fixed template (a fresh create yields a correct `tapi/sync` clone).
   **how-to:** if a stale TG clone exists, delete it and create a fresh one; confirm it replies.
   **verdict:** ☐ PASS ☐ FAIL ☑ N/A | **source:** 04-HUMAN-UAT.md #3
   **owner (2026-07-16):** "couldn't test" (no stale clone at hand).
6. **Deactivate the clone after the window; prod bagkz untouched.**
   **expected:** the per-bot Telegram clone is DEACTIVATED once the test window closes (clones run
   against real contacts); prod bagkz was not touched.
   **how-to:** deactivate the clone, confirm prod bagkz stayed dormant.
   **verdict:** ☐ PASS ☐ FAIL ☑ N/A | **source:** 04-HUMAN-UAT.md #6
   **owner (2026-07-16):** marked n/a — **REMINDER OUTSTANDING:** deactivate the test clone once
   the window closes (bot-activation policy — clones run against real contacts). Prod untouched.

## H. Live «Вместе» + Dashboard live-data — Phase 7 (rides the SAME dev-n8n session as G)

> Run this INSIDE the same dev-n8n window as Group G — the deployed Suggest_Replies workflow and
> the authorized Telegram profile are the shared prerequisites. Don't spin up a separate session.

1. **Suggestions populate in a real Telegram chat.**
   **expected:** open a Telegram chat on a Telegram-authed bot, toggle «Вместе» (or open with
   «Вместе» as the bot default) → 4 suggestions populate (payload carried `channel=="telegram"` +
   `botTgId==telegramWorkflowId`, server accepted).
   **how-to:** in the TG bot's chat, enable «Вместе» and observe the suggestions panel.
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 07-HUMAN-UAT.md (suggestions populate)
2. **Suggestion is RAG-grounded via `botTgId` (SUGG-02).**
   **expected:** on a bot whose price-list/catalog is seeded in RAG, a suggestion reflects that
   content → the server's `botTgId` RAG branch matched (not the WA branch, not skip-RAG). If no RAG
   data is seeded, record N/A (PENDING) and re-run after an Upload File on the TG bot.
   **how-to:** on a RAG-seeded TG bot, confirm a suggestion cites catalog/price content.
   **verdict:** ☐ PASS ☑ FAIL ☐ N/A | **source:** 07-HUMAN-UAT.md (RAG-grounded suggestion)
   **owner (2026-07-16):** initially "how to test?" (instructions provided in-session); follow-up
   verdict: "suggested messages are not relevant to last incoming message" — relevance to the
   newest incoming is broken, almost certainly DOWNSTREAM of **D5** (the suggestion payload is
   built from the in-app transcript, which never ingests the new incoming until re-enter).
   Re-test relevance AND RAG grounding together after the D5 fix.
3. **«Сводка» counts/lists the real Telegram conversation.**
   **expected:** after the Group G e2e produces a real Telegram conversation, a «Сводка» refresh
   shows that Telegram chat in the counts + recent list (DashboardOutcomes receives both channels'
   profile ids).
   **how-to:** trigger a «Сводка» refresh; confirm the TG conversation appears.
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 07-HUMAN-UAT.md (Dashboard live-data pass)
4. **One chip per dual-channel bot (covers both profiles).**
   **expected:** a bot with both channels shows exactly ONE filter chip; selecting it shows BOTH
   channels' rows.
   **how-to:** on the dashboard, find a dual-channel bot's chip and select it.
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 07-HUMAN-UAT.md (Dashboard live-data pass)
5. **A Telegram outcome row deep-links into that Telegram chat.**
   **expected:** tapping a Telegram outcome row lands in that Telegram chat («Чаты» tab, Telegram
   channel selected, chat open).
   **how-to:** tap a Telegram row in the dashboard drill-down; confirm the deep-link target.
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 07-HUMAN-UAT.md (Dashboard live-data pass)

## I. Carried v1.0 (Phases 01/02) — run OR explicitly re-defer

> Each item is **run OR explicitly re-defer with a one-line reason** — a re-defer is a valid
> disposition and rolls forward via 08-03. The verdict box adds **RE-DEFER** for this group.

### I.1 — Phase 01 pending device scenarios (mock-data suggestions panel)

1. **Per-chat semi-auto persistence survives an app restart (SC-1 / SEMI-02).**
   **expected:** flip a chat to semi-auto, fully quit + relaunch the device build → the same chat
   reopens with the toggle lit and the panel shown; other chats stay manual / no-panel.
   **how-to:** toggle semi-auto on a chat, quit + relaunch, reopen the chat.
   **verdict:** ☑ PASS ☐ FAIL ☐ RE-DEFER (reason: ______) | **source:** 01-HUMAN-UAT.md #1
2. **Panel renders all visual states at a fixed footprint with no layout pop (SC-2 / PANEL-04 / PANEL-06).**
   **expected:** toggle on → 4 shimmer skeletons → 4 ranked RU cards; «Рекомендуем» on the TOP card
   only; the ~209-char reply truncates to ~2 lines + ellipsis without widening the card; empty
   («Нет предложений») and error («Не удалось загрузить» + «Обновить») render at the SAME footprint;
   rounded corners on sheet/cards/chip/badge.
   **how-to:** exercise each state (loading / cards / empty / error) and watch for layout pop.
   **verdict:** ☑ PASS ☐ FAIL ☐ RE-DEFER (reason: ______) | **source:** 01-HUMAN-UAT.md #2
3. **Card-tap hand-off + re-cluster; incoming never overwrites a draft (INT-01 / INT-02 / INT-04).**
   **expected:** tap a card → its RU text loads into the composer (editable, overwrites any draft)
   AND a fresh steered set of 4 appears; nothing auto-sends. Type a draft, then trigger an incoming
   message → cards refresh but the composer draft is NOT touched.
   **how-to:** tap a card (confirm composer fill + re-cluster, no send); type a draft then trigger
   an incoming message.
   **verdict:** ☐ PASS ☑ FAIL ☐ RE-DEFER (reason: ______) | **source:** 01-HUMAN-UAT.md #3
   **owner (2026-07-16):** card-tap fill + re-cluster OK; BUT "incoming message is not shown until
   i reenter the chat, so i cant confirm this" — the draft-protection half is BLOCKED by the
   live-incoming render defect → **D5**.
4. **Stale/out-of-order/crossed responses never render under rapid picks + chat switches (SC-5 / DATA-03).**
   **expected:** rapidly tap several cards and/or switch chats mid-request → no stale or crossed set
   ever appears; the newest request for the current chat wins; superseded/foreign responses
   silently discarded.
   **how-to:** rapid-tap cards + switch chats mid-load; watch for any crossed/stale set.
   **verdict:** ☑ PASS ☐ FAIL ☐ RE-DEFER (reason: ______) | **source:** 01-HUMAN-UAT.md #4

### I.2 — Phase 02 pending device scenarios (n8n live wiring)

5. **Toggle → live cards (SC-1 / N8N-01 / N8N-02).** *(round-1 owner smoke-passed; detail checks pending)*
   **expected:** open a WhatsApp chat on an authed bot, flip «Вместе» ON → skeleton, then 4 cards
   within ~3–4 s — each a DIFFERENT move (from «Ответ/Уточнить/Вариант/К заказу/Отложить/Отказ»),
   ranked best-first, "Recommended" on card 1 only, no numeric %, grounded in the bot's catalog
   where relevant.
   **how-to:** flip «Вместе» on a live WhatsApp chat; verify distinct moves + badge + grounding.
   **verdict:** ☑ PASS ☐ FAIL ☐ RE-DEFER (reason: ______) | **source:** 02-HUMAN-UAT.md #1
6. **Incoming refresh + draft protection (INT-04 / DATA-01).**
   **expected:** with the panel open, an incoming customer message refreshes the cards; typing a
   draft then triggering another incoming does NOT overwrite the in-progress draft.
   **how-to:** open the panel, receive a message (cards refresh), type a draft, receive another.
   **verdict:** ☐ PASS ☑ FAIL ☐ RE-DEFER (reason: ______) | **source:** 02-HUMAN-UAT.md #2
   **owner (2026-07-16):** "seems like it is not" — cards do not refresh on an incoming message
   (consistent with I.1 #3: incoming doesn't render live at all) → **D5**.
7. **Pick → composer + steer (SC-2 / N8N-03 / INT-01).**
   **expected:** tap a card → text loads into the composer to edit (does NOT auto-send) AND a fresh
   re-clustered set of 4 appears; editing + sending via the normal Send button hands off correctly.
   **how-to:** tap a card, confirm composer fill + re-cluster (no auto-send), then edit + send.
   **verdict:** ☑ PASS ☐ FAIL ☐ RE-DEFER (reason: ______) | **source:** 02-HUMAN-UAT.md #3
8. **Airplane mode → error → recover (SC-4 / N8N-04).**
   **expected:** airplane mode + refresh → the panel's error state renders (no raw JSON, no crash);
   airplane off + manual refresh → cards return.
   **how-to:** enable airplane mode, refresh, then disable + refresh.
   **verdict:** ☑ PASS ☐ FAIL ☐ RE-DEFER (reason: ______) | **source:** 02-HUMAN-UAT.md #4
9. **Rapid picks / chat switch — no stale or crossed cards (SC-3 / DATA-03).**
   **expected:** pick several cards quickly, then switch chats mid-load → no stale or crossed set
   ever renders in the wrong chat; the newest request for the current chat wins.
   **how-to:** rapid-pick + switch chats mid-load against the live provider.
   **verdict:** ☑ PASS ☐ FAIL ☐ RE-DEFER (reason: ______) | **source:** 02-HUMAN-UAT.md #5

### I.3 — Phase 01 verification device confirmation

10. **01-VERIFICATION device confirmation (formal sign-off).**
    **expected:** the 4 device/visual items 01-VERIFICATION routed to human (restart persistence,
    visual state machine, hand-off + auto-populate, stale-response discard under load — the same
    behaviors as I.1 above) are confirmed on a device build, flipping 01-VERIFICATION from
    `human_needed` to passed. If I.1 items 1–4 are all PASS, this is PASS by aggregation; otherwise
    re-defer with the same reasons.
    **how-to:** confirm I.1 items 1–4 all passed on device, then mark this sign-off.
    **verdict:** ☑ PASS ☐ FAIL ☐ RE-DEFER | **source:** 01-VERIFICATION.md (human_verification ×4)
    **re-aggregated 2026-07-21 (round 7, Gate A PASS):** the sole blocker — I.1 #3's draft-protection half, blocked
    by **D5** (live-incoming render) — was RESOLVED at re-verify 2026-07-17 (08-04: incoming renders within ~one
    cycle on both channels, «Вместе» refreshes, typed draft survives). With D5 closed, I.1 items 1–4 are all
    effectively PASS, so per this item's own aggregation rule ("If I.1 items 1–4 are all PASS, this is PASS by
    aggregation") the formal 01-VERIFICATION human sign-off flips from `human_needed` to **passed**. This
    re-aggregation is the round-7 all-PASS action tied to the Gate A → PASS disposition.

---

## Round 4 re-verify (2026-07-20) — D2-view / D12-ext / D14 + G6 (BLOCKING)

> **RUN 2026-07-20 — Overall ISSUES** (verdicts transcribed below; 2 new defects **D15**/**D16** filed
> in §Defects). ONE Android build off the post-08-24 tree (fixes **08-22** D2-view + **08-23** D12-ext +
> **08-24** D14 all merged) to confirm the three round-4 residuals are closed, mirroring the
> 08-10 / 08-16 / 08-21 passes. Record EXACTLY ONE verdict per item (`PASS` / `FAIL` / `N/A`),
> transcribed VERBATIM; any FAIL adds/updates a §Defects row with its source anchor. On all-PASS **and**
> an explicit G6 disposition → flip Gate A to PASS, re-aggregate I.3 #10, unblock Gates B/C; any FAIL →
> keep Gate A = ISSUES and spin round 5 via `/gsd-plan-phase 08 --gaps`. **Outcome: D12-ext CTA + D14
> PASS; D2-view still FAILS; two new defects; G6 still-outstanding → Gate A stays ISSUES.**
>
> **Pre-build gate (met):** EditMode suite **1176/1176 Passed, 0 failed** FRESH via the in-Editor
> bridge (`Temp/claude/test-summary.json`, `editorAssemblyWrittenUtc` 2026-07-20T12:34:31Z —
> postdates the last app edit 08-24 `e99ebaa`; no app code changed since). Baseline = pre-round-4
> **1170 + 6** (08-23 `EmptyStateReasonPolicy` tests) = **1176**, exactly the +6 the round-4 fixes add
> (08-22 +0 / 08-23 +6 / 08-24 +0). Owner re-confirms the suite green FRESH from the bridge at run
> time before building (the total grows concurrently from parallel phases — read it, never hardcode).
>
> **G6 is a BLOCKING disposition item** — outstanding across THREE consecutive checkpoints
> (08-10 → 08-16 → 08-21); clones run against REAL contacts (bot-activation policy). **This checkpoint
> does NOT accept an all-PASS without an explicit G6 disposition.**

1. **D2-view — reaction bubble repaint (the round-3 repro).**
   **expected:** change a reaction on message bubble A (long-press → tap an emoji), then open the
   reaction bar on ANOTHER bubble B and change its reaction. Bubble A's pill shows the emoji you set
   on A — it does NOT stay stale. Repeat a few times (the old bug was intermittent).
   **how-to:** on a Telegram bot, react on bubble A, then open the bar on bubble B and react; watch
   A's pill. Repeat.
   **verdict:** ☐ PASS ☑ FAIL ☐ N/A | **source:** 08-DEVICE-UAT.md D2-view / B9–B13
   **owner (2026-07-20):** "no pass, still sometimes not updating bubble reaction when it is updated in
   telegram even though logs show updated reaction." → **D2-view stays open** (see §Defects). The repro
   is a reaction changed IN the Telegram app (remote change arriving via the live poll) with the new
   compiled `[D2-view]` log firing on the correct data, yet the bubble pill sometimes doesn't repaint.
   **ORCHESTRATOR HYPOTHESIS (not owner input, not asserted as fact):** the 08-22 fix defers a re-render
   only on the reaction-BAR dismiss path (`ReactionBarController.Hide` → `RefreshSourceNextFrame`); the
   poll-driven `HandleReactionsChanged` path may still repaint under whatever condition eats the mesh —
   WR-01's mechanism may be incomplete or a SECOND mechanism exists. Round-5 diagnosis candidates: what
   state the bubble/canvas is in when a poll-driven `HandleReactionsChanged` fires; RectMask2D / maskable
   culling; view recycling.
2. **D2-view — WhatsApp unaffected.**
   **expected:** on a WhatsApp bot, add/change a reaction — the pill repaints exactly as before.
   **how-to:** on a WhatsApp bot, add then change a reaction; confirm the pill updates as it always did.
   **verdict:** ☐ PASS ☐ FAIL ☐ N/A — *not-regressed-not-confirmed* (owner did not verdict the WA
   add/change repaint check; instead reported a NEW WA removal defect) | **source:** WhatsApp byte-identical invariant (08-22)
   **owner (2026-07-20):** "i noticed that if in whatsapp itself reaction is removed it is still not
   removed in our app" → NEW defect **D15** (a reaction REMOVED in the WhatsApp app itself is not removed
   in our app). Context: TG removal semantics were handled in **08-17**'s `TelegramReactionMerge`
   absence-vs-removal work; the WA-side `ReactionStore` was deliberately left untouched throughout v1.1,
   so WA removal propagation was likely NEVER implemented (**pre-existing, not a round-4 regression**).
3. **D12-ext — create-bot CTA survives a channel switch (BOTH channels, zero bots).**
   **expected:** with NO bots, open the Chats screen; tap «Создать бота» → the Add-Bot form opens
   (this already worked). Then switch the WhatsApp↔Telegram chip and tap «Создать бота» again — it
   STILL opens the form, on BOTH channels, with the channel you're viewing preselected. Switch back
   and forth a couple of times to confirm it never goes dead.
   **how-to:** delete all bots, open Chats, tap the create CTA, switch the channel chip, tap it again;
   repeat on both channels.
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 08-DEVICE-UAT.md D12-ext / F-group
   **owner (2026-07-20):** "pass" → **D12-ext CTA RESOLVED** (the create-bot CTA survives a
   WhatsApp↔Telegram chip switch on both channels; 08-23 `EmptyStateReasonPolicy` NoBots-coercion effective).
4. **D12-ext — no stale wrong-channel card over the cover (WR-02).**
   **expected:** on a Telegram-only bot inside its ~5-min sync cover, flip to WhatsApp (its «WhatsApp
   не подключён» card shows), then flip back to Telegram — you see the Telegram syncing cover, NOT a
   leftover «WhatsApp не подключён» card sitting on top / blocking taps.
   **how-to:** create a fresh Telegram bot, and while the cover is up flip WA↔TG; confirm no stale WA
   card lingers over the Telegram cover.
   **verdict:** ☐ PASS ☐ FAIL ☐ N/A — *not explicitly verdicted* (owner substituted a new
   late-channel-cover observation; the interrupted first-draft reply had "4: PASS" for the WR-02
   stale-card check but the owner REPLACED item 4 in the authoritative message — WR-02 is NOT recorded
   as PASS on that basis) | **source:** 08-DEVICE-UAT.md D12-ext / 08-REVIEW WR-02
   **owner (2026-07-20):** "if whatsapp channel exists and telegram channel is created its sunc cover
   page is not shown," → NEW defect **D16** (late-channel Telegram auth shows no sync cover). This is the
   KNOWN documented **08-19 deviation** ("late-channel auth stamps NO sync window on EITHER channel —
   exact parity, follow-up noted" — see 08-19-SUMMARY.md Follow-ups); filed as a **promotion of a
   documented follow-up, NOT a regression**.
5. **D14 — Telegram cover reads Telegram-blue.**
   **expected:** create a fresh Telegram bot → the post-creation cover's spinner, progress fill, and
   countdown are Telegram brand blue (#2AABEE), matching the blue empty-state accent + switcher chip.
   **how-to:** create a fresh Telegram bot; inspect the cover's spinner/fill/countdown color.
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 08-DEVICE-UAT.md D14 / D13 cover
   **owner (2026-07-20):** "PASS," → **D14 RESOLVED** (fresh Telegram cover's spinner/fill/countdown read
   brand blue #2AABEE).
6. **D14 — WhatsApp cover byte-identical.**
   **expected:** a fresh WhatsApp bot's cover is unchanged — green spinner/fill, green countdown.
   **how-to:** create a fresh WhatsApp bot; confirm the cover stays green.
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** WhatsApp byte-identical invariant (08-24)
   **owner (2026-07-20):** "PASS," → WhatsApp cover unchanged (green spinner/fill + green countdown).
7. **G6 — deactivate the dev test clone (BLOCKING line item).**
   **expected:** once any dev-n8n test window closes, the per-bot Telegram/WhatsApp clone is
   DEACTIVATED (clones run against real contacts); prod bagkz stayed dormant. **This checkpoint does
   NOT accept an all-PASS without an explicit G6 disposition** — record one of: `done` /
   `not-needed-this-pass-because-no-clone-active` / `still-outstanding`.
   **how-to:** after the test window, deactivate the clone (or confirm none was active); confirm prod
   bagkz untouched.
   **disposition:** ☑ done (post-checkpoint 2026-07-20) ☐ not-needed-this-pass (no clone active)
   ☐ still-outstanding
   **owner (2026-07-20):** "G6: what exactly should be done?" (**FOURTH consecutive checkpoint** carry —
   08-10 → 08-16 → 08-21 → 08-25). NOT recorded as done (the interrupted first-draft reply had "G6: done"
   before the owner substituted this clarification question in the authoritative message — the "done"
   draft is superseded and NOT recorded). G6 = after any dev-n8n test window, DEACTIVATE the per-bot
   Telegram/WhatsApp workflow clone (bot-activation policy — clones run against REAL contacts); prod bagkz
   stays dormant. **RESOLVED same day post-checkpoint:** after the deactivation steps were explained
   (in-app «Бот на паузе» / delete test bots, or toggle the clones inactive in dev n8n localhost:5678),
   the owner confirmed verbatim: **"G6 done"** (2026-07-20). NO round-5 carry — the 4-checkpoint
   outstanding streak ends here.
   **source:** 08-DEVICE-UAT.md G6 / 04-HUMAN-UAT.md #6
8. **D2-ext echo-hex (NICE-TO-HAVE, non-blocking).**
   **expected:** if convenient during D2-view testing, capture the tapi reaction-echo hex from the
   `[TG reaction echo]` Editor log (ChatManager.cs) / `Tools/tapi/probe-message.sh`. Absence is fine.
   **how-to:** watch the Editor log while changing a reaction; note the echo hex, or record
   "not captured".
   **verdict:** ☐ captured (hex: ______) ☑ not captured | **source:** 08-DEVICE-UAT.md D2-ext
   **owner (2026-07-20):** echo-hex not captured (THIRD consecutive checkpoint it went uncaptured;
   nice-to-have only — the D2-ext data layer is already proven correct by the owner's log observation).

**Round-4 Overall:** ☐ PASS (all D2-view / D12-ext / D14 items PASS **and** G6 dispositioned) ☑ ISSUES
— 3 PASS (D12-ext CTA #3, D14 #5, D14 #6), 1 FAIL (D2-view #1), 2 NEW defects (**D15** WhatsApp
reaction-removal not propagated #2, **D16** late-channel Telegram sync cover #4), G6 dispositioned
**done** post-checkpoint same day (see #7 — streak ended), echo-hex not captured.
**Round-4 Gate A disposition:** ☐ PASS (→ re-aggregate I.3 #10, unblock Gates B/C; prod bagkz stays
dormant until 08-02) ☑ ISSUES → **Gate A STAYS ISSUES.** D2-view FAIL + D15 + D16 filed in §Defects with
anchors; spin round 5 via `/gsd-plan-phase 08 --gaps`. Gates B/C + I.3 #10 re-aggregation stay blocked;
prod bagkz stays dormant. **Round-5 scope:** D2-view continued diagnosis on the poll-driven
`HandleReactionsChanged` repaint path (NOT the merge — data layer proven); D15 WhatsApp reaction-removal
propagation; D16 late-channel Telegram sync-cover stamp. (G6 resolved post-checkpoint 2026-07-20 — no carry.)

---

## Round 5 re-verify (2026-07-21) — D2-view / D15 / D16 (Gate A — the milestone v1.1 gate)

> **RUN 2026-07-21 — Overall ISSUES, Gate A STAYS ISSUES** (verdicts transcribed VERBATIM below WITH the
> discriminating log captures; D2-view relocated UPSTREAM, D15 removal-shape answered, D17 minted, echo-hex CAPTURED).
> ONE build off the post-08-28 tree (fixes **08-26** D2-view poll-path re-render + **08-27** D15 WhatsApp
> reaction-removal ingest + **08-28** D16 late-channel Telegram sync-cover stamp all merged); the owner ran the pass
> and returned per-item verdicts + two Unity Editor Console screenshots, mirroring the 08-10 / 08-16 / 08-21 / 08-25
> passes. **NOTE: this round-5 repro session ran in the Unity EDITOR** (the screenshots are the Editor Console, not
> device logcat) — the Editor reproduces both FAILs, which makes round-6 verification EASIER (Editor-reproducible; no
> device build needed for the fix loop, though a device pass still gates Gate A). Verdicts transcribed VERBATIM; each
> FAIL updates its §Defects row with the anchor **and** the captured `[D2-view]`/`[D15]` Console line(s). **Outcome:
> item 2/4/5 PASS (incl. D16 late-TG cover RESOLVED); item 1 D2-view FAIL (relocated); item 3 D15 FAIL (candidate (b)
> answered); item 6 owner SCOPE-OVERRIDE → new D17; item 7 echo-hex CAPTURED → Gate A stays ISSUES, round 6 next.**
>
> **Pre-build gate (as authored — re-confirm FRESH before building):** EditMode suite **1181/1181 Passed,
> 0 failed** per the three wave-13 summaries (08-26 → 1180, 08-27 → 1181, 08-28 → 1181 delta-0), FRESH via the
> in-Editor bridge. Baseline = pre-round-5 **1176 + 5** = **1181**, exactly the +5 the round-5 fixes add
> (08-26 **+4** D2-view converter tests / 08-27 **+1** D15 removal-redelivery pin / 08-28 **+0** D16). The suite
> grows concurrently from the parallel phases (9/10/11) — read the total FRESH from `Temp/claude/test-summary.json`
> (or `Tools/run-tests-headless.sh` if the Editor is closed) at run time; the gate is "completed/Passed,
> 0 failures at the current baseline", never a hardcoded absolute.
>
> **G6 is RESOLVED (no carry).** The owner confirmed "G6 done" post-08-25 (commit 7c1ad48) — the
> four-checkpoint dev-clone-deactivation streak ended; it is NOT a round-5 line item.
>
> **New this round — two device-log capture asks that make a FAIL discriminable rather than another guess:** the
> `[D2-view] post-render` state log (08-26) reports pill active / label length / canvasRenderer.cull to tell
> exception vs cull-state vs TMP submesh-churn; the `[D15] wa-reaction` log (08-27) records the removal shape
> (rawId/stanzaId/bodyEmpty/seen) to tell which of the three IN-02 candidate removal shapes WhatsApp uses. **On
> ANY D2-view or D15 FAIL, capture the relevant logcat line(s) and paste them into the Defects row** — that is
> the whole point of the discriminating logs.

1. **D2-view — reaction changed IN the Telegram app repaints the bubble (the round-4 repro).**
   **expected:** on a Telegram bot, change a reaction on a message IN the Telegram app (a remote change arriving
   via the live poll). The bubble pill in-app updates to the new emoji — it does NOT stay stale. Repeat a few
   times, incl. changing on one bubble then another (the old bug was intermittent).
   **how-to:** on a Telegram bot, change a reaction in the Telegram app on bubble A, watch A's pill repaint; then
   change one on bubble B right after. Repeat several times.
   **IF FAIL:** capture the `[D2-view] post-render id=… active=… len=… culled=…` logcat line (active/len/culled
   discriminates TMP submesh-churn vs RectMask2D cull vs exception) and paste it into the Defects row.
   **verdict:** ☐ PASS ☑ FAIL ☐ N/A | **source:** 08-DEVICE-UAT.md D2-view / B9–B13
   **owner (2026-07-21):** "still not updating reaction when changed in telegram. logs show right reaction but it
   doesnt update on message bubble. (Screenshot 1, logs update reaction, but not on screen.)"
   **captured logs (Screenshot 1 — Unity Editor Console):** `[D2-view] reactions changed id=23475 n=1` →
   `[D2-view] post-render id=23475 active=True len=24 culled=False` (00:24:00), then NO further `[D2-view]`
   reactions-changed / post-render lines despite the emoji changing twice more (👍 → 😁 → 👌); every subsequent
   change fired only `[TG reaction echo]` at Normalize level (full timeline in the log-evidence block below).
   **ORCHESTRATOR ANALYSIS (labeled — not owner input):** **FAIL (4th round), but the mechanism class is now
   REFUTED-and-relocated.** The captured logs show the first remote change fired `OnMessageReactionsChanged` and
   the 08-26 hardened re-render reported a HEALTHY post-render (`active=True len=24 culled=False` → the exception
   candidate is refuted [the log fired at all], the cull candidate is refuted [culled=False]). Decisively, the two
   SUBSEQUENT emoji changes (😁 U+1F601, 👌 U+1F44C) produced `[TG reaction echo]` lines at Normalize level but NO
   `OnMessageReactionsChanged` / `[D2-view]` events at all. The failure is UPSTREAM of the view: after the first
   own-user remote reaction is applied, subsequent changes stop emitting change events. The 08-26 view hardening
   STAYS (it fixed the one-shot-loss class and its diagnostic proved the relocation). **Evidence-backed HYPOTHESIS
   for round 6 (labeled hypothesis, NOT fact):** the `TelegramReactionMerge` own-reaction optimistic-grace window
   (90s, identity-keyed via `_tgOwnUserId` — note `user_id == ownId` in every echo) suppresses server-observed
   changes to one's OWN reaction after the first apply; the owner's quick successive changes all fell inside the
   window, which also retroactively explains the round-2..5 intermittency (waiting >90s between tests would make it
   "work"). **Round-6 scope:** trace/fix the merge's own-reaction grace so a REMOTE own-user change to a DIFFERENT
   emoji is never suppressed (grace should key on a pending LOCAL optimistic set, not mere own-identity), with an
   EditMode test reproducing echo-without-event.
2. **D2-view — WhatsApp add/change unaffected.**
   **expected:** on a WhatsApp bot, add then change a reaction — the pill repaints exactly as before (the
   poll-path re-render is channel-agnostic + idempotent; the WhatsApp live path self-heals identically).
   **how-to:** on a WhatsApp bot, add a reaction then change it; confirm the pill updates as it always did.
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** WhatsApp byte-identical invariant (08-26)
   **owner (2026-07-21):** "pass" (item 2) → WhatsApp add/change reaction repaints unaffected; the 08-26 poll-path
   re-render is channel-agnostic + idempotent (WhatsApp byte-identical holds).
3. **D15 — a reaction REMOVED in the WhatsApp app clears in-app.**
   **expected:** on a WhatsApp bot, add a reaction in the WhatsApp app, then REMOVE it in the WhatsApp app — the
   pill disappears in-app within a poll cycle (candidate-a: an already-seen reaction raw re-emitted under the same
   id now re-runs `HandleReactionEvent`; no longer sticks).
   **how-to:** on a WhatsApp bot, add a reaction in the WhatsApp app, watch it appear in-app, then remove it in
   the WhatsApp app; watch the pill clear within a poll cycle.
   **IF FAIL:** capture the `[D15] wa-reaction rawId=… stanza=… bodyEmpty=… seen=…` line(s) for the removal —
   they identify which of the three IN-02 shapes (a re-emit / no raw / missing stanza) WhatsApp uses — paste into
   the Defects row so round 6 targets exactly one site.
   **verdict:** ☐ PASS ☑ FAIL ☐ N/A | **source:** 08-DEVICE-UAT.md D15 / round-4 item 2
   **owner (2026-07-21):** "still same, removing reaction in whatsaap doesnt remove it in our app. (Screenshot 2)"
   **captured logs (Screenshot 2 — Unity Editor Console):** `[D15] wa-reaction rawId=3A8976F33979EE5EE8EB
   stanza=3AAFD6395EE4345C8EA0 bodyEmpty=False seen=False` (00:33:20, the ADD) → `[D2-view] reactions changed
   id=3AAFD6395EE4345C8EA0 n=1` + `post-render active=True len=24 culled=False` (add applied) → `[D15] wa-reaction
   rawId=… stanza=… bodyEmpty=False seen=True` (00:33:22) → the SAME add-raw re-delivered `bodyEmpty=False seen=True`
   on every subsequent poll (00:33:25…). NO empty-body raw EVER arrives after the in-WhatsApp removal (full timeline
   in the log-evidence block below).
   **ORCHESTRATOR ANALYSIS (labeled — not owner input):** **FAIL, discriminator answered.** The in-WhatsApp removal
   produces NO raw at all — no empty-body re-emit (`bodyEmpty` stays False), no new rawId; the original ADD raw
   simply keeps re-delivering (`seen=True`) on every poll. IN-02 candidate **(a)** (same-id empty-body re-emit) is
   **REFUTED by evidence** — the 08-27 re-process fix is correct-but-INERT for removal (harmless, idempotent). This
   is candidate **(b)** (no removal raw) — Wappi WA sync never surfaces the removal. **Round-6 scope (diagnosis-first):**
   absence-based reconcile for WhatsApp (mirror the Telegram approach — poll the target message's `reactions[]`
   state if Wappi's WA sync exposes it: check `messages/id/get` or the `chats/filter` payload — and clear the
   `ReactionStore` entry when the server state no longer carries the reaction), OR document as a platform limit if
   Wappi WA truly never reflects a removal.
4. **D15 — WhatsApp add/change reaction still repaints (invariant).**
   **expected:** adding and changing a reaction in the WhatsApp app still updates the pill as before — the
   unseen-id path is untouched, and re-processing an already-applied reaction is an idempotent no-op.
   **how-to:** on a WhatsApp bot, add a reaction then change it; confirm the pill updates normally (no
   double-pill, no stuck state).
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** WhatsApp byte-identical invariant (08-27)
   **owner (2026-07-21):** "pass" (item 4) → WhatsApp add/change reaction still repaints normally; the unseen-id
   path is untouched and re-processing an already-applied reaction is an idempotent no-op (confirmed by the
   Screenshot-2 ADD applying cleanly through `[D2-view] reactions changed` + healthy post-render).
5. **D16 — late Telegram auth on a WhatsApp bot shows the Telegram sync cover.**
   **expected:** on a bot that already has WhatsApp, authorize Telegram from settings; when you open the bot's
   Telegram channel, the ~5-min post-creation sync cover (spinner + progress + countdown, brand blue per D14)
   shows over the chat list (`{bot}TelegramSyncUntil` now stamped at late-auth success).
   **how-to:** on a WhatsApp-only bot, open settings → authorize Telegram (code or QR); switch to the Telegram
   channel and confirm the sync cover shows.
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 08-DEVICE-UAT.md D16 / 08-19 late-channel follow-up
   **owner (2026-07-21):** "pass" (item 5) → **D16 RESOLVED**: a WhatsApp-first bot that authorizes Telegram later
   now shows the Telegram post-creation sync cover (the 08-28 `{bot}TelegramSyncUntil` late-auth stamp fires the
   08-19 cover gate).
6. **D16 — WhatsApp byte-identical: late WhatsApp auth shows NO new cover.**
   **expected:** on a bot that already has Telegram, authorizing WhatsApp later does NOT newly show a WhatsApp
   cover (the documented parity decision — no late-auth WhatsApp stamp; WhatsApp behaviour is unchanged).
   **how-to:** on a Telegram-only bot, authorize WhatsApp later; confirm no new WhatsApp sync cover appears.
   **verdict:** ☐ PASS ☐ FAIL ☐ N/A — **owner SCOPE-OVERRIDE** (the 08-28 expected behaviour [no WA cover] was
   CONFIRMED, but the owner has now OVERRIDDEN the parity decision → new work item **D17**) | **source:** WhatsApp
   byte-identical invariant (08-28) / parity decision
   **owner (2026-07-21):** "when telegram channel exist on bot and then when adding whatsapp channel to same bot
   there is no sync chats cover page for whatsapp (should be sync chats cover page for both channels every time
   they are just added)"
   **ORCHESTRATOR ANALYSIS (labeled — not owner input):** the 08-28 documented parity decision (IN-01: NO late-auth
   WhatsApp stamp) behaved EXACTLY as designed — no WhatsApp cover appeared — so this is NOT a defect against 08-28.
   But the owner has now OVERRIDDEN the parity decision: covers must show for BOTH channels every time a channel is
   late-added. This SUPERSEDES the 08-28 parity decision (owner-approved scope change, exactly like D14 was). Mint
   NEW work item **D17**: stamp `{bot}WhatsappSyncUntil` on late WhatsApp auth — the exact mirror of 08-28's
   Telegram stamp (ShowAuthSuccess settings-reauth branch, gated `authPage == WhatsappAuth`, 300s
   `WhatsAppSyncWindowSeconds`, reuse `SyncUntilSuffixFor(WhatsApp)` + `Bot.DeleteBot` teardown). **Round-6 scope.**
7. **D2-ext echo-hex (NICE-TO-HAVE, non-blocking).**
   **expected:** if convenient during D2-view testing, capture the tapi reaction-echo hex from the
   `[TG reaction echo]` Editor log (ChatManager.cs) / `Tools/tapi/probe-message.sh`. Absence is fine — the D2-ext
   data layer is already proven; record "not captured".
   **how-to:** watch the Editor log while changing a reaction; note the echo hex, or record "not captured".
   **verdict:** ☑ captured (hex: 👍 U+1F44D, 😁 U+1F601, 👌 U+1F44C — BASE form, no FE0F; `user_id == ownId`
   1038376805) ☐ not captured | **source:** 08-DEVICE-UAT.md D2-ext
   **owner (2026-07-21):** "screenshot 1 have [TG reaction echo] logs, hope it is what you need" → **echo-hex
   CAPTURED at last** (previously uncaptured across 4 checkpoints). The `[TG reaction echo]` lines show tapi
   reaction echoes carry BASE-form codepoints (U+1F44D / U+1F601 / U+1F44C — no U+FE0F qualifier) and
   `user_id == ownId` for own reactions — consistent with the round-2 `ReactionEmoji` base-form finding (08-11).
   **The echo-hex ask is now CLOSED.**

**Captured log evidence (round 5 — Unity Editor Console; the repro session ran in the EDITOR, not on a device):**

> Both FAILs (D2-view + D15) were captured from screenshots of the Unity Editor **Console** — so this round-5 repro
> ran in the Editor, not on a device build. The Editor reproduces BOTH failures, which makes round-6 verification
> EASIER (Editor-reproducible → no device build needed for the fix loop). A device pass still gates Gate A.

**Screenshot 1 (D2-view item 1 + echo-hex item 7) — timeline:**
- 00:23:58 `[TG reaction echo] '👍' [U+1F44D] user_id=1038376805 ownId=1038376805`
- 00:24:00 `[D2-view] reactions changed id=23475 n=1`
- 00:24:00 `[D2-view] post-render id=23475 active=True len=24 culled=False`
- 00:24:01 `[TG reaction echo] '👍' [U+1F44D] user_id=1038376805 ownId=1038376805`
- 00:24:04 `[TG reaction echo] '😁' [U+1F601] user_id=1038376805 ownId=1038376805`
- 00:24:07 `[TG reaction echo] '👌' [U+1F44C] user_id=1038376805 ownId=1038376805`
- 00:24:10 / :13 / :16 / :19 / :22 `[TG reaction echo] '👌' [U+1F44C]` (same echo re-delivered each poll)
- Echo-log stack: `ChatManager:LogTelegramReactionEcho` (ChatManager.cs:1712) ← `Normalize` (1671) ← `SyncLatestMessages` (754).
- **KEY:** NO further `[D2-view] reactions changed` / `post-render` lines after 00:24:00 despite the emoji changing
  twice more (1F601, then 1F44C) → the change event stops firing after the first own-user reaction applies
  (upstream event-suppression, not a view repaint miss).

**Screenshot 2 (D15 item 3) — timeline:**
- 00:33:20 `[D15] wa-reaction rawId=3A8976F33979EE5EE8EB stanza=3AAFD6395EE4345C8EA0 bodyEmpty=False seen=False`
- 00:33:20 `[D2-view] reactions changed id=3AAFD6395EE4345C8EA0 n=1` + `post-render … active=True len=24 culled=False` (the ADD applied)
- 00:33:22 `[D15] wa-reaction rawId=3A8976F33979EE5EE8EB stanza=3AAFD6395EE4345C8EA0 bodyEmpty=False seen=True`
- 00:33:25 `[D15] wa-reaction rawId=… bodyEmpty=False seen=True` (same add-raw re-delivered; NO empty-body raw EVER arrives after the in-WhatsApp removal)

**Round-5 Overall:** ☐ PASS (all D2-view / D15 / D16 items PASS) ☑ ISSUES (any FAIL) — **RUN 2026-07-21**: 3 PASS
(item 2 D2-view WA add/change, item 4 D15 WA add/change, item 5 **D16 late-TG cover RESOLVED**), 2 FAIL (item 1
**D2-view** — relocated UPSTREAM to an own-reaction event-suppression, item 3 **D15** — removal-shape answered
[candidate (b): no removal raw]), 1 owner SCOPE-OVERRIDE (item 6 → new **D17** late-WhatsApp-auth cover, supersedes
the 08-28 parity decision), echo-hex **CAPTURED** (item 7, ask CLOSED). G6 resolved, not carried.
**Round-5 Gate A disposition:** ☐ PASS (→ flip Gate A to PASS, re-aggregate I.3 #10, unblock Gates B
[prod replication, 08-02] and C [milestone close, 08-03]; prod bagkz stays dormant until 08-02) ☑ ISSUES →
**Gate A STAYS ISSUES.** D2-view FAIL (relocated) + D15 FAIL (shape answered) + D17 minted, all filed/updated in
§Defects with anchors + the captured `[D2-view]`/`[D15]` log lines; **spin round 6** via `/gsd-plan-phase 08 --gaps`.
Gates B/C + I.3 #10 re-aggregation stay blocked; prod bagkz stays dormant. Do NOT touch Gates B/C or I.3 #10 this
pass. **Round-6 scope:** **D2-view** upstream event-suppression fix (own-reaction grace in `TelegramReactionMerge`
should key on a pending LOCAL optimistic set, not mere own-identity — evidence: echo-without-event; Editor-reproducible
EditMode test); **D15** absence-based WhatsApp removal reconcile (poll the target message's `reactions[]` state and
clear the `ReactionStore` entry when the server no longer carries it — OR document as a Wappi WA platform limit);
**D17** late-WhatsApp-auth cover stamp (`{bot}WhatsappSyncUntil`, exact mirror of 08-28's Telegram stamp — supersedes
the 08-28 parity decision).

---

## Round 6 re-verify (2026-07-21) — D2-view / WR-02 / D17 / D15 disposition (Gate A — the milestone v1.1 gate)

> **RUN 2026-07-21 — Overall ISSUES, Gate A STAYS ISSUES** (verdicts transcribed VERBATIM below WITH the
> discriminating `[D2-merge]`/`[D15]` Console captures from two Unity Editor Console screenshots; the round-6
> repro ran in the Editor Play Mode, as round 5 established both fix items are Editor-reproducible — no device
> build was needed for the fix loop). The owner verified the round-6 fixes — **08-30** (D2-view
> confirmation-clears-grace + WR-01 tombstone drop-on-confirmed-absence), **08-31** (WR-02 revert + the D15
> platform-limit disposition + Editor `[D15-probe]`), **08-32** (D17 late-WhatsApp-auth sync-cover stamp) — all
> merged on the tree at HEAD `5468d18`. **Outcome: 4 PASS (item 2 WR-02 in-app WhatsApp removal STAYS removed,
> item 3 WhatsApp add/change invariant, item 4 Telegram add/change/remove invariant, item 5 D17 late-WhatsApp
> cover), 1 FAIL (item 1 D2-view — residual now EXACT: an external own-change that DIFFERS from the optimistic
> emoji is suppressed for the whole grace window; the `[D2-merge]` discriminator CAPTURED = item 7), item 6 D15
> probe DID NOT FIRE (UNCONFIRMED-not-refuted — no WhatsApp quoted-reply resolve happened, so the probe seam
> never triggered) → Gate A stays ISSUES, round 7 next.**
>
> **KEY CHANGE from prior rounds — Editor Play-Mode is sufficient for the fix items.** Round 5 proved BOTH
> failing items (D2-view + WR-02) reproduce in the Unity Editor Console (the round-5 evidence was Editor Console
> screenshots, not device logcat). So **items 1–2 (D2-view rapid-change repaint + WR-02 removal-stays-removed)
> verify in the Editor Play Mode** — no device build needed for the fix loop — with **ONE Android build only for
> the final Gate A confirmation sweep** (items 3–5: the WA/TG invariants + the D17 late-WA cover). Item 6 (the
> D15 probe) captures in the Editor OR on the build; item 7 is a nice-to-have discriminator noted during item 1.
>
> **Pre-build/pre-verify gate (read FRESH — never hardcode):** confirm the EditMode suite is completed/Passed
> with 0 failures at the CURRENT baseline before verifying/building — read the total FRESH from the in-Editor
> bridge (`Temp/claude/test-summary.json`) or `Tools/run-tests-headless.sh` if the Editor is closed. Round-6
> fixes add **+3** (08-30 net +3 — CR-01 +2 + WR-01 +1, with 2 tombstone-test renames flat / 08-31 **+0** — a
> glue revert + an Editor-only probe / 08-32 **+0** — a PlayerPrefs stamp). Baseline was **1181** at round 5 →
> **1184** at round 6 (per the three wave-15 summaries; 08-30/08-31/08-32 all read 1184/1184 FRESH). The suite
> grows concurrently from the parallel phases (9/10/11), so the gate is "completed/Passed, 0 failures at the
> current baseline", never a hardcoded absolute — re-read it at run time.
>
> **G6 is RESOLVED (no carry).** The owner confirmed "G6 done" post-08-25 — the four-checkpoint
> dev-clone-deactivation streak ended; it is NOT a round-6 line item.
>
> **The D2-ext echo-hex is CLOSED (no carry).** It was CAPTURED at round 5 (tapi echoes carry BASE-form
> codepoints U+1F44D/U+1F601/U+1F44C, `user_id == ownId`); the ask is closed and is NOT a round-6 line item.
>
> **Discriminating logs that make a FAIL diagnosable (round 6):** the Editor-only `[D2-merge]` log (08-30) fires
> ONLY when a DIFFERING echo is suppressed within the grace — for a genuine external own-change it should NOT
> fire (the change should just apply); its presence/absence tells whether the suppression path is still eating
> the change. The Editor-only `[D15-probe]` log (08-31) / an Editor `response.txt` grep confirms whether a
> WhatsApp per-message payload carries any reaction-state key (the D15 platform-limit disposition; expected
> `reactionsKey=False reactionKey=False`). **On ANY D2-view or WR-02 FAIL, capture the relevant Console/logcat
> line(s) and paste them into the Defects row** — that is the whole point of the discriminating logs.

**— Editor Play-Mode items (1–2; Editor-reproducible — no device build needed for the fix loop) —**

1. **D2-view — rapid own-reaction change in the Telegram app repaints EVERY time.**
   **expected:** on a Telegram bot, tap a reaction on a message IN OUR app (arms the optimistic grace), then
   within 90 s change your OWN reaction on that message IN the Telegram app 2–3 times (e.g. 👍 → 😁 → 👌). Each
   change repaints the in-app bubble pill to the new emoji — no change is dropped after the first.
   **how-to:** in the Editor Play Mode on a Telegram bot, tap a reaction in-app, then within 90 s change your own
   reaction on that same message in the Telegram app 2–3 times; watch the in-app pill repaint on every change.
   **IF FAIL:** capture any `[D2-merge] suppressed server-me '…' by fresh local '…' age=…s` line(s) (a genuine
   external change should NOT log this — its presence means the suppression path is still eating the change) plus
   the `[D2-view]`/`[TG reaction echo]` timeline — paste into the Defects row.
   **verdict:** ☐ PASS ☑ FAIL ☐ N/A | **source:** 08-DEVICE-UAT.md D2-view / B9–B13
   **owner (2026-07-21):** "still same behavior. see screenshot" (Screenshot 1 — the `[D2-merge]` suppression
   lines fire on every subsequent own-change).
   **captured logs (Screenshot 1 — Unity Editor Console):** the `[D2-merge] suppressed server-me '…' by fresh
   local '🥺' age=…s` line fires on EVERY subsequent change with a climbing age (9s → 12s → 15s → 18s → 21s and
   climbing), the server-me emoji being the newer Telegram-app value (🔥, then 👎) while the suppressing local
   stays the optimistic 🥺 (full timeline in the log-evidence block below).
   **ORCHESTRATOR ANALYSIS (labeled — not owner input):** **FAIL (5th round for D2-view), residual mechanism now
   EXACT.** The `[D2-merge]` discriminator — built in 08-30 precisely for this — shows the never-clear-on-differ
   branch suppressing genuine external own-changes for the full grace window (age 9s→21s and climbing, fresh
   local 🥺). Root of the residual: the round-6 fix assumed a SAME-emoji echo would arrive to consume the grace,
   but under the rapid-change repro the confirming echo of the in-app tap (🥺) NEVER arrives — tapi `reactions[]`
   carries only the CURRENT state, so intermediate states never echo. The first observed server-me is already
   the newer TG-app emoji (🔥), which DIFFERS from the optimistic one, so the stale-echo defense eats it for the
   full 90s. The merge cannot currently distinguish "stale pre-tap state" from "genuinely newer external change"
   when both differ from the optimistic emoji. **ROUND-7 DESIGN CANDIDATES (candidates, not a decision):** (a)
   track the DISPLACED emoji (what the optimistic tap replaced) — suppress ONLY when server-me equals the
   displaced state; any THIRD value adopts+clears; (b) clear the grace on reaction-send HTTP success (the POST
   confirms the server state; any later differing server-me is genuinely newer), possibly with a short residual;
   (c) drastically shorten the 90s grace. **Progress note:** items 4 (TG in-app add/change/remove) and the
   first-change repaint all work — the remaining failure is ONLY an external-change-during-grace.
2. **WR-02 regression — an own WhatsApp reaction removed in-app STAYS removed across polls.**
   **expected:** on a WhatsApp bot, add a reaction in OUR app (or from the WhatsApp phone app), then REMOVE it in
   OUR app — the pill clears and STAYS cleared across the next several poll cycles (it does NOT resurrect within
   one poll as it did before the 08-31 revert).
   **how-to:** in the Editor Play Mode on a WhatsApp bot, add a reaction in-app, then remove it in-app; watch the
   pill through several poll cycles and confirm it never resurrects.
   **IF FAIL:** capture the `[D15] wa-reaction rawId=… stanza=… bodyEmpty=… seen=…` line(s) around the
   remove-then-poll window — paste into the Defects row.
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 08-DEVICE-UAT.md D15 / 08-REVIEW WR-02
   **owner (2026-07-21):** "on a WhatsApp bot, add then remove a reaction in our app → the pill stays cleared
   across several polls. this works now and worked before. what needs the fix is that removing reaction in
   whatsapp app doest remove reaction in our app."
   **captured logs (Screenshot 2 — Unity Editor Console):** `[D2-view] reactions changed
   id=3EB0A97D71DC0A5FAEA3F7 n=0` + `post-render active=False len=28 culled=False` (13:14:52) — the in-app
   removal event applying and STAYING against the SAME add-raw re-delivering `bodyEmpty=False seen=True` every
   ~3s (13:14:34..:53); the re-delivered raw no longer resurrects the pill (full timeline in the log-evidence
   block below).
   **ORCHESTRATOR ANALYSIS (labeled — not owner input):** **PASS.** The 08-31 revert of the harmful 08-27
   candidate-(a) re-process holds — an own WhatsApp reaction removed in-app is no longer resurrected by the
   re-delivered add raw on the next poll (Screenshot 2 confirms the removal `n=0` applying against the still
   re-delivering stale raws). The owner's REMAINING complaint in this item — "removing reaction in the WhatsApp
   app doesn't remove it in our app" — IS **D15** (a removal made in the WhatsApp app itself does not propagate),
   NOT WR-02. D15 was answered at round 5 as candidate (b) "no removal raw" and is already documented as the
   WhatsApp reaction-removal PLATFORM LIMIT (CLAUDE.md), pending the item-6 probe confirmation.

**— Android-build items (3–5; ONE build — the final Gate A confirmation sweep) —**

3. **D2-view — WhatsApp add/change unaffected (invariant).**
   **expected:** on a WhatsApp bot, add then change a reaction — the pill repaints exactly as before.
   **how-to:** on a WhatsApp bot, add a reaction then change it; confirm the pill updates as it always did.
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** WhatsApp byte-identical invariant (08-30/08-31)
   **owner (2026-07-21):** "pass" (item 3) → WhatsApp add/change reaction repaints unaffected; the 08-30/08-31
   changes are Telegram-only / a glue revert (WhatsApp byte-identical holds).
4. **Telegram add/change/remove still correct (invariant).**
   **expected:** on a Telegram bot, add / change / remove your own reaction in OUR app — count is 1 (not «2»), a
   change leaves ONE pill, a removal clears and stays cleared (the round-2/round-4 fixes still hold).
   **how-to:** on a Telegram bot, add a reaction (count shows 1), change it (one pill), then remove it (clears
   and stays cleared).
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** WhatsApp/Telegram reaction invariants (08-06/08-11/08-17/08-30)
   **owner (2026-07-21):** "add/change/remove reaction in telegram channel in our app works" → the in-app
   Telegram reaction lifecycle (add count 1 / change to one pill / removal clears and stays) still holds; the
   only D2-view failure is the external-change-during-grace repro (item 1), NOT the in-app path.
5. **D17 — late WhatsApp auth on an existing bot shows the WhatsApp sync cover.**
   **expected:** on a bot that already has Telegram, authorize WhatsApp from settings; when you open the bot's
   WhatsApp channel, the ~5-min post-creation sync cover (spinner + progress + countdown) shows over the chat
   list — mirroring the Telegram late-auth cover (D16). Also spot-check: a late Telegram auth on a WhatsApp-first
   bot still shows the Telegram cover (D16 unchanged).
   **how-to:** on a Telegram-only bot, open settings → authorize WhatsApp (code or QR); open the WhatsApp channel
   and confirm the cover shows. Then spot-check the D16 mirror: on a WhatsApp-first bot, authorize Telegram later
   and confirm the Telegram cover still shows.
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 08-DEVICE-UAT.md D17 / 08-REVIEW IN-02
   **owner (2026-07-21):** "ok" (item 5) → **D17 RESOLVED**: a Telegram-first bot that authorizes WhatsApp later
   now shows the WhatsApp post-creation sync cover (the 08-32 `{bot}WhatsappSyncUntil` late-auth stamp fires the
   cover gate) — the exact mirror of 08-28's Telegram stamp; the D16 Telegram-late-auth cover stays unchanged.

**— D15 disposition + nice-to-have (6–7; Editor probe OR build logcat) —**

6. **D15 disposition — no WhatsApp reaction-state key surfaces (platform-limit confirmation).**
   **expected:** with the Editor open (or on the build with logcat), capture the `[D15-probe] wa msgId=…
   reactionsKey=False reactionKey=False` line(s) when a WhatsApp quote/message resolves — AND/OR grep an Editor
   `response.txt` dump for a WhatsApp chat containing a reacted message for any `"reactions"` key on target rows.
   If BOTH come back empty (as predicted): D15 is closed as a Wappi WA platform limit (keep the CLAUDE.md note).
   If a reaction-state key DOES surface: revert the CLAUDE.md note and spin round 7 (absence-based WA reconcile).
   **how-to:** trigger a WhatsApp quote/message resolve on a chat with a reacted message; read the `[D15-probe]`
   line and/or grep the `response.txt` dump for a `"reactions"` key.
   **disposition:** ☐ D15 closed platform-limited (probe empty — `reactionsKey=False reactionKey=False`, keep the
   CLAUDE.md note) ☐ reaction-state key surfaced → round 7 (revert the CLAUDE.md note; absence-based WA reconcile)
   ☑ **probe DID NOT FIRE — UNCONFIRMED (not refuted); platform-limit note STAYS, marked "probe confirmation
   pending"** | **source:** 08-DEVICE-UAT.md D15 / 08-REVIEW IN-01. NOTE: `secrets.json` is deny-ruled — any live
   `messages/id/get` call is owner-run (the app authed in Play Mode, or a direct token call by the owner); the
   executor never handles the token.
   **owner (2026-07-21):** "see screenshot, do not see any reactionsKey=False" (item 6).
   **captured logs (Screenshot 2 — Unity Editor Console):** NO `[D15-probe]` line appears anywhere in the
   session.
   **ORCHESTRATOR ANALYSIS (labeled — not owner input):** **DID NOT FIRE — disposition UNCONFIRMED, not
   refuted.** The `[D15-probe]` seam only triggers on a WhatsApp quoted-reply resolve via QuoteResolve's
   `messages/id/get` — no such resolve happened in this session, so the probe never ran (its absence is
   expected, NOT evidence against the platform-limit disposition). The CLAUDE.md WhatsApp reaction-removal
   platform-limit note STAYS (the round-5 evidence still supports it — no removal raw, no reaction-state key
   observed anywhere), but is now marked "probe confirmation pending". **Round-7 item:** give the probe a
   DETERMINISTIC trigger — e.g. an Editor-only one-shot auto-probe on the FIRST WhatsApp `type:"reaction"` raw
   seen (resolve its TARGET stanza id via the existing authed seam), so no owner choreography is needed to
   confirm-or-refute the disposition.
7. **D2-view residual `[D2-merge]` discriminator (NICE-TO-HAVE).**
   **expected:** if item 1 passes, note whether any `[D2-merge]` line fired during the rapid-change repro
   (ideally none for genuine external changes). Absence is the expected healthy signal.
   **how-to:** watch the Editor Console during item 1's rapid-change repro; record "not fired" or paste any
   `[D2-merge]` lines seen.
   **verdict:** ☐ `[D2-merge]` not fired (healthy — expected) ☑ **fired** (item 1 FAILED — the discriminator
   captured EXACTLY the differ-during-grace suppression it was built to diagnose; see the log-evidence block:
   `[D2-merge] suppressed server-me '🔥' by fresh local '🥺' age=9s` … `'👎' … age=21s`) | **source:** 08-30
   D2-merge log
   **owner (2026-07-21):** "see screenshot." (item 7 — the `[D2-merge]` lines on Screenshot 1).
   **ORCHESTRATOR ANALYSIS (labeled — not owner input):** the discriminator did its job — item 1 is a FAIL and
   the `[D2-merge]` lines pinpoint the residual as the differ-during-grace suppression path (NOT a view repaint
   miss, NOT an exception). This is the diagnostic that makes round 7's grace-discrimination fix targetable.

**Captured log evidence (round 6 — Unity Editor Console; the round-6 repro ran in the EDITOR Play Mode, not on a
device build — two screenshots):**

> Both signals (the D2-view `[D2-merge]` suppression on Screenshot 1, and the WR-02 PASS + probe-absent on
> Screenshot 2) were captured from the Unity Editor **Console** — the round-6 repro ran in the Editor, as round
> 5 established both fix items are Editor-reproducible. A device pass still gates Gate A on the final sweep.

**Screenshot 1 (item 1 D2-view + item 7 `[D2-merge]` discriminator) — timeline:**
- 12:39:57 `[D2-merge] suppressed server-me '🔥' by fresh local '🥺' age=9s`
- 12:39:57 `[TG reaction echo] '👌' [U+1F44C] user_id=1038376805 ownId=1038376805`
- 12:40:00 `[TG reaction echo] '🔥' [U+1F525]` → `[D2-merge] suppressed server-me '🔥' by fresh local '🥺' age=12s`
- 12:40:03 `[TG reaction echo] '🔥'` → `[D2-merge] suppressed server-me '🔥' by fresh local '🥺' age=15s`
- 12:40:06 `[TG reaction echo] '👎' [U+1F44E]` → `[D2-merge] suppressed server-me '👎' by fresh local '🥺' age=18s`
- 12:40:09 `[TG reaction echo] '👎'` → `[D2-merge] suppressed server-me '👎' by fresh local '🥺' age=21s`
- Suppression stack: `TelegramReactionMerge:Merge` (TelegramReactionMerge.cs:79) ← `ChatManager:RefreshCachedMessageReactions` (ChatManager.cs:1862) ← `SyncLatestMessages` (752).
- **KEY:** the confirming echo of the in-app tap (🥺) NEVER arrives — every server-me observed (🔥, then 👎) is the newer Telegram-app value, which DIFFERS from the optimistic 🥺, so the never-clear-on-differ branch suppresses it for the full grace window (age climbing 9s→21s).

**Screenshot 2 (item 2 WR-02 PASS + item 6 probe absent) — timeline:**
- 13:14:32 `[D2-view] reactions changed id=3EB0A97D71DC0A5FAEA3F7 n=1` + `post-render active=True len=28 culled=False` (the ADD applied)
- 13:14:34..13:14:53 `[D15] wa-reaction rawId=3EB09C1E576856CD345288 stanza=3EB0A97D71DC0A5FAEA3F7 bodyEmpty=False seen=True` — the SAME add-raw re-delivering every ~3s (the stale add raw, as round 5 established: no empty-body removal raw ever arrives)
- 13:14:52 `[D2-view] reactions changed id=3EB0A97D71DC0A5FAEA3F7 n=0` + `post-render active=False len=28 culled=False` — the in-app removal applying and STAYING; the re-delivered raw no longer resurrects it → **WR-02 fix CONFIRMED**.
- **NO `[D15-probe]` line anywhere in the session** (no WhatsApp quoted-reply resolve happened → the probe seam never triggered → D15 disposition UNCONFIRMED, not refuted).

**Round-6 Overall:** ☐ PASS (all D2-view / WR-02 / D17 / invariant items PASS **and** the D15 disposition
recorded) ☑ ISSUES (any FAIL) — **RUN 2026-07-21**: 4 PASS (item 2 **WR-02** in-app WhatsApp removal STAYS
removed, item 3 WhatsApp add/change invariant, item 4 Telegram add/change/remove invariant, item 5 **D17**
late-WhatsApp cover), 1 FAIL (item 1 **D2-view** — residual now EXACT: an external own-change that DIFFERS from
the optimistic emoji is suppressed for the whole grace window, `[D2-merge]` discriminator CAPTURED = item 7),
item 6 **D15 probe DID NOT FIRE** (UNCONFIRMED-not-refuted; platform-limit note stays, probe confirmation
pending). G6 resolved, not carried; echo-hex closed, not carried.
**Round-6 Gate A disposition:** ☐ PASS → flip Gate A to PASS, re-aggregate I.3 #10 (01-VERIFICATION sign-off,
previously blocked by D5 — now unblocked), unblock Gates B (prod replication, 08-02) and C (milestone close,
08-03); close D15 as platform-limited (keep the CLAUDE.md note); prod bagkz stays dormant until 08-02. ☑ ISSUES →
**Gate A STAYS ISSUES.** D2-view FAIL (differ-during-grace residual, confirmed by `[D2-merge]`) filed/updated in
§Defects with the anchor + the captured `[D2-merge]` lines; **spin round 7** via `/gsd-plan-phase 08 --gaps`.
Gates B/C + I.3 #10 re-aggregation stay blocked; prod bagkz stays dormant. **Do NOT touch Gates B/C or I.3 #10
this pass.** **Round-7 scope:** **D2-view** grace discrimination so an external own-change that DIFFERS from the
optimistic emoji is never suppressed (candidates: (a) track the DISPLACED emoji and suppress only when server-me
equals it, any THIRD value adopts+clears; (b) clear the grace on reaction-send HTTP success; (c) drastically
shorten the 90s grace); **D15** give the `[D15-probe]` a DETERMINISTIC trigger (Editor-only one-shot auto-probe
on the first WhatsApp `type:"reaction"` raw — resolve its TARGET stanza id via the existing authed seam — to
confirm-or-refute the platform-limit disposition without owner choreography). *G6 resolved, not carried; echo-hex
closed, not carried.*

---

## Round 7 re-verify (2026-07-21) — D2-view rapid-change / stale-echo sanity / WA+TG invariants / D15 deterministic probe (Gate A — the milestone v1.1 gate)

> **PREPARED 2026-07-21 (08-35) — owner run PENDING. Every checkbox below ships BLANK.** Writing this runbook
> block was autonomous; RUNNING it is the owner gate. The phase stays open until the owner records verdicts
> here. **Do NOT tick these on the owner's behalf.** The owner verifies the round-7 fixes — **08-34** (CR-01a
> displaced-emoji discrimination + CR-02 the Reconcile seam that makes it land + WR-01 null-displaced pin + the
> D15 deterministic `[D15-probe]` trigger), all merged on the tree at HEAD `974b66b` — and records exactly ONE
> verdict per item VERBATIM. On all-PASS: flip Gate A → PASS, re-aggregate I.3 #10, unblock Gates B/C, and
> FINALIZE the CLAUDE.md D15 platform-limit note (remove "probe confirmation pending"). On any FAIL: keep Gate A
> = ISSUES, capture the `[D2-merge]`/`[D15-probe]` log line(s) into the §Defects row, and spin round 8 via
> `/gsd-plan-phase 08 --gaps`.
>
> **KEY CHANGE from round 6 — the fix now discriminates by the DISPLACED pre-tap emoji, landing through the
> live-poll call sites (CR-02).** Round 6's `[D2-merge]` showed the never-clear-on-differ branch eating genuine
> external own-changes for the full 90s grace (`🥺→🔥` age=9s→21s climbing) because tapi `reactions[]` is
> current-state-only, so the confirming SAME-emoji echo of the in-app tap never arrived. 08-34 fixes this two
> ways: (1) `TelegramReactionMerge` now suppresses a differing server-me ONLY when it equals the DISPLACED pre-tap
> emoji carried on the persisted optimistic entry (`displacedEmoji`); any THIRD value is a genuine external
> own-change and is adopted+repainted; (2) `RefreshCachedMessageReactions` ALWAYS adopts the reconciled list
> through a pure `Reconcile(cached, server, now, out renderChanged)` seam (all three call sites), so the
> freshness-consumption the round-6 fix silently discarded finally lands. The `[D2-merge]` message now reads
> **"suppressed stale displaced echo '…' under fresh local '…' age=…s"** and fires ONLY on a displaced-matching
> suppress — for a genuine external own-change it must NOT fire.
>
> **Editor-first — Editor Play-Mode is sufficient for the fix items (1–2).** Rounds 5/6 proved the D2-view
> rapid-change repro + the stale-echo sanity reproduce in the Unity Editor Console (Editor-reproducible), so
> **items 1–2 verify in Editor Play Mode** — **OPEN the Unity Editor first** (it is CLOSED as of prep; Play Mode
> needs it open), then run the reaction repros. **ONE Android build only for the final Gate A confirmation sweep**
> (items 3–4: the WA/TG add/change/remove invariants + no-regression device spot-check). The D15 deterministic
> probe (item 5) captures in the Editor on the first WhatsApp `type:"reaction"` raw.
>
> **Pre-build/pre-verify gate (read FRESH — never hardcode):** confirm the EditMode suite is completed/Passed
> with 0 failures at the CURRENT baseline before verifying/building. As of prep the suite is **1191/1191 Passed**
> (headless, `Tools/test-output/headless-summary.json`, at HEAD `974b66b`, Editor closed). Round-7 fixes add
> **+7** over the round-6 baseline **1184** (08-34: Task 1 +6 = displaced third-value adopt ×2, tombstone-
> different-emoji re-add, WR-01 revert pin, VS16 displaced seam, JsonUtility default-null pin; Task 2 +1 = the
> Reconcile through-the-seam test; Task 3 +0 = the Editor-only probe glue; sibling fixtures in both
> TelegramReactionMergeTests.cs and TelegramReactionReceiveTests.cs updated in place, count flat). The suite grows
> concurrently from the parallel phases (9/10/11), so the gate is "completed/Passed, 0 failures at the current
> baseline", never a hardcoded absolute — re-read it at run time (once the Editor is open, the in-Editor bridge
> `Temp/claude/test-summary.json` can re-confirm, or trust the headless 1191 green at HEAD).
>
> **G6 is RESOLVED (no carry)** — the four-checkpoint dev-clone-deactivation streak ended post-08-25; NOT a
> round-7 line item. **The D2-ext echo-hex is CLOSED (no carry)** — captured at round 5 (BASE-form codepoints,
> `user_id == ownId`); NOT a round-7 line item.
>
> **Discriminating logs that make a FAIL diagnosable (round 7):** the Editor-only `[D2-merge] suppressed stale
> displaced echo '…' under fresh local '…' age=…s` log (08-34) fires ONLY when a DISPLACED-matching stale echo is
> suppressed — for a genuine external own-change it should NOT fire; its presence during the rapid-change repro
> means either a displaced-back-to edge (the documented, accepted residual: an external own-change BACK TO the
> displaced emoji within the window is indistinguishable from the stale echo) or a defect. The Editor-only
> `[D15-probe] wa msgId=… reactionsKey=… reactionKey=…` log (08-34) now fires DETERMINISTICALLY on the first
> WhatsApp `type:"reaction"` raw of the session (no quoted-reply choreography). **On ANY FAIL, capture the
> relevant Console/logcat line(s) and paste them into the §Defects row** — that is the whole point of the
> discriminating logs.

**— Editor Play-Mode items (1–2; Editor-reproducible — OPEN the Editor first, no device build needed for the fix loop) —**

1. **D2-view — rapid own-reaction change in the Telegram app repaints EVERY time.**
   **expected:** on a Telegram bot, tap a reaction on a message IN OUR app (arms the optimistic + displaced
   state), then within 90 s change your OWN reaction on that message IN the Telegram app 2–3 times (e.g.
   🥺 → 🔥 → 👎). Each change repaints the in-app bubble pill to the new emoji — NO change is dropped after the
   first (this is the exact round-6 capture, now fixed).
   **how-to:** in the Editor Play Mode on a Telegram bot, tap a reaction in-app, then within 90 s change your own
   reaction on that same message in the Telegram app 2–3 times; watch the in-app pill repaint on every change.
   **IF FAIL:** capture any `[D2-merge] suppressed stale displaced echo '…' under fresh local '…' age=…s` line(s)
   (a genuine external change should NOT log this — its presence means the suppression path is still eating the
   change, unless it is the accepted displaced-back-to edge) plus the `[TG reaction echo]` timeline — paste into
   the Defects row.
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 08-DEVICE-UAT.md D2-view / B9–B13
   **owner (2026-07-21):** "1. seems ok" — after SIX failing rounds the rapid-change repro PASSES: every own-reaction
   change made IN the Telegram app now repaints the in-app bubble pill. The 08-34 displaced-emoji discrimination
   (CR-01a) + the Reconcile always-adopt seam (CR-02) land. Corroborated by the Editor Console screenshot: two
   successive `[D2-view] reactions changed id=3AAFD6395EE4345C8EA0 n=1` events applying within the same session
   (15:06:12 `post-render active=True len=30 culled=False` then 15:06:18 `post-render active=True len=24 culled=False`
   — the new semantics visibly repainting on each change). No `[D2-merge]` displaced-suppress line reported eating a
   genuine change.
2. **Stale-echo defense sanity — no flicker back to the old emoji.**
   **expected:** on a Telegram bot, tap a reaction (or change one) IN OUR app and leave it — the pill stays on
   the new emoji and does NOT flicker back to the previous one while the server echo lags for a poll or two (the
   displaced-match suppress still defends the original round-2 D2 case). It settles cleanly once the server
   confirms.
   **how-to:** in the Editor Play Mode on a Telegram bot, tap/change a reaction in-app and leave it; watch the
   pill through a poll or two and confirm it never flickers back to the old emoji.
   **IF FAIL:** capture the `[D2-merge]`/`[TG reaction echo]` timeline around the flicker — paste into the
   Defects row.
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 08-DEVICE-UAT.md D2 / 08-REVIEW CR-01 (never-clear-on-displaced-match)
   **owner (2026-07-21):** "2. ok" — the original round-2 stale-echo defense still holds: an in-app tap does not
   flicker back to the previous emoji while the server echo lags. The displaced-match suppress defends the stale
   case without eating genuine changes (the whole point of the CR-01a discrimination).

**— Android-build items (3–4; ONE build — the final Gate A confirmation sweep, run only if items 1–2 pass) —**

3. **In-app WhatsApp AND Telegram add/change/remove invariants (ONE build).**
   **expected:** on a WhatsApp bot AND on a Telegram bot, add / change / remove your own reaction IN OUR app —
   count is 1 (not «2»), a change leaves ONE pill, a removal clears and stays cleared. WhatsApp repaints exactly
   as before (byte-identical); the round-2/round-4/round-6 Telegram fixes still hold.
   **how-to:** on a WhatsApp bot add a reaction (count 1), change it (one pill), remove it (clears and stays);
   repeat on a Telegram bot.
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** WhatsApp/Telegram reaction invariants (08-06/08-11/08-17/08-30/08-34)
   **owner (2026-07-21):** "3. ok" — in-app WhatsApp AND Telegram add/change/remove invariants hold: count is 1, a
   change leaves ONE pill, a removal clears and stays cleared. WhatsApp repaints byte-identical; the round-2/4/6
   Telegram fixes still hold under the 08-34 tree.
4. **Final Gate A device sweep — no regression on one build.**
   **expected:** the merged 08-34 build behaves correctly for items 1–3 on a real device (spot-check the
   rapid-change repro once on-device if a live TG-app path is available; otherwise device-verify the invariants
   and rely on the Editor pass for the rapid-change repro). Nothing regressed vs round 6's passing items (WA
   add/change, TG add/change/remove, the D17 cover).
   **how-to:** on the merged 08-34 Android build, spot-check items 1–3 and confirm no regression vs round 6.
   **verdict:** ☑ PASS ☐ FAIL ☐ N/A | **source:** 08-DEVICE-UAT.md §Overall Gate A sweep
   **owner (2026-07-21):** "4. ok" — no regression on the merged 08-34 build; nothing regressed vs round 6's passing
   items (WA add/change, TG add/change/remove, the D17 late-WhatsApp cover). Combined with items 1–3 all PASS, the
   final Gate A sweep is clean.

**— D15 disposition (5; deterministic Editor probe) —**

5. **D15 disposition — deterministic probe fires; no WhatsApp reaction-state key surfaces.**
   **expected:** with the Editor open on a WhatsApp bot, when a reaction raw arrives (someone reacts to a WA
   message, or you react from the WhatsApp phone app), the log shows `[D15-probe] arming target-payload probe for
   stanza=…` followed by `[D15-probe] wa msgId=… reactionsKey=False reactionKey=False`. Both False (as predicted)
   ⇒ D15 is closed as a Wappi WhatsApp platform limit: FINALIZE the CLAUDE.md note (remove "probe confirmation
   pending"). If EITHER key comes back True ⇒ a reaction-state key surfaced: revert nothing yet, keep the note,
   and spin round 8 for an absence-based WA reconcile.
   **how-to:** with the Editor open on a WhatsApp bot, cause a reaction raw to arrive (react to a WA message, or
   from the WhatsApp phone app); read the `[D15-probe]` line for the `reactionsKey`/`reactionKey` booleans.
   **disposition:** ☐ D15 closed platform-limited (probe fired empty — `reactionsKey=False reactionKey=False`;
   FINALIZE the CLAUDE.md note, remove "probe confirmation pending") ☑ **reaction-state key SURFACED** → the
   probe fired deterministically and returned **`reactionsKey=True reactionKey=False`** on the WhatsApp target-message
   payload via `messages/id/get`; per the checkpoint rule "a surfaced key spins an absence-reconcile round" — but
   see the owner Gate-A decision below: **D15 is revised to OPEN-DEFERRED (tracked follow-up, NOT a Gate A blocker)**
   ☐ probe DID NOT FIRE (UNCONFIRMED-not-refuted; note stays "probe confirmation pending") | **source:**
   08-DEVICE-UAT.md D15 / 08-REVIEW IN-01. NOTE: `secrets.json` is deny-ruled — the app is authed in Play Mode; the
   probe reuses the existing authed `messages/id/get` seam, the executor never handles a token.
   **owner (2026-07-21):** "5. screenshot added" — Editor Console screenshot key lines (all @ 15:06:12):
   `[D15] wa-reaction rawId=3A938EDDC110E5E95847 stanza=3AAFD6395EE4345C8EA0 bodyEmpty=False seen=False` →
   `[D15-probe] arming target-payload probe for stanza=3AAFD6395EE4345C8EA0` →
   **`[D15-probe] wa msgId=3AAFD6395EE4345C8EA0 reactionsKey=True reactionKey=False`**. The deterministic probe
   (08-34) fired without owner choreography, and the WhatsApp target-message payload CARRIES a `reactions` key. This
   REVISES the round-5/6 disposition: D15 is **NOT** a Wappi platform limit — the payload exposes reaction state, so
   an absence-based reconcile (mirror of the Telegram approach: fetch/poll the target's current `reactions[]` and
   clear `ReactionStore` on absence) IS implementable. What still stands from the old evidence: an in-WhatsApp-app
   removal emits NO raw in the polled stream (the raw-event pipeline can't see it). What changes: the target payload
   is a second, workable signal. Per the owner Gate-A decision this becomes a **TRACKED FOLLOW-UP** (v1.2 or a
   post-milestone round), NOT a Gate A blocker. **Follow-up first step: capture the `reactions` key's SHAPE** — the
   probe only proved PRESENCE (`reactionsKey=True`), not the array's contents/format needed to reconcile against.

**Round-7 Overall:** ☑ **PASS** (items 1–4 all PASS — D2-view rapid-change repaint, stale-echo sanity, WA+TG
invariants, final device sweep; item 5 D15 disposition recorded — probe fired, `reactionsKey=True` → revised to a
tracked follow-up per the owner Gate-A decision) ☐ ISSUES — **RUN 2026-07-21 (08-35).** Owner verbatim: "1. seems
ok / 2. ok / 3. ok / 4. ok / 5. screenshot added". Every verdict transcribed VERBATIM above, mapped to its source
anchor; the D15 probe evidence (`[D15-probe] wa msgId=3AAFD6395EE4345C8EA0 reactionsKey=True reactionKey=False`)
recorded; D2-view §Defects row → RESOLVED, D15 §Defects row → OPEN-DEFERRED follow-up. *G6 resolved, not carried;
echo-hex closed, not carried.*
**Round-7 Gate A disposition:** ☑ **PASS** → **GATE A FLIPPED TO PASS.** **Owner decision (2026-07-21, asked
explicitly after the probe result): "Flip Gate A now"** — all v1.1 Telegram-parity items pass; close Gate A,
unblock Gates B/C. I.3 #10 (01-VERIFICATION sign-off) re-aggregated to PASS (previously blocked by D5, now
resolved). **Gates B (prod replication, `08-PROD-REPLICATION.md` runbook) and C (milestone close,
`08-MILESTONE-CLOSE.md` runbook) are UNBLOCKED.** The CLAUDE.md D15 note is REVISED (not finalized-as-platform-limit,
because the probe surfaced a key): reaction removal emits no raw in the polled stream, BUT the `messages/id/get`
target payload carries a `reactions` key → absence-based reconcile is possible; a TRACKED FOLLOW-UP (v1.2 or
post-milestone), first step = capture the key's SHAPE. Prod bagkz stays dormant until the 08-PROD-REPLICATION
runbook is run (owner-assisted). The `[D2-merge]` / `[D15-probe]` / `[D15]` Editor diagnostics are tagged for
removal at phase close (08-REVIEW IN-02/IN-03).

---

## Defects found

Log every FAIL here so it can spin its own gap-closure plan and stays traceable to the fix that
must reopen. (Empty = no defects.)

| # | Item (group + number) | Severity | Source-anchor | → gap-closure plan? |
|---|-----------------------|----------|---------------|---------------------|
| D1 | B9a — most TG reactions rejected: HTTP 400 `REACTION_INVALID` from tapi `message/reaction`; only a subset of emoji ever succeeds | medium | 05-VERIFICATION.md #2 | yes — almost certainly Telegram's allowed-reactions platform set: constrain the TG reaction bar to the allowed emoji + revert the optimistic pill (graceful error) on 400 → **RESOLVED @ re-verify 2026-07-17** (08-06: quick-bar + «+» picker picks succeed, no 400) |
| D2 | B9b + B13 — removing an own reaction succeeds in Telegram but NEVER clears in-app (worse than the accepted one-cycle flicker; never self-heals) | medium | 05-VERIFICATION.md #2 / STATE IN-05 (superseded) | yes — 05-06 reconcile merge preserves optimistic 'me' with no removal state → needs a removal tombstone/suppression → **RE-FAIL @ re-verify 2026-07-17 with NEW symptoms** (08-06 tombstone + 08-REVIEW WR-03 grace-window fix shipped, but: an own reaction shows count «2»; changing a reaction can leave BOTH old+new pills; adding ❤ when a reaction already exists renders TWO different heart glyphs. All three are consistent with a reaction-IDENTITY mismatch between the optimistic local emoji and the tapi echo form — VS16/variation-selector or alternate codepoint — so merge/tombstone/count equality misses. Pre-flagged as 08-REVIEW IN-01 + IN-06; capture the exact tapi echo bytes during diagnosis) → **CORE RESOLVED @ re-verify round-2 2026-07-17** (08-16: owner "seems working" — own-reaction count, change-swap, and single-heart-glyph all hold). **NEW RESIDUAL → D2-ext:** reaction changes/removals made IN the Telegram app itself may not reflect in-app (owner: "if i change or remove reaction in telegram itself it may not change in our app", intermittent) |
| D3 | B5 + E1 — video-note presentation: duration-badge left/right corners render SHARP (RoundedCorners refresh?); in a new TG bot a note renders as a round video ON a white background bubble instead of bubble-free | medium | 05-HUMAN-UAT.md #2 / 05-08 note-float | yes — suspect the incoming-bubble transparency path (05-08 tested outgoing) + badge RoundedCorners; repro axis likely incoming vs outgoing → **RESOLVED @ re-verify 2026-07-17** (08-07: B5 badge corners + E1 incoming float both PASS) |
| D4 | B12 + F8 — owner decision: REMOVE the per-row swipe-delete affordance on Telegram rows (the network guard already no-ops; the visual slide must go too) | low (approved scope) | 06-HUMAN-UAT.md Deferred polish | yes — hide the swipe visual on TG rows (ChatItemView / prefab) → **RESOLVED @ re-verify 2026-07-17** (08-08: no TG affordance; WA swipe-delete intact) |
| D5 | I.1 #3 + I.2 #6 + H2 — incoming messages do NOT render in the open chat until re-entering it; «Вместе» cards do not refresh; suggestions are not relevant to the last incoming message (stale transcript in the payload). **Owner-confirmed 2026-07-16: happens on BOTH WhatsApp and Telegram** — not channel-specific | high | 01-HUMAN-UAT.md #3 / 02-HUMAN-UAT.md #2 / 07-HUMAN-UAT.md | yes — diagnose the open-chat live-refresh path end-to-end; acceptance = a new incoming renders in the open chat within one refresh cycle, «Вместе» refreshes, suggestions track the newest message. (Owner long-term preference: push-based delivery — n8n → device notification with the incoming text — instead of polling; recorded as a v2 design item in STATE Deferred Items, NOT this gap) → **CORE RESOLVED @ re-verify 2026-07-17** (08-04: incoming renders within ~one cycle on BOTH channels, «Вместе» cards refresh, typed draft survives; Telegram suggestions relevant). Residual: WhatsApp-channel suggestion RELEVANCE still off → split to **D10** |
| D6 | Extra #1 — NullReferenceException after creating a bot, on the auto-return to the Bots page. Owner-provided stack: `SwipeToDelete.SetContentX` (Assets/Scripts/Chat/SwipeToDelete.cs:157) ← `ResetClosed` (:80) ← `ChatItemView.Bind` (Assets/Scripts/UI/ChatItemView.cs:122) ← `ChatListView.AddChat` (:61) ← `ChatManager.ParseChatsJson` (ChatManager.cs:351) ← `SyncAllChats` (:428) | medium | new (this pass) | yes — null content ref inside SwipeToDelete during row Bind on a fresh sync; fix alongside **D4** (same swipe stack on chat rows) → **RESOLVED @ re-verify 2026-07-17** (08-08: bot-create → Bots return, no NRE) |
| D7 | Extra #3 — one chat DUPLICATED in the Telegram list AND also visible in the WhatsApp list. **Owner clarification 2026-07-16: it is Telegram's own SERVICE dialog** (login codes / device-confirmation messages; likely service user 777000 — confirm in capture): two rows in the TG list, one with the Telegram-logo avatar and one with the default silhouette (⇒ two distinct chat-id forms resolving to the same dialog), and the dialog also shows on the WhatsApp page | high | new (this pass) / CHAT-11 cache isolation | yes — diagnose the double id-form (dedup/normalize) + the cross-channel appearance (cache-root bleed, or a row synthesized under the wrong ActiveChannel) → **RESOLVED @ re-verify 2026-07-17** (08-05: one TG service row, absent from the WA list, no real WA chat lost) |
| D8 | F9 — owner decision: KEEP the RU-localization sweep → Russianise the residual English empty-state copy (IN-09) | low (approved scope) | 06-HUMAN-UAT.md Deferred polish | yes — string sweep → **RESOLVED @ re-verify 2026-07-17** (08-09: all three empty states read in Russian) |
| D9 | Extra #2 (was O1) — the Telegram chat list appears instantly with NO sync/loading indicator on initial load; owner: "the not-good part is that Telegram shows chats instantly with no sync indicator" | low (approved scope) | new (this pass) | yes — add a TG chat-list sync/loading indicator (WhatsApp-parity affordance) → **RE-FAIL @ re-verify 2026-07-17** (08-09 shipped the pill + OnChatListSyncStart/End events around SyncAllChats, but the owner still sees NO indicator — list appears instantly. Suspects: the cached list paints instantly and SyncAllChats finishes before a visible frame; the start event fires before the pill's OnEnable subscription during panel activation; the TG gate reads ActiveChannel at the wrong moment; or z-order/alpha occlusion. Needs runtime diagnosis in Editor first — possibly a minimum-visible-duration if the sync is genuinely that fast) → **SUPERSEDED @ re-verify round-2 2026-07-17 (owner decision)** (08-16: owner questioned the pill — "why we have this pill here at all, and why it is not in whatsapp? when we decided to add it?". Trail: round-1 owner ask O1 [2026-07-16] → pill shipped 08-09 + 08-12 gate → round-2 reversal on device. **Decision: REMOVE the pill**, replaced by the WhatsApp-parity post-creation cover → **D13**. Owner's "pull-to-refresh does nothing" is EXPECTED — that gesture is not implemented in the app [the checkpoint instruction wrongly suggested trying it], not a defect) |
| D10 | H2 (WhatsApp half, split from D5) — «Вместе» suggestions are IRRELEVANT on the WhatsApp channel; Telegram suggestions are relevant, and live refresh + draft protection PASS on both channels | medium | 07-HUMAN-UAT.md / H2 (re-verify 2026-07-17) | yes — diagnose the WhatsApp-side payload (transcript freshness, botWaId/RAG branch inputs) AND the dev-n8n Suggest_Replies WhatsApp branch (prompt/RAG grounding); the Telegram path is the working reference to diff against → **RESOLVED @ re-verify round-2 2026-07-17** (08-16: owner "seems ok" — re-tested against dev n8n with the D10-fixed Suggest_Replies deployed by canonical PUT, activation preserved) |
| D11 | B-group media — SOME video files never download (incl. GIFs and video notes); owner suspects a Wappi/tapi server-side cause | medium | new (re-verify 2026-07-17) | yes — instrument FIRST: capture failing message ids + HTTP status/body from `message/media/download` (expired s3 link? size cap? media type?); if server-side, add graceful retry/error UX + file a Wappi ticket; keep the download queue strictly serial per repo constraint → **RESOLVED @ re-verify round-2 2026-07-17** (08-16: owner "seems ok" — no download failure reproduced this pass, so no `[MediaDownload] FAIL` lines to capture; the 08-15 instrumentation + serial-safe transient retry stay armed for any future failure) |
| D12 | F-group — the Telegram empty-state create-bot CTA does NOTHING; expected: same flow as the WhatsApp CTA but with Telegram preselected in the add-bot form | medium | new (re-verify 2026-07-17) | yes — wire the EmptyStateView create-CTA on the Telegram channel to open AddBotPanel with Telegram preselected (mirror the WhatsApp CTA handler; check whether 05-10/05-12's TG empty-state branches left the click handler unwired for the create reason) → **RE-FAIL @ re-verify round-2 2026-07-17** (08-16: owner "nothing happens when pressing the button" — CONTRADICTS 08-14's opens-with-WhatsApp diagnosis [the `SelectPlatform(1)`→`ActiveChannel` preselect fix]; the CTA is INERT on device, not merely mis-preselected. Round-3 on-device/runtime diagnosis: a raycast blocker over the button, a per-channel empty-state instance with an unwired handler, an `AddBotPanel.Instance` null path, or a swallowed exception) → **PARTIAL RE-FAIL @ re-verify round-3 2026-07-20 (08-21), residual → D12-ext** (owner verbatim: "works, but stops working when whatsapp/telegram chip is switched, both whatsapp and telegram create first bot button stops working." — the 08-18 fix is effective initially; the residual onset is exactly the channel-switch event whose handler 08-18 added) |
| D2-ext | B9/B13 residual — reaction changes/removals performed IN the Telegram app itself may not reflect in our app (intermittent; owner "may not") | medium | new (re-verify round-2 2026-07-17) / D2 | yes (round 3) — HYPOTHESIS (not fact): poll-window absence-vs-removal semantics in `TelegramReactionMerge` — a server-originated reaction delta only reconciles if the message is inside the polled window, and a removal arriving as an empty `reactions[]` (absence) vs an explicit change may be dropped. Diagnose the poll refresh + Merge on server-originated deltas; the D2 echo-hex capture (08-11 `#if UNITY_EDITOR` log) is still wanted → **PARTIAL @ re-verify round-3 2026-07-20 (08-21): data layer RESOLVED, NEW residual → D2-view** (owner verbatim: "it seems working, but sometimes reaction on message bubble in our app is not updated when it is changed in telegram app. logs actually always shows correct reaction but on message bubble it is not changed sometimes (not sure but i guess reaction is not updating appears when i was first changing reaction on one bubble then started to change on another message bubble.)" — the 08-17 merge/reconcile is provably correct [logs always right]; the miss is the VIEW repaint. Echo-hex again NOT captured — downgraded to nice-to-have now the data layer is proven) → **echo-hex CAPTURED @ round-5 2026-07-21 (08-29), ask CLOSED** (owner "screenshot 1 have [TG reaction echo] logs"): the `[TG reaction echo]` Console lines show tapi reaction echoes carry BASE-form codepoints (👍 U+1F44D / 😁 U+1F601 / 👌 U+1F44C — no U+FE0F qualifier) and `user_id == ownId` (1038376805) for own reactions — confirms the round-2 `ReactionEmoji` base-form finding (08-11). This same echo-WITHOUT-event evidence is what relocated **D2-view** upstream (own-reaction event-suppression, not a view miss) |
| D13 | New (this pass) — a freshly-created Telegram bot has NO post-creation cover page with the ~5-min loading slider over the chats list (WhatsApp has it; Telegram doesn't); owner "when telegram bot is just created there is no cover page with 5 minute loading slider on top of chats list page" | medium (owner-approved scope) | new (re-verify round-2 2026-07-17) | yes (round 3) — OWNER DECISION: build the WhatsApp-parity cover for Telegram (full overlay + ~5-min progress slider over the chats list) AND remove the D9 «Синхронизация…» pill as ONE work item. LEAD (confirm before mirroring): the WhatsApp cover is `SyncingState` — built by `Assets/Editor/SyncingStateBuilder.cs` into Screen_Whatsapp/ChatsPanel (ProgressTrack/ProgressFill = its time-based bar), driven at runtime by `Assets/Scripts/UI/SyncingView.cs`; find why it doesn't fire for a Telegram-created bot and mirror it on the Telegram channel → **RESOLVED @ re-verify round-3 2026-07-20 (08-21)** — cover (08-19): owner verbatim "works"; pill removal (08-20): owner verbatim "ok". NEW owner-approved polish spun off → **D14** (TG cover green elements → brand blue) |
| D2-view | D2-ext residual (round-3 2026-07-20) — the reaction DATA always reconciles (logs correct) but the message-bubble VISUAL sometimes misses the update when a reaction is changed in the Telegram app. Owner repro hint: change a reaction on one bubble, then start changing a reaction on ANOTHER message bubble — the second may not repaint | medium | 08-21 / D2-ext (data layer resolved) | yes (round 4) — scope = the VIEW/refresh layer ONLY: the `OnMessageReactionsChanged` → bubble re-render path (event subscription/binding on pooled `MessageItemView` rows, repaint of a non-focused bubble while another bubble's reaction UI is active). Do NOT touch `TelegramReactionMerge`/reconcile — the data layer is proven correct by the owner's log observation → **RE-FAIL @ round-4 2026-07-20 (08-25)** (owner "no pass, still sometimes not updating bubble reaction … even though logs show updated reaction"; 08-22 view-layer deferred re-render on the bar-dismiss path INSUFFICIENT) → 08-26 routed the poll-driven `HandleReactionsChanged` through the SAME hardened re-render + added a `[D2-view] post-render` state log → **RE-FAIL @ round-5 2026-07-21 (08-29), mechanism REFUTED-and-RELOCATED UPSTREAM.** Owner: "still not updating reaction when changed in telegram. logs show right reaction but it doesnt update on message bubble." **Captured `[D2-view]` logs (Screenshot 1):** first remote change fired `[D2-view] reactions changed id=23475 n=1` → `post-render id=23475 active=True len=24 culled=False` (HEALTHY — exception refuted [log fired], cull refuted [culled=False]); but the two SUBSEQUENT emoji changes (😁 U+1F601, 👌 U+1F44C) produced `[TG reaction echo]` lines at Normalize level and **NO `OnMessageReactionsChanged`/`[D2-view]` event at all**. Failure is UPSTREAM of the view — after the first own-user remote reaction applies, subsequent changes stop emitting change events. The 08-22/08-26 view hardening is CORRECT (fixed the one-shot-loss class + its diagnostic proved the relocation) — do NOT re-open it. **Round-6 scope (view layer EXONERATED):** own-reaction event-suppression in `TelegramReactionMerge`'s optimistic-grace window (90s, `_tgOwnUserId`-keyed — `user_id == ownId` in every echo). HYPOTHESIS (labeled): grace suppresses server-observed changes to one's OWN reaction after the first apply; fix = grace keys on a pending LOCAL optimistic set, not mere own-identity, so a REMOTE own-user change to a DIFFERENT emoji is never suppressed; Editor-reproducible EditMode test for echo-without-event → **RE-FAIL @ round-6 2026-07-21 (08-33), residual mechanism now EXACT (confirmed by the `[D2-merge]` discriminator).** Owner: "still same behavior. see screenshot." The 08-30 confirmation-clears-grace assumed a SAME-emoji echo would arrive to consume the grace, but under the rapid-change repro the confirming echo of the in-app tap (🥺) NEVER arrives — tapi `reactions[]` carries only the CURRENT state, so intermediate states never echo. The first observed server-me is already the newer Telegram-app emoji, which DIFFERS from the optimistic one, so the never-clear-on-differ branch eats it for the full grace. **Captured `[D2-merge]` logs (Screenshot 1):** `[D2-merge] suppressed server-me '🔥' by fresh local '🥺' age=9s` → `'🔥' age=12s` → `'🔥' age=15s` → `'👎' age=18s` → `'👎' age=21s` (age climbing; stack `TelegramReactionMerge.cs:79` ← `RefreshCachedMessageReactions` ChatManager.cs:1862 ← `SyncLatestMessages`:752). The merge cannot currently distinguish "stale pre-tap state" from "genuinely newer external change" when both differ from the optimistic emoji. **Working parts (not re-open):** the first-change repaint + items 4 (TG in-app add/change/remove) all PASS — the ONLY residual is external-change-during-grace; the 08-30 view hardening + confirmation-clears-grace stay. **Round-7 scope (grace discrimination):** (a) track the DISPLACED emoji (what the optimistic tap replaced) — suppress ONLY when server-me equals it, any THIRD value adopts+clears; (b) clear the grace on reaction-send HTTP success (any later differing server-me is genuinely newer); (c) drastically shorten the 90s grace → **RESOLVED @ round-7 2026-07-21 (08-35)** (owner "1. seems ok" + "2. ok"): candidate (a) shipped in **08-34** — `MessageReaction.displacedEmoji` rides the pre-tap emoji ON the persisted optimistic entry (CR-01a), `Merge` suppresses a differing server-me ONLY when it equals the displaced value and adopts any THIRD value, and the pure `Reconcile(cached, server, now, out renderChanged)` seam makes `RefreshCachedMessageReactions` ALWAYS adopt so the freshness-consumption finally lands through all three live-poll call sites (CR-02). After SIX failing rounds the rapid-change repro repaints EVERY own-reaction change made in the Telegram app, and the original stale-echo defense still holds (no flicker-back). Editor Console screenshot corroborates: two successive `[D2-view] reactions changed id=3AAFD6395EE4345C8EA0 n=1` events applying with healthy post-renders (`active=True len=30/24 culled=False`) in one session; no `[D2-merge]` line ate a genuine change. Documented residual (accepted v1): an external own-change BACK TO the displaced emoji within the window is indistinguishable from the stale echo. **milestone v1.1 #1 defect CLOSED** |
| D17 | Round-5 item 6 (NEW 2026-07-21, owner SCOPE-OVERRIDE) — a bot that already has Telegram and authorizes WhatsApp LATER shows NO WhatsApp sync cover; the owner overrode the 08-28 parity decision: covers must show for BOTH channels every time a channel is late-added (owner: "when telegram channel exist on bot and then when adding whatsapp channel to same bot there is no sync chats cover page for whatsapp (should be sync chats cover page for both channels every time they are just added)") | medium (owner-approved scope) | 08-29 (round-5) / 08-28 parity decision IN-01 (SUPERSEDED) | yes (round 6) — **SUPERSEDES the 08-28 documented parity decision** (which correctly showed NO WA cover, as designed — NOT a defect against 08-28; this is an owner-approved scope change, exactly like D14). Scope = stamp `{bot}WhatsappSyncUntil` on late WhatsApp auth — the EXACT mirror of 08-28's Telegram stamp: ShowAuthSuccess settings-reauth branch (`else if (!isCreatingBot && Manager.openBot != null)`), gated `authPage == WhatsappAuth`, 300s `WhatsAppSyncWindowSeconds`, reuse `SyncUntilSuffixFor(WhatsApp)` (`Bot.DeleteBot` already clears the WA key — verify) so the WhatsApp cover fires when WhatsApp is connected after Telegram → **RESOLVED @ round-6 2026-07-21 (08-33)** (owner "ok" item 5 — a Telegram-first bot that authorizes WhatsApp later now shows the WhatsApp post-creation sync cover; the 08-32 `{bot}WhatsappSyncUntil` late-auth stamp fires the cover gate, mirroring 08-28's Telegram stamp; the D16 Telegram-late-auth cover stays unchanged) |
| D12-ext | D12 residual (round-3 2026-07-20) — the create-first-bot CTA works initially (08-18 fix effective) but after a WhatsApp↔Telegram chip switch it stops working on BOTH channels (owner: "works, but stops working when whatsapp/telegram chip is switched, both whatsapp and telegram create first bot button stops working.") | medium | 08-21 / D12 | yes (round 4) — LEAD: directly implicates the `OnActiveChannelChanged` re-configure path 08-18 added (`HandleActiveChannelChanged` → `ConfigureForReason(_lastReason)+Show`) — the failure onset is exactly that event, on both channels. Secondary named suspect (pre-flagged, documented-not-fixed in 08-18): `BeginLoadForActiveBot` resolves zero-bots via `FindBotByName(DefaultBotId)==null` → fires a connect-state reason instead of `NoBotsExist` on a channel switch with zero bots. The guarded `[D12]` ENTRY logs remain in place as the diagnosis pivot |
| D14 | New (round-3 2026-07-20, owner-approved polish) — on the Telegram post-creation cover, the green-tinted elements must use Telegram brand blue instead of WhatsApp green (owner: "change the green colored objects at this page to telegrams brand blue. (spinner, sync)") | low (approved scope) | 08-21 / D13 cover | yes (round 4) — recolor the TG cover's spinner + sync-progress elements to brand blue #2AABEE; the established `ChannelAccent.Resolve(channel, authored)` seam (05-10/05-11/05-12) is the pattern — WhatsApp cover stays byte-identical → **RESOLVED @ re-verify round-4 2026-07-20 (08-25)** (owner "PASS" — TG cover reads brand blue, WA cover unchanged green) |
| D15 | Round-4 item 2 (NEW 2026-07-20) — a reaction REMOVED in the WhatsApp app itself is not removed in our app (owner: "i noticed that if in whatsapp itself reaction is removed it is still not removed in our app") | medium | 08-25 (round-4) / D2 (WhatsApp analogue) | yes (round 5) — WA-side reaction-removal propagation was likely NEVER implemented: TG removal semantics were built in **08-17** (`TelegramReactionMerge` absence-vs-removal) but the WhatsApp `ReactionStore` was deliberately left untouched throughout v1.1, so this is **PRE-EXISTING, not a round-4 regression**. Scope = mirror the TG removal-tombstone / absence-vs-removal reconcile on the WhatsApp reaction path (add/change already repaints; the WA repaint check itself was not-regressed-not-confirmed this pass) → 08-27 shipped candidate-(a) already-seen re-process + a `[D15] wa-reaction` shape log → **RE-FAIL @ round-5 2026-07-21 (08-29), removal SHAPE ANSWERED = candidate (b) (no removal raw).** Owner: "still same, removing reaction in whatsaap doesnt remove it in our app." **Captured `[D15]` logs (Screenshot 2):** the ADD arrives (`rawId=3A8976F33979EE5EE8EB stanza=3AAFD6395EE4345C8EA0 bodyEmpty=False seen=False` → applies via `[D2-view] reactions changed` + healthy post-render), then the SAME add-raw re-delivers `bodyEmpty=False seen=True` every poll; **NO empty-body raw EVER arrives after the in-WhatsApp removal**. Candidate (a) (same-id empty-body re-emit) is REFUTED — the 08-27 re-process fix is correct-but-INERT for removal (harmless, idempotent). **Round-6 scope (diagnosis-first):** absence-based reconcile for WhatsApp — poll the target message's `reactions[]` state if Wappi's WA sync exposes it (`messages/id/get` or the `chats/filter` payload) and clear the `ReactionStore` entry when the server no longer carries the reaction; OR document as a Wappi WA platform limit if removal is truly never reflected → **DISPOSITION @ round-6 2026-07-21 (08-33): 08-31 documented it as the WhatsApp reaction-removal PLATFORM LIMIT (CLAUDE.md) + shipped the Editor `[D15-probe]`; the probe DID NOT FIRE this session** (owner: "see screenshot, do not see any reactionsKey=False" — NO `[D15-probe]` line anywhere, because no WhatsApp quoted-reply resolve happened, so the `messages/id/get` probe seam never triggered). The platform-limit disposition is **UNCONFIRMED-not-refuted** — the round-5 evidence (no removal raw, no reaction-state key observed anywhere) still supports it, so the CLAUDE.md note STAYS, marked **"probe confirmation pending"**. **Round-7 item:** give the `[D15-probe]` a DETERMINISTIC trigger (Editor-only one-shot auto-probe on the FIRST WhatsApp `type:"reaction"` raw seen — resolve its TARGET stanza id via the existing authed seam) so the disposition can be confirmed-or-refuted without owner choreography. **Related — WR-02 (08-27 candidate-(a) re-process regression) RESOLVED @ round-6 (08-31 revert):** an own WhatsApp reaction removed IN-APP now STAYS removed across polls (owner item 2 PASS; Screenshot 2 shows the removal `n=0` applying against the still re-delivering stale add-raw) — WR-02 is distinct from D15 (a removal in the WhatsApp app itself, the platform limit) → **DISPOSITION REVISED @ round-7 2026-07-21 (08-35): NOT a platform limit → OPEN-DEFERRED tracked follow-up.** The 08-34 deterministic `[D15-probe]` fired without owner choreography (owner "5. screenshot added") and returned **`[D15-probe] wa msgId=3AAFD6395EE4345C8EA0 reactionsKey=True reactionKey=False`** — the WhatsApp target-message payload from `messages/id/get` CARRIES a `reactions` key. Per the checkpoint's own rule ("a surfaced key spins an absence-reconcile round") the round-5/6 platform-limit disposition is now WITHDRAWN. What still stands: an in-WhatsApp-app removal emits NO raw in the polled stream (the raw-event pipeline can't see it). What changed: the target payload is a second, workable signal, so an absence-based reconcile (mirror of the Telegram approach — fetch/poll the target's current `reactions[]`, clear `ReactionStore` on absence) IS implementable. **Per owner Gate-A decision ("Flip Gate A now") this is a TRACKED FOLLOW-UP (v1.2 or a post-milestone round), NOT a Gate A blocker.** Follow-up scope: (1) FIRST capture the `reactions` key's SHAPE — the probe only proved PRESENCE, not the array contents/format; (2) then build the absence-based WA reconcile against that shape. Recorded in STATE Accumulated Context + Deferred Items |
| D16 | Round-4 item 4 (NEW 2026-07-20) — when a bot already has WhatsApp and Telegram is added later, the Telegram post-creation sync cover never shows (owner: "if whatsapp channel exists and telegram channel is created its sunc cover page is not shown") | medium | 08-25 (round-4) / D13 (08-19 late-channel follow-up) | yes (round 5) — **PROMOTION of a documented 08-19 follow-up, NOT a regression**: 08-19-SUMMARY.md explicitly noted "late-channel auth stamps NO window on EITHER channel (exact parity; follow-up if ever wanted)" — the wizard tail is the only site that stamps `{bot}TelegramSyncUntil`. Scope = stamp the per-channel sync window at the late-auth completion site (BotSettings Telegram auth success) so the cover fires when Telegram is connected after WhatsApp → 08-28 stamped `{bot}TelegramSyncUntil` in the ShowAuthSuccess settings-reauth branch (Telegram-gated) → **RESOLVED @ round-5 2026-07-21 (08-29)** (owner "pass" item 5 — a WhatsApp-first bot that authorizes Telegram later now shows the Telegram post-creation sync cover). NOTE: the mirror WhatsApp late-auth cover (owner scope-override on the byte-identical check) → new **D17** |

**Observations — resolved 2026-07-16:**

- **O1 (Extra #2) → promoted to D9:** owner clarified — "the not-good part is that Telegram
  shows chats instantly with no sync indicator". Filed as **D9** (TG chat-list sync indicator).

**Pre-device notes (trivially-obvious fixes surfaced while authoring — NOT folded in):** none.

## Overall result

**Overall:** ☑ **PASS (GATE A PASSED @ round 7, 2026-07-21)** ☐ ISSUES

> **Final disposition (round 7, 08-35):** all v1.1 Telegram-parity items PASS. **D2-view RESOLVED** after six
> failing rounds (08-34 displaced-emoji discrimination + Reconcile always-adopt seam). **D15 REVISED to a tracked
> follow-up** (probe surfaced `reactionsKey=True` → absence-based reconcile is possible; NOT a platform limit, NOT a
> Gate A blocker — owner decision "Flip Gate A now"). **Gate A → PASS unblocks Gate B (`08-PROD-REPLICATION.md`) +
> Gate C (`08-MILESTONE-CLOSE.md`); I.3 #10 re-aggregated to PASS.** Prod bagkz stays dormant until the
> 08-PROD-REPLICATION runbook is run (owner-assisted). The historical ISSUES narrative below is retained for the
> audit trail.

- **Result (rounds 1–6, historical):** ISSUES — 9 defect/scope rows (D1–D9): 2 high (D5 live-incoming render on BOTH
  channels — folds in H2's stale-suggestion relevance; D7 TG service-dialog duplication +
  cross-channel bleed), 4 medium (D1 REACTION_INVALID, D2 reaction-removal never clears,
  D3 video-note presentation, D6 bot-creation NRE — stack lands in SwipeToDelete), 3
  owner-approved polish scopes (D4 remove TG swipe affordance, D8 RU empty-state copy,
  D9 TG chat-list sync indicator). Everything else green: auth/2FA (A), chat core
  (B1–4/6/8/10–12), 05-09 fixes (C), vthumb probe (D, low-confidence), switcher (F1–7),
  auto-reply e2e (G1–4), dashboard (H3/4/5), carried v1.0 (I mostly PASS).
- **Re-deferred carried-v1.0 items (with reasons):** I.3 #10 formal 01-VERIFICATION sign-off —
  RE-DEFER: blocked by D5 (I.1 #3 unconfirmable until live-incoming render is fixed);
  re-aggregate after gap closure.
- **Re-verify 2026-07-17 (08-10, ONE Android build @ 1b2e60b):** 7/9 defects RESOLVED on device
  (D1, D3a+D3b, D4, D5-core, D6, D7, D8) and B7 static-webp now PASS (was N/A). Still open:
  **D2** (re-fail, refined: reaction-identity/VS16 mismatch — count «2» on an own reaction,
  changed reaction leaves both pills, two different heart glyphs) and **D9** (re-fail: pill never
  visible). New this pass: **D10** (WA-channel «Вместе» relevance, split from D5/H2), **D11**
  (some video/GIF/video-note downloads never complete — tapi suspect), **D12** (TG create-bot
  CTA dead). **Gate A stays ISSUES** — next round: `/gsd-plan-phase 08 --gaps` for
  D2/D9/D10/D11/D12. I.3 #10 stays re-deferred (I.1 #3 itself confirmed on device; re-aggregate
  once D10 closes).
- **Round-3 re-verify 2026-07-20 (08-21, ONE Android build off the post-08-20 tree — fixes
  08-17..08-20 all included):** **D13 fully RESOLVED** (cover "works" + pill "ok" — both halves of
  the 08-16 owner decision); **D2-ext data layer RESOLVED** (owner: logs always show the correct
  reaction) with NEW residual **D2-view** (the bubble VISUAL sometimes misses the repaint; owner
  repro hint: change a reaction on one bubble, then start changing on ANOTHER — the second may not
  repaint; round-4 scope = view/refresh layer, NOT the merge); **D12 PARTIAL RE-FAIL → D12-ext**
  (CTA works until a WhatsApp↔Telegram chip switch, then dies on BOTH channels — lead: 08-18's
  `OnActiveChannelChanged` re-configure path); NEW owner-approved polish **D14** (TG cover green
  elements → Telegram brand blue). **Gate A stays ISSUES** — round 4: `/gsd-plan-phase 08 --gaps`
  for D2-view / D12-ext / D14. Echo-hex NOT captured again (downgraded to nice-to-have — data layer
  proven); G6 dev-clone deactivation STILL OUTSTANDING (third consecutive checkpoint); prod bagkz
  untouched. The 08-20 1136-green gate was SUPERSEDED by later suite growth (Editor Bee crash
  resolved post-checkpoint; phase-9/11 sessions ran the grown suite green — 1165 @ 11-01,
  1170 @ 09-03).
- **Round-4 re-verify 2026-07-20 (08-25, ONE Android build off the post-08-24 tree — fixes 08-22
  D2-view + 08-23 D12-ext + 08-24 D14 all merged):** **Overall ISSUES** — 3 PASS, 1 FAIL, 2 NEW defects.
  Pre-build gate had been MET (EditMode **1176/1176 Passed** FRESH; baseline 1170 + 6). **D12-ext CTA
  RESOLVED** (owner "pass" — create-bot CTA survives a WA↔TG chip switch on both channels); **D14
  RESOLVED both checks** (owner "PASS" — fresh TG cover reads brand blue #2AABEE, WhatsApp cover
  unchanged green). **D2-view STILL FAILS** (owner: "no pass, still sometimes not updating bubble
  reaction when it is updated in telegram even though logs show updated reaction" — the repro is a
  reaction changed IN the Telegram app arriving via the live poll, with the new `[D2-view]` log firing
  correctly, yet the bubble pill sometimes doesn't repaint; round-5 scope = the poll-driven
  `HandleReactionsChanged` repaint path, NOT the merge). NEW **D15** (owner: a reaction REMOVED in the
  WhatsApp app itself is not removed in our app — pre-existing, WA removal propagation likely never
  implemented) + NEW **D16** (owner: a bot that already has WhatsApp shows NO Telegram sync cover when
  Telegram is added later — promotion of the documented 08-19 late-channel follow-up, not a regression).
  **Gate A stays ISSUES** — round 5: `/gsd-plan-phase 08 --gaps` for D2-view / D15 / D16. G6 dev-clone
  deactivation STILL OUTSTANDING (**FOURTH consecutive checkpoint** — owner asked "what exactly should be
  done?"; explained, awaiting confirmation; carry BLOCKING). Echo-hex NOT captured again (third
  consecutive; nice-to-have — data layer proven). Gates B/C + I.3 #10 re-aggregation stay blocked;
  prod bagkz stays dormant.
- **Round-5 re-verify 2026-07-21 (08-29, one build off the post-08-28 tree — fixes 08-26 D2-view poll-path
  re-render + 08-27 D15 WA reaction-removal ingest + 08-28 D16 late-TG sync-cover stamp all merged; repro ran
  in the Unity EDITOR — screenshots are the Editor Console):** **Overall ISSUES** — 3 PASS, 2 FAIL, 1 owner
  scope-override, echo-hex CAPTURED. **D16 late-TG cover RESOLVED** (owner "pass" item 5 — a WhatsApp-first bot
  authorizing Telegram later now shows the Telegram sync cover); D2-view WA add/change + D15 WA add/change both
  PASS (items 2/4, WhatsApp byte-identical holds). **D2-view STILL FAILS but the mechanism is REFUTED-and-RELOCATED
  UPSTREAM** (owner: "still not updating reaction when changed in telegram. logs show right reaction but it doesnt
  update on message bubble." — captured `[D2-view]` logs show a HEALTHY post-render on the first change [active=True
  len=24 culled=False] but NO change event AT ALL for the two subsequent emoji changes, only `[TG reaction echo]`
  at Normalize level → the view layer is EXONERATED; round-6 target = own-reaction event-suppression in
  `TelegramReactionMerge`'s 90s optimistic-grace window). **D15 STILL FAILS, removal SHAPE ANSWERED = candidate (b)
  no removal raw** (owner: "still same, removing reaction in whatsaap doesnt remove it in our app." — captured
  `[D15]` logs show the ADD raw re-delivering `bodyEmpty=False seen=True` every poll, NO empty-body raw ever after
  the in-WhatsApp removal → 08-27's candidate-(a) fix is correct-but-inert; round-6 target = absence-based WA
  reconcile OR documented Wappi platform limit). **NEW D17** (owner scope-override on the byte-identical check —
  "should be sync chats cover page for both channels every time they are just added" — SUPERSEDES the 08-28 parity
  decision; mirror the 08-28 Telegram stamp with `{bot}WhatsappSyncUntil` on late WhatsApp auth). **Echo-hex CAPTURED
  at last** (item 7, ask CLOSED — tapi echoes carry BASE-form codepoints U+1F44D/U+1F601/U+1F44C, `user_id == ownId`;
  the echo-without-event evidence is what relocated D2-view). **Gate A STAYS ISSUES** — round 6:
  `/gsd-plan-phase 08 --gaps` for D2-view (upstream event-suppression) / D15 (absence-based WA reconcile or platform
  limit) / D17 (late-WA-auth cover stamp). G6 resolved (not carried). The Editor reproduces both FAILs → round-6
  fix loop is Editor-reproducible (no device build needed for the fix; a device pass still gates Gate A). Gates B/C +
  I.3 #10 re-aggregation stay blocked; prod bagkz stays dormant.
- **Round-6 re-verify 2026-07-21 (08-33, Editor Play-Mode repro off the post-08-32 tree — fixes 08-30 D2-view
  confirmation-clears-grace + WR-01 tombstone / 08-31 WR-02 revert + D15 platform-limit disposition + `[D15-probe]`
  / 08-32 D17 late-WhatsApp-auth cover stamp all merged; two Unity Editor Console screenshots):** **Overall ISSUES**
  — 4 PASS, 1 FAIL, 1 probe-did-not-fire. **D17 RESOLVED** (owner "ok" item 5 — a Telegram-first bot authorizing
  WhatsApp later now shows the WhatsApp sync cover); **WR-02 RESOLVED** (owner item 2 — an own WhatsApp reaction
  removed IN-APP STAYS removed across polls; Screenshot 2 shows the removal `n=0` applying against the still
  re-delivering stale add-raw); WhatsApp add/change (item 3) + Telegram add/change/remove (item 4) invariants both
  PASS. **D2-view STILL FAILS, residual now EXACT** (owner "still same behavior. see screenshot" — the `[D2-merge]`
  discriminator built in 08-30 fires on EVERY subsequent own-change: `suppressed server-me '🔥' by fresh local '🥺'
  age=9s` … `'👎' age=21s`, climbing; the confirming echo of the in-app tap 🥺 NEVER arrives [tapi `reactions[]` is
  current-state-only], so the first server-me is already the newer Telegram-app emoji which DIFFERS from the
  optimistic one → the never-clear-on-differ branch eats it for the full grace). **D15 probe DID NOT FIRE**
  (owner "do not see any reactionsKey=False" — NO `[D15-probe]` line, because no WhatsApp quoted-reply resolve
  triggered the `messages/id/get` probe seam; disposition UNCONFIRMED-not-refuted, CLAUDE.md platform-limit note
  STAYS marked "probe confirmation pending"). **Gate A STAYS ISSUES** — round 7: `/gsd-plan-phase 08 --gaps` for
  D2-view (grace discrimination: track the DISPLACED emoji / clear grace on send-HTTP-success / shorten the 90s
  grace) + D15 (give the `[D15-probe]` a deterministic Editor-only trigger on the first WhatsApp `type:"reaction"`
  raw). G6 resolved (not carried); echo-hex closed (not carried). Do NOT touch Gates B/C or I.3 #10 this pass;
  prod bagkz stays dormant.
- **Round-7 re-verify 2026-07-21 (08-35, Editor Play-Mode + probe evidence off the post-08-34 tree — fixes 08-34
  CR-01a displaced-emoji discrimination + CR-02 Reconcile always-adopt seam + WR-01 null-displaced pin + the D15
  deterministic `[D15-probe]` trigger, all merged; one Unity Editor Console screenshot):** **Overall PASS — GATE A
  PASSED.** Owner verbatim: "1. seems ok / 2. ok / 3. ok / 4. ok / 5. screenshot added". **D2-view RESOLVED** (item
  1 — after six failing rounds, every own-reaction change made IN the Telegram app repaints the in-app bubble pill;
  the displaced-emoji discrimination distinguishes a stale echo from a genuine external change, and the Reconcile
  seam makes the freshness land through all three live-poll call sites); stale-echo sanity (item 2), WA+TG
  add/change/remove invariants (item 3), and the final device sweep (item 4) all PASS. **D15 disposition REVISED**
  (item 5) — the deterministic `[D15-probe]` fired and returned `[D15-probe] wa msgId=3AAFD6395EE4345C8EA0
  reactionsKey=True reactionKey=False`: the WhatsApp target-message payload (`messages/id/get`) DOES carry a
  `reactions` key, so D15 is **NOT** a Wappi platform limit — an absence-based reconcile (mirror of the Telegram
  approach) is implementable. Per the owner's explicit Gate-A decision ("Flip Gate A now") D15 becomes an
  **OPEN-DEFERRED tracked follow-up** (v1.2 or post-milestone; first step = capture the key's SHAPE), NOT a Gate A
  blocker. **Gate A → PASS:** I.3 #10 (01-VERIFICATION formal sign-off) re-aggregated to PASS (blocker D5 resolved);
  **Gate B (`08-PROD-REPLICATION.md` runbook) and Gate C (`08-MILESTONE-CLOSE.md` runbook) UNBLOCKED**; the CLAUDE.md
  D15 note revised (removal emits no raw, but the target payload carries reaction state → follow-up). Prod bagkz
  stays dormant until the 08-PROD-REPLICATION runbook is run (owner-assisted). G6 resolved (not carried); echo-hex
  closed (not carried). The `[D2-merge]` / `[D15-probe]` / `[D15]` Editor diagnostics stay tagged for removal at
  phase close (08-REVIEW IN-02/IN-03).
- **Notes:** B7 static-webp N/A (no sample at hand); G5 N/A (no stale clone to test);
  G6 n/a with an OUTSTANDING reminder to deactivate the test clone (bot-activation policy);
  H2 FAIL — downstream of D5, re-test relevance + RAG grounding together after the fix;
  prod bagkz untouched throughout. Owner clarifications (D5 both-channels, D6 stack,
  D7 service-dialog identity, O1→D9) received and folded in 2026-07-16.

> Any **FAIL** spins its own gap-closure plan — run `/gsd-plan-phase 08 --gaps` and file the
> specifics from the Defects table. Do NOT hand-patch fixes here; pre-planning those fixes is out
> of scope for this runbook. On a green Overall (or FAILs filed as gaps + carried items re-deferred
> with reasons), this gate closes and the milestone-closeout mechanics proceed.

---
*Consolidated device-UAT gate for milestone v1.1 (Telegram Parity). Aggregates: Phase 5
(05-HUMAN-UAT.md, 05-VERIFICATION.md, 05-06-REVIEW WR-02, 05-08/05-09 refinements), Phase 6
(06-HUMAN-UAT.md), Phase 4 (04-HUMAN-UAT.md), Phase 7 (07-HUMAN-UAT.md), and carried v1.0
(01-HUMAN-UAT.md, 02-HUMAN-UAT.md, 01-VERIFICATION.md). Owner-run — do NOT tick on the owner's behalf.*

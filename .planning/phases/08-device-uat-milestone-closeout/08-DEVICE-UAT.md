# Phase 8 — Device UAT: v1.1 Telegram Parity milestone gate (consolidated, owner-run)

**Status:** RUN 2026-07-15/16 — **Overall: ISSUES** (9 defect/scope rows D1–D9 in §Defects found; owner clarifications for D5/D6/D7/D9 recorded 2026-07-16). This pass is the single source of truth for "is v1.1 shippable."

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
    **verdict:** ☐ PASS ☐ FAIL ☑ RE-DEFER (reason: I.1 #3 blocked by D5 — live-incoming render;
    re-aggregate after gap closure) | **source:** 01-VERIFICATION.md (human_verification ×4)

---

## Defects found

Log every FAIL here so it can spin its own gap-closure plan and stays traceable to the fix that
must reopen. (Empty = no defects.)

| # | Item (group + number) | Severity | Source-anchor | → gap-closure plan? |
|---|-----------------------|----------|---------------|---------------------|
| D1 | B9a — most TG reactions rejected: HTTP 400 `REACTION_INVALID` from tapi `message/reaction`; only a subset of emoji ever succeeds | medium | 05-VERIFICATION.md #2 | yes — almost certainly Telegram's allowed-reactions platform set: constrain the TG reaction bar to the allowed emoji + revert the optimistic pill (graceful error) on 400 → **RESOLVED @ re-verify 2026-07-17** (08-06: quick-bar + «+» picker picks succeed, no 400) |
| D2 | B9b + B13 — removing an own reaction succeeds in Telegram but NEVER clears in-app (worse than the accepted one-cycle flicker; never self-heals) | medium | 05-VERIFICATION.md #2 / STATE IN-05 (superseded) | yes — 05-06 reconcile merge preserves optimistic 'me' with no removal state → needs a removal tombstone/suppression → **RE-FAIL @ re-verify 2026-07-17 with NEW symptoms** (08-06 tombstone + 08-REVIEW WR-03 grace-window fix shipped, but: an own reaction shows count «2»; changing a reaction can leave BOTH old+new pills; adding ❤ when a reaction already exists renders TWO different heart glyphs. All three are consistent with a reaction-IDENTITY mismatch between the optimistic local emoji and the tapi echo form — VS16/variation-selector or alternate codepoint — so merge/tombstone/count equality misses. Pre-flagged as 08-REVIEW IN-01 + IN-06; capture the exact tapi echo bytes during diagnosis) |
| D3 | B5 + E1 — video-note presentation: duration-badge left/right corners render SHARP (RoundedCorners refresh?); in a new TG bot a note renders as a round video ON a white background bubble instead of bubble-free | medium | 05-HUMAN-UAT.md #2 / 05-08 note-float | yes — suspect the incoming-bubble transparency path (05-08 tested outgoing) + badge RoundedCorners; repro axis likely incoming vs outgoing → **RESOLVED @ re-verify 2026-07-17** (08-07: B5 badge corners + E1 incoming float both PASS) |
| D4 | B12 + F8 — owner decision: REMOVE the per-row swipe-delete affordance on Telegram rows (the network guard already no-ops; the visual slide must go too) | low (approved scope) | 06-HUMAN-UAT.md Deferred polish | yes — hide the swipe visual on TG rows (ChatItemView / prefab) → **RESOLVED @ re-verify 2026-07-17** (08-08: no TG affordance; WA swipe-delete intact) |
| D5 | I.1 #3 + I.2 #6 + H2 — incoming messages do NOT render in the open chat until re-entering it; «Вместе» cards do not refresh; suggestions are not relevant to the last incoming message (stale transcript in the payload). **Owner-confirmed 2026-07-16: happens on BOTH WhatsApp and Telegram** — not channel-specific | high | 01-HUMAN-UAT.md #3 / 02-HUMAN-UAT.md #2 / 07-HUMAN-UAT.md | yes — diagnose the open-chat live-refresh path end-to-end; acceptance = a new incoming renders in the open chat within one refresh cycle, «Вместе» refreshes, suggestions track the newest message. (Owner long-term preference: push-based delivery — n8n → device notification with the incoming text — instead of polling; recorded as a v2 design item in STATE Deferred Items, NOT this gap) → **CORE RESOLVED @ re-verify 2026-07-17** (08-04: incoming renders within ~one cycle on BOTH channels, «Вместе» cards refresh, typed draft survives; Telegram suggestions relevant). Residual: WhatsApp-channel suggestion RELEVANCE still off → split to **D10** |
| D6 | Extra #1 — NullReferenceException after creating a bot, on the auto-return to the Bots page. Owner-provided stack: `SwipeToDelete.SetContentX` (Assets/Scripts/Chat/SwipeToDelete.cs:157) ← `ResetClosed` (:80) ← `ChatItemView.Bind` (Assets/Scripts/UI/ChatItemView.cs:122) ← `ChatListView.AddChat` (:61) ← `ChatManager.ParseChatsJson` (ChatManager.cs:351) ← `SyncAllChats` (:428) | medium | new (this pass) | yes — null content ref inside SwipeToDelete during row Bind on a fresh sync; fix alongside **D4** (same swipe stack on chat rows) → **RESOLVED @ re-verify 2026-07-17** (08-08: bot-create → Bots return, no NRE) |
| D7 | Extra #3 — one chat DUPLICATED in the Telegram list AND also visible in the WhatsApp list. **Owner clarification 2026-07-16: it is Telegram's own SERVICE dialog** (login codes / device-confirmation messages; likely service user 777000 — confirm in capture): two rows in the TG list, one with the Telegram-logo avatar and one with the default silhouette (⇒ two distinct chat-id forms resolving to the same dialog), and the dialog also shows on the WhatsApp page | high | new (this pass) / CHAT-11 cache isolation | yes — diagnose the double id-form (dedup/normalize) + the cross-channel appearance (cache-root bleed, or a row synthesized under the wrong ActiveChannel) → **RESOLVED @ re-verify 2026-07-17** (08-05: one TG service row, absent from the WA list, no real WA chat lost) |
| D8 | F9 — owner decision: KEEP the RU-localization sweep → Russianise the residual English empty-state copy (IN-09) | low (approved scope) | 06-HUMAN-UAT.md Deferred polish | yes — string sweep → **RESOLVED @ re-verify 2026-07-17** (08-09: all three empty states read in Russian) |
| D9 | Extra #2 (was O1) — the Telegram chat list appears instantly with NO sync/loading indicator on initial load; owner: "the not-good part is that Telegram shows chats instantly with no sync indicator" | low (approved scope) | new (this pass) | yes — add a TG chat-list sync/loading indicator (WhatsApp-parity affordance) → **RE-FAIL @ re-verify 2026-07-17** (08-09 shipped the pill + OnChatListSyncStart/End events around SyncAllChats, but the owner still sees NO indicator — list appears instantly. Suspects: the cached list paints instantly and SyncAllChats finishes before a visible frame; the start event fires before the pill's OnEnable subscription during panel activation; the TG gate reads ActiveChannel at the wrong moment; or z-order/alpha occlusion. Needs runtime diagnosis in Editor first — possibly a minimum-visible-duration if the sync is genuinely that fast) |
| D10 | H2 (WhatsApp half, split from D5) — «Вместе» suggestions are IRRELEVANT on the WhatsApp channel; Telegram suggestions are relevant, and live refresh + draft protection PASS on both channels | medium | 07-HUMAN-UAT.md / H2 (re-verify 2026-07-17) | yes — diagnose the WhatsApp-side payload (transcript freshness, botWaId/RAG branch inputs) AND the dev-n8n Suggest_Replies WhatsApp branch (prompt/RAG grounding); the Telegram path is the working reference to diff against |
| D11 | B-group media — SOME video files never download (incl. GIFs and video notes); owner suspects a Wappi/tapi server-side cause | medium | new (re-verify 2026-07-17) | yes — instrument FIRST: capture failing message ids + HTTP status/body from `message/media/download` (expired s3 link? size cap? media type?); if server-side, add graceful retry/error UX + file a Wappi ticket; keep the download queue strictly serial per repo constraint |
| D12 | F-group — the Telegram empty-state create-bot CTA does NOTHING; expected: same flow as the WhatsApp CTA but with Telegram preselected in the add-bot form | medium | new (re-verify 2026-07-17) | yes — wire the EmptyStateView create-CTA on the Telegram channel to open AddBotPanel with Telegram preselected (mirror the WhatsApp CTA handler; check whether 05-10/05-12's TG empty-state branches left the click handler unwired for the create reason) |

**Observations — resolved 2026-07-16:**

- **O1 (Extra #2) → promoted to D9:** owner clarified — "the not-good part is that Telegram
  shows chats instantly with no sync indicator". Filed as **D9** (TG chat-list sync indicator).

**Pre-device notes (trivially-obvious fixes surfaced while authoring — NOT folded in):** none.

## Overall result

**Overall:** ☐ PASS ☑ ISSUES

- **Result:** ISSUES — 9 defect/scope rows (D1–D9): 2 high (D5 live-incoming render on BOTH
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

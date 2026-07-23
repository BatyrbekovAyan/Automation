# Automation — WhatsApp/Telegram AI Bot Manager

## What This Is

A Unity 6 mobile app (iOS/Android) that lets small-business owners in the CIS market run a no-code AI chatbot on their **own** WhatsApp/Telegram number. The owner creates and configures bots (products, services, prompts), and a full in-app WhatsApp chat client lets them monitor every conversation and take over any chat at will. n8n workflows power the automation behind each bot.

v1.0 shipped the **semi-auto reply path**: a per-chat «Вместе» mode where a Reply Suggestions Panel proposes 4 ranked reply *moves* (closed 6-label taxonomy, grounded in the bot's catalog/RAG via a live n8n + gpt-4o-mini workflow) that the owner picks, refines in the composer, and sends — the hands-on end of the automation↔semi-auto spectrum. Suggestions never auto-send.

## Core Value

The owner stays in control along a spectrum from fully autonomous to hands-on: the bot can answer on its own, **or** propose replies the owner picks and refines — without ever losing trust or the ability to take over a conversation.

## Current Milestone: v1.1 Telegram Parity

**Goal:** A Telegram-authed bot works end-to-end exactly like a WhatsApp one — chat client, n8n auto-replies, «Вместе» suggestions, dashboard — on the Wappi tapi API.

**Target features:**
- Channel-aware chat pipeline (ChatManager seam: profile resolution, api/sync↔tapi/sync bases, per-channel caches, empty states, tapi parser divergences)
- In-screen WhatsApp|Telegram channel switcher on the chats screen (Telegram bottom tab removed)
- n8n Telegram_Bot template fixed to tapi (outbound URLs, type:"text", sessionKey, voice duration) + RAG re-stamp on late channel auth
- «Вместе» suggestions channel-aware (client payload v1.1 + channel-branched RAG filter)
- Telegram 2FA auth fix (detail:"2fa" → auth/2fa step)
- Dashboard «Сводка» includes Telegram profiles (bot-level chips, channel-aware deep-link)

**Design spec:** `docs/superpowers/specs/2026-07-12-telegram-parity-design.md` (decisions D1–D7 + owner veto points §9)
**Research:** `.planning/research/telegram-parity/` (7 deep-read reports + Wappi tapi docs extract)
**Strategy (locked):** Telegram ships on Wappi tapi; official business-bots path PARKED (client-side Premium paywall — memory `telegram-channel-strategy`)

**Cross-milestone state (v1.2 Reply-Trigger Discipline):** BOTH phases COMPLETE 2026-07-22. Phase 9 semi-auto-suppression — `reply_mode_flags` live (default-deny RLS), `/webhook/SetReplyMode` deployed (`SCLcpn6DMDG3Z4VN`), fail-closed gate runData-verified both channels, device HUMAN-UAT all 5 scenarios PASS («Вместе»=no reply+unread+suggestions; «Авто» restores; '*' default; absence→reply; activation switch independent). Phase 10 message-batching-debounce — multi-fragment messages produce one combined auto-reply (both channels, runData + device verified). Phase-10 UAT debt (suggestions-coalesce on-device + composition) is now UNBLOCKED by SetReplyMode being live — re-verify via 10-HUMAN-UAT.md. Code-review debt: 09-REVIEW.md 0C/4W (WR-01 heal tri-state collapse, WR-02 post-auth '*' gap) → /gsd-code-review-fix 9 available; /gsd-secure-phase 9 pending.

## Requirements

### Validated

<!-- Existing capabilities inferred from the codebase map (.planning/codebase/). -->

- ✓ Bot creation wizard (channel → name → WhatsApp/Telegram auth → business → summary → confirmation) — existing
- ✓ Bot CRUD, activation toggle, and per-bot persistence in PlayerPrefs — existing
- ✓ WhatsApp chat client via Wappi: chat list + pagination, send, media, quoted replies, reactions, chat delete — existing
- ✓ Bot configuration UI (General / Business / Products / Services / Prompts) — existing
- ✓ Per-bot n8n workflow automation (create / edit / activate WhatsApp & Telegram workflows) — existing
- ✓ Autonomous automation mode (bot answers customers via n8n) — existing

- ✓ Per-chat **semi-auto toggle** + Reply Suggestions Panel (4 ranked cards, intent labels, Recommended badge, no numeric confidence) — v1.0
- ✓ Auto-populate on incoming, manual refresh, tap-to-composer (never auto-sends), re-cluster steering loop — v1.0
- ✓ UI proven on mock data behind the `ISuggestionsProvider` seam — v1.0
- ✓ n8n emits live suggestions (shared `Suggest Replies` webhook workflow: RAG + gpt-4o-mini, closed 6-move enum, injection-hardened) with steer-toward re-clustering — v1.0
- ✓ **Live wiring**: `N8nSuggestionsProvider` behind the seam, zero Phase-1 UI edits; panel consumes real suggestions end-to-end — v1.0

### Active

<!-- v1.1 Telegram Parity (see REQUIREMENTS.md for REQ-IDs): -->

- [ ] Telegram chat client at parity: list, messages, media, send, quoted replies, reactions-send on tapi
- [ ] In-screen per-bot channel switcher (WhatsApp|Telegram) on the chats screen
- [ ] Telegram bots actually converse: Telegram_Bot template outbound nodes on tapi bases
- [ ] «Вместе» suggestions work in Telegram chats
- [ ] Telegram 2FA accounts can authorize
- [ ] Dashboard «Сводка» counts and lists Telegram conversations
- [ ] tapi live-shape verification (user-assisted capture; gates parser/media work)

<!-- Carried from v1.0 (not this milestone's scope, still pending): -->

- [ ] Detailed device UAT (v1.0 deferred items — folded into v1.1 closeout phase)
- [ ] Prod bagkz replication (Suggest Replies + Telegram fixes; one bulk copy when dev is done — prod stays dormant)
- ~~Server-side «Вместе» suppression~~ ✓ Validated in Phase 9 (v1.2, 2026-07-22): fail-closed `Read Reply Mode`+`Suppressed?` gate in both templates; `reply_mode_flags` ('*' default + per-chat override) written via `/webhook/SetReplyMode` from all three client sites; behavioral e2e passed both channels

### Out of Scope

<!-- Explicit boundaries with reasoning. -->

- Official Telegram business-bots path — PARKED: client-side Premium paywall kills try-before-buy (revisit triggers in memory `telegram-channel-strategy`)
- Server-side «Вместе» suppression — separate deferred item, its own future phase
- Reactions-RECEIVE on Telegram if live capture shows no viable transport (tapi documents no `type:"reaction"` message; decision recorded in the capture phase)
- WhatsApp template sessionKey change — session-continuity risk for shipped bots outweighs symmetry with the Telegram fix
- Migrating bot persistence off PlayerPrefs / breaking up the `Manager.cs` god-object — real concerns (see `.planning/codebase/CONCERNS.md`) but a separate effort

## Context

**Current state (post-v1.0, 2026-07-11):** Live suggestions are dev-complete — shared n8n workflow `9PTyYcelRQI7bGDb` (`/webhook/SuggestReplies`, canonical export in `Tools/n8n/workflows/`, deployer `Tools/n8n/build-suggest-replies.py`), `N8nSuggestionsProvider` live behind the seam, 27 suggestion EditMode tests / 787 full suite green, security 14/14 closed (`02-SECURITY.md`). Prod bagkz remains dormant (one bulk replication planned). Detailed device UAT deferred (tracked). Dev n8n + Cloudflare tunnel must be running for live suggestions (`Tools/n8n/rotate-tunnel.py` refreshes the tunnel URL).

- **Brownfield.** Full codebase map lives in `.planning/codebase/` (ARCHITECTURE, STRUCTURE, STACK, INTEGRATIONS, CONVENTIONS, TESTING, CONCERNS). Read those before planning.
- The chat UI is canvas-based in a single scene. Existing `QuickReplyPanel`, `QuickReplyButton`, and `MessagesBottomPanel` in the WhatsApp chat screen are the closest analogs and the likely host area for the new panel.
- Live customer messages arrive via `ChatManager.OnLiveMessagesReceived`; the composer is the existing message input (`ExpandableInput` / `MessagesBottomPanel`).
- Networking is `UnityWebRequest` + coroutines; n8n is reached via webhooks with the `X-N8N-API-KEY` header. Suggestion transport will follow these existing patterns.
- **Known constraint:** Wappi has confirmed concurrent-response crossing bugs (`/media/download` and `messages/get`). Any new fetch must respect the established serial/guard patterns rather than fire concurrently.

## Constraints

- **Tech stack**: Unity 6 (`6000.3.9f1`), C#, URP, TMPro, DOTween — match existing conventions (singletons, `[SerializeField]`, event-driven UI).
- **UI quality**: 1080×1920 canvas reference units; senior-grade mobile UI (4px spacing multiples, type hierarchy, thumb-zone layout, DOTween motion). See `unity-ui-builder` skill.
- **Networking**: `UnityWebRequest` + coroutines only; secrets via `Secrets.cs` (never hardcode). See `unity-api-integration` skill.
- **Persistence**: per-chat semi-auto state follows existing PlayerPrefs / chat conventions.
- **Platform**: iOS (primary) + Android.

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| 4 cards: text + intent label; ranked best-first + "Recommended" badge (no numeric %) | Research-backed (arXiv 2402.07632): numeric LLM confidence is miscalibrated, skews high, and erodes trust (core value); no shipped agent-assist product shows per-reply % | ✓ Good (v1.0) |
| Tapping a card loads into composer to edit, never auto-sends | Maximizes owner control and trust (core value) | ✓ Good (v1.0; also the injection backstop — T-02-01/14) |
| Picking regenerates a full fresh set of 4, re-clustered toward the pick | Delivers the spectrum-of-control refine loop | ✓ Good (v1.0, steer field live) |
| Per-chat semi-auto toggle (not global) | Keeps the automation↔semi-auto spectrum per conversation | ✓ Good (v1.0) |
| UI built first against mock data; n8n wired second | Decouples polish from backend; matches the owner's build order | ✓ Good (seam held: live swap = 1 line, zero UI edits) |
| WhatsApp chat only this milestone | That's the live chat client surface; Telegram chat UI deferred | ✓ Good (v1.0) |
| Suggestions = 4 DISTINCT MOVES from a closed 6-label taxonomy («Ответ/Уточнить/Вариант/К заказу/Отложить/Отказ»), no duplicates | Distinct strategies cover the owner's real decision space — 4 paraphrases waste 3 slots; labels enable pick-by-scan | ✓ Good (v1.0) |
| Always exactly 4 cards on success (server-validated) | Owner preference: predictable layout over adaptive count | ✓ Good (v1.0) |
| Grounding rule: prices/stock only from catalog/RAG; missing fact → «Уточнить»/«Отложить», never invented | One hallucinated price sent by the owner's own hand kills product trust | ✓ Good (adversarially proven — 0 invented prices) |
| Shared always-active `Suggest Replies` workflow (DashboardOutcomes pattern), NOT a per-bot template change | On-demand pull, hot reply path untouched, no per-bot clone-propagation problem | ✓ Good (v1.0) |
| App sends conversation context (last ≤12 messages) instead of reading `n8n_chat_histories` | App history is fresh even for paused bots; Telegram-ready later; workflow stays stateless | ✓ Good (v1.0) |
| Provider coroutines on `ChatManager.Instance`, not the controller | Controller GameObject is inactive ~300ms at `OnChatSelected`; a network call can't answer synchronously like the mock | ✓ Good (v1.0) |
| Server-side «Вместе» suppression deferred; unauthenticated webhook accepted (R-02-01) | Scope control for v1.0; suppression needs a per-chat mode flag + bot-template change — its own phase | — Pending (future milestone candidate) |
| Telegram ships on Wappi tapi; official business-bots path PARKED | Client-side Premium paywall (₸13,490/yr, no trial) kills try-before-buy onboarding; tapi = same architecture as WhatsApp | — Pending (v1.1) |
| In-screen channel switcher on the chats screen; Telegram bottom tab removed | ChatManager is a hard singleton with event-driven views; separate screen duplicates 2 subtrees + 7 overlays and collides with 3 singletons; SetActiveBot reset choreography is a proven near-clone for channel switch | — Pending (v1.1, owner veto point) |
| Telegram cache under `BotCache/{botId}/telegram/`, WhatsApp keeps legacy root | Channel-scopes all per-bot caches with zero migration; purge/privacy paths already recurse | — Pending (v1.1) |
| Telegram_Bot template sessionKey → `profile_id + ':' + chatId` (WhatsApp template untouched) | tapi `from` can be a username (memory fragmentation); WhatsApp change would reset shipped bots' session memory | — Pending (v1.1) |

## Evolution

This document evolves at phase transitions and milestone boundaries.

**After each phase transition** (via `/gsd-transition`):
1. Requirements invalidated? → Move to Out of Scope with reason
2. Requirements validated? → Move to Validated with phase reference
3. New requirements emerged? → Add to Active
4. Decisions to log? → Add to Key Decisions
5. "What This Is" still accurate? → Update if drifted

**After each milestone** (via `/gsd-complete-milestone`):
1. Full review of all sections
2. Core Value check — still the right priority?
3. Audit Out of Scope — reasons still valid?
4. Update Context with current state

---
*Last updated: 2026-07-23 after completing Phase 11 (first-run-onboarding-flow) — v1.3 First-Run Onboarding complete: one-time carousel, both-channel trust blocks, standalone success overlay → price-list, derived-state «Первые шаги» checklist; ONB-01..05 proven on device (UAT 2 rounds)*

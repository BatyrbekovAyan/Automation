# Automation — WhatsApp/Telegram AI Bot Manager

## What This Is

A Unity 6 mobile app (iOS/Android) that lets small-business owners in the CIS market run a no-code AI chatbot on their **own** WhatsApp/Telegram number. The owner creates and configures bots (products, services, prompts), and a full in-app WhatsApp chat client lets them monitor every conversation and take over any chat at will. n8n workflows power the automation behind each bot.

This milestone adds the **semi-auto reply path**: a Reply Suggestions Panel that proposes candidate replies the owner picks, refines, and sends — the hands-on end of the automation↔semi-auto spectrum.

## Core Value

The owner stays in control along a spectrum from fully autonomous to hands-on: the bot can answer on its own, **or** propose replies the owner picks and refines — without ever losing trust or the ability to take over a conversation.

## Requirements

### Validated

<!-- Existing capabilities inferred from the codebase map (.planning/codebase/). -->

- ✓ Bot creation wizard (channel → name → WhatsApp/Telegram auth → business → summary → confirmation) — existing
- ✓ Bot CRUD, activation toggle, and per-bot persistence in PlayerPrefs — existing
- ✓ WhatsApp chat client via Wappi: chat list + pagination, send, media, quoted replies, reactions, chat delete — existing
- ✓ Bot configuration UI (General / Business / Products / Services / Prompts) — existing
- ✓ Per-bot n8n workflow automation (create / edit / activate WhatsApp & Telegram workflows) — existing
- ✓ Autonomous automation mode (bot answers customers via n8n) — existing

### Active

<!-- This milestone: the Reply Suggestions Panel (semi-auto mode). Hypotheses until shipped. -->

- [ ] Per-chat **semi-auto toggle** that enables the suggestions panel for a specific chat
- [ ] Reply Suggestions Panel UI — bottom sheet above the composer, **4 cards** ordered best-first, each showing reply text + intent label; the top card carries a "Recommended" badge (no numeric confidence)
- [ ] Suggestions **auto-populate on each incoming customer message**, plus a manual refresh
- [ ] Tapping a card **loads its text into the composer to edit** (never auto-sends)
- [ ] **Re-cluster loop**: picking regenerates a fresh set of 4 suggestions re-ranked toward the chosen reply; owner keeps refining or edits the composer draft and sends
- [ ] Phase-1 UI runs against **mock/stub suggestion data** (no n8n dependency) so visuals + interaction can be polished independently
- [ ] Existing **n8n automations modified** to emit suggestions (text / intent label / confidence) and support re-clustering toward a pick
- [ ] **Live wiring**: panel consumes real suggestions from n8n end-to-end

### Out of Scope

<!-- Explicit boundaries with reasoning. -->

- Telegram chat UI for suggestions — the WhatsApp chat client is the only live chat surface this milestone; defer Telegram
- Changing autonomous automation-mode behavior — this milestone only **adds** the semi-auto path alongside it
- Migrating bot persistence off PlayerPrefs / breaking up the `Manager.cs` god-object — real concerns (see `.planning/codebase/CONCERNS.md`) but a separate effort
- Unrelated chat features (e.g. reactions-over-webhook) — not part of this milestone

## Context

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
| 4 cards: text + intent label; ranked best-first + "Recommended" badge (no numeric %) | Research-backed (arXiv 2402.07632): numeric LLM confidence is miscalibrated, skews high, and erodes trust (core value); no shipped agent-assist product shows per-reply % | — Pending |
| Tapping a card loads into composer to edit, never auto-sends | Maximizes owner control and trust (core value) | — Pending |
| Picking regenerates a full fresh set of 4, re-clustered toward the pick | Delivers the spectrum-of-control refine loop | — Pending |
| Per-chat semi-auto toggle (not global) | Keeps the automation↔semi-auto spectrum per conversation | — Pending |
| UI built first against mock data; n8n wired second | Decouples polish from backend; matches the owner's build order | — Pending |
| WhatsApp chat only this milestone | That's the live chat client surface; Telegram chat UI deferred | — Pending |

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
*Last updated: 2026-06-23 after initialization*

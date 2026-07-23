---
sketch: 001
name: ai-assistant-redesign
question: "Full-app visual identity candidate for the WhatsApp/Telegram AI sales-assistant redesign (Claude Code entry in the multi-platform comparison)"
winner: null
tags: [full-app, identity, redesign]
---

# Sketch 001: AI Sales Assistant Redesign — "Артель"

## Design Question

This is not a per-question variant sketch — it's a complete, single production-grade
mockup built from the standalone design brief in
[`docs/ui-redesign-prompt.md`](../../../docs/ui-redesign-prompt.md) ("Prompt 1"), which
is meant to be pasted into multiple AI platforms and compared. This file is the **Claude
Code candidate** in that comparison, not an internally-branching exploration.

Identity direction: **"Артель"** — a warm, paper/ink/wax-seal visual language (trusted
neighborhood notary, not a SaaS dashboard or a WhatsApp clone). One rust/terracotta
accent reserved for primary actions and the «Бот работает» state; Golos Text (Cyrillic-
native); 8pt spacing grid; a recurring "seal-ring" motif that doubles as the Авто/
Вместе mode indicator across the app.

## How to View

```
open .planning/sketches/001-ai-assistant-redesign/index.html
```

Self-contained — all CSS/JS/SVG inline, only a Google Fonts request for Golos Text
(Cyrillic subset). Works fully offline aside from that font load.

## Screens Included

1. «Боты» — home (bot cards, empty state, loading/skeleton state — cycle via the small
   demo toggle top-right of the screen)
2. «Чаты» — bot switcher, chat list, Авто/Вместе toggle with the mode-switch
   confirmation dialog (the "trust moment")
3. Open conversation — bubbles, ticks, date separator, reply-quote card, image + voice
   messages, the «Вместе» AI-suggestion panel (tap a card to send it)
4. «Сводка» — period/bot filters, headline stats, 5-outcome distribution, recent list
5. Bot settings drill-in — Основное / О бизнесе / Товары и услуги / Прайс-листы /
   Промпт / danger zone
6. «Профиль» — stub

## Provenance / Known Limitations

Built via a background workflow (concept judge panel → section-by-section build against
slot markers → polish → QA/patch-fix loop). One subagent stage narrated instead of
returning pure HTML and left a stray prose paragraph before `<!DOCTYPE html>`; another
introduced a real layout bug (`.phone-screen`'s `overflow:hidden` + translateX-offscreen
overlay children created a phantom scrollable region that a focus-scroll could shift,
visibly misaligning dialogs). Both were diagnosed from the workflow journal and fixed
directly (narrative stripped at the source; `overflow: clip` + explicit `overflow-x`
added) rather than accepted as "transient." Verified interactively in-browser after the
fix: all 6 screens reachable, dialog/toggle/suggestion-tap interactions confirmed
working, no lorem ipsum / English UI strings / duplicate ids.

Known minor gaps (not blocking, noted for transparency):
- All three bot cards open the same static settings overlay; all chat/outcome rows open
  the same static Айгерим thread (intentional scope limit for a single-thread mockup).
- The «Вместе» suggestion panel doesn't visually clear/refresh after a card is tapped
  (the tap-to-send mechanic itself works correctly).
- No dark theme (deliberately skipped per the brief's own "only if it costs no quality"
  clause, to protect light-theme polish).

## What to Look For

Compare against other platforms' output on: the open-conversation and «Сводка» screens
(these degrade first on weaker platforms per the prompt doc's own guidance), whether the
«Вместе» panel feels native vs. bolted-on, and whether the seal-ring motif reads as a
considered system or a gimmick.

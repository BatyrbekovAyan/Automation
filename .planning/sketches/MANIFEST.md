# Sketch Manifest

## Design Direction

Sketch 001 is a full single-file mockup (not a per-question variant exploration) — the
Claude Code candidate for the multi-platform UI redesign comparison described in
`docs/ui-redesign-prompt.md`. See its README for the identity direction and scope.

## Reference Points

`docs/ui-redesign-prompt.md` — the portable design brief this sketch was built from,
intended for pasting into multiple AI platforms (Claude, ChatGPT, Gemini, v0, etc.) for
comparison.

## Sketches

| # | Name | Design Question | Winner | Tags |
|---|------|----------------|--------|------|
| 001 | ai-assistant-redesign | Full-app visual identity candidate ("Артель") for the WhatsApp/Telegram AI sales-assistant redesign | N/A — single full-app candidate, judged externally against other platforms | full-app, identity, redesign |
| 002 | suggestions-panel-redesign | What form factor + skin for the reply-suggestions panel on the messages page (6 rounds: form factor → 4-rows → E+G synthesis → title placement → title on J → border-legend position) | **P** — J chassis (fixed sheet + inner scroll, bordered full-text cards) + border-legend title top-left; locked 2026-08-07 | suggestions, chat, panel, restyle |
| 003 | suggestions-keyboard-slot | If the suggestions panel moves INTO the keyboard slot (below the composer, mutually exclusive with the keyboard), how does it look and what switches it? Keeps 002's locked card design; explores chrome + switch affordance | **A «Прямая замена»** — locked P sheet in the keyboard slot, header kept, ✦⇄⌨ key inside the input field; locked 2026-08-12 | suggestions, messages-page, keyboard, layout |
| 004 | slot-key-placement | Where does the ✦⇄⌨ slot key live? Owner dislikes today's field-START spot («+» + key crowd the left edge). Baseline vs field-END / standalone button by send / panel-header exit + entry-only field key | — (pivoted → 005: keyboard is now focus-driven, so the key's panel⇄keyboard role dissolved; the placement findings feed 005's controls, all drawn at field-END) | suggestions, messages-page, composer, keyboard |
| 005 | slot-collapse | New model (given): keyboard only while the field is focused, panel is the slot's DEFAULT tenant — so what collapses EVERYTHING (composer flush to the bottom) and what brings the panel back? Thread-tap step-down / header chevron / chameleon field key / collapse-to-strip; round 2 added **E «тап + ручка»** (owner's corrections: thread tap only raises; draggable handle under the composer with 3 detents — collapsed / standard / full-content) | — (direction: E, awaiting confirm) | suggestions, messages-page, composer, keyboard, collapse |

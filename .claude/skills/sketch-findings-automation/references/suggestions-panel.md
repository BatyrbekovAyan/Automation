# Suggestions Panel («Вместе» mode, messages page) — LOCKED DESIGN

Winner **P** of sketch 002 (6 rounds, locked 2026-08-07). Replaces the mint 2×2 grid
currently produced by `SuggestionsPanelBuilder`. Interactive reference:
`sources/002-suggestions-panel-redesign/index.html?v=p` (tab «P · На рамке ★»).

> **CHASSIS SUPERSEDED 2026-08-12 — sketch 003 winner A («Прямая замена»).** The CARD design
> below (bordered full-text cards, border-legend pills, tint-only recommended, header
> «‹ ✦ ПРЕДЛОЖЕНИЯ ↻», 4 states) remains LOCKED and unchanged, but the sheet no longer floats
> ABOVE the composer: the panel is now a KEYBOARD-SLOT tenant at the very bottom of the screen —
> exactly where and exactly as tall as the native keyboard, mutually exclusive with it. Slot
> height = last measured keyboard height (`SuggestionSlotHeight`, fallback 780u); swaps hold
> `max(keyboard, slot)` on `KeyboardAwarePanel.VirtualBottomInset` so the composer never moves
> during a handoff (no-dip; `SuggestionSlotSwap`). Grabber, drag-to-dismiss, drag-to-expand and
> the sheet's left-edge swipe proxy died with the sheet chassis; the mic-slot ✦ toggle was
> retired for the in-field key (which survives typing — the mic-slot one vanished with text).
> Reference: `.planning/sketches/003-suggestions-keyboard-slot/` (variant A) + its README
> Decision block.
>
> **SWITCHING MODEL SUPERSEDED 2026-08-13 — sketch 005 winner E («тап + ручка +
> клавиша-хамелеон»).** 003-A's both-ways ✦⇄⌨ toggle (key at the field START) is replaced by
> a focus-driven model — see «Interaction model (005 E)» right below. The slot-tenant chassis
> and the P cards stay. **IMPLEMENTED 2026-08-14** (`d09aa94` seams + `201ee23` runtime +
> `68d264a` scene; suite 1812/1812; iOS device pass pending) — the «Unity implementation notes»
> below are now a record of what was built, not a plan. Sections further down describe the
> still-valid card/skin spec + the OLD chassis for history.
>
> **Two spec values changed during implementation, both forced by the real scene:**
> the tint circle is **64u, not 81u** (the composer pill is 74u tall and carries a `RectMask2D`
> inset 8u vertically — 81 overflowed the pill AND would have been sheared to a flat chord every
> time the ✦ face showed), and the Text Area insets are the exact mirror **24/120** rather than
> the spec's 39/105, keeping the pre-005 total of 144 so the composer's auto-grow threshold does
> not shift. The 42u handle strip grows the chrome, and since the standard detent stays equal to
> the keyboard height, **the visible card area loses 42u** — worth a look on device.

## Interaction model — sketch 005 winner E (LOCKED 2026-08-13)

Owner-directed synthesis, 4 rounds. Reference: `sources/005-slot-collapse/index.html?v=e`
(tab «E · Синтез: тап + ручка ★»; deep-links `&sl=sg|kb|none|sgx`, `&theme=ink-dark`).

**Model:** the keyboard exists ONLY while the input field is focused; the panel is the slot's
DEFAULT tenant; `collapsed` (slot height 0, composer flush to the screen bottom) is reachable
ONLY via the handle. Slot states: `panel` · `keyboard` · `expanded` · `collapsed`.

**Transitions:**
- **Tap the input field**: panel/expanded → keyboard (normal focus). From COLLAPSED the same
  tap raises the PANEL instead — the field is NOT focused, no keyboard (two-step entry,
  «панель прежде клавиатуры»). In «Авто» mode the field always focuses normally.
- **Tap the thread**: only RAISES — keyboard → panel (blur), collapsed → panel; an open panel
  never hides from thread taps (repeat taps are no-ops). Scroll gestures are not taps.
- **The handle** — grabber strip on the panel's top edge, directly under the composer: free
  drag of the slot height; on release snaps to the NEAREST of three detents — collapsed (0) /
  standard (keyboard height) / expanded (chrome + full card content, capped so a strip of
  thread stays visible). The bottom fade hides in expanded (nothing is cut). Grabbing during
  an animation catches the panel where it currently is.
- **The key** — ONE morphing key at the field END, destination-glyph grammar (WhatsApp
  emoji-key): while panel/expanded is up → ⌨ in neutral `InkTertiary`, no tint (tap =
  keyboard + focus); while collapsed or keyboard → ✦ in `PositiveInk` on a 13% tint circle
  (tap = panel). Hidden in «Авто».
- **Pick** — CORRECTED 2026-08-13 during implementation. The sketch closed the slot on a pick, but that was
  demo scaffolding (so the loop could replay); the SHIPPED locked flow wins: a pick opens NO keyboard AND
  **keeps the panel open**, so a re-clustered variant is one tap away (`HandleCardTapped`,
  SuggestionsController.cs:291 + the flow comment at :302-304). The panel drops to **collapsed** later, on
  the outgoing echo (the «answered run» hide, `HandleLive` → :410) — which is precisely the state rule 9's
  auto-raise fires from, so the two rules interlock.
- **Incoming message auto-raise — DECIDED 2026-08-13: only-if-collapsed.** A new client
  message raises the panel ONLY when the slot is collapsed. Keyboard up ⇒ nothing moves
  (no steal; no parking needed — in this model dismissing the keyboard lands on the panel
  anyway). Panel already up ⇒ state unchanged, only the content refreshes (existing
  debounce/refresh flow).

**Sizes (sketch px → ×3 reference units):** handle strip 14 → 42 hit-height; grabber bar
36×4 r2 → 108×12 r6 (`Border`, hover `InkTertiary`); key circle 27 → 81, glyphs ✦16/⌨17 →
48/51; field right padding beside the key 6 → 18 (left stays 13 → 39); standard detent =
`SuggestionSlotHeight` (measured live keyboard, fallback 780u); expanded detent = handle +
header + cards content height, cap ≈ leave ≥ ~360u of thread visible (sketch: 470px cap on a
762px phone). If content fits under the standard detent, the third detent collapses into it.

**Collapsed is a STATE, not just a geometry.** Today `HidePanel` produces slot 0 + panel Deactivated, and
that same geometry is what «Авто», a bot switch and the attach-sheet swap produce — so the shipped
`_sheetOpen` bool cannot express «the owner collapsed it». Model E needs an explicit 4-state field
(Collapsed | Panel | Expanded | Keyboard), because rules 3/4/5/9 all key off «collapsed» specifically.
Collapsed does NOT keep the handle on screen (the panel leaves with the slot) — that is intended: the three
ways back are the thread tap, the composer tap and the ✦ key. Collapsed does not persist across a chat or
bot switch; every chat opens with the panel as the default tenant.

**Unity implementation notes (delta over the shipped 003-A build):**
- `ComposerSlotKey` already does destination-glyph morphing — MOVE it to the field END
  (Text Area RIGHT inset 24→120 instead of the left one) and add the state styling: tint
  circle only under ✦; ⌨ neutral.
- «Raise without focus»: a tap on the collapsed composer's field must NOT activate the TMP
  input. **CORRECTED 2026-08-13 — gating `OnPointerClick` alone is provably insufficient**: TMP
  reaches activation by THREE routes — its own `OnPointerDown` calls
  `EventSystem.SetSelectedGameObject` (TMP_InputField.cs:1982) → `OnSelect` → activate; the
  overridden `OnPointerClick` (DeferredDismissInputField.cs:218); and `TextSelectionRouter`'s
  long-press/double-tap path, which calls `SetSelectedGameObject` + `ActivateInputField`
  directly (TextSelectionRouter.cs:255-256). The veto must therefore be a pointer-scoped
  predicate consulted from an overridden `OnPointerDown` AND `OnPointerClick`, default-null so
  the other ~12 scene fields are untouched, installed by the controller on the composer field
  only, false whenever `!semiAutoOn` or the field is already focused. Do NOT gate `OnSelect` —
  that would break the ⌨ key (SuggestionsController.cs:702), the post-Send re-focus
  (MessagesBottomPanel.cs:138) and the reply focus (:164), all of which must keep working.
  Note also `DragShield` does not cover the field's full width (x∈[24,790] of 834), so a tap at
  either edge reaches TMP directly — the veto must live on the field, not on the shield.
  Respect the single-focus and materialized-focus invariants throughout.
- Thread-tap raise rides the existing keyboard-dismiss path; must not fire on ScrollRect
  drags (tap ≠ drag).
- The handle drives `VirtualBottomInset` LIVE during drag (bypass SmoothDamp while
  dragging), then snaps through the `SuggestionSlotSwap` max-hold machinery. The expanded
  detent makes inset > keyboard height — the no-dip `max(keyboard, slot)` rule and
  `ScrollTopInsetCompensator` already tolerate taller insets by design.
- Auto-shows still never steal the slot from a visible keyboard (park + land on close).
- «Авто»: panel, key, and handle all absent (existing SetVisible path).

**Rejected in 004/005 (do not resurrect):**
- Field-START key position — the original complaint: «+» + key crowd the left edge, text
  starts only after the key.
- Standalone key button between field and send (004 B); panel-header ⌨ exit + entry-only
  field key (004 C) — both dissolved by the model pivot.
- Thread-tap step-down that HIDES the panel on a repeat tap (005 A round 1) — «при повторном
  нажатии панель не должна убегать».
- Header collapse chevron (005 B), chameleon collapse key ✦⇄˅ (005 C), 26px peek strip
  (005 D) — lost to the handle; the composer must be able to lie flush on the bottom.
- TWO-button switch pair ✦|⌨ at the field end (005 round 3) — owner: ONE morphing key.
- «Glyph = where you are» grammar — chosen grammar is «glyph = destination» (WhatsApp
  emoji-key convention, and what `ComposerSlotKey` already implements).
- (Still true from 003:) QuickType «✦ N подсказок» chip is unbuildable — iOS system bar.

## Design Decisions

**Chassis — bottom sheet, FIXED height, cards scroll inside:**
- Sheet on `ThemeRole.Surface`, top corners rounded, up-shadow, sits directly above the composer.
- Chrome (top→bottom): grabber bar → header row: ✦ sparkle (PositiveInk) + «ПРЕДЛОЖЕНИЯ»
  overline label (InkTertiary, uppercase, letterspaced) + quiet refresh icon-button right
  (InkSecondary glyph, no background). **The old mint FAB is gone.**
- Card viewport below the header is a FIXED-height vertical scroll region. The sheet NEVER
  changes height between states — this preserves the panel's existing fixed-footprint
  invariant (D-12), so `SuggestionsPanel.Footprint`/message-list clearance logic stays as-is.
- Scroll affordance (all three together): the 4th card is cut off at the viewport edge, a
  bottom fade (Surface→transparent gradient overlay, input-transparent) washes the cut, and
  a thin scrollbar appears on the right. No «ещё N» counter chip (that was rejected K).

**Cards — individual, bordered, full text:**
- One full-width card per suggestion, stacked vertically. Card = `Surface` fill +
  1-unit `Border` outline, radius 42u. NO internal per-card scrolling, NO truncation/ellipsis —
  the full reply text always renders (cards grow; the SHEET scrolls).
- Whole card is a single tap target (locked earlier, D-01). Tap → text into composer,
  panel slides away.

**Intent title — legend ON the card's top border, LEFT:**
- The intent label («ЦЕНА», «ДОСТАВКА», …) sits centered ON the card's top border line,
  left-inset, like a fieldset legend. It consumes ZERO interior height — the reply text
  starts immediately at the card's top padding.
- Micro-typography: uppercase, bold, letterspaced, `InkSecondary`.
- Positions center/right/bottom were explicitly rejected (round 6) — top-LEFT only.

**Recommended card (best-first ordering):**
- Always FIRST in the stack. `PositiveBg` fill, border tinted `PositiveInk` (~45%), legend
  text `PositiveInk`, ✦ sparkle glyph inside the legend before the word.
- Tint-only emphasis — NO badge, NO numeric confidence % (older locked decision, D-07).

**States (all inside the same fixed viewport — footprint constant):**
- Loading: card-shaped skeletons with the 3-bouncing-dots motif (ThinkingDots identity).
- Empty: centered «Нет предложений» / «Напишите ответ вручную».
- Error: centered heading + hint + ghost «Обновить» button (`InputBorder` outline,
  `AccentText` label).
- Refresh tap → loading → cards.

**Interactions (kept from current implementation):**
- Panel slide-up on show ~0.25–0.28s OutCubic; slide-away on suggestion pick.
- Switching the header toggle to «Авто» hides the panel.
- Send-button pulse after a pick (sketch nicety — optional).

## Sizes (sketch CSS px ≈ dp → ×3 = scene reference units)

| Element | Sketch px | Reference units |
|---|---|---|
| Sheet total height | ~284 | **852** |
| — chrome (grabber+header) | ~38 | 114 |
| — card viewport (fixed) | 246 | **738** |
| Sheet top radius | 16 | 48 |
| Grabber | 36×4, r2 | 108×12, r6 |
| Header ✦ | 11 | 33 |
| Header label font | 9.5 | 28 (Caption tier) |
| Refresh icon-button | 26 circle, 14 glyph | 78 circle, 42 glyph |
| Card side padding (sheet) | 8 | 24 |
| Card gap | 5 | 15; **first-card top gap 9→27** (legend pokes above) |
| Card radius | 14 | 42 |
| Card border | 1 | 3 (hairline look; use 2–3u stroke) |
| Card padding | 8 top / 11 sides / 9 bottom | 24 / 33 / 27 |
| Reply text | 12.5, lh 1.36 | **38** (matches today's reply size) |
| Legend font | 8.5, ls .06em | 25–26 (Micro tier) |
| Legend inset from card left | 11 | 33 |
| Legend vertical offset | −7 (half out) | −21 |
| Legend side padding | 5 | 15 |
| ✦ in legend | 9 | 27 |
| Bottom fade height | 24 | 72 |
| Scrollbar width | 3 | 9 |

## Theme token mapping (ThemeRole)

Sheet `Surface` · card fill `Surface` · card border `Border` · legend ink `InkSecondary` ·
reply text `InkPrimary` · header label `InkTertiary` · sparkle + recommended accents
`PositiveInk` / `PositiveBg` · error retry `InputBorder` outline + `AccentText` label ·
fade gradient from `Surface`. No hardcoded colors anywhere — verified working in both
Theme_Light (today) and Theme_Dark («Чернильный») in the sketch.

## CSS patterns (from the winning variant)

```css
/* legend on the border — zero interior height */
.p-card { position:relative; }
.p-int  { position:absolute; top:-7px; left:11px; padding:0 5px;
          font-size:8.5px; font-weight:700; letter-spacing:.06em; text-transform:uppercase;
          color:var(--ink-2);
          background:linear-gradient(to bottom, var(--surface) 50%, var(--surface) 50%); }
.syn-card.top .p-int { color:var(--positive-ink);
          background:linear-gradient(to bottom, var(--surface) 50%, var(--positive-bg) 50%); }

/* fixed viewport + fade + thin bar */
.syn-fix  { height:246px; overflow-y:auto; scrollbar-width:thin; }
.syn-fade { position:absolute; left:0; right:0; bottom:0; height:24px; pointer-events:none;
            background:linear-gradient(transparent, var(--surface)); }

/* cards */
.syn-card      { background:var(--surface); border:1px solid var(--border); border-radius:14px;
                 padding:8px 11px 9px; }
.syn-card.top  { background:var(--positive-bg);
                 border-color:color-mix(in srgb, var(--positive-ink) 45%, transparent); }
```

## HTML structure (winning variant)

```html
<sheet Surface, r16 top, shadow-up>
  <grabber/>
  <header> ✦ ПРЕДЛОЖЕНИЯ <spacer/> <refresh-icon/> </header>
  <viewport fixed-height scroll>
    <card top>   <legend>✦ ЦЕНА</legend>      <text full/> </card>
    <card>       <legend>ДОСТАВКА</legend>    <text full/> </card>
    <card>       <legend>УТОЧНЕНИЕ</legend>   <text full/> </card>
    <card>       <legend>АЛЬТЕРНАТИВА</legend><text full/> </card>
  </viewport>
  <fade-overlay/>
</sheet>
```

## Unity implementation notes

- Restyle by UPDATING `SuggestionsPanelBuilder` (it owns this subtree) and re-running it;
  after rebuild, re-verify every serialized reference wired by `SuggestionsControllerWirer`
  (builders-must-rewire-consumers). Commit the scene right after the builder run.
- Viewport = `ScrollRect` (vertical) + `RectMask2D`; ensure every card child Graphic is
  Maskable (`m_Maskable:1`) or scrolling culls/janks.
- Legend "border break": Unity has no fieldset — build the legend as a small container
  with TWO stacked background strips (top half = sheet `Surface`, bottom half = the card's
  own fill: `Surface` or `PositiveBg`) + TMP label, anchored to the card's top edge,
  x = +33u from card left, centered vertically on the border (offset −21u). Match the CSS
  gradient trick exactly. RoundedCorners not needed on the strips (they're behind text).
- Fade = full-width Image with a vertical white→transparent sprite (or Gradient shader),
  `raycastTarget = false`, anchored to viewport bottom, tinted via ThemedColor to `Surface`.
- Fixed footprint: keep the existing footprint/clearance constant; states swap INSIDE the
  viewport only.
- Bind all colors via `ThemedColor` (additive, preserveAlpha ON) — never hardcode; the
  panel must flip correctly when the «Чернильный» palette lands.
- TMP glyph icons don't render in this project — refresh glyph and ✦ must be Image+sprite.

## What to Avoid (tried and rejected across the 6 rounds)

- **Mint palette** (#EAF6F0 sheet / #C9EFD9 card / #18A06B FAB) — the core complaint; all
  color must come from theme tokens.
- **2×2 grid with per-card internal scrolling** — cramped cells, text hidden behind scroll.
- **Straddling top-center intent pills («язычки»)** — busy; replaced by border-legend.
- **Chip-column titles in front of text** (variants G/B/F) — squeeze the text width.
- **Intent overline above text** (E/I/J/K original) — eats ~40u/card, forced scrolling.
- **Run-in word titles** (L gray/green, O navy) — reply starts with a word the customer
  never sees.
- **Corner-float (M), footer meta (Q), no-label (N)** — rejected; label must lead, on the border.
- **Legend at center (R), right (S), bottom border (T)** — top-left only.
- **Flat list rows** (E's look) — owner: "looks like a list"; individual cards required.
- **Any truncation/clamp of reply text** (A chips, F compact, G 2-line) — full text always.
- **Filled green recommended block** (H) — too pushy; tint-only.
- **Floating refresh FAB above the sheet** — replaced by the quiet header icon.
- **Chips row (A) / horizontal carousel (C)** — rejected form factors.
- **«ещё N» scroll counter chip** (K) — fade + cut card + scrollbar won.
- **Numeric confidence %, «recommended» badges** — locked out long before this sketch.

## Origin

Synthesized from sketch: 002 (rounds 1–6, winner P).
Source files: `sources/002-suggestions-panel-redesign/` (all 20 variants remain viewable),
themes in `sources/themes/`.

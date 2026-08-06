## Module 1 — SPATIAL DEPTH: the business floats in one calm, layered space

*One warm paper world under real frosted panes; nothing slides — everything advances and recedes.*

**Silhouette test** — a 10%-zoom screenshot of any screen must show: paper gutters between floating panes, a detached floating tab capsule with wallpaper visible around it, no edge-welded chrome, no lateral motion mid-transition.
**Exclusive structural signature** (no other module may copy) — the detached floating tab-bar capsule + advance/recede navigation. This module owns depth-as-navigation; it does NOT own the specular sweep (that belongs to Liquid Glass — never fire one here).

**Art direction brief** — One warm paper world seen through cool glass. The doodle wallpaper (#F5F2EA paper, #E5DAC6 ink — LOCKED colors) becomes the app-wide substrate; every screen is frosted panes floating above it with visible gutters, a lit top edge, and soft umber shadows falling straight down from one high light. The frost is real: pane fills sample a pre-baked blurred wallpaper, so the doodle reads as soft color under glass, never as sharp strokes behind a translucent rect. Navigation never slides; it advances and recedes, like windows in visionOS. Touchstones: iOS 26 system chrome restraint (glass is chrome, reading surfaces near-opaque), the iOS 26 Spatial Scenes lock screen (information always wins over depth), Apple Wallet's stacked cards (real occlusion, contact shadows). The feeling: a tidy desk under glass — the owner sees everything, touches anything, loses nothing.

**Design tokens**

Shadow/offset convention (used everywhere below): format `x / y-offset / blur @alpha` in reference units; x is always 0 (one overhead light, slightly left only in the rim, never in shadows); negative y = down, toward the floor. Layer ladder: `Z0` = the wallpaper substrate root · `Z1` = content panes · `Z2` = floating chrome (tab capsule, composer) · `Z3` = sheets/modals over snapshot blur.

| Token | Value | Notes |
|---|---|---|
| `sub.paper` / `sub.ink` | #F5F2EA / #E5DAC6 | substrate wallpaper (LOCKED — never edit these hexes) |
| `sub.wash` | vertical #EDF2F8 → #F5F2EA | baked into the wallpaper sprite, top 30% |
| `sub.blur` | pre-baked blur-45u wallpaper sprite | sampled by pane fills AND used behind Z3 (+#0A0D14 @30%) |
| `glass.pane` (Z1) | `UIPaneFrost` fill: `sub.blur` sampled screen-space + #FFFFFF @58% tint | real frost, zero runtime blur — see recipes |
| `glass.text` | same, tint #FFFFFF @86% | any pane carrying body paragraphs |
| `glass.chip` | same, tint #FFFFFF @34% + mandatory 3u stroke #808A99 | thin glass: filter chips, quick replies |
| `glass.chrome` (Z2) | tint #FFFFFF @72% | floating tab capsule, composer |
| `glass.modal` (Z3) | #FFFFFF @94% flat | over live snapshot blur |
| `stroke.edge` | 3u perimeter #FFFFFF @66% | every pane |
| `stroke.rim` | 3u top hairline #FFFFFF @90% | horizontal inset = the pane's corner radius |
| `stroke.control` | 3u #808A99 | mandatory boundary on chips, toggle tracks, wells (3.1:1 vs paper) |
| `shadow.tint` | #2E2A1F | umber from the warm paper — never pure black |
| Elevation E1 (Z1) | ambient 0/−24/72 @14% + contact 0/−6/18 @10% | |
| Elevation E2 (Z2) | ambient 0/−36/96 @18% + contact 0/−6/18 @12% | |
| Elevation E3 (Z3) | ambient 0/−72/180 @24% + full dim beneath | |
| Ink | #1A1A2E / #65676B / #6B7484 | primary / secondary / tertiary TEXT (#6B7484 ≥4.5:1 on panes); #8E8E93 = non-text glyphs only |
| `cta.fill` / `cta.pressed` | #1668CC / #1257A8 | white label 5.40:1 / 7.11:1 — the ONE saturated CTA per screen |
| Channel | WA #25D366 (deep #00A884) · TG #2AABEE | small icons/dots ON glass only — never text fills |
| `badge.unread` | fill #00734F, count 30 white (5.89:1) | never white-on-#25D366 (1.98:1) |
| `order_collected` | dot #23A55A · text #14713C · tint #E8F8EE | 5.52:1 |
| `owner_needed` | dot #F8942F · text #9A4E0B · tint #FCE1D0 | 4.85:1 |
| `in_dialog` | dot #1B7CEB · text #1257A8 · tint #E8F2FD | 6.28:1 |
| `client_silent` | dot #8A93A3 · text #566070 · tint #EEF1F5 | 5.61:1 |
| `question_closed` | dot #A348D4 · text #7A2FA6 · tint #EADCF1 | 5.75:1 |
| Radii | pane 72 · inner card 48 · input well 36 · chrome capsule 999 | concentric: r_inner = r_outer − padding |
| Chat bubbles | incoming #FFFFFF @96% · outgoing #C5EEB6 @96% | reading surfaces — never frosted |

**Material recipes**

*Regular pane (Z1), back-to-front — the exact hierarchy the builder emits:* `ShadowAmbient` (shared 9-slice blurred-rect sprite, rect +72u/side, `shadow.tint` @14%) → `ShadowContact` (same sprite, +18u/side, y −6, @10%) → `Fill` (Image with `UIPaneFrost` material — a fork of `Assets/Shaders/RoundedCornersBordered.shader` that samples the pre-baked `sub.blur` sprite in screen-space UV, offset by the pane's current parallax offset passed as a vec2, then composites the white tint on top; one texture fetch, zero runtime blur, honest frost) → `EdgeStroke` (RoundedCornersBordered, 3u, #FFFFFF @66%) → `TopRim` (Image 3u tall, anchored top, horizontal inset = corner radius) → `Grain` (128² tiling noise sprite, alpha 3% — OMITTED on any pane carrying text below 54u) → content.
*States:* pressed = scale 0.97, y −6, shadow alphas ×0.6 · focused = element holds 1.0 while siblings scale 0.97 + #0A0D14 @12% dim · disabled = tint @40%, ink @35%, shadows off.
*Thin chip:* tint @34%, mandatory 3u `stroke.control`, contact shadow only, capsule. Selected = stroke → #1668CC 3u + a leading check/dot glyph (never tint alone).
*Z3 sheet/modal:* on open, snapshot-blur the screen (backbuffer capture → ¼-res RT → 4 dual-Kawase iterations) into a full-screen RawImage, add #0A0D14 @35% dim, then the sheet at `glass.modal`, r=84 top corners, E3 shadow, grabber 12×120 #C7C7CC. Blur + dim fade in simultaneously with the sheet. On `OnApplicationFocus(true)` with a live sheet, re-capture the snapshot or swap to the flat `sub.blur` sprite — Android routinely loses RT contents on resume, and the pairing-code flow backgrounds by design.
*Concave input well:* fill #1A1A2E @6% on the parent pane, r=36, 3u `stroke.control`, inner top shadow (9-slice inset sprite, 6u, @15%), no drop shadow — wells sink, they don't float.

**Guardrails — do NOT**

- Do NOT attempt live backdrop blur or URP Renderer Features — the overlay canvas defeats them; the pane frost is the pre-baked wallpaper sample, modal blur is snapshot-on-open only.
- Do NOT stack more than 2 translucent tiers in any region; the third layer goes ≥94% opaque.
- Do NOT frost reading surfaces — bubbles, message text, body paragraphs sit on ≥86% fills.
- Do NOT slide any screen laterally; one `DOAnchorPosX` page transition kills the illusion.
- Do NOT put grain under any text smaller than 54u.
- Do NOT fire a specular sweep anywhere — it is Liquid Glass vocabulary, not this module's.
- Do NOT use pure #000000 in any shadow or dim — always #2E2A1F or #0A0D14.
- Do NOT fill large areas with translucent saturated brand color; brand = one opaque CTA, small icons, 12–20% tints.
- Do NOT break the concentric-radius rule — an r=72 child inside an r=48 parent reads instantly fake.
- Do NOT put the top rim on any other edge, or offset shadows sideways — one world light, always.
- Do NOT weld bottom chrome full-width to the screen edge; the tab capsule floats with paper visible around it.
- Do NOT ship a tappable chip or track whose only boundary is a <3:1 white stroke — `stroke.control` is mandatory.
- Do NOT dim twice with different colors under one modal; exactly one dim per open layer.

**Icons & type voice**

Icons: 2.5u-stroke outline sprites, rounded caps and joins, drawn on a 66u grid — airy line work matching the doodle wallpaper's hand. Always Image + sprite (TMP glyphs never render). Type: the 4 project fonts `SFProText-{Regular,Medium,Semibold,Bold} SDF.asset`; Semibold for names/CTAs, Regular for body; no new typefaces. Illustration: reuse `bot_hero.png` and the existing doodle language only — no new illustration assets without an owner decision.

**Component specs**

- **Primary button** — capsule 132 tall, opaque `cta.fill`, TopRim @30%, label 42 semibold white, side padding ≥48. One per screen.
- **Secondary button** — capsule 132, thin-chip recipe, label 42 #1668CC.
- **Bot card** — Z1 pane r=72, padding 48, min-h 372; name 44 semibold, channel icons 60 sprites at full saturation, footer row 120 with the activation switch; inner status block r=48.
- **Chat row** — rows live ON one full-width list pane (r=72, inset 24u from both screen edges), not one pane each. Row 204, avatar 132, title 42, preview 36 #65676B, hairline #E4E6EB @60%, unread badge capsule ≥60×60 `badge.unread`.
- **Input field** — well recipe, 132 tall, text 42; focus = stroke animates to #1668CC 3u + parent pane advances (siblings recede).
- **Toggle** — track 132×78 capsule, hit rect padded to ≥132×120; off = well #1A1A2E @10% + 3u `stroke.control` (border in BOTH states); on = #25D366 with TopRim; knob 66 white + contact shadow, travel +54u; RU label swaps («Бот работает»/«Бот на паузе»). Three state channels always: knob position + track fill + label.
- **Status pill** — capsule 78 tall (raycast padded to 120 where tappable), padding 36/24, dot 24 + text 32 medium in the deep ink on the tint, stroke #FFFFFF @40%. Pill grows when the label wraps; system labels never truncate.
- **Tab bar** — detached Z2 capsule 150 tall, inset 48/side, floating inside the baked 204 bottom zone (zone container transparent, wallpaper visible around it). Icons 66 sprites; active icon #1B7CEB (3.96:1 ≥3 OK for icons) + 26 label #1257A8; inactive icon #8E8E93, label #626C7A.
- **Bottom sheet / modal** — Z3 recipe; dialog variant r=72 all corners, width 936, centered.
- **Empty state** — no pane: illustration, text, CTA sit directly on the sharp substrate. Emptiness = open space, by design.
- **Avatar** — 132 circle, 3u ring #FFFFFF @80%, contact shadow; initials fallback on the matching soft tint, initials ink #6B7484.

**Screen-by-screen direction**

- **Onboarding**: sharp full substrate; one centered Z1 pane per step, advancing/receding. Success moment: substrate dim lifts to 0 and the success pane scales 0.9→1.0 while advancing one tier (shadows deepen) — depth IS the celebration; no sweep.
- **Bots list**: cards float with 24u paper gutters; header «+» is a 132 Z2 capsule (top placement accepted: low-frequency action, first-run covered by the empty-state CTA). Hero: toggling activation visibly *lifts* the card (shadows deepen 0.2s) — «Бот работает» literally sits higher than «Бот на паузе».
- **Dashboard (Сводка)**: filter chips = thin capsules 96 tall (hit rect padded ≥120); outcome pills use dot/deep-ink/tint; metrics on one wide Z1 pane, drill-down rows on a second. Hero: latching a filter chip makes the drill-down pane ADVANCE one tier (scale 1.0→1.02, shadows deepen) while the metrics pane recedes to 0.96 — the one screen where "your business floats in knowable space" pays off as comprehension.
- **Chats list**: search well 132 concave atop the single list pane; swipe-to-delete reveals paper behind the row, not a red block.
- **Chat thread**: wallpaper already IS the substrate — bubbles stay 96% opaque with contact shadow only; quoted cards r=36 (concentric); reaction pills over media use the `glass.text` ≥86% fill + contact shadow (never the thin-chip recipe on photos/video); composer becomes a Z2 capsule. Hero: the attach sheet opening over live snapshot-blurred chat — the one place users *see* real modal glass.
- **Bot Settings**: each tab = one scrolling `glass.text` pane (no grain — text below 54u); section headers sit in the gutters ON the paper; product/service cards r=48. FocusScrim becomes snapshot-blur + dim.
- **Add-Bot wizard**: each step is an advancing pane; business tiles r=48 with pressed-recede; QR/pairing panels are `glass.text` panes with the code in a concave well.
- **Profile**: sub-pages advance/recede instead of sliding; list groups as separate panes with paper gutters — Settings-in-space.

**States (loading / error / empty)** — per the master triad, rendered in this material: loading >300ms = the pane hierarchy renders with content children replaced by #1A1A2E @6% blocks pulsing alpha 4–8% (the pane itself never pulses); network failure = an inline retry row ON the pane + the existing rollback affordances restyled; empty = open substrate (above). Offline = the pane dims to disabled recipe with a 32u status line.

**Motion & feedback** — this table OVERRIDES the app default 0.3s OutCubic page enter.

| Action | Tween | Duration | Ease |
|---|---|---|---|
| Page push | new pane fade + scale 0.94→1.0; old →0.96 + dim →25% | 0.32s | OutQuint |
| Page pop | exact reverse | 0.28s | OutQuint |
| Sheet open | DOAnchorPosY from below; blur RT + dim DOFade in parallel | 0.28s | OutQuint |
| Press in / out | DOScale 0.97 + shadow DOFade ×0.6 / restore | 0.12 / 0.25s | OutQuad / OutBack |
| Focus field | siblings DOScale 0.97 + dim 12% | 0.20s | OutCubic |
| Scroll parallax | substrate root y = content offset ×0.3, direct set | per frame | — |
| List cascade | rows fade + y 24→0, stagger 0.04s | 0.25s | OutCubic |
| Dashboard chip latch | drill-down pane scale →1.02 + shadow deepen; metrics pane →0.96 | 0.25s | OutCubic |
| **Peak: bot connected** | substrate dim lifts to 0; success pane DOScale 0.9→1.0 + advance one tier (shadow alphas deepen) | 0.6s | OutBack(1.02) |
| **End: wizard done** | wizard pane recedes (scale →0.92, fade, `sub.blur` crossfade), bots list revealed, new card cascades in | 0.4s | OutQuint |

Gyro parallax is OPTIONAL and the LAST task of the migration: one script `SubstrateParallax.cs`, `Input.gyro` only, low-pass filtered, caps Z0 ±30u / Z1 ±12 / Z2 ±6 / Z3 0 enforced by `Mathf.Clamp` in the script, disabled when `Theme.A11y.ReduceMotion`. Ship the style without it if any budget is at risk.

**Unity notes — style-specific deltas only** (the universal contract lives in the master)

- Fork `RoundedCornersBordered.shader` → `UIPaneFrost.shader`: keep clipping + `fwidth` AA; add `_WallpaperBlurTex` (the pre-baked blur-45u wallpaper sprite), `_ScreenRect`, `_ParallaxOffset`, `_TintColor/_TintAlpha`. Each pane fill is one material instance (counts against the Nobi budget — a pane consumes Fill + EdgeStroke = 2 instances, so budget ~10 panes/screen).
- Snapshot pipeline for Z3 only: `ScreenCapture.CaptureScreenshotIntoRenderTexture()` → 270×600 RT → 4 dual-Kawase `Graphics.Blit` passes → RawImage; release RT on close; re-capture on `OnApplicationFocus(true)`.
- `ThemeBuilderKit.AddPane(go, radius, Elevation)` emits the exact six-child hierarchy above.
- Advance/recede is a NAVIGATION BEHAVIOR change (owner checkpoint — see master §5): replace lateral slides in `ProfileSubPages` slide-in tweens, `SwipeToBackPanel`, `SwipeToBackBotSettings`, `SwipeToBack`, and any `DOAnchorPosX` page transition in `Manager`/`BottomTabManager` with scale+fade advance/recede. Swipe gestures stay; only the animated response changes.
- Put parallax layer roots on nested Canvases so per-frame offsets don't dirty the main canvas; freeze parallax during scroll momentum.

**Accessibility floor** — the master floor (§4) applies in full; module-specific: body ink #1A1A2E on any pane over paper measures ≥12:1 — keep it there; tertiary text is #6B7484, never #8E8E93; the pill deep inks are pre-verified ≥4.5:1 — do not lighten them. `Theme.A11y.ReduceTransparency` (and low-power — same fallback): all pane fills jump to ≥96% flat alpha in the same hue; strokes and shadows stay so hierarchy survives. `ReduceMotion`: parallax off; advance/recede becomes crossfade + dim 0.2s (keep the dim — it is hierarchy, not motion).

**Definition of done**

1. `grep -rn 'Hex("' Assets/Editor/ --include='*.cs' | grep -v ThemeBuilderKit` returns 0 for every restyled builder; all colors resolve through `Theme`.
2. An EditMode test asserts every emitted pane has exactly ShadowAmbient / ShadowContact / Fill / EdgeStroke / TopRim / (Grain) children in order, and that Grain is absent on `glass.text` panes; suite green.
3. Frame Debugger per restyled screen: ≤45 draw calls; all shadow quads batch into ≤3 calls; ≤20 material instances (pane Fill + EdgeStroke both counted).
4. DEVICE-GATED (owner UAT): steady-state 0 blur passes; sheet-open spike ≤2.5ms on a mid-range Android device.
5. Every nested rounded pair on Screen_Bots and the BotSettings General tab satisfies r_inner = r_outer − padding within ±4u.
6. Screenshot color-picker audit: body text ≥4.5:1, all 5 outcome pill inks ≥4.5:1, delivery ticks ≥3:1, tab labels ≥4.5:1.
7. No navigation code contains a lateral `DOAnchorPosX` page transition (knob/composer tweens exempt) — grep log attached.
8. `Theme.A11y.ReduceTransparency = true` produces ≥96%-opaque fills and screenshots still show 3 distinct elevation tiers.
9. `SubstrateParallax.cs` (if shipped) contains `Mathf.Clamp` covering all four tier caps — the clamp is the proof; parallax provably freezes during scroll momentum.
10. Longest RU strings render unclipped at 1080×2400 on every restyled screen.
11. Bot activation ON measurably deepens the card's shadow alphas vs OFF (inspect serialized colors).
12. Each builder run is followed by an immediate scene commit with the payload verified per the master ritual (§3.8).

## Module 4 — NEO-SKEUOMORPHISM: machined controls that tell the truth by touch

**Status: archived exploration** — not shortlisted for v1 (finalists: Spatial, Liquid Glass, Refined Modern). Kept build-ready for the style-swap kit; every number below is verified.

*A warm, overhead-lit machine: five materials assigned by role, data printed on paper plates, and every control still legible with every bevel stripped.*

**Silhouette test** — a 10%-zoom screenshot must show: a warm `#EFEDE6` ground, cool paper plates sitting on it, colored enamel keys as the only saturation, and list rows printed DIRECTLY on the ground (no cards, no shadows).
**Exclusive structural signature** (no other module may copy) — the mechanical latch: switch-with-detent whose shadow lags the knob by 40ms, the enamel keypad (wizard business tiles as depressible keys), and the receipt-ledger dashboard. This module never fires a specular sweep (that belongs to Liquid Glass) — a machine that latches doesn't also glint.

**Art direction brief** — Build a warm, overhead-lit instrument panel: the app is a small, dependable machine that runs the shop while the owner sleeps. One light source, top-center, forever — every highlight is a top edge, every shade a bottom edge, every shadow falls straight down. Surfaces are matte and close-valued (≤12% luminance travel per object); data is printed on near-flat paper plates; only controls protrude. Neutrals are slightly warm so the enamels and the five outcome colors are the *only* saturated things on screen. Fine uniform grain at one frequency app-wide gives the plastic its tooth. Touchstones: Panic's Playdate OS (one material, ruthless discipline), (Not Boring) Habits (material + feedback as one system), Teenage Engineering (anodized restraint). Anti-touchstones: leather, stitching, wood, chrome, gears.

**Design tokens** — reference units throughout (1dp = 3u).

**Material roles (hard rule — replaces any per-screen material count):** `Chassis` = structure/chrome · `Plate` = data · `Well` = input · `Enamel` = action/state · `Jewel` = status lights. One material per role, no role doubles up, no material moonlights. Target two materials + one accent per screen (Ground excluded); Bot Settings is the sanctioned three-material screen (Chassis + Well + Plate).

| Token | Value |
|---|---|
| `Chassis` (matte plastic — bars, tabs, secondary controls) | vertical gradient `#F5F6F8 → #E7EAEE` |
| `Plate` (paper — cards, rows, data surfaces) | `#FCFCFA → #F4F3EF` (warm; kin to wallpaper `#F5F2EA`) |
| `Well` (debossed — inputs, tracks, active segments) | inverted `#E4E7EC → #EEF0F3` |
| `Ground` (screen background) | flat `#EFEDE6`, grain α 0.04 |
| `Enamel/Primary` (text CTAs) | `#0F4FA8 → #1257A8 → #1668CC` (dark top = rubber; white label worst stop 5.40:1, best 7.77:1) |
| `Enamel/Success` (switch tracks ON — per-BOT, never per-channel) | `#1FBA58 → #25D366 → #3ADD78` — state only, never text |
| `Enamel/Telegram` (identity on auth screens only — never text, never switches) | `#2196D6 → #2AABEE → #4BBAF3` |
| `Enamel/Danger` | `#8E1F1B → #B32721 → #C62828` (white label worst stop 5.62:1) |
| Ink primary / secondary / tertiary / disabled | `#1A1A2E` / `#65676B` (4.84:1 on Ground, 5.10:1 on Plate) / `#63666D` (4.91:1 on Ground, 5.04:1 on Well) / `#C7C7CC` (disabled only, never information) |
| Letterpress (ink on light) / (white on enamel) | white α 0.60 at (0, −3u) / black α 0.18 at (0, −3u) — **permitted only on text ≥54u**; smaller text prints flat |
| Bevel light / dark | white α 0.75, 3u top / black α 0.10, 3u bottom |
| Interactive border | 3u solid `#767E8A` on every interactive — 3.40:1 vs Chassis dark stop, 3.79:1 vs light stop, 3.50:1 vs Ground, 3.59:1 vs Well (store all four pairs in `ThemeAsset`; the border, not the fill, is the 1.4.11 boundary) |
| Hairline / divider | `#1A1A2E` α 0.06 / `#E4E6EB`, 3u |
| Elevation E1 (controls, cards) | contact (0, −6u, blur 12u, α 0.18) + ambient (0, −18u, blur 48u, α 0.10) |
| Elevation E2 (sheets, modals) | contact (0, −9u, blur 18u, α 0.20) + ambient (0, −36u, blur 96u, α 0.12) |
| Paper shadow ink | `#3C3223` (warm), plastic shadow ink `#2B2A33` |
| Grain | one 128² tile: α 0.03 Chassis/Enamel, 0.04 Ground, 0 under any text below 54u, ceiling 0.06 |
| Radii | R-card 40 · R-control 36 · pill 66 · R-sheet 48 (sheets/modals only). Max three distinct radii per screen; sheets and circles (avatars/jewels) exempt |
| `order_collected` | ink `#14713C`, plate `#E8F8EE` (5.52:1), jewel `#23A55A` |
| `owner_needed` | ink `#9A4E0B`, plate `#FCE1D0` (4.85:1), jewel `#F8942F` |
| `in_dialog` | ink `#1257A8`, plate `#E8F2FD` (6.28:1), jewel `#1B7CEB` |
| `client_silent` | ink `#566070`, plate `#EEF1F5` (5.61:1), jewel `#8A93A3` |
| `question_closed` | ink `#7A2FA6`, plate `#EADCF1` (5.75:1), jewel `#A348D4` |

**Material recipes** — layers bottom-to-top; construction per layer is in the Unity notes.

*Chassis, raised:* ambient shadow quad → contact shadow quad → base fill (rounded 9-slice sprite, vertex gradient `#F5F6F8→#E7EAEE`) → top bevel hairline 3u white α 0.75 → bottom hairline 3u black α 0.10 → grain overlay α 0.03 → 3u border `#767E8A`. **Pressed:** translate down 3u; both outer shadow quads fade to 0; an inset-shadow overlay (top-weighted, black α 0.16, 12u reach) fades in; fill darkens 8%; top bevel α → 0.30. In 0.07s, out 0.18s. **Disabled:** flat `#EDEFF2`, bevels and shadows off, ink `#C7C7CC`, border kept.
*Plate:* no bevel ever. Warm cast shadow `#3C3223` (0, −3u, blur 9u, α 0.10) + (0, −15u, blur 42u, α 0.07) → fill gradient → hairline border α 0.06 → radius 40. Paper never depresses: tap = scale 0.985 with the ambient shadow tightening (blur 42u → 24u).
*Well:* zero drop shadow. Inverted fill → inner top shadow (inset 9-slice, black α 0.15, reaching 12u down) → 3u bottom-edge outer highlight white α 0.70 → 3u border `#767E8A` when interactive. **Focused:** 3u inner rim `#1B7CEB` α 0.90, 0.14s fade.
*Enamel:* Chassis structure with the 3-stop saturated gradient (dark top — rubber subsurface read), one broad sheen band across the top 40% at white α 0.08 max, border black α 0.20. **Pressed:** down 6u, scaleY 0.96 / scaleX 1.02, darken 10%, contact shadow collapses; release springs back with ~1.5% overshoot.
*Jewel (glass, ≤72u only):* authored radial sprite — dark rim α 0.40, lighter center, top crescent white α 0.25 — tinted per status. Used for outcome dots, unread badges, connection lamps. Never a button, never grain. **When a jewel carries a count (unread badge):** the count is 26/700 white printed on a flat `#00734F` core disc under the rim highlights (5.89:1 measured at the text position); badge diameter 60u.
*Sheets/modals over content:* snapshot-blur on open (capture → ¼-res dual-Kawase → held RawImage) + scrim black α 0.35. On `OnApplicationFocus(true)` with a live sheet, re-capture the snapshot or swap to the authored flat fallback (`Plate → #FAF9F6`) — Android routinely loses RT contents on resume, and the pairing-code flow backgrounds by design.

**Guardrails — do NOT**

- Do not give any shadow a non-zero X offset. One light, straight overhead, app-wide.
- Do not use pure `#FFFFFF` or `#000000` as a surface fill.
- Do not put text on `Enamel/Success` or `Enamel/Telegram` — they carry state and identity only; text CTAs are `Enamel/Primary` or `Enamel/Danger`.
- Do not give a `Well`, input, track, or active segment a drop shadow. Recessed things have inner shadows only.
- Do not exceed 12% luminance spread on any surface, or 4% on any surface carrying body text.
- Do not exceed grain α 0.06 anywhere, and never place grain under text smaller than 54u.
- Do not draw a hard-edged gloss arc or a white top-half highlight; specular cap is α 0.16, one per object.
- Do not let any role double up or any material moonlight (a Plate that presses, an Enamel that holds data). New material = design-review event.
- Do not use a fourth distinct radius on a screen (sheets/circles exempt).
- Do not texture the chat transcript, message text, or dashboard numbers — content is flat, chrome is material.
- Do not make bevels or fills the affordance: every interactive keeps its 3u `#767E8A` border, which must measure ≥3:1 against BOTH its own fill and its neighbor with all decoration (shadows/bevels/gradients/grain) stripped.
- Do not letterpress any text below 54u — small deltas and captions print flat.
- Do not use a dial or knob for a boolean or a list — state space must match the metaphor.
- Do not fire a specular sweep — the latch and the print are this module's peak physics.
- Do not run looping ambience (breathing shadows, idle sheens) on the main canvas.

**Icons & type voice**

Icons: filled, machined glyphs — solid fills, 3u squared terminals, flat single-color, on a 66u grid; they read as printed or engraved, never outlined. Always Image + sprite (TMP glyphs never render). Type: the 4 project fonts `SFProText-{Regular,Medium,Semibold,Bold} SDF.asset`; 600 for controls/names, 400 body; pairing-code digits 60/700. Illustration: reuse `bot_hero.png` and existing sprites; the pre-blurred doodle wallpaper sprite may serve as onboarding ground; no new illustration assets without an owner decision.

**Component specs**

| Component | Spec |
|---|---|
| Primary button | h 132, R-control, pad-x 48, `Enamel/Primary`, label 42/600 white (letterpress OK only if label ≥54 — at 42 it prints flat); E1 |
| Secondary button | h 132, R-control, `Chassis`, label 42/600 `#1560C0` (5.02:1 vs worst stop), contact shadow only |
| Bot card | width − 48 margins, R-card `Plate`, E1; info row 180, footer 120 = a `Well` strip housing the switch; name 47/600, status caption 32 |
| Chat row | h 216 directly on `Ground` — no card, no shadow; avatar 132; 3u divider inset 216 left; unread badge = 60u jewel with the flat-core count; time/meta ink `#63666D` |
| Input field | h 132 min, R-control, `Well`, ink 42, placeholder `#63666D` (5.04:1 vs Well's lightest stop) |
| Switch | track 168×96 pill `Well` + 3u `#767E8A` border in BOTH states; knob 84 `Chassis` dome with both bevels; travel 72; ON = track fills `Enamel/Success` (per-bot always) + RU label swaps; hit rect padded to ≥132×132 |
| Status pill | min-h 84 (grows when the label wraps to 2 lines — outcome labels NEVER ellipsize), pill radius, caption 32/600 status ink on status plate, 3u rim at jewel color α 0.30, 24u jewel dot; no shadow |
| Filter chip (dashboard) | small Chassis key, h ≥120 (or 96 visual + raycast padded to ≥120, stated in the builder); depresses on latch |
| Price-list ✕ key | Chassis key, visual ≥72, hit rect ≥132×132 |
| Reaction pill | 54u Chassis chip visual, raycast rect padded to ≥120×120 |
| Tab bar | 204 `Chassis` slab (baked height), 3u top rim white α 0.80; per tab: icon over a debossed `Well` pad 96×96 (visual only) + 26/600 label — active icon enamel-tinted + label `#1257A8` (5.89:1), inactive label `#63666D`; each tab's HIT rect = its full slab column ≥216×204 |
| Bottom sheet | `Plate`, top corners 48 (R-sheet), grabber = debossed groove 108×12; opens over snapshot-blur + scrim; E2 |
| Modal | `Plate` R-card, E2, over the same snapshot-blur scrim; buttons full-spec, never bare text |
| Empty state | hero sprite on `Ground`, headline 50/600, body 39 `#65676B`, one primary button |
| Avatar | 132 chat / 168 bot card / 96 dashboard; circle image (Nobi), 3u inner rim black α 0.08 + 1.5u outer white α 0.60 |

**Screen-by-screen direction**

- **Onboarding:** `Ground` with the pre-blurred doodle wallpaper baked as a sprite; channel connect = one `Plate` per channel with a real switch; hero moment = the first latch.
- **Bots list:** `Plate` index cards on `Ground`; the footer switch is the hero — it latches with a detent and the card's shadow contracts 40ms behind the knob. Header `+` = Chassis key ≥132 (top placement accepted: low-frequency, first-run covered by the empty-state CTA).
- **Dashboard (Сводка):** a printed ledger — one `Plate` per stat block, deltas 36 printed FLAT (no letterpress below 54u) with a leading +/− sign, the five pills the only saturation on screen; filter chips are Chassis keys that visibly depress and latch; recent-order rows are receipt lines with jewel dots. Hero: the chip latch re-printing the ledger.
- **Chats list:** rows on bare `Ground`, search bar the single `Well`; swipe-to-delete reveals `Enamel/Danger` *beneath* the row, as if under it.
- **Chat thread:** stays near-flat — bubbles keep current fills (`#FFFFFF`-near / `#C5EEB6`) plus only a 3u contact shadow and hairline; quoted cards become recessed `Well` strips inside the bubble; reaction pills per the spec above; composer = Chassis bar with a `Well` input.
- **Bot Settings:** the control panel — tab strip is a Chassis segmented control whose active segment is debossed; every editable field a `Well`; product/service cards `Plate`; price-list rows read as receipts with the ✕ key.
- **Add-Bot wizard:** business-type tiles are Chassis keys on a keypad (this module's signature grid), selection = held-down deboss + accent rim + check glyph; QR sits on a clean `Plate`; pairing-code digits letterpressed at 60/700 (≥54 ✓).
- **Profile:** `Plate` lists on `Ground`; toggle rows reuse the exact bot-card switch so the metaphor is learned once. «Удалить все данные» = `Enamel/Danger` key.

**States (loading / error / empty)** — per the master triad, in this material: loading >300ms = a blank embossed `Plate` (structure printed, data not yet) pulsing grain-free fill 4–8% alpha — restyle the EXISTING indicators only (chat-list sync, dashboard fetch); failure = an inline retry receipt-row + the existing rollback affordances restyled; empty = hero sprite on `Ground` (above). Offline = the affected Plate at the disabled recipe + one 32u status line.

**Motion & feedback**

| Action | Tween | Duration | Ease |
|---|---|---|---|
| Press down (Chassis/Enamel) | `DOAnchorPosY(−3u)` + darken + inset crossfade `DOFade` | 0.07s | Linear |
| Release | position/fade back | 0.18s | OutBack(1.2) |
| Plate tap | `DOScale(0.985)` + shadow tighten | 0.12s | OutCubic |
| Shadow lag | card shadow tween `SetDelay(0.04)` | — | — |
| Switch latch | knob `DOAnchorPosX(72u)` + track `DOColor` to `Enamel/Success` | 0.16s | OutBack(1.6) |
| Well focus | rim `DOFade(0→0.9)` | 0.14s | OutSine |
| Sheet open | `DOAnchorPosY` in + blur alpha 0→1 | 0.25s / 0.18s | OutBack / OutSine |
| Page enter | `DOFade` + `DOAnchorPosX(36u→0)` | 0.3s | OutCubic |
| **Peak — bot connected** | master switch latch (detent, shadow lag) → `DOPunchScale(0.06)` on the channel Plate — the latch IS the moment; no sweep | 0.16 / 0.35s | OutBack / OutCubic |
| **End — wizard confirm** | new bot card "prints" onto the list: slide down 96u + shadow settle | 0.3s | OutCubic |

Haptics are OPTIONAL polish: grep `AndroidBridge`/`IOSBridge` for an existing vibrate method; if none exists, DO NOT add plugins or native code — record it as a follow-up and ship without. If available: fire on the *down* transition only, max two per interaction (press + latch).

**Unity notes — style-specific deltas only** (the universal contract lives in the master)

- 9-slice-instead-of-Nobi strategy: cards and buttons use pre-rounded 9-slice fills, NOT `ImageWithRoundedCorners` — that is what lets the gradient+shadow+bevel stack batch. Reserve Nobi for avatars and media only (each instance = 1 draw call; budget ≤20/screen).
- Shadows are sibling 9-slice pre-blurred sprite quads (one outer-shadow sprite per radius family — r40, r66 — plus one inset-shadow sprite); stacked E1/E2 = 2–3 siblings sharing the same sprite at different spreads/alphas → one batched draw call. Gradients are vertex colors via the shared `AddVerticalGradient` `BaseMeshEffect`. Total authored sprites ≈ 8 (<1 MB), all on the default UI material.
- `ThemeBuilderKit` helpers: `AddChassis`, `AddPlate`, `AddWell`, `AddEnamel(go, EnamelKind)`, `AddJewel`.
- Letterpress = a shared TMP **material preset with underlay** (offset 0,−3u), never `UnityEngine.UI.Shadow` on text — it doubles the mesh and murders the chat list; if a label wraps, the underlay wraps with it.
- Chat bubbles: contact shadow + hairline only, added in `MessageItemView` code where bubble metrics live — not prefab edits.
- Snapshot pipeline for sheets/modals: capture → ¼-res dual-Kawase → held RawImage; release the RT on close; re-capture on `OnApplicationFocus(true)`.

**Accessibility floor** — the master floor (§4) applies in full; module-specific: measure against the *worst* gradient stop always — `#1A1A2E` on `#F4F3EF` ≈ 13:1 (body ok); `#65676B` ≥4.8:1 on Plate and Ground; all five status inks ≥4.85:1 on their plates (shared verified set); white 42/600 on `Enamel/Primary` = 5.40:1 at the lightest stop `#1668CC` — if a future enamel fails, darken the enamel, never shrink the text. Every interactive: 3u `#767E8A` border ≥3:1 vs fill AND neighbor with decoration stripped. Touch HIT rects ≥132u (or the stated padded raycast). `Theme.A11y.ReduceMotion`: kill springs, squash, and shadow-lag; keep the 3u press translate and a 100ms fill change so every press still reports. `ReduceTransparency`/`HighContrast`: each material ships an authored flat hex (`Chassis→#EFF1F4`, `Plate→#FAF9F6`, `Well→#E7EAEF`) + its border; grain and gradients to zero. Russian: buttons auto-size width with 48u padding, wrap to two lines at 39u before any shrink (floor 36u); outcome pills grow/wrap and NEVER ellipsize (ellipsis is legal only on user content); the bot-card footer reserves 420u for «Бот на паузе»; system labels never drop below 32u.

**Definition of done**

1. `grep -rn 'Hex("' Assets/Editor/ --include='*.cs' | grep -v ThemeBuilderKit` returns 0 for every restyled builder; `ThemeAsset` stores the four border-vs-material pairs.
2. An EditMode test walks restyled hierarchies: every child named `Shadow*` has `anchoredPosition.x == 0 && y <= 0`.
3. Exactly one grain texture and ≤8 authored material sprites exist; all decoration uses the default UI material.
4. Frame Debugger on Screen_Bots with 10 cards: ≤45 draw calls, all shadow/gradient/grain layers ≤8 calls combined; Nobi instances ≤20, avatars/media only.
5. MANUAL/UAT: every button and switch shows translate + darken + inset on press (60fps screen recording).
6. Every `Well` has zero drop shadow; every input focus shows the 3u blue rim.
7. `Tools/check-contrast.py` asserts: all five status pills ≥4.5:1 ink-on-plate, CTA labels ≥4.5:1 at the LIGHTEST gradient stop, every interactive border ≥3:1 vs both fill and neighbor.
8. An EditMode test asserts every interactive HIT rect ≥132×132 (switch knob, ✕ keys, reaction pills, tab columns included).
9. Steady-state blur passes = 0; each sheet/modal open captures exactly one snapshot; the RT is released on close and re-captured on app resume (pairing-code flow test: background the app, return, no black/garbage backdrop).
10. Tab bar shows text labels under icons on every tab; zero icon-only navigation.
11. Longest RU strings («Бот на паузе», «Требуется владелец», the five outcome labels) render un-clipped and un-ellipsized at 1080×2400.
12. Each builder re-ran idempotently, its `transform.Find` consumers verified, and `Main.unity` committed per the master ritual (§3.8).

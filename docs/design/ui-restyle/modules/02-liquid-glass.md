## Module 2 — LIQUID GLASS: light bends at the edges; your data never moves

*Glass is chrome only — 95% of surfaces are solid cards; the identity lives in one lensing plane and the 135° edge light echoed at quarter strength on everything solid.*

**Silhouette test** — a 10%-zoom screenshot must show: edge-to-edge iOS-style chrome bars, exactly ONE lensing glass plane (or none), solid white capsule-and-squircle cards each carrying a faint top-light hairline. Capsule-first control geometry is this module's exclusive control language.
**Exclusive structural signature** (no other module may copy) — the single full-bleed snapshot-glass overlay (`AddBotPanel`) + scroll-edge bars. This module OWNS the app's only specular sweep; no other module may fire one.

**Art direction brief** — A pane of thick, freshly cleaned glass hovering a few millimetres above bright paper. Content beneath stays sharp, saturated, opaque; only floating chrome — sheets, popups, the wizard overlay — is glass, and the identity of that glass lives in its rim: a band where the backdrop visibly bends, caught by two specular arcs from a fixed 135° top-left light. Centers are calm; edges perform — and that rule applies to SOLID surfaces too, at quarter strength, so daily screens carry the same light without one blur pass. Touchstones: iOS 26 Control Center tiles (the rim is the effect, not the blur), Apple Maps' floating sheet over a live map, Things 3 for flat, quiet content under performing chrome.

**Design tokens**

Shadow convention: `(x, y)` offsets in units, negative y = down; x is always 0.

| Token | Value | Use |
|---|---|---|
| `Surface/Page` | `#F0F2F5` | Screen floor |
| `Surface/Card` | `#FFFFFF` | Bot cards, chat rows, tiles — opaque, never glass |
| `Surface/Well` | `#F2F2F7` | Inputs, inset groups |
| `Surface/Scrim` | `#1A1A2E` @ 40% | Under glass over LIGHT backdrops (see text-on-glass rule) |
| `Glass/Tint` | `#FFFFFF` @ 16% | Base fill of a glass plane; 20% is an authoring-time choice for surfaces over dark backdrops (e.g. the wizard over the dimmed bots list) — never computed at runtime |
| `Glass/Saturate` | 1.65 | Backdrop saturation multiplier — never ship blur without it |
| `Glass/Brightness` | 1.06 | Backdrop lift; 1.00 over dark backdrops |
| `Glass/RimLight` | `#FFFFFF` 50% → 5% | Top-left specular arc, 3u stroke |
| `Glass/RimCounter` | `#FFFFFF` @ 18% | Bottom-right counter-arc, 3u |
| `Glass/EdgeDark` | `#1A1A2E` @ 8% | Lower-perimeter hairline |
| `Solid/TopLight` | 1u top hairline `#FFFFFF` @ 40% | quarter-strength rim on EVERY solid card — the solid-surface signature |
| `Rim/Width` | 24u sheets · 18u buttons | Lensing band, edge inward |
| `Refract/Max` | 15u | Peak backdrop displacement at the rim, 0 at center |
| `Blur/Sheet` | 60u equiv (3 Kawase iters, ¼ res) | Sheets, modals, wizard |
| `Blur/Control` | 36u equiv (2 iters) | Small glass capsules |
| `Ink/Primary` | `#1A1A2E` | Titles, values |
| `Ink/Secondary` | `#626C7A` | Labels, timestamps, placeholder (5.3:1 on white, 4.8:1 on Well) |
| `Ink/Tertiary` | `#C7C7CC` | Disabled ONLY — never information |
| `Brand/CTA` | `#1257A8 → #1668CC`, 135° | CTA gradient; white label 7.11:1 / 5.40:1 |
| `Brand/Icon` | `#1B7CEB` | icons, rings, non-text accents (≥3:1 in context) |
| `Channel/WhatsApp` | `#25D366`, deep `#00A884` | Flat, saturated identity — never behind glass, never a text fill |
| `Channel/Telegram` | `#2AABEE` | Same rule |
| `Badge/Unread` | fill `#00734F`, 26 white count (5.89:1) | channel identity stays in the channel dot |
| `Status` (dot/fg/bg) | collected `#23A55A`/`#14713C`/`#E8F8EE` (5.52) · owner `#F8942F`/`#9A4E0B`/`#FCE1D0` (4.85) · dialog `#1B7CEB`/`#1257A8`/`#E8F2FD` (6.28) · silent `#8A93A3`/`#566070`/`#EEF1F5` (5.61) · closed `#A348D4`/`#7A2FA6`/`#EADCF1` (5.75) | 5 dashboard pills, solid, always dot + label |
| `Danger` | `#C62828` text / bg `#FCE8E6` | Destructive |
| `Border/Hairline` | `#E4E6EB`, 3u | Card edges (decorative) |
| `Border/Strong` | `#7A8699`, 3u | Inputs, toggle-off tracks — the 1.4.11 boundary (3.69:1 on white) |
| `Radius` | Sheet 60 (top) · Card/Glass 48 · Input 36 · Capsule 999 | inner = outer − padding where padding permits; when a stated radius pair cannot satisfy the rule at the stated padding, the INNER radius yields |
| `Shadow/Float` | (0, −24), blur 90, `#1A1A2E` 14% | Under glass |
| `Shadow/Card` | (0, −9), blur 30, `#1A1A2E` 6% | Under solid cards |

**Material recipes**

*Glass Regular — one shader, one quad.* Layer 1: frozen backdrop snapshot in `_BackdropTex` (¼-res, `Blur/Sheet`), saturation ×1.65, brightness ×1.06. Layer 2: refraction — displace `_BackdropTex` UVs along the rounded-rect SDF's outward normal, magnitude `Refract/Max × smoothstep(Rim/Width, 0, sd)`. The R/G/B-staggered prism fringe (scales ×1.00/1.05/1.10) is a STRETCH GOAL — if it costs more than an hour or shows artifacts inside `RectMask2D`, ship without it; the rim arcs carry the style. Layer 3: `Glass/Tint` flat fill. Layer 4: 135° sheen, white 10% → 0% across the top 40%. Layer 5: dual rim arcs — `Glass/RimLight` centered on the top-left corner falling to 5% at the side midpoints, `Glass/RimCounter` opposite; never a uniform border. Layer 6: `Glass/EdgeDark` on the lower half-perimeter. Shadow: `Shadow/Float` as a 9-slice pre-blurred sprite sibling behind the quad.

*Text on glass — deterministic rule, decided per surface at AUTHORING time, never runtime:* (a) over light backdrops (paper, bots list, settings): `Surface/Scrim` @40% under the plane + dark ink — verified ≥4.5:1; (b) over dark or media-heavy backdrops (attach sheet over a photo-filled chat): EITHER the text-bearing region carries a ≥86% white text plate under dark ink (≥13:1 over a black plate), OR ink flips to `#FFFFFF` with the scrim raised to 55%. A dark scrim under dark ink can never pass on dark plates — pick (a) or (b) per surface and record it in the builder.

*States.* **Raised** (frontmost sheet): tint 20%, shadow blur 120 @ 18%, rim light 60%. **Pressed**: scale 0.97, tint 22%, rim light 65%. **Disabled**: tint 10%, rims flat 15%, sheen and shadow off, ink `Ink/Tertiary`. **Reduce Transparency** (`Theme.A11y`): layers 1–4 replaced by opaque `#FFFFFF` 96% + `Border/Strong` hairline; keep radius and shadow.

*Solid Card — the 95% case.* `Surface/Card`, radius 48, 3u `Border/Hairline`, `Shadow/Card`, PLUS the signature: `Solid/TopLight` 1u top hairline + `Glass/EdgeDark` lower hairline — every solid card reads as "lit from 135°" without a single blur pass. Controls on solid screens are capsules wherever geometry permits.

*Scroll-edge bar — the live-content substitute.* Top bars, composer, tab bar sit over scrolling lists where snapshots are impossible: fill `#FFFFFF` @ 97%. Top bars add a 48u-tall Image immediately BELOW the bar, gradient `Surface/Page` 100% at its top edge → 0% at its bottom. Bottom bars/composer mirror it: strip immediately ABOVE the bar, 100% at its bottom edge → 0% at top. This is the iOS scroll-edge effect and it reads as glass without one blur pass.

**Guardrails — do NOT**

- Do NOT put glass on content: bot cards, chat rows, bubbles, dashboard tiles, pills, form fields.
- Do NOT attempt live blur under scrolling content — snapshot-frozen backdrops only; bars use the scroll-edge recipe.
- Do NOT nest glass in glass, and never exceed 3 simultaneous planes (target 1).
- Do NOT ship blur without `Glass/Saturate` 1.65 — desaturated blur is gray mud.
- Do NOT draw a uniform 1px white border on glass; the dual-arc 50%/18% asymmetry IS the light direction.
- Do NOT capture or blur per frame, and never animate blur radius — animate tint/scale of the held snapshot.
- Do NOT let glass tint touch `#25D366`/`#2AABEE` — channel identity stays flat and saturated.
- Do NOT glass the activation switch, delivery ticks, or unread badges — trust-critical signals stay solid high-contrast.
- Do NOT add `Shadow`/`Outline` components to TMP text for depth — mesh duplication, hard edges.
- Do NOT put body text on glass without the text-on-glass rule applied — a 40% dark scrim alone never licenses dark ink over a dark backdrop.
- Do NOT animate `_SweepPos` on a shared material — per-element material, ≤2 hero elements, hero moments only.
- Do NOT put white text on `#1B7CEB`, `#1FA2FF`, `#25D366`, or `#2AABEE` — text fills come from the `Brand/CTA` family or `Badge/Unread`.

**Icons & type voice**

Icons: filled sprites with continuous-corner (squircle) construction, 66u grid, no interior strokes — glass reads best against solid filled glyphs. Type: the 4 project `SFProText-* SDF` fonts; Semibold titles, Regular body. Illustration: pre-baked blurred wallpaper sprite + existing assets (`bot_hero.png`) only; no new illustration assets without an owner decision.

**Component specs**

- **Primary button** — 132 tall capsule, 135° `Brand/CTA` gradient fill, label 42 semibold `#FFFFFF` (worst stop 5.40:1), inner top highlight white 28% × 3u, shadow (0,−12) blur 36 @ 14%. Solid — the CTA is never ambiguous glass.
- **Secondary button** — 132 tall capsule; inside glass sheets → Glass Regular (`Blur/Control`); everywhere else → `Surface/Well` fill with label 42 `#1668CC`.
- **Bot card** — Solid Card, 48 side margins, padding 48; avatar 132, name 47 semibold, channel dot 24; 3u divider; footer 120 with the activation switch.
- **Chat row** — solid, 216 tall; avatar 144 circle, title 42 `Ink/Primary`, preview 38 `Ink/Secondary` one line, unread capsule 48 tall `Badge/Unread` with 26 white count.
- **Input field** — 132 tall, radius 36, `Surface/Well`, 3u `Border/Strong`; focused: 6u `Brand/Icon` + outer glow `#1B7CEB` 18% blur 36.
- **Toggle** — track 156×90 capsule, hit rect padded to ≥132×132; off `#C6CBD3` fill + 2u `Border/Strong` border (the visible boundary), on `#25D366` (ALWAYS — per-bot, never per-channel); knob 78 white with contact shadow (0,−3) blur 9 @ 10%; RU label swaps. Always solid.
- **Status pill** — 72 tall capsule, 36 side padding, `Status` dot+fg/bg, label 32 semibold in the deep fg ink. Solid chips; capsule grows when the label wraps.
- **Tab bar** — height 204 (baked safe zone), Scroll-edge bar recipe; active icon `Brand/Icon` + 26 label `#1257A8`, inactive icon `#8E8E93`, label `#626C7A`.
- **Bottom sheet** — Glass Regular, top radius 60, grabber 108×12 `#C6CBD3` 60%, over the text-on-glass rule's chosen variant + snapshot. Controls inside are solid.
- **Modal** — 912 wide, radius 60, Glass Regular; title 48, body 42 on a text plate, stacked 132 buttons with 24 gap.
- **Empty state** — illustration 480, headline 52, body 38 `Ink/Secondary` max 2 lines, CTA 72 below. Flat.
- **Avatar** — 144 list / 132 card / 96 dashboard, circle, 3u white 60% inner ring, silhouette fallback on `Surface/Well`.

**Screen-by-screen direction**

- **Onboarding** — Pre-bake the doodle wallpaper blurred as a sprite; the channel-connect card is the only glass, floating on it. Hero: on connect, the glass card fires the app's one specular sweep and crossfades to the CONNECTED channel's solid color (`#25D366` or `#2AABEE`) — glass becoming solid says "this is now real."
- **Bots list** — All cards solid on `Surface/Page` with the `Solid/TopLight` signature; header and tab bar use the scroll-edge recipe. Hero: the activation switch — solid, highest-contrast control on the card, latching with the 0.22s knob glide. (The delete popup is just a standard glass modal — destruction is never the aesthetic centerpiece.)
- **Dashboard (Сводка)** — Filter chips are solid capsules in a scroll-edge pinned bar (h 72 visual, hit rect padded to 132); outcome pills and metric tiles solid with delta arrows (▲/▼ + sign, never color alone) in the status color. Hero: the drill-down list sliding under the pinned bar's gradient edge.
- **Chats list** — Search capsule 120 tall (hit rect 132) in a scroll-edge top bar; rows solid; swipe-to-delete reveals a solid `Danger`-tinted panel.
- **Chat thread** — Wallpaper and bubbles unchanged (solid `#FFFFFF` / `#C5EEB6`); composer uses the scroll-edge recipe; the attach sheet and reaction bar are Glass Regular over a snapshot with the DARK variant of the text-on-glass rule (media-heavy backdrop). Quoted cards: 6u channel-color left bar on an 8% tint. Ticks and reactions stay solid.
- **Bot Settings** — Tab strip = solid segmented capsule in a scroll-edge header; panes are solid wells. `FocusScrim` becomes the snapshot-blur + scrim; `ItemEditSheet` is the flagship glass sheet.
- **Add-Bot wizard** — `AddBotPanel` is ONE full glass plane over the frozen, blurred Bots list — the strongest single use in the app and this module's structural signature. Business tiles inside stay solid colored squircles; QR/code panels are solid cards on the glass.
- **Profile** — Solid grouped rows; sub-pages (`ProfileSubPagesBuilder`, `Tools/Profile Sub-Pages/Build`) slide in as glass planes over the frozen parent, so back-swipe reads as peeling a pane away.

**States (loading / error / empty)** — per the master triad, in this material: loading >300ms = restyle the existing indicators (chat-list sync, dashboard fetch) as a dimmed scroll-edge bar pulse + solid skeleton rows; failure = restyle the existing rollback/PopupUI affordances; empty = flat (above). No new systems.

**Motion & feedback**

| Action | Tween | Duration | Ease |
|---|---|---|---|
| Sheet open | `DOAnchorPosY` + snapshot `DOFade` 0→1 | 0.25s | OutBack |
| Sheet close | `DOAnchorPosY`, snapshot fade 0.15s | 0.22s | InCubic |
| Page enter | `DOAnchorPosX` + `DOFade` | 0.30s | OutCubic |
| Press | `DOPunchScale` 0.03 + tint `DOFloat` 0.16→0.22 | 0.15s | OutQuad |
| Tab switch | icon `DOColor` + `DOPunchScale` 0.08 | 0.22s | OutCubic |
| Scrim in | `DOFade` 0→0.40 | 0.20s | Linear |
| Sheen sweep (hero only) | shader `_SweepPos` `DOFloat` −1→1 | 0.60s | InOutSine |
| **Peak — bot connected** | card `DOScale` 1→1.04→1, rim `DOFloat` 0.50→0.90→0.50, ONE sweep, then `DOColor` crossfade to the channel solid | 0.90s | OutBack → InOutSine |
| **End — settings saved** | check `DOScale` 0→1 + row flash `#E8F8EE` and back | 0.45s | OutBack |

**Unity notes — style-specific deltas only** (the universal contract lives in the master)

- Fork `Assets/Shaders/RoundedCornersBordered.shader` → `UILiquidGlass.shader`; keep its clipping and `fwidth` AA; add `_BackdropTex/_RimWidth/_RefractScale/_TintAlpha/_Saturate/_SweepPos`. Rim, sheen, and refraction live in one pass on one quad — you never fight Nobi's no-child-masking.
- Snapshot pipeline: on open, `ScreenCapture.CaptureScreenshotIntoRenderTexture()` → 270×600 → 3 dual-Kawase iterations via `Graphics.Blit` → bind `_BackdropTex`; release the RT on close; re-capture on `OnApplicationFocus(true)` (the pairing-code flow backgrounds by design — a lost RT on Android resume otherwise leaves garbage behind the auth sheet).
- Low-end fallback: one static check in a single utility class via `SystemInfo` (`graphicsMemorySize` / device heuristic) at launch — no plugin, no package; below tier, `_BackdropTex` is replaced by flat `#FFFFFF` 94% while rim + shadow stay (the rim carries the style).
- Glass adds ≤3 draw calls (≤3 planes, each its own material); everything else must batch.

**Accessibility floor** — the master floor (§4) applies in full; module-specific: body 42 on any glass plane must measure ≥4.5:1 on all four test plates (white, black, 50% gray, doodle wallpaper) — the text-on-glass rule is how, not optional polish. `Theme.A11y.ReduceTransparency` → the opaque variant on all glass; `ReduceMotion` → replace OutBack overshoot and the sweep with 0.20s linear crossfades. RU: system CTAs and switch labels wrap at 38 before any shrink; 32 is legal only for chips/captions; nothing shrinks below 32; «Бот работает»/«Бот на паузе» must both fit the footer at 38 untruncated.

**Definition of done**

1. `UILiquidGlass.shader` exists, forked from `RoundedCornersBordered.shader`; a glass sheet inside a `RectMask2D` shows no edge bleed.
2. `Main.unity` still reads `m_RenderMode: 0` — no canvas migration commit exists.
3. Frame Debugger during a `ScrollRect` drag on Bots/Chats/Dashboard shows zero capture or blur passes; exactly one capture+Kawase chain fires on sheet open.
4. The wizard overlay (heaviest screen) shows ≤3 glass material instances and ≤45 total draw calls.
5. Every sheet/popup (`ItemEditSheet`, attach sheet, bot-switcher, delete popups, `AddBotPanel`) opens over a held snapshot; the RT is released on close (Memory Profiler: no leak after 10 cycles) and re-captured on app resume.
6. `grep -rn 'Hex("' Assets/Editor/ --include='*.cs' | grep -v ThemeBuilderKit` returns 0 for every restyled builder.
7. Zoomed screenshot of any glass plane shows the rim: top-left arc visibly brighter than bottom-right, displacement in the outer band, center undistorted; every SOLID card shows the `Solid/TopLight` hairline.
8. All 5 status pill fg/bg pairs and body-on-glass measure ≥4.5:1 on all four test plates; the CTA label ≥4.5:1 at the gradient's LIGHTEST stop.
9. An EditMode test over the rebuilt hierarchies asserts every interactive hit rect ≥132×132 with ≥24 spacing.
10. `Theme.A11y.ReduceTransparency` and `ReduceMotion` each produce a screenshot-verified distinct, fully legible build.
11. «Бот работает», «Бот на паузе», and the longest RU wizard strings render untruncated at 1080×1920 and 1080×2400.
12. Each restyled builder is its own commit with the scene saved and the payload verified per the master ritual (§3.8).

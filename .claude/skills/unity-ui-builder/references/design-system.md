# Design System — Full Reference (Project-Calibrated)

Read this when you need the rationale behind the calibrated numbers, the full rendering-gotcha catalog with how-to, or per-component specs. The quick tables in `SKILL.md` are the abridged version; this is the source of truth.

## Table of contents
1. Why reference units (and not pixels)
2. The measured type scale
3. The spacing scale
4. Rendering gotchas — full catalog with fixes
5. Runtime override points (edits that don't take in the prefab)
6. Per-component specs
7. Verification workflow

---

## 1. Why reference units (and not pixels)

`Assets/Scenes/Main.unity` has one Canvas with `CanvasScaler` set to **Scale With Screen Size**, reference resolution **1080×1920** (`m_ReferenceResolution: {x: 1080, y: 1920}`, ~line 1191), `Match = Width (0)`.

Consequences:
- The canvas is **always 1080 units wide** regardless of device. Heights vary with aspect ratio (the game view tests at 1080×2400).
- Because Match=Width, horizontal layout is rock-solid at 1080; vertical content must tolerate taller/shorter screens (use anchors + layout groups, not fixed Y).
- **Every authored size is in these units.** A value of `48` is 48/1080 of screen width ≈ 4.4% — not "48 pixels."
- The device is xxhdpi-class, so **1 dp ≈ 3 reference units**. Mockups specified in dp or CSS px must be scaled ×3 for spacing/sizing. Skipping this is the #1 cause of "looks tiny on device, must hand-fix."

Type is the exception: don't blindly ×3 a mockup's `font-size`. Use the measured scale in §2, which already reflects how text is sized in this project.

## 2. The measured type scale

Distribution of `m_fontSize` actually used across `Main.unity` (count × size):

```
31× 42    16× 44    11× 50    10× 39     7× 48     7× 28
 6× 47     6× 26     4× 40     4× 36     4× 32     2× 72
 2× 24     1× 66     1× 60     1× 55     1× 38
```

42 is by far the most common — it's the default body size. Mapping the clusters to roles:

| Role | Size (units) | Maps to clusters |
|------|------|-----|
| Display / hero number | 60–72 | 60, 66, 72 |
| H1 — page title | 50–55 | 50, 55 |
| H2 — section header | 47–48 | 47, 48 |
| H3 — card title / prominent label | 42–44 | 42, 44 |
| **Body — default** | **40–42** | 40, 42 (workhorse) |
| Body2 — secondary | 36–39 | 36, 38, 39 |
| Caption — meta | 28–32 | 28, 32 |
| Micro / overline | 24–26 | 24, 26 |

For comparison: a generic web design system would call body "16sp." Here body is **42**. That's the ~2.6× gap that makes mockup-pixel text render unreadably small. When in doubt, **use 42 for body and step up/down the table from there.**

## 3. The spacing scale

Spacing follows a 4dp grid, converted ×3 to reference units:

| Token | dp | Units | Use |
|-------|----|----|-----|
| xs | 4 | 12 | Icon padding, tight groups |
| sm | 8 | 24 | Between related elements |
| md | 16 | 48 | Card padding, section padding |
| lg | 24 | 72 | Between sections |
| xl | 32 | 96 | Page margins |
| xxl | 48 | 144 | Hero spacing |

Sizing anchors (also ×3):
- Touch target minimum: 40–44 dp → **120–132 units**
- FAB / round icon button: ~40 dp → **~120 units**
- List row height: typically 1 unit-line of body + md padding top/bottom.

## 4. Rendering gotchas — full catalog with fixes

These have each cost a manual fix-up pass before. Handle them proactively.

**4.1 Rounded corners need a script, not just an Image.**
A plain `Image` renders square. Section backgrounds, cards, and buttons need the rounded-corner component applied explicitly (the project uses a RoundedCorners package / corner script). When writing a builder, add and configure that component on every rounded surface — don't assume the sprite handles it.

**4.2 TMP-drawn icons don't render.**
Chevrons, arrows, plus-signs, and similar glyphs typed as TextMeshPro characters silently fail to show on device. **Always use an `Image` + sprite for icons.** If you catch yourself putting a "›" or "→" in a TMP string for visual purposes, stop and swap it for a sprite.

**4.3 Set TMP alignment explicitly.**
Default TMP alignment is frequently wrong for: avatar initials (need center+middle), button labels (center), numeric badges (center), right-aligned timestamps. Set `alignment` on every TMP element where position matters rather than relying on the default.

**4.4 Pixel numbers that look wrong but are right.**
`Screen_Whatsapp/ChatsPanel/TopBar` has `sizeDelta.y = 250` (Main.unity ~line 10904). It looks ~2× too tall in the raw scene file but renders correctly on device once safe-area + scaling apply — confirmed visually on iOS. **Do not shrink header bars** on the WhatsApp/Telegram/Bots pages just because the raw pixel height looks large. If a header looks off, verify in Game view before changing it.

## 5. Runtime override points (edits that don't take in the prefab)

Some visual values are re-stamped by code at bind/instantiate time, so editing the prefab or a default constant has **no effect** on what's rendered.

**Chat message bubbles:** `MessageItemView` re-applies `layout.padding` per `MessageType` when each message binds. Editing the prefab's padding AND the `BubblePad*` constants both do nothing — the per-type branch overrides them. To change bubble padding/height, edit the per-type `RectOffset` in `MessageItemView` (e.g. the `MessageType.Chat` branch, which covers both incoming and outgoing text bubbles). A bubble clipped slightly at the top edge is the accepted cost of matching WhatsApp's per-screen bubble count — don't chase it.

General rule: if a UI metric won't change after you edit the prefab, grep the binding view (`*ItemView`, `*View`) for where it sets `padding`, `sizeDelta`, `color`, or `alignment` at runtime and edit there.

## 6. Per-component specs

Derive specifics from the closest existing screen, but as defaults:

- **Page title row:** H1 (50–55), left-aligned, xl (96) top margin or below the top bar, md (48) horizontal margins.
- **Section header:** H2 (47–48), lg (72) above, sm (24) below.
- **Card:** md (48) internal padding, rounded corners via script, sm (24) between stacked cards, H3 (42–44) title + Body2 (36–39) subtitle.
- **List row:** Body (42) primary text, Caption (28–32) meta, touch target ≥ 120 units tall, sprite icons only.
- **Primary button:** full-width or thumb-zone, ~120 units tall, Body (42) center-aligned label, rounded corners, `DOPunchScale` on press.
- **Modal / sheet:** open with `DOScale 0.9→1 + DOFade` (OutBack, 0.25s); dim scrim behind; primary action in thumb zone.

## 7. Verification workflow

UI polish is inherently visual and iterative. The skill cannot see the screen, so the goal is to be *right on the first pass* and make any remaining iteration cheap:

1. Build the UI in a `[MenuItem]` builder, following the closest existing builder.
2. Run the self-check in `SKILL.md` — units, icons, corners, alignment, override points.
3. Render in **Game view at 1080×2400** and compare side-by-side with the nearest existing screen.
4. If something's off, fix at the correct tune-point (§5) — don't blame scroll position or "rest state" for a size difference; it's almost always a metric.
5. If you (the model) cannot open Game view, say so plainly and hand the user a precise, short list of what to eyeball — never claim visual correctness you didn't verify.

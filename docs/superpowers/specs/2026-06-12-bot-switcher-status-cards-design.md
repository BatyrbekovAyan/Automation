# Bot Switcher Status Cards — Design

## Goal

Rebuild `Sheet_BotSwitcher` from the current undersized native-list look into a status-card bottom sheet (user-approved variant B): gray sheet, one white rounded card per bot, WhatsApp + Telegram connection chips on every card, and a blue ring + corner check badge on the active bot.

This also fixes the root visual defect: `BotSwitcherSheetBuilder` authors sizes in raw iPhone points (72-unit rows, 16-unit fonts, 48-unit avatars) on a 1080×1920 reference canvas where 1 dp ≈ 3 units — everything renders at roughly 40% of intended size. The rebuild authors everything in calibrated reference units.

User decisions (2026-06-12): variant B (status cards) over refined-list and avatar-grid; accent = app primary blue `#1B7CEB`; extras = Telegram status chips only (no "New bot" entry, no bot-count header label).

## Non-goals

- No change to open/close mechanics, backdrop, or the `BotSwitcherSheet` slide-up contract (bottom-anchored panel, pivot Y = 0).
- No change to `BotSwitcherTitleBinder` or the WhatsApp header title.
- No change to bot data, persistence, or `ChatManager.SetActiveBot` flow.
- No real WhatsApp/Telegram profile avatars — cards keep the business-tint + icon identity badge.
- No dynamic sheet height — fixed height, scrolls beyond ~4 bots (matches the existing controller's height-captured-at-Awake contract).

## Visual spec (reference units, 1080×1920 canvas)

### Sheet shell

| Element | Value |
| --- | --- |
| Panel height | 1180, bottom-anchored (pivot/anchor Y = 0) |
| Panel background | `#F0F2F5` (app gray, same as BotSettings `Bg`) |
| Top corner radius | 60 (top-only via `ImageWithIndependentRoundedCorners`) |
| Grabber | area 72 tall; pill 108×12, radius 6, color `(0.78, 0.78, 0.80)` |
| Title | "Switch bot", TMP 44 semibold, `#1A1A2E`, centered, 100 tall |
| List | `ScrollRect`, side padding 48, top 12, bottom 96 (bottom includes home-indicator allowance — safe zones are baked into sizes in this project, never runtime `Screen.safeArea`), card spacing 24 |

No divider — cards on gray don't need one.

### Bot card

| Element | Value |
| --- | --- |
| Card | 228 tall, white, radius 48, `HorizontalLayoutGroup` padding 36 L/R, spacing 36, middle-left aligned |
| Selection ring | root image, radius 54, color clear normally / `#1B7CEB` when active (card body is a child inset 6 on all sides — see Architecture) |
| Avatar | 144 circle (`ImageWithRoundedCorners` radius 72), business tint via `Bot.GetBusinessIconTint()`, child `IconSprite` at 64% (~92) via `Bot.GetBusinessIconSprite()` |
| Name | TMP 42 semibold, `#1A1A2E`, single line, ellipsis |
| Chip row | below name, gap 12 above, chips spaced 16 apart |
| Chip | 66 tall pill (radius 33), h-padding 24, icon 36 + label TMP 28, icon-label gap 12 |
| Check badge | 60 circle `#1B7CEB`, white check sprite ~36, anchored to card's top-right corner (anchor (1,1), pivot (0.5,0.5), position (0,0) — straddles the corner), active bot only |

Chip states (colors follow the mockup, derived from the app palette):

| Chip | Connected | Not connected |
| --- | --- | --- |
| WhatsApp | bg `#E9F7EF`, label `#0F6E56`, icon full color | bg `#ECECEE`, label `#8E8E93`, icon alpha 0.35 |
| Telegram | bg `#E6F1FB`, label `#185FA5`, icon full color | bg `#ECECEE`, label `#8E8E93`, icon alpha 0.35 |

Chip labels are the full words "WhatsApp" / "Telegram". Icons are the existing project sprites `Assets/Images/Icons/WhatsApp.svg.png` and `Assets/Images/Icons/Telegram_2019_Logo.svg.png` — brand logos are full-color, so the disconnected state fades alpha rather than tinting (tinting a colored logo goes muddy).

Connected is the same rule the current row already uses for WhatsApp, extended to Telegram: `!string.IsNullOrEmpty(profileId) && profileId != Bot.UnauthedProfileSentinel`, reading `bot.whatsappProfileId` / `bot.telegramProfileId`.

### Animation

- Sheet slide + backdrop fade: unchanged (`BotSwitcherSheet` DOTween, 0.3s/0.25s).
- New: card cascade on open — each row's `CanvasGroup` fades 0→1 over 0.2s with `SetDelay(index * 0.05f)` and `SetLink(row.gameObject)` so destroyed rows kill their tweens.
- Tap punch-scale on card: unchanged.

## Architecture

### Why a ring-root card (not a border)

The RoundedCorners package has no border/stroke mode, and UI children always render on top of their parent — so a "ring behind the card" can't be a child of the card image. Instead the **row root image is the ring**: a rounded rect (radius 54) whose color is `Color.clear` normally and `#1B7CEB` when selected. The white card body (`CardBg`, radius 48) is a child inset 6 units on all sides; all content lives inside `CardBg`. A clear `Image` still raycasts, so the root keeps the `Button`. Selection toggles = set root image color + `SetActive` the badge. The constant 6-unit transparent rim around unselected cards shows the gray sheet behind — invisible against the 24-unit card gaps.

### `BotSwitcherRowView.cs` — rework

Fields removed: `subLineLabel`, `statusDot`, `selectedBackground`, `selectedAccentBar`, `statusConnectedColor`, `statusDisconnectedColor`.

Fields kept: `avatarImage`, `avatarIcon`, `nameLabel`, `rowButton`.

Fields added (all wired by the builder):

```csharp
[SerializeField] private Image ringImage;          // row root — clear vs accent
[SerializeField] private GameObject selectedBadge; // corner check circle
[SerializeField] private CanvasGroup canvasGroup;  // cascade fade
[SerializeField] private Image waChipBg;
[SerializeField] private Image waChipIcon;
[SerializeField] private TextMeshProUGUI waChipLabel;
[SerializeField] private Image tgChipBg;
[SerializeField] private Image tgChipIcon;
[SerializeField] private TextMeshProUGUI tgChipLabel;
```

`Bind(bot, isSelected, tapHandler)` keeps its signature (no `BotSwitcherSheet` change needed for binding):

- Name + avatar: unchanged logic.
- `ApplyChip(bg, icon, label, connected, onBg, onLabel)` private helper sets pill bg color, label color, and icon alpha per the chip-state table. Called once per platform with the connected flag from the sentinel check.
- Selected: `ringImage.color = isSelected ? accent : Color.clear`, `selectedBadge.SetActive(isSelected)`. Name stays semibold always (cards don't need the bold/normal toggle — the ring + badge carry selection).

Chip colors, the disconnected gray, and the ring/badge accent (`#1B7CEB`) live as serialized `[Header("Style")]` fields with the table values as defaults, matching the existing `statusConnectedColor` convention.

### `BotSwitcherSheet.cs` — minimal touch

`PopulateRows()` gains the cascade: after `Bind`, set `row` CanvasGroup alpha 0 and `DOFade(1f, 0.2f).SetDelay(i * 0.05f).SetLink(row.gameObject)`. Everything else (Awake hidden-position math, Open/Close, backdrop) is untouched — the new panel height flows through the existing `sheetPanel.rect.height` capture.

### `BotSwitcherSheetBuilder.cs` — rewrite

Same menu item (`Tools/Bot Switcher/Build Sheet`), rebuilt output:

- All constants authored in reference units per the table above (no scale helper needed — write final values).
- Builds the sheet shell: root + backdrop + gray panel + grabber + title + ScrollRect list.
- **Parenting (shipped 2026-06-12):** the sheet root lives inside the WhatsApp `ChatsPanel` (the screen it serves, same as AttachSheet inside MessagesPanel), not at canvas root. The builder resolves the panel by finding `BotSwitcherTitleBinder` and walking up to the transform named `ChatsPanel` — immune to container nesting between canvas and screen. The destroy sweep removes prior sheets anywhere under the canvas.
- **No UISprite (shipped 2026-06-12):** every surface (panel, grabber, ring, card, avatar tile, chip, badge) is a plain `Image` with `sprite = null`, rounded purely by the RoundedCorners shader. Stretching `UI/Skin/UISprite.psd` bakes a soft blurry border into every edge — the house builders (AttachSheet, BotSettings) never use it. Only the check glyph keeps a sprite (`UI/Skin/Checkmark.psd`).
- Builds the row template **and saves it directly to `Assets/Prefabs/BotSwitcherRow.prefab`** via `PrefabUtility.SaveAsPrefabAsset`, then wires `BotSwitcherSheet.rowPrefab` to the saved asset and deletes the in-scene holder. This removes the old manual steps (drag-to-prefab, re-wire, run avatar rebuilder) — one menu item produces the working sheet.
- Avatar internals (tile + `IconSprite` child at 64%) are built inline by this builder, keeping the same `avatarImage`/`avatarIcon` contract the runtime expects.
- Loads the two brand sprites via `AssetDatabase.LoadAssetAtPath<Sprite>`; logs an error and aborts if either is missing or not imported as a Sprite.
- Check badge uses the built-in `UI/Skin/Checkmark.psd` tinted white (same sprite the old trailing check used, recolored).

`BotSwitcherRowAvatarRebuilder` becomes obsolete (its job is folded into the sheet builder) and is deleted — running it after the new builder would restyle the avatar at the old 48-unit size. `BotSwitcherTitleAvatarRebuilder` (header title avatar) is unrelated and stays.

## Files touched

| File | Change |
| --- | --- |
| `Assets/Scripts/UI/BotSwitcherRowView.cs` | Field rework + chip binding + ring/badge selection (see above). |
| `Assets/Scripts/UI/BotSwitcherSheet.cs` | Cascade stagger in `PopulateRows` only. |
| `Assets/Editor/BotSwitcherSheetBuilder.cs` | Full rewrite: calibrated sizes, card layout, direct prefab save. |
| `Assets/Editor/BotSwitcherRowAvatarRebuilder.cs` | Delete (superseded by the new builder). |
| `Assets/Prefabs/BotSwitcherRow.prefab` | Regenerated by the builder (asset overwrite, not hand-edit). |
| `Assets/Scenes/Main.unity` | `Sheet_BotSwitcher` subtree rebuilt by the builder. |

## Risks / things to watch

- **Full rebuild wipes post-build customizations** on the current sheet and row (the old spec preserved them; this redesign intentionally replaces them). Anything the user hand-tuned on the old sheet that should survive must be re-stated in the builder constants — flag this at review.
- **Brand sprite import settings**: both PNGs must be Texture Type = Sprite (2D and UI). Verify before relying on them; the builder aborts loudly if not.
- **Main.unity churn**: rebuilding the sheet produces a large scene diff; per project experience, layout-driven RectTransform and RoundedCorners material churn is benign — verify per-fileID, not line counts.
- **`PopulateRows` destroys rows while tweens may be pending** — `SetLink(row.gameObject)` on the cascade tween makes DOTween kill them on destroy.
- **Telegram logo legibility at 36 units** inside a tinted pill: the logo is a blue disc with a white plane; if it reads muddy on-device, fallback is swapping the chip icon to a monochrome glyph later — chip structure doesn't change.
- **Sheet height vs. small phones**: 1180 of 1920 reference height (~61%) leaves the top bar visible behind the backdrop; taller aspect devices only get more headroom. Scroll handles >4 bots.

## Verification

1. Compile check via the Unity test bridge (Editor open: drop `Temp/claude/run-tests.trigger`; closed: `Tools/run-tests-headless.sh`) — existing EditMode chat tests must stay green; this change adds no tests (pure UI rebuild, no logic beyond color/alpha mapping).
2. Run `Tools/Bot Switcher/Build Sheet`, then eyeball in Game view at 1080×2400: card sizes against the BotSettings cards, chip states for a connected and an unconnected bot, ring + badge on the active bot, cascade on open, tap-to-switch closes and updates the header title.
3. The user confirms on-device look (GREEN) before commit.

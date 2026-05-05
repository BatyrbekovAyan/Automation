# BotSettings — Sticky "Add item" Button (Product/Service tabs)

## Problem

In the Products and Services tabs of `BotSettings`, the `AddProductButton` / `AddServiceButton` is a sibling of the list inside the scrolling `Content`. Once enough items are added, the button scrolls off-screen.

## Goal

Pin the Add button to the bottom of the tab so it is always visible, while the list of cards above continues to scroll independently. Only Products and Services tabs are affected.

## Non-goals

- No change to button styling, label, icon, or tap behavior.
- No change to General / Business / Prompt tabs.
- No full rebuild of `BotSettings.prefab`. The change ships as a targeted, idempotent editor tool.

## Target structure (per tab)

```
Tab (RectTransform, Image, ScrollRect)
├── Viewport                    ← bottom offset = FOOTER_HEIGHT + gap
│   └── Content                 ← list only; AddButton removed from here
│       ├── SectionHeader
│       └── ProductsParent | ServicesParent
└── StickyFooter                ← NEW. Anchored to bottom of Tab (outside ScrollRect)
    ├── Divider                 ← 1px Border hairline at top edge
    └── AddProductButton | AddServiceButton   ← reparented unchanged
```

`StickyFooter` is a direct child of the Tab GameObject (NOT inside Viewport/Content), so it does not scroll. `ScrollRect.viewport` remains the existing Viewport; we only change the Viewport's bottom offset so list content does not render underneath the footer.

## Layout details

- `FOOTER_HEIGHT` = 52 (button height) + 24 (vertical padding: 12 top + 12 bottom) = **76 px** (unscaled; applied via existing `Sz`/`Szi` helpers).
- `Viewport.offsetMin.y` set to `FOOTER_HEIGHT` so list viewport stops above the footer.
- `StickyFooter` anchored bottom-stretch:
  - `anchorMin = (0, 0)`, `anchorMax = (1, 0)`, `pivot = (0.5, 0)`
  - `sizeDelta = (0, FOOTER_HEIGHT)`
  - `anchoredPosition = (0, 0)`
- Footer background: transparent (no Image). The tab's own `Bg` color shows through.
- `Divider`: thin child at the top edge of the footer
  - `anchorMin = (0, 1)`, `anchorMax = (1, 1)`, `pivot = (0.5, 1)`
  - `sizeDelta = (0, 1)` (1 px hairline; left/right side inset of 0 so it spans full width)
  - Image color = existing `Border` token (`#E4E6EB`)
- Button reparent: existing `AddProductButton` / `AddServiceButton` GameObject is moved under `StickyFooter` with its RectTransform reset to horizontal-stretch, vertically centered:
  - `anchorMin = (0, 0.5)`, `anchorMax = (1, 0.5)`, `pivot = (0.5, 0.5)`
  - `offsetMin = (20, -26)` (20 px left inset; -26 so top edge sits 26 px above center → 52 px tall)
  - `offsetMax = (-20, 26)` (20 px right inset; +26 so top edge sits 26 px above center)
  - Net: button spans `tab_width - 40` wide × 52 tall, centered in the 76 px footer (→ 12 px gap above and below)
  - All existing components (Image, Button, AddItemButton, rounded corners, Row/PlusIcon/Label children) untouched.

## Editor tool

New file: `Assets/Editor/BotSettingsStickyAddButtonBuilder.cs`

Pattern mirrors `BotSettingsScrollableTextAreaBuilder.cs`:

- `#if UNITY_EDITOR` guard, `Automation.BotSettingsUI` usings.
- `[MenuItem("Tools/BotSettings/Pin Add Button To Bottom")]` entry point.
- Loads `Assets/Prefabs/BotSettings.prefab` via `PrefabUtility.LoadPrefabContents`.
- For each tab ("Product", "Service"):
  1. Find the tab GameObject via `BotSettings.Product` / `BotSettings.Service` serialized refs.
  2. Locate `Viewport` child (guarded — skip with warning if missing).
  3. Locate `AddProductButton` / `AddServiceButton` either currently inside `Viewport/Content` (first-time run) or already under `StickyFooter` (re-run) — skip reparenting if already in place.
  4. Create `StickyFooter` child on Tab if absent. Wire anchors and size as specified.
  5. Create `Divider` child on `StickyFooter` if absent. Wire anchors and color.
  6. Reparent the Add button under `StickyFooter` with `SetParent(worldPositionStays: false)`; reset RectTransform to the spec above.
  7. Shrink `Viewport.offsetMin.y` to `FOOTER_HEIGHT` only if different.
- Every mutation gated by an equality check → re-running the tool is a no-op once applied.
- Uses `SerializedObject` for any serialized-field assignments.
- Saves with `PrefabUtility.SaveAsPrefabAsset` if any mutation occurred.

## Testing (manual)

1. Run `Tools > BotSettings > Pin Add Button To Bottom`.
2. Open prefab in Unity: verify Products and Services tabs show the Add button pinned at the bottom with a faint hairline divider.
3. Add 10+ test cards in a live run: confirm button stays visible while cards scroll behind the footer.
4. Re-run the menu item: expect "Nothing to do — already pinned."
5. General / Business / Prompt tabs: visually unchanged.

## Rollback

Undo the prefab change in Unity or revert the commit. No runtime scripts change, so no migration path needed.

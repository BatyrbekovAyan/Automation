# Bot Settings Rebuilder Fix — Design Spec

**Date:** 2026-04-20
**Branch:** `ui-support-scripts-and-refinements`
**Owner:** Ayan
**Scope:** `Assets/Editor/BotSettingsRebuilder.cs`, followed by a manual polish pass in Unity on `Assets/Prefabs/BotSettings.prefab`, `Assets/Prefabs/Product.prefab`, `Assets/Prefabs/Service.prefab`.

---

## 1. Goal

Make `BotSettingsRebuilder.cs` produce prefabs that (a) match `Design/mockup.html` pages 7 and 8 visually, and (b) have zero runtime errors. Then do a manual polish pass in Unity to close residual visual gaps.

The underlying architecture from the 2026-04-17 redesign spec — `EditableField`, `EditableTextArea`, `FocusScrim`, `ToggleRow`, `ItemEditSheet`, `ProductCardView`, `ServiceCardView`, the thin `BotSettings.cs` controller — is correct and does not change. Only the editor script that wires them into prefabs changes.

## 2. Non-goals

- No changes to the component architecture under `Assets/Scripts/Main/BotSettings/`.
- No changes to `Manager.cs` logic. Its field-reference reads/writes already go through the new components.
- No redesign of `WhatsappAuthorization`, `TelegramAuthorization`, or `ConfirmChange*` popups — they are preserved verbatim.
- No changes to any other page (Bots list, Profile, Add Bot, Chat).
- No new C# scripts. All fixes live in the editor script and in the Unity prefab (manual pass).

## 3. Root causes of current failures

| Symptom | Root cause | Location |
|---|---|---|
| `UnassignedReferenceException: m_TextViewport` on every input tap | `TMP_InputField` created without `textViewport`, without `placeholder`, and the `Text Area` hierarchy isn't wired to the input's serialized fields | `BotSettingsRebuilder.cs:517-529` |
| `The dropdown template is not assigned` | `TMP_Dropdown` added as a bare component with no Viewport/Content/Item/ItemBackground/ItemCheckmark/ItemLabel/ScrollRect children | `BotSettingsRebuilder.cs:414-417` |
| `Unicode character U+0001F4E6 was not found in SFProText-Regular SDF` | Product/Service thumbs set emoji text (`📦 🛠 👟`) on a TMP component whose font atlas has no glyphs for those code points | `BotSettingsRebuilder.cs:99-100, 122-125` |
| No rounded corners, no shadows, no icons | Rebuilder never adds `Nobi.UiRoundedCorners.ImageWithRoundedCorners` (package is installed), never adds `Shadow`, never references sprite assets | Throughout `BotSettingsRebuilder.cs` |
| Header has no Back icon, no Save button | Header factory builds only a centered title | `BotSettingsRebuilder.cs:319-335` |
| Tab bar has no active-tab underline | Tab factory creates identical tabs; `SetActiveTab` doesn't update visuals | `BotSettingsRebuilder.cs:337-368` and `BotSettings.cs:135-144` |
| Delete Bot button unwired | Rebuilder creates a button but doesn't wire its `onClick` to any handler | `BotSettingsRebuilder.cs:431-438` |
| Section headers stack flush against previous field | VLG has uniform spacing; no 20px top margin on section headers | `BotSettingsRebuilder.cs:370-395` |
| Business type dropdown unusable | Broken TMP_Dropdown replaced the working one from the pre-rebuilder prefab | `BotSettingsRebuilder.cs:414-420` |

## 4. Fix approach — three modular changes

### 4.1 Sprite asset registry (new static class inside rebuilder)

A single `static class Sprites` at the top of `BotSettingsRebuilder.cs` holding sprite references loaded once via `AssetDatabase.LoadAssetAtPath<Sprite>()` at the start of `RebuildAll()`.

```
Sprites.BackIcon     → Assets/Images/Chat/chevron-left.png
Sprites.ChevronRight → Assets/Images/Chat/chevron-right.png
Sprites.ArrowDown    → Assets/Images/Icons/arrowDown.png
Sprites.Plus         → Assets/Images/Chat/Plus.png
Sprites.ToggleBg     → Assets/Images/Toggle/bg2.png
Sprites.ToggleHandle → Assets/Images/Toggle/handle.png
Sprites.Bin          → Assets/Images/Icons/Bin.png
```

If any `LoadAssetAtPath` returns null, the rebuilder logs `Debug.LogWarning` with the missing path and leaves the sprite slot empty (Unity shows a "missing sprite" placeholder instead of crashing). All factory methods read from `Sprites.*`, never build icons from TMP text.

### 4.2 `BusinessTypeDropdown` preservation

The `PreserveTopLevelNames` allowlist is augmented with a **depth-first search** for any `TMP_Dropdown` named `BusinessTypeDropdown` at any level of the prefab tree. Before destroying anything:

1. Walk the prefab hierarchy looking for a `TMP_Dropdown` component on a GameObject named `BusinessTypeDropdown`.
2. Verify its `template` serialized property is non-null. If null (it's the rebuilder's broken stub), discard it and log a warning instructing the user to add a fresh `GameObject → UI → Dropdown – TextMeshPro` after the rebuild.
3. If valid, detach it from its current parent (`transform.SetParent(null, false)`) and stash the reference.
4. After `BuildGeneralTab` runs, re-parent the stashed dropdown into the correct layout position inside the General tab, between the Business Type field's logical slot.

If no valid dropdown is found, the rebuilder creates a named empty GameObject as a placeholder so the `BotSettings.BusinessTypeDropdown` Inspector slot has something to reference; the user replaces it manually post-rebuild.

### 4.3 Corrected factories

**`CreateEditableField(parent, labelText, scrim, multiline)`** — produces this hierarchy:

```
Field                            (Image + ImageWithRoundedCorners-10 + Shadow)
├─ Label                         (TMP, 12sp, 500, text-muted; anchored top)
└─ Input                         (TMP_InputField; fully wired)
   └─ Text Area                  (RectTransform + RectMask2D)      ← textViewport
      ├─ Placeholder             (TMP, 16sp, italic, text-muted)   ← placeholder
      └─ Text                    (TMP, 16sp, 500, text)            ← textComponent
```

Input wiring:
- `input.textViewport = Text Area RectTransform`
- `input.textComponent = Text TMP`
- `input.placeholder = Placeholder TMP`
- `input.lineType = SingleLine` (or `MultiLineNewline` when `multiline: true`)
- `input.targetGraphic = the field's background Image`

Sizes: single-line card 64h; multi-line card 240h. Label offset from top 10; input offset from top 26 (label sits above, value below).

**`CreateProductCard` / `CreateServiceCard`** (unified `BuildCardPrefab<T>`) — thumb becomes a real `Image` component with `ImageWithRoundedCorners-10`, `color = Bg` (light gray placeholder), `sprite = null` by default. No emoji text. Chevron on the right uses `Sprites.ChevronRight` via an `Image` with native size. The entire root is still a `Button` for tap-to-open-sheet. Card gets `ImageWithRoundedCorners-10 + Shadow`.

**`BuildHeader`** — three children laid out horizontally via explicit anchors:

```
HeaderGroup      (Image white, 56h)
├─ BackButton    (Button, 44x44, left-anchored, Sprites.BackIcon with primary tint)
├─ Title         (TMP, 20sp 700, center-anchored, flex-fill)
└─ SaveButton    (Button, right-anchored)
   ├─ SaveButton Image: primary fill + ImageWithRoundedCorners-10
   └─ SaveButton Label: "Сохранить", 14sp 600, white, centered
```

`SaveButton.onClick` is wired to `Manager.Instance.SaveSettings` via `UnityEventTools.AddPersistentListener` in the rebuilder (so the reference survives prefab saves).

**`BuildTabBar`** — each tab becomes:

```
Tab_<label>                      (Image transparent, Button)
├─ Label                         (TMP, 13sp 600; color switches primary/text-muted)
└─ Underline                     (Image 2h, primary color, bottom-anchored, initially disabled)
```

`BotSettings.SetActiveTab` is updated (post-rebuild manual edit in `BotSettings.cs`) to enable the correct tab's `Underline` and set its label color. The rebuilder records references to both `Label` and `Underline` per tab in a new `TabVisual[]` array exposed via a `SerializedField` on `BotSettings`.

**`CreateToggleRow`** — track becomes an `Image` with `sprite = Sprites.ToggleBg` and `ImageWithRoundedCorners-16`. Handle becomes an `Image` with `sprite = Sprites.ToggleHandle`. Colors applied via `Image.color` tint for on/off state. `ToggleRow.cs` already owns the DOTween thumb-slide animation.

**`BuildDeleteBotButton`** — new sibling at bottom of General tab's VLG:

```
DeleteBotButton   (Image Danger + ImageWithRoundedCorners-14 + Shadow + Button, 56h)
└─ Label          ("Удалить бота", 17sp 700, white, centered)
```

Wired to `Manager.Instance.DeleteBot` (whatever the current method is — verified during implementation; if wiring site unclear, rebuilder leaves `onClick` empty and the user wires it manually).

**`BuildAddItemButton`** — replaces the current dashed-border approximation with a light-primary fill:

```
AddItemButton    (Image primary-light + ImageWithRoundedCorners-10 + Button, 52h)
└─ Row HorizontalLayoutGroup
   ├─ Plus Icon  (Image, Sprites.Plus, primary tint, 20x20)
   └─ Label      ("Добавить товар" / "Добавить услугу", 15sp 600, primary)
```

### 4.4 Global visual treatments

Applied inside each factory at build time:

- **Rounded corners** via `ImageWithRoundedCorners` on: fields (10), settings cards (10), toggle track (16), product thumb (10), add-item button (10), save pill (10), delete bot button (14), dropdown visible area (10).
- **Shadows** via Unity built-in `Shadow` component on cards only (fields, product/service cards, delete-bot, save pill): `effectColor = new Color(0, 0, 0, 0.08f)`, `effectDistance = new Vector2(0, -1)`.
- **Section header spacing** — section-header GameObjects get a `LayoutElement` with `preferredHeight = Szi(40)` so there's a visual break before each section.

## 5. Manual polish pass — after running the rebuilder

Open `BotSettings.prefab` in Unity and:

1. If the rebuilder warned about a missing `BusinessTypeDropdown`: right-click the General tab in the Hierarchy → UI → Dropdown – TextMeshPro. Rename to `BusinessTypeDropdown`. Drag into `BotSettings.BusinessTypeDropdown` Inspector slot. Make sure Manager's BusinessTypesSO population runs on `Start`.
2. Verify `SaveButton.onClick` shows the wired `Manager.Instance.SaveSettings` entry. If not, wire manually.
3. Verify `DeleteBotButton.onClick` is wired to the delete-bot flow.
4. Assign sprites that the rebuilder couldn't find (if any warnings appeared).
5. Verify the General tab's VLG spacing matches the mockup (tune `spacing` and padding if needed).
6. Verify tabs cycle correctly and active underline toggles.
7. Save the prefab. Check it into git.

## 6. Acceptance criteria — smoke test

Run at 1080×2400 in Play mode:

1. Open a bot → General tab renders cleanly. Console shows zero errors or warnings related to BotSettings, TMP_InputField, or TMP_Dropdown.
2. Tap each input field → field raises above scrim, keyboard appears, text commits on outside-tap or onEndEdit.
3. Tap business type dropdown → list of types appears and selection works.
4. Tap WhatsApp toggle → handle slides, color transitions, save button enables.
5. Tap Telegram toggle → same.
6. Switch between all 5 tabs → content swaps, active underline moves.
7. Products tab → Add → new card appears at top. Tap card → edit sheet rises from bottom. Edit three fields, tap Готово → values reflect on card, save-dirty state set.
8. Delete from sheet → confirmation popup, finger-release confirms, card destroys.
9. Services tab → same flows.
10. Business tab → edit textarea, header and tab bar hide on focus, restore on blur. Save dirty fires.
11. Prompt tab → same.
12. Save button → calls existing `Manager.SaveSettings`, Saved toast shows, button disables.
13. Delete Bot → confirmation popup → bot removed from Bots list.
14. Close and reopen settings → fields populate from PlayerPrefs exactly as before (no key format changes, verified by loading an existing saved bot).
15. WhatsApp/Telegram authorization flows → unchanged, work end to end.

## 7. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Previous rebuilder's broken `TMP_Dropdown` is preserved as-is | Depth-first scan rejects dropdowns with null `template` and logs a clear warning. |
| `AssetDatabase.LoadAssetAtPath<Sprite>` returns null for a typo'd path | Each lookup is guarded; missing sprites log one-line warnings and leave the slot blank (Unity shows a default placeholder, no crash). |
| `ImageWithRoundedCorners` + `Shadow` interact badly (visible artifacts) | Manual polish pass can disable one or both per element via Inspector. Not blocking. |
| Rebuilder-wired `SaveButton.onClick` → `Manager.Instance.SaveSettings` persists incorrectly across plays | Use `UnityEventTools.AddPersistentListener` with a method reference; if the method signature is uncertain, leave `onClick` empty and document the manual wire step. |
| Active tab underline logic not in current `BotSettings.SetActiveTab` | `BotSettings.cs` needs a one-method edit to toggle each tab's `Underline.gameObject.SetActive` and `Label.color`. Included in implementation plan. |
| Section-header spacing still doesn't match mockup after fix | Tune `VerticalLayoutGroup.spacing` and `LayoutElement.preferredHeight` in Inspector. Not blocking. |
| Emoji-free product thumb looks empty | Accept as follow-up. Real implementation: per-card sprite slot on `ProductCardView`, assigned via the edit sheet when the user picks an image. Out of scope for this spec. |

## 8. Rollback plan

Every implementation step is a commit. If any step regresses more than it fixes, `git revert` that commit. The end state must pass the Section 6 checklist in full; otherwise don't ship.

## 9. Out-of-scope follow-ups

- Per-card image picker for product/service thumbs (current design leaves a neutral placeholder).
- Replacement of the TMP default font with Inter (requires importing and generating a font asset).
- Visual redesign of the WhatsApp/Telegram auth screens.
- Dashed-border sprite for the Add Item button (current design uses solid light-primary fill as a reasonable visual alternative).

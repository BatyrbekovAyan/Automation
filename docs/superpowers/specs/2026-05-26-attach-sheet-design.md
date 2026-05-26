# Attach Sheet (Camera / Gallery / Document)

**Date**: 2026-05-26
**Status**: Approved, awaiting implementation plan
**Scope**: `Assets/Scripts/Chat/MessagesBottomPanel.cs`, `Assets/Scripts/Chat/KeyboardAwarePanel.cs`, new `Assets/Scripts/Chat/AttachSheet.cs`, new `Assets/Scripts/Chat/AttachmentPick.cs`, new `Assets/Editor/AttachSheetBuilder.cs`, new sprites under `Assets/Sprites/AttachSheet/`

## 1. Problem

The chat input bar's plus-icon button (`MessagesBottomPanel.attachButton`) is wired but inert — `OnAttachClicked()` is a `Debug.Log` stub. Users have no way to attach media or files from the chat screen.

Existing experimentation in `Manager.cs` (lines 1428–1463 for `NativeFilePicker`, lines 2991–3088 for `NativeGallery`) was an unrelated tryout for the bot's knowledge-base flow. Those calls validate the plugin patterns but are not part of this feature and stay untouched.

## 2. Goal

Make the plus button open a WhatsApp-style attach sheet with three options — **Camera**, **Gallery**, **Document** — that invoke the existing `NativeGallery` / `NativeFilePicker` plugins and emit a typed event with the picked file's metadata. No actual send-to-Wappi, preview, or caption flow — that's a separate later feature.

The sheet behaves as a "keyboard substitute": it lives in the keyboard area, its motion is driven by input field focus (the same mechanism that drives the OS keyboard), and the plus button only toggles which occupant fills that area (OS keyboard vs. our sheet).

## 3. Scope

**In scope**

- `AttachSheet` MonoBehaviour controller that owns sheet open/close, picker invocation, kind detection, and the `OnPicked` event.
- `AttachmentPick` POCO carrying the result payload.
- `AttachSheetBuilder` editor menu item that constructs the sheet hierarchy and wires `[SerializeField]` refs.
- Minimal additive extension to `KeyboardAwarePanel.cs`: `ExtraBottomInsetPx` setter and `EffectiveAreaCanvasPx` read-only property.
- `MessagesBottomPanel.cs` modifications: serialize fields for the sheet ref, the plus/keyboard sprites, and the attach button's `Image`; replace the `OnAttachClicked` stub with `attachSheet.Toggle()`; add `ShowPlusIcon` / `ShowKeyboardIcon` helpers; subscribe to input field deselect to close the sheet.
- Three white-on-color icon sprites + one keyboard glyph under `Assets/Sprites/AttachSheet/`. (Authored externally and dropped into the inspector — builder leaves slots empty for them.)
- Debug.Log subscriber inside `AttachSheet` so the wiring is testable in editor without a real consumer.

**Out of scope**

- Wappi media-send endpoint integration. `MessageViewModel.mediaUrl/videoUrl/mimeType/fileName/fileSize` fields already exist; using them is the next feature.
- Preview screen, caption input, send button on a staged attachment.
- Multi-select gallery (single pick only).
- Camera video (photo only).
- Document type filtering (any file accepted; `NativeFilePicker.PickFile()` with no filter).
- Drag-down-to-dismiss gesture, Android back-button handling, drag-handle visual.
- Refactoring or removing the experimentation in `Manager.cs:1428–1463` and `Manager.cs:2991–3088`. Those stay intact.
- Replacing the existing attach button GameObject or its `Button` component.

## 4. Architecture & files

**New files**

- `Assets/Scripts/Chat/AttachmentPick.cs` — `enum AttachmentKind { Photo, GalleryImage, GalleryVideo, Document }` and `class AttachmentPick { Kind, Path, FileName, MimeType, FileSizeBytes }`.
- `Assets/Scripts/Chat/AttachSheet.cs` — MonoBehaviour. `[SerializeField]` refs to the three buttons, the input field, the `KeyboardAwarePanel`, the sheet's `RectTransform`, and a serialized `sheetHeight` (default 290 canvas px). Public `event Action<AttachmentPick> OnPicked`. Public `Open()`, `Close()`, `Toggle()`.
- `Assets/Editor/AttachSheetBuilder.cs` — `[MenuItem("Automation/Build/Attach Sheet")]` builder following the `BotSwitcherSheetBuilder.cs` pattern. Idempotent: deletes any existing `AttachSheet` node before rebuilding.

**Modified files**

- `Assets/Scripts/Chat/MessagesBottomPanel.cs`:
  - Add `[SerializeField] AttachSheet attachSheet;`
  - Add `[SerializeField] Image attachButtonIcon;` (the `Image` inside the existing attach `Button`)
  - Add `[SerializeField] Sprite plusIconSprite;` and `[SerializeField] Sprite keyboardIconSprite;`
  - Replace the `Debug.Log` in `OnAttachClicked()` with `attachSheet.Toggle();`
  - Add `public void ShowKeyboardIcon()` / `public void ShowPlusIcon()` that set `attachButtonIcon.sprite`.
  - Subscribe input field's `onDeselect` (or `onEndEdit`) → `attachSheet.Close()`.

- `Assets/Scripts/Chat/KeyboardAwarePanel.cs`:
  - Add `public float ExtraBottomInsetPx { get; set; }` — pixel (screen) value pushed by `AttachSheet`.
  - Add `public float EffectiveAreaCanvasPx { get; private set; }` — set each frame by `Update()` so `AttachSheet` can read the current canvas-space area height for its own position tracking.
  - In `GetAndroidLiveHeight()` and `GetIOSTargetHeight()`: return `Mathf.Max(rawValue, ExtraBottomInsetPx)`.
  - Compute and stash `EffectiveAreaCanvasPx = ConvertToCanvasSpace(rawEffective)` each `Update()`.

**Untouched**

- `Manager.cs` — the experimentation around lines 1428–1463 and 2991–3088 stays in place. `AttachSheet` mirrors the same plugin call patterns but in its own file.

## 5. Picker behavior

When a row is tapped, the sheet starts its close transition first, then invokes the picker on close-complete (instant in Case A — see §6, on tween complete in Case B). This avoids the OS picker animating in over a still-visible sheet.

**Camera** (Photo only)
```csharp
if (NativeGallery.IsMediaPickerBusy()) return;
NativeGallery.TakePicture(path => {
    if (string.IsNullOrEmpty(path)) return;  // user cancelled — silent
    EmitPick(AttachmentKind.Photo, path);
}, maxSize: 2048);
```

**Gallery** (mixed media, single pick)
```csharp
if (NativeGallery.IsMediaPickerBusy()) return;
NativeGallery.GetMixedMediaFromGallery(
    path => {
        if (string.IsNullOrEmpty(path)) return;
        var kind = IsImageExtension(path)
            ? AttachmentKind.GalleryImage
            : AttachmentKind.GalleryVideo;
        EmitPick(kind, path);
    },
    NativeGallery.MediaType.Image | NativeGallery.MediaType.Video,
    "Select a photo or video");
```
`IsImageExtension` matches `.jpg .jpeg .png .gif .webp .heic` case-insensitively; anything else from the mixed picker is treated as video.

**Document** (any file)
```csharp
NativeFilePicker.PickFile(path => {
    if (string.IsNullOrEmpty(path)) return;
    EmitPick(AttachmentKind.Document, path);
});
```
No allowed-types filter — full file-system access per user direction.

**Cancel semantics**: when a picker callback receives null/empty, no event fires. The sheet has already closed by then; the user simply ends up back in the normal chat input state.

**Busy guard**: `NativeGallery.IsMediaPickerBusy()` is checked before Camera and Gallery taps, matching the guard at `Manager.cs:2991`. Tap is ignored when busy.

## 6. UI & lifecycle (AttachSheet lives in the keyboard area)

### 6.1 Conceptual model

The "keyboard area" is a bottom strip whose height is driven by input field focus. It can hold either the OS keyboard (default) or the `AttachSheet` (when toggled). The plus button only swaps the occupant; it doesn't drive motion. When the input field loses focus, the area collapses and whatever was in it goes with it.

### 6.2 Hierarchy

Built by `AttachSheetBuilder` under the chat screen's canvas as a sibling of `MessagesBottomPanel`:

```
AttachSheet                      RectTransform: anchored bottom (anchorMin 0,0 — anchorMax 1,0)
                                 pivot (0.5, 0), sizeDelta height = sheetHeight (default 290 canvas px)
                                 opaque Image background, white #FFFFFF
└─ Row                          HorizontalLayoutGroup, padding 24/24/16/16, child alignment center,
                                 ChildForceExpand.Width = true so the three tiles distribute evenly
                                 across the row width with even gaps
   ├─ CameraOpt                 88×120 Button, VerticalLayoutGroup (icon top, label below, spacing 8)
   │   ├─ Icon                  56×56 Image, circle background #E84545, white glyph sprite slot
   │   └─ Label                 TMP_Text, 11pt, weight 500, color #555, "Camera"
   ├─ GalleryOpt                same, background #7B5BD8, label "Gallery"
   └─ DocumentOpt               same, background #4A90E2, label "Document"
```

`sheetHeight` is configured in canvas px. The conversion to screen px (needed for `ExtraBottomInsetPx`, which works in screen px to match `KeyboardAwarePanel`'s existing internal units) happens inside `AttachSheet` using the same `Canvas.scaleFactor` / safe-area logic already in `KeyboardAwarePanel.ConvertToCanvasSpace`, run in reverse.

No `TapCatcher` GameObject. Unity's `EventSystem` already deselects the input field on outside taps; that's the close trigger.

### 6.3 Position tracking (`AttachSheet.Update()`)

```csharp
float area = keyboardPanel.EffectiveAreaCanvasPx;
float y = -sheetHeight + Mathf.Min(sheetHeight, area);
rectTransform.anchoredPosition = new Vector2(0, y);
```

- area = 0 → sheet at `y = -sheetHeight` (off-screen below)
- area = sheetHeight → sheet at `y = 0` (fully visible at canvas bottom)
- partial area → sheet position interpolates — automatically inherits Android's live `area.y` tracking and iOS's `SmoothDamp` spring.

### 6.4 Open (called from `Toggle()` when sheet is hidden)

1. Snapshot which case applies:
   - **Case A** — `TouchScreenKeyboard.visible == true` → OS keyboard is currently up.
   - **Case B** — otherwise.
2. Set `keyboardPanel.ExtraBottomInsetPx = sheetHeightInScreenPx` to keep the area "up" regardless of OS keyboard state.
3. **Case A**: dismiss the OS keyboard while keeping the input field visually selected (cursor still blinking). Because the area is already at full height via `ExtraBottomInsetPx`, the sheet's position-tracking sets `y = 0` on the next frame — feels like a panel swap, no slide. The exact mechanism for "dismiss OS keyboard but retain visual selection" is platform-finicky — see §9.1.
4. **Case B**: animate `ExtraBottomInsetPx` from 0 → `sheetHeightInScreenPx` via `DOTween.To(...)` over 0.3 s, `Ease.OutCubic`. Sheet rises in lockstep because position-tracking follows. Input field is given visual selection without raising the OS keyboard — same mechanism as Case A (§9.1).
5. `SetActive(true)` on sheet root if not already.
6. Call `messagesBottomPanel.ShowKeyboardIcon()` to swap the plus button icon to the keyboard glyph.
7. Remember `_openedOverKeyboard = (caseA)` for symmetric close.

### 6.5 Close (called from `Toggle()` when sheet is shown, or from input field `onDeselect`)

- **Closing from Case A by tapping the plus button again**: re-show OS keyboard via `inputField.ActivateInputField()`. Tween `ExtraBottomInsetPx` → 0 (no visual impact because OS keyboard now provides the area height). On tween complete: `SetActive(false)` on sheet root.
- **Closing from Case B by tapping the plus button again, OR closing from either case via input field deselect**: tween `ExtraBottomInsetPx` → 0 over 0.25 s. Sheet slides down with the area. On tween complete: `SetActive(false)`, ensure input field deselected.
- In all cases: call `messagesBottomPanel.ShowPlusIcon()` to swap the icon back.

### 6.6 Picker row tap

`Close()` first, then invoke the picker on close-complete. Camera and Gallery additionally check `NativeGallery.IsMediaPickerBusy()` and ignore the tap if busy.

## 7. Handoff API

`AttachSheet` exposes:

```csharp
public event Action<AttachmentPick> OnPicked;
```

Payload:

```csharp
public enum AttachmentKind { Photo, GalleryImage, GalleryVideo, Document }

public class AttachmentPick
{
    public AttachmentKind Kind;
    public string Path;            // absolute local path
    public string FileName;        // Path.GetFileName(Path)
    public string MimeType;        // best-effort from extension; null if unknown
    public long   FileSizeBytes;   // new FileInfo(path).Length; 0 if unreadable
}
```

The event fires only on successful picker return (path non-empty). Cancel = silent, no event.

Mime-type lookup uses a small static extension → MIME map inside `AttachSheet`: `.jpg/.jpeg → image/jpeg`, `.png → image/png`, `.gif → image/gif`, `.webp → image/webp`, `.heic → image/heic`, `.mp4 → video/mp4`, `.mov → video/quicktime`, `.pdf → application/pdf`, `.doc → application/msword`, `.docx → application/vnd.openxmlformats-officedocument.wordprocessingml.document`, `.xls → application/vnd.ms-excel`, `.xlsx → application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`, `.txt → text/plain`, `.zip → application/zip`. Unknown extensions → `MimeType = null`. (We do *not* reuse `NativeFilePicker.ConvertExtensionToFileType`, which returns a platform-specific filter token — UTI on iOS, MIME on Android — not a portable MIME string.)

**Self-subscriber for v1**: `AttachSheet` subscribes its own `OnPicked` to a private `Debug.Log` that prints `Kind`, `FileName`, `FileSizeBytes`, `Path`. This makes the wiring testable in editor without a real consumer. The real subscriber (Wappi media-send or preview/caption screen) is the next feature and will subscribe externally.

## 8. Editor builder & icon assets

`Assets/Editor/AttachSheetBuilder.cs` mirrors the structure of `BotSwitcherSheetBuilder.cs`:

- `[MenuItem("Automation/Build/Attach Sheet")]`
- Idempotent: deletes any existing `AttachSheet` GameObject before rebuilding.
- Constructs the hierarchy described in §6.2 with sizes in canvas pixels at the project's 1080×2400 reference resolution.
- Uses `SerializedObject` / `SerializedProperty` to wire all `[SerializeField]` references on `MessagesBottomPanel` and `AttachSheet`.
- Leaves the four sprite slots (`plusIconSprite`, `keyboardIconSprite`, and the three tile icons) empty so the user can drop the authored sprites in via the inspector. The builder places 32×32 white-circle placeholder children inside each tile icon `Image` so the layout reads correctly until real sprites are assigned.

**Sprite assets** — authored externally as 1× PNGs and placed under `Assets/Sprites/AttachSheet/`:
- `icon_camera.png` (white camera glyph on transparent background)
- `icon_gallery.png` (white image/mountain glyph)
- `icon_document.png` (white document glyph)
- `icon_keyboard.png` (keyboard glyph matching the visual weight of the existing plus icon)

Import settings: `Sprite (2D and UI)`, `Filter Mode: Bilinear`, `Compression: None`.

**Wiring set by the builder**:
- `MessagesBottomPanel.attachSheet` → new `AttachSheet` component
- `MessagesBottomPanel.attachButtonIcon` → existing `Image` inside the attach button (located by recursive search; missing → builder logs an error and aborts)
- `AttachSheet.inputField` → `MessagesBottomPanel.inputField`
- `AttachSheet.keyboardPanel` → existing `KeyboardAwarePanel` on `MessagesBottomPanel`
- `AttachSheet.rectTransform` → self
- `AttachSheet.cameraButton` / `galleryButton` / `documentButton` → the three tile buttons

## 9. Risks & validation

1. **Suppressing OS keyboard while retaining input field visual focus** is the one piece of platform-dependent uncertainty. Falling back to "input field not focused while sheet open" is acceptable if the suppression mechanism doesn't pan out — the architecture stands regardless. Validate on both iOS and Android during execute.
2. **Order of icon swap vs. tween start** matters for perceived snappiness — swap before the open tween starts; swap after the close tween completes.
3. **Re-entering Case A close repeatedly** (`+`, `kb`, `+`, `kb` rapidly) must not leave `ExtraBottomInsetPx` stuck at a non-zero value or leave the input field in a half-focused state. `Toggle()` guards on an `_isAnimating` flag and ignores re-entry until the current tween completes.
4. **Input field deselect happens during picker invocation** if the OS picker steals focus from Unity. The sheet should not also try to close itself in that path — guard `Close()` to be idempotent.

# Attach Sheet Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire the chat input bar's plus button to a keyboard-substitute attach sheet with Camera / Gallery / Document options that emits a typed `AttachmentPick` event.

**Architecture:** A new `AttachSheet` MonoBehaviour lives in the keyboard area; its position is driven by `KeyboardAwarePanel.EffectiveAreaCanvasPx`, which we extend with an additive `ExtraBottomInsetPx` so the sheet can hold the area open without an OS keyboard. The plus button only toggles which occupant fills the area (OS keyboard vs. sheet). Picker invocations mirror the patterns already validated in `Manager.cs` but in isolated files. An editor builder constructs the visual hierarchy idempotently.

**Tech Stack:** Unity 6 / C# / DOTween / NativeGallery / NativeFilePicker / TMPro / NUnit

**Spec:** [docs/superpowers/specs/2026-05-26-attach-sheet-design.md](../specs/2026-05-26-attach-sheet-design.md)

---

## File Map

| File | Responsibility | Status |
| --- | --- | --- |
| `Assets/Scripts/Chat/AttachmentPick.cs` | POCO + enum carrying picker result | **Create** |
| `Assets/Scripts/Chat/AttachmentTypeUtil.cs` | Pure helpers: image-extension predicate, MIME map, gallery-kind detection | **Create** |
| `Assets/Tests/Editor/Chat/AttachmentTypeUtilTests.cs` | NUnit tests for the pure helpers | **Create** |
| `Assets/Scripts/Chat/AttachSheet.cs` | MonoBehaviour controller — Open/Close/Toggle, position tracking, picker invocation, `OnPicked` event | **Create** |
| `Assets/Editor/AttachSheetBuilder.cs` | `[MenuItem]` builder that constructs the sheet hierarchy and wires `[SerializeField]` refs | **Create** |
| `Assets/Sprites/AttachSheet/` | Four sprites: `icon_camera.png`, `icon_gallery.png`, `icon_document.png`, `icon_keyboard.png` | **Author externally** |
| `Assets/Scripts/Chat/KeyboardAwarePanel.cs` | Add `ExtraBottomInsetPx` setter and `EffectiveAreaCanvasPx` read-only property | **Modify** |
| `Assets/Scripts/Chat/MessagesBottomPanel.cs` | Add sheet ref, icon-swap helpers, replace `OnAttachClicked` stub, subscribe to input field deselect | **Modify** |

---

## Task 1: AttachmentPick POCO

**Files:**
- Create: `Assets/Scripts/Chat/AttachmentPick.cs`

- [ ] **Step 1: Create the file**

Path: `Assets/Scripts/Chat/AttachmentPick.cs`

```csharp
public enum AttachmentKind
{
    Photo,
    GalleryImage,
    GalleryVideo,
    Document
}

public class AttachmentPick
{
    public AttachmentKind Kind;
    public string Path;
    public string FileName;
    public string MimeType;
    public long   FileSizeBytes;
}
```

- [ ] **Step 2: Let Unity compile**

Open Unity (or wait for it to auto-recompile). Expected: no compile errors. The file lives in the default `Assembly-CSharp`.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Chat/AttachmentPick.cs Assets/Scripts/Chat/AttachmentPick.cs.meta
git commit -m "feat(chat): add AttachmentPick POCO for attach-sheet handoff"
```

---

## Task 2: AttachmentTypeUtil (TDD)

**Files:**
- Create: `Assets/Tests/Editor/Chat/AttachmentTypeUtilTests.cs`
- Create: `Assets/Scripts/Chat/AttachmentTypeUtil.cs`

The helpers are pure functions (string in → string/enum out), so they're ideal for unit testing. Follow the same NUnit pattern used in `Assets/Tests/Editor/Chat/DeliveryTickFormatterTests.cs`.

- [ ] **Step 1: Write the failing tests**

Path: `Assets/Tests/Editor/Chat/AttachmentTypeUtilTests.cs`

```csharp
using NUnit.Framework;

public class AttachmentTypeUtilTests
{
    [TestCase("/path/photo.jpg",  true)]
    [TestCase("/path/photo.JPEG", true)]
    [TestCase("/path/photo.png",  true)]
    [TestCase("/path/photo.gif",  true)]
    [TestCase("/path/photo.webp", true)]
    [TestCase("/path/photo.heic", true)]
    [TestCase("/path/clip.mp4",   false)]
    [TestCase("/path/clip.MOV",   false)]
    [TestCase("/path/file.pdf",   false)]
    [TestCase("",                 false)]
    [TestCase(null,               false)]
    public void IsImageExtension_PathSuffix_ReturnsExpected(string path, bool expected)
    {
        Assert.AreEqual(expected, AttachmentTypeUtil.IsImageExtension(path));
    }

    [TestCase("/p/a.jpg",  "image/jpeg")]
    [TestCase("/p/a.jpeg", "image/jpeg")]
    [TestCase("/p/a.PNG",  "image/png")]
    [TestCase("/p/a.gif",  "image/gif")]
    [TestCase("/p/a.webp", "image/webp")]
    [TestCase("/p/a.heic", "image/heic")]
    [TestCase("/p/a.mp4",  "video/mp4")]
    [TestCase("/p/a.mov",  "video/quicktime")]
    [TestCase("/p/a.pdf",  "application/pdf")]
    [TestCase("/p/a.doc",  "application/msword")]
    [TestCase("/p/a.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [TestCase("/p/a.xls",  "application/vnd.ms-excel")]
    [TestCase("/p/a.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [TestCase("/p/a.txt",  "text/plain")]
    [TestCase("/p/a.zip",  "application/zip")]
    public void MimeFromExtension_Known_ReturnsMappedMime(string path, string expected)
    {
        Assert.AreEqual(expected, AttachmentTypeUtil.MimeFromExtension(path));
    }

    [TestCase("/p/a.xyz")]
    [TestCase("/p/no-extension")]
    [TestCase("")]
    [TestCase(null)]
    public void MimeFromExtension_Unknown_ReturnsNull(string path)
    {
        Assert.IsNull(AttachmentTypeUtil.MimeFromExtension(path));
    }

    [TestCase("/p/a.jpg", AttachmentKind.GalleryImage)]
    [TestCase("/p/a.png", AttachmentKind.GalleryImage)]
    [TestCase("/p/a.mp4", AttachmentKind.GalleryVideo)]
    [TestCase("/p/a.mov", AttachmentKind.GalleryVideo)]
    [TestCase("/p/a",     AttachmentKind.GalleryVideo)]
    public void GalleryKindFromPath_ImageVsVideo(string path, AttachmentKind expected)
    {
        Assert.AreEqual(expected, AttachmentTypeUtil.GalleryKindFromPath(path));
    }
}
```

- [ ] **Step 2: Verify tests fail to compile (class doesn't exist)**

In Unity: Window → General → Test Runner → EditMode → Run All. Expected: compile error or all `AttachmentTypeUtilTests` cases fail with "The type or namespace name 'AttachmentTypeUtil' could not be found."

- [ ] **Step 3: Implement AttachmentTypeUtil**

Path: `Assets/Scripts/Chat/AttachmentTypeUtil.cs`

```csharp
using System;
using System.Collections.Generic;
using System.IO;

public static class AttachmentTypeUtil
{
    private static readonly HashSet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".heic"
    };

    private static readonly Dictionary<string, string> MimeByExtension = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { ".jpg",  "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".png",  "image/png" },
        { ".gif",  "image/gif" },
        { ".webp", "image/webp" },
        { ".heic", "image/heic" },
        { ".mp4",  "video/mp4" },
        { ".mov",  "video/quicktime" },
        { ".pdf",  "application/pdf" },
        { ".doc",  "application/msword" },
        { ".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
        { ".xls",  "application/vnd.ms-excel" },
        { ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
        { ".txt",  "text/plain" },
        { ".zip",  "application/zip" }
    };

    public static bool IsImageExtension(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var ext = Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext) && ImageExtensions.Contains(ext);
    }

    public static string MimeFromExtension(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return null;
        return MimeByExtension.TryGetValue(ext, out var mime) ? mime : null;
    }

    public static AttachmentKind GalleryKindFromPath(string path)
    {
        return IsImageExtension(path) ? AttachmentKind.GalleryImage : AttachmentKind.GalleryVideo;
    }
}
```

- [ ] **Step 4: Run tests, verify they pass**

Test Runner → EditMode → Run All. Expected: all `AttachmentTypeUtilTests` pass (44 cases across three methods).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Chat/AttachmentTypeUtil.cs Assets/Scripts/Chat/AttachmentTypeUtil.cs.meta \
        Assets/Tests/Editor/Chat/AttachmentTypeUtilTests.cs Assets/Tests/Editor/Chat/AttachmentTypeUtilTests.cs.meta
git commit -m "feat(chat): add AttachmentTypeUtil + NUnit tests for MIME / kind detection"
```

---

## Task 3: Extend KeyboardAwarePanel

**Files:**
- Modify: `Assets/Scripts/Chat/KeyboardAwarePanel.cs`

Additive extension only. Existing behavior must be preserved when `ExtraBottomInsetPx` is its default value of 0.

- [ ] **Step 1: Add `ExtraBottomInsetPx` property and `EffectiveAreaCanvasPx` field**

Open `Assets/Scripts/Chat/KeyboardAwarePanel.cs`. Inside the class body, just below the `_velocityY` field at line 30, insert:

```csharp
    // ── attach-sheet hook ──────────────────────────────────────────
    /// <summary>Set by AttachSheet to keep the keyboard area "up" without an OS keyboard. Screen pixels.</summary>
    public float ExtraBottomInsetPx { get; set; }

    /// <summary>Last computed effective keyboard area in canvas-space pixels. Updated every frame.</summary>
    public float EffectiveAreaCanvasPx { get; private set; }
```

- [ ] **Step 2: Wire `EffectiveAreaCanvasPx` into Update — Android branch**

In the same file, find `void Update()` (currently line 49). Replace the `#elif UNITY_ANDROID` branch:

Before:
```csharp
#elif UNITY_ANDROID
        ApplyAndroid(GetAndroidLiveHeight());
```

After:
```csharp
#elif UNITY_ANDROID
        float liveAndroid = GetAndroidLiveHeight();
        EffectiveAreaCanvasPx = ConvertToCanvasSpace(liveAndroid);
        ApplyAndroid(liveAndroid);
```

- [ ] **Step 3: Wire `EffectiveAreaCanvasPx` into Update — iOS branch**

In the same `Update()`, replace the `#elif UNITY_IOS` branch:

Before:
```csharp
#elif UNITY_IOS
        ApplyIOS(GetIOSTargetHeight());
```

After:
```csharp
#elif UNITY_IOS
        float targetIos = GetIOSTargetHeight();
        EffectiveAreaCanvasPx = ConvertToCanvasSpace(targetIos);
        ApplyIOS(targetIos);
```

- [ ] **Step 4: Wire `EffectiveAreaCanvasPx` into the editor-simulation branch**

In the `#if UNITY_EDITOR` branch of `Update()`, replace:

Before:
```csharp
        float editorTarget = _editorKbVisible ? EditorKbTargetHeight : 0f;
        _editorSimulated = Mathf.MoveTowards(_editorSimulated, editorTarget,
                                             EditorKbSpeed * Time.unscaledDeltaTime);
        ApplyAndroid(_editorSimulated);
```

After:
```csharp
        float editorTarget = _editorKbVisible ? EditorKbTargetHeight : 0f;
        _editorSimulated = Mathf.MoveTowards(_editorSimulated, editorTarget,
                                             EditorKbSpeed * Time.unscaledDeltaTime);
        EffectiveAreaCanvasPx = ConvertToCanvasSpace(_editorSimulated);
        ApplyAndroid(_editorSimulated);
```

- [ ] **Step 5: Apply `ExtraBottomInsetPx` inside both height readers**

Find `GetAndroidLiveHeight()` (currently line 100). Replace its body:

Before:
```csharp
    float GetAndroidLiveHeight()
    {
#if UNITY_ANDROID
        if (!TouchScreenKeyboard.visible) return 0f;
        return Screen.height - TouchScreenKeyboard.area.y;
#else
        return 0f;
#endif
    }
```

After:
```csharp
    float GetAndroidLiveHeight()
    {
#if UNITY_ANDROID
        float raw = TouchScreenKeyboard.visible ? (Screen.height - TouchScreenKeyboard.area.y) : 0f;
#else
        float raw = 0f;
#endif
        return Mathf.Max(raw, ExtraBottomInsetPx);
    }
```

Find `GetIOSTargetHeight()` (currently line 110). Replace:

Before:
```csharp
    float GetIOSTargetHeight()
    {
#if UNITY_IOS
        if (!TouchScreenKeyboard.visible) return 0f;
        return TouchScreenKeyboard.area.height;
#else
        return 0f;
#endif
    }
```

After:
```csharp
    float GetIOSTargetHeight()
    {
#if UNITY_IOS
        float raw = TouchScreenKeyboard.visible ? TouchScreenKeyboard.area.height : 0f;
#else
        float raw = 0f;
#endif
        return Mathf.Max(raw, ExtraBottomInsetPx);
    }
```

- [ ] **Step 6: Verify Unity compiles, sanity-check existing behavior**

Open Unity, ensure no compile errors. Open the chat screen, focus the input field — input bar should rise as before (existing behavior preserved when `ExtraBottomInsetPx` is 0).

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Chat/KeyboardAwarePanel.cs
git commit -m "feat(chat): extend KeyboardAwarePanel with ExtraBottomInsetPx hook for attach sheet"
```

---

## Task 4: AttachSheet skeleton

Skeleton means: public surface complete with stub bodies, position tracking working, no picker calls yet. After this task, the class compiles and `Open()` / `Close()` / `Toggle()` can be called but only mutate state and log.

**Files:**
- Create: `Assets/Scripts/Chat/AttachSheet.cs`

- [ ] **Step 1: Create the file with state, fields, and stub methods**

Path: `Assets/Scripts/Chat/AttachSheet.cs`

```csharp
using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
public class AttachSheet : MonoBehaviour
{
    [Header("Layout")]
    [Tooltip("Sheet height in canvas pixels — used as fallback when no keyboard height has been observed yet.")]
    [SerializeField] private float sheetHeightCanvasPx = 290f;

    [Header("References")]
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private KeyboardAwarePanel keyboardPanel;
    [SerializeField] private MessagesBottomPanel messagesBottomPanel;
    [SerializeField] private Button cameraButton;
    [SerializeField] private Button galleryButton;
    [SerializeField] private Button documentButton;

    [Header("Tween Timings")]
    [SerializeField] private float openDuration  = 0.30f;
    [SerializeField] private float closeDuration = 0.25f;

    public event Action<AttachmentPick> OnPicked;

    private RectTransform _rt;
    private Canvas        _canvas;
    private bool          _isOpen;
    private bool          _openedOverKeyboard;
    private bool          _isAnimating;
    private bool          _suppressDeselectListener;
    private Tween         _insetTween;

    void Awake()
    {
        _rt     = GetComponent<RectTransform>();
        _canvas = GetComponentInParent<Canvas>();

        // Debug self-subscriber so wiring is observable in editor without a real consumer.
        OnPicked += pick =>
            Debug.Log($"[AttachSheet] OnPicked: kind={pick.Kind} file={pick.FileName} " +
                      $"size={pick.FileSizeBytes} mime={pick.MimeType} path={pick.Path}");
    }

    void OnEnable()
    {
        if (cameraButton   != null) cameraButton.onClick.AddListener(OnCameraTapped);
        if (galleryButton  != null) galleryButton.onClick.AddListener(OnGalleryTapped);
        if (documentButton != null) documentButton.onClick.AddListener(OnDocumentTapped);
        if (inputField     != null) inputField.onDeselect.AddListener(OnInputFieldDeselected);
    }

    void OnDisable()
    {
        if (cameraButton   != null) cameraButton.onClick.RemoveListener(OnCameraTapped);
        if (galleryButton  != null) galleryButton.onClick.RemoveListener(OnGalleryTapped);
        if (documentButton != null) documentButton.onClick.RemoveListener(OnDocumentTapped);
        if (inputField     != null) inputField.onDeselect.RemoveListener(OnInputFieldDeselected);

        _insetTween?.Kill();
    }

    void Update()
    {
        if (keyboardPanel == null) return;
        float area = keyboardPanel.EffectiveAreaCanvasPx;
        float y = -sheetHeightCanvasPx + Mathf.Min(sheetHeightCanvasPx, area);
        _rt.anchoredPosition = new Vector2(0, y);
    }

    public void Toggle()
    {
        if (_isAnimating) return;
        if (_isOpen) Close();
        else         Open();
    }

    public void Open()
    {
        if (_isOpen || _isAnimating) return;
        _isOpen = true;
        Debug.Log("[AttachSheet] Open (stub)");
        // Body filled in Task 7.
    }

    public void Close()
    {
        if (!_isOpen || _isAnimating) return;
        _isOpen = false;
        Debug.Log("[AttachSheet] Close (stub)");
        // Body filled in Task 8.
    }

    private void OnInputFieldDeselected(string _)
    {
        // Open() in Case A deactivates the input field intentionally, which fires onDeselect.
        // The suppression flag prevents that synthetic deselect from recursively closing the sheet.
        if (_suppressDeselectListener) return;
        if (_isOpen) Close();
    }

    // Used by Open() in Case A to swallow the immediate onDeselect that follows DeactivateInputField.
    private System.Collections.IEnumerator ClearSuppressNextFrame()
    {
        yield return null;
        _suppressDeselectListener = false;
    }

    private void OnCameraTapped()   { /* Filled in Task 9. */ }
    private void OnGalleryTapped()  { /* Filled in Task 9. */ }
    private void OnDocumentTapped() { /* Filled in Task 9. */ }
}
```

- [ ] **Step 2: Verify Unity compiles**

No compile errors expected. The class is incomplete (Open/Close are stubs) but the surface is in place.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Chat/AttachSheet.cs Assets/Scripts/Chat/AttachSheet.cs.meta
git commit -m "feat(chat): AttachSheet skeleton (state, position tracking, public surface)"
```

---

## Task 5: MessagesBottomPanel wiring

**Files:**
- Modify: `Assets/Scripts/Chat/MessagesBottomPanel.cs`

- [ ] **Step 1: Add the new serialized fields**

Open `Assets/Scripts/Chat/MessagesBottomPanel.cs`. Replace the `UI References` block at lines 9-13:

Before:
```csharp
    [Header("UI References")]
    public TMP_InputField inputField;
    public Button sendButton;
    public Button micButton;
    public Button attachButton;
```

After:
```csharp
    [Header("UI References")]
    public TMP_InputField inputField;
    public Button sendButton;
    public Button micButton;
    public Button attachButton;
    [SerializeField] private Image attachButtonIcon;
    [SerializeField] private Sprite plusIconSprite;
    [SerializeField] private Sprite keyboardIconSprite;
    [SerializeField] private AttachSheet attachSheet;
```

- [ ] **Step 2: Add the icon-swap helpers**

At the end of the class (just before the closing brace), add:

```csharp
    public void ShowKeyboardIcon()
    {
        if (attachButtonIcon != null && keyboardIconSprite != null)
            attachButtonIcon.sprite = keyboardIconSprite;
    }

    public void ShowPlusIcon()
    {
        if (attachButtonIcon != null && plusIconSprite != null)
            attachButtonIcon.sprite = plusIconSprite;
    }
```

- [ ] **Step 3: Replace `OnAttachClicked` to toggle the sheet**

Find `OnAttachClicked` (line 91):

Before:
```csharp
    private void OnAttachClicked()
    {
        Debug.Log("Attachment button clicked!");
    }
```

After:
```csharp
    private void OnAttachClicked()
    {
        if (attachSheet != null)
            attachSheet.Toggle();
        else
            Debug.LogWarning("[MessagesBottomPanel] attachSheet ref is null — open Tools menu and run Build Attach Sheet");
    }
```

- [ ] **Step 4: Verify Unity compiles**

No compile errors. The inspector will now show four new slots on MessagesBottomPanel; they'll be empty until Task 6's builder runs.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Chat/MessagesBottomPanel.cs
git commit -m "feat(chat): wire MessagesBottomPanel to AttachSheet, add icon-swap helpers"
```

---

## Task 6: AttachSheetBuilder editor script

**Files:**
- Create: `Assets/Editor/AttachSheetBuilder.cs`

Mirrors `BotSwitcherSheetBuilder.cs`. Idempotent: deletes any existing `AttachSheet` GameObject before rebuilding.

- [ ] **Step 1: Create the builder script**

Path: `Assets/Editor/AttachSheetBuilder.cs`

```csharp
#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class AttachSheetBuilder
{
    private const string SheetName = "AttachSheet";

    // Layout — canvas-space px at the project's 1080×2400 reference resolution
    private const float SheetHeight   = 290f;
    private const float TilePrefWidth = 88f;
    private const float TileHeight    = 120f;
    private const float IconSize      = 56f;
    private const float IconSpacing   = 8f;
    private const float LabelFontSize = 11f;
    private const int   PaddingX      = 24;
    private const int   PaddingY      = 16;

    private static readonly Color BackgroundColor = Color.white;
    private static readonly Color LabelColor      = new Color(0.33f, 0.33f, 0.33f);
    private static readonly Color CameraTint      = new Color(0.91f, 0.27f, 0.27f); // #E84545
    private static readonly Color GalleryTint     = new Color(0.48f, 0.36f, 0.85f); // #7B5BD8
    private static readonly Color DocumentTint    = new Color(0.29f, 0.56f, 0.89f); // #4A90E2

    [MenuItem("Tools/Attach Sheet/Build")]
    public static void Build()
    {
        var bottomPanel = Object.FindFirstObjectByType<MessagesBottomPanel>(FindObjectsInactive.Include);
        if (bottomPanel == null)
        {
            Debug.LogError("[AttachSheetBuilder] MessagesBottomPanel not found in the open scene.");
            return;
        }

        Transform parent = bottomPanel.transform.parent; // sheet is sibling of the input bar
        if (parent == null)
        {
            Debug.LogError("[AttachSheetBuilder] MessagesBottomPanel has no parent — unexpected hierarchy.");
            return;
        }

        // Idempotent: nuke any existing sheet
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i);
            if (child.name == SheetName) Object.DestroyImmediate(child.gameObject);
        }

        var sheetGo = new GameObject(SheetName, typeof(RectTransform), typeof(Image), typeof(AttachSheet));
        sheetGo.transform.SetParent(parent, false);

        var rt = (RectTransform)sheetGo.transform;
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot     = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(0f, SheetHeight);          // height in canvas px; width stretched by anchors
        rt.anchoredPosition = new Vector2(0f, -SheetHeight);  // start off-screen; AttachSheet.Update will track

        var bg = sheetGo.GetComponent<Image>();
        bg.color = BackgroundColor;
        bg.raycastTarget = true; // catches stray taps so they don't bubble through

        var row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(sheetGo.transform, false);
        var rowRt = (RectTransform)row.transform;
        rowRt.anchorMin = Vector2.zero;
        rowRt.anchorMax = Vector2.one;
        rowRt.offsetMin = new Vector2(PaddingX, PaddingY);
        rowRt.offsetMax = new Vector2(-PaddingX, -PaddingY);
        var hl = row.GetComponent<HorizontalLayoutGroup>();
        hl.childAlignment         = TextAnchor.MiddleCenter;
        hl.childControlWidth      = true;   // distribute remaining row width across 3 tiles
        hl.childControlHeight     = false;
        hl.childForceExpandWidth  = true;
        hl.childForceExpandHeight = false;
        hl.spacing                = 0;

        var cameraTile   = BuildTile(row.transform, "CameraOpt",   "Camera",   CameraTint);
        var galleryTile  = BuildTile(row.transform, "GalleryOpt",  "Gallery",  GalleryTint);
        var documentTile = BuildTile(row.transform, "DocumentOpt", "Document", DocumentTint);

        // Wire via SerializedObject for undo safety
        var attachSheet = sheetGo.GetComponent<AttachSheet>();
        var so = new SerializedObject(attachSheet);
        SetObjectRef(so, "inputField",          bottomPanel.inputField);
        SetObjectRef(so, "keyboardPanel",       bottomPanel.GetComponent<KeyboardAwarePanel>());
        SetObjectRef(so, "messagesBottomPanel", bottomPanel);
        SetObjectRef(so, "cameraButton",        cameraTile.button);
        SetObjectRef(so, "galleryButton",       galleryTile.button);
        SetObjectRef(so, "documentButton",      documentTile.button);
        so.ApplyModifiedPropertiesWithoutUndo();

        // Wire MessagesBottomPanel.attachSheet + attachButtonIcon
        var soPanel = new SerializedObject(bottomPanel);
        SetObjectRef(soPanel, "attachSheet", attachSheet);
        var attachIcon = FindButtonIconImage(bottomPanel.attachButton);
        if (attachIcon != null) SetObjectRef(soPanel, "attachButtonIcon", attachIcon);
        else Debug.LogWarning("[AttachSheetBuilder] Could not find Image child inside attachButton — please assign attachButtonIcon manually.");
        soPanel.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(bottomPanel.gameObject.scene);
        Debug.Log("[AttachSheetBuilder] Built AttachSheet. Now assign sprites in the inspector: " +
                  "MessagesBottomPanel.plusIconSprite/keyboardIconSprite, and each tile's Icon.sprite.");
    }

    private struct Tile { public GameObject root; public Button button; public Image icon; }

    private static Tile BuildTile(Transform parent, string name, string label, Color tint)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button),
                                typeof(VerticalLayoutGroup), typeof(LayoutElement));
        go.transform.SetParent(parent, false);

        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(TilePrefWidth, TileHeight);

        var bg = go.GetComponent<Image>();
        bg.color = new Color(0, 0, 0, 0);   // transparent tile bg; only icon shows color
        bg.raycastTarget = true;            // tap target

        var vl = go.GetComponent<VerticalLayoutGroup>();
        vl.childAlignment       = TextAnchor.UpperCenter;
        vl.childControlWidth    = false;
        vl.childControlHeight   = false;
        vl.childForceExpandWidth  = false;
        vl.childForceExpandHeight = false;
        vl.spacing = IconSpacing;
        vl.padding = new RectOffset(0, 0, 0, 0);

        var le = go.GetComponent<LayoutElement>();
        le.preferredWidth  = TilePrefWidth;
        le.preferredHeight = TileHeight;
        le.flexibleWidth   = 0;

        // Icon circle
        var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        iconGo.transform.SetParent(go.transform, false);
        var iconRt = (RectTransform)iconGo.transform;
        iconRt.sizeDelta = new Vector2(IconSize, IconSize);
        var iconImg = iconGo.GetComponent<Image>();
        iconImg.color = tint;
        // Sprite slot stays empty — user drops the white glyph PNG in via inspector after authoring.
        iconImg.raycastTarget = false;

        // Label
        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(go.transform, false);
        var labelRt = (RectTransform)labelGo.transform;
        labelRt.sizeDelta = new Vector2(TilePrefWidth, 16);
        var tmp = labelGo.GetComponent<TextMeshProUGUI>();
        tmp.text         = label;
        tmp.fontSize     = LabelFontSize;
        tmp.color        = LabelColor;
        tmp.alignment    = TextAlignmentOptions.Center;
        tmp.fontStyle    = FontStyles.Normal;
        tmp.raycastTarget = false;

        return new Tile { root = go, button = go.GetComponent<Button>(), icon = iconImg };
    }

    private static void SetObjectRef(SerializedObject so, string propertyName, Object value)
    {
        var p = so.FindProperty(propertyName);
        if (p != null) p.objectReferenceValue = value;
        else Debug.LogWarning($"[AttachSheetBuilder] Property {propertyName} not found on {so.targetObject}");
    }

    private static Image FindButtonIconImage(Button button)
    {
        if (button == null) return null;
        // Prefer a child named "Icon" or the first Image that's not the button's own background.
        foreach (var img in button.GetComponentsInChildren<Image>(true))
        {
            if (img.gameObject == button.gameObject) continue; // skip button's own bg
            return img;
        }
        return null;
    }
}
#endif
```

- [ ] **Step 2: Run the builder in Unity**

Unity → menu → `Tools` → `Attach Sheet` → `Build`. Expected console output: `[AttachSheetBuilder] Built AttachSheet. Now assign sprites in the inspector: ...`

Confirm `AttachSheet` GameObject appears in the scene hierarchy as a sibling of `MessagesBottomPanel`, with `Row` → `CameraOpt` / `GalleryOpt` / `DocumentOpt` children. Inspect `MessagesBottomPanel` and confirm `attachSheet` and `attachButtonIcon` are populated.

- [ ] **Step 3: Drop in placeholder sprites (if final art isn't ready)**

If the four sprites aren't authored yet, use any small white circle PNG as a placeholder for `MessagesBottomPanel.plusIconSprite`, `keyboardIconSprite`, and each tile's `Icon.sprite`. The flow is still testable with placeholders — the icons just won't have a glyph.

- [ ] **Step 4: Save the scene**

`File` → `Save`. Confirm `Assets/Scenes/Main.unity` shows in `git status`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Editor/AttachSheetBuilder.cs Assets/Editor/AttachSheetBuilder.cs.meta \
        Assets/Scenes/Main.unity
git commit -m "feat(chat): editor builder for AttachSheet hierarchy + scene wiring"
```

---

## Task 7: Implement AttachSheet.Open

**Files:**
- Modify: `Assets/Scripts/Chat/AttachSheet.cs`

- [ ] **Step 1: Add screen-px conversion helper and Open body**

Open `Assets/Scripts/Chat/AttachSheet.cs`. Replace the `Open()` stub at the bottom of the class:

Before:
```csharp
    public void Open()
    {
        if (_isOpen || _isAnimating) return;
        _isOpen = true;
        Debug.Log("[AttachSheet] Open (stub)");
        // Body filled in Task 7.
    }
```

After:
```csharp
    public void Open()
    {
        if (_isOpen || _isAnimating) return;
        _isOpen = true;
        _openedOverKeyboard = TouchScreenKeyboard.visible;

        // Spec §9.2: icon swap happens BEFORE the open transition starts.
        if (messagesBottomPanel != null) messagesBottomPanel.ShowKeyboardIcon();
        if (!gameObject.activeSelf) gameObject.SetActive(true);

        float sheetHeightScreenPx = CanvasPxToScreenPx(sheetHeightCanvasPx);

        if (_openedOverKeyboard)
        {
            // Case A: keyboard is visible. Park the inset immediately so the area
            // stays "up" while we dismiss the OS keyboard. Sheet's Update tracks
            // the area to y=0 within one frame — visually a panel swap.
            if (keyboardPanel != null) keyboardPanel.ExtraBottomInsetPx = sheetHeightScreenPx;

            if (inputField != null)
            {
                // DeactivateInputField will fire onDeselect synchronously — suppress so it
                // doesn't recursively trigger Close().
                _suppressDeselectListener = true;
                inputField.DeactivateInputField();
                StartCoroutine(ClearSuppressNextFrame());
            }
        }
        else
        {
            // Case B: keyboard is down. Tween the inset up so the area rises smoothly
            // and the sheet follows.
            _isAnimating = true;
            _insetTween?.Kill();
            float start = keyboardPanel != null ? keyboardPanel.ExtraBottomInsetPx : 0f;
            _insetTween = DOTween.To(
                () => keyboardPanel.ExtraBottomInsetPx,
                v  => keyboardPanel.ExtraBottomInsetPx = v,
                sheetHeightScreenPx,
                openDuration)
                .From(start)
                .SetEase(Ease.OutCubic)
                .OnComplete(() => { _isAnimating = false; });

            if (inputField != null)
            {
                // Visual selection without raising the OS keyboard (see spec §9.1).
                _suppressDeselectListener = true;
                EventSystem.current.SetSelectedGameObject(inputField.gameObject);
                StartCoroutine(ClearSuppressNextFrame());
            }
        }
    }

    private float CanvasPxToScreenPx(float canvasPx)
    {
        if (_canvas == null) return canvasPx;
        if (_canvas.renderMode == RenderMode.ScreenSpaceOverlay) return canvasPx * _canvas.scaleFactor;
        float screenH = Screen.height;
        float canvasH = ((RectTransform)_canvas.transform).rect.height;
        return canvasH > 0f ? canvasPx * (screenH / canvasH) : canvasPx;
    }
```

- [ ] **Step 2: Verify Unity compiles**

No compile errors. Open should now visibly swap the keyboard for the sheet (or slide the sheet in when no keyboard is up). Sheet rows are inert — picker bodies are Task 9.

- [ ] **Step 3: Manual smoke test (Case B)**

Hit Play. With the chat input bar focused but keyboard hidden (in editor: press `K` to simulate keyboard, then press `K` again to hide), tap the attach button. Expected: sheet slides up over ~0.3 s; input bar rises with it; attach button icon swaps to keyboard glyph (if you assigned `keyboardIconSprite`). Tap input field — should deselect → close-path runs (Task 8 is needed for the close half to look right).

Editor-only note: with the `K` key simulating keyboard, `TouchScreenKeyboard.visible` may stay false. That means the `_openedOverKeyboard` snapshot will be Case B in editor — Case A behaviour is best validated on device.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Chat/AttachSheet.cs
git commit -m "feat(chat): implement AttachSheet.Open for both keyboard-up and keyboard-down cases"
```

---

## Task 8: Implement AttachSheet.Close

**Files:**
- Modify: `Assets/Scripts/Chat/AttachSheet.cs`

- [ ] **Step 1: Fill the Close body**

In `Assets/Scripts/Chat/AttachSheet.cs`, replace the `Close()` stub:

Before:
```csharp
    public void Close()
    {
        if (!_isOpen || _isAnimating) return;
        _isOpen = false;
        Debug.Log("[AttachSheet] Close (stub)");
        // Body filled in Task 8.
    }
```

After:
```csharp
    public void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;

        if (_openedOverKeyboard)
        {
            // Case A close: bring the OS keyboard back, drop the extra inset (no visual
            // impact because OS keyboard now provides the area height). ActivateInputField
            // may fire onSelect → no recursion risk, but suppress just in case the platform
            // also fires onDeselect on the previously selected target.
            if (keyboardPanel != null) keyboardPanel.ExtraBottomInsetPx = 0f;
            if (inputField != null)
            {
                _suppressDeselectListener = true;
                inputField.ActivateInputField();
                StartCoroutine(ClearSuppressNextFrame());
            }

            // Case A is instant — no slide tween. Spec §9.2 says "swap after the close
            // transition completes"; for instant Case A that's immediately.
            if (messagesBottomPanel != null) messagesBottomPanel.ShowPlusIcon();
            gameObject.SetActive(false);
            return;
        }

        // Case B close: tween the inset down, sheet slides with the area.
        _isAnimating = true;
        _insetTween?.Kill();
        float start = keyboardPanel != null ? keyboardPanel.ExtraBottomInsetPx : 0f;
        _insetTween = DOTween.To(
            () => keyboardPanel.ExtraBottomInsetPx,
            v  => keyboardPanel.ExtraBottomInsetPx = v,
            0f,
            closeDuration)
            .From(start)
            .SetEase(Ease.OutCubic)
            .OnComplete(() =>
            {
                _isAnimating = false;
                if (inputField != null)
                {
                    _suppressDeselectListener = true;
                    inputField.DeactivateInputField();
                    StartCoroutine(ClearSuppressNextFrame());
                }
                // Spec §9.2: swap AFTER the close tween completes.
                if (messagesBottomPanel != null) messagesBottomPanel.ShowPlusIcon();
                gameObject.SetActive(false);
            });
    }
```

Notes:
- Removed the `_isAnimating` guard at the top of `Close()` because outside-tap deselect should win over a mid-flight open animation. The `_insetTween?.Kill()` cancels whatever's in flight.
- Icon swap is at the end of Case A (instant) and inside OnComplete for Case B, matching spec §9.2.
- Every `inputField.Deactivate/ActivateInputField()` call uses the same suppress-deselect dance as Open to avoid recursive callbacks.

- [ ] **Step 2: Verify Unity compiles**

No compile errors.

- [ ] **Step 3: Manual smoke test of open + close**

Play. Tap attach → sheet opens. Tap attach again → sheet slides closed (Case B) or pops closed (Case A on device). Tap somewhere else on the chat after opening — input field deselects → sheet closes via the listener.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Chat/AttachSheet.cs
git commit -m "feat(chat): implement AttachSheet.Close (Case A pop + Case B slide)"
```

---

## Task 9: Picker callbacks + event firing

**Files:**
- Modify: `Assets/Scripts/Chat/AttachSheet.cs`

- [ ] **Step 1: Fill the three picker bodies and add EmitPick**

In `Assets/Scripts/Chat/AttachSheet.cs`, replace the three picker stubs at the bottom:

Before:
```csharp
    private void OnCameraTapped()   { /* Filled in Task 9. */ }
    private void OnGalleryTapped()  { /* Filled in Task 9. */ }
    private void OnDocumentTapped() { /* Filled in Task 9. */ }
```

After:
```csharp
    private void OnCameraTapped()
    {
        if (NativeGallery.IsMediaPickerBusy()) return;
        Close();
        InvokeAfterClose(() =>
            NativeGallery.TakePicture(path =>
            {
                if (string.IsNullOrEmpty(path)) return;
                EmitPick(AttachmentKind.Photo, path);
            }, maxSize: 2048));
    }

    private void OnGalleryTapped()
    {
        if (NativeGallery.IsMediaPickerBusy()) return;
        Close();
        InvokeAfterClose(() =>
            NativeGallery.GetMixedMediaFromGallery(path =>
                {
                    if (string.IsNullOrEmpty(path)) return;
                    EmitPick(AttachmentTypeUtil.GalleryKindFromPath(path), path);
                },
                NativeGallery.MediaType.Image | NativeGallery.MediaType.Video,
                "Select a photo or video"));
    }

    private void OnDocumentTapped()
    {
        Close();
        InvokeAfterClose(() =>
            NativeFilePicker.PickFile(path =>
            {
                if (string.IsNullOrEmpty(path)) return;
                EmitPick(AttachmentKind.Document, path);
            }));
    }

    private void EmitPick(AttachmentKind kind, string path)
    {
        long size = 0;
        try { if (System.IO.File.Exists(path)) size = new System.IO.FileInfo(path).Length; }
        catch { size = 0; }

        var pick = new AttachmentPick
        {
            Kind          = kind,
            Path          = path,
            FileName      = System.IO.Path.GetFileName(path),
            MimeType      = AttachmentTypeUtil.MimeFromExtension(path),
            FileSizeBytes = size
        };
        OnPicked?.Invoke(pick);
    }

    private void InvokeAfterClose(System.Action action)
    {
        // Case A close is synchronous; Case B has a tween. Wait one extra frame
        // either way so the close has settled before the OS picker animates in.
        StartCoroutine(InvokeAfterCloseRoutine(action));
    }

    private System.Collections.IEnumerator InvokeAfterCloseRoutine(System.Action action)
    {
        // Wait until any close tween is done (or just one frame for Case A).
        while (_isAnimating) yield return null;
        yield return null;
        action?.Invoke();
    }
```

- [ ] **Step 2: Verify Unity compiles, check NativeGallery/NativeFilePicker references resolve**

If either type is unrecognised, confirm the plugins are present in `Assets/Plugins/`. `Manager.cs` already references both at the lines noted in the spec; no namespace changes should be needed.

- [ ] **Step 3: Manual UAT — happy paths**

Build & run (or play in editor with editor-supported pickers). For each row:
- Tap Camera → device camera opens → capture → console shows `[AttachSheet] OnPicked: kind=Photo file=... size=... mime=image/jpeg path=...`
- Tap Gallery → system picker → choose image → console shows `kind=GalleryImage mime=image/jpeg`; choose video → `kind=GalleryVideo mime=video/mp4` (or whichever)
- Tap Document → file picker → choose any file → console shows `kind=Document` with appropriate MIME or null

Cancel each picker — confirm no event fires, console silent.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Chat/AttachSheet.cs
git commit -m "feat(chat): wire picker callbacks, fire OnPicked event with typed payload"
```

---

## Task 10: Manual UAT checklist

This task is verification only — no code changes. Run through the checklist on real devices (or editor + simulators).

- [ ] **Editor (Mac), keyboard simulated via `K` key (Case B path)**

  1. Open chat screen.
  2. Tap attach button → sheet slides up smoothly over ~0.3 s; input bar rises with it; icon swaps to keyboard glyph.
  3. Tap attach button again → sheet slides down smoothly; input bar lowers; icon swaps back to +.
  4. Open sheet, then tap on the chat scroll area → input field deselects → sheet slides closed.
  5. Open sheet, tap Camera → sheet closes, camera attempt fails on Mac (no camera plugin support) → no event, no error.

- [ ] **Android device (Case A path)**

  1. Open chat screen, tap input field → OS keyboard rises.
  2. Tap attach button → sheet appears instantly (panel swap), input bar stays at the same height, icon swaps to keyboard glyph. OS keyboard should be hidden behind the sheet.
  3. Tap attach button (keyboard icon) → sheet disappears instantly, OS keyboard visible again, input bar still at same height, icon swaps back to +.
  4. Open sheet via Case A entry, then tap chat area → input field deselects → sheet slides closed AND OS keyboard slides down (because the field deselect now closes both).

- [ ] **Android device (Case B path)**

  1. Open chat screen with input field not focused.
  2. Tap attach button → sheet slides up; input bar rises with it; icon swaps.
  3. Tap each row, complete the picker → console shows the expected `OnPicked` payload.
  4. Cancel each picker → console silent.

- [ ] **iOS device — repeat Case A + Case B**

  Same expectations as Android. Iron out any platform quirks in "Case A keep-input-field-visually-focused" path; if it doesn't pan out, accept the deactivation fallback noted in spec §9.1.

- [ ] **Edge cases**

  1. Rapid attach button taps (5x within a second) — no stuck animations, no doubled icon swaps.
  2. Attach sheet open → background app → return → state remains consistent (or recovers on next attach tap).
  3. Picker invoked, OS picker cancelled, attach tapped again → fresh sheet opens; no `_isAnimating` lock.

- [ ] **Commit nothing; tag verification result in a follow-up message or PR description.**

---

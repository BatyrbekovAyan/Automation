# Input Text Selection (iOS-style) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** iOS-style select / cut / copy / paste (long-press word select, draggable pins, floating RU edit menu) in every `TMP_InputField` of the app, on iOS and Android, with zero scene/prefab churn.

**Architecture:** One runtime-bootstrapped singleton (`TextSelectionRouter`) observes touches, raycasts to the field under the finger, and runs a pure gesture state machine; a runtime-created overlay canvas hosts two draggable pins and a themed pill menu. All string/selection math lives in pure C# seams with EditMode tests. One thin seam (`KeyboardSelectionSync`) pushes programmatic selection into the hidden native keyboard buffer (TMP only does this on its own pointer paths). Spec: `docs/superpowers/specs/2026-08-07-input-text-selection-design.md`.

**Tech Stack:** Unity 6000.3.9f1, uGUI 2 / TextMeshPro (`com.unity.ugui@bb329a87fcdc`), NUnit EditMode tests, `Nobi.UiRoundedCorners` (UPM), project `Theme` facade.

## Global Constraints

- **Reference units:** canvas is 1080×1920 reference units (dp×3). All sizes below are reference units.
- **No scene/prefab edits anywhere in this plan.** The feature is 100% runtime-created (scene is source of truth; hand-tuned).
- **Never modify** `DeferredDismissInputField.cs`, keyboard config, or any existing input component. The layer is additive.
- **Focus activation** always routes through the field's normal path (`EventSystem.SetSelectedGameObject` + `ActivateInputField()`), never a bespoke path.
- **Mutations** only ever write the **focused field's own** `.text` (the project invariant forbids writing a *different* field while one is focused).
- **RU labels, exact strings:** «Вырезать», «Копировать», «Вставить», «Выделить всё».
- **Tests** live in `Assets/Tests/Editor/Chat/` (no asmdef — they compile into `Assembly-CSharp-Editor`). Run headless (Editor **closed**): `Tools/run-tests-headless.sh "<FilterRegex>"` → summary + NUnit XML in `Tools/test-output/`. Editor **open**: create empty file `Temp/claude/run-tests.trigger`, read `Temp/claude/test-summary.json` (Editor must be focused). Freshness: after runtime-only edits check `Assembly-CSharp.dll` mtime, not the editor-assembly stamp.
- **New-file import quirk:** with the Editor open, brand-new `.cs` files are only imported after `Assets/Refresh` (verify the `.meta` appears). Headless runs import fresh automatically.
- **Commits:** stage `.cs` **and** generated `.meta` files. Message style `feat(textselect): …`, ending with `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`. Per-task commit consent per project norm.
- **Legacy Input Manager assumed** (`Input.touchCount` / `Input.GetMouseButton`). Verify in Task 9 step 1; if the project runs the new Input System only, stop and flag before writing router input code.
- **Parallelism:** Tasks 3–8 may proceed while the owner runs the Task 2 device spike. Task 9 must NOT start before the spike verdict is GO (fallback redesign otherwise — see spec).

## File Map

| File | Kind | Responsibility |
|---|---|---|
| `Assets/Scripts/TextSelection/KeyboardSelectionSync.cs` | Create | Push Unity-side selection into the native keyboard buffer (reflection seam) |
| `Assets/Scripts/TextSelection/WordBoundary.cs` | Create | Word/emoji-cluster range at a string index; surrogate-safe clamps |
| `Assets/Scripts/TextSelection/SelectionActions.cs` | Create | Cut/copy/paste string math → `SelectionEdit` |
| `Assets/Scripts/TextSelection/SelectionMenuPolicy.cs` | Create | Which menu items are visible |
| `Assets/Scripts/TextSelection/SelectionGestureMachine.cs` | Create | Pure tap / double-tap / long-press / slop-cancel state machine |
| `Assets/Scripts/TextSelection/SelectionOverlay.cs` | Create | Runtime overlay canvas; hosts pins + menu |
| `Assets/Scripts/TextSelection/SelectionHandleView.cs` | Create | One draggable pin (view + drag events only) |
| `Assets/Scripts/TextSelection/SelectionMenuView.cs` | Create | The pill menu (view + tap events only) |
| `Assets/Scripts/TextSelection/TextSelectionRouter.cs` | Create | Singleton: touch watching, raycast, gesture→selection, menu actions, theming |
| `Assets/Editor/TextSelectionSpikeBuilder.cs` | Create | Menu item that builds the throwaway spike scene |
| `Assets/Scripts/TextSelection/TextSelectionSpikeProbe.cs` | Create | On-device OnGUI probe for the 4 GO/NO-GO checks |
| `Assets/Tests/Editor/Chat/KeyboardSelectionSyncPinTests.cs` | Test | Reflection target pin |
| `Assets/Tests/Editor/Chat/WordBoundaryTests.cs` | Test | Word/emoji/punctuation/whitespace ranges |
| `Assets/Tests/Editor/Chat/SelectionActionsTests.cs` | Test | Cut/paste/limit/surrogate clamp |
| `Assets/Tests/Editor/Chat/SelectionMenuPolicyTests.cs` | Test | Visibility matrix |
| `Assets/Tests/Editor/Chat/SelectionGestureMachineTests.cs` | Test | Gesture timing/slop matrix |

---

### Task 1: KeyboardSelectionSync seam + reflection pin test

**Files:**
- Create: `Assets/Scripts/TextSelection/KeyboardSelectionSync.cs`
- Test: `Assets/Tests/Editor/Chat/KeyboardSelectionSyncPinTests.cs`

**Interfaces:**
- Consumes: `TMPro.TMP_InputField` (private method `UpdateKeyboardStringPosition`, verified to exist in `com.unity.ugui@bb329a87fcdc` at line 1554).
- Produces: `KeyboardSelectionSync.Push(TMP_InputField field)` — safe no-op in Editor and when no keyboard is open; `KeyboardSelectionSync.TargetExists : bool`; `KeyboardSelectionSync.PushOverrideForTests : Action<TMP_InputField>` (internal test seam).

- [ ] **Step 1: Write the failing pin test**

```csharp
// Assets/Tests/Editor/Chat/KeyboardSelectionSyncPinTests.cs
using NUnit.Framework;

public class KeyboardSelectionSyncPinTests
{
    [Test]
    public void UpdateKeyboardStringPosition_StillExistsInThisTmpVersion()
    {
        Assert.IsTrue(KeyboardSelectionSync.TargetExists,
            "TMP_InputField.UpdateKeyboardStringPosition is gone — a Unity/uGUI upgrade broke " +
            "KeyboardSelectionSync. Re-point the seam (see docs/superpowers/specs/2026-08-07-input-text-selection-design.md).");
    }

    [Test]
    public void Push_WithNullField_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => KeyboardSelectionSync.Push(null));
    }
}
```

- [ ] **Step 2: Run to verify it fails to compile (class missing)**

Run: `Tools/run-tests-headless.sh "KeyboardSelectionSyncPinTests"`
Expected: compile error — `KeyboardSelectionSync` does not exist.

- [ ] **Step 3: Implement the seam**

```csharp
// Assets/Scripts/TextSelection/KeyboardSelectionSync.cs
using System.Reflection;
using TMPro;

/// Pushes the field's current Unity-side selection into the hidden native
/// TouchScreenKeyboard buffer. TMP only does this on its own pointer paths
/// (2 call sites in this uGUI version), so every PROGRAMMATIC selection
/// change must route through here — otherwise the next keystroke on iOS
/// edits at the native buffer's stale caret instead of replacing the
/// selection. The invoked method carries TMP's own platform/null/
/// canSetSelection guards, so calling it is safe in the Editor and when no
/// keyboard is open.
public static class KeyboardSelectionSync
{
    static readonly MethodInfo PushMethod = typeof(TMP_InputField).GetMethod(
        "UpdateKeyboardStringPosition", BindingFlags.Instance | BindingFlags.NonPublic);

    internal static System.Action<TMP_InputField> PushOverrideForTests;

    public static bool TargetExists => PushMethod != null;

    public static void Push(TMP_InputField field)
    {
        if (field == null) return;
        if (PushOverrideForTests != null) { PushOverrideForTests(field); return; }
        PushMethod?.Invoke(field, null);
    }
}
```

- [ ] **Step 4: Run to verify both tests pass**

Run: `Tools/run-tests-headless.sh "KeyboardSelectionSyncPinTests"`
Expected: `2 passed, 0 failed`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/TextSelection Assets/Tests/Editor/Chat/KeyboardSelectionSyncPinTests.cs Assets/Tests/Editor/Chat/KeyboardSelectionSyncPinTests.cs.meta
git commit -m "feat(textselect): KeyboardSelectionSync seam + reflection pin

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

(`git add Assets/Scripts/TextSelection` picks up the new folder's `.meta` files too.)

---

### Task 2: Device spike — GO/NO-GO gate (OWNER CHECKPOINT)

**Files:**
- Create: `Assets/Editor/TextSelectionSpikeBuilder.cs`
- Create: `Assets/Scripts/TextSelection/TextSelectionSpikeProbe.cs`
- Output (builder-generated, committed): `Assets/Scenes/SpikeTextSelection.unity`

**Interfaces:**
- Consumes: `KeyboardSelectionSync.Push` (Task 1).
- Produces: a device-runnable scene proving the spec's checks (a)–(d); spike verdict recorded at the bottom of the spec file. **Task 9 is blocked until this verdict is GO.**

- [ ] **Step 1: Write the probe**

Emoji may render as boxes in the spike (default font, no project sprite atlas) — irrelevant: check (d) is about **string indices**, not glyphs.

```csharp
// Assets/Scripts/TextSelection/TextSelectionSpikeProbe.cs
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;

/// Throwaway on-device probe for the 4 GO/NO-GO checks in the 2026-08-07
/// text-selection spec. Drive it with the OnGUI buttons; results accumulate
/// on screen. Delete together with the spike scene once the verdict lands.
public class TextSelectionSpikeProbe : MonoBehaviour
{
    public TMP_InputField plainField;   // "alpha beta gamma"
    public TMP_InputField emojiField;   // "hi 😂👍 end"

    static readonly FieldInfo KbField = typeof(TMP_InputField).GetField(
        "m_SoftKeyboard", BindingFlags.Instance | BindingFlags.NonPublic);

    readonly List<string> _log = new List<string>();
    string _expectAfterTyping;
    TMP_InputField _watched;

    TouchScreenKeyboard Kb(TMP_InputField f) => KbField?.GetValue(f) as TouchScreenKeyboard;

    void Log(string s) { _log.Add(s); Debug.Log("[spike] " + s); }

    void OnGUI()
    {
        GUI.matrix = Matrix4x4.Scale(Vector3.one * (Screen.width / 400f));
        GUILayout.BeginArea(new Rect(5, 120, 390, 560));

        if (GUILayout.Button("A: keyboard + canSetSelection", GUILayout.Height(34)))
        {
            var kb = Kb(plainField);
            Log(kb == null
                ? "A: FAIL — no TouchScreenKeyboard (focus the top field first)"
                : $"A: {(kb.canSetSelection ? "PASS" : "FAIL")} — canSetSelection={kb.canSetSelection}, canGetSelection={kb.canGetSelection}");
        }

        if (GUILayout.Button("B: select 'beta' + sync (then type X)", GUILayout.Height(34)))
        {
            plainField.text = "alpha beta gamma";
            int s = plainField.text.IndexOf("beta");
            plainField.selectionStringAnchorPosition = s;
            plainField.selectionStringFocusPosition = s + 4;
            KeyboardSelectionSync.Push(plainField);
            _watched = plainField;
            _expectAfterTyping = "alpha X gamma";
            Log("B: armed — now type a capital X on the keyboard");
        }

        if (GUILayout.Button("C: paste-sim 'ZZ' at 6 (then type Y)", GUILayout.Height(34)))
        {
            plainField.text = "alpha beta gamma";
            var edit = new SelectionEditProbe("alpha beta gamma", 6, 10, "ZZ"); // replaces "beta"
            plainField.text = edit.NewText;
            plainField.stringPosition = edit.NewCaret;
            KeyboardSelectionSync.Push(plainField);
            _watched = plainField;
            _expectAfterTyping = "alpha ZZY gamma";
            Log("C: armed — now type a capital Y");
        }

        if (GUILayout.Button("D: emoji indices", GUILayout.Height(34)))
        {
            string t = emojiField.text; // "hi 😂👍 end"
            int i = t.IndexOf(" end");
            emojiField.selectionStringAnchorPosition = 3;   // start of emoji run
            emojiField.selectionStringFocusPosition = i;    // end of emoji run
            KeyboardSelectionSync.Push(emojiField);
            string copied = t.Substring(3, i - 3);
            Log($"D: {(copied == "😂👍" ? "PASS" : "FAIL")} — substring='{copied}' len={copied.Length}");
        }

        if (GUILayout.Button("Bonus: log selection each frame (spacebar-trackpad)", GUILayout.Height(34)))
            InvokeRepeating(nameof(LogSel), 0f, 0.5f);

        foreach (var line in _log) GUILayout.Label(line);
        GUILayout.EndArea();
    }

    void LogSel()
    {
        if (plainField != null && plainField.isFocused)
            Log($"sel now: caret={plainField.stringPosition} anchor={plainField.selectionStringAnchorPosition}");
    }

    void Update()
    {
        if (_watched == null || _expectAfterTyping == null) return;
        if (_watched.text == _expectAfterTyping)
        {
            Log("PASS — typed char replaced the synced selection");
            _watched = null; _expectAfterTyping = null;
        }
        else if (_watched.text.Length > "alpha beta gamma".Length && _watched.text.Contains("beta") &&
                 (_watched.text.EndsWith("X") || _watched.text.EndsWith("Y")))
        {
            Log($"FAIL — char appended at stale caret: '{_watched.text}'");
            _watched = null; _expectAfterTyping = null;
        }
    }

    /// Local copy of the Paste math so the spike does not depend on Task 4.
    readonly struct SelectionEditProbe
    {
        public readonly string NewText; public readonly int NewCaret;
        public SelectionEditProbe(string text, int start, int end, string clip)
        { NewText = text.Remove(start, end - start).Insert(start, clip); NewCaret = start + clip.Length; }
    }
}
```

- [ ] **Step 2: Write the spike-scene builder**

```csharp
// Assets/Editor/TextSelectionSpikeBuilder.cs
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// Builds the throwaway spike scene for the 2026-08-07 text-selection spec.
/// Edit-Mode only. Saves Assets/Scenes/SpikeTextSelection.unity. Never
/// touches Main.unity.
public static class TextSelectionSpikeBuilder
{
    [MenuItem("Tools/Text Selection/Build Spike Scene")]
    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;

        var plain = BuildField(canvasGo.transform, "PlainField", new Vector2(0, 500), "alpha beta gamma");
        var emoji = BuildField(canvasGo.transform, "EmojiField", new Vector2(0, 280), "hi \U0001F602\U0001F44D end");

        var probeGo = new GameObject("SpikeProbe", typeof(TextSelectionSpikeProbe));
        var probe = probeGo.GetComponent<TextSelectionSpikeProbe>();
        probe.plainField = plain;
        probe.emojiField = emoji;

        EditorSceneManager.SaveScene(scene, "Assets/Scenes/SpikeTextSelection.unity");
        Debug.Log("[TextSelectionSpikeBuilder] Spike scene saved.");
    }

    static TMP_InputField BuildField(Transform parent, string name, Vector2 pos, string text)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(960, 140);
        rt.anchoredPosition = pos;
        go.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.18f, 1f);

        var area = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
        area.transform.SetParent(go.transform, false);
        var art = (RectTransform)area.transform;
        art.anchorMin = Vector2.zero; art.anchorMax = Vector2.one;
        art.offsetMin = new Vector2(24, 12); art.offsetMax = new Vector2(-24, -12);

        var label = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        label.transform.SetParent(area.transform, false);
        var lrt = (RectTransform)label.transform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
        var tmp = label.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = 44;
        tmp.color = Color.white;

        var field = go.GetComponent<TMP_InputField>();
        field.textViewport = art;
        field.textComponent = tmp;
        field.lineType = TMP_InputField.LineType.MultiLineSubmit;
        field.shouldHideMobileInput = true;   // matches every field in the app
        field.text = text;
        return field;
    }
}
```

- [ ] **Step 3: Import + compile gate**

Editor closed: `Tools/run-tests-headless.sh "KeyboardSelectionSyncPinTests"` (compiles everything; expect `2 passed`). Editor open: `Assets/Refresh` via mcp-unity, verify both new `.meta` files exist, then run the builder menu item `Tools/Text Selection/Build Spike Scene` and confirm `Assets/Scenes/SpikeTextSelection.unity` exists.

- [ ] **Step 4: Commit**

```bash
git add Assets/Editor/TextSelectionSpikeBuilder.cs Assets/Editor/TextSelectionSpikeBuilder.cs.meta Assets/Scripts/TextSelection/TextSelectionSpikeProbe.cs Assets/Scripts/TextSelection/TextSelectionSpikeProbe.cs.meta Assets/Scenes/SpikeTextSelection.unity Assets/Scenes/SpikeTextSelection.unity.meta
git commit -m "feat(textselect): device spike scene + probe for keyboard-sync GO/NO-GO

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

- [ ] **Step 5: OWNER GATE — run on iPhone (and one Android device if handy)**

Owner instructions: open `SpikeTextSelection` scene → File ▸ Build Settings → add it as scene 0 (temporarily) → Build & Run on the iPhone. Then, per button:

| Check | Do | PASS looks like |
|---|---|---|
| A | Tap the top field (keyboard opens), tap **A** | `A: PASS — canSetSelection=True` |
| B | Tap **B**, then type `X` | `PASS — typed char replaced the synced selection` |
| C | Tap **C**, then type `Y` | same PASS line; text reads `alpha ZZY gamma` |
| D | Tap **D** | `D: PASS — substring='😂👍' len=4` |
| Bonus | Tap **Bonus**, hold spacebar, slide finger | `sel now: caret=…` values move |

Record the verdict (GO / NO-GO + which checks failed) at the bottom of `docs/superpowers/specs/2026-08-07-input-text-selection-design.md` under a new `## Spike verdict` heading, and commit. **If A or B failed:** stop; the fallback (`onValueChanged` diff-correction, see spec) must be designed before Task 9.

---

### Task 3: WordBoundary (pure) — TDD

**Files:**
- Create: `Assets/Scripts/TextSelection/WordBoundary.cs`
- Test: `Assets/Tests/Editor/Chat/WordBoundaryTests.cs`

**Interfaces:**
- Consumes: nothing (pure).
- Produces: `WordBoundary.WordRangeAt(string text, int index) : (int start, int end)` — `(i,i)` means caret placement; `WordBoundary.ClampToCharBoundary(string text, int index) : int`.

- [ ] **Step 1: Write the failing tests**

```csharp
// Assets/Tests/Editor/Chat/WordBoundaryTests.cs
using NUnit.Framework;

public class WordBoundaryTests
{
    static (int, int) R(string t, int i) => WordBoundary.WordRangeAt(t, i);

    [Test] public void Latin_word_selected_from_middle() =>
        Assert.AreEqual((6, 10), R("alpha beta gamma", 8));   // "beta"

    [Test] public void Cyrillic_word_selected() =>
        Assert.AreEqual((0, 6), R("Привет мир", 2));

    [Test] public void Digits_and_underscore_are_word_chars() =>
        Assert.AreEqual((0, 8), R("abc_1234 x", 4));

    [Test] public void Whitespace_returns_caret_placement() =>
        Assert.AreEqual((5, 5), R("alpha beta", 5));

    [Test] public void Punctuation_selects_the_punctuation_run() =>
        Assert.AreEqual((3, 5), R("ab !? cd", 3));

    [Test] public void Apostrophe_stays_inside_word() =>
        Assert.AreEqual((0, 5), R("don't stop", 2));

    [Test] public void Surrogate_pair_never_split()
    {
        var (s, e) = R("hi \U0001F602 yo", 3); // 😂 occupies string indices 3..5
        Assert.AreEqual((3, 5), (s, e));
    }

    [Test] public void Adjacent_emoji_select_as_one_run_v1()
    {
        // Documented v1 behavior: a run of emoji/ZWJ/FE0F selects together.
        var (s, e) = R("x \U0001F602\U0001F44D y", 2);
        Assert.AreEqual((2, 6), (s, e));
    }

    [Test] public void Index_at_text_end_selects_last_word() =>
        Assert.AreEqual((6, 10), R("alpha beta", 10));

    [Test] public void Empty_text_returns_zero_caret() =>
        Assert.AreEqual((0, 0), R("", 0));

    [Test] public void Clamp_moves_off_low_surrogate() =>
        Assert.AreEqual(3, WordBoundary.ClampToCharBoundary("hi \U0001F602", 4));

    [Test] public void Clamp_bounds_negative_and_overflow()
    {
        Assert.AreEqual(0, WordBoundary.ClampToCharBoundary("abc", -5));
        Assert.AreEqual(3, WordBoundary.ClampToCharBoundary("abc", 99));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `Tools/run-tests-headless.sh "WordBoundaryTests"`
Expected: compile error — `WordBoundary` does not exist.

- [ ] **Step 3: Implement**

```csharp
// Assets/Scripts/TextSelection/WordBoundary.cs
/// Pure string math for iOS-style word selection. All indices are STRING
/// indices (UTF-16 code units). Guarantee: no returned boundary ever splits
/// a surrogate pair. v1 emoji rule: a maximal run of {surrogates, ZWJ,
/// FE0F} is one cluster (adjacent emoji select together — pinned by test).
public static class WordBoundary
{
    public static int ClampToCharBoundary(string text, int index)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        if (index < 0) index = 0;
        if (index > text.Length) index = text.Length;
        if (index > 0 && index < text.Length && char.IsLowSurrogate(text[index]))
            index--;
        return index;
    }

    public static (int start, int end) WordRangeAt(string text, int index)
    {
        if (string.IsNullOrEmpty(text)) return (0, 0);
        index = ClampToCharBoundary(text, index);
        if (index >= text.Length) index = text.Length - 1;
        if (char.IsLowSurrogate(text[index]) && index > 0) index--;

        char c = text[index];
        if (IsEmojiPart(c)) return RunAt(text, index, IsEmojiPart);
        if (char.IsWhiteSpace(c)) return (index, index);
        if (IsWordChar(c)) return RunAt(text, index, IsWordChar);
        return RunAt(text, index, IsPunct);
    }

    static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '\'';
    static bool IsEmojiPart(char c) => char.IsSurrogate(c) || c == '\u200D' || c == '\uFE0F'; // ZWJ, variation selector
    static bool IsPunct(char c) => !IsWordChar(c) && !char.IsWhiteSpace(c) && !IsEmojiPart(c);

    static (int, int) RunAt(string text, int index, System.Func<char, bool> inRun)
    {
        int start = index;
        while (start > 0 && inRun(text[start - 1])) start--;
        int end = index;
        while (end < text.Length && inRun(text[end])) end++;
        return (start, end);
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `Tools/run-tests-headless.sh "WordBoundaryTests"`
Expected: `12 passed, 0 failed`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/TextSelection/WordBoundary.cs Assets/Scripts/TextSelection/WordBoundary.cs.meta Assets/Tests/Editor/Chat/WordBoundaryTests.cs Assets/Tests/Editor/Chat/WordBoundaryTests.cs.meta
git commit -m "feat(textselect): WordBoundary word/emoji-cluster math

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: SelectionActions (pure) — TDD

**Files:**
- Create: `Assets/Scripts/TextSelection/SelectionActions.cs`
- Test: `Assets/Tests/Editor/Chat/SelectionActionsTests.cs`

**Interfaces:**
- Consumes: `WordBoundary.ClampToCharBoundary` (Task 3).
- Produces:
  - `readonly struct SelectionEdit { string NewText; int NewCaret; }`
  - `SelectionActions.CopyText(string text, int anchor, int focus) : string`
  - `SelectionActions.Cut(string text, int anchor, int focus) : SelectionEdit`
  - `SelectionActions.Paste(string text, int anchor, int focus, string clip, int characterLimit) : SelectionEdit` — `characterLimit <= 0` means unlimited (TMP convention); collapsed selection inserts at the caret.

- [ ] **Step 1: Write the failing tests**

```csharp
// Assets/Tests/Editor/Chat/SelectionActionsTests.cs
using NUnit.Framework;

public class SelectionActionsTests
{
    [Test] public void Copy_returns_selected_substring_regardless_of_direction()
    {
        Assert.AreEqual("beta", SelectionActions.CopyText("alpha beta gamma", 6, 10));
        Assert.AreEqual("beta", SelectionActions.CopyText("alpha beta gamma", 10, 6));
    }

    [Test] public void Cut_removes_selection_and_places_caret_at_start()
    {
        var e = SelectionActions.Cut("alpha beta gamma", 6, 10);
        Assert.AreEqual("alpha  gamma", e.NewText);
        Assert.AreEqual(6, e.NewCaret);
    }

    [Test] public void Paste_replaces_selection()
    {
        var e = SelectionActions.Paste("alpha beta gamma", 6, 10, "ZZ", 0);
        Assert.AreEqual("alpha ZZ gamma", e.NewText);
        Assert.AreEqual(8, e.NewCaret);
    }

    [Test] public void Paste_with_collapsed_selection_inserts_at_caret()
    {
        var e = SelectionActions.Paste("ab", 1, 1, "XY", 0);
        Assert.AreEqual("aXYb", e.NewText);
        Assert.AreEqual(3, e.NewCaret);
    }

    [Test] public void Paste_respects_character_limit_by_truncating_clip()
    {
        var e = SelectionActions.Paste("12345", 5, 5, "abcdef", 8);
        Assert.AreEqual("12345abc", e.NewText);
        Assert.AreEqual(8, e.NewCaret);
    }

    [Test] public void Paste_truncation_never_splits_a_surrogate_pair()
    {
        var e = SelectionActions.Paste("", 0, 0, "a\U0001F602", 2); // room for 2 units; 😂 needs both at index 1..3
        Assert.AreEqual("a", e.NewText);
    }

    [Test] public void Paste_null_clipboard_is_empty()
    {
        var e = SelectionActions.Paste("ab", 0, 1, null, 0);
        Assert.AreEqual("b", e.NewText);
        Assert.AreEqual(0, e.NewCaret);
    }

    [Test] public void Indices_are_clamped_into_range()
    {
        var e = SelectionActions.Cut("abc", -4, 99);
        Assert.AreEqual("", e.NewText);
        Assert.AreEqual(0, e.NewCaret);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `Tools/run-tests-headless.sh "SelectionActionsTests"`
Expected: compile error — `SelectionActions` does not exist.

- [ ] **Step 3: Implement**

```csharp
// Assets/Scripts/TextSelection/SelectionActions.cs
/// Pure cut/copy/paste string math. Inputs are STRING indices in either
/// order (anchor/focus); outputs are the full new text + caret so the
/// caller can apply them through the focused field's own .text write-through
/// path and then KeyboardSelectionSync.Push.
public readonly struct SelectionEdit
{
    public readonly string NewText;
    public readonly int NewCaret;
    public SelectionEdit(string newText, int newCaret) { NewText = newText; NewCaret = newCaret; }
}

public static class SelectionActions
{
    public static string CopyText(string text, int anchor, int focus)
    {
        var (s, e) = Normalize(text, anchor, focus);
        return text.Substring(s, e - s);
    }

    public static SelectionEdit Cut(string text, int anchor, int focus)
    {
        var (s, e) = Normalize(text, anchor, focus);
        return new SelectionEdit(text.Remove(s, e - s), s);
    }

    public static SelectionEdit Paste(string text, int anchor, int focus, string clip, int characterLimit)
    {
        var (s, e) = Normalize(text, anchor, focus);
        clip = clip ?? "";
        string removed = text.Remove(s, e - s);
        if (characterLimit > 0)
        {
            int room = characterLimit - removed.Length;
            if (room <= 0) clip = "";
            else if (clip.Length > room)
                clip = clip.Substring(0, WordBoundary.ClampToCharBoundary(clip, room));
        }
        return new SelectionEdit(removed.Insert(s, clip), s + clip.Length);
    }

    static (int start, int end) Normalize(string text, int anchor, int focus)
    {
        text = text ?? "";
        int a = WordBoundary.ClampToCharBoundary(text, anchor);
        int f = WordBoundary.ClampToCharBoundary(text, focus);
        return a <= f ? (a, f) : (f, a);
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `Tools/run-tests-headless.sh "SelectionActionsTests|WordBoundaryTests"`
Expected: `20 passed, 0 failed`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/TextSelection/SelectionActions.cs Assets/Scripts/TextSelection/SelectionActions.cs.meta Assets/Tests/Editor/Chat/SelectionActionsTests.cs Assets/Tests/Editor/Chat/SelectionActionsTests.cs.meta
git commit -m "feat(textselect): SelectionActions cut/copy/paste math

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: SelectionMenuPolicy (pure) — TDD

**Files:**
- Create: `Assets/Scripts/TextSelection/SelectionMenuPolicy.cs`
- Test: `Assets/Tests/Editor/Chat/SelectionMenuPolicyTests.cs`

**Interfaces:**
- Consumes: nothing (pure).
- Produces: `[Flags] enum SelectionMenuItems { None=0, Cut=1, Copy=2, Paste=4, SelectAll=8 }`; `SelectionMenuPolicy.Visible(bool hasSelection, bool clipboardHasText, int textLength, bool allSelected, bool readOnly) : SelectionMenuItems`.

- [ ] **Step 1: Write the failing tests**

```csharp
// Assets/Tests/Editor/Chat/SelectionMenuPolicyTests.cs
using NUnit.Framework;

public class SelectionMenuPolicyTests
{
    [Test] public void Selection_with_clipboard_shows_everything_when_not_all_selected() =>
        Assert.AreEqual(
            SelectionMenuItems.Cut | SelectionMenuItems.Copy | SelectionMenuItems.Paste | SelectionMenuItems.SelectAll,
            SelectionMenuPolicy.Visible(hasSelection: true, clipboardHasText: true, textLength: 10, allSelected: false, readOnly: false));

    [Test] public void All_selected_hides_select_all() =>
        Assert.IsFalse(SelectionMenuPolicy.Visible(true, true, 10, allSelected: true, readOnly: false)
            .HasFlag(SelectionMenuItems.SelectAll));

    [Test] public void Caret_only_with_clipboard_shows_paste_and_select_all() =>
        Assert.AreEqual(SelectionMenuItems.Paste | SelectionMenuItems.SelectAll,
            SelectionMenuPolicy.Visible(false, true, 10, false, false));

    [Test] public void Caret_only_empty_clipboard_empty_text_shows_nothing() =>
        Assert.AreEqual(SelectionMenuItems.None,
            SelectionMenuPolicy.Visible(false, false, 0, false, false));

    [Test] public void ReadOnly_hides_cut_and_paste_keeps_copy() =>
        Assert.AreEqual(SelectionMenuItems.Copy | SelectionMenuItems.SelectAll,
            SelectionMenuPolicy.Visible(true, true, 10, false, readOnly: true));
}
```

- [ ] **Step 2: Run to verify failure**

Run: `Tools/run-tests-headless.sh "SelectionMenuPolicyTests"`
Expected: compile error.

- [ ] **Step 3: Implement**

```csharp
// Assets/Scripts/TextSelection/SelectionMenuPolicy.cs
/// iOS-parity visibility rules for the floating edit menu.
[System.Flags]
public enum SelectionMenuItems
{
    None = 0,
    Cut = 1,
    Copy = 2,
    Paste = 4,
    SelectAll = 8,
}

public static class SelectionMenuPolicy
{
    public static SelectionMenuItems Visible(
        bool hasSelection, bool clipboardHasText, int textLength, bool allSelected, bool readOnly)
    {
        var items = SelectionMenuItems.None;
        if (hasSelection && !readOnly) items |= SelectionMenuItems.Cut;
        if (hasSelection) items |= SelectionMenuItems.Copy;
        if (clipboardHasText && !readOnly) items |= SelectionMenuItems.Paste;
        if (textLength > 0 && !allSelected) items |= SelectionMenuItems.SelectAll;
        return items;
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `Tools/run-tests-headless.sh "SelectionMenuPolicyTests"`
Expected: `5 passed, 0 failed`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/TextSelection/SelectionMenuPolicy.cs Assets/Scripts/TextSelection/SelectionMenuPolicy.cs.meta Assets/Tests/Editor/Chat/SelectionMenuPolicyTests.cs Assets/Tests/Editor/Chat/SelectionMenuPolicyTests.cs.meta
git commit -m "feat(textselect): SelectionMenuPolicy visibility matrix

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: SelectionGestureMachine (pure) — TDD

**Files:**
- Create: `Assets/Scripts/TextSelection/SelectionGestureMachine.cs`
- Test: `Assets/Tests/Editor/Chat/SelectionGestureMachineTests.cs`

**Interfaces:**
- Consumes: `UnityEngine.Vector2` only.
- Produces: `SelectionGestureMachine(float longPressSeconds = 0.45f, float doubleTapSeconds = 0.3f, float slopPixels = 30f)` with `Press(Vector2, float now)`, `Move(Vector2, float now)`, `Tick(float now)`, `Release(Vector2, float now)` — each returning `SelectionGestureMachine.Result { None, Tap, DoubleTap, LongPress, Cancel }` — plus `IsPressed : bool`, `LongPressActive : bool` (true from LongPress/DoubleTap until Release; the router uses it to route finger drags into selection-extension).

- [ ] **Step 1: Write the failing tests**

```csharp
// Assets/Tests/Editor/Chat/SelectionGestureMachineTests.cs
using NUnit.Framework;
using UnityEngine;

public class SelectionGestureMachineTests
{
    SelectionGestureMachine M() => new SelectionGestureMachine(0.45f, 0.3f, 30f);
    static readonly Vector2 P = new Vector2(100, 100);

    [Test] public void Quick_release_is_a_tap()
    {
        var m = M();
        m.Press(P, 0f);
        Assert.AreEqual(SelectionGestureMachine.Result.Tap, m.Release(P, 0.1f));
    }

    [Test] public void Second_tap_within_window_and_slop_is_double_tap()
    {
        var m = M();
        m.Press(P, 0f); m.Release(P, 0.1f);
        Assert.AreEqual(SelectionGestureMachine.Result.DoubleTap, m.Press(P + new Vector2(5, 5), 0.3f));
    }

    [Test] public void Second_tap_after_window_is_not_double_tap()
    {
        var m = M();
        m.Press(P, 0f); m.Release(P, 0.1f);
        Assert.AreEqual(SelectionGestureMachine.Result.None, m.Press(P, 0.6f));
    }

    [Test] public void Long_press_fires_at_threshold_while_within_slop()
    {
        var m = M();
        m.Press(P, 0f);
        Assert.AreEqual(SelectionGestureMachine.Result.None, m.Tick(0.44f));
        Assert.AreEqual(SelectionGestureMachine.Result.LongPress, m.Tick(0.46f));
        Assert.IsTrue(m.LongPressActive);
    }

    [Test] public void Move_past_slop_before_timer_cancels()
    {
        var m = M();
        m.Press(P, 0f);
        Assert.AreEqual(SelectionGestureMachine.Result.Cancel, m.Move(P + new Vector2(40, 0), 0.2f));
        Assert.AreEqual(SelectionGestureMachine.Result.None, m.Tick(1f));
        Assert.AreEqual(SelectionGestureMachine.Result.None, m.Release(P, 1.1f));
    }

    [Test] public void Move_after_long_press_does_not_cancel()
    {
        var m = M();
        m.Press(P, 0f);
        m.Tick(0.5f);
        Assert.AreEqual(SelectionGestureMachine.Result.None, m.Move(P + new Vector2(200, 0), 0.6f));
        Assert.IsTrue(m.LongPressActive);
    }

    [Test] public void Long_press_release_is_not_a_tap()
    {
        var m = M();
        m.Press(P, 0f);
        m.Tick(0.5f);
        Assert.AreEqual(SelectionGestureMachine.Result.None, m.Release(P, 0.6f));
        Assert.IsFalse(m.LongPressActive);
    }

    [Test] public void Double_tap_press_suppresses_long_press_timer()
    {
        var m = M();
        m.Press(P, 0f); m.Release(P, 0.1f);
        m.Press(P, 0.2f); // DoubleTap
        Assert.AreEqual(SelectionGestureMachine.Result.None, m.Tick(2f));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `Tools/run-tests-headless.sh "SelectionGestureMachineTests"`
Expected: compile error.

- [ ] **Step 3: Implement**

```csharp
// Assets/Scripts/TextSelection/SelectionGestureMachine.cs
using UnityEngine;

/// Pure tap / double-tap / long-press / slop-cancel state machine. No Unity
/// lifecycle: the router feeds pointer events and Tick with explicit
/// timestamps, so EditMode tests drive time directly. After LongPress or
/// DoubleTap, LongPressActive stays true until Release — the router routes
/// finger movement into selection extension during that window.
public sealed class SelectionGestureMachine
{
    public enum Result { None, Tap, DoubleTap, LongPress, Cancel }

    readonly float _longPressSeconds;
    readonly float _doubleTapSeconds;
    readonly float _slopSqr;

    bool _pressed;
    bool _cancelled;
    bool _committed;          // LongPress or DoubleTap already fired for this press
    Vector2 _pressPos;
    float _pressTime;
    Vector2 _lastTapPos;
    float _lastTapTime = float.NegativeInfinity;

    public SelectionGestureMachine(float longPressSeconds = 0.45f, float doubleTapSeconds = 0.3f, float slopPixels = 30f)
    {
        _longPressSeconds = longPressSeconds;
        _doubleTapSeconds = doubleTapSeconds;
        _slopSqr = slopPixels * slopPixels;
    }

    public bool IsPressed => _pressed;
    public bool LongPressActive => _pressed && _committed;

    public Result Press(Vector2 pos, float now)
    {
        _pressed = true;
        _cancelled = false;
        _committed = false;
        _pressPos = pos;
        _pressTime = now;

        if (now - _lastTapTime <= _doubleTapSeconds && (pos - _lastTapPos).sqrMagnitude <= _slopSqr)
        {
            _lastTapTime = float.NegativeInfinity;
            _committed = true;
            return Result.DoubleTap;
        }
        return Result.None;
    }

    public Result Move(Vector2 pos, float now)
    {
        if (!_pressed || _cancelled || _committed) return Result.None;
        if ((pos - _pressPos).sqrMagnitude > _slopSqr)
        {
            _cancelled = true;
            return Result.Cancel;
        }
        return Result.None;
    }

    public Result Tick(float now)
    {
        if (!_pressed || _cancelled || _committed) return Result.None;
        if (now - _pressTime >= _longPressSeconds)
        {
            _committed = true;
            return Result.LongPress;
        }
        return Result.None;
    }

    public Result Release(Vector2 pos, float now)
    {
        bool clean = _pressed && !_cancelled && !_committed;
        _pressed = false;
        if (!clean) return Result.None;
        if (now - _pressTime <= _doubleTapSeconds)
        {
            _lastTapPos = _pressPos;
            _lastTapTime = now;
            return Result.Tap;
        }
        return Result.Tap; // slow but in-slop release still places the caret like a tap
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `Tools/run-tests-headless.sh "SelectionGestureMachineTests"`
Expected: `8 passed, 0 failed`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/TextSelection/SelectionGestureMachine.cs Assets/Scripts/TextSelection/SelectionGestureMachine.cs.meta Assets/Tests/Editor/Chat/SelectionGestureMachineTests.cs Assets/Tests/Editor/Chat/SelectionGestureMachineTests.cs.meta
git commit -m "feat(textselect): gesture state machine (tap/double-tap/long-press/slop)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 7: SelectionOverlay + SelectionHandleView (runtime views)

**Files:**
- Create: `Assets/Scripts/TextSelection/SelectionOverlay.cs`
- Create: `Assets/Scripts/TextSelection/SelectionHandleView.cs`

**Interfaces:**
- Consumes: `Theme.Color(ThemeRole …)` + `Theme.Changed` (project Theme facade), `Nobi.UiRoundedCorners.ImageWithRoundedCorners`.
- Produces:
  - `SelectionOverlay.Create() : SelectionOverlay` — builds `ScreenSpaceOverlay` canvas; `StartHandle`/`EndHandle : SelectionHandleView`; `MenuRoot : RectTransform` (Task 8 parents the menu here); `ShowHandles()`, `HideHandles()`, `HideAll()`, `PositionHandle(SelectionHandleView h, Vector3 worldTop, Vector3 worldBottom, bool stemUp)`; `bool HandlesVisible`.
  - `SelectionHandleView` — `IsStart : bool`; events `DragMoved : Action<SelectionHandleView, Vector2 screenPos>`, `DragEnded : Action<SelectionHandleView>`; `static Build(Transform parent, bool isStart) : SelectionHandleView`.

- [ ] **Step 1: Discover the exact ThemeRole members and overlay sorting order**

Run:
```bash
grep -n "enum ThemeRole" -A 40 Assets/Scripts/Theme/ThemeRole.cs
grep -n "Changed" Assets/Scripts/Theme/Theme.cs
grep -n "m_SortingOrder" Assets/Scenes/Main.unity | sort | uniq -c
grep -rn "sortingOrder" Assets/Scripts Assets/Editor --include="*.cs" | grep -i -e loading -e success -e overlay | head
```
Record: (1) the accent / elevated-surface / primary-ink role members (called `Accent`, `SurfaceElevated`, `InkPrimary` below — substitute the real names in Tasks 7–9); (2) the `Theme.Changed` delegate signature — Task 9's `OnThemeChanged` handler must match it; (3) pick `SORTING_ORDER` strictly **below** LoadingPanel's canvas and above ScreenContainer's (memory: ScreenContainer < LoadingPanel < SuccessOverlay). If LoadingPanel has no explicit canvas order, use `50` and verify visually in the Editor smoke (Task 10).

- [ ] **Step 2: Write the handle view**

```csharp
// Assets/Scripts/TextSelection/SelectionHandleView.cs
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Nobi.UiRoundedCorners;

/// One iOS-style selection pin: invisible 132-unit hit area, visible
/// 6-unit stem + 48-unit circular head. Start pin renders the head above
/// the line (stem up), end pin below. Pure view: reports drags, owns no
/// selection logic.
public class SelectionHandleView : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public const float HitSize = 132f;
    public const float HeadSize = 48f;
    public const float StemWidth = 6f;

    public bool IsStart { get; private set; }
    public System.Action<SelectionHandleView, Vector2> DragMoved;
    public System.Action<SelectionHandleView> DragEnded;

    Image _stem;
    Image _head;

    public static SelectionHandleView Build(Transform parent, bool isStart)
    {
        var go = new GameObject(isStart ? "HandleStart" : "HandleEnd",
            typeof(RectTransform), typeof(Image), typeof(SelectionHandleView));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(HitSize, HitSize);

        var hit = go.GetComponent<Image>();
        hit.color = Color.clear;           // raycast target, invisible
        hit.raycastTarget = true;

        var view = go.GetComponent<SelectionHandleView>();
        view.IsStart = isStart;

        view._stem = NewChildImage(go.transform, "Stem", new Vector2(StemWidth, 64f));
        view._head = NewChildImage(go.transform, "Head", new Vector2(HeadSize, HeadSize));
        view._head.gameObject.AddComponent<ImageWithRoundedCorners>().radius = HeadSize / 2f;

        float dir = isStart ? 1f : -1f;   // start: head above the line
        ((RectTransform)view._head.transform).anchoredPosition = new Vector2(0, dir * (32f + HeadSize / 2f));
        ((RectTransform)view._stem.transform).anchoredPosition = new Vector2(0, dir * 0f);

        go.SetActive(false);
        return view;
    }

    static Image NewChildImage(Transform parent, string name, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        ((RectTransform)go.transform).sizeDelta = size;
        var img = go.GetComponent<Image>();
        img.sprite = null;                 // project convention: null sprite + RoundedCorners
        img.raycastTarget = false;
        return img;
    }

    public void SetColor(Color c) { _stem.color = c; _head.color = c; }

    public void SetStemHeight(float h) =>
        ((RectTransform)_stem.transform).sizeDelta = new Vector2(StemWidth, Mathf.Max(24f, h));

    public void OnBeginDrag(PointerEventData e) => DragMoved?.Invoke(this, e.position);
    public void OnDrag(PointerEventData e) => DragMoved?.Invoke(this, e.position);
    public void OnEndDrag(PointerEventData e) => DragEnded?.Invoke(this);
}
```

- [ ] **Step 3: Write the overlay**

```csharp
// Assets/Scripts/TextSelection/SelectionOverlay.cs
using UnityEngine;
using UnityEngine.UI;

/// Runtime-created ScreenSpaceOverlay canvas hosting the two selection pins
/// and the edit menu. Sorting sits above the screen content and below
/// LoadingPanel (verify SORTING_ORDER against the scene, see plan Task 7
/// step 1). Created lazily by TextSelectionRouter; survives for the app's
/// lifetime.
public class SelectionOverlay : MonoBehaviour
{
    public const int SORTING_ORDER = 50; // adjust from Task 7 step 1 findings

    public SelectionHandleView StartHandle { get; private set; }
    public SelectionHandleView EndHandle { get; private set; }
    public RectTransform MenuRoot { get; private set; }
    public Canvas Canvas { get; private set; }

    public bool HandlesVisible => StartHandle != null && StartHandle.gameObject.activeSelf;

    public static SelectionOverlay Create()
    {
        var go = new GameObject("TextSelectionOverlay",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(SelectionOverlay));
        DontDestroyOnLoad(go);

        var overlay = go.GetComponent<SelectionOverlay>();
        overlay.Canvas = go.GetComponent<Canvas>();
        overlay.Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        overlay.Canvas.sortingOrder = SORTING_ORDER;

        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;

        overlay.StartHandle = SelectionHandleView.Build(go.transform, isStart: true);
        overlay.EndHandle = SelectionHandleView.Build(go.transform, isStart: false);

        var menuRoot = new GameObject("MenuRoot", typeof(RectTransform));
        menuRoot.transform.SetParent(go.transform, false);
        overlay.MenuRoot = (RectTransform)menuRoot.transform;
        var mrt = overlay.MenuRoot;
        mrt.anchorMin = Vector2.zero; mrt.anchorMax = Vector2.one;
        mrt.offsetMin = Vector2.zero; mrt.offsetMax = Vector2.zero;

        return overlay;
    }

    public void ShowHandles()
    {
        StartHandle.gameObject.SetActive(true);
        EndHandle.gameObject.SetActive(true);
    }

    public void HideHandles()
    {
        StartHandle.gameObject.SetActive(false);
        EndHandle.gameObject.SetActive(false);
    }

    public void HideAll()
    {
        HideHandles();
        for (int i = 0; i < MenuRoot.childCount; i++)
            MenuRoot.GetChild(i).gameObject.SetActive(false);
    }

    /// worldTop/worldBottom: the caret line's top/bottom in world space at
    /// the selection edge. The pin parks its stem over that line segment.
    public void PositionHandle(SelectionHandleView h, Vector3 worldTop, Vector3 worldBottom, bool stemUp)
    {
        var rt = (RectTransform)h.transform;
        Vector2 screenTop = RectTransformUtility.WorldToScreenPoint(null, worldTop);
        Vector2 screenBottom = RectTransformUtility.WorldToScreenPoint(null, worldBottom);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            (RectTransform)transform, (screenTop + screenBottom) * 0.5f, null, out var local);
        rt.anchoredPosition = local;
        h.SetStemHeight(Mathf.Abs(screenTop.y - screenBottom.y) / CanvasScale());
        h.SetColor(CurrentAccent());
    }

    float CanvasScale() => Canvas.scaleFactor <= 0f ? 1f : Canvas.scaleFactor;

    static Color CurrentAccent() => Theme.Color(ThemeRole.Accent); // substitute real member from step 1
}
```

- [ ] **Step 4: Compile gate**

Run: `Tools/run-tests-headless.sh "WordBoundaryTests"`
Expected: `12 passed, 0 failed` (proves the new files compile; no behavior tests for pure views — they're covered by the device pass).
If `ThemeRole.Accent` or `Theme.Color` signatures differ from the step-1 findings, fix the two references now.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/TextSelection/SelectionOverlay.cs Assets/Scripts/TextSelection/SelectionOverlay.cs.meta Assets/Scripts/TextSelection/SelectionHandleView.cs Assets/Scripts/TextSelection/SelectionHandleView.cs.meta
git commit -m "feat(textselect): runtime overlay canvas + draggable selection pins

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 8: SelectionMenuView (runtime view)

**Files:**
- Create: `Assets/Scripts/TextSelection/SelectionMenuView.cs`

**Interfaces:**
- Consumes: `SelectionMenuItems` (Task 5), `SelectionOverlay.MenuRoot` (Task 7), `Theme.Color`, `ImageWithRoundedCorners`, a `TMP_FontAsset` supplied by the router (taken from the focused field → guaranteed Cyrillic-capable).
- Produces: `SelectionMenuView.Build(RectTransform parent) : SelectionMenuView`; `Show(SelectionMenuItems items, Vector2 screenAnchorTop, Vector2 screenAnchorBottom, TMPro.TMP_FontAsset font)`; `Hide()`; `bool IsVisible`; event `ItemTapped : Action<SelectionMenuItems>` (single flag per invoke); `ApplyTheme()`.

- [ ] **Step 1: Write the menu view**

Layout: pill height 120, corner radius 60, item side padding 40, label size 44, hairline separators 2 wide at 20% ink alpha. Anchored above `screenAnchorTop` with a 24-unit gap; if that would clip the top of the screen, place below `screenAnchorBottom` instead; clamp horizontally with a 24-unit margin.

```csharp
// Assets/Scripts/TextSelection/SelectionMenuView.cs
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Nobi.UiRoundedCorners;

/// The floating iOS-style edit menu («Вырезать · Копировать · Вставить ·
/// Выделить всё»). Pure view: renders whichever items the policy allows and
/// reports taps; owns no clipboard/selection logic.
public class SelectionMenuView : MonoBehaviour
{
    const float Height = 120f;
    const float Radius = 60f;
    const float ItemPad = 40f;
    const float LabelSize = 44f;
    const float Gap = 24f;
    const float EdgeMargin = 24f;

    public System.Action<SelectionMenuItems> ItemTapped;
    public bool IsVisible => gameObject.activeSelf;

    RectTransform _rt;
    Image _bg;
    HorizontalLayoutGroup _layout;
    readonly List<(SelectionMenuItems item, GameObject root, TMP_Text label, Image hairline)> _items
        = new List<(SelectionMenuItems, GameObject, TMP_Text, Image)>();

    static readonly (SelectionMenuItems item, string label)[] Order =
    {
        (SelectionMenuItems.Cut, "Вырезать"),
        (SelectionMenuItems.Copy, "Копировать"),
        (SelectionMenuItems.Paste, "Вставить"),
        (SelectionMenuItems.SelectAll, "Выделить всё"),
    };

    public static SelectionMenuView Build(RectTransform parent)
    {
        var go = new GameObject("SelectionMenu",
            typeof(RectTransform), typeof(Image), typeof(SelectionMenuView),
            typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        go.transform.SetParent(parent, false);

        var view = go.GetComponent<SelectionMenuView>();
        view._rt = (RectTransform)go.transform;
        view._bg = go.GetComponent<Image>();
        view._bg.sprite = null;
        go.AddComponent<ImageWithRoundedCorners>().radius = Radius;

        view._layout = go.GetComponent<HorizontalLayoutGroup>();
        view._layout.childAlignment = TextAnchor.MiddleCenter;
        view._layout.childControlWidth = true;
        view._layout.childControlHeight = true;
        view._layout.childForceExpandWidth = false;
        view._layout.childForceExpandHeight = false;

        var fitter = go.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        view._rt.sizeDelta = new Vector2(0, Height);

        foreach (var (item, label) in Order)
        {
            if (view._items.Count > 0)
            {
                var sep = new GameObject("Hairline", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                sep.transform.SetParent(go.transform, false);
                sep.GetComponent<LayoutElement>().preferredWidth = 2f;
                sep.GetComponent<LayoutElement>().preferredHeight = Height * 0.55f;
                var sepImg = sep.GetComponent<Image>();
                sepImg.sprite = null;
                sepImg.raycastTarget = false;
                view._items.Add((SelectionMenuItems.None, sep, null, sepImg));
            }

            var itemGo = new GameObject(item.ToString(),
                typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            itemGo.transform.SetParent(go.transform, false);
            itemGo.GetComponent<Image>().color = Color.clear;
            itemGo.GetComponent<LayoutElement>().preferredHeight = Height;

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            labelGo.transform.SetParent(itemGo.transform, false);
            var tmp = labelGo.GetComponent<TextMeshProUGUI>();
            tmp.fontSize = LabelSize;
            tmp.text = label;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.margin = new Vector4(ItemPad, 0, ItemPad, 0);
            labelGo.GetComponent<LayoutElement>().preferredHeight = Height;
            var lrt = (RectTransform)labelGo.transform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;

            var itemGroup = itemGo.AddComponent<HorizontalLayoutGroup>();
            itemGroup.childControlWidth = true;
            itemGroup.childControlHeight = true;

            var captured = item;
            itemGo.GetComponent<Button>().onClick.AddListener(() => view.ItemTapped?.Invoke(captured));
            view._items.Add((item, itemGo, tmp, null));
        }

        go.SetActive(false);
        return view;
    }

    public void Show(SelectionMenuItems items, Vector2 screenAnchorTop, Vector2 screenAnchorBottom, TMP_FontAsset font)
    {
        if (items == SelectionMenuItems.None) { Hide(); return; }
        gameObject.SetActive(true);
        ApplyTheme();

        bool prevWasVisibleItem = false;
        foreach (var e in _items)
        {
            if (e.item == SelectionMenuItems.None)      // hairline: show only after a visible item
            {
                e.root.SetActive(prevWasVisibleItem);
                continue;
            }
            bool visible = (items & e.item) != 0;
            e.root.SetActive(visible);
            if (visible && font != null) e.label.font = font;
            if (visible) prevWasVisibleItem = true;
        }
        TrimTrailingHairline();
        LayoutRebuilder.ForceRebuildLayoutImmediate(_rt);

        var parent = (RectTransform)_rt.parent;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenAnchorTop, null, out var top);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenAnchorBottom, null, out var bottom);

        float halfW = _rt.rect.width / 2f;
        float x = Mathf.Clamp(top.x, -parent.rect.width / 2f + halfW + EdgeMargin,
                                      parent.rect.width / 2f - halfW - EdgeMargin);
        float yAbove = top.y + Gap + Height / 2f;
        float y = (yAbove + Height / 2f + EdgeMargin > parent.rect.height / 2f)
            ? bottom.y - Gap - Height / 2f
            : yAbove;
        _rt.anchoredPosition = new Vector2(x, y);
    }

    void TrimTrailingHairline()
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            if (!_items[i].root.activeSelf) continue;
            if (_items[i].item == SelectionMenuItems.None) _items[i].root.SetActive(false);
            return;
        }
    }

    public void Hide() => gameObject.SetActive(false);

    public void ApplyTheme()
    {
        // Substitute the real ThemeRole members from Task 7 step 1:
        // elevated surface for the pill, primary ink for labels.
        _bg.color = Theme.Color(ThemeRole.SurfaceElevated);
        foreach (var e in _items)
        {
            if (e.label != null) e.label.color = Theme.Color(ThemeRole.InkPrimary);
            if (e.hairline != null)
            {
                var ink = Theme.Color(ThemeRole.InkPrimary);
                e.hairline.color = new Color(ink.r, ink.g, ink.b, 0.2f);
            }
        }
    }
}
```

- [ ] **Step 2: Compile gate**

Run: `Tools/run-tests-headless.sh "SelectionMenuPolicyTests"`
Expected: `5 passed, 0 failed`. Fix any `ThemeRole` member mismatches against the Task 7 step-1 findings.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/TextSelection/SelectionMenuView.cs Assets/Scripts/TextSelection/SelectionMenuView.cs.meta
git commit -m "feat(textselect): floating RU edit-menu view

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 9: TextSelectionRouter — integration (BLOCKED until Task 2 verdict is GO)

**Files:**
- Create: `Assets/Scripts/TextSelection/TextSelectionRouter.cs`

**Interfaces:**
- Consumes: everything above — `SelectionGestureMachine`, `WordBoundary.WordRangeAt`, `SelectionActions.CopyText/Cut/Paste` + `SelectionEdit`, `SelectionMenuPolicy.Visible` + `SelectionMenuItems`, `KeyboardSelectionSync.Push`, `SelectionOverlay.Create/ShowHandles/HideHandles/HideAll/PositionHandle/MenuRoot/HandlesVisible`, `SelectionHandleView.DragMoved/DragEnded/IsStart`, `SelectionMenuView.Build/Show/Hide/IsVisible/ItemTapped`, `Theme.Changed`, `GUIUtility.systemCopyBuffer`, `TMP_TextUtilities.GetCursorIndexFromPosition`.
- Produces: `TextSelectionRouter.Instance` (creates) / `TextSelectionRouter.Existing` (never creates) — UploadCenter pattern; auto-bootstrapped via `[RuntimeInitializeOnLoadMethod]`, play mode only. No other class needs to call anything for the feature to work.

- [ ] **Step 1: Verify the input backend**

Run: `grep -n "activeInputHandler" ProjectSettings/ProjectSettings.asset`
Expected: `activeInputHandler: 0` (legacy) or `2` (both) — proceed. If `1` (new Input System only): **stop and flag**; the router's `Input.*` reads below must be rewritten against the new API first.

- [ ] **Step 2: Write the router**

```csharp
// Assets/Scripts/TextSelection/TextSelectionRouter.cs
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// App-wide iOS-style text-selection layer. One always-active singleton
/// (UploadCenter pattern: Instance creates, Existing never does) that
/// OBSERVES pointer input — it never consumes events, so taps, typing,
/// scrolling and every existing gesture behave exactly as before. It runs
/// the long-press/double-tap machine over whatever TMP_InputField sits
/// under the finger (raycast through ClickPassthrough strips), drives the
/// pins + menu on the runtime overlay, and routes every programmatic
/// selection change through KeyboardSelectionSync so the hidden native
/// keyboard buffer stays honest (see 2026-08-07 spec).
public class TextSelectionRouter : MonoBehaviour
{
    static TextSelectionRouter _instance;
    public static TextSelectionRouter Existing => _instance;
    public static TextSelectionRouter Instance
    {
        get
        {
            if (_instance == null && Application.isPlaying)
            {
                var go = new GameObject("TextSelectionRouter", typeof(TextSelectionRouter));
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap() { var _ = Instance; }

    static readonly FieldInfo KbField = typeof(TMP_InputField).GetField(
        "m_SoftKeyboard", BindingFlags.Instance | BindingFlags.NonPublic);

    readonly SelectionGestureMachine _machine = new SelectionGestureMachine(
        0.45f, 0.3f, 10f * (Screen.dpi > 0 ? Screen.dpi : 160f) / 160f); // 10 dp slop in px

    SelectionOverlay _overlay;
    SelectionMenuView _menu;
    readonly List<RaycastResult> _hits = new List<RaycastResult>();
    PointerEventData _ped;

    TMP_InputField _pressField;        // field under the current press
    TMP_InputField _activeField;       // field owning the visible selection UI
    TMP_InputField _pendingField;      // long-pressed while unfocused; select after focus materializes
    Vector2 _pendingPos;
    float _pendingDeadline;
    bool _applyingEdit;                // our own mutation → don't treat as external text change
    bool _menuPendingOnRelease;
    int _lastAnchor = -1, _lastFocusPos = -1;

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        Theme.Changed += OnThemeChanged;
    }

    void OnDestroy()
    {
        if (_instance == this) _instance = null;
        Theme.Changed -= OnThemeChanged;
    }

    void OnThemeChanged()
    {
        if (_menu != null && _menu.IsVisible) _menu.ApplyTheme();
        if (_activeField != null) ApplySelectionTint(_activeField);
    }

    // ---------- input pump ----------

    void Update()
    {
        float now = Time.unscaledTime;

        if (PointerDownThisFrame(out var pos))
        {
            if (IsOverOwnUi(pos)) { /* overlay handles it */ }
            else
            {
                _pressField = FieldUnderPointer(pos);
                var result = _machine.Press(pos, now);
                if (_pressField == null && result != SelectionGestureMachine.Result.DoubleTap)
                    DismissAll();          // outside tap
                else
                    HandleGesture(result, pos);
            }
        }
        else if (PointerHeld(out pos))
        {
            HandleGesture(_machine.Move(pos, now), pos);
            HandleGesture(_machine.Tick(now), pos);
            if (_machine.LongPressActive && _pressField != null && _pendingField == null)
                ExtendSelectionTo(_pressField, pos);
        }
        else if (PointerUpThisFrame(out pos))
        {
            HandleGesture(_machine.Release(pos, now), pos);
            if (_menuPendingOnRelease) { _menuPendingOnRelease = false; ShowMenuForActiveField(); }
        }

        ProcessPendingFocusSelect();
        WatchExternalSelection();
        WatchFieldLifecycle();
    }

    void LateUpdate()
    {
        if (_overlay != null && _overlay.HandlesVisible && _activeField != null)
            RepositionHandles();
    }

    bool PointerDownThisFrame(out Vector2 pos)
    {
        if (Input.touchCount > 0)
        {
            var t = Input.GetTouch(0);
            pos = t.position;
            return t.phase == TouchPhase.Began;
        }
        pos = Input.mousePosition;
        return Input.GetMouseButtonDown(0);
    }

    bool PointerHeld(out Vector2 pos)
    {
        if (Input.touchCount > 0)
        {
            var t = Input.GetTouch(0);
            pos = t.position;
            return t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary;
        }
        pos = Input.mousePosition;
        return Input.GetMouseButton(0) && !Input.GetMouseButtonDown(0);
    }

    bool PointerUpThisFrame(out Vector2 pos)
    {
        if (Input.touchCount > 0)
        {
            var t = Input.GetTouch(0);
            pos = t.position;
            return t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled;
        }
        pos = Input.mousePosition;
        return Input.GetMouseButtonUp(0);
    }

    // ---------- raycast ----------

    TMP_InputField FieldUnderPointer(Vector2 screenPos)
    {
        if (EventSystem.current == null) return null;
        _ped = _ped ?? new PointerEventData(EventSystem.current);
        _ped.position = screenPos;
        _hits.Clear();
        EventSystem.current.RaycastAll(_ped, _hits);
        for (int i = 0; i < _hits.Count; i++)
        {
            var f = _hits[i].gameObject.GetComponentInParent<TMP_InputField>();
            if (f != null && f.interactable) return f;
        }
        return null;
    }

    bool IsOverOwnUi(Vector2 screenPos)
    {
        if (_overlay == null || EventSystem.current == null) return false;
        _ped = _ped ?? new PointerEventData(EventSystem.current);
        _ped.position = screenPos;
        _hits.Clear();
        EventSystem.current.RaycastAll(_ped, _hits);
        for (int i = 0; i < _hits.Count; i++)
            if (_hits[i].gameObject.GetComponentInParent<SelectionOverlay>() != null) return true;
        return false;
    }

    // ---------- gesture handling ----------

    void HandleGesture(SelectionGestureMachine.Result result, Vector2 pos)
    {
        switch (result)
        {
            case SelectionGestureMachine.Result.LongPress:
            case SelectionGestureMachine.Result.DoubleTap:
                if (_pressField == null) break;
                if (!_pressField.isFocused)
                {
                    EventSystem.current.SetSelectedGameObject(_pressField.gameObject);
                    _pressField.ActivateInputField();
                    _pendingField = _pressField;   // apply once focus MATERIALIZES (spec)
                    _pendingPos = pos;
                    _pendingDeadline = Time.unscaledTime + 1f;
                }
                else
                {
                    SelectWordAt(_pressField, pos);
                }
                _menuPendingOnRelease = true;
                break;

            case SelectionGestureMachine.Result.Tap:
                if (_pressField == null || _pressField != _activeField) DismissAll();
                else HideMenuKeepSelectionUi(); // caret moved inside the active field
                break;

            case SelectionGestureMachine.Result.Cancel:
                _menuPendingOnRelease = false;
                break;
        }
    }

    void ProcessPendingFocusSelect()
    {
        if (_pendingField == null) return;
        if (Time.unscaledTime > _pendingDeadline) { _pendingField = null; return; }
        bool keyboardReady = Application.isEditor || KbField?.GetValue(_pendingField) != null;
        if (_pendingField.isFocused && keyboardReady)
        {
            SelectWordAt(_pendingField, _pendingPos);
            _pendingField = null;
        }
    }

    // ---------- selection ops ----------

    void SelectWordAt(TMP_InputField field, Vector2 screenPos)
    {
        int stringIdx = StringIndexAt(field, screenPos);
        var (start, end) = WordBoundary.WordRangeAt(field.text, stringIdx);
        ApplySelectionTint(field);
        _activeField = field;

        if (start == end)
        {
            field.stringPosition = start;
            KeyboardSelectionSync.Push(field);
            _overlay?.HideHandles();
        }
        else
        {
            field.selectionStringAnchorPosition = start;
            field.selectionStringFocusPosition = end;
            KeyboardSelectionSync.Push(field);
            EnsureOverlay();
            _overlay.ShowHandles();
        }
        RememberSelection(field);
    }

    void ExtendSelectionTo(TMP_InputField field, Vector2 screenPos)
    {
        if (_activeField != field) return;
        int idx = StringIndexAt(field, screenPos);
        if (idx == field.selectionStringFocusPosition) return;
        field.selectionStringFocusPosition = idx;
        KeyboardSelectionSync.Push(field);
        if (field.selectionStringAnchorPosition != field.selectionStringFocusPosition)
        { EnsureOverlay(); _overlay.ShowHandles(); }
        RememberSelection(field);
    }

    void OnHandleDragged(SelectionHandleView handle, Vector2 screenPos)
    {
        if (_activeField == null) return;
        _menu?.Hide();
        int idx = StringIndexAt(_activeField, screenPos);
        int anchor = _activeField.selectionStringAnchorPosition;
        int focus = _activeField.selectionStringFocusPosition;
        int lo = Mathf.Min(anchor, focus), hi = Mathf.Max(anchor, focus);

        if (handle.IsStart) lo = Mathf.Min(idx, hi - 1);   // min 1 char, pins swap at the router level
        else hi = Mathf.Max(idx, lo + 1);

        _activeField.selectionStringAnchorPosition = lo;
        _activeField.selectionStringFocusPosition = hi;
        KeyboardSelectionSync.Push(_activeField);
        RememberSelection(_activeField);
        AutoScrollTowards(_activeField, screenPos);
    }

    void OnHandleDragEnded(SelectionHandleView handle) => ShowMenuForActiveField();

    int StringIndexAt(TMP_InputField field, Vector2 screenPos)
    {
        var cam = FieldCamera(field);
        int charIdx = TMP_TextUtilities.GetCursorIndexFromPosition(
            field.textComponent, screenPos, cam, out CaretPosition side);
        var info = field.textComponent.textInfo;
        if (info.characterCount == 0) return 0;
        charIdx = Mathf.Clamp(charIdx, 0, info.characterCount - 1);
        var ci = info.characterInfo[charIdx];
        int stringIdx = side == CaretPosition.Right ? ci.index + ci.stringLength : ci.index;
        return WordBoundary.ClampToCharBoundary(field.text, stringIdx);
    }

    static Camera FieldCamera(TMP_InputField field)
    {
        var canvas = field.GetComponentInParent<Canvas>();
        return canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera : null;
    }

    // ---------- menu ----------

    void ShowMenuForActiveField()
    {
        if (_activeField == null) return;
        EnsureOverlay();
        bool hasSel = _activeField.selectionStringAnchorPosition != _activeField.selectionStringFocusPosition;
        var items = SelectionMenuPolicy.Visible(
            hasSelection: hasSel,
            clipboardHasText: !string.IsNullOrEmpty(GUIUtility.systemCopyBuffer),
            textLength: _activeField.text.Length,
            allSelected: hasSel && SelectionSpan(_activeField) == _activeField.text.Length,
            readOnly: _activeField.readOnly);
        if (items == SelectionMenuItems.None) return;
        var (top, bottom) = SelectionScreenBounds(_activeField);
        _menu.Show(items, top, bottom, _activeField.textComponent.font);
    }

    void OnMenuItem(SelectionMenuItems item)
    {
        if (_activeField == null) return;
        var f = _activeField;
        int a = f.selectionStringAnchorPosition, s = f.selectionStringFocusPosition;

        switch (item)
        {
            case SelectionMenuItems.Copy:
                GUIUtility.systemCopyBuffer = SelectionActions.CopyText(f.text, a, s);
                _menu.Hide();
                break;

            case SelectionMenuItems.Cut:
                GUIUtility.systemCopyBuffer = SelectionActions.CopyText(f.text, a, s);
                ApplyEdit(f, SelectionActions.Cut(f.text, a, s));
                break;

            case SelectionMenuItems.Paste:
                ApplyEdit(f, SelectionActions.Paste(f.text, a, s, GUIUtility.systemCopyBuffer, f.characterLimit));
                break;

            case SelectionMenuItems.SelectAll:
                f.selectionStringAnchorPosition = 0;
                f.selectionStringFocusPosition = f.text.Length;
                KeyboardSelectionSync.Push(f);
                RememberSelection(f);
                EnsureOverlay();
                _overlay.ShowHandles();
                ShowMenuForActiveField();
                break;
        }
    }

    void ApplyEdit(TMP_InputField field, SelectionEdit edit)
    {
        _applyingEdit = true;
        field.text = edit.NewText;                    // focused-field write-through (safe by invariant)
        field.stringPosition = edit.NewCaret;
        KeyboardSelectionSync.Push(field);
        _applyingEdit = false;
        HideMenuKeepSelectionUi();
        _overlay?.HideHandles();
        RememberSelection(field);
    }

    // ---------- watching ----------

    void WatchExternalSelection()
    {
        if (_activeField == null || _machine.IsPressed) return;
        // A TMP-originated drag-select (composer) also deserves pins + menu.
        int a = _activeField.selectionStringAnchorPosition, s = _activeField.selectionStringFocusPosition;
        if (a == _lastAnchor && s == _lastFocusPos) return;
        RememberSelection(_activeField);
        if (a != s) { EnsureOverlay(); _overlay.ShowHandles(); ShowMenuForActiveField(); }
    }

    void WatchFieldLifecycle()
    {
        // Selection UI lives only while its field is focused. Focus loss also
        // covers keyboard dismissal (fields deactivate when the OS keyboard
        // closes) and screen changes.
        if (_activeField != null && !_activeField.isFocused) DismissAll();

        // External text change (typing) while UI shown → dismiss.
        if (_activeField != null && !_applyingEdit && _overlay != null && _overlay.HandlesVisible)
        {
            int span = SelectionSpan(_activeField);
            if (span == 0) DismissAll();
        }
    }

    void RememberSelection(TMP_InputField f)
    {
        _lastAnchor = f.selectionStringAnchorPosition;
        _lastFocusPos = f.selectionStringFocusPosition;
    }

    static int SelectionSpan(TMP_InputField f) =>
        Mathf.Abs(f.selectionStringFocusPosition - f.selectionStringAnchorPosition);

    // ---------- overlay plumbing ----------

    void EnsureOverlay()
    {
        if (_overlay != null) return;
        _overlay = SelectionOverlay.Create();
        _overlay.StartHandle.DragMoved += OnHandleDragged;
        _overlay.EndHandle.DragMoved += OnHandleDragged;
        _overlay.StartHandle.DragEnded += OnHandleDragEnded;
        _overlay.EndHandle.DragEnded += OnHandleDragEnded;
        _menu = SelectionMenuView.Build(_overlay.MenuRoot);
        _menu.ItemTapped += OnMenuItem;
    }

    void RepositionHandles()
    {
        var (startTop, startBottom, endTop, endBottom) = SelectionEdgeWorldCorners(_activeField);
        _overlay.PositionHandle(_overlay.StartHandle, startTop, startBottom, stemUp: true);
        _overlay.PositionHandle(_overlay.EndHandle, endTop, endBottom, stemUp: false);
    }

    (Vector3, Vector3, Vector3, Vector3) SelectionEdgeWorldCorners(TMP_InputField field)
    {
        var info = field.textComponent.textInfo;
        int lo = Mathf.Min(field.selectionStringAnchorPosition, field.selectionStringFocusPosition);
        int hi = Mathf.Max(field.selectionStringAnchorPosition, field.selectionStringFocusPosition);
        var t = field.textComponent.transform;

        var (sx, sTopY, sBotY) = CaretMetrics(info, lo, leftEdge: true);
        var (ex, eTopY, eBotY) = CaretMetrics(info, hi, leftEdge: false);
        return (t.TransformPoint(new Vector3(sx, sTopY)), t.TransformPoint(new Vector3(sx, sBotY)),
                t.TransformPoint(new Vector3(ex, eTopY)), t.TransformPoint(new Vector3(ex, eBotY)));
    }

    static (float x, float top, float bottom) CaretMetrics(TMP_TextInfo info, int stringIndex, bool leftEdge)
    {
        if (info.characterCount == 0) return (0, 0, 0);
        int charIdx = 0;
        for (int i = 0; i < info.characterCount; i++)
        {
            charIdx = i;
            if (info.characterInfo[i].index >= stringIndex) break;
        }
        var ci = info.characterInfo[charIdx];
        bool useRight = !leftEdge && ci.index < stringIndex;
        float x = useRight ? ci.xAdvance : ci.origin;
        return (x, ci.ascender, ci.descender);
    }

    (Vector2 top, Vector2 bottom) SelectionScreenBounds(TMP_InputField field)
    {
        var (sTop, sBot, eTop, eBot) = SelectionEdgeWorldCorners(field);
        Vector2 a = RectTransformUtility.WorldToScreenPoint(null, sTop);
        Vector2 b = RectTransformUtility.WorldToScreenPoint(null, eTop);
        Vector2 c = RectTransformUtility.WorldToScreenPoint(null, sBot);
        Vector2 d = RectTransformUtility.WorldToScreenPoint(null, eBot);
        return (new Vector2((a.x + b.x) / 2f, Mathf.Max(a.y, b.y)),
                new Vector2((c.x + d.x) / 2f, Mathf.Min(c.y, d.y)));
    }

    // ---------- misc ----------

    void AutoScrollTowards(TMP_InputField field, Vector2 screenPos)
    {
        var scroll = field.textComponent.GetComponentInParent<ScrollRect>();
        if (scroll == null || !scroll.vertical) return;
        var viewport = (RectTransform)(scroll.viewport != null ? scroll.viewport : scroll.transform);
        var corners = new Vector3[4];
        viewport.GetWorldCorners(corners);
        float bottomY = RectTransformUtility.WorldToScreenPoint(null, corners[0]).y;
        float topY = RectTransformUtility.WorldToScreenPoint(null, corners[1]).y;
        const float BandPx = 60f;
        const float Speed = 1.2f;
        if (screenPos.y > topY - BandPx)
            scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition + Speed * Time.unscaledDeltaTime);
        else if (screenPos.y < bottomY + BandPx)
            scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition - Speed * Time.unscaledDeltaTime);
    }

    void ApplySelectionTint(TMP_InputField field)
    {
        var accent = Theme.Color(ThemeRole.Accent);   // substitute real member (Task 7 step 1)
        field.selectionColor = new Color(accent.r, accent.g, accent.b, 0.25f);
    }

    void HideMenuKeepSelectionUi() => _menu?.Hide();

    void DismissAll()
    {
        _menu?.Hide();
        _overlay?.HideHandles();
        _menuPendingOnRelease = false;
        _pendingField = null;
        _activeField = null;
        _lastAnchor = _lastFocusPos = -1;
    }
}
```

- [ ] **Step 3: Compile gate + full regression**

Run: `Tools/run-tests-headless.sh` (no filter — full suite)
Expected: previous total (1471) + 35 new (12+8+5+8+2) = 1506 passed, 0 failed. Fix `ThemeRole` member mismatches if any remain.

- [ ] **Step 4: Editor smoke (Game view, mouse)**

With the Editor open (Main scene, Play mode, 1080×2400 Game view): press-and-hold ~half a second on text in the chat composer → word highlights, two pins appear, menu appears on mouse-up; click «Копировать»; click into another field → UI dismisses; long-press → «Вставить» → text inserted and Save-dirty behavior in BotSettings unaffected elsewhere. Keyboard-buffer behavior is NOT verifiable in the Editor (no TouchScreenKeyboard) — that's Task 11.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/TextSelection/TextSelectionRouter.cs Assets/Scripts/TextSelection/TextSelectionRouter.cs.meta
git commit -m "feat(textselect): TextSelectionRouter — gestures to pins/menu/clipboard, keyboard-buffer sync

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 10: Full suite, docs, and cleanup hooks

**Files:**
- Modify: `CLAUDE.md` (Architecture → add the `TextSelection/` subsystem summary)
- Modify: `docs/superpowers/specs/2026-08-07-input-text-selection-design.md` (append implementation notes if anything deviated)

- [ ] **Step 1: Full headless suite**

Run: `Tools/run-tests-headless.sh`
Expected: all passed, 0 failed.

- [ ] **Step 2: Update CLAUDE.md**

Add to the Scripts layout section (adjust wording to findings):

```markdown
- `TextSelection/` — iOS-style select/cut/copy/paste layer for every TMP_InputField (shipped 2026-08). `TextSelectionRouter` (lazy always-active singleton, `Instance`/`Existing` — bootstrapped via RuntimeInitializeOnLoadMethod, zero scene wiring) observes touches and runs long-press/double-tap word selection with draggable pins + floating RU menu on a runtime overlay canvas. Pure seams (`WordBoundary`, `SelectionActions`, `SelectionMenuPolicy`, `SelectionGestureMachine`) are EditMode-tested. CRITICAL: every programmatic selection change MUST route through `KeyboardSelectionSync.Push` — TMP only syncs the hidden native keyboard buffer on its own pointer paths, and a stale buffer caret makes the next keystroke edit the wrong position on iOS. Mutations write only the focused field's own `.text` (never another field's). Spike scene `SpikeTextSelection.unity` + `Tools/Text Selection/Build Spike Scene` are throwaway diagnostics.
```

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md docs/superpowers/specs/2026-08-07-input-text-selection-design.md
git commit -m "docs(textselect): CLAUDE.md subsystem entry + spec implementation notes

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 11: Device passes (OWNER CHECKPOINT) + polish

**Files:**
- Modify: whatever the device pass demands (expect tuning constants in `SelectionOverlay` / `SelectionMenuView` / `TextSelectionRouter`).

- [ ] **Step 1: iOS device pass (owner)**

Checklist, run in the real app build — each row on: chat composer, a BotSettings `EditableField`, the Промпт `ScrollableTextArea`, a product-sheet field:

1. Long-press a word → selects it, pins + menu appear; selection tint is the accent color.
2. Drag each pin → character-precise adjustment; pins swap when crossed; menu returns on release; inner text area auto-scrolls when dragging near its edge.
3. Double-tap → word selected.
4. «Копировать» → paste the result in WhatsApp/Telegram (cross-app clipboard out).
5. Copy a phrase in Safari → long-press in-app → «Вставить» pastes it (cross-app in); Save button lights in BotSettings after the edit.
6. Select a word, type a letter → the letter REPLACES the selection (the load-bearing sync check).
7. «Вырезать» → text removed, in clipboard; «Выделить всё» → full selection with working pins.
8. Tap elsewhere → everything dismisses; keyboard dismissal dismisses the UI; theme flip (dark↔light) recolors pins/menu.
9. Regression sweep: plain taps, typing, field switching (single-focus smooth-switch), swipe-back, sheet drag, chat scroll all unchanged.

- [ ] **Step 2: Android device pass (owner)** — same checklist; expect the per-frame TMP sync to cover step 6 natively.

- [ ] **Step 3: Fix + tune** — apply whatever the passes surface (constants: long-press 0.45 s, slop 10 dp, auto-scroll band/speed, menu offsets). Re-run the full suite after each fix. Commit per fix:

```bash
git add -A Assets/Scripts/TextSelection
git commit -m "fix(textselect): device-pass tuning — <what changed>

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

- [ ] **Step 4: Close out** — record the device-pass result in the spec file; update the `project_text_selection_gap` memory to SHIPPED status.

---

## Execution findings (2026-08-07, Tasks 1–8)

- `activeInputHandler: 1` — the project runs the **new Input System only**. Task 9's input pump must NOT use legacy `Input.*`; use the repo idiom (see `MessageBubbleLongPress.cs`, `DeferredDismissInputField.IsPointerPressed`): `Pointer.current.press.wasPressedThisFrame / isPressed / wasReleasedThisFrame` + `Pointer.current.position.ReadValue()`, null-guarded, with `Touchscreen.current.primaryTouch` as the touch-specific check. Add `using UnityEngine.InputSystem;`.
- The spike builder was corrected accordingly: `InputSystemUIInputModule` (namespace `UnityEngine.InputSystem.UI`), not `StandaloneInputModule`, and builds ADDITIVELY (create → populate → save → close) so the open Main scene is never touched.
- Theme substitutions resolved in Tasks 7–8: `ThemeRole.AccentFill` (pins + selection tint), `ThemeRole.Surface` (menu pill), `ThemeRole.InkPrimary` (labels), `ThemeRole.Hairline` (separators). `Theme.Changed` is `event Action`. Overlay `SortingOrder = 4` (main canvas is 0; reaction-bar runtime canvas is 5).
- `SelectionOverlay.PositionHandle` dropped the redundant `stemUp` parameter — pin direction is baked at Build time.
- Full suite after Task 8: **1506/1506** (1471 pre-existing + 35 new).

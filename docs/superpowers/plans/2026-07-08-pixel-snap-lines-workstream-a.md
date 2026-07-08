# Pixel-Snapped Lines (Workstream A) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every thin divider/line/rule in the app render at a consistent, crisp whole-pixel thickness on all devices, replacing the single-purpose `NativeHairline`.

**Architecture:** A pure `PixelSnap` helper converts a designed thickness (canvas units) to the nearest whole physical pixel using `rootCanvas.scaleFactor`. A `PixelSnapLine` MonoBehaviour applies that to a RectTransform axis on enable/resize. A one-shot editor tool (`Tools/Pixel Snap/Wire All`) discovers line objects across the scene and prefabs, wires the confidently-classified ones, and prints a review log flagging ambiguous `Border`/odd-named objects for a human call.

**Tech Stack:** Unity 6000.3.9f1, C# (Assembly-CSharp + Assembly-CSharp-Editor), Unity Test Framework (NUnit, EditMode).

## Global Constraints

- Unity **6000.3.9f1**, URP; single-scene (`Assets/Scenes/Main.unity`), UI toggled via `SetActive`.
- Thickness policy: **nearest whole physical pixel** (preserve designed weight), min 1px. Derive from `canvas.rootCanvas.scaleFactor` — never the nearest canvas.
- EditMode tests live in `Assets/Tests/Editor/Chat/` with **no asmdef** (they compile into `Assembly-CSharp-Editor`). Do not add an asmdef.
- Runtime scripts (`PixelSnap`, `PixelSnapLine`) go in `Assets/Scripts/Chat/` (Assembly-CSharp). Editor tool goes in `Assets/Editor/` wrapped in `#if UNITY_EDITOR` (belt-and-braces even though `Assets/Editor/` is already an editor folder).
- Run EditMode tests via the project test bridge: **Editor closed** → `bash Tools/run-tests-headless.sh '<filter>'`; **Editor open** → drop empty `Temp/claude/run-tests.trigger`, read `Temp/claude/test-summary.json`.
- Brand-new `.cs` files can be silently excluded from the first compile — after creating one, trigger an Asset Refresh (mcp-unity `execute_menu_item Assets/Refresh` or focus the Editor) and confirm a `.meta` appeared before running tests.
- C# quality: `[SerializeField] private` for inspector fields, PascalCase types/methods, expression-bodied one-liners OK, no placeholders.
- Commits: stage the specific `.cs` **and** its `.meta`. Ask for per-task commit consent (project rule); do not push without being asked. Work stays on branch `feat/pixel-snap-lines`.

---

### Task 1: `PixelSnap` helper + unit tests

**Files:**
- Create: `Assets/Scripts/Chat/PixelSnap.cs`
- Test: `Assets/Tests/Editor/Chat/PixelSnapTests.cs`

**Interfaces:**
- Produces:
  - `static float PixelSnap.SnapPx(float designUnits, float scaleFactor)` — pure; nearest whole physical pixel expressed back in canvas units, min 1px; returns `designUnits` unchanged if `scaleFactor <= 0`.
  - `static float PixelSnap.SnapUnits(float designUnits, Canvas canvas)` — reads `canvas.rootCanvas.scaleFactor` and delegates to `SnapPx`; returns `designUnits` if `canvas == null`.

- [ ] **Step 1: Write the failing tests**

Create `Assets/Tests/Editor/Chat/PixelSnapTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

public class PixelSnapTests
{
    [Test]
    public void SnapPx_UnitScale_ReturnsSameUnit()
    {
        Assert.AreEqual(1f, PixelSnap.SnapPx(1f, 1f), 1e-4f);
    }

    [Test]
    public void SnapPx_HighDensity_SnapsToWholePixelCount()
    {
        // 1 unit * 3 = 3px -> 3/3 = 1 unit (represents exactly 3 physical px)
        Assert.AreEqual(1f, PixelSnap.SnapPx(1f, 3f), 1e-4f);
    }

    [Test]
    public void SnapPx_FractionalScale_RoundsToNearestPixel()
    {
        // 2 * 1.33 = 2.66 -> round 3 -> 3 / 1.33
        Assert.AreEqual(3f / 1.33f, PixelSnap.SnapPx(2f, 1.33f), 1e-4f);
    }

    [Test]
    public void SnapPx_ResultIsAlwaysWholePhysicalPixels()
    {
        foreach (var sf in new[] { 1f, 1.33f, 2f, 2.625f, 3f, 3.5f })
        foreach (var u in new[] { 1f, 2f, 3f })
        {
            float units = PixelSnap.SnapPx(u, sf);
            float px = units * sf;
            Assert.AreEqual(Mathf.Round(px), px, 1e-3f, $"sf={sf} u={u} -> {px}px not whole");
            Assert.GreaterOrEqual(px, 1f - 1e-3f, $"sf={sf} u={u} under 1px");
        }
    }

    [Test]
    public void SnapPx_SubPixelDesign_ClampsToOnePixel()
    {
        // 0.4 * 1 = 0.4 -> round 0 -> max(1,0) = 1 -> 1px
        Assert.AreEqual(1f, PixelSnap.SnapPx(0.4f, 1f), 1e-4f);
    }

    [Test]
    public void SnapPx_InvalidScale_ReturnsDesignUnchanged()
    {
        Assert.AreEqual(1f, PixelSnap.SnapPx(1f, 0f), 1e-4f);
        Assert.AreEqual(2f, PixelSnap.SnapPx(2f, -3f), 1e-4f);
    }
}
```

- [ ] **Step 2: Write a stub so it compiles and fails**

Create `Assets/Scripts/Chat/PixelSnap.cs`:

```csharp
using UnityEngine;

public static class PixelSnap
{
    public static float SnapPx(float designUnits, float scaleFactor)
    {
        throw new System.NotImplementedException();
    }

    public static float SnapUnits(float designUnits, Canvas canvas)
    {
        if (canvas == null) return designUnits;
        return SnapPx(designUnits, canvas.rootCanvas.scaleFactor);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Trigger an Asset Refresh so both new files compile, confirm `PixelSnap.cs.meta` and `PixelSnapTests.cs.meta` exist, then:

Run: `bash Tools/run-tests-headless.sh 'PixelSnap'`
Expected: tests run and FAIL (NotImplementedException in every `SnapPx` assertion).

- [ ] **Step 4: Implement the real body**

Replace `SnapPx` in `Assets/Scripts/Chat/PixelSnap.cs`:

```csharp
    public static float SnapPx(float designUnits, float scaleFactor)
    {
        if (scaleFactor <= 0f) return designUnits;
        float px = Mathf.Max(1f, Mathf.Round(designUnits * scaleFactor));
        return px / scaleFactor;
    }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `bash Tools/run-tests-headless.sh 'PixelSnap'`
Expected: PASS (6 tests, 0 failures).

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Chat/PixelSnap.cs Assets/Scripts/Chat/PixelSnap.cs.meta \
        Assets/Tests/Editor/Chat/PixelSnapTests.cs Assets/Tests/Editor/Chat/PixelSnapTests.cs.meta
git commit -m "feat(ui): PixelSnap helper — nearest whole-pixel thickness from scaleFactor"
```

---

### Task 2: `PixelSnapLine` component + axis-mapping test

**Files:**
- Create: `Assets/Scripts/Chat/PixelSnapLine.cs`
- Test: `Assets/Tests/Editor/Chat/PixelSnapLineTests.cs`

**Interfaces:**
- Consumes: `PixelSnap.SnapUnits(float, Canvas)` (Task 1).
- Produces:
  - `enum PixelSnapLine.SnapAxis { Height, Width }`
  - `static RectTransform.Axis PixelSnapLine.ToUnityAxis(SnapAxis)` — `Height→Vertical`, `Width→Horizontal`.
  - `void PixelSnapLine.EditorConfigure(SnapAxis axis, float thicknessUnits)` — editor-only, sets the serialized fields (used by Task 3's wirer).

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/Editor/Chat/PixelSnapLineTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

public class PixelSnapLineTests
{
    [Test]
    public void ToUnityAxis_Height_MapsToVertical()
    {
        Assert.AreEqual(RectTransform.Axis.Vertical,
            PixelSnapLine.ToUnityAxis(PixelSnapLine.SnapAxis.Height));
    }

    [Test]
    public void ToUnityAxis_Width_MapsToHorizontal()
    {
        Assert.AreEqual(RectTransform.Axis.Horizontal,
            PixelSnapLine.ToUnityAxis(PixelSnapLine.SnapAxis.Width));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `bash Tools/run-tests-headless.sh 'PixelSnapLine'`
Expected: FAIL to compile / test error — `PixelSnapLine` does not exist yet.

- [ ] **Step 3: Write the component**

Create `Assets/Scripts/Chat/PixelSnapLine.cs`:

```csharp
using UnityEngine;

/// <summary>
/// Sizes one axis of this RectTransform to the nearest whole physical pixel so a
/// thin line/divider renders crisp and identically across devices (no sub-pixel
/// shimmer). Generalizes the retired NativeHairline (which only forced 1px height).
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class PixelSnapLine : MonoBehaviour
{
    public enum SnapAxis { Height, Width }

    [SerializeField] private SnapAxis axis = SnapAxis.Height;
    [Tooltip("Authored thickness in canvas units (1, 2, 3…). Snapped to whole px at runtime.")]
    [SerializeField] private float designThicknessUnits = 1f;

    private RectTransform rect;
    private Canvas canvas;

    public static RectTransform.Axis ToUnityAxis(SnapAxis a)
        => a == SnapAxis.Height ? RectTransform.Axis.Vertical : RectTransform.Axis.Horizontal;

    private void OnEnable() { Cache(); Apply(); }

    private void OnRectTransformDimensionsChange() { Apply(); }

    private void Cache()
    {
        rect = (RectTransform)transform;
        canvas = GetComponentInParent<Canvas>();
    }

    private void Apply()
    {
        if (rect == null || canvas == null) Cache();
        if (rect == null || canvas == null) return;

        float snapped = PixelSnap.SnapUnits(designThicknessUnits, canvas);
        var uAxis = ToUnityAxis(axis);

        // Epsilon guard: skip the resize when nothing changes, so we don't re-dirty
        // layout on unrelated rebuild passes. SnapUnits is idempotent, so this converges.
        float current = uAxis == RectTransform.Axis.Vertical ? rect.rect.height : rect.rect.width;
        if (Mathf.Abs(current - snapped) < 0.01f) return;

        rect.SetSizeWithCurrentAnchors(uAxis, snapped);
    }

#if UNITY_EDITOR
    /// <summary>Editor-only: set serialized fields from the wiring tool.</summary>
    public void EditorConfigure(SnapAxis snapAxis, float thicknessUnits)
    {
        axis = snapAxis;
        designThicknessUnits = thicknessUnits;
    }
#endif
}
```

- [ ] **Step 4: Run test to verify it passes**

Trigger Asset Refresh, confirm `PixelSnapLine.cs.meta` exists, then:

Run: `bash Tools/run-tests-headless.sh 'PixelSnapLine'`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Chat/PixelSnapLine.cs Assets/Scripts/Chat/PixelSnapLine.cs.meta \
        Assets/Tests/Editor/Chat/PixelSnapLineTests.cs Assets/Tests/Editor/Chat/PixelSnapLineTests.cs.meta
git commit -m "feat(ui): PixelSnapLine component — axis-aware whole-pixel line snapping"
```

---

### Task 3: `PixelSnapWirer` editor tool

**Files:**
- Create: `Assets/Editor/PixelSnapWirer.cs`

**Interfaces:**
- Consumes: `PixelSnapLine` + `PixelSnapLine.EditorConfigure` (Task 2).
- Produces: menu `Tools/Pixel Snap/Wire All` (no runtime interface). Deliberately does **not** reference `NativeHairline`, so it compiles independently of Task 4's deletion; the ChatItem `Divider` is wired by name like any other divider and its stale `NativeHairline` is removed in Task 4.

This task has no unit test (it drives the Unity Editor / mutates assets). It is verified by running it and reading its log in Task 4.

- [ ] **Step 1: Write the tool**

Create `Assets/Editor/PixelSnapWirer.cs`:

```csharp
#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-shot, idempotent tool: adds PixelSnapLine to confidently-classified thin
/// line objects across the open scene and the known prefabs, and prints a review
/// log. Ambiguous objects (Border frames, odd-named or non-thin candidates) are
/// FLAGGED, never modified — a human resolves them from the log.
/// </summary>
public static class PixelSnapWirer
{
    private const float MaxThicknessUnits = 8f; // anything thicker isn't a hairline; flag it

    private static readonly string[] PrefabPaths =
    {
        "Assets/Prefabs/ChatItem.prefab",
        "Assets/Prefabs/Bot.prefab",
        "Assets/Prefabs/BotSettings.prefab",
        "Assets/Prefabs/DateSeparator.prefab",
        "Assets/Prefabs/UnreadSeparator.prefab",
    };

    [MenuItem("Tools/Pixel Snap/Wire All")]
    public static void WireAll()
    {
        var log = new StringBuilder();
        int wired = 0, flagged = 0;

        var scene = EditorSceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects())
            Walk(root.transform, "scene", log, ref wired, ref flagged);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        foreach (var path in PrefabPaths)
        {
            var contents = PrefabUtility.LoadPrefabContents(path);
            int before = wired;
            Walk(contents.transform, path, log, ref wired, ref flagged);
            if (wired != before) PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[PixelSnapWirer] wired={wired} flagged={flagged}\n{log}");
    }

    private static void Walk(Transform t, string where, StringBuilder log, ref int wired, ref int flagged)
    {
        Classify(t, where, log, ref wired, ref flagged);
        foreach (Transform child in t)
            Walk(child, where, log, ref wired, ref flagged);
    }

    private static void Classify(Transform t, string where, StringBuilder log, ref int wired, ref int flagged)
    {
        if (t is not RectTransform rt) return;
        string n = t.name;

        // Border frames: never auto-wired — need a per-object human decision.
        if (n.StartsWith("Border"))
        {
            log.AppendLine($"FLAG [{where}] {n} — Border frame, decide manually (size={rt.rect.size})");
            flagged++;
            return;
        }

        bool looksLikeLine =
            n == "Divider" || n.EndsWith("Divider") ||
            n == "Separator" || n.EndsWith("Separator") ||
            n == "LeftLine" || n == "RightLine" ||
            n.StartsWith("Line") || n.EndsWith("Line");
        if (!looksLikeLine) return;

        float w = rt.rect.width, h = rt.rect.height;

        // Axis: explicit vertical names first, else infer from which side is thin.
        PixelSnapLine.SnapAxis axis;
        float thickness;
        if (n == "LeftLine" || n == "RightLine") { axis = PixelSnapLine.SnapAxis.Width; thickness = w; }
        else if (h > 0f && h <= MaxThicknessUnits && w > h) { axis = PixelSnapLine.SnapAxis.Height; thickness = h; }
        else if (w > 0f && w <= MaxThicknessUnits && h > w) { axis = PixelSnapLine.SnapAxis.Width;  thickness = w; }
        else
        {
            log.AppendLine($"FLAG [{where}] {n} — ambiguous aspect (w={w:0.#}, h={h:0.#}), decide manually");
            flagged++;
            return;
        }

        if (thickness <= 0f || thickness > MaxThicknessUnits)
        {
            log.AppendLine($"FLAG [{where}] {n} — thickness {thickness:0.##} out of 1..{MaxThicknessUnits} range, verify");
            flagged++;
            return;
        }

        var line = t.GetComponent<PixelSnapLine>();
        if (line == null) line = t.gameObject.AddComponent<PixelSnapLine>();
        line.EditorConfigure(axis, thickness);
        EditorUtility.SetDirty(t.gameObject);
        log.AppendLine($"WIRE [{where}] {n} — axis={axis} thickness={thickness:0.##}");
        wired++;
    }
}
#endif
```

- [ ] **Step 2: Compile-check (no test)**

Trigger Asset Refresh, confirm `Assets/Editor/PixelSnapWirer.cs.meta` exists and the console shows **no compile errors**. Do not run the menu yet — that happens in Task 4 where its output is reviewed.

- [ ] **Step 3: Commit**

```bash
git add Assets/Editor/PixelSnapWirer.cs Assets/Editor/PixelSnapWirer.cs.meta
git commit -m "feat(editor): Tools/Pixel Snap/Wire All — discover + wire thin lines, flag ambiguous"
```

---

### Task 4: Run the wirer, resolve flags, retire `NativeHairline`

**Files:**
- Modify (via tool): `Assets/Scenes/Main.unity`, `Assets/Prefabs/ChatItem.prefab`, `Assets/Prefabs/Bot.prefab`, `Assets/Prefabs/BotSettings.prefab`, `Assets/Prefabs/DateSeparator.prefab`, `Assets/Prefabs/UnreadSeparator.prefab`
- Modify (manual, in Editor): `Assets/Prefabs/ChatItem.prefab` (remove stale `NativeHairline` component)
- Delete: `Assets/Scripts/Chat/NativeHairline.cs` (+ `.meta`)

**Interfaces:**
- Consumes: `Tools/Pixel Snap/Wire All` (Task 3), `PixelSnapLine` (Task 2).

This task is Editor-driven and verified visually + by absence of missing-script warnings; it has no automated test.

- [ ] **Step 1: Run the wirer**

In the Unity Editor with `Main.unity` open, run menu **Tools → Pixel Snap → Wire All**. Read the `[PixelSnapWirer]` console log.

- [ ] **Step 2: Review the log and resolve every FLAG**

For each `FLAG` line, open the object and decide manually:
- A genuine thin line the heuristic missed → add `PixelSnapLine` by hand, set axis + `designThicknessUnits` to the authored thickness.
- A `Border` **frame** (4-sided) that is a rounded outline → leave for Workstream B (SDF) or leave as-is; do not add `PixelSnapLine`.
- Not actually a line → leave as-is.

Confirm the `WIRE` lines cover the expected sites (chat/message separators, bots-list divider, settings dividers, scene `Divider`/`Line*`/`LeftLine`/`RightLine`). Note anything surprising.

- [ ] **Step 3: Migrate ChatItem's divider off NativeHairline**

The wirer added `PixelSnapLine` to ChatItem's `Divider`, but the stale `NativeHairline` is still on it and will fight for the height. In the Editor, open `Assets/Prefabs/ChatItem.prefab`, select `Divider`, and **remove the `NativeHairline` component** (leave `PixelSnapLine`). Save the prefab.

- [ ] **Step 4: Delete the NativeHairline script**

With no remaining references (ChatItem was the only one), delete the file:

```bash
git rm Assets/Scripts/Chat/NativeHairline.cs Assets/Scripts/Chat/NativeHairline.cs.meta
```

Trigger Asset Refresh.

- [ ] **Step 5: Verify no missing scripts**

In the Editor console after refresh, confirm there are **no "missing script" / "can't be loaded" warnings** on `ChatItem` or any wired object. Enter Play Mode briefly on `Main.unity`, open the chats list, and confirm the row dividers still render (now via `PixelSnapLine`). Exit Play Mode.

- [ ] **Step 6: Commit**

```bash
git add -A Assets/Scenes/Main.unity Assets/Prefabs/ChatItem.prefab Assets/Prefabs/Bot.prefab \
          Assets/Prefabs/BotSettings.prefab Assets/Prefabs/DateSeparator.prefab \
          Assets/Prefabs/UnreadSeparator.prefab
git add -u Assets/Scripts/Chat/NativeHairline.cs Assets/Scripts/Chat/NativeHairline.cs.meta
git commit -m "feat(ui): wire PixelSnapLine across lines; retire NativeHairline"
```

Note: large `Main.unity` diffs after a scene save are benign in this project (layout zeroing / material regen) — verify per-object, don't be alarmed by churn.

---

### Task 5: Full EditMode green + device visual pass

**Files:** none (verification only).

- [ ] **Step 1: Run the full EditMode suite**

Run: `bash Tools/run-tests-headless.sh` (no filter — full suite)
Expected: PASS, including the 8 new tests, with **no regressions** vs the pre-change count.

- [ ] **Step 2: Device visual pass (Android build — the shimmer is only visible on device)**

On a physical high-DPI Android device (scaleFactor > 1), verify:
1. Scroll the **chats list** and a **message list** — row/date/unread dividers hold a steady thickness, no breathing/shimmer during motion.
2. Section dividers on the **Bots** page and **BotSettings** tabs render crisp and identical to each other.
3. The chats-list row divider looks right at its designed weight (it is intentionally no longer a forced 1px — see spec §12); if it now reads too heavy, flip it to a true hairline via a `forceHairline` follow-up rather than reverting.
4. Rotate the device / resize — lines re-snap and stay crisp.

- [ ] **Step 3: Record the result**

Confirm tests green and the device checklist passes. This completes Workstream A. (Any FLAG objects deferred in Task 4 Step 2 that should become lines are captured as follow-ups here.)

---

## Self-Review

**Spec coverage (Workstream A scope):**
- Shared `PixelSnap` helper (spec §4) → Task 1. ✔ (Adds a pure `SnapPx(units, scaleFactor)` seam that `SnapUnits(units, canvas)` delegates to — a faithful refinement of §4 for testability.)
- `PixelSnapLine` component, axis-aware, explicit `designThicknessUnits`, epsilon guard (spec §5.1) → Task 2. ✔
- Retire `NativeHairline`, migrate ChatItem (spec §5.2) → Task 4. ✔
- Wiring tool: discover, classify, stamp, flag `Border`/ambiguous (spec §5.3) → Tasks 3–4. ✔
- Performance epsilon guard (spec §7) → Task 2 component. ✔
- Testing: unit tests on the snap math + device pass (spec §9) → Tasks 1, 2, 5. ✔
- Out of scope here: bubble SDF stroke (Workstream B) — separate plan; position snapping (spec §11) — deferred.

**Placeholder scan:** No TBD/TODO; every code step shows complete code; every run step shows an exact command + expected result. ✔

**Type consistency:** `SnapPx`/`SnapUnits`, `SnapAxis {Height,Width}`, `ToUnityAxis`, `EditorConfigure` are used with identical names/signatures across Tasks 1–3. The wirer calls `EditorConfigure(axis, thickness)` exactly as defined in Task 2. ✔

**Note on TDD in Unity:** a missing type is a *compile* failure (not a clean red test), so Task 1 uses a compiling stub that throws to get a meaningful fail; Task 2's "fail" step is an honest compile failure before the type exists. This is the pragmatic red-green under Unity's compile-coupled test model.

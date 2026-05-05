# Bot Settings Swipe-Back Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add iOS-style swipe-right-to-go-back to the BotSettings page with parallax on the BotsPage behind it, matching the existing chat gesture, plus a matching slide-in animation when BotSettings opens.

**Architecture:** New `SwipeToBackBotSettings` MonoBehaviour on the BotSettings root mirrors `Assets/Scripts/Chat/SwipeToBack.cs` (drag handlers + custom-lerp coroutine with the same physics constants). It drives the BotSettings wrapper RectTransform horizontally and applies a 30% parallax to BotsPage. `Bot.OpenSettings()` and `BotSettings.OnBackPressed()` route through the component so tap-open and tap-back share the same animation path. An editor script auto-wires the component on the prefab.

**Tech Stack:** Unity 6000.3.9f1, C#, Unity EventSystems (`IInitializePotentialDragHandler`, `IBeginDragHandler`, `IDragHandler`, `IEndDragHandler`), ScrollRect, Coroutines. No DOTween / no external packages.

**Testing approach:** This is a Unity mobile UI feature. There is no automated test harness in the project — verification is manual in the Unity Editor Game view at 1080x2400 (mobile aspect). Each task ends with a compile check (`Unity → Edit → Play` or reopening the project) and Play-mode manual checks as listed.

---

## File Structure

- **Create:** `Assets/Scripts/Main/SwipeToBackBotSettings.cs` — the gesture + animation component. One responsibility: drive the bot-settings wrapper and BotsPage horizontally.
- **Create:** `Assets/Editor/BotSettingsSwipeWirer.cs` — editor-only menu item that adds and wires the component on the BotSettings prefab.
- **Modify:** `Assets/Scripts/Main/BotSettings.cs` — add `CurrentTabScrollRect` property; restructure `OnBackPressed()` to route through the swipe component; add `SettleClosedInstant()` used by the snap coroutine on commit.
- **Modify:** `Assets/Scripts/Main/Bot.cs` — route `OpenSettings()` through `SwipeToBackBotSettings.SlideInFromRight()` with fallback.

---

## Task 1: Skeleton of `SwipeToBackBotSettings` (component compiles, no logic)

**Why:** Get the empty component compiling and mounted on the prefab before wiring behavior. This lets subsequent tasks iterate in small, verifiable steps.

**Files:**
- Create: `Assets/Scripts/Main/SwipeToBackBotSettings.cs`

- [ ] **Step 1: Create the skeleton file**

Write `Assets/Scripts/Main/SwipeToBackBotSettings.cs`:

```csharp
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Swipe-right-to-go-back + slide-in-from-right animation for the BotSettings
// page. Mirrors Assets/Scripts/Chat/SwipeToBack.cs but is scoped to the
// bot-settings flow (BotSettings wrapper + BotsPage parallax, no chat
// ScrollRect or bottom-tab panel).
public class SwipeToBackBotSettings : MonoBehaviour,
    IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public static SwipeToBackBotSettings Instance;

    [Header("UI References")]
    [SerializeField] private RectTransform botSettingsPanelToSlide;
    [SerializeField] private RectTransform botsPagePanel;

    [Header("Swipe Physics")]
    [Range(0.1f, 1f)] [SerializeField] private float parallaxStrength = 0.3f;
    [SerializeField] private float snapSpeed = 10f;
    [SerializeField] private float slowSwipeThreshold = 0.4f;
    [SerializeField] private float flickVelocityThreshold = 1000f;
    [SerializeField] private float minSnapSpeed = 1500f;

    private Canvas canvas;
    private Coroutine snapCoroutine;
    private bool dragDecided;
    private bool isHorizontalDrag;
    private float dragStartTime;
    private Vector2 dragStartPos;
    private ScrollRect dragScrollRect; // ScrollRect captured at drag-begin for restore

    public bool IsAnimating => snapCoroutine != null;

    private void Awake()
    {
        Instance = this;
        var localCanvas = GetComponentInParent<Canvas>();
        if (localCanvas != null) canvas = localCanvas.rootCanvas;
        if (EventSystem.current != null) EventSystem.current.pixelDragThreshold = 15;
    }

    public void OnInitializePotentialDrag(PointerEventData eventData) { }
    public void OnBeginDrag(PointerEventData eventData) { }
    public void OnDrag(PointerEventData eventData) { }
    public void OnEndDrag(PointerEventData eventData) { }
}
```

- [ ] **Step 2: Verify compile**

Open Unity. Confirm Console shows no compile errors.
Expected: Clean compile, `SwipeToBackBotSettings` visible as a component in `Add Component` search.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Main/SwipeToBackBotSettings.cs \
        Assets/Scripts/Main/SwipeToBackBotSettings.cs.meta
git commit -m "feat(bot-settings): add empty SwipeToBackBotSettings component"
```

---

## Task 2: Implement programmatic slide-in / slide-out (no gesture yet)

**Why:** The animation API is used by tap-open and tap-back as well as the gesture commit. Implementing and testing it first in isolation means Task 3's gesture code only has to call into already-working primitives.

**Files:**
- Modify: `Assets/Scripts/Main/SwipeToBackBotSettings.cs`

- [ ] **Step 1: Add the snap coroutine and public slide methods**

Replace the whole file with:

```csharp
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class SwipeToBackBotSettings : MonoBehaviour,
    IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public static SwipeToBackBotSettings Instance;

    [Header("UI References")]
    [SerializeField] private RectTransform botSettingsPanelToSlide;
    [SerializeField] private RectTransform botsPagePanel;

    [Header("Swipe Physics")]
    [Range(0.1f, 1f)] [SerializeField] private float parallaxStrength = 0.3f;
    [SerializeField] private float snapSpeed = 10f;
    [SerializeField] private float slowSwipeThreshold = 0.4f;
    [SerializeField] private float flickVelocityThreshold = 1000f;
    [SerializeField] private float minSnapSpeed = 1500f;

    private Canvas canvas;
    private Coroutine snapCoroutine;
    private bool dragDecided;
    private bool isHorizontalDrag;
    private float dragStartTime;
    private Vector2 dragStartPos;
    private ScrollRect dragScrollRect;

    public bool IsAnimating => snapCoroutine != null;

    private void Awake()
    {
        Instance = this;
        var localCanvas = GetComponentInParent<Canvas>();
        if (localCanvas != null) canvas = localCanvas.rootCanvas;
        if (EventSystem.current != null) EventSystem.current.pixelDragThreshold = 15;
    }

    // Called by Bot.OpenSettings() after activating the BotSettings wrapper.
    // BotsPage must still be active when this is invoked so the parallax is
    // visible; the onComplete callback deactivates BotsPage once the slide
    // finishes.
    public void SlideInFromRight(Action onComplete = null)
    {
        if (botSettingsPanelToSlide == null) { onComplete?.Invoke(); return; }
        var screenWidth = GetScreenWidth();

        SetPanelX(botSettingsPanelToSlide, screenWidth);
        SetPanelX(botsPagePanel, 0f);

        if (snapCoroutine != null) StopCoroutine(snapCoroutine);
        snapCoroutine = StartCoroutine(SnapToPosition(0f, commitBack: false, onComplete: onComplete));
    }

    // Called by BotSettings.OnBackPressed() after the revert step and after
    // BotsPage has been re-activated. When the animation finishes, onComplete
    // runs — BotSettings uses that to deactivate its wrapper.
    public void SlideOutToBotsPage(Action onComplete = null)
    {
        if (botSettingsPanelToSlide == null) { onComplete?.Invoke(); return; }
        var screenWidth = GetScreenWidth();

        if (snapCoroutine != null) StopCoroutine(snapCoroutine);
        snapCoroutine = StartCoroutine(SnapToPosition(screenWidth, commitBack: false, onComplete: onComplete));
    }

    // One coroutine powers both directions. commitBack=true means "call
    // BotSettings.OnBackPressed() at the end" — used only by the gesture
    // path (see Task 4). Programmatic callers pass commitBack=false.
    private IEnumerator SnapToPosition(float targetX, bool commitBack, Action onComplete = null)
    {
        var screenWidth = GetScreenWidth();
        var maxOffset = screenWidth * parallaxStrength;

        while (Mathf.Abs(botSettingsPanelToSlide.anchoredPosition.x - targetX) > 2f)
        {
            var currentX = botSettingsPanelToSlide.anchoredPosition.x;
            var newX = Mathf.Lerp(currentX, targetX, Time.deltaTime * snapSpeed);

            var minStep = minSnapSpeed * Time.deltaTime;
            if (Mathf.Abs(newX - currentX) < minStep)
                newX = Mathf.MoveTowards(currentX, targetX, minStep);

            ApplyPositions(newX, screenWidth, maxOffset);
            yield return null;
        }

        ApplyPositions(targetX, screenWidth, maxOffset);
        snapCoroutine = null;

        if (commitBack && BotSettings.Instance != null)
            BotSettings.Instance.OnSwipeCommitted();

        onComplete?.Invoke();
    }

    private void ApplyPositions(float panelX, float screenWidth, float maxOffset)
    {
        SetPanelX(botSettingsPanelToSlide, panelX);
        if (botsPagePanel != null)
        {
            var progress = panelX / screenWidth;
            SetPanelX(botsPagePanel, -maxOffset + (maxOffset * progress));
        }
    }

    private static void SetPanelX(RectTransform rt, float x)
    {
        if (rt == null) return;
        rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y);
    }

    private float GetScreenWidth() =>
        canvas != null ? canvas.GetComponent<RectTransform>().rect.width : Screen.width;

    // Stubs — filled in Task 4.
    public void OnInitializePotentialDrag(PointerEventData eventData) { }
    public void OnBeginDrag(PointerEventData eventData) { }
    public void OnDrag(PointerEventData eventData) { }
    public void OnEndDrag(PointerEventData eventData) { }
}
```

Note: `BotSettings.OnSwipeCommitted()` is added in Task 3. It won't exist yet, so this step will produce a compile error. That's intentional — Task 3 adds the method. If you want an intermediate commit here, comment out the two `OnSwipeCommitted` lines temporarily; otherwise just proceed straight to Task 3 before compiling.

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Main/SwipeToBackBotSettings.cs
git commit -m "feat(bot-settings): implement slide-in/out snap coroutine"
```

---

## Task 3: Add `CurrentTabScrollRect`, `OnSwipeCommitted`, and `SettleClosedInstant` on `BotSettings`

**Why:** The swipe component needs an accessor for the active tab's ScrollRect (to disable vertical scrolling during horizontal drag). It also needs a hook that runs the existing revert-and-deactivate logic at the end of a commit animation, without recursing into its own animation.

**Files:**
- Modify: `Assets/Scripts/Main/BotSettings.cs`

- [ ] **Step 1: Add `using UnityEngine.UI` usings if not present**

Confirm line 3 of `Assets/Scripts/Main/BotSettings.cs` already has `using UnityEngine.UI;`. It does. No change needed.

- [ ] **Step 2: Add `CurrentTabScrollRect` property**

Insert this method just above the `//// TABS ////` section header (currently at line 335). Place it after `OnDisable()` at line 333.

```csharp
// Returns the vertical ScrollRect under the currently-active tab root, if
// any. Used by SwipeToBackBotSettings to disable vertical scrolling during
// a horizontal swipe gesture. Returns null when no tab is active or the
// active tab has no ScrollRect child.
public ScrollRect CurrentTabScrollRect
{
    get
    {
        GameObject tab = null;
        if (General  != null && General.activeInHierarchy)  tab = General;
        else if (Business != null && Business.activeInHierarchy) tab = Business;
        else if (Product  != null && Product.activeInHierarchy)  tab = Product;
        else if (Service  != null && Service.activeInHierarchy)  tab = Service;
        else if (Prompt   != null && Prompt.activeInHierarchy)   tab = Prompt;
        return tab != null ? tab.GetComponentInChildren<ScrollRect>(false) : null;
    }
}
```

- [ ] **Step 3: Extract the existing deactivate logic into `SettleClosedInstant()`**

Find `OnBackPressed()` at line 213. Currently it does both the revert work AND the deactivate. Replace it wholesale with two methods. Delete lines 213-244 (the entire current `OnBackPressed()` method) and insert:

```csharp
private void OnBackPressed()
{
    // If already animating (either slide-in or slide-out in progress), ignore
    // duplicate taps. The gesture commit path calls OnSwipeCommitted() directly
    // so it does not go through here.
    if (SwipeToBackBotSettings.Instance != null && SwipeToBackBotSettings.Instance.IsAnimating)
        return;

    RevertUnsavedEdits();

    // BotsPage must be visible during the slide-out so the parallax shows.
    if (BotsPage.Instance != null)
        BotsPage.Instance.gameObject.SetActive(true);

    if (SwipeToBackBotSettings.Instance != null)
        SwipeToBackBotSettings.Instance.SlideOutToBotsPage(SettleClosedInstant);
    else
        SettleClosedInstant(); // fallback when swipe component isn't wired
}

// Called by SwipeToBackBotSettings once the commit animation finishes (either
// gesture-driven or programmatic via OnBackPressed). Deactivates the wrapper
// and clears the open-bot references. Also used as the fallback when the
// swipe component is missing.
public void SettleClosedInstant()
{
    if (Manager.BotSettingsParentStatic != null)
    {
        var parentGo = Manager.BotSettingsParentStatic.transform.parent != null
            ? Manager.BotSettingsParentStatic.transform.parent.gameObject
            : Manager.BotSettingsParentStatic;
        parentGo.SetActive(false);
    }
    if (BotsPage.Instance != null)
        BotsPage.Instance.gameObject.SetActive(true);

    Manager.openBot = null;
    Manager.openBotSettings = null;
}

// Runs the existing revert-unsaved-edits behavior. Extracted so
// OnBackPressed and the gesture-commit path share one source of truth.
private void RevertUnsavedEdits()
{
    // Revert any unsaved edits from PlayerPrefs. CloseSettings uses
    // Toggle.SetIsOnWithoutNotify, which flips isOn but bypasses the
    // ToggleRow's onValueChanged listener, so the iOS-style thumb/track
    // stays stuck on the user's last choice. Resync the row visuals
    // below to the now-reverted Toggle.isOn state.
    Manager.Instance.CloseSettings();

    if (whatsappRow != null && WhatsappToggle != null)
        whatsappRow.SetIsOnQuiet(WhatsappToggle.isOn);
    if (telegramRow != null && TelegramToggle != null)
        telegramRow.SetIsOnQuiet(TelegramToggle.isOn);

    if (saveButton != null) saveButton.interactable = false;
}

// Called by SwipeToBackBotSettings when the user swipes past the commit
// threshold (or flicks hard enough). Runs the same revert step that the tap
// path runs, then settles the page closed. Does NOT start another animation
// — the swipe component's snap coroutine is what animated us here.
public void OnSwipeCommitted()
{
    RevertUnsavedEdits();
    SettleClosedInstant();
}
```

- [ ] **Step 4: Verify compile**

Return focus to Unity, let it recompile. Console should be clean.
Expected: No compile errors. `SwipeToBackBotSettings.cs` now resolves `BotSettings.Instance.OnSwipeCommitted()`, and `BotSettings` itself exposes `CurrentTabScrollRect`, `SettleClosedInstant`, and `OnSwipeCommitted`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Main/BotSettings.cs
git commit -m "feat(bot-settings): split OnBackPressed into revert + settle; add CurrentTabScrollRect"
```

---

## Task 4: Implement the drag gesture in `SwipeToBackBotSettings`

**Why:** Now that the programmatic slide + the `BotSettings` hooks compile, add the four drag handlers so a horizontal swipe drives the same animation.

**Files:**
- Modify: `Assets/Scripts/Main/SwipeToBackBotSettings.cs`

- [ ] **Step 1: Replace the four stub handlers with real implementations**

Find the four stubs at the bottom of `Assets/Scripts/Main/SwipeToBackBotSettings.cs`:

```csharp
    public void OnInitializePotentialDrag(PointerEventData eventData) { }
    public void OnBeginDrag(PointerEventData eventData) { }
    public void OnDrag(PointerEventData eventData) { }
    public void OnEndDrag(PointerEventData eventData) { }
```

Replace with:

```csharp
    public void OnInitializePotentialDrag(PointerEventData eventData)
    {
        dragDecided = false;
        dragScrollRect = BotSettings.Instance != null ? BotSettings.Instance.CurrentTabScrollRect : null;
        if (dragScrollRect != null) dragScrollRect.OnInitializePotentialDrag(eventData);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        var trajectory = eventData.position - eventData.pressPosition;
        var mostlyHorizontal = Mathf.Abs(trajectory.x) > Mathf.Abs(trajectory.y);
        var swipingRight = trajectory.x > 0f;

        if (mostlyHorizontal && swipingRight)
        {
            isHorizontalDrag = true;
            if (snapCoroutine != null) { StopCoroutine(snapCoroutine); snapCoroutine = null; }
            if (dragScrollRect != null) dragScrollRect.vertical = false;
            if (botsPagePanel != null) botsPagePanel.gameObject.SetActive(true);
        }
        else
        {
            isHorizontalDrag = false;
            if (dragScrollRect != null) dragScrollRect.OnBeginDrag(eventData);
        }

        dragDecided = true;
        dragStartTime = Time.unscaledTime;
        dragStartPos = eventData.position;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!dragDecided) return;

        if (isHorizontalDrag)
        {
            var scaleFactor = canvas != null ? canvas.scaleFactor : 1f;
            var deltaX = eventData.delta.x / scaleFactor;
            var newX = Mathf.Max(0f, botSettingsPanelToSlide.anchoredPosition.x + deltaX);

            var screenWidth = GetScreenWidth();
            var maxOffset = screenWidth * parallaxStrength;
            ApplyPositions(newX, screenWidth, maxOffset);
        }
        else if (dragScrollRect != null)
        {
            dragScrollRect.OnDrag(eventData);
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!dragDecided) return;

        if (isHorizontalDrag)
        {
            var screenWidth = GetScreenWidth();
            var dragDuration = Mathf.Max(0.0001f, Time.unscaledTime - dragStartTime);
            var dragDistanceX = eventData.position.x - dragStartPos.x;
            var velocityX = dragDistanceX / dragDuration;

            var fastFlick = velocityX > flickVelocityThreshold && dragDistanceX > 20f;
            var pastThreshold = botSettingsPanelToSlide.anchoredPosition.x > (screenWidth * slowSwipeThreshold);

            if (fastFlick || pastThreshold)
                snapCoroutine = StartCoroutine(SnapToPosition(screenWidth, commitBack: true));
            else
                snapCoroutine = StartCoroutine(SnapToPosition(0f, commitBack: false));

            if (dragScrollRect != null) dragScrollRect.vertical = true;
        }
        else if (dragScrollRect != null)
        {
            dragScrollRect.OnEndDrag(eventData);
        }

        dragDecided = false;
        isHorizontalDrag = false;
    }
```

- [ ] **Step 2: Verify compile**

Return focus to Unity. Console should be clean.
Expected: No compile errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Main/SwipeToBackBotSettings.cs
git commit -m "feat(bot-settings): implement swipe-right drag handlers with parallax"
```

---

## Task 5: Route `Bot.OpenSettings()` through the animated slide-in

**Why:** Opening BotSettings should use the same slide + parallax animation in reverse. The existing instant-activate path is kept as a fallback when the component isn't wired yet.

**Files:**
- Modify: `Assets/Scripts/Main/Bot.cs:73-94`

- [ ] **Step 1: Replace `OpenSettings()` body**

Find `OpenSettings()` at `Bot.cs:73`:

```csharp
private void OpenSettings()
{
    BotsPage.Instance.gameObject.SetActive(false);
    Manager.BotSettingsParentStatic.transform.parent.gameObject.SetActive(true);

    if (Manager.BotSettingsParentStatic.transform.childCount != 0)
    {
        foreach (Transform botSettings in Manager.BotSettingsParentStatic.transform)
        {
            if (botSettings.GetSiblingIndex() == transform.GetSiblingIndex())
            {
                botSettings.gameObject.SetActive(true);
                Manager.openBot = gameObject;
                Manager.openBotSettings = botSettings.gameObject.GetComponent<BotSettings>();
            }
            else
            {
                botSettings.gameObject.SetActive(false);
            }
        }
    }
}
```

Replace with:

```csharp
private void OpenSettings()
{
    // Keep BotsPage active during the slide-in so its parallax is visible.
    // It is deactivated in the slide-in onComplete callback below.
    Manager.BotSettingsParentStatic.transform.parent.gameObject.SetActive(true);

    if (Manager.BotSettingsParentStatic.transform.childCount != 0)
    {
        foreach (Transform botSettings in Manager.BotSettingsParentStatic.transform)
        {
            if (botSettings.GetSiblingIndex() == transform.GetSiblingIndex())
            {
                botSettings.gameObject.SetActive(true);
                Manager.openBot = gameObject;
                Manager.openBotSettings = botSettings.gameObject.GetComponent<BotSettings>();
            }
            else
            {
                botSettings.gameObject.SetActive(false);
            }
        }
    }

    if (SwipeToBackBotSettings.Instance != null)
    {
        SwipeToBackBotSettings.Instance.SlideInFromRight(() =>
        {
            if (BotsPage.Instance != null)
                BotsPage.Instance.gameObject.SetActive(false);
        });
    }
    else
    {
        Debug.LogWarning("[Bot.OpenSettings] SwipeToBackBotSettings.Instance is null — " +
                         "falling back to instant open. Run Tools/Bot Settings/Wire Swipe Back.");
        if (BotsPage.Instance != null) BotsPage.Instance.gameObject.SetActive(false);
    }
}
```

- [ ] **Step 2: Verify compile**

Return focus to Unity. Console should be clean.
Expected: No compile errors. Since the prefab isn't wired yet, opening a bot will log the warning and behave as before (instant open) — that's the safety fallback.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Main/Bot.cs
git commit -m "feat(bot-settings): animate OpenSettings via SwipeToBackBotSettings"
```

---

## Task 6: Editor auto-wire script

**Why:** The component's two `RectTransform` references must be filled before Play-mode testing works. The existing codebase uses editor builder scripts (e.g. `BotSettingsRebuilder.cs`) for this pattern — follow the same convention so the wiring is reproducible.

**Files:**
- Create: `Assets/Editor/BotSettingsSwipeWirer.cs`

- [ ] **Step 1: Read one existing builder to match conventions**

Read `Assets/Editor/BotSettingsRebuilder.cs` (first ~50 lines) to confirm the `MenuItem` path format, prefab save pattern, and how it locates the scene BotsPage.

- [ ] **Step 2: Create the wirer**

Write `Assets/Editor/BotSettingsSwipeWirer.cs`:

```csharp
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Editor-only: attach SwipeToBackBotSettings to the BotSettings prefab root
// and wire its two RectTransform references (the wrapper that slides + the
// BotsPage panel that receives parallax). Run once after pulling the swipe
// change, or any time the BotSettings prefab root is replaced.
//
// Menu: Tools/Bot Settings/Wire Swipe Back
public static class BotSettingsSwipeWirer
{
    private const string PrefabPath = "Assets/Prefabs/BotSettings.prefab";
    private const string MenuPath = "Tools/Bot Settings/Wire Swipe Back";

    [MenuItem(MenuPath)]
    public static void Wire()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[SwipeWirer] Prefab not found at {PrefabPath}");
            return;
        }

        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var component = root.GetComponent<SwipeToBackBotSettings>();
            if (component == null) component = root.AddComponent<SwipeToBackBotSettings>();

            var botSettingsRect = root.GetComponent<RectTransform>();
            if (botSettingsRect == null)
            {
                Debug.LogError("[SwipeWirer] BotSettings prefab root has no RectTransform.");
                return;
            }

            var botsPageRect = FindBotsPageRectInScene();
            if (botsPageRect == null)
            {
                Debug.LogWarning("[SwipeWirer] Could not find BotsPage in the open scene. " +
                                 "Open Assets/Scenes/Main.unity and re-run.");
            }

            var serialized = new SerializedObject(component);
            serialized.FindProperty("botSettingsPanelToSlide").objectReferenceValue = botSettingsRect;
            if (botsPageRect != null)
                serialized.FindProperty("botsPagePanel").objectReferenceValue = botsPageRect;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[SwipeWirer] Wired SwipeToBackBotSettings on {PrefabPath}" +
                      (botsPageRect != null ? " (BotsPage linked from scene)." : " (BotsPage ref missing — open Main.unity and re-run)."));
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        EditorSceneManager.MarkAllScenesDirty();
    }

    // Looks for the BotsPage singleton in the currently-open scene. Must be
    // called while Main.unity is loaded in the editor.
    private static RectTransform FindBotsPageRectInScene()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return null;

        foreach (var go in scene.GetRootGameObjects())
        {
            var page = go.GetComponentInChildren<BotsPage>(true);
            if (page != null) return page.GetComponent<RectTransform>();
        }
        return null;
    }
}
#endif
```

- [ ] **Step 3: Verify compile**

Return focus to Unity. Console should be clean.
Expected: No compile errors. New menu item exists at `Tools → Bot Settings → Wire Swipe Back`.

- [ ] **Step 4: Run the wirer**

In Unity:
1. Open `Assets/Scenes/Main.unity`.
2. Click `Tools → Bot Settings → Wire Swipe Back`.
3. Check Console: expect `[SwipeWirer] Wired SwipeToBackBotSettings on Assets/Prefabs/BotSettings.prefab (BotsPage linked from scene).`
4. Open `Assets/Prefabs/BotSettings.prefab` in the Inspector. Confirm `SwipeToBackBotSettings` is present with both `Bot Settings Panel To Slide` and `Bots Page Panel` fields populated.

- [ ] **Step 5: Commit the prefab change along with the script**

```bash
git add Assets/Editor/BotSettingsSwipeWirer.cs \
        Assets/Editor/BotSettingsSwipeWirer.cs.meta \
        Assets/Prefabs/BotSettings.prefab
git commit -m "feat(bot-settings): add editor wirer for SwipeToBackBotSettings and run it"
```

---

## Task 7: Manual verification in Play mode

**Why:** This is a gesture + animation feature. The only meaningful verification is driving it in the Unity Editor Game view.

- [ ] **Step 1: Enter Play mode with mobile aspect**

In Unity: Game view → resolution dropdown → select a 1080x2400 preset (or create one). Press Play.

- [ ] **Step 2: Open a bot from the Bots list**

Tap any bot card.
Expected: BotSettings slides in from the right edge over ~0.25s; BotsPage subtly slides left by ~30% of screen width as it goes, then disappears when the animation completes.

- [ ] **Step 3: Slow swipe-right, release before 40%**

From anywhere on BotSettings, drag right to roughly 20-30% of screen width, then release.
Expected: BotSettings snaps back to fully visible; BotsPage (which was partially visible behind) snaps back off to the left.

- [ ] **Step 4: Slow swipe-right past 40%**

Drag right past ~half the screen width, release.
Expected: BotSettings finishes sliding off to the right; BotsPage slides to center; when done, BotSettings is hidden, unsaved edits are reverted (verify by editing a field, not saving, then swipe-back; reopen and confirm field is back to saved value).

- [ ] **Step 5: Fast flick**

From near the left edge, flick right quickly with small distance.
Expected: Velocity path triggers; panel commits to closed even though distance was short.

- [ ] **Step 6: Vertical scroll inside each tab still works**

Open each of the 5 tabs (General / Business / Product / Service / Prompt). In each, swipe vertically inside the scroll area.
Expected: Content scrolls vertically as before; the back-swipe gesture does NOT engage (panel stays at x=0).

- [ ] **Step 7: Tap-back button still works**

Open BotSettings, tap the back button in the header.
Expected: Same slide-out animation as the gesture. Unsaved edits revert.

- [ ] **Step 8: Open/close repeatedly (10x)**

Open, swipe-back, open, tap-back, etc., ~10 times.
Expected: No drift in positions. BotsPage always returns to x=0 after each close. No console errors.

- [ ] **Step 9: Confirm chat swipe still works**

Open a chat from the chat list, swipe right.
Expected: Existing chat swipe-back behaves identically to before (unchanged — we didn't touch `SwipeToBack.cs`).

- [ ] **Step 10: If everything passes, no further commits needed**

If any step fails, capture the console output, describe which step and what happened, and fix before moving on. Do not claim the feature done unless all ten steps pass.

---

## Self-review notes (pre-execution)

- **Spec coverage:** All four goals from the spec (swipe dismiss, matching physics, slide-in open, vertical scroll preserved) are covered by Tasks 2 + 4 + 5. The Non-Goals are respected — no changes to `SwipeToBack.cs`, no DOTween, no edge restriction.
- **Re-entry guard:** `OnBackPressed` early-returns when `IsAnimating`; gesture commit calls `OnSwipeCommitted` (not `OnBackPressed`) so no recursion. Tap during animation is dropped.
- **Fallback when unwired:** `Bot.OpenSettings` and `BotSettings.OnBackPressed` both fall back to instant open/close when `SwipeToBackBotSettings.Instance` is null, with a warning logged. The wirer is a one-shot menu that guarantees the Instance exists after run.
- **Type consistency:** `IsAnimating`, `SlideInFromRight`, `SlideOutToBotsPage`, `OnSwipeCommitted`, `SettleClosedInstant`, `CurrentTabScrollRect` names appear identically across the files that use them.
- **No placeholders:** Every step has exact code or exact menu click paths. The only non-code step is Task 6 Step 1 (read an existing file for reference), which is just orientation — the actual script is given in full in Step 2.

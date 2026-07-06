# Bot Card Activation Switch (Split-Card Footer) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore the per-bot activation switch on the BotsPage card as a split-card footer (label left, iOS-style switch right) per `docs/superpowers/specs/2026-07-06-bot-card-activation-switch-design.md`.

**Architecture:** A pure static helper (`BotSwitchFooter`) supplies label text/color and switch-handle geometry; `Bot.cs` gains one serialized TMP ref and applies the helper in its two existing state-application points; an idempotent editor builder rebuilds the footer inside `Assets/Prefabs/Bot.prefab` (prefab-only — bots are runtime-instantiated from `Manager.BotPrefab`; the scene holds no baked card instances).

**Tech Stack:** Unity 6000.3.9f1, uGUI + TMPro, DOTween (existing tweens untouched), Nobi.UiRoundedCorners, NUnit EditMode tests (no asmdef — `Assembly-CSharp-Editor`).

## Global Constraints

- All sizes are 1080×1920 canvas **reference units** (dp×3), never CSS/mockup px.
- Exact RU strings: on → `Бот работает`, off → `Бот на паузе` (no punctuation).
- Colors: track on `#34C759`, track off `#E9E9EA`, handle `#FFFFFF`, divider `#E9E9EB`, label on `#3A3A3C`, label off `#8E8E93`.
- **Resolved spec latitude:** card height **360** (row 232 + divider 2 + footer zone 126; switch 84 centered → 21 breathing above/below). Spec said "≈348 on the 4-unit grid with builder latitude"; 348 left only 15 around the switch, which reads cramped next to the Row's 44 padding. 360 is the landed value — flag to Ayan at visual review; it's one builder constant (`CardHeight`) if he wants it tighter.
- Never use `UISprite.psd` on surfaces — null sprite + `ImageWithRoundedCorners` (resolve the type by AppDomain scan, never `Type.GetType(..., "Assembly-CSharp")`).
- TMP alignment set explicitly; icons/graphics are Images, never TMP glyphs.
- Stage `.cs` **and** `.meta` files together. **Every commit needs Ayan's go-ahead first** (standing preference) — prepare the commit, surface it, wait.
- Tests: Editor open → drop `Temp/claude/run-tests.trigger`, read `Temp/claude/test-summary.json` (Editor must be focused; never recompile mid-run). Editor closed → `Tools/run-tests-headless.sh 'BotSwitchFooterTests'`. MCP `run_tests` only with an EXACT class-name filter.
- New-file quirk: if a brand-new `.cs` type is "not found" despite clean compile, delete the `.cs` + `.meta`, let Unity register the deletion, recreate.
- Do not touch: `BotStatusPill`, the hidden `Status` TMP data channel, PlayerPrefs keys, n8n enable calls, reply-mode toggles.

---

## File Structure

- **Create** `Assets/Scripts/Main/BotSwitchFooter.cs` — pure static mapping: footer label text/color per state + handle rest-offset geometry. No MonoBehaviour, no UnityEngine.UI.
- **Create** `Assets/Tests/Editor/Chat/BotSwitchFooterTests.cs` — EditMode tests for the helper (this folder is the project's only editor-test dir).
- **Modify** `Assets/Scripts/Main/Bot.cs` — one `[SerializeField]` TMP ref, one private apply method, two call sites, one geometry-formula replacement.
- **Create** `Assets/Editor/BotCardFooterBuilder.cs` — idempotent `[MenuItem]` prefab builder (delete-and-rebuild footer, restyle switch, rewire serialized refs).
- **Modify (via builder)** `Assets/Prefabs/Bot.prefab`.
- **Modify** `CLAUDE.md` — drop the stale "all vs active filter / `BotsPage.onlyActiveBotsVisible`" claim.

### Prefab facts the code below relies on (verified 2026-07-06)

- Root `Bot`: RectTransform anchors (0.5,0.5), `sizeDelta (0, 232)`; `LayoutElement` min/preferred height 232; white null-sprite Image (raycast ON) + `ImageWithRoundedCorners` radius 40; **the settings `Button` lives on the root** targeting that Image (`Bot.EditButton`) — hence the footer needs a raycast blocker.
- `BotsParent` VLG: `childControlHeight: 0` → **card height comes from root `sizeDelta.y`**, not the LayoutElement (update both anyway); width is VLG-controlled.
- `Row`: **stretch-all** child, HorizontalLayoutGroup padding 44 all sides, spacing 39 → must be re-anchored to a fixed 232 top band or the footer would squash it.
- `ActivationSwitch`: parked at `(-9999,-9999)`, `localScale 0`, size 100×40, `LayoutElement.ignoreLayout 1`; child chain `ActivationSwitch → Background → Handle` (Bot.SetSwitches resolves the handle via `GetChild(0).GetChild(0)` — **preserve this chain**).
- `Background` (track): anchors/pivot left-mid, 100×40, Image = **UISprite sliced**, color currently `#00CC00`; **no RoundedCorners component** (shape came from the sprite).
- `Handle`: center anchors, 36×36, white, **UISprite sliced**.
- `Toggle`: `targetGraphic` = track Image, **`graphic` = Handle Image with `toggleTransition: Fade`** — Unity treats the handle as a checkmark and alpha-hides it when off. Builder must set `graphic = null` + transition None or the knob vanishes in the off state.
- `Bot` serialized fields on the prefab: `backgroundActiveColor` currently `{0, 0.8, 0}` (#00CC00), `handleActiveColor` white.

---

### Task 1: BotSwitchFooter helper + EditMode tests

**Files:**
- Create: `Assets/Scripts/Main/BotSwitchFooter.cs`
- Test: `Assets/Tests/Editor/Chat/BotSwitchFooterTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces (Tasks 2 and 3 call exactly these):
  - `public const float BotSwitchFooter.HandleEdgeInset` (= 5f)
  - `public static string BotSwitchFooter.TextFor(bool isOn)`
  - `public static Color BotSwitchFooter.ColorFor(bool isOn)`
  - `public static float BotSwitchFooter.RestOffset(float trackWidth, float handleWidth)`

Unity note: a test referencing a missing type breaks the whole compile (no per-test RED), so the cycle is stub → failing asserts → real values.

- [ ] **Step 1: Write the stub helper** (compiles, deliberately wrong values)

```csharp
using UnityEngine;

/// <summary>
/// Pure mapping for the bot card's activation footer: label text/color per
/// switch state, plus the switch handle's rest-offset geometry. Kept free of
/// MonoBehaviour so EditMode tests cover it (same pattern as ScrollFabMath).
/// </summary>
public static class BotSwitchFooter
{
    public const float HandleEdgeInset = 5f;

    public static string TextFor(bool isOn) => "";

    public static Color ColorFor(bool isOn) => Color.clear;

    public static float RestOffset(float trackWidth, float handleWidth) => 0f;
}
```

- [ ] **Step 2: Write the tests**

```csharp
using NUnit.Framework;
using UnityEngine;

public class BotSwitchFooterTests
{
    [Test]
    public void TextFor_On_IsBotRabotaet() =>
        Assert.AreEqual("Бот работает", BotSwitchFooter.TextFor(true));

    [Test]
    public void TextFor_Off_IsBotNaPauze() =>
        Assert.AreEqual("Бот на паузе", BotSwitchFooter.TextFor(false));

    [Test]
    public void ColorFor_On_IsInk3A3A3C() =>
        Assert.AreEqual((Color)new Color32(0x3A, 0x3A, 0x3C, 0xFF), BotSwitchFooter.ColorFor(true));

    [Test]
    public void ColorFor_Off_IsMuted8E8E93() =>
        Assert.AreEqual((Color)new Color32(0x8E, 0x8E, 0x93, 0xFF), BotSwitchFooter.ColorFor(false));

    [Test]
    public void RestOffset_NewGeometry_150Track74Handle_Is33() =>
        Assert.AreEqual(33f, BotSwitchFooter.RestOffset(150f, 74f), 0.001f);

    [Test]
    public void RestOffset_OldGeometry_100Track36Handle_Is27() =>
        Assert.AreEqual(27f, BotSwitchFooter.RestOffset(100f, 36f), 0.001f);
}
```

- [ ] **Step 3: Run the tests — expect 6 FAIL**

Editor open: `mkdir -p Temp/claude && touch Temp/claude/run-tests.trigger`, then read `Temp/claude/test-summary.json` (Editor focused).
Editor closed: `Tools/run-tests-headless.sh 'BotSwitchFooterTests'`
Expected: 6 failed (assert mismatches, NOT compile errors). If the runner reports the class not found, apply the new-file quirk fix from Global Constraints.

- [ ] **Step 4: Fill in the real implementation**

```csharp
using UnityEngine;

/// <summary>
/// Pure mapping for the bot card's activation footer: label text/color per
/// switch state, plus the switch handle's rest-offset geometry. Kept free of
/// MonoBehaviour so EditMode tests cover it (same pattern as ScrollFabMath).
/// </summary>
public static class BotSwitchFooter
{
    /// <summary>Gap between the handle's edge and the track's edge at rest.</summary>
    public const float HandleEdgeInset = 5f;

    private static readonly Color OnColor  = new Color32(0x3A, 0x3A, 0x3C, 0xFF);
    private static readonly Color OffColor = new Color32(0x8E, 0x8E, 0x93, 0xFF);

    public static string TextFor(bool isOn) => isOn ? "Бот работает" : "Бот на паузе";

    public static Color ColorFor(bool isOn) => isOn ? OnColor : OffColor;

    /// <summary>
    /// Distance from track centre to the handle's rest point on either side —
    /// replaces the old magic "-30 * width / 160" which was tuned to the
    /// original 100×40 track and under-travels any other size.
    /// </summary>
    public static float RestOffset(float trackWidth, float handleWidth) =>
        (trackWidth - handleWidth) / 2f - HandleEdgeInset;
}
```

- [ ] **Step 5: Run the tests — expect 6 PASS** (same command as Step 3)

- [ ] **Step 6: Commit (after Ayan's go-ahead)**

```bash
git add Assets/Scripts/Main/BotSwitchFooter.cs Assets/Scripts/Main/BotSwitchFooter.cs.meta \
        Assets/Tests/Editor/Chat/BotSwitchFooterTests.cs Assets/Tests/Editor/Chat/BotSwitchFooterTests.cs.meta
git commit -m "feat(bots): BotSwitchFooter pure helper — footer label mapping + handle geometry"
```

---

### Task 2: Wire Bot.cs to the footer label + fix handle travel

**Files:**
- Modify: `Assets/Scripts/Main/Bot.cs` (fields ~line 9–21, `EnableBot` ~251–263, `SetSwitches` ~265–306)

**Interfaces:**
- Consumes: `BotSwitchFooter.TextFor/ColorFor/RestOffset` (Task 1).
- Produces: serialized field **`SwitchFooterLabel`** (exact name — Task 3's builder wires it via `SerializedObject.FindProperty("SwitchFooterLabel")`).

- [ ] **Step 1: Add the serialized field** — after the existing `ActivationSwitch` field (line 13), matching the file's private-`[SerializeField]` style used by `BotIconTile`:

```csharp
    [SerializeField] public Toggle ActivationSwitch;

    [Tooltip("Footer caption under the divider: «Бот работает» / «Бот на паузе». " +
             "Wired by BotCardFooterBuilder.")]
    [SerializeField] private TextMeshProUGUI SwitchFooterLabel;
```

- [ ] **Step 2: Add the apply method** — next to `EnableBot`:

```csharp
    private void ApplySwitchFooterLabel(bool isOn)
    {
        if (SwitchFooterLabel == null) return;

        SwitchFooterLabel.text = BotSwitchFooter.TextFor(isOn);
        SwitchFooterLabel.color = BotSwitchFooter.ColorFor(isOn);
    }
```

- [ ] **Step 3: Call it from `EnableBot`** — insert one line after the three tween lines, before `PlayerPrefs.SetInt`:

```csharp
    private void EnableBot (bool enabled)
    {
        switchHandle.DOAnchorPos (enabled ? switchHandlePosition * -1 : switchHandlePosition, .4f).SetEase (Ease.InOutBack);
        switchBackgroundImage.DOColor (enabled ? backgroundActiveColor : backgroundDefaultColor, .6f);
        switchHandleImage.DOColor (enabled ? handleActiveColor : handleDefaultColor, .4f);

        ApplySwitchFooterLabel(enabled);

        PlayerPrefs.SetInt(transform.name, enabled ? 1 : 0);
        Status.text = enabled ? active ? "Active" : "Connecting.." : "Not Active";
        Status.color = enabled ? active ? green : blue : red;
        
        Manager.Instance.GetEnableWhatsappWorkflow(whatsappWorkflowId, enabled);
        Manager.Instance.GetEnableTelegramWorkflow(telegramWorkflowId, enabled);
    }
```

- [ ] **Step 4: Replace the travel formula and label both branches in `SetSwitches`** — the full method after the edit (three changed spots: the formula line, and one `ApplySwitchFooterLabel` per branch):

```csharp
    private IEnumerator SetSwitches()
    {
        yield return new WaitForEndOfFrame();

        switchHandle = ActivationSwitch.transform.GetChild(0).GetChild(0).GetComponent<RectTransform>();

        var track = ActivationSwitch.transform.GetChild(0).GetComponent<RectTransform>();
        switchHandle.localPosition = new Vector2(
            -BotSwitchFooter.RestOffset(track.rect.width, switchHandle.rect.width),
            switchHandle.localPosition.y);

        switchHandlePosition = switchHandle.anchoredPosition;

        switchBackgroundImage = switchHandle.parent.GetComponent<Image>();
        switchHandleImage = switchHandle.GetComponent<Image>();

        backgroundDefaultColor = switchBackgroundImage.color;
        handleDefaultColor = switchHandleImage.color;

        if (PlayerPrefs.GetInt(transform.name, 1) == 1)
        {
            ActivationSwitch.isOn = true;

            switchHandle.DOAnchorPos(switchHandlePosition * -1, .4f).SetEase(Ease.InOutBack);
            switchBackgroundImage.DOColor(backgroundActiveColor, .6f);
            switchHandleImage.DOColor(handleActiveColor, .4f);

            ApplySwitchFooterLabel(true);

            if (active)
            {
                Status.text = "Active";
                Status.color = green;
            }
            else
            {
                Status.text = "Connecting..";
                Status.color = blue;
            }
        }
        else
        {
            ActivationSwitch.isOn = false;
            Status.text = "Not Active";
            Status.color = red;

            ApplySwitchFooterLabel(false);
        }
    }
```

Nothing else in the method changes — `backgroundDefaultColor`/`handleDefaultColor` capture, the isOn writes (which fire `EnableBot` via the Awake-registered listener, as today), and the tweens all stay byte-identical.

- [ ] **Step 5: Run the FULL EditMode suite** (no filter) — same trigger/headless commands as Task 1 Step 3.
Expected: all green, same total as before this task +6 (no regressions; this task adds no new tests).

- [ ] **Step 6: Commit (after Ayan's go-ahead)**

```bash
git add Assets/Scripts/Main/Bot.cs
git commit -m "feat(bots): Bot.cs drives the activation footer label; geometry-derived handle travel"
```

---

### Task 3: BotCardFooterBuilder — rebuild the prefab

**Files:**
- Create: `Assets/Editor/BotCardFooterBuilder.cs`
- Modify (via running it): `Assets/Prefabs/Bot.prefab`

**Interfaces:**
- Consumes: `Bot.ActivationSwitch`, `Bot.BotDesc` (existing public fields), serialized props `"SwitchFooterLabel"` (Task 2) and `"backgroundActiveColor"`; `BotSwitchFooter.TextFor/ColorFor/RestOffset` (Task 1).
- Produces: rebuilt `Bot.prefab` — new children `FooterRow/{Divider, SwitchLabel}` + reparented `ActivationSwitch`; menu item `Tools/Bots Page/Build Bot Card Footer`; headless entry `BotCardFooterBuilder.Build`.

- [ ] **Step 1: Write the builder**

```csharp
#if UNITY_EDITOR
using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Rebuilds the activation footer inside Assets/Prefabs/Bot.prefab (split-card
/// design, spec 2026-07-06): re-anchors Row to a fixed 232-unit top band, adds
/// FooterRow (hairline divider, «Бот работает» label, the un-parked
/// ActivationSwitch at proper touch size), and rewires Bot's serialized refs.
///
/// Idempotent: pulls the switch out, deletes any previous FooterRow, rebuilds.
/// Prefab-only — bots are runtime-instantiated from Manager.BotPrefab, so no
/// scene edit or scene save is involved.
///
/// Editor-closed path:
///   Unity -batchmode -nographics -projectPath . -executeMethod BotCardFooterBuilder.Build -quit
/// </summary>
public static class BotCardFooterBuilder
{
    private const string PrefabPath = "Assets/Prefabs/Bot.prefab";
    private const string FooterName = "FooterRow";
    private const string DividerName = "Divider";
    private const string LabelName = "SwitchLabel";

    private const float RowHeight = 232f;      // untouched top band (existing card height)
    private const float DividerHeight = 2f;
    private const float CardHeight = 360f;     // 232 + 2 + 126 footer zone (switch 84 + 21 above/below)
    private const float SidePadding = 44f;     // matches Row's HorizontalLayoutGroup padding
    private const float TrackWidth = 150f;
    private const float TrackHeight = 84f;
    private const float HandleSize = 74f;
    private const float LabelFontSize = 38f;   // Body2 on the project type scale

    private static readonly Color TrackOffColor = new Color32(0xE9, 0xE9, 0xEA, 0xFF);
    private static readonly Color TrackOnColor  = new Color32(0x34, 0xC7, 0x59, 0xFF); // matches pill FgActive
    private static readonly Color DividerColor  = new Color32(0xE9, 0xE9, 0xEB, 0xFF);

    private static Type cachedRoundedType;

    [MenuItem("Tools/Bots Page/Build Bot Card Footer")]
    public static void Build()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            BuildInto(root);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("[BotCardFooterBuilder] Bot card footer built and saved.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void BuildInto(GameObject root)
    {
        var bot = root.GetComponent<Bot>();
        if (bot == null || bot.ActivationSwitch == null)
            throw new InvalidOperationException("Bot component or its ActivationSwitch ref missing on prefab root.");

        var rootRect = (RectTransform)root.transform;
        var switchRect = (RectTransform)bot.ActivationSwitch.transform;

        // Idempotency: park the switch on the root before deleting a previous footer.
        switchRect.SetParent(root.transform, false);
        Transform oldFooter = root.transform.Find(FooterName);
        if (oldFooter != null) UnityEngine.Object.DestroyImmediate(oldFooter.gameObject);

        // 1. Card height. BotsParent's VLG has childControlHeight: 0, so the
        //    root sizeDelta is what actually spaces the list; keep the
        //    LayoutElement in sync for safety.
        rootRect.sizeDelta = new Vector2(rootRect.sizeDelta.x, CardHeight);
        var rootLayout = root.GetComponent<LayoutElement>();
        if (rootLayout != null)
        {
            rootLayout.minHeight = CardHeight;
            rootLayout.preferredHeight = CardHeight;
        }

        // 2. Row: stretch-all → fixed top band, so growing the card doesn't
        //    re-center the existing content into the footer zone.
        var row = (RectTransform)root.transform.Find("Row");
        if (row != null)
        {
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.anchoredPosition = Vector2.zero;
            row.sizeDelta = new Vector2(0f, RowHeight);
        }

        // 3. FooterRow — its transparent Image is the raycast blocker that keeps
        //    footer taps off the card's root settings Button.
        var footer = NewUiChild(FooterName, root.transform, root.layer, typeof(Image));
        var footerRect = (RectTransform)footer.transform;
        footerRect.anchorMin = new Vector2(0f, 1f);
        footerRect.anchorMax = new Vector2(1f, 1f);
        footerRect.pivot = new Vector2(0.5f, 1f);
        footerRect.anchoredPosition = new Vector2(0f, -RowHeight);
        footerRect.sizeDelta = new Vector2(0f, CardHeight - RowHeight);
        var blocker = footer.GetComponent<Image>();
        blocker.color = new Color(1f, 1f, 1f, 0f);
        blocker.raycastTarget = true;

        // 4. Divider — hairline inset to the Row's content padding.
        var divider = NewUiChild(DividerName, footer.transform, root.layer, typeof(Image));
        var divRect = (RectTransform)divider.transform;
        divRect.anchorMin = new Vector2(0f, 1f);
        divRect.anchorMax = new Vector2(1f, 1f);
        divRect.pivot = new Vector2(0.5f, 1f);
        divRect.anchoredPosition = Vector2.zero;
        divRect.sizeDelta = new Vector2(-SidePadding * 2f, DividerHeight);
        var divImage = divider.GetComponent<Image>();
        divImage.color = DividerColor;
        divImage.raycastTarget = false;

        // Center of the zone below the divider (both label and switch sit here).
        float contentCenterY = -DividerHeight / 2f;

        // 5. Label — font copied from BotDesc so the card stays typographically whole.
        var labelGo = NewUiChild(LabelName, footer.transform, root.layer, typeof(TextMeshProUGUI));
        var labelRect = (RectTransform)labelGo.transform;
        labelRect.anchorMin = new Vector2(0f, 0.5f);
        labelRect.anchorMax = new Vector2(0f, 0.5f);
        labelRect.pivot = new Vector2(0f, 0.5f);
        labelRect.anchoredPosition = new Vector2(SidePadding, contentCenterY);
        labelRect.sizeDelta = new Vector2(560f, 80f);
        var labelTmp = labelGo.GetComponent<TextMeshProUGUI>();
        if (bot.BotDesc != null) labelTmp.font = bot.BotDesc.font;
        labelTmp.text = BotSwitchFooter.TextFor(true);
        labelTmp.color = BotSwitchFooter.ColorFor(true);
        labelTmp.fontSize = LabelFontSize;
        labelTmp.alignment = TextAlignmentOptions.MidlineLeft; // explicit — project gotcha
        labelTmp.enableWordWrapping = false;
        labelTmp.raycastTarget = false;

        // 6. Switch — un-park, resize, restyle. Child chain must stay
        //    ActivationSwitch → Background → Handle (Bot.SetSwitches walks it).
        switchRect.SetParent(footer.transform, false);
        switchRect.localScale = Vector3.one;
        switchRect.anchorMin = switchRect.anchorMax = new Vector2(1f, 0.5f);
        switchRect.pivot = new Vector2(1f, 0.5f);
        switchRect.anchoredPosition = new Vector2(-SidePadding, contentCenterY);
        switchRect.sizeDelta = new Vector2(TrackWidth, TrackHeight);

        var toggle = bot.ActivationSwitch;
        toggle.graphic = null; // Unity alpha-hides the "checkmark" graphic when off — that was the Handle
        toggle.toggleTransition = Toggle.ToggleTransition.None;

        var track = (RectTransform)switchRect.GetChild(0); // Background
        track.anchorMin = track.anchorMax = new Vector2(0f, 0.5f);
        track.pivot = new Vector2(0f, 0.5f);
        track.anchoredPosition = Vector2.zero;
        track.sizeDelta = new Vector2(TrackWidth, TrackHeight);
        var trackImage = track.GetComponent<Image>();
        trackImage.sprite = null; // was built-in UISprite — blurry edges
        trackImage.color = TrackOffColor;
        trackImage.raycastPadding = new Vector4(20f, 18f, 20f, 18f); // 190×120 invisible hit zone
        EnsureRounded(track.gameObject, TrackHeight / 2f);

        var handle = (RectTransform)track.GetChild(0); // Handle
        handle.anchorMin = handle.anchorMax = new Vector2(0.5f, 0.5f);
        handle.pivot = new Vector2(0.5f, 0.5f);
        handle.sizeDelta = new Vector2(HandleSize, HandleSize);
        handle.anchoredPosition = new Vector2(-BotSwitchFooter.RestOffset(TrackWidth, HandleSize), 0f);
        var handleImage = handle.GetComponent<Image>();
        handleImage.sprite = null;
        handleImage.color = Color.white;
        handleImage.raycastTarget = false;
        EnsureRounded(handle.gameObject, HandleSize / 2f);

        // 7. Serialized wiring on Bot.
        var so = new SerializedObject(bot);
        var labelProp = so.FindProperty("SwitchFooterLabel");
        if (labelProp == null)
            throw new InvalidOperationException("Bot.SwitchFooterLabel not found — compile Task 2 first.");
        labelProp.objectReferenceValue = labelTmp;
        so.FindProperty("backgroundActiveColor").colorValue = TrackOnColor;
        so.ApplyModifiedPropertiesWithoutUndo();

        // No RoundedCorners Refresh here: inside LoadPrefabContents there is no
        // canvas so stretch rects have no size yet; the component re-validates
        // itself at runtime on enable/dimension change (how every other prefab
        // in the project uses it).
    }

    private static GameObject NewUiChild(string name, Transform parent, int layer, Type graphicType)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), graphicType);
        go.layer = layer;
        go.transform.SetParent(parent, false);
        return go;
    }

    // RoundedCorners lives in its OWN UPM assembly — Type.GetType(..., "Assembly-CSharp")
    // silently fails (project memory). Scan loaded assemblies instead.
    private static Type ResolveRoundedType()
    {
        if (cachedRoundedType != null) return cachedRoundedType;

        const string fullName = "Nobi.UiRoundedCorners.ImageWithRoundedCorners";
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType(fullName);
            if (t != null)
            {
                cachedRoundedType = t;
                return t;
            }
        }
        return null;
    }

    private static void EnsureRounded(GameObject go, float radius)
    {
        Type type = ResolveRoundedType();
        if (type == null)
        {
            Debug.LogWarning("[BotCardFooterBuilder] ImageWithRoundedCorners not found — corners will be square.");
            return;
        }
        Component rc = go.GetComponent(type) ?? go.AddComponent(type);
        type.GetField("radius")?.SetValue(rc, radius);
        type.GetField("image")?.SetValue(rc, go.GetComponent<Image>());
    }
}
#endif
```

- [ ] **Step 2: Compile** (mcp-unity `recompile_scripts`, or let the Editor pick it up). If `BotCardFooterBuilder` is "not found" in the menu afterwards, apply the new-file quirk fix.

- [ ] **Step 3: Run the builder**

Editor open: menu `Tools → Bots Page → Build Bot Card Footer` (via mcp-unity `execute_menu_item`; if this immediately follows a recompile, re-run it after the server-restart log line and confirm via console logs — stale-assembly gotcha).
Editor closed: `Unity -batchmode -nographics -projectPath . -executeMethod BotCardFooterBuilder.Build -quit`
Expected console line: `[BotCardFooterBuilder] Bot card footer built and saved.`

- [ ] **Step 4: Assert the prefab YAML** (objective, greppable):

```bash
grep -c "m_Name: FooterRow" Assets/Prefabs/Bot.prefab        # 1
grep -c "m_Name: SwitchLabel" Assets/Prefabs/Bot.prefab      # 1
grep -c -- "-9999" Assets/Prefabs/Bot.prefab                 # 0  (switch un-parked)
grep -c "y: 360" Assets/Prefabs/Bot.prefab                   # ≥1 (root sizeDelta)
grep -A2 "backgroundActiveColor" Assets/Prefabs/Bot.prefab | head -1   # r: 0.20392157, g: 0.78039217, b: 0.34901962
grep -c "guid: 0000000000000000f000000000000000" Assets/Prefabs/Bot.prefab  # fewer than before (UISprite gone from track+handle; the chevron sprite refs remain)
```

Also confirm the switch's child order survived: `Background` still first child of `ActivationSwitch`, `Handle` first child of `Background` (open the prefab in the Editor hierarchy or read the YAML `m_Children`).

- [ ] **Step 5: Re-run the builder once more** (same command) and re-run the Step 4 greps — identical results proves idempotency.

- [ ] **Step 6: Commit (after Ayan's go-ahead)**

```bash
git add Assets/Editor/BotCardFooterBuilder.cs Assets/Editor/BotCardFooterBuilder.cs.meta Assets/Prefabs/Bot.prefab
git commit -m "feat(bots): split-card activation footer — builder + rebuilt Bot.prefab"
```

---

### Task 4: Visual + interaction verification, CLAUDE.md touch-up

**Files:**
- Modify: `CLAUDE.md` (the `BotsPage.cs` architecture line)

**Interfaces:**
- Consumes: everything above.
- Produces: verified feature; docs honest.

- [ ] **Step 1: Game-view visual pass at 1080×2400** — enter Play Mode with at least one saved bot (or create one) and check against the spec:
  - Card is 360 tall; top row content identical to before (icon, name/desc, pill, chevron positions unmoved).
  - Divider insets align with the Row's 44 padding.
  - Switch: green `#34C759` when on, gray `#E9E9EA` when off, white knob resting 5 units from the track end on both sides, fully round corners (no blurry UISprite edges), knob VISIBLE in the off state.
  - Label: «Бот работает» (`#3A3A3C`) on / «Бот на паузе» (`#8E8E93`) off, baseline-centered with the switch.
  - All three pill states still render (Активен green / Подключение blue / Неактивен red) — pill logic untouched.
  - Long bot name still ellipsizes in the top row; footer unaffected.

- [ ] **Step 2: Tap-zone pass (Play Mode):**
  - Tap anywhere in the top row → BotSettings opens (existing slide-in).
  - Tap the footer label / empty footer space → nothing happens (blocker works).
  - Tap the switch → toggles with the DOTween slide; pill flips red «Неактивен» / back; label text+color swap; `[n8n]` enable/deactivate requests appear in the console log.
  - Toggle state survives app restart (PlayerPrefs) — quit Play Mode, re-enter, switch position matches.

- [ ] **Step 3: Full EditMode suite one last time** — all green (same commands as Task 1 Step 3).

- [ ] **Step 4: Fix the stale CLAUDE.md line** — in the Architecture section, replace:

```markdown
  - `BotsPage.cs` — Bots list page (all vs active filter). `BotsPage.Instance`, `BotsPage.onlyActiveBotsVisible`.
```

with:

```markdown
  - `BotsPage.cs` — Bots list page. `BotsPage.Instance`. Each card is `Bot.prefab`: top row opens settings, footer row holds the activation switch («Бот работает» / «Бот на паузе», rebuilt by `BotCardFooterBuilder`).
```

- [ ] **Step 5: Commit (after Ayan's go-ahead)**

```bash
git add CLAUDE.md
git commit -m "docs: BotsPage line — drop stale active-filter claim, note card footer switch"
```

---

## Self-Review (done at plan time)

- **Spec coverage:** layout/geometry → Task 3; colors → Tasks 1+3; behavior (label, helper, travel formula) → Tasks 1+2; tap zones → Task 3 step 1 (blocker) + Task 4 step 2 (verify); build approach → Task 3; verification → Tasks 3–4; CLAUDE.md → Task 4. Deviation from spec recorded in Global Constraints (card height 360 vs ≈348) — surface at Task 4 visual review.
- **Placeholder scan:** none — all steps carry full code/commands/expected output.
- **Type consistency:** `BotSwitchFooter.{HandleEdgeInset, TextFor, ColorFor, RestOffset}` and serialized name `SwitchFooterLabel` are identical across Tasks 1, 2, and 3.
- **Latent-bug fixes folded in (from prefab archaeology):** Toggle `graphic`=Handle with Fade would alpha-hide the knob when off → nulled; track/handle UISprite → null sprite + RoundedCorners; track color was saved as active-green (`#00CC00`) so the old switch never visibly changed track color → off-gray default + proper serialized active color.

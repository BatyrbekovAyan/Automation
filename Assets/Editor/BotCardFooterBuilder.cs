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
            throw new InvalidOperationException("Bot.SwitchFooterLabel not found — compile the Bot.cs change first.");
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

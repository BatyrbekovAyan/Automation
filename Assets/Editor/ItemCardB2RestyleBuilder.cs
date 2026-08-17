using System;
using System.Reflection;
using Automation.BotSettingsUI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Converts the product/service list card to the sketch-007 «B2 Ценник» look:
/// a colour-coded monogram where the grey placeholder square used to be, a
/// flexible text column, and the price as a tag that sizes itself to its own
/// digits. The chevron goes — in B2 the whole card is the target.
///
/// ADDITIVE AND IDEMPOTENT. Existing nodes are REPARENTED, never destroyed and
/// re-created, so every serialized reference survives (the card views' five
/// refs plus the ThemedColor targets). Only two GameObjects are new per card:
/// «Letter» inside the monogram and «Pill» around the price. Node NAMES are
/// kept (Info / NameDesc / Name / Price) so the existing guards and
/// ItemCardTextBoundsWirer keep resolving.
/// Never fix this card with Tools/Rebuild Bot Settings Prefabs — that builder
/// is destructive and has long diverged from these prefabs.
///
/// The price tag needs ZERO runtime code to size itself: the tag's own
/// HorizontalLayoutGroup publishes preferred width = padding + text, and the
/// card's group reads it. Two rules make that hold, and both are pinned by
/// tests — do not "simplify" them away:
///   • the tag's LayoutElement.preferredWidth MUST stay -1. LayoutElement has
///     layoutPriority 1 against a LayoutGroup's 0, so any value there would
///     outrank the group's calculation and freeze the tag at one width.
///   • the text column's LayoutElement carries preferredWidth 0 with
///     flexibleWidth 1 — the CSS `flex:1 1 auto; min-width:0` of the mockup.
///     It masks the column's own huge preferred width so the row's total
///     preferred never exceeds the card, which is what stops uGUI from
///     shrinking the tag when a name is long. minWidth 300 is the opposite
///     guard: a pathological price truncates instead of eating the name.
/// </summary>
public static class ItemCardB2RestyleBuilder
{
    private static readonly string[] CardPrefabPaths =
    {
        "Assets/Prefabs/Product.prefab",
        "Assets/Prefabs/Service.prefab",
    };

    // ---- Layout, in 1080x1920 reference units (sketch CSS px x3) ----------
    // The card is 984 wide: canvas 1080, ScreenContainer +4, Content padding 50/50.
    private const float CardPadding = 36f;      // 12px
    private const float CardSpacing = 33f;      // 11px
    private const float CardRadius = 39f;       // 13px — 1:1 with the shader
    private const float MonoSize = 120f;        // 40px
    private const float MonoRadius = 36f;       // 12px
    private const float LetterFontSize = 48f;   // 16px bold

    private const float NameFontSize = 42f;     // 14px
    private const float DescFontSize = 34.5f;   // 11.5px
    private const float ColumnSpacing = 9f;     // 3px
    private const float ColumnMinWidth = 300f;

    private const float PillHeight = 72f;       // 24px
    private const float PillRadius = 36f;       // full round
    private const float PillPadX = 30f;         // 10px
    private const float PillSpacing = 8f;
    private const float PriceFontSize = 36f;    // 12px bold

    private const string MonoName = "Thumb";    // kept: ScreenThemeWirer skips it by name
    private const string LetterName = "Letter";
    private const string PillName = "Pill";

    private static Type cachedRoundedType;

    [MenuItem("Tools/BotSettings/Restyle Item Card (B2)")]
    public static void Restyle()
    {
        int cards = Run();
        Debug.Log($"[ItemCardB2RestyleBuilder] {cards} card prefab(s) converted to B2.");
    }

    /// <summary>Batch entry: Tools/run-editor-builder.sh ItemCardB2RestyleBuilder.BuildHeadless</summary>
    public static void BuildHeadless()
    {
        int cards = Run();
        Debug.Log($"[ItemCardB2RestyleBuilder] Converted {cards} card prefab(s).");
        Debug.Log("[ItemCardB2RestyleBuilder] Headless build + save complete");
    }

    private static int Run()
    {
        foreach (var path in CardPrefabPaths)
        {
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                RestyleCard(contents, path);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }
        return CardPrefabPaths.Length;
    }

    private static void RestyleCard(GameObject card, string path)
    {
        var root = (RectTransform)card.transform;

        var mono = Require(root, MonoName, path);
        var info = Require(root, "Info", path);
        var nameDesc = Require(info, "NameDesc", path);
        var nameLabel = Require(nameDesc, "Name", path).GetComponent<TextMeshProUGUI>();
        var price = FindAnywhere(root, "Price", path);
        var currency = FindAnywhere(root, "Currency", path);
        var desc = Require(nameDesc, "Desc", path).GetComponent<TextMeshProUGUI>();

        RestyleRoot(root);
        var monogram = RestyleMonogram(mono, nameLabel);
        RestyleColumn(info, nameDesc, nameLabel, desc);
        var pill = BuildPill(root, price, currency);

        // Chevron is gone in B2 — deactivated, not destroyed, so the node and
        // its theme binding survive a revert.
        var chevron = root.Find("Chevron");
        if (chevron != null) chevron.gameObject.SetActive(false);

        mono.SetSiblingIndex(0);
        info.SetSiblingIndex(1);
        pill.SetSiblingIndex(2);

        StampCardView(card, monogram, pill.gameObject, path);
    }

    private static void RestyleRoot(RectTransform root)
    {
        var layout = GetOrAdd<HorizontalLayoutGroup>(root.gameObject);
        layout.padding = new RectOffset((int)CardPadding, (int)CardPadding, (int)CardPadding, (int)CardPadding);
        layout.spacing = CardSpacing;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        SetCornerRadius(root.gameObject, CardRadius);
    }

    private static ItemCardMonogram RestyleMonogram(RectTransform mono, TextMeshProUGUI fontSource)
    {
        var image = mono.GetComponent<Image>();
        if (image != null)
        {
            image.sprite = null;
            image.color = Color.white;   // ItemCardMonogram owns the real colour
            image.raycastTarget = false;
        }

        // The square's colour comes from the item's name, so a ThemedColor here
        // would repaint it flat on the next theme change.
        var themed = mono.GetComponent<ThemedColor>();
        if (themed != null) UnityEngine.Object.DestroyImmediate(themed, true);

        SetCornerRadius(mono.gameObject, MonoRadius);

        var element = GetOrAdd<LayoutElement>(mono.gameObject);
        element.minWidth = element.preferredWidth = MonoSize;
        element.minHeight = element.preferredHeight = MonoSize;
        element.flexibleWidth = 0f;

        // The owner's Product.png / Service.png stay in the prefab, just unused.
        var icon = mono.Find("Icon");
        if (icon != null) icon.gameObject.SetActive(false);

        var letter = GetOrCreate(mono, LetterName);
        Stretch(letter);
        var letterText = GetOrAdd<TextMeshProUGUI>(letter.gameObject);
        // Take the card's own SDF asset rather than TMP's default, or Cyrillic
        // initials fall back to a font that may not carry them.
        if (fontSource != null && fontSource.font != null) letterText.font = fontSource.font;
        letterText.fontSize = LetterFontSize;
        letterText.fontWeight = FontWeight.Bold;
        letterText.alignment = TextAlignmentOptions.Center;
        letterText.raycastTarget = false;
        letterText.text = "Т";

        var monogram = GetOrAdd<ItemCardMonogram>(mono.gameObject);
        var so = new SerializedObject(monogram);
        so.FindProperty("background").objectReferenceValue = image;
        so.FindProperty("letter").objectReferenceValue = letterText;
        so.ApplyModifiedPropertiesWithoutUndo();
        return monogram;
    }

    private static void RestyleColumn(
        RectTransform info, RectTransform nameDesc, TextMeshProUGUI nameLabel, TextMeshProUGUI desc)
    {
        var infoLayout = GetOrAdd<VerticalLayoutGroup>(info.gameObject);
        infoLayout.padding = new RectOffset(0, 0, 0, 0);
        infoLayout.spacing = 0f;
        infoLayout.childAlignment = TextAnchor.MiddleLeft;
        infoLayout.childControlWidth = true;
        infoLayout.childControlHeight = true;
        infoLayout.childForceExpandWidth = true;
        infoLayout.childForceExpandHeight = false;

        var infoElement = GetOrAdd<LayoutElement>(info.gameObject);
        infoElement.preferredWidth = 0f;    // mask the text's own preferred width
        infoElement.minWidth = ColumnMinWidth;
        infoElement.flexibleWidth = 1f;
        infoElement.preferredHeight = -1f;
        infoElement.minHeight = -1f;

        var columnLayout = GetOrAdd<VerticalLayoutGroup>(nameDesc.gameObject);
        columnLayout.spacing = ColumnSpacing;
        columnLayout.childAlignment = TextAnchor.MiddleLeft;
        columnLayout.childControlWidth = true;
        columnLayout.childControlHeight = true;
        columnLayout.childForceExpandWidth = true;
        columnLayout.childForceExpandHeight = false;

        if (nameLabel != null)
        {
            nameLabel.fontSize = NameFontSize;
            nameLabel.fontWeight = FontWeight.Bold;
            nameLabel.textWrappingMode = TextWrappingModes.NoWrap;
            nameLabel.overflowMode = TextOverflowModes.Ellipsis;
        }

        if (desc != null)
        {
            desc.fontSize = DescFontSize;
            // NoWrap keeps the row one line tall AND makes preferredHeight
            // independent of width — the measure-before-width-settles trap.
            desc.textWrappingMode = TextWrappingModes.NoWrap;
            desc.overflowMode = TextOverflowModes.Ellipsis;
        }
    }

    private static RectTransform BuildPill(RectTransform root, RectTransform price, RectTransform currency)
    {
        var pill = GetOrCreate(root, PillName);

        var image = GetOrAdd<Image>(pill.gameObject);
        image.sprite = null;
        image.color = Color.white;
        image.raycastTarget = false;
        BindTheme(pill.gameObject, ThemeRole.AccentSoft);
        SetCornerRadius(pill.gameObject, PillRadius);

        var layout = GetOrAdd<HorizontalLayoutGroup>(pill.gameObject);
        layout.padding = new RectOffset((int)PillPadX, (int)PillPadX, 0, 0);
        layout.spacing = PillSpacing;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var element = GetOrAdd<LayoutElement>(pill.gameObject);
        element.minHeight = element.preferredHeight = PillHeight;
        element.flexibleWidth = 0f;
        element.preferredWidth = -1f;   // load-bearing: see the class summary
        element.minWidth = -1f;

        // Reparent, never re-create: priceLabel is a serialized reference.
        foreach (var label in new[] { price, currency })
        {
            if (label == null) continue;
            label.SetParent(pill, false);
            var text = label.GetComponent<TextMeshProUGUI>();
            if (text == null) continue;
            text.fontSize = PriceFontSize;
            text.fontWeight = FontWeight.Bold;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            BindTheme(label.gameObject, ThemeRole.AccentText);
        }
        if (price != null) price.SetSiblingIndex(0);
        if (currency != null) currency.SetAsLastSibling();

        return pill;
    }

    private static void StampCardView(GameObject card, ItemCardMonogram monogram, GameObject pill, string path)
    {
        // The two card views are twins with no shared base type, so both are
        // probed and whichever is present gets stamped.
        Component view = card.GetComponent<ProductCardView>();
        if (view == null) view = card.GetComponent<ServiceCardView>();
        if (view == null)
            throw new InvalidOperationException($"{path}: no ProductCardView/ServiceCardView on the card root.");

        var so = new SerializedObject(view);
        so.FindProperty("monogram").objectReferenceValue = monogram;
        so.FindProperty("pricePill").objectReferenceValue = pill;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ==================================================================
    // Helpers
    // ==================================================================

    private static RectTransform Require(Transform parent, string name, string path)
    {
        var found = parent.Find(name) as RectTransform;
        if (found == null)
            throw new InvalidOperationException($"{path}: '{parent.name}/{name}' is missing — card structure changed.");
        return found;
    }

    private static RectTransform FindAnywhere(Transform root, string name, string path)
    {
        foreach (var candidate in root.GetComponentsInChildren<RectTransform>(true))
            if (candidate.name == name) return candidate;
        throw new InvalidOperationException($"{path}: no '{name}' node anywhere on the card.");
    }

    private static T GetOrAdd<T>(GameObject host) where T : Component =>
        host.GetComponent<T>() ?? host.AddComponent<T>();

    private static RectTransform GetOrCreate(Transform parent, string name)
    {
        var existing = parent.Find(name) as RectTransform;
        if (existing != null) return existing;

        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    // RoundedCorners lives in its OWN UPM assembly — Type.GetType(..., "Assembly-CSharp")
    // silently fails and the corners come out square.
    private static void SetCornerRadius(GameObject go, float radius)
    {
        if (cachedRoundedType == null)
        {
            const string fullName = "Nobi.UiRoundedCorners.ImageWithRoundedCorners";
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = asm.GetType(fullName);
                if (type != null) { cachedRoundedType = type; break; }
            }
        }
        if (cachedRoundedType == null)
        {
            Debug.LogWarning("[ItemCardB2RestyleBuilder] ImageWithRoundedCorners not found — corners stay square.");
            return;
        }

        var component = go.GetComponent(cachedRoundedType) ?? go.AddComponent(cachedRoundedType);
        // The field IS the visual radius: Refresh sends radius*2 and the shader
        // halves it again (SDFUtils.cginc CalcAlpha).
        cachedRoundedType.GetField("radius")?.SetValue(component, radius);
        cachedRoundedType.GetField("image", BindingFlags.Instance | BindingFlags.NonPublic)?
            .SetValue(component, go.GetComponent<MaskableGraphic>());
    }

    private static void BindTheme(GameObject go, ThemeRole role, bool preserveAlpha = true)
    {
        var graphic = go.GetComponent<Graphic>();
        if (graphic == null) return;

        var themed = go.GetComponent<ThemedColor>() ?? go.AddComponent<ThemedColor>();
        var so = new SerializedObject(themed);
        so.FindProperty("role").enumValueIndex = (int)role;
        so.FindProperty("target").objectReferenceValue = graphic;
        so.FindProperty("preserveAlpha").boolValue = preserveAlpha;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}

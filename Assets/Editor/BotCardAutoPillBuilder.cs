#if UNITY_EDITOR
using System;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Rebuilds Assets/Prefabs/Bot.prefab to the C2 card (sketch 006, locked
/// 2026-08-13): the activation footer (divider + «Бот работает» label + iOS
/// switch) and the status pill are gone; the card is a single 232u row —
/// avatar · name/subline · «Авто» capsule · chevron. The capsule is the
/// chats-header pill 1:1 (76u, ring/fill + lamp + label, painted at runtime by
/// ReplyModeToggleBinder.PaintChip); the subline hosts the business type and
/// the WA/TG white-glyph icons that Bot.cs tints per state.
///
/// Supersedes BotCardFooterBuilder (deleted with the footer). Idempotent:
/// parks BotDesc, deletes previous SubRow/AutoPill, rebuilds. Prefab-only —
/// cards are runtime-instantiated from Manager.BotPrefab, no scene involved.
/// The hidden Status TMP (Manager's data channel) is reparented to the card
/// root before its old StatusPill home is destroyed.
/// </summary>
public static class BotCardAutoPillBuilder
{
    private const string PrefabPath = "Assets/Prefabs/Bot.prefab";
    private const string WaGlyphPath = "Assets/Images/Icons/ChannelGlyph_WA.png";
    private const string TgGlyphPath = "Assets/Images/Icons/ChannelGlyph_TG.png";
    private const string HeaderFontGuid = "a2b0b38b6764047da9250bcff1b0f432"; // BotName / header semibold

    private const float CardHeight = 232f;
    private const float SublineIconSize = 38f;   // optically matches the 36u subline text

    // Light-theme literals for the authored prefab state — the runtime repaints
    // everything from Theme on init (PaintChip + RefreshCardSubline), so these
    // only matter for how the prefab looks in the Editor.
    private static readonly Color LSurface = Hex("#FFFFFF");
    private static readonly Color LBorder = Hex("#C4D6D7");
    private static readonly Color LInkSecondary = Hex("#4C6265");
    private static readonly Color LInkTertiary = Hex("#64797C");

    private static Type cachedRoundedType;

    [MenuItem("Tools/Bots Page/Build Bot Card Auto Pill (C2)")]
    public static void Build()
    {
        EnsureGlyphImportSettings(WaGlyphPath);
        EnsureGlyphImportSettings(TgGlyphPath);

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            BuildInto(root);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("[BotCardAutoPillBuilder] C2 card built and saved.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void BuildInto(GameObject root)
    {
        var bot = root.GetComponent<Bot>();
        if (bot == null || bot.BotDesc == null || bot.Status == null)
            throw new InvalidOperationException("Bot component (or BotDesc/Status refs) missing on prefab root.");

        Transform row = root.transform.Find("Row");
        Transform details = row != null ? row.Find("BotDetails") : null;
        if (row == null || details == null)
            throw new InvalidOperationException("Row/BotDetails not found — prefab shape changed.");

        // 1. The hidden Status data channel lived under StatusPill — park it on
        //    the root BEFORE the pill is destroyed (its LayoutElement already
        //    ignores layout, so it is inert anywhere).
        if (bot.Status.transform.parent != root.transform)
            bot.Status.transform.SetParent(root.transform, false);

        // 2. Retire the footer and the status pill.
        DestroyExisting(root.transform, "FooterRow");
        DestroyExisting(row, "StatusPill");

        // 3. Single-row card height.
        var rootRect = (RectTransform)root.transform;
        rootRect.sizeDelta = new Vector2(rootRect.sizeDelta.x, CardHeight);
        var rootLayout = root.GetComponent<LayoutElement>();
        if (rootLayout != null)
        {
            rootLayout.minHeight = CardHeight;
            rootLayout.preferredHeight = CardHeight;
        }

        BuildSubline(bot, details);
        BuildAutoPill(bot, row);
    }

    // ---- Subline: [BotDesc = business type] [WaIcon] [TgIcon] -------------

    private static void BuildSubline(Bot bot, Transform details)
    {
        Transform desc = bot.BotDesc.transform;

        // Idempotency: pull BotDesc out of a previous SubRow before rebuilding.
        Transform oldSubRow = details.Find("SubRow");
        if (oldSubRow != null)
        {
            if (desc.parent == oldSubRow) desc.SetParent(details, false);
            UnityEngine.Object.DestroyImmediate(oldSubRow.gameObject);
        }

        int sublineIndex = desc.GetSiblingIndex();

        var subRow = NewUiChild(details, "SubRow",
            typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        subRow.transform.SetSiblingIndex(sublineIndex);

        var subLe = subRow.GetComponent<LayoutElement>();
        subLe.minHeight = 52f;
        subLe.preferredHeight = 52f;

        var hlg = subRow.GetComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.spacing = 14f;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        desc.SetParent(subRow.transform, false);

        // The subline is STATE-driven (InkTertiary ↔ blinking connecting blue)
        // — Bot.cs owns the color now, a ThemedColor binding would repaint over it.
        var themed = desc.GetComponent<ThemedColor>();
        if (themed != null) UnityEngine.Object.DestroyImmediate(themed);

        Image waIcon = BuildChannelIcon(subRow.transform, "WaIcon", WaGlyphPath);
        Image tgIcon = BuildChannelIcon(subRow.transform, "TgIcon", TgGlyphPath);

        var so = new SerializedObject(bot);
        SetRef(so, "waChannelIcon", waIcon);
        SetRef(so, "tgChannelIcon", tgIcon);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Image BuildChannelIcon(Transform parent, string name, string spritePath)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
        if (sprite == null)
            throw new InvalidOperationException($"Channel glyph sprite missing at {spritePath} — " +
                                                "run Tools/render_channel_icons.js and reimport.");

        var go = NewUiChild(parent, name,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.color = LInkTertiary;   // runtime tints per state (brand / gray / hidden)

        var le = go.GetComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = SublineIconSize;
        le.minHeight = le.preferredHeight = SublineIconSize;
        return image;
    }

    // ---- The «Авто» capsule (chats-header pill 1:1) ----------------------

    private static void BuildAutoPill(Bot bot, Transform row)
    {
        DestroyExisting(row, "AutoPill");

        Transform arrow = row.Find("BotArrow");
        int pillIndex = arrow != null ? arrow.GetSiblingIndex() : row.childCount;

        var font = LoadHeaderFont();

        var root = NewUiChild(row, "AutoPill", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image), typeof(Button), typeof(LayoutElement));
        root.transform.SetSiblingIndex(pillIndex);

        var le = root.GetComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = 190f;
        le.minHeight = le.preferredHeight = 96f;

        var hitImage = root.GetComponent<Image>();
        hitImage.color = new Color(0f, 0f, 0f, 0f);   // invisible ≥96u hit target
        hitImage.raycastTarget = true;

        var button = root.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hitImage;

        // Visual pill — 76u inside the 96u hit rect (header metrics).
        var pill = NewUiChild(root.transform, "Pill", typeof(RectTransform));
        var pillRt = (RectTransform)pill.transform;
        pillRt.anchorMin = new Vector2(0f, 0.5f);
        pillRt.anchorMax = new Vector2(1f, 0.5f);
        pillRt.pivot = new Vector2(0.5f, 0.5f);
        pillRt.sizeDelta = new Vector2(0f, 76f);
        pillRt.anchoredPosition = Vector2.zero;

        Image ring = BuildStretchedImage(pill.transform, "Ring", LBorder, 38f, Vector2.zero);
        Image fill = BuildStretchedImage(pill.transform, "Fill", LSurface, 35f, new Vector2(3f, 3f));

        var content = NewUiChild(pill.transform, "Content",
            typeof(RectTransform), typeof(HorizontalLayoutGroup));
        Stretch((RectTransform)content.transform, Vector2.zero);
        var hlg = content.GetComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.spacing = 14f;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        (Image dotRing, Image dotCore) = BuildLamp(content.transform, 18f, 4f, LInkTertiary, LSurface);

        TextMeshProUGUI label = BuildTmp(content.transform, "Label", "Авто", 30f, FontStyles.Bold,
            LInkSecondary, font, new Vector2(96f, 40f));

        var so = new SerializedObject(bot);
        SetRef(so, "autoPillButton", button);
        SetRef(so, "autoPillRing", ring);
        SetRef(so, "autoPillFill", fill);
        SetRef(so, "autoPillLabel", label);
        SetRef(so, "autoPillDotRing", dotRing);
        SetRef(so, "autoPillDotCore", dotCore);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ---- Import settings for the white glyphs ---------------------------

    private static void EnsureGlyphImportSettings(string assetPath)
    {
        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
        {
            Debug.LogWarning($"[BotCardAutoPillBuilder] No TextureImporter at {assetPath} — " +
                             "asset not imported yet? Run Assets/Refresh first.");
            return;
        }

        bool dirty = importer.textureType != TextureImporterType.Sprite
                     || importer.spriteImportMode != SpriteImportMode.Single
                     || importer.mipmapEnabled
                     || !importer.alphaIsTransparency
                     || importer.maxTextureSize != 256;
        if (!dirty) return;

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.maxTextureSize = 256;
        importer.SaveAndReimport();
    }

    // ---- Shared helpers (ChatsTopBarRestyleBuilder patterns) -------------

    private static GameObject NewUiChild(Transform parent, string name, params Type[] components)
    {
        var go = new GameObject(name, components);
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void Stretch(RectTransform rt, Vector2 inset)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = inset;
        rt.offsetMax = -inset;
    }

    private static Image BuildStretchedImage(Transform parent, string name, Color color,
        float radius, Vector2 inset)
    {
        var go = NewUiChild(parent, name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        Stretch((RectTransform)go.transform, inset);
        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        AddRounded(go, radius);
        return image;
    }

    private static Image BuildCircle(Transform parent, string name, float size, Color color)
    {
        var go = NewUiChild(parent, name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        ((RectTransform)go.transform).sizeDelta = new Vector2(size, size);
        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        AddRounded(go, size / 2f);
        return image;
    }

    private static (Image ring, Image core) BuildLamp(Transform parent, float size, float coreInset,
        Color ringColor, Color coreColor)
    {
        Image ring = BuildCircle(parent, "Lamp", size, ringColor);
        var core = NewUiChild(ring.transform, "Core", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        Stretch((RectTransform)core.transform, new Vector2(coreInset, coreInset));
        var coreImage = core.GetComponent<Image>();
        coreImage.color = coreColor;
        coreImage.raycastTarget = false;
        AddRounded(core.gameObject, (size - coreInset * 2f) / 2f);
        return (ring, coreImage);
    }

    private static TextMeshProUGUI BuildTmp(Transform parent, string name, string text, float fontSize,
        FontStyles style, Color color, TMP_FontAsset font, Vector2 sizeDelta)
    {
        var go = NewUiChild(parent, name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        ((RectTransform)go.transform).sizeDelta = sizeDelta;

        var tmp = go.GetComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = color;
        tmp.characterSpacing = -2f;
        tmp.enableWordWrapping = false;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static TMP_FontAsset LoadHeaderFont()
    {
        string path = AssetDatabase.GUIDToAssetPath(HeaderFontGuid);
        return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
    }

    private static void DestroyExisting(Transform parent, string childName)
    {
        Transform existing = parent.Find(childName);
        if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
    }

    private static void SetRef(SerializedObject so, string property, UnityEngine.Object value)
    {
        SerializedProperty prop = so.FindProperty(property);
        if (prop != null) prop.objectReferenceValue = value;
        else Debug.LogWarning($"[BotCardAutoPillBuilder] Serialized property '{property}' not found.");
    }

    // RoundedCorners lives in its own UPM assembly — scan loaded assemblies
    // (project memory: Type.GetType against Assembly-CSharp silently fails).
    private static Type ResolveRoundedType()
    {
        if (cachedRoundedType != null) return cachedRoundedType;

        const string fullName = "Nobi.UiRoundedCorners.ImageWithRoundedCorners";
        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type = asm.GetType(fullName);
            if (type != null)
            {
                cachedRoundedType = type;
                return type;
            }
        }
        return null;
    }

    private static void AddRounded(GameObject go, float radius)
    {
        Type type = ResolveRoundedType();
        if (type == null)
        {
            Debug.LogWarning("[BotCardAutoPillBuilder] ImageWithRoundedCorners not found — corners square.");
            return;
        }
        Component rc = go.GetComponent(type) ?? go.AddComponent(type);
        type.GetField("radius")?.SetValue(rc, radius);
        type.GetField("image")?.SetValue(rc, go.GetComponent<Image>());
    }

    private static Color Hex(string hex) =>
        ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
}
#endif

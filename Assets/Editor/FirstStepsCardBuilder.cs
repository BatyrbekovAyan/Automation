#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using Nobi.UiRoundedCorners;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the «Первые шаги» first-run checklist card (ONB-04) under BotsPage, above
/// the bots list, and stamps the <see cref="FirstStepsCard"/> [SerializeField] refs.
///
/// Structure produced (child of BotsPage, sibling-ordered just before EmptyState so it
/// draws over the list top while the empty-state overlay stays on top):
///   FirstStepsCard [Image + RoundedCorners + VerticalLayoutGroup + ContentSizeFitter + FirstStepsCard]
///     ├─ Head (Title «Первые шаги» + Progress «N из 4»)
///     ├─ ProgressBar (Track → Fill)
///     ├─ Rows (VerticalLayoutGroup) → Row0..Row3
///     │     each: [transparent hit Image + Button + CanvasGroup + HorizontalLayoutGroup]
///     │       ├─ CheckCircle (Image, rounded) → CheckMark (Image, white tick)
///     │       ├─ Label (TMP)
///     │       └─ Chevron (Image, chevron-right)
///     └─ Hint (row-4 guidance caption)
///
/// Idempotent delete-and-rebuild. All sizes in 1080×1920 canvas reference units.
/// Clones the NavRestructureBuilder / DashboardPageBuilder helper idioms verbatim
/// (Image+sprite icons, deferred RoundedCorners bake, SerializedObject field stamping).
/// Save the scene after running (the headless entry saves automatically).
///
/// CRITICAL: FirstStepsCard resolves each row's parts by transform.Find on EXACT child
/// names — CheckCircle / CheckCircle/CheckMark / Label / Chevron. Do not rename them.
/// </summary>
public static class FirstStepsCardBuilder
{
    // ── Design tokens ───────────────────────────────────────────────────────────
    private const float HeaderHeight = 300f;   // BotsPage header (matches scene)
    private const float TopGap = 28f;           // card top, below the header
    private const float Gutter = 44f;
    private const float CardRadius = 40f;
    private const float RowHeight = 84f;
    private const float CircleSize = 48f;
    private const float CheckMarkSize = 30f;
    private const float ChevronSize = 26f;
    private const float BarHeight = 20f;

    private static readonly Color Card = Color.white;
    private static readonly Color Ink = Hex("#1A1A2E");
    private static readonly Color Muted = Hex("#65676B");
    private static readonly Color Primary = Hex("#1B7CEB");
    private static readonly Color TrackBg = Hex("#EDEFF3");
    private static readonly Color TodoCircle = Hex("#E1E5EC");
    private static readonly Color CheckWhite = Color.white;

    // Fonts by GUID (default font's weight table is empty — always assign explicitly).
    private const string RegularGuid = "e0cdfe2d6a51446bcba7d2df147e2415";
    private const string SemiboldGuid = "a2b0b38b6764047da9250bcff1b0f432";
    private const string BoldGuid = "1cd715823fef34be4a3d3f3c5572594c";

    private const string ChevronRightPath = "Assets/Images/Chat/chevron-right.png";
    private const string CheckIconPath = "Assets/Images/Icons/[CITYPNG.COM]HD Green Check True Tick Mark Icon Sign PNG - 3000x3000.png";

    // Copy deck (spec §Screen specs — owner-approved). Row 1's channel label is filled
    // at runtime by FirstStepsCard; the builder seeds the WhatsApp default.
    private const string CardTitleText = "Первые шаги";
    private const string ProgressSeedText = "0 из 4";
    private static readonly string[] RowSeedLabels =
    {
        "Создать бота",
        "Подключить WhatsApp",
        "Загрузить прайс-лист",
        "Получить первый ответ бота",
    };
    private const string HintText = "Попросите знакомого написать вам — и посмотрите, как бот ответит";

    private static TMP_FontAsset _regular, _semibold, _bold;
    private static Sprite _chevron, _check;
    private static readonly List<Component> _roundedToRefresh = new List<Component>();

    // ── Entry points ─────────────────────────────────────────────────────────────

    [MenuItem("Tools/Onboarding/Build Checklist Card")]
    public static void Build()
    {
        BuildInternal();
        EditorSceneManager.MarkAllScenesDirty();
        Debug.Log("[FirstStepsCardBuilder] Built «Первые шаги» checklist card — SAVE THE SCENE (Cmd+S).");
    }

    // Headless entry (Editor closed):
    //   Unity -batchmode -nographics -projectPath . \
    //         -executeMethod FirstStepsCardBuilder.BuildHeadless -quit
    public static void BuildHeadless()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        BuildInternal();
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[FirstStepsCardBuilder] Headless build + save complete");   // EXACT sentinel
    }

    // ── Main build ───────────────────────────────────────────────────────────────

    private static void BuildInternal()
    {
        AssetDatabase.Refresh();
        EnsureIconImportSettings();
        LoadAssets();
        _roundedToRefresh.Clear();

        var botsPage = Object.FindFirstObjectByType<BotsPage>(FindObjectsInactive.Include);
        if (botsPage == null)
            throw new System.InvalidOperationException(
                "[FirstStepsCardBuilder] BotsPage not found — is Main.unity open?");

        // Locate the existing bots list container (BotsPage/…/BotsParent).
        var botsParent = FindDeepChild(botsPage.transform, "BotsParent");
        if (botsParent == null)
            Debug.LogWarning("[FirstStepsCardBuilder] 'BotsParent' not found under BotsPage — FirstStepsCard.botsParent left unstamped.");

        // Temporarily activate the BotsPage ancestor chain so layout resolves and the
        // rounded-corner bake sees real rects (edit-mode SetActive does NOT run Awake).
        var restore = ForceActivateChain(botsPage.gameObject);
        try
        {
            DestroyAllByName(botsPage.transform, "FirstStepsCard");   // idempotent

            var card = BuildCard(botsPage.gameObject, out var titleLabel, out var progressLabel,
                out var progressFill, out var rowsRoot, out var hintLabel);

            // Draw order: card renders over the list top; EmptyState stays the topmost
            // overlay (it must cover everything when zero bots exist).
            var emptyState = botsPage.transform.Find("EmptyState");
            if (emptyState != null) emptyState.SetAsLastSibling();

            var component = card.GetComponent<FirstStepsCard>() ?? card.AddComponent<FirstStepsCard>();
            var so = new SerializedObject(component);
            StampField(so, "titleLabel", titleLabel);
            StampField(so, "progressLabel", progressLabel);
            StampField(so, "progressFill", progressFill);
            StampField(so, "rowsRoot", rowsRoot);
            StampField(so, "hintLabel", hintLabel);
            if (botsParent != null) StampField(so, "botsParent", botsParent);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(component);

            // Radius bake needs sized rects.
            Canvas.ForceUpdateCanvases();
            foreach (var rounded in _roundedToRefresh) RefreshRounded(rounded);
        }
        finally
        {
            RestoreChain(restore);
        }
    }

    // ── Card tree ─────────────────────────────────────────────────────────────────

    private static GameObject BuildCard(GameObject host, out TextMeshProUGUI titleLabel,
        out TextMeshProUGUI progressLabel, out Image progressFill, out Transform rowsRoot,
        out TextMeshProUGUI hintLabel)
    {
        var card = NewChild(host, "FirstStepsCard", out var cardRt);
        // Top banner, below the header, full width minus gutters. Height is content-driven
        // (ContentSizeFitter) so the card never clips.
        SetAnchors(cardRt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        cardRt.offsetMin = new Vector2(Gutter, 0f);
        cardRt.offsetMax = new Vector2(-Gutter, 0f);
        cardRt.anchoredPosition = new Vector2(0f, -(HeaderHeight + TopGap));

        var bg = card.AddComponent<Image>();
        bg.color = Card;
        bg.raycastTarget = true;
        AddRounded(card, CardRadius);

        var vlg = card.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(36, 36, 32, 32);
        vlg.spacing = 16f;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        card.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        BuildHead(card, out titleLabel, out progressLabel);
        BuildProgressBar(card, out progressFill);
        BuildRows(card, out rowsRoot);
        BuildHint(card, out hintLabel);

        return card;
    }

    private static void BuildHead(GameObject card, out TextMeshProUGUI titleLabel, out TextMeshProUGUI progressLabel)
    {
        var head = NewChild(card, "Head", out _);
        head.AddComponent<LayoutElement>().preferredHeight = 60f;
        var hlg = head.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;

        var titleGo = NewChild(head, "Title", out _);
        titleGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
        titleLabel = AddText(titleGo, CardTitleText, 44f, _bold, Ink);

        var progressGo = NewChild(head, "Progress", out _);
        progressLabel = AddText(progressGo, ProgressSeedText, 32f, _semibold, Primary);
        progressLabel.alignment = TextAlignmentOptions.MidlineRight;
    }

    private static void BuildProgressBar(GameObject card, out Image progressFill)
    {
        var track = NewChild(card, "ProgressBar", out _);
        track.AddComponent<LayoutElement>().preferredHeight = BarHeight;
        var trackImg = track.AddComponent<Image>();
        trackImg.color = TrackBg;
        trackImg.raycastTarget = false;
        AddRounded(track, BarHeight / 2f);

        var fillGo = NewChild(track, "Fill", out var fillRt);
        SetAnchors(fillRt, new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, 0.5f));
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;
        progressFill = fillGo.AddComponent<Image>();
        progressFill.color = Primary;
        progressFill.raycastTarget = false;
        AddRounded(fillGo, BarHeight / 2f);
    }

    private static void BuildRows(GameObject card, out Transform rowsRoot)
    {
        var rows = NewChild(card, "Rows", out _);
        var vlg = rows.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(0, 0, 8, 8);
        vlg.spacing = 6f;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        rowsRoot = rows.transform;

        for (int i = 0; i < RowSeedLabels.Length; i++)
            BuildRow(rows, i, RowSeedLabels[i]);
    }

    private static void BuildRow(GameObject rows, int index, string label)
    {
        var row = NewChild(rows, $"Row{index}", out _);
        row.AddComponent<LayoutElement>().preferredHeight = RowHeight;
        var hit = row.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);   // transparent hit area for the row Button
        hit.raycastTarget = true;
        row.AddComponent<Button>().targetGraphic = hit;
        row.AddComponent<CanvasGroup>();          // driven by the cascade fade

        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 22f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;

        // Check circle + white tick (tick hidden by default; FirstStepsCard toggles it).
        var circleGo = NewChild(row, "CheckCircle", out _);
        SetPreferredSize(circleGo, CircleSize, CircleSize);
        var circleImg = circleGo.AddComponent<Image>();
        circleImg.color = TodoCircle;
        circleImg.raycastTarget = false;
        AddRounded(circleGo, CircleSize / 2f);
        var checkGo = NewChild(circleGo, "CheckMark", out var checkRt);
        SetAnchors(checkRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        checkRt.sizeDelta = new Vector2(CheckMarkSize, CheckMarkSize);
        AddIconImage(checkGo, _check, CheckWhite);
        checkGo.SetActive(false);

        // Label — takes the remaining width.
        var labelGo = NewChild(row, "Label", out _);
        labelGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
        var labelTmp = AddText(labelGo, label, 38f, _regular, Ink);
        labelTmp.textWrappingMode = TextWrappingModes.NoWrap;
        labelTmp.overflowMode = TextOverflowModes.Ellipsis;

        // Chevron — tap affordance (FirstStepsCard hides it on completed rows).
        var chevGo = NewChild(row, "Chevron", out _);
        SetPreferredSize(chevGo, ChevronSize, ChevronSize);
        AddIconImage(chevGo, _chevron, Hex("#C6CBD3"));
    }

    private static void BuildHint(GameObject card, out TextMeshProUGUI hintLabel)
    {
        var hintGo = NewChild(card, "Hint", out _);
        hintLabel = AddText(hintGo, HintText, 30f, _regular, Muted);
        hintLabel.textWrappingMode = TextWrappingModes.Normal;
        hintLabel.lineSpacing = 6f;
        hintLabel.margin = new Vector4(0f, 8f, 0f, 0f);
    }

    // ── Temporary activation of the host ancestor chain ─────────────────────────

    private static List<GameObject> ForceActivateChain(GameObject leaf)
    {
        var toRestore = new List<GameObject>();
        for (Transform t = leaf.transform; t != null; t = t.parent)
        {
            if (!t.gameObject.activeSelf)
            {
                toRestore.Add(t.gameObject);
                t.gameObject.SetActive(true);
            }
        }
        return toRestore;
    }

    private static void RestoreChain(List<GameObject> toRestore)
    {
        // Restore leaf-last so parents deactivate cleanly.
        for (int i = toRestore.Count - 1; i >= 0; i--)
            if (toRestore[i] != null) toRestore[i].SetActive(false);
    }

    // ── Field stamping ──────────────────────────────────────────────────────────

    private static void StampField(SerializedObject so, string field, Object value)
    {
        var prop = so.FindProperty(field);
        if (prop == null)
        {
            Debug.LogWarning($"[FirstStepsCardBuilder] FirstStepsCard.{field} not found — skipped.");
            return;
        }
        prop.objectReferenceValue = value;
    }

    // ── Asset loading / import settings ─────────────────────────────────────────

    private static void EnsureIconImportSettings()
    {
        foreach (string path in new[] { ChevronRightPath, CheckIconPath })
        {
            if (!File.Exists(path)) continue;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;

            bool dirty = importer.textureType != TextureImporterType.Sprite
                         || importer.spriteImportMode != SpriteImportMode.Single
                         || importer.mipmapEnabled
                         || importer.filterMode != FilterMode.Bilinear
                         || importer.wrapMode != TextureWrapMode.Clamp
                         || !importer.alphaIsTransparency;
            if (!dirty) continue;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }
    }

    private static void LoadAssets()
    {
        _regular = LoadFont(RegularGuid);
        _semibold = LoadFont(SemiboldGuid);
        _bold = LoadFont(BoldGuid);
        _chevron = LoadSprite(ChevronRightPath);
        _check = LoadSprite(CheckIconPath);
    }

    private static TMP_FontAsset LoadFont(string guid)
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        var font = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (font == null) Debug.LogWarning($"[FirstStepsCardBuilder] Font missing for GUID {guid}");
        return font;
    }

    private static Sprite LoadSprite(string path)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null) Debug.LogWarning($"[FirstStepsCardBuilder] Sprite missing: {path}");
        return sprite;
    }

    // ── Low-level helpers (verbatim NavRestructureBuilder idiom) ────────────────

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color color);
        return color;
    }

    private static GameObject NewChild(GameObject parent, string name, out RectTransform rt)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        rt = go.GetComponent<RectTransform>();
        return go;
    }

    private static void SetAnchors(RectTransform rt, Vector2 min, Vector2 max, Vector2 pivot)
    {
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.pivot = pivot;
    }

    private static void SetPreferredSize(GameObject go, float width, float height)
    {
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = width;
        le.preferredHeight = height;
        le.minWidth = width;
        le.minHeight = height;
        le.flexibleWidth = 0f;
        le.flexibleHeight = 0f;
    }

    private static TextMeshProUGUI AddText(GameObject go, string text, float size, TMP_FontAsset font, Color color)
    {
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        if (font != null) tmp.font = font;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void AddIconImage(GameObject go, Sprite sprite, Color tint)
    {
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.color = tint;
        img.preserveAspect = true;
        img.raycastTarget = false;
    }

    private static void AddRounded(GameObject go, float radius)
    {
        var rounded = go.AddComponent<ImageWithRoundedCorners>();
        rounded.radius = radius;
        _roundedToRefresh.Add(rounded);
    }

    private static void RefreshRounded(Component rounded)
    {
        if (rounded == null) return;
        switch (rounded)
        {
            case ImageWithRoundedCorners simple:
                simple.Validate();
                simple.Refresh();
                break;
            case ImageWithIndependentRoundedCorners independent:
                independent.Validate();
                independent.Refresh();
                break;
        }
    }

    private static void DestroyAllByName(Transform root, string name)
    {
        var all = root.GetComponentsInChildren<Transform>(true);
        foreach (var t in all)
        {
            if (t != null && t != root && t.name == name)
                Object.DestroyImmediate(t.gameObject);
        }
    }

    private static Transform FindDeepChild(Transform root, string name)
    {
        var all = root.GetComponentsInChildren<Transform>(true);
        foreach (var t in all)
        {
            if (t != null && t != root && t.name == name)
                return t;
        }
        return null;
    }
}
#endif

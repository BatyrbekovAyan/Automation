#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public static class BotSwitcherSheetBuilder
{
    private const string SheetName = "Sheet_BotSwitcher";
    private const string RowName = "BotSwitcherRow";

    [MenuItem("Tools/Bot Switcher/Build Sheet")]
    public static void Build()
    {
        Canvas canvas = GameObject.FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[BotSwitcherSheetBuilder] No Canvas found in scene. Open the Main scene first.");
            return;
        }

        // Remove existing instance
        Transform existing = canvas.transform.Find(SheetName);
        if (existing != null)
        {
            Object.DestroyImmediate(existing.gameObject);
        }

        // Root sheet object
        GameObject sheet = new GameObject(SheetName, typeof(RectTransform));
        sheet.transform.SetParent(canvas.transform, false);
        RectTransform sheetRT = sheet.GetComponent<RectTransform>();
        sheetRT.anchorMin = Vector2.zero;
        sheetRT.anchorMax = Vector2.one;
        sheetRT.offsetMin = Vector2.zero;
        sheetRT.offsetMax = Vector2.zero;

        BotSwitcherSheet controller = sheet.AddComponent<BotSwitcherSheet>();

        // Backdrop
        GameObject backdrop = new GameObject("Backdrop", typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(Button));
        backdrop.transform.SetParent(sheet.transform, false);
        RectTransform bdRT = backdrop.GetComponent<RectTransform>();
        bdRT.anchorMin = Vector2.zero;
        bdRT.anchorMax = Vector2.one;
        bdRT.offsetMin = Vector2.zero;
        bdRT.offsetMax = Vector2.zero;
        backdrop.GetComponent<Image>().color = new Color(0f, 0f, 0f, 1f); // alpha controlled by CanvasGroup
        var backdropCanvasGroup = backdrop.GetComponent<CanvasGroup>();
        backdropCanvasGroup.alpha = 0f;
        backdropCanvasGroup.blocksRaycasts = false;
        var backdropButton = backdrop.GetComponent<Button>();

        // Sheet panel — bottom-anchored (pivot Y = 0, anchor Y = 0) per BotSwitcherSheet contract.
        GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        panel.transform.SetParent(sheet.transform, false);
        RectTransform panelRT = panel.GetComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0f, 0f);
        panelRT.anchorMax = new Vector2(1f, 0f);
        panelRT.pivot = new Vector2(0.5f, 0f);
        panelRT.anchoredPosition = Vector2.zero;
        panelRT.sizeDelta = new Vector2(0f, 720f); // initial; ContentSizeFitter will refine
        panel.GetComponent<Image>().color = Color.white;

        var panelLayout = panel.GetComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(0, 0, 16, 16);
        panelLayout.spacing = 0;
        panelLayout.childForceExpandWidth = true;
        panelLayout.childForceExpandHeight = false;

        var panelFitter = panel.AddComponent<ContentSizeFitter>();
        panelFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Sheet header label
        GameObject header = new GameObject("Header", typeof(RectTransform), typeof(TextMeshProUGUI));
        header.transform.SetParent(panel.transform, false);
        var headerText = header.GetComponent<TextMeshProUGUI>();
        headerText.text = "Select bot";
        headerText.fontSize = 16;
        headerText.fontStyle = FontStyles.Bold;
        headerText.color = new Color(0.1f, 0.1f, 0.1f);
        headerText.alignment = TextAlignmentOptions.Center;
        var headerLE = header.AddComponent<LayoutElement>();
        headerLE.minHeight = 56;
        headerLE.preferredHeight = 56;

        // Row container (scrollable)
        GameObject scroll = new GameObject("RowScroll", typeof(RectTransform), typeof(ScrollRect), typeof(Image));
        scroll.transform.SetParent(panel.transform, false);
        var scrollImage = scroll.GetComponent<Image>();
        scrollImage.color = new Color(0, 0, 0, 0); // transparent
        var scrollLE = scroll.AddComponent<LayoutElement>();
        scrollLE.minHeight = 320;
        scrollLE.preferredHeight = 480;

        var scrollRect = scroll.GetComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;

        GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(scroll.transform, false);
        var viewportRT = viewport.GetComponent<RectTransform>();
        viewportRT.anchorMin = Vector2.zero;
        viewportRT.anchorMax = Vector2.one;
        viewportRT.offsetMin = Vector2.zero;
        viewportRT.offsetMax = Vector2.zero;
        viewport.GetComponent<Image>().color = new Color(1, 1, 1, 0.01f);
        viewport.GetComponent<Mask>().showMaskGraphic = false;

        GameObject content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        var contentRT = content.GetComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0, 1);
        contentRT.anchorMax = new Vector2(1, 1);
        contentRT.pivot = new Vector2(0.5f, 1);
        contentRT.offsetMin = Vector2.zero;
        contentRT.offsetMax = Vector2.zero;
        var contentLayout = content.GetComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 4;
        contentLayout.padding = new RectOffset(4, 4, 4, 4);
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        var contentFitter = content.GetComponent<ContentSizeFitter>();
        contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = viewportRT;
        scrollRect.content = contentRT;

        // Row prefab
        GameObject row = BuildRowPrefab();

        // Wire controller serialized fields via SerializedObject
        var so = new SerializedObject(controller);
        so.FindProperty("backdropGroup").objectReferenceValue = backdropCanvasGroup;
        so.FindProperty("backdropButton").objectReferenceValue = backdropButton;
        so.FindProperty("sheetPanel").objectReferenceValue = panelRT;
        so.FindProperty("rowContainer").objectReferenceValue = contentRT;
        so.FindProperty("rowPrefab").objectReferenceValue = row.GetComponent<BotSwitcherRowView>();
        so.ApplyModifiedPropertiesWithoutUndo();

        sheet.SetActive(false);
        Debug.Log($"[BotSwitcherSheetBuilder] Built {SheetName} under {canvas.name}.");
        Selection.activeGameObject = sheet;
    }

    private static GameObject BuildRowPrefab()
    {
        // The row lives as a hidden template under the canvas; BotSwitcherSheet
        // instantiates it at runtime. We attach it under a special holder so it
        // does not render in the live scene.
        const string HolderName = "BotSwitcherRowPrefabHolder";
        Canvas canvas = GameObject.FindObjectOfType<Canvas>();
        Transform holder = canvas.transform.Find(HolderName);
        if (holder != null) Object.DestroyImmediate(holder.gameObject);

        GameObject holderGO = new GameObject(HolderName, typeof(RectTransform));
        holderGO.SetActive(false);
        holderGO.transform.SetParent(canvas.transform, false);

        GameObject row = new GameObject(RowName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(holderGO.transform, false);
        RectTransform rowRT = row.GetComponent<RectTransform>();
        rowRT.sizeDelta = new Vector2(0, 64);
        var rowImage = row.GetComponent<Image>();
        rowImage.color = new Color(1, 1, 1, 0); // transparent base; clicks still hit
        var rowButton = row.GetComponent<Button>();
        var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
        rowLayout.padding = new RectOffset(16, 16, 8, 8);
        rowLayout.spacing = 12;
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;

        // Selected background (full-row)
        GameObject selBg = new GameObject("SelectedBackground", typeof(RectTransform), typeof(Image));
        selBg.transform.SetParent(row.transform, false);
        var selBgRT = selBg.GetComponent<RectTransform>();
        selBgRT.anchorMin = Vector2.zero;
        selBgRT.anchorMax = Vector2.one;
        selBgRT.offsetMin = new Vector2(4, 0);
        selBgRT.offsetMax = new Vector2(-4, 0);
        var selBgImage = selBg.GetComponent<Image>();
        selBgImage.color = new Color(0.13f, 0.78f, 0.42f, 0.10f); // ~10% accent tint
        selBg.transform.SetAsFirstSibling();
        selBg.SetActive(false);

        // Selected accent bar (left edge)
        GameObject accentBar = new GameObject("SelectedAccentBar", typeof(RectTransform), typeof(Image));
        accentBar.transform.SetParent(row.transform, false);
        var barRT = accentBar.GetComponent<RectTransform>();
        barRT.anchorMin = new Vector2(0, 0);
        barRT.anchorMax = new Vector2(0, 1);
        barRT.pivot = new Vector2(0, 0.5f);
        barRT.sizeDelta = new Vector2(2, 0);
        barRT.anchoredPosition = new Vector2(4, 0);
        accentBar.GetComponent<Image>().color = new Color(0.13f, 0.78f, 0.42f);
        accentBar.SetActive(false);

        // Avatar
        GameObject avatar = new GameObject("Avatar", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        avatar.transform.SetParent(row.transform, false);
        avatar.GetComponent<RectTransform>().sizeDelta = new Vector2(40, 40);
        var avLE = avatar.GetComponent<LayoutElement>();
        avLE.preferredWidth = 40;
        avLE.preferredHeight = 40;
        var avImage = avatar.GetComponent<Image>();
        avImage.color = new Color(0.85f, 0.85f, 0.85f);

        // Stack: name + sub-line
        GameObject stack = new GameObject("Stack", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        stack.transform.SetParent(row.transform, false);
        var stackLayout = stack.GetComponent<VerticalLayoutGroup>();
        stackLayout.spacing = 2;
        stackLayout.childAlignment = TextAnchor.MiddleLeft;
        stackLayout.childForceExpandWidth = true;
        stackLayout.childForceExpandHeight = false;
        var stackLE = stack.GetComponent<LayoutElement>();
        stackLE.flexibleWidth = 1;

        GameObject nameGO = new GameObject("Name", typeof(RectTransform), typeof(TextMeshProUGUI));
        nameGO.transform.SetParent(stack.transform, false);
        var nameText = nameGO.GetComponent<TextMeshProUGUI>();
        nameText.text = "Bot";
        nameText.fontSize = 16;
        nameText.color = new Color(0.1f, 0.1f, 0.1f);

        GameObject subGO = new GameObject("SubLine", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        subGO.transform.SetParent(stack.transform, false);
        var subLayout = subGO.GetComponent<HorizontalLayoutGroup>();
        subLayout.spacing = 6;
        subLayout.childAlignment = TextAnchor.MiddleLeft;
        subLayout.childForceExpandWidth = false;
        subLayout.childForceExpandHeight = false;

        GameObject statusDotGO = new GameObject("StatusDot", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        statusDotGO.transform.SetParent(subGO.transform, false);
        statusDotGO.GetComponent<RectTransform>().sizeDelta = new Vector2(8, 8);
        var dotLE = statusDotGO.GetComponent<LayoutElement>();
        dotLE.preferredWidth = 8;
        dotLE.preferredHeight = 8;
        var dotImage = statusDotGO.GetComponent<Image>();
        dotImage.color = new Color(0.6f, 0.6f, 0.6f);

        GameObject subTextGO = new GameObject("SubText", typeof(RectTransform), typeof(TextMeshProUGUI));
        subTextGO.transform.SetParent(subGO.transform, false);
        var subText = subTextGO.GetComponent<TextMeshProUGUI>();
        subText.text = "WhatsApp not connected";
        subText.fontSize = 12;
        subText.color = new Color(0.45f, 0.45f, 0.45f);

        // Wire BotSwitcherRowView serialized fields
        var rowView = row.AddComponent<BotSwitcherRowView>();
        var so = new SerializedObject(rowView);
        so.FindProperty("avatarImage").objectReferenceValue = avImage;
        so.FindProperty("nameLabel").objectReferenceValue = nameText;
        so.FindProperty("subLineLabel").objectReferenceValue = subText;
        so.FindProperty("statusDot").objectReferenceValue = dotImage;
        so.FindProperty("selectedBackground").objectReferenceValue = selBgImage;
        so.FindProperty("selectedAccentBar").objectReferenceValue = accentBar.GetComponent<Image>();
        so.FindProperty("rowButton").objectReferenceValue = rowButton;
        so.ApplyModifiedPropertiesWithoutUndo();

        return row;
    }
}
#endif

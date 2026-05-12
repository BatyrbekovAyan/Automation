using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class ChatsSearchBarBuilder
{
    private const string SearchBarName = "ChatsSearchBar";

    [MenuItem("Tools/UI/Build Chats Search Bar")]
    public static void Build()
    {
        var selection = Selection.activeGameObject;
        if (selection == null || selection.name != "ChatsPanel")
        {
            Debug.LogError(
                "ChatsSearchBarBuilder: select the ChatsPanel GameObject in the Hierarchy first.");
            return;
        }

        var listView = selection.GetComponent<ChatListView>();
        if (listView == null)
        {
            Debug.LogError(
                "ChatsSearchBarBuilder: selected GameObject has no ChatListView component.");
            return;
        }

        var content = listView.content;
        if (content == null)
        {
            Debug.LogError(
                "ChatsSearchBarBuilder: ChatListView.content is unassigned.");
            return;
        }

        if (content.GetComponent<VerticalLayoutGroup>() == null)
        {
            Debug.LogError(
                "ChatsSearchBarBuilder: ChatListView.content has no VerticalLayoutGroup.");
            return;
        }

        var existing = content.Find(SearchBarName);
        if (existing != null)
        {
            Debug.Log(
                $"ChatsSearchBarBuilder: '{SearchBarName}' already exists under "
                + $"{content.name}. Aborting — delete it first if you want to rebuild.");
            Selection.activeGameObject = existing.gameObject;
            return;
        }

        // Register the operation as a single undo step.
        Undo.SetCurrentGroupName("Build Chats Search Bar");
        int undoGroup = Undo.GetCurrentGroup();

        var row = CreateRow(content);
        var pill = CreatePill(row.transform);
        var magnifier = CreateMagnifier(pill.transform);
        var input = CreateInput(pill.transform);
        var clearButton = CreateClearButton(pill.transform, out var clearIcon);

        var bar = row.AddComponent<ChatSearchBar>();
        var so = new SerializedObject(bar);
        so.FindProperty("input").objectReferenceValue = input;
        so.FindProperty("clearButton").objectReferenceValue = clearButton;
        so.FindProperty("clearIcon").objectReferenceValue = clearIcon;
        so.ApplyModifiedPropertiesWithoutUndo();

        row.transform.SetAsFirstSibling();

        Undo.CollapseUndoOperations(undoGroup);
        Selection.activeGameObject = row;
        EditorUtility.SetDirty(content);
        Debug.Log("ChatsSearchBarBuilder: built ChatsSearchBar under " + content.name);
    }

    private static GameObject CreateRow(Transform parent)
    {
        var go = new GameObject(SearchBarName, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Create ChatsSearchBar");
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0, 112);

        var le = go.AddComponent<LayoutElement>();
        le.minHeight = 112;
        le.preferredHeight = 112;

        return go;
    }

    private static GameObject CreatePill(Transform parent)
    {
        var go = new GameObject("Pill",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(HorizontalLayoutGroup));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0, 0.5f);
        rt.anchorMax = new Vector2(1, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(-32, 80); // full width − 32 (16 each side), 80 tall

        var img = go.GetComponent<Image>();
        img.color = HexColor("#EFEFF0");
        img.raycastTarget = true;

        // Project ships the RoundedCorners package. The component name lives in
        // the Nobi.UiRoundedCorners namespace; we look it up by type name to
        // avoid a hard compile dependency in this editor script.
        var roundedType = System.Type.GetType("Nobi.UiRoundedCorners.ImageWithRoundedCorners, Assembly-CSharp")
                         ?? System.Type.GetType("Nobi.UiRoundedCorners.ImageWithRoundedCorners");
        if (roundedType != null)
        {
            var rounded = go.AddComponent(roundedType);
            var radiusProp = roundedType.GetField("radius");
            if (radiusProp != null) radiusProp.SetValue(rounded, 24f);
        }
        else
        {
            Debug.LogWarning(
                "ChatsSearchBarBuilder: ImageWithRoundedCorners type not found — "
                + "pill will render as a hard rectangle. Add the rounded-corner "
                + "component manually if needed.");
        }

        var hlg = go.GetComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(20, 16, 0, 0);
        hlg.spacing = 16;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;

        return go;
    }

    private static GameObject CreateMagnifier(Transform parent)
    {
        var go = new GameObject("Magnifier",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(LayoutElement));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);

        var img = go.GetComponent<Image>();
        img.color = HexColor("#8E8E93");
        img.raycastTarget = false;
        // Built-in Unity sprite used as a placeholder glyph. Replace with the
        // project's magnifier sprite in the Inspector after build if a custom
        // asset exists.
        var fallback = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        if (fallback != null) img.sprite = fallback;

        var le = go.GetComponent<LayoutElement>();
        le.minWidth = 32;
        le.preferredWidth = 32;
        le.minHeight = 32;
        le.preferredHeight = 32;

        return go;
    }

    private static TMP_InputField CreateInput(Transform parent)
    {
        var go = new GameObject("Input",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(TMP_InputField), typeof(LayoutElement));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);

        var img = go.GetComponent<Image>();
        img.color = new Color(1, 1, 1, 0); // transparent background, pill provides fill
        img.raycastTarget = true;

        var le = go.GetComponent<LayoutElement>();
        le.flexibleWidth = 1;
        le.minHeight = 60;
        le.preferredHeight = 60;

        // Text viewport
        var viewport = new GameObject("Text Area",
            typeof(RectTransform), typeof(RectMask2D));
        viewport.layer = LayerMask.NameToLayer("UI");
        viewport.transform.SetParent(go.transform, false);
        var viewportRt = (RectTransform)viewport.transform;
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.offsetMin = Vector2.zero;
        viewportRt.offsetMax = Vector2.zero;

        // Placeholder
        var placeholder = new GameObject("Placeholder",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        placeholder.layer = LayerMask.NameToLayer("UI");
        placeholder.transform.SetParent(viewport.transform, false);
        var placeholderRt = (RectTransform)placeholder.transform;
        placeholderRt.anchorMin = Vector2.zero;
        placeholderRt.anchorMax = Vector2.one;
        placeholderRt.offsetMin = Vector2.zero;
        placeholderRt.offsetMax = Vector2.zero;
        var placeholderTmp = placeholder.GetComponent<TextMeshProUGUI>();
        placeholderTmp.text = "Search";
        placeholderTmp.fontSize = 30;
        placeholderTmp.color = HexColor("#8E8E93");
        placeholderTmp.alignment = TextAlignmentOptions.MidlineLeft;
        placeholderTmp.raycastTarget = false;
        placeholderTmp.enableWordWrapping = false;
        placeholderTmp.overflowMode = TextOverflowModes.Ellipsis;

        // Active text
        var text = new GameObject("Text",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        text.layer = LayerMask.NameToLayer("UI");
        text.transform.SetParent(viewport.transform, false);
        var textRt = (RectTransform)text.transform;
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;
        var textTmp = text.GetComponent<TextMeshProUGUI>();
        textTmp.text = "";
        textTmp.fontSize = 30;
        textTmp.color = HexColor("#111111");
        textTmp.alignment = TextAlignmentOptions.MidlineLeft;
        textTmp.raycastTarget = false;
        textTmp.enableWordWrapping = false;
        textTmp.overflowMode = TextOverflowModes.Ellipsis;

        var input = go.GetComponent<TMP_InputField>();
        input.textViewport = viewportRt;
        input.textComponent = textTmp;
        input.placeholder = placeholderTmp;
        input.fontAsset = textTmp.font;
        input.caretWidth = 2;
        input.customCaretColor = true;
        input.caretColor = HexColor("#00A884");
        input.selectionColor = new Color(0, 0.66f, 0.52f, 0.25f);
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.contentType = TMP_InputField.ContentType.Standard;
        input.text = "";

        return input;
    }

    private static Button CreateClearButton(Transform parent, out GameObject clearIcon)
    {
        var go = new GameObject("ClearButton",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(Button), typeof(LayoutElement));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);

        var img = go.GetComponent<Image>();
        img.color = HexColor("#C7C7CC");
        img.raycastTarget = true;
        var fallback = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        if (fallback != null) img.sprite = fallback;
        img.type = Image.Type.Simple;
        img.preserveAspect = true;

        var le = go.GetComponent<LayoutElement>();
        le.minWidth = 40;
        le.preferredWidth = 40;
        le.minHeight = 40;
        le.preferredHeight = 40;

        // Expand hit area to 80×80 via a transparent child Image.
        var hit = new GameObject("HitArea",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        hit.layer = LayerMask.NameToLayer("UI");
        hit.transform.SetParent(go.transform, false);
        var hitRt = (RectTransform)hit.transform;
        hitRt.anchorMin = new Vector2(0.5f, 0.5f);
        hitRt.anchorMax = new Vector2(0.5f, 0.5f);
        hitRt.pivot = new Vector2(0.5f, 0.5f);
        hitRt.sizeDelta = new Vector2(80, 80);
        var hitImg = hit.GetComponent<Image>();
        hitImg.color = new Color(0, 0, 0, 0);
        hitImg.raycastTarget = true;

        // "✕" glyph child — toggled via clearIcon SetActive
        var x = new GameObject("X",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        x.layer = LayerMask.NameToLayer("UI");
        x.transform.SetParent(go.transform, false);
        var xRt = (RectTransform)x.transform;
        xRt.anchorMin = Vector2.zero;
        xRt.anchorMax = Vector2.one;
        xRt.offsetMin = Vector2.zero;
        xRt.offsetMax = Vector2.zero;
        var xTmp = x.GetComponent<TextMeshProUGUI>();
        xTmp.text = "✕"; // ✕
        xTmp.fontSize = 28;
        xTmp.color = Color.white;
        xTmp.alignment = TextAlignmentOptions.Center;
        xTmp.raycastTarget = false;

        var btn = go.GetComponent<Button>();
        var colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = Color.white;
        colors.pressedColor = new Color(0.92f, 0.92f, 0.92f, 1f);
        colors.selectedColor = Color.white;
        btn.colors = colors;

        clearIcon = go; // toggle the whole ClearButton GameObject's visibility
        return btn;
    }

    private static Color HexColor(string hex)
    {
        if (ColorUtility.TryParseHtmlString(hex, out var c)) return c;
        return Color.magenta;
    }
}

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public static class EmptyStateViewBuilder
{
    private const string ScreenName = "Screen_Whatsapp";
    private const string ChatsPanelName = "ChatsPanel";
    private const string EmptyStateName = "EmptyState";

    [MenuItem("Tools/Bot Switcher/Build EmptyState")]
    public static void Build()
    {
        GameObject screen = FindGameObjectByNameIncludeInactive(ScreenName);
        if (screen == null)
        {
            Debug.LogError($"[EmptyStateViewBuilder] Could not find {ScreenName} (active or inactive). Open the Main scene.");
            return;
        }

        Transform chatsPanel = screen.transform.Find(ChatsPanelName);
        if (chatsPanel == null)
        {
            Debug.LogError($"[EmptyStateViewBuilder] {ScreenName} has no child named '{ChatsPanelName}'.");
            return;
        }

        // Remove existing instance
        Transform existing = chatsPanel.Find(EmptyStateName);
        if (existing != null)
        {
            Object.DestroyImmediate(existing.gameObject);
        }

        // Root: full-stretch RectTransform that overlays the scroll area inside ChatsPanel.
        GameObject root = new GameObject(EmptyStateName,
            typeof(RectTransform), typeof(CanvasGroup), typeof(VerticalLayoutGroup));
        root.transform.SetParent(chatsPanel, false);
        // Make sure it sits visually above the scroll content (last sibling) so when shown it covers the chat list.
        root.transform.SetAsLastSibling();

        var rootRT = root.GetComponent<RectTransform>();
        rootRT.anchorMin = Vector2.zero;
        rootRT.anchorMax = Vector2.one;
        rootRT.offsetMin = Vector2.zero;
        rootRT.offsetMax = Vector2.zero;

        var rootLayout = root.GetComponent<VerticalLayoutGroup>();
        rootLayout.padding = new RectOffset(32, 32, 0, 0);
        rootLayout.spacing = 16;
        rootLayout.childAlignment = TextAnchor.MiddleCenter;
        rootLayout.childForceExpandWidth = false;
        rootLayout.childForceExpandHeight = false;

        // Icon (placeholder — no sprite assigned; the Image renders its tint until art lands).
        GameObject icon = new GameObject("Icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        icon.transform.SetParent(root.transform, false);
        icon.GetComponent<RectTransform>().sizeDelta = new Vector2(64, 64);
        var iconLE = icon.GetComponent<LayoutElement>();
        iconLE.preferredWidth = 64;
        iconLE.preferredHeight = 64;
        var iconImage = icon.GetComponent<Image>();
        iconImage.color = new Color(0.85f, 0.85f, 0.85f);

        // Title (TMP 18sp Semibold)
        GameObject title = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        title.transform.SetParent(root.transform, false);
        var titleText = title.GetComponent<TextMeshProUGUI>();
        titleText.text = "No bots yet";
        titleText.fontSize = 18;
        titleText.fontStyle = FontStyles.Bold;
        titleText.color = new Color(0.1f, 0.1f, 0.1f);
        titleText.alignment = TextAlignmentOptions.Center;
        var titleLE = title.GetComponent<LayoutElement>();
        titleLE.preferredHeight = 28;
        titleLE.preferredWidth = 360;

        // Body (TMP 14sp Regular, muted)
        GameObject body = new GameObject("Body", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        body.transform.SetParent(root.transform, false);
        var bodyText = body.GetComponent<TextMeshProUGUI>();
        bodyText.text = "Create your first bot to start managing chats.";
        bodyText.fontSize = 14;
        bodyText.color = new Color(0.45f, 0.45f, 0.45f);
        bodyText.alignment = TextAlignmentOptions.Center;
        bodyText.enableWordWrapping = true;
        var bodyLE = body.GetComponent<LayoutElement>();
        bodyLE.preferredHeight = 44;
        bodyLE.preferredWidth = 360;

        // Primary button (44dp tall)
        GameObject btn = new GameObject("PrimaryButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        btn.transform.SetParent(root.transform, false);
        var btnImage = btn.GetComponent<Image>();
        btnImage.color = new Color(0.13f, 0.78f, 0.42f); // accent green
        var btnLE = btn.GetComponent<LayoutElement>();
        btnLE.preferredWidth = 240;
        btnLE.preferredHeight = 44;
        var btnRT = btn.GetComponent<RectTransform>();
        btnRT.sizeDelta = new Vector2(240, 44);

        GameObject btnLabel = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        btnLabel.transform.SetParent(btn.transform, false);
        var btnLabelRT = btnLabel.GetComponent<RectTransform>();
        btnLabelRT.anchorMin = Vector2.zero;
        btnLabelRT.anchorMax = Vector2.one;
        btnLabelRT.offsetMin = Vector2.zero;
        btnLabelRT.offsetMax = Vector2.zero;
        var btnLabelText = btnLabel.GetComponent<TextMeshProUGUI>();
        btnLabelText.text = "Create your first bot";
        btnLabelText.fontSize = 16;
        btnLabelText.fontStyle = FontStyles.Bold;
        btnLabelText.color = Color.white;
        btnLabelText.alignment = TextAlignmentOptions.Center;

        // Add the EmptyStateView component AFTER children exist (so its Awake/OnEnable can find them if needed).
        var view = root.AddComponent<EmptyStateView>();

        // Wire serialized fields via SerializedObject.
        var so = new SerializedObject(view);
        so.FindProperty("iconImage").objectReferenceValue = iconImage;
        so.FindProperty("titleLabel").objectReferenceValue = titleText;
        so.FindProperty("bodyLabel").objectReferenceValue = bodyText;
        so.FindProperty("primaryButton").objectReferenceValue = btn.GetComponent<Button>();
        so.FindProperty("primaryButtonLabel").objectReferenceValue = btnLabelText;
        // iconNoBots / iconNoWhatsApp left null — designer will drag in sprite assets later.
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log($"[EmptyStateViewBuilder] Built {EmptyStateName} under {ScreenName}/{ChatsPanelName}.");
        Selection.activeGameObject = root;
    }

    private static GameObject FindGameObjectByNameIncludeInactive(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        Transform[] all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].name == name)
            {
                return all[i].gameObject;
            }
        }
        return null;
    }
}
#endif

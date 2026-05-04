#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public static class Screen_WhatsappHeaderRebuilder
{
    private const string ScreenName = "Screen_Whatsapp";
    // The actual header child name in this scene is "TopBar" under ChatsPanel.
    private const string ChatsPanelName = "ChatsPanel";
    private const string HeaderChildName = "TopBar";
    private const string TitleName = "BotSwitcherTitle";

    [MenuItem("Tools/Bot Switcher/Rebuild Whatsapp Header")]
    public static void Rebuild()
    {
        GameObject screen = FindGameObjectByNameIncludeInactive(ScreenName);
        if (screen == null)
        {
            Debug.LogError($"[Screen_WhatsappHeaderRebuilder] Could not find {ScreenName} in the active scene (active or inactive). Open the Main scene.");
            return;
        }

        Transform chatsPanel = screen.transform.Find(ChatsPanelName);
        if (chatsPanel == null)
        {
            Debug.LogError($"[Screen_WhatsappHeaderRebuilder] {ScreenName} has no child named '{ChatsPanelName}'. Inspect the scene and update ChatsPanelName in this builder.");
            return;
        }

        Transform header = chatsPanel.Find(HeaderChildName);
        if (header == null)
        {
            Debug.LogError($"[Screen_WhatsappHeaderRebuilder] {ScreenName}/{ChatsPanelName} has no child named '{HeaderChildName}'. Inspect the scene and update HeaderChildName in this builder.");
            return;
        }

        Transform existing = header.Find(TitleName);
        if (existing != null)
        {
            Object.DestroyImmediate(existing.gameObject);
        }

        GameObject root = new GameObject(TitleName,
            typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(Button), typeof(Image));
        root.transform.SetParent(header, false);

        var rootRT = root.GetComponent<RectTransform>();
        rootRT.anchorMin = new Vector2(0.5f, 0.5f);
        rootRT.anchorMax = new Vector2(0.5f, 0.5f);
        rootRT.pivot = new Vector2(0.5f, 0.5f);
        rootRT.sizeDelta = new Vector2(240, 44); // 44dp touch target
        rootRT.anchoredPosition = Vector2.zero;

        // Invisible base — keeps raycasts active without visual artifact.
        var rootImage = root.GetComponent<Image>();
        rootImage.color = new Color(1, 1, 1, 0.001f);
        rootImage.raycastTarget = true;

        var rootLayout = root.GetComponent<HorizontalLayoutGroup>();
        rootLayout.spacing = 8;
        rootLayout.childAlignment = TextAnchor.MiddleCenter;
        rootLayout.padding = new RectOffset(8, 8, 0, 0);
        rootLayout.childForceExpandWidth = false;
        rootLayout.childForceExpandHeight = false;
        rootLayout.childControlWidth = true;
        rootLayout.childControlHeight = true;

        // Avatar
        GameObject avatar = new GameObject("Avatar", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        avatar.transform.SetParent(root.transform, false);
        avatar.GetComponent<RectTransform>().sizeDelta = new Vector2(24, 24);
        var avLE = avatar.GetComponent<LayoutElement>();
        avLE.preferredWidth = 24;
        avLE.preferredHeight = 24;
        avatar.GetComponent<Image>().color = new Color(0.85f, 0.85f, 0.85f);

        // Name
        GameObject nameGO = new GameObject("BotName", typeof(RectTransform), typeof(TextMeshProUGUI));
        nameGO.transform.SetParent(root.transform, false);
        var nameText = nameGO.GetComponent<TextMeshProUGUI>();
        nameText.text = "Bot";
        nameText.fontSize = 18;
        nameText.fontStyle = FontStyles.Bold;
        nameText.color = new Color(0.1f, 0.1f, 0.1f);
        nameText.alignment = TextAlignmentOptions.Center;

        // Chevron (placeholder ▼ glyph; replace with sprite when art arrives)
        GameObject chev = new GameObject("Chevron", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        chev.transform.SetParent(root.transform, false);
        var chevText = chev.GetComponent<TextMeshProUGUI>();
        chevText.text = "▼";
        chevText.fontSize = 12;
        chevText.color = new Color(0.45f, 0.45f, 0.45f);
        var chevLE = chev.GetComponent<LayoutElement>();
        chevLE.preferredWidth = 16;
        chevLE.preferredHeight = 16;

        // Add the runtime binder. It wires the Button.onClick at Awake by finding
        // BotSwitcherSheet in the scene — no Editor-time listener serialization.
        root.AddComponent<BotSwitcherTitleBinder>();

        // Sanity warning if the sheet isn't present yet.
        if (Object.FindFirstObjectByType<BotSwitcherSheet>(FindObjectsInactive.Include) == null)
        {
            Debug.LogWarning("[Screen_WhatsappHeaderRebuilder] No BotSwitcherSheet in scene — run 'Tools → Bot Switcher → Build Sheet' first, then re-run this builder.");
        }

        EditorUtility.SetDirty(root);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log("[Screen_WhatsappHeaderRebuilder] Whatsapp header rebuilt with bot-switcher title.");
        Selection.activeGameObject = root;
    }

    /// <summary>
    /// Finds a GameObject by name in the loaded scenes, including inactive ones.
    /// Returns the first match, or null. Used because GameObject.Find skips inactive
    /// objects, but our screen panels are deactivated when not the current tab.
    /// </summary>
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

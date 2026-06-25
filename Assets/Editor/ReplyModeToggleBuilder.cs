#if UNITY_EDITOR
using System;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the reply-mode sliding-knob toggle into Screen_Whatsapp/ChatsPanel/TopBar
/// and a matching confirm popup under the Screen_Whatsapp root, then wires the
/// runtime <see cref="ReplyModeToggleBinder"/> serialized references.
///
/// Layout (Native-polish base): a 312×126 pill track on the right of the bar
/// (left of the existing options kebab) holding both words — Полу on the left,
/// Авто on the right — with a white thumb that covers the active one. Also sets
/// the bar background to true white and hides the competing centre "Title".
///
/// Safe to re-run: destroys and rebuilds the toggle + popup each time.
/// </summary>
public static class ReplyModeToggleBuilder
{
    private const string ScreenName = "Screen_Whatsapp";
    private const string ChatsPanelName = "ChatsPanel";
    private const string TopBarName = "TopBar";
    private const string ToggleName = "ModeToggle";
    private const string PopupName = "ReplyModeConfirmPopup";

    // SF Pro Text Regular — the header font used elsewhere in the bar.
    private const string HeaderFontGuid = "a2b0b38b6764047da9250bcff1b0f432";

    private static Type cachedRoundedType;

    [MenuItem("Tools/Bot Switcher/Build Reply Mode Toggle")]
    public static void Build()
    {
        GameObject screen = FindByNameIncludeInactive(ScreenName);
        if (screen == null)
        {
            Debug.LogError($"[ReplyModeToggleBuilder] Could not find '{ScreenName}'. Open the Main scene.");
            return;
        }

        Transform chatsPanel = screen.transform.Find(ChatsPanelName);
        Transform topBar = chatsPanel != null ? chatsPanel.Find(TopBarName) : null;
        if (topBar == null)
        {
            Debug.LogError($"[ReplyModeToggleBuilder] '{ScreenName}/{ChatsPanelName}/{TopBarName}' not found.");
            return;
        }

        Transform rightZone = topBar.Find("RightZone");
        if (rightZone == null)
        {
            Debug.LogError($"[ReplyModeToggleBuilder] '{TopBarName}/RightZone' not found.");
            return;
        }

        TMP_FontAsset font = LoadHeaderFont();

        // Sweep every existing toggle anywhere under the bar (covers both the old
        // TopBar child and any hand-moved copy inside RightZone) and the popup in
        // its old + new homes, so re-runs never leave duplicates behind.
        DestroyAllByName(topBar, ToggleName);
        DestroyExisting(chatsPanel, PopupName);
        DestroyExisting(screen.transform, PopupName);

        // 1. Confirm popup (built first so the toggle can reference its parts).
        //    Lives under ChatsPanel, beside DeleteChatConfirmPanel — the project's
        //    home for chats-list confirm popups.
        ConfirmPopupRefs popup = BuildConfirmPopup(chatsPanel, font);

        // 2. The sliding-knob toggle — inside RightZone, left of the new-chat button.
        ToggleRefs toggle = BuildToggle(rightZone, font);

        // 3. Wire the runtime binder.
        var binder = toggle.Root.AddComponent<ReplyModeToggleBinder>();
        var so = new SerializedObject(binder);
        SetRef(so, "trackImage", toggle.Track);
        SetRef(so, "toggleButton", toggle.Button);
        SetRef(so, "thumb", toggle.Thumb);
        SetRef(so, "thumbLabel", toggle.ThumbLabel);
        SetRef(so, "faintAvto", toggle.FaintAvto);
        SetRef(so, "faintPolu", toggle.FaintPolu);
        SetRef(so, "confirmPopup", popup.Root);
        SetRef(so, "confirmTitle", popup.Title);
        SetRef(so, "confirmBody", popup.Body);
        SetRef(so, "confirmButton", popup.ConfirmButton);
        SetRef(so, "cancelButton", popup.CancelButton);
        so.ApplyModifiedPropertiesWithoutUndo();

        // 4. Native-polish base touches: true-white bar, hide the centre title,
        //    and scale the bot-switcher identity to match.
        WhitenBarBackground(topBar);
        HideChild(topBar, "CenterZone");
        Component avatarRounded = RefineLeftIdentity(topBar);

        // Bake rounded corners now that rects are sized.
        Canvas.ForceUpdateCanvases();
        RefreshRounded(toggle.TrackRounded);
        RefreshRounded(toggle.ThumbRounded);
        RefreshRounded(avatarRounded);

        EditorUtility.SetDirty(toggle.Root);
        EditorSceneManager.MarkSceneDirty(topBar.gameObject.scene);
        Selection.activeGameObject = toggle.Root;
        Debug.Log("[ReplyModeToggleBuilder] Reply-mode toggle built and wired.");
    }

    // ---- Toggle ----------------------------------------------------------

    private struct ToggleRefs
    {
        public GameObject Root;
        public Image Track;
        public Button Button;
        public RectTransform Thumb;
        public TextMeshProUGUI ThumbLabel;
        public TextMeshProUGUI FaintAvto;
        public TextMeshProUGUI FaintPolu;
        public Component TrackRounded;
        public Component ThumbRounded;
    }

    private static ToggleRefs BuildToggle(Transform rightZone, TMP_FontAsset font)
    {
        // RightZone arranges its children with a HorizontalLayoutGroup (MiddleRight);
        // add a small gap so the toggle doesn't touch the new-chat button.
        var hlg = rightZone.GetComponent<HorizontalLayoutGroup>();
        if (hlg != null) hlg.spacing = 20f;

        var root = new GameObject(ToggleName,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        root.layer = LayerMask.NameToLayer("UI");
        root.transform.SetParent(rightZone, false);
        root.transform.SetAsFirstSibling(); // left of the new-chat button

        var rt = (RectTransform)root.transform;
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.sizeDelta = new Vector2(280f, 72f);
        rt.anchoredPosition = Vector2.zero; // positioned by the RightZone layout group

        var track = root.GetComponent<Image>();
        track.color = Hex("#2FB344"); // default Авто state; binder re-resolves per bot
        track.raycastTarget = true;
        Component trackRounded = AddRounded(root, 36f);

        var button = root.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = track;

        // Both words live inside the track; the thumb (added last) covers the active one.
        var faintPolu = BuildTrackLabel(root.transform, "FaintPolu", "Вместе", -70f, font);
        var faintAvto = BuildTrackLabel(root.transform, "FaintAvto", "Авто", 70f, font);

        var thumbGo = new GameObject("Thumb",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        thumbGo.layer = LayerMask.NameToLayer("UI");
        thumbGo.transform.SetParent(root.transform, false);
        var thumbRt = (RectTransform)thumbGo.transform;
        thumbRt.anchorMin = new Vector2(0.5f, 0.5f);
        thumbRt.anchorMax = new Vector2(0.5f, 0.5f);
        thumbRt.pivot = new Vector2(0.5f, 0.5f);
        thumbRt.sizeDelta = new Vector2(124f, 56f);
        thumbRt.anchoredPosition = new Vector2(70f, 0f); // right half — default Авто (8u inset all around)
        var thumbImg = thumbGo.GetComponent<Image>();
        thumbImg.color = Color.white;
        thumbImg.raycastTarget = false;
        Component thumbRounded = AddRounded(thumbGo, 28f);

        var thumbLabel = BuildLabel(thumbGo.transform, "ThumbLabel", "Авто", 28f,
            FontStyles.Bold, Hex("#206A2C"), font);
        thumbLabel.characterSpacing = -2f; // fits wider active words like "Вместе"
        thumbLabel.enableWordWrapping = false; // keep "Вместе" on one line

        return new ToggleRefs
        {
            Root = root,
            Track = track,
            Button = button,
            Thumb = thumbRt,
            ThumbLabel = thumbLabel,
            FaintAvto = faintAvto,
            FaintPolu = faintPolu,
            TrackRounded = trackRounded,
            ThumbRounded = thumbRounded,
        };
    }

    // A half-track word (recessive tint); the thumb covers the active one.
    private static TextMeshProUGUI BuildTrackLabel(Transform parent, string name,
        string text, float x, TMP_FontAsset font)
    {
        var go = new GameObject(name,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(140f, 72f);
        rt.anchoredPosition = new Vector2(x, 0f);

        var tmp = go.GetComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = 28f;
        tmp.fontStyle = FontStyles.Normal;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Hex("#C3EFCB"); // recessive-on-green; binder updates per state
        tmp.characterSpacing = -2f; // project text standard
        tmp.enableWordWrapping = false; // never break "Вместе" onto two lines
        tmp.raycastTarget = false;
        return tmp;
    }

    private static TextMeshProUGUI BuildLabel(Transform parent, string name, string text,
        float fontSize, FontStyles style, Color color, TMP_FontAsset font)
    {
        var go = new GameObject(name,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var tmp = go.GetComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = color;
        tmp.raycastTarget = false;
        return tmp;
    }

    // ---- Confirm popup ---------------------------------------------------

    private struct ConfirmPopupRefs
    {
        public GameObject Root;
        public TextMeshProUGUI Title;
        public TextMeshProUGUI Body;
        public Button ConfirmButton;
        public Button CancelButton;
    }

    private static ConfirmPopupRefs BuildConfirmPopup(Transform parent, TMP_FontAsset font)
    {
        var popup = new GameObject(PopupName,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        popup.layer = LayerMask.NameToLayer("UI");
        popup.transform.SetParent(parent, false);
        var prt = (RectTransform)popup.transform;
        prt.anchorMin = Vector2.zero;
        prt.anchorMax = Vector2.one;
        prt.offsetMin = Vector2.zero;
        prt.offsetMax = Vector2.zero;
        prt.SetAsLastSibling();
        popup.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f); // PopupUI fades it to 0.5

        // Card — must be named "Content" so PopupUI.Show locates it.
        var card = new GameObject("Content",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        card.layer = LayerMask.NameToLayer("UI");
        card.transform.SetParent(popup.transform, false);
        var crt = (RectTransform)card.transform;
        crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0.5f, 0.5f);
        crt.sizeDelta = new Vector2(720f, 440f);
        crt.anchoredPosition = Vector2.zero;
        card.GetComponent<Image>().color = Color.white;
        AddRounded(card, 40f);
        card.AddComponent<EventAbsorber>();

        var title = BuildPopupText(card.transform, "Title", "Сменить режим?", 42f, true,
            Hex("#1C1C1F"), new Vector2(0f, -52f), new Vector2(-80f, 64f),
            TextAlignmentOptions.Top, font);
        // Body: manual tweaks preserved — nudged up to -118 and vertically centred.
        var body = BuildPopupText(card.transform, "Body",
            "Бот перестанет отвечать сам — он будет предлагать варианты ответа, а вы выберете.",
            34f, false, Hex("#646468"), new Vector2(0f, -118f), new Vector2(-80f, 130f),
            TextAlignmentOptions.Center, font);

        var (cancelBtn, _) = BuildPopupButton(card.transform, "CancelButton", "Отмена",
            Hex("#EFEFF0"), Hex("#1C1C1F"), 0.27f, font);
        var (confirmBtn, _) = BuildPopupButton(card.transform, "ConfirmButton", "Продолжить",
            Hex("#2FB344"), Color.white, 0.73f, font);

        popup.SetActive(false);

        return new ConfirmPopupRefs
        {
            Root = popup,
            Title = title,
            Body = body,
            ConfirmButton = confirmBtn,
            CancelButton = cancelBtn,
        };
    }

    private static TextMeshProUGUI BuildPopupText(Transform parent, string name, string text,
        float fontSize, bool bold, Color color, Vector2 anchoredPosition, Vector2 sizeDelta,
        TextAlignmentOptions align, TMP_FontAsset font)
    {
        var go = new GameObject(name,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = sizeDelta;

        var tmp = go.GetComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        tmp.alignment = align;
        tmp.color = color;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static (Button button, TextMeshProUGUI label) BuildPopupButton(Transform parent,
        string name, string label, Color bg, Color textColor, float anchorX, TMP_FontAsset font)
    {
        var go = new GameObject(name,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(anchorX, 0f);
        rt.anchorMax = new Vector2(anchorX, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 44f);
        rt.sizeDelta = new Vector2(280f, 104f);

        var img = go.GetComponent<Image>();
        img.color = bg;
        AddRounded(go, 28f);

        var tmp = BuildLabel(go.transform, "Label", label, 34f, FontStyles.Bold, textColor, font);

        var btn = go.GetComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.targetGraphic = img;
        return (btn, tmp);
    }

    // ---- Bar tweaks ------------------------------------------------------

    // Set the full-stretch background Image to true white (Native-polish base).
    private static void WhitenBarBackground(Transform topBar)
    {
        for (int i = 0; i < topBar.childCount; i++)
        {
            var child = (RectTransform)topBar.GetChild(i);
            bool fullStretch = child.anchorMin == Vector2.zero && child.anchorMax == Vector2.one;
            var img = child.GetComponent<Image>();
            if (fullStretch && img != null)
            {
                img.color = Color.white;
                return;
            }
        }
        Debug.LogWarning("[ReplyModeToggleBuilder] Could not find a full-stretch BG image to whiten.");
    }

    private static void HideChild(Transform parent, string childName)
    {
        var child = parent.Find(childName);
        if (child != null) child.gameObject.SetActive(false);
    }

    // Scale the existing bot-switcher (LeftZone/BotSwitcherTitle) up to the
    // Native-polish identity scale: a larger avatar with a proportionally larger
    // icon and a taller row. The bot name is already 44 Bold, so it's left as-is.
    // Returns the avatar's rounding component so Build can Refresh it after layout.
    private static Component RefineLeftIdentity(Transform topBar)
    {
        Transform leftZone = topBar.Find("LeftZone");
        Transform title = leftZone != null ? leftZone.Find("BotSwitcherTitle") : null;
        if (title == null)
        {
            Debug.LogWarning("[ReplyModeToggleBuilder] BotSwitcherTitle not found — skipping left-identity refine.");
            return null;
        }

        const float avatarSize = 84f;       // up from 60
        const float iconRatio = 0.47f;      // icon was 28.16 of a 60 avatar

        var titleRt = (RectTransform)title;
        titleRt.sizeDelta = new Vector2(titleRt.sizeDelta.x, avatarSize); // row tall enough for the avatar

        Transform avatar = title.Find("Avatar");
        if (avatar == null) return null;

        var le = avatar.GetComponent<LayoutElement>();
        if (le != null)
        {
            le.preferredWidth = avatarSize;
            le.preferredHeight = avatarSize;
        }

        if (avatar.childCount > 0)
        {
            var icon = (RectTransform)avatar.GetChild(0);
            float iconSize = avatarSize * iconRatio;
            icon.sizeDelta = new Vector2(iconSize, iconSize);
        }

        // Set the circle radius now; the actual Refresh happens after a layout pass.
        var type = ResolveRoundedType();
        if (type == null) return null;
        var rc = avatar.GetComponent(type);
        if (rc != null) type.GetField("radius")?.SetValue(rc, avatarSize / 2f);
        return rc as Component;
    }

    // ---- Helpers ---------------------------------------------------------

    private static void DestroyExisting(Transform parent, string childName)
    {
        var existing = parent.Find(childName);
        if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
    }

    // Destroy every descendant named `name` (any depth) — clears duplicates left
    // by hand-moving objects or earlier builds.
    private static void DestroyAllByName(Transform root, string name)
    {
        var all = root.GetComponentsInChildren<Transform>(true);
        foreach (var t in all)
        {
            if (t != null && t != root && t.name == name)
                UnityEngine.Object.DestroyImmediate(t.gameObject);
        }
    }

    private static void SetRef(SerializedObject so, string property, UnityEngine.Object value)
    {
        var prop = so.FindProperty(property);
        if (prop != null) prop.objectReferenceValue = value;
        else Debug.LogWarning($"[ReplyModeToggleBuilder] Binder property '{property}' not found.");
    }

    private static TMP_FontAsset LoadHeaderFont()
    {
        string path = AssetDatabase.GUIDToAssetPath(HeaderFontGuid);
        var font = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (font != null) return font;

        var anyTmp = UnityEngine.Object.FindFirstObjectByType<TextMeshProUGUI>(FindObjectsInactive.Include);
        return anyTmp != null ? anyTmp.font : null;
    }

    // RoundedCorners ships in its OWN UPM assembly, so Type.GetType(...,Assembly-CSharp)
    // silently fails (project memory: that's the ChatsSearchBarBuilder bug). Scan every
    // loaded assembly for the type instead, by name, to avoid a hard editor dependency.
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

    private static Component AddRounded(GameObject go, float radius)
    {
        var type = ResolveRoundedType();
        if (type == null)
        {
            Debug.LogWarning("[ReplyModeToggleBuilder] ImageWithRoundedCorners not found — corners will be square.");
            return null;
        }
        var rc = go.AddComponent(type);
        type.GetField("radius")?.SetValue(rc, radius);
        type.GetField("image")?.SetValue(rc, go.GetComponent<Image>());
        return rc;
    }

    private static void RefreshRounded(Component rc)
    {
        if (rc == null) return;
        Type t = rc.GetType();
        t.GetMethod("Validate", BindingFlags.Public | BindingFlags.Instance)?.Invoke(rc, null);
        t.GetMethod("Refresh", BindingFlags.Public | BindingFlags.Instance)?.Invoke(rc, null);
    }

    private static GameObject FindByNameIncludeInactive(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var all = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var t in all)
            if (t != null && t.name == name) return t.gameObject;
        return null;
    }

    private static Color Hex(string hex) =>
        ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
}
#endif

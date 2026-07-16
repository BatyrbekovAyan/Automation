#if UNITY_EDITOR
using System;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the Telegram chat-list "syncing" pill (D9) into Screen_Whatsapp/ChatsPanel — a small
/// floating rounded pill holding a rotating ring spinner + «Синхронизация…» label, hidden by
/// default (CanvasGroup alpha 0) and shown only while a Telegram chats/filter sync is in flight.
/// Adds the <see cref="ChatListSyncIndicator"/> component and stamps its two serialized refs
/// (spinner RectTransform + label TMP) via SerializedObject.
///
/// The pill is a SIBLING of EmptyState / SyncingView under ChatsPanel (which is exactly
/// <c>ChatManager.ChatListPanel</c>), appended LAST so it renders above the list, anchored
/// top-centre just below the TopBar. Reuses the same ring sprite SyncingView spins
/// (Assets/Images/Chat/Loading.png, guid 753b3c81498bd499499770c754f31e95), tinted Telegram blue.
///
/// Idioms (null-guarded RoundedCorners via AppDomain scan — the Nobi assembly is invisible to
/// editor Type.GetType; header font by GUID with a scene-TMP fallback; SerializedObject SetRef;
/// FindByNameIncludeInactive; DestroyAllByName; idempotent delete-and-rebuild) are copied verbatim
/// from ChannelSwitcherBuilder. Edit-Mode only; saves the scene; no Undo grouping.
///
/// Two entry points (mirroring EmptyStateTelegramIconBuilder):
///   • Editor OPEN   → run "Tools/Chat List Sync Indicator/Build", then SAVE (Cmd+S).
///   • Editor CLOSED → Tools/run-editor-builder.sh ChatListSyncIndicatorBuilder.StampHeadless
/// </summary>
public static class ChatListSyncIndicatorBuilder
{
    private const string ScreenName = "Screen_Whatsapp";
    private const string ChatsPanelName = "ChatsPanel";
    private const string IndicatorName = "ChatListSyncIndicator";

    // SF Pro Text — the header font used across the bar (same GUID ChannelSwitcherBuilder uses).
    private const string HeaderFontGuid = "a2b0b38b6764047da9250bcff1b0f432";
    // The ring sprite SyncingView spins (Assets/Images/Chat/Loading.png).
    private const string SpinnerSpriteGuid = "753b3c81498bd499499770c754f31e95";

    // ── Pill geometry (1080×1920 reference units) ──
    private const float PillWidth = 380f;
    private const float PillHeight = 76f;
    private const float PillTopOffset = -300f;   // below the ~284-tall TopBar (safe-zone baked)
    private const float SpinnerSize = 44f;
    private const float SpinnerCenterX = 52f;     // from the pill's left edge
    private const float LabelLeftInset = 92f;     // clears the spinner
    private const float LabelRightInset = -24f;
    private const float LabelSize = 32f;          // caption/meta scale
    private const float LabelSpacing = -2f;       // project text standard

    // Neutral pill track (matches the ChannelSwitcher track), Telegram-blue spinner, dark label.
    private static readonly Color PillColor = Hex("#EFEFF0");
    private static readonly Color SpinnerColor = Hex("#2AABEE");
    private static readonly Color LabelColor = Hex("#3A3A3C");

    private static Type cachedRoundedType;

    // ── Entry points ────────────────────────────────────────────────────────

    [MenuItem("Tools/Chat List Sync Indicator/Build")]
    public static void Build()
    {
        GameObject root = BuildInternal();
        if (root != null)
        {
            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(root.scene);
        }
        Debug.Log("[ChatListSyncIndicatorBuilder] Build complete: Telegram sync pill. SAVE THE SCENE (Cmd+S).");
    }

    // Headless entry (Editor closed):
    //   Tools/run-editor-builder.sh ChatListSyncIndicatorBuilder.StampHeadless
    public static void StampHeadless()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        BuildInternal();
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[ChatListSyncIndicatorBuilder] Headless build + save complete: Telegram sync pill stamped.");
    }

    // ── Main build ──────────────────────────────────────────────────────────

    private static GameObject BuildInternal()
    {
        GameObject screen = FindByNameIncludeInactive(ScreenName);
        if (screen == null)
            throw new InvalidOperationException($"[ChatListSyncIndicatorBuilder] '{ScreenName}' not found — open Main.unity.");

        Transform chatsPanel = screen.transform.Find(ChatsPanelName);
        if (chatsPanel == null)
            throw new InvalidOperationException(
                $"[ChatListSyncIndicatorBuilder] '{ScreenName}/{ChatsPanelName}' not found.");

        // Idempotent rebuild — drop any prior instance first.
        DestroyAllByName(chatsPanel, IndicatorName);

        TMP_FontAsset font = LoadHeaderFont();

        // ── Pill root: rounded background + CanvasGroup + the driver component. ──
        var root = new GameObject(IndicatorName,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
        root.layer = LayerMask.NameToLayer("UI");
        root.transform.SetParent(chatsPanel, false); // last child → renders above the list

        var rt = (RectTransform)root.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(PillWidth, PillHeight);
        rt.anchoredPosition = new Vector2(0f, PillTopOffset);

        var pill = root.GetComponent<Image>();
        pill.color = PillColor;
        pill.raycastTarget = false; // informational overlay — never intercept list taps
        Component pillRounded = AddRounded(root, PillHeight / 2f);

        // Hidden by default; ChatListSyncIndicator toggles alpha at runtime.
        var canvasGroup = root.GetComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        // ── Spinner (rotating ring, Telegram blue). ──
        RectTransform spinner = BuildSpinner(root.transform);

        // ── Label «Синхронизация…». ──
        TextMeshProUGUI label = BuildLabel(root.transform, font);

        // ── Driver component + serialized-ref stamping (05-12 field-name contract). ──
        var indicator = root.AddComponent<ChatListSyncIndicator>();
        var so = new SerializedObject(indicator);
        SetRef(so, "spinner", spinner);
        SetRef(so, "label", label);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(indicator);

        // Bake rounded corners once the rect is sized.
        Canvas.ForceUpdateCanvases();
        RefreshRounded(pillRounded);

        return root;
    }

    private static RectTransform BuildSpinner(Transform parent)
    {
        var go = new GameObject("Spinner",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);

        var srt = (RectTransform)go.transform;
        srt.anchorMin = srt.anchorMax = new Vector2(0f, 0.5f);
        srt.pivot = new Vector2(0.5f, 0.5f);
        srt.sizeDelta = new Vector2(SpinnerSize, SpinnerSize);
        srt.anchoredPosition = new Vector2(SpinnerCenterX, 0f);

        var img = go.GetComponent<Image>();
        img.sprite = LoadSpinnerSprite();
        img.color = SpinnerColor;
        img.preserveAspect = true;
        img.raycastTarget = false;
        return srt;
    }

    private static TextMeshProUGUI BuildLabel(Transform parent, TMP_FontAsset font)
    {
        var go = new GameObject("Label",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(LabelLeftInset, 0f);
        rt.offsetMax = new Vector2(LabelRightInset, 0f);

        var tmp = go.GetComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = "Синхронизация…"; // ChatListSyncIndicator.Awake re-stamps this too
        tmp.fontSize = LabelSize;
        tmp.fontStyle = FontStyles.Normal;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.color = LabelColor;
        tmp.characterSpacing = LabelSpacing;
        tmp.enableWordWrapping = false;
        tmp.raycastTarget = false;
        return tmp;
    }

    // ── Helpers (verbatim ChannelSwitcherBuilder idioms) ────────────────────

    private static void SetRef(SerializedObject so, string property, UnityEngine.Object value)
    {
        var prop = so.FindProperty(property);
        if (prop != null) prop.objectReferenceValue = value;
        else Debug.LogWarning($"[ChatListSyncIndicatorBuilder] Component property '{property}' not found — recompile first.");
    }

    private static Sprite LoadSpinnerSprite()
    {
        string path = AssetDatabase.GUIDToAssetPath(SpinnerSpriteGuid);
        var sprite = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
            Debug.LogWarning($"[ChatListSyncIndicatorBuilder] Spinner sprite not found at guid {SpinnerSpriteGuid} " +
                             $"(path: '{path}') — spinner will render as a plain rect.");
        return sprite;
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
    // silently fails (project memory: roundedcorners-assembly). Scan every loaded assembly
    // for the type by name to avoid a hard editor dependency.
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
            Debug.LogWarning("[ChatListSyncIndicatorBuilder] ImageWithRoundedCorners not found — corners will be square.");
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

    private static void DestroyAllByName(Transform root, string name)
    {
        var all = root.GetComponentsInChildren<Transform>(true);
        foreach (var t in all)
        {
            if (t != null && t != root && t.name == name)
                UnityEngine.Object.DestroyImmediate(t.gameObject);
        }
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

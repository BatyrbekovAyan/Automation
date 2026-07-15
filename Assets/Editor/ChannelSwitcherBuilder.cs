#if UNITY_EDITOR
using System;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the WhatsApp | Telegram segmented channel switcher into
/// Screen_Whatsapp/ChatsPanel/TopBar/CenterZone and stamps the 06-01
/// <see cref="ChannelSwitcherView"/> serialized references, then performs the
/// navigation restructure in the same pass:
///   • removes the Telegram bottom-nav tab (BottomTabManager.tabs[1]),
///   • relabels tab 0 «Чаты» (inspector name + scene TMP text),
///   • deletes the Screen_Telegram placeholder and its TelegramTab bottom-bar root.
///
/// Visual language mirrors the neighbouring ModeToggle pill
/// (<see cref="ReplyModeToggleBuilder"/>): a neutral rounded track holding two
/// chips, each a transparent Button over a brand-coloured RoundedCorners fill and
/// a header-font label. Idioms (null sprite + AppDomain-scanned RoundedCorners,
/// header font by GUID, SerializedObject SetRef, FindByNameIncludeInactive,
/// DestroyAllByName) are copied verbatim from ReplyModeToggleBuilder; the headless
/// entry mirrors NavRestructureBuilder.BuildHeadless.
///
/// Safe to re-run: the pill is delete-and-rebuilt by exact name, and the Telegram
/// tab removal is GUARDED so a re-run (when Dashboard has already shifted into
/// index 1) never deletes the wrong tab.
/// </summary>
public static class ChannelSwitcherBuilder
{
    private const string ScreenName = "Screen_Whatsapp";
    private const string ChatsPanelName = "ChatsPanel";
    private const string TopBarName = "TopBar";
    private const string CenterZoneName = "CenterZone";
    private const string SwitcherName = "ChannelSwitcher";

    // SF Pro Text — the header font used across the bar (same GUID ReplyModeToggleBuilder uses).
    private const string HeaderFontGuid = "a2b0b38b6764047da9250bcff1b0f432";

    // ── Pill geometry (1080×1920 reference units; mirrors ModeToggle proportions) ──
    private const float TrackWidth = 340f;   // fits the 360-wide CenterZone slot with margin
    private const float TrackHeight = 76f;
    private const float ChipWidth = 162f;
    private const float ChipHeight = 64f;
    private const float ChipGap = 4f;        // centre gap between the two chips
    private const float LabelSize = 22f;     // 28→22 so "WhatsApp"/"Telegram" clear the chip edges (05-09)
    private const float LabelSpacing = -2f;  // project text standard

    // Brand accents — mirror ChannelSwitcherView's selected fills (WA #25D366 / TG #2AABEE).
    private static readonly Color WaBrand = Hex("#25D366");
    private static readonly Color TgBrand = Hex("#2AABEE");
    private static readonly Color TrackColor = Hex("#EFEFF0");      // neutral iOS-style segmented track
    private static readonly Color UnselectedLabel = Hex("#3A3A3C"); // binder repaints per state at runtime

    private static Type cachedRoundedType;

    // ── Entry points ────────────────────────────────────────────────────────

    [MenuItem("Tools/Channel Switcher/Build")]
    public static void Build()
    {
        GameObject root = BuildInternal();
        if (root != null)
        {
            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(root.scene);
        }
        Debug.Log("[ChannelSwitcherBuilder] Build complete: switcher pill + nav restructure. SAVE THE SCENE (Cmd+S).");
    }

    // Headless entry (Editor closed):
    //   Unity -batchmode -nographics -projectPath . \
    //         -executeMethod ChannelSwitcherBuilder.BuildHeadless -quit
    public static void BuildHeadless()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        BuildInternal();
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[ChannelSwitcherBuilder] Headless build + save complete: switcher pill + nav restructure.");
    }

    // ── Main build (pill + nav restructure in one pass) ─────────────────────

    private static GameObject BuildInternal()
    {
        TMP_FontAsset font = LoadHeaderFont();
        GameObject root = BuildSwitcherPill(font);
        RestructureNav();
        return root;
    }

    // ── Switcher pill ───────────────────────────────────────────────────────

    private static GameObject BuildSwitcherPill(TMP_FontAsset font)
    {
        GameObject screen = FindByNameIncludeInactive(ScreenName);
        if (screen == null)
            throw new InvalidOperationException($"[ChannelSwitcherBuilder] '{ScreenName}' not found — open Main.unity.");

        Transform chatsPanel = screen.transform.Find(ChatsPanelName);
        Transform topBar = chatsPanel != null ? chatsPanel.Find(TopBarName) : null;
        Transform centerZone = topBar != null ? topBar.Find(CenterZoneName) : null;
        if (centerZone == null)
            throw new InvalidOperationException(
                $"[ChannelSwitcherBuilder] '{ScreenName}/{ChatsPanelName}/{TopBarName}/{CenterZoneName}' not found.");

        // Activate the reserved centre slot, drop the unused Title, idempotent rebuild.
        centerZone.gameObject.SetActive(true);
        DestroyAllByName(centerZone, "Title");
        DestroyAllByName(centerZone, SwitcherName);

        // Track = neutral rounded pill; the root also carries the binder.
        var root = new GameObject(SwitcherName,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        root.layer = LayerMask.NameToLayer("UI");
        root.transform.SetParent(centerZone, false);

        var rt = (RectTransform)root.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(TrackWidth, TrackHeight);
        rt.anchoredPosition = Vector2.zero;

        var track = root.GetComponent<Image>();
        track.color = TrackColor;
        track.raycastTarget = false; // the chips own the raycast area
        Component trackRounded = AddRounded(root, TrackHeight / 2f);

        // Two independently-tappable chips. WhatsApp is the default channel, so it
        // starts filled — the binder re-resolves both on the first OnEnable Refresh.
        ChipRefs wa = BuildChip(root.transform, "WaChip", "WhatsApp", -1f, WaBrand, font, selectedByDefault: true);
        ChipRefs tg = BuildChip(root.transform, "TgChip", "Telegram", +1f, TgBrand, font, selectedByDefault: false);

        // Runtime binder — stamp every serialized ref (06-01 field-name contract).
        var binder = root.AddComponent<ChannelSwitcherView>();
        var so = new SerializedObject(binder);
        SetRef(so, "waChipButton", wa.Button);
        SetRef(so, "tgChipButton", tg.Button);
        SetRef(so, "waChipFill", wa.Fill);
        SetRef(so, "tgChipFill", tg.Fill);
        SetRef(so, "waLabel", wa.Label);
        SetRef(so, "tgLabel", tg.Label);
        // Icons are optional (text-only v1, mirroring ModeToggle) — left unstamped.
        so.ApplyModifiedPropertiesWithoutUndo();

        // Bake rounded corners once the rects are sized.
        Canvas.ForceUpdateCanvases();
        RefreshRounded(trackRounded);
        RefreshRounded(wa.Rounded);
        RefreshRounded(tg.Rounded);

        return root;
    }

    private struct ChipRefs
    {
        public Button Button;
        public Image Fill;
        public TextMeshProUGUI Label;
        public Component Rounded;
    }

    // One segment: a transparent raycast Button over a brand-coloured rounded fill
    // (alpha toggled by the binder) and a centred header-font label.
    private static ChipRefs BuildChip(Transform track, string name, string label,
        float side, Color brand, TMP_FontAsset font, bool selectedByDefault)
    {
        var chip = new GameObject(name,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        chip.layer = LayerMask.NameToLayer("UI");
        chip.transform.SetParent(track, false);

        var crt = (RectTransform)chip.transform;
        crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0.5f, 0.5f);
        crt.sizeDelta = new Vector2(ChipWidth, ChipHeight);
        crt.anchoredPosition = new Vector2(side * (ChipWidth + ChipGap) / 2f, 0f);

        // Transparent raycast target = the Button's hit area (ReplyModeToggle popup-button pattern).
        var hit = chip.GetComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        hit.raycastTarget = true;
        var button = chip.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hit;

        // Selected-state fill — brand colour, rounded, alpha driven by the binder.
        var fillGo = new GameObject("Fill",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fillGo.layer = LayerMask.NameToLayer("UI");
        fillGo.transform.SetParent(chip.transform, false);
        var frt = (RectTransform)fillGo.transform;
        frt.anchorMin = Vector2.zero;
        frt.anchorMax = Vector2.one;
        frt.offsetMin = Vector2.zero;
        frt.offsetMax = Vector2.zero;
        var fill = fillGo.GetComponent<Image>();
        Color fillColor = brand;
        fillColor.a = selectedByDefault ? 1f : 0f;
        fill.color = fillColor;
        fill.raycastTarget = false;
        Component rounded = AddRounded(fillGo, ChipHeight / 2f);

        // Label on top of the fill.
        var labelTmp = BuildChipLabel(chip.transform, "Label", label, font,
            selectedByDefault ? Color.white : UnselectedLabel);

        return new ChipRefs { Button = button, Fill = fill, Label = labelTmp, Rounded = rounded };
    }

    private static TextMeshProUGUI BuildChipLabel(Transform parent, string name,
        string text, TMP_FontAsset font, Color color)
    {
        var go = new GameObject(name,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        // Inset the centred label horizontally so "WhatsApp"/"Telegram" keep clear of the
        // chip edges (05-09 device UAT); vertical stays full-height. Multiples-of-4 spacing.
        rt.offsetMin = new Vector2(12f, 0f);
        rt.offsetMax = new Vector2(-12f, 0f);

        var tmp = go.GetComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = LabelSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = color;
        tmp.characterSpacing = LabelSpacing;
        tmp.enableWordWrapping = false; // keep "WhatsApp"/"Telegram" on one line
        tmp.raycastTarget = false;
        return tmp;
    }

    // ── Navigation restructure ──────────────────────────────────────────────

    private static void RestructureNav()
    {
        var tabManager = UnityEngine.Object.FindFirstObjectByType<BottomTabManager>(FindObjectsInactive.Include);
        if (tabManager == null)
            throw new InvalidOperationException("[ChannelSwitcherBuilder] BottomTabManager not found — is Main.unity open?");

        // ── Guarded Telegram-tab removal (T-06-06) ──
        // Only delete index 1 when it is verifiably the Telegram tab. After a prior
        // run the array is 4 long and tabs[1] is «Сводка» — the guard skips it.
        GameObject telegramTabRoot = null;
        GameObject telegramScreen = null;

        var soDel = new SerializedObject(tabManager);
        var tabsDel = soDel.FindProperty("tabs");
        if (tabsDel == null || !tabsDel.isArray)
            throw new InvalidOperationException("[ChannelSwitcherBuilder] BottomTabManager.tabs property not found.");

        if (tabsDel.arraySize >= 2)
        {
            var tab1 = tabsDel.GetArrayElementAtIndex(1);
            string tab1Name = tab1.FindPropertyRelative("tabName").stringValue;
            telegramScreen = tab1.FindPropertyRelative("screenPanel").objectReferenceValue as GameObject;
            telegramTabRoot = tab1.FindPropertyRelative("tabRoot").objectReferenceValue as GameObject;

            bool isTelegram = tab1Name == "Telegram"
                              || (telegramScreen != null && telegramScreen.name == "Screen_Telegram");

            if (isTelegram)
            {
                int before = tabsDel.arraySize;
                tabsDel.DeleteArrayElementAtIndex(1);
                soDel.ApplyModifiedPropertiesWithoutUndo();

                // TabData is a managed [Serializable] class, so one delete suffices —
                // but defensively re-check against the object-reference delete-twice quirk.
                soDel.Update();
                if (tabsDel.arraySize == before)
                {
                    tabsDel.DeleteArrayElementAtIndex(1);
                    soDel.ApplyModifiedPropertiesWithoutUndo();
                }
                Debug.Log($"[ChannelSwitcherBuilder] Telegram tab removed: tabs {before} → {tabsDel.arraySize}.");
            }
            else
            {
                // Not the Telegram tab — already restructured. Do NOT delete anything.
                telegramTabRoot = null;
                telegramScreen = null;
                Debug.Log("[ChannelSwitcherBuilder] Telegram tab already removed — skipping tab deletion.");
            }
        }

        // ── Relabel tab 0 «Чаты» ── (index 0 is unaffected by the index-1 delete)
        var soLbl = new SerializedObject(tabManager);
        var tabsLbl = soLbl.FindProperty("tabs");
        TextMeshProUGUI tab0Label = null;
        if (tabsLbl.arraySize >= 1)
        {
            var tab0 = tabsLbl.GetArrayElementAtIndex(0);
            tab0.FindPropertyRelative("tabName").stringValue = "Чаты";
            tab0Label = tab0.FindPropertyRelative("labelText").objectReferenceValue as TextMeshProUGUI;
            soLbl.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorUtility.SetDirty(tabManager);

        // ── Delete the captured GameObjects (list no longer references them) ──
        if (telegramScreen != null) UnityEngine.Object.DestroyImmediate(telegramScreen);
        if (telegramTabRoot != null) UnityEngine.Object.DestroyImmediate(telegramTabRoot);

        // ── Update tab 0's scene label TMP text (NavRestructureBuilder idiom) ──
        if (tab0Label != null)
        {
            var lso = new SerializedObject(tab0Label);
            lso.FindProperty("m_text").stringValue = "Чаты";
            lso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(tab0Label);
        }
        else
        {
            Debug.LogWarning("[ChannelSwitcherBuilder] tabs[0].labelText is null — «Чаты» label text not updated.");
        }
    }

    // ── Helpers (verbatim ReplyModeToggleBuilder idioms) ────────────────────

    private static void SetRef(SerializedObject so, string property, UnityEngine.Object value)
    {
        var prop = so.FindProperty(property);
        if (prop != null) prop.objectReferenceValue = value;
        else Debug.LogWarning($"[ChannelSwitcherBuilder] Binder property '{property}' not found.");
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
    // silently fails (project memory: roundedcorners-assembly). Scan every loaded
    // assembly for the type by name to avoid a hard editor dependency.
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
            Debug.LogWarning("[ChannelSwitcherBuilder] ImageWithRoundedCorners not found — corners will be square.");
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

#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// The learning layer of the four «Авто» capsules (spec 2026-10-22-learning-progress-percent §4.3–4.4). ADDITIVE and
/// idempotent: it authors the objects AutoChipProgress and ReplyModeToggleBinder.PaintCaption paint, and nothing else.
///
///   &lt;capsule root&gt;          AutoPill (bot card) · AutoButton (chats header) · AutoChip (sheet row) · SemiAutoToggle (chat)
///     Pill                   + AutoChipProgress (clip, fill, baseFill)
///       Ring
///       Fill                 the capsule's face — the owner's serialized ref; the Pill is found through it, never by name
///       LearnClip            RectMask2D pinned to the face's left edge, the face's height; its width is the runtime's
///                            fraction × the face's width (authored 0)
///         LearnFill          the face's radius and full inner size, FIXED — only the clip resizes. No rounded image ever
///                            resizes with the percent: under the sheet's stencil Mask Nobi's corners lag behind a resize
///                            (RoundedCornerMaskSync), which is also why the cut is a RectMask2D, never a stencil Mask
///       Content              lamp + label, above the fill
///     LearnCaption           «Учится · N %» under the pill — the bot card and the chats header only, authored hidden;
///                            PaintCaption writes its text and shows it. ThemedColor(InkTertiary) owns its colour
///
/// Places: Bot.prefab and BotSwitcherRow.prefab, and in Main.unity the chats-header AutoButton and the open chat's
/// SemiAutoToggle (the only ReplyModeToggleBinder and the only SemiAutoToggle). BotCardAutoPillBuilder and
/// ChatsTopBarRestyleBuilder rebuild those capsules wholesale and call the Wire* entry points right after, so a re-run of
/// either never strips the layer — though both are destructive and must not be run against the hand-tuned scene.
///
/// Every object this tool creates is found again by its own name and every value is compared before it is written: a second
/// run changes nothing and saves nothing — the scene especially, since a save re-mints every Nobi material in it. Geometry
/// is in 1080×1920 canvas reference units.
/// </summary>
public static class AutoChipLearnLayerBuilder
{
    // CaptionGap is the caption's anchoredPosition (0, +2) with a TOP pivot on the capsule root's bottom edge, so the caption box
    // starts 2u INSIDE that edge (the root is the 96u hit area: 10u under the visual pill, so the box starts 8u under the pill).
    // AutoChipLearnLayerPremiseTests adds the gap downward instead — it over-estimates by 4u, which only makes its check conservative.
    public const float CaptionGap = 2f, CaptionFontSize = 28f;
    public static readonly Vector2 CaptionSize = new Vector2(220f, 34f);

    private const string Tag = "[AutoChipLearnLayerBuilder]";
    private const string ScenePath = "Assets/Scenes/Main.unity";
    private const string BotPrefabPath = "Assets/Prefabs/Bot.prefab";
    private const string RowPrefabPath = "Assets/Prefabs/BotSwitcherRow.prefab";

    private const string FaceName = "Fill";
    private const string ClipName = "LearnClip";
    private const string FillName = "LearnFill";
    private const string CaptionName = "LearnCaption";

    // The caption's authored text only previews it in the Scene view — it is authored hidden, and PaintCaption writes the
    // bot's own percent before it ever shows it.
    private const int PreviewPercent = 45;

    private static readonly Vector2 LeftMiddle = new Vector2(0f, 0.5f);
    private static readonly Vector2 BottomCentre = new Vector2(0.5f, 0f);
    private static readonly Vector2 TopCentre = new Vector2(0.5f, 1f);

    private static Type cachedRoundedType;

    private delegate void WireStep<in T>(T owner, ref bool modified);

    // ---- entry points ----

    [MenuItem("Tools/Learning/Add Progress Fill To Auto Chips")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError($"{Tag} Edit Mode only — leave Play mode first.");
            return;
        }
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.isLoaded)
        {
            Debug.LogError($"{Tag} Open {ScenePath} first.");
            return;
        }
        Apply(scene);
        Debug.Log($"{Tag} Build complete.");
    }

    /// <summary>Batch entry (Editor closed): Tools/run-editor-builder.sh AutoChipLearnLayerBuilder.BuildHeadless</summary>
    public static void BuildHeadless()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath);
        Apply(scene);
        Debug.Log($"{Tag} Headless build + save complete.");
    }

    /// <summary>
    /// Lays the learning fill over <paramref name="pill"/>'s face (its «Fill» child) and returns the layer. A second call finds
    /// the first one's objects and changes nothing.
    /// </summary>
    public static AutoChipProgress EnsureLayer(Transform pill)
    {
        bool modified = false;
        return EnsureLayer(FaceOf(pill), ref modified);
    }

    /// <summary>The «Учится · N %» caption under the capsule whose root is <paramref name="chipRoot"/>, in the label's font.</summary>
    public static TextMeshProUGUI EnsureCaption(Transform chipRoot, TMP_FontAsset font)
    {
        bool modified = false;
        return EnsureCaption(chipRoot, font, ref modified);
    }

    public static void WireBotCard(Bot bot)
    {
        bool modified = false;
        WireBotCard(bot, ref modified);
    }

    public static void WireHeader(ReplyModeToggleBinder binder)
    {
        bool modified = false;
        WireHeader(binder, ref modified);
    }

    public static void WireSwitcherRow(BotSwitcherRowView row)
    {
        bool modified = false;
        WireSwitcherRow(row, ref modified);
    }

    public static void WireConversationChip(SemiAutoToggle chip)
    {
        bool modified = false;
        WireConversationChip(chip, ref modified);
    }

    // ---- the four places ----

    // Both prefabs, then the scene; each is saved only when something in it changed.
    private static void Apply(Scene scene)
    {
        WirePrefab<Bot>(BotPrefabPath, WireBotCard);
        WirePrefab<BotSwitcherRowView>(RowPrefabPath, WireSwitcherRow);

        bool modified = false;
        WireHeader(Single<ReplyModeToggleBinder>(scene), ref modified);
        WireConversationChip(Single<SemiAutoToggle>(scene), ref modified);
        if (!modified)
        {
            Debug.Log($"{Tag} {ScenePath}: already applied — not saved.");
            return;
        }
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException($"{Tag} Could not save {ScenePath}.");
        Debug.Log($"{Tag} {ScenePath}: the header and chat capsules updated and saved.");
    }

    private static void WirePrefab<T>(string path, WireStep<T> wire) where T : Component
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var owner = root.GetComponent<T>();
            if (owner == null) throw new InvalidOperationException($"{Tag} {path}: no {typeof(T).Name} on the prefab root.");
            bool modified = false;
            wire(owner, ref modified);
            if (!modified)
            {
                Debug.Log($"{Tag} {path}: already applied — not saved.");
                return;
            }
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
            if (!saved) throw new InvalidOperationException($"{Tag} Could not save {path}.");
            Debug.Log($"{Tag} {path}: updated and saved.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void WireBotCard(Bot bot, ref bool modified)
    {
        var owner = new SerializedObject(bot);
        var face = Ref<Image>(owner, "autoPillFill");
        AutoChipProgress progress = EnsureLayer(face.rectTransform, ref modified);
        TextMeshProUGUI caption = EnsureCaption(ChipRootOf(face), Ref<TextMeshProUGUI>(owner, "autoPillLabel").font, ref modified);
        modified |= SetRef(owner, "autoPillProgress", progress);
        modified |= SetRef(owner, "learnCaption", caption);
    }

    private static void WireHeader(ReplyModeToggleBinder binder, ref bool modified)
    {
        var owner = new SerializedObject(binder);
        var face = Ref<Image>(owner, "fillImage");
        AutoChipProgress progress = EnsureLayer(face.rectTransform, ref modified);
        TextMeshProUGUI caption = EnsureCaption(ChipRootOf(face), Ref<TextMeshProUGUI>(owner, "label").font, ref modified);
        modified |= SetRef(owner, "progress", progress);
        modified |= SetRef(owner, "learnCaption", caption);
    }

    // The sheet row and the chat chip are too small for a caption (spec §4.4): the fill only.
    private static void WireSwitcherRow(BotSwitcherRowView row, ref bool modified)
    {
        var owner = new SerializedObject(row);
        AutoChipProgress progress = EnsureLayer(Ref<Image>(owner, "chipFill").rectTransform, ref modified);
        modified |= SetRef(owner, "chipProgress", progress);
    }

    private static void WireConversationChip(SemiAutoToggle chip, ref bool modified)
    {
        var owner = new SerializedObject(chip);
        AutoChipProgress progress = EnsureLayer(Ref<Image>(owner, "fillImage").rectTransform, ref modified);
        modified |= SetRef(owner, "progress", progress);
    }

    // ---- the layer ----

    private static AutoChipProgress EnsureLayer(RectTransform face, ref bool modified)
    {
        if (!(face.parent is RectTransform pill)) throw new InvalidOperationException($"{Tag} {face.name} has no Pill above it.");
        float inset = RingInset(face);
        float radius = RadiusOf(face.gameObject);
        Vector2 inner = InnerSize(face, pill);

        RectTransform clip = EnsureChild(pill, ClipName, ref modified, typeof(RectMask2D));
        modified |= PlaceAfter(clip, face);
        modified |= SetRect(clip, Vector2.zero, Vector2.up, LeftMiddle, new Vector2(inset, 0f), new Vector2(0f, -2f * inset));

        RectTransform fillRect = EnsureChild(clip, FillName, ref modified, typeof(CanvasRenderer), typeof(Image));
        var fill = fillRect.GetComponent<Image>();
        modified |= SetFillImage(fill);
        modified |= EnsureRounded(fill, radius);
        modified |= SetRect(fillRect, LeftMiddle, LeftMiddle, LeftMiddle, Vector2.zero, inner);

        var progress = pill.GetComponent<AutoChipProgress>();
        if (progress == null)
        {
            progress = pill.gameObject.AddComponent<AutoChipProgress>();
            modified = true;
        }
        var layer = new SerializedObject(progress);
        modified |= SetRef(layer, "clip", clip);
        modified |= SetRef(layer, "fill", fill);
        modified |= SetRef(layer, "baseFill", face);
        return progress;
    }

    // The face is its Pill inset evenly by the ring: the clip starts at that inset and spans the face's height.
    private static float RingInset(RectTransform face)
    {
        Vector2 inset = face.offsetMin;
        bool even = face.anchorMin == Vector2.zero && face.anchorMax == Vector2.one && face.offsetMax == -inset
                    && Mathf.Approximately(inset.x, inset.y);
        if (!even)
            throw new InvalidOperationException(
                $"{Tag} {face.parent.name}/{face.name} is no longer its Pill inset evenly by the ring — the learning fill's geometry assumes it.");
        return inset.x;
    }

    // The face's size in the running app. The card's and the sheet row's capsule roots are sized by a layout group, and a
    // prefab keeps a layout-driven rect as it was last laid out — Bot.prefab's AutoPill reads 100×100 while the app lays it out
    // at its LayoutElement's 190×96 — so the size follows the layout's intent, root → Pill → face, never the stale rect.
    // AutoChipProgress refits the fill to the live face anyway; this is the size it is authored at.
    private static Vector2 InnerSize(RectTransform face, RectTransform pill)
    {
        if (!(pill.parent is RectTransform root)) throw new InvalidOperationException($"{Tag} {pill.name} has no capsule root above it.");
        return SizeIn(face, SizeIn(pill, LaidOutSize(root)));
    }

    private static Vector2 LaidOutSize(RectTransform rect)
    {
        Vector2 size = rect.rect.size;
        var group = rect.parent != null ? rect.parent.GetComponent<HorizontalOrVerticalLayoutGroup>() : null;
        var element = rect.GetComponent<LayoutElement>();
        if (group == null || element == null || element.ignoreLayout) return size;
        if (group.childControlWidth && element.preferredWidth > 0f) size.x = element.preferredWidth;
        if (group.childControlHeight && element.preferredHeight > 0f) size.y = element.preferredHeight;
        return size;
    }

    private static Vector2 SizeIn(RectTransform rect, Vector2 parentSize) =>
        Vector2.Scale(parentSize, rect.anchorMax - rect.anchorMin) + rect.sizeDelta;

    // The authored colour only previews the fill in the Scene view (the clip is authored 0 wide); AutoChipProgress paints
    // the role the capsule's state asks for. Light, like the rest of the chip's authored state.
    private static bool SetFillImage(Image fill)
    {
        Color preview = Theme.Light.Resolve(ThemeRole.LearnFillOff);
        if (fill.color == preview && !fill.raycastTarget && fill.maskable) return false;
        fill.color = preview;
        fill.raycastTarget = false;   // the fill never takes a tap
        fill.maskable = true;         // a RectMask2D clips only a maskable graphic
        return true;
    }

    // ---- the caption ----

    private static TextMeshProUGUI EnsureCaption(Transform chipRoot, TMP_FontAsset font, ref bool modified)
    {
        if (chipRoot == null) throw new ArgumentNullException(nameof(chipRoot));
        if (font == null) throw new InvalidOperationException($"{Tag} {chipRoot.name}: the capsule's label has no font to give its caption.");

        RectTransform rect = EnsureChild(chipRoot, CaptionName, ref modified,
            typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(ThemedColor));
        if (rect.GetSiblingIndex() != chipRoot.childCount - 1)
        {
            rect.SetAsLastSibling();   // under the pill, drawn after it
            modified = true;
        }
        var caption = rect.GetComponent<TextMeshProUGUI>();
        // Top pivot at the root's bottom edge, raised by CaptionGap: the box starts CaptionGap INSIDE that edge (see CaptionGap).
        modified |= SetRect(rect, BottomCentre, BottomCentre, TopCentre, new Vector2(0f, CaptionGap), CaptionSize);
        modified |= SetCaptionText(caption, font);
        modified |= BindTheme(rect.GetComponent<ThemedColor>(), ThemeRole.InkTertiary, caption);
        if (rect.gameObject.activeSelf)
        {
            rect.gameObject.SetActive(false);   // PaintCaption shows it
            modified = true;
        }
        return caption;
    }

    private static bool SetCaptionText(TextMeshProUGUI caption, TMP_FontAsset font)
    {
        string preview = AutoChipLook.Caption(PreviewPercent);
        Color ink = Theme.Light.Resolve(ThemeRole.InkTertiary);
        if (caption.font == font && caption.text == preview && Mathf.Approximately(caption.fontSize, CaptionFontSize)
            && caption.fontStyle == FontStyles.Normal && caption.alignment == TextAlignmentOptions.Center
            && caption.textWrappingMode == TextWrappingModes.NoWrap && !caption.enableAutoSizing && !caption.raycastTarget
            && caption.color == ink)
            return false;
        caption.font = font;
        caption.text = preview;
        caption.fontSize = CaptionFontSize;
        caption.fontStyle = FontStyles.Normal;
        caption.alignment = TextAlignmentOptions.Center;
        caption.textWrappingMode = TextWrappingModes.NoWrap;
        caption.enableAutoSizing = false;
        caption.raycastTarget = false;   // the bot card is a button: the caption never takes its taps
        caption.color = ink;             // opaque — the ThemedColor binding keeps the authored alpha
        return true;
    }

    // Role, target and alpha through SerializedObject only — ThemedColor.Configure would repaint at once from whatever theme
    // this editor happens to be in.
    private static bool BindTheme(ThemedColor themed, ThemeRole role, Graphic target)
    {
        var binding = new SerializedObject(themed);
        SerializedProperty roleProp = binding.FindProperty("role");
        SerializedProperty targetProp = binding.FindProperty("target");
        SerializedProperty alphaProp = binding.FindProperty("preserveAlpha");
        if (roleProp.enumValueIndex == (int)role && targetProp.objectReferenceValue == target && alphaProp.boolValue) return false;
        roleProp.enumValueIndex = (int)role;
        targetProp.objectReferenceValue = target;
        alphaProp.boolValue = true;
        binding.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    // ---- rounded corners ----

    // RoundedCorners ships in its own UPM assembly — the type is looked up across the loaded assemblies, as
    // BotCardAutoPillBuilder and ChatsTopBarRestyleBuilder do (a Type.GetType without the assembly name silently returns null
    // and the image renders square). Not finding it is an error here, never a square fill.
    private static Type RoundedType()
    {
        if (cachedRoundedType != null) return cachedRoundedType;

        const string fullName = "Nobi.UiRoundedCorners.ImageWithRoundedCorners";
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type = assembly.GetType(fullName);
            if (type == null) continue;
            cachedRoundedType = type;
            return type;
        }
        throw new InvalidOperationException($"{Tag} {fullName} not found — the learning fill would render square.");
    }

    private static float RadiusOf(GameObject face)
    {
        Component corners = face.GetComponent(RoundedType());
        if (corners == null)
            throw new InvalidOperationException($"{Tag} {face.name} carries no ImageWithRoundedCorners — the fill takes the face's own radius.");
        return new SerializedObject(corners).FindProperty("radius").floatValue;
    }

    // Both fields through one SerializedObject — "image" is private, and Nobi's own Validate sets it only on enable.
    private static bool EnsureRounded(Image image, float radius)
    {
        bool modified = false;
        Component corners = image.GetComponent(RoundedType());
        if (corners == null)
        {
            corners = image.gameObject.AddComponent(RoundedType());
            modified = true;
        }
        var rounded = new SerializedObject(corners);
        SerializedProperty radiusProp = rounded.FindProperty("radius");
        SerializedProperty imageProp = rounded.FindProperty("image");
        if (Mathf.Approximately(radiusProp.floatValue, radius) && imageProp.objectReferenceValue == image) return modified;
        radiusProp.floatValue = radius;   // 1:1 with the visual radius — never halved or doubled (CLAUDE.md)
        imageProp.objectReferenceValue = image;
        rounded.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    // ---- helpers ----

    private static RectTransform FaceOf(Transform pill)
    {
        if (pill == null) throw new ArgumentNullException(nameof(pill));
        if (!(pill.Find(FaceName) is RectTransform face) || face.GetComponent<Image>() == null)
            throw new InvalidOperationException($"{Tag} {pill.name} has no «{FaceName}» image to lay the learning fill over.");
        return face;
    }

    private static Transform ChipRootOf(Graphic face)
    {
        Transform pill = face.transform.parent;
        if (pill == null || pill.parent == null) throw new InvalidOperationException($"{Tag} {face.name} is not inside a capsule.");
        return pill.parent;
    }

    private static T Single<T>(Scene scene) where T : Component
    {
        T[] found = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(component => component.gameObject.scene == scene).ToArray();
        if (found.Length != 1)
            throw new InvalidOperationException($"{Tag} Expected exactly one {typeof(T).Name} in {ScenePath}, found {found.Length}.");
        return found[0];
    }

    private static RectTransform EnsureChild(Transform parent, string name, ref bool modified, params Type[] components)
    {
        var child = parent.Find(name) as RectTransform;
        if (child == null)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = parent.gameObject.layer };
            child = (RectTransform)go.transform;
            child.SetParent(parent, false);
            modified = true;
        }
        foreach (Type component in components)
        {
            if (child.GetComponent(component) != null) continue;
            child.gameObject.AddComponent(component);
            modified = true;
        }
        return child;
    }

    // Moves item to right after anchor (both children of one parent).
    private static bool PlaceAfter(Transform item, Transform anchor)
    {
        int index = item.GetSiblingIndex();
        int target = anchor.GetSiblingIndex() + (index < anchor.GetSiblingIndex() ? 0 : 1);
        if (index == target) return false;
        item.SetSiblingIndex(target);
        return true;
    }

    private static bool SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
    {
        if (rect.anchorMin == anchorMin && rect.anchorMax == anchorMax && rect.pivot == pivot
            && rect.anchoredPosition == position && rect.sizeDelta == size)
            return false;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return true;
    }

    private static T Ref<T>(SerializedObject owner, string field) where T : Object
    {
        SerializedProperty property = owner.FindProperty(field);
        if (property == null)
            throw new InvalidOperationException($"{Tag} {owner.targetObject.GetType().Name} has no serialized field '{field}'.");
        if (!(property.objectReferenceValue is T value))
            throw new InvalidOperationException(
                $"{Tag} {owner.targetObject.name}: {owner.targetObject.GetType().Name}.{field} is not wired — the capsule's own builder must run first.");
        return value;
    }

    private static bool SetRef(SerializedObject owner, string field, Object value)
    {
        SerializedProperty property = owner.FindProperty(field);
        if (property == null)
            throw new InvalidOperationException($"{Tag} {owner.targetObject.GetType().Name} has no serialized field '{field}'.");
        if (property.objectReferenceValue == value) return false;
        property.objectReferenceValue = value;
        owner.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }
}
#endif

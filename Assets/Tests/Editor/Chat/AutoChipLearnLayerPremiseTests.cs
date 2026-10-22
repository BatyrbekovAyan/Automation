using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nobi.UiRoundedCorners;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pins the learning layer that Tools/Learning/Add Progress Fill To Auto Chips (AutoChipLearnLayerBuilder) authors into the
/// four «Авто» capsules (spec 2026-10-22-learning-progress-percent §4.3–4.4) — the fill AutoChipProgress sizes at runtime and
/// the «Учится · N %» caption PaintCaption shows:
/// <list type="bullet">
///   <item>Pill: Ring, Fill, LearnClip, Content — the fill lies over the capsule's face and under its lamp and label;</item>
///   <item>LearnClip: a RectMask2D pinned to the face's left edge (never a stencil Mask: Nobi's corners lag under one);</item>
///   <item>LearnFill: the face's own radius and full inner size, fixed — only the clip ever resizes;</item>
///   <item>LearnCaption: under the card's and the header's capsule only, hidden until PaintCaption shows it.</item>
/// </list>
/// A stale builder or a destructive re-run of an older chip builder fails here, not on device. The prefabs are loaded with
/// PrefabUtility.LoadPrefabContents; the scene is read as raw YAML, like ConfirmCardScenePremiseTests, so no test ever opens
/// Main.unity (which would wake every [ExecuteInEditMode] component in it).
/// </summary>
public class AutoChipLearnLayerPremiseTests
{
    private const string BotPrefabPath = "Assets/Prefabs/Bot.prefab";
    private const string RowPrefabPath = "Assets/Prefabs/BotSwitcherRow.prefab";
    private const string ScenePath = "Assets/Scenes/Main.unity";

    // The bot card is 232u with its 76u pill centred: 78u of card under the pill (spec §4.3).
    private const float FreeUnderCardPill = 78f;
    // The header: 64u between the pill and the channel well — the caption must stay clear of the well (spec §4.3).
    private const float FreeAboveChannelWell = 64f;
    // The caption hangs off the 96u hit area, whose bottom edge sits (96 − 76) / 2 = 10u under the visual pill.
    private const float HitAreaUnderPill = 10f;
    // Every capsule's face (Fill) is its Pill inset by the 3u ring.
    private const float RingInset = 3f;

    private static readonly string[] PillOrder = { "Ring", "Fill", "LearnClip", "Content" };
    private static readonly Vector2 WideInner = new Vector2(184f, 70f);   // card and header: a 190×76 pill
    private static readonly Vector2 SlimInner = new Vector2(164f, 54f);   // sheet row and chat: a 170×60 pill

    // ---- the bot card ----

    [Test]
    public void BotPrefab_AutoPill_HasTheLearnLayer_AndTheCaption()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(BotPrefabPath);
        try
        {
            var card = new SerializedObject(root.GetComponent<Bot>());
            var face = card.FindProperty("autoPillFill").objectReferenceValue as Image;
            var label = card.FindProperty("autoPillLabel").objectReferenceValue as TextMeshProUGUI;
            var button = card.FindProperty("autoPillButton").objectReferenceValue as Button;
            Assert.IsNotNull(face, "Bot.autoPillFill — the capsule BotCardAutoPillBuilder builds");
            Assert.IsNotNull(label, "Bot.autoPillLabel");
            Assert.IsNotNull(button, "Bot.autoPillButton");

            AutoChipProgress progress = AssertLayer(face, WideInner, 35f, "Bot.prefab");
            Assert.AreSame(progress, card.FindProperty("autoPillProgress").objectReferenceValue,
                "Bot.autoPillProgress is the capsule's own learning layer");

            AssertCaption(card.FindProperty("learnCaption").objectReferenceValue as TextMeshProUGUI, button.transform, label, "Bot.prefab");
            Assert.LessOrEqual(AutoChipLearnLayerBuilder.CaptionGap + AutoChipLearnLayerBuilder.CaptionSize.y + HitAreaUnderPill,
                FreeUnderCardPill, "the caption fits the 78u of card under the capsule — the card does not grow, the capsule does not move");

            Transform[] labels = button.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Label").ToArray();
            Assert.AreEqual(1, labels.Length,
                "BotCardModelTests finds the capsule's «Label» depth-first under AutoPill — no other object may carry that name");
            Assert.AreSame(label.transform, labels[0], "the only «Label» is the capsule's own");
            Assert.AreEqual("Content", labels[0].parent.name, "no object named «Label» outside Content");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ---- the bot-switcher sheet's row ----

    [Test]
    public void SwitcherRowPrefab_Chip_HasTheLearnLayer_AndNoCaption()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(RowPrefabPath);
        try
        {
            var row = new SerializedObject(root.GetComponent<BotSwitcherRowView>());
            var face = row.FindProperty("chipFill").objectReferenceValue as Image;
            Assert.IsNotNull(face, "BotSwitcherRowView.chipFill — the chip ChatsTopBarRestyleBuilder builds");

            AutoChipProgress progress = AssertLayer(face, SlimInner, 27f, "BotSwitcherRow.prefab");
            Assert.AreSame(progress, row.FindProperty("chipProgress").objectReferenceValue,
                "BotSwitcherRowView.chipProgress is the chip's own learning layer");
            Assert.IsFalse(root.GetComponentsInChildren<Transform>(true).Any(t => t.name == "LearnCaption"),
                "the sheet row is too small for a caption — it gets the fill only (spec §4.4)");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ---- the chats header and the open chat (Main.unity) ----

    [Test]
    public void Scene_HeaderPill_AndConversationChip_HaveTheLearnLayer()
    {
        SceneYaml scene = SceneYaml.Load(ScenePath);
        string binder = scene.Single(ScriptGuid(typeof(ReplyModeToggleBinder)), "ReplyModeToggleBinder (the chats-header «Авто»)");
        string chat = scene.Single(ScriptGuid(typeof(SemiAutoToggle)), "SemiAutoToggle (the open chat's «Авто»)");

        AssertLayer(scene, binder, "fillImage", "progress", WideInner, 35f, "header");
        AssertLayer(scene, chat, "fillImage", "progress", SlimInner, 27f, "chat");

        string caption = scene.Ref(binder, "learnCaption");
        Assert.IsNotNull(caption, "ReplyModeToggleBinder.learnCaption is wired");
        Assert.AreEqual(ScriptGuid(typeof(TextMeshProUGUI)), scene.ScriptGuidOf(caption), "the caption is a TMP label");
        string captionGo = scene.GameObjectOf(caption);
        string captionRect = scene.RectOf(captionGo);
        Assert.AreEqual("LearnCaption", scene.NameOf(captionGo));
        Assert.AreEqual(scene.RectOf(scene.GameObjectOf(binder)), scene.Parent(captionRect), "a child of AutoButton, under the pill");
        Assert.IsFalse(scene.IsActive(captionGo), "hidden by default — PaintCaption shows it");
        Assert.AreEqual("0", scene.Field(caption, "m_RaycastTarget"), "the caption never takes the button's taps");
        Assert.AreEqual(scene.Field(scene.Ref(binder, "label"), "m_fontAsset"), scene.Field(caption, "m_fontAsset"),
            "the capsule label's font");
        Assert.AreEqual(AutoChipLearnLayerBuilder.CaptionFontSize, scene.Float(caption, "m_fontSize"), 0.001f);
        Assert.AreEqual(new Vector2(0.5f, 0f), scene.Vec(captionRect, "m_AnchorMin"));
        Assert.AreEqual(new Vector2(0.5f, 0f), scene.Vec(captionRect, "m_AnchorMax"));
        Assert.AreEqual(new Vector2(0.5f, 1f), scene.Vec(captionRect, "m_Pivot"));
        Assert.AreEqual(new Vector2(0f, AutoChipLearnLayerBuilder.CaptionGap), scene.Vec(captionRect, "m_AnchoredPosition"));
        Assert.AreEqual(AutoChipLearnLayerBuilder.CaptionSize, scene.Vec(captionRect, "m_SizeDelta"));
        Assert.LessOrEqual(AutoChipLearnLayerBuilder.CaptionGap + AutoChipLearnLayerBuilder.CaptionSize.y + HitAreaUnderPill,
            FreeAboveChannelWell, "the caption stays clear of the channel well under the header's pill");

        string chatRoot = scene.RectOf(scene.GameObjectOf(chat));
        Assert.IsFalse(scene.Subtree(chatRoot).Any(r => scene.NameOf(scene.GameObjectOf(r)) == "LearnCaption"),
            "the chat chip is too small for a caption — it gets the fill only (spec §4.4)");
    }

    // ---- EnsureLayer ----

    [Test]
    public void EnsureLayer_IsIdempotent()
    {
        var chip = new GameObject("Chip", typeof(RectTransform));
        try
        {
            ((RectTransform)chip.transform).sizeDelta = new Vector2(190f, 96f);
            RectTransform pill = NewRect("Pill", chip.transform);
            pill.anchorMin = new Vector2(0f, 0.5f);
            pill.anchorMax = new Vector2(1f, 0.5f);
            pill.sizeDelta = new Vector2(0f, 76f);
            NewFace("Ring", pill, 0f, 38f);
            Image face = NewFace("Fill", pill, RingInset, 35f);
            NewFace("Content", pill, 0f, 0f);

            AutoChipProgress first = AutoChipLearnLayerBuilder.EnsureLayer(pill);
            AutoChipProgress second = AutoChipLearnLayerBuilder.EnsureLayer(pill);

            Assert.AreSame(first, second, "the second call finds the first call's layer");
            Assert.AreEqual(1, pill.GetComponents<AutoChipProgress>().Length);
            Assert.AreEqual(1, pill.Cast<Transform>().Count(t => t.name == "LearnClip"), "two calls give one LearnClip");
            Assert.AreEqual(1, chip.GetComponentsInChildren<Transform>(true).Count(t => t.name == "LearnFill"));
            AssertLayer(face, WideInner, 35f, "a capsule built in code");
        }
        finally
        {
            Object.DestroyImmediate(chip);
        }
    }

    // ---- the older builders that rebuild a capsule wholesale ----

    // They are destructive and never run against the hand-tuned scene, so only their source can show that a re-run would
    // give the rebuilt capsule its learning layer back.
    [TestCase("Assets/Editor/BotCardAutoPillBuilder.cs", "AutoChipLearnLayerBuilder.WireBotCard(bot);")]
    [TestCase("Assets/Editor/ChatsTopBarRestyleBuilder.cs", "AutoChipLearnLayerBuilder.WireHeader(binder);")]
    [TestCase("Assets/Editor/ChatsTopBarRestyleBuilder.cs", "AutoChipLearnLayerBuilder.WireSwitcherRow(view);")]
    [TestCase("Assets/Editor/ChatsTopBarRestyleBuilder.cs", "AutoChipLearnLayerBuilder.WireConversationChip(toggle);")]
    public void ABuilderThatRebuildsACapsule_GivesItBackItsLearnLayer(string builder, string call)
    {
        StringAssert.Contains(call, File.ReadAllText(builder),
            $"{builder} rebuilds an «Авто» capsule wholesale — it must hand it to AutoChipLearnLayerBuilder right after");
    }

    // ---- shared asserts ----

    private static AutoChipProgress AssertLayer(Image face, Vector2 inner, float radius, string where)
    {
        Transform pill = face.transform.parent;
        CollectionAssert.AreEqual(PillOrder, pill.Cast<Transform>().Select(t => t.name).ToArray(),
            $"{where}: the learning fill lies over the face and under the lamp and the label");
        Assert.AreEqual(new Vector2(RingInset, RingInset), face.rectTransform.offsetMin, $"{where}: the face is its Pill inset by the ring");

        var clip = (RectTransform)pill.GetChild(2);
        Assert.IsNotNull(clip.GetComponent<RectMask2D>(), $"{where}: a RectMask2D cuts the fill");
        Assert.IsNull(clip.GetComponent<Mask>(), $"{where}: never a stencil Mask — Nobi's corners lag behind a resize under one");
        AssertRect(clip, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
            new Vector2(RingInset, 0f), new Vector2(0f, -2f * RingInset), $"{where}: LearnClip");

        Assert.AreEqual(1, clip.childCount, $"{where}: LearnClip holds only the fill");
        var fillRect = (RectTransform)clip.GetChild(0);
        Assert.AreEqual("LearnFill", fillRect.name);
        var fill = fillRect.GetComponent<Image>();
        Assert.IsNotNull(fill, $"{where}: LearnFill is an Image");
        Assert.IsFalse(fill.raycastTarget, $"{where}: the fill never takes a tap");
        Assert.IsTrue(fill.maskable, $"{where}: a RectMask2D clips only a maskable graphic");
        var corners = fillRect.GetComponent<ImageWithRoundedCorners>();
        Assert.IsNotNull(corners, $"{where}: the fill is rounded like the capsule");
        Assert.AreEqual(radius, corners.radius, 0.001f, $"{where}: LearnFill's radius");
        Assert.AreEqual(face.GetComponent<ImageWithRoundedCorners>().radius, corners.radius, 0.001f, $"{where}: the face's own radius");
        AssertRect(fillRect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, inner, $"{where}: LearnFill");

        var progress = pill.GetComponent<AutoChipProgress>();
        Assert.IsNotNull(progress, $"{where}: AutoChipProgress sits on the Pill");
        var layer = new SerializedObject(progress);
        Assert.AreSame(clip, layer.FindProperty("clip").objectReferenceValue, $"{where}: AutoChipProgress.clip");
        Assert.AreSame(fill, layer.FindProperty("fill").objectReferenceValue, $"{where}: AutoChipProgress.fill");
        Assert.AreSame(face.rectTransform, layer.FindProperty("baseFill").objectReferenceValue, $"{where}: AutoChipProgress.baseFill");
        return progress;
    }

    private static void AssertLayer(SceneYaml scene, string owner, string faceField, string progressField, Vector2 inner,
                                    float radius, string where)
    {
        string face = scene.Ref(owner, faceField);
        Assert.IsNotNull(face, $"{where}: {faceField} is wired");
        string faceRect = scene.RectOf(scene.GameObjectOf(face));
        string pill = scene.Parent(faceRect);
        CollectionAssert.AreEqual(PillOrder, scene.Children(pill).Select(r => scene.NameOf(scene.GameObjectOf(r))).ToArray(),
            $"{where}: the learning fill lies over the face and under the lamp and the label");

        string progress = scene.Ref(owner, progressField);
        Assert.IsNotNull(progress, $"{where}: {progressField} is wired");
        Assert.AreEqual(ScriptGuid(typeof(AutoChipProgress)), scene.ScriptGuidOf(progress), $"{where}: {progressField} is an AutoChipProgress");
        Assert.AreEqual(scene.GameObjectOf(pill), scene.GameObjectOf(progress), $"{where}: AutoChipProgress sits on the Pill");
        Assert.AreEqual(faceRect, scene.Ref(progress, "baseFill"), $"{where}: AutoChipProgress.baseFill is the face");

        string clip = scene.Ref(progress, "clip");
        Assert.AreEqual("LearnClip", scene.NameOf(scene.GameObjectOf(clip)), $"{where}: AutoChipProgress.clip");
        Assert.AreEqual(pill, scene.Parent(clip));
        Assert.IsNotNull(scene.Component(scene.GameObjectOf(clip), ScriptGuid(typeof(RectMask2D))), $"{where}: a RectMask2D cuts the fill");

        string fill = scene.Ref(progress, "fill");
        string fillGo = scene.GameObjectOf(fill);
        Assert.AreEqual("LearnFill", scene.NameOf(fillGo), $"{where}: AutoChipProgress.fill");
        Assert.AreEqual(clip, scene.Parent(scene.RectOf(fillGo)), $"{where}: LearnFill lies inside LearnClip");
        Assert.AreEqual(ScriptGuid(typeof(Image)), scene.ScriptGuidOf(fill));
        Assert.AreEqual("0", scene.Field(fill, "m_RaycastTarget"), $"{where}: the fill never takes a tap");
        Assert.AreEqual(inner, scene.Vec(scene.RectOf(fillGo), "m_SizeDelta"), $"{where}: LearnFill is the face's full inner size");
        string corners = scene.Component(fillGo, ScriptGuid(typeof(ImageWithRoundedCorners)));
        Assert.IsNotNull(corners, $"{where}: the fill is rounded like the capsule");
        Assert.AreEqual(radius, scene.Float(corners, "radius"), 0.001f, $"{where}: LearnFill's radius");
    }

    private static void AssertCaption(TextMeshProUGUI caption, Transform chipRoot, TextMeshProUGUI label, string where)
    {
        Assert.IsNotNull(caption, $"{where}: learnCaption is wired");
        Assert.AreEqual("LearnCaption", caption.name);
        Assert.AreSame(chipRoot, caption.transform.parent, $"{where}: a child of the capsule's root, under the pill");
        Assert.IsFalse(caption.gameObject.activeSelf, $"{where}: hidden by default — PaintCaption shows it");
        Assert.IsFalse(caption.raycastTarget, $"{where}: the card is a button — the caption must not take its taps");
        Assert.AreSame(label.font, caption.font, $"{where}: the capsule label's font");
        Assert.AreEqual(AutoChipLearnLayerBuilder.CaptionFontSize, caption.fontSize, 0.001f);
        Assert.AreEqual(TextAlignmentOptions.Center, caption.alignment, $"{where}: centred under the capsule");
        Assert.AreEqual(TextWrappingModes.NoWrap, caption.textWrappingMode, $"{where}: one line");
        var themed = caption.GetComponent<ThemedColor>();
        Assert.IsNotNull(themed, $"{where}: the caption's colour is its ThemedColor binding");
        Assert.AreEqual(ThemeRole.InkTertiary, themed.Role);
        Assert.AreSame(caption, new SerializedObject(themed).FindProperty("target").objectReferenceValue);
        AssertRect(caption.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 1f),
            new Vector2(0f, AutoChipLearnLayerBuilder.CaptionGap), AutoChipLearnLayerBuilder.CaptionSize, $"{where}: LearnCaption");
    }

    private static void AssertRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position,
                                   Vector2 size, string what)
    {
        Assert.AreEqual(anchorMin, rect.anchorMin, $"{what}.anchorMin");
        Assert.AreEqual(anchorMax, rect.anchorMax, $"{what}.anchorMax");
        Assert.AreEqual(pivot, rect.pivot, $"{what}.pivot");
        Assert.AreEqual(position, rect.anchoredPosition, $"{what}.anchoredPosition");
        Assert.AreEqual(size, rect.sizeDelta, $"{what}.sizeDelta");
    }

    // ---- test hierarchy ----

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    // A stretched child inset on every side; rounded when it is a face (radius > 0), a bare rect otherwise.
    private static Image NewFace(string name, RectTransform pill, float inset, float radius)
    {
        RectTransform rect = NewRect(name, pill);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
        if (radius <= 0f) return null;
        var image = rect.gameObject.AddComponent<Image>();
        rect.gameObject.AddComponent<ImageWithRoundedCorners>().radius = radius;
        return image;
    }

    // ---- script identity ----

    private static readonly Dictionary<System.Type, string> Guids = new Dictionary<System.Type, string>();

    // A component's script GUID as the scene stores it in m_Script — resolved from the type, never typed in.
    private static string ScriptGuid(System.Type type)
    {
        if (Guids.TryGetValue(type, out string known)) return known;
        MonoScript script = MonoImporter.GetAllRuntimeMonoScripts().FirstOrDefault(s => s != null && s.GetClass() == type);
        Assert.IsNotNull(script, $"no MonoScript for {type.Name}");
        string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(script));
        Assert.IsNotEmpty(guid, $"no GUID for {type.Name}'s script");
        Guids[type] = guid;
        return guid;
    }

    /// <summary>
    /// Main.unity as raw YAML documents keyed by local fileID — enough to walk GameObjects, their components and the
    /// RectTransform tree, and to read a component's top-level fields.
    /// </summary>
    private sealed class SceneYaml
    {
        private const string MonoBehaviourClass = "114";
        private const string RectTransformClass = "224";

        private readonly Dictionary<string, string> docs = new Dictionary<string, string>();
        private readonly Dictionary<string, string> classes = new Dictionary<string, string>();

        public static SceneYaml Load(string path)
        {
            var yaml = new SceneYaml();
            string text = File.ReadAllText(path);
            MatchCollection headers = Regex.Matches(text, @"^--- !u!(\d+) &(-?\d+)(?: stripped)?\s*$", RegexOptions.Multiline);
            for (int i = 0; i < headers.Count; i++)
            {
                int start = headers[i].Index + headers[i].Length;
                int end = i + 1 < headers.Count ? headers[i + 1].Index : text.Length;
                string id = headers[i].Groups[2].Value;
                yaml.docs[id] = text.Substring(start, end - start);
                yaml.classes[id] = headers[i].Groups[1].Value;
            }
            Assert.Greater(yaml.docs.Count, 1000, $"{path} did not parse — every assertion below would be a false green");
            return yaml;
        }

        public string Single(string scriptGuid, string what)
        {
            string[] hits = docs.Keys.Where(id => classes[id] == MonoBehaviourClass && ScriptGuidOf(id) == scriptGuid).ToArray();
            Assert.AreEqual(1, hits.Length, $"exactly one {what} in the scene");
            return hits[0];
        }

        /// <summary>A top-level field's raw value (two-space indent = the document's own field, never a nested one).</summary>
        public string Field(string id, string name)
        {
            Assert.IsTrue(docs.ContainsKey(id), $"no document {id} in the scene");
            Match match = Regex.Match(docs[id], $@"^  {Regex.Escape(name)}: (.*?)\s*$", RegexOptions.Multiline);
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>The local fileID a reference field points at; null when it is unset ({fileID: 0}) or absent.</summary>
        public string Ref(string id, string name)
        {
            string value = Field(id, name);
            Match match = value != null ? Regex.Match(value, @"^\{fileID: (-?\d+)\}$") : Match.Empty;
            return match.Success && match.Groups[1].Value != "0" ? match.Groups[1].Value : null;
        }

        public string ScriptGuidOf(string id)
        {
            Match match = Regex.Match(docs[id], @"^  m_Script: \{fileID: -?\d+, guid: ([0-9a-f]{32}), type: \d+\}", RegexOptions.Multiline);
            return match.Success ? match.Groups[1].Value : null;
        }

        public string GameObjectOf(string componentId) => Ref(componentId, "m_GameObject");

        public string NameOf(string gameObjectId) => Field(gameObjectId, "m_Name");

        public bool IsActive(string gameObjectId) => Field(gameObjectId, "m_IsActive") == "1";

        public string RectOf(string gameObjectId) => Components(gameObjectId).FirstOrDefault(id => classes[id] == RectTransformClass);

        public string Component(string gameObjectId, string scriptGuid) =>
            Components(gameObjectId).FirstOrDefault(id => classes[id] == MonoBehaviourClass && ScriptGuidOf(id) == scriptGuid);

        public string Parent(string rectId) => Ref(rectId, "m_Father");

        public IEnumerable<string> Children(string rectId)
        {
            string body = docs[rectId];
            int start = body.IndexOf("  m_Children:", System.StringComparison.Ordinal);
            int end = body.IndexOf("  m_Father:", System.StringComparison.Ordinal);
            if (start < 0 || end < start) yield break;
            foreach (Match match in Regex.Matches(body.Substring(start, end - start), @"\{fileID: (-?\d+)\}"))
                yield return match.Groups[1].Value;
        }

        public IEnumerable<string> Subtree(string rectId)
        {
            foreach (string child in Children(rectId))
            {
                yield return child;
                foreach (string below in Subtree(child)) yield return below;
            }
        }

        public Vector2 Vec(string id, string name)
        {
            Match match = Regex.Match(Field(id, name) ?? "", @"^\{x: (-?[\d.eE+-]+), y: (-?[\d.eE+-]+)\}$");
            Assert.IsTrue(match.Success, $"{name} of document {id} is not a vector: '{Field(id, name)}'");
            return new Vector2(ParseFloat(match.Groups[1].Value), ParseFloat(match.Groups[2].Value));
        }

        public float Float(string id, string name)
        {
            string value = Field(id, name);
            Assert.IsNotNull(value, $"{name} missing on document {id}");
            return ParseFloat(value);
        }

        private IEnumerable<string> Components(string gameObjectId)
        {
            foreach (Match match in Regex.Matches(docs[gameObjectId], @"- component: \{fileID: (-?\d+)\}"))
                yield return match.Groups[1].Value;
        }

        private static float ParseFloat(string value) => float.Parse(value, CultureInfo.InvariantCulture);
    }
}

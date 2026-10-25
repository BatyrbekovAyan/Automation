using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Nobi.UiRoundedCorners;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static SceneYaml;

/// <summary>
/// Pins what Tools/Personal Chats/Build (PersonalChatsBuilder) authors for the working chat list (spec §5.2, sketch 009
/// screen 2):
/// <list type="bullet">
///   <item>Main.unity — the «Личные» row, PersonalChatsRow, in the list's Content right after ChatsSearchBar: a chat row's
///   twin (its height, its avatar column, its name font) with the lock (Lock.png, InkTertiary) in a circle, the title, a
///   grey counter and a chevron. Authored hidden — ChatListView shows it when there are personal chats and no search —
///   and wired: ChatListView.personalRow, PersonalChatsRowView's button and counter;</item>
///   <item>ChatItem.prefab — AudienceLock, the lock a personal chat wears in the search results: right before the name,
///   hidden by default, wired to ChatItemView.audienceLock.</item>
/// </list>
/// The builder is additive and idempotent: a second run changes nothing. The prefab is loaded with
/// PrefabUtility.LoadPrefabContents (never saved here); the scene is read as raw YAML, so no test opens Main.unity.
/// </summary>
public class PersonalChatsBuilderPremiseTests
{
    private const string ScenePath = "Assets/Scenes/Main.unity";
    private const string ItemPrefabPath = "Assets/Prefabs/ChatItem.prefab";
    private const string LockPath = "Assets/Images/Icons/Lock.png";

    private readonly List<Object> _created = new List<Object>();

    [TearDown]
    public void DestroyWhatTheTestBuilt()
    {
        foreach (Object created in _created)
            if (created != null) Object.DestroyImmediate(created);
        _created.Clear();
    }

    // ---- Main.unity: the «Личные» row ----

    [Test]
    public void Scene_PersonalChatsRow_SitsRightAfterTheSearchBar()
    {
        SceneYaml scene = SceneYaml.Load(ScenePath);
        string list = scene.Single(ScriptGuid(typeof(ChatListView)), "ChatListView");
        string content = scene.Ref(list, "content");
        Assert.IsNotNull(content, "ChatListView.content is wired");

        string[] names = scene.Children(content).Select(r => scene.NameOf(scene.GameObjectOf(r))).ToArray();
        int bar = System.Array.FindIndex(scene.Children(content).ToArray(),
            r => scene.Component(scene.GameObjectOf(r), ScriptGuid(typeof(ChatSearchBar))) != null);
        Assert.GreaterOrEqual(bar, 0, "the search bar is a child of the list's Content");
        Assert.Greater(names.Length, bar + 1, "nothing after the search bar");
        Assert.AreEqual(PersonalChatsBuilder.RowName, names[bar + 1], "the «Личные» row sits right after the search bar");
        Assert.AreEqual(1, names.Count(n => n == PersonalChatsBuilder.RowName), "one «Личные» row");
    }

    [Test]
    public void Scene_PersonalChatsRow_IsWired_AndAuthoredHidden()
    {
        SceneYaml scene = SceneYaml.Load(ScenePath);
        string rowGo = RowGameObject(scene, out string list);
        string view = scene.Component(rowGo, ScriptGuid(typeof(PersonalChatsRowView)));
        Assert.IsNotNull(view, "PersonalChatsRowView sits on the row");
        Assert.AreEqual(view, scene.Ref(list, "personalRow"), "ChatListView.personalRow is the row's view");

        string button = scene.Ref(view, "button");
        Assert.AreEqual(scene.Component(rowGo, ScriptGuid(typeof(Button))), button, "the whole row is the button");
        Assert.AreEqual(scene.Component(rowGo, ScriptGuid(typeof(Image))), scene.Ref(button, "m_TargetGraphic"),
            "the row's own background takes the press");

        string counter = scene.Ref(view, "counter");
        Assert.IsNotNull(counter, "PersonalChatsRowView.counter is wired");
        Assert.AreEqual("Counter", scene.NameOf(counter));
        Assert.IsTrue(scene.Subtree(scene.RectOf(rowGo)).Contains(scene.RectOf(counter)), "the counter is the row's own");
        string counterText = scene.Ref(view, "counterText");
        Assert.AreEqual(ScriptGuid(typeof(TextMeshProUGUI)), scene.ScriptGuidOf(counterText));
        Assert.AreEqual(scene.RectOf(counter), scene.Parent(scene.RectOf(scene.GameObjectOf(counterText))), "the number sits in the counter");

        Assert.IsFalse(scene.IsActive(rowGo), "hidden by default — ChatListView shows it when there are personal chats");
        Assert.IsFalse(scene.IsActive(counter), "the counter is hidden by default — 0 personal chats with unread");
    }

    [Test]
    public void Scene_PersonalChatsRow_IsAChatRowsTwin()
    {
        SceneYaml scene = SceneYaml.Load(ScenePath);
        string rowGo = RowGameObject(scene, out _);
        ChatItemView item = ItemPrefab();

        string layout = scene.Component(rowGo, ScriptGuid(typeof(LayoutElement)));
        float itemHeight = item.GetComponent<LayoutElement>().preferredHeight;
        Assert.AreEqual(itemHeight, scene.Float(layout, "m_PreferredHeight"), 0.001f, "the row is as tall as a chat row");
        Assert.AreEqual(itemHeight, scene.Float(layout, "m_MinHeight"), 0.001f);

        // the title: «Личные» in the chat name's font
        string title = scene.Component(scene.Named(rowGo, "Title"), ScriptGuid(typeof(TextMeshProUGUI)));
        Assert.AreEqual(PersonalChatsCopy.RowTitle, scene.Text(title, "m_text"), "authored from PersonalChatsCopy");
        Assert.AreEqual(AssetGuid(item.titleText.font), scene.FontGuid(title), "the chat name's font");
        Assert.AreEqual(item.titleText.fontSize, scene.Float(title, "m_fontSize"), 0.001f, "the chat name's size");
        AssertRole(scene, scene.Named(rowGo, "Title"), ThemeRole.InkPrimary, title);

        // the lock in a circle the size of a chat's avatar
        string circleGo = scene.Named(rowGo, "Circle");
        string circleRect = scene.RectOf(circleGo);
        Vector2 avatar = item.avatarImage.rectTransform.sizeDelta;
        Assert.AreEqual(avatar, scene.Vec(circleRect, "m_SizeDelta"), "the circle is a chat avatar's size");
        string corners = scene.Component(circleGo, ScriptGuid(typeof(ImageWithRoundedCorners)));
        Assert.IsNotNull(corners, "the circle is rounded");
        Assert.AreEqual(avatar.x / 2f, scene.Float(corners, "radius"), 0.001f, "…all the way round");
        AssertRole(scene, circleGo, ThemeRole.Hairline, scene.Component(circleGo, ScriptGuid(typeof(Image))));

        string lockGo = scene.Named(rowGo, "Lock");
        Assert.AreEqual(circleRect, scene.Parent(scene.RectOf(lockGo)), "the lock sits in the circle");
        string lockImage = scene.Component(lockGo, ScriptGuid(typeof(Image)));
        Assert.AreEqual(AssetDatabase.AssetPathToGUID(LockPath), scene.SpriteGuid(lockImage), "the app's Lock.png glyph");
        Assert.AreEqual("1", scene.Field(lockImage, "m_PreserveAspect"));
        AssertRole(scene, lockGo, ThemeRole.InkTertiary, lockImage);

        // the grey counter: an unread badge in InkTertiary
        string counterGo = scene.Named(rowGo, "Counter");
        AssertRole(scene, counterGo, ThemeRole.InkTertiary, scene.Component(counterGo, ScriptGuid(typeof(Image))));
        string count = scene.Component(scene.Named(rowGo, "Count"), ScriptGuid(typeof(TextMeshProUGUI)));
        AssertRole(scene, scene.Named(rowGo, "Count"), ThemeRole.AccentOnFill, count);
        Assert.AreEqual(AssetGuid(item.unreadCountText.font), scene.FontGuid(count), "the unread badge's font");

        // the chevron, and the row's raycast stays on its own background
        string chevron = scene.Component(scene.Named(rowGo, "Chevron"), ScriptGuid(typeof(LayoutElement)));
        Assert.IsNotNull(chevron, "a chevron says the row opens something");
        foreach (string graphic in new[] { title, lockImage, count })
            Assert.AreEqual("0", scene.Field(graphic, "m_RaycastTarget"), "only the row's background takes a tap");
    }

    // ---- ChatItem.prefab: the lock of a personal chat in the search results ----

    [Test]
    public void ChatItemPrefab_AudienceLock_IsHiddenBeforeTheName_AndWired()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(ItemPrefabPath);
        try
        {
            var item = root.GetComponent<ChatItemView>();
            var lockGo = new SerializedObject(item).FindProperty("audienceLock")?.objectReferenceValue as GameObject;
            Assert.IsNotNull(lockGo, "ChatItemView.audienceLock is wired");
            Assert.AreEqual(PersonalChatsBuilder.LockName, lockGo.name);
            Assert.IsFalse(lockGo.activeSelf, "hidden by default — ChatListView shows it on a personal chat in the search results");

            Transform name = item.titleText.transform;
            Assert.AreSame(name.parent, lockGo.transform.parent, "the lock is in the name's row");
            Assert.AreEqual(name.GetSiblingIndex() - 1, lockGo.transform.GetSiblingIndex(), "right before the name");

            var slot = lockGo.GetComponent<LayoutElement>();
            Assert.IsNotNull(slot, "the lock takes its place in the row's layout");
            Assert.AreEqual(PersonalChatsBuilder.LockGlyphSize + PersonalChatsBuilder.LockGap, slot.preferredWidth, 0.001f);
            Assert.AreEqual(PersonalChatsBuilder.LockGlyphSize, slot.preferredHeight, 0.001f);

            Image icon = lockGo.GetComponentsInChildren<Image>(true).Single();
            Assert.AreSame(AssetDatabase.LoadAssetAtPath<Sprite>(LockPath), icon.sprite, "the app's Lock.png glyph");
            Assert.IsTrue(icon.preserveAspect);
            Assert.IsFalse(icon.raycastTarget, "the lock never takes the row's tap");
            var themed = icon.GetComponent<ThemedColor>();
            Assert.IsNotNull(themed, "the glyph is tinted by its role, never baked");
            Assert.AreEqual(ThemeRole.InkTertiary, themed.Role);
            Assert.AreSame(icon, new SerializedObject(themed).FindProperty("target").objectReferenceValue);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [Test]
    public void ChatItemPrefab_ASecondRun_ChangesNothing()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(ItemPrefabPath);
        try
        {
            Assert.IsFalse(PersonalChatsBuilder.EnsureChatItemLock(root.GetComponent<ChatItemView>()),
                "the saved prefab already holds everything the builder authors");
            Assert.AreEqual(1, root.GetComponentsInChildren<Transform>(true).Count(t => t.name == PersonalChatsBuilder.LockName));
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ---- EnsureListRow on a list built in code ----

    [Test]
    public void EnsureListRow_BuildsOnce_RightAfterTheSearchBar()
    {
        var panel = new GameObject("ChatsPanel", typeof(RectTransform));
        _created.Add(panel);
        var list = panel.AddComponent<ChatListView>();
        var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup));
        content.transform.SetParent(panel.transform, false);
        var bar = new GameObject("ChatsSearchBar", typeof(RectTransform), typeof(ChatSearchBar));
        bar.transform.SetParent(content.transform, false);
        new GameObject("SomeoneElsesChild", typeof(RectTransform)).transform.SetParent(content.transform, false);
        list.content = content.transform;
        list.prefab = ItemPrefab();

        Assert.IsTrue(PersonalChatsBuilder.EnsureListRow(list), "the first run builds the row");
        Assert.IsFalse(PersonalChatsBuilder.EnsureListRow(list), "a second run changes nothing");

        Transform[] rows = content.transform.Cast<Transform>().Where(t => t.name == PersonalChatsBuilder.RowName).ToArray();
        Assert.AreEqual(1, rows.Length, "one row, however many runs");
        Assert.AreEqual(1, rows[0].GetSiblingIndex(), "right after the search bar, before everyone else");
        var view = rows[0].GetComponent<PersonalChatsRowView>();
        Assert.IsNotNull(view);
        Assert.AreSame(view, new SerializedObject(list).FindProperty("personalRow").objectReferenceValue);
        Assert.IsFalse(rows[0].gameObject.activeSelf, "authored hidden");
    }

    // ---- helpers ----

    private static ChatItemView ItemPrefab()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ItemPrefabPath);
        Assert.IsNotNull(prefab, $"{ItemPrefabPath} moved");
        var item = prefab.GetComponent<ChatItemView>();
        Assert.IsNotNull(item, "ChatItem.prefab carries ChatItemView on its root");
        return item;
    }

    private static string RowGameObject(SceneYaml scene, out string list)
    {
        list = scene.Single(ScriptGuid(typeof(ChatListView)), "ChatListView");
        string content = scene.Ref(list, "content");
        string row = scene.Children(content).FirstOrDefault(r => scene.NameOf(scene.GameObjectOf(r)) == PersonalChatsBuilder.RowName);
        Assert.IsNotNull(row, $"no {PersonalChatsBuilder.RowName} in the list's Content — run Tools/Personal Chats/Build");
        return scene.GameObjectOf(row);
    }

    private static void AssertRole(SceneYaml scene, string gameObject, ThemeRole role, string graphic)
    {
        string themed = scene.Component(gameObject, ScriptGuid(typeof(ThemedColor)));
        Assert.IsNotNull(themed, $"{scene.NameOf(gameObject)}: its colour is a ThemedColor binding");
        Assert.AreEqual(((int)role).ToString(CultureInfo.InvariantCulture), scene.Field(themed, "role"), $"{scene.NameOf(gameObject)}: {role}");
        Assert.AreEqual(graphic, scene.Ref(themed, "target"), $"{scene.NameOf(gameObject)}: the binding paints its own graphic");
    }

    private static string AssetGuid(Object asset) => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));
}

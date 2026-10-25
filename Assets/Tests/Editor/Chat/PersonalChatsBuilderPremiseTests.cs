using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Automation.BotSettingsUI;
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
///   hidden by default, wired to ChatItemView.audienceLock;</item>
///   <item>ChatItem.prefab — MoveButton, the swipe's «В личные» / «В рабочие» (spec §5.2, §5.3): 200u, pinned to the row's
///   right edge right before DeleteButton (both behind the sliding content), an AccentFill fill and «Удалить»'s word style in
///   AccentOnFill, hidden until SwipeToDelete.SetActions shows it, wired to SwipeToDelete.moveButton and ChatItemView's
///   moveButton / moveLabel.</item>
///   <item>Main.unity — the open chat's header (spec §5.5, §5.6): PersonalPill, the «Личный» pill, right after the «Авто»
///   chip in its zone, as tall as the chip and wide enough for its word (the chip's ring-and-fill pill in Border / Hairline,
///   the lock and «Личный» in the chip's word font, InkSecondary), authored hidden; NameTap, a transparent button over the
///   avatar and the name — the
///   zone's last child, ignored by its layout group, never over the back button. MessageHeaderView holds the chip, the pill,
///   its lock, the tap and the scene's SuggestionsController.</item>
///   <item>BotSettings.prefab — the «ЛИЧНЫЕ ЧАТЫ» section of the Основное tab (spec §5.7, sketch 009 screen 7), right above
///   «РАСПИСАНИЕ РАБОТЫ»: the header (a clone of the tab's first one), PersonalChatsRow (the channel rows' card as one button —
///   «Личные чаты», the number, «›») and the hint, PersonalChatsCopy.SettingsHint, its box its measured height; BotSettings'
///   personalChatsRow and personalChatsCount are stamped.</item>
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
            var item = root.GetComponent<ChatItemView>();
            Assert.IsFalse(PersonalChatsBuilder.EnsureChatItemLock(item),
                "the saved prefab already holds everything the builder authors");
            Assert.IsFalse(PersonalChatsBuilder.EnsureChatItemMoveButton(item), "…the move button included");
            Assert.IsFalse(PersonalChatsBuilder.EnsureChatItemSelectCheck(item), "…and the checkbox");
            Assert.AreEqual(1, root.GetComponentsInChildren<Transform>(true).Count(t => t.name == PersonalChatsBuilder.LockName));
            Assert.AreEqual(1, root.GetComponentsInChildren<Transform>(true).Count(t => t.name == PersonalChatsBuilder.MoveButtonName));
            Assert.AreEqual(1, root.GetComponentsInChildren<Transform>(true).Count(t => t.name == PersonalChatsBuilder.SelectCheckName));
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ---- ChatItem.prefab: the swipe's move button («В личные» / «В рабочие») ----

    [Test]
    public void ChatItemPrefab_MoveButton_Is200u_RightBeforeDelete_AndWired()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(ItemPrefabPath);
        try
        {
            var item = root.GetComponent<ChatItemView>();
            SwipeToDelete swipe = item.swipeToDelete;
            Assert.IsNotNull(swipe, "ChatItemView.swipeToDelete is wired");
            var swipeRefs = new SerializedObject(swipe);
            var move = swipeRefs.FindProperty("moveButton")?.objectReferenceValue as RectTransform;
            var delete = swipeRefs.FindProperty("deleteButton")?.objectReferenceValue as RectTransform;
            Assert.IsNotNull(move, "SwipeToDelete.moveButton is wired");
            Assert.IsNotNull(delete, "SwipeToDelete.deleteButton is wired");
            Assert.AreEqual(PersonalChatsBuilder.MoveButtonName, move.name);

            // 200u, right before «Удалить», both behind the sliding content
            Assert.AreEqual(200f, PersonalChatsBuilder.MoveButtonWidth, "200u wide");
            Assert.AreEqual(PersonalChatsBuilder.MoveButtonWidth, move.sizeDelta.x, 0.001f);
            Assert.AreSame(delete.parent, move.parent, "a sibling of «Удалить»");
            Assert.AreEqual(delete.GetSiblingIndex() - 1, move.GetSiblingIndex(), "right before «Удалить»");
            Assert.Less(move.GetSiblingIndex(), swipe.transform.GetSiblingIndex(), "behind the row's sliding content");
            Assert.AreEqual(delete.anchorMin, move.anchorMin, "pinned to the row's right edge like «Удалить»");
            Assert.AreEqual(delete.anchorMax, move.anchorMax);
            Assert.AreEqual(delete.pivot, move.pivot);
            Assert.AreEqual(delete.sizeDelta.y, move.sizeDelta.y, 0.001f, "as tall as «Удалить»");
            Assert.AreEqual(delete.anchoredPosition.x - PersonalChatsBuilder.MoveButtonWidth, move.anchoredPosition.x, 0.001f, "on its left");
            Assert.IsFalse(move.gameObject.activeSelf, "hidden until SwipeToDelete.SetActions shows it");

            // the fill: AccentFill, tinted by its role; pressed like «Удалить»
            var fill = move.GetComponent<Image>();
            Assert.IsNotNull(fill, "the button's fill");
            Assert.IsTrue(fill.raycastTarget, "the fill takes the tap");
            AssertBound(fill, ThemeRole.AccentFill);
            var button = move.GetComponent<Button>();
            Assert.IsNotNull(button);
            Assert.AreSame(fill, button.targetGraphic);
            Assert.AreEqual(item.deleteButton.colors, button.colors, "pressed like «Удалить»");

            // the word: «Удалить»'s font and size, AccentOnFill, never a tap target of its own
            TextMeshProUGUI label = move.GetComponentsInChildren<TextMeshProUGUI>(true).Single();
            TextMeshProUGUI deleteLabel = delete.GetComponentsInChildren<TextMeshProUGUI>(true).Single();
            Assert.AreEqual(PersonalChatsCopy.SwipeToPersonal, label.text, "authored with the working list's word");
            Assert.AreSame(deleteLabel.font, label.font, "«Удалить»'s font");
            Assert.AreEqual(deleteLabel.fontSize, label.fontSize, 0.001f, "«Удалить»'s size");
            Assert.IsFalse(label.raycastTarget);
            AssertBound(label, ThemeRole.AccentOnFill);

            var rowRefs = new SerializedObject(item);
            Assert.AreSame(button, rowRefs.FindProperty("moveButton")?.objectReferenceValue, "ChatItemView.moveButton");
            Assert.AreSame(label, rowRefs.FindProperty("moveLabel")?.objectReferenceValue, "ChatItemView.moveLabel");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [Test]
    public void EnsureChatItemMoveButton_OnARowWithoutIt_BuildsOnce()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(ItemPrefabPath);   // never saved here
        try
        {
            var item = root.GetComponent<ChatItemView>();
            foreach (Transform stale in root.GetComponentsInChildren<Transform>(true).Where(t => t.name == PersonalChatsBuilder.MoveButtonName).ToArray())
                Object.DestroyImmediate(stale.gameObject);

            Assert.IsTrue(PersonalChatsBuilder.EnsureChatItemMoveButton(item), "the first run builds the button");
            Assert.IsFalse(PersonalChatsBuilder.EnsureChatItemMoveButton(item), "a second run changes nothing");

            Transform[] buttons = root.GetComponentsInChildren<Transform>(true).Where(t => t.name == PersonalChatsBuilder.MoveButtonName).ToArray();
            Assert.AreEqual(1, buttons.Length, "one button, however many runs");
            Transform delete = item.deleteButton.transform;
            Assert.AreEqual(delete.GetSiblingIndex() - 1, buttons[0].GetSiblingIndex(), "right before «Удалить»");
            Assert.AreSame(buttons[0], new SerializedObject(item.swipeToDelete).FindProperty("moveButton").objectReferenceValue);
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

    // ---- Main.unity: the open chat's header (spec §5.5, §5.6) ----

    [Test]
    public void Scene_PersonalPill_IsInactive_RightAfterTheChip_AndWired()
    {
        SceneYaml scene = SceneYaml.Load(ScenePath);
        string header = scene.Single(ScriptGuid(typeof(MessageHeaderView)), "MessageHeaderView");
        string chip = scene.Ref(header, "semiAutoToggle");
        Assert.IsNotNull(chip, "MessageHeaderView.semiAutoToggle is wired");
        string controller = scene.Single(ScriptGuid(typeof(SuggestionsController)), "SuggestionsController");
        Assert.AreEqual(scene.Ref(controller, "_toggle"), chip, "the header hides the very chip the suggestions controller drives");
        Assert.AreEqual(controller, scene.Ref(header, "suggestions"), "MessageHeaderView.suggestions — the chat's mode for the sheet's text");

        string pillButton = scene.Ref(header, "personalPill");
        Assert.IsNotNull(pillButton, "MessageHeaderView.personalPill is wired");
        Assert.AreEqual(ScriptGuid(typeof(Button)), scene.ScriptGuidOf(pillButton));
        string pillGo = scene.GameObjectOf(pillButton);
        Assert.AreEqual(PersonalChatsBuilder.PersonalPillName, scene.NameOf(pillGo));
        Assert.IsFalse(scene.IsActive(pillGo), "hidden until the open chat is personal — MessageHeaderView shows it");

        string chipRect = scene.RectOf(scene.GameObjectOf(chip));
        string pillRect = scene.RectOf(pillGo);
        string[] zone = scene.Children(scene.Parent(chipRect)).ToArray();
        Assert.AreEqual(pillRect, zone[System.Array.IndexOf(zone, chipRect) + 1], "right after the chip, in the chip's zone");
        Assert.AreEqual(scene.Vec(chipRect, "m_SizeDelta").y, scene.Vec(pillRect, "m_SizeDelta").y, 0.001f, "as tall as the chip");
        Assert.AreEqual(PersonalChatsBuilder.PersonalPillWidth, scene.Vec(pillRect, "m_SizeDelta").x, 0.001f,
            "wide enough for the lock and «Личный» (Scene_PersonalPill_TheLockAndTheWordFit measures them)");
        Assert.Less(PersonalChatsBuilder.PersonalPillWidth, scene.Vec(scene.Parent(chipRect), "m_SizeDelta").x, "the pill fits the chip's zone");
        Assert.AreEqual(1, scene.Subtree(scene.RectOf(scene.GameObjectOf(header))).Count(r => scene.NameOf(scene.GameObjectOf(r)) == PersonalChatsBuilder.PersonalPillName),
            "one pill");

        // the tap target is the pill's own transparent box, as the chip's is
        string hit = scene.Component(pillGo, ScriptGuid(typeof(Image)));
        Assert.AreEqual(hit, scene.Ref(pillButton, "m_TargetGraphic"));
        Assert.AreEqual("1", scene.Field(hit, "m_RaycastTarget"));
        StringAssert.Contains("a: 0}", scene.Field(hit, "m_Color"), "transparent — the face below is what shows");

        // the face: the chip's ring-and-fill pill in Border / Hairline, the lock and «Личный» in InkSecondary
        AssertRole(scene, scene.Named(pillGo, "Ring"), ThemeRole.Border, scene.Component(scene.Named(pillGo, "Ring"), ScriptGuid(typeof(Image))));
        AssertRole(scene, scene.Named(pillGo, "Fill"), ThemeRole.Hairline, scene.Component(scene.Named(pillGo, "Fill"), ScriptGuid(typeof(Image))));
        string lockImage = scene.Ref(header, "pillLock");
        Assert.IsNotNull(lockImage, "MessageHeaderView.pillLock — the sheet's action card wears the same glyph");
        Assert.AreEqual(scene.Named(pillGo, "Lock"), scene.GameObjectOf(lockImage));
        Assert.AreEqual(AssetDatabase.AssetPathToGUID(LockPath), scene.SpriteGuid(lockImage), "the app's Lock.png glyph");
        AssertRole(scene, scene.Named(pillGo, "Lock"), ThemeRole.InkSecondary, lockImage);
        string label = scene.Component(scene.Named(pillGo, "Label"), ScriptGuid(typeof(TextMeshProUGUI)));
        Assert.AreEqual(PersonalChatsCopy.Pill, scene.Text(label, "m_text"), "authored from PersonalChatsCopy");
        string chipLabel = scene.Ref(chip, "label");
        Assert.AreEqual(scene.FontGuid(chipLabel), scene.FontGuid(label), "the chip's word font");
        Assert.AreEqual(scene.Float(chipLabel, "m_fontSize"), scene.Float(label, "m_fontSize"), 0.001f, "…its size");
        Assert.AreEqual(scene.Field(chipLabel, "m_fontStyle"), scene.Field(label, "m_fontStyle"), "…and its style");
        AssertRole(scene, scene.Named(pillGo, "Label"), ThemeRole.InkSecondary, label);
        foreach (string graphic in new[] { lockImage, label })
            Assert.AreEqual("0", scene.Field(graphic, "m_RaycastTarget"), "only the pill's box takes the tap");
    }

    [Test]
    public void Scene_NameTap_CoversTheAvatarAndTheName_NeverTheBackButton()
    {
        SceneYaml scene = SceneYaml.Load(ScenePath);
        string header = scene.Single(ScriptGuid(typeof(MessageHeaderView)), "MessageHeaderView");
        string tapButton = scene.Ref(header, "nameTap");
        Assert.IsNotNull(tapButton, "MessageHeaderView.nameTap is wired");
        string tapGo = scene.GameObjectOf(tapButton);
        string tapRect = scene.RectOf(tapGo);
        Assert.AreEqual(PersonalChatsBuilder.NameTapName, scene.NameOf(tapGo));
        Assert.IsTrue(scene.IsActive(tapGo), "a working chat's name is always tappable");

        string avatarSlot = scene.Parent(scene.RectOf(scene.GameObjectOf(scene.Ref(header, "avatarImage"))));
        string textColumn = scene.Parent(scene.RectOf(scene.GameObjectOf(scene.Ref(header, "nameText"))));
        string zone = scene.Parent(tapRect);
        Assert.AreEqual(zone, scene.Parent(avatarSlot), "in the zone of the avatar…");
        Assert.AreEqual(zone, scene.Parent(textColumn), "…and of the name");
        Assert.AreEqual(tapRect, scene.Children(zone).Last(), "the zone's last child: it wins the tap over the avatar and the name");
        Assert.AreEqual("1", scene.Field(scene.Component(tapGo, ScriptGuid(typeof(LayoutElement))), "m_IgnoreLayout"),
            "the zone's layout group never moves the avatar or the name for it");
        string image = scene.Component(tapGo, ScriptGuid(typeof(Image)));
        Assert.AreEqual(image, scene.Ref(tapButton, "m_TargetGraphic"));
        Assert.AreEqual("1", scene.Field(image, "m_RaycastTarget"));
        StringAssert.Contains("a: 0}", scene.Field(image, "m_Color"), "invisible");

        float zoneWidth = scene.Vec(zone, "m_SizeDelta").x;   // the zone is point-anchored in the bar
        (float left, float right) tap = Edges(scene, tapRect, zoneWidth);
        (float left, float right) back = Edges(scene, scene.RectOf(scene.Named(scene.GameObjectOf(zone), "BackButton")), zoneWidth);
        (float left, float right) avatar = Edges(scene, avatarSlot, zoneWidth);
        (float left, float right) text = Edges(scene, textColumn, zoneWidth);
        Assert.GreaterOrEqual(tap.left, back.right, "the name's tap never reaches into the back button");
        Assert.LessOrEqual(tap.left, avatar.left + 0.01f, "it covers the avatar…");
        Assert.GreaterOrEqual(tap.right, text.right - 0.01f, "…and the name");
        Vector2 anchorMin = scene.Vec(tapRect, "m_AnchorMin"), anchorMax = scene.Vec(tapRect, "m_AnchorMax");
        Assert.AreEqual(0f, anchorMin.y, 1e-4f, "the zone's full height…");
        Assert.AreEqual(1f, anchorMax.y, 1e-4f);
        Assert.AreEqual(2f * PersonalChatsBuilder.NameTapBleed, scene.Vec(tapRect, "m_SizeDelta").y, 1e-4f, "…and a little beyond it");
        Assert.AreEqual(0f, scene.Vec(tapRect, "m_AnchoredPosition").y, 1e-4f, "centred on it");
        Assert.GreaterOrEqual(scene.Vec(zone, "m_SizeDelta").y + 2f * PersonalChatsBuilder.NameTapBleed, 120f,
            "at least the house touch floor tall");
    }

    [Test]
    public void Scene_PersonalPill_TheLockAndTheWordFit()
    {
        SceneYaml scene = SceneYaml.Load(ScenePath);
        string header = scene.Single(ScriptGuid(typeof(MessageHeaderView)), "MessageHeaderView");
        string pillGo = scene.GameObjectOf(scene.Ref(header, "personalPill"));
        float width = scene.Vec(scene.RectOf(pillGo), "m_SizeDelta").x;
        string label = scene.Component(scene.Named(pillGo, "Label"), ScriptGuid(typeof(TextMeshProUGUI)));

        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(scene.FontGuid(label)));
        Assert.IsNotNull(font, "the pill's word font");
        StringAssert.DoesNotContain("Medium", font.name, "measured only in a font whose materials do not rewrite themselves");
        StringAssert.DoesNotContain("Bold SDF", font.name);
        var host = new GameObject("PillMeasure", typeof(RectTransform));
        _created.Add(host);
        var tmp = host.AddComponent<TextMeshProUGUI>();
        tmp.font = font;
        tmp.fontSize = scene.Float(label, "m_fontSize");
        tmp.fontStyle = (FontStyles)int.Parse(scene.Field(label, "m_fontStyle"), CultureInfo.InvariantCulture);
        tmp.characterSpacing = scene.Float(label, "m_characterSpacing");
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.text = PersonalChatsCopy.Pill;
        float word = tmp.GetPreferredValues().x;
        Assert.Greater(word, 0f, "measured");

        float content = PersonalChatsBuilder.PillLockSize + PersonalChatsBuilder.PillSpacing + word;
        TestContext.Out.WriteLine($"[FIT] «{PersonalChatsCopy.Pill}» {word:F1}u + lock → {content:F1}u in a {width:F0}u pill");
        Assert.LessOrEqual(content + 2f * PersonalChatsBuilder.PillMinPadding, width,
            "the lock and «Личный» sit inside the pill with room on both sides");
    }

    [Test]
    public void EnsureHeaderParts_OnACodeBuiltHeader_BuildsOnce()
    {
        MessageHeaderView header = CodeBuiltHeader(out SemiAutoToggle chip, out RectTransform back, out RectTransform avatarSlot,
            out RectTransform textColumn);

        Assert.IsTrue(PersonalChatsBuilder.EnsureHeaderParts(header, null), "the first run builds the pill and the name's tap");
        Assert.IsFalse(PersonalChatsBuilder.EnsureHeaderParts(header, null), "a second run changes nothing");

        Transform[] pills = header.GetComponentsInChildren<Transform>(true).Where(t => t.name == PersonalChatsBuilder.PersonalPillName).ToArray();
        Transform[] taps = header.GetComponentsInChildren<Transform>(true).Where(t => t.name == PersonalChatsBuilder.NameTapName).ToArray();
        Assert.AreEqual(1, pills.Length, "one pill, however many runs");
        Assert.AreEqual(1, taps.Length, "one tap, however many runs");
        Assert.IsFalse(pills[0].gameObject.activeSelf, "authored hidden");
        Assert.AreSame(chip.transform.parent, pills[0].parent, "in the chip's zone…");
        Assert.AreEqual(chip.transform.GetSiblingIndex() + 1, pills[0].GetSiblingIndex(), "…right after the chip");
        Assert.AreEqual(new Vector2(PersonalChatsBuilder.PersonalPillWidth, ((RectTransform)chip.transform).sizeDelta.y),
            ((RectTransform)pills[0]).sizeDelta, "as tall as the chip, wide enough for its word");
        Assert.AreSame(avatarSlot.parent, taps[0].parent);
        Assert.AreEqual(taps[0].parent.childCount - 1, taps[0].GetSiblingIndex(), "the zone's last child");

        var zone = (RectTransform)taps[0].parent;
        (float left, float right) tap = Edges((RectTransform)taps[0], zone);
        Assert.GreaterOrEqual(tap.left, Edges(back, zone).right, "never over the back button");
        Assert.LessOrEqual(tap.left, Edges(avatarSlot, zone).left + 0.01f);
        Assert.GreaterOrEqual(tap.right, Edges(textColumn, zone).right - 0.01f);

        var refs = new SerializedObject(header);
        Assert.AreSame(chip, refs.FindProperty("semiAutoToggle").objectReferenceValue);
        Assert.AreSame(pills[0].GetComponent<Button>(), refs.FindProperty("personalPill").objectReferenceValue);
        Assert.AreSame(taps[0].GetComponent<Button>(), refs.FindProperty("nameTap").objectReferenceValue);
        var pillLock = refs.FindProperty("pillLock").objectReferenceValue as Image;
        Assert.IsNotNull(pillLock);
        Assert.AreSame(AssetDatabase.LoadAssetAtPath<Sprite>(LockPath), pillLock.sprite);
        var word = pills[0].GetComponentsInChildren<TextMeshProUGUI>(true).Single();
        Assert.AreEqual(PersonalChatsCopy.Pill, word.text);
        var chipWord = (TextMeshProUGUI)new SerializedObject(chip).FindProperty("label").objectReferenceValue;
        Assert.AreSame(chipWord.font, word.font, "the chip's word font");
        Assert.AreEqual(chipWord.fontSize, word.fontSize, 0.001f);
        Assert.AreEqual(chipWord.fontStyle, word.fontStyle);
    }

    // ---- ChatItem.prefab: the checkbox of the folder's selection modes (spec §5.3, §5.4) ----

    [Test]
    public void ChatItemPrefab_SelectCheck_IsHiddenBeforeTheAvatar_AndWired()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(ItemPrefabPath);
        try
        {
            var item = root.GetComponent<ChatItemView>();
            var refs = new SerializedObject(item);
            var check = refs.FindProperty("selectCheck")?.objectReferenceValue as GameObject;
            var marked = refs.FindProperty("selectChecked")?.objectReferenceValue as GameObject;
            Assert.IsNotNull(check, "ChatItemView.selectCheck is wired");
            Assert.IsNotNull(marked, "ChatItemView.selectChecked is wired");
            Assert.AreEqual(PersonalChatsBuilder.SelectCheckName, check.name);
            Assert.IsFalse(check.activeSelf, "hidden by default — only the folder's selection modes show it");
            Assert.IsFalse(marked.activeSelf, "unmarked by default");
            Assert.AreSame(check.transform, marked.transform.parent, "the mark is the checkbox's own face");

            Transform avatarColumn = item.avatarImage.transform.parent;
            Assert.AreSame(avatarColumn.parent, check.transform.parent, "in the row's layout, beside the avatar");
            Assert.AreEqual(avatarColumn.GetSiblingIndex() - 1, check.transform.GetSiblingIndex(), "right before the avatar");
            var slot = check.GetComponent<LayoutElement>();
            Assert.IsNotNull(slot, "the checkbox takes its place in the row's layout");
            Assert.AreEqual(PersonalChatsBuilder.SelectCheckSize, slot.preferredWidth, 0.001f);
            Assert.AreEqual(PersonalChatsBuilder.SelectCheckSize, ((RectTransform)check.transform).sizeDelta.y, 0.001f,
                "the row's layout does not control heights — the box is its own height");

            foreach (Graphic graphic in check.GetComponentsInChildren<Graphic>(true))
            {
                Assert.IsFalse(graphic.raycastTarget, $"{graphic.name}: the row takes the tap, never its checkbox");
                Assert.IsNotNull(graphic.GetComponent<ThemedColor>(), $"{graphic.name}: tinted by its role");
            }
            AssertBound(GraphicNamed(check, "Ring"), ThemeRole.InputBorder);
            AssertBound(GraphicNamed(check, "Hole"), ThemeRole.Surface);
            AssertBound(GraphicNamed(marked, "Fill"), ThemeRole.AccentFill);
            var tick = (Image)GraphicNamed(marked, "Tick");
            AssertBound(tick, ThemeRole.AccentOnFill);
            Assert.AreSame(AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Images/Icons/Tick.png"), tick.sprite, "the app's tick glyph");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [Test]
    public void EnsureChatItemSelectCheck_OnARowWithoutIt_BuildsOnce()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(ItemPrefabPath);   // never saved here
        try
        {
            var item = root.GetComponent<ChatItemView>();
            foreach (Transform stale in root.GetComponentsInChildren<Transform>(true).Where(t => t.name == PersonalChatsBuilder.SelectCheckName).ToArray())
                Object.DestroyImmediate(stale.gameObject);

            Assert.IsTrue(PersonalChatsBuilder.EnsureChatItemSelectCheck(item), "the first run builds the checkbox");
            Assert.IsFalse(PersonalChatsBuilder.EnsureChatItemSelectCheck(item), "a second run changes nothing");
            Assert.AreEqual(1, root.GetComponentsInChildren<Transform>(true).Count(t => t.name == PersonalChatsBuilder.SelectCheckName));
            Assert.AreSame(root.GetComponentsInChildren<Transform>(true).Single(t => t.name == PersonalChatsBuilder.SelectCheckName).gameObject,
                new SerializedObject(item).FindProperty("selectCheck").objectReferenceValue);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ---- Main.unity: the «Личные» folder (spec §5.3, §5.4; sketch 009 screens 1, 3, 4) ----

    private static readonly string[] FolderFields =
    {
        "backButton", "backChevron", "backLabel", "title", "editButton", "statusLine", "statusText", "body", "bodyText", "search",
        "searchPlaceholder", "sectionHeader", "loadingLabel", "scroll", "rowsContent", "noContactsNote", "noContactsText", "bottomBar",
        "primaryButton", "primaryLabel", "secondaryButton", "footer", "swipeBack",
    };

    [Test]
    public void Scene_FolderPanel_StandsAfterTheChatList_AndBeforeTheThread_OverTheTabBar()
    {
        SceneYaml scene = SceneYaml.Load(ScenePath);
        string listRect = scene.RectOf(scene.GameObjectOf(scene.Single(ScriptGuid(typeof(ChatListView)), "ChatListView")));
        string[] screen = scene.Children(scene.Parent(listRect)).ToArray();
        int list = System.Array.IndexOf(screen, listRect);
        Assert.Greater(screen.Length, list + 1, "nothing after the chat list");
        string panel = screen[list + 1];
        Assert.AreEqual(PersonalChatsBuilder.FolderPanelName, scene.NameOf(scene.GameObjectOf(panel)), "right after the chat list");
        Assert.IsFalse(scene.IsActive(scene.GameObjectOf(panel)), "authored hidden — the folder shows itself");
        Assert.AreEqual(1, screen.Count(r => scene.NameOf(scene.GameObjectOf(r)) == PersonalChatsBuilder.FolderPanelName), "one folder");

        string thread = scene.RectOf(scene.Ref(scene.Single(ScriptGuid(typeof(ChatManager)), "ChatManager"), "MessageListPanel"));
        Assert.Greater(System.Array.IndexOf(screen, thread), list + 1, "before the open chat, which slides in over it");
        foreach (string field in new[] { "m_AnchorMin", "m_AnchorMax", "m_Pivot", "m_AnchoredPosition", "m_SizeDelta" })
            Assert.AreEqual(scene.Field(thread, field), scene.Field(panel, field), $"the open chat's full screen, over the tab bar ({field})");
    }

    [Test]
    public void Scene_FolderPanel_IsWired_ToItsOwnParts_AndToTheChatList()
    {
        SceneYaml scene = SceneYaml.Load(ScenePath);
        string view = scene.Single(ScriptGuid(typeof(PersonalChatsFolderView)), "PersonalChatsFolderView");
        string panelGo = scene.GameObjectOf(view);
        Assert.AreEqual(PersonalChatsBuilder.FolderPanelName, scene.NameOf(panelGo));
        string list = scene.Single(ScriptGuid(typeof(ChatListView)), "ChatListView");
        Assert.AreEqual(list, scene.Ref(view, "chatList"), "a row's move and delete go to the chat list's own requests");
        Assert.AreEqual(scene.Field(list, "prefab"), scene.Field(view, "rowPrefab"), "the folder's rows are the chat list's rows");

        var own = new HashSet<string>(scene.Subtree(scene.RectOf(panelGo)));
        foreach (string field in FolderFields)
        {
            string target = scene.Ref(view, field);
            Assert.IsNotNull(target, $"PersonalChatsFolderView.{field} is wired — run Tools/Personal Chats/Build");
            Assert.IsTrue(own.Contains(scene.RectOf(scene.GameObjectOf(target) ?? target)), $"{field} is one of the folder's own parts");
        }

        string surface = scene.Component(panelGo, ScriptGuid(typeof(Image)));
        Assert.AreEqual("1", scene.Field(surface, "m_RaycastTarget"), "the page takes every tap — nothing reaches the list below");
        AssertRole(scene, panelGo, ThemeRole.Surface, surface);
    }

    [Test]
    public void Scene_FolderSwipeBack_SlidesThePanel_AndForwardsVerticalDragsToItsRows()
    {
        SceneYaml scene = SceneYaml.Load(ScenePath);
        string view = scene.Single(ScriptGuid(typeof(PersonalChatsFolderView)), "PersonalChatsFolderView");
        string panelRect = scene.RectOf(scene.GameObjectOf(view));
        string swipe = scene.Ref(view, "swipeBack");
        string stripGo = scene.GameObjectOf(swipe);
        Assert.AreEqual(scene.RectOf(stripGo), scene.Children(panelRect).Last(), "the strip is the panel's last child: it wins the edge");
        Assert.AreEqual(panelRect, scene.Ref(swipe, "panelToSlide"), "it slides the folder");
        Assert.AreEqual(scene.Ref(view, "scroll"), scene.Ref(swipe, "contentScrollRect"), "a vertical drag still scrolls the rows");
        string pass = scene.Component(stripGo, ScriptGuid(typeof(ClickPassthrough)));
        Assert.IsNotNull(pass, "taps on the strip reach what is under it");
        Assert.AreEqual(panelRect, scene.Ref(pass, "allowedPanel"), "…inside the folder only — never the chat list below");
        Assert.AreEqual("1", scene.Field(pass, "deliverPressToAllBehind"));
        Assert.AreEqual("1", scene.Field(scene.Component(stripGo, ScriptGuid(typeof(LayoutElement))), "m_IgnoreLayout"),
            "the page's layout never moves the strip");

        string folder = System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "Scripts/UI/PersonalChatsFolderView.cs"));
        StringAssert.Contains("swipeBack.OnCommitted = HandleSwipedAway;", folder,
            "OnCommitted is not serialized: the folder takes the strip's commit when it wires itself");
    }

    [Test]
    public void Scene_FolderSearch_IsConfiguredLikeTheOtherFields()
    {
        SceneYaml scene = SceneYaml.Load(ScenePath);
        string view = scene.Single(ScriptGuid(typeof(PersonalChatsFolderView)), "PersonalChatsFolderView");
        string bar = scene.Ref(view, "search");
        Assert.AreEqual(ScriptGuid(typeof(ChatSearchBar)), scene.ScriptGuidOf(bar), "the chat list's own search bar, cloned");
        string listBar = scene.Children(scene.Ref(scene.Single(ScriptGuid(typeof(ChatListView)), "ChatListView"), "content"))
            .Select(scene.GameObjectOf).First(go => scene.Component(go, ScriptGuid(typeof(ChatSearchBar))) != null);
        Assert.AreNotEqual(listBar, scene.GameObjectOf(bar), "…its own copy, not the list's bar");

        string input = scene.Ref(bar, "input");
        Assert.AreEqual(ScriptGuid(typeof(DeferredDismissInputField)), scene.ScriptGuidOf(input),
            "the single-focus field type every input of the app uses");
        Assert.AreNotEqual(((int)TMP_InputField.LineType.SingleLine).ToString(CultureInfo.InvariantCulture), scene.Field(input, "m_LineType"),
            "never SingleLine — mixed line types restart the IME on a switch");
        Assert.AreEqual(((int)TMP_InputField.InputType.Standard).ToString(CultureInfo.InvariantCulture), scene.Field(input, "m_InputType"),
            "autocorrection off");
        Assert.AreEqual(((int)TouchScreenKeyboardType.Default).ToString(CultureInfo.InvariantCulture), scene.Field(input, "m_KeyboardType"),
            "the Default keypad");
        Assert.AreEqual(scene.Ref(input, "m_Placeholder"), scene.Ref(view, "searchPlaceholder"), "the folder rewrites the bar's own hint");
    }

    [Test]
    public void Scene_DeleteConfirm_StandsAboveTheFolder()
    {
        SceneYaml scene = SceneYaml.Load(ScenePath);
        string list = scene.Single(ScriptGuid(typeof(ChatListView)), "ChatListView");
        string listRect = scene.RectOf(scene.GameObjectOf(list));
        string[] screen = scene.Children(scene.Parent(listRect)).ToArray();
        string confirm = scene.RectOf(scene.GameObjectOf(scene.Ref(list, "deleteConfirm")));
        int folder = System.Array.IndexOf(screen, screen.First(r => scene.NameOf(scene.GameObjectOf(r)) == PersonalChatsBuilder.FolderPanelName));
        Assert.AreEqual(folder + 1, System.Array.IndexOf(screen, confirm),
            "«Удалить» in the folder asks through the chat list's card — it must stand over the folder, not under it");
        string thread = scene.RectOf(scene.Ref(scene.Single(ScriptGuid(typeof(ChatManager)), "ChatManager"), "MessageListPanel"));
        Assert.Less(System.Array.IndexOf(screen, confirm), System.Array.IndexOf(screen, thread), "…and under the open chat, as before");
        foreach ((string field, string value) in new[] { ("m_AnchorMin", "{x: 0, y: 0}"), ("m_AnchorMax", "{x: 1, y: 1}"), ("m_SizeDelta", "{x: 0, y: 0}") })
            Assert.AreEqual(value, scene.Field(confirm, field), $"it still covers the chats screen ({field})");
    }

    [Test]
    public void Scene_FolderTitles_FitTheHeader()
    {
        SceneYaml scene = SceneYaml.Load(ScenePath);
        string title = scene.Ref(scene.Single(ScriptGuid(typeof(PersonalChatsFolderView)), "PersonalChatsFolderView"), "title");
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(scene.FontGuid(title)));
        Assert.IsNotNull(font, "the title's font");
        StringAssert.DoesNotContain("Medium", font.name, "measured only in a font whose materials do not rewrite themselves");
        StringAssert.DoesNotContain("Bold SDF", font.name);
        var host = new GameObject("TitleMeasure", typeof(RectTransform));
        _created.Add(host);
        var tmp = host.AddComponent<TextMeshProUGUI>();
        tmp.font = font;
        tmp.fontSize = scene.Float(title, "m_fontSize");
        tmp.textWrappingMode = TextWrappingModes.NoWrap;

        float room = 1080f - 2f * PersonalChatsBuilder.HeaderSideWidth;   // the header is at least the reference width
        foreach (string text in new[] { PersonalChatsCopy.FolderTitle, PersonalChatsCopy.EditTitle, PersonalChatsCopy.OnboardingTitle })
        {
            tmp.text = text;
            float width = tmp.GetPreferredValues().x;
            TestContext.Out.WriteLine($"[FIT] «{text}» {width:F1}u in {room:F0}u");
            Assert.Greater(width, 0f, "measured");
            Assert.LessOrEqual(width, room, $"«{text}» fits between the header's two side buttons");
        }
    }

    [Test]
    public void EnsureFolderPanel_OnACodeBuiltScreen_BuildsOnce()
    {
        ChatListView list = CodeBuiltChatsScreen(out RectTransform thread, out GameObject confirm);
        Transform screen = list.transform.parent;

        Assert.IsTrue(PersonalChatsBuilder.EnsureFolderPanel(list, thread), "the first run builds the folder");
        Assert.IsFalse(PersonalChatsBuilder.EnsureFolderPanel(list, thread), "a second run changes nothing");

        Transform[] panels = screen.Cast<Transform>().Where(t => t.name == PersonalChatsBuilder.FolderPanelName).ToArray();
        Assert.AreEqual(1, panels.Length, "one folder, however many runs");
        Assert.AreEqual(list.transform.GetSiblingIndex() + 1, panels[0].GetSiblingIndex(), "right after the chat list");
        Assert.AreSame(screen, confirm.transform.parent, "the delete card moved up beside the folder…");
        Assert.AreEqual(panels[0].GetSiblingIndex() + 1, confirm.transform.GetSiblingIndex(), "…right over it");
        Assert.Less(confirm.transform.GetSiblingIndex(), thread.GetSiblingIndex(), "…and still under the open chat");
        Assert.IsFalse(panels[0].gameObject.activeSelf, "authored hidden");

        var view = panels[0].GetComponent<PersonalChatsFolderView>();
        Assert.IsNotNull(view);
        var refs = new SerializedObject(view);
        Assert.AreSame(list, refs.FindProperty("chatList").objectReferenceValue);
        Assert.AreSame(list.prefab, refs.FindProperty("rowPrefab").objectReferenceValue);
        foreach (string field in FolderFields)
            Assert.IsNotNull(refs.FindProperty(field).objectReferenceValue, $"{field} is wired");
        var search = (ChatSearchBar)refs.FindProperty("search").objectReferenceValue;
        var input = (TMP_InputField)new SerializedObject(search).FindProperty("input").objectReferenceValue;
        Assert.IsInstanceOf<DeferredDismissInputField>(input, "the clone takes the single-focus field type");
        Assert.AreEqual(TMP_InputField.LineType.MultiLineSubmit, input.lineType, "the clone takes the house line type");
        Assert.IsTrue(input.transform.IsChildOf(search.transform), "the clone's own input");

        var listBar = list.content.GetComponentInChildren<ChatSearchBar>(true);
        var listInput = (TMP_InputField)new SerializedObject(listBar).FindProperty("input").objectReferenceValue;
        Assert.AreEqual(TMP_InputField.LineType.SingleLine, listInput.lineType, "the chat list's own bar is left as it is");
    }

    // The chats screen the builder anchors on, built in code: the chat list (its Content holding a search bar with a real
    // input — the single-focus field type, SingleLine, like the scene's — its row prefab, its delete card) and the open
    // chat's panel after it.
    private ChatListView CodeBuiltChatsScreen(out RectTransform thread, out GameObject confirm)
    {
        var screen = new GameObject("Screen_Messanger", typeof(RectTransform));
        _created.Add(screen);
        RectTransform panel = Rect("ChatsPanel", screen.transform, Vector2.zero, Vector2.one, Centre(), Vector2.zero, Vector2.zero);
        var list = panel.gameObject.AddComponent<ChatListView>();
        RectTransform content = Rect("Content", panel, new Vector2(0f, 1f), Vector2.one, new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
        content.gameObject.AddComponent<VerticalLayoutGroup>();
        list.content = content;
        list.prefab = ItemPrefab();
        CodeBuiltSearchBar(content);

        confirm = Rect("DeleteChatConfirmPanel", panel, Vector2.zero, Vector2.one, Centre(), Vector2.zero, Vector2.zero).gameObject;
        var card = confirm.AddComponent<ChatDeleteConfirm>();
        var listRefs = new SerializedObject(list);
        listRefs.FindProperty("deleteConfirm").objectReferenceValue = card;
        listRefs.ApplyModifiedPropertiesWithoutUndo();

        thread = Rect("MessagesPanel", screen.transform, Vector2.zero, Vector2.one, Centre(), new Vector2(0f, -104f), new Vector2(0f, 208f));
        return list;
    }

    private static void CodeBuiltSearchBar(Transform content)
    {
        RectTransform barRect = Rect("ChatsSearchBar", content, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0.5f, 1f), Vector2.zero,
            new Vector2(1080f, 124f));
        var bar = barRect.gameObject.AddComponent<ChatSearchBar>();
        barRect.gameObject.AddComponent<LayoutElement>().preferredHeight = 124f;
        RectTransform inputRect = Rect("Input", barRect, Centre(), Centre(), Centre(), Vector2.zero, new Vector2(800f, 60f));
        inputRect.gameObject.AddComponent<Image>();
        var input = inputRect.gameObject.AddComponent<DeferredDismissInputField>();
        input.lineType = TMP_InputField.LineType.SingleLine;
        RectTransform area = Rect("Text Area", inputRect, Vector2.zero, Vector2.one, Centre(), Vector2.zero, Vector2.zero);
        var placeholder = Rect("Placeholder", area, Vector2.zero, Vector2.one, Centre(), Vector2.zero, Vector2.zero).gameObject.AddComponent<TextMeshProUGUI>();
        var text = Rect("Text", area, Vector2.zero, Vector2.one, Centre(), Vector2.zero, Vector2.zero).gameObject.AddComponent<TextMeshProUGUI>();
        input.textViewport = area;
        input.textComponent = text;
        input.placeholder = placeholder;
        var barRefs = new SerializedObject(bar);
        barRefs.FindProperty("input").objectReferenceValue = input;
        barRefs.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Graphic GraphicNamed(GameObject root, string name) =>
        root.GetComponentsInChildren<Graphic>(true).Single(g => g.name == name);

    // ---- BotSettings.prefab: the «ЛИЧНЫЕ ЧАТЫ» section (spec §5.7, sketch 009 screen 7) ----

    [Test]
    public void SettingsPrefab_TheSection_StandsRightAboveTheSchedule()
    {
        BotSettings settings = SettingsPrefab();
        Transform content = settings.BotNameField.transform.parent;
        Transform header = content.Find(PersonalChatsBuilder.SettingsHeaderName);
        Assert.IsNotNull(header, $"no {PersonalChatsBuilder.SettingsHeaderName} in the Основное tab — run Tools/Personal Chats/Build");
        Assert.IsNotNull(settings.PersonalChatsRow, "BotSettings.personalChatsRow is wired");
        Transform row = settings.PersonalChatsRow.transform;
        Transform hint = content.Find(PersonalChatsBuilder.SettingsHintName);
        Assert.IsNotNull(hint, $"no {PersonalChatsBuilder.SettingsHintName} in the Основное tab");
        Transform scheduleHeader = ScheduleHeaderOf(settings);

        Transform[] order = { header, row, hint, scheduleHeader };
        foreach (Transform part in order)
            Assert.AreSame(content, part.parent, $"{part.name} is a direct child of the tab's list");
        for (int i = 1; i < order.Length; i++)
            Assert.AreEqual(order[i - 1].GetSiblingIndex() + 1, order[i].GetSiblingIndex(),
                $"{order[i - 1].name} → {order[i].name}: one block, the hint right before «РАСПИСАНИЕ РАБОТЫ»");
        foreach (string name in new[] { PersonalChatsBuilder.SettingsHeaderName, PersonalChatsBuilder.SettingsRowName, PersonalChatsBuilder.SettingsHintName })
            Assert.AreEqual(1, content.Cast<Transform>().Count(t => t.name == name), $"one {name}");
    }

    [Test]
    public void SettingsPrefab_TheHeader_IsATwinOfTheTabsHeaders()
    {
        BotSettings settings = SettingsPrefab();
        Transform content = settings.BotNameField.transform.parent;
        var header = content.Find(PersonalChatsBuilder.SettingsHeaderName).GetComponent<SectionHeader>();
        Assert.IsNotNull(header, "the header carries SectionHeader");
        Assert.AreEqual(PersonalChatsCopy.SettingsHeader, header.Text, "authored from PersonalChatsCopy");
        var first = content.GetComponentInChildren<SectionHeader>(true).GetComponent<TextMeshProUGUI>();
        var label = header.GetComponent<TextMeshProUGUI>();
        Assert.AreSame(first.font, label.font, "the tab's header font");
        Assert.AreEqual(first.fontSize, label.fontSize, 0.001f);
        Assert.AreEqual(((RectTransform)first.transform).sizeDelta.y, ((RectTransform)label.transform).sizeDelta.y, 0.001f);
        AssertBound(label, first.GetComponent<ThemedColor>().Role);
    }

    [Test]
    public void SettingsPrefab_TheRow_IsAChannelRowsCard_AsOneButton()
    {
        BotSettings settings = SettingsPrefab();
        Button row = settings.PersonalChatsRow;
        Assert.IsNotNull(row, "BotSettings.personalChatsRow is wired — run Tools/Personal Chats/Build");
        Assert.AreEqual(PersonalChatsBuilder.SettingsRowName, row.name);
        var card = row.GetComponent<Image>();
        Assert.AreSame(card, row.targetGraphic, "the card takes the press");
        Assert.IsTrue(card.raycastTarget, "the card takes the tap");
        AssertBound(card, ThemeRole.Surface);

        ToggleRow channelRow = settings.whatsappRow;
        Assert.AreEqual(((RectTransform)channelRow.transform).sizeDelta.y, ((RectTransform)row.transform).sizeDelta.y, 0.001f,
            "as tall as the channel rows");
        Assert.AreEqual(channelRow.GetComponent<ImageWithRoundedCorners>().radius, row.GetComponent<ImageWithRoundedCorners>().radius, 0.001f,
            "their corners");
        var shadow = row.GetComponent<Shadow>();
        Assert.IsNotNull(shadow, "their shadow");
        Assert.AreEqual(channelRow.GetComponent<Shadow>().effectColor, shadow.effectColor);
        Assert.AreEqual(channelRow.GetComponent<Shadow>().effectDistance, shadow.effectDistance);

        var channelLabel = (TextMeshProUGUI)new SerializedObject(channelRow).FindProperty("labelText").objectReferenceValue;
        TextMeshProUGUI label = TextNamed(row, "Label");
        Assert.AreEqual(PersonalChatsCopy.SettingsRow, label.text);
        Assert.AreSame(channelLabel.font, label.font, "the channel rows' label font…");
        Assert.AreEqual(channelLabel.fontSize, label.fontSize, 0.001f, "…size…");
        Assert.AreEqual(channelLabel.fontWeight, label.fontWeight, "…and weight");
        AssertBound(label, ThemeRole.InkPrimary);

        TextMeshProUGUI count = settings.PersonalChatsCountLabel;
        Assert.IsNotNull(count, "BotSettings.personalChatsCount is wired");
        Assert.AreSame(row.transform, count.transform.parent, "the number is the row's own");
        Assert.AreEqual(HorizontalAlignmentOptions.Right, count.horizontalAlignment, "the number stands against the chevron");
        AssertBound(count, ThemeRole.InkSecondary);

        TextMeshProUGUI chevron = TextNamed(row, "Chevron");
        Assert.AreEqual(PersonalChatsCopy.SettingsChevron, chevron.text);
        AssertBound(chevron, ThemeRole.InkTertiary);

        foreach (TextMeshProUGUI text in row.GetComponentsInChildren<TextMeshProUGUI>(true))
            Assert.IsFalse(text.raycastTarget, $"{text.name}: only the card takes the tap");

        // Left to right, whatever the card's width: the label ends before the number, the number before the chevron.
        var labelRect = (RectTransform)label.transform;
        var countRect = (RectTransform)count.transform;
        var chevronRect = (RectTransform)chevron.transform;
        foreach (RectTransform pinned in new[] { countRect, chevronRect })
        {
            Assert.AreEqual(1f, pinned.anchorMin.x, 0.001f, $"{pinned.name} is pinned to the card's right edge");
            Assert.AreEqual(1f, pinned.anchorMax.x, 0.001f);
            Assert.AreEqual(1f, pinned.pivot.x, 0.001f);
        }
        Assert.AreEqual(0f, labelRect.anchorMin.x, 0.001f, "the label spans the card");
        Assert.AreEqual(1f, labelRect.anchorMax.x, 0.001f);
        Assert.LessOrEqual(labelRect.offsetMax.x, countRect.anchoredPosition.x - countRect.sizeDelta.x, "the label ends before the number");
        Assert.LessOrEqual(countRect.anchoredPosition.x, chevronRect.anchoredPosition.x - chevronRect.sizeDelta.x, "the number ends before the chevron");
        Assert.Less(chevronRect.anchoredPosition.x, 0f, "the chevron keeps off the card's edge");
    }

    [Test]
    public void SettingsPrefab_TheHint_IsTheSettingsHint_AndHoldsAllItsLines()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PersonalChatsBuilder.SettingsPrefabPath);   // never saved here
        try
        {
            var settings = root.GetComponent<BotSettings>();
            Transform content = settings.BotNameField.transform.parent;
            Transform hintRect = content.Find(PersonalChatsBuilder.SettingsHintName);
            Assert.IsNotNull(hintRect, "run Tools/Personal Chats/Build");
            var hint = hintRect.GetComponent<TextMeshProUGUI>();
            Assert.AreEqual(PersonalChatsCopy.SettingsHint, hint.text, "authored from PersonalChatsCopy");
            Assert.AreEqual(TextWrappingModes.Normal, hint.textWrappingMode, "it wraps");
            Assert.IsFalse(hint.raycastTarget);
            AssertBound(hint, ThemeRole.InkTertiary);
            Assert.AreSame(settings.BotNameField.GetComponentInChildren<TextMeshProUGUI>(true).font, hint.font,
                "the schedule hint's font (SF Pro Regular)");

            var list = content.GetComponent<VerticalLayoutGroup>();
            float width = 1080f - list.padding.left - list.padding.right;
            float lines = hint.GetPreferredValues(hint.text, width, 0f).y;
            float box = ((RectTransform)hintRect).sizeDelta.y;
            Assert.GreaterOrEqual(box + 0.5f, lines, "the box holds every line of the hint");
            Assert.Less(box, lines + hint.fontSize, "…and no more than that");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [Test]
    public void EnsureSettingsSection_ASecondRun_ChangesNothing()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PersonalChatsBuilder.SettingsPrefabPath);   // never saved here
        try
        {
            var settings = root.GetComponent<BotSettings>();
            Assert.IsFalse(PersonalChatsBuilder.EnsureSettingsSection(settings), "the saved prefab already holds everything the builder authors");
            Transform content = settings.BotNameField.transform.parent;
            foreach (string name in new[] { PersonalChatsBuilder.SettingsHeaderName, PersonalChatsBuilder.SettingsRowName, PersonalChatsBuilder.SettingsHintName })
                Assert.AreEqual(1, content.Cast<Transform>().Count(t => t.name == name), $"one {name}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [Test]
    public void EnsureSettingsSection_OnAPrefabWithoutIt_BuildsOnce_AboveTheSchedule()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PersonalChatsBuilder.SettingsPrefabPath);   // never saved here
        try
        {
            var settings = root.GetComponent<BotSettings>();
            Transform content = settings.BotNameField.transform.parent;
            foreach (string name in new[] { PersonalChatsBuilder.SettingsHeaderName, PersonalChatsBuilder.SettingsRowName, PersonalChatsBuilder.SettingsHintName })
            {
                Transform part = content.Find(name);
                if (part != null) Object.DestroyImmediate(part.gameObject);
            }
            var refs = new SerializedObject(settings);
            refs.FindProperty("personalChatsRow").objectReferenceValue = null;
            refs.FindProperty("personalChatsCount").objectReferenceValue = null;
            refs.ApplyModifiedPropertiesWithoutUndo();

            Assert.IsTrue(PersonalChatsBuilder.EnsureSettingsSection(settings), "the first run builds the section");
            Assert.IsFalse(PersonalChatsBuilder.EnsureSettingsSection(settings), "a second run changes nothing");

            Transform header = content.Find(PersonalChatsBuilder.SettingsHeaderName);
            Transform hint = content.Find(PersonalChatsBuilder.SettingsHintName);
            Assert.IsNotNull(settings.PersonalChatsRow, "the row is stamped again");
            Assert.IsNotNull(settings.PersonalChatsCountLabel, "the number is stamped again");
            Assert.AreSame(settings.PersonalChatsRow.transform, settings.PersonalChatsCountLabel.transform.parent);
            Transform[] order = { header, settings.PersonalChatsRow.transform, hint, ScheduleHeaderOf(settings) };
            for (int i = 1; i < order.Length; i++)
                Assert.AreEqual(order[i - 1].GetSiblingIndex() + 1, order[i].GetSiblingIndex(), $"{order[i - 1].name} → {order[i].name}");
            foreach (string name in new[] { PersonalChatsBuilder.SettingsHeaderName, PersonalChatsBuilder.SettingsRowName, PersonalChatsBuilder.SettingsHintName })
                Assert.AreEqual(1, content.Cast<Transform>().Count(t => t.name == name), $"one {name}, however many runs");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static BotSettings SettingsPrefab()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PersonalChatsBuilder.SettingsPrefabPath);
        Assert.IsNotNull(prefab, $"{PersonalChatsBuilder.SettingsPrefabPath} moved");
        var settings = prefab.GetComponent<BotSettings>();
        Assert.IsNotNull(settings, "BotSettings.prefab carries BotSettings on its root");
        return settings;
    }

    // «РАСПИСАНИЕ РАБОТЫ»: the header right above the schedule's toggle (BotSettings.scheduleRow).
    private static Transform ScheduleHeaderOf(BotSettings settings)
    {
        Assert.IsNotNull(settings.ScheduleRow, "BotSettings.scheduleRow is wired");
        Transform toggle = settings.ScheduleRow.transform;
        Transform header = toggle.parent.GetChild(toggle.GetSiblingIndex() - 1);
        var section = header.GetComponent<SectionHeader>();
        Assert.IsNotNull(section, "a header stands right above the schedule's toggle");
        Assert.AreEqual(ScheduleCopy.SectionTitle, section.Text, "…«РАСПИСАНИЕ РАБОТЫ»");
        return header;
    }

    private static TextMeshProUGUI TextNamed(Component root, string name) =>
        root.GetComponentsInChildren<TextMeshProUGUI>(true).Single(t => t.name == name);

    // ---- helpers ----

    // A header like the scene's: LeftZone [BackButton][Avatar slot/Avatar][Text/Name], RightZone [SemiAutoToggle Pill/Ring + Label].
    private MessageHeaderView CodeBuiltHeader(out SemiAutoToggle chip, out RectTransform back, out RectTransform avatarSlot,
        out RectTransform textColumn)
    {
        var top = new GameObject("TopBar", typeof(RectTransform));
        _created.Add(top);
        var header = top.AddComponent<MessageHeaderView>();

        RectTransform left = Rect("LeftZone", top.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(0f, -66f), new Vector2(800f, 100f));
        left.gameObject.AddComponent<HorizontalLayoutGroup>();
        back = Rect("BackButton", left, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(52f, 2f), new Vector2(104f, 104f));
        avatarSlot = Rect("Avatar", left, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(230f, -50f), new Vector2(100f, 100f));
        header.avatarImage = Rect("Avatar", avatarSlot, Centre(), Centre(), Centre(), Vector2.zero, new Vector2(100f, 100f)).gameObject.AddComponent<Image>();
        textColumn = Rect("Text", left, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(554f, -50f), new Vector2(500f, 100f));
        header.nameText = Rect("Name", textColumn, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(0f, -50f),
            new Vector2(500f, 60f)).gameObject.AddComponent<TextMeshProUGUI>();

        RectTransform right = Rect("RightZone", top.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(0f, -66f), new Vector2(280f, 100f));
        right.gameObject.AddComponent<HorizontalLayoutGroup>();
        RectTransform chipRect = Rect("SemiAutoToggle", right, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(1f, 0.5f),
            new Vector2(240f, -50f), new Vector2(170f, 88f));
        chip = chipRect.gameObject.AddComponent<SemiAutoToggle>();
        RectTransform pill = Rect("Pill", chipRect, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), Centre(), Vector2.zero, new Vector2(0f, 60f));
        Image ring = Rect("Ring", pill, Vector2.zero, Vector2.one, Centre(), Vector2.zero, Vector2.zero).gameObject.AddComponent<Image>();
        var label = Rect("Label", pill, Centre(), Centre(), Centre(), Vector2.zero, new Vector2(70f, 34f)).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Fonts/SFProText-Semibold SDF.asset");
        label.fontSize = 26f;
        label.fontStyle = FontStyles.Bold;
        label.characterSpacing = -2f;
        label.text = "Авто";
        var chipRefs = new SerializedObject(chip);
        chipRefs.FindProperty("ringImage").objectReferenceValue = ring;
        chipRefs.FindProperty("label").objectReferenceValue = label;
        chipRefs.ApplyModifiedPropertiesWithoutUndo();
        return header;
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    private static Vector2 Centre() => new Vector2(0.5f, 0.5f);

    // A child's horizontal edges, from its parent rect's left edge — the anchors, the pivot, the position and the size as
    // serialized (the test's own arithmetic, so the builder's is checked rather than reused).
    private static (float left, float right) Edges(SceneYaml scene, string rect, float parentWidth) =>
        Edges(scene.Vec(rect, "m_AnchorMin").x, scene.Vec(rect, "m_AnchorMax").x, scene.Vec(rect, "m_Pivot").x,
            scene.Vec(rect, "m_AnchoredPosition").x, scene.Vec(rect, "m_SizeDelta").x, parentWidth);

    private static (float left, float right) Edges(RectTransform rect, RectTransform parent) =>
        Edges(rect.anchorMin.x, rect.anchorMax.x, rect.pivot.x, rect.anchoredPosition.x, rect.sizeDelta.x, parent.rect.width);

    private static (float left, float right) Edges(float anchorMin, float anchorMax, float pivot, float position, float size, float parentWidth)
    {
        float anchorLeft = anchorMin * parentWidth;
        float anchorRight = anchorMax * parentWidth;
        float width = anchorRight - anchorLeft + size;
        float left = anchorLeft + (anchorRight - anchorLeft) * pivot + position - pivot * width;
        return (left, left + width);
    }

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

    private static void AssertBound(Graphic graphic, ThemeRole role)
    {
        var themed = graphic.GetComponent<ThemedColor>();
        Assert.IsNotNull(themed, $"{graphic.name}: its colour is a ThemedColor binding");
        Assert.AreEqual(role, themed.Role, $"{graphic.name}: {role}");
        Assert.AreSame(graphic, new SerializedObject(themed).FindProperty("target").objectReferenceValue,
            $"{graphic.name}: the binding paints its own graphic");
    }
}

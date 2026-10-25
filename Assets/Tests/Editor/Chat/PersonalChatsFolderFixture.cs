using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The «Личные» folder over a bare app, for the tests that drive the real <see cref="PersonalChatsFolderView"/>: a Manager whose
/// bot holds a WhatsApp profile and a ChatManager on that bot's WhatsApp channel (<see cref="OpenChatFixture"/>), both
/// installed as the Instances; a screen holding the chat list (ChatListView — where the rows' moves and deletes go) and the
/// folder panel, authored hidden like the scene's, its parts wired the way PersonalChatsBuilder wires them and its rows the
/// real ChatItem.prefab (its labels are SF Pro Regular / Semibold — nothing here measures Medium or Bold). The chats are added
/// straight to ChatManager.Chats with an empty last message, so no row measures its preview. Every GameObject goes to the
/// caller's list; the caller restores the Instances.
/// </summary>
internal sealed class FolderHarness
{
    public PersonalChatsFolderView Folder;
    public ChatListView List;
    public ChatManager Chats;
    public Manager Manager;
    public Button Back, Edit, Primary, Secondary;
    public GameObject BackChevron, BackLabel, StatusLine, Body, SectionHeader, NoContactsNote, BottomBar, Footer;
    public TextMeshProUGUI Title, PrimaryLabel, LoadingLabel;
    public SwipeToBackPanel Swipe;
    public RectTransform Rows;

    /// <summary>A chat in the bot's chat list: a person (a saved contact or not) or, for an @g.us id, a group.</summary>
    public ChatViewModel AddChat(string chatId, string title, long lastMessageUnixSec, bool contact = false)
    {
        var chat = new ChatViewModel(chatId, title, null, "", lastMessageUnixSec,
            facts: new AudienceFacts(false, chatId, "", false, false, false, contact));
        Chats.Chats.Add(chat);
        return chat;
    }
}

internal static class PersonalChatsFolderFixture
{
    public const string ItemPrefabPath = "Assets/Prefabs/ChatItem.prefab";

    public static FolderHarness Build(string botName, string profileId, List<GameObject> hosts)
    {
        var h = new FolderHarness
        {
            Manager = OpenChatFixture.ManagerWithBot(botName, profileId, hosts),
            Chats = OpenChatFixture.BareChatManager(botName, hosts),
        };
        Manager.Instance = h.Manager;
        ChatManager.Instance = h.Chats;

        var screen = new GameObject("Screen_Messanger", typeof(RectTransform));
        hosts.Add(screen);
        var listGo = new GameObject("ChatsPanel", typeof(RectTransform));
        listGo.transform.SetParent(screen.transform, false);
        h.List = listGo.AddComponent<ChatListView>();

        var panel = new GameObject("PersonalChatsPanel", typeof(RectTransform));
        panel.SetActive(false);   // authored hidden — the folder shows itself
        panel.transform.SetParent(screen.transform, false);
        h.Folder = panel.AddComponent<PersonalChatsFolderView>();
        BuildParts(h, panel.transform);
        Wire(h);
        return h;
    }

    private static void BuildParts(FolderHarness h, Transform panel)
    {
        h.Back = NewButton(panel, "Back");
        h.BackChevron = Child(h.Back.transform, "Icon").gameObject;
        h.BackLabel = Child(h.Back.transform, "Label").gameObject;
        h.Title = Child(panel, "Title").gameObject.AddComponent<TextMeshProUGUI>();
        h.Edit = NewButton(panel, "Edit");
        h.StatusLine = Child(panel, "StatusLine").gameObject;
        h.Body = Child(panel, "Body").gameObject;
        h.SectionHeader = Child(panel, "SectionHeader").gameObject;
        h.LoadingLabel = Child(panel, "LoadingLabel").gameObject.AddComponent<TextMeshProUGUI>();
        h.Rows = Child(panel, "Content");
        h.NoContactsNote = Child(panel, "NoContactsNote").gameObject;
        h.BottomBar = Child(panel, "BottomBar").gameObject;
        h.Primary = NewButton(h.BottomBar.transform, "Primary");
        h.PrimaryLabel = Child(h.Primary.transform, "Label").gameObject.AddComponent<TextMeshProUGUI>();
        h.Secondary = NewButton(h.BottomBar.transform, "Secondary");
        h.Footer = Child(panel, "Footer").gameObject;
        h.Swipe = Child(panel, "SwipeBack").gameObject.AddComponent<SwipeToBackPanel>();
    }

    private static void Wire(FolderHarness h)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ItemPrefabPath);
        Assert.IsNotNull(prefab, $"{ItemPrefabPath} moved");
        var refs = new SerializedObject(h.Folder);
        Set(refs, "chatList", h.List);
        Set(refs, "rowPrefab", prefab.GetComponent<ChatItemView>());
        Set(refs, "rowsContent", h.Rows);
        Set(refs, "backButton", h.Back);
        Set(refs, "backChevron", h.BackChevron);
        Set(refs, "backLabel", h.BackLabel);
        Set(refs, "title", h.Title);
        Set(refs, "editButton", h.Edit);
        Set(refs, "statusLine", h.StatusLine);
        Set(refs, "body", h.Body);
        Set(refs, "sectionHeader", h.SectionHeader);
        Set(refs, "loadingLabel", h.LoadingLabel);
        Set(refs, "noContactsNote", h.NoContactsNote);
        Set(refs, "bottomBar", h.BottomBar);
        Set(refs, "primaryButton", h.Primary);
        Set(refs, "primaryLabel", h.PrimaryLabel);
        Set(refs, "secondaryButton", h.Secondary);
        Set(refs, "footer", h.Footer);
        Set(refs, "swipeBack", h.Swipe);
        refs.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Set(SerializedObject refs, string field, Object value)
    {
        SerializedProperty property = refs.FindProperty(field);
        Assert.IsNotNull(property, $"PersonalChatsFolderView.{field} moved — update PersonalChatsFolderFixture");
        property.objectReferenceValue = value;
    }

    private static Button NewButton(Transform parent, string name)
    {
        RectTransform rect = Child(parent, name);
        rect.gameObject.AddComponent<Image>();
        return rect.gameObject.AddComponent<Button>();
    }

    private static RectTransform Child(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The «Личные и рабочие чаты» tests' open chat over a bare app: a Manager whose BotsParent holds one bot with a WhatsApp
/// profile (ChatManager.GetActiveProfileId resolves it), a real ChatManager without its Awake/Start (EditMode runs neither)
/// on that bot's WhatsApp channel, the driver that feeds it a chats/filter answer through its real ParseChatsJson, and the
/// open chat (ChatManager.currentChatId — what SuggestionsController and N8nSuggestionsProvider read). Every GameObject it
/// makes is added to the caller's list, which the caller's TearDown destroys; installing Manager.Instance /
/// ChatManager.Instance stays the caller's call. Shared by PersonalChatCueTests, PersonalChatSheetsWiringTests and
/// SuggestionsAudienceGuardTests (beside SourceGuard and SceneYaml).
/// </summary>
internal static class OpenChatFixture
{
    /// <summary>A Manager whose BotsParent holds the bot <paramref name="botName"/> with the WhatsApp profile <paramref name="whatsappProfileId"/>.</summary>
    public static Manager ManagerWithBot(string botName, string whatsappProfileId, List<GameObject> hosts)
    {
        var managerGo = new GameObject("ManagerHost");
        hosts.Add(managerGo);
        var manager = managerGo.AddComponent<Manager>();
        var botsParent = new GameObject("BotsParent");
        hosts.Add(botsParent);
        var botGo = new GameObject(botName);
        botGo.transform.SetParent(botsParent.transform);
        botGo.AddComponent<Bot>().whatsappProfileId = whatsappProfileId;
        FieldInfo field = typeof(Manager).GetField("BotsParent", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "Manager.BotsParent moved — update OpenChatFixture");
        field.SetValue(manager, botsParent);
        return manager;
    }

    /// <summary>A real ChatManager without its Awake/Start, on the bot <paramref name="botName"/>'s WhatsApp channel.</summary>
    public static ChatManager BareChatManager(string botName, List<GameObject> hosts)
    {
        var host = new GameObject("ChatManagerHost");
        hosts.Add(host);
        var manager = host.AddComponent<ChatManager>();
        SetProperty(manager, nameof(ChatManager.ActiveChannel), ChatChannel.WhatsApp);
        SetProperty(manager, nameof(ChatManager.CurrentBotId), botName);
        return manager;
    }

    /// <summary>Feeds <paramref name="manager"/> a chats/filter answer through its real ParseChatsJson.</summary>
    public static void Parse(ChatManager manager, string json, bool isInitialLoad)
    {
        MethodInfo parse = typeof(ChatManager).GetMethod("ParseChatsJson", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(parse, "ChatManager.ParseChatsJson moved — update OpenChatFixture");
        Call(() => parse.Invoke(manager, new object[] { json, isInitialLoad }));
    }

    /// <summary>Makes <paramref name="chatId"/> the manager's open chat (ChatManager.CurrentChatId).</summary>
    public static void OpenChat(ChatManager manager, string chatId)
    {
        FieldInfo open = typeof(ChatManager).GetField("currentChatId", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(open, "ChatManager.currentChatId moved — update OpenChatFixture");
        open.SetValue(manager, chatId);
    }

    /// <summary>Runs a reflection call and rethrows what the called method threw, not the TargetInvocationException around it.</summary>
    public static void Call(Action call)
    {
        try
        {
            call();
        }
        catch (TargetInvocationException e)
        {
            ExceptionDispatchInfo.Capture(e.InnerException).Throw();
        }
    }

    private static void SetProperty(ChatManager manager, string name, object value)
    {
        PropertyInfo property = typeof(ChatManager).GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        Assert.IsNotNull(property, $"ChatManager.{name} moved — update OpenChatFixture");
        property.SetValue(manager, value);
    }
}

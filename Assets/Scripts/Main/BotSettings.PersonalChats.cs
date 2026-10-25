using System.Collections;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// «Личные и рабочие чаты» (spec §5.7, sketch 009 screen 7): the «ЛИЧНЫЕ ЧАТЫ» section of the Основное tab — one row,
/// «Личные чаты · N ›», and a hint. Authored ADDITIVELY by PersonalChatsBuilder (Tools/Personal Chats/Build) right above
/// «РАСПИСАНИЕ РАБОТЫ»; nothing here builds UI.
///
/// N is this bot's personal chats on both channels (<see cref="PersonalChatsCount"/>), read whenever the screen shows — a
/// screen left open showing again (OnEnable), a fresh open once Bot.OpenSettings' pairing names its bot — and not live while
/// it is shown.
///
/// A tap opens the «Личные» folder of THIS screen's bot. The way out is the back chevron's own (<see cref="RequestBack"/>),
/// which discards unsaved edits — so with an unsaved edit (the Save button's own dirty check,
/// Manager.HasUnsavedBotSettings) the tap only says «Сначала сохраните изменения». Otherwise the screen goes back to the bots
/// page, and once it has settled closed — its slide-out is a coroutine of this screen, which a tab switch would stop half-way
/// — the chats switch to the bot, the «Чаты» tab shows, the channel is the one the chats screen shows when the bot has it
/// connected, else the other one, and the folder opens in Browse. A bot without a connected channel lands on its chats
/// screen alone, which says why.
/// </summary>
public partial class BotSettings
{
    /// <summary>How long a tap waits for this screen to settle closed before it gives up.</summary>
    public const float PersonalChatsSettleSeconds = 2f;

    [SerializeField] private Button personalChatsRow;
    [SerializeField] private TextMeshProUGUI personalChatsCount;

    public Button PersonalChatsRow => personalChatsRow;
    public TextMeshProUGUI PersonalChatsCountLabel => personalChatsCount;

    // This screen's bot while it is the open one (Bot.OpenSettings pairs them); null otherwise.
    private string PersonalChatsBotId => Manager.openBotSettings == this && Manager.openBot != null ? Manager.openBot.name : null;

    /// <summary>The row's number again, for this screen's bot — nothing while this is not the open screen.</summary>
    public void RefreshPersonalChatsCount()
    {
        string bot = PersonalChatsBotId;
        if (personalChatsCount == null || bot == null) return;
        string number = PersonalChatsCount.Read(bot).ToString(CultureInfo.InvariantCulture);
        if (personalChatsCount.text != number) personalChatsCount.text = number;
    }

    private void WirePersonalChats()
    {
        if (personalChatsRow != null) personalChatsRow.onClick.AddListener(OnPersonalChatsTapped);
    }

    internal void OnPersonalChatsTapped()
    {
        string bot = PersonalChatsBotId;
        if (bot == null || Manager.Instance == null) return;
        // The chevron refuses a back while the screen slides; so does this — a refused back must not leave a wait behind.
        if (SwipeToBackBotSettings.Instance != null && SwipeToBackBotSettings.Instance.IsAnimating) return;
        if (Manager.Instance.HasUnsavedBotSettings())
        {
            AudienceToast.Show(PersonalChatsCopy.SaveFirst);
            return;
        }
        RequestBack();
        Manager.Instance.StartCoroutine(OpenPersonalChatsWhenClosed(this, bot));
    }

    // Hosted on the Manager: this screen deactivates when it settles closed, and its own coroutines die with it.
    private static IEnumerator OpenPersonalChatsWhenClosed(BotSettings screen, string bot)
    {
        float giveUpAt = Time.unscaledTime + PersonalChatsSettleSeconds;
        while (Manager.openBotSettings == screen && Time.unscaledTime < giveUpAt)
            yield return null;
        if (Manager.openBotSettings != null) yield break;   // it never closed, or another bot's settings opened meanwhile
        OpenPersonalChatsFolder(bot);
    }

    private static void OpenPersonalChatsFolder(string bot)
    {
        ChatManager chats = ChatManager.Instance;
        if (chats == null) return;
        // The bot first, while the chats are hidden: a folder open for another bot closes at once, without a slide.
        chats.SetActiveBot(bot);
        if (BottomTabManager.Instance != null) BottomTabManager.Instance.SwitchTab(BottomTabManager.WhatsAppTabIndex);

        // Read after the bot switch: SetActiveBot announces the bot first and resolves its channel afterwards.
        Bot owner = Manager.Instance != null ? Manager.Instance.FindBotByName(bot) : null;
        ChatChannel? channel = PersonalChatsFolderModel.ChannelFromSettings(chats.ActiveChannel,
            BotChatStats.ProfileIdOf(owner, ChatChannel.WhatsApp) != null, BotChatStats.ProfileIdOf(owner, ChatChannel.Telegram) != null);
        if (channel.HasValue) chats.SetActiveChannel(channel.Value);

        // A chat left open stands over the folder (the folder slides in under an open chat): it goes the way Back closes it.
        if (chats.MessageListPanel != null && chats.MessageListPanel.activeSelf) chats.ShowChatList();
        if (!channel.HasValue) return;

        PersonalChatsFolderView folder = PersonalChatsFolderView.Instance;
        if (folder != null) folder.Open(FolderMode.Browse);
    }
}

using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Profile → Аккаунт: profile summary card (reuses ProfilePage's edit popup)
// and the honestly-named «Удалить все данные» — the complete local wipe.
public partial class ProfileSubPages
{
    [Header("Account page")]
    [SerializeField] private TextMeshProUGUI accountAvatarInitial;
    [SerializeField] private TextMeshProUGUI accountName;
    [SerializeField] private TextMeshProUGUI accountEmail;
    [SerializeField] private Button accountEditButton;
    [SerializeField] private Button accountDeleteButton;

    private void WireAccount()
    {
        if (accountEditButton != null)
            accountEditButton.onClick.AddListener(() => ProfilePage.Instance?.OpenEditPopupPublic());
        if (accountDeleteButton != null)
            accountDeleteButton.onClick.AddListener(ConfirmWipe);
    }

    /// <summary>Public: ProfilePage.SaveProfile refreshes this card after an edit.</summary>
    public void RefreshAccountCard()
    {
        string savedName = PlayerPrefs.GetString(ProfilePage.KeyName, ProfilePage.DefaultName);
        string savedEmail = PlayerPrefs.GetString(ProfilePage.KeyEmail, ProfilePage.DefaultEmail);

        if (accountName != null) accountName.text = savedName;
        if (accountEmail != null) accountEmail.text = savedEmail;
        if (accountAvatarInitial != null)
            accountAvatarInitial.text = savedName.Length > 0 ? savedName[0].ToString().ToUpper() : "?";
    }

    private void ConfirmWipe() => ShowConfirm(
        "Удалить все данные?",
        "Удалит всех ботов, историю и настройки на этом устройстве. Действие нельзя отменить.",
        "Удалить",
        RunWipe);

    private void RunWipe()
    {
        // 1. Per-bot teardown through the canonical path — the only code that
        //    also kills Wappi profiles, n8n workflows, Supabase chunks, the
        //    paired BotSettings, and the bot's cache dir. Backwards: DeleteBot
        //    destroys the paired BotSettings by sibling index.
        Transform botsRoot = Manager.Instance != null ? Manager.Instance.BotsRoot : null;
        if (botsRoot != null)
        {
            for (int i = botsRoot.childCount - 1; i >= 0; i--)
            {
                var bot = botsRoot.GetChild(i).GetComponent<Bot>();
                if (bot != null) bot.DeleteBot();
            }
        }

        // 2. Prefs: DeleteAll is the only complete option — per-chat SemiAuto
        //    keys and historical orphans cannot be enumerated.
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();

        // 3. Disk: cache tree, response dumps, stickers, link previews.
        LocalDataWipe.DeleteDiskData(Application.persistentDataPath);

        RefreshAccountCard();
        ProfilePage.Instance?.RefreshProfileCard();

        FinishClose(Page.Account);
        ProfilePage.Instance?.NavigateToWhatsAppTab();
    }
}

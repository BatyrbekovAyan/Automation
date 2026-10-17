using System.Collections;
using System.Collections.Generic;
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

    private bool _wiping;

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
        if (_wiping || Manager.Instance == null) return;
        StartCoroutine(RunWipeRoutine());
    }

    // V12 (iteration 2026-09-28): every bot leaves the phone only on its server verdict (Manager.DeleteBotOnServer) —
    // the wipe used to call the old one-call delete, which wiped locally and never read the server's answer, so a
    // refused delete left the bot's memory and clone on the server with no way back to them from the app.
    //   * Done / NothingOnServer → that bot goes now (its server data is gone);
    //   * Retry → the wipe STOPS («повторите»): the bots still on the server keep their ids, nothing else is wiped;
    //   * Refused → the bot is collected; after the loop ONE question offers «Удалить только с телефона» for all of
    //     them (owner decision 2026-10-07) — a bot another install now owns must never block the wipe forever.
    // The prefs and the disk go only after every bot is settled (FinishWipe).
    private IEnumerator RunWipeRoutine()
    {
        _wiping = true;
        var refused = new List<Bot>();
        int ownedElsewhere = 0;
        Transform botsRoot = Manager.Instance.BotsRoot;
        var bots = new List<Bot>();
        // Backwards (by sibling index): DeleteBotLocally destroys the paired BotSettings by index, so several local
        // deletions in one frame (bots that never reached the server need no network) must run from the end.
        for (int i = botsRoot != null ? botsRoot.childCount - 1 : -1; i >= 0; i--)
        {
            var bot = botsRoot.GetChild(i).GetComponent<Bot>();
            if (bot != null) bots.Add(bot);
        }

        Manager.Instance.LoadingPanel.SetActive(true);
        foreach (var bot in bots)
        {
            if (bot == null) continue;
            var outcome = BotServerDeletion.Outcome.Retry;
            yield return Manager.Instance.DeleteBotOnServer(bot, o => outcome = o, showLoading: false);
            if (outcome.Verdict == BotServerDeletion.Verdict.Retry)
            {
                Manager.Instance.LoadingPanel.SetActive(false);
                _wiping = false;
                ShowConfirm(BotDeletionCopy.WipeRetryTitle, BotDeletionCopy.WipeRetryBody, BotDeletionCopy.RetryAction, RunWipe);
                yield break;
            }
            if (outcome.Verdict == BotServerDeletion.Verdict.Refused)
            {
                refused.Add(bot);
                if (outcome.Reason == BotServerDeletion.ReasonOwnedElsewhere) ownedElsewhere++;
                continue;
            }
            bot.DeleteBotLocally();
        }
        Manager.Instance.LoadingPanel.SetActive(false);
        _wiping = false;

        if (refused.Count == 0)
        {
            FinishWipe();
            yield break;
        }
        ShowConfirm(BotDeletionCopy.RefusedTitle, BotDeletionCopy.WipeRefusedBody(refused.Count, ownedElsewhere == refused.Count),
                    BotDeletionCopy.PhoneOnlyAction, () => DeleteRefusedLocallyThenFinish(refused), longAction: true);
    }

    // The owner chose «Удалить только с телефона» for the bots the server refused. Same frame, so backwards by sibling
    // index (the list was collected that way).
    private void DeleteRefusedLocallyThenFinish(List<Bot> refused)
    {
        foreach (var bot in refused)
            if (bot != null) bot.DeleteBotLocally();
        FinishWipe();
    }

    private void FinishWipe()
    {
        // 1. Bots: settled by RunWipeRoutine (each on its server verdict) before this runs.
        // 2. Prefs: DeleteAll is the only complete option — per-chat SemiAuto
        //    keys and historical orphans cannot be enumerated. The reply-mode queue
        //    lives in memory too and re-persists itself on its next write: empty it
        //    first, or a new Bot0 would inherit the old bots' pending choices (R5).
        ReplyModeSyncService.Existing?.ForgetAll();
        ScheduleSyncService.Existing?.ForgetAll();   // B2: the schedule's queue re-persists itself the same way
        // V3 (owner decision 2026-10-06): the install's server identity SURVIVES the wipe — the RevenueCat id lives in
        // the SDK's own storage and comes back on the next refresh; a fresh install naming it would be refused (one
        // install per billing id) and the subscription would be stuck in billingConflict until support revokes.
        // PreserveAcrossWipe saves once, after the credentials are back — the disk never holds a wipe without them.
        AuthClient.PreserveAcrossWipe(() => PlayerPrefs.DeleteAll());

        // 3. Disk: cache tree, response dumps, stickers, link previews.
        LocalDataWipe.DeleteDiskData(Application.persistentDataPath);

        // The token still lists the retired bots for up to 12 h: re-read ownership now (same install, nothing owned).
        var auth = AuthClient.Existing;
        if (auth != null) auth.StartCoroutine(auth.ForceRefresh());

        RefreshAccountCard();
        ProfilePage.Instance?.RefreshProfileCard();

        FinishClose(Page.Account);
        ProfilePage.Instance?.NavigateToWhatsAppTab();

        // 4. The wipe restores a TRUE first-run state (bots gone, OnboardingSeen cleared by
        //    DeleteAll above), so the welcome carousel comes back now — not on the next
        //    launch, and not only if the owner walks to the Bots tab. This is the sanctioned
        //    first-run reset, and it lands on the Chats tab (line above), which is exactly
        //    where the boot gate would have been unreachable. Same single gate as boot.
        BotsPage.Instance?.TryShowFirstRunCarousel();
    }
}

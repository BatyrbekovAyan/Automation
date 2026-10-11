using TMPro;
using UnityEngine;

// The in-settings «Удалить бота» flow. DeleteBotButton opens DeleteBotConfirmPopup (authored in BotSettings.prefab by
// BotSettingsDeleteBotPopupBuilder); its confirm asks the SERVER first (Manager.DeleteBotOnServer) and the bot leaves
// the phone only on that verdict — V12 (iteration 2026-09-28; owner decision 2026-10-07):
//   * Done / NothingOnServer → the bot goes (FinishDeleteLocally);
//   * Retry (no confirmation came: no connection, a server/database fault, an answer that is neither the server's
//     `success: true` nor its refusal body — C1/C1b) → the bot stays; the same popup says so and offers «Повторить»;
//   * Refused (the server's final word, a 4xx carrying its refusal body: another install owns it, or it cannot be
//     proven this install's) → the bot stays; the popup tells where the data stays and offers «Удалить только с
//     телефона», which removes it locally.
// The popup is re-used for all three states: its texts are swapped and the card is fitted to them after Show
// (ConfirmCardFitter — the prefab's authored body-to-button clearance is pinned by BotDeletionWiringTests).
public partial class BotSettings
{
    private enum DeletePopupState { Ask, Retry, Refused }

    private const float LongActionMinFontSize = 30f;

    private DeletePopupState _deleteState;
    private bool _deleting;
    private ConfirmCardFitter.Baseline _deleteCardBaseline;
    private RectTransform _deleteCard;
    private TextMeshProUGUI _deleteTitle;
    private TextMeshProUGUI _deleteBody;
    private TextMeshProUGUI _deleteConfirmLabel;
    private string _askTitle;
    private string _askBody;
    private string _askAction;
    private float _actionFontSize;

    private void WireDeleteBot()
    {
        if (deleteBotButton != null)
            deleteBotButton.onClick.AddListener(OpenDeleteBotPopup);
        if (deleteConfirmButton != null)
            PopupUI.WireFingerUp(deleteConfirmButton, ConfirmDeleteBot);
        if (deleteCancelButton != null)
            PopupUI.WireFingerUp(deleteCancelButton, CancelDeleteBot);
        CacheDeletePopup();
    }

    // Read once, while the card is still exactly as authored: the Ask texts come back from here, and the fitter's
    // baseline must never be captured from a card it has already grown.
    private void CacheDeletePopup()
    {
        if (deleteConfirmPopup == null) return;
        _deleteCard = deleteConfirmPopup.transform.Find("Content") as RectTransform;
        _deleteTitle = Label(_deleteCard, "Title");
        _deleteBody = Label(_deleteCard, "Body");
        _deleteConfirmLabel = deleteConfirmButton != null ? deleteConfirmButton.GetComponentInChildren<TextMeshProUGUI>(true) : null;
        _askTitle = _deleteTitle != null ? _deleteTitle.text : null;
        _askBody = _deleteBody != null ? _deleteBody.text : null;
        _askAction = _deleteConfirmLabel != null ? _deleteConfirmLabel.text : null;
        _actionFontSize = _deleteConfirmLabel != null ? _deleteConfirmLabel.fontSize : 0f;
        if (_deleteCard != null && _deleteBody != null)
            ConfirmCardFitter.Capture(_deleteCard, _deleteTitle, _deleteBody, ref _deleteCardBaseline);
    }

    private static TextMeshProUGUI Label(Transform root, string childName)
    {
        Transform child = root != null ? root.Find(childName) : null;
        return child != null ? child.GetComponent<TextMeshProUGUI>() : null;
    }

    private void OpenDeleteBotPopup() => ShowDeletePopup(DeletePopupState.Ask, null);

    private void CancelDeleteBot()
    {
        if (deleteConfirmPopup != null) PopupUI.Hide(deleteConfirmPopup);
    }

    private void ConfirmDeleteBot()
    {
        DeletePopupState state = _deleteState;
        // Snapped, not animated: a bot that never reached the server (or the phone-only choice) destroys this whole
        // page in the same frame, and DOTween would chase the destroyed popup.
        if (deleteConfirmPopup != null) deleteConfirmPopup.SetActive(false);

        var bot = Manager.openBot != null ? Manager.openBot.GetComponent<Bot>() : null;
        if (bot == null) return;

        if (state == DeletePopupState.Refused)
        {
            FinishDeleteLocally(bot);           // the owner's explicit «Удалить только с телефона»
            return;
        }
        StartServerDelete(bot);
    }

    private void StartServerDelete(Bot bot)
    {
        if (_deleting || Manager.Instance == null) return;
        _deleting = true;
        Manager.Instance.StartCoroutine(Manager.Instance.DeleteBotOnServer(bot, outcome =>
        {
            _deleting = false;
            HandleDeleteOutcome(bot, outcome);
        }));
    }

    private void HandleDeleteOutcome(Bot bot, BotServerDeletion.Outcome outcome)
    {
        if (this == null || bot == null) return;
        switch (outcome.Verdict)
        {
            case BotServerDeletion.Verdict.NothingOnServer:
            case BotServerDeletion.Verdict.Done:
                FinishDeleteLocally(bot);
                break;
            case BotServerDeletion.Verdict.Retry: ShowDeletePopup(DeletePopupState.Retry, null); break;
            case BotServerDeletion.Verdict.Refused: ShowDeletePopup(DeletePopupState.Refused, outcome.Reason); break;
        }
    }

    private void ShowDeletePopup(DeletePopupState state, string reason)
    {
        if (deleteConfirmPopup == null) return;
        _deleteState = state;
        ApplyDeleteCopy(state, reason);
        PopupUI.Show(deleteConfirmPopup);
        // After Show: TMP measures nothing on a never-active object (ConfirmCardFitter rule 1).
        if (_deleteCard != null && _deleteBody != null)
            ConfirmCardFitter.Fit(_deleteCard, _deleteTitle, _deleteBody, ref _deleteCardBaseline);
    }

    private void ApplyDeleteCopy(DeletePopupState state, string reason)
    {
        switch (state)
        {
            case DeletePopupState.Retry:
                SetDeleteTexts(BotDeletionCopy.RetryTitle, BotDeletionCopy.RetryBody, BotDeletionCopy.RetryAction);
                break;
            case DeletePopupState.Refused:
                SetDeleteTexts(BotDeletionCopy.RefusedTitle, BotDeletionCopy.RefusedBody(reason), BotDeletionCopy.PhoneOnlyAction);
                break;
            default:
                SetDeleteTexts(_askTitle, _askBody, _askAction);
                break;
        }
        if (_deleteConfirmLabel == null) return;
        // «Удалить только с телефона» is wider than the 384u button at its authored size: let it wrap to two lines
        // and shrink as far as needed; every other label keeps the authored size.
        bool longAction = state == DeletePopupState.Refused;
        _deleteConfirmLabel.enableAutoSizing = longAction;
        if (longAction)
        {
            _deleteConfirmLabel.fontSizeMin = LongActionMinFontSize;
            _deleteConfirmLabel.fontSizeMax = _actionFontSize;
        }
        else if (_actionFontSize > 0f)
        {
            _deleteConfirmLabel.fontSize = _actionFontSize;
        }
    }

    private void SetDeleteTexts(string title, string body, string action)
    {
        if (_deleteTitle != null && title != null) _deleteTitle.text = title;
        if (_deleteBody != null && body != null) _deleteBody.text = body;
        if (_deleteConfirmLabel != null && action != null) _deleteConfirmLabel.text = action;
    }

    // The local removal: back to the Bots list, then the bot's local teardown (which destroys this page too).
    private void FinishDeleteLocally(Bot bot)
    {
        if (deleteConfirmPopup != null) deleteConfirmPopup.SetActive(false);

        if (Manager.BotSettingsParentStatic != null)
        {
            var parentGo = Manager.BotSettingsParentStatic.transform.parent != null
                ? Manager.BotSettingsParentStatic.transform.parent.gameObject
                : Manager.BotSettingsParentStatic;
            parentGo.SetActive(false);
        }
        if (BotsPage.Instance != null)
        {
            // Slide-in pushed BotsPage left for the parallax depth effect. The normal back path animates it back to
            // X=0 via SlideOutToBotsPage, but Delete skips that animation, so restore the centered position explicitly
            // before reactivating — otherwise the list reappears shifted ~30% of screen width to the left.
            var botsPageRt = BotsPage.Instance.GetComponent<RectTransform>();
            if (botsPageRt != null)
                botsPageRt.anchoredPosition = new Vector2(0f, botsPageRt.anchoredPosition.y);
            BotsPage.Instance.gameObject.SetActive(true);
        }

        Manager.openBot = null;
        Manager.openBotSettings = null;

        bot.DeleteBotLocally();
    }
}

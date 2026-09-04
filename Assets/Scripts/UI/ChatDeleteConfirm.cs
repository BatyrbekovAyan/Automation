using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Modal confirm for deleting a chat. Reuses PopupUI's show/hide animation. Cancel hides;
/// Delete calls ChatManager.DeleteChat for the pending chat. Wired from ChatListView.RequestDelete.
///
/// This is the only confirm body in the app assembled from data the app does not
/// control — <see cref="Ask"/> interpolates the chat's own title — so it is the
/// one that genuinely runs long: a ~50-character group name already wraps the
/// body to three lines. Every element sits at an absolute offset inside a
/// fixed-height card, so the card is fitted to the copy on every show via
/// <see cref="ConfirmCardFitter"/>, which grows the body box and the card
/// together and leaves the authored 44u clearance above the buttons intact.
///
/// No title reference is serialized on purpose: «Удалить чат?» is fixed copy
/// that measures 304u in a 760u column, so it is one line forever and can never
/// contribute growth. ConfirmCardFitter reads a null title as zero.
/// </summary>
public class ChatDeleteConfirm : MonoBehaviour
{
    [SerializeField] private GameObject panel;       // backdrop Image + "Content" card child
    [SerializeField] private TextMeshProUGUI bodyText;
    [SerializeField] private Button cancelButton;
    [SerializeField] private Button deleteButton;

    private string _pendingChatId;
    private ConfirmCardFitter.Baseline _cardBaseline;   // authored card geometry, captured once

    private void Awake()
    {
        if (cancelButton != null) cancelButton.onClick.AddListener(Cancel);
        if (deleteButton != null) deleteButton.onClick.AddListener(Confirm);
        // NOTE: do NOT SetActive(false) here. This component lives on the panel, which starts
        // inactive (saved that way in the scene), so Awake runs only when PopupUI.Show first
        // activates it — deactivating here would immediately re-hide that first show.

        // Which is also why this is the right place to snapshot the card: Awake runs from
        // inside that first SetActive, before Ask has fitted anything, so what it reads is
        // the geometry the scene authored. Every later solve starts from these values
        // rather than from the previous result, so growth can never compound.
        ConfirmCardFitter.Capture(Card(), null, bodyText, ref _cardBaseline);
    }

    /// <summary>
    /// The card PopupUI animates — resolved by the same "Content" → "Card" → first child
    /// rule Show/Hide use, so the thing that is scaled and the thing that is resized can
    /// never be two different objects.
    /// </summary>
    private RectTransform Card() =>
        panel != null ? PopupUI.FindCard(panel.transform) as RectTransform : null;

    /// <summary>
    /// The body copy for one chat. A seam rather than an inline interpolation so the
    /// tests that measure this card — the only one whose copy the app does not fully
    /// control — wrap the string that actually ships instead of a copy of it.
    /// </summary>
    public static string BodyText(string chatTitle) =>
        string.IsNullOrEmpty(chatTitle)
            ? "Чат будет удалён безвозвратно."
            : $"Чат «{chatTitle}» будет удалён безвозвратно.";

    public void Ask(string chatId, string chatTitle)
    {
        if (string.IsNullOrEmpty(chatId)) return;
        _pendingChatId = chatId;
        if (bodyText != null) bodyText.text = BodyText(chatTitle);

        // Show FIRST, fit second — never the other way round. The panel sits inactive
        // between shows, and TMP cannot measure text on a GameObject that has never been
        // active: CalculatePreferredValues takes its "no text to generate" early return
        // and reports 0, which the fitter reads as "no measurement" and leaves the card
        // exactly as authored. Show activates it, so the fit lands in the same frame,
        // well before the first render.
        PopupUI.Show(panel);
        ConfirmCardFitter.Fit(Card(), null, bodyText, ref _cardBaseline);
    }

    private void Cancel()
    {
        _pendingChatId = null;
        PopupUI.Hide(panel);
    }

    private void Confirm()
    {
        string id = _pendingChatId;
        _pendingChatId = null;
        PopupUI.Hide(panel);
        if (!string.IsNullOrEmpty(id)) ChatManager.Instance?.DeleteChat(id);
    }
}

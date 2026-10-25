using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// «Личные и рабочие чаты» (spec §5.2, sketch 009 screen 2): the «Личные» row at the top of the working chat list — the lock
/// in a circle, «Личные», a grey counter of the personal chats with something unread, a chevron. It stands for every chat
/// the assistant stays out of; a tap raises <see cref="Tapped"/> (the folder opens on it). <see cref="ChatListView"/> decides
/// when the row is shown and what the counter says (<see cref="PersonalChatsListModel"/>); this view only draws it.
///
/// Authored by PersonalChatsBuilder (Tools/Personal Chats/Build) into the list's Content right after ChatsSearchBar, hidden;
/// every colour is a ThemedColor binding, so nothing here is runtime-tinted. A scroll or a swipe that ends on the row is
/// not a tap — the same rules as a chat row.
/// </summary>
public class PersonalChatsRowView : MonoBehaviour
{
    private const int MaxShownCount = 99;

    [SerializeField] private Button button;
    [SerializeField] private GameObject counter;            // the grey pill — hidden at 0
    [SerializeField] private TextMeshProUGUI counterText;

    /// <summary>The owner tapped the row.</summary>
    public static event Action Tapped;

    private void Awake()
    {
        if (button != null) button.onClick.AddListener(HandleClick);
    }

    private void OnDestroy()
    {
        if (button != null) button.onClick.RemoveListener(HandleClick);
    }

    /// <summary>The number of personal chats with something unread; 0 hides the counter.</summary>
    public void Bind(int personalUnreadChats)
    {
        bool show = personalUnreadChats > 0;
        if (counter != null && counter.activeSelf != show) counter.SetActive(show);
        if (!show || counterText == null) return;
        counterText.text = personalUnreadChats > MaxShownCount
            ? $"{MaxShownCount}+"
            : personalUnreadChats.ToString(CultureInfo.InvariantCulture);
    }

    private void HandleClick()
    {
        // A real tap while a chat row's swipe is open only puts it away (a chat row's own rule).
        if (SwipeToDelete.AnyOpen)
        {
            SwipeToDelete.CloseAnyOpen();
            return;
        }
        // A tap that caught a flying list stops it — it is not a tap on the row.
        if (ScrollClickBlocker.IsBlocking) return;
        Tapped?.Invoke();
    }
}

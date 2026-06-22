using UnityEngine;
using UnityEngine.UI;

public class BotsPage : MonoBehaviour
{
    [Tooltip("Plus button in the Bots page header (top-right).")]
    [SerializeField] private Button NewBotButton;

    [Tooltip("Plus button in the bottom navigation bar (Screen_New tab). " +
             "Pressing the header + forwards to this button so the tab " +
             "switch — including icon/label active state — is handled by " +
             "the existing BottomTabManager wiring.")]
    [SerializeField] private Button BottomNavNewButton;

    public static BotsPage Instance;

    void Start()
    {
        Instance = this;

        if (NewBotButton != null)
            NewBotButton.onClick.AddListener(StartNewBot);
    }

    /// <summary>
    /// Opens the add-bot wizard. Forwards to the bottom-nav + button so behavior is
    /// identical to tapping it directly: BottomTabManager activates Screen_New and
    /// updates the active icon/label visuals for us. Public so the empty-state CTA
    /// can launch the same flow.
    /// </summary>
    public void StartNewBot()
    {
        if (BottomNavNewButton != null)
            BottomNavNewButton.onClick.Invoke();
    }
}

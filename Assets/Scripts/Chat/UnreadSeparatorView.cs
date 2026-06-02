using UnityEngine;
using TMPro;

/// <summary>
/// Full-width "N UNREAD MESSAGES" divider inserted into the message stream at the
/// open-time unread boundary. Modeled on DateSeparatorView. The label text is built by
/// the pure static FormatLabel so pluralization is unit-testable.
/// </summary>
public class UnreadSeparatorView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI label;

    public void SetCount(int count)
    {
        if (label != null) label.text = FormatLabel(count);
    }

    public static string FormatLabel(int count) =>
        count == 1 ? "1 UNREAD MESSAGE" : $"{count} UNREAD MESSAGES";
}

using UnityEngine;
using TMPro;

/// <summary>
/// Full-width "N НЕПРОЧИТАННЫХ СООБЩЕНИЙ" divider inserted into the message stream at the
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

    /// <summary>
    /// RU label with the three-form plural agreement: 1 непрочитанное сообщение /
    /// 2..4 непрочитанных сообщения / 5+ непрочитанных сообщений (11..14 take the
    /// "many" form despite ending in 1..4).
    /// </summary>
    public static string FormatLabel(int count) =>
        $"{count} " + RuPlural.Pick(count,
            "НЕПРОЧИТАННОЕ СООБЩЕНИЕ",
            "НЕПРОЧИТАННЫХ СООБЩЕНИЯ",
            "НЕПРОЧИТАННЫХ СООБЩЕНИЙ");
}

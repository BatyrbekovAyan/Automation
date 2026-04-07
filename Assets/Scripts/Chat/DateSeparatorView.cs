using UnityEngine;
using TMPro;

public class DateSeparatorView : MonoBehaviour
{
    public TextMeshProUGUI dateText;

    public void SetDate(string dateString)
    {
        if (dateText != null)
        {
            dateText.text = dateString;
        }
    }
}
using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

[RequireComponent(typeof(TextMeshProUGUI))]
public class TMPLinkHandler : MonoBehaviour, IPointerClickHandler
{
    private TextMeshProUGUI textMeshPro;

    void Awake()
    {
        textMeshPro = GetComponent<TextMeshProUGUI>();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // Protect against accidental clicks while scrolling!
        if (ScrollClickBlocker.IsBlocking) return;

        // Ask TMP if the exact pixel the user touched contains a <link> tag
        int linkIndex = TMP_TextUtilities.FindIntersectingLink(textMeshPro, eventData.position, eventData.pressEventCamera);
        
        if (linkIndex != -1)
        {
            TMP_LinkInfo linkInfo = textMeshPro.textInfo.linkInfo[linkIndex];
            string linkId = linkInfo.GetLinkID();

            // The tag carries a short numeric id (so it never overflows TMP's 128-char tag
            // buffer); resolve it back to the full URL via the owning bubble. Fall back to the
            // id itself for any tag that still embeds a raw URL.
            string url = linkId;
            var owner = GetComponentInParent<MessageItemView>();
            if (owner != null && owner.TryResolveLink(linkId, out string resolved))
                url = resolved;

            if (!string.IsNullOrEmpty(url)) Application.OpenURL(url);
        }
    }
}
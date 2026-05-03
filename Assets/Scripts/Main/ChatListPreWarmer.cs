using System.Collections;
using UnityEngine;

[DefaultExecutionOrder(-200)]
public class ChatListPreWarmer : MonoBehaviour
{
    [SerializeField, Tooltip("Screen_Whatsapp. Briefly activated during boot so ChatListView spawns its row prefabs before the user taps the WhatsApp tab.")]
    private GameObject chatScreen;

    private CanvasGroup hideGroup;

    private void Awake()
    {
        if (chatScreen == null || chatScreen.activeSelf) return;

        hideGroup = chatScreen.GetComponent<CanvasGroup>();
        if (hideGroup == null) hideGroup = chatScreen.AddComponent<CanvasGroup>();
        hideGroup.alpha = 0f;
        hideGroup.blocksRaycasts = false;
        hideGroup.interactable = false;

        chatScreen.SetActive(true);
        StartCoroutine(RestoreAfterPreWarm());
    }

    private IEnumerator RestoreAfterPreWarm()
    {
        yield return null;
        yield return null;

        if (hideGroup == null) yield break;
        hideGroup.alpha = 1f;
        hideGroup.blocksRaycasts = true;
        hideGroup.interactable = true;
    }
}

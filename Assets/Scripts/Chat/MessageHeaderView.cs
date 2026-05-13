using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;

public class MessageHeaderView : MonoBehaviour
{
    [Header("Text UI")]
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI statusText;

    [Header("Avatar UI")]
    public Image avatarImage;
    public Image defaultAvatarBg;
    public Image defaultAvatarSilhouette; // Explicitly assign the child silhouette here!

    private string currentChatId;

    void OnEnable()
    {
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnChatSelected += HandleChatSelected;
        }
    }

    void OnDisable()
    {
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnChatSelected -= HandleChatSelected;
        }
    }

    void HandleChatSelected(string chatId)
    {
        currentChatId = chatId;
        ChatViewModel vm = ChatManager.Instance.GetChat(chatId);

        if (vm == null) return;

        // 1. Set the Text
        nameText.text = vm.Title;
        // statusText.text = vm.OnlineStatus;

        // 2. Handle the Avatar
        if (vm.AvatarSprite != null)
        {
            ApplyAvatarSprite(vm.AvatarSprite);
        }
        else
        {
            bool loadedFromCache = false;

            // Check hard drive cache instantly
            if (!string.IsNullOrEmpty(vm.AvatarUrl) && MediaCacheManager.Instance != null && MediaCacheManager.Instance.IsImageCached(vm.AvatarUrl))
            {
                string path = MediaCacheManager.Instance.GetFilePathFromUrl(vm.AvatarUrl);
                byte[] bytes = System.IO.File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2);

                if (tex.LoadImage(bytes))
                {
                    var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                    vm.AvatarSprite = sprite;
                    ApplyAvatarSprite(sprite);
                    loadedFromCache = true;
                }
            }

            // Fallback to Colored Default Avatar
            if (!loadedFromCache)
            {
                avatarImage.gameObject.SetActive(false);
                defaultAvatarBg.gameObject.SetActive(true);

                ApplyDefaultAvatarColor(chatId);

                // Fetch from network if it has a URL but wasn't cached
                if (!string.IsNullOrEmpty(vm.AvatarUrl))
                {
                    StopAllCoroutines();
                    StartCoroutine(LoadAvatar(vm));
                }
            }
        }
    }

    // Assigns the avatar sprite and forces a clean Image + ImageWithRoundedCorners
    // refresh. Without the off/on toggle, the first sprite assigned after panel
    // activation can render squeezed: Image.preserveAspect produces a non-square
    // mesh for non-square source textures, but the rounded-corner shader keeps
    // drawing its SDF in the RectTransform's 96x96 logical space, so the visible
    // shape comes out as an ellipse. The toggle re-fires OnEnable on both the
    // Image (clean mesh rebuild while active) and ImageWithRoundedCorners
    // (re-reads outerUV from the new sprite and re-pushes the shader props).
    private void ApplyAvatarSprite(Sprite sprite)
    {
        avatarImage.sprite = sprite;
        avatarImage.gameObject.SetActive(false);
        avatarImage.gameObject.SetActive(true);
        defaultAvatarBg.gameObject.SetActive(false);
    }

    IEnumerator LoadAvatar(ChatViewModel vm)
    {
        using UnityWebRequest req = UnityWebRequest.Get(vm.AvatarUrl);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success) yield break;

        byte[] bytes = req.downloadHandler.data;

        if (MediaCacheManager.Instance != null) 
            MediaCacheManager.Instance.SaveImageToCache(vm.AvatarUrl, bytes);

        var tex = new Texture2D(2, 2);
        if (tex.LoadImage(bytes))
        {
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            vm.AvatarSprite = sprite;

            // Safety check: ensure the user hasn't quickly swiped to a different chat while this was downloading!
            if (currentChatId == vm.ChatId)
            {
                ApplyAvatarSprite(sprite);
            }
        }
    }

    // --- EXACT SAME COLOR LOGIC FROM CHAT LIST ---
    private static readonly string[][] AvatarColors = new string[][]
    {
        new string[] { "#CFE9E4", "#00A884" }, 
        new string[] { "#D6E4FB", "#1FA2FF" }, 
        new string[] { "#EADCF1", "#A348D4" }, 
        new string[] { "#FCE1D0", "#F8942F" }, 
        new string[] { "#FCE2EC", "#E14781" }  
    };

    private void ApplyDefaultAvatarColor(string id)
    {
        if (defaultAvatarBg == null || defaultAvatarSilhouette == null) return;
        
        int hash = 0;
        if (!string.IsNullOrEmpty(id))
        {
            foreach (char c in id) hash += c;
        }
        
        int colorIndex = Mathf.Abs(hash) % AvatarColors.Length;

        Color bgColor, fgColor;
        UnityEngine.ColorUtility.TryParseHtmlString(AvatarColors[colorIndex][0], out bgColor);
        UnityEngine.ColorUtility.TryParseHtmlString(AvatarColors[colorIndex][1], out fgColor);

        defaultAvatarBg.color = bgColor;
        defaultAvatarSilhouette.color = fgColor;
    }
}
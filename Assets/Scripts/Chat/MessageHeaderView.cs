using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;
using DG.Tweening;

public class MessageHeaderView : MonoBehaviour
{
    [Header("Text UI")]
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI statusText;

    [Header("Avatar UI")]
    public Image avatarImage;
    public Image defaultAvatarBg;
    public Image defaultAvatarSilhouette; // Explicitly assign the child silhouette here!

    // «Личные и рабочие чаты» (spec §5.5, §5.6) — stamped by PersonalChatsBuilder.
    [Header("Personal chats")]
    [SerializeField] private SemiAutoToggle semiAutoToggle;          // «Авто» — hidden in a personal chat
    [SerializeField] private Button personalPill;                    // «Личный» — in its place, opens «Сделать чат рабочим?»
    [SerializeField] private Image pillLock;                         // the pill's lock — the «Сделать личным» card wears it too
    [SerializeField] private Button nameTap;                         // over the avatar and the name — «Сделать личным»
    [SerializeField] private SuggestionsController suggestions;      // the chat's mode once working — the sheet's text

    private string currentChatId;
    private ChatViewModel watchedChat;   // the open chat — its facts can move it between personal and working

    // Avatar download deferred from HandleChatSelected because the GameObject
    // was inactive at the time (SelectChat fires OnChatSelected during Prep,
    // before OpenChatRoutine activates the panel). Flushed in OnEnable.
    private ChatViewModel pendingAvatarVm;

    // OnChatSelected subscription lives in Awake (not OnEnable) so the event
    // delivery works even when the messages panel is inactive — which is the
    // case between chats (slide-out deactivates the panel) and during the Prep
    // phase before SlideInToMessages re-activates it. Matches MessageListView's
    // pattern for the same reason.
    void Awake()
    {
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnChatSelected += HandleChatSelected;
        }
        // In Awake for the same reason: a move can land while the messages panel is inactive.
        ChatAudienceStore.OnChanged += ApplyAudience;
        if (personalPill != null) personalPill.onClick.AddListener(HandlePersonalPillTapped);
        if (nameTap != null) nameTap.onClick.AddListener(HandleNameTapped);
    }

    void OnDestroy()
    {
        EmojiPatchService.OnEmojiReady -= HandleEmojiReady;
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnChatSelected -= HandleChatSelected;
        }
        ChatAudienceStore.OnChanged -= ApplyAudience;
        WatchChat(null);
        if (personalPill != null) personalPill.onClick.RemoveListener(HandlePersonalPillTapped);
        if (nameTap != null) nameTap.onClick.RemoveListener(HandleNameTapped);
    }

    private void ApplyTitle(ChatViewModel vm)
    {
        nameText.text = UnicodeEmojiConverter.ConvertRealEmojisToSprites(
            vm.Title ?? "", MissingEmojiMode.Hide, out bool titleMissing);

        EmojiPatchService.OnEmojiReady -= HandleEmojiReady;
        if (titleMissing) EmojiPatchService.OnEmojiReady += HandleEmojiReady;
    }

    private void HandleEmojiReady(string spriteName)
    {
        if (string.IsNullOrEmpty(currentChatId) || ChatManager.Instance == null) return;
        ChatViewModel vm = ChatManager.Instance.GetChat(currentChatId);
        if (vm != null) ApplyTitle(vm);
    }

    void OnEnable()
    {
        if (pendingAvatarVm != null)
        {
            var vm = pendingAvatarVm;
            pendingAvatarVm = null;
            StartCoroutine(LoadAvatar(vm));
        }
    }

    void HandleChatSelected(string chatId)
    {
        currentChatId = chatId;
        pendingAvatarVm = null; // discard any deferred fetch from a previous chat

        ChatViewModel vm = ChatManager.Instance.GetChat(chatId);
        WatchChat(vm);
        ApplyAudience();   // the pill or the chip, before the panel is even shown

        if (vm == null) return;

        // 1. Set the Text — Hide mode so a not-yet-downloaded emoji sprite never
        // shows as tofu/tag text; re-rendered via OnEmojiReady once it lands.
        ApplyTitle(vm);
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

                // Fetch from network if it has a URL but wasn't cached. StartCoroutine
                // throws on an inactive GameObject — defer to OnEnable when this fires
                // during Prep (before OpenChatRoutine activates the panel).
                if (!string.IsNullOrEmpty(vm.AvatarUrl))
                {
                    if (isActiveAndEnabled)
                    {
                        StopAllCoroutines();
                        StartCoroutine(LoadAvatar(vm));
                    }
                    else
                    {
                        pendingAvatarVm = vm;
                    }
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

    // --- «Личные и рабочие чаты» (spec §5.5, §5.6, sketch 009 screens 5–6) ---
    // A personal chat wears the «Личный» pill instead of the «Авто» chip; the pill is a button: «Сделать чат рабочим?». A
    // working chat keeps the chip, and a tap on its name or avatar opens «Сделать личным». Judged by the verdict the list
    // draws (ChatAudienceStore.ViewOf under the active channel's profile) and judged again on every decision or move and on
    // every update of the open chat — a move made from here shows at once, while the chat is still open.

    private void ApplyAudience()
    {
        bool personal = TryGetOpenChat(out _, out _, out _, out AudienceView view) && view.Kind == AudienceKind.Personal;
        if (semiAutoToggle != null && semiAutoToggle.gameObject.activeSelf == personal) semiAutoToggle.gameObject.SetActive(!personal);
        if (personalPill != null && personalPill.gameObject.activeSelf != personal) personalPill.gameObject.SetActive(personal);
    }

    // «Сделать чат рабочим?». A chat that is not a person can never be working — its pill says why instead.
    private void HandlePersonalPillTapped()
    {
        Punch(personalPill.transform);
        if (!TryGetOpenChat(out string botName, out string profileId, out ChatViewModel chat, out AudienceView view)) return;
        if (view.Kind != AudienceKind.Personal) return;   // moved since the pill was drawn
        if (!view.CanMove)
        {
            AudienceToast.Show(PersonalChatsCopy.NonHumanLine(view.NonHuman));
            return;
        }
        if (!CanBeMoved(profileId, chat)) return;
        // «Авто» once working = the panel would not suggest there: the chat's own mode, the schedule and the quota.
        bool autoOn = suggestions != null && !suggestions.WouldSuggestIgnoringAudience();
        PersonalChatSheets.AskMakeBusiness(botName, profileId, chat, autoOn, RegularFont, SemiboldFont);
    }

    // «Сделать личным» — only for a working chat (a personal one moves through its pill).
    private void HandleNameTapped()
    {
        if (!TryGetOpenChat(out string botName, out string profileId, out ChatViewModel chat, out AudienceView view)) return;
        if (view.Kind != AudienceKind.Business || !view.CanMove || !CanBeMoved(profileId, chat)) return;
        PersonalChatSheets.AskMakePersonal(botName, profileId, chat, PersonalChatSheets.NumberLine(chat, view), RegularFont, SemiboldFont,
            pillLock != null ? pillLock.sprite : null);
    }

    private bool TryGetOpenChat(out string botName, out string profileId, out ChatViewModel chat, out AudienceView view)
    {
        botName = profileId = null;
        chat = null;
        view = null;
        ChatManager manager = ChatManager.Instance;
        if (manager == null || string.IsNullOrEmpty(currentChatId)) return false;
        chat = manager.GetChat(currentChatId);
        if (chat == null) return false;
        botName = manager.CurrentBotId;
        profileId = manager.ActiveChannelProfileId();
        view = ChatAudienceStore.ViewOf(profileId, chat);
        return true;
    }

    // A move the queue would drop (no real profile, an id the server refuses) is never offered.
    private static bool CanBeMoved(string profileId, ChatViewModel chat) =>
        ChatAudienceRequest.IsRealProfileId(profileId) && ChatAudienceRequest.IsSendableChatId(chat.ChatId);

    private void WatchChat(ChatViewModel chat)
    {
        if (watchedChat == chat) return;
        if (watchedChat != null) watchedChat.OnUpdated -= HandleWatchedChatUpdated;
        watchedChat = chat;
        if (watchedChat != null) watchedChat.OnUpdated += HandleWatchedChatUpdated;
    }

    private void HandleWatchedChatUpdated(ChatViewModel chat) => ApplyAudience();

    // The sheets wear the header's own fonts: SF Pro Regular (the status line) and Semibold (the name).
    private TMP_FontAsset RegularFont => statusText != null ? statusText.font : null;
    private TMP_FontAsset SemiboldFont => nameText != null ? nameText.font : null;

    // The «Авто» chip's press (SemiAutoToggle).
    private static void Punch(Transform target)
    {
        target.DOKill();
        target.localScale = Vector3.one;
        target.DOPunchScale(Vector3.one * -0.04f, 0.18f, 1, 0.5f);
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
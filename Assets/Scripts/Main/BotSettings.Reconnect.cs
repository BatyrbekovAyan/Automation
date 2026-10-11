using System;
using System.Collections;
using Automation.BotSettingsUI;
using Nobi.UiRoundedCorners;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// F2 (plan 2026-10-11 §5.1–5.2, task T9): the settings screen around a channel the server retired.
/// <list type="bullet">
/// <item>Under each channel row, a hint: «Отключён · нажмите, чтобы подключить снова» (+ «Ранее: +7 …» when a number is
/// stored) — a tap starts the channel's fresh path; an unfinished app delete reads «Удаление не завершено · удалите ещё
/// раз», its switch is locked and a tap opens the delete question (never «Подключить»). The row's old number field is
/// hidden: tapping it would log out a profile the server deleted.</item>
/// <item>At the top of «Основное», while a channel waits for its reconnect and no plan is active, the «Бот отключён»
/// banner with «Выбрать тариф» (the paywall, through the gate).</item>
/// <item>Before the fresh path creates a profile, the wait for the server to see the payment
/// (<see cref="ReconnectSettlePolicy"/>): «Подтверждаем оплату…» in the row's hint, and — when the schedule is spent —
/// «Оплата ещё не дошла до сервера…», no pairing.</item>
/// </list>
/// Everything here is RUNTIME-ADDED into the tab's existing VerticalLayoutGroup — the «Проверить бота» row's pattern
/// (BotSettings.Test): the prefab stays untouched, so no Bot Settings builder (and never the destructive rebuild) can lose
/// it. The decisions are <see cref="RetiredChannelPolicy"/>'s, the copy is <see cref="RetiredChannelCopy"/>'s.
/// </summary>
public partial class BotSettings
{
    public const string ReconnectBannerName = "ReconnectBanner";
    public const string ReconnectHintWhatsappName = "ReconnectHintWhatsapp";
    public const string ReconnectHintTelegramName = "ReconnectHintTelegram";

    // Banner — a card like the rows around it (the «Проверить бота» row's radius and insets).
    private const float BannerRadius = 28f;
    private const float BannerInset = 40f;
    private const float BannerTop = 36f;
    private const float BannerTitleSize = 36f;
    private const float BannerTitleHeight = 46f;
    private const float BannerBodySize = 30f;
    private const float BannerBodyGap = 12f;
    private const float BannerBodyFallback = 152f;          // four lines at 30 — the longest body takes three
    private const float BannerCtaGap = 28f;
    private const float BannerCtaHeight = 120f;             // house touch floor
    private const float BannerCtaSize = 34f;

    // Row hint — a footnote under the row's card, inset like the card's own label.
    private const float HintInset = 40f;
    private const float HintSize = 30f;
    private const float HintPad = 6f;
    private const float HintFallbackHeight = 84f;           // two lines at 30
    private const float GeneralContentWidthFallback = 980f; // 1080 minus the tab's 50 + 50 padding

    private sealed class ReconnectHint
    {
        public GameObject Root;
        public TextMeshProUGUI Text;
        public ChatChannel Channel;
        public RetiredChannelPolicy.SettingsRowState State;
        public string Notice;
        public bool NoticeIsError;
    }

    private GameObject reconnectBanner;
    private Image bannerImage, bannerCtaImage;
    private TextMeshProUGUI bannerTitle, bannerBody, bannerCtaLabel;
    private ReconnectHint whatsappHint, telegramHint;
    private bool reconnectEventsBound, reconnectLayoutPending;
    private string seenWhatsappProfileId, seenTelegramProfileId;
    private Bot pairedBotCache;     // resolved on every refresh; LateUpdate reads only this (no per-frame GetComponent)

    /// <summary>The bot card paired with this screen — the same sibling index under BotsParent (Bot.OpenSettings' rule).</summary>
    private Bot PairedBot
    {
        get
        {
            Transform bots = Manager.Instance != null ? Manager.Instance.BotsRoot : null;
            int index = transform.GetSiblingIndex();
            return bots != null && index < bots.childCount ? bots.GetChild(index).GetComponent<Bot>() : null;
        }
    }

    /// <summary>Re-derives the banner, both row hints, the switches' lock and the number fields from the bot's state now.</summary>
    public void RefreshReconnectUi()
    {
        if (!EnsureReconnectUi()) return;
        Bot bot = pairedBotCache = PairedBot;
        var claims = AuthClient.Existing != null ? AuthClient.Existing.Claims : null;
        RetiredChannelPolicy.Channel wa = ProfileRetirement.ChannelOf(bot, ChatChannel.WhatsApp);
        RetiredChannelPolicy.Channel tg = ProfileRetirement.ChannelOf(bot, ChatChannel.Telegram);

        ApplyChannelHint(whatsappHint, RetiredChannelPolicy.SettingsRow(wa, claims), whatsappRow, WhatsappNumberField);
        ApplyChannelHint(telegramHint, RetiredChannelPolicy.SettingsRow(tg, claims), telegramRow, TelegramNumberField);

        bool banner = bot != null && RetiredChannelPolicy.ShowBanner(wa, tg, claims, EntitlementGate.CurrentTier);
        reconnectBanner.SetActive(banner);
        if (banner) bannerBody.text = RetiredChannelCopy.BannerBody(RetiredChannelPolicy.BannerReason(wa, tg, claims));

        PlaceReconnectUi();
        PaintReconnectUi();
        reconnectLayoutPending = true;
        MeasureReconnectUi();
        seenWhatsappProfileId = bot != null ? bot.whatsappProfileId : null;
        seenTelegramProfileId = bot != null ? bot.telegramProfileId : null;
    }

    /// <summary>
    /// Starts the fresh path of a retired channel — the post-purchase sheet's «Подключить» and the row hint's tap. Waits for
    /// the screen's slide-in, moves an M4-era marker aside first, then switches the row on exactly as the owner would
    /// (gate → server wait → profile.add → pairing → Create naming the retired profile).
    /// </summary>
    public void BeginReconnect(ChatChannel channel)
    {
        if (!isActiveAndEnabled) return;
        StartCoroutine(BeginReconnectRoutine(channel));
    }

    private IEnumerator BeginReconnectRoutine(ChatChannel channel)
    {
        while (SwipeToBackBotSettings.Instance != null && SwipeToBackBotSettings.Instance.IsAnimating) yield return null;
        Bot bot = PairedBot;
        if (bot == null) yield break;
        var claims = AuthClient.Existing != null ? AuthClient.Existing.Claims : null;
        ProfileRetirement.MoveAsideIfMarked(bot, channel, claims);
        if (RetiredChannelPolicy.SettingsRow(ProfileRetirement.ChannelOf(bot, channel), claims)
            != RetiredChannelPolicy.SettingsRowState.Reconnect) yield break;     // live again, or an unfinished delete

        OpenGeneralTab();
        Toggle toggle = channel == ChatChannel.Telegram ? TelegramToggle : WhatsappToggle;
        if (toggle == null) yield break;
        if (!toggle.isOn) toggle.isOn = true;   // the owner's own switch: EnableSave + CheckXAuthorization
        else StartCoroutine(channel == ChatChannel.Telegram ? CheckTelegramAuthorization() : CheckWhatsappAuthorization());
    }

    /// <summary>
    /// The fresh path's wait for the server (plan §5.1): only when the last GetUsage snapshot DEFINITELY says «no free
    /// channel» (the store callback lands before RevenueCat's webhook) — then re-read on the settle schedule until the
    /// server agrees. <paramref name="done"/>(false) = gave up: nothing may be paired (Create would refuse and the server
    /// would delete the freshly paired profile).
    /// </summary>
    private IEnumerator WaitForServerChannelSlot(ChatChannel channel, Action<bool> done)
    {
        SetRowNotice(channel, null, false);
        if (!ReconnectSettlePolicy.ShouldWaitBeforePairing(UsageStore.Current))
        {
            done(true);
            yield break;
        }
        SetRowNotice(channel, RetiredChannelCopy.Settling, false);
        for (int read = 0; ; read++)
        {
            float? delay = ReconnectSettlePolicy.DelayBeforeRead(read);
            if (delay == null) break;
            if (delay.Value > 0f) yield return new WaitForSeconds(delay.Value);
            yield return UsageClient.FetchRoutine();
            if (ReconnectSettlePolicy.ShouldWaitBeforePairing(UsageStore.Current)) continue;
            SetRowNotice(channel, null, false);
            done(true);
            yield break;
        }
        SetRowNotice(channel, RetiredChannelCopy.SettleTimedOut, true);
        done(false);
    }

    private void SetRowNotice(ChatChannel channel, string notice, bool isError)
    {
        if (!EnsureReconnectUi()) return;
        ReconnectHint hint = channel == ChatChannel.Telegram ? telegramHint : whatsappHint;
        hint.Notice = notice;
        hint.NoticeIsError = isError;
        RefreshReconnectUi();
    }

    // ── lifecycle (called from BotSettings.OnEnable / OnDisable / Start) ──

    private void BindReconnectEvents()
    {
        if (reconnectEventsBound) return;
        reconnectEventsBound = true;
        ProfileRetirement.Changed += HandleReconnectRetirementChanged;
        BillingService.OnEntitlementChanged += HandleReconnectEntitlementChanged;
        UsageStore.OnUsageChanged += RefreshReconnectUi;
        Theme.Changed += RefreshReconnectUi;    // the «Ранее» line carries its colour in the text — re-derive, not only repaint
    }

    private void UnbindReconnectEvents()
    {
        if (!reconnectEventsBound) return;
        reconnectEventsBound = false;
        ProfileRetirement.Changed -= HandleReconnectRetirementChanged;
        BillingService.OnEntitlementChanged -= HandleReconnectEntitlementChanged;
        UsageStore.OnUsageChanged -= RefreshReconnectUi;
        Theme.Changed -= RefreshReconnectUi;
    }

    /// <summary>The wait above dies with the screen (Unity stops its coroutines) — so do its notices.</summary>
    private void ResetReconnectNotices()
    {
        foreach (ReconnectHint hint in new[] { whatsappHint, telegramHint })
        {
            if (hint == null) continue;
            hint.Notice = null;
            hint.NoticeIsError = false;
        }
    }

    private void HandleReconnectRetirementChanged(string botKey)
    {
        if (pairedBotCache == null || pairedBotCache.name == botKey) RefreshReconnectUi();
    }

    private void HandleReconnectEntitlementChanged(PlanTier _) => RefreshReconnectUi();

    // A profile id changes behind the screen's back (a pairing created one, a back-out deleted it, Create's channel_limit
    // reset it): re-derive then. A measurement that had to wait for the tab to be active lands here too.
    private void LateUpdate()
    {
        Bot bot = pairedBotCache;
        if (bot != null && (bot.whatsappProfileId != seenWhatsappProfileId || bot.telegramProfileId != seenTelegramProfileId))
            RefreshReconnectUi();
        else if (reconnectLayoutPending) MeasureReconnectUi();
    }

    // ── state → screen ──

    private void ApplyChannelHint(ReconnectHint hint, RetiredChannelPolicy.SettingsRowState state, ToggleRow row, EditableField numberField)
    {
        hint.State = state;
        string text = HintText(hint, numberField);
        hint.Root.SetActive(text != null);
        if (text != null) hint.Text.text = text;
        if (row != null && row.Toggle != null)
            row.Toggle.interactable = state != RetiredChannelPolicy.SettingsRowState.DeleteUnfinished;
        if (numberField != null)
            numberField.gameObject.SetActive(state == RetiredChannelPolicy.SettingsRowState.Normal
                                             && !string.IsNullOrEmpty(numberField.Value));
    }

    private static string HintText(ReconnectHint hint, EditableField numberField)
    {
        if (hint.Notice != null) return hint.Notice;
        switch (hint.State)
        {
            case RetiredChannelPolicy.SettingsRowState.Reconnect:
                string previous = RetiredChannelCopy.PreviousNumber(numberField != null ? numberField.Value : null);
                if (previous == null) return RetiredChannelCopy.RowHint;
                string tertiary = ColorUtility.ToHtmlStringRGB(Theme.Color(ThemeRole.InkTertiary));
                return $"{RetiredChannelCopy.RowHint}\n<color=#{tertiary}>{previous}</color>";
            case RetiredChannelPolicy.SettingsRowState.DeleteUnfinished:
                return ProfileRetirementCopy.DeleteUnfinished;
            default:
                return null;
        }
    }

    private void OnReconnectHintTapped(ReconnectHint hint)
    {
        if (hint.Notice != null && hint.State == RetiredChannelPolicy.SettingsRowState.Normal) return;
        if (hint.State == RetiredChannelPolicy.SettingsRowState.Reconnect) BeginReconnect(hint.Channel);
        else if (hint.State == RetiredChannelPolicy.SettingsRowState.DeleteUnfinished) OpenDeleteBotPopup();
    }

    private static void OnBannerCtaTapped() => EntitlementGate.RequestPaywall(PaywallTrigger.Browse);

    private void PaintReconnectUi()
    {
        if (reconnectBanner == null) return;
        bannerImage.color = Theme.Color(ThemeRole.DestructiveSoft);
        bannerTitle.color = Theme.Color(ThemeRole.Destructive);
        bannerBody.color = Theme.Color(ThemeRole.InkPrimary);
        bannerCtaImage.color = Theme.Color(ThemeRole.AccentFill);
        bannerCtaLabel.color = Theme.Color(ThemeRole.AccentOnFill);
        foreach (ReconnectHint hint in new[] { whatsappHint, telegramHint })
            hint.Text.color = Theme.Color(HintRole(hint));
    }

    // An action reads as a link (the accent text), a failure and an unfinished delete in destructive ink.
    private static ThemeRole HintRole(ReconnectHint hint)
    {
        if (hint.Notice != null) return hint.NoticeIsError ? ThemeRole.Destructive : ThemeRole.AccentText;
        return hint.State == RetiredChannelPolicy.SettingsRowState.DeleteUnfinished ? ThemeRole.Destructive : ThemeRole.AccentText;
    }

    // ── layout ──

    private void PlaceReconnectUi()
    {
        Transform group = whatsappRow != null ? whatsappRow.transform.parent : null;
        if (group == null) return;
        if (reconnectBanner.transform.GetSiblingIndex() != 0) reconnectBanner.transform.SetSiblingIndex(0);
        PlaceAfter(whatsappHint.Root.transform, AnchorFor(whatsappRow, WhatsappNumberField));
        PlaceAfter(telegramHint.Root.transform, AnchorFor(telegramRow, TelegramNumberField));
    }

    /// <summary>The hint sits right under the row's number field when that is the row's sibling, else right under the row.</summary>
    private static Transform AnchorFor(ToggleRow row, EditableField numberField) =>
        numberField != null && row != null && numberField.transform.parent == row.transform.parent
            ? numberField.transform
            : row != null ? row.transform : null;

    private static void PlaceAfter(Transform item, Transform anchor)
    {
        if (item == null || anchor == null || item.parent != anchor.parent) return;
        int a = anchor.GetSiblingIndex();
        int i = item.GetSiblingIndex();
        int target = i < a ? a : a + 1;
        if (i != target) item.SetSiblingIndex(target);
    }

    /// <summary>
    /// Heights from the measured text — TMP measures only an ACTIVE label, so a refresh while «Основное» is hidden leaves
    /// this pending for <see cref="LateUpdate"/>.
    /// </summary>
    private void MeasureReconnectUi()
    {
        if (reconnectBanner == null || General == null || !General.activeInHierarchy) return;
        reconnectLayoutPending = false;
        float width = GeneralContentWidth();
        foreach (ReconnectHint hint in new[] { whatsappHint, telegramHint })
        {
            if (!hint.Root.activeSelf) continue;
            float measured = hint.Text.GetPreferredValues(hint.Text.text, width - 2f * HintInset, 0f).y;
            float height = measured > 0f ? Mathf.Ceil(measured) + 2f * HintPad : HintFallbackHeight;
            SetHeight((RectTransform)hint.Root.transform, height);
        }
        if (reconnectBanner.activeSelf)
        {
            float measured = bannerBody.GetPreferredValues(bannerBody.text, width - 2f * BannerInset, 0f).y;
            float body = measured > 0f ? Mathf.Ceil(measured) : BannerBodyFallback;
            float bodyTop = BannerTop + BannerTitleHeight + BannerBodyGap;
            PlaceTopStretch((RectTransform)bannerBody.transform, bodyTop, body, BannerInset);
            float ctaTop = bodyTop + body + BannerCtaGap;
            PlaceTopStretch((RectTransform)bannerCtaImage.transform, ctaTop, BannerCtaHeight, BannerInset);
            SetHeight((RectTransform)reconnectBanner.transform, ctaTop + BannerCtaHeight + BannerTop);
        }
        if (whatsappRow != null && whatsappRow.transform.parent is RectTransform group)
            LayoutRebuilder.MarkLayoutForRebuild(group);
    }

    private float GeneralContentWidth()
    {
        if (whatsappRow != null && whatsappRow.transform.parent is RectTransform group)
        {
            var layout = group.GetComponent<VerticalLayoutGroup>();
            float inner = group.rect.width - (layout != null ? layout.padding.left + layout.padding.right : 0f);
            if (inner > 0f) return inner;
        }
        return GeneralContentWidthFallback;
    }

    private static void SetHeight(RectTransform rt, float height)
    {
        if (!Mathf.Approximately(rt.sizeDelta.y, height)) rt.sizeDelta = new Vector2(rt.sizeDelta.x, height);
    }

    // ── build (once per screen instance; the layout group sizes the width) ──

    private bool EnsureReconnectUi()
    {
        if (reconnectBanner != null) return true;
        Transform group = whatsappRow != null ? whatsappRow.transform.parent : null;
        if (group == null || telegramRow == null) return false;
        reconnectBanner = BuildReconnectBanner(group);
        whatsappHint = BuildReconnectHint(group, ReconnectHintWhatsappName, ChatChannel.WhatsApp);
        telegramHint = BuildReconnectHint(group, ReconnectHintTelegramName, ChatChannel.Telegram);
        return true;
    }

    private GameObject BuildReconnectBanner(Transform group)
    {
        var go = NewReconnectChild(group, ReconnectBannerName, typeof(Image));
        ((RectTransform)go.transform).sizeDelta = new Vector2(0f, 360f);
        bannerImage = go.GetComponent<Image>();
        bannerImage.raycastTarget = false;     // a card, not a control: drags still reach the tab's ScrollRect
        var rounded = go.AddComponent<ImageWithRoundedCorners>();
        rounded.radius = BannerRadius;
        rounded.Validate();
        rounded.Refresh();

        bannerTitle = NewReconnectTmp(go.transform, "Title", RetiredChannelCopy.BannerTitle, BannerTitleSize, TestTitleFont);
        bannerTitle.textWrappingMode = TextWrappingModes.NoWrap;
        bannerTitle.overflowMode = TextOverflowModes.Ellipsis;
        PlaceTopStretch((RectTransform)bannerTitle.transform, BannerTop, BannerTitleHeight, BannerInset);

        bannerBody = NewReconnectTmp(go.transform, "Body", RetiredChannelCopy.BannerBodyOther, BannerBodySize, TestBodyFont);
        PlaceTopStretch((RectTransform)bannerBody.transform, BannerTop + BannerTitleHeight + BannerBodyGap, BannerBodyFallback, BannerInset);

        var cta = NewReconnectChild(go.transform, "Cta", typeof(Image), typeof(Button));
        bannerCtaImage = cta.GetComponent<Image>();
        bannerCtaImage.raycastTarget = true;
        var ctaRounded = cta.AddComponent<ImageWithRoundedCorners>();
        ctaRounded.radius = BannerCtaHeight * 0.5f;
        ctaRounded.Validate();
        ctaRounded.Refresh();
        var button = cta.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = bannerCtaImage;
        PopupUI.WireFingerUp(button, OnBannerCtaTapped);
        bannerCtaLabel = NewReconnectTmp(cta.transform, "Label", RetiredChannelCopy.BannerCta, BannerCtaSize, TestTitleFont);
        bannerCtaLabel.alignment = TextAlignmentOptions.Center;
        bannerCtaLabel.textWrappingMode = TextWrappingModes.NoWrap;
        var labelRect = (RectTransform)bannerCtaLabel.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;

        go.SetActive(false);
        return go;
    }

    private ReconnectHint BuildReconnectHint(Transform group, string name, ChatChannel channel)
    {
        var go = NewReconnectChild(group, name, typeof(Image), typeof(Button));
        ((RectTransform)go.transform).sizeDelta = new Vector2(0f, HintFallbackHeight);
        var hit = go.GetComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);  // the whole footnote is the tap target, ≥ 84 units tall
        hit.raycastTarget = true;
        var button = go.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hit;

        var text = NewReconnectTmp(go.transform, "Text", "", HintSize, TestBodyFont);
        var rt = (RectTransform)text.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(HintInset, HintPad);
        rt.offsetMax = new Vector2(-HintInset, -HintPad);
        text.alignment = TextAlignmentOptions.TopLeft;

        var hint = new ReconnectHint { Root = go, Text = text, Channel = channel };
        PopupUI.WireFingerUp(button, () => OnReconnectHintTapped(hint));
        go.SetActive(false);
        return hint;
    }

    private static GameObject NewReconnectChild(Transform parent, string name, params Type[] components)
    {
        var all = new Type[components.Length + 1];
        all[0] = typeof(RectTransform);
        components.CopyTo(all, 1);
        var go = new GameObject(name, all);
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        return go;
    }

    private static TextMeshProUGUI NewReconnectTmp(Transform parent, string name, string text, float size, TMP_FontAsset font)
    {
        var go = NewReconnectChild(parent, name, typeof(TextMeshProUGUI));
        var tmp = go.GetComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.richText = true;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void PlaceTopStretch(RectTransform rt, float top, float height, float inset)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -top);
        rt.sizeDelta = new Vector2(-2f * inset, height);
    }
}

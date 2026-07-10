using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Phase-2 live data source behind the <see cref="ISuggestionsProvider"/> seam (N8N-02). Consumes
/// the shared Suggest Replies n8n webhook and maps its <c>{text,label}[]</c> into ranked
/// <see cref="SuggestionItem"/>s — swapping in for <see cref="MockSuggestionsProvider"/> on the
/// SINGLE <c>SuggestionsController.Awake</c> line, with ZERO other Phase-1 edits.
///
/// Plain C# class (NOT a MonoBehaviour). The network coroutine runs on the ALWAYS-active
/// <see cref="ChatManager.Instance"/> (the controller GameObject can be inactive ~300 ms when
/// OnChatSelected fires — Pitfall 1), waits on the public <c>WaitForChatFetchesDrain()</c> serial
/// guard before assembling the payload, and NEVER bumps the chat-fetch in-flight counter (it only
/// waits — it is not a messages/get caller). <see cref="BuildPayloadJson"/> and
/// <see cref="MapResponse"/> are pure statics so every assembly/mapping branch is EditMode-testable.
/// </summary>
public class N8nSuggestionsProvider : ISuggestionsProvider
{
    private const int MaxMessages     = 12;
    private const int MaxTextChars    = 500;
    private const int MaxPromptChars  = 500;
    private const int MaxCatalogChars = 1500;

    // --- ISuggestionsProvider ------------------------------------------------

    public void Request(SuggestionRequest request, Action<SuggestionResult> callback)
    {
        var cm = ChatManager.Instance;
        if (cm == null) { callback?.Invoke(Empty(request)); return; }

        // An active bot + an open chat are required to scope the request. Missing either
        // (or no ChatManager) short-circuits to Empty with NO coroutine and NO network call.
        Bot bot = Manager.Instance != null ? Manager.Instance.FindBotByName(cm.CurrentBotId) : null;
        if (bot == null || string.IsNullOrEmpty(cm.CurrentChatId))
        {
            callback?.Invoke(Empty(request));
            return;
        }

        // ChatManager is ALWAYS active — run the coroutine here, never on the controller (Pitfall 1).
        cm.StartCoroutine(Run(request, callback));
    }

    // --- Network coroutine (hosted on ChatManager.Instance) ------------------

    private IEnumerator Run(SuggestionRequest req, Action<SuggestionResult> cb)
    {
        // Defer to in-flight chat-open/sync/pagination fetches so this pull never races them
        // (Wappi crossing bugs). PUBLIC hook (no "To"); the provider only WAITS — it never bumps
        // the chat-fetch in-flight counter (it is not a messages/get caller — deadlock risk).
        yield return ChatManager.Instance.WaitForChatFetchesDrain();

        // Re-resolve AFTER the drain (up to ~3 s) so we assemble against the freshest history and
        // bail if the chat/bot changed underneath us.
        var cm = ChatManager.Instance;
        Bot bot = (cm != null && Manager.Instance != null) ? Manager.Instance.FindBotByName(cm.CurrentBotId) : null;
        if (cm == null || bot == null || !cm.TryGetRecentMessages(cm.CurrentChatId, MaxMessages, out var msgs))
        {
            cb?.Invoke(Empty(req));
            yield break;
        }

        string botName = bot.name;
        string json = BuildPayloadJson(
            req,
            profileId:      bot.whatsappProfileId,
            botWaId:        bot.whatsappWorkflowId,   // == the n8n workflow id; ""/"-1" => server skips RAG
            businessTypeId: PlayerPrefs.GetString(botName + "BusinessType", ""),
            businessName:   PlayerPrefs.GetString(botName + "Name", ""),
            ownerPrompt:    PlayerPrefs.GetString(botName + "Prompt", ""),
            catalog:        BuildCatalog(botName),
            recentMessages: msgs);

        using var www = new UnityWebRequest($"{Manager.n8nBaseUrl}/webhook/SuggestReplies", "POST");
        www.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");   // REQUIRED — libcurl else stamps x-www-form-urlencoded => n8n mis-parse
        www.timeout = 30;
        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[Suggest] fetch failed [{www.responseCode}] {www.error}");
            cb?.Invoke(Error(req));
            yield break;
        }

        cb?.Invoke(MapResponse(www.downloadHandler.text, req.requestSeq));   // requestSeq stamped from the REQUEST
    }

    // --- Catalog assembly (impure: reads PlayerPrefs) ------------------------

    // "• {name} — {price}" lines from the bot's products then services; empty-name items skipped.
    // The <=1500 clamp lives in BuildPayloadJson so it stays unit-tested (this stays PlayerPrefs-bound).
    private static string BuildCatalog(string botName)
    {
        var sb = new StringBuilder();
        AppendCatalogItems(sb, botName, "Product", PlayerPrefs.GetInt(botName + "ProductsNumber", 0));
        AppendCatalogItems(sb, botName, "Service", PlayerPrefs.GetInt(botName + "ServicesNumber", 0));
        return sb.ToString().TrimEnd('\n');
    }

    // SINGULAR item keys (Product{i}/Service{i}) per the bot-persistence key catalog.
    private static void AppendCatalogItems(StringBuilder sb, string botName, string singular, int count)
    {
        for (int i = 0; i < count; i++)
        {
            string name = PlayerPrefs.GetString(botName + singular + i, "");
            if (string.IsNullOrEmpty(name)) continue;
            string price = PlayerPrefs.GetString(botName + singular + i + "Price", "");
            sb.Append("• ").Append(name);
            if (!string.IsNullOrEmpty(price)) sb.Append(" — ").Append(price);
            sb.Append('\n');
        }
    }

    // --- Pure payload builder (Unity-free except MessageViewModel, a plain data class) ---

    /// <summary>
    /// Builds the frozen v1 request JSON: <c>v==1</c>; req fields passed through; the supplied
    /// bot fields; <paramref name="ownerPrompt"/> clamped &lt;=500 and <paramref name="catalog"/>
    /// &lt;=1500; messages = at most the LAST 12 of <paramref name="recentMessages"/>, oldest-&gt;newest,
    /// each text media-mapped then clamped &lt;=500, role="client" if incoming else "business".
    /// </summary>
    public static string BuildPayloadJson(
        SuggestionRequest req,
        string profileId,
        string botWaId,
        string businessTypeId,
        string businessName,
        string ownerPrompt,
        string catalog,
        List<MessageViewModel> recentMessages)
    {
        var dto = new SuggestRepliesRequestDto
        {
            v                = 1,
            requestSeq       = req != null ? req.requestSeq : 0L,
            profileId        = profileId,
            chatId           = req?.chatId,
            botWaId          = botWaId,
            businessTypeId   = businessTypeId,
            businessName     = businessName,
            ownerPrompt      = Clamp(ownerPrompt, MaxPromptChars),
            catalog          = Clamp(catalog, MaxCatalogChars),
            steerTowardText  = req?.steerTowardText,
            lastIncomingText = req?.lastIncomingText,
            messages         = ToWireMessages(recentMessages),
        };
        return JsonConvert.SerializeObject(dto);
    }

    // Take the LAST 12, preserve oldest->newest, map role + media placeholder + clamp each text.
    private static List<WireMessage> ToWireMessages(List<MessageViewModel> src)
    {
        var wire = new List<WireMessage>();
        if (src == null) return wire;

        int start = Mathf.Max(0, src.Count - MaxMessages);
        for (int i = start; i < src.Count; i++)
        {
            var m = src[i];
            if (m == null) continue;
            wire.Add(new WireMessage
            {
                role = m.isIncoming ? "client" : "business",
                text = Clamp(MediaText(m.type, m.text), MaxTextChars),
                ts   = m.timestamp,
            });
        }
        return wire;
    }

    /// <summary>
    /// Maps a message to its wire text: plain chat = the body verbatim; media types collapse to
    /// an RU placeholder, with a non-empty caption appended after it. Pure — exposed for coverage.
    /// </summary>
    public static string MediaText(MessageType type, string caption)
    {
        string placeholder = type switch
        {
            MessageType.Chat     => null,   // plain text — use the body/caption verbatim
            MessageType.Image    => "[фото]",
            MessageType.Video    => "[видео]",
            MessageType.Voice    => "[голосовое сообщение]",
            MessageType.Audio    => "[голосовое сообщение]",
            MessageType.Document => "[документ]",
            MessageType.Sticker  => "[стикер]",
            _                    => "[сообщение]",
        };

        if (placeholder == null) return caption ?? "";
        return string.IsNullOrEmpty(caption) ? placeholder : $"{placeholder} {caption}";
    }

    private static string Clamp(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
        return s.Substring(0, max);
    }

    // --- Pure response mapper (mirrors DashboardResponse.Parse tolerance) -----

    /// <summary>
    /// Maps the server JSON to a <see cref="SuggestionResult"/>: malformed/null/error-field/
    /// null-suggestions/0-valid => <see cref="SuggestionStatus.Error"/>; 1–4 valid {text,label}
    /// => <see cref="SuggestionStatus.Ok"/> ({text,label} -&gt; {text,intentLabel}, order preserved).
    /// <paramref name="requestSeq"/> is stamped from the REQUEST, not the server echo.
    /// </summary>
    public static SuggestionResult MapResponse(string json, long requestSeq)
    {
        SuggestRepliesResponse r = null;
        try { r = JsonConvert.DeserializeObject<SuggestRepliesResponse>(json); } catch { }

        if (r == null || !string.IsNullOrEmpty(r.error) || r.suggestions == null)
            return new SuggestionResult { items = null, requestSeq = requestSeq, status = SuggestionStatus.Error };

        var items = r.suggestions
            .Where(s => s != null && !string.IsNullOrEmpty(s.text) && !string.IsNullOrEmpty(s.label))
            .Select(s => new SuggestionItem { text = s.text, intentLabel = s.label })   // {text,label} -> {text,intentLabel}
            .ToList();

        return items.Count == 0
            ? new SuggestionResult { items = null, requestSeq = requestSeq, status = SuggestionStatus.Error }
            : new SuggestionResult { items = items, requestSeq = requestSeq, status = SuggestionStatus.Ok };   // lenient 1–4
    }

    // --- Local short-circuit results -----------------------------------------

    private static SuggestionResult Empty(SuggestionRequest req)
        => new SuggestionResult { items = null, requestSeq = req != null ? req.requestSeq : 0L, status = SuggestionStatus.Empty };

    private static SuggestionResult Error(SuggestionRequest req)
        => new SuggestionResult { items = null, requestSeq = req != null ? req.requestSeq : 0L, status = SuggestionStatus.Error };
}

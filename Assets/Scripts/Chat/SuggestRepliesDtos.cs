using System.Collections.Generic;

/// <summary>
/// Frozen wire-contract v1 DTOs for the shared Suggest Replies n8n webhook
/// (POST {Manager.n8nBaseUrl}/webhook/SuggestReplies). Plain <c>[System.Serializable]</c>
/// public-field classes so Newtonsoft.Json round-trips them for the Phase-2 provider swap
/// (N8N-01/02) — mirroring the DashboardModels.cs DTO + tolerant-parse pattern.
///
/// Outbound: <see cref="SuggestRepliesRequestDto"/> is assembled by
/// <see cref="N8nSuggestionsProvider.BuildPayloadJson"/>. Inbound: <see cref="SuggestRepliesResponse"/>
/// is remapped by <see cref="N8nSuggestionsProvider.MapResponse"/> into the seam's
/// <see cref="SuggestionItem"/> list. Nothing here references Unity, the messaging API, or
/// web-request types — the field names ARE the wire keys, so do NOT rename them.
/// </summary>

/// <summary>One conversation turn in the outbound payload.</summary>
[System.Serializable]
public class WireMessage
{
    public string role;   // "client" (incoming) | "business" (outgoing)
    public string text;   // media placeholder + optional caption, clamped <=500
    public long   ts;     // unix seconds (MessageViewModel.timestamp)
}

/// <summary>
/// The request body. v1 keys (<c>v</c>…<c>messages</c>) are a FROZEN wire contract — do NOT
/// rename or reorder them. v1.1 appended two ADDITIVE keys (<c>botTgId</c>, <c>channel</c>) for
/// Telegram «Вместе» parity: stripping exactly those two yields the frozen v1 object again —
/// STRUCTURAL identity (same exact key set + values), which is what
/// <c>SuggestRepliesPayloadTests.WhatsAppRequest_AdditivelyIdenticalToV1</c> enforces via
/// JToken.DeepEquals + an exact key-set check. Matching v1's byte order additionally follows
/// from Json.NET's declaration-order emission over these appended-last fields, but only the
/// structural identity is test-enforced. The server Prep defaults an absent <c>channel</c> to
/// whatsapp (Phase 4). Field name IS the wire key here too — do NOT rename.
/// </summary>
[System.Serializable]
public class SuggestRepliesRequestDto
{
    public int    v;                 // == 1
    public long   requestSeq;        // correlation id, echoed verbatim (N8N-01)
    public string profileId;         // active bot's channel-appropriate profile id (WA: whatsappProfileId, TG: telegramProfileId)
    public string chatId;            // open chat id (scoping)
    public string botWaId;           // active bot's whatsappWorkflowId; ""/"-1" => server skips WA RAG (ALWAYS sent — backward compat)
    public string businessTypeId;    // kebab vertical id (e.g. auto_parts) or legacy/empty
    public string businessName;      // bot display name
    public string ownerPrompt;       // owner instructions, clamped <=500
    public string catalog;           // "• name — price" lines, clamped <=1500
    public string steerTowardText;   // picked reply for re-cluster (N8N-03); null = fresh set
    public string lastIncomingText;  // trigger message or null
    public List<WireMessage> messages = new();  // <=24, oldest->newest (12 until the 2026-08 audit F8)
    // --- v1.1 additive keys (ADD-only; field name IS the wire key — do NOT rename) ---
    public string botTgId;           // active bot's telegramWorkflowId; ""/"-1" => server skips TG RAG (mirrors botWaId sentinel)
    public string channel;           // "whatsapp" | "telegram" (lowercase, enum-derived); absent => whatsapp (server Prep default, Phase 4)
    // --- v1.2 additive keys (2026-08 audit F1/F2 grounding; ADD-only, same rules) ---
    public string businessKnowledge; // ComposeBusinessKnowledge output (description + Контакты block), clamped <=1200; "" when unset
    public string now;               // device local time "yyyy-MM-dd HH:mm, <ru day>"; server sanitizes before prompt use
    public string pickStats;         // per-bot pick counters "Ответ:12,К заказу:8" (preference learning v1, 2026-08-11); "" when none
}

/// <summary>One suggestion in the response envelope: server sends {text,label,move}.
/// <c>move</c> is v1.3-additive (drill redesign 2026-08-18) — the internal 6-enum move;
/// a legacy server omits it, so null must be tolerated end-to-end.</summary>
[System.Serializable]
public class SuggestReplyDto
{
    public string text;
    public string label;
    public string move;   // v1.3 additive — internal move taxonomy; null/"" from a legacy server
}

/// <summary>
/// The v1 success/failure envelope. A non-empty <c>error</c> OR a null/empty
/// <c>suggestions</c> list maps to <see cref="SuggestionStatus.Error"/> — EXCEPT the
/// deliberate <c>abstain</c> envelope (owner decision 2026-08-11): an empty list with
/// <c>abstain=true</c> means "this message needs no business reply" and maps to
/// <see cref="SuggestionStatus.Empty"/> (the quiet «Нет предложений» state, no retry nag).
/// </summary>
[System.Serializable]
public class SuggestRepliesResponse
{
    public int    v;
    public long   requestSeq;   // server echo (validated for logging only)
    public string error;        // e.g. "generation_failed"; non-empty => Error
    public bool   abstain;      // true + empty suggestions => Empty (non-business message)
    public List<SuggestReplyDto> suggestions = new();
}

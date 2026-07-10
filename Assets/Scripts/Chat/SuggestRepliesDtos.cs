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

/// <summary>The full v1 request body — every field name is a locked wire key.</summary>
[System.Serializable]
public class SuggestRepliesRequestDto
{
    public int    v;                 // == 1
    public long   requestSeq;        // correlation id, echoed verbatim (N8N-01)
    public string profileId;         // active bot's whatsappProfileId
    public string chatId;            // open chat id (scoping)
    public string botWaId;           // active bot's whatsappWorkflowId; ""/"-1" => server skips RAG
    public string businessTypeId;    // kebab vertical id (e.g. auto_parts) or legacy/empty
    public string businessName;      // bot display name
    public string ownerPrompt;       // owner instructions, clamped <=500
    public string catalog;           // "• name — price" lines, clamped <=1500
    public string steerTowardText;   // picked reply for re-cluster (N8N-03); null = fresh set
    public string lastIncomingText;  // trigger message or null
    public List<WireMessage> messages = new();  // <=12, oldest->newest
}

/// <summary>One suggestion in the response envelope: server sends {text,label}.</summary>
[System.Serializable]
public class SuggestReplyDto
{
    public string text;
    public string label;
}

/// <summary>
/// The v1 success/failure envelope. A non-empty <c>error</c> OR a null/empty
/// <c>suggestions</c> list maps to <see cref="SuggestionStatus.Error"/>.
/// </summary>
[System.Serializable]
public class SuggestRepliesResponse
{
    public int    v;
    public long   requestSeq;   // server echo (validated for logging only)
    public string error;        // e.g. "generation_failed"; non-empty => Error
    public List<SuggestReplyDto> suggestions = new();
}

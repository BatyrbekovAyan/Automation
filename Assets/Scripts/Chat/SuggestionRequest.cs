/// <summary>
/// Input DTO for <see cref="ISuggestionsProvider.Request"/> (DATA-01).
/// Plain serializable public-field DTO (JsonConvert-friendly for the Phase-2 swap).
/// </summary>
[System.Serializable]
public class SuggestionRequest
{
    public string chatId;            // captured active chat (scoping)
    public string lastIncomingText;  // trigger message (INT-02) or null (manual/pick)
    public string steerTowardText;   // picked reply for re-cluster (INT-04/D-01); null = fresh set
    public long   requestSeq;        // monotonic; echoed back for the guard (DATA-03)
}

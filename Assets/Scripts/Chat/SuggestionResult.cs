using System.Collections.Generic;

/// <summary>
/// Output DTO for <see cref="ISuggestionsProvider.Request"/> (DATA-01).
/// <c>items</c> are ranked best-first (PANEL-03); <c>requestSeq</c> echoes the
/// request's correlation id (DATA-03); <c>status</c> drives the panel states (PANEL-04).
/// </summary>
public class SuggestionResult
{
    public List<SuggestionItem> items;  // ranked best-first (PANEL-03)
    public long requestSeq;             // echoed correlation id
    public SuggestionStatus status;     // Ok | Empty | Error (drives PANEL-04 states)
}

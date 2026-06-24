using System;

/// <summary>
/// The reply-suggestions seam (DATA-01). A pure C# callback contract that the
/// suggestions controller depends on, so the data source can be swapped without
/// touching any UI.
///
/// Pure C# seam. Nothing above this seam may reference the live automation backend,
/// the messaging API, or web-request types (ROADMAP §Phase 1 SC-5). Phase 1 ships
/// <see cref="MockSuggestionsProvider"/>; Phase 2 swaps in the live backend provider
/// implementing this same interface with ZERO UI edits.
/// </summary>
public interface ISuggestionsProvider
{
    /// <summary>
    /// Request a ranked set of reply suggestions. <c>request.steerTowardText</c> null =
    /// a fresh refresh; set = re-cluster toward the picked reply (INT-04/D-01).
    /// <c>request.requestSeq</c> rides through to <c>result.requestSeq</c> so the
    /// controller can reject stale/superseded results (DATA-03).
    /// </summary>
    void Request(SuggestionRequest request, Action<SuggestionResult> callback);
}

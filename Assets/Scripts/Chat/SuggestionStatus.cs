/// <summary>
/// Result status driving the panel's PANEL-04 state machine:
/// <c>Ok</c> = ranked cards, <c>Empty</c> = "no suggestions", <c>Error</c> = inline error + retry.
/// </summary>
public enum SuggestionStatus
{
    Ok,
    Empty,
    Error
}

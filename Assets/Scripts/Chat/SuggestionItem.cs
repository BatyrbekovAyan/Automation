/// <summary>
/// One ranked reply suggestion. The <c>{ text, intentLabel }</c> shape mirrors
/// Phase-2's n8n <c>{ text, label }[]</c> contract (N8N-01) so the seam swap is
/// a pure data remap with no UI change.
/// </summary>
public class SuggestionItem
{
    public string text;
    public string intentLabel;
}

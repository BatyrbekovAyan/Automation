using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Renders a neutral reaction pill (emoji[s] + count) onto a message bubble.
/// Hidden when there are no reactions. The emoji string is converted to TMP
/// sprite tags at render time (display layer) and re-rendered when a previously
/// missing emoji's sprite is downloaded.
/// </summary>
public class ReactionPillView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI label;

    private List<MessageReaction> _last;

    private void OnEnable()  { EmojiPatchService.OnEmojiReady += HandleEmojiReady; }
    private void OnDisable() { EmojiPatchService.OnEmojiReady -= HandleEmojiReady; }

    /// <summary>Render the pill for a message's reactions (null/empty hides it).</summary>
    public void Render(List<MessageReaction> reactions)
    {
        _last = reactions;

        var (emojis, count) = ReactionSummary.Build(reactions);
        if (emojis.Count == 0)
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
            return;
        }

        if (!gameObject.activeSelf) gameObject.SetActive(true);

        string raw = string.Concat(emojis);
        string sprites = UnicodeEmojiConverter.ConvertRealEmojisToSprites(raw, MissingEmojiMode.Hide);
        if (label != null)
            label.text = count >= 2 ? $"{sprites} {count}" : sprites;
    }

    public bool HasReactions => _last != null && _last.Count > 0;

    private void HandleEmojiReady(string spriteName)
    {
        if (HasReactions) Render(_last);
    }
}

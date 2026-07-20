using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Controls what <see cref="UnicodeEmojiConverter.ConvertRealEmojisToSprites(string, MissingEmojiMode, out bool)"/>
/// emits for an emoji that has no registered sprite yet (never fetched, fetch in flight, or fetch failed).
/// </summary>
public enum MissingEmojiMode
{
    /// <summary>
    /// Keep the raw Unicode codepoints in the output. Use for data-layer strings
    /// (NormalizedMessage text, ChatHistoryCache, view-models) so the emoji survives
    /// for re-conversion once the background download registers the sprite.
    /// </summary>
    KeepRaw,

    /// <summary>
    /// Omit the emoji entirely. Use for display-layer strings so bubbles and labels
    /// never show tofu boxes or literal sprite-tag text while a sprite is unavailable.
    /// </summary>
    Hide
}

public static class UnicodeEmojiConverter
{
    /// <summary>
    /// Backward-compatible overload — callers that do not need the missing-emoji flag.
    /// </summary>
    public static string ConvertRealEmojisToSprites(string input)
    {
        return ConvertRealEmojisToSprites(input, MissingEmojiMode.KeepRaw, out _);
    }

    /// <summary>
    /// Backward-compatible overload — data-layer behaviour (missing emoji stay as raw Unicode).
    /// </summary>
    public static string ConvertRealEmojisToSprites(string input, out bool hasMissingEmojis)
    {
        return ConvertRealEmojisToSprites(input, MissingEmojiMode.KeepRaw, out hasMissingEmojis);
    }

    /// <summary>
    /// Overload for display call sites that do not need the missing-emoji flag.
    /// </summary>
    public static string ConvertRealEmojisToSprites(string input, MissingEmojiMode mode)
    {
        return ConvertRealEmojisToSprites(input, mode, out _);
    }

    /// <summary>
    /// Converts real Unicode emoji in <paramref name="input"/> to TMP sprite tags for any
    /// emoji that is registered in <see cref="EmojiSpriteRegistry"/>. Emoji without a
    /// registered sprite are kept as raw Unicode or omitted per <paramref name="mode"/> —
    /// never emitted as a sprite tag, because TMP renders a tag it cannot resolve as
    /// literal text. <paramref name="hasMissingEmojis"/> is set to <c>true</c> so the
    /// caller can re-convert when <see cref="EmojiPatchService.OnEmojiReady"/> fires.
    /// </summary>
    public static string ConvertRealEmojisToSprites(string input, MissingEmojiMode mode, out bool hasMissingEmojis)
    {
        hasMissingEmojis = false;
        if (string.IsNullOrEmpty(input)) return input;

        StringBuilder sb = new StringBuilder();
        sb.Append('\u200B');
        
        int i = 0;

        while (i < input.Length)
        {
            // WR-01 (D2-view / 08-REVIEW CR-01 candidate 1): a lone/unpaired surrogate in a malformed
            // reaction-emoji payload makes char.ConvertToUtf32 throw ArgumentException, which aborts the
            // whole OnMessageReactionsChanged multicast AND kills the SyncLatestMessages coroutine mid-loop.
            // Emit the stray surrogate raw and advance one char so the walk is throw-safe. For well-formed
            // text every char here is either a BMP scalar or the HIGH half of a valid pair, so both guards
            // are false and behaviour is byte-identical (a valid pair keeps its normal path — the +2 read).
            if (char.IsHighSurrogate(input[i]) && (i + 1 >= input.Length || !char.IsLowSurrogate(input[i + 1])))
            {
                sb.Append(input[i]);
                i++;
                continue;
            }
            if (char.IsLowSurrogate(input[i]))
            {
                sb.Append(input[i]);
                i++;
                continue;
            }

            int codepoint = char.ConvertToUtf32(input, i);
            int length = char.IsHighSurrogate(input[i]) ? 2 : 1;

            // Keycap emoji (#️⃣ *️⃣ 0️⃣–9️⃣) are an ASCII base (# * or 0-9) followed by an
            // optional FE0F and a combining enclosing keycap (U+20E3). The base is a normal
            // text character, so it only counts as an emoji when U+20E3 actually follows —
            // checked contextually so a bare '#' or digit in text is never converted.
            bool isKeycap = IsKeycapBase(codepoint) && KeycapFollows(input, i + length);

            if (IsEmoji(codepoint) || isKeycap)
            {
                // Start the sequence
                List<int> emojiSequence = new List<int> { codepoint };
                int currentIdx = i + length;

                // LOOKAHEAD LOOP: Check for modifiers, skin tones, or ZWJ
                while (currentIdx < input.Length)
                {
                    int nextCp = char.ConvertToUtf32(input, currentIdx);
                    int nextLen = char.IsHighSurrogate(input[currentIdx]) ? 2 : 1;

                    // 1. Fitzpatrick Skin Tones, Variation Selector (FE0F) & keycap (20E3)
                    if ((nextCp >= 0x1F3FB && nextCp <= 0x1F3FF) || nextCp == 0xFE0F || nextCp == 0x20E3)
                    {
                        emojiSequence.Add(nextCp);
                        currentIdx += nextLen;
                    }
                    // 2. Zero Width Joiner (200D) - glues emojis
                    else if (nextCp == 0x200D)
                    {
                        emojiSequence.Add(nextCp);
                        currentIdx += nextLen;
                        
                        if (currentIdx < input.Length)
                        {
                            int joinedCp = char.ConvertToUtf32(input, currentIdx);
                            int joinedLen = char.IsHighSurrogate(input[currentIdx]) ? 2 : 1;
                            emojiSequence.Add(joinedCp);
                            currentIdx += joinedLen;
                        }
                    }
                    // 3. --- THE FLAG FIX --- 
                    // If the base character AND the next character are BOTH Regional Indicators (1F1E6 - 1F1FF)
                    else if (codepoint >= 0x1F1E6 && codepoint <= 0x1F1FF && nextCp >= 0x1F1E6 && nextCp <= 0x1F1FF)
                    {
                        emojiSequence.Add(nextCp);
                        currentIdx += nextLen;
                        
                        // Standard country flags are strictly pairs. Once we find the second half, 
                        // we stop looking ahead so we don't accidentally fuse 3 flags together!
                        break;
                    }
                    else
                    {
                        // Stop if we hit a regular character
                        break;
                    }
                }

                // Keycaps resolve by their fully-qualified Twemoji name ("0023-fe0f-20e3").
                // If the sender omitted the FE0F variation selector, insert it so the name
                // matches the registered sprite.
                if (emojiSequence[emojiSequence.Count - 1] == 0x20E3 && !emojiSequence.Contains(0xFE0F))
                    emojiSequence.Insert(emojiSequence.Count - 1, 0xFE0F);

                // Convert list of hex codes to string: "1f44b-1f3fb" or "1f1f0-1f1ff"
                string hexName = GetHexName(emojiSequence);

                if (EmojiSpriteRegistry.IsKnown(hexName))
                {
                    // Sprite is registered \u2014 emit TMP rich-text tag with spacing.
                    bool needsGap = sb.Length > 0
                        && !char.IsWhiteSpace(sb[sb.Length - 1])
                        && sb[sb.Length - 1] != '>'
                        && sb[sb.Length - 1] != '\u200B';

                    sb.Append('\u200B');
                    if (needsGap) sb.Append("<space=0.12em>");
                    sb.Append($"<sprite name=\"{hexName}\">");
                    sb.Append('\u200B');
                }
                else
                {
                    // No sprite yet (never fetched, fetch pending, or last fetch failed).
                    // Never emit the tag here: TMP renders an unresolvable <sprite> tag
                    // as literal text. KeepRaw preserves the emoji for re-conversion
                    // after download; Hide drops it so the UI shows nothing instead.
                    hasMissingEmojis = true;
                    if (mode == MissingEmojiMode.KeepRaw)
                        sb.Append(input, i, currentIdx - i);
                    EmojiSpriteRegistry.ClearFailed(hexName);
                    if (EmojiPatchService.Instance != null)
                        EmojiPatchService.Instance.RequestEmoji(hexName);
                }

                i = currentIdx;
            }
            else
            {
                sb.Append(input[i]);
                if (length == 2) sb.Append(input[i + 1]);
                i += length;
            }
        }

        return sb.ToString();
    }

    private static bool IsEmoji(int codepoint)
    {
        return (codepoint >= 0x1F000 && codepoint <= 0x1FAFF) || 
               (codepoint >= 0x2600 && codepoint <= 0x27BF) ||   
               (codepoint >= 0x2300 && codepoint <= 0x23FF) ||   
               (codepoint >= 0x2B00 && codepoint <= 0x2BFF) ||   
               (codepoint >= 0x25A0 && codepoint <= 0x25FF) ||   
               (codepoint == 0x203C || codepoint == 0x2049) ||   
               (codepoint == 0x00A9 || codepoint == 0x00AE || codepoint == 0x2122);
    }

    /// <summary>The ASCII base of a keycap emoji: '#', '*' or a digit 0-9.</summary>
    private static bool IsKeycapBase(int codepoint) =>
        codepoint == 0x23 || codepoint == 0x2A || (codepoint >= 0x30 && codepoint <= 0x39);

    /// <summary>
    /// True when the characters at <paramref name="idx"/> complete a keycap emoji:
    /// an optional FE0F variation selector followed by U+20E3 (combining enclosing keycap).
    /// </summary>
    private static bool KeycapFollows(string s, int idx)
    {
        if (idx < s.Length && s[idx] == 0xFE0F) idx++;   // variation selector-16
        return idx < s.Length && s[idx] == 0x20E3;        // combining enclosing keycap
    }

    private static string GetHexName(List<int> sequence)
    {
        StringBuilder hexBuilder = new StringBuilder();
        for (int j = 0; j < sequence.Count; j++)
        {
            if (j > 0) hexBuilder.Append("-");
            hexBuilder.Append(sequence[j].ToString("x4")); // min 4 digits — matches Twemoji CDN convention (e.g. "00a9" not "a9")
        }
        return hexBuilder.ToString();
    }
}
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public static class UnicodeEmojiConverter
{
    /// <summary>
    /// Backward-compatible overload — callers that do not need the missing-emoji flag.
    /// </summary>
    public static string ConvertRealEmojisToSprites(string input)
    {
        return ConvertRealEmojisToSprites(input, out _);
    }

    /// <summary>
    /// Converts real Unicode emoji in <paramref name="input"/> to TMP sprite tags for any
    /// emoji that is registered in <see cref="EmojiSpriteRegistry"/>. Unknown emoji are left
    /// as raw Unicode so font-fallback can render them, and <paramref name="hasMissingEmojis"/>
    /// is set to <c>true</c> so the caller can schedule a background CDN fetch.
    /// </summary>
    public static string ConvertRealEmojisToSprites(string input, out bool hasMissingEmojis)
    {
        hasMissingEmojis = false;
        if (string.IsNullOrEmpty(input)) return input;

        StringBuilder sb = new StringBuilder();
        sb.Append('\u200B');
        
        int i = 0;

        while (i < input.Length)
        {
            int codepoint = char.ConvertToUtf32(input, i);
            int length = char.IsHighSurrogate(input[i]) ? 2 : 1;
            
            if (IsEmoji(codepoint))
            {
                // Start the sequence
                List<int> emojiSequence = new List<int> { codepoint };
                int currentIdx = i + length;

                // LOOKAHEAD LOOP: Check for modifiers, skin tones, or ZWJ
                while (currentIdx < input.Length)
                {
                    int nextCp = char.ConvertToUtf32(input, currentIdx);
                    int nextLen = char.IsHighSurrogate(input[currentIdx]) ? 2 : 1;

                    // 1. Fitzpatrick Skin Tones & Variation Selectors
                    if ((nextCp >= 0x1F3FB && nextCp <= 0x1F3FF) || nextCp == 0xFE0F)
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

                // Convert list of hex codes to string: "1f44b-1f3fb" or "1f1f0-1f1ff"
                string hexName = GetHexName(emojiSequence);

                if (EmojiSpriteRegistry.IsKnown(hexName))
                {
                    // Sprite exists \u2014 emit TMP rich-text tag with spacing
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
                    // Sprite missing \u2014 leave raw Unicode so font fallback renders it,
                    // and queue a background CDN fetch.
                    hasMissingEmojis = true;
                    sb.Append(input, i, currentIdx - i);
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

    private static string GetHexName(List<int> sequence)
    {
        StringBuilder hexBuilder = new StringBuilder();
        for (int j = 0; j < sequence.Count; j++)
        {
            if (j > 0) hexBuilder.Append("-");
            hexBuilder.Append(sequence[j].ToString("x")); 
        }
        return hexBuilder.ToString();
    }
}
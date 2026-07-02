using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Converts RTF documents to plain text on-device, mirroring the other
/// client-side converters (XML/tables/docx): the Upload File workflow only
/// ever receives text/plain or PDF. Covers the common Word/TextEdit output:
/// ANSI codepage escapes (\'hh — Cyrillic price lists are cp1251), \uN
/// unicode with \ucN fallback skipping, table cells/rows, and skipped
/// non-content destinations (font/color tables, pictures, metadata).
/// </summary>
public static class RtfToTextConverter
{
    static RtfToTextConverter()
    {
        // Required for GetEncoding(1251/1252) on Unity's .NET runtime.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private struct GroupState
    {
        public bool Skipping;
        public int UnicodeFallbackLength;
    }

    private static readonly HashSet<string> SkippedDestinations = new()
    {
        "fonttbl", "colortbl", "stylesheet", "info", "pict", "themedata",
        "colorschememapping", "header", "footer", "headerl", "headerr", "headerf",
        "footerl", "footerr", "footerf", "filetbl", "revtbl", "generator",
        "fldinst", "listtable", "listoverridetable", "latentstyles", "datastore",
    };

    private static readonly Dictionary<string, string> SymbolWords = new()
    {
        { "par", "\n" }, { "line", "\n" }, { "row", "\n" }, { "sect", "\n" }, { "page", "\n" },
        { "tab", "\t" }, { "cell", "\t" },
        { "emdash", "—" }, { "endash", "–" }, { "bullet", "•" },
        { "lquote", "‘" }, { "rquote", "’" },
        { "ldblquote", "“" }, { "rdblquote", "”" },
    };

    public static string Convert(byte[] rtfBytes)
    {
        if (rtfBytes == null || rtfBytes.Length == 0) return string.Empty;
        // Latin1 preserves every raw byte 1:1; text bytes arrive as \'hh
        // escapes OR raw 8-bit ANSI bytes (some writers skip escaping) and
        // both are decoded against the document codepage during parsing.
        string raw = Encoding.GetEncoding(28591).GetString(rtfBytes);
        if (!raw.TrimStart().StartsWith("{\\rtf", StringComparison.Ordinal))
            return TextEncodingSniffer.Decode(rtfBytes).Trim(); // misnamed plain text — sniff, don't garble
        return Convert(raw);
    }

    public static string Convert(string rtf)
    {
        if (string.IsNullOrWhiteSpace(rtf)) return string.Empty;
        if (!rtf.TrimStart().StartsWith("{\\rtf", StringComparison.Ordinal))
            return rtf.Trim(); // not RTF — already plain text, pass through

        var output = new StringBuilder(rtf.Length / 2);
        var pendingBytes = new List<byte>();
        var groups = new Stack<GroupState>();
        var state = new GroupState { Skipping = false, UnicodeFallbackLength = 1 };
        Encoding ansi = Encoding.GetEncoding(1252);
        int fallbackToSkip = 0;
        int pos = 0;

        while (pos < rtf.Length)
        {
            char c = rtf[pos];
            switch (c)
            {
                case '{':
                    FlushBytes(output, pendingBytes, ansi, state.Skipping);
                    groups.Push(state);
                    pos++;
                    break;
                case '}':
                    FlushBytes(output, pendingBytes, ansi, state.Skipping);
                    if (groups.Count > 0) state = groups.Pop();
                    pos++;
                    break;
                case '\\':
                    pos = HandleControl(rtf, pos, output, pendingBytes, ref state, ref ansi, ref fallbackToSkip);
                    break;
                case '\r':
                case '\n':
                    pos++; // raw newlines in the RTF source are file formatting, not content
                    break;
                default:
                    if (fallbackToSkip > 0) fallbackToSkip--;
                    else if (c >= '\u0080' && c <= '\u00FF')
                    {
                        // Raw 8-bit ANSI byte (writer skipped \'hh escaping) —
                        // Latin1 round-trips it; decode with the codepage.
                        if (!state.Skipping) pendingBytes.Add((byte)c);
                    }
                    else
                    {
                        FlushBytes(output, pendingBytes, ansi, state.Skipping);
                        if (!state.Skipping) output.Append(c);
                    }
                    pos++;
                    break;
            }
        }

        FlushBytes(output, pendingBytes, ansi, state.Skipping);
        return Normalize(output.ToString());
    }

    private static int HandleControl(string rtf, int pos, StringBuilder output, List<byte> pendingBytes,
        ref GroupState state, ref Encoding ansi, ref int fallbackToSkip)
    {
        pos++; // past '\'
        if (pos >= rtf.Length) return pos;
        char c = rtf[pos];

        if (c == '\'') // \'hh codepage byte — accumulate so multi-byte runs decode together
        {
            int value = ParseHexPair(rtf, pos + 1);
            if (value < 0) return pos + 1;
            if (fallbackToSkip > 0) fallbackToSkip--;
            else if (!state.Skipping) pendingBytes.Add((byte)value);
            return pos + 3;
        }

        if (!char.IsLetter(c)) // control symbols and escaped literals
        {
            FlushBytes(output, pendingBytes, ansi, state.Skipping);
            switch (c)
            {
                case '\\': case '{': case '}':
                    if (!state.Skipping) output.Append(c);
                    break;
                case '~':
                    if (!state.Skipping) output.Append(' ');
                    break;
                case '_':
                    if (!state.Skipping) output.Append('-');
                    break;
                case '*': // unknown destination marker — drop the whole group
                    state.Skipping = true;
                    break;
            }
            return pos + 1;
        }

        FlushBytes(output, pendingBytes, ansi, state.Skipping);
        int wordStart = pos;
        while (pos < rtf.Length && char.IsLetter(rtf[pos])) pos++;
        string word = rtf.Substring(wordStart, pos - wordStart);

        bool hasParam = false;
        int param = 0, sign = 1;
        if (pos < rtf.Length && (rtf[pos] == '-' || char.IsDigit(rtf[pos])))
        {
            hasParam = true;
            if (rtf[pos] == '-') { sign = -1; pos++; }
            while (pos < rtf.Length && char.IsDigit(rtf[pos]))
                param = param * 10 + (rtf[pos++] - '0');
            param *= sign;
        }
        if (pos < rtf.Length && rtf[pos] == ' ') pos++; // delimiter space belongs to the control word

        switch (word)
        {
            case "u": // \uN unicode char; the following UnicodeFallbackLength chars are a legacy fallback
                if (!state.Skipping) output.Append((char)(param < 0 ? param + 65536 : param));
                fallbackToSkip = state.UnicodeFallbackLength;
                break;
            case "uc":
                state.UnicodeFallbackLength = hasParam ? param : 1;
                break;
            case "ansicpg":
                if (hasParam) ansi = SafeGetEncoding(param, ansi);
                break;
            case "bin": // raw binary payload follows — skip it wholesale
                pos += Math.Max(0, param);
                break;
            default:
                if (SkippedDestinations.Contains(word)) state.Skipping = true;
                else if (!state.Skipping && SymbolWords.TryGetValue(word, out string mapped)) output.Append(mapped);
                break;
        }
        return pos;
    }

    private static void FlushBytes(StringBuilder output, List<byte> pendingBytes, Encoding ansi, bool skipping)
    {
        if (pendingBytes.Count == 0) return;
        if (!skipping) output.Append(ansi.GetString(pendingBytes.ToArray()));
        pendingBytes.Clear();
    }

    private static int ParseHexPair(string rtf, int start)
    {
        if (start + 1 >= rtf.Length) return -1;
        int hi = HexValue(rtf[start]);
        int lo = HexValue(rtf[start + 1]);
        return hi < 0 || lo < 0 ? -1 : hi * 16 + lo;
    }

    private static int HexValue(char c) =>
        c >= '0' && c <= '9' ? c - '0' :
        c >= 'a' && c <= 'f' ? c - 'a' + 10 :
        c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;

    private static Encoding SafeGetEncoding(int codepage, Encoding fallback)
    {
        try { return Encoding.GetEncoding(codepage); }
        catch (Exception) { return fallback; }
    }

    private static string Normalize(string text)
    {
        text = Regex.Replace(text, "[ \t]+\n", "\n");
        text = Regex.Replace(text, "\n{3,}", "\n\n");
        return text.Trim();
    }
}

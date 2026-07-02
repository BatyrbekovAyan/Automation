using System.Text;

/// <summary>
/// Decodes text bytes of unknown encoding, tuned for CIS price lists:
/// a BOM wins (UTF-8 / UTF-16 LE / UTF-16 BE — old Notepad "Unicode" saves
/// UTF-16 LE with BOM); otherwise strictly-valid UTF-8 is decoded as UTF-8;
/// anything else is treated as windows-1251, the overwhelmingly common
/// legacy encoding for ru/kk Excel CSV exports and old-Windows TXT files.
/// </summary>
public static class TextEncodingSniffer
{
    static TextEncodingSniffer()
    {
        // Required for GetEncoding(1251) on Unity's .NET runtime.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static string Decode(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return string.Empty;

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        // BOM-less UTF-16 (some DB/1C exports): valid UTF-8 or cp1251 TEXT
        // never contains NUL bytes, while UTF-16 puts one on every ASCII char
        // (digits, ';', spaces, newlines — any real price list has plenty).
        // Cyrillic-in-UTF-16 is otherwise all low bytes, so the UTF-8 check
        // below would "pass" it and ingest control-char garbage.
        int zeros = 0, zerosAtEven = 0;
        for (int i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != 0) continue;
            zeros++;
            if ((i & 1) == 0) zerosAtEven++;
        }
        if (zeros > 0)
        {
            bool bigEndian = zerosAtEven * 2 >= zeros; // ASCII high half holds the zero
            return (bigEndian ? Encoding.BigEndianUnicode : Encoding.Unicode).GetString(bytes);
        }

        return IsValidUtf8(bytes)
            ? Encoding.UTF8.GetString(bytes)
            : Encoding.GetEncoding(1251).GetString(bytes);
    }

    /// <summary>
    /// Strict UTF-8 validation (correct continuation bytes, no overlongs, no
    /// surrogates, ≤ U+10FFFF). Real cp1251 Cyrillic text essentially never
    /// passes, which is what makes the UTF-8-else-1251 heuristic safe.
    /// </summary>
    public static bool IsValidUtf8(byte[] bytes)
    {
        int i = 0;
        while (i < bytes.Length)
        {
            byte b = bytes[i];
            int continuation;
            if (b <= 0x7F) { i++; continue; }
            else if (b >= 0xC2 && b <= 0xDF) continuation = 1;
            else if (b >= 0xE0 && b <= 0xEF) continuation = 2;
            else if (b >= 0xF0 && b <= 0xF4) continuation = 3;
            else return false; // 0x80-0xC1 lead or 0xF5+ are never valid

            if (i + continuation >= bytes.Length) return false;
            if (b == 0xE0 && (bytes[i + 1] & 0xE0) != 0xA0) return false; // overlong 3-byte
            if (b == 0xED && (bytes[i + 1] & 0xE0) != 0x80) return false; // UTF-16 surrogate range
            if (b == 0xF0 && (bytes[i + 1] & 0xF0) == 0x80) return false; // overlong 4-byte
            if (b == 0xF4 && bytes[i + 1] > 0x8F) return false;           // above U+10FFFF

            for (int k = 1; k <= continuation; k++)
                if ((bytes[i + k] & 0xC0) != 0x80) return false;
            i += continuation + 1;
        }
        return true;
    }
}

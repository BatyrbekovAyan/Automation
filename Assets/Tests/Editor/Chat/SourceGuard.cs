using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The «Личные и рабочие чаты» wiring guards' source reader: a script's text, a method's body by the start of its signature
/// (braces matched, or an expression body up to its ';'), and every call that starts with a marker (parentheses matched).
/// For wiring EditMode cannot run — a coroutine, a download, a notification cue.
/// </summary>
internal static class SourceGuard
{
    /// <summary>A script under Assets/, e.g. "Scripts/Main/ChatManager.cs".</summary>
    public static string Read(string pathUnderAssets) => File.ReadAllText(Path.Combine(Application.dataPath, pathUnderAssets));

    /// <summary>The body of the method whose signature starts with <paramref name="marker"/>.</summary>
    public static string MethodBody(string source, string marker)
    {
        int at = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.GreaterOrEqual(at, 0, $"«{marker}» not found in the source");
        int open = source.IndexOf('{', at);
        int arrow = source.IndexOf("=>", at, StringComparison.Ordinal);
        if (arrow >= 0 && (open < 0 || arrow < open))
        {
            int end = source.IndexOf(';', arrow);
            return source.Substring(arrow, end - arrow + 1);
        }
        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source.Substring(open, i - open + 1);
        }
        Assert.Fail($"«{marker}»: unbalanced braces");
        return null;
    }

    /// <summary>Every call in <paramref name="body"/> that starts with <paramref name="marker"/> (which ends with its '(').</summary>
    public static List<string> Calls(string body, string marker)
    {
        var calls = new List<string>();
        int from = 0;
        while ((from = body.IndexOf(marker, from, StringComparison.Ordinal)) >= 0)
        {
            int depth = 0;
            int end = from + marker.Length - 1;
            for (; end < body.Length; end++)
            {
                if (body[end] == '(') depth++;
                else if (body[end] == ')' && --depth == 0) break;
            }
            calls.Add(body.Substring(from, end - from + 1));
            from = end;
        }
        return calls;
    }
}

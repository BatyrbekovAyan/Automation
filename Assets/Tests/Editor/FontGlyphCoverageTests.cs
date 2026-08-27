using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using Automation.BotSettingsUI;

/// <summary>
/// Closes the "authored character silently becomes a space" class. TMP replaces a
/// char absent from a label's font (and every fallback) with nothing visible and
/// only logs at layout time — that is how «до −17%» shipped as «до 17%» (U+2212,
/// fixed in f4fe603) and how the Support FAQ's «→» never rendered at all. The four
/// SF Pro Text SDFs are STATIC atlases (711 chars, no fallback tables, TMP Settings
/// global fallbacks empty), so membership in the baked character table IS device
/// truth; the LiberationSans chain ends in a DYNAMIC fallback whose bundled
/// LiberationSans.ttf is queried directly. Chars found in the Twemoji sprite assets
/// render as sprites; Unicode Format chars (ZWJ, VS16, BOM…) and whitespace are
/// ignorable — TMP either consumes them in emoji sequences or draws nothing anyway.
///
/// Two authored surfaces are checked: every TMP label serialized in Main.unity and
/// Assets/Prefabs (each against ITS OWN m_fontAsset; {fileID: 0} = TMP default
/// font, mirroring TMP's runtime fallback), and the pure RU copy seams whose
/// strings are bound into SF Pro labels at runtime. Known-absent from SF Pro, for
/// the next author: − (U+2212), № (U+2116), ✕ (U+2715), → (U+2192), ⇒ (U+21D2) —
/// use "-", «Вместе»-style ‹ › (U+2039/203A), or × (U+00D7) instead.
/// </summary>
public class FontGlyphCoverageTests
{
    private static readonly string[] SfProPaths =
    {
        "Assets/TextMesh Pro/Fonts/SFProText-Regular SDF.asset",
        "Assets/TextMesh Pro/Fonts/SFProText-Medium SDF.asset",
        "Assets/TextMesh Pro/Fonts/SFProText-Semibold SDF.asset",
        "Assets/TextMesh Pro/Fonts/SFProText-Bold SDF.asset",
    };

    private static readonly string[] SpriteAssetFolders =
    {
        "Assets/Resources/Sprite Assets",
        "Assets/TextMesh Pro/Resources/Sprite Assets",
    };

    // ---------- renderability model ----------

    private static HashSet<uint> CollectSpriteUnicodes()
    {
        var set = new HashSet<uint>();
        foreach (var guid in AssetDatabase.FindAssets("t:TMP_SpriteAsset", SpriteAssetFolders))
        {
            var sa = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(AssetDatabase.GUIDToAssetPath(guid));
            if (sa == null || sa.spriteCharacterTable == null) continue;
            foreach (var sc in sa.spriteCharacterTable)
                if (sc != null && sc.unicode != 0xFFFE)
                    set.Add(sc.unicode);
        }
        return set;
    }

    private static bool FontChainHas(TMP_FontAsset f, int cp, HashSet<int> seen)
    {
        if (f == null || !seen.Add(f.GetInstanceID())) return false;
        if (f.characterLookupTable != null && f.characterLookupTable.ContainsKey((uint)cp)) return true;
        // A dynamic atlas repopulates on device from its bundled source font, so the
        // source file's cmap is the honest coverage there (LiberationSans - Fallback).
        if (f.atlasPopulationMode == AtlasPopulationMode.Dynamic && f.sourceFontFile != null
            && cp <= 0xFFFF && f.sourceFontFile.HasCharacter((char)cp)) return true;
        if (f.fallbackFontAssetTable != null)
            foreach (var fb in f.fallbackFontAssetTable)
                if (FontChainHas(fb, cp, seen)) return true;
        return false;
    }

    private static bool Ignorable(int cp)
    {
        if (cp <= 0x20) return true;
        if (cp <= 0xFFFF)
        {
            var ch = (char)cp;
            if (char.IsWhiteSpace(ch)) return true;
            var cat = char.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.Format || cat == UnicodeCategory.Control) return true;
            if (char.IsSurrogate(ch)) return true; // halves; full pairs arrive as codepoints
        }
        return false;
    }

    private static void CheckText(string src, string text, TMP_FontAsset font,
        HashSet<uint> sprites, List<string> failures)
    {
        if (string.IsNullOrEmpty(text)) return;
        for (int i = 0; i < text.Length; i++)
        {
            int cp = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                cp = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }
            if (Ignorable(cp)) continue;
            if (sprites.Contains((uint)cp)) continue;
            if (FontChainHas(font, cp, new HashSet<int>())) continue;
            failures.Add($"{src}: U+{cp:X4} '{char.ConvertFromUtf32(cp)}' is not in " +
                         $"'{font.name}' (or its fallbacks/sprites) — text: {Trim(text)}");
        }
    }

    private static string Trim(string s) => s.Length <= 60 ? s : s.Substring(0, 60) + "…";

    // ---------- serialized-label surface ----------

    private struct LabelHit
    {
        public string File;
        public int Line;
        public string Go;
        public string Text;
        public string FontGuid;
    }

    private static readonly Regex DocHeader = new Regex(@"^--- !u!(\d+) &(\d+)");
    private static readonly Regex GuidRx = new Regex(@"guid: ([0-9a-f]{32})");
    private static readonly Regex ClosedQuote = new Regex(@"(?<!\\)(\\\\)*""$");

    private static string DecodeYamlScalar(string raw)
    {
        raw = raw.Trim();
        if (raw.StartsWith("\""))
        {
            var body = raw.EndsWith("\"") && raw.Length > 1 ? raw.Substring(1, raw.Length - 2) : raw.Substring(1);
            var outSb = new StringBuilder();
            for (int i = 0; i < body.Length; i++)
            {
                char c = body[i];
                if (c == '\\' && i + 1 < body.Length)
                {
                    char n = body[i + 1];
                    if (n == 'u' && i + 5 < body.Length)
                    { outSb.Append((char)Convert.ToInt32(body.Substring(i + 2, 4), 16)); i += 5; continue; }
                    if (n == 'U' && i + 9 < body.Length)
                    { outSb.Append(char.ConvertFromUtf32(Convert.ToInt32(body.Substring(i + 2, 8), 16))); i += 9; continue; }
                    if (n == 'x' && i + 3 < body.Length)
                    { outSb.Append((char)Convert.ToInt32(body.Substring(i + 2, 2), 16)); i += 3; continue; }
                    switch (n)
                    {
                        case 'n': outSb.Append('\n'); break;
                        case 't': outSb.Append('\t'); break;
                        case 'r': outSb.Append('\r'); break;
                        default: outSb.Append(n); break;
                    }
                    i++;
                    continue;
                }
                outSb.Append(c);
            }
            return outSb.ToString();
        }
        if (raw.StartsWith("'"))
        {
            var body = raw.EndsWith("'") && raw.Length > 1 ? raw.Substring(1, raw.Length - 2) : raw.Substring(1);
            return body.Replace("''", "'");
        }
        return raw;
    }

    private static List<LabelHit> ParseLabels(string path)
    {
        var hits = new List<LabelHit>();
        var goNames = new Dictionary<string, string>();
        var lines = File.ReadAllLines(path);
        bool inTextDoc = false;
        string pendingGoNameFor = null;
        string text = null, fontGuid = null, goId = null;
        int textLine = 0;

        void Flush()
        {
            if (inTextDoc && text != null)
                hits.Add(new LabelHit { File = path, Line = textLine, Go = goId, Text = text, FontGuid = fontGuid });
            text = null; fontGuid = null; goId = null;
        }

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var m = DocHeader.Match(line);
            if (m.Success)
            {
                Flush();
                inTextDoc = m.Groups[1].Value == "114";
                pendingGoNameFor = m.Groups[1].Value == "1" ? m.Groups[2].Value : null;
                continue;
            }
            if (pendingGoNameFor != null && line.StartsWith("  m_Name:"))
            {
                goNames[pendingGoNameFor] = line.Substring("  m_Name:".Length).Trim();
                pendingGoNameFor = null;
                continue;
            }
            if (!inTextDoc) continue;
            if (line.StartsWith("  m_text:"))
            {
                var s = line.Substring("  m_text:".Length).Trim();
                while (s.StartsWith("\"") && (s.Length == 1 || !ClosedQuote.IsMatch(s)) && i + 1 < lines.Length)
                    s += " " + lines[++i].Trim();
                text = DecodeYamlScalar(s);
                textLine = i + 1;
            }
            else if (line.StartsWith("  m_fontAsset:"))
            {
                var g = GuidRx.Match(line);
                fontGuid = g.Success ? g.Groups[1].Value : null;
            }
            else if (line.StartsWith("  m_GameObject:"))
            {
                var g = Regex.Match(line, @"fileID: (\d+)");
                goId = g.Success ? g.Groups[1].Value : null;
            }
        }
        Flush();

        for (int i = 0; i < hits.Count; i++)
        {
            var h = hits[i];
            h.Go = h.Go != null && goNames.TryGetValue(h.Go, out var n) ? n : "?";
            hits[i] = h;
        }
        return hits;
    }

    [Test]
    public void Serialized_labels_use_only_chars_their_font_can_draw()
    {
        var sprites = CollectSpriteUnicodes();
        Assert.GreaterOrEqual(sprites.Count, 500,
            "Twemoji sprite unicode table collapsed — plain-text emoji would stop rendering.");

        var files = new List<string> { "Assets/Scenes/Main.unity" };
        files.AddRange(Directory.GetFiles("Assets/Prefabs", "*.prefab").OrderBy(p => p));

        var fontCache = new Dictionary<string, TMP_FontAsset>();
        TMP_FontAsset ResolveFont(string guid)
        {
            // Mirrors TMP runtime: no font (or a dangling guid) falls back to the default.
            if (guid == null) return TMP_Settings.defaultFontAsset;
            if (!fontCache.TryGetValue(guid, out var f))
            {
                f = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
                fontCache[guid] = f;
            }
            return f != null ? f : TMP_Settings.defaultFontAsset;
        }

        var failures = new List<string>();
        int total = 0, sceneCount = 0;
        bool sawCyrillic = false, sawTypographic = false;
        foreach (var file in files)
        {
            foreach (var h in ParseLabels(file))
            {
                if (string.IsNullOrWhiteSpace(h.Text)) continue;
                total++;
                if (file.EndsWith(".unity")) sceneCount++;
                sawCyrillic |= h.Text.Any(c => c >= 0x0400 && c <= 0x04FF);
                sawTypographic |= h.Text.IndexOfAny(new[] { '«', '»', '₸', '…' }) >= 0;
                var font = ResolveFont(h.FontGuid);
                Assert.IsNotNull(font, $"No font resolvable for {file}:{h.Line} (TMP Settings default missing?)");
                CheckText($"{file}:{h.Line} [{h.Go}] ({font.name})", h.Text, font, sprites, failures);
            }
        }

        // The parser proving it actually decoded real content — a regex that silently
        // matches nothing would otherwise turn this guard into a false green.
        Assert.GreaterOrEqual(sceneCount, 200, "Main.unity label parse collapsed");
        Assert.GreaterOrEqual(total, 300, "label parse collapsed");
        Assert.IsTrue(sawCyrillic, "no Cyrillic decoded — \\u escape decoding is broken");
        Assert.IsTrue(sawTypographic, "no «»/₸/… decoded — escape decoding is broken");
        Assert.IsTrue(failures.Count == 0,
            $"{failures.Count} serialized label char(s) will silently not render:\n" + string.Join("\n", failures));
    }

    // ---------- authored copy-seam surface ----------

    private static IEnumerable<(string src, string text)> SeamStrings()
    {
        // Mechanical layer: every public static string constant on the copy seams.
        var constTypes = new[]
        {
            typeof(PaywallCopy), typeof(PaywallRows), typeof(SubscriptionPageRows),
            typeof(BillingGateRows), typeof(BotsPageRows), typeof(LegalLinks),
        };
        foreach (var t in constTypes)
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static))
                if (f.FieldType == typeof(string))
                    yield return ($"{t.Name}.{f.Name}", (string)f.GetValue(null));

        // Composed samples: representative outputs of every formatter that emits
        // authored (non-server) text.
        yield return ("PaywallCopy.Number(-5)", PaywallCopy.Number(-5));
        yield return ("PaywallCopy.Kzt", PaywallCopy.Kzt(19900));
        yield return ("PaywallCopy.PerMonth", PaywallCopy.PerMonth(9900));
        yield return ("PaywallCopy.PerYear", PaywallCopy.PerYear(199000));
        yield return ("PaywallCopy.YearLine", PaywallCopy.YearLine(PlanCatalog.Get(PlanTier.Start)));
        yield return ("PaywallCopy.Dialogs", PaywallCopy.Dialogs(1000));
        yield return ("PaywallCopy.DialogsPerMonth", PaywallCopy.DialogsPerMonth(300));
        yield return ("PaywallCopy.Bots", PaywallCopy.Bots(2));
        yield return ("PaywallCopy.Channels", PaywallCopy.Channels(3));
        yield return ("PaywallCopy.TrialCta", PaywallCopy.TrialCta());
        yield return ("PaywallCopy.TrialPill", PaywallCopy.TrialPill(3));
        yield return ("PaywallCopy.ReceiptTitle", PaywallCopy.ReceiptTitle());
        foreach (PlanTier tier in Enum.GetValues(typeof(PlanTier)))
        {
            yield return ($"PaywallCopy.TierName({tier})", PaywallCopy.TierName(tier));
            yield return ($"PaywallCopy.SubscribeCta({tier})",
                PaywallCopy.SubscribeCta(tier, PaywallCopy.PerMonth(19900)));
        }
        foreach (PaywallTrigger trig in Enum.GetValues(typeof(PaywallTrigger)))
        {
            yield return ($"BillingGateRows.Title({trig})", BillingGateRows.Title(trig));
            yield return ($"BillingGateRows.Body({trig})", BillingGateRows.Body(trig, PlanTier.Start));
        }
        for (int month = 1; month <= 12; month++)
            yield return ($"BotsPageRows.MeterTitle(m{month})",
                BotsPageRows.MeterTitle(new DateTime(2026, month, 15)));
        yield return ("BotsPageRows.MeterHint", BotsPageRows.MeterHint(3, 10, 2));
        yield return ("BotsPageRows.MeterHint(over)", BotsPageRows.MeterHint(10, 10, 0));
        yield return ("BotsPageRows.ReserveHint", BotsPageRows.ReserveHint(2));
        yield return ("BotsPageRows.AddBotSubtext", BotsPageRows.AddBotSubtext(PlanTier.Start, 1));
        yield return ("BotsPageRows.AddBotSubtext(none)", BotsPageRows.AddBotSubtext(PlanTier.None, 0));
        yield return ("SubscriptionPageRows.ActiveSubline(month)",
            SubscriptionPageRows.ActiveSubline(PlanTier.Business, "2026-09-26T12:00:00Z", "month"));
        yield return ("SubscriptionPageRows.ActiveSubline(year)",
            SubscriptionPageRows.ActiveSubline(PlanTier.Business, "2027-08-26T12:00:00Z", "year"));
        yield return ("SubscriptionPageRows.TrialSubline", SubscriptionPageRows.TrialSubline(3));
        yield return ("SubscriptionPageRows.CountLine", SubscriptionPageRows.CountLine(412, 1000));
        yield return ("SubscriptionPageRows.TopUpRowText", SubscriptionPageRows.TopUpRowText());

        foreach (OutcomeStatus s in Enum.GetValues(typeof(OutcomeStatus)))
            yield return ($"DashboardStatusInfo.Label({s})", DashboardStatusInfo.Label(s));

        foreach (var p in PromptSuggestionCatalog.All)
        {
            yield return ($"PromptSuggestionCatalog[{p.Id}].Text", p.Text);
            yield return ($"PromptSuggestionCatalog[{p.Id}].ShortLabel", p.ShortLabel);
        }

        // The Support FAQ binds into an SF Pro Regular label at runtime — the field is
        // private, so pin it by reflection and fail loudly if it is renamed.
        var faqField = typeof(ProfileSubPages).GetField("Faq", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(faqField, "ProfileSubPages.Faq renamed — update FontGlyphCoverageTests.");
        var faq = ((string question, string answer)[])faqField.GetValue(null);
        for (int i = 0; i < faq.Length; i++)
        {
            yield return ($"ProfileSubPages.Faq[{i}].question", faq[i].question);
            yield return ($"ProfileSubPages.Faq[{i}].answer", faq[i].answer);
        }
    }

    [Test]
    public void Authored_copy_seams_use_only_chars_the_sfpro_atlases_can_draw()
    {
        var sprites = CollectSpriteUnicodes();
        var fonts = SfProPaths
            .Select(p => (path: p, font: AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(p)))
            .ToArray();
        foreach (var (path, font) in fonts)
            Assert.IsNotNull(font, $"SF Pro font asset moved: {path} — update FontGlyphCoverageTests.");

        var failures = new List<string>();
        int count = 0;
        foreach (var (src, text) in SeamStrings())
        {
            count++;
            foreach (var (_, font) in fonts)
                CheckText(src, text, font, sprites, failures);
        }
        Assert.GreaterOrEqual(count, 60, "copy-seam enumeration collapsed");
        Assert.IsTrue(failures.Count == 0,
            $"{failures.Count} authored copy char(s) will silently not render on an SF Pro label:\n"
            + string.Join("\n", failures.Distinct()));
    }
}

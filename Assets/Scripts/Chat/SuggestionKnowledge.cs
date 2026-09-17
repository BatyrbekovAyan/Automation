using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// S1 (audit stage 5): the ONE knowledge contract both modes see — the manual catalog WITH its
/// descriptions (the auto path always had them; the panel used to send name + price only),
/// the contact fields as fields (never buried at the end of a long description), the business
/// timezone, and a version the cache keys on. Pure; the provider fills it from PlayerPrefs.
/// The server composes the prompt text from this with a budget that protects contacts and marks
/// what it had to cut — the client never truncates a tail blindly again.
/// </summary>
[Serializable]
public class SuggestionKnowledgeItem
{
    public string kind;          // product | service
    public string name;
    public string price;         // verbatim owner input; currency only if the owner typed it
    public string description;   // "" when none — never invented
}

[Serializable]
public class SuggestionKnowledgeContacts
{
    public string phone, hours, address, instagram, email;
}

[Serializable]
public class SuggestionKnowledgeDto
{
    public int v = 1;
    public string version;                       // content hash — a settings/price edit changes it
    public string timezone;                      // IANA id stamped at bot creation; null for legacy bots
    public string tzSource;                      // bot | none (server then defaults + hedges)
    public string description;                   // the «О бизнесе» text, untrimmed (server budgets it)
    public SuggestionKnowledgeContacts contacts = new();
    public List<SuggestionKnowledgeItem> items = new();
    public int documents;                        // uploaded price-list count (server RAG covers their content)
}

public static class SuggestionKnowledge
{
    private static readonly char Sep = (char)31;   // unit separator between fields of the version input
    private static readonly char Rec = (char)30;   // record separator between items
    public const int MaxItems = 120;
    public const int MaxNameChars = 120;
    public const int MaxPriceChars = 40;
    public const int MaxDescriptionChars = 600;   // per item; the server budgets the whole block
    public const int MaxBusinessDescriptionChars = 4000;

    public static SuggestionKnowledgeDto Compose(
        IReadOnlyList<SuggestionKnowledgeItem> items, SuggestionKnowledgeContacts contacts,
        string description, string timezone, int documents)
    {
        var dto = new SuggestionKnowledgeDto
        {
            description = Clamp(description, MaxBusinessDescriptionChars),
            contacts = contacts ?? new SuggestionKnowledgeContacts(),
            timezone = string.IsNullOrWhiteSpace(timezone) ? null : timezone.Trim(),
            documents = Math.Max(0, documents),
        };
        dto.tzSource = dto.timezone == null ? "none" : "bot";
        if (items != null)
        {
            foreach (var it in items)
            {
                if (it == null || string.IsNullOrWhiteSpace(it.name)) continue;
                if (dto.items.Count >= MaxItems) break;
                dto.items.Add(new SuggestionKnowledgeItem
                {
                    kind = it.kind == "service" ? "service" : "product",
                    name = Clamp(it.name.Trim(), MaxNameChars),
                    price = Clamp((it.price ?? "").Trim(), MaxPriceChars),
                    description = Clamp((it.description ?? "").Trim(), MaxDescriptionChars),
                });
            }
        }
        dto.version = Version(dto);
        return dto;
    }

    /// <summary>Stable content hash (FNV-1a 64, hex) over everything the model may read — the cache's knowledge key.</summary>
    public static string Version(SuggestionKnowledgeDto d, string ownerPrompt = null)
    {
        var sb = new StringBuilder();
        sb.Append(d.description).Append(Sep).Append(d.timezone ?? "").Append(Sep).Append(d.documents).Append(Sep);
        var c = d.contacts ?? new SuggestionKnowledgeContacts();
        sb.Append(c.phone).Append('|').Append(c.hours).Append('|').Append(c.address).Append('|').Append(c.instagram).Append('|').Append(c.email).Append(Sep);
        foreach (var it in d.items) sb.Append(it.kind).Append('|').Append(it.name).Append('|').Append(it.price).Append('|').Append(it.description).Append(Rec);
        sb.Append(Sep).Append(ownerPrompt ?? "");
        return Fnv1a64(sb.ToString());
    }

    public static string Fnv1a64(string s)
    {
        const ulong offset = 14695981039346656037UL, prime = 1099511628211UL;
        ulong h = offset;
        foreach (byte b in Encoding.UTF8.GetBytes(s ?? "")) { h ^= b; h *= prime; }
        return h.ToString("x16", CultureInfo.InvariantCulture);
    }

    private static string Clamp(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? (s ?? "") : s.Substring(0, max);
}

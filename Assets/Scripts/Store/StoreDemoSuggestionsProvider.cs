#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Deterministic <see cref="ISuggestionsProvider"/> for App Store screenshots of the «Вместе»
/// panel — the app's flagship screen.
///
/// It exists because the panel has no offline path at all: cards arrive only from a live
/// SuggestReplies round-trip, <c>SuggestionCache</c> is in-memory and keyed on the live history
/// tail, and there is no disk fallback. Offline the panel renders «Нет предложений» or an error
/// after a 15s skeleton, so the one screen that sells the product could not be photographed.
///
/// The card texts are NOT invented here — they come from the same fixture as the rest of the
/// demo data and were authored against the shipped panel prompt
/// (Tools/n8n/prompts/panel/auto_parts.md): owner's voice in the first person, prices quoted
/// verbatim from the seeded catalogue, and the installation question deferred rather than
/// answered with a made-up price. Editor-only by compilation.
/// </summary>
public class StoreDemoSuggestionsProvider : ISuggestionsProvider
{
    private const string FixturePath = "Tools/store/fixtures/demo-data.json";

    private readonly List<SuggestionItem> _items = new();

    public StoreDemoSuggestionsProvider()
    {
        if (!File.Exists(FixturePath))
        {
            Debug.LogError($"[StoreCapture] нет {FixturePath} — панель снимется пустой");
            return;
        }

        var root = JObject.Parse(File.ReadAllText(FixturePath));
        if (root["suggestions"] is not JArray cards)
        {
            Debug.LogError("[StoreCapture] в фикстуре нет блока suggestions");
            return;
        }

        foreach (var card in cards)
        {
            _items.Add(new SuggestionItem
            {
                text = card.Value<string>("text"),
                intentLabel = card.Value<string>("label"),
                move = card.Value<string>("move"),
            });
        }
    }

    /// <summary>
    /// Answers synchronously. The live provider's latency exists to exercise the skeleton
    /// state; here it would only race the capture, and the skeleton is not what we photograph.
    /// </summary>
    public void Request(SuggestionRequest request, Action<SuggestionResult> callback)
    {
        callback?.Invoke(new SuggestionResult
        {
            items = _items,
            requestSeq = request.requestSeq,
            status = _items.Count > 0 ? SuggestionStatus.Ok : SuggestionStatus.Empty,
        });
    }
}
#endif

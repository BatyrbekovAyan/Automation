using NUnit.Framework;

// EditMode coverage for N8nSuggestionsProvider.MapResponse — the PURE tolerant response
// mapper. Mirrors DashboardResponseParseTests (no asmdef; NUnit; success + garbage triplet).
// Verifies the frozen mapping policy: Ok on 1–4 valid items, Error on every failure branch,
// {text,label} -> {text,intentLabel}, and requestSeq stamped from the REQUEST (not the echo).
public class SuggestRepliesMapTests
{
    [Test]
    public void FourItems_Ok_OrderPreserved_StampsRequestSeqFromRequest()
    {
        string json = "{\"v\":1,\"requestSeq\":999,\"suggestions\":[" +
            "{\"text\":\"Здравствуйте!\",\"label\":\"Ответ\"}," +
            "{\"text\":\"Уточните год?\",\"label\":\"Уточнить\"}," +
            "{\"text\":\"Есть аналог\",\"label\":\"Вариант\"}," +
            "{\"text\":\"Оформить?\",\"label\":\"К заказу\"}]}";
        var r = N8nSuggestionsProvider.MapResponse(json, 7);

        Assert.AreEqual(SuggestionStatus.Ok, r.status);
        Assert.AreEqual(4, r.items.Count);
        Assert.AreEqual("Ответ", r.items[0].intentLabel);      // best-first lead preserved
        Assert.AreEqual("Здравствуйте!", r.items[0].text);
        Assert.AreEqual("К заказу", r.items[3].intentLabel);   // order preserved to the tail
        Assert.AreEqual(7L, r.requestSeq);                     // from the REQUEST, not the echo (999)
    }

    [Test]
    public void TwoItems_LenientOk()
    {
        string json = "{\"v\":1,\"requestSeq\":1,\"suggestions\":[" +
            "{\"text\":\"a\",\"label\":\"Ответ\"},{\"text\":\"b\",\"label\":\"Уточнить\"}]}";
        var r = N8nSuggestionsProvider.MapResponse(json, 3);
        Assert.AreEqual(SuggestionStatus.Ok, r.status);
        Assert.AreEqual(2, r.items.Count);
        Assert.AreEqual(3L, r.requestSeq);
    }

    [Test]
    public void SingleItem_LenientOk()
    {
        var r = N8nSuggestionsProvider.MapResponse(
            "{\"v\":1,\"requestSeq\":1,\"suggestions\":[{\"text\":\"ок\",\"label\":\"Ответ\"}]}", 5);
        Assert.AreEqual(SuggestionStatus.Ok, r.status);
        Assert.AreEqual(1, r.items.Count);
    }

    [Test]
    public void FiveItems_CappedAtFour_Ok()
    {
        // >4 valid items must clamp to the wire contract's upper bound (first 4, order kept) —
        // the mapper is the trust boundary; the panel renders one card per item with no cap.
        string json = "{\"v\":1,\"requestSeq\":1,\"suggestions\":[" +
            "{\"text\":\"a\",\"label\":\"Ответ\"}," +
            "{\"text\":\"b\",\"label\":\"Уточнить\"}," +
            "{\"text\":\"c\",\"label\":\"Вариант\"}," +
            "{\"text\":\"d\",\"label\":\"К заказу\"}," +
            "{\"text\":\"e\",\"label\":\"Отложить\"}]}";
        var r = N8nSuggestionsProvider.MapResponse(json, 9);
        Assert.AreEqual(SuggestionStatus.Ok, r.status);
        Assert.AreEqual(4, r.items.Count);
        Assert.AreEqual("a", r.items[0].text);        // first four kept, order preserved
        Assert.AreEqual("d", r.items[3].text);        // the 5th ("e") is dropped
        Assert.AreEqual(9L, r.requestSeq);
    }

    [Test]
    public void ErrorField_MapsToError() =>
        Assert.AreEqual(SuggestionStatus.Error,
            N8nSuggestionsProvider.MapResponse(
                "{\"v\":1,\"requestSeq\":7,\"suggestions\":[],\"error\":\"generation_failed\"}", 7).status);

    [Test]
    public void MalformedJson_MapsToError() =>
        Assert.AreEqual(SuggestionStatus.Error, N8nSuggestionsProvider.MapResponse("not json", 7).status);

    [Test]
    public void NullJson_MapsToError() =>
        Assert.AreEqual(SuggestionStatus.Error, N8nSuggestionsProvider.MapResponse(null, 7).status);

    [Test]
    public void EmptySuggestionsArray_MapsToError() =>
        Assert.AreEqual(SuggestionStatus.Error,
            N8nSuggestionsProvider.MapResponse("{\"v\":1,\"requestSeq\":7,\"suggestions\":[]}", 7).status);

    [Test]
    public void MissingSuggestions_MapsToError() =>
        Assert.AreEqual(SuggestionStatus.Error,
            N8nSuggestionsProvider.MapResponse("{\"v\":1,\"requestSeq\":7}", 7).status);

    [Test]
    public void AllItemsEmptyTextOrLabel_Dropped_IsError()
    {
        string json = "{\"v\":1,\"requestSeq\":1,\"suggestions\":[" +
            "{\"text\":\"\",\"label\":\"Ответ\"},{\"text\":\"hi\",\"label\":\"\"}]}";
        Assert.AreEqual(SuggestionStatus.Error, N8nSuggestionsProvider.MapResponse(json, 2).status);
    }

    [Test]
    public void OneEmptyItemDropped_RemainderIsOk()
    {
        string json = "{\"v\":1,\"requestSeq\":1,\"suggestions\":[" +
            "{\"text\":\"\",\"label\":\"Ответ\"},{\"text\":\"валид\",\"label\":\"Уточнить\"}]}";
        var r = N8nSuggestionsProvider.MapResponse(json, 2);
        Assert.AreEqual(SuggestionStatus.Ok, r.status);
        Assert.AreEqual(1, r.items.Count);
        Assert.AreEqual("валид", r.items[0].text);
        Assert.AreEqual("Уточнить", r.items[0].intentLabel);
        Assert.AreEqual(2L, r.requestSeq);
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Phase-1 mock data source behind the <see cref="ISuggestionsProvider"/> seam (DATA-02).
/// Carries the entire Phase-1 demo: ranked Russian small-business replies (D-14), simulated
/// latency so the skeleton loading state is genuinely exercised, a steered re-cluster
/// (INT-04/D-01), and adversarial out-of-order + error/empty paths (D-15) that exercise the
/// controller's <see cref="SuggestionSequenceGuard"/>.
///
/// Plain C# class (NOT a MonoBehaviour) so the ranking/steer/error logic stays
/// EditMode-testable: <see cref="BuildResult"/> is pure and called directly by tests; the
/// latency coroutine is the ONLY Unity dependency and runs on the injected runner. Nothing
/// here references the live backend, the messaging API, or web-request types, and it NEVER
/// touches the chat-fetch in-flight counter (the mock makes no live call — Pitfall 2).
/// </summary>
public class MockSuggestionsProvider : ISuggestionsProvider
{
    // RU intent labels (D-14; UI-SPEC Copywriting Contract) — live in the mock, not UI chrome.
    private const string LabelGreeting = "Приветствие";
    private const string LabelPrice    = "Цена";
    private const string LabelStock    = "Наличие";
    private const string LabelBooking  = "Запись";
    private const string LabelDecline  = "Вежливый отказ";

    private readonly MonoBehaviour _runner;
    private readonly float _latencySeconds;

    // --- Adversarial / error controls (D-15) — flipped by the controller demo + tests ---
    public bool  simulateError;
    public bool  simulateEmpty;
    public bool  simulateOutOfOrder;   // echo a one-behind seq (a late, superseded reply)
    public long? forcedEchoSeq;        // when set, BuildResult echoes this instead of req.requestSeq

    public MockSuggestionsProvider(MonoBehaviour runner, float latencySeconds = 1.0f)
    {
        _runner = runner;
        _latencySeconds = latencySeconds;
    }

    public void Request(SuggestionRequest request, Action<SuggestionResult> callback)
    {
        // No runner (tests), OR the runner's GameObject is inactive — OnChatSelected fires ~300ms
        // before SlideInToMessages activates the chat panel, so StartCoroutine would THROW
        // ("game object is inactive"). Answer synchronously instead of crashing/losing the request.
        if (_runner == null || !_runner.isActiveAndEnabled)
        {
            callback?.Invoke(BuildResult(request));
            return;
        }
        _runner.StartCoroutine(RespondAfterLatency(request, callback));
    }

    private IEnumerator RespondAfterLatency(SuggestionRequest req, Action<SuggestionResult> cb)
    {
        yield return new WaitForSeconds(_latencySeconds);  // skeleton genuinely exercised (D-15)
        cb?.Invoke(BuildResult(req));                       // BuildResult = pure, testable
    }

    /// <summary>
    /// Pure, latency-free result builder — tests call this directly. Echoes the request seq
    /// (or a forced/one-behind seq for the adversarial path), applies the error/empty modes,
    /// and otherwise returns the fresh or steered ranked RU set.
    /// </summary>
    public SuggestionResult BuildResult(SuggestionRequest req)
    {
        long echoSeq = ResolveEchoSeq(req);

        if (simulateError)
            return new SuggestionResult { items = null, requestSeq = echoSeq, status = SuggestionStatus.Error };
        if (simulateEmpty)
            return new SuggestionResult { items = new List<SuggestionItem>(), requestSeq = echoSeq, status = SuggestionStatus.Empty };

        bool fresh = string.IsNullOrEmpty(req?.steerTowardText);
        var items = fresh ? BuildFreshSet() : BuildSteeredSet(req.steerTowardText);
        return new SuggestionResult { items = items, requestSeq = echoSeq, status = SuggestionStatus.Ok };
    }

    private long ResolveEchoSeq(SuggestionRequest req)
    {
        long baseSeq = req?.requestSeq ?? 0L;
        if (forcedEchoSeq.HasValue) return forcedEchoSeq.Value;  // explicit stale override (tests)
        if (simulateOutOfOrder) return baseSeq - 1;              // one-behind superseded reply
        return baseSeq;                                          // normal correlation echo (DATA-03)
    }

    // Fresh, unsteered ranked set — best-first; item[0] is the recommended lead (PANEL-03).
    // item[3] is the deliberately long reply (>120 chars) for the PANEL-06 truncation demo.
    private static List<SuggestionItem> BuildFreshSet() => new List<SuggestionItem>
    {
        Item("Здравствуйте! Спасибо за обращение. Чем могу помочь?", LabelGreeting),
        Item("Стоимость зависит от объёма заказа. Подскажите, что именно вас интересует?", LabelPrice),
        Item("Да, товар есть в наличии. Могу оформить для вас прямо сейчас.", LabelStock),
        Item("Конечно, давайте подберём удобное для вас время. У нас есть свободные слоты на этой неделе " +
             "в первой половине дня и ближе к вечеру — подскажите, какой день вам подходит, и я сразу " +
             "забронирую запись на ваше имя.", LabelBooking),
    };

    // Steered re-cluster (INT-04/D-01): a DIFFERENT ordered set biased toward the pick, so
    // items[0] differs from the fresh lead. Phase-2 replaces this deterministic transform with
    // the live "steer toward" field (N8N-03).
    private static List<SuggestionItem> BuildSteeredSet(string steerTowardText) => new List<SuggestionItem>
    {
        Item("Отлично, тогда уточню детали по вашему запросу и сразу всё подготовлю.", LabelBooking),
        Item("Могу предложить пару вариантов под ваш бюджет — какой ориентир по цене вам комфортен?", LabelPrice),
        Item("Уже проверяю наличие на складе, буквально минуту.", LabelStock),
        Item("К сожалению, сейчас это направление мы не обслуживаем, но буду рад помочь с другими вопросами.", LabelDecline),
    };

    private static SuggestionItem Item(string text, string intentLabel)
        => new SuggestionItem { text = text, intentLabel = intentLabel };
}

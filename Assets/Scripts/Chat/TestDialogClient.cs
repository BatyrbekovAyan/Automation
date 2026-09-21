using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>One turn of the synthetic history the owner builds while testing (client / business).</summary>
[Serializable]
public class TestDialogTurn
{
    public string role;
    public string text;
}

[Serializable]
public class TestDialogRequest
{
    public string profileId;
    public string workflowId;
    public string channel;
    public string text;
    public List<TestDialogTurn> history = new();
}

[Serializable]
public class TestDialogDocumentSource
{
    public string snippet;
    public string fileId;
}

[Serializable]
public class TestDialogSources
{
    public bool catalog;
    public bool contacts;
    public bool businessText;
    public List<TestDialogDocumentSource> documents = new();
}

[Serializable]
public class TestDialogHandoff
{
    public string reason;
    public string name;
    public string phone;
    public string summary;
}

/// <summary>
/// Stage 7.1: the server's answer to a test message — the bot's REAL prompt answered it, nothing was sent
/// to anyone (<c>sent</c> is always false, <c>quotaCounted</c> always false) and the handoff the model
/// WOULD have created is reported as data instead of being created.
/// </summary>
[Serializable]
public class TestDialogResponse
{
    public bool success;
    public string error;
    public string reply;
    public TestDialogSources sources;
    public TestDialogHandoff wouldHandoff;
    public bool wouldSend;
    public bool sent;
    public bool quotaCounted;
    public string correlationId;

    public static TestDialogResponse Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonConvert.DeserializeObject<TestDialogResponse>(json); }
        catch (Exception) { return null; }
    }
}

/// <summary>The classified outcome of one test call — the sheet shows a named reason, never a raw code.</summary>
public sealed class TestDialogResult
{
    public const string KindNetwork = "network";
    public const string KindUnauthorized = "unauthorized";
    public const string KindForbidden = "forbidden";
    public const string KindPromptUnavailable = "prompt_unavailable";
    public const string KindGenerationFailed = "generation_failed";
    public const string KindBadRequest = "bad_request";
    public const string KindUnknown = "unknown";

    public readonly TestDialogResponse Response;
    public readonly string ErrorKind;   // null when the call succeeded

    public TestDialogResult(TestDialogResponse response, string errorKind) { Response = response; ErrorKind = errorKind; }

    public bool IsOk => ErrorKind == null && Response != null && Response.success && !string.IsNullOrEmpty(Response.reply);
}

/// <summary>
/// Stage 7.1 transport for <c>/webhook/TestDialog</c>: ONLY through <see cref="AuthorizedRequest"/> (the
/// gate binds the profile/workflow ids to this install). The bot's own memory, quota and ledger are untouched
/// by design — see build-test-dialog.py.
/// </summary>
public static class TestDialogClient
{
    public const int RequestTimeoutSeconds = 45;   // the workflow's own executionTimeout is 60 s
    public const int MaxHistoryTurns = 10;

    public static IEnumerator Send(TestDialogRequest request, Action<TestDialogResult> done)
    {
        string url = $"{Manager.n8nBaseUrl}/webhook/TestDialog";
        string body = JsonConvert.SerializeObject(request);
        UnityWebRequest req = null;
        yield return AuthorizedRequest.Send(() => AuthorizedRequest.JsonPost(url, body, RequestTimeoutSeconds), r => req = r);
        using var scope = req;
        string text = req.downloadHandler != null ? req.downloadHandler.text : null;
        var parsed = TestDialogResponse.Parse(text);
        string kind = ClassifyFailure(req.result == UnityWebRequest.Result.Success, req.responseCode, parsed);
        if (kind != null) Debug.LogWarning($"[TestDialog] {kind} [{req.responseCode}] {req.error}");
        done?.Invoke(new TestDialogResult(parsed, kind));
    }

    /// <summary>Pure: the reason a call did not yield a reply (null = it did).</summary>
    public static string ClassifyFailure(bool transportOk, long status, TestDialogResponse parsed)
    {
        if (parsed != null && parsed.success && !string.IsNullOrEmpty(parsed.reply)) return null;
        if (status == 401) return TestDialogResult.KindUnauthorized;
        if (status == 403) return TestDialogResult.KindForbidden;
        if (parsed != null && !string.IsNullOrEmpty(parsed.error))
        {
            switch (parsed.error)
            {
                case "bot_prompt_unavailable": return TestDialogResult.KindPromptUnavailable;
                case "generation_failed": return TestDialogResult.KindGenerationFailed;
                case "bad_request": return TestDialogResult.KindBadRequest;
                case "forbidden": return TestDialogResult.KindForbidden;
                case "unauthorized": return TestDialogResult.KindUnauthorized;
            }
        }
        if (!transportOk && status == 0) return TestDialogResult.KindNetwork;
        return TestDialogResult.KindUnknown;
    }

    /// <summary>Pure: the next history the owner's follow-up carries (bounded, newest last).</summary>
    public static List<TestDialogTurn> AppendTurn(List<TestDialogTurn> history, string clientText, string businessReply)
    {
        var next = history != null ? new List<TestDialogTurn>(history) : new List<TestDialogTurn>();
        next.Add(new TestDialogTurn { role = "client", text = clientText ?? "" });
        if (!string.IsNullOrEmpty(businessReply)) next.Add(new TestDialogTurn { role = "business", text = businessReply });
        while (next.Count > MaxHistoryTurns) next.RemoveAt(0);
        return next;
    }
}

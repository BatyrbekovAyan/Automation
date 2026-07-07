using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

public enum OutcomeStatus { Unknown, OrderCollected, OwnerNeeded, InDialog, ClientSilent, QuestionClosed }

public static class OutcomeStatusMap
{
    public static OutcomeStatus FromId(string id) => id switch
    {
        "order_collected" => OutcomeStatus.OrderCollected,
        "owner_needed"    => OutcomeStatus.OwnerNeeded,
        "in_dialog"       => OutcomeStatus.InDialog,
        "client_silent"   => OutcomeStatus.ClientSilent,
        "question_closed" => OutcomeStatus.QuestionClosed,
        _                 => OutcomeStatus.Unknown,
    };

    public static string ToId(OutcomeStatus s) => s switch
    {
        OutcomeStatus.OrderCollected => "order_collected",
        OutcomeStatus.OwnerNeeded    => "owner_needed",
        OutcomeStatus.InDialog       => "in_dialog",
        OutcomeStatus.ClientSilent   => "client_silent",
        OutcomeStatus.QuestionClosed => "question_closed",
        _                            => "",
    };
}

[Serializable]
public class DashboardOutcome
{
    public string profileId;
    public string chatId;
    public string outcome;
    public string summary;
    public long outcomeAt;      // unix ms
    public long lastMessageAt;  // unix ms

    [JsonIgnore] public OutcomeStatus Status => OutcomeStatusMap.FromId(outcome);
}

[Serializable]
public class DashboardResponse
{
    public bool success;
    public int classified;
    public bool truncated;
    public List<DashboardOutcome> outcomes = new();

    public static DashboardResponse Parse(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var r = JsonConvert.DeserializeObject<DashboardResponse>(json);
            if (r != null) r.outcomes ??= new List<DashboardOutcome>();
            return r;
        }
        catch (JsonException) { return null; }
    }
}

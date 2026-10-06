// Read-only reproduction: policy copied verbatim from Assets/Scripts/Main/BotReadiness.cs at runtime.
using System;
public readonly struct BotTestTicket
{
    public readonly int Seq;
    public readonly string BotName;
    public readonly string ProfileId;
    public readonly string WorkflowId;

    public BotTestTicket(int seq, string botName, string profileId, string workflowId)
    { Seq = seq; BotName = botName; ProfileId = profileId; WorkflowId = workflowId; }
}

/// <summary>What one returned test result may change (R16).</summary>
public readonly struct BotTestOutcome
{
    public readonly bool ApplyToScreen;   // the sheet still shows the request's screen: reply, history and busy may change
    public readonly int? Verdict;         // 1 passed / 0 failed, written under the TICKET's bot; null = no verdict

    public BotTestOutcome(bool applyToScreen, int? verdict) { ApplyToScreen = applyToScreen; Verdict = verdict; }
}

/// <summary>R16, pure: who a late test answer may touch. The sheet is one shared static surface reused for every bot.</summary>
public static class BotTestResultPolicy
{
    /// <summary>Only the bot's own outcome is a verdict: a reply passes, a failed generation or an unreadable prompt
    /// fails; no connection, no session or anything else says nothing about the bot.</summary>
    public static int? VerdictOf(TestDialogResult result)
    {
        if (result == null) return null;
        if (result.IsOk) return 1;
        bool botSide = result.ErrorKind == TestDialogResult.KindGenerationFailed || result.ErrorKind == TestDialogResult.KindPromptUnavailable;
        return botSide ? 0 : (int?)null;
    }

    /// <param name="currentSeq">the sheet's sequence now — bumped by every Show and every send</param>
    /// <param name="botAlive">the ticket's Bot still exists (Unity-null once deleted)</param>
    /// <param name="currentTarget">the ticket's bot's target NOW (null when no channel is connected)</param>
    /// <param name="lastRecordedSeq">the newest ticket whose verdict was already written for this bot (0 = none)</param>
    public static BotTestOutcome Decide(BotTestTicket ticket, int currentSeq, bool botAlive, BotTestTarget currentTarget, int lastRecordedSeq, TestDialogResult result)
    {
        int? verdict = VerdictOf(result);
        bool sameTarget = botAlive && currentTarget != null
            && currentTarget.ProfileId == ticket.ProfileId && currentTarget.WorkflowId == ticket.WorkflowId;
        bool newest = ticket.Seq > lastRecordedSeq;
        bool writable = verdict.HasValue && sameTarget && newest && !string.IsNullOrEmpty(ticket.BotName);
        return new BotTestOutcome(ticket.Seq == currentSeq, writable ? verdict : null);
    }
}


// Minimal test inputs only; no Unity/PlayerPrefs or real bot data.
public sealed class BotTestTarget { public string ProfileId, WorkflowId; }
public sealed class TestDialogResult { public bool IsOk; public string ErrorKind; public const string KindGenerationFailed="generation_failed", KindPromptUnavailable="bot_prompt_unavailable"; }
public static class Probe {
 static void Print(string name, BotTestOutcome o) { Console.WriteLine(name + ": ApplyToScreen=" + o.ApplyToScreen + ", Verdict=" + (o.Verdict.HasValue ? o.Verdict.ToString() : "null")); }
 public static void Main() {
 var t = new BotTestTicket(2,"ReviewA","p-A","wf-A");
 var ok = new TestDialogResult { IsOk=true };
 Print("same current request, alive same target", BotTestResultPolicy.Decide(t,2,true,new BotTestTarget{ProfileId="p-A",WorkflowId="wf-A"},0,ok));
 Print("same current request, bot deleted", BotTestResultPolicy.Decide(t,2,false,null,0,ok));
 Print("same current request, workflow changed", BotTestResultPolicy.Decide(t,2,true,new BotTestTarget{ProfileId="p-A",WorkflowId="wf-B"},0,ok));
 Print("new screen sequence", BotTestResultPolicy.Decide(t,3,true,new BotTestTarget{ProfileId="p-A",WorkflowId="wf-A"},0,ok));
 }
}

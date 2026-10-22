using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// V12 (iteration 2026-09-28, found 2026-10-06 on the owner's bot «23»): a bot is removed from the phone only when the
/// server CONFIRMED its deletion — or when the owner, told the truth about a final refusal, chose «Удалить только с
/// телефона». Before, Bot.DeleteBot wiped the local state first and fired the server calls without reading a single
/// answer: the server refused (403), the app deleted the bot anyway, and the memory, history and clone stayed on the
/// server with nothing left in the app to delete them from.
///
/// Source guards (EditMode cannot run the network): the local wipe carries no server call; the settings screen and
/// the full wipe reach it only from the verdict (or the explicit phone-only choice); the server half reads every
/// answer, unlinks the Wappi profile FIRST (while it is still the owner's live row), and holds the reply-mode queue so
/// no send recreates a row the delete removes. Plus the authored premise the popup's runtime fit depends on.
/// </summary>
public class BotDeletionWiringTests
{
    private static string Src(string rel) => File.ReadAllText(Path.Combine(Application.dataPath, rel));

    private static string MethodBody(string text, string method)
    {
        int i = text.IndexOf(method + "(", System.StringComparison.Ordinal);
        Assert.GreaterOrEqual(i, 0, $"{method} not found");
        int open = text.IndexOf('{', i);
        int depth = 0;
        for (int k = open; k < text.Length; k++)
        {
            if (text[k] == '{') depth++;
            else if (text[k] == '}' && --depth == 0) return text.Substring(open, k - open + 1);
        }
        Assert.Fail($"{method}: unbalanced braces"); return null;
    }

    private static int Count(string text, string needle) => Regex.Matches(text, Regex.Escape(needle)).Count;

    [Test]
    public void LocalWipe_TalksToNoServer_AndTheOldOneCallDeleteIsGone()
    {
        string bot = Src("Scripts/Main/Bot.cs");
        Assert.IsFalse(bot.Contains("public void DeleteBot("),
            "the one-call delete (local wipe + unread fire-and-forget server calls) must be gone — nothing may delete a bot locally without a verdict");
        string local = MethodBody(bot, "public void DeleteBotLocally");
        foreach (var call in new[] { "DeleteBotFilesOnServer(", "DeleteProfilesAndWorkflows(", "WappiProxy", "AuthorizedRequest", "DeleteBotOnServer(" })
            Assert.IsFalse(local.Contains(call), $"the local wipe must not talk to the server ({call})");
        Assert.IsTrue(local.Contains("ReplyModeSyncService.Existing?.ForgetBot(transform.name)"),
            "a deleted bot's queued reply-mode choices go with it (R5)");
    }

    [Test]
    public void LocalWipe_ClearsTheRetiredChannelMarkers()
    {
        // M4 (2026-10-09): a channel the server retired is remembered per bot — {bot}{Whatsapp|Telegram}Retired{ProfileId|Reason}.
        // Per-bot keys like any other: a deleted bot must not leave them behind (bot-persistence).
        // F2 (T8): the retired set also holds the channel's former clone ({bot}{Ch}RetiredWorkflowId), and a Create that
        // failed from settings leaves {bot}{Ch}CreatePending — both are per-bot keys that must go with the bot.
        string local = MethodBody(Src("Scripts/Main/Bot.cs"), "public void DeleteBotLocally");
        Assert.IsTrue(local.Contains("ProfileRetirement.Clear(transform.name)"), "the local wipe must clear the retired-channel markers");
        string retirement = Src("Scripts/Auth/ProfileRetirement.cs");
        foreach (var suffix in new[] { "WhatsappRetiredProfileId", "WhatsappRetiredReason", "TelegramRetiredProfileId", "TelegramRetiredReason",
                                       "WhatsappRetiredWorkflowId", "TelegramRetiredWorkflowId" })
            Assert.IsTrue(retirement.Contains($"\"{suffix}\""), $"retired-set key suffix {suffix} must be declared in ProfileRetirement");
        Assert.IsTrue(MethodBody(retirement, "public static void Clear").Contains("KeySuffixes"), "Clear deletes every declared marker key");
        Assert.IsTrue(local.Contains("PlayerPrefs.DeleteKey(transform.name + CreateWorkflowResponse.PendingSuffix(channel))"),
            "the local wipe must clear the pending-Create flag of both channels");
        string response = Src("Scripts/Main/CreateWorkflowResponse.cs");
        foreach (var suffix in new[] { "WhatsappCreatePending", "TelegramCreatePending" })
            Assert.IsTrue(response.Contains($"\"{suffix}\""), $"pending-Create key suffix {suffix} must be declared in CreateWorkflowResponse");
    }

    [Test]
    public void LocalWipe_ClearsTheLearningMemory()
    {
        // Learning percent: the phone remembers each bot's best percent and "«Авто» has opened" as {bot}LearnPeak / {bot}LearnUnlocked.
        // Per-bot keys like any other: a deleted bot must not leave them behind — a NEW bot that ever takes the slot would
        // start at the old one's percent, and a stale unlock would keep its «Авто» open.
        string local = MethodBody(Src("Scripts/Main/Bot.cs"), "public void DeleteBotLocally");
        string code = Regex.Replace(local, @"//[^\r\n]*", "");   // a commented-out call is no call
        Assert.IsTrue(code.Contains("LearnProgressStore.Clear(transform.name)"), "the local wipe must clear the learning memory");
        string store = Src("Scripts/Billing/LearnProgressStore.cs");
        foreach (var suffix in new[] { "LearnPeak", "LearnUnlocked" })
            Assert.IsTrue(store.Contains($"\"{suffix}\""), $"learning-memory key suffix {suffix} must be declared in LearnProgressStore");
        string clear = MethodBody(store, "public static void Clear");
        Assert.IsTrue(clear.Contains("PeakSuffix") && clear.Contains("UnlockedSuffix"), "Clear deletes both declared keys");
    }

    [Test]
    public void ServerHalf_DeletesTheRetiredSetToo_ButUnlinksOnlyTheCurrentProfiles()
    {
        // F2 (T8): after the transfer a retired channel's ids live in {bot}{Ch}Retired* and the current ones are -1. V12
        // must still delete what the server holds under them (plan: «перенос без чтения retired-id при удалении выпускать
        // нельзя»): Delete Bot Files gets the retired set (a second call only when the channel ALSO has current ids), the
        // retired clone is deleted, and only CURRENT profiles are unlinked — a retired one is already gone at Wappi.
        string m = MethodBody(Src("Scripts/Main/Manager.cs"), "public IEnumerator DeleteBotOnServer");
        int ids = m.IndexOf("ProfileRetirement.ServerIdsOf(bot)", System.StringComparison.Ordinal);
        int trace = m.IndexOf("BotServerDeletion.HasServerTrace(ids)", System.StringComparison.Ordinal);
        Assert.IsTrue(ids >= 0 && trace > ids, "the trace check sees the retired ids too (else NothingOnServer — the V12 regression)");
        StringAssert.Contains("BotServerDeletion.ProfilesToUnlink(ids)", m, "the unlink loop walks the current profiles only");
        StringAssert.Contains("DeleteBotFilesPayload.ComposeAll(ids, BillingIdentity.AppUserId)", m, "every Delete Bot Files payload, retired set included");
        StringAssert.Contains("BotServerDeletion.ClonesToDelete(ids)", m, "the clones, retired one included");
        string deletion = Src("Scripts/Main/BotServerDeletion.cs");
        StringAssert.DoesNotContain("Retired", MethodBody(deletion, "public static IEnumerable<(string profileId, ChatChannel channel)> ProfilesToUnlink"),
            "a retired profile is never unlinked — Wappi deleted it already");
        StringAssert.Contains("RetiredWorkflowId", MethodBody(deletion, "public static IEnumerable<string> ClonesToDelete"),
            "the channel's former clone is deleted too");
    }

    [Test]
    public void ServerHalf_UnlinksTheProfileFirst_ReadsEveryAnswer_AndHoldsTheQueue()
    {
        string manager = Src("Scripts/Main/Manager.cs");
        Assert.IsFalse(manager.Contains("IEnumerator DeleteBotFilesRoutine("), "the fire-and-forget DeleteBotFiles routine (answer only logged) must be gone");
        Assert.IsFalse(manager.Contains("public void DeleteProfilesAndWorkflows("), "the unread profile/workflow delete fan-out must be gone");
        string m = MethodBody(manager, "public IEnumerator DeleteBotOnServer");
        int hold = m.IndexOf("ReplyModeSyncService.Existing?.Hold(", System.StringComparison.Ordinal);
        int profile = m.IndexOf("WappiProxy.Op.ProfileDelete", System.StringComparison.Ordinal);
        int files = m.IndexOf("/webhook/DeleteBotFiles", System.StringComparison.Ordinal);
        int workflow = m.IndexOf("WorkflowStateRequest.Delete", System.StringComparison.Ordinal);
        Assert.IsTrue(hold >= 0 && profile > hold && files > profile && workflow > files,
            $"order: hold the queue ({hold}) → unlink the Wappi profile while its row is live ({profile}) → delete the files/memory ({files}) → the clone ({workflow})");
        Assert.IsTrue(m.Contains("BotServerDeletion.ClassifyProfileDelete(") && Count(m, "BotServerDeletion.ClassifyServerDelete(") >= 2,
            "every answer must be classified — a 403 is final, a 5xx/no answer is a retry");
        Assert.IsTrue(m.Contains("ReplyModeSyncService.Existing?.Release("), "a bot that stays must get its queue back");
        Assert.IsTrue(m.Contains("BotServerDeletion.HasServerTrace("), "a never-authorised bot needs no network to go");
    }

    [Test]
    public void ServerDeleteAnswers_AreClassifiedWithTheirBodies()
    {
        // C1: a step is done only on the body's positive word, so a request that cannot read its body would make every
        // deletion a retry forever. Both factories DeleteBotOnServer uses must carry a body reader, and both answers
        // must reach the classifier with their own body.
        string manager = Src("Scripts/Main/Manager.cs");
        string m = MethodBody(manager, "public IEnumerator DeleteBotOnServer");
        Assert.IsTrue(m.Contains("AuthorizedRequest.JsonPost(") && m.Contains("NewWorkflowStateRequest("),
            "Delete Bot Files goes through JsonPost, the clone through NewWorkflowStateRequest");
        Assert.IsTrue(MethodBody(manager, "private static UnityWebRequest NewWorkflowStateRequest").Contains("new DownloadHandlerBuffer()"),
            "Set Workflow State's answer must be readable");
        Assert.IsTrue(MethodBody(Src("Scripts/Auth/AuthorizedRequest.cs"), "public static UnityWebRequest JsonPost").Contains("new DownloadHandlerBuffer()"),
            "Delete Bot Files' answer must be readable");
        Assert.AreEqual(2, Regex.Matches(m, @"ClassifyServerDelete\((\w+)\.responseCode,\s*\1\.downloadHandler\?\.text\)").Count,
            "each server delete is classified by its own status AND body");
    }

    [TestCase("lmjYsdNcQA2IE5rl-Delete_Bot_Files.json")]
    [TestCase("0DP79STI3pafXO9B-Set_Workflow_State.json")]
    public void ServerCanon_AnswersA2xxOnlyWithThePositiveWord(string workflowFile)
    {
        // The premise of C1, read from the server canon: the client's ONLY success sign is `"success": true`, so every
        // 200 these webhooks answer must carry it, and every `"success": false` must go out with a non-2xx status.
        // Change the server's success shape and every bot deletion becomes a retry — this fails first.
        var workflow = JObject.Parse(File.ReadAllText(Path.Combine(Application.dataPath, "..", "Tools", "n8n", "workflows", workflowFile)));
        var positive = new Regex(@"""success""\s*:\s*true");
        var negative = new Regex(@"""success""\s*:\s*false");
        int successNodes = 0;
        foreach (var node in workflow["nodes"])
        {
            if ((string)node["type"] != "n8n-nodes-base.respondToWebhook") continue;
            string name = (string)node["name"];
            string body = (string)node["parameters"]?["responseBody"] ?? "";
            string status = node["parameters"]?["options"]?["responseCode"]?.ToString();
            if (status == null)
            {
                Assert.IsTrue(positive.IsMatch(body), $"{workflowFile} «{name}» answers 200 without \"success\": true: {body}");
                successNodes++;
            }
            else if (negative.IsMatch(body))
                Assert.IsFalse(Regex.IsMatch(status, @"(^|\D)2\d\d(\D|$)"), $"{workflowFile} «{name}» refuses with a 2xx status: {status}");
        }
        Assert.Greater(successNodes, 0, $"{workflowFile}: no success answer found");
    }

    // C1b: the 4xx answers a deleting webhook can give — the client reads a 4xx as a refusal only with one of these
    // bodies. Every responder whose status can be a 4xx (other than 401, a session fault) must be one of these known
    // nodes; a new one fails here until the client is taught it, instead of reading as a retry forever.
    private static readonly HashSet<string> KnownRefusalResponders = new HashSet<string>
        { "Respond Owner Refused", "Respond 401", "Respond Rejected", "Respond Forbidden" };

    [TestCase("lmjYsdNcQA2IE5rl-Delete_Bot_Files.json")]
    [TestCase("0DP79STI3pafXO9B-Set_Workflow_State.json")]
    public void ServerCanon_EveryRefusalItSends_IsReadAsARefusal(string workflowFile)
    {
        var workflow = Canon(workflowFile);
        int refusals = 0;
        foreach (var node in workflow["nodes"])
        {
            if ((string)node["type"] != "n8n-nodes-base.respondToWebhook") continue;
            string name = (string)node["name"];
            string status = node["parameters"]?["options"]?["responseCode"]?.ToString() ?? "";
            if (!Regex.IsMatch(status, @"(?<!\d)4(?!01)\d\d(?!\d)")) continue;
            Assert.IsTrue(KnownRefusalResponders.Contains(name),
                $"{workflowFile} «{name}» can answer a 4xx ({status}) the client does not know — teach BotServerDeletion its body");
            refusals += AssertRefusalsOf(workflow, workflowFile, name, status, (string)node["parameters"]["responseBody"]);
        }
        Assert.Greater(refusals, 0, $"{workflowFile}: no refusal found");
    }

    /// <summary>Checks one known refusal responder's bodies against the client; returns how many bodies it checked.</summary>
    private static int AssertRefusalsOf(JObject workflow, string file, string name, string status, string body)
    {
        switch (name)
        {
            case "Respond Owner Refused":
            {
                // Delete Ownership: allowed=false → 403 forbidden + its reason; a DB fault → 503 db_unavailable.
                StringAssert.Contains("=== false ? 403 : 503", status, $"{file} «{name}»");
                StringAssert.Contains("=== false ? \"forbidden\" : \"db_unavailable\"", body, $"{file} «{name}»");
                StringAssert.Contains("($json.reason || \"unproven\")", body, $"{file} «{name}»");
                var reasons = new HashSet<string>();
                foreach (Match m in Regex.Matches(NodeParameter(workflow, "Delete Ownership", "query"), @"then '(\w+)'"))
                    if (m.Groups[1].Value != "ok") reasons.Add(m.Groups[1].Value);
                CollectionAssert.AreEquivalent(new[] { BotServerDeletion.ReasonOwnedElsewhere, BotServerDeletion.ReasonUnproven }, reasons,
                    $"{file}: Delete Ownership's refusal reasons");
                foreach (string reason in reasons)
                    AssertRefused(403, $"{{\"success\":false,\"error\":\"forbidden\",\"reason\":\"{reason}\"}}", reason);
                AssertRetry(403, "{\"success\":false,\"error\":\"db_unavailable\",\"reason\":null}");
                return reasons.Count;
            }
            case "Respond 401":
            {
                // The gate: 'forbidden:<field>' → 403, 'unauthorized:<why>' → 401 (a session fault, retried by status).
                StringAssert.Contains("startsWith('forbidden') ? 403 : 401", status, $"{file} «{name}»");
                StringAssert.Contains("\"error\": \"unauthorized\", \"reason\": $json.crAuth.reason", body, $"{file} «{name}»");
                StringAssert.Contains("deny = 'forbidden:' + f", NodeParameter(workflow, "Auth Gate", "jsCode"), $"{file}: the gate's refusal reason");
                AssertRefused(403, "{\"success\":false,\"error\":\"unauthorized\",\"reason\":\"forbidden:workflowId\"}", BotServerDeletion.ReasonUnproven);
                return 1;
            }
            case "Respond Rejected":
            {
                // Set Workflow State's Validate: a malformed id/action or a protected infra id — 400, final.
                Assert.AreEqual("400", status, $"{file} «{name}»");
                StringAssert.Contains("\"error\": $json.error", body, $"{file} «{name}»");
                var errors = new HashSet<string>();
                foreach (Match m in Regex.Matches(NodeParameter(workflow, "Validate", "jsCode"), @"error: '(\w+)'"))
                    errors.Add(m.Groups[1].Value);
                Assert.IsNotEmpty(errors, $"{file}: Validate names no error");
                foreach (string error in errors)
                    AssertRefused(400, $"{{\"success\":false,\"error\":\"{error}\"}}", BotServerDeletion.ReasonUnproven);
                return errors.Count;
            }
            default:
            {
                // A literal body (Respond Forbidden): the exact JSON the client receives.
                string literal = Regex.Match(body, @"^=\{\{\s*(\{.*\})\s*\}\}$", RegexOptions.Singleline).Groups[1].Value;
                Assert.IsNotEmpty(literal, $"{file} «{name}»: expected a literal body, got {body}");
                AssertRefused(int.Parse(status), JObject.Parse(literal).ToString(), BotServerDeletion.ReasonUnproven);
                return 1;
            }
        }
    }

    private static void AssertRefused(long code, string body, string reason)
    {
        Assert.AreEqual(BotServerDeletion.Step.Refused, BotServerDeletion.ClassifyServerDelete(code, body), $"{code} {body}");
        Assert.AreEqual(reason, BotServerDeletion.Combine(new[] { (BotServerDeletion.Step.Refused, body) }).Reason, body);
    }

    private static void AssertRetry(long code, string body) =>
        Assert.AreEqual(BotServerDeletion.Step.Transient, BotServerDeletion.ClassifyServerDelete(code, body), $"{code} {body}");

    private static JObject Canon(string workflowFile) =>
        JObject.Parse(File.ReadAllText(Path.Combine(Application.dataPath, "..", "Tools", "n8n", "workflows", workflowFile)));

    private static string NodeParameter(JObject workflow, string node, string parameter)
    {
        foreach (var n in workflow["nodes"])
            if ((string)n["name"] == node) return (string)n["parameters"]?[parameter] ?? "";
        Assert.Fail($"node «{node}» not found");
        return null;
    }

    [Test]
    public void ServerCanon_ACloneAlreadyGone_IsConfirmed_NotRefused()
    {
        // Set Workflow State turns the n8n API's 404 (the clone is already deleted) into its 200 success:true — a repeated
        // delete is Done. Without it the client would see no positive word and retry a finished deletion forever.
        var workflow = JObject.Parse(File.ReadAllText(Path.Combine(Application.dataPath, "..", "Tools", "n8n", "workflows",
                                                                   "0DP79STI3pafXO9B-Set_Workflow_State.json")));
        JToken gone = null;
        foreach (var node in workflow["nodes"])
            if ((string)node["name"] == "Workflow Gone?") gone = node;
        Assert.IsNotNull(gone, "Set Workflow State: «Workflow Gone?» not found");
        StringAssert.Contains("404", gone["parameters"].ToString(), "an already-deleted clone (404) counts as gone");
        Assert.AreEqual("Respond OK", (string)workflow["connections"]?["Workflow Gone?"]?["main"]?[0]?[0]?["node"],
            "«gone» answers with the success Respond");
    }

    [Test]
    public void SettingsDelete_RemovesTheBotOnlyOnTheVerdict_OrTheOwnersPhoneOnlyChoice()
    {
        Assert.IsFalse(Src("Scripts/Main/BotSettings.cs").Contains("DeleteBotLocally("), "the delete flow lives in the BotSettings.Delete partial only");
        string bs = Src("Scripts/Main/BotSettings.Delete.cs");
        Assert.AreEqual(1, Count(bs, ".DeleteBotLocally()"), "exactly one local delete — inside FinishDeleteLocally");
        Assert.IsTrue(MethodBody(bs, "private void FinishDeleteLocally").Contains(".DeleteBotLocally()"));
        string confirm = MethodBody(bs, "private void ConfirmDeleteBot");
        Assert.IsTrue(Regex.IsMatch(confirm, @"DeletePopupState\.Refused\)[\s\S]*?FinishDeleteLocally\("),
            "the confirm button deletes locally ONLY in the refused state (the owner's explicit «Удалить только с телефона»)");
        Assert.IsTrue(confirm.Contains("StartServerDelete("), "otherwise the confirm asks the server");
        string handle = MethodBody(bs, "private void HandleDeleteOutcome");
        Assert.IsTrue(Regex.IsMatch(handle, @"Verdict\.Done:[\s\S]*?FinishDeleteLocally\("), "Done → the bot goes");
        Assert.IsTrue(Regex.IsMatch(handle, @"Verdict\.Retry:\s*ShowDeletePopup\(DeletePopupState\.Retry"), "Retry → the bot stays, «повторите»");
        Assert.IsTrue(Regex.IsMatch(handle, @"Verdict\.Refused:\s*ShowDeletePopup\(DeletePopupState\.Refused"), "Refused → the honest phone-only choice");
        Assert.IsTrue(MethodBody(bs, "private void StartServerDelete").Contains("Manager.Instance.DeleteBotOnServer("));
    }

    [Test]
    public void FullWipe_ClearsPrefsOnlyAfterEveryBotIsSettled()
    {
        string acc = Src("Scripts/Main/ProfileSubPages.Account.cs");
        Assert.AreEqual(1, Count(acc, "PlayerPrefs.DeleteAll()"), "one DeleteAll — in FinishWipe");
        string finish = MethodBody(acc, "private void FinishWipe");
        int forget = finish.IndexOf("ReplyModeSyncService.Existing?.ForgetAll()", System.StringComparison.Ordinal);
        int all = finish.IndexOf("PlayerPrefs.DeleteAll()", System.StringComparison.Ordinal);
        Assert.IsTrue(forget >= 0 && all > forget, "the in-memory queue is emptied before the prefs (it re-persists itself — R5)");
        string wipe = MethodBody(acc, "private IEnumerator RunWipeRoutine");
        Assert.IsTrue(wipe.Contains("yield return Manager.Instance.DeleteBotOnServer("), "each bot waits for its server verdict");
        int retry = wipe.IndexOf("BotServerDeletion.Verdict.Retry", System.StringComparison.Ordinal);
        Assert.GreaterOrEqual(retry, 0, "a temporary failure must be handled");
        int stop = wipe.IndexOf("yield break", retry, System.StringComparison.Ordinal);
        Assert.IsTrue(stop > retry && !wipe.Substring(retry, stop - retry).Contains("FinishWipe("),
            "a temporary failure STOPS the wipe before the prefs go (the bots still on the server keep their ids)");
        Assert.IsTrue(wipe.Contains("BotDeletionCopy.PhoneOnlyAction"), "a final refusal offers the phone-only choice instead of blocking the wipe forever");
    }

    [Test]
    public void DeletePopup_BodyClearsTheButtons_InThePrefab()
    {
        // ConfirmCardFitter grows the card by the texts' overflow and so PRESERVES the authored body-to-button gap —
        // including a negative one (the DeleteChatConfirmPanel lesson). Check that gap from the prefab itself.
        // Resolved by PATH from the popup (C1b): «Content/Body» alone named the first of SIX confirm popups in this prefab
        // that have one — ConfirmChangeWhatsappNumberPopup, whose 180u body made the gap read 72u instead of the real 32u.
        string prefab = File.ReadAllText(BotSettingsPrefab);
        float cardH = RectAt(prefab, DeletePopup, "Content").size.y;
        var body = RectAt(prefab, DeletePopup, "Content", "Body");
        float bodyBottomFromTop = -body.pos.y + body.size.y;
        foreach (string buttonName in new[] { "ConfirmButton", "CancelButton" })
        {
            var button = RectAt(prefab, DeletePopup, "Content", buttonName);
            float buttonTopFromTop = cardH - (button.pos.y + button.size.y);
            Assert.GreaterOrEqual(buttonTopFromTop - bodyBottomFromTop, 24f,
                $"{DeletePopup}: Body ends at {bodyBottomFromTop}u, {buttonName} starts at {buttonTopFromTop}u (card {cardH}u)");
        }
    }

    [Test]
    public void DeletePopup_EveryStatesBody_FitsItsAuthoredBox()
    {
        // The popup is re-used for Ask / Retry / Refused and ConfirmCardFitter grows the card for a longer body — but the
        // server-state copy is the app's own, so it is held to the authored box: no state needs the growth, and a copy
        // change that would need it fails here first (measured with the real font, size and column of the prefab).
        string prefab = File.ReadAllText(BotSettingsPrefab);
        float cardW = RectAt(prefab, DeletePopup, "Content").size.x;
        var box = RectAt(prefab, DeletePopup, "Content", "Body");
        Assert.AreEqual(0f, box.anchorMin.x, "the body stretches across the card");
        Assert.AreEqual(1f, box.anchorMax.x, "the body stretches across the card");
        float width = cardW + box.size.x;
        var (fontGuid, fontSize) = TextStyle(prefab, box.goId);
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(fontGuid));
        Assert.IsNotNull(font, $"the body's font (guid {fontGuid})");

        var canvas = new GameObject("Canvas", typeof(Canvas), typeof(RectTransform));
        try
        {
            var textGo = new GameObject("Body", typeof(RectTransform));
            textGo.transform.SetParent(canvas.transform, false);
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.font = font;
            tmp.fontSize = fontSize;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.rectTransform.sizeDelta = new Vector2(width, box.size.y);
            foreach (var (state, text) in new[]
                     {
                         ("Retry", BotDeletionCopy.RetryBody),
                         ("Refused unproven", BotDeletionCopy.RefusedBody(BotServerDeletion.ReasonUnproven)),
                         ("Refused owned_elsewhere", BotDeletionCopy.RefusedBody(BotServerDeletion.ReasonOwnedElsewhere)),
                     })
            {
                tmp.text = text;
                float height = tmp.GetPreferredValues(width, 32767f).y;
                Assert.Greater(height, fontSize, $"{state}: nothing was measured");
                Assert.LessOrEqual(height, box.size.y, $"{state}: «{text}» needs {height}u in a {width}×{box.size.y}u box");
            }
        }
        finally
        {
            Object.DestroyImmediate(canvas);
        }
    }

    /// <summary>The font guid and size of the TMP on the GameObject <paramref name="goId"/>.</summary>
    private static (string guid, float size) TextStyle(string yaml, string goId)
    {
        foreach (var d in Regex.Split(yaml, @"^--- !u!", RegexOptions.Multiline))
        {
            if (!d.StartsWith("114 &") || !d.Contains($"m_GameObject: {{fileID: {goId}}}") || !d.Contains("m_fontAsset:")) continue;
            string guid = Regex.Match(d, @"m_fontAsset: \{fileID: \d+, guid: (\w+)").Groups[1].Value;
            float size = float.Parse(Regex.Match(d, @"m_fontSize: ([\d.]+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            return (guid, size);
        }
        Assert.Fail($"GameObject {goId}: no TextMeshPro component");
        return default;
    }

    private struct RectInfo { public Vector2 pos, size, anchorMin, anchorMax; public string goId; }

    private const string BotSettingsPrefab = "Assets/Prefabs/BotSettings.prefab";
    private const string DeletePopup = "DeleteBotConfirmPopup";

    /// <summary>
    /// The RectTransform at <paramref name="path"/>: a GameObject name that is unique in the prefab, then child names,
    /// each of which must name exactly one child. A bare «parent/child» pair is ambiguous here — six confirm popups in
    /// BotSettings.prefab have a «Content/Body».
    /// </summary>
    private static RectInfo RectAt(string yaml, params string[] path)
    {
        var names = new Dictionary<string, string>();                                 // GameObject fileID → m_Name
        var rects = new Dictionary<string, (string go, string father, string doc)>(); // RectTransform fileID → …
        foreach (var d in Regex.Split(yaml, @"^--- !u!", RegexOptions.Multiline))
        {
            var head = Regex.Match(d, @"^(\d+) &(\d+)");
            if (!head.Success) continue;
            if (head.Groups[1].Value == "1")
                names[head.Groups[2].Value] = Regex.Match(d, @"m_Name: (.*)").Groups[1].Value.Trim();
            else if (head.Groups[1].Value == "224")
                rects[head.Groups[2].Value] = (Regex.Match(d, @"m_GameObject: \{fileID: (\d+)\}").Groups[1].Value,
                                               Regex.Match(d, @"m_Father: \{fileID: (\d+)\}").Groups[1].Value, d);
        }
        string NameOf(string rect) => names.TryGetValue(rects[rect].go, out var n) ? n : null;

        var matches = rects.Keys.Where(r => NameOf(r) == path[0]).ToList();
        Assert.AreEqual(1, matches.Count, $"«{path[0]}» must name exactly one object in the prefab");
        string current = matches[0];
        for (int i = 1; i < path.Length; i++)
        {
            string parent = current;
            matches = rects.Keys.Where(r => rects[r].father == parent && NameOf(r) == path[i]).ToList();
            Assert.AreEqual(1, matches.Count, $"{string.Join("/", path, 0, i + 1)}: expected exactly one");
            current = matches[0];
        }
        string doc = rects[current].doc;
        return new RectInfo
        {
            pos = Vec(doc, "m_AnchoredPosition"), size = Vec(doc, "m_SizeDelta"),
            anchorMin = Vec(doc, "m_AnchorMin"), anchorMax = Vec(doc, "m_AnchorMax"), goId = rects[current].go,
        };
    }

    private static Vector2 Vec(string doc, string key)
    {
        var m = Regex.Match(doc, key + @": \{x: (-?[\d.]+), y: (-?[\d.]+)\}");
        Assert.IsTrue(m.Success, key);
        return new Vector2(float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                           float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
    }
}

using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
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
        string local = MethodBody(Src("Scripts/Main/Bot.cs"), "public void DeleteBotLocally");
        Assert.IsTrue(local.Contains("ProfileRetirement.Clear(transform.name)"), "the local wipe must clear the retired-channel markers");
        string retirement = Src("Scripts/Auth/ProfileRetirement.cs");
        foreach (var suffix in new[] { "WhatsappRetiredProfileId", "WhatsappRetiredReason", "TelegramRetiredProfileId", "TelegramRetiredReason" })
            Assert.IsTrue(retirement.Contains($"\"{suffix}\""), $"marker key suffix {suffix} must be declared in ProfileRetirement");
        Assert.IsTrue(MethodBody(retirement, "public static void Clear").Contains("KeySuffixes"), "Clear deletes every declared marker key");
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
        string prefab = File.ReadAllText("Assets/Prefabs/BotSettings.prefab");
        float cardH = Rect(prefab, "Content", "DeleteBotConfirmPopup").size.y;
        var body = Rect(prefab, "Body", "Content");
        var button = Rect(prefab, "ConfirmButton", "Content");
        float bodyBottomFromTop = -body.pos.y + body.size.y;
        float buttonTopFromTop = cardH - (button.pos.y + button.size.y);
        Assert.GreaterOrEqual(buttonTopFromTop - bodyBottomFromTop, 24f,
            $"DeleteBotConfirmPopup: Body ends at {bodyBottomFromTop}u, buttons start at {buttonTopFromTop}u (card {cardH}u)");
    }

    private struct RectInfo { public Vector2 pos, size; }

    /// <summary>The RectTransform of the GameObject named <paramref name="name"/> whose parent is named <paramref name="parent"/>.</summary>
    private static RectInfo Rect(string yaml, string name, string parent)
    {
        var docs = Regex.Split(yaml, @"^--- !u!", RegexOptions.Multiline);
        string goId = null, rectOfParent = null;
        foreach (var d in docs)
        {
            if (!d.StartsWith("1 &")) continue;
            var nm = Regex.Match(d, @"m_Name: (.*)");
            if (!nm.Success || nm.Groups[1].Value.Trim() != parent) continue;
            string pid = Regex.Match(d, @"^1 &(\d+)").Groups[1].Value;
            foreach (var r in docs)
                if (r.StartsWith("224 &") && r.Contains($"m_GameObject: {{fileID: {pid}}}"))
                    rectOfParent = Regex.Match(r, @"^224 &(\d+)").Groups[1].Value;
            if (rectOfParent == null) continue;
            foreach (var r in docs)
            {
                if (!r.StartsWith("224 &") || !r.Contains($"m_Father: {{fileID: {rectOfParent}}}")) continue;
                string g = Regex.Match(r, @"m_GameObject: \{fileID: (\d+)\}").Groups[1].Value;
                foreach (var gd in docs)
                    if (gd.StartsWith($"1 &{g}") && Regex.Match(gd, @"m_Name: (.*)").Groups[1].Value.Trim() == name)
                    {
                        goId = g;
                        return new RectInfo { pos = Vec(r, "m_AnchoredPosition"), size = Vec(r, "m_SizeDelta") };
                    }
            }
        }
        Assert.Fail($"{parent}/{name} not found in the prefab (goId {goId})");
        return default;
    }

    private static Vector2 Vec(string doc, string key)
    {
        var m = Regex.Match(doc, key + @": \{x: (-?[\d.]+), y: (-?[\d.]+)\}");
        Assert.IsTrue(m.Success, key);
        return new Vector2(float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                           float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
    }
}

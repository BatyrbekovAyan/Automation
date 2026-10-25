using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Newtonsoft.Json;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static SourceGuard;

/// <summary>
/// «Личные и рабочие чаты» (spec §5.5, §5.6, sketch 009 screens 5–6): the open chat.
///   • A personal chat wears the «Личный» pill instead of the «Авто» chip; the pill opens «Сделать чат рабочим?», whose text
///     says what the assistant will do (MakeBusinessCopy) — «Сделать рабочим» moves the chat, «Оставить личным» and Back
///     leave it. A chat that is not a person (a group, a channel, …) can never be working: its pill only says why.
///   • A working chat keeps the chip; a tap on its name or avatar opens «Сделать личным» — the name, the number with
///     «в контактах» / «новый», the action card (MakePersonal + «Чат перенесён в «Личные»») and «Отмена».
/// The sheets and the header are driven for real (the header on a bare ChatManager fed through ParseChatsJson, the way
/// PersonalChatCueTests does); the taps go through the very DelayedFingerUpAction the sheets wired. In EditMode the queue
/// sends nothing, so a move is observed through PersonalChatSheets.Committed, raised right before the queue is called.
/// </summary>
public class PersonalChatSheetsWiringTests
{
    private const string SheetsPath = "Scripts/UI/PersonalChatSheets.cs";
    private const string HeaderPath = "Scripts/Chat/MessageHeaderView.cs";
    private const string TestBot = "PersonalChatSheetsTestBot";
    private const string Profile = "sheets-wa-profile";
    private const string After = "2026-10-21T10:00:00Z";   // after the connection moment
    private static readonly long MomentMs = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    private readonly List<GameObject> hosts = new List<GameObject>();
    private readonly List<(AudienceKind kind, string bot, string profile, string chat)> moves =
        new List<(AudienceKind, string, string, string)>();
    private string dir;
    private Manager previousManager;
    private ChatManager previousChatManager;

    [SetUp]
    public void SetUp()
    {
        PersonalChatSheets.ResetForTests();
        AudienceToast.ResetForTests();
        dir = Path.Combine(Path.GetTempPath(), "PersonalChatSheetsWiringTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        ChatAudienceStore.UseCacheFileForTests(Path.Combine(dir, ChatAudienceStore.CacheFileName));
        ChatAudienceStore.ApplyConnected(Profile, MomentMs);
        previousManager = Manager.Instance;
        previousChatManager = ChatManager.Instance;
        moves.Clear();
        PersonalChatSheets.Committed += RecordMove;
    }

    [TearDown]
    public void TearDown()
    {
        PersonalChatSheets.Committed -= RecordMove;
        PersonalChatSheets.ResetForTests();
        AudienceToast.ResetForTests();
        Manager.Instance = previousManager;
        ChatManager.Instance = previousChatManager;
        foreach (GameObject host in hosts)
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
        hosts.Clear();
        ChatAudienceStore.ResetForTests();
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    private void RecordMove(AudienceKind kind, string bot, string profile, string chat) => moves.Add((kind, bot, profile, chat));

    // ---- the sheets' buttons ----

    [Test]
    public void TheSheetButtons_MoveTheChatThroughTheQueue()
    {
        string source = Read(SheetsPath);
        string business = MethodBody(source, "private static void OnMakeBusiness()");
        StringAssert.Contains("ChatAudienceSyncService.MakeBusiness(", business, "«Сделать рабочим» moves the chat through the queue");
        Assert.IsFalse(business.Contains("MakePersonal("));
        string personal = MethodBody(source, "private static void OnMakePersonal()");
        StringAssert.Contains("ChatAudienceSyncService.MakePersonal(", personal, "«Сделать личным» moves the chat through the queue");
        StringAssert.Contains("AudienceToast.Show(PersonalChatsCopy.MovedToPersonal)", personal, "…and the toast says so");
        Assert.IsFalse(personal.Contains("MakeBusiness("));
        foreach (string dismiss in new[] { MethodBody(source, "private static void KeepPersonal()"), MethodBody(source, "private static void CancelMakePersonal()") })
        {
            Assert.IsFalse(dismiss.Contains("ChatAudienceSyncService"), "«Оставить личным» / «Отмена» move nothing");
            Assert.IsFalse(dismiss.Contains("Committed"));
        }
    }

    // ---- «Сделать чат рабочим?» ----

    [TestCase(true, false, 2.0, PersonalChatsCopy.MakeBusinessAutoFresh, true)]
    [TestCase(true, false, 30.0, PersonalChatsCopy.MakeBusinessAutoNoFresh, true)]
    [TestCase(true, true, 2.0, PersonalChatsCopy.MakeBusinessAutoNoFresh, false)]
    [TestCase(false, false, 2.0, PersonalChatsCopy.MakeBusinessManual, false)]
    public void MakeBusiness_SaysWhatTheAssistantWillDo(bool autoOn, bool ownerLast, double ageHours, string body, bool note)
    {
        ChatViewModel chat = Chat("77000000301@c.us", "Сауле", ageHours, ownerLast);
        PersonalChatSheets.AskMakeBusiness(TestBot, Profile, chat, autoOn, null, null);

        Assert.IsTrue(PersonalChatSheets.IsShowing);
        Transform panel = PersonalChatSheets.MakeBusinessPanelForTests.transform;
        Assert.AreEqual(PersonalChatsCopy.MakeBusinessTitle, TextOf(panel, "Title"));
        Assert.AreEqual(body, TextOf(panel, "Body"));
        Transform noteNode = Find(panel, "Note");
        Assert.AreEqual(note, noteNode.gameObject.activeSelf, "the note shows only when a waiting message is skipped or answered");
        if (note) Assert.AreEqual(PersonalChatsCopy.MakeBusinessStaleNote, noteNode.GetComponent<TextMeshProUGUI>().text);
        Assert.AreEqual(PersonalChatsCopy.MakeBusinessYes, TextOf(Find(panel, "Primary"), "Label"));
        Assert.AreEqual(PersonalChatsCopy.MakeBusinessNo, TextOf(Find(panel, "Secondary"), "Label"));
    }

    [Test]
    public void MakeBusiness_Yes_MovesTheChat_AndCloses()
    {
        ChatViewModel chat = Chat("77000000302@c.us", "Сауле", 1, ownerLast: false);
        PersonalChatSheets.AskMakeBusiness(TestBot, Profile, chat, true, null, null);
        Tap(Find(PersonalChatSheets.MakeBusinessPanelForTests.transform, "Primary"));

        CollectionAssert.AreEqual(new[] { (AudienceKind.Business, TestBot, Profile, chat.ChatId) }, moves,
            "the chat the sheet was opened for, under its bot and profile");
        Assert.IsFalse(PersonalChatSheets.IsShowing);
        Tap(Find(PersonalChatSheets.MakeBusinessPanelForTests.transform, "Primary"));
        Assert.AreEqual(1, moves.Count, "a second tap on a leaving sheet moves nothing");
    }

    [Test]
    public void MakeBusiness_KeepPersonal_AndTheScrim_MoveNothing()
    {
        ChatViewModel chat = Chat("77000000303@c.us", "Сауле", 1, ownerLast: false);
        PersonalChatSheets.AskMakeBusiness(TestBot, Profile, chat, true, null, null);
        Tap(Find(PersonalChatSheets.MakeBusinessPanelForTests.transform, "Secondary"));
        Assert.IsFalse(PersonalChatSheets.IsShowing, "«Оставить личным» closes the sheet");

        PersonalChatSheets.AskMakeBusiness(TestBot, Profile, chat, true, null, null);
        Tap(Find(PersonalChatSheets.MakeBusinessPanelForTests.transform, "Scrim"));
        Assert.IsFalse(PersonalChatSheets.IsShowing, "a tap outside the sheet closes it too");
        Assert.IsEmpty(moves, "the chat stays personal");
    }

    // ---- «Сделать личным» ----

    [Test]
    public void MakePersonal_ShowsTheChat_AndWhatTheMoveMeans()
    {
        ChatViewModel chat = Chat("77000000304@c.us", "Асем клиент", 1, ownerLast: false);
        PersonalChatSheets.AskMakePersonal(TestBot, Profile, chat, "77000000304 · в контактах", null, null);

        Assert.IsTrue(PersonalChatSheets.IsShowing);
        Transform panel = PersonalChatSheets.MakePersonalPanelForTests.transform;
        Assert.AreEqual(UnicodeEmojiConverter.ConvertRealEmojisToSprites("Асем клиент", MissingEmojiMode.Hide), TextOf(panel, "Name"),
            "the name as the header shows it");
        Assert.AreEqual("77000000304 · в контактах", TextOf(panel, "Number"));
        Assert.AreEqual(PersonalChatsCopy.MakePersonalAction, TextOf(panel, "ActionTitle"));
        Assert.AreEqual(PersonalChatsCopy.MakePersonalBody(ChatAudienceRule.WipeDays), TextOf(panel, "ActionBody"),
            "«через 2 дня» is the server's wipe delay");
        Assert.AreEqual(PersonalChatsCopy.Cancel, TextOf(Find(panel, "Secondary"), "Label"));

        PersonalChatSheets.TryCloseTop();
        PersonalChatSheets.AskMakePersonal(TestBot, Profile, chat, "", null, null);
        Assert.IsFalse(Find(panel, "Number").gameObject.activeSelf, "no number and no mark — no empty line");
    }

    [Test]
    public void MakePersonal_TheActionCard_MovesTheChat_AndSaysSo()
    {
        ChatViewModel chat = Chat("77000000305@c.us", "Асем клиент", 1, ownerLast: false);
        PersonalChatSheets.AskMakePersonal(TestBot, Profile, chat, "77000000305 · новый", null, null);
        Tap(Find(PersonalChatSheets.MakePersonalPanelForTests.transform, "ActionCard"));

        CollectionAssert.AreEqual(new[] { (AudienceKind.Personal, TestBot, Profile, chat.ChatId) }, moves);
        Assert.AreEqual(PersonalChatsCopy.MovedToPersonal, AudienceToast.LastText, "«Чат перенесён в «Личные»»");
        Assert.IsFalse(PersonalChatSheets.IsShowing);
    }

    [Test]
    public void MakePersonal_Cancel_MovesNothing()
    {
        ChatViewModel chat = Chat("77000000306@c.us", "Асем клиент", 1, ownerLast: false);
        PersonalChatSheets.AskMakePersonal(TestBot, Profile, chat, "77000000306", null, null);
        Tap(Find(PersonalChatSheets.MakePersonalPanelForTests.transform, "Secondary"));
        Assert.IsFalse(PersonalChatSheets.IsShowing);
        Assert.IsEmpty(moves);
        Assert.IsNull(AudienceToast.LastText, "nothing moved, nothing to tell");
    }

    [Test]
    public void TheTwoSheets_NeverStack_AndBackClosesTheOneUp()
    {
        ChatViewModel chat = Chat("77000000307@c.us", "Сауле", 1, ownerLast: false);
        PersonalChatSheets.AskMakePersonal(TestBot, Profile, chat, "", null, null);
        PersonalChatSheets.AskMakeBusiness(TestBot, Profile, chat, false, null, null);
        Assert.IsTrue(PersonalChatSheets.TryCloseTop(), "Back closes the sheet that is up");
        Assert.IsFalse(PersonalChatSheets.IsShowing, "…and nothing is left under it: asking one sheet sent the other away");
        Assert.IsFalse(PersonalChatSheets.TryCloseTop(), "nothing left to close");
        Assert.IsEmpty(moves);
    }

    // ---- the number line ----

    [Test]
    public void NumberLine_IsTheHeadersNumber_WithItsMark()
    {
        ChatViewModel contact = Chat("77011234567@c.us", "Асем клиент", 1, false, contact: true);
        Assert.AreEqual("77011234567 · в контактах",
            PersonalChatSheets.NumberLine(contact, View(AudienceKind.Business, AudienceReason.Owner)), "a contact the owner made working");

        ChatViewModel stranger = Chat("77011234568@c.us", "Ерлан", 1, false, contact: false);
        Assert.AreEqual("77011234568 · новый", PersonalChatSheets.NumberLine(stranger, View(AudienceKind.Business, AudienceReason.New)));
        Assert.AreEqual("77011234568", PersonalChatSheets.NumberLine(stranger, View(AudienceKind.Business, AudienceReason.Owner)),
            "a chat the owner made working is neither");

        // A chat without a name is titled with its number — as ParseChatsJson titles it, through the emoji converter.
        string fallbackTitle = UnicodeEmojiConverter.ConvertRealEmojisToSprites(ChatIdFormat.DisplayFallback("77011234569@c.us"));
        ChatViewModel nameless = Chat("77011234569@c.us", fallbackTitle, 1, false, contact: false);
        Assert.AreEqual(PersonalChatsCopy.NewMark, PersonalChatSheets.NumberLine(nameless, View(AudienceKind.Business, AudienceReason.New)),
            "the name line already is the number");
    }

    [Test]
    public void NumberLine_HasNoNumber_ForAnIdThatIsNoPhone()
    {
        var telegram = new ChatViewModel("123456789", "Ерлан", null, "привет", 1, facts:
            new AudienceFacts(true, "123456789", "user", false, false, false, false));
        Assert.AreEqual(PersonalChatsCopy.NewMark, PersonalChatSheets.NumberLine(telegram, View(AudienceKind.Business, AudienceReason.New)),
            "a Telegram id is no phone number");

        var lid = new ChatViewModel("123456789012345@lid", "Ерлан", null, "привет", 1, facts:
            new AudienceFacts(false, "123456789012345@lid", "", false, false, false, false));
        Assert.AreEqual(string.Empty, PersonalChatSheets.NumberLine(lid, View(AudienceKind.Business, AudienceReason.Owner)),
            "a WhatsApp @lid hides the number");
        Assert.AreEqual(string.Empty, PersonalChatSheets.NumberLine(null, null));
    }

    // ---- the sheets fit their copy (the real fonts the header hands in) ----

    [TestCase(true, false, 1.0)]
    [TestCase(true, false, 30.0)]
    [TestCase(false, false, 1.0)]
    public void MakeBusiness_TheTextNeverRunsIntoTheButtons(bool autoOn, bool ownerLast, double ageHours)
    {
        (TMP_FontAsset regular, TMP_FontAsset semibold) = Fonts();
        PersonalChatSheets.AskMakeBusiness(TestBot, Profile, Chat("77000000308@c.us", "Сауле", ageHours, ownerLast), autoOn, regular, semibold);

        Transform panel = PersonalChatSheets.MakeBusinessPanelForTests.transform;
        var sheet = (RectTransform)Find(panel, "SheetRoot");
        (float top, float height) title = BoxIn(sheet, Find(panel, "Title"));
        (float top, float height) body = BoxIn(sheet, Find(panel, "Body"));
        (float top, float height) primary = BoxIn(sheet, Find(panel, "Primary"));
        (float top, float height) secondary = BoxIn(sheet, Find(panel, "Secondary"));

        Assert.AreEqual(PersonalChatSheets.BusinessLayout.TitleTop, title.top, 0.01f);
        Assert.LessOrEqual(title.top + title.height, body.top + 0.01f, "the body starts below every line of the title");
        AssertHolds(Find(panel, "Body"), body.height);
        float textBottom = body.top + body.height;
        Transform noteNode = Find(panel, "Note");
        if (noteNode.gameObject.activeSelf)
        {
            (float top, float height) note = BoxIn(sheet, noteNode);
            Assert.AreEqual(PersonalChatSheets.BusinessLayout.NoteGap, note.top - textBottom, 0.5f, "the note sits right under the body");
            AssertHolds(noteNode, note.height);
            textBottom = note.top + note.height;
        }
        Assert.AreEqual(PersonalChatSheets.BusinessLayout.BodyToPrimary, primary.top - textBottom, 0.5f,
            "the authored clearance above «Сделать рабочим» survives whatever the text did");
        Assert.LessOrEqual(primary.top + primary.height, secondary.top + 0.01f, "«Оставить личным» under the button");
        Assert.AreEqual(PersonalChatSheets.SheetWidth, sheet.rect.width, 0.01f);
        Assert.GreaterOrEqual(sheet.rect.height, PersonalChatSheets.BusinessLayout.SheetHeight - 0.01f, "the sheet only ever grows");
        TestContext.Out.WriteLine($"[FIT] make-business auto={autoOn} age={ageHours}: sheet {sheet.rect.height:F1}u");
    }

    [Test]
    public void MakePersonal_TheCardHoldsItsText_AndClearsCancel()
    {
        (TMP_FontAsset regular, TMP_FontAsset semibold) = Fonts();
        PersonalChatSheets.AskMakePersonal(TestBot, Profile, Chat("77000000309@c.us", "Асем клиент", 1, false),
            "77000000309 · в контактах", regular, semibold);

        Transform panel = PersonalChatSheets.MakePersonalPanelForTests.transform;
        var sheet = (RectTransform)Find(panel, "SheetRoot");
        (float top, float height) number = BoxIn(sheet, Find(panel, "Number"));
        (float top, float height) card = BoxIn(sheet, Find(panel, "ActionCard"));
        (float top, float height) title = BoxIn(sheet, Find(panel, "ActionTitle"));
        (float top, float height) body = BoxIn(sheet, Find(panel, "ActionBody"));
        (float top, float height) cancel = BoxIn(sheet, Find(panel, "Secondary"));

        Assert.LessOrEqual(number.top + number.height, card.top + 0.01f, "the card starts below the number line");
        Assert.LessOrEqual(title.top + title.height, body.top + 0.01f);
        AssertHolds(Find(panel, "ActionBody"), body.height);
        Assert.AreEqual(PersonalChatSheets.PersonalLayout.CardPadding, card.top + card.height - (body.top + body.height), 0.5f,
            "the card grows with its text — the padding under the text survives");
        Assert.AreEqual(PersonalChatSheets.PersonalLayout.CardToSecondary, cancel.top - (card.top + card.height), 0.5f,
            "the authored clearance above «Отмена» survives");
        TestContext.Out.WriteLine($"[FIT] make-personal: sheet {sheet.rect.height:F1}u, card {card.height:F1}u");
    }

    // ---- the header ----

    [Test]
    public void APersonalChat_WearsThePill_AWorkingChat_TheChip()
    {
        const string friend = "77000000401@c.us";
        const string client = "77000000402@c.us";
        Header header = NewOpenChatHeader(Row(friend, "Сауле", contact: "Сауле"), Row(client, "Ерлан", contact: ""));

        header.Select(friend);
        Assert.IsTrue(header.Pill.gameObject.activeSelf, "a saved contact is personal: «Личный»");
        Assert.IsFalse(header.Chip.gameObject.activeSelf, "…instead of «Авто»");

        header.Select(client);
        Assert.IsFalse(header.Pill.gameObject.activeSelf, "a new person is a working chat");
        Assert.IsTrue(header.Chip.gameObject.activeSelf, "…with the «Авто» chip as today");
    }

    [Test]
    public void AMove_RepaintsTheOpenChatsHeaderAtOnce()
    {
        const string client = "77000000403@c.us";
        Header header = NewOpenChatHeader(Row(client, "Ерлан", contact: ""));
        header.Select(client);
        Assert.IsTrue(header.Chip.gameObject.activeSelf);

        ChatAudienceStore.ApplyDesire(Profile, client, AudienceKind.Personal);
        Assert.IsTrue(header.Pill.gameObject.activeSelf, "«Сделать личным» — the pill appears while the chat is still open");
        Assert.IsFalse(header.Chip.gameObject.activeSelf);

        ChatAudienceStore.ApplyDesire(Profile, client, AudienceKind.Business);
        Assert.IsFalse(header.Pill.gameObject.activeSelf, "«Сделать рабочим» — the chip comes back");
        Assert.IsTrue(header.Chip.gameObject.activeSelf);
    }

    [Test]
    public void ThePill_OpensMakeBusiness_WithTheChatsText()
    {
        const string friend = "77000000404@c.us";
        Header header = NewOpenChatHeader(Row(friend, "Сауле", contact: "Сауле"));
        header.Select(friend);

        header.Pill.onClick.Invoke();

        Assert.IsTrue(PersonalChatSheets.IsShowing, "the pill is a button: «Сделать чат рабочим?»");
        Assert.IsNotNull(PersonalChatSheets.MakeBusinessPanelForTests);
        Assert.AreEqual(PersonalChatsCopy.MakeBusinessManual, TextOf(PersonalChatSheets.MakeBusinessPanelForTests.transform, "Body"),
            "no suggestions controller wired in this harness — the mode reads as not «Авто»");
        Tap(Find(PersonalChatSheets.MakeBusinessPanelForTests.transform, "Primary"));
        CollectionAssert.AreEqual(new[] { (AudienceKind.Business, TestBot, Profile, friend) }, moves);
    }

    [Test]
    public void TheName_OpensMakePersonal_OnlyInAWorkingChat()
    {
        const string friend = "77000000405@c.us";
        const string client = "77000000406@c.us";
        Header header = NewOpenChatHeader(Row(friend, "Сауле", contact: "Сауле"), Row(client, "Ерлан", contact: ""));

        header.Select(friend);
        header.NameTap.onClick.Invoke();
        Assert.IsFalse(PersonalChatSheets.IsShowing, "a personal chat's name opens nothing — its pill does the move");

        header.Select(client);
        header.NameTap.onClick.Invoke();
        Assert.IsTrue(PersonalChatSheets.IsShowing, "a working chat's name: «Сделать личным»");
        Transform panel = PersonalChatSheets.MakePersonalPanelForTests.transform;
        Assert.AreEqual(header.View.nameText.text, TextOf(panel, "Name"), "the name exactly as the header shows it");
        StringAssert.EndsWith("Ерлан", TextOf(panel, "Name"));
        Assert.AreEqual("77000000406 · новый", TextOf(panel, "Number"), "the number as the header shows it, and «новый»");
        Tap(Find(panel, "ActionCard"));
        CollectionAssert.AreEqual(new[] { (AudienceKind.Personal, TestBot, Profile, client) }, moves);
    }

    [Test]
    public void ANonPersonsPill_SaysWhyItCannotBeWorking()
    {
        const string group = "120363000000000001@g.us";
        Header header = NewOpenChatHeader(Row(group, "Семья", contact: ""));
        header.Select(group);
        Assert.IsTrue(header.Pill.gameObject.activeSelf, "a group is personal");

        header.Pill.onClick.Invoke();
        Assert.IsFalse(PersonalChatSheets.IsShowing, "a group can never be made working — no sheet offers it");
        Assert.AreEqual(PersonalChatsCopy.NonHumanGroup, AudienceToast.LastText, "the pill says why instead");
        Assert.IsEmpty(moves);
    }

    [Test]
    public void TheHeader_AsksTheModeWithoutTheAudience_AndTheChatsLastMessage()
    {
        string pill = MethodBody(Read(HeaderPath), "private void HandlePersonalPillTapped()");
        StringAssert.Contains("!suggestions.WouldSuggestIgnoringAudience()", pill,
            "«Авто» in this chat = the panel would not suggest there (its stored mode, the schedule, the quota)");
        StringAssert.Contains("PersonalChatSheets.AskMakeBusiness(", pill);
        string ask = MethodBody(Read(SheetsPath), "public static void AskMakeBusiness(");
        StringAssert.Contains("!chat.IsLastMessageMine", ask, "the client's message last…");
        StringAssert.Contains("MakeBusinessCopy.AgeHours(chat.LastMessageTime", ask, "…and how old it is");
        StringAssert.Contains("MakeBusinessCopy.For(", ask);
        string name = MethodBody(Read(HeaderPath), "private void HandleNameTapped()");
        StringAssert.Contains("PersonalChatSheets.AskMakePersonal(", name);
        StringAssert.Contains("PersonalChatSheets.NumberLine(chat, view)", name, "the line under the name in the sheet");
    }

    // ---- harness: the sheets ----

    private static ChatViewModel Chat(string id, string title, double ageHours, bool ownerLast, bool contact = false)
    {
        long time = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (long)(ageHours * 3600);
        var facts = new AudienceFacts(false, id, "", false, false, false, contact);
        return new ChatViewModel(id, title, null, "Здравствуйте, есть красные розы на завтра?", time, isLastMessageMine: ownerLast, facts: facts);
    }

    private static AudienceView View(AudienceKind kind, AudienceReason reason) =>
        new AudienceView { Kind = kind, Reason = reason, NonHuman = NonHumanKind.None, Decided = true };

    private static (TMP_FontAsset regular, TMP_FontAsset semibold) Fonts()
    {
        // Medium/Bold are left out on purpose: their font materials carry stale TMP shader ratios in the repo, and measuring
        // text with them makes the Editor rewrite those 80MB assets.
        var regular = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Fonts/SFProText-Regular SDF.asset");
        var semibold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Fonts/SFProText-Semibold SDF.asset");
        Assert.IsNotNull(regular, "SF Pro Regular moved");
        Assert.IsNotNull(semibold, "SF Pro Semibold moved");
        return (regular, semibold);
    }

    private static void AssertHolds(Transform text, float boxHeight)
    {
        var tmp = text.GetComponent<TextMeshProUGUI>();
        float needed = tmp.GetPreferredValues(tmp.rectTransform.rect.width, 32767f).y;
        Assert.Greater(needed, 0f, $"{text.name} was measured");
        Assert.GreaterOrEqual(boxHeight, needed - 0.5f, $"{text.name}: the box holds every line the text needs");
    }

    private static Transform Find(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform hit = Find(root.GetChild(i), name);
            if (hit != null) return hit;
        }
        return null;
    }

    private static string TextOf(Transform root, string name)
    {
        Transform hit = Find(root, name);
        Assert.IsNotNull(hit, $"{name} is built");
        return hit.GetComponent<TextMeshProUGUI>().text;
    }

    private static void Tap(Transform target)
    {
        Assert.IsNotNull(target, "tap target");
        var handler = target.GetComponent<DelayedFingerUpAction>();
        Assert.IsNotNull(handler, $"{target.name} is wired through PopupUI.WireFingerUp");
        FieldInfo field = typeof(DelayedFingerUpAction).GetField("OnRealRelease", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(field, "DelayedFingerUpAction.OnRealRelease backing field");
        ((Action)field.GetValue(handler))?.Invoke();
    }

    /// <summary>Distance of the element's top edge from the sheet's top edge, and its height — in the sheet's units.</summary>
    private static (float top, float height) BoxIn(RectTransform sheet, Transform element)
    {
        var corners = new Vector3[4];   // 0 bottom-left, 1 top-left, 2 top-right, 3 bottom-right
        ((RectTransform)element).GetWorldCorners(corners);
        float top = sheet.rect.yMax - sheet.InverseTransformPoint(corners[1]).y;
        float bottom = sheet.rect.yMax - sheet.InverseTransformPoint(corners[0]).y;
        return (top, bottom - top);
    }

    // ---- harness: the header over a bare ChatManager ----

    private sealed class Header
    {
        public MessageHeaderView View;
        public SemiAutoToggle Chip;
        public Button Pill;
        public Button NameTap;

        public void Select(string chatId) => Invoke(View, "HandleChatSelected", chatId);
    }

    private Header NewOpenChatHeader(params object[] rows)
    {
        Manager.Instance = NewManagerWithBot();
        ChatManager chats = NewChatManager();
        ChatManager.Instance = chats;
        Parse(chats, JsonConvert.SerializeObject(new { status = "done", dialogs = rows }));

        GameObject top = NewHost("TopBar");
        var header = top.AddComponent<MessageHeaderView>();
        header.nameText = NewChild(top, "Name").AddComponent<TextMeshProUGUI>();
        header.statusText = NewChild(top, "Status").AddComponent<TextMeshProUGUI>();
        header.avatarImage = NewChild(top, "Avatar").AddComponent<Image>();
        header.defaultAvatarBg = NewChild(top, "Default").AddComponent<Image>();
        header.defaultAvatarSilhouette = NewChild(header.defaultAvatarBg.gameObject, "DefaultImage").AddComponent<Image>();
        var chip = NewChild(top, "SemiAutoToggle").AddComponent<SemiAutoToggle>();
        GameObject pillGo = NewChild(top, "PersonalPill");
        pillGo.AddComponent<Image>();
        var pill = pillGo.AddComponent<Button>();
        pillGo.SetActive(false);
        GameObject tapGo = NewChild(top, "NameTap");
        tapGo.AddComponent<Image>();
        var nameTap = tapGo.AddComponent<Button>();

        var refs = new SerializedObject(header);
        SetRef(refs, "semiAutoToggle", chip);
        SetRef(refs, "personalPill", pill);
        SetRef(refs, "nameTap", nameTap);
        refs.ApplyModifiedPropertiesWithoutUndo();
        Invoke(header, "Awake");   // EditMode runs no Awake: the header subscribes and wires its buttons there
        return new Header { View = header, Chip = chip, Pill = pill, NameTap = nameTap };
    }

    private static void SetRef(SerializedObject owner, string field, UnityEngine.Object value)
    {
        SerializedProperty property = owner.FindProperty(field);
        Assert.IsNotNull(property, $"MessageHeaderView.{field} is a serialized field");
        property.objectReferenceValue = value;
    }

    // A WhatsApp chats/filter row: a saved contact carries the address book's name, a stranger an empty one.
    private static object Row(string id, string name, string contact) => new
    {
        id,
        name,
        contact = new { Found = true, FullName = contact },
        isGroup = id.EndsWith("@g.us", StringComparison.Ordinal),
        unread_count = 0,
        last_message_id = "M-" + id,
        last_message_data = "Здравствуйте",
        last_timestamp = After,
        last_message_sender = new { isMe = false, pushname = "" },
    };

    private Manager NewManagerWithBot()
    {
        var manager = NewHost("ManagerHost").AddComponent<Manager>();
        GameObject botsParent = NewHost("BotsParent");
        var botGo = new GameObject(TestBot);
        botGo.transform.SetParent(botsParent.transform);
        botGo.AddComponent<Bot>().whatsappProfileId = Profile;
        FieldInfo field = typeof(Manager).GetField("BotsParent", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "Manager.BotsParent moved — update PersonalChatSheetsWiringTests");
        field.SetValue(manager, botsParent);
        return manager;
    }

    private ChatManager NewChatManager()
    {
        var manager = NewHost("ChatManagerHost").AddComponent<ChatManager>();
        typeof(ChatManager).GetProperty(nameof(ChatManager.ActiveChannel))?.SetValue(manager, ChatChannel.WhatsApp);
        typeof(ChatManager).GetProperty(nameof(ChatManager.CurrentBotId))?.SetValue(manager, TestBot);
        return manager;
    }

    private static void Parse(ChatManager manager, string json)
    {
        MethodInfo parse = typeof(ChatManager).GetMethod("ParseChatsJson", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(parse, "ChatManager.ParseChatsJson moved — update PersonalChatSheetsWiringTests");
        Call(() => parse.Invoke(manager, new object[] { json, true }));
    }

    private static void Invoke(object target, string method, params object[] args)
    {
        MethodInfo info = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(info, $"{target.GetType().Name}.{method} moved — update PersonalChatSheetsWiringTests");
        Call(() => info.Invoke(target, args));
    }

    private static void Call(Action call)
    {
        try
        {
            call();
        }
        catch (TargetInvocationException e)
        {
            ExceptionDispatchInfo.Capture(e.InnerException).Throw();
        }
    }

    private GameObject NewHost(string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        hosts.Add(go);
        return go;
    }

    private static GameObject NewChild(GameObject parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        return go;
    }
}

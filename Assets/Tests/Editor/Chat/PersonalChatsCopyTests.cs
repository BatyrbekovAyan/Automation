using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

/// <summary>
/// «Личные и рабочие чаты» (spec §5.9): every string of the feature lives in <see cref="PersonalChatsCopy"/>, word for word
/// as the plan's table gives it (the owner reads them before the release). Two texts state a number the server keeps:
/// «через 2 дня» is <see cref="ChatAudienceRule.WipeDays"/> and «старше суток» is <see cref="ChatAudienceRule.FreshHours"/> —
/// a change of either constant must reach the text. The glyphs are checked against the SF Pro atlases by
/// FontGlyphCoverageTests; the word «Выбрать» is not shown on these screens.
/// </summary>
public class PersonalChatsCopyTests
{
    [Test]
    public void MakePersonalBody_SaysTwoDays()
    {
        Assert.AreEqual(2, ChatAudienceRule.WipeDays, "the server wipes a personal chat's data after two days");
        string body = PersonalChatsCopy.MakePersonalBody(ChatAudienceRule.WipeDays);
        Assert.AreEqual("ИИ-сотрудник перестанет участвовать в этом чате. Через 2 дня он забудет эту переписку.", body);
        StringAssert.Contains("Через 2 дня", body);
    }

    [TestCase(1, "Через 1 день")]
    [TestCase(3, "Через 3 дня")]
    [TestCase(5, "Через 5 дней")]
    [TestCase(11, "Через 11 дней")]
    [TestCase(21, "Через 21 день")]
    public void MakePersonalBody_AgreesTheDays(int days, string phrase)
    {
        StringAssert.Contains(phrase + " он забудет эту переписку.", PersonalChatsCopy.MakePersonalBody(days));
    }

    [TestCase(2, "2 чата стали рабочими")]
    [TestCase(4, "4 чата стали рабочими")]
    [TestCase(5, "5 чатов стали рабочими")]
    [TestCase(11, "11 чатов стали рабочими")]
    [TestCase(21, "21 чат стал рабочим")]
    [TestCase(22, "22 чата стали рабочими")]
    public void MovedToBusinessMany_AgreesTheCount(int count, string text)
    {
        Assert.AreEqual(text, PersonalChatsCopy.MovedToBusinessMany(count));
    }

    [Test]
    public void StaleNote_MatchesFreshHours()
    {
        Assert.AreEqual(24, ChatAudienceRule.FreshHours,
            "the note says «старше суток»: a new freshness threshold must change the text in the same edit");
        Assert.AreEqual("На сообщения старше суток он не отвечает — начнёт с нового.", PersonalChatsCopy.MakeBusinessStaleNote);
        StringAssert.Contains("суток", PersonalChatsCopy.MakeBusinessStaleNote);
    }

    [Test]
    public void TheListAndTheFolder_WordForWord()
    {
        Assert.AreEqual("Личные", PersonalChatsCopy.RowTitle);
        Assert.AreEqual("Личные", PersonalChatsCopy.FolderTitle);
        Assert.AreEqual("Изменить", PersonalChatsCopy.Edit);
        Assert.AreEqual("Отмена", PersonalChatsCopy.Cancel);
        Assert.AreEqual("Контакты из телефона, все, кто писал до подключения, группы и каналы. ИИ-сотрудник здесь не участвует, уведомлений нет.",
            PersonalChatsCopy.FolderHint);
        Assert.AreEqual("Свайп по чату — сделать его рабочим", PersonalChatsCopy.FolderFooter);
        Assert.AreEqual("Отметьте клиентов", PersonalChatsCopy.EditTitle);
        Assert.AreEqual("Поиск по личным", PersonalChatsCopy.EditSearch);
        Assert.AreEqual("Сделать рабочими · 2", PersonalChatsCopy.MakeBusinessMany(2));
        Assert.AreEqual("Отметьте хотя бы один чат", PersonalChatsCopy.SelectAtLeastOne);
    }

    [Test]
    public void TheOnboardingScreen_WordForWord()
    {
        Assert.AreEqual("WhatsApp подключён", PersonalChatsCopy.OnboardingConnected(telegram: false));
        Assert.AreEqual("Telegram подключён", PersonalChatsCopy.OnboardingConnected(telegram: true));
        Assert.AreEqual("Кто из них клиенты?", PersonalChatsCopy.OnboardingTitle);
        Assert.AreEqual("Все ваши контакты и все, с кем вы переписывались до подключения, — личные: ИИ-сотрудник их не трогает. Отметьте клиентов, с ними он будет работать.",
            PersonalChatsCopy.OnboardingBody);
        Assert.AreEqual("Поиск по чатам", PersonalChatsCopy.OnboardingSearch);
        Assert.AreEqual("НЕДАВНИЕ ЧАТЫ", PersonalChatsCopy.OnboardingSection);
        Assert.AreEqual("в контактах", PersonalChatsCopy.SublineContact);
        Assert.AreEqual("не в контактах, писал раньше", PersonalChatsCopy.SublineHistory);
        Assert.AreEqual("Готово · клиентов: 0", PersonalChatsCopy.OnboardingDone(0));
        Assert.AreEqual("Готово · клиентов: 3", PersonalChatsCopy.OnboardingDone(3));
        Assert.AreEqual("Отмечу позже", PersonalChatsCopy.OnboardingLater);
        Assert.AreEqual("Загружаем чаты из WhatsApp…", PersonalChatsCopy.OnboardingLoading(telegram: false));
        Assert.AreEqual("Загружаем чаты из Telegram…", PersonalChatsCopy.OnboardingLoading(telegram: true));
        Assert.AreEqual("WhatsApp на телефоне, похоже, не видит контакты. Клиентов можно отметить вручную.",
            PersonalChatsCopy.OnboardingNoContacts(telegram: false));
        Assert.AreEqual("Telegram на телефоне, похоже, не видит контакты. Клиентов можно отметить вручную.",
            PersonalChatsCopy.OnboardingNoContacts(telegram: true));
    }

    [Test]
    public void TheNonPersons_WordForWord()
    {
        Assert.AreEqual("Группа · ИИ-сотрудник в группах не отвечает", PersonalChatsCopy.NonHumanGroup);
        Assert.AreEqual("Канал · ИИ-сотрудник каналам не отвечает", PersonalChatsCopy.NonHumanChannel);
        Assert.AreEqual("Бот · ИИ-сотрудник ботам не отвечает", PersonalChatsCopy.NonHumanBot);
        Assert.AreEqual("Служебные уведомления · ИИ-сотрудник им не отвечает", PersonalChatsCopy.NonHumanService);
        Assert.AreEqual("Избранное · сообщения самому себе", PersonalChatsCopy.NonHumanSelf);
    }

    [Test]
    public void TheSwipesAndToasts_WordForWord()
    {
        Assert.AreEqual("В личные", PersonalChatsCopy.SwipeToPersonal);
        Assert.AreEqual("В рабочие", PersonalChatsCopy.SwipeToBusiness);
        Assert.AreEqual("Удалить", PersonalChatsCopy.SwipeDelete);
        Assert.AreEqual("Чат перенесён в «Личные»", PersonalChatsCopy.MovedToPersonal);
        Assert.AreEqual("Чат стал рабочим", PersonalChatsCopy.MovedToBusiness);
        Assert.AreEqual("Не удалось перенести чат", PersonalChatsCopy.WriteFailed,
            "a server refusal — a network fault is retried, never told, so the text names no connection");
        Assert.AreEqual("Сначала сохраните изменения", PersonalChatsCopy.SaveFirst);
    }

    [Test]
    public void TheOpenChat_WordForWord()
    {
        Assert.AreEqual("Личный", PersonalChatsCopy.Pill);
        Assert.AreEqual("Сделать чат рабочим?", PersonalChatsCopy.MakeBusinessTitle);
        Assert.AreEqual("Сделать рабочим", PersonalChatsCopy.MakeBusinessYes);
        Assert.AreEqual("Оставить личным", PersonalChatsCopy.MakeBusinessNo);
        Assert.AreEqual("У вас включено «Авто»: ИИ-сотрудник сразу ответит на последнее сообщение и дальше будет отвечать сам. Выключить «Авто» в этом чате можно отдельно.",
            PersonalChatsCopy.MakeBusinessAutoFresh);
        Assert.AreEqual("У вас включено «Авто»: ИИ-сотрудник будет отвечать сам на новые сообщения. Выключить «Авто» в этом чате можно отдельно.",
            PersonalChatsCopy.MakeBusinessAutoNoFresh);
        Assert.AreEqual("ИИ-сотрудник будет подсказывать ответы, а отправлять их будете вы.", PersonalChatsCopy.MakeBusinessManual);
        Assert.AreEqual("Сделать личным", PersonalChatsCopy.MakePersonalAction);
        Assert.AreEqual("в контактах", PersonalChatsCopy.ContactMark);
        Assert.AreEqual("новый", PersonalChatsCopy.NewMark);
    }

    [Test]
    public void TheSettingsSection_WordForWord()
    {
        Assert.AreEqual("ЛИЧНЫЕ ЧАТЫ", PersonalChatsCopy.SettingsHeader);
        Assert.AreEqual("Личные чаты", PersonalChatsCopy.SettingsRow);
        Assert.AreEqual("Контакты из телефона и все, кто писал до подключения. ИИ-сотрудник в них не участвует. Внутри можно отметить клиентов — с ними он будет работать. Ботам, каналам и рассылкам он не отвечает никогда.",
            PersonalChatsCopy.SettingsHint);
    }

    [Test]
    public void NoText_ShowsTheWordВыбрать()
    {
        foreach (var (name, text) in AllTexts())
            StringAssert.DoesNotContain("Выбрать", text, $"{name}: the mode shows only as the «Авто» capsule on these screens (spec §0, decision 2)");
    }

    [Test]
    public void NoText_IsEmpty()
    {
        foreach (var (name, text) in AllTexts())
            Assert.IsFalse(string.IsNullOrWhiteSpace(text), $"{name} is empty");
    }

    // Every constant and a sample of every composed text.
    private static IEnumerable<(string name, string text)> AllTexts()
    {
        FieldInfo[] constants = typeof(PersonalChatsCopy).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(string)).ToArray();
        Assert.Greater(constants.Length, 40, "the copy table collapsed");
        foreach (FieldInfo field in constants)
            yield return (field.Name, (string)field.GetValue(null));

        yield return ("MakeBusinessMany(2)", PersonalChatsCopy.MakeBusinessMany(2));
        yield return ("OnboardingDone(1)", PersonalChatsCopy.OnboardingDone(1));
        foreach (bool telegram in new[] { false, true })
        {
            yield return ($"OnboardingConnected({telegram})", PersonalChatsCopy.OnboardingConnected(telegram));
            yield return ($"OnboardingLoading({telegram})", PersonalChatsCopy.OnboardingLoading(telegram));
            yield return ($"OnboardingNoContacts({telegram})", PersonalChatsCopy.OnboardingNoContacts(telegram));
        }
        yield return ("MakePersonalBody(WipeDays)", PersonalChatsCopy.MakePersonalBody(ChatAudienceRule.WipeDays));
    }
}

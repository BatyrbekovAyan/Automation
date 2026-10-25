using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

/// <summary>
/// «Личные и рабочие чаты»: a move the server refused goes back on the screen and the owner is TOLD — the toast is subscribed
/// to <see cref="ChatAudienceSyncService.OnWriteFailed"/> from launch (a lazy overlay could not otherwise hear a refusal that
/// lands before it was ever shown) and says <see cref="PersonalChatsCopy.WriteFailed"/>. A network fault never raises the
/// event (it is retried), so the toast never speaks of the connection. In EditMode the toast builds nothing and only records
/// what it was asked to say.
/// </summary>
public class AudienceToastTests
{
    [SetUp]
    [TearDown]
    public void Reset() => AudienceToast.ResetForTests();

    [Test]
    public void AServerRefusal_ShowsWriteFailed()
    {
        AudienceToast.Boot();
        RaiseWriteFailed("Bot0", new List<string> { "77000000001@c.us" });
        Assert.AreEqual(PersonalChatsCopy.WriteFailed, AudienceToast.LastText);
    }

    [Test]
    public void ABootTwice_SubscribesOnce()
    {
        AudienceToast.Boot();
        AudienceToast.Boot();
        Assert.AreEqual(1, Handlers().Count(h => h.Method.DeclaringType == typeof(AudienceToast)),
            "a second launch without a domain reload must not double the toast");
    }

    [Test]
    public void Show_RecordsTheText_AndAnEmptyTextIsNoToast()
    {
        AudienceToast.Show(PersonalChatsCopy.MovedToPersonal);
        Assert.AreEqual(PersonalChatsCopy.MovedToPersonal, AudienceToast.LastText);
        AudienceToast.Show("");
        Assert.AreEqual(PersonalChatsCopy.MovedToPersonal, AudienceToast.LastText, "an empty text is no toast");
    }

    // The event's backing field — the only way to raise a field-like event from outside its class.
    private static Delegate[] Handlers()
    {
        FieldInfo field = typeof(ChatAudienceSyncService).GetField(nameof(ChatAudienceSyncService.OnWriteFailed),
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(field, "ChatAudienceSyncService.OnWriteFailed is no longer a field-like event — update AudienceToastTests");
        var handler = field.GetValue(null) as Delegate;
        return handler?.GetInvocationList() ?? Array.Empty<Delegate>();
    }

    private static void RaiseWriteFailed(string botName, IReadOnlyList<string> chatIds)
    {
        Delegate[] handlers = Handlers();
        Assert.IsNotEmpty(handlers, "nobody listens to OnWriteFailed");
        foreach (Delegate handler in handlers) handler.DynamicInvoke(botName, chatIds);
    }
}

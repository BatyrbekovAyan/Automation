using NUnit.Framework;

public class ChatIdFormatTests
{
    // --- Recipient: suffix-conditional @c.us stripping (NOT channel-conditional) ---

    [Test] public void Recipient_StripsCUs() =>
        Assert.AreEqual("79995579399", ChatIdFormat.Recipient("79995579399@c.us"));

    [Test] public void Recipient_KeepsGroup() =>
        Assert.AreEqual("120363012345@g.us", ChatIdFormat.Recipient("120363012345@g.us"));

    [Test] public void Recipient_KeepsBareNumeric() =>
        Assert.AreEqual("89323786", ChatIdFormat.Recipient("89323786"));

    [Test] public void Recipient_NullPassesThrough() =>
        Assert.IsNull(ChatIdFormat.Recipient(null));

    [Test] public void Recipient_EmptyPassesThrough() =>
        Assert.AreEqual("", ChatIdFormat.Recipient(""));

    // --- DisplayFallback: strip a PRESENT @c.us/@g.us suffix, else verbatim; never slice, never throw ---

    [Test] public void DisplayFallback_StripsCUs() =>
        Assert.AreEqual("79115576367", ChatIdFormat.DisplayFallback("79115576367@c.us"));

    [Test] public void DisplayFallback_StripsGUs() =>
        Assert.AreEqual("120363", ChatIdFormat.DisplayFallback("120363@g.us"));

    [Test] public void DisplayFallback_NumericTgId_Verbatim_NoSlice() =>
        Assert.AreEqual("89323786", ChatIdFormat.DisplayFallback("89323786"));

    [Test] public void DisplayFallback_ShortId_NoThrow_Verbatim() =>
        Assert.AreEqual("1234", ChatIdFormat.DisplayFallback("1234"));

    [Test] public void DisplayFallback_Empty_ReturnsEmpty() =>
        Assert.AreEqual("", ChatIdFormat.DisplayFallback(""));

    [Test] public void DisplayFallback_Null_ReturnsEmpty() =>
        Assert.AreEqual("", ChatIdFormat.DisplayFallback(null));

    // T-0501-01: the retired chat.id[..^5] crash — short/empty ids must never throw.
    [Test] public void DisplayFallback_NeverThrows_OnShortOrEmpty()
    {
        Assert.DoesNotThrow(() => ChatIdFormat.DisplayFallback("ab"));
        Assert.DoesNotThrow(() => ChatIdFormat.DisplayFallback(""));
        Assert.DoesNotThrow(() => ChatIdFormat.DisplayFallback(null));
    }

    // --- IsGroup(chatId): suffix-only overload ---

    [Test] public void IsGroup_Suffix_GroupSuffix_True() =>
        Assert.IsTrue(ChatIdFormat.IsGroup("120363012345@g.us"));

    [Test] public void IsGroup_Suffix_OneToOne_False() =>
        Assert.IsFalse(ChatIdFormat.IsGroup("79995579399@c.us"));

    [Test] public void IsGroup_Suffix_Empty_False() =>
        Assert.IsFalse(ChatIdFormat.IsGroup(""));

    // --- IsGroup(chatId, dialogType, dialogIsGroup): full overload ---

    [Test] public void IsGroup_Full_TelegramChatType_True() =>
        Assert.IsTrue(ChatIdFormat.IsGroup("4127433587", "chat", false)); // TG group

    [Test] public void IsGroup_Full_TelegramUserType_False() =>
        Assert.IsFalse(ChatIdFormat.IsGroup("89323786", "user", false)); // TG private

    [Test] public void IsGroup_Full_WaSuffixWins_OverTgType() =>
        Assert.IsTrue(ChatIdFormat.IsGroup("120363@g.us", "user", false)); // WA @g.us suffix wins

    [Test] public void IsGroup_Full_WaIsGroupFlag_True() =>
        Assert.IsTrue(ChatIdFormat.IsGroup("x", null, true)); // WA isGroup flag

    // WR-03 regression: the "chat" type rule is trusted ONLY for suffix-less (Telegram
    // numeric) ids. A WA id that carries any @ suffix must resolve by suffix/flag alone,
    // even if Wappi ever starts populating type:"chat" on the api side.
    [Test] public void IsGroup_Full_WaOneToOneSuffix_ChatType_StaysNonGroup() =>
        Assert.IsFalse(ChatIdFormat.IsGroup("79995579399@c.us", "chat", false)); // WA 1:1 stays 1:1

    [Test] public void IsGroup_Full_WaGroupSuffix_ChatType_StaysGroup() =>
        Assert.IsTrue(ChatIdFormat.IsGroup("120363012345@g.us", "chat", false)); // @g.us wins by suffix

    [Test] public void IsGroup_Full_NullId_ChatType_True() =>
        Assert.IsTrue(ChatIdFormat.IsGroup(null, "chat", false)); // suffix-less (TG-shaped) → trust type
}

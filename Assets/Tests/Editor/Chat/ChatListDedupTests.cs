using NUnit.Framework;

// D7 (device-UAT): the Telegram service dialog (login codes / 777000) showed TWICE on the
// Telegram list (Telegram-logo avatar row + silhouette row ⇒ two distinct chat-id forms of the
// SAME dialog) AND leaked onto the WhatsApp list (cross-channel cache bleed).
//
// The dedup + bleed defence is the pure ChatIdFormat seam that ParseChatsJson keys off:
//   • CanonicalKey(id, channel)      — collapses two id-forms of one dialog to a single row key.
//   • IsForeignToChannel(id, channel)— rejects a foreign-channel dialog that bled into a cache.
//
// These tests pin the contract (WhatsApp byte-identical; Telegram twin collapses; no real chat
// merged or dropped). The ParseChatsJson wiring itself reads private ChatManager state and is
// validated on-device at 08-10; here we prove the extractable, pure decision it depends on.
public class ChatListDedupTests
{
    // --- CanonicalKey: two id-forms of the SAME dialog collapse to ONE key ---

    // The core D7 defect: the 777000 service dialog arrives under a bare form and a spurious
    // WhatsApp-suffixed twin; both must dedup to a single Telegram row.
    [Test]
    public void CanonicalKey_ServiceDialog_TwoIdForms_SameKey()
    {
        string bare = ChatIdFormat.CanonicalKey("777000", ChatChannel.Telegram);
        string twin = ChatIdFormat.CanonicalKey("777000@c.us", ChatChannel.Telegram);
        Assert.AreEqual(bare, twin, "The bare 777000 and its spurious @c.us twin must share one dedup key.");
        Assert.AreEqual("777000", bare, "The canonical key is the bare Telegram id (the correct tapi id).");
    }

    // --- WhatsApp non-regression: distinct chats stay distinct; keys byte-identical to chat.id ---

    // Two different WhatsApp @c.us chats must NEVER collapse — the suffix + phone number are the
    // identity. (Regression guard: a naive "strip @c.us" rule would merge every 1:1 chat.)
    [Test]
    public void CanonicalKey_Whatsapp_TwoDistinctCUsIds_DifferentKeys()
    {
        string a = ChatIdFormat.CanonicalKey("79995579399@c.us", ChatChannel.WhatsApp);
        string b = ChatIdFormat.CanonicalKey("79115576367@c.us", ChatChannel.WhatsApp);
        Assert.AreNotEqual(a, b, "Distinct WhatsApp 1:1 chats must not merge.");
    }

    // WhatsApp keys are byte-identical to the raw chat.id (proves ParseChatsJson dedup is a no-op
    // change on WhatsApp — the whole channel stays byte-identical).
    [Test]
    public void CanonicalKey_Whatsapp_IsByteIdenticalToChatId()
    {
        Assert.AreEqual("79995579399@c.us", ChatIdFormat.CanonicalKey("79995579399@c.us", ChatChannel.WhatsApp));
        Assert.AreEqual("120363012345@g.us", ChatIdFormat.CanonicalKey("120363012345@g.us", ChatChannel.WhatsApp));
    }

    // A @g.us group id stays distinct from a @c.us 1:1 id — group vs 1:1 is never collapsed.
    [Test]
    public void CanonicalKey_Whatsapp_GroupPreservedDistinctFromOneToOne()
    {
        string group = ChatIdFormat.CanonicalKey("120363012345@g.us", ChatChannel.WhatsApp);
        string oneToOne = ChatIdFormat.CanonicalKey("120363012345@c.us", ChatChannel.WhatsApp);
        Assert.AreNotEqual(group, oneToOne);
    }

    // Null/empty never throw (mirrors DisplayFallback's no-throw contract; T-08-05-03).
    [Test]
    public void CanonicalKey_NullOrEmpty_PassesThrough()
    {
        Assert.IsNull(ChatIdFormat.CanonicalKey(null, ChatChannel.Telegram));
        Assert.AreEqual("", ChatIdFormat.CanonicalKey("", ChatChannel.WhatsApp));
        Assert.DoesNotThrow(() => ChatIdFormat.CanonicalKey(null, ChatChannel.WhatsApp));
    }

    // --- IsForeignToChannel: the cross-channel bleed defence ---

    // A bare (Telegram-form) id on the WhatsApp list is a leaked Telegram dialog — reject it.
    [Test]
    public void IsForeign_Whatsapp_RejectsBareTelegramId()
    {
        Assert.IsTrue(ChatIdFormat.IsForeignToChannel("777000", ChatChannel.WhatsApp));
    }

    // A real WhatsApp id ALWAYS carries an '@' jid suffix — never dropped (@c.us, @g.us, and any
    // exotic-but-genuine jid like status@broadcast / @newsletter / @lid all stay).
    [Test]
    public void IsForeign_Whatsapp_KeepsEveryGenuineJid()
    {
        Assert.IsFalse(ChatIdFormat.IsForeignToChannel("79995579399@c.us", ChatChannel.WhatsApp));
        Assert.IsFalse(ChatIdFormat.IsForeignToChannel("120363012345@g.us", ChatChannel.WhatsApp));
        Assert.IsFalse(ChatIdFormat.IsForeignToChannel("status@broadcast", ChatChannel.WhatsApp));
    }

    // Telegram never rejects: the bare id is native, and a spurious @c.us twin is MERGED by
    // CanonicalKey, never dropped — the service dialog must stay on the Telegram list.
    [Test]
    public void IsForeign_Telegram_NeverRejects()
    {
        Assert.IsFalse(ChatIdFormat.IsForeignToChannel("777000", ChatChannel.Telegram));
        Assert.IsFalse(ChatIdFormat.IsForeignToChannel("777000@c.us", ChatChannel.Telegram));
    }

    [Test]
    public void IsForeign_NullOrEmpty_NeverForeign()
    {
        Assert.IsFalse(ChatIdFormat.IsForeignToChannel(null, ChatChannel.WhatsApp));
        Assert.IsFalse(ChatIdFormat.IsForeignToChannel("", ChatChannel.WhatsApp));
    }
}

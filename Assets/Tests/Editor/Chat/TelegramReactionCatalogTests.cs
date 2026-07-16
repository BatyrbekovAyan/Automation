using NUnit.Framework;

/// <summary>
/// Covers the Telegram-allowed reaction set (D1): the quick-bar + "+" picker must only ever
/// offer emoji tapi's <c>message/reaction</c> accepts, or a normal tap 400s with
/// REACTION_INVALID. The WhatsApp bar/catalog are untouched (this seam is Telegram-only).
/// Pure/UnityEngine-free, so no scene is needed. The allowed set is a starting point and is
/// re-confirmed against a live capture at 08-10.
/// </summary>
public class TelegramReactionCatalogTests
{
    [Test]
    public void QuickEmojis_HasSix()
        => Assert.AreEqual(6, TelegramReactionCatalog.QuickEmojis.Length);

    [Test]
    public void QuickEmojis_EveryEntryIsAllowed()
    {
        foreach (var emoji in TelegramReactionCatalog.QuickEmojis)
            Assert.IsTrue(TelegramReactionCatalog.IsAllowed(emoji),
                $"Telegram quick emoji '{emoji}' is not in the allowed set");
    }

    [Test]
    public void QuickEmojis_SwapsTheWhatsAppInvalids()
    {
        // The WhatsApp bar's 😂 and 😮 are the two that broke on tapi — they must NOT be
        // in the Telegram quick set, and their replacements (😁 🔥) must be.
        CollectionAssert.DoesNotContain(TelegramReactionCatalog.QuickEmojis, "😂");
        CollectionAssert.DoesNotContain(TelegramReactionCatalog.QuickEmojis, "😮");
        CollectionAssert.Contains(TelegramReactionCatalog.QuickEmojis, "😁");
        CollectionAssert.Contains(TelegramReactionCatalog.QuickEmojis, "🔥");
    }

    [Test]
    public void IsAllowed_TheTwoThatBroke_AreRejected()
    {
        Assert.IsFalse(TelegramReactionCatalog.IsAllowed("😂"));  // D1 offender #1
        Assert.IsFalse(TelegramReactionCatalog.IsAllowed("😮"));  // D1 offender #2
    }

    [Test]
    public void IsAllowed_KnownStandardReactions_Accepted()
    {
        foreach (var emoji in new[] { "👍", "👎", "❤️", "🔥", "🥰", "🙏", "🎉", "😁" })
            Assert.IsTrue(TelegramReactionCatalog.IsAllowed(emoji), $"'{emoji}' should be allowed");
    }

    [Test]
    public void IsAllowed_NormalizesVariationSelector()
    {
        // Telegram accepts the base form; "❤" (U+2764) and "❤️" (U+2764 U+FE0F) must both match.
        Assert.AreEqual(TelegramReactionCatalog.IsAllowed("❤"), TelegramReactionCatalog.IsAllowed("❤️"));
        Assert.IsTrue(TelegramReactionCatalog.IsAllowed("❤"));
    }

    [Test]
    public void IsAllowed_NullOrEmpty_False()
    {
        Assert.IsFalse(TelegramReactionCatalog.IsAllowed(null));
        Assert.IsFalse(TelegramReactionCatalog.IsAllowed(""));
    }

    [Test]
    public void FilterCategories_OnlyAllowedEmoji_NoEmptyCategory()
    {
        var categories = TelegramReactionCatalog.FilterCategories();
        Assert.Greater(categories.Count, 0, "expected at least one non-empty category");
        foreach (var category in categories)
        {
            Assert.Greater(category.Emojis.Length, 0, $"category '{category.Name}' is empty");
            foreach (var emoji in category.Emojis)
                Assert.IsTrue(TelegramReactionCatalog.IsAllowed(emoji),
                    $"filtered category '{category.Name}' leaked a disallowed emoji '{emoji}'");
        }
    }

    [Test]
    public void FilterCategories_DropsTheWhatsAppInvalids()
    {
        // 😂/😮 live in the full catalog but must never survive the Telegram filter.
        foreach (var category in TelegramReactionCatalog.FilterCategories())
        {
            CollectionAssert.DoesNotContain(category.Emojis, "😂");
            CollectionAssert.DoesNotContain(category.Emojis, "😮");
        }
    }
}

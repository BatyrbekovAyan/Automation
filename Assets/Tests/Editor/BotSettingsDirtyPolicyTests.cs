using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Automation.BotSettingsUI;

/// <summary>
/// Pins the Save-button contract: interactable the moment any value differs
/// from what is persisted, non-interactable again once every value is back at
/// its saved state.
///
/// The verdict used to be split between an inline field comparison that could
/// only turn Save OFF and a coroutine that could only turn it ON, which is how
/// a service-card edit became invisible, a blank product card latched Save lit
/// forever, and a saved change kept the button lit until a second Save press.
/// Every case below is a defect that shipped; keep them green.
/// </summary>
public class BotSettingsDirtyPolicyTests
{
    private const int ContactCount = 5; // BotSettings.ContactKeys.Length

    // Deliberately not a real "BotN" name so a stray key can never collide with
    // a bot in the developer's editor session. Wiped around every test.
    private const string TestBot = "DirtyPolicyTestBot";

    [SetUp]
    public void ClearTestBotKeys() => WipeTestBot();

    [TearDown]
    public void RemoveTestBotKeys() => WipeTestBot();

    private static void WipeTestBot()
    {
        foreach (var suffix in new[]
        {
            "Name", "BusinessType", "isOnWhatsapp", "isOnTelegram",
            "WhatsappNumber", "TelegramNumber", "Business", "Prompt",
            "ProductsNumber", "ServicesNumber",
        })
        {
            PlayerPrefs.DeleteKey(TestBot + suffix);
        }

        foreach (var contactKey in BotSettings.ContactKeys)
            PlayerPrefs.DeleteKey(TestBot + contactKey);

        for (int i = 0; i < 16; i++)
        {
            foreach (var singular in new[] { "Product", "Service" })
            {
                PlayerPrefs.DeleteKey(TestBot + singular + i);
                PlayerPrefs.DeleteKey(TestBot + singular + i + "Price");
                PlayerPrefs.DeleteKey(TestBot + singular + i + "Description");
            }
        }
    }

    private static BotSettingsSnapshot Baseline() => new BotSettingsSnapshot
    {
        Name = "Ассистент",
        BusinessTypeId = "flowers",
        WhatsappOn = true,
        TelegramOn = false,
        WhatsappNumber = "77001234567",
        TelegramNumber = "77009876543",
        Business = "Цветочный магазин",
        Prompt = "Отвечай коротко",
        Contacts = new[] { "+7 700 000", "9-19", "Алматы", "@shop", "a@b.kz" },
        Products = new List<BotSettingsListItem>
        {
            new BotSettingsListItem("Розы", "5000", "красные"),
        },
        Services = new List<BotSettingsListItem>
        {
            new BotSettingsListItem("Доставка", "1000", "по городу"),
        },
    };

    private static BotSettingsSnapshot Clone(BotSettingsSnapshot source) => new BotSettingsSnapshot
    {
        Name = source.Name,
        BusinessTypeId = source.BusinessTypeId,
        WhatsappOn = source.WhatsappOn,
        TelegramOn = source.TelegramOn,
        WhatsappNumber = source.WhatsappNumber,
        TelegramNumber = source.TelegramNumber,
        Business = source.Business,
        Prompt = source.Prompt,
        Contacts = (string[])source.Contacts.Clone(),
        Products = new List<BotSettingsListItem>(source.Products),
        Services = new List<BotSettingsListItem>(source.Services),
    };

    [Test]
    public void UntouchedScreen_IsClean()
    {
        Assert.IsFalse(BotSettingsDirtyPolicy.IsDirty(Baseline(), Baseline()));
    }

    // BotSettings.WireDirtyOnEdit hooks TMP_InputField.onValueChanged so Save
    // lights while the user is still typing. A field whose input reference is
    // missing silently loses that hook and reverts to the blur-only behaviour
    // that made the first «Сохранить» tap do nothing.
    [Test]
    public void EverySettingsField_ExposesAnInputFieldToHook()
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/BotSettings.prefab");
        Assert.IsNotNull(prefab, "BotSettings.prefab not found");
        var settings = prefab.GetComponent<BotSettings>();

        var fields = new List<EditableField>
        {
            settings.BotNameField, settings.BusinessField, settings.PromptField,
        };
        fields.AddRange(settings.ContactFields);

        foreach (var field in fields)
        {
            Assert.IsNotNull(field, "A settings field is not wired on the prefab.");
            Assert.IsNotNull(field.InputField,
                $"'{field.name}' has no TMP_InputField — the live dirty hook cannot attach.");
        }
    }

    // ── the five contact rows added to the Бизнес tab ──────────────────

    [TestCase(0, TestName = "Contact_Телефон_Dirties")]
    [TestCase(1, TestName = "Contact_ЧасыРаботы_Dirties")]
    [TestCase(2, TestName = "Contact_Адрес_Dirties")]
    [TestCase(3, TestName = "Contact_Instagram_Dirties")]
    [TestCase(4, TestName = "Contact_Email_Dirties")]
    public void EachContactRow_Dirties(int index)
    {
        var saved = Baseline();
        var edited = Clone(saved);
        edited.Contacts[index] = "изменено";

        Assert.IsTrue(BotSettingsDirtyPolicy.IsDirty(edited, saved),
            $"Contact row {index} does not participate in the dirty check.");
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void EachContactRow_RevertedToInitial_IsCleanAgain(int index)
    {
        var saved = Baseline();
        var edited = Clone(saved);
        edited.Contacts[index] = "изменено";
        Assert.IsTrue(BotSettingsDirtyPolicy.IsDirty(edited, saved));

        edited.Contacts[index] = saved.Contacts[index];
        Assert.IsFalse(BotSettingsDirtyPolicy.IsDirty(edited, saved),
            $"Contact row {index} stays dirty after being returned to its saved value.");
    }

    [Test]
    public void ContactRow_ClearedToEmpty_Dirties()
    {
        var saved = Baseline();
        var edited = Clone(saved);
        edited.Contacts[0] = "";

        Assert.IsTrue(BotSettingsDirtyPolicy.IsDirty(edited, saved),
            "Deleting a contact value is a change and must light Save.");
    }

    [Test]
    public void ContactRow_NotWiredOnPrefab_IsSkipped()
    {
        // A prefab predating the contact builder reports null for the card.
        // Comparing null against the saved "" would read as permanently dirty.
        var saved = Baseline();
        var edited = Clone(saved);
        for (int c = 0; c < ContactCount; c++) edited.Contacts[c] = null;

        Assert.IsFalse(BotSettingsDirtyPolicy.IsDirty(edited, saved));
    }

    [Test]
    public void ContactArrayShorterThanSaved_DoesNotThrow()
    {
        var saved = Baseline();
        var edited = Clone(saved);
        edited.Contacts = new[] { "+7 700 000" };

        Assert.IsFalse(BotSettingsDirtyPolicy.IsDirty(edited, saved));
    }

    // ── channel toggles ───────────────────────────────────────────────

    [Test]
    public void WhatsappToggle_TurnedOff_Dirties()
    {
        var saved = Baseline();
        var edited = Clone(saved);
        edited.WhatsappOn = false;

        Assert.IsTrue(BotSettingsDirtyPolicy.IsDirty(edited, saved));
    }

    [Test]
    public void WhatsappToggle_TurnedBackOn_IsCleanAgain()
    {
        // The ON direction used to skip the dirty check entirely, so Save
        // stayed lit after the user undid their own toggle.
        var saved = Baseline();
        var edited = Clone(saved);
        edited.WhatsappOn = false;
        Assert.IsTrue(BotSettingsDirtyPolicy.IsDirty(edited, saved));

        edited.WhatsappOn = true;
        Assert.IsFalse(BotSettingsDirtyPolicy.IsDirty(edited, saved));
    }

    [Test]
    public void TelegramToggle_TurnedOn_Dirties()
    {
        // Saved OFF, user switches it ON: a genuine change that used to leave
        // Save dim, so it could not be saved at all.
        var saved = Baseline();
        var edited = Clone(saved);
        edited.TelegramOn = true;

        Assert.IsTrue(BotSettingsDirtyPolicy.IsDirty(edited, saved));
    }

    [Test]
    public void ToggleChange_WrittenToPrefs_ReadsBackClean()
    {
        // The double-press bug: Save persisted the toggle only after a network
        // round-trip, so the re-check at the end of the save still read the old
        // value and re-lit the button. This goes through the REAL saved-side
        // reader, so it fails if that write/read pair ever drifts apart again.
        PlayerPrefs.SetInt(TestBot + "isOnTelegram", 1);
        PlayerPrefs.SetInt(TestBot + "isOnWhatsapp", 0);

        var saved = Manager.ReadSavedSettings(TestBot);
        Assert.IsTrue(saved.TelegramOn, "isOnTelegram=1 must read back as ON.");
        Assert.IsFalse(saved.WhatsappOn, "isOnWhatsapp=0 must read back as OFF.");

        var edited = Clone(saved);
        Assert.IsFalse(BotSettingsDirtyPolicy.IsDirty(edited, saved),
            "A screen matching the just-written prefs must dim Save on the first re-check.");

        edited.TelegramOn = false;
        Assert.IsTrue(BotSettingsDirtyPolicy.IsDirty(edited, saved));
    }

    [Test]
    public void ContactRows_WrittenToPrefs_ReadBackUnderTheRightKeys()
    {
        // Guards the key suffixes themselves: a typo here would make a contact
        // row read back as "" forever, i.e. permanently dirty after every save.
        var values = new[] { "+7 700 111", "10-20", "Астана", "@bot", "x@y.kz" };
        for (int c = 0; c < BotSettings.ContactKeys.Length; c++)
            PlayerPrefs.SetString(TestBot + BotSettings.ContactKeys[c], values[c]);

        var saved = Manager.ReadSavedSettings(TestBot);
        CollectionAssert.AreEqual(values, saved.Contacts);

        var edited = Clone(saved);
        Assert.IsFalse(BotSettingsDirtyPolicy.IsDirty(edited, saved));
    }

    // ── business type placeholder ──────────────────────────────────────

    [Test]
    public void UnknownBusinessType_CountsAsUnchanged()
    {
        // A bot saved with a pre-vertical legacy id selects «Тип не выбран»,
        // which resolves to no entry (null). Saving keeps the stored id, so the
        // placeholder must never read as a change.
        var saved = Baseline();
        saved.BusinessTypeId = "car_service";
        var edited = Clone(saved);
        edited.BusinessTypeId = null;

        Assert.IsFalse(BotSettingsDirtyPolicy.IsDirty(edited, saved));
    }

    [Test]
    public void BusinessTypeChanged_Dirties()
    {
        var saved = Baseline();
        var edited = Clone(saved);
        edited.BusinessTypeId = "auto_parts";

        Assert.IsTrue(BotSettingsDirtyPolicy.IsDirty(edited, saved));
    }

    // ── scalar fields ─────────────────────────────────────────────────

    [Test]
    public void EveryScalarField_Dirties()
    {
        var saved = Baseline();

        var mutations = new Dictionary<string, System.Action<BotSettingsSnapshot>>
        {
            { "Name", s => s.Name = "Другое" },
            { "WhatsappNumber", s => s.WhatsappNumber = "77000000000" },
            { "TelegramNumber", s => s.TelegramNumber = "77000000000" },
            { "Business", s => s.Business = "Другое описание" },
            { "Prompt", s => s.Prompt = "Отвечай подробно" },
        };

        foreach (var mutation in mutations)
        {
            var edited = Clone(saved);
            mutation.Value(edited);
            Assert.IsTrue(BotSettingsDirtyPolicy.IsDirty(edited, saved),
                $"{mutation.Key} does not light Save.");
        }
    }

    // ── products & services ───────────────────────────────────────────

    [Test]
    public void ServiceContentEdit_Dirties()
    {
        // The regression that motivated collapsing the old if/else-if chain:
        // its services branch was unreachable, so a price change on an existing
        // service card could never light Save and was lost on back.
        var saved = Baseline();
        var edited = Clone(saved);
        edited.Services[0] = new BotSettingsListItem("Доставка", "1500", "по городу");

        Assert.IsTrue(BotSettingsDirtyPolicy.IsDirty(edited, saved),
            "A service content edit must light Save.");
    }

    [Test]
    public void ProductContentEdit_Dirties()
    {
        var saved = Baseline();
        var edited = Clone(saved);
        edited.Products[0] = new BotSettingsListItem("Розы", "6000", "красные");

        Assert.IsTrue(BotSettingsDirtyPolicy.IsDirty(edited, saved));
    }

    [Test]
    public void ServiceDescriptionEdit_Dirties()
    {
        var saved = Baseline();
        var edited = Clone(saved);
        edited.Services[0] = new BotSettingsListItem("Доставка", "1000", "по всему Казахстану");

        Assert.IsTrue(BotSettingsDirtyPolicy.IsDirty(edited, saved));
    }

    [Test]
    public void AddedRow_Dirties_AndRemovedRow_Dirties()
    {
        var saved = Baseline();

        var added = Clone(saved);
        added.Products.Add(new BotSettingsListItem("Тюльпаны", "3000", ""));
        Assert.IsTrue(BotSettingsDirtyPolicy.IsDirty(added, saved));

        var removed = Clone(saved);
        removed.Services.Clear();
        Assert.IsTrue(BotSettingsDirtyPolicy.IsDirty(removed, saved));
    }

    [Test]
    public void ReorderedRows_Dirty()
    {
        var saved = Baseline();
        saved.Products.Add(new BotSettingsListItem("Тюльпаны", "3000", ""));

        var edited = Clone(saved);
        var first = edited.Products[0];
        edited.Products[0] = edited.Products[1];
        edited.Products[1] = first;

        Assert.IsTrue(BotSettingsDirtyPolicy.IsDirty(edited, saved),
            "Slots are positional, so a reorder is a real change.");
    }

    // ── what actually reaches PlayerPrefs ─────────────────────────────

    [Test]
    public void Persistable_DropsBlankRows_AndTrimsNames()
    {
        var cards = new List<BotSettingsListItem>
        {
            new BotSettingsListItem("", "0", ""),          // added, never filled
            new BotSettingsListItem("  Розы  ", "5000", "красные"),
            new BotSettingsListItem("   ", "0", ""),       // whitespace-only
        };

        var rows = BotSettingsListSlots.Persistable(cards);

        Assert.AreEqual(1, rows.Count, "Only rows the save path writes may count.");
        Assert.AreEqual("Розы", rows[0].Name, "Name must be trimmed, as SaveSettings trims it.");
        Assert.AreEqual("5000", rows[0].Price);
    }

    [Test]
    public void BlankCardAmongRealOnes_IsNotDirtyAfterSave()
    {
        // The latch: a blank card added and abandoned used to make the dirty
        // check count childCount against a non-empty saved count, so Save
        // stayed interactable for the rest of the session — including straight
        // after a successful save. Full round-trip through the real writer.
        var cards = new List<BotSettingsListItem>
        {
            new BotSettingsListItem("", "", ""),
            new BotSettingsListItem("Розы", "5000", "красные"),
        };
        var rows = BotSettingsListSlots.Persistable(cards);

        BotSettingsListSlots.Persist(TestBot, "Product", "ProductsNumber", rows);

        // The real row must land at slot 0, not at its child index.
        Assert.AreEqual("Розы", PlayerPrefs.GetString(TestBot + "Product0", ""));
        Assert.AreEqual(1, PlayerPrefs.GetInt(TestBot + "ProductsNumber", -1));

        var saved = Manager.ReadSavedSettings(TestBot);
        var edited = Clone(saved);
        edited.Products = rows;

        Assert.IsFalse(BotSettingsDirtyPolicy.IsDirty(edited, saved),
            "A blank card is never written, so it must not keep Save lit after a save.");
    }

    [Test]
    public void ShrunkList_DeletesOrphanTail()
    {
        BotSettingsListSlots.Persist(TestBot, "Service", "ServicesNumber", new List<BotSettingsListItem>
        {
            new BotSettingsListItem("Доставка", "1000", "по городу"),
            new BotSettingsListItem("Сборка", "2000", ""),
        });

        BotSettingsListSlots.Persist(TestBot, "Service", "ServicesNumber", new List<BotSettingsListItem>
        {
            new BotSettingsListItem("Доставка", "1000", "по городу"),
        });

        Assert.AreEqual(1, PlayerPrefs.GetInt(TestBot + "ServicesNumber", -1));
        Assert.IsFalse(PlayerPrefs.HasKey(TestBot + "Service1"), "Orphan tail key leaked.");
        Assert.IsFalse(PlayerPrefs.HasKey(TestBot + "Service1Price"));
        Assert.IsFalse(PlayerPrefs.HasKey(TestBot + "Service1Description"));
    }

    [Test]
    public void PersistThenRead_RoundTripsEveryField()
    {
        var rows = new List<BotSettingsListItem>
        {
            new BotSettingsListItem("Розы", "5000", "красные"),
            new BotSettingsListItem("Тюльпаны", "3000", ""),
        };

        BotSettingsListSlots.Persist(TestBot, "Product", "ProductsNumber", rows);
        var read = BotSettingsListSlots.Read(TestBot, "Product", "ProductsNumber");

        Assert.AreEqual(rows.Count, read.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            Assert.AreEqual(rows[i].Name, read[i].Name);
            Assert.AreEqual(rows[i].Price, read[i].Price);
            Assert.AreEqual(rows[i].Description, read[i].Description);
        }
        Assert.IsFalse(BotSettingsDirtyPolicy.ListChanged(rows, read));
    }

    [Test]
    public void ServiceContentEdit_SurvivesTheFullSaveRoundTrip()
    {
        // End to end for the defect that could not light Save at all: edit,
        // verify dirty, persist, verify clean.
        BotSettingsListSlots.Persist(TestBot, "Service", "ServicesNumber", new List<BotSettingsListItem>
        {
            new BotSettingsListItem("Доставка", "1000", "по городу"),
        });

        var saved = Manager.ReadSavedSettings(TestBot);
        var edited = Clone(saved);
        edited.Services = new List<BotSettingsListItem>
        {
            new BotSettingsListItem("Доставка", "1500", "по городу"),
        };
        Assert.IsTrue(BotSettingsDirtyPolicy.IsDirty(edited, saved), "Price edit must light Save.");

        BotSettingsListSlots.Persist(TestBot, "Service", "ServicesNumber", edited.Services);
        Assert.IsFalse(BotSettingsDirtyPolicy.IsDirty(edited, Manager.ReadSavedSettings(TestBot)),
            "One save must be enough to dim Save again.");
    }

    [Test]
    public void ListChanged_IsPositionalAndCountSensitive()
    {
        var saved = new List<BotSettingsListItem> { new BotSettingsListItem("A", "1", "x") };

        Assert.IsFalse(BotSettingsDirtyPolicy.ListChanged(
            new List<BotSettingsListItem> { new BotSettingsListItem("A", "1", "x") }, saved));
        Assert.IsTrue(BotSettingsDirtyPolicy.ListChanged(new List<BotSettingsListItem>(), saved));
        Assert.IsTrue(BotSettingsDirtyPolicy.ListChanged(null, saved));
        Assert.IsFalse(BotSettingsDirtyPolicy.ListChanged(null, null));
    }

    [Test]
    public void NullAndEmptyString_AreTheSameValue()
    {
        // PlayerPrefs hands back "" for a missing key while an unwired label
        // hands back null; treating those as different would light Save on open.
        var saved = Baseline();
        saved.Prompt = "";
        var edited = Clone(saved);
        edited.Prompt = null;

        Assert.IsFalse(BotSettingsDirtyPolicy.IsDirty(edited, saved));
    }

    [Test]
    public void NullSnapshot_IsTreatedAsClean()
    {
        Assert.IsFalse(BotSettingsDirtyPolicy.IsDirty(null, Baseline()));
        Assert.IsFalse(BotSettingsDirtyPolicy.IsDirty(Baseline(), null));
    }
}

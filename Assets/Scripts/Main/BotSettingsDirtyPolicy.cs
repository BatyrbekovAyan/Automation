using System.Collections.Generic;

namespace Automation.BotSettingsUI
{
    /// <summary>
    /// One Продукты / Услуги row as the dirty check sees it. Plain data so the
    /// comparison never touches a ProductCardView / ServiceCardView (the two
    /// share no base type exposing Name).
    /// </summary>
    public struct BotSettingsListItem
    {
        public string Name;
        public string Price;
        public string Description;

        public BotSettingsListItem(string name, string price, string description)
        {
            Name = name ?? string.Empty;
            Price = price ?? string.Empty;
            Description = description ?? string.Empty;
        }
    }

    /// <summary>
    /// Every value the Save button's verdict is computed from — filled once
    /// from the live screen and once from PlayerPrefs, then compared.
    /// </summary>
    public sealed class BotSettingsSnapshot
    {
        public string Name = string.Empty;

        /// <summary>
        /// null means "the dropdown resolves to no known business type" — the
        /// «Тип не выбран» placeholder a bot with a pre-vertical legacy id
        /// selects. Saving deliberately keeps the stored id in that case, so a
        /// null must never count as a change.
        /// </summary>
        public string BusinessTypeId;

        public bool WhatsappOn;
        public bool TelegramOn;
        public string WhatsappNumber = string.Empty;
        public string TelegramNumber = string.Empty;
        public string Business = string.Empty;
        public string Prompt = string.Empty;

        /// <summary>
        /// Index-aligned with BotSettings.ContactKeys. A null entry means that
        /// card is not wired on this prefab (it predates the contact builder)
        /// and is skipped on both sides.
        /// </summary>
        public string[] Contacts = new string[0];

        public List<BotSettingsListItem> Products = new List<BotSettingsListItem>();
        public List<BotSettingsListItem> Services = new List<BotSettingsListItem>();
    }

    /// <summary>
    /// The single source of truth for "does the Bot Settings screen hold unsaved
    /// changes?" — the rule behind the Save button becoming interactable on a
    /// change and non-interactable again once every value is back at its
    /// persisted state.
    ///
    /// Pure and static on purpose: the verdict used to be split across an
    /// inline field comparison (which could only ever turn the button OFF) and
    /// a one-frame-later coroutine (which could only ever turn it ON), so a
    /// stale "dirty" could survive a save and a real edit could be dimmed away.
    /// One two-way function, unit-testable without a scene, replaces both.
    /// </summary>
    public static class BotSettingsDirtyPolicy
    {
        public static bool IsDirty(BotSettingsSnapshot edited, BotSettingsSnapshot saved)
        {
            if (edited == null || saved == null) return false;

            if (Differs(edited.Name, saved.Name)) return true;
            if (edited.WhatsappOn != saved.WhatsappOn) return true;
            if (edited.TelegramOn != saved.TelegramOn) return true;
            if (edited.BusinessTypeId != null
                && Differs(edited.BusinessTypeId, saved.BusinessTypeId)) return true;
            if (Differs(edited.WhatsappNumber, saved.WhatsappNumber)) return true;
            if (Differs(edited.TelegramNumber, saved.TelegramNumber)) return true;
            if (Differs(edited.Business, saved.Business)) return true;
            if (Differs(edited.Prompt, saved.Prompt)) return true;
            if (ContactsChanged(edited.Contacts, saved.Contacts)) return true;

            return ListChanged(edited.Products, saved.Products)
                || ListChanged(edited.Services, saved.Services);
        }

        /// <summary>
        /// Walks the contact cards pairwise. An unwired card (null on the
        /// edited side) is skipped rather than compared against "" — a prefab
        /// without the card must not read as permanently dirty.
        /// </summary>
        public static bool ContactsChanged(string[] edited, string[] saved)
        {
            if (edited == null || saved == null) return false;
            int count = edited.Length < saved.Length ? edited.Length : saved.Length;
            for (int c = 0; c < count; c++)
            {
                if (edited[c] == null) continue;
                if (Differs(edited[c], saved[c])) return true;
            }
            return false;
        }

        /// <summary>
        /// Compares a products/services list against its persisted slots. The
        /// edited side must already be filtered to the rows the save path
        /// actually writes (non-empty trimmed Name), so a blank card the user
        /// added and abandoned reads as clean instead of pinning Save lit.
        /// </summary>
        public static bool ListChanged(List<BotSettingsListItem> edited, List<BotSettingsListItem> saved)
        {
            int editedCount = edited != null ? edited.Count : 0;
            int savedCount = saved != null ? saved.Count : 0;
            if (editedCount != savedCount) return true;

            for (int i = 0; i < editedCount; i++)
            {
                if (Differs(edited[i].Name, saved[i].Name)) return true;
                if (Differs(edited[i].Price, saved[i].Price)) return true;
                if (Differs(edited[i].Description, saved[i].Description)) return true;
            }
            return false;
        }

        private static bool Differs(string a, string b) => (a ?? string.Empty) != (b ?? string.Empty);
    }
}

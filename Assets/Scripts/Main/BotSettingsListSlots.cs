using System.Collections.Generic;
using UnityEngine;

namespace Automation.BotSettingsUI
{
    /// <summary>
    /// The one definition of how the Продукты / Услуги lists are laid out in
    /// PlayerPrefs, and of which rows reach it at all.
    ///
    /// Both the save path and the dirty check run the live cards through
    /// <see cref="Persistable"/> before anything else, so "just saved, nothing
    /// left to write" always evaluates to clean. They used to disagree: the
    /// save wrote each row at its CHILD index while the count only counted
    /// non-empty rows, so one blank card in the list stored the real row at a
    /// slot the next load never read (the row vanished) and left the Save
    /// button latched interactable for the rest of the session.
    ///
    /// Keys are `{botName}{singular}{slot}` plus `Price`/`Description`
    /// suffixes, with the row count under `{botName}{countKey}` — e.g.
    /// Bot0Product0, Bot0Product0Price, Bot0ProductsNumber.
    /// </summary>
    public static class BotSettingsListSlots
    {
        /// <summary>
        /// The rows the save path will actually write: Name trimmed, rows with
        /// a blank Name dropped, order preserved. Price/Description are stored
        /// verbatim (the save path does not trim them either).
        /// </summary>
        public static List<BotSettingsListItem> Persistable(IEnumerable<BotSettingsListItem> rows)
        {
            var persistable = new List<BotSettingsListItem>();
            if (rows == null) return persistable;

            foreach (var row in rows)
            {
                var name = row.Name?.Trim() ?? string.Empty;
                if (name.Length == 0) continue;
                persistable.Add(new BotSettingsListItem(name, row.Price, row.Description));
            }
            return persistable;
        }

        /// <summary>
        /// Writes the rows to contiguous slots 0..N-1, deletes the orphan tail
        /// a shrunk list leaves behind, and stores N. Pass rows that already
        /// went through <see cref="Persistable"/>.
        /// </summary>
        public static void Persist(string botName, string singular, string countKey,
                                   List<BotSettingsListItem> rows)
        {
            int previousCount = PlayerPrefs.GetInt(botName + countKey, 0);
            int count = rows != null ? rows.Count : 0;

            for (int slot = 0; slot < count; slot++)
            {
                PlayerPrefs.SetString(botName + singular + slot, rows[slot].Name);
                PlayerPrefs.SetString(botName + singular + slot + "Price", rows[slot].Price);
                PlayerPrefs.SetString(botName + singular + slot + "Description", rows[slot].Description);
            }

            for (int slot = count; slot < previousCount; slot++)
            {
                PlayerPrefs.DeleteKey(botName + singular + slot);
                PlayerPrefs.DeleteKey(botName + singular + slot + "Price");
                PlayerPrefs.DeleteKey(botName + singular + slot + "Description");
            }

            PlayerPrefs.SetInt(botName + countKey, count);
        }

        public static List<BotSettingsListItem> Read(string botName, string singular, string countKey)
        {
            int count = PlayerPrefs.GetInt(botName + countKey, 0);
            var rows = new List<BotSettingsListItem>(count);
            for (int slot = 0; slot < count; slot++)
            {
                rows.Add(new BotSettingsListItem(
                    PlayerPrefs.GetString(botName + singular + slot, ""),
                    PlayerPrefs.GetString(botName + singular + slot + "Price", ""),
                    PlayerPrefs.GetString(botName + singular + slot + "Description", "")));
            }
            return rows;
        }
    }
}

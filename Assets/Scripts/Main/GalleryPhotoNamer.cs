using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Display names for gallery-picked photos. iOS never exposes the real asset
/// filename — NativeGallery returns temp copies named pickedMedia1.jpg,
/// pickedMedia2.jpg, REUSED on every pick session. Surfacing those as row
/// names made every later photo look like a same-named "replace" of an
/// earlier one, and the replace flow then deleted the earlier photo's
/// knowledge. Synthesized names are readable («Фото 03.07.2026 14:22 (2).jpg»)
/// and guaranteed unique against the names already stored for the bot, so
/// FindByName can never cross-match two different photos. Always ends in
/// .jpg — the upload payload routes on the name's final extension.
/// </summary>
public static class GalleryPhotoNamer
{
    public static string DisplayName(DateTime localNow, int indexInBatch, int batchSize, ICollection<string> takenNames)
    {
        string stamp = localNow.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
        string batchPart = batchSize > 1 ? $" ({indexInBatch + 1})" : string.Empty;

        string name = $"Фото {stamp}{batchPart}.jpg";
        int bump = 2;
        while (takenNames != null && takenNames.Contains(name))
            name = $"Фото {stamp}{batchPart} — {bump++}.jpg";

        return name;
    }
}

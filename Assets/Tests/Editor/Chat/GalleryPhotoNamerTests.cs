using System;
using System.Collections.Generic;
using NUnit.Framework;

public class GalleryPhotoNamerTests
{
    private static readonly DateTime Stamp = new DateTime(2026, 7, 3, 14, 22, 0);

    [Test]
    public void DisplayName_SinglePhoto_NoBatchIndex()
    {
        string name = GalleryPhotoNamer.DisplayName(Stamp, 0, 1, new HashSet<string>());
        Assert.AreEqual("Фото 03.07.2026 14:22.jpg", name);
    }

    [Test]
    public void DisplayName_Batch_NumbersFromOne()
    {
        Assert.AreEqual("Фото 03.07.2026 14:22 (1).jpg",
            GalleryPhotoNamer.DisplayName(Stamp, 0, 3, new HashSet<string>()));
        Assert.AreEqual("Фото 03.07.2026 14:22 (2).jpg",
            GalleryPhotoNamer.DisplayName(Stamp, 1, 3, new HashSet<string>()));
    }

    [Test]
    public void DisplayName_WithinBatch_NamesDiffer()
    {
        var taken = new HashSet<string>();
        string first = GalleryPhotoNamer.DisplayName(Stamp, 0, 2, taken);
        taken.Add(first);
        string second = GalleryPhotoNamer.DisplayName(Stamp, 1, 2, taken);
        Assert.AreNotEqual(first, second);
    }

    [Test]
    public void DisplayName_CollisionWithStoredName_Bumps()
    {
        var taken = new HashSet<string> { "Фото 03.07.2026 14:22.jpg" };
        string name = GalleryPhotoNamer.DisplayName(Stamp, 0, 1, taken);
        Assert.AreEqual("Фото 03.07.2026 14:22 — 2.jpg", name);
    }

    [Test]
    public void DisplayName_RepeatedCollisions_KeepBumping()
    {
        var taken = new HashSet<string>
        {
            "Фото 03.07.2026 14:22.jpg",
            "Фото 03.07.2026 14:22 — 2.jpg",
        };
        string name = GalleryPhotoNamer.DisplayName(Stamp, 0, 1, taken);
        Assert.AreEqual("Фото 03.07.2026 14:22 — 3.jpg", name);
    }

    [Test]
    public void DisplayName_AlwaysEndsWithJpg()
    {
        // The image branch routes the payload by the name's final extension —
        // a synthesized name without .jpg would land in the workflow's 415 fallback.
        StringAssert.EndsWith(".jpg", GalleryPhotoNamer.DisplayName(Stamp, 0, 1, null));
        StringAssert.EndsWith(".jpg", GalleryPhotoNamer.DisplayName(Stamp, 2, 5,
            new HashSet<string> { "Фото 03.07.2026 14:22 (3).jpg" }));
    }

    [Test]
    public void DisplayName_NullTakenNames_Tolerated()
    {
        Assert.AreEqual("Фото 03.07.2026 14:22.jpg", GalleryPhotoNamer.DisplayName(Stamp, 0, 1, null));
    }
}

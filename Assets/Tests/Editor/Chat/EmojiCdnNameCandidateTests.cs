using NUnit.Framework;

/// <summary>
/// Tests for EmojiPatchService.BuildCandidateNames — the ordered list of Twemoji
/// CDN filename guesses tried when the name built from the sender's codepoints
/// 404s. Twemoji filenames place U+FE0F inconsistently per emoji family, and
/// senders are equally inconsistent about including it, so the fetch walks
/// these variants. Expected filenames below were verified against
/// cdn.jsdelivr.net/gh/jdecked/twemoji (June 2026).
/// </summary>
public class EmojiCdnNameCandidateTests
{
    [Test]
    public void RequestedNameIsAlwaysFirstCandidate()
    {
        var candidates = EmojiPatchService.BuildCandidateNames("1f636-200d-1f32b");

        Assert.AreEqual("1f636-200d-1f32b", candidates[0]);
    }

    [Test]
    public void PlainSingleCodepoint_HasNoExtraCandidates()
    {
        var candidates = EmojiPatchService.BuildCandidateNames("1f600");

        Assert.AreEqual(1, candidates.Count);
    }

    [Test]
    public void FullyQualifiedSingle_AddsStrippedName()
    {
        // ☺️ sent as 263a-fe0f — Twemoji file is 263a.png
        var candidates = EmojiPatchService.BuildCandidateNames("263a-fe0f");

        CollectionAssert.Contains(candidates, "263a");
    }

    [Test]
    public void MinimalZwj_AddsTrailingFe0fVariant()
    {
        // 😶‍🌫️ sent without FE0F — Twemoji file is 1f636-200d-1f32b-fe0f.png
        var candidates = EmojiPatchService.BuildCandidateNames("1f636-200d-1f32b");

        CollectionAssert.Contains(candidates, "1f636-200d-1f32b-fe0f");
    }

    [Test]
    public void MinimalZwj_AddsBmpCodepointFe0fVariant()
    {
        // ❤️‍🔥 sent without FE0F — Twemoji file is 2764-fe0f-200d-1f525.png
        var candidates = EmojiPatchService.BuildCandidateNames("2764-200d-1f525");

        CollectionAssert.Contains(candidates, "2764-fe0f-200d-1f525");
    }

    [Test]
    public void MinimalZwj_AddsFirstSegmentFe0fVariant()
    {
        // 🏳️‍🌈 sent without FE0F — Twemoji file is 1f3f3-fe0f-200d-1f308.png
        var candidates = EmojiPatchService.BuildCandidateNames("1f3f3-200d-1f308");

        CollectionAssert.Contains(candidates, "1f3f3-fe0f-200d-1f308");
    }

    [Test]
    public void MinimalZwj_AddsAllSegmentsFe0fVariant()
    {
        // 🏳️‍⚧️ sent without FE0F — Twemoji file is 1f3f3-fe0f-200d-26a7-fe0f.png
        var candidates = EmojiPatchService.BuildCandidateNames("1f3f3-200d-26a7");

        CollectionAssert.Contains(candidates, "1f3f3-fe0f-200d-26a7-fe0f");
    }

    [Test]
    public void OverQualifiedZwj_AddsStrippedVariant()
    {
        // 👁️‍🗨️ sent fully qualified — Twemoji file is the minimal 1f441-200d-1f5e8.png
        var candidates = EmojiPatchService.BuildCandidateNames("1f441-fe0f-200d-1f5e8-fe0f");

        CollectionAssert.Contains(candidates, "1f441-200d-1f5e8");
    }

    [Test]
    public void SkinToneZwj_Fe0fAppliedToBmpSegmentOnly_NeverAfterSkinTone()
    {
        // 🧑🏻‍❤️‍💋‍🧑🏼 sent without FE0F — Twemoji file qualifies only the heart:
        // 1f9d1-1f3fb-200d-2764-fe0f-200d-1f48b-200d-1f9d1-1f3fc.png
        var candidates = EmojiPatchService.BuildCandidateNames(
            "1f9d1-1f3fb-200d-2764-200d-1f48b-200d-1f9d1-1f3fc");

        CollectionAssert.Contains(candidates,
            "1f9d1-1f3fb-200d-2764-fe0f-200d-1f48b-200d-1f9d1-1f3fc");
        foreach (var candidate in candidates)
        {
            StringAssert.DoesNotContain("1f3fb-fe0f", candidate);
            StringAssert.DoesNotContain("1f3fc-fe0f", candidate);
        }
    }

    [Test]
    public void CandidatesContainNoDuplicates()
    {
        foreach (var name in new[]
        {
            "1f600", "263a-fe0f", "1f636-200d-1f32b", "2764-200d-1f525",
            "1f3f3-200d-26a7", "1f441-fe0f-200d-1f5e8-fe0f",
        })
        {
            var candidates = EmojiPatchService.BuildCandidateNames(name);
            CollectionAssert.AllItemsAreUnique(candidates, $"duplicates for {name}");
        }
    }
}

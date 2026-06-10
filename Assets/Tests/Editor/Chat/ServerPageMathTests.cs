using NUnit.Framework;

public class ServerPageMathTests
{
    private const int PageSize = 50;

    // --- PageContaining ---

    [TestCase(0, 1)]
    [TestCase(49, 1)]
    [TestCase(50, 2)]
    [TestCase(99, 2)]
    [TestCase(100, 3)]
    public void PageContaining_MapsOffsetToOneBasedPage(int offset, int expectedPage)
    {
        Assert.AreEqual(expectedPage, ServerPageMath.PageContaining(offset, PageSize));
    }

    [Test]
    public void PageContaining_NegativeOffset_ClampsToFirstPage()
    {
        Assert.AreEqual(1, ServerPageMath.PageContaining(-5, PageSize));
    }

    [Test]
    public void PageContaining_ZeroPageSize_ReturnsFirstPage()
    {
        Assert.AreEqual(1, ServerPageMath.PageContaining(120, 0));
    }

    // --- NextServerPage ---

    // Regression: un-cached chat. Open fetched page 1 (50 messages), the
    // scroll-up drain of page 1's queued tail consumed a page number, and the
    // old counter then asked for page 3 — dropping page 2 (offsets 50-99)
    // from the rendered history entirely.
    [Test]
    public void AfterPageOneFullyServed_ResumesAtPageTwo()
    {
        Assert.AreEqual(2, ServerPageMath.NextServerPage(50, 1, PageSize));
    }

    [Test]
    public void FilteredPageOne_StillResumesAtPageTwo()
    {
        // Two of page 1's messages were filtered out (Unknown type): only 48
        // served, but the page itself was fully consumed — don't refetch it.
        Assert.AreEqual(2, ServerPageMath.NextServerPage(48, 1, PageSize));
    }

    // Regression: cached chat. A 100-message cache covers server pages 1-2;
    // the old counter advanced to 3 during the two drains, so the first real
    // fetch asked for page 4 and offsets 100-149 were skipped.
    [Test]
    public void FullCacheServed_ResumesAtPageThree()
    {
        Assert.AreEqual(3, ServerPageMath.NextServerPage(100, 0, PageSize));
    }

    [Test]
    public void PartialCacheServed_RoundsDownToOverlapNotGap()
    {
        // 85 cached messages: offsets 85-99 of page 2 were never fetched.
        // Resume AT page 2 — the 35 already-seen entries dedup away, the 15
        // unseen ones render. Rounding up here would lose them.
        Assert.AreEqual(2, ServerPageMath.NextServerPage(85, 0, PageSize));
    }

    [Test]
    public void SmallCacheServed_ResumesAtPageOne()
    {
        Assert.AreEqual(1, ServerPageMath.NextServerPage(30, 0, PageSize));
    }

    [Test]
    public void NothingServed_StartsAtPageOne()
    {
        Assert.AreEqual(1, ServerPageMath.NextServerPage(0, 0, PageSize));
    }

    [Test]
    public void NeverRefetchesBelowLastFetchedPage()
    {
        // Server fetches already reached page 3; a lower served count (live
        // arrivals don't add to it) must not pull the cursor backwards.
        Assert.AreEqual(4, ServerPageMath.NextServerPage(85, 3, PageSize));
    }
}

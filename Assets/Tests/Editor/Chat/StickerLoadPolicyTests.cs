using System;
using NUnit.Framework;

public class StickerLoadPolicyTests
{
    private static byte[] WebP(int trailing = 8)
    {
        byte[] header =
        {
            (byte)'R', (byte)'I', (byte)'F', (byte)'F',
            0x00, 0x00, 0x00, 0x00,
            (byte)'W', (byte)'E', (byte)'B', (byte)'P'
        };
        byte[] withBody = new byte[header.Length + Math.Max(0, trailing)];
        Array.Copy(header, withBody, header.Length);
        return withBody;
    }

    private static byte[] Junk => new byte[] { 0xEF, 0xBB, 0xBF, (byte)'N', (byte)'a', (byte)'m', (byte)'e' }; // BOM + "Name" (the CSV)

    [Test]
    public void ValidWebP_OnFirstAttempt_Renders()
    {
        Assert.AreEqual(StickerLoadAction.Render, StickerLoadPolicy.Decide(WebP(), 0, 3));
    }

    [Test]
    public void ValidWebP_OnLastAttempt_StillRenders()
    {
        Assert.AreEqual(StickerLoadAction.Render, StickerLoadPolicy.Decide(WebP(), 2, 3));
    }

    [Test]
    public void Junk_WithAttemptsRemaining_Retries()
    {
        Assert.AreEqual(StickerLoadAction.Retry, StickerLoadPolicy.Decide(Junk, 0, 3));
        Assert.AreEqual(StickerLoadAction.Retry, StickerLoadPolicy.Decide(Junk, 1, 3));
    }

    [Test]
    public void Junk_OnLastAttempt_GivesUp()
    {
        Assert.AreEqual(StickerLoadAction.GiveUp, StickerLoadPolicy.Decide(Junk, 2, 3));
    }

    [Test]
    public void FetchFailure_NullBytes_RetriesThenGivesUp()
    {
        Assert.AreEqual(StickerLoadAction.Retry, StickerLoadPolicy.Decide(null, 0, 3));
        Assert.AreEqual(StickerLoadAction.GiveUp, StickerLoadPolicy.Decide(null, 2, 3));
    }

    [Test]
    public void SingleAttemptCap_JunkGivesUpImmediately()
    {
        Assert.AreEqual(StickerLoadAction.GiveUp, StickerLoadPolicy.Decide(Junk, 0, 1));
    }

    [Test]
    public void SingleAttemptCap_ValidWebPStillRenders()
    {
        Assert.AreEqual(StickerLoadAction.Render, StickerLoadPolicy.Decide(WebP(), 0, 1));
    }
}

using NUnit.Framework;
using UnityEngine;

public class ResizeEdgeRepairTests
{
    private const float Eps = 1.5f / 255f;   // one 8-bit step of RGB24 quantization

    // ── builders ──────────────────────────────────────────────────

    private static Texture2D Solid(int w, int h, float gray)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        var px = new Color[w * h];
        var c = new Color(gray, gray, gray);
        for (int i = 0; i < px.Length; i++) px[i] = c;
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    private static void SetColumn(Texture2D tex, int x, float gray)
    {
        var col = new Color[tex.height];
        var c = new Color(gray, gray, gray);
        for (int i = 0; i < col.Length; i++) col[i] = c;
        tex.SetPixels(x, 0, 1, tex.height, col);
        tex.Apply();
    }

    private static void SetRow(Texture2D tex, int y, float gray)
    {
        var row = new Color[tex.width];
        var c = new Color(gray, gray, gray);
        for (int i = 0; i < row.Length; i++) row[i] = c;
        tex.SetPixels(0, y, tex.width, 1, row);
        tex.Apply();
    }

    private static float Mean(Color[] line)
    {
        float s = 0f;
        foreach (var c in line) s += (c.r + c.g + c.b) / 3f;
        return s / line.Length;
    }

    // ── portrait (shorter dim = width -> vertical line) ───────────

    [Test]
    public void Portrait_DarkRightColumn_IsRepairedFromNeighbor()
    {
        var tex = Solid(60, 100, 1f);
        SetColumn(tex, 59, 0.45f);   // the baked-in dark trailing edge

        bool repaired = ResizeEdgeRepair.Repair(tex, 100);

        Assert.IsTrue(repaired);
        float edge = Mean(tex.GetPixels(59, 0, 1, 100));
        float neighbor = Mean(tex.GetPixels(58, 0, 1, 100));
        Assert.AreEqual(neighbor, edge, Eps, "right column should match its neighbor after repair");
        Assert.AreEqual(1f, edge, Eps, "right column should be restored to the white body");
    }

    [Test]
    public void Portrait_DarkLeftColumn_IsRepaired()
    {
        // A platform whose scaler darkens the leading edge instead — both ends are checked.
        var tex = Solid(60, 100, 1f);
        SetColumn(tex, 0, 0.45f);

        bool repaired = ResizeEdgeRepair.Repair(tex, 100);

        Assert.IsTrue(repaired);
        Assert.AreEqual(1f, Mean(tex.GetPixels(0, 0, 1, 100)), Eps);
    }

    // ── landscape (shorter dim = height -> horizontal line) ───────

    [Test]
    public void Landscape_DarkBottomRow_IsRepaired()
    {
        var tex = Solid(100, 60, 1f);
        SetRow(tex, 0, 0.45f);

        bool repaired = ResizeEdgeRepair.Repair(tex, 100);

        Assert.IsTrue(repaired);
        Assert.AreEqual(1f, Mean(tex.GetPixels(0, 0, 100, 1)), Eps);
    }

    [Test]
    public void Landscape_DarkTopRow_IsRepaired()
    {
        var tex = Solid(100, 60, 1f);
        SetRow(tex, 59, 0.45f);

        bool repaired = ResizeEdgeRepair.Repair(tex, 100);

        Assert.IsTrue(repaired);
        Assert.AreEqual(1f, Mean(tex.GetPixels(0, 59, 100, 1)), Eps);
    }

    // ── no-op cases ───────────────────────────────────────────────

    [Test]
    public void CleanTexture_IsNotRepaired()
    {
        var tex = Solid(60, 100, 1f);
        Assert.IsFalse(ResizeEdgeRepair.Repair(tex, 100));
    }

    [Test]
    public void GentleGradient_IsNotMistakenForAnEdgeArtifact()
    {
        // A smooth horizontal gradient: every column differs slightly from its neighbor, but no
        // single edge column dominates -> must not be "repaired".
        var tex = new Texture2D(60, 100, TextureFormat.RGB24, false);
        var px = new Color[60 * 100];
        for (int y = 0; y < 100; y++)
            for (int x = 0; x < 60; x++)
            {
                float g = 0.2f + 0.6f * (x / 59f);
                px[y * 60 + x] = new Color(g, g, g);
            }
        tex.SetPixels(px);
        tex.Apply();

        Assert.IsFalse(ResizeEdgeRepair.Repair(tex, 100));
    }

    [Test]
    public void NotDownscaledToMaxSize_IsSkipped()
    {
        var tex = Solid(60, 100, 1f);
        SetColumn(tex, 59, 0.45f);
        Assert.IsFalse(ResizeEdgeRepair.Repair(tex, 512), "long edge != maxSize -> no resize happened");
    }

    [Test]
    public void Square_IsSkipped()
    {
        var tex = Solid(80, 80, 1f);
        SetColumn(tex, 79, 0.45f);
        Assert.IsFalse(ResizeEdgeRepair.Repair(tex, 80));
    }

    [Test]
    public void TooSmall_IsSkipped()
    {
        var tex = Solid(2, 100, 1f);
        Assert.IsFalse(ResizeEdgeRepair.Repair(tex, 100));
    }

    [Test]
    public void NullTexture_IsSkipped()
    {
        Assert.IsFalse(ResizeEdgeRepair.Repair(null, 1024));
    }
}

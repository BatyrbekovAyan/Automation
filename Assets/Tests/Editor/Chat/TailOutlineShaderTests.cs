using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Device-only rendering guard for <c>UI/TailDilatedOutline</c>.
///
/// The dilation loop samples the tail sprite nine times per pixel, and those samples
/// used to be implicit-derivative <c>tex2D()</c> calls issued from INSIDE the
/// out-of-bounds early return in <c>SampleArtAlpha</c> — i.e. from divergent control
/// flow, where the mip level a <c>tex2D</c> picks is undefined by the spec.
///
/// Desktop compilers (what the Editor runs) flatten that branch, the derivatives stay
/// defined, and the Editor renders a clean tail. The mobile compilers keep the branch:
/// the LOD collapses toward the smallest mip of <c>Tail.png</c>, whose average alpha is
/// ~0.24 — so the WHOLE outline quad rendered as a uniform 24 %-opacity BubbleBorder
/// rectangle. Over the dark wallpaper that is rgb(19,22,27) against rgb(9,11,14): the
/// faint grey box reported around every tail on device, invisible in the Editor.
///
/// The fix samples at an EXPLICIT mip level and masks the out-of-bounds region
/// branchlessly. Nothing in EditMode can render a shader, so these asserts are what
/// keeps the hazard from coming back — do not relax them into "tex2D is fine here".
/// </summary>
public class TailOutlineShaderTests
{
    private const string ShaderRelativePath = "Shaders/TailDilatedOutline.shader";

    private static string ReadShaderSource()
    {
        string path = Path.Combine(Application.dataPath, ShaderRelativePath);
        Assert.IsTrue(File.Exists(path), $"Shader missing at {path}");
        return File.ReadAllText(path);
    }

    /// <summary>Body of a top-level function, found by brace matching from its signature.</summary>
    private static string ExtractFunctionBody(string source, string signatureFragment)
    {
        int start = source.IndexOf(signatureFragment, System.StringComparison.Ordinal);
        Assert.Greater(start, -1, $"Could not find '{signatureFragment}' in the shader");
        int open = source.IndexOf('{', start);
        Assert.Greater(open, -1, $"'{signatureFragment}' has no body");

        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0) return source.Substring(open, i - open + 1);
            }
        }

        Assert.Fail($"Unbalanced braces after '{signatureFragment}'");
        return null;
    }

    [Test]
    public void DilationSampler_UsesExplicitLod_NotImplicitDerivativeTex2D()
    {
        string source = ReadShaderSource();

        // \b...\( with a negative lookahead so tex2Dlod/tex2Dgrad/tex2Dbias do not match.
        MatchCollection implicitSamples = Regex.Matches(source, @"\btex2D(?!lod|grad|bias)\s*\(");
        Assert.AreEqual(
            0,
            implicitSamples.Count,
            "UI/TailDilatedOutline must not sample with implicit-derivative tex2D: the dilation " +
            "samples run inside divergent control flow, where the LOD is undefined on the mobile " +
            "shader compilers and collapses to the smallest mip (a filled grey box around the tail).");

        StringAssert.Contains(
            "tex2Dlod(",
            source,
            "The dilation must sample at an explicit mip level.");
    }

    [Test]
    public void SampleArtAlpha_IsBranchless()
    {
        string body = ExtractFunctionBody(ReadShaderSource(), "float SampleArtAlpha");

        Assert.IsFalse(
            Regex.IsMatch(body, @"\bif\s*\("),
            "SampleArtAlpha must mask its out-of-bounds region arithmetically, not with a branch — " +
            "a branch around the sample is what made the LOD undefined on device.");
    }

    [Test]
    public void ShaderDeclaresShaderModel3_ForFragmentLodSampling()
    {
        string source = ReadShaderSource();

        Match target = Regex.Match(source, @"#pragma\s+target\s+(\d+(?:\.\d+)?)");
        Assert.IsTrue(target.Success, "Shader must declare '#pragma target' — tex2Dlod in a fragment " +
                                      "shader needs shader model 3.0, above Unity's 2.5 default.");
        Assert.GreaterOrEqual(
            float.Parse(target.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
            3.0f,
            "tex2Dlod in a fragment shader requires shader model 3.0 or higher.");
    }
}

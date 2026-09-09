using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

/// <summary>
/// Source guard for the Android keyboard readers (2026-09-09 device pass).
///
/// android.graphics.Rect exposes top/bottom/left/right as public FIELDS. Reading them through
/// AndroidJavaObject.Call&lt;int&gt;("bottom") throws java.lang.NoSuchMethodError, and because
/// every reader wraps its JNI in a bare catch that returns 0, the failure was invisible: the
/// keyboard height was 0 on every Android device, the chat composer slid under the IME, and
/// the settings sheets/forms never lifted. EditMode cannot execute JNI, so the accessor is
/// pinned at the source: a Rect field must be read with Get&lt;&gt;.
/// </summary>
public class AndroidRectJniGuardTests
{
    private static readonly string[] Readers =
    {
        "Assets/Scripts/Main/KeyboardInset.cs",
        "Assets/Scripts/Main/BotSettings/ItemEditSheet.cs",
        "Assets/Scripts/Main/KeyboardScrollFix.cs",
    };

    private static readonly Regex RectFieldAsMethod =
        new Regex(@"\.Call<[^>]+>\(\s*""(top|bottom|left|right)""\s*\)", RegexOptions.Compiled);

    private static readonly Regex RectFieldAsField =
        new Regex(@"visibleRect\.Get<int>\(\s*""bottom""\s*\)", RegexOptions.Compiled);

    [Test]
    public void EveryReader_ReadsRectBottomAsAField()
    {
        foreach (var path in Readers)
        {
            Assert.IsTrue(File.Exists(path), path);
            string source = File.ReadAllText(path);

            var bad = RectFieldAsMethod.Match(source);
            Assert.IsFalse(bad.Success,
                $"{Path.GetFileName(path)}: '{bad.Value}' calls a Rect FIELD as a method — NoSuchMethodError at runtime, swallowed by the catch. Use Get<int>.");
            Assert.IsTrue(RectFieldAsField.IsMatch(source),
                $"{Path.GetFileName(path)}: expected visibleRect.Get<int>(\"bottom\") in the visible-frame reader");
        }
    }
}

using NUnit.Framework;

public class VideoConverterTests
{
    // In the Editor (UNITY_EDITOR defined), the iOS branch is compiled out, so
    // Convert must pass the original path straight through with no conversion.
    [Test]
    public void Convert_InEditor_PassesInputPathThroughToOnResult()
    {
        string result = null;
        string error = null;

        var routine = VideoConverter.Convert(
            "/tmp/in.mov", "/tmp/out.mp4", 16L * 1024 * 1024,
            r => result = r,
            e => error = e);

        // Drive the coroutine to completion synchronously (Editor path is immediate).
        while (routine.MoveNext()) { }

        Assert.AreEqual("/tmp/in.mov", result);
        Assert.IsNull(error);
    }
}

using System;
using System.IO;
using System.Threading.Tasks;

/// <summary>
/// Reads a file and base64-encodes it on a thread-pool thread so the ~33%
/// size inflation + large-buffer allocation never blocks a Unity frame.
/// The returned Task faults (FileNotFoundException / IOException) if the
/// file is gone — callers treat a faulted task as a send failure.
/// </summary>
public static class Base64Encoder
{
    public static Task<string> EncodeFileAsync(string path) => Task.Run(() =>
    {
        byte[] bytes = File.ReadAllBytes(path);
        return Convert.ToBase64String(bytes);
    });
}

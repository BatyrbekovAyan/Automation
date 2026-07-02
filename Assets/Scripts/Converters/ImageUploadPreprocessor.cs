using UnityEngine;

/// <summary>
/// One path for every picker-provided image regardless of source or format:
/// native decode (HEIC works on device; the Editor fallback covers png/jpg),
/// downscale to MaxDimension, re-encode as JPEG. Returns null when the file
/// is missing or undecodable — callers turn that into a failed upload row.
/// </summary>
public static class ImageUploadPreprocessor
{
    public const int MaxDimension = 2048;
    public const int JpegQuality = 85;

    public static byte[] ToJpegPayload(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath)) return null;

        Texture2D texture = NativeGallery.LoadImageAtPath(filePath, MaxDimension, markTextureNonReadable: false);
        if (texture == null) return null;
        try
        {
            if (texture.width < 8 || texture.height < 8) return null; // degenerate decode
            ResizeEdgeRepair.Repair(texture, MaxDimension); // guards the native fractional-rect edge artifact
            return texture.EncodeToJPG(JpegQuality);
        }
        finally
        {
#if UNITY_EDITOR
            Object.DestroyImmediate(texture);
#else
            Object.Destroy(texture);
#endif
        }
    }
}

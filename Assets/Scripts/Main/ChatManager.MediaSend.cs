using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Media-attachment send concerns split out of ChatManager — keeps the
/// god-object trimmer and groups related behavior. Mirrors ChatManager.Outbox.cs
/// and ChatManager.BotState.cs. Houses optimistic staging, the per-kind cache
/// seed helpers, and (Task 5) the Wappi upload + reconcile coroutine.
/// </summary>
public partial class ChatManager
{
    /// <summary>
    /// Part "b" optimistic-staging for media attachments. Builds a
    /// MessageViewModel from the AttachmentPick + caption, pre-seeds the
    /// image/video thumbnail into MediaCacheManager under a synthetic
    /// "staged://" URL so existing bubble views render unchanged, then
    /// fires OnLiveMessagesReceived. Does NOT persist (no ChatHistoryCache,
    /// no Outbox) and does NOT upload to Wappi — part "c" replaces this body
    /// with the real upload + persist path.
    /// </summary>
    public void StageLocalMedia(AttachmentPick pick, string caption, Texture2D preloadedImage = null)
    {
        if (string.IsNullOrEmpty(currentChatId)) return;
        if (pick == null || string.IsNullOrEmpty(pick.Path)) return;

        string tempId = "staging_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        seenMessageIds.Add(tempId);

        var vm = new MessageViewModel
        {
            messageId      = tempId,
            chatId         = currentChatId,
            senderName     = "Me",
            isIncoming     = false,
            timestamp      = now,
            text           = caption ?? "",
            mimeType       = pick.MimeType,
            fileName       = pick.FileName,
            fileSize       = pick.FileSizeBytes,
            deliveryStatus = DeliveryStatus.Pending,
        };

        switch (pick.Kind)
        {
            case AttachmentKind.Photo:
            case AttachmentKind.GalleryImage:
                vm.type = MessageType.Image;
                if (preloadedImage != null)
                {
                    vm.mediaUrl    = SeedImageCacheFromTexture(preloadedImage, tempId);
                    vm.aspectRatio = preloadedImage.height > 0
                                   ? (float)preloadedImage.width / preloadedImage.height
                                   : 1f;
                }
                else
                {
                    // Fallback: only reached if AttachmentPreviewScreen.LoadTextureFromFile
                    // returned null (decode failed). SeedImageCache will try the same path
                    // again and likely fail the same way — known performance cliff on the
                    // already-failing path. Part c's outbox-retry caller may legitimately
                    // call StageLocalMedia without a preloaded texture; revisit the
                    // double-decode shape then.
                    var img = SeedImageCache(pick.Path, tempId);
                    vm.mediaUrl    = img.syntheticUrl;
                    vm.aspectRatio = img.aspect;
                }
                break;

            case AttachmentKind.GalleryVideo:
                vm.type         = MessageType.Video;
                vm.thumbnailUrl = SeedVideoThumbCache(pick.Path, tempId);
                vm.videoUrl     = "file://" + pick.Path;
                var meta = ReadVideoMetadata(pick.Path);
                vm.aspectRatio  = meta.aspect;
                vm.duration     = meta.durationSec;
                break;

            case AttachmentKind.Document:
                vm.type = MessageType.Document;
                // Non-empty mediaUrl bypasses MessageItemView's isMissing guard at line 615.
                // The staged:// URL is a placeholder — document bubbles don't decode it, only
                // check it's non-empty. Tap-to-open won't work in part b (no real upload yet)
                // but the bubble visually renders with the file icon + name + size.
                //
                // The document path at MessageItemView.cs:616 also checks
                //   isLinkExpired = vm.expireTime > 0 && vm.expireTime < now
                // which evaluates to false because vm.expireTime stays at 0 here. If a
                // future change defaults vm.expireTime to "now" for staged messages
                // (e.g. for UI timestamp display), the needsDownload guard would flip
                // true and re-break this. Keep expireTime at 0 for staged documents.
                vm.mediaUrl = $"staged://document/{tempId}";
                break;
        }

        OnLiveMessagesReceived?.Invoke(new List<MessageViewModel> { vm });
    }

    private (string syntheticUrl, float aspect) SeedImageCache(string localPath, string tempId)
    {
        string syntheticUrl = $"staged://image/{tempId}";
        Texture2D tex = null;
        try
        {
            // NativeGallery.LoadImageAtPath decodes HEIC → RGBA natively on iOS.
            // markTextureNonReadable: false so we can EncodeToJPG below.
            tex = NativeGallery.LoadImageAtPath(localPath,
                                                markTextureNonReadable: false,
                                                generateMipmaps: false);
            if (tex == null)
            {
                Debug.LogWarning($"[ChatManager] SeedImageCache: LoadImageAtPath returned null for {localPath}");
                return (syntheticUrl, 1.0f);
            }

            // Re-encode as JPEG so MediaCacheManager's downstream Texture2D.LoadImage
            // (which is JPG/PNG only) reads it back successfully — even if the source
            // file was HEIC.
            byte[] jpgBytes = tex.EncodeToJPG(90);
            string targetPath = MediaCacheManager.Instance.GetFilePathFromUrl(syntheticUrl);
            System.IO.File.WriteAllBytes(targetPath, jpgBytes);

            float aspect = tex.height > 0 ? (float)tex.width / tex.height : 1.0f;
            return (syntheticUrl, aspect);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[ChatManager] SeedImageCache failed for {localPath}: {ex.Message}");
            return (syntheticUrl, 1.0f);
        }
        finally
        {
            if (tex != null) UnityEngine.Object.Destroy(tex);
        }
    }

    private string SeedImageCacheFromTexture(Texture2D tex, string tempId)
    {
        string syntheticUrl = $"staged://image/{tempId}";
        if (tex == null) return syntheticUrl;
        if (MediaCacheManager.Instance == null)
        {
            Debug.LogWarning("[ChatManager] SeedImageCacheFromTexture: MediaCacheManager.Instance is null");
            return syntheticUrl;
        }
        try
        {
            // Re-encode as JPEG so MediaCacheManager's downstream Texture2D.LoadImage
            // (JPG/PNG only) reads it back successfully. Same scheme as SeedImageCache,
            // but skips the file-path decode entirely because the caller already has
            // a decoded Texture2D in hand.
            byte[] jpgBytes = tex.EncodeToJPG(90);
            string targetPath = MediaCacheManager.Instance.GetFilePathFromUrl(syntheticUrl);
            System.IO.File.WriteAllBytes(targetPath, jpgBytes);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[ChatManager] SeedImageCacheFromTexture failed: {ex.Message}");
        }
        return syntheticUrl;
    }

    private string SeedVideoThumbCache(string localPath, string tempId)
    {
        string syntheticUrl = $"thumb://staged/{tempId}";
        Texture2D thumb = null;
        try
        {
            thumb = NativeGallery.GetVideoThumbnail(localPath);
            if (thumb == null) return syntheticUrl;
            byte[] png = thumb.EncodeToPNG();
            string targetPath = MediaCacheManager.Instance.GetFilePathFromUrl(syntheticUrl);
            System.IO.File.WriteAllBytes(targetPath, png);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[ChatManager] SeedVideoThumbCache failed for {localPath}: {ex.Message}");
        }
        finally
        {
            if (thumb != null) UnityEngine.Object.Destroy(thumb);
        }
        return syntheticUrl;
    }

    private (float aspect, int durationSec) ReadVideoMetadata(string path)
    {
        try
        {
            var props = NativeGallery.GetVideoProperties(path);
            float aspect = props.height > 0 ? (float)props.width / props.height : 1.0f;
            int durationSec = (int)(props.duration / 1000);
            return (aspect, durationSec);
        }
        catch { return (1.0f, 0); }
    }
}

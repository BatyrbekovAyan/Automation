// Converts a picked video to MP4 (H.264/AAC) for Wappi upload.
// HEVC / .mov / high-bitrate sources are transcoded to <=720p H.264 at ~2 Mbps via an
// AVAssetReader -> AVAssetWriter pipeline; an already-small MP4/H.264 is used as-is.
// Exposed to Unity as start/poll/progress/error/free C functions (see VideoConverter.cs).
#import <Foundation/Foundation.h>
#import <AVFoundation/AVFoundation.h>
#import <CoreMedia/CoreMedia.h>
#import <AudioToolbox/AudioToolbox.h>

// status: 0 running, 1 done, 2 failed, 3 use-original-as-is
typedef struct {
    int   status;
    float progress;   // 0..1 during transcode
    char *error;      // strdup'd on failure
} ConvertJob;

static NSMutableDictionary<NSNumber *, NSValue *> *gJobs = nil;
static NSObject *gLock = nil;
static int gNextJob = 1;

// Encoder budget (WhatsApp-style: size-targeted so the upload body stays under
// Wappi's ~16 MB limit regardless of clip length).
static const int  kMaxLongSide        = 1280;
static const int  kMaxShortSide       = 720;
static const long kTargetFileBytes    = 9L * 1024 * 1024;  // aim each transcode at ~9 MB (body ~12 MB)
static const int  kMinVideoBitrate    = 200000;   // floor for encode validity; clips too long to fit at this rate exceed the cap and fail cleanly
static const int  kMaxVideoBitrate    = 6000000;  // ceiling so short clips don't bloat
static const int  kDefaultVideoBitrate = 2000000; // fallback when duration is unknown
static const int  kAudioBitrate       = 128000;   // 128 kbps
static const long kUseAsIsMaxBitrate  = 2500000;  // already small enough -> skip re-encode

static void EnsureInit() {
    static dispatch_once_t once;
    dispatch_once(&once, ^{ gJobs = [NSMutableDictionary dictionary]; gLock = [NSObject new]; });
}

static void SetJob(int jobId, int status, const char *err) {
    @synchronized (gLock) {
        NSValue *v = gJobs[@(jobId)];
        if (!v) return;
        ConvertJob *job = (ConvertJob *)v.pointerValue;
        if (job->status != 0) return;   // already terminal; never overwrite
        if (err) { if (job->error) free(job->error); job->error = strdup(err); }
        job->status = status;
    }
}

static void SetProgress(int jobId, float p) {
    @synchronized (gLock) {
        NSValue *v = gJobs[@(jobId)];
        if (!v) return;
        ((ConvertJob *)v.pointerValue)->progress = p;
    }
}

static int EvenClamp(CGFloat x) { int v = (int)(x + 0.5); v -= (v % 2); return v < 2 ? 2 : v; }

// AVAssetReader -> AVAssetWriter transcode to <=720p H.264 @ ~2 Mbps + AAC 128k MP4.
static void TranscodeReaderWriter(AVURLAsset *asset, NSString *outPath, int jobId) {
    NSError *err = nil;

    AVAssetTrack *vtrack = [asset tracksWithMediaType:AVMediaTypeVideo].firstObject;
    if (vtrack == nil) { SetJob(jobId, 2, "no video track"); return; }

    AVAssetReader *reader = [AVAssetReader assetReaderWithAsset:asset error:&err];
    if (reader == nil) { SetJob(jobId, 2, (err.localizedDescription ?: @"reader init failed").UTF8String); return; }

    [[NSFileManager defaultManager] removeItemAtPath:outPath error:nil];
    AVAssetWriter *writer = [AVAssetWriter assetWriterWithURL:[NSURL fileURLWithPath:outPath] fileType:AVFileTypeMPEG4 error:&err];
    if (writer == nil) { SetJob(jobId, 2, (err.localizedDescription ?: @"writer init failed").UTF8String); return; }
    writer.shouldOptimizeForNetworkUse = YES;

    // Target size: fit the displayed frame within (1280 x 720), preserve aspect, never upscale.
    CGSize natural = vtrack.naturalSize;
    CGAffineTransform tx = vtrack.preferredTransform;
    // Strip translation before sizing (defensive; affects only odd legacy transforms).
    CGAffineTransform rot = CGAffineTransformMake(tx.a, tx.b, tx.c, tx.d, 0, 0);
    CGSize disp = CGSizeApplyAffineTransform(natural, rot);
    CGFloat dispW = fabs(disp.width), dispH = fabs(disp.height);
    CGFloat longer = MAX(dispW, dispH), shorter = MIN(dispW, dispH);
    CGFloat scale = (longer > 0 && shorter > 0)
        ? MIN((CGFloat)kMaxLongSide / longer, (CGFloat)kMaxShortSide / shorter) : 1.0;
    if (scale > 1.0) scale = 1.0;
    int outW = EvenClamp(natural.width * scale);
    int outH = EvenClamp(natural.height * scale);

    // Size-targeted bitrate: derive from duration to keep the file near kTargetFileBytes
    // (short clips get more, long clips less). Clamped for encode validity / no bloat.
    CMTime dur = asset.duration;
    double durSec = (dur.timescale > 0) ? (double)dur.value / dur.timescale : 0;
    long videoBitrate = (durSec > 0)
        ? (long)((double)kTargetFileBytes * 8.0 / durSec) - kAudioBitrate
        : kDefaultVideoBitrate;
    if (videoBitrate < kMinVideoBitrate) videoBitrate = kMinVideoBitrate;
    if (videoBitrate > kMaxVideoBitrate) videoBitrate = kMaxVideoBitrate;

    AVAssetReaderTrackOutput *vOut = [AVAssetReaderTrackOutput
        assetReaderTrackOutputWithTrack:vtrack
        outputSettings:@{
            (id)kCVPixelBufferPixelFormatTypeKey: @(kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange),
            (id)kCVPixelBufferWidthKey:  @(outW),
            (id)kCVPixelBufferHeightKey: @(outH)
        }];
    vOut.alwaysCopiesSampleData = NO;
    if (![reader canAddOutput:vOut]) { SetJob(jobId, 2, "cannot add video reader output"); return; }
    [reader addOutput:vOut];

    AVAssetWriterInput *vIn = [AVAssetWriterInput
        assetWriterInputWithMediaType:AVMediaTypeVideo
        outputSettings:@{
            AVVideoCodecKey: AVVideoCodecTypeH264,
            AVVideoWidthKey: @(outW),
            AVVideoHeightKey: @(outH),
            AVVideoCompressionPropertiesKey: @{
                AVVideoAverageBitRateKey: @(videoBitrate),
                AVVideoMaxKeyFrameIntervalKey: @(60),
                AVVideoProfileLevelKey: AVVideoProfileLevelH264MainAutoLevel
            }
        }];
    vIn.expectsMediaDataInRealTime = NO;
    vIn.transform = tx;   // carry rotation as metadata; encoder scales natural buffers to outW x outH
    if (![writer canAddInput:vIn]) { SetJob(jobId, 2, "cannot add video writer input"); return; }
    [writer addInput:vIn];

    AVAssetTrack *atrack = [asset tracksWithMediaType:AVMediaTypeAudio].firstObject;
    AVAssetReaderTrackOutput *aOut = nil;
    AVAssetWriterInput *aIn = nil;
    if (atrack) {
        aOut = [AVAssetReaderTrackOutput
            assetReaderTrackOutputWithTrack:atrack
            outputSettings:@{ AVFormatIDKey: @(kAudioFormatLinearPCM) }];
        aOut.alwaysCopiesSampleData = NO;
        if ([reader canAddOutput:aOut]) {
            [reader addOutput:aOut];
            aIn = [AVAssetWriterInput
                assetWriterInputWithMediaType:AVMediaTypeAudio
                outputSettings:@{
                    AVFormatIDKey: @(kAudioFormatMPEG4AAC),
                    AVNumberOfChannelsKey: @(2),
                    AVSampleRateKey: @(44100),
                    AVEncoderBitRateKey: @(kAudioBitrate)
                }];
            aIn.expectsMediaDataInRealTime = NO;
            if ([writer canAddInput:aIn]) { [writer addInput:aIn]; } else { aIn = nil; aOut = nil; }
        } else {
            aOut = nil;
        }
    }

    if (![reader startReading]) { SetJob(jobId, 2, (reader.error.localizedDescription ?: @"startReading failed").UTF8String); return; }
    if (![writer startWriting]) { SetJob(jobId, 2, (writer.error.localizedDescription ?: @"startWriting failed").UTF8String); return; }
    [writer startSessionAtSourceTime:kCMTimeZero];

    dispatch_group_t group = dispatch_group_create();

    dispatch_group_enter(group);
    dispatch_queue_t vQ = dispatch_queue_create("videoconv.v", DISPATCH_QUEUE_SERIAL);
    [vIn requestMediaDataWhenReadyOnQueue:vQ usingBlock:^{
        while (vIn.isReadyForMoreMediaData) {
            CMSampleBufferRef sb = [vOut copyNextSampleBuffer];
            if (!sb) {   // end of stream (or reader error)
                [vIn markAsFinished];
                dispatch_group_leave(group);
                return;
            }
            if (durSec > 0) {
                double sec = CMTimeGetSeconds(CMSampleBufferGetPresentationTimeStamp(sb));
                SetProgress(jobId, (float)MIN(1.0, MAX(0.0, sec / durSec)));
            }
            BOOL ok = [vIn appendSampleBuffer:sb];
            CFRelease(sb);
            if (!ok) {   // writer failed mid-encode
                [vIn markAsFinished];
                dispatch_group_leave(group);
                return;
            }
        }
        // isReadyForMoreMediaData == NO: if the writer failed (not backpressure) the
        // block won't be re-invoked, so finish now to avoid a permanent status-0 hang.
        if (writer.status == AVAssetWriterStatusFailed || writer.status == AVAssetWriterStatusCancelled) {
            [vIn markAsFinished];
            dispatch_group_leave(group);
        }
    }];

    if (aIn) {
        dispatch_group_enter(group);
        dispatch_queue_t aQ = dispatch_queue_create("videoconv.a", DISPATCH_QUEUE_SERIAL);
        [aIn requestMediaDataWhenReadyOnQueue:aQ usingBlock:^{
            while (aIn.isReadyForMoreMediaData) {
                CMSampleBufferRef sb = [aOut copyNextSampleBuffer];
                if (!sb) {
                    [aIn markAsFinished];
                    dispatch_group_leave(group);
                    return;
                }
                BOOL ok = [aIn appendSampleBuffer:sb];
                CFRelease(sb);
                if (!ok) {
                    [aIn markAsFinished];
                    dispatch_group_leave(group);
                    return;
                }
            }
            if (writer.status == AVAssetWriterStatusFailed || writer.status == AVAssetWriterStatusCancelled) {
                [aIn markAsFinished];
                dispatch_group_leave(group);
            }
        }];
    }

    dispatch_group_notify(group, dispatch_get_global_queue(QOS_CLASS_DEFAULT, 0), ^{
        if (reader.status == AVAssetReaderStatusFailed) {
            [writer cancelWriting];
            SetJob(jobId, 2, (reader.error.localizedDescription ?: @"reader failed").UTF8String);
            return;
        }
        [writer finishWritingWithCompletionHandler:^{
            if (writer.status == AVAssetWriterStatusCompleted) {
                SetProgress(jobId, 1.0f);
                SetJob(jobId, 1, NULL);
            } else {
                SetJob(jobId, 2, (writer.error.localizedDescription ?: @"writer failed").UTF8String);
            }
        }];
    });
}

extern "C" int _StartVideoConvert(const char *inPathC, const char *outPathC, long maxBytes) {
    EnsureInit();
    NSString *inPath  = [NSString stringWithUTF8String:inPathC];
    NSString *outPath = [NSString stringWithUTF8String:outPathC];

    ConvertJob *job = (ConvertJob *)calloc(1, sizeof(ConvertJob));
    int jobId;
    @synchronized (gLock) { jobId = gNextJob++; gJobs[@(jobId)] = [NSValue valueWithPointer:job]; }

    AVURLAsset *asset = [AVURLAsset URLAssetWithURL:[NSURL fileURLWithPath:inPath] options:nil];
    AVAssetTrack *vtrack = [asset tracksWithMediaType:AVMediaTypeVideo].firstObject;
    if (vtrack == nil) { SetJob(jobId, 2, "no video track"); return jobId; }

    BOOL isH264 = NO;
    NSArray *fmts = vtrack.formatDescriptions;
    if (fmts.count > 0) {
        CMFormatDescriptionRef desc = (__bridge CMFormatDescriptionRef)fmts.firstObject;
        isH264 = (CMFormatDescriptionGetMediaSubType(desc) == kCMVideoCodecType_H264);
    }

    unsigned long long fileSize = 0;
    NSDictionary *attrs = [[NSFileManager defaultManager] attributesOfItemAtPath:inPath error:nil];
    if (attrs) fileSize = [attrs fileSize];
    BOOL isMp4 = [[inPath.pathExtension lowercaseString] isEqualToString:@"mp4"];
    BOOL underCap = (maxBytes <= 0) || (fileSize <= (unsigned long long)maxBytes);

    CMTime dur = asset.duration;
    double durSec = (dur.timescale > 0) ? (double)dur.value / dur.timescale : 0;
    long approxBitrate = (durSec > 0) ? (long)((double)fileSize * 8.0 / durSec) : LONG_MAX;

    // Already small/deliverable -> upload original untouched.
    if (isH264 && isMp4 && underCap && approxBitrate <= kUseAsIsMaxBitrate) {
        SetJob(jobId, 3, NULL);
        return jobId;
    }

    // Everything else (HEVC, .mov, high-bitrate/oversized H.264) -> transcode.
    // Run async so the call returns immediately; Unity polls status/progress.
    dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED, 0), ^{
        TranscodeReaderWriter(asset, outPath, jobId);
    });
    return jobId;
}

extern "C" int _PollVideoConvert(int jobId) {
    @synchronized (gLock) {
        NSValue *v = gJobs[@(jobId)];
        if (!v) return 2;
        return ((ConvertJob *)v.pointerValue)->status;
    }
}

extern "C" float _PollVideoConvertProgress(int jobId) {
    @synchronized (gLock) {
        NSValue *v = gJobs[@(jobId)];
        if (!v) return 0.0f;
        return ((ConvertJob *)v.pointerValue)->progress;
    }
}

extern "C" const char *_VideoConvertError(int jobId) {
    @synchronized (gLock) {
        NSValue *v = gJobs[@(jobId)];
        if (!v) return "unknown job";
        ConvertJob *job = (ConvertJob *)v.pointerValue;
        return job->error ? job->error : "";   // Unity copies via PtrToStringAnsi; do not free here
    }
}

extern "C" void _FreeVideoConvertJob(int jobId) {
    EnsureInit();
    @synchronized (gLock) {
        NSValue *v = gJobs[@(jobId)];
        if (!v) return;
        ConvertJob *job = (ConvertJob *)v.pointerValue;
        if (job->error) free(job->error);
        free(job);
        [gJobs removeObjectForKey:@(jobId)];
    }
}

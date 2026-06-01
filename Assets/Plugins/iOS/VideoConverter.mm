// Converts a picked video to MP4 (H.264/AAC) via AVAssetExportSession.
// Exposed to Unity as start/poll/error C functions (see VideoConverter.cs).
#import <Foundation/Foundation.h>
#import <AVFoundation/AVFoundation.h>
#import <CoreMedia/CoreMedia.h>

// status: 0 running, 1 done, 2 failed, 3 use-original-as-is
typedef struct {
    int   status;
    char *error;   // strdup'd on failure; freed when overwritten
} ConvertJob;

static NSMutableDictionary<NSNumber *, NSValue *> *gJobs = nil;
static NSObject *gLock = nil;
static int gNextJob = 1;

static void EnsureInit() {
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        gJobs = [NSMutableDictionary dictionary];
        gLock = [NSObject new];
    });
}

static void SetJob(int jobId, int status, const char *err) {
    @synchronized (gLock) {
        NSValue *v = gJobs[@(jobId)];
        if (!v) return;
        ConvertJob *job = (ConvertJob *)v.pointerValue;
        if (job->status != 0) return;   // already terminal; never overwrite (keeps the error pointer stable for Unity)
        if (err) { if (job->error) free(job->error); job->error = strdup(err); }
        job->status = status;
    }
}

static void RunExport(AVAssetExportSession *session, int jobId) {
    [session exportAsynchronouslyWithCompletionHandler:^{
        if (session.status == AVAssetExportSessionStatusCompleted) {
            SetJob(jobId, 1, NULL);
        } else {
            NSString *msg = session.error ? session.error.localizedDescription : @"export failed";
            SetJob(jobId, 2, msg.UTF8String);
        }
    }];
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
    BOOL isMp4    = [[inPath.pathExtension lowercaseString] isEqualToString:@"mp4"];
    BOOL underCap = (maxBytes <= 0) || (fileSize <= (unsigned long long)maxBytes);

    // Already an MP4/H.264 under the cap → tell Unity to upload the original untouched.
    if (isH264 && isMp4 && underCap) { SetJob(jobId, 3, NULL); return jobId; }

    // Remux H.264 that's under cap; otherwise transcode/downscale to 720p H.264.
    NSString *preset = (isH264 && underCap) ? AVAssetExportPresetPassthrough
                                            : AVAssetExportPreset1280x720;

    [[NSFileManager defaultManager] removeItemAtPath:outPath error:nil];

    AVAssetExportSession *exp = [[AVAssetExportSession alloc] initWithAsset:asset presetName:preset];
    if (exp == nil) { SetJob(jobId, 2, "could not create export session"); return jobId; }
    exp.outputURL = [NSURL fileURLWithPath:outPath];
    exp.outputFileType = AVFileTypeMPEG4;
    exp.shouldOptimizeForNetworkUse = YES;

    // Passthrough is not always compatible with MP4 output; fall back to transcode.
    if ([preset isEqualToString:AVAssetExportPresetPassthrough]) {
        [exp determineCompatibleFileTypesWithCompletionHandler:^(NSArray<AVFileType> *types) {
            if ([types containsObject:AVFileTypeMPEG4]) {
                RunExport(exp, jobId);
            } else {
                AVAssetExportSession *t = [[AVAssetExportSession alloc] initWithAsset:asset
                                                                          presetName:AVAssetExportPreset1280x720];
                if (t == nil) { SetJob(jobId, 2, "could not create transcode session"); return; }
                t.outputURL = [NSURL fileURLWithPath:outPath];
                t.outputFileType = AVFileTypeMPEG4;
                t.shouldOptimizeForNetworkUse = YES;
                RunExport(t, jobId);
            }
        }];
    } else {
        RunExport(exp, jobId);
    }
    return jobId;
}

extern "C" int _PollVideoConvert(int jobId) {
    EnsureInit();
    @synchronized (gLock) {
        NSValue *v = gJobs[@(jobId)];
        if (!v) return 2;
        return ((ConvertJob *)v.pointerValue)->status;
    }
}

extern "C" const char *_VideoConvertError(int jobId) {
    EnsureInit();
    @synchronized (gLock) {
        NSValue *v = gJobs[@(jobId)];
        if (!v) return "unknown job";
        ConvertJob *job = (ConvertJob *)v.pointerValue;
        return job->error ? job->error : "";   // Unity copies via PtrToStringAnsi; do not free here
    }
}

// Called by VideoConverter.cs once it has consumed the terminal status (and copied
// any error string) so jobs don't accumulate in gJobs across a long session.
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

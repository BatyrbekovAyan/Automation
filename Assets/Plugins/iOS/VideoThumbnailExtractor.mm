#import <Foundation/Foundation.h>
#import <AVFoundation/AVFoundation.h>
#import <UIKit/UIKit.h>

// Job-based async thumbnail extraction from a (remote) video URL, polled from a
// Unity coroutine. AVURLAsset over HTTPS fetches only the byte ranges needed for the
// moov + the keyframe near the requested time — no full download. Mirrors
// VideoConverter.mm's job/poll lifecycle and error-string handling.

typedef NS_ENUM(NSInteger, ThumbJobStatus) {
    ThumbJobRunning = 0,
    ThumbJobDone    = 1,
    ThumbJobFailed  = 2
};

@interface ThumbJob : NSObject
@property (nonatomic) ThumbJobStatus status;
@property (nonatomic, strong) NSString *error;
@property (nonatomic, strong) AVAssetImageGenerator *generator; // retained until generation completes
@end
@implementation ThumbJob
@end

static NSMutableDictionary<NSNumber *, ThumbJob *> *gThumbJobs = nil;
static int gThumbNextId = 1;

static NSMutableDictionary<NSNumber *, ThumbJob *> *ThumbJobs(void) {
    if (gThumbJobs == nil) gThumbJobs = [NSMutableDictionary dictionary];
    return gThumbJobs;
}

extern "C" int _StartThumbExtract(const char *cUrl, const char *cOutPath, double timeSec) {
    @autoreleasepool {
        NSString *urlStr  = cUrl     ? [NSString stringWithUTF8String:cUrl]     : @"";
        NSString *outPath = cOutPath ? [NSString stringWithUTF8String:cOutPath] : @"";

        int jobId = gThumbNextId++;
        ThumbJob *job = [ThumbJob new];
        job.status = ThumbJobRunning;
        ThumbJobs()[@(jobId)] = job;

        NSURL *url = [NSURL URLWithString:urlStr];
        if (url == nil || outPath.length == 0) {
            job.status = ThumbJobFailed;
            job.error  = @"invalid URL or output path";
            return jobId;
        }

        AVURLAsset *asset = [AVURLAsset URLAssetWithURL:url options:nil];
        AVAssetImageGenerator *gen = [[AVAssetImageGenerator alloc] initWithAsset:asset];
        gen.appliesPreferredTrackTransform = YES;                 // correct rotation
        gen.maximumSize = CGSizeMake(640, 640);                   // long-edge cap for a thumbnail
        gen.requestedTimeToleranceBefore = kCMTimeZero;
        gen.requestedTimeToleranceAfter  = CMTimeMakeWithSeconds(1.0, 600); // snap to a nearby keyframe
        job.generator = gen;

        [asset loadValuesAsynchronouslyForKeys:@[@"duration"] completionHandler:^{
            NSError *durErr = nil;
            if ([asset statusOfValueForKey:@"duration" error:&durErr] != AVKeyValueStatusLoaded) {
                job.status = ThumbJobFailed;
                job.error  = durErr ? durErr.localizedDescription : @"asset load failed";
                return;
            }

            Float64 durationSec = CMTimeGetSeconds(asset.duration);
            Float64 t = timeSec;
            if (durationSec > 0 && t > durationSec) t = durationSec * 0.5;
            if (t < 0) t = 0;
            CMTime requested = CMTimeMakeWithSeconds(t, 600);

            [gen generateCGImagesAsynchronouslyForTimes:@[[NSValue valueWithCMTime:requested]]
                completionHandler:^(CMTime requestedTime, CGImageRef image, CMTime actualTime,
                                    AVAssetImageGeneratorResult result, NSError *error) {
                    if (result != AVAssetImageGeneratorSucceeded || image == NULL) {
                        job.status = ThumbJobFailed;
                        job.error  = error ? error.localizedDescription : @"frame generation failed";
                        return;
                    }
                    UIImage *ui  = [UIImage imageWithCGImage:image];
                    NSData  *jpg = UIImageJPEGRepresentation(ui, 0.9);
                    BOOL ok = jpg != nil && [jpg writeToFile:outPath atomically:YES];
                    if (ok) { job.status = ThumbJobDone; }
                    else    { job.status = ThumbJobFailed; job.error = @"write failed"; }
                }];
        }];

        return jobId;
    }
}

extern "C" int _PollThumbExtract(int jobId) {
    ThumbJob *job = ThumbJobs()[@(jobId)];
    if (job == nil) return ThumbJobFailed;
    return (int)job.status;
}

extern "C" const char *_ThumbExtractError(int jobId) {
    ThumbJob *job = ThumbJobs()[@(jobId)];
    if (job == nil || job.error == nil) return "";
    return [job.error UTF8String];   // valid until the job is freed (read immediately by C#)
}

extern "C" void _FreeThumbExtractJob(int jobId) {
    [ThumbJobs() removeObjectForKey:@(jobId)];
}

#import <Foundation/Foundation.h>
#import <AVFoundation/AVFoundation.h>

static AVPlayer *player = nil;
static id timeObserver = nil;
static id endObserver = nil; // --- ADDED: To track when the audio finishes ---
static NSString *currentUrl = nil;
static float desiredSpeed = 1.0f;

// --- ADDED: A safe helper to clean up the player and plug memory leaks ---
void _cleanupPlayer() {
    if (player) {
        [player pause];
        
        if (timeObserver) {
            [player removeTimeObserver:timeObserver];
            timeObserver = nil;
        }
        
        if (endObserver) {
            [[NSNotificationCenter defaultCenter] removeObserver:endObserver];
            endObserver = nil;
        }
        
        player = nil;
    }
}

extern "C"
{

void playUrl(const char* url)
{
    NSString *nsUrl = [NSString stringWithUTF8String:url];
    currentUrl = nsUrl;
    NSURL *audioUrl = [NSURL URLWithString:nsUrl];

    // Safely clear out any old audio playing
    _cleanupPlayer();

    player = [[AVPlayer alloc] initWithURL:audioUrl];
    [player play];
    player.rate = desiredSpeed;

    // --- Progress updates ---
    CMTime interval = CMTimeMake(1, 10); // 0.1 sec — match Android cadence for a smooth waveform fill

    timeObserver =
    [player addPeriodicTimeObserverForInterval:interval
                                         queue:dispatch_get_main_queue()
                                    usingBlock:^(CMTime time)
    {
        if (!player || !player.currentItem) return;
        
        float pos = CMTimeGetSeconds(time);
        float dur = CMTimeGetSeconds(player.currentItem.duration);
        
        // Prevent iOS from sending "NaN" (Not a Number) if the audio is still buffering
        if (isnan(dur)) dur = 0;

        NSString *msg =
            [NSString stringWithFormat:@"%@|%f|%f",
             currentUrl, pos, dur];

        UnitySendMessage("AudioController",
                         "OnNativeProgress",
                         [msg UTF8String]);
    }];
    
    // --- THE FIX: End of track listener ---
    endObserver = 
    [[NSNotificationCenter defaultCenter] addObserverForName:AVPlayerItemDidPlayToEndTimeNotification
                                                      object:player.currentItem
                                                       queue:[NSOperationQueue mainQueue]
                                                  usingBlock:^(NSNotification * _Nonnull note) 
    {
        if (currentUrl) {
            // Tell Unity the audio is officially over!
            UnitySendMessage("AudioController", "OnNativeAudioFinished", [currentUrl UTF8String]);
        }
    }];
}

void pausePlayer()
{
    if (player) [player pause];
}

void resumePlayer()
{
    if (player) { [player play]; player.rate = desiredSpeed; }
}

void stopPlayer()
{
    // Use the safe cleanup method
    _cleanupPlayer();
}

void seekPlayer(float seconds)
{
    if (!player) return;

    CMTime time = CMTimeMakeWithSeconds(seconds, 600);
    [player seekToTime:time];
}

void setSpeed(float speed)
{
    desiredSpeed = speed;
    if (player && player.rate != 0.0f) player.rate = speed; // only while playing
}

}
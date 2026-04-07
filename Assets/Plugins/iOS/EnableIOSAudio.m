#import <AVFoundation/AVFoundation.h>
#import <UIKit/UIKit.h>

// Global trackers for the smart sensor
static BOOL isProximityEnabled = NO;
static BOOL pendingProximityDisable = NO;

// 1. The Mute Switch Fix
void _ForceIOSPlaybackMode() {
    AVAudioSession *session = [AVAudioSession sharedInstance];
    [session setCategory:AVAudioSessionCategoryPlayback error:nil];
    [session setActive:YES error:nil];
}

// Helper to actually shut down the sensor hardware
void _ActuallyDisableProximityIOS() {
    isProximityEnabled = NO;
    pendingProximityDisable = NO;
    
    UIDevice *device = [UIDevice currentDevice];
    [[NSNotificationCenter defaultCenter] removeObserver:device name:UIDeviceProximityStateDidChangeNotification object:nil];
    device.proximityMonitoringEnabled = NO; // Turn screen back on
    
    AVAudioSession *session = [AVAudioSession sharedInstance];
    [session setCategory:AVAudioSessionCategoryPlayback error:nil];
    [session overrideOutputAudioPort:AVAudioSessionPortOverrideSpeaker error:nil]; // Route back to loud speaker
}

// 2. The Smart Proximity Sensor Logic
void _ToggleProximitySensor(bool enable) {
    UIDevice *device = [UIDevice currentDevice];
    
    if (enable) {
        pendingProximityDisable = NO; // Cancel any pending shut-offs
        if (isProximityEnabled) return;
        isProximityEnabled = YES;
        
        device.proximityMonitoringEnabled = YES; // Turn on sensor
        
        [[NSNotificationCenter defaultCenter] addObserverForName:UIDeviceProximityStateDidChangeNotification object:nil queue:[NSOperationQueue mainQueue] usingBlock:^(NSNotification * _Nonnull note) {
            
            AVAudioSession *session = [AVAudioSession sharedInstance];
            
            if (device.proximityState) {
                // Phone is at the ear!
                [session setCategory:AVAudioSessionCategoryPlayAndRecord error:nil];
                [session overrideOutputAudioPort:AVAudioSessionPortOverrideNone error:nil];
            } else {
                // Phone pulled away!
                [session setCategory:AVAudioSessionCategoryPlayback error:nil];
                [session overrideOutputAudioPort:AVAudioSessionPortOverrideSpeaker error:nil];
                
                // If the track ended while they were listening, shut it down now!
                if (pendingProximityDisable) {
                    _ActuallyDisableProximityIOS();
                }
            }
        }];
    } else {
        // Audio stopped natively or by user
        if (!isProximityEnabled && !pendingProximityDisable) return;
        
        if (device.proximityState) {
            // Phone is still at the ear. Wait for them to pull it away!
            pendingProximityDisable = YES;
        } else {
            // Phone is away, shut it down instantly.
            _ActuallyDisableProximityIOS();
        }
    }
}
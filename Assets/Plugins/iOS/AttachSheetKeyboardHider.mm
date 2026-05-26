#import <UIKit/UIKit.h>

// Hides/shows the iOS soft-keyboard window directly via UIKit, bypassing the
// resignFirstResponder slide-down animation. Used by AttachSheet.cs so the
// keyboard appears to "stay still" while our Unity sheet covers its position.
//
// NOTE: matches windows by class-name prefix (`UIRemoteKeyboardWindow`,
// `UITextEffectsWindow`). These are private UIKit classes; their names have
// been stable across iOS 8–17 but may change in future versions. For internal
// builds this is safe; for App Store submission, expect possible review notes.

extern "C" {

void ASKeyboardHider_SetHidden(bool hidden) {
    BOOL targetHidden = hidden ? YES : NO;

    void (^processWindows)(NSArray<UIWindow *> *) = ^(NSArray<UIWindow *> *windows) {
        for (UIWindow *window in windows) {
            NSString *className = NSStringFromClass([window class]);
            if ([className hasPrefix:@"UIRemoteKeyboardWindow"] ||
                [className hasPrefix:@"UITextEffectsWindow"]) {
                window.hidden = targetHidden;
            }
        }
    };

    if (@available(iOS 13.0, *)) {
        for (UIScene *scene in [UIApplication sharedApplication].connectedScenes) {
            if ([scene isKindOfClass:[UIWindowScene class]]) {
                processWindows(((UIWindowScene *)scene).windows);
            }
        }
    } else {
        processWindows([UIApplication sharedApplication].windows);
    }
}

}

#import <UIKit/UIKit.h>

// Forces the system keyboard into its dark appearance by overriding the
// interface style of the app's windows. Unity draws all in-app UI itself,
// so the only visible change is the keyboard (and any system sheet
// presented while the override is active). The override must be cleared
// with enable=false, or every keyboard in the app stays dark.
void _SetDarkKeyboard(bool enable) {
    UIUserInterfaceStyle style = enable ? UIUserInterfaceStyleDark
                                        : UIUserInterfaceStyleUnspecified;
    for (UIWindow *window in [UIApplication sharedApplication].windows) {
        window.overrideUserInterfaceStyle = style;
    }
}

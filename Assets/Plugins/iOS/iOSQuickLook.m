#import <UIKit/UIKit.h>

@interface IOSQuickLook : NSObject <UIDocumentInteractionControllerDelegate>
@property (nonatomic, strong) UIDocumentInteractionController *dic;
@end

@implementation IOSQuickLook

+ (instancetype)sharedInstance {
    static IOSQuickLook *instance = nil;
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        instance = [[IOSQuickLook alloc] init];
    });
    return instance;
}

- (void)preview:(NSString *)path {
    NSURL *url = [NSURL fileURLWithPath:path];
    self.dic = [UIDocumentInteractionController interactionControllerWithURL:url];
    self.dic.delegate = self;
    
    // Grabs the main Unity View Controller to display the preview over
    [self.dic presentPreviewAnimated:YES];
}

// Delegate method required by iOS to know where to render the Quick Look screen
- (UIViewController *)documentInteractionControllerViewControllerForPreview:(UIDocumentInteractionController *)controller {
    return UnityGetGLViewController();
}

@end

// Just expose the function directly, no wrapper needed in a .m file!
void _ShowQuickLook(const char* path) {
    [[IOSQuickLook sharedInstance] preview:[NSString stringWithUTF8String:path]];
}
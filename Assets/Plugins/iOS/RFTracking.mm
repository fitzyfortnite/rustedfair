// Rusted Fair - App Tracking Transparency bridge (v1.0.0).
// AppTrackingTransparency.framework is linked (weak) by RFBuildPost; NSUserTrackingUsageDescription and its
// translations (InfoPlist.strings, 14 languages) are written there too.
#import <Foundation/Foundation.h>
#import <AppTrackingTransparency/AppTrackingTransparency.h>

extern void UnitySendMessage(const char* obj, const char* method, const char* msg);

extern "C" {

int RF_TrackingStatus()
{
    if (@available(iOS 14.5, *))
        return (int)[ATTrackingManager trackingAuthorizationStatus];
    return 3; // before iOS 14.5 no prompt is needed (the minimum deployment target is iOS 15 anyway)
}

void RF_TrackingRequest(const char* receiver)
{
    NSString* target = [NSString stringWithUTF8String:receiver];
    if (@available(iOS 14.5, *))
    {
        [ATTrackingManager requestTrackingAuthorizationWithCompletionHandler:^(ATTrackingManagerAuthorizationStatus status) {
            dispatch_async(dispatch_get_main_queue(), ^{
                NSString* s = [NSString stringWithFormat:@"%d", (int)status];
                UnitySendMessage([target UTF8String], "OnTrackingResult", [s UTF8String]);
            });
        }];
        return;
    }
    UnitySendMessage([target UTF8String], "OnTrackingResult", "3");
}

}

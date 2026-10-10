#import <AVFoundation/AVFoundation.h>
#import <Security/Security.h>
#import <UIKit/UIKit.h>
#import <CoreImage/CoreImage.h>
#include <arpa/inet.h>
#include <mach/mach.h>
#include <os/proc.h>
#include <malloc/malloc.h>
#include <execinfo.h>
#include <dlfcn.h>
#include <pthread.h>
#include <signal.h>
#include <stdatomic.h>
#include <unistd.h>

#define STS2_EXPORT extern "C" __attribute__((visibility("default"), used))

STS2_EXPORT int sts2_audio_activate() {
    AVAudioSession *session = [AVAudioSession sharedInstance];
    NSError *error = nil;
    if (![session setCategory:AVAudioSessionCategoryPlayback mode:AVAudioSessionModeDefault options:0 error:&error])
        return (int)error.code;
    if (![session setActive:YES error:&error])
        return (int)error.code;
    return 0;
}

static NSDictionary *credentialQuery() {
    return @{(__bridge id)kSecClass: (__bridge id)kSecClassGenericPassword,
        (__bridge id)kSecAttrService: [NSBundle.mainBundle.bundleIdentifier stringByAppendingString:@".steam"],
        (__bridge id)kSecAttrAccount: @"refresh-token"};
}

STS2_EXPORT int sts2_keychain_write(const char *value) {
    NSData *data = [[NSString stringWithUTF8String:value] dataUsingEncoding:NSUTF8StringEncoding];
    NSDictionary *attributes = @{(__bridge id)kSecValueData: data,
        (__bridge id)kSecAttrAccessible: (__bridge id)kSecAttrAccessibleWhenUnlockedThisDeviceOnly};
    OSStatus status = SecItemUpdate((__bridge CFDictionaryRef)credentialQuery(), (__bridge CFDictionaryRef)attributes);
    if (status == errSecItemNotFound) {
        NSMutableDictionary *query = [credentialQuery() mutableCopy];
        [query addEntriesFromDictionary:attributes];
        status = SecItemAdd((__bridge CFDictionaryRef)query, nullptr);
    }
    return (int)status;
}

STS2_EXPORT char *sts2_keychain_read() {
    NSMutableDictionary *query = [credentialQuery() mutableCopy];
    query[(__bridge id)kSecReturnData] = @YES;
    CFTypeRef result = nullptr;
    if (SecItemCopyMatching((__bridge CFDictionaryRef)query, &result) != errSecSuccess)
        return nullptr;
    NSData *data = (__bridge_transfer NSData *)result;
    NSString *value = [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
    return value ? strdup(value.UTF8String) : nullptr;
}

STS2_EXPORT int sts2_keychain_delete() {
    OSStatus status = SecItemDelete((__bridge CFDictionaryRef)credentialQuery());
    return status == errSecItemNotFound ? 0 : (int)status;
}

// Device-bound key that encrypts the launcher's credential files. Returns the
// base64 key, creating it on first use, or null when the Keychain cannot serve
// it (for example before first unlock); *status then carries the OSStatus.
STS2_EXPORT char *sts2_vault_key(int *status) {
    NSMutableDictionary *query = [@{(__bridge id)kSecClass: (__bridge id)kSecClassGenericPassword,
        (__bridge id)kSecAttrService: [NSBundle.mainBundle.bundleIdentifier stringByAppendingString:@".vault"],
        (__bridge id)kSecAttrAccount: @"file-key"} mutableCopy];
    NSMutableDictionary *read = [query mutableCopy];
    read[(__bridge id)kSecReturnData] = @YES;
    CFTypeRef result = nullptr;
    OSStatus found = SecItemCopyMatching((__bridge CFDictionaryRef)read, &result);
    NSData *key = nil;
    if (found == errSecSuccess) {
        key = (__bridge_transfer NSData *)result;
    } else if (found == errSecItemNotFound) {
        NSMutableData *fresh = [NSMutableData dataWithLength:32];
        if (SecRandomCopyBytes(kSecRandomDefault, fresh.length, fresh.mutableBytes) != errSecSuccess) {
            *status = errSecAllocate;
            return nullptr;
        }
        query[(__bridge id)kSecValueData] = fresh;
        query[(__bridge id)kSecAttrAccessible] = (__bridge id)kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly;
        found = SecItemAdd((__bridge CFDictionaryRef)query, nullptr);
        if (found == errSecSuccess) key = fresh;
    }
    *status = (int)found;
    if (key.length != 32) return nullptr;
    return strdup([key base64EncodedStringWithOptions:0].UTF8String);
}

// Memory the system charges to this app, the figure its per-app limit is checked against.
STS2_EXPORT unsigned long long sts2_memory_footprint() {
    task_vm_info_data_t info;
    mach_msg_type_number_t count = TASK_VM_INFO_COUNT;
    if (task_info(mach_task_self(), TASK_VM_INFO, (task_info_t)&info, &count) != KERN_SUCCESS) return 0;
    return info.phys_footprint;
}

// Memory the app may still take before the system ends it; with the footprint
// this gives the per-app limit.
STS2_EXPORT unsigned long long sts2_memory_available() { return os_proc_available_memory(); }

// Where the footprint sits: values[0] graphics (Metal and IOSurface memory),
// values[1] malloc heap in use, values[2] compressed, values[3] other anonymous.
STS2_EXPORT void sts2_memory_breakdown(unsigned long long *values) {
    task_vm_info_data_t info;
    mach_msg_type_number_t count = TASK_VM_INFO_COUNT;
    memset(values, 0, 4 * sizeof(*values));
    if (task_info(mach_task_self(), TASK_VM_INFO, (task_info_t)&info, &count) != KERN_SUCCESS) return;
    values[0] = info.ledger_tag_graphics_footprint;
    malloc_statistics_t heap = {};
    malloc_zone_statistics(nullptr, &heap);
    values[1] = heap.size_in_use;
    values[2] = info.compressed;
    values[3] = info.internal;
}

// Diagnostic: when the main thread goes longer than the given time without a
// frame tick, print where it is. Used to find what a long frame is doing.
static _Atomic unsigned long long stallTicks;
static pthread_t stallMainThread;
static void StallSignal(int) {
    void *frames[64];
    int count = backtrace(frames, 64);
    static const char header[] = "[Stall] main thread stack:\n";
    write(STDERR_FILENO, header, sizeof(header) - 1);
    backtrace_symbols_fd(frames, count, STDERR_FILENO);
}
// Sampling profiler for the main thread, sharing the stall signal: while a
// profile runs, each signal stores a stack instead of printing it.
enum { kProfileDepth = 40, kProfileCapacity = 6000 };
static void *profileFrames[kProfileCapacity][kProfileDepth];
static int profileCounts[kProfileCapacity];
static unsigned long long profileTicks[kProfileCapacity];
static _Atomic int profileUsed;
static _Atomic int profiling;

static void ProfileSignal(int signal) {
    if (!profiling) { StallSignal(signal); return; }
    int index = profileUsed;
    if (index >= kProfileCapacity) return;
    profileCounts[index] = backtrace(profileFrames[index], kProfileDepth);
    profileTicks[index] = stallTicks;
    profileUsed = index + 1;
}

// Names a code address: a native symbol, or a Mono method for generated code.
static NSString *ProfileName(void *address, char *(*pmip)(void *)) {
    Dl_info info;
    if (dladdr(address, &info) && info.dli_sname) return @(info.dli_sname);
    if (pmip) {
        char *managed = pmip(address);
        if (managed) {
            NSString *text = @(managed);
            NSRange cut = [text rangeOfString:@" [{"];
            if (cut.location != NSNotFound) text = [text substringToIndex:cut.location];
            return [@"[managed]" stringByAppendingString:text];
        }
    }
    return @"?";
}

STS2_EXPORT void sts2_profile(int milliseconds, int intervalMicroseconds) {
    if (profiling) return;
    dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED, 0), ^{
        profileUsed = 0;
        profiling = 1;
        for (int elapsed = 0; elapsed < milliseconds * 1000; elapsed += intervalMicroseconds) {
            pthread_kill(stallMainThread, SIGINFO);
            usleep(intervalMicroseconds);
        }
        profiling = 0;
        usleep(20 * 1000);
        int samples = profileUsed;

        void *(*attach)(void *) = (void *(*)(void *))dlsym(RTLD_DEFAULT, "mono_jit_thread_attach");
        void *(*root)(void) = (void *(*)(void))dlsym(RTLD_DEFAULT, "mono_get_root_domain");
        char *(*pmip)(void *) = (char *(*)(void *))dlsym(RTLD_DEFAULT, "mono_pmip");
        if (attach && root) attach(root());

        // The frame that collected the most samples is the longest one in the window.
        NSCountedSet<NSNumber *> *perFrame = [NSCountedSet new];
        for (int i = 0; i < samples; i++) [perFrame addObject:@(profileTicks[i])];
        NSNumber *longest = nil;
        for (NSNumber *tick in perFrame) if (!longest || [perFrame countForObject:tick] > [perFrame countForObject:longest]) longest = tick;
        NSMutableDictionary<NSValue *, NSString *> *names = [NSMutableDictionary new];
        for (int pass = 0; pass < 2; pass++) {
            NSMutableDictionary<NSString *, NSNumber *> *inclusive = [NSMutableDictionary new];
            NSMutableDictionary<NSString *, NSNumber *> *leaf = [NSMutableDictionary new];
            int used = 0;
            for (int i = 0; i < samples; i++) {
                if (pass == 1 && profileTicks[i] != longest.unsignedLongLongValue) continue;
                used++;
                NSMutableSet<NSString *> *seen = [NSMutableSet new];
                BOOL first = YES;
                // The first three frames are the handler and the signal trampoline.
                for (int f = 3; f < profileCounts[i]; f++) {
                    NSValue *key = [NSValue valueWithPointer:profileFrames[i][f]];
                    NSString *name = names[key];
                    if (!name) { name = ProfileName(profileFrames[i][f], pmip); names[key] = name; }
                    if (first) { leaf[name] = @(leaf[name].intValue + 1); first = NO; }
                    if (![seen containsObject:name]) { [seen addObject:name]; inclusive[name] = @(inclusive[name].intValue + 1); }
                }
            }
            const char *scope = pass == 0 ? "whole window" : "longest frame";
            for (NSDictionary<NSString *, NSNumber *> *table in @[leaf, inclusive]) {
                fprintf(stderr, "[Profile] %s, %s, %d samples every %d us:\n", scope, table == leaf ? "self time" : "inclusive time", used, intervalMicroseconds);
                NSArray<NSString *> *order = [table keysSortedByValueUsingComparator:^NSComparisonResult(NSNumber *a, NSNumber *b) { return [b compare:a]; }];
                for (NSUInteger i = 0; i < MIN((NSUInteger)(table == leaf ? 30 : (pass ? 90 : 45)), order.count); i++)
                    fprintf(stderr, "[Profile] %5.1f%%  %s\n", 100.0 * table[order[i]].intValue / MAX(used, 1), order[i].UTF8String);
            }
        }
        fprintf(stderr, "[Profile] done\n");
    });
}

STS2_EXPORT void sts2_stall_tick() { stallTicks++; }
STS2_EXPORT void sts2_stall_watch(int milliseconds) {
    stallMainThread = pthread_self();
    struct sigaction action = {};
    action.sa_handler = ProfileSignal;
    sigaction(SIGINFO, &action, nullptr);
    dispatch_async(dispatch_get_global_queue(QOS_CLASS_UTILITY, 0), ^{
        unsigned long long seen = stallTicks;
        int quiet = 0, reported = 0;
        for (;;) {
            usleep(50 * 1000);
            unsigned long long now = stallTicks;
            if (now != seen) { seen = now; quiet = 0; reported = 0; continue; }
            quiet += 50;
            // Two samples per stall, some time apart.
            if (quiet >= milliseconds && reported < 2 && quiet >= milliseconds * (reported + 1)) {
                reported++;
                pthread_kill(stallMainThread, SIGINFO);
            }
        }
    });
}

STS2_EXPORT void sts2_free(void *pointer) { free(pointer); }

// iOS cannot relaunch an app. Explain that once, then close on confirmation so
// nothing keeps running against a game install that only applies after reopening.
STS2_EXPORT void sts2_restart_required() {
    dispatch_async(dispatch_get_main_queue(), ^{
        BOOL chinese = [NSLocale.preferredLanguages.firstObject hasPrefix:@"zh"];
        UIAlertController *alert = [UIAlertController
            alertControllerWithTitle:chinese ? @"需要重新打开应用" : @"Reopen the app"
            message:chinese ? @"更改将在重新打开应用后生效。关闭后请再次打开，并重新开启 JIT。"
                            : @"The change takes effect after the app is reopened. Close it, open it again and enable JIT again."
            preferredStyle:UIAlertControllerStyleAlert];
        [alert addAction:[UIAlertAction actionWithTitle:chinese ? @"关闭应用" : @"Close app"
            style:UIAlertActionStyleDefault handler:^(UIAlertAction *action) { exit(0); }]];
        UIViewController *top = UIApplication.sharedApplication.delegate.window.rootViewController;
        while (top.presentedViewController) top = top.presentedViewController;
        [top presentViewController:alert animated:YES completion:nil];
    });
}

STS2_EXPORT char *sts2_qr_png(const char *value) {
    NSData *data = [[NSString stringWithUTF8String:value] dataUsingEncoding:NSUTF8StringEncoding];
    CIFilter *filter = [CIFilter filterWithName:@"CIQRCodeGenerator"];
    [filter setValue:data forKey:@"inputMessage"];
    [filter setValue:@"M" forKey:@"inputCorrectionLevel"];
    CIImage *image = [filter.outputImage imageByApplyingTransform:CGAffineTransformMakeScale(8, 8)];
    CIContext *context = [CIContext contextWithOptions:nil];
    CGImageRef cgImage = [context createCGImage:image fromRect:image.extent];
    if (!cgImage) return nullptr;
    NSData *png = UIImagePNGRepresentation([UIImage imageWithCGImage:cgImage]);
    CGImageRelease(cgImage);
    return strdup([png base64EncodedStringWithOptions:0].UTF8String);
}

// Bonjour lives on the main run loop; Godot reads a bounded JSON snapshot.
@interface Sts2LanDiscovery : NSObject <NSNetServiceBrowserDelegate, NSNetServiceDelegate>
@property(nonatomic, strong) NSNetServiceBrowser *browser;
@property(nonatomic, strong) NSNetService *published;
@property(nonatomic, strong) NSMutableArray<NSNetService *> *services;
@property(nonatomic, copy) NSString *browseError;
@end

@implementation Sts2LanDiscovery
- (instancetype)init {
    self = [super init];
    if (self) { self.services = [NSMutableArray array]; self.browseError = @""; }
    return self;
}
- (void)stopBrowsing {
    self.browser.delegate = nil;
    [self.browser stop]; self.browser = nil;
    for (NSNetService *service in self.services) { service.delegate = nil; [service stop]; }
    [self.services removeAllObjects];
}
- (void)netServiceBrowser:(NSNetServiceBrowser *)browser didFindService:(NSNetService *)service moreComing:(BOOL)more {
    if (browser != self.browser || self.services.count >= 64 || [self.services containsObject:service]) return;
    [self.services addObject:service];
    service.delegate = self;
    [service resolveWithTimeout:5];
}
- (void)netServiceBrowser:(NSNetServiceBrowser *)browser didRemoveService:(NSNetService *)service moreComing:(BOOL)more {
    if (browser != self.browser) return;
    NSUInteger index = [self.services indexOfObject:service];
    if (index != NSNotFound) {
        NSNetService *found = self.services[index]; found.delegate = nil; [found stop];
        [self.services removeObjectAtIndex:index];
    }
}
- (void)netServiceBrowser:(NSNetServiceBrowser *)browser didNotSearch:(NSDictionary *)error {
    if (browser == self.browser)
        self.browseError = @"Nearby search failed. Check Settings > Privacy & Security > Local Network, then try again.";
}
- (void)netService:(NSNetService *)service didNotResolve:(NSDictionary *)error {
    [service stop];
    [self.services removeObject:service];
}
- (NSString *)snapshot {
    NSMutableArray *hosts = [NSMutableArray array];
    for (NSNetService *service in self.services) {
        if (service.port <= 0 || service.port > 65535) continue;
        for (NSData *data in service.addresses) {
            if (data.length < sizeof(struct sockaddr_in)) continue;
            const struct sockaddr_in *address = (const struct sockaddr_in *)data.bytes;
            if (address->sin_family != AF_INET) continue;
            char ip[INET_ADDRSTRLEN];
            if (!inet_ntop(AF_INET, &address->sin_addr, ip, sizeof(ip))) continue;
            NSString *name = service.name ?: @"LAN host";
            if (name.length > 64) name = [name substringToIndex:64];
            [hosts addObject:@{@"name": name, @"address": @(ip), @"port": @(service.port)}];
            break;
        }
    }
    NSData *json = [NSJSONSerialization dataWithJSONObject:@{@"hosts": hosts, @"error": self.browseError} options:0 error:nil];
    return [[NSString alloc] initWithData:json encoding:NSUTF8StringEncoding];
}
@end

static Sts2LanDiscovery *sts2Lan;
static void lanOnMain(void (^action)(void)) {
    void (^run)(void) = ^{
        if (!sts2Lan) sts2Lan = [[Sts2LanDiscovery alloc] init];
        action();
    };
    if (NSThread.isMainThread) run();
    else dispatch_sync(dispatch_get_main_queue(), run);
}

STS2_EXPORT int sts2_lan_browse() {
    lanOnMain(^{
        [sts2Lan stopBrowsing]; sts2Lan.browseError = @"";
        sts2Lan.browser = [[NSNetServiceBrowser alloc] init];
        sts2Lan.browser.delegate = sts2Lan;
        [sts2Lan.browser searchForServicesOfType:@"_sts2lan._udp." inDomain:@"local."];
    });
    return 0;
}
STS2_EXPORT int sts2_lan_browse_stop() {
    lanOnMain(^{ [sts2Lan stopBrowsing]; }); return 0;
}
STS2_EXPORT int sts2_lan_publish(int port) {
    if (port <= 0 || port > 65535) return -1;
    lanOnMain(^{
        [sts2Lan.published stop];
        // Empty name lets Bonjour choose and disambiguate the device's local name.
        sts2Lan.published = [[NSNetService alloc] initWithDomain:@"local." type:@"_sts2lan._udp." name:@"" port:port];
        [sts2Lan.published publish];
    });
    return 0;
}
STS2_EXPORT int sts2_lan_publish_stop() {
    lanOnMain(^{ [sts2Lan.published stop]; sts2Lan.published = nil; }); return 0;
}
STS2_EXPORT char *sts2_lan_snapshot() {
    __block char *result = nullptr;
    lanOnMain(^{ result = strdup([sts2Lan snapshot].UTF8String); });
    return result;
}

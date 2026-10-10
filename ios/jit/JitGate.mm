#import <UIKit/UIKit.h>
#import <objc/runtime.h>
#include <unistd.h>
#include <TargetConditionals.h>
#include <dlfcn.h>
#include <pthread.h>
#include <signal.h>
#include <sys/sysctl.h>
#include <sys/ucontext.h>

extern "C" int csops(pid_t, unsigned int, void *, size_t);
static BOOL (*originalLaunch)(id, SEL, UIApplication *, NSDictionary *);
static NSMutableDictionary<NSString *, NSValue *> *lifecycleMethods;
static BOOL engineStarted;

// --- Debugger-assisted executable memory -------------------------------------
// On iOS 26 and later the system only runs generated code in pages a debugger
// prepared while attached. The app asks for that with a breakpoint the JIT
// tool's script recognises (StikDebug's "universal" protocol): x16 selects the
// command, x0/x1 carry the region. These must live in the main executable.
__attribute__((noinline, naked)) static void *JIT26PrepareRegion(void *address, size_t length) {
    __asm__("mov x16, #1\n" "brk #0xf00d\n" "ret\n");
}
__attribute__((noinline, naked)) static void JIT26Detach(void) {
    __asm__("mov x16, #0\n" "brk #0xf00d\n" "ret\n");
}

// With no debugger serving it, the breakpoint arrives here as SIGTRAP. Only the
// expected instruction on the expected thread is skipped; any other trap goes
// to whoever handled it before.
static uintptr_t guardedPc;
static pthread_t guardedThread;
static volatile sig_atomic_t guardTripped;
static struct sigaction previousTrap;

static void TrapGuard(int signal, siginfo_t *info, void *context) {
    ucontext_t *user = (ucontext_t *)context;
    if (pthread_equal(pthread_self(), guardedThread) && user->uc_mcontext->__ss.__pc == guardedPc) {
        guardTripped = 1;
        user->uc_mcontext->__ss.__pc += 4;
        return;
    }
    if (previousTrap.sa_flags & SA_SIGINFO) {
        if (previousTrap.sa_sigaction) previousTrap.sa_sigaction(signal, info, context);
    } else if (previousTrap.sa_handler == SIG_DFL) {
        sigaction(SIGTRAP, &previousTrap, nullptr); // re-executes the instruction and takes the default action
    } else if (previousTrap.sa_handler != SIG_IGN) {
        previousTrap.sa_handler(signal);
    }
}

// Runs one protocol breakpoint. Returns true when a debugger served it.
static bool WithTrapGuard(void *helper, void (^body)(void)) {
    struct sigaction guard = {};
    guard.sa_sigaction = TrapGuard;
    guard.sa_flags = SA_SIGINFO;
    sigemptyset(&guard.sa_mask);
    guardedPc = (uintptr_t)helper + 4; // the brk follows the mov
    guardedThread = pthread_self();
    guardTripped = 0;
    sigaction(SIGTRAP, &guard, &previousTrap);
    body();
    sigaction(SIGTRAP, &previousTrap, nullptr);
    return !guardTripped;
}

static int PrepareWithDebugger(void *address, size_t length) {
    __block void *prepared = nullptr;
    bool served = WithTrapGuard((void *)&JIT26PrepareRegion, ^{ prepared = JIT26PrepareRegion(address, length); });
    // The script echoes the address even when it failed; the arena checks every page itself.
    return served && prepared == address ? 0 : 1;
}

// A debugger that is still attached keeps intercepting the runtime's own signals.
static bool IsTraced() {
    struct kinfo_proc info = {};
    size_t size = sizeof(info);
    int name[] = {CTL_KERN, KERN_PROC, KERN_PROC_PID, getpid()};
    return sysctl(name, 4, &info, &size, nullptr, 0) == 0 && (info.kp_proc.p_flag & P_TRACED);
}

typedef const char *(*ArenaCreate)(size_t, int (*)(void *, size_t));
typedef const char *(*ArenaSelfTest)(void);

// Reserves all memory the JIT will ever use. Returns nil on success.
static NSString *PrepareJitMemory() {
    NSString *runtime = [NSBundle.mainBundle.privateFrameworksPath stringByAppendingPathComponent:@"sts2.framework/sts2"];
    void *handle = dlopen(runtime.fileSystemRepresentation, RTLD_NOW);
    ArenaCreate create = handle ? (ArenaCreate)dlsym(handle, "sts2_jit_arena_create") : nullptr;
    ArenaSelfTest selfTest = handle ? (ArenaSelfTest)dlsym(handle, "sts2_jit_arena_selftest") : nullptr;
    if (!create || !selfTest) return @"runtime framework is missing";
    NSNumber *configured = NSBundle.mainBundle.infoDictionary[@"STS2JitArenaMiB"];
    size_t size = (size_t)(configured ? configured.unsignedIntegerValue : 256) << 20;
    const char *failure = create(size, TARGET_OS_SIMULATOR ? nullptr : PrepareWithDebugger);
    if (!failure && !TARGET_OS_SIMULATOR) {
        // Unserved means the debugger already left, which is the state we want.
        WithTrapGuard((void *)&JIT26Detach, ^{ JIT26Detach(); });
        // Detaching completes shortly after the breakpoint returns.
        for (int attempt = 0; attempt < 100 && IsTraced(); attempt++) usleep(50 * 1000);
        if (IsTraced()) failure = "the debugger stayed attached";
    }
    if (!failure) failure = selfTest();
    return failure ? @(failure) : nil;
}

static NSString *Text(NSString *english, NSString *chinese) {
    return [NSLocale.preferredLanguages.firstObject hasPrefix:@"zh"] ? chinese : english;
}

@interface STS2JitGate : NSObject
@property(nonatomic, strong) id service;
@property(nonatomic, strong) UIApplication *application;
@property(nonatomic, copy) NSDictionary *launchOptions;
@property(nonatomic, strong) UIWindow *window;
@property(nonatomic, strong) UILabel *notice;
@property(nonatomic, strong) UIButton *enable;
@property(nonatomic, strong) NSTimer *timer;
@property(nonatomic, copy) NSString *documents;
@property(nonatomic) BOOL requested;
- (void)start;
- (void)checkReadiness;
- (void)requestJit;
@end

static STS2JitGate *startupGate;

static BOOL LaunchWhenReady(id service, SEL selector, UIApplication *application, NSDictionary *options) {
    startupGate = [STS2JitGate new];
    startupGate.service = service;
    startupGate.application = application;
    startupGate.launchOptions = options;
    [startupGate start];
    return YES;
}

static void ForwardLifecycle(id service, SEL selector, UIApplication *application) {
    // Godot's lifecycle callbacks dereference its OS singleton before checking it.
    if (!engineStarted) return;
    auto original = (void (*)(id, SEL, UIApplication *))lifecycleMethods[NSStringFromSelector(selector)].pointerValue;
    original(service, selector, application);
}

// The engine renders at the screen's full pixel scale, 2752x2064 on a 13-inch
// iPad. The game's layered backgrounds and full-screen passes are bound by fill
// rate there (30 to 55 fps in a fight on an M4, 120 at 1680x1260). Godot has no
// render-scale setting on iOS and takes its factor from -[UIScreen scale], so
// that is what it is given: frames are drawn smaller and the system scales the
// layer up.
static CGFloat renderScale;
static CGFloat (*originalScreenScale)(id, SEL);
static CGFloat RenderScreenScale(id screen, SEL selector) {
    CGFloat native = originalScreenScale(screen, selector);
    return renderScale > 0 && renderScale < native ? renderScale : native;
}

static void InstallRenderScale() {
    // STS2RenderFraction is the share of the native scale to render at (0.75
    // gives 1.5x on a 2x iPad and 2.25x on a 3x iPhone). The environment
    // variable sets an absolute scale for measurements.
    const char *requested = getenv("STS2_RENDER_SCALE");
    NSNumber *fraction = NSBundle.mainBundle.infoDictionary[@"STS2RenderFraction"];
    double value = requested ? atof(requested) : fraction.doubleValue * UIScreen.mainScreen.scale;
    if (value < 1.0) return;
    renderScale = value;
    Method method = class_getInstanceMethod(UIScreen.class, @selector(scale));
    originalScreenScale = (CGFloat (*)(id, SEL))method_setImplementation(method, (IMP)RenderScreenScale);
}

@implementation STS2JitGate
+ (void)load {
    InstallRenderScale();
    // Main::setup loads the PCK during didFinishLaunching, before setupView runs.
    Class service = NSClassFromString(@"GDTAppDelegateService");
    Method launch = class_getInstanceMethod(service, @selector(application:didFinishLaunchingWithOptions:));
    if (!launch) abort();
    originalLaunch = (BOOL (*)(id, SEL, UIApplication *, NSDictionary *))method_setImplementation(launch, (IMP)LaunchWhenReady);
    lifecycleMethods = [NSMutableDictionary new];
    for (NSString *name in @[@"applicationDidBecomeActive:", @"applicationWillResignActive:",
             @"applicationDidEnterBackground:", @"applicationWillEnterForeground:",
             @"applicationDidReceiveMemoryWarning:", @"applicationWillTerminate:"]) {
        Method method = class_getInstanceMethod(service, NSSelectorFromString(name));
        if (!method) abort();
        lifecycleMethods[name] = [NSValue valueWithPointer:(const void *)method_setImplementation(method, (IMP)ForwardLifecycle)];
    }
}

- (void)start {
    self.documents = NSSearchPathForDirectoriesInDomains(NSDocumentDirectory, NSUserDomainMask, YES).firstObject;
    UIViewController *controller = [UIViewController new];
    controller.view.backgroundColor = UIColor.systemBackgroundColor;
    self.notice = [UILabel new];
    self.notice.translatesAutoresizingMaskIntoConstraints = NO;
    self.notice.textColor = UIColor.labelColor;
    self.notice.font = [UIFont preferredFontForTextStyle:UIFontTextStyleTitle3];
    self.notice.textAlignment = NSTextAlignmentCenter;
    self.notice.numberOfLines = 0;
    [controller.view addSubview:self.notice];
    UILayoutGuide *area = controller.view.safeAreaLayoutGuide;
    [NSLayoutConstraint activateConstraints:@[
        [self.notice.centerYAnchor constraintEqualToAnchor:area.centerYAnchor],
        [self.notice.leadingAnchor constraintEqualToAnchor:area.leadingAnchor constant:24],
        [self.notice.trailingAnchor constraintEqualToAnchor:area.trailingAnchor constant:-24],
    ]];
    // One tap hands this process to StikDebug, the on-device JIT enabler; it
    // attaches as a debugger and serves the same protocol the Mac script does.
    UIButtonConfiguration *style = [UIButtonConfiguration filledButtonConfiguration];
    style.title = Text(@"Enable JIT with StikDebug", @"用 StikDebug 开启 JIT");
    style.buttonSize = UIButtonConfigurationSizeLarge;
    self.enable = [UIButton buttonWithConfiguration:style primaryAction:nil];
    self.enable.translatesAutoresizingMaskIntoConstraints = NO;
    self.enable.hidden = YES;
    [self.enable addTarget:self action:@selector(requestJit) forControlEvents:UIControlEventTouchUpInside];
    [controller.view addSubview:self.enable];
    [NSLayoutConstraint activateConstraints:@[
        [self.enable.topAnchor constraintEqualToAnchor:self.notice.bottomAnchor constant:24],
        [self.enable.centerXAnchor constraintEqualToAnchor:area.centerXAnchor],
    ]];
    self.window = [[UIWindow alloc] initWithFrame:UIScreen.mainScreen.bounds];
    self.window.rootViewController = controller;
    [self.service setValue:self.window forKey:@"window"];
    [self.window makeKeyAndVisible];

    // The launcher keeps the game where the Android build does: <data>/game.
    NSError *error = nil;
    NSString *game = [self.documents stringByAppendingPathComponent:@"game"];
    if (![NSFileManager.defaultManager createDirectoryAtPath:game withIntermediateDirectories:YES attributes:nil error:&error]) {
        self.notice.text = [@"Could not prepare the game folder.\n" stringByAppendingString:error.localizedDescription];
        return;
    }

    __weak STS2JitGate *gate = self;
    self.timer = [NSTimer scheduledTimerWithTimeInterval:0.5 repeats:YES block:^(NSTimer *timer) {
        [gate checkReadiness];
    }];
    self.notice.text = Text(@"Preparing...", @"正在准备…");
}

- (void)requestJit {
    NSString *address = [NSString stringWithFormat:
        @"stikdebug://enable-jit?bundle-id=%@&pid=%d&script-name=universal.js",
        NSBundle.mainBundle.bundleIdentifier, getpid()];
    __weak STS2JitGate *gate = self;
    [self.application openURL:[NSURL URLWithString:address] options:@{} completionHandler:^(BOOL opened) {
        if (opened || !gate) return;
        gate.notice.text = Text(
            @"StikDebug is not installed\n\nInstall StikDebug and its pairing file on this device, connect LocalDevVPN, "
            @"then tap the button again. Starting the app from a Mac with build.py launch also works.",
            @"未安装 StikDebug\n\n请在本机安装 StikDebug 并导入配对文件，连接 LocalDevVPN 后再点一次按钮。"
            @"也可以在 Mac 上用 build.py launch 启动。");
    }];
}

- (void)checkReadiness {
    if (self.application.applicationState != UIApplicationStateActive) return;
    NSString *pack = [self.documents stringByAppendingPathComponent:@"game/SlayTheSpire2.pck"];
    // The launcher replaces this file when it updates the game, so only the
    // pack signature is checked here; the launcher validates the install itself.
    NSFileHandle *handle = [NSFileHandle fileHandleForReadingAtPath:pack];
    NSData *magic = [handle readDataOfLength:4];
    [handle closeFile];
    BOOL complete = magic.length == 4 && memcmp(magic.bytes, "GDPC", 4) == 0;
    if (!complete) {
        self.notice.text = Text(
            @"Game content is missing or still copying.\n\nInstall it from the Mac with build.py install --content.",
            @"游戏内容缺失或仍在复制。\n\n请在 Mac 上用 build.py install --content 安装。");
        return;
    }
    uint32_t flags = 0;
    // Simulator processes may generate code without a debugger.
    if (!TARGET_OS_SIMULATOR && (csops(getpid(), 0, &flags, sizeof(flags)) != 0 || !(flags & 0x10000000))) {
        self.notice.text = Text(
            @"JIT required\n\nThis app runs the game with JIT. iOS needs it enabled again every time the app is reopened; "
            @"the game continues automatically once it is on.\n\nEnable JIT for this app with StikDebug (universal script) or a Mac.",
            @"需要开启 JIT\n\n本应用通过 JIT 运行游戏。iOS 每次重新打开应用后都需要重新开启一次，开启后会自动继续。\n\n"
            @"请用 StikDebug（universal 脚本）或 Mac 为本应用开启 JIT。");
        self.enable.hidden = NO;
        // Hand off once without a tap; the button stays for a retry.
        if (!self.requested) {
            self.requested = YES;
            [self requestJit];
        }
        return;
    }
    self.enable.hidden = YES;
    [self.timer invalidate];
    self.timer = nil;
    NSString *failure = PrepareJitMemory();
    if (failure) {
        // The engine log does not exist yet; leave the reason where the Mac can read it.
        [failure writeToFile:[self.documents stringByAppendingPathComponent:@"jit-gate-failure.txt"]
                  atomically:YES encoding:NSUTF8StringEncoding error:nil];
        // Memory can only be prepared once per process, so this state is final.
        self.notice.text = [NSString stringWithFormat:Text(
            @"JIT could not start\n\n%@.\n\nClose the app, reopen it and enable JIT again. "
            @"On iOS 26 and later the JIT tool must run the universal script.",
            @"JIT 启动失败\n\n%@。\n\n请关闭应用后重新打开，并再次开启 JIT。iOS 26 及以上需要 JIT 工具使用 universal 脚本。"), failure];
        return;
    }
    self.notice.text = Text(@"JIT is on. Starting...", @"JIT 已开启，正在启动…");
    originalLaunch(self.service, @selector(application:didFinishLaunchingWithOptions:), self.application, self.launchOptions);
    engineStarted = YES;
    // The initial active notification was deliberately withheld while waiting.
    ForwardLifecycle(self.service, @selector(applicationDidBecomeActive:), self.application);
    self.window.hidden = YES;
    startupGate = nil;
}
@end

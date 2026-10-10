#import <Foundation/Foundation.h>
#include <dlfcn.h>
#include <execinfo.h>
#include <sys/mman.h>
#include <libkern/OSCacheControl.h>
#include <mono/jit/jit.h>
#include <mono/utils/mono-logger.h>
#include <mach/mach.h>
#include <errno.h>
#include <unistd.h>
#include <TargetConditionals.h>
#include "JitArena.h"

extern "C" int coreclr_initialize(const char *, const char *, int, const char **, const char **, void **, unsigned int *);
extern "C" int coreclr_create_delegate(void *, unsigned int, const char *, const char *, const char *, void **);

static bool initialized;
static void *runtime;
static unsigned int domain;

extern "C" __attribute__((visibility("default"))) void sts2_jit_log(const char *message) {
    fprintf(stderr, "%s\n", message);
}

extern "C" __attribute__((visibility("default"))) int sts2_jit_patch(
        void *target, const void *source, int size, void *backup, int backupSize) {
    if (!target || !source || size <= 0) return EINVAL;
    // Harmony may only rewrite live Mono code. Native functions need a separate
    // backend, and pages the arena has taken back belong to nobody.
    if (!sts2_jit_owns(target)) return ENOTSUP;
    if (!sts2_jit_is_allocated(target, size)) return EFAULT;
    if (backup && backupSize >= size) memcpy(backup, target, size);
    memcpy(sts2_jit_writable(target, size), source, size);
    sts2_jit_flush(target, size);
    return 0;
}

static bool InitializeRuntime() {
    if (initialized) return true;
    // The startup gate reserves and tests the arena before the engine starts.
    if (!sts2_jit_rx_base) {
        fprintf(stderr, "[JIT] No code arena. Enable JIT for this process before starting.\n");
        return false;
    }
    fprintf(stderr, "[JIT] Code arena ready: %zu MiB\n", sts2_jit_arena_size >> 20);
    // A process that leaves through exit() produces no crash report; record who asked.
    atexit([] {
        void *frames[48];
        int count = backtrace(frames, 48);
        fprintf(stderr, "[Host] Process is exiting through exit(); caller stack:\n");
        backtrace_symbols_fd(frames, count, STDERR_FILENO);
        size_t used = 0, high = 0;
        sts2_jit_stats(&used, &high, nullptr);
        fprintf(stderr, "[JIT] Code arena at exit: %zu MiB in use, peak %zu MiB\n", used >> 20, high >> 20);
    });
    mono_trace_set_log_handler([](const char *domain, const char *level, const char *message, mono_bool fatal, void *) {
        fprintf(stderr, "[Mono/%s] %s: %s\n", level ?: "", domain ?: "", message ?: "");
        if (fatal) abort();
    }, nullptr);
    NSString *managed = [NSBundle.mainBundle.resourcePath stringByAppendingPathComponent:@"Managed"];
    NSString *frameworks = NSBundle.mainBundle.privateFrameworksPath;
    NSMutableArray<NSString *> *assemblies = [NSMutableArray array];
    for (NSString *file in [NSFileManager.defaultManager contentsOfDirectoryAtPath:managed error:nil]) {
        if ([file.pathExtension isEqualToString:@"dll"])
            [assemblies addObject:[managed stringByAppendingPathComponent:file]];
    }
    NSString *tpa = [assemblies componentsJoinedByString:@":"];
    setenv("MONO_THREADS_SUSPEND", "preemptive", 1);
    // The default 4 MiB nursery fills about 80 times a minute with mods that
    // allocate every frame; 32 MiB brings that to about 10 with pauses under one
    // 60 Hz frame, while 64 MiB made each pause 22-30 ms.
    setenv("MONO_GC_PARAMS", "nursery-size=32m", 0);
    const char *keys[] = {"TRUSTED_PLATFORM_ASSEMBLIES", "APP_PATHS", "APP_CONTEXT_BASE_DIRECTORY",
        "NATIVE_DLL_SEARCH_DIRECTORIES", "System.Globalization.Invariant", "System.Diagnostics.Tracing.EventSource.IsSupported"};
    const char *values[] = {tpa.UTF8String, managed.UTF8String, managed.UTF8String,
        frameworks.UTF8String, "false", "false"};
    int result = coreclr_initialize(nullptr, "StS2Jit", 6, keys, values, &runtime, &domain);
    if (result != 0) {
        fprintf(stderr, "[JIT] Runtime initialization failed: %x\n", result);
        return false;
    }
    initialized = true;
    return true;
}

extern "C" __attribute__((visibility("default"))) bool godotsharp_game_main_init(
        void *godot, void *callbacks, void *interop, int size) {
    NSString *logPath = [NSSearchPathForDirectoriesInDomains(NSDocumentDirectory, NSUserDomainMask, YES).firstObject stringByAppendingPathComponent:@"jit-game.log"];
    freopen(logPath.fileSystemRepresentation, "w", stdout);
    dup2(fileno(stdout), fileno(stderr));
    setbuf(stdout, nullptr);
    setbuf(stderr, nullptr);
    if (!InitializeRuntime()) return false;
    bool (*entry)(void *, void *, void *, int) = nullptr;
    int result = coreclr_create_delegate(runtime, domain, "sts2", "GodotPlugins.Game.Main", "InitializeFromGameProject", (void **)&entry);
    if (result != 0 || !entry || !entry(godot ?: dlopen(nullptr, RTLD_NOW), callbacks, interop, size)) return false;
    // Same contract as the Android engine patch: the launcher assembly applies
    // its Harmony patches and decides between launcher-only and game startup.
    void (*apply)() = nullptr;
    result = coreclr_create_delegate(runtime, domain, "STS2Mobile", "STS2Mobile.ModEntry", "Apply", (void **)&apply);
    if (result != 0 || !apply) {
        fprintf(stderr, "[JIT] STS2Mobile.ModEntry.Apply is unavailable: %x\n", result);
        return false;
    }
    apply();
    return true;
}

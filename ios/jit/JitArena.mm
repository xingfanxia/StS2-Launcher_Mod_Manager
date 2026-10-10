#include <sys/mman.h>
#include <mach/mach.h>
#include <libkern/OSCacheControl.h>
#include <pthread.h>
#include <map>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include "JitArena.h"

#define STS2_EXPORT extern "C" __attribute__((visibility("default")))

STS2_EXPORT uintptr_t sts2_jit_rx_base = 0;
STS2_EXPORT size_t sts2_jit_arena_size = 0;
STS2_EXPORT intptr_t sts2_jit_rw_delta = 0;

// Free extents by start address. Dynamic methods return their code, and Mono
// keeps at most 16 cached chunks per size, so freed pages must be reusable.
static std::map<uintptr_t, size_t> freeExtents;
static pthread_mutex_t mutex = PTHREAD_MUTEX_INITIALIZER;
static size_t usedBytes, highWater, allocations;

// Harmony gives every patched method its own small piece of code, so space is
// handed out in sub-page units; page-sized requests still start on a page.
static const size_t kGranule = 256;

static size_t RoundToGranule(size_t length) {
    return (length + kGranule - 1) & ~(kGranule - 1);
}

static size_t RoundToPage(size_t length) {
    size_t page = (size_t)getpagesize();
    return (length + page - 1) & ~(page - 1);
}

STS2_EXPORT void sts2_jit_bad_range(const void *address, size_t size) {
    fprintf(stderr, "[JIT] Code write of %zu bytes at %p runs past the arena\n", size, address);
    abort();
}

// Caller holds the mutex. True when [start, start+size) touches no free extent.
static bool IsAllocatedLocked(uintptr_t start, size_t size) {
    auto next = freeExtents.lower_bound(start);
    if (next != freeExtents.end() && next->first < start + size) return false;
    if (next == freeExtents.begin()) return true;
    auto previous = std::prev(next);
    return previous->first + previous->second <= start;
}

STS2_EXPORT int sts2_jit_is_allocated(const void *address, size_t length) {
    uintptr_t start = (uintptr_t)address & ~(uintptr_t)(kGranule - 1);
    size_t size = RoundToGranule((uintptr_t)address + length - start);
    if (!length || start - sts2_jit_rx_base >= sts2_jit_arena_size
        || size > sts2_jit_arena_size - (start - sts2_jit_rx_base)) return 0;
    pthread_mutex_lock(&mutex);
    bool allocated = IsAllocatedLocked(start, size);
    pthread_mutex_unlock(&mutex);
    return allocated;
}

STS2_EXPORT void *sts2_jit_alloc(size_t length) {
    size_t wanted = RoundToGranule(length);
    size_t page = (size_t)getpagesize();
    size_t alignment = wanted % page == 0 ? page : kGranule;
    void *result = nullptr;
    pthread_mutex_lock(&mutex);
    for (auto it = freeExtents.begin(); it != freeExtents.end(); ++it) {
        uintptr_t start = (it->first + alignment - 1) & ~(uintptr_t)(alignment - 1);
        size_t lead = start - it->first;
        if (it->second < lead || it->second - lead < wanted) continue;
        uintptr_t extent = it->first;
        size_t rest = it->second - lead - wanted;
        freeExtents.erase(it);
        if (lead) freeExtents.emplace(extent, lead);
        if (rest) freeExtents.emplace(start + wanted, rest);
        usedBytes += wanted;
        allocations++;
        if (usedBytes > highWater) highWater = usedBytes;
        result = (void *)start;
        break;
    }
    size_t used = usedBytes;
    pthread_mutex_unlock(&mutex);
    if (!result) {
        fprintf(stderr, "[JIT] Code arena exhausted: requested %zu bytes with %zu of %zu in use "
            "(%zu allocations so far). Too many mods or too long a session for the reserved JIT memory.\n",
            wanted, used, sts2_jit_arena_size, allocations);
        abort();
    }
    // Recycled pages hold old code and freshly prepared pages hold a marker byte.
    memset(sts2_jit_writable(result, wanted), 0, wanted);
    return result;
}

STS2_EXPORT int sts2_jit_free(void *address, size_t length) {
    if (!sts2_jit_owns(address)) return 0;
    uintptr_t start = (uintptr_t)address;
    size_t size = RoundToGranule(length);
    pthread_mutex_lock(&mutex);
    if ((start & (kGranule - 1)) || !size || size > sts2_jit_arena_size - (start - sts2_jit_rx_base)
        || !IsAllocatedLocked(start, size)) {
        pthread_mutex_unlock(&mutex);
        // Recycling pages that are already free would hand live code to a second owner.
        fprintf(stderr, "[JIT] Invalid code free of %zu bytes at %p\n", length, address);
        abort();
    }
    auto next = freeExtents.lower_bound(start);
    if (next != freeExtents.end() && start + size == next->first) {
        size += next->second;
        next = freeExtents.erase(next);
    }
    if (next != freeExtents.begin()) {
        auto previous = std::prev(next);
        if (previous->first + previous->second == start) {
            start = previous->first;
            size += previous->second;
            freeExtents.erase(previous);
        }
    }
    freeExtents.emplace(start, size);
    usedBytes -= RoundToGranule(length);
    pthread_mutex_unlock(&mutex);
    return 1;
}

STS2_EXPORT void sts2_jit_flush(void *address, size_t length) {
    if (sts2_jit_owns(address)) sys_dcache_flush(sts2_jit_writable(address, length), length);
    sys_icache_invalidate(address, length);
}

STS2_EXPORT void sts2_jit_stats(size_t *used, size_t *high, size_t *size) {
    pthread_mutex_lock(&mutex);
    if (used) *used = usedBytes;
    if (high) *high = highWater;
    pthread_mutex_unlock(&mutex);
    if (size) *size = sts2_jit_arena_size;
}

// Writes `mov w0, #value; ret` through the alias and calls it at the executable address.
static bool RunsCode(uintptr_t rx, int value) {
    uint32_t code[] = {0x52800000u | ((uint32_t)value << 5), 0xd65f03c0u};
    memcpy(sts2_jit_writable((void *)rx, sizeof(code)), code, sizeof(code));
    sts2_jit_flush((void *)rx, sizeof(code));
    return ((int (*)())rx)() == value;
}

STS2_EXPORT const char *sts2_jit_arena_create(size_t size, sts2_jit_prepare_fn prepare) {
    if (sts2_jit_rx_base) return nullptr;
    size = RoundToPage(size);
    size_t page = (size_t)getpagesize();
    // iOS 26 refuses to turn writable pages executable, so the primary mapping
    // is executable from the start and only the alias is ever writable.
    void *rx = mmap(nullptr, size, PROT_READ | PROT_EXEC, MAP_PRIVATE | MAP_ANON, -1, 0);
    if (rx == MAP_FAILED) return "could not map executable memory";
    if (prepare) {
        // The stock debugger script encodes page addresses in 36 bits; preparing
        // a truncated address would mark someone else's memory.
        if (((uintptr_t)rx + size) >> 36) {
            munmap(rx, size);
            return "executable memory is mapped too high for the debugger protocol";
        }
        if (prepare(rx, size) != 0) {
            munmap(rx, size);
            return "no debugger prepared the executable memory";
        }
        // The debugger prepares a page by writing a marker to its first byte and
        // does not report pages it failed on, so check each one. This must come
        // before anything else writes to the arena.
        for (size_t offset = 0; offset < size; offset += page) {
            if (*((const uint8_t *)rx + offset) != 0x69) {
                munmap(rx, size);
                return "the debugger left some executable pages unprepared";
            }
        }
    }
    vm_address_t alias = 0;
    vm_prot_t current = 0, maximum = 0;
    if (vm_remap(mach_task_self(), &alias, size, 0, VM_FLAGS_ANYWHERE, mach_task_self(),
            (vm_address_t)rx, false, &current, &maximum, VM_INHERIT_NONE) != KERN_SUCCESS) {
        munmap(rx, size);
        return "could not create the writable alias";
    }
    if (vm_protect(mach_task_self(), alias, size, false, VM_PROT_READ | VM_PROT_WRITE) != KERN_SUCCESS) {
        munmap((void *)alias, size);
        munmap(rx, size);
        return "could not make the alias writable";
    }
    pthread_mutex_lock(&mutex);
    freeExtents.emplace((uintptr_t)rx, size);
    pthread_mutex_unlock(&mutex);
    sts2_jit_rw_delta = (intptr_t)alias - (intptr_t)rx;
    sts2_jit_arena_size = size;
    sts2_jit_rx_base = (uintptr_t)rx;
    return nullptr;
}

static void *RunsOnThread(void *address) {
    return RunsCode((uintptr_t)address, 44) ? address : nullptr;
}

STS2_EXPORT const char *sts2_jit_arena_selftest(void) {
    if (!sts2_jit_rx_base) return "the code arena was not created";
    size_t page = (size_t)getpagesize(), size = sts2_jit_arena_size;
    // First, last and evenly spread pages; every use writes, runs and rewrites.
    for (int step = 0; step <= 16; step++) {
        uintptr_t address = sts2_jit_rx_base + (((size - page) / 16 * step) & ~(page - 1));
        if (!RunsCode(address, 42) || !RunsCode(address, 43)) return "prepared memory does not execute";
        pthread_t thread;
        void *ran = nullptr;
        if (pthread_create(&thread, nullptr, RunsOnThread, (void *)address) != 0
            || pthread_join(thread, &ran) != 0 || !ran) return "prepared memory does not execute on a second thread";
        memset(sts2_jit_writable((void *)address, page), 0, 8);
    }
    return nullptr;
}

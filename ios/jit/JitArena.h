#pragma once
#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/*
 * One executable arena reserved before Mono starts, with a second writable
 * mapping of the same pages. Mono keeps executable addresses everywhere
 * (branches, JIT info, unwinding) and translates only the destination of a
 * write. This header is also copied into the Mono source tree.
 */
extern uintptr_t sts2_jit_rx_base;   /* first executable address, 0 until created */
extern size_t sts2_jit_arena_size;   /* bytes in the arena */
extern intptr_t sts2_jit_rw_delta;   /* writable alias minus executable address */

static inline int sts2_jit_owns(const void *address) {
    return (uintptr_t)address - sts2_jit_rx_base < sts2_jit_arena_size;
}

void sts2_jit_bad_range(const void *address, size_t size) __attribute__((noreturn));

/* Called for every emitted instruction, so it stays two compares and an add.
 * Addresses outside the arena (Mono's temporary emission buffers) pass through. */
static inline void *sts2_jit_writable(void *address, size_t size) {
    uintptr_t offset = (uintptr_t)address - sts2_jit_rx_base;
    if (offset >= sts2_jit_arena_size) return address;
    if (size > sts2_jit_arena_size - offset) sts2_jit_bad_range(address, size);
    return (void *)((uintptr_t)address + sts2_jit_rw_delta);
}

/* Page-granular allocation inside the arena; returns zeroed executable memory.
 * Never returns NULL: Mono dereferences a failed code allocation before it
 * checks it, so exhaustion is reported here and the process aborts. */
void *sts2_jit_alloc(size_t length);
/* Returns 1 and recycles the pages when the address belongs to the arena.
 * Any page-aligned part of an allocation may be freed on its own. */
int sts2_jit_free(void *address, size_t length);
/* 1 when every page of the range is currently allocated. */
int sts2_jit_is_allocated(const void *address, size_t length);
/* Publishes written code: data cache through the alias, instruction cache at the executable address. */
void sts2_jit_flush(void *address, size_t length);

/* Bytes currently allocated, the highest that figure has been, and the arena size. */
void sts2_jit_stats(size_t *used, size_t *high_water, size_t *size);

/* Asks an attached debugger to make [address, address+length) executable.
 * Returns 0 when a debugger served the request. Supplied by the main binary. */
typedef int (*sts2_jit_prepare_fn)(void *address, size_t length);

/* Maps the arena and its writable alias. `prepare` may be NULL where executable
 * pages need no debugger (simulator). Returns NULL on success or a static
 * description of the step that failed. */
const char *sts2_jit_arena_create(size_t size, sts2_jit_prepare_fn prepare);
/* Rewrites and runs code on sampled pages, on the calling thread and on a
 * second one. Run it after the debugger has detached: that is the state the
 * game runs in. Returns NULL on success. */
const char *sts2_jit_arena_selftest(void);

#ifdef __cplusplus
}
#endif

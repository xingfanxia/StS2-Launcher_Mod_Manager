# iOS port status

Plan: [PLAN_IOS_PORT.md](../../PLAN_IOS_PORT.md). Updated as milestones move.

| Milestone | State | Evidence |
|---|---|---|
| IOS-1 Baseline host | simulator and device builds run | 2026-10-08: `build.py build --simulator` produces an app that boots in the iOS 27.0 iPad simulator. 2026-10-09: `build.py build` + `install --content` puts a signed app and the game pack on an iPad Pro 13-inch (M4), iPadOS 27.0 |
| IOS-2 JIT on iOS 27 | core path proven on one iPad; checks below open | 2026-10-09, iPad Pro 13-inch (M4, iPad16,5), iPadOS 27.0, `build.py launch`: LLDB on the Mac prepared the 256 MiB arena (16384 pages, 10.5 s) and detached; the in-app self-test passed after detach, Mono JIT ran and every Harmony patch in `ModEntry.Apply` applied; the launcher rendered with Metal. Log: `Documents/jit-game.log` on the device |
| IOS-3 Shared assembly | seam started; game reached on device | `PlatformHost` carries secrets (Keychain-held AES key), QR rendering, restart, pack preparation, storage permission and Steam login ids; iOS implementations live in `src/STS2Mobile.iOS/Platform`. About forty other `GodotApp` calls still fall through to a null host. 2026-10-09 on the M4 iPad: QR sign-in, ownership check, saved session reused after reopen |
| IOS-4 Launcher, install, restart model | on-device install works; game-free start not done | 2026-10-09 on the M4 iPad: 2.7 GB depot download (about 90 s), in-app pack preparation (`IosGamePack`, byte-identical to the Python tools on the macOS pack), install activation, reopen prompt, then PLAY to the main menu of v0.107.1. The app still needs a game pack pushed from the Mac for its very first start, and loads game assemblies from the bundle |
| IOS-5 Saves and cloud | first run only | Same session: 211 cloud files listed, conflict dialog answered, saves pulled and verified, automatic local backup written under `Documents/Saves`. Kill and recovery cases not run |
| IOS-6 Mods | Workshop mods load on device | 2026-10-09 on the M4 iPad: Mod Hub downloaded 14 Workshop mods into `Documents/Mods`; after a reopen the game reports 14 loaded mods at the main menu (3.05 GB footprint, 60 fps, code arena 62 of 256 MiB). Needed: sub-page code arena units, a Mono fix for obfuscated control flow and one for a vtable lookup (both in `ios/jit/mono-ios-jit.patch`), restart routed through `PlatformHost`. RegentFX is kept disabled: its 1211 frame textures add about 2.5 GB and the app is killed at the 5120 MiB per-app limit |
| IOS-7 iPhone and iPad experience | not started | |
| IOS-8 Distribution | not started | |

## Known gaps in what runs today

- Memory: with 14 mods the footprint is about 3.0 GB at the main menu and grows
  by a few hundred MB over an hour of play. Exceeding the per-app limit kills
  the app without a crash report (visible only in the device syslog as
  `memorystatus ... exceeded mem limit`). The `increased-memory-limit`
  entitlement raises the limit to 6144 MiB (see Device signing).
- First start with no game pack on the device is not possible yet: Godot needs a
  main pack to boot, so `install --content` seeds one.
- First PLAY spends about 144 s in shader warm-up on the M4 iPad. Android
  restarts afterwards; iOS continues in the same process.
- Starting through the Mac takes about 20 s, half of it preparing the code
  arena over USB; the screen is static meanwhile.
- `STS2_STALL_STACKS=1` prints the main thread's native stack and the particle
  systems in the tree whenever a frame takes longer than 300 ms; `devui.sh`
  also has `console`, `mem`, `load`, `loadset` and `drop` commands.
- Host-driven test hooks, all off unless set in the launch environment
  (`build.py launch --env NAME=VALUE`): `STS2_DEBUG_QR_LOGIN`,
  `STS2_DEBUG_AUTO_PLAY`, `STS2_DEBUG_MOD_SAFE_MODE`,
  `STS2_DEBUG_MOD_PARTITION=i/n`, `STS2_DEBUG_DISABLE_MODS=A,B`,
  `STS2_DEBUG_ENABLE_MODS=A,B`, `STS2_FRAME_STATS` (frame, memory and code-arena
  log lines) and `STS2_DEBUG_AUTOMATION` (screenshots, taps and drags through
  `scripts/ios/devui.sh`).
- The app records the iOS 26 SDK in its executable because Godot 4.5.1 has no
  UIScene lifecycle, which iOS 27 requires of apps linked against its SDK.
- The local Steam install is the `public` branch (v0.107.1). The `public-beta`
  build (v0.111) has not been run on iOS.
- `src/STS2Jit` and `src/STS2MobileIos` are the prototype's managed code, kept
  only as a source for iOS-specific patches still to be ported; they are not
  built.

## Device signing

`build.py build` signs with Xcode's automatic provisioning, authenticated by
the App Store Connect API key named in `ios/config.local.json`
(`asc_key_path`, `asc_key_id`, `asc_issuer_id`). The explicit App ID
`ai.ax0x.sts2launcher` exists in team `2T5DG4GZBN` since 2026-10-09 and carries
the Increased Memory Limit capability, which only Xcode's automatic signing or
the developer website can enable (the public API has no such capability type).
With the entitlement the M4 iPad reports a per-app limit of 6144 MiB instead of
5120 MiB.

Both the API and Xcode refuse with `REQUIRED_AGREEMENTS_MISSING_OR_EXPIRED`
whenever the Account Holder has a pending Program License Agreement; after it
is accepted the refusal can persist for a few minutes. Setting
`provisioning_profile` and `sign_identity` in the config signs an unsigned
build with an installed profile instead (no extra entitlements).

## Play-test findings (2026-10-09, M4 iPad, 14 Workshop mods)

Three host-driven sessions on an empty profile: one run to floor 28 (first
boss beaten), one to the first boss, plus mod and regression passes. Notes and
screenshots: `scratch/reports/ios-port/climb-notes.md` (not committed).

Fixed and confirmed on the device:

- Abort when the engine printed an error while Harmony frames were on the
  stack (`DebuggingUtils.GetStackFrameMethodDecl` asked Mono for the parameters
  of runtime wrapper frames). The freeze after picking a card reward has not
  recurred since.
- Mod text drawn as boxes or not at all: system font fallback cannot supply
  CJK on iOS 18+ (PingFang uses a glyph format FreeType does not read). Default
  fonts, `SystemFont` name lists and fonts that mods assign now get the game
  pack's CJK fonts as fallback.
- Tooltips that stayed after a touch (no hover-out on a touch screen) now close
  three seconds after the finger lifts.
- Per-aim engine warnings with C# backtraces from mouse cursor calls.
- All mods exercised work: Rewind, SpeedX, QuickSL, QuickLink checkpoint
  history, BetterSaveSlots, ModConfig/BetterModMenu pages, MintySpire2,
  DamageMeter, SkadaHelper, unlock_all.

Fixed later the same day, each confirmed on the device:

- Memory spike and one-second stall on every return to the main menu (and a
  3.7 s frame at startup): the menu's two cloud particle layers have a 400 s
  preprocess, simulated in about 24,000 steps in one frame. `IosParticles`
  coarsens the simulation step for long preprocess times. Peak footprint over a
  session with 14 mods went from 4.4 to 5.3 GB down to 3.1 GB. Two earlier
  attempts (collecting after asset unloads, reordering the run exit) and a lower
  load concurrency did nothing and were removed.
- Frame pacing: dropping the game's 60 fps limiter on the 120 Hz display took
  frames above 20 ms from 110 to 149 per five seconds to under 10.
- First-use stalls: a background thread compiles the game, engine bindings,
  launcher and mod assemblies ahead of use through `sts2_jit_compile_only`,
  which skips class constructors (about 150,000 methods, 40 s, code arena 137
  of 256 MiB). First attack card went from 210 to 241 ms of JIT frames to 100.
- Fourth enemy off screen on the 4:3 display: the fight layout is refitted
  whenever a creature joins or leaves (`IosCombatLayout`).

- Fight frame rate: fill-rate bound at native resolution (a main-thread
  profile showed 70% of the time waiting on the GPU). Rendering at 0.75 of the
  native scale (`STS2RenderFraction`, 2064x1548 on the 13-inch iPad) holds 117
  to 120 fps where native gave 44 to 78. Needs the source-built engine (D8).
  Sharpness at this scale has not been judged by a person yet.

Open:

- Entering a room still costs one frame of 85 to 160 ms (JIT, shader or render
  work, also on repeat visits).
- RegentFX (1211 frame textures, about 2.5 GB) now fits under the raised limit
  but leaves about 500 MB: 5.6 GB in a fight against 6144 MiB.
- 4:3 layout: a tutorial bubble and the Rewind result panel overflow.
- DamageMeter's window is always on top by the mod's design and can cover map
  nodes, rewards and intents until it is dragged away or disabled in the mod
  settings.
- The game's frame-limit setting still reads 60 while frames follow the display.

## Device tooling

- A paired device stuck in `connecting` in `xcrun devicectl list devices` is a
  Mac-side fault: `pkill -f "CoreDevice.framework.*CoreDeviceService"`, wait a
  few seconds and list again (seen 2026-10-09; the running app survived).
- Game and mod log lines (`[INFO]`, `[ERROR]`, engine warnings with C#
  backtraces) go to the system log, not to `Documents/jit-game.log`:
  `idevicesyslog -u <udid> -p StS2`.
- A kill for memory leaves no crash report; look for `memorystatus` in the
  unfiltered `idevicesyslog` output.
- Attaching LLDB to a running app to read thread stacks ends the app when the
  debugger detaches, so it is only for a process that is already hung.

## Room-change stalls (2026-10-10)

Measured with the main-thread sampler (`devui.sh profile`, which now also
reports the single longest frame). Entering a fight took one frame of about
100 ms every time. Two causes, both removed:

- Spine's `mesh_get_surface` call per new mesh read three buffers back from
  the GPU (60 % of that frame in `fence_wait`). Fixed in the engine patch.
- The game calls `GC.Collect()` after every room load, 50-70 ms under Mono.
  On iOS that call starts a concurrent major collection instead
  (`sts2_gc_collect_concurrent` in the Mono patch,
  `IosPlatform.CollectConcurrently`). Removing the call outright was tried
  first and rejected: over 20 real rooms the footprint grew from 2.8 to 3.9 GB
  and nursery pauses on the map rose from 23 to 33-42 ms.

With the readback fix and the concurrent collection, the third and fourth
fight entry in a session had a longest frame of 34 ms, all of it waiting for
the GPU. The first entries still pay one-time costs (particle shader creation,
JSON metadata in a mod, JIT) of 40-60 ms.

Round 10 (18 real rooms through the act 1 boss, started without a Mac, before
the concurrent-collection change): no crash, Spine rendering correct for the
player and 15 enemy types, repeat room entries at most 67 ms (86-150 ms
before), average 117.7 fps over 452 windows.

Round 11 (18 more real rooms with the concurrent collection): footprint 3055
MiB at the menu, 3365 after 9 rooms, 3394 after 18, 3388 back at the menu
(round 10: 2794, 3487, 3788, 3743). Slow frames attributed to collection fell
from 568 to 92; one 109 ms collection frame was seen on a shop entry. Nursery
collections on the map still take about 23 ms each, about twice a second.

Open from round 10:

- The first time some cards are played, one frame takes 260-520 ms (shader
  pipeline creation three times, JIT once). This is now the largest stall.
- After "continue" into a reward or map screen the DamageMeter window is
  missing until the next fight.

## Starting without a Mac (verified 2026-10-10)

On the M4 iPad (iPadOS 27.0) the app started with no Mac debugger: the gate
opened StikDebug 3.1.13 by URL, StikDebug attached through LocalDevVPN, served
the universal script for the 256 MiB arena and returned to the app in about
4 s; the game then reached the main menu with 14 mods and played cards in a
fight. Setup is in [building.md](building.md#starting-without-a-mac).

Open: StikDebug asks "Enable JIT?" on every hand-off until its setting
"Confirm JIT Links" is turned off on the device; only one VPN can
be active, so another VPN is off while LocalDevVPN is connected.

## Not yet verified on a device

- Any iPhone, and any device other than the M4 iPad.
- IOS-2 checks still open: freeing and reusing dynamic methods past Mono's
  16-chunk freelist, background and resume, arena high-water mark with mods.
- Save upload conflicts, background/resume and kill-during-write cases.

## Candidates to carry to Android (not applied, not tested there)

Android also runs the game on Mono with an engine built from source, so these
iOS findings probably apply; none was changed for Android in this work.

- `GC.Collect()` after every room load (`PreloadManager.LoadRoomAssets`):
  removing it needs a memory check over 15+ real rooms first (see above); the
  concurrent replacement needs the Mono export.
- Spine's GPU readback in `mesh_get_surface`: the mesh-storage part of
  `ios/engine/godot-ios.patch` is renderer code shared with Android.
- Particle systems with very long preprocess times (main-menu clouds, 400 s)
  simulated in one frame: `IosParticles` is platform-neutral.
- Background JIT warm-up needs the `sts2_jit_compile_only` runtime export, so
  it would need the same Mono patch on Android.

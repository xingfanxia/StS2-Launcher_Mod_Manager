# Plan: iOS / iPadOS port

Status ledger and evidence live in [docs/ios/STATUS.md](docs/ios/STATUS.md).
Decisions and their reasons live in [docs/ios/DECISIONS.md](docs/ios/DECISIONS.md).

## Objective

A sideloaded iOS / iPadOS 27 build of this launcher (iPhone and iPad) that runs
the user's own Steam copy of Slay the Spire 2 under Mono JIT with Harmony, loads
third-party mods at runtime, and carries the Android launcher's feature set:
Steam login, on-device game download, Workshop Mod Hub, Steam Cloud sync with
the Save Manager, local backups and the Simplified Chinese UI.

Scope decisions made by AX on 2026-10-08: target iOS and iPadOS 27; Mono JIT
with full runtime mod support (not an interpreter or NativeAOT build).

The Android build is not changed in behaviour by this work.

## Starting point

- `TheUnknownThing/StS2-Launcher-iOS` (MIT) is the base for the iOS host: the
  official Godot 4.5.1 iOS export template, a `sts2.framework` that implements
  Godot's `godotsharp_game_main_init` and boots a Mono 9.0.20 built from source
  with the JIT enabled, a Mono patch that writes code through a writable alias
  of executable pages, and Cecil adapters for CoreLib and Harmony 2.4.2. It was
  exercised on one M4 iPad Pro on iPadOS 18.1 and has no launcher, no on-device
  download, no Workshop and no cloud sync.
- `src/STS2Mobile` (this repo) holds the launcher, SteamKit2 integration, cloud
  and save safety, Mod Hub and the mobile patches. It reaches Android through
  about fifty `GodotApp` methods called over `JavaClassWrapper`.

## Constraints that shape the design

- An iOS app cannot restart itself, and every new process needs JIT enabled
  again by an external debugger. Each restart on Android is a manual step on
  iOS.
- On iOS 26 and later, devices with the Trusted Execution Monitor only run
  executable memory that was prepared while the debugger was attached. The
  prototype's on-demand `mmap` → `mprotect(RX)` after detach is expected to
  fail there. Nothing about iOS 27 is proven until it runs on a device.
- Uninstalling the app deletes its container, including game files, saves and
  local backups. Keychain items may survive uninstall, so recovery handles both
surviving credentials with an empty container and restored saves with no
credentials.
- iOS suspends a backgrounded app within seconds: sockets drop during a Steam
  Guard app switch and a 3 GB download cannot continue in the background.

## Milestones

`IOS-n` ordinals are stable names, not the order of work. Execution order:
IOS-1, IOS-3 and the runtime-resolution part of IOS-4 start now in the
simulator; IOS-2 runs as soon as a device is paired and gates every iOS 27
claim; then IOS-5, IOS-6, the rest of IOS-4 (on-device download), IOS-7, IOS-8.

### IOS-1: Baseline host in this repo

Import the prototype's host, runtime patch, adapters and build scripts under
`ios/`, `scripts/ios/` and `src/` with its MIT notice. Add a simulator slice
(Mono, Spine and FMOD extension for `iphonesimulator`) so managed and UI work
has a local loop.

Verify: the unmodified game reaches its main menu in the iOS 27 simulator from
this repo's build. Device signing and install are checked when a device is
paired and do not hold up IOS-3.

### IOS-2: JIT on iOS 27 (feasibility gate)

One code arena reserved at startup, one writable alias, and a sub-allocator
behind `mono_valloc(MONO_MMAP_JIT)` / `mono_vfree`, with two ways to obtain the
arena: the debugger-assisted protocol on Trusted Execution Monitor devices and
plain `mmap` + `vm_remap` elsewhere (simulator and older devices). The native
probe executes and rewrites code inside the reserved arena. A Mac-side LLDB
script and the StikDebug path both drive the same in-app protocol.

Verify on a physical iOS 27 iPhone and iPad, after the debugger detaches:
allocate and run previously unused code slots, rewrite and run them from a
second thread, create, free and reuse dynamic methods past Mono's 16-entry
chunk freelist, then background and resume. Record arena high-water mark with
real mods before fixing the shipped size.

Nothing after this milestone is claimed to work on iOS 27 until this passes.

### IOS-3: One managed assembly, two platforms

Compile `src/STS2Mobile` for iOS through an iOS project file, behind a typed
`PlatformHost` seam that replaces the `GodotApp` calls (Android keeps JNI; iOS
uses the native bridge). Android-only patches are selected out at the
composition boundary in `ModEntry`, not with conditionals inside business
flows. Unsupported operations fail visibly rather than returning made-up
success. iOS deltas (audio session, Keychain, safe areas, lifecycle) come from
the prototype.

Verify: Android build and its existing test suites still pass; the iOS build
reaches the game through `STS2Mobile.ModEntry` with the Android patch set that
applies.

### IOS-4: Game-free launcher, install and restart model

- The app starts with no game present: runtime paths resolve the pinned BCL,
  GodotSharp and adapted Harmony from the bundle and game assemblies only from
  the validated active install. Updating the game does not need an app rebuild.
- SteamKit2 login (credentials, Steam Guard, QR), branch selection and depot
  download on the device. The 2FA wait and the download hold a background
  task, which buys bounded time only; both resume on return, and the download
  keeps the screen awake and resumes after process death.
- On-device PCK preparation ported to C#: Sentry removal in `project.binary`
  and version-derived `.cs` placeholders. A failed patch blocks activation.
- Install staging, rollback and the "startup healthy" validation that clears
  rollback state are kept (Android ties the last one to its startup journal).
- Restart-required is a terminal session state: writes and PLAY are blocked and
  the app shows what happens next; the dialog's button closes the app. Allowed reasons: first
  install, game update, branch switch. Every other Android restart site is
  removed or replaced on iOS.

JIT budget this milestone must meet: first run two JIT enables and one reopen;
normal start one enable; game update one extra reopen.

Verify: fresh install and a game update in the simulator without rebuilding the
app; interrupted download and interrupted PCK patch at each publish boundary.

### IOS-5: Saves and Steam Cloud

Port cloud sync, the Save Manager, the cloud write guard, account-scoped
backups and pending-upload recovery with their existing contracts. Persist
enough dirty/retry state to reconcile after a kill. Tokens go in the Keychain
with distinct locked / missing / error results. Game payload and staging live
in Application Support and are excluded from device backup; saves and backups
live in Documents and can be exported.

Verify: existing `tools/stability-tests` account and install fixtures; kill
during local save, cloud batch and download.

### IOS-6: Mods

Mod Hub and Workshop writing to `Documents/Mods`, the `ModsDisabled` stash,
zip import through the document picker, mod FMOD banks, and the BaseLib and
Harmony compatibility shims re-evaluated under Mono on iOS. Mod consent is
answered from the launcher so the first modded play does not cost a reopen.

Verify: the mods the prototype smoke-tested plus the ones this fork's Android
proofs use, loaded from Workshop on a device.

### IOS-7: iPhone and iPad experience

Phone-width launcher layout with 44 pt targets, safe-area insets, keyboard
handling for login, system-locale default language (zh-Hans → Chinese), native
gate limited to JIT states with one-tap hand-off to the JIT tool, log sharing,
full-screen iPad. LAN multiplayer with Bonjour.

### IOS-8: Distribution

Signed build script, IPA for sideloading, Chinese and English install guide
covering the JIT tool, and a statement of what was and was not device-tested.
No game files, FMOD or Spine binaries are committed.

## Not ported

APK self-update, logcat capture, storage permissions, the Storage Access
Framework bridge, Vulkan renderer recovery, the shader-warmup restart, the
in-game Quit button, and account switching in the first version.

## Evidence ladder

1. Pinned sources and artifact inventory.
2. Existing offline suites plus PCK fixtures.
3. Simulator: managed logic, launcher flows, Harmony and mods under Mono JIT.
4. Device package inspection (signing, entitlements, slices).
5. Physical iOS 27 iPhone and iPad: JIT, memory, FMOD and Spine, mods, saves,
   background and kill cases.

Simulator results establish UI and managed behaviour only. They say nothing
about Trusted Execution Monitor, memory limits, device audio/animation
libraries, JIT-tool compatibility or performance.

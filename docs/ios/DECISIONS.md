# iOS port decisions

Planned jointly on 2026-10-08/09: implementation by Claude Opus 5.5, counsel
from GPT-6 Astra (runtime, contracts) and Claude Fable 5.1 (product, UX).
Each entry gives the choice, why, and what would reopen it.

## D1. Shared `STS2Mobile` source with a typed platform seam

One source tree and assembly identity, an iOS project file, and a small typed
`PlatformHost` interface in place of string-dispatched `GodotApp` calls.
Android-only patches are chosen at the composition boundary.

Why: a second launcher would fork the cloud, account and install behaviour
this fork spent months hardening. A string `Call(name)` shim would let
unsupported operations return fabricated success.

Revisit if the first game-free bootstrap shows runtime-facing types that cannot
be isolated at entry and patch selection.

## D2. Reopen instead of mounting the game pack at runtime

First install, game update and branch switch end the process; the normal
launcher → PLAY path stays in one process, as on Android.

Why: `ProjectSettings::load_resource_pack` only mounts files and refreshes
class and UID caches. It does not load `project.binary` settings, rebuild the
input map, construct autoloads, rescan the extension list or change the
renderer (Godot 4.5.1 `project_settings.cpp`, `main.cpp`, `input_map.cpp`).
Reproducing engine startup is a project of its own.

Restart-required is a terminal state that blocks PLAY and writes: after a game
update or branch switch the loaded assemblies and static game state no longer
match the files on disk. The dialog's button closes the app, because pressing
Home and tapping the icon resumes the same process.

The seam exposes `RequestRestart(RestartReason)` with the allowed reasons as
the type instead of a bare restart call. Android maps every reason to its
process restart; iOS maps them to the terminal state, so a restart site that
has no allowed reason fails to compile.

Revisit after a fixture shows cold-boot parity for a runtime mount.

## D3. One reserved code arena, two ways to obtain it

A single executable arena with one writable alias and a real sub-allocator
(dynamic methods do free code; Mono keeps 16 cached chunks per size). The
arena comes from the debugger-assisted protocol on Trusted Execution Monitor
devices and from `mmap` + `vm_remap` elsewhere. Freed sub-allocations never
reach `munmap` or `mprotect` on the arena. Exhaustion is detected at a low
watermark; Mono asserts on a failed code allocation, so no recoverable
out-of-memory is promised.

Sizes of 128 and 256 MiB are experiment values. The shipped size comes from
device measurements with real mods.

Dissent kept on record (Astra): the prototype's Mono patch mixes memory
plumbing with semantic changes (`beforefieldinit` laziness applied to every
assembly, an `IgnoresAccessChecksTo` early return ahead of generic-argument
visibility checks). These are split out and justified separately, alongside
this fork's own `ModelDbInitPatch`, rather than stacked blindly.

## D4. Storage layout

Game payload, download state and staging: `Library/Application Support`,
excluded from device backup, hidden from Files. `Documents` (visible in Files):
`Mods/`, `ModsDisabled/`, `Saves/`, `Logs/`. Backups can be exported through
the share sheet because uninstalling deletes the container.

## D5. First-version cuts

Hidden or removed on iOS: account switching (each switch is a restart), the
in-game Quit button (backgrounding runs a bounded best-effort drain of the
cloud queue; suspension can interrupt it, so the durable retry state from
IOS-5 is what guarantees reconciliation), the shader
warmup restart, APK self-update, logcat capture, storage permission UI.

## D6. Order of work

The import, the simulator slice, the platform seam and runtime resolution from
the active install start immediately. Device JIT proof (IOS-2) runs as soon as
a device is paired and gates every iOS 27 claim. Then saves and cloud, mods,
and on-device download. AX can play before on-device download
exists by preparing the pack from the macOS Steam install.

Later option (not in the first version): run the launcher-only stage under the
Mono interpreter so login, download and Mod Hub need no JIT.

## D7. Identity

Bundle id `ai.ax0x.sts2launcher`. Display name "尖塔2启动器" (zh-Hans) and
"StS2 Launcher" (en). No Mega Crit marks in the name or icon. Default language
follows the system locale.

## Open

- Exact debugger protocol and whether it still works on iOS 27: under
  research, then device proof (IOS-2).
- Whether the increased-memory-limit entitlement is available to free
  developer accounts; affects who the public guide can promise iPhone support.
- StikDebug URL scheme for one-tap JIT hand-off: not yet verified.
## D8. Engine built from source for devices too

Decided 2026-10-09. The device app links `libgodot.a` built from `vendor/godot`
plus `ios/engine/godot-ios.patch`, not the one in the official export template.

Why: fights were bound by fill rate at the native 2752x2064 (44 to 78 fps on an
M4; 117 to 120 at 1.5x), Godot has no render-scale control on iOS, and its
display server truncated a fractional screen scale to an integer. The simulator
slice was already built from this tree, and iOS 27's UIScene requirement will
need an engine change as well.

Cost: a local engine build (a few minutes) becomes part of setting up, and the
patch has to be carried across Godot updates. The official template remains the
source of the Xcode project and resources.


# Building the iOS app

Everything runs from the repository root with the tool environment first on
`PATH`: `export PATH="$PWD/.tools/python/bin:$PATH"`.

## One-time inputs

- A Steam macOS install of the game (references and native extensions are
  taken from it): `python3 scripts/ios/build.py prepare`, then
  `python3 scripts/ios/build.py content` for a game pack to seed a device with.
- `ios/config.local.json` from `ios/config.example.json` (team, bundle id,
  device, App Store Connect key; see `docs/ios/STATUS.md` → Device signing).

## Engine

The app links a Godot 4.5.1 built from `vendor/godot` (commit `f62fdbde`) with
`ios/engine/godot-ios.patch` applied. The official export template is still
used for the Xcode project and resources; only `libgodot.a` is replaced.

```
cd vendor/godot
git apply ../../ios/engine/godot-ios.patch        # once per checkout
scons platform=ios target=template_release arch=arm64 module_mono_enabled=yes \
      vulkan=no production=no lto=none debug_symbols=no                  # device
scons platform=ios target=template_release arch=arm64 ios_simulator=yes \
      module_mono_enabled=yes vulkan=no production=no lto=none debug_symbols=no
```

The patch makes the iOS display server scale sizes in floating point, which the
render-scale setting needs (`STS2RenderFraction`, see `ios/jit/JitGate.mm`).
It also keeps a CPU copy of mesh surfaces created with
`ARRAY_FLAG_USE_DYNAMIC_UPDATE`, so `mesh_get_surface` does not read them back
from the GPU. The Spine extension calls it for every mesh it creates, and each
readback stalled the frame until the GPU was idle.
The simulator slice exists because the official .NET template ships an
x86_64-only simulator engine.

## Runtime

`python3 scripts/ios/build.py runtime` (add `--simulator` for the simulator)
builds Mono 9.0.20 from `vendor/dotnet-runtime-jit` with
`ios/jit/mono-ios-jit.patch`.

## App

```
python3 scripts/ios/build.py build              # device, signed
python3 scripts/ios/build.py install --content  # app plus the seed game pack
python3 scripts/ios/build.py launch             # start with JIT through LLDB
python3 scripts/ios/build.py build --simulator  # arm64 simulator app
```

`launch --env NAME=VALUE` passes test switches to the app; they are listed in
`docs/ios/STATUS.md`.

## Starting without a Mac

iOS only lets a process generate code after a debugger has attached to it, so
every start needs one. On the device that debugger is StikDebug, which reaches
the device's own debug service through a loopback VPN.

1. Install LocalDevVPN from the App Store on the device and connect it.
2. StikDebug is no longer on the App Store. Download the release IPA from
   `StikDebug/StikDebug`, set a bundle id under your team, sign it with a
   development profile that covers the device and install it with
   `xcrun devicectl device install app`.
3. Create the pairing record over USB with `idevice-tools` (built from
   `jkcoxson/idevice`, `cargo build --release -p idevice-tools`):
   `idevice-tools --udid <udid> remotepairing_lockdown pair <label> <file>`.
   It is a credential for the device; keep it out of the repo. Copy it to
   StikDebug as `Documents/pairingFile.plist` with
   `xcrun devicectl device copy to --domain-type appDataContainer`.
   The record stops working after an iOS update or reset; create it again.
4. Open the game app. Its gate opens StikDebug with
   `stikdebug://enable-jit?...&script-name=universal.js`; StikDebug returns to
   the app when the script is done. Turn off "Confirm JIT Links"
   in StikDebug's settings to skip its prompt.

The same `idevice-tools` binary takes whole-device screenshots
(`screenshot <file>`) and presses hardware buttons (`hid button home`); its
touch injection did not work on iPadOS 27.0.

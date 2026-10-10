#!/usr/bin/env python3
"""Mono JIT runtime and app backend for build.py."""

import hashlib
import json
import os
from pathlib import Path
import plistlib
import shutil
import subprocess
import selectors
import time
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[2]
CACHE = ROOT / ".cache"
SOURCE = ROOT / "vendor/dotnet-runtime-jit"
DOTNET = ROOT / ".tools/dotnet/dotnet"
VERSION = "9.0.20"
COMMIT = "3879076d9a06ce098d37c3882fb1845a6627335b"


class Target:
    """Physical device or simulator slice: SDK, runtime pack and output directories."""

    def __init__(self, simulator):
        self.simulator = simulator
        suffix = "-sim" if simulator else ""
        self.sdk = "iphonesimulator" if simulator else "iphoneos"
        self.rid = "iossimulator-arm64" if simulator else "ios-arm64"
        self.pack_sha256 = ("9936bc0eb9a3c9ffd42a84f3bdfa313d4d644381649e6410950d71b922d11824" if simulator
                            else "ccb189791773d4ca77cee3dd03e28dc2a9b6231b479e1e44e28088a378692b3f")
        self.min_version = ("-mios-simulator-version-min=16.0" if simulator else "-miphoneos-version-min=16.0")
        self.destination = "generic/platform=iOS Simulator" if simulator else "generic/platform=iOS"
        self.build = CACHE / ("jit-mono-build" + suffix)
        self.pack_root = CACHE / ("jit-runtime-pack" + suffix)
        self.pack = self.pack_root / "runtimes" / self.rid
        self.host = CACHE / ("jit-host" + suffix)
        self.derived = CACHE / ("xcode-jit" + suffix)
        self.app = self.derived / f"Build/Products/Release-{self.sdk}/StS2.app"


DEVICE = Target(False)
SIMULATOR = Target(True)


def run(*args, **kwargs):
    print("+ " + " ".join(str(arg) for arg in args), flush=True)
    return subprocess.run([str(arg) for arg in args], check=True, **kwargs)


def signing_identity(app, cfg):
    if cfg.get("sign_identity"):
        return cfg["sign_identity"]
    details = subprocess.run(["codesign", "--display", "--verbose=4", str(app)],
        check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    for line in details.stderr.splitlines():
        if line.startswith("Authority=Apple Development:"):
            return line.removeprefix("Authority=")
    raise SystemExit("Could not identify the development certificate; set sign_identity in ios/config.local.json")


def native_libraries(target):
    paths = [target.build / "mono/mini" / name for name in (
        "libmonosgen-2.0.a", "libmono-component-marshal-ilgen-static.a",
        "libmono-component-debugger-stub-static.a", "libmono-component-hot_reload-stub-static.a",
        "libmono-component-diagnostics_tracing-stub-static.a")]
    if not all(path.is_file() for path in paths):
        raise SystemExit("Build the runtime first: python3 scripts/ios/build.py runtime"
                         + (" --simulator" if target.simulator else ""))
    return paths


def prepare_adapters(managed, target):
    run(DOTNET, "build", ROOT / "src/STS2JitPrepare", "-c", "Release", cwd=ROOT)
    prepare = ROOT / "src/STS2JitPrepare/bin/Release/net9.0/STS2JitPrepare.dll"
    for mode, source in (("corelib", target.pack / "native/System.Private.CoreLib.dll"),
                         ("harmony", ROOT / "upstream/game-refs/0Harmony.dll")):
        run(DOTNET, prepare, mode, source, managed / "STS2JitSupport.dll", managed / source.name)


def copy_to_device(source, destination, cfg):
    run("xcrun", "devicectl", "device", "copy", "to", "--device", cfg["device"],
        "--source", source, "--destination", destination,
        "--domain-type", "appDataContainer", "--domain-identifier", cfg["bundle_id"])


def install_app(app, cfg):
    info = app / "Info.plist"
    if not info.is_file():
        raise SystemExit("Build the app before installing it")
    if plistlib.loads(info.read_bytes()).get("CFBundleIdentifier") != cfg["bundle_id"]:
        raise SystemExit("App bundle ID does not match config; rebuild before installing")
    run("xcrun", "devicectl", "device", "install", "app", "--device", cfg["device"], app)


def patch_runtime():
    patch = ROOT / "ios/jit/mono-ios-jit.patch"
    reverse = subprocess.run(["git", "apply", "--reverse", "--check", str(patch)],
        cwd=SOURCE, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    if reverse.returncode != 0:
        check = subprocess.run(["git", "apply", "--check", str(patch)], cwd=SOURCE)
        if check.returncode != 0:
            raise SystemExit("Runtime source differs from the supported patch; checkout left unchanged")
        run("git", "apply", patch, cwd=SOURCE)
    header = ROOT / "ios/jit/JitArena.h"
    shutil.copy2(header, SOURCE / "src/mono/mono/utils/sts2-ios-jit-memory.h")


def runtime(target=DEVICE):
    CACHE.mkdir(exist_ok=True)
    if not SOURCE.exists():
        run("git", "clone", "--depth", "1", "--branch", "v" + VERSION,
            "https://github.com/dotnet/runtime.git", SOURCE)
    actual = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=SOURCE, text=True).strip()
    if actual != COMMIT:
        raise SystemExit("Unexpected runtime revision; existing checkout left unchanged")
    patch_runtime()
    archive = CACHE / "downloads" / f"microsoft.netcore.app.runtime.mono.{target.rid}.{VERSION}.nupkg"
    archive.parent.mkdir(exist_ok=True)
    if not archive.exists():
        urllib.request.urlretrieve("https://api.nuget.org/v3-flatcontainer/"
            f"microsoft.netcore.app.runtime.mono.{target.rid}/{VERSION}/{archive.name}", archive)
    if hashlib.sha256(archive.read_bytes()).hexdigest() != target.pack_sha256:
        raise SystemExit("Runtime package checksum mismatch")
    with zipfile.ZipFile(archive) as zipped:
        zipped.extractall(target.pack_root)
    generated = SOURCE / "artifacts/obj"
    generated.mkdir(parents=True, exist_ok=True)
    (generated / "runtime_version.h").write_text("\n".join(
        f"#define Runtime{kind}{part} {number}" for kind, parts in (
            ("File", (("MajorVersion", 9), ("MinorVersion", 0), ("BuildVersion", 20), ("RevisionVersion", 0))),
            ("Product", (("MajorVersion", 9), ("MinorVersion", 0), ("PatchVersion", 20))))
        for part, number in parts) + "\n")
    (generated / "_version.c").write_text(
        f'static char sccsid[] __attribute__((used)) = "@(#)Version {VERSION} @Commit: {COMMIT}";\n')
    run("cmake", "-S", SOURCE / "src/mono", "-B", target.build, "-G", "Ninja",
        "-DCMAKE_SYSTEM_NAME=iOS", "-DCMAKE_OSX_ARCHITECTURES=arm64", "-DCMAKE_OSX_SYSROOT=" + target.sdk,
        "-DCMAKE_OSX_DEPLOYMENT_TARGET=16.0", "-DCMAKE_BUILD_TYPE=Release",
        "-DCLR_CMAKE_HOST_ARCH=arm64", "-DDISABLE_JIT=OFF", "-DDISABLE_AOT=ON",
        "-DDISABLE_REFLECTION_EMIT=OFF", "-DDISABLE_EXECUTABLES=ON",
        "-DDISABLE_EVENTPIPE=ON", "-DDISABLE_DEBUGGER_AGENT=ON", "-DDISABLE_LLDB=ON",
        "-DSTATIC_COMPONENTS=ON", "-DDISABLE_LINK_STATIC_COMPONENTS=ON",
        "-DDISABLE_SHARED_LIBS=ON", "-DENABLE_LAZY_GC_THREAD_CREATION=OFF",
        "-DENABLE_ICALL_EXPORT=ON", "-DGC_SUSPEND=preemptive")
    run("cmake", "--build", target.build, "-j", "8", "--target", "monosgen-static",
        "mono-component-marshal-ilgen-static", "mono-component-debugger-stub-static",
        "mono-component-hot_reload-stub-static", "mono-component-diagnostics_tracing-stub-static")


def validate_inputs(steam, target=DEVICE):
    native_libraries(target)
    content = CACHE / "StS2.pck"
    metadata = CACHE / "content-input.json"
    references = ROOT / "upstream/game-refs/sts2.dll"
    assembly = steam / "Contents/Resources/data_sts2_macos_arm64/sts2.dll"
    source_pack = steam / "Contents/Resources/Slay the Spire 2.pck"
    if not all(path.is_file() for path in (content, metadata, references, assembly, source_pack,
                                          steam / "Contents/Resources/release_info.json")):
        raise SystemExit("Run build.py prepare and content using the same Steam game before building")
    expected = json.loads(metadata.read_text())
    digest = hashlib.sha256(assembly.read_bytes()).hexdigest()
    if (hashlib.sha256(references.read_bytes()).hexdigest() != digest
            or expected.get("assembly_sha256") != digest
            or expected.get("source_pack_bytes") != source_pack.stat().st_size):
        raise SystemExit("Steam game, prepared references and content differ; rerun prepare and content")


def retag_for_simulator(binary):
    run("xcrun", "vtool", "-set-build-version", "iossim", "14.0", "17.5", "-replace",
        "-output", binary.with_name(binary.name + ".sim"), binary)
    binary.with_name(binary.name + ".sim").replace(binary)


def use_simulator_libraries(host):
    """Give the exported host simulator slices of its device-only native extensions.

    Spine and the published FMOD extension ship arm64 device binaries only. Their
    code does not depend on the platform, so the Mach-O build platform is retagged
    instead of rebuilding (FMOD's SDK headers are login-gated).
    """
    # Godot's .NET export templates carry an x86_64-only simulator engine.
    engine = ROOT / "vendor/godot/bin/libgodot.ios.template_release.arm64.simulator.a"
    if not engine.is_file():
        raise SystemExit("Build the arm64 simulator engine first; see docs/ios/building.md")
    shutil.copy2(engine, host / "StS2.xcframework/ios-arm64_x86_64-simulator/libgodot.a")
    name = "libspine_godot.ios.template_release"
    retag_for_simulator(host / "StS2/dylibs/addons/spine/ios" / (name + ".framework") / name)
    libraries = ROOT / "vendor/fmod-release/fmod/libs/iOS"
    for kind in ("fmod", "fmodstudio"):
        shutil.copy2(libraries / f"lib{kind}_iphonesimulator.a", host / f"StS2/addons/fmod/libs/ios/lib{kind}_iphoneos.a")
    framework = host / "StS2/dylibs/addons/fmod/libs/ios/libGodotFmod.ios.template_release.xcframework"
    info = plistlib.loads((framework / "Info.plist").read_bytes())
    library = info["AvailableLibraries"][0]
    # Godot's exporter wraps the dylib in a framework inside the slice.
    wrapper = framework / library["LibraryIdentifier"] / library["LibraryPath"]
    retag_for_simulator(wrapper / wrapper.stem if wrapper.is_dir() else wrapper)
    (framework / library["LibraryIdentifier"]).rename(framework / "ios-arm64-simulator")
    library["LibraryIdentifier"] = "ios-arm64-simulator"
    library["SupportedPlatformVariant"] = "simulator"
    (framework / "Info.plist").write_bytes(plistlib.dumps(info))


def use_source_engine(host):
    """Link the device app against the engine built from vendor/godot.

    The official export template works, but the fork carries a few fixes the
    port depends on (fractional screen scale for the render-scale setting), and
    the simulator slice is built from the same tree anyway.
    """
    engine = ROOT / "vendor/godot/bin/libgodot.ios.template_release.arm64.a"
    if not engine.is_file():
        raise SystemExit("Build the device engine first: scons platform=ios target=template_release "
                         "arch=arm64 module_mono_enabled=yes vulkan=no in vendor/godot")
    shutil.copy2(engine, host / "StS2.xcframework/ios-arm64/libgodot.a")


def build_game(cfg, steam, host, target=DEVICE):
    from prepare_xcode import prepare

    libraries = native_libraries(target)
    framework = host / "StS2/dylibs/sts2.framework"
    framework.mkdir(parents=True, exist_ok=True)
    sdk = subprocess.check_output(["xcrun", "--sdk", target.sdk, "--show-sdk-path"], text=True).strip()
    run("xcrun", "clang++", "-dynamiclib", "-arch", "arm64", "-isysroot", sdk,
        target.min_version, "-std=c++17", "-fobjc-arc", "-O2",
        "-I" + str(target.pack / "native/include/mono-2.0"), ROOT / "ios/jit/RuntimeHost.mm",
        ROOT / "ios/jit/JitArena.mm",
        *["-Wl,-force_load," + str(library) for library in libraries],
        "-framework", "Foundation", "-framework", "Security", "-lz", "-liconv",
        "-Wl,-install_name,@rpath/sts2.framework/sts2", "-o", framework / "sts2")
    prepare(host, cfg, (CACHE / "StS2.pck").stat().st_size)
    if target.simulator:
        use_simulator_libraries(host)
    else:
        use_source_engine(host)
    command = ["xcodebuild", "-project", host / "StS2.xcodeproj", "-scheme", "StS2",
        "-configuration", "Release", "-destination", target.destination,
        "-derivedDataPath", target.derived]
    if target.simulator:
        # Simulator apps run unsigned; only the arm64 slice has a Mono runtime.
        command += ["ARCHS=arm64", "ONLY_ACTIVE_ARCH=NO", "CODE_SIGNING_ALLOWED=NO"]
    elif cfg.get("provisioning_profile"):
        # An installed development profile (for example a team wildcard) signs
        # without contacting Apple, which blocks provisioning until agreements are accepted.
        # Xcode refuses managed profiles for manual signing, so the app is signed below.
        command += ["CODE_SIGNING_ALLOWED=NO"]
    else:
        command += ["-allowProvisioningUpdates", "-allowProvisioningDeviceRegistration",
            "DEVELOPMENT_TEAM=" + cfg["team_id"]]
        if cfg.get("asc_key_path"):
            # App Store Connect API key signs in for provisioning without an Xcode account.
            command += ["-authenticationKeyPath", Path(cfg["asc_key_path"]).expanduser(),
                "-authenticationKeyID", cfg["asc_key_id"], "-authenticationKeyIssuerID", cfg["asc_issuer_id"]]
    run(*command, "build")
    app = target.app
    identity = "-" if target.simulator else signing_identity(app, cfg)
    # Godot 4.5.1 has no UIScene lifecycle, which iOS 27 requires of apps linked
    # against its SDK (they trap at launch). Record the iOS 26 SDK instead.
    executable = app / "StS2"
    run("xcrun", "vtool", "-set-build-version", "iossim" if target.simulator else "ios", "16.0", "26.0",
        "-replace", "-output", executable, executable)
    shutil.copy2(steam / "Contents/Resources/release_info.json", app / "release_info.json")
    managed = app / "Managed"
    if managed.exists():
        shutil.rmtree(managed)
    managed.mkdir()
    for directory in (target.pack / "lib/net9.0", ROOT / "upstream/game-refs"):
        for assembly in directory.glob("*.dll"):
            shutil.copy2(assembly, managed / assembly.name)
    for name in ("STS2JitSupport", "STS2Mobile.iOS"):
        run(DOTNET, "build", ROOT / "src" / name, "-c", "Release", "-o", managed)
    prepare_adapters(managed, target)
    for library in (target.pack / "native").glob("libSystem.*.dylib"):
        destination = app / "Frameworks" / library.name
        shutil.copy2(library, destination)
        run("codesign", "--force", "--sign", identity, destination)
    if target.simulator:
        run("codesign", "--force", "--sign", identity, app)
        return
    if cfg.get("provisioning_profile"):
        sign_with_profile(app, cfg)
        return
    entitlements = target.derived / "Build/Intermediates.noindex/StS2.build/Release-iphoneos/StS2.build/StS2.app.xcent"
    run("codesign", "--force", "--sign", identity, "--entitlements", entitlements, app)


def sign_with_profile(app, cfg):
    """Sign an unsigned device build with an installed development profile."""
    name = cfg["provisioning_profile"] + ".mobileprovision"
    candidates = [Path.home() / "Library/MobileDevice/Provisioning Profiles" / name,
                  Path.home() / "Library/Developer/Xcode/UserData/Provisioning Profiles" / name]
    source = next((path for path in candidates if path.is_file()), None)
    if source is None:
        raise SystemExit(f"Provisioning profile {name} is not installed")
    profile = plistlib.loads(subprocess.check_output(["security", "cms", "-D", "-i", str(source)],
        stderr=subprocess.DEVNULL))
    granted = profile["Entitlements"]
    application = cfg["team_id"] + "." + cfg["bundle_id"]
    if granted["application-identifier"] not in (application, cfg["team_id"] + ".*"):
        raise SystemExit("The provisioning profile does not cover " + cfg["bundle_id"])
    shutil.copy2(source, app / "embedded.mobileprovision")
    entitlements = CACHE / "device.entitlements"
    entitlements.write_bytes(plistlib.dumps({
        "application-identifier": application,
        "com.apple.developer.team-identifier": cfg["team_id"],
        "get-task-allow": True,
        "keychain-access-groups": [application],
    }))
    identity = cfg["sign_identity"]
    nested = sorted((app / "Frameworks").glob("*.framework")) + sorted((app / "Frameworks").glob("*.dylib"))
    for item in nested:
        run("codesign", "--force", "--sign", identity, "--timestamp=none", item)
    run("codesign", "--force", "--sign", identity, "--timestamp=none", "--entitlements", entitlements, app)


def install_game(cfg, push_content=False, mods=None):
    if push_content and not (CACHE / "StS2.pck").is_file():
        raise SystemExit("Prepare content first with scripts/ios/build.py content")
    if mods is not None and not mods.is_dir():
        raise SystemExit("--mods must name a directory containing mod folders")
    install_app(DEVICE.app, cfg)
    if push_content:
        copy_to_device(CACHE / "StS2.pck", "Documents/game/SlayTheSpire2.pck", cfg)
    if mods is not None:
        copy_to_device(mods, "Documents/mods", cfg)
    print("Installed StS2 JIT. Enable JIT with build.py launch.")


def launch(cfg, pid=None, environment=None):
    """Attach LLDB, serve the app's JIT26 protocol requests, and leave when it asks."""
    if pid is None:
        result = CACHE / "jit-launch.json"
        variables = ["--environment-variables", json.dumps(environment)] if environment else []
        run("xcrun", "devicectl", "device", "process", "launch", "--device", cfg["device"],
            "--start-stopped", "--terminate-existing", *variables, "--json-output", result, cfg["bundle_id"])
        pid = json.loads(result.read_text())["result"]["process"]["processIdentifier"]
    debugger = subprocess.Popen(["xcrun", "lldb", "-o", "settings set use-color false",
        "-o", "settings set show-statusline false", "-o", "device select " + cfg["device"],
        "-o", f"device process attach -p {pid}",
        "-o", "command script import " + str(ROOT / "scripts/ios/jit26_bless.py")],
        stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    output = b""
    resumed = False
    handled_stops = 1  # the stop that attaching produces
    selector = selectors.DefaultSelector()
    selector.register(debugger.stdout, selectors.EVENT_READ)
    # Preparing the code arena writes every page over the device tunnel.
    deadline = time.monotonic() + 900
    try:
        while time.monotonic() < deadline:
            if not selector.select(1):
                continue
            chunk = os.read(debugger.stdout.fileno(), 8192)
            if not chunk:
                break
            print(chunk.decode(errors="replace"), end="", flush=True)
            output += chunk
            # The attach completes asynchronously, after the import has been echoed.
            if not resumed and b"Stop hook #" in output and f"Process {pid} stopped".encode() in output:
                debugger.stdin.write(b"process continue\n")
                debugger.stdin.flush()
                resumed = True
            for failure in (b"[JIT26] write failed", b"[JIT26] unknown command", b"is unsupported"):
                if failure in output:
                    raise SystemExit("The debugger could not prepare JIT memory; see its output above")
            # LLDB does not always resume after the stop hook handled a request.
            stops = output.count(f"Process {pid} stopped".encode())
            if resumed and stops > handled_stops and b"[JIT26] detached" not in output:
                handled_stops = stops
                debugger.stdin.write(b"process continue\n")
                debugger.stdin.flush()
            if b"[JIT26] detached" in output:
                debugger.stdin.write(b"quit\n")
                debugger.stdin.flush()
                debugger.communicate(timeout=15)
                print(f"JIT prepared for process {pid}. The debugger is detached.")
                return
            if b"exited with status" in output:
                raise SystemExit("The app exited before JIT was prepared; see the debugger output above")
        raise SystemExit("Could not prepare JIT; check device connection and debugger output")
    finally:
        selector.close()
        if debugger.poll() is None:
            debugger.terminate()
            debugger.wait(timeout=10)

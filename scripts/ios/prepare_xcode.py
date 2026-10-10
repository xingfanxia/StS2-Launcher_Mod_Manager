#!/usr/bin/env python3
"""Attach the Mono JIT framework and startup gate to Godot's Xcode host."""

from pathlib import Path
import plistlib
import re
import shutil

ROOT = Path(__file__).resolve().parents[2]


def prepare(build, config, content_size):
    framework = build / "StS2/dylibs/sts2.framework"
    if not (framework / "sts2").is_file():
        raise SystemExit("Build the Mono JIT framework first")
    (framework / "Info.plist").write_bytes(plistlib.dumps({
        "CFBundleExecutable": "sts2",
        "CFBundleIdentifier": config["bundle_id"] + ".runtime",
        "CFBundleName": "sts2",
        "CFBundlePackageType": "FMWK",
        "CFBundleShortVersionString": "1.0",
        "CFBundleVersion": "1",
        "MinimumOSVersion": "16.0",
    }))

    project = build / "StS2.xcodeproj/project.pbxproj"
    text = project.read_text()
    file_id, copy_id = "535453320000000000000001", "535453320000000000000002"
    if file_id not in text:
        text = text.replace("/* End PBXFileReference section */", f'''
        {file_id} = {{isa = PBXFileReference; lastKnownFileType = wrapper.framework;
            path = "StS2/dylibs/sts2.framework"; sourceTree = "<group>"; }};
/* End PBXFileReference section */''')
        text = text.replace("/* End PBXBuildFile section */", f'''
        {copy_id} = {{isa = PBXBuildFile; fileRef = {file_id};
            settings = {{ATTRIBUTES = (CodeSignOnCopy, ); }}; }};
/* End PBXBuildFile section */''')
        start = text.index("/* Begin PBXCopyFilesBuildPhase section */")
        position = text.index("files = (", start) + len("files = (")
        text = text[:position] + f"\n{copy_id}," + text[position:]
    # The native bridge supplies the playback audio session and device-only Keychain storage.
    text = text.replace('lastKnownFileType = sourcecode.cpp.cpp; path = dummy.cpp;',
                        'lastKnownFileType = sourcecode.cpp.objcpp; path = dummy.cpp;')
    if "-Wl,-export_dynamic" not in text:
        text = re.sub(r'(OTHER_LDFLAGS = ")([^"]*)(";)',
                      lambda match: match[1] + match[2] + ' -framework AVFoundation -framework Security -Wl,-export_dynamic' + match[3], text)
    if "-framework CoreImage" not in text:
        text = text.replace("-Wl,-export_dynamic", "-Wl,-export_dynamic -framework CoreImage")
    project.write_text(text)
    shutil.copy2(ROOT / "ios/Sts2Native.mm", build / "StS2/Sts2Native.mm")

    # The game with mods sits near the default per-app memory limit; ask for the
    # higher one. The capability has to exist on the App ID, which Xcode's
    # automatic signing sets up (the App Store Connect API cannot).
    entitlements_path = build / "StS2/StS2.entitlements"
    entitlements = plistlib.loads(entitlements_path.read_bytes()) if entitlements_path.exists() else {}
    entitlements["com.apple.developer.kernel.increased-memory-limit"] = True
    entitlements_path.write_bytes(plistlib.dumps(entitlements))

    info_path = build / "StS2/StS2-Info.plist"
    info = plistlib.loads(info_path.read_bytes())
    info["godot_cmdline"] = ["--main-pack", "user://game/SlayTheSpire2.pck"]
    if config.get("profile", False):
        info["godot_cmdline"] += ["--", "--sts2-profile"]
    info["CFBundleDisplayName"] = "StS2 Launcher"
    # Every prepared arena page counts against the app's memory limit at once.
    # 14 Workshop mods peaked at 78 MiB over an hour of play (2026-10-09); the
    # background warm-up of the game assembly comes on top of that.
    info["STS2JitArenaMiB"] = int(config.get("jit_arena_mib", 256))
    # Share of the screen's native scale to render at; fights are fill-rate
    # bound at full resolution (docs/ios/STATUS.md).
    info["STS2RenderFraction"] = float(config.get("render_fraction", 0.75))
    info["STS2ContentSize"] = content_size
    info["UIFileSharingEnabled"] = True
    info["LSSupportsOpeningDocumentsInPlace"] = True
    info["NSLocalNetworkUsageDescription"] = "Find and join Slay the Spire 2 games on your Wi-Fi network."
    info["NSBonjourServices"] = ["_sts2lan._udp"]
    orientations = ["UIInterfaceOrientationLandscapeLeft", "UIInterfaceOrientationLandscapeRight"]
    info["UISupportedInterfaceOrientations"] = orientations
    info["UISupportedInterfaceOrientations~ipad"] = orientations
    info_path.write_bytes(plistlib.dumps(info))

    shutil.copy2(ROOT / "ios/jit/JitGate.mm", build / "StS2/JitGate.mm")
    dummy = build / "StS2/dummy.cpp"
    content = dummy.read_text()
    if "load_all_fmod_plugins" not in content:
        content += '''
// The game has no custom FMOD DSP plugins to register.
extern "C" __attribute__((visibility("default"))) __attribute__((used))
unsigned int *load_all_fmod_plugins(void *, unsigned int *count) {
    if (count) *count = 0;
    return nullptr;
}
'''
    if '#include "Sts2Native.mm"' not in content:
        content += '\n#include "Sts2Native.mm"\n'
    if '#include "JitGate.mm"' not in content:
        content += '\n#include "JitGate.mm"\n'
    dummy.write_text(content)
    print("Prepared Mono JIT framework, startup gate and native extensions")

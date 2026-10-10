#!/usr/bin/env python3
"""Prepare, build, install and launch the iOS Mono JIT game."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess

from pck_ls import read_dir

ROOT = Path(__file__).resolve().parents[2]
CACHE = ROOT / ".cache"
IOS = ROOT / "ios"
DOTNET = ROOT / ".tools/dotnet/dotnet"
GODOT = ROOT / ".tools/godot/Godot_mono.app/Contents/MacOS/Godot"
DEFAULT_GAME = Path.home() / "Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app"


def run(*args):
    print("+ " + " ".join(str(a) for a in args), flush=True)
    env = dict(os.environ, DOTNET_ROOT=str(DOTNET.parent))
    env["PATH"] = str(DOTNET.parent) + os.pathsep + env.get("PATH", "")
    subprocess.run([str(a) for a in args], cwd=ROOT, env=env, check=True)


def copy(source, destination):
    destination.parent.mkdir(parents=True, exist_ok=True)
    if source.is_dir():
        shutil.copytree(source, destination, dirs_exist_ok=True)
    else:
        shutil.copy2(source, destination)


def config(require_device=False):
    path = IOS / "config.local.json"
    if not path.exists():
        raise SystemExit("Create ios/config.local.json from ios/config.example.json first")
    value = json.loads(path.read_text())
    # A second device can be targeted without editing the config.
    if os.environ.get("STS2_IOS_DEVICE"):
        value["device"] = os.environ["STS2_IOS_DEVICE"]
    for key in (("team_id", "bundle_id", "device") if require_device else ("team_id", "bundle_id")):
        if not isinstance(value.get(key), str) or not re.fullmatch(r"[A-Za-z0-9.-]+", value[key]) or value[key].startswith("YOUR_"):
            raise SystemExit(f"Set a valid {key} in {path}")
    return value


def prepare(game):
    resources = game / "Contents/Resources"
    managed = resources / "data_sts2_macos_arm64"
    if not (managed / "sts2.dll").is_file():
        raise SystemExit(f"No supported macOS ARM64 game installation at {game}")
    framework_names = {p.name for p in (ROOT / ".tools/dotnet/shared/Microsoft.NETCore.App").glob("9.*/*.dll")}
    if not framework_names:
        raise SystemExit("Install the pinned .NET 9 SDK into .tools/dotnet first")
    references = ROOT / "upstream/game-refs"
    references.mkdir(parents=True, exist_ok=True)
    for old in references.glob("*.dll"):
        old.unlink()
    for dll in managed.glob("*.dll"):
        if dll.name in framework_names:
            continue
        copy(dll, references / dll.name)

    with (resources / "Slay the Spire 2.pck").open("rb") as pack:
        for name, offset, size, flags in read_dir(pack):
            if name not in {"addons/fmod/fmod.gdextension", "addons/spine/spine_godot_extension.gdextension"}:
                continue
            if flags & 1:
                raise SystemExit("Encrypted extensions are unsupported")
            pack.seek(offset)
            path = IOS / name
            path.parent.mkdir(parents=True, exist_ok=True)
            descriptor = pack.read(size).decode("utf-8")
            # Steam ships release frameworks; use those for the export editor too.
            descriptor = descriptor.replace(".macos.editor.framework", ".macos.template_release.framework")
            descriptor = descriptor.replace("libfmodL.dylib", "libfmod.dylib")
            descriptor = descriptor.replace("libfmodstudioL.dylib", "libfmodstudio.dylib")
            path.write_text(descriptor)

    frameworks = game / "Contents/Frameworks"
    for name in ("libGodotFmod.macos.template_release.framework", "libfmod.dylib", "libfmodstudio.dylib"):
        copy(frameworks / name, IOS / "addons/fmod/libs/macos" / name)
    copy(frameworks / "libspine_godot.macos.template_release.framework",
         IOS / "addons/spine/macos/libspine_godot.macos.template_release.framework")
    for name in ("libGodotFmod.ios.template_release.xcframework", "libfmod_iphoneos.a", "libfmodstudio_iphoneos.a"):
        copy(ROOT / "vendor/fmod-release/fmod/libs/iOS" / name, IOS / "addons/fmod/libs/ios" / name)
    name = "libspine_godot.ios.template_release.framework"
    copy(ROOT / "vendor/spine-runtimes/spine-godot/bin/ios" / name, IOS / "addons/spine/ios" / name)


def content(game):
    from pck_add_cs import patch as add_scripts
    from pck_patch_sentry import patch as remove_sentry

    managed = game / "Contents/Resources/data_sts2_macos_arm64/sts2.dll"
    paths = sorted(set(re.findall(rb"res://[A-Za-z0-9_/.]+\.cs", managed.read_bytes())))
    if not paths:
        raise SystemExit("No embedded Godot script paths found; unsupported game version")
    paths_file = CACHE / "cs_paths.txt"
    paths_file.write_bytes(b"\n".join(paths) + b"\n")
    source = game / "Contents/Resources/Slay the Spire 2.pck"
    remove_sentry(source, CACHE / "game-nosentry.pck")
    add_scripts(CACHE / "game-nosentry.pck", paths_file, CACHE / "StS2.pck")
    metadata = {
        "assembly_sha256": hashlib.sha256(managed.read_bytes()).hexdigest(),
        "source_pack_bytes": source.stat().st_size,
        "script_count": len(paths),
    }
    (CACHE / "content-input.json").write_text(json.dumps(metadata, indent=2) + "\n")


def export_host(cfg, destination):
    # A separate shell prevents Godot's editor from also building both simulators.
    host = CACHE / "ios-host"
    if host.exists():
        shutil.rmtree(host)
    host.mkdir(parents=True)
    for name in ("project.godot", "bootstrap.tscn", "icon.svg"):
        copy(IOS / name, host / name)
    copy(IOS / "addons", host / "addons")
    preset = (IOS / "export_presets.template.cfg").read_text()
    for key, value in {
        "TEAM_ID": cfg["team_id"], "BUNDLE_ID": cfg["bundle_id"],
        "TEMPLATE": str(ROOT / ".tools/templates/ios.zip"),
    }.items():
        preset = preset.replace("@" + key + "@", value)
    (host / "export_presets.cfg").write_text(preset)
    if destination.exists():
        shutil.rmtree(destination)
    destination.mkdir(parents=True)
    # Let extension documentation callbacks drain before editor shutdown.
    run(GODOT, "--headless", "--path", host, "--editor", "--quit-after", "60")
    run(GODOT, "--headless", "--path", host, "--export-release", "iOS", destination / "StS2.ipa")


def main():
    import jit

    parser = argparse.ArgumentParser(description=__doc__)
    stages = parser.add_subparsers(dest="stage", required=True)
    for name in ("prepare", "content", "build"):
        stage = stages.add_parser(name)
        stage.add_argument("--game", type=Path, help="Steam macOS ARM64 .app; overrides config.game")
    for name in ("runtime", "build"):
        stages.choices.get(name, None) or stages.add_parser(name)
        stages.choices[name].add_argument("--simulator", action="store_true",
            help="Target the arm64 iOS simulator instead of a physical device")
    installer = stages.add_parser("install")
    installer.add_argument("--content", action="store_true", help="Transfer the prepared game pack (about 2 GB)")
    installer.add_argument("--mods", type=Path, help="Copy this directory of mod folders to Documents/mods")
    launcher = stages.add_parser("launch")
    launcher.add_argument("--pid", type=int, help="Enable JIT for an already running app process")
    launcher.add_argument("--env", action="append", default=[], metavar="NAME=VALUE",
        help="Environment variable for the launched app; repeatable")
    args = parser.parse_args()
    CACHE.mkdir(exist_ok=True)
    target = jit.SIMULATOR if getattr(args, "simulator", False) else jit.DEVICE
    if args.stage == "runtime":
        jit.runtime(target)
        return
    cfg = config(require_device=args.stage in {"install", "launch"})
    if args.stage in {"prepare", "content", "build"}:
        game = (args.game or Path(cfg.get("game", str(DEFAULT_GAME)))).expanduser().resolve()
        if args.stage == "prepare":
            prepare(game)
        elif args.stage == "content":
            content(game)
        else:
            jit.validate_inputs(game, target)
            export_host(cfg, target.host)
            jit.build_game(cfg, game, target.host, target)
    elif args.stage == "install":
        jit.install_game(cfg, args.content, args.mods.expanduser().resolve() if args.mods else None)
    else:
        jit.launch(cfg, args.pid, dict(item.split("=", 1) for item in args.env))


if __name__ == "__main__":
    main()

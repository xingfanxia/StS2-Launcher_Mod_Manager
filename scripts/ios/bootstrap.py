#!/usr/bin/env python3
"""Fetch pinned public build tools and build Spine for physical iOS devices."""

import hashlib
from pathlib import Path
import shutil
import subprocess
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[2]
DOWNLOADS = ROOT / ".cache/downloads"


def run(*args, cwd=ROOT):
    subprocess.run([str(a) for a in args], cwd=cwd, check=True)


def download(name, url, digest, algorithm="sha256"):
    path = DOWNLOADS / name
    def valid():
        if not path.exists():
            return False
        checksum = hashlib.new(algorithm)
        with path.open("rb") as stream:
            for block in iter(lambda: stream.read(1024 * 1024), b""):
                checksum.update(block)
        return checksum.hexdigest() == digest

    if not valid():
        print(f"Downloading {name} (resume supported)", flush=True)
        run("curl", "--fail", "--location", "--retry", "5", "--continue-at", "-", "--output", path, url)
        if not valid():
            raise SystemExit(f"Checksum mismatch: {path}. Inspect/remove the cached download and retry.")
    return path


def checkout(path, url, commit, sparse=()):
    if not path.exists():
        path.mkdir(parents=True)
        run("git", "init", path)
        run("git", "remote", "add", "origin", url, cwd=path)
        if sparse:
            run("git", "sparse-checkout", "set", *sparse, cwd=path)
        run("git", "fetch", "--depth", "1", "origin", commit, cwd=path)
        run("git", "checkout", "--detach", "FETCH_HEAD", cwd=path)
    actual = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=path, text=True).strip()
    if actual != commit:
        raise SystemExit(f"Existing checkout {path} is at {actual}; expected {commit}. Left unchanged.")


def main():
    if sys.platform != "darwin":
        raise SystemExit("This workflow requires an Apple Silicon Mac with Xcode")
    run("xcodebuild", "-version")
    DOWNLOADS.mkdir(parents=True, exist_ok=True)
    tools = ROOT / ".tools"
    dotnet = tools / "dotnet"
    if not (dotnet / "dotnet").exists():
        archive = download("dotnet9.tar.gz",
            "https://builds.dotnet.microsoft.com/dotnet/Sdk/9.0.318/dotnet-sdk-9.0.318-osx-arm64.tar.gz",
            "4305c929005f47a933f33c253b6f47f8b21fd6096c7e359e78495e0de7c8ed1c6700aec2785a983d76ac7c99a66b1422abedb4056259b00e748ffbe36f243532", "sha512")
        dotnet.mkdir(parents=True, exist_ok=True)
        run("tar", "-xzf", archive, "-C", dotnet)
    godot = tools / "godot"
    if not (godot / "Godot_mono.app").exists():
        archive = download("godot-mono.zip",
            "https://github.com/godotengine/godot/releases/download/4.5.1-stable/Godot_v4.5.1-stable_mono_macos.universal.zip",
            "f00ee565e9d3682584117ef8865f5ff6d8f571fbf2733075ee06b9e0953261b8")
        run("ditto", "-x", "-k", archive, godot)
    templates = tools / "templates"
    if not (templates / "ios.zip").exists():
        archive = download("godot-templates.tpz",
            "https://github.com/godotengine/godot/releases/download/4.5.1-stable/Godot_v4.5.1-stable_mono_export_templates.tpz",
            "c425633061bb49f4390bdcece2b9ce68b3b0be3c71095b40644d8f6f17b1146e")
        templates.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(archive) as zipped, zipped.open("templates/ios.zip") as src:
            with (templates / "ios.zip").open("wb") as dst:
                shutil.copyfileobj(src, dst)
    fmod = ROOT / "vendor/fmod-release"
    if not (fmod / "fmod/libs/iOS/libfmodstudio_iphoneos.a").exists():
        archive = download("fmod-addons.zip",
            "https://github.com/utopia-rise/fmod-gdextension/releases/download/6.1.0-4.5.0/addons.zip",
            "8796f6cd1cb38bf7ff4da2648f8af2695244ffdb80a7ac4ab8a171ca44895713")
        with zipfile.ZipFile(archive) as zipped:
            for name in zipped.namelist():
                if name.startswith("fmod/libs/iOS/"):
                    zipped.extract(name, fmod)

    spine = ROOT / "vendor/spine-runtimes"
    checkout(spine, "https://github.com/EsotericSoftware/spine-runtimes.git",
             "e7dc1435fa4a0083ab431f1b28e083c14a1f5c68", ("spine-cpp", "spine-godot"))
    checkout(spine / "spine-godot/godot-cpp", "https://github.com/godotengine/godot-cpp.git",
             "e83fd0904c13356ed1d4c3d09f8bb9132bdc6b77")
    if not (spine / "spine-godot/bin/ios/libspine_godot.ios.template_release.framework").exists():
        python = tools / "python/bin/python"
        if not python.exists():
            run(sys.executable, "-m", "venv", tools / "python")
        run(python, "-m", "pip", "install", "scons==4.11.1")
        shutil.copytree(spine / "spine-cpp/spine-cpp", spine / "spine-godot/spine_godot/spine-cpp", dirs_exist_ok=True)
        run(tools / "python/bin/scons", "platform=ios", "arch=arm64", "target=template_release",
            "ios_min_version=14.0", "-j8", cwd=spine / "spine-godot")
    print("Public tools ready. Continue with build.py prepare using your installed Steam copy.")


if __name__ == "__main__":
    main()

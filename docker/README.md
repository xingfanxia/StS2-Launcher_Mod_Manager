# Reproducible APK build

The upstream release process depends on binaries that cannot be committed for
licensing reasons. This container pins the public toolchain and runs the
repository's existing `scripts/setup-deps.sh` and `scripts/build.sh` inside an
isolated copy of the checkout. The image contains only the public toolchain;
the build script is always read from the mounted checkout at
`/src/docker/build-apk.sh`.

## Dependency directory

Mount a private directory at `/deps` with this layout:

```text
StS2Launcher-v0.2.0.apk
Godot_v4.5.1-stable_mono_export_templates.tpz
fmodstudioapi20313android.tar.gz
data_sts2_windows_x86_64/
  sts2.dll
fmod-android-2.03.13/
  fmod.jar
  libfmod.so
  libfmodstudio.so
  libGodotFmod.android.template_release.arm64.so
```

The first three inputs correspond to the files expected by
`scripts/setup-deps.sh`. `sts2.dll` comes from a locally owned Steam copy of the
game and is used only as a compile-time reference. The final directory is the
matched FMOD 2.03.13 set used by current upstream releases; it intentionally
overrides the older FMOD 2.02 binaries harvested by `setup-deps.sh`.

Do not commit or upload this directory as a public Actions artifact.

## CI trust boundary

The workflow builds the public toolchain image on a GitHub-hosted runner and
publishes it to GHCR under an immutable Dockerfile hash. A dedicated rootless
self-hosted runner then pulls that image and mounts the private dependency
directory read-only. The APK job runs only for trusted `main` pushes and manual
runs on `main`; pull requests never receive access to the private inputs.

## Local build

```bash
docker build --platform linux/amd64 -t sts2-launcher-build:local docker

docker run --rm --platform linux/amd64 \
  -v "$PWD:/src:ro" \
  -v "/path/to/req_files:/deps:ro" \
  -v "sts2-launcher-cache:/cache" \
  -v "/path/to/output:/out" \
  sts2-launcher-build:local
```

Without signing inputs this produces an aligned but unsigned release APK. To
create an installable APK, mount a keystore and pass all three signing values:

```bash
docker run --rm \
  -v "$PWD:/src:ro" \
  -v "/path/to/req_files:/deps:ro" \
  -v "/path/to/output:/out" \
  -v "/path/to/signing:/signing:ro" \
  -e ANDROID_KEYSTORE_PATH=/signing/sts2.keystore \
  -e ANDROID_KEYSTORE_PASSWORD \
  -e ANDROID_KEY_ALIAS \
  sts2-launcher-build:local
```

An APK signed with a fork-specific key cannot update an APK signed by the
upstream maintainer. Android requires one uninstall before switching signing
identities; subsequent builds signed by the same fork key can update in place.

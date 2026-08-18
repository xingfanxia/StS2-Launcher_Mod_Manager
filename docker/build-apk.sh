#!/usr/bin/env bash
set -euo pipefail

SOURCE_DIR="${SOURCE_DIR:-/src}"
DEPS_DIR="${DEPS_DIR:-/deps}"
OUTPUT_DIR="${OUTPUT_DIR:-/out}"
WORK_DIR="${WORK_DIR:-/workspace/repo}"
CACHE_DIR="${CACHE_DIR:-/cache}"
FMOD_DIR="$DEPS_DIR/fmod-android-2.03.13"

fail() {
    echo "ERROR: $*" >&2
    exit 1
}

require_file() {
    [ -f "$1" ] || fail "Missing $1"
}

require_file "$SOURCE_DIR/scripts/setup-deps.sh"
require_file "$SOURCE_DIR/scripts/build.sh"
require_file "$FMOD_DIR/fmod.jar"
require_file "$FMOD_DIR/libfmod.so"
require_file "$FMOD_DIR/libfmodstudio.so"
require_file "$FMOD_DIR/libGodotFmod.android.template_release.arm64.so"

# libfmod resolves these Java helpers from JNI_OnLoad. A partial fmod.jar can
# compile and package successfully, but Android then rejects JNI_OnLoad's FMOD
# error 28 and the activity crashes before onCreate.
required_fmod_classes=(
    org/fmod/AudioDevice.class
    'org/fmod/FMOD$PluginAudioDeviceCallback.class'
    'org/fmod/FMOD$PluginBroadcastReceiver.class'
    org/fmod/FMOD.class
    org/fmod/MediaCodec.class
)
fmod_jar_entries="$(jar tf "$FMOD_DIR/fmod.jar")"
for class_file in "${required_fmod_classes[@]}"; do
    grep -Fxq "$class_file" <<<"$fmod_jar_entries" \
        || fail "Incomplete FMOD Java glue: $class_file is missing from $FMOD_DIR/fmod.jar"
done

[ ! -e "$WORK_DIR" ] || fail "Work directory already exists: $WORK_DIR"
mkdir -p "$WORK_DIR" "$OUTPUT_DIR" "$CACHE_DIR/gradle" "$CACHE_DIR/nuget"
cp -a "$SOURCE_DIR/." "$WORK_DIR/"

cd "$WORK_DIR"
export DEPS_DIR
export GRADLE_USER_HOME="$CACHE_DIR/gradle"
export NUGET_PACKAGES="$CACHE_DIR/nuget"
bash scripts/setup-deps.sh

# A debug-version suffix exposes deterministic QA intents while keeping the
# exact release build type, optimizer, package id, signing identity, and native
# payload used by production. The default is empty and therefore byte-for-byte
# follows the ordinary release version path.
VERSION_NAME_SUFFIX="${VERSION_NAME_SUFFIX:-}"
if [ -n "$VERSION_NAME_SUFFIX" ]; then
    [[ "$VERSION_NAME_SUFFIX" =~ ^-[A-Za-z0-9][A-Za-z0-9.-]*$ ]] \
        || fail "Invalid VERSION_NAME_SUFFIX: $VERSION_NAME_SUFFIX"
    base_version="$(sed -nE 's/^export_version_name=(.+)/\1/p' android/gradle.properties)"
    [ -n "$base_version" ] || fail "export_version_name is missing"
    sed -i \
        "s/^export_version_name=.*/export_version_name=${base_version}${VERSION_NAME_SUFFIX}/" \
        android/gradle.properties
fi

# setup-deps.sh seeds native libraries from Ekyso's v0.2.0 APK. Releases since
# v0.4.1 replace the FMOD 2.02 files with a matched FMOD 2.03.13 set.
for variant in release debug; do
    variant_dir="android/libs/$variant"
    mkdir -p "$variant_dir/arm64-v8a"
    cp -f "$FMOD_DIR/fmod.jar" "$variant_dir/fmod.jar"
    cp -f "$FMOD_DIR/libfmod.so" "$variant_dir/arm64-v8a/libfmod.so"
    cp -f "$FMOD_DIR/libfmodstudio.so" "$variant_dir/arm64-v8a/libfmodstudio.so"
    cp -f \
        "$FMOD_DIR/libGodotFmod.android.template_release.arm64.so" \
        "$variant_dir/arm64-v8a/libGodotFmod.android.template_release.arm64.so"
    cp -f \
        "$FMOD_DIR/libGodotFmod.android.template_release.arm64.so" \
        "$variant_dir/arm64-v8a/libGodotFmod.android.template_debug.arm64.so"
done

actual_fmod_md5="$(md5sum android/libs/release/arm64-v8a/libfmod.so | cut -d' ' -f1)"
[ "$actual_fmod_md5" = "678ca6c0f92d956c3b62e08b34634a0a" ] \
    || fail "Unexpected FMOD runtime: $actual_fmod_md5"

is_signed=false
if [ -n "${ANDROID_KEYSTORE_PATH:-}" ]; then
    require_file "$ANDROID_KEYSTORE_PATH"
    [ -n "${ANDROID_KEYSTORE_PASSWORD:-}" ] || fail "ANDROID_KEYSTORE_PASSWORD is required"
    [ -n "${ANDROID_KEY_ALIAS:-}" ] || fail "ANDROID_KEY_ALIAS is required"
    cp -f "$ANDROID_KEYSTORE_PATH" android/sts2.keystore
    export ORG_GRADLE_PROJECT_release_keystore_password="$ANDROID_KEYSTORE_PASSWORD"
    export ORG_GRADLE_PROJECT_release_keystore_alias="$ANDROID_KEY_ALIAS"
    is_signed=true
else
    sed -i 's/^perform_signing=.*/perform_signing=false/' android/gradle.properties
fi

if [ ! -f android/gradle/wrapper/gradle-wrapper.jar ]; then
    (cd android && gradle wrapper --gradle-version 8.13 --distribution-type bin)
fi

echo "Running focused stability and compatibility regressions..."
dotnet run --project tools/stability-tests/stability-tests.csproj
bash tools/localization-audit/tests/run.sh
bash tools/stability-tests-java/run.sh
bash tools/device-stability/tests/run.sh
bash tools/device-performance/tests/run.sh
bash tools/memberref-audit/tests/run.sh
bash tools/patch-target-audit/tests/run.sh

bash scripts/build.sh --no-bump

# This existing regression project references the freshly built STS2Mobile.dll.
dotnet run --project tools/workshop-sync-tests/workshop-sync-tests.csproj

apk_path="$(find android/build/outputs/apk/mono/release -maxdepth 1 -type f -name '*.apk' -print -quit)"
[ -n "$apk_path" ] || fail "Gradle completed without producing an APK"

# Verify the helpers survived dependency merging/shrinking and reached DEX.
unzip -p "$apk_path" classes.dex > "$WORK_DIR/classes.dex"
"$ANDROID_HOME/build-tools/35.0.0/dexdump" "$WORK_DIR/classes.dex" > "$WORK_DIR/classes.dump"
for class_file in "${required_fmod_classes[@]}"; do
    class_descriptor="L${class_file%.class};"
    grep -Fq "Class descriptor  : '$class_descriptor'" "$WORK_DIR/classes.dump" \
        || fail "Packaged APK is missing FMOD Java glue: $class_descriptor"
done

cp -f "$apk_path" "$OUTPUT_DIR/"
output_name="$(basename "$apk_path")"
if [ "$is_signed" = true ]; then
    "$ANDROID_HOME/build-tools/35.0.0/apksigner" verify --verbose "$OUTPUT_DIR/$output_name"
else
    echo "WARNING: APK is unsigned; provide ANDROID_KEYSTORE_* to create an installable artifact."
fi

(cd "$OUTPUT_DIR" && sha256sum "$output_name" | tee "$output_name.sha256")

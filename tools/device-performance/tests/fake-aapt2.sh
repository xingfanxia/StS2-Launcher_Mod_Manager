#!/usr/bin/env bash
set -euo pipefail

[ "${1:-}" = "dump" ] && [ "${2:-}" = "badging" ] || exit 2
echo "package: name='${FAKE_APK_PACKAGE:-com.game.sts2launcher.modmanager}' versionCode='339' versionName='0.4.2'"

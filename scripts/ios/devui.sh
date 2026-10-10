#!/bin/bash
# Drive the app on a connected device through its STS2_DEBUG_AUTOMATION hook.
#   devui.sh shot <name>              -> scratch/media/ios-port/<name>.jpg (half size)
#   devui.sh tap <x> <y>              coordinates are pixels of a shot
#   devui.sh drag <x1> <y1> <x2> <y2>
#   devui.sh console <command>        the game's developer console, e.g. help
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
DEVICE=${STS2_IOS_DEVICE:-$(python3 -c "import json;print(json.load(open('$ROOT/ios/config.local.json'))['device'])")}
BUNDLE=$(python3 -c "import json;print(json.load(open('$ROOT/ios/config.local.json'))['bundle_id'])")
WORK=$(mktemp -d); trap 'rm -rf "$WORK"' EXIT
copy() { xcrun devicectl device copy "$1" --device "$DEVICE" --domain-type appDataContainer --domain-identifier "$BUNDLE" --source "$2" --destination "$3" >/dev/null 2>&1; }
echo "$*" > "$WORK/command.txt"
echo pending > "$WORK/result.txt"
copy to "$WORK/result.txt" Documents/automation/result.txt
copy to "$WORK/command.txt" Documents/automation/command.txt
for _ in $(seq 1 20); do
    sleep 0.5
    copy from Documents/automation/result.txt "$WORK/answer.txt" || continue
    grep -q pending "$WORK/answer.txt" || break
done
cat "$WORK/answer.txt"
if [ "$1" = shot ]; then
    mkdir -p "$ROOT/scratch/media/ios-port"
    rm -f "$ROOT/scratch/media/ios-port/$2.jpg"
    copy from "Documents/automation/$2.jpg" "$ROOT/scratch/media/ios-port/$2.jpg"
    echo "$ROOT/scratch/media/ios-port/$2.jpg"
fi

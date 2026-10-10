#!/usr/bin/env python3
"""Copy iOS saves to a new dated backup on the Mac without modifying the device."""

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]


def backup(device, bundle_id, destination, runner=subprocess.run):
    destination = Path(destination).expanduser().resolve()
    if destination.exists():
        raise ValueError("Backup destination already exists; choose a new directory")
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = Path(tempfile.mkdtemp(prefix=".backup-", dir=destination.parent))
    try:
        runner([
            "xcrun", "devicectl", "device", "copy", "from", "--device", device,
            "--domain-type", "appDataContainer", "--domain-identifier", bundle_id,
            "--source", "Documents/default", "--destination", str(temporary / "default"),
        ], check=True, stdout=subprocess.DEVNULL)
        files = {}
        for path in sorted((temporary / "default").rglob("*")):
            if path.is_symlink():
                raise ValueError("Backup contains an unexpected symbolic link")
            if path.is_file():
                data = path.read_bytes()
                files[path.relative_to(temporary).as_posix()] = {
                    "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest(),
                }
        if not files:
            raise ValueError("No save files were copied; start the game and create a profile first")
        (temporary / "manifest.json").write_text(json.dumps({
            "schema": 1, "created_utc": datetime.now(timezone.utc).isoformat(),
            "source": "Documents/default", "files": files,
        }, indent=2) + "\n")
        if destination.exists():
            raise ValueError("Backup destination appeared during copying; choose a new directory")
        temporary.rename(destination)
        return len(files)
    finally:
        if temporary.exists():
            shutil.rmtree(temporary)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=Path, default=ROOT / "ios/config.local.json")
    parser.add_argument("--destination", type=Path, help="New directory; existing backups are never replaced")
    args = parser.parse_args()
    try:
        config = json.loads(args.config.read_text())
        for key in ("device", "bundle_id"):
            if not isinstance(config.get(key), str) or not config[key] or config[key].startswith("YOUR_"):
                raise ValueError(f"Set {key} in the local config first")
        destination = args.destination or ROOT / ".cache/save-backups" / datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%S-%f")
        print("Return to the main menu before backing up so saves are not being written.", flush=True)
        count = backup(config["device"], config["bundle_id"], destination)
        print(f"Backed up {count} files to {destination}")
        print("Keep a copy outside this checkout. Saves and manifests are private; do not commit them.")
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        parser.exit(1, f"Backup failed: {error}\n")


if __name__ == "__main__":
    main()

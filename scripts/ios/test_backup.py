import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

from backup import backup


class BackupTests(unittest.TestCase):
    def test_copy_is_read_only_and_manifest_matches(self):
        def copy(args, **kwargs):
            self.assertEqual(args[:5], ["xcrun", "devicectl", "device", "copy", "from"])
            self.assertEqual(args[args.index("--source") + 1], "Documents/default")
            folder = Path(args[args.index("--destination") + 1]) / "1/profile1/saves"
            folder.mkdir(parents=True)
            (folder / "progress.save").write_bytes(b"test save")

        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "snapshot"
            self.assertEqual(backup("device", "test.bundle", output, copy), 1)
            manifest = json.loads((output / "manifest.json").read_text())
            entry = manifest["files"]["default/1/profile1/saves/progress.save"]
            self.assertEqual(entry["sha256"], hashlib.sha256(b"test save").hexdigest())
            self.assertNotIn("device", manifest)
            with self.assertRaises(ValueError):
                backup("device", "test.bundle", output, copy)

    def test_failed_or_empty_copy_never_publishes_backup(self):
        for fail in (True, False):
            def copy(args, **kwargs):
                if fail:
                    folder = Path(args[-1])
                    folder.mkdir()
                    (folder / "partial.save").write_bytes(b"partial")
                    raise subprocess.CalledProcessError(1, "devicectl")

            with tempfile.TemporaryDirectory() as directory:
                output = Path(directory) / "snapshot"
                with self.assertRaises((subprocess.CalledProcessError, ValueError)):
                    backup("device", "test.bundle", output, copy)
                self.assertEqual(list(Path(directory).iterdir()), [])


if __name__ == "__main__":
    unittest.main()

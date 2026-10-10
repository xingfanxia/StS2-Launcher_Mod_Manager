"""Synthetic pack checks; no game files needed."""

import hashlib
from pathlib import Path
import struct
import tempfile
import unittest

from pck_add_cs import patch as add_scripts
from pck_ls import read_dir
from pck_patch_sentry import patch as remove_sentry


def make_pack(path, autoload):
    files = {
        ".godot/extension_list.cfg": b"res://addons/fmod/fmod.gdextension\nres://addons/spine/spine.gdextension\nres://addons/sentry/sentry.gdextension\n",
        "project.binary": b"ECFG" + (b"autoload/SentryInit" if autoload else b"application/config/name"),
        "addons/sentry/sentry.gdextension": b"sentry",
        "untouched.bin": bytes(range(256)),
    }
    data = bytearray(112)
    entries = bytearray(struct.pack("<I", len(files)))
    for name, content in files.items():
        encoded = name.encode()
        encoded += b"\0" * (-len(encoded) % 4)
        entries += struct.pack("<I", len(encoded)) + encoded
        entries += struct.pack("<QQ", len(data) - 112, len(content))
        entries += hashlib.md5(content).digest() + struct.pack("<I", 0)
        data += content
    struct.pack_into("<4sIIIIIQQ", data, 0, b"GDPC", 3, 4, 5, 1, 2, 112, len(data))
    path.write_bytes(data + entries)
    return files


def unpack(path):
    result = {}
    with path.open("rb") as stream:
        for name, offset, size, _ in read_dir(stream):
            stream.seek(offset)
            result[name] = stream.read(size)
    return result


class PackTests(unittest.TestCase):
    def test_pipeline_preserves_source_and_unrelated_resources(self):
        for autoload in (False, True):
            with self.subTest(autoload=autoload), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                src, clean, dst = (root / name for name in ("source.pck", "clean.pck", "result.pck"))
                files = make_pack(src, autoload)
                original = src.read_bytes()
                remove_sentry(src, clean)
                paths = root / "scripts.txt"
                paths.write_text("res://nodes/Game.cs\nres://nodes/Game.cs\n")
                add_scripts(clean, paths, dst)
                result = unpack(dst)
                self.assertEqual(src.read_bytes(), original)
                self.assertEqual(result["untouched.bin"], files["untouched.bin"])
                self.assertNotIn("addons/sentry/sentry.gdextension", result)
                self.assertNotIn(b"sentry", result[".godot/extension_list.cfg"])
                self.assertNotIn(b"autoload/SentryInit", result["project.binary"])
                self.assertEqual(result["res://nodes/Game.cs"], b"\n")
                add_scripts(dst, paths, root / "again.pck")
                self.assertEqual(unpack(root / "again.pck"), result)

    def test_failure_does_not_replace_existing_output(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            src, dst, paths = root / "source.pck", root / "output.pck", root / "scripts.txt"
            src.write_bytes(b"invalid pack")
            dst.write_bytes(b"previous valid output")
            paths.write_text("res://nodes/Game.cs\n")
            for operation in (lambda: remove_sentry(src, dst), lambda: add_scripts(src, paths, dst)):
                with self.assertRaises(AssertionError):
                    operation()
                self.assertEqual(dst.read_bytes(), b"previous valid output")
                self.assertFalse(list(root.glob("output.pck.*")))

    def test_source_aliases_are_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            src = root / "source.pck"
            make_pack(src, False)
            alias = root / "alias.pck"
            alias.hardlink_to(src)
            for dst in (src, alias):
                with self.assertRaises(ValueError):
                    remove_sentry(src, dst)


if __name__ == "__main__":
    unittest.main()

import hashlib
import json
from pathlib import Path
import plistlib
import tempfile
import unittest
from unittest.mock import patch

import build
import jit
import prepare_xcode


class JitBuildTests(unittest.TestCase):
    def test_config_and_install_use_exact_bundle_id(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            cfg = {"team_id": "TESTTEAM", "bundle_id": "dev.example.game"}
            config_path = root / "config.local.json"
            config_path.write_text(json.dumps(cfg))
            with patch.object(build, "IOS", root):
                self.assertEqual(build.config(), cfg)
                with self.assertRaises(SystemExit):
                    build.config(require_device=True)
                cfg["device"] = "test-device"
                config_path.write_text(json.dumps(cfg))
                self.assertEqual(build.config(require_device=True), cfg)

            (root / "Info.plist").write_bytes(plistlib.dumps({"CFBundleIdentifier": cfg["bundle_id"]}))
            with patch.object(jit, "run") as run:
                jit.install_app(root, cfg)
                run.assert_called_once()
                run.reset_mock()
                jit.copy_to_device(root, "Documents/mods", cfg)
                self.assertEqual(run.call_args.args[-1], cfg["bundle_id"])
                run.reset_mock()
                with self.assertRaises(SystemExit):
                    jit.install_app(root, dict(cfg, bundle_id="dev.example.other"))
                run.assert_not_called()

    def test_input_validation_rejects_mixed_game_and_content(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            cache = root / ".cache"
            cache.mkdir()
            steam = root / "game.app"
            resources = steam / "Contents/Resources"
            assembly = resources / "data_sts2_macos_arm64/sts2.dll"
            assembly.parent.mkdir(parents=True)
            assembly.write_bytes(b"game-one")
            reference = root / "upstream/game-refs/sts2.dll"
            reference.parent.mkdir(parents=True)
            reference.write_bytes(assembly.read_bytes())
            (resources / "release_info.json").write_text("{}")
            (resources / "Slay the Spire 2.pck").write_bytes(b"source-pack")
            (cache / "StS2.pck").write_bytes(b"prepared-pack")
            metadata = {"assembly_sha256": hashlib.sha256(assembly.read_bytes()).hexdigest(),
                        "source_pack_bytes": len(b"source-pack")}
            (cache / "content-input.json").write_text(json.dumps(metadata))
            with patch.object(jit, "ROOT", root), patch.object(jit, "CACHE", cache), \
                    patch.object(jit, "native_libraries"):
                jit.validate_inputs(steam)
                reference.write_bytes(b"game-two")
                with self.assertRaises(SystemExit):
                    jit.validate_inputs(steam)
                reference.write_bytes(assembly.read_bytes())
                metadata["assembly_sha256"] = "outdated-content"
                (cache / "content-input.json").write_text(json.dumps(metadata))
                with self.assertRaises(SystemExit):
                    jit.validate_inputs(steam)

    def test_host_wires_jit_gate_and_preserves_profile_setting(self):
        with tempfile.TemporaryDirectory() as temporary:
            host = Path(temporary)
            project = host / "StS2.xcodeproj/project.pbxproj"
            project.parent.mkdir()
            project.write_text('''/* End PBXFileReference section */
/* End PBXBuildFile section */
/* Begin PBXCopyFilesBuildPhase section */
files = ();
lastKnownFileType = sourcecode.cpp.cpp; path = dummy.cpp;
OTHER_LDFLAGS = "-lz";
''')
            framework = host / "StS2/dylibs/sts2.framework"
            framework.mkdir(parents=True)
            (framework / "sts2").write_bytes(b"native-framework")
            info_path = host / "StS2/StS2-Info.plist"
            info_path.write_bytes(plistlib.dumps({"CFBundleIdentifier": "dev.example.game"}))
            dummy = host / "StS2/dummy.cpp"
            dummy.touch()
            cfg = {"bundle_id": "dev.example.game", "profile": True}
            prepare_xcode.prepare(host, cfg, 12345)
            info = plistlib.loads(info_path.read_bytes())
            self.assertEqual(info["CFBundleIdentifier"], cfg["bundle_id"])
            self.assertEqual(info["CFBundleDisplayName"], "StS2 JIT")
            self.assertEqual(info["STS2ContentSize"], 12345)
            self.assertEqual(info["godot_cmdline"], ["--main-pack", "user://StS2.pck", "--", "--sts2-profile"])
            self.assertEqual(info["NSBonjourServices"], ["_sts2lan._udp"])
            prepare_xcode.prepare(host, dict(cfg, profile=False), 23456)
            info = plistlib.loads(info_path.read_bytes())
            self.assertNotIn("--sts2-profile", info["godot_cmdline"])
            self.assertEqual(info["STS2ContentSize"], 23456)
            self.assertEqual(dummy.read_text().count('#include "JitGate.mm"'), 1)
            self.assertEqual(dummy.read_text().count('#include "Sts2Native.mm"'), 1)
            self.assertIn("CodeSignOnCopy", project.read_text())
            self.assertIn("-framework CoreImage", project.read_text())


if __name__ == "__main__":
    unittest.main()

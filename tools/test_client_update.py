import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import zipfile


class ClientUpdateTests(unittest.TestCase):
    def test_versioned_update_name_is_preserved_and_engine_is_not_bundled(self):
        with tempfile.TemporaryDirectory(prefix="VoiceLite-update-") as directory:
            root = Path(directory)
            published = root / "published"
            published.mkdir()
            (published / "VoiceLite.dll").write_bytes(b"new client")
            base = root / "base.json"
            engine_entry = {"path": "engine/model.bin", "bytes": 99, "sha256": "retained-model-hash"}
            base.write_text(json.dumps({"profile": {"id": "verified-fixture"}, "files": [engine_entry]}))
            guide = root / "guide.txt"
            guide.write_text("update guide")
            destination = root / "VoiceLite-0.1.1-ogg-update"
            result = subprocess.run([sys.executable, "-B", str(Path(__file__).with_name("package-client-update.py")), str(published), str(base), str(destination), str(guide)], capture_output=True, text=True)
            self.assertEqual(0, result.returncode, result.stderr)
            archive = root / "VoiceLite-0.1.1-ogg-update.zip"
            self.assertTrue(archive.exists(), "Version dots must not be treated as a file suffix")
            with zipfile.ZipFile(archive) as zipped:
                self.assertEqual(b"new client", zipped.read("VoiceLite.dll"))
                self.assertFalse(any(name.startswith("engine/") for name in zipped.namelist()))
                manifest = json.loads(zipped.read("package-files.json"))
                self.assertIn(engine_entry, manifest["files"])


if __name__ == "__main__":
    unittest.main()

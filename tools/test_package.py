import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

SCRIPT = Path(__file__).with_name("package-resources.py")


class PackageTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="VoiceLite-package-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.source = self.root / "source"
        for name in ["api_v2.py", "runtime/python.exe", "tools/my_utils.py", "tools/asr/models/model.pt", "GPT_SoVITS/pretrained_models/chosen.ckpt", "GPT_SoVITS/pretrained_models/unused.pth", "runtime/test.wav"]:
            path = self.source / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b"hello")
        self.profile = self.root / "profile.json"
        self.profile.write_text(json.dumps({"api_sha256": "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824",
            "public_redistribution_cleared": False, "g2pw": False,
            "required_files": ["runtime/python.exe"], "model_paths": ["GPT_SoVITS/pretrained_models/chosen.ckpt"]}))
        self.destination = self.root / "engine"

    def run_package(self, local=True):
        return subprocess.run([sys.executable, "-B", str(SCRIPT), "--source", str(self.source), "--destination", str(self.destination), "--profile", str(self.profile)] + (["--local-validation"] if local else []), capture_output=True, text=True)

    def test_candidate_excludes_unrelated_models_asr_and_audio_and_hashes_copies(self):
        result = self.run_package()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertFalse((self.destination / "tools/asr").exists())
        self.assertFalse((self.destination / "runtime/test.wav").exists())
        self.assertFalse((self.destination / "GPT_SoVITS/pretrained_models/unused.pth").exists())
        entries = json.loads((self.root / "engine-files.json").read_text())["files"]
        copied = next(item for item in entries if item["path"] == "engine/runtime/python.exe")
        self.assertEqual("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824", copied["sha256"])
        self.assertEqual(b"hello", (self.source / "api_v2.py").read_bytes())

    def test_public_release_is_blocked_until_review_is_cleared(self):
        self.assertNotEqual(0, self.run_package(local=False).returncode)
        self.assertFalse(self.destination.exists())

    def test_changed_api_is_rejected_before_copy(self):
        (self.source / "api_v2.py").write_bytes(b"changed")
        self.assertNotEqual(0, self.run_package().returncode)
        self.assertFalse(self.destination.exists())

    def test_missing_required_runtime_is_rejected_before_copy(self):
        (self.source / "runtime/python.exe").unlink()
        self.assertNotEqual(0, self.run_package().returncode)
        self.assertFalse(self.destination.exists())

    def test_profile_cannot_silently_exclude_a_required_text_model(self):
        model = "GPT_SoVITS/text/G2PWModel/g2pW.onnx"
        path = self.source / model
        path.parent.mkdir(parents=True)
        path.write_bytes(b"local model")
        profile = json.loads(self.profile.read_text())
        profile["required_files"].append(model)
        self.profile.write_text(json.dumps(profile))
        self.assertNotEqual(0, self.run_package().returncode)
        self.assertFalse(self.destination.exists())


if __name__ == "__main__":
    unittest.main()

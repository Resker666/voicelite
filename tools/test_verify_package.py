import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

SCRIPT = Path(__file__).with_name("verify-package.py")
SPEC = importlib.util.spec_from_file_location("verify_package", SCRIPT)
VERIFY = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VERIFY)


class VerifyPackageTests(unittest.TestCase):
    def run_verify(self, package):
        return subprocess.run([sys.executable, "-B", str(SCRIPT), str(package)], capture_output=True, text=True)

    def write_manifest(self, package, relative, data):
        (package / "package-files.json").write_text(json.dumps({"files": [{
            "path": relative, "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest(),
        }]}), encoding="utf-8")

    def test_windows_drive_and_unc_prefixes_are_preserved(self):
        cases = [
            ("C:\\package", "\\\\?\\C:\\package"),
            ("\\\\server\\share\\package", "\\\\?\\UNC\\server\\share\\package"),
            ("\\\\?\\C:\\package", "\\\\?\\C:\\package"),
            ("\\\\?\\UNC\\server\\share\\package", "\\\\?\\UNC\\server\\share\\package"),
        ]
        for normal, expected in cases:
            with self.subTest(path=normal):
                self.assertEqual(expected, VERIFY.windows_extended_path(normal))

    @unittest.skipUnless(os.name == "nt", "Windows extended-length paths")
    def test_manifest_file_beyond_max_path_is_verified_without_changing_manifest(self):
        with tempfile.TemporaryDirectory(prefix="VoiceLite-verify-") as directory:
            root = Path(directory).resolve()
            extended_root = Path("\\\\?\\" + str(root))
            relative = "engine/" + "/".join(["nested-" + "x" * 75] * 3) + "/model.bin"
            data = b"verified local package resource"
            resource = extended_root / relative
            try:
                resource.parent.mkdir(parents=True)
                resource.write_bytes(data)
                self.assertGreater(len(str(root / relative)), 260)
                self.write_manifest(root, relative, data)
                original_manifest = (root / "package-files.json").read_bytes()
                result = self.run_verify(root)
                self.assertEqual(0, result.returncode, result.stderr)
                self.assertEqual(1, json.loads(result.stdout)["verified_files"])
                self.assertEqual(original_manifest, (root / "package-files.json").read_bytes())
            finally:
                shutil.rmtree(extended_root)

    def test_corrupted_contents_fail_hash_verification(self):
        with tempfile.TemporaryDirectory(prefix="VoiceLite-verify-") as directory:
            root = Path(directory)
            self.write_manifest(root, "resource.bin", b"expected")
            (root / "resource.bin").write_bytes(b"modified")
            result = self.run_verify(root)
            self.assertNotEqual(0, result.returncode)
            self.assertIn("Package hash mismatch", result.stderr)

    def test_manifest_cannot_reference_an_external_file(self):
        with tempfile.TemporaryDirectory(prefix="VoiceLite-verify-") as directory:
            root = Path(directory) / "package"
            root.mkdir()
            data = b"outside this package"
            (root.parent / "outside.bin").write_bytes(data)
            self.write_manifest(root, "../outside.bin", data)
            result = self.run_verify(root)
            self.assertNotEqual(0, result.returncode)
            self.assertIn("Invalid package entry", result.stderr)


if __name__ == "__main__":
    unittest.main()

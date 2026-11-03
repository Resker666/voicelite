import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET
import zipfile

TOOLS = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("client_update", TOOLS / "apply-client-update.py")
updater = importlib.util.module_from_spec(spec)
spec.loader.exec_module(updater)


def entry(path, relative):
    return {"path": relative, "bytes": path.stat().st_size, "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}


class ClientUpdateTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="VoiceLite-update-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name).resolve()
        self.package = self.root / "package"
        self.package.mkdir()
        for name, content in {
            "VoiceLite.dll": b"old client", "VoiceLite.exe": b"unchanged executable",
            "START_HERE.txt": b"old guide", "engine/api_v2.py": b"audited api", "engine/model.bin": b"existing model",
        }.items():
            target = self.package / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(content)
        self.profile = {"id": "verified-fixture", "api_sha256": hashlib.sha256(b"audited api").hexdigest()}
        self.engine_entries = [entry(self.package / name, name) for name in ("engine/api_v2.py", "engine/model.bin")]
        (self.package / "engine-files.json").write_text(json.dumps({"profile": self.profile, "files": self.engine_entries}))
        self.original_manifest = {"profile": self.profile, "files": [
            entry(path, path.relative_to(self.package).as_posix()) for path in sorted(self.package.rglob("*")) if path.is_file()
        ]}
        self.base = self.root / "base.json"
        self.base.write_text(json.dumps(self.original_manifest))
        (self.package / "package-files.json").write_bytes(self.base.read_bytes())
        self.published = self.root / "published"
        self.published.mkdir()
        for name, content in {"VoiceLite.dll": b"new client", "VoiceLite.Core.dll": b"new core", "VoiceLite.exe": b"unchanged executable", "engine_runner.py": b"new runner"}.items():
            (self.published / name).write_bytes(content)
        self.guide = self.root / "guide.txt"
        self.guide.write_text("updated guide")
        self.update = self.root / "VoiceLite-0.1.2-client-update"

    def build_update(self, explicit_version=True):
        command = [sys.executable, "-B", str(TOOLS / "package-client-update.py"), str(self.published), str(self.base), str(self.update), str(self.guide)]
        if explicit_version:
            command.extend(["--version", "0.1.2"])
        return subprocess.run(command, capture_output=True, text=True)

    def build_valid_update(self):
        result = self.build_update()
        self.assertEqual(0, result.returncode, result.stderr)

    def run_update(self, *arguments):
        return subprocess.run([sys.executable, "-B", str(TOOLS / "apply-client-update.py"), *map(str, arguments)], capture_output=True, text=True)

    def test_cumulative_update_preserves_versioned_name_and_engine_hashes(self):
        self.build_valid_update()
        archive = Path(str(self.update) + ".zip")
        self.assertTrue(archive.exists(), "Version dots must not be treated as a file suffix")
        with zipfile.ZipFile(archive) as zipped:
            self.assertEqual(b"new client", zipped.read("VoiceLite.dll"))
            self.assertIn("engine_runner.py", zipped.namelist())
            self.assertFalse(any(name.startswith("engine/") for name in zipped.namelist()))
            manifest = json.loads(zipped.read("package-files.json"))
            self.assertEqual("0.1.2", manifest["client_version"])
            self.assertTrue(all(item in manifest["files"] for item in self.engine_entries))
            update = json.loads(zipped.read("client-update.json"))
            self.assertEqual("0.1.2", update["version"])
            self.assertIn("package-files.json", {item["path"] for item in update["files"]})
            self.assertIn("apply-client-update.py", {item["path"] for item in update["files"]})
            self.assertNotIn("VoiceLite.exe", {item["path"] for item in update["files"]})

    def test_version_defaults_to_project_version(self):
        result = self.build_update(explicit_version=False)
        self.assertEqual(0, result.returncode, result.stderr)
        expected = ET.parse(TOOLS.parent / "Directory.Build.props").findtext(".//Version")
        self.assertEqual(expected, json.loads((self.update / "client-update.json").read_text())["version"])

    def test_published_engine_is_rejected_before_output_is_created(self):
        (self.published / "engine").mkdir()
        (self.published / "engine/model.bin").write_bytes(b"must not ship")
        self.assertNotEqual(0, self.build_update().returncode)
        self.assertFalse(self.update.exists())

    def test_check_apply_and_rollback_preserve_engine_and_existing_files(self):
        self.build_valid_update()
        original = {path.relative_to(self.package).as_posix(): path.read_bytes() for path in self.package.rglob("*") if path.is_file()}
        backup = self.root / "backup"
        result = self.run_update(self.update, self.package, "--check")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(original, {path.relative_to(self.package).as_posix(): path.read_bytes() for path in self.package.rglob("*") if path.is_file()})
        result = self.run_update(self.update, self.package, "--backup", backup)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(b"new client", (self.package / "VoiceLite.dll").read_bytes())
        self.assertEqual(b"new core", (self.package / "VoiceLite.Core.dll").read_bytes())
        self.assertEqual(b"existing model", (self.package / "engine/model.bin").read_bytes())
        self.assertFalse((self.package / "client-update.json").exists())
        self.assertNotEqual(0, self.run_update(self.update, self.package, "--backup", backup).returncode)
        verified = subprocess.run([sys.executable, "-B", str(TOOLS / "verify-package.py"), str(self.package)], capture_output=True, text=True)
        self.assertEqual(0, verified.returncode, verified.stderr)
        result = self.run_update("--rollback", backup, self.package)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(original, {path.relative_to(self.package).as_posix(): path.read_bytes() for path in self.package.rglob("*") if path.is_file()})

    def test_corrupt_payload_is_rejected_without_creating_backup(self):
        self.build_valid_update()
        (self.update / "VoiceLite.dll").write_bytes(b"corrupt")
        backup = self.root / "backup"
        self.assertNotEqual(0, self.run_update(self.update, self.package, "--backup", backup).returncode)
        self.assertFalse(backup.exists())
        self.assertEqual(b"old client", (self.package / "VoiceLite.dll").read_bytes())

    def test_wrong_engine_profile_or_api_is_rejected(self):
        self.build_valid_update()
        manifest = json.loads((self.package / "engine-files.json").read_text())
        manifest["profile"]["id"] = "different-model-profile"
        (self.package / "engine-files.json").write_text(json.dumps(manifest))
        self.assertNotEqual(0, self.run_update(self.update, self.package, "--check").returncode)
        manifest["profile"]["id"] = self.profile["id"]
        (self.package / "engine-files.json").write_text(json.dumps(manifest))
        (self.package / "engine/api_v2.py").write_bytes(b"modified api")
        self.assertNotEqual(0, self.run_update(self.update, self.package, "--check").returncode)

    def test_traversal_and_case_duplicate_entries_are_rejected(self):
        self.build_valid_update()
        metadata_path = self.update / "client-update.json"
        metadata = json.loads(metadata_path.read_text())
        original = metadata_path.read_bytes()
        metadata["files"][0]["path"] = "../outside.dll"
        metadata_path.write_text(json.dumps(metadata))
        self.assertNotEqual(0, self.run_update(self.update, self.package, "--check").returncode)
        metadata = json.loads(original)
        duplicate = dict(metadata["files"][0])
        duplicate["path"] = duplicate["path"].upper()
        metadata["files"].append(duplicate)
        metadata_path.write_text(json.dumps(metadata))
        self.assertNotEqual(0, self.run_update(self.update, self.package, "--check").returncode)
        self.assertEqual(b"old client", (self.package / "VoiceLite.dll").read_bytes())

    def test_failed_application_restores_already_replaced_files(self):
        self.build_valid_update()
        original = {path.relative_to(self.package).as_posix(): path.read_bytes() for path in self.package.rglob("*") if path.is_file()}
        replace = updater.replace_file
        count = 0

        def failing_replace(*arguments):
            nonlocal count
            count += 1
            if count == 3:
                raise OSError("simulated replacement failure")
            return replace(*arguments)

        with patch.object(updater, "replace_file", side_effect=failing_replace):
            with self.assertRaises(OSError):
                updater.apply_update(self.update, self.package, self.root / "backup")
        self.assertEqual(original, {path.relative_to(self.package).as_posix(): path.read_bytes() for path in self.package.rglob("*") if path.is_file()})

    def test_rollback_refuses_to_overwrite_files_changed_after_the_update(self):
        self.build_valid_update()
        backup = self.root / "backup"
        updater.apply_update(self.update, self.package, backup)
        (self.package / "VoiceLite.dll").write_bytes(b"a later manual change")
        result = self.run_update("--rollback", backup, self.package)
        self.assertNotEqual(0, result.returncode)
        self.assertEqual(b"a later manual change", (self.package / "VoiceLite.dll").read_bytes())


if __name__ == "__main__":
    unittest.main()

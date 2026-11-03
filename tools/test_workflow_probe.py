"""Opt-in executable boundary tests for the real-engine workflow probe; no models needed."""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest


DOTNET = os.environ.get("VOICELITE_WORKFLOW_PROBE_DOTNET")
PROBE = os.environ.get("VOICELITE_WORKFLOW_PROBE_DLL")


@unittest.skipUnless(os.name == "nt" and DOTNET and PROBE, "Set VOICELITE_WORKFLOW_PROBE_DOTNET and VOICELITE_WORKFLOW_PROBE_DLL to run Windows probe boundary tests.")
class WorkflowProbeInputTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="VoiceLiteProbeBoundary-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.engine = self.root / "engine"
        self.engine.mkdir()
        self.reference = self.root / "reference.wav"
        self.reference.write_bytes(b"input validation must run before audio processing")
        self.reference.with_suffix(".json").write_text(json.dumps({"text": "test", "language": "zh"}), encoding="utf-8")

    def assert_rejected_without_writes(self, engine, data):
        result = subprocess.run([DOTNET, PROBE, str(engine), str(self.reference), str(data)],
                                capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=30)
        self.assertNotEqual(0, result.returncode)
        self.assertFalse(data.exists(), "Probe wrote test data into the protected engine directory.")
        self.assertIn("Test data must be outside the engine", result.stdout + result.stderr)

    def test_plain_nested_data_is_rejected_before_creation(self):
        self.assert_rejected_without_writes(self.engine, self.engine / "new-data")

    def test_trailing_engine_separator_cannot_bypass_isolation(self):
        self.assert_rejected_without_writes(str(self.engine) + os.sep, self.engine / "new-data")

    def test_extended_engine_alias_cannot_bypass_isolation(self):
        self.assert_rejected_without_writes("\\\\?\\" + str(self.engine), self.engine / "new-data")

    def test_extended_data_alias_cannot_bypass_isolation(self):
        self.assert_rejected_without_writes(self.engine, Path("\\\\?\\" + str(self.engine / "new-data")))

    def test_regular_external_data_reaches_engine_validation(self):
        data = self.root / "probe-data"
        result = subprocess.run([DOTNET, PROBE, str(self.engine), str(self.reference), str(data)],
                                capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=30)
        self.assertNotEqual(0, result.returncode)  # The fake engine intentionally has no Python/models.
        report = json.loads((data / "workflow-report.json").read_text(encoding="utf-8"))
        self.assertFalse(report["passed"])
        self.assertIn("Generation failed before /tts", report["error"])

    def test_parent_junction_cannot_redirect_test_data_into_engine(self):
        alias = self.root / "engine-alias"
        environment = dict(os.environ, VOICELITE_JUNCTION_PATH=str(alias), VOICELITE_JUNCTION_TARGET=str(self.engine))
        subprocess.run(["powershell.exe", "-NoProfile", "-NonInteractive", "-Command",
                        "New-Item -ItemType Junction -Path $env:VOICELITE_JUNCTION_PATH -Target $env:VOICELITE_JUNCTION_TARGET | Out-Null"],
                       env=environment, capture_output=True, check=True, timeout=15)
        # Remove only the junction itself before TemporaryDirectory cleans its own tree.
        self.addCleanup(os.rmdir, alias)
        self.assert_rejected_without_writes(self.engine, alias / "new-data")


if __name__ == "__main__":
    unittest.main()

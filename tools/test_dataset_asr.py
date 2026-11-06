"""Pure bridge contract tests; real model inference is an explicit local acceptance step."""
import importlib.util
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
import uuid
import wave

SCRIPT = Path(__file__).with_name("dataset_asr.py")


class DatasetAsrTests(unittest.TestCase):
    def setUp(self):
        self.assertTrue(SCRIPT.is_file(), "The offline dataset ASR bridge is missing")
        spec = importlib.util.spec_from_file_location("dataset_asr", SCRIPT)
        self.bridge = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.bridge)
        self.temporary = tempfile.TemporaryDirectory(prefix="VoiceLiteDatasetAsr-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.engine = self.root / "engine"
        for name in self.bridge.MODEL_NAMES:
            model = self.engine / "tools/asr/models" / name
            model.mkdir(parents=True)
            (model / "model.pt").write_bytes(b"test model placeholder")
            (model / "config.yaml").write_text("test: true", encoding="utf-8")
        audio = self.root / "audio"
        audio.mkdir()
        self.clip_id = uuid.uuid4().hex
        self.wav = audio / (self.clip_id + ".wav")
        with wave.open(str(self.wav), "wb") as writer:
            writer.setnchannels(1); writer.setsampwidth(2); writer.setframerate(32000)
            writer.writeframes(b"\0\0" * 32000 * 3)
        self.plan = {"schema": "voicelite-asr-input-1", "audio_root": str(audio),
                     "inputs": [{"id": self.clip_id, "audio_path": str(self.wav),
                                 "sha256": hashlib.sha256(self.wav.read_bytes()).hexdigest()}]}

    def test_uses_local_cpu_models_with_download_and_remote_code_disabled(self):
        captured = {}
        def factory(**arguments):
            captured.update(arguments)
            return object()
        self.bridge.build_model(self.engine, factory)
        self.assertEqual("cpu", captured["device"])
        self.assertTrue(captured["disable_update"])
        self.assertFalse(captured["check_latest"])
        self.assertFalse(captured["trust_remote_code"])
        self.assertTrue(Path(captured["model"]).is_relative_to(self.engine))

    def test_missing_local_model_is_rejected_before_factory(self):
        (self.engine / "tools/asr/models" / self.bridge.MODEL_NAMES[0] / "model.pt").unlink()
        with self.assertRaises(FileNotFoundError):
            self.bridge.build_model(self.engine, lambda **arguments: self.fail("Should not load/download"))

    def test_plan_checks_hash_duplicates_and_audio_scope(self):
        self.bridge.validate_plan(self.plan)
        for mutation in (lambda plan: plan["inputs"][0].update(sha256="0" * 64),
                         lambda plan: plan["inputs"].append(dict(plan["inputs"][0])),
                         lambda plan: plan.update(audio_root=str(self.root / "other"))):
            plan = json.loads(json.dumps(self.plan)); mutation(plan)
            with self.assertRaises((ValueError, FileNotFoundError)):
                self.bridge.validate_plan(plan)

    def test_results_remain_drafts_and_bad_labels_are_reported_per_clip(self):
        class Model:
            def generate(inner, **arguments): return [{"text": "测试草稿"}]
        result = self.bridge.transcribe(self.plan, Model())
        self.assertEqual(self.clip_id, result["drafts"][0]["id"])
        self.assertEqual("测试草稿", result["drafts"][0]["text"])
        self.assertFalse(result["human_verified"])
        class BadModel:
            def generate(inner, **arguments): return [{"text": "bad|label"}]
        result = self.bridge.transcribe(self.plan, BadModel())
        self.assertEqual([], result["drafts"])
        self.assertEqual(self.clip_id, result["issues"][0]["id"])


if __name__ == "__main__": unittest.main()

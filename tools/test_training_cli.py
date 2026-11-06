"""Training adapter tests use tiny audited stand-ins, never launch a GPU model."""
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock
import uuid
import wave

SCRIPT = Path(__file__).with_name("training_cli.py")


class TrainingCliTests(unittest.TestCase):
    def setUp(self):
        self.assertTrue(SCRIPT.is_file(), "Reusable training adapter is missing")
        spec = importlib.util.spec_from_file_location("training_cli", SCRIPT)
        self.cli = importlib.util.module_from_spec(spec); spec.loader.exec_module(self.cli)
        self.temporary = tempfile.TemporaryDirectory(prefix="VoiceLiteTrainingAdapter-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name); self.engine = self.root / "engine"; self.dataset = self.root / "dataset"
        self.engine.mkdir(); (self.dataset / "wavs").mkdir(parents=True)
        identity = uuid.uuid4().hex; audio = self.dataset / "wavs" / (identity + ".wav")
        with wave.open(str(audio), "wb") as file:
            file.setnchannels(1); file.setsampwidth(2); file.setframerate(32000); file.writeframes(b"\0\0" * 32000 * 3)
        digest = hashlib.sha256(audio.read_bytes()).hexdigest()
        self.document = {"schema": "voicelite-dataset-1", "id": uuid.uuid4().hex, "name": "测试素材", "revision": 1,
                         "clips": [{"id": identity, "wavePath": "wavs/" + identity + ".wav", "sourceName": "test.wav",
                                    "sourceSha256": digest, "waveSha256": digest, "durationSeconds": 3,
                                    "text": "测试校对", "language": "zh", "included": True, "reviewed": True}]}
        self.save_dataset()
        sources = {"api_v2.py": b"audited placeholder", "config.py": b"", "LICENSE": b"test",
                   "GPT_SoVITS/configs/s2v2ProPlus.json": json.dumps({"train": {}, "data": {}, "model": {}}).encode()}
        for name, content in sources.items():
            path = self.engine / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(content)
        models = ["GPT_SoVITS/pretrained_models/s1v3.ckpt", "GPT_SoVITS/pretrained_models/v2Pro/s2Gv2ProPlus.pth",
                  "GPT_SoVITS/pretrained_models/v2Pro/s2Dv2ProPlus.pth", "GPT_SoVITS/pretrained_models/sv/pretrained_eres2netv2w24s4ep4.ckpt"]
        for name in models:
            path = self.engine / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(b"test model")
        self.profile = {"schema": 1, "model_version": "v2ProPlus", "api_sha256": hashlib.sha256(sources["api_v2.py"]).hexdigest(),
                        "sources": [{"path": name, "sha256": hashlib.sha256(content).hexdigest()} for name, content in sources.items()],
                        "models": [{"path": name, "sha256": hashlib.sha256(b"test model").hexdigest()} for name in models]}

    def save_dataset(self):
        (self.dataset / "dataset.json").write_text(json.dumps(self.document), encoding="utf-8")
        row = self.document["clips"][0]
        (self.dataset / "train.list").write_text(f"{row['wavePath']}|voicelite|{row['language'].upper()}|{row['text']}\n", encoding="utf-8")

    def test_validates_portable_reviewed_dataset_and_rejects_bad_paths_or_labels(self):
        self.assertEqual(1, len(self.cli.validate_dataset(self.dataset)["clips"]))
        for key, value in (("reviewed", False), ("included", False), ("wavePath", "../outside.wav"), ("text", "bad|label"), ("waveSha256", "0" * 64)):
            previous = self.document["clips"][0][key]; self.document["clips"][0][key] = value; self.save_dataset()
            with self.assertRaises(ValueError): self.cli.validate_dataset(self.dataset)
            self.document["clips"][0][key] = previous; self.save_dataset()

    def test_workspace_creates_generic_sovits_config_and_preserves_engine_sources(self):
        work = self.root / "run"
        state = self.cli.init_workspace(self.engine, self.dataset, work, self.profile, make_links=False)
        config = json.loads((work / "configs/s2.json").read_text(encoding="utf-8"))
        self.assertEqual(8, config["train"]["epochs"])
        self.assertEqual(2, config["train"]["batch_size"])
        self.assertEqual("v2ProPlus", config["model"]["version"])
        self.assertNotIn("hutao", config["name"])
        self.assertEqual(str(self.engine.resolve()), state["engine"])
        self.assertTrue((work / "data/upstream.list").read_text(encoding="utf-8").startswith((work / "data/wavs").as_posix()))
        self.assertTrue((work / "data/train.list").read_text(encoding="utf-8").startswith("wavs/"))
        self.assertEqual(b"audited placeholder", (self.engine / "api_v2.py").read_bytes())
        with self.assertRaises(FileExistsError): self.cli.init_workspace(self.engine, self.dataset, work, self.profile, make_links=False)

    def test_workspace_must_stay_outside_inputs_and_engine(self):
        for work in (self.engine / "new-run", self.dataset / "new-run"):
            with self.assertRaises(ValueError): self.cli.init_workspace(self.engine, self.dataset, work, self.profile, make_links=False)

    def test_changed_upstream_is_rejected_before_workspace_creation(self):
        (self.engine / "api_v2.py").write_bytes(b"different")
        with self.assertRaises(ValueError): self.cli.init_workspace(self.engine, self.dataset, self.root / "run", self.profile, make_links=False)
        self.assertFalse((self.root / "run").exists())

    def test_resume_requires_complete_checkpoints_and_increasing_epochs(self):
        work = self.root / "run"; self.cli.init_workspace(self.engine, self.dataset, work, self.profile, make_links=False)
        with self.assertRaises(ValueError): self.cli.configure_training(work, epochs=9, resume=True)
        state_path = work / "run.json"; state = json.loads(state_path.read_text(encoding="utf-8")); state["epochs_completed"] = 8
        state_path.write_text(json.dumps(state), encoding="utf-8")
        logs = work / "features/logs_s2_v2ProPlus"; logs.mkdir(parents=True, exist_ok=True)
        for name in ("G_233333333333.pth", "D_233333333333.pth"): (logs / name).write_bytes(b"checkpoint placeholder")
        config = self.cli.configure_training(work, epochs=9, resume=True)
        self.assertEqual(9, config["train"]["epochs"])
        with self.assertRaises(ValueError): self.cli.configure_training(work, epochs=8, resume=True)

    def test_interrupted_run_with_checkpoints_cannot_restart_silently(self):
        work = self.root / "run"; self.cli.init_workspace(self.engine, self.dataset, work, self.profile, make_links=False)
        (work / "features/logs_s2_v2ProPlus/G_233333333333.pth").write_bytes(b"interrupted checkpoint")
        with self.assertRaises(ValueError): self.cli.configure_training(work, epochs=8, resume=False)

    def test_stage_rejects_external_config_or_output_directory(self):
        work = self.root / "run"; self.cli.init_workspace(self.engine, self.dataset, work, self.profile, make_links=False)
        self.assertTrue(callable(getattr(self.cli, "validate_stage_arguments", None)), "Stage config isolation is missing")
        with self.assertRaises(ValueError): self.cli.validate_stage_arguments(work, "GPT_SoVITS/s2_train.py", ["--config", str(self.engine / "foreign.json")])
        config_path = work / "configs/s2.json"; config = json.loads(config_path.read_text(encoding="utf-8"))
        config["save_weight_dir"] = str(self.engine / "weights")
        config_path.write_text(json.dumps(config), encoding="utf-8")
        with self.assertRaises(ValueError): self.cli.validate_stage_arguments(work, "GPT_SoVITS/s2_train.py", ["--config", str(config_path)])

    def test_english_features_do_not_require_chinese_bert_artifacts(self):
        self.document["clips"][0]["language"] = "en"; self.save_dataset()
        work = self.root / "run"; self.cli.init_workspace(self.engine, self.dataset, work, self.profile, make_links=False)
        name = self.document["clips"][0]["id"] + ".wav"
        def fake_stage(*arguments, **keywords):
            folder = work / "features"
            (folder / "2-name2text-0.txt").write_text(name + "\tplaceholder\n", encoding="utf-8")
            (folder / "6-name2semantic-0.tsv").write_text(name + "\t1 2 3\n", encoding="utf-8")
            for directory, suffix in (("4-cnhubert", ".pt"), ("5-wav32k", ""), ("7-sv_cn", ".pt")):
                path = folder / directory / (name + suffix); path.parent.mkdir(exist_ok=True); path.write_bytes(b"feature placeholder")
        with mock.patch.object(self.cli, "run_stage", side_effect=fake_stage): self.cli.prepare_features(work)
        self.assertTrue(json.loads((work / "run.json").read_text(encoding="utf-8"))["features_verified"])

    def test_changed_training_inputs_require_a_new_workspace_before_any_stage(self):
        work = self.root / "run"; self.cli.init_workspace(self.engine, self.dataset, work, self.profile, make_links=False)
        document = json.loads((work / "data/dataset.json").read_text(encoding="utf-8")); document["clips"][0]["text"] = "更正后的文字"
        (work / "data/dataset.json").write_text(json.dumps(document), encoding="utf-8")
        row = document["clips"][0]
        (work / "data/train.list").write_text(f"{row['wavePath']}|voicelite|ZH|{row['text']}\n", encoding="utf-8")
        with mock.patch.object(self.cli, "run_stage", side_effect=AssertionError("Changed input must not start a stage")):
            with self.assertRaises(ValueError): self.cli.prepare_features(work)

    def test_dotdot_alias_cannot_put_a_workspace_inside_the_engine(self):
        (self.root / "detour").mkdir()
        alias = self.root / "detour" / ".." / "engine" / "new-run"
        with self.assertRaises(ValueError): self.cli.init_workspace(self.engine, self.dataset, alias, self.profile, make_links=False)


if __name__ == "__main__": unittest.main()

"""Offline Chinese transcription drafts, using existing local classic FunASR models."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import sys
import uuid
import wave

MODEL_NAMES = (
    "speech_paraformer-large_asr_nat-zh-cn-16k-common-vocab8404-pytorch",
    "speech_fsmn_vad_zh-cn-16k-common-pytorch",
    "punc_ct-transformer_zh-cn-common-vocab272727-pytorch",
)


def plain(path):
    path = Path(path).absolute()
    for item in (path, *path.parents):
        if item.is_symlink() or (hasattr(item, "is_junction") and item.is_junction()):
            raise ValueError("Linked input/output paths are not supported")


def validate_plan(plan):
    if plan.get("schema") != "voicelite-asr-input-1" or not 1 <= len(plan.get("inputs", [])) <= 500:
        raise ValueError("Invalid ASR plan")
    root = Path(plan["audio_root"])
    if not root.is_absolute() or not root.is_dir():
        raise ValueError("Missing absolute audio root")
    plain(root)
    identities = set()
    for item in plan["inputs"]:
        identity = item["id"]
        if uuid.UUID(hex=identity).hex != identity or identity in identities:
            raise ValueError("Duplicate or invalid clip ID")
        identities.add(identity)
        path = Path(item["audio_path"])
        plain(path)
        if not path.is_absolute() or not path.resolve().is_relative_to(root.resolve()) or path.name != identity + ".wav":
            raise ValueError("Audio path outside the managed input root")
        if not path.is_file() or path.stat().st_size > 2 * 1024 * 1024:
            raise ValueError("Missing or oversized audio")
        with path.open("rb") as stream:
            if hashlib.file_digest(stream, "sha256").hexdigest().lower() != item["sha256"].lower():
                raise ValueError("Audio SHA mismatch")
        with wave.open(str(path), "rb") as audio:
            if audio.getnchannels() != 1 or audio.getsampwidth() != 2 or audio.getframerate() != 32000 or not 2 <= audio.getnframes() / 32000 <= 18:
                raise ValueError("Expected 2-18 seconds, 32kHz mono PCM16")
    return plan


def build_model(engine, factory=None):
    paths = [Path(engine).resolve() / "tools/asr/models" / name for name in MODEL_NAMES]
    for folder in paths:
        for name in ("model.pt", "config.yaml"):
            file = folder / name
            if not file.is_file() or file.stat().st_size == 0:
                raise FileNotFoundError(file)
    if factory is None:
        from funasr import AutoModel
        factory = AutoModel
    return factory(model=str(paths[0]), vad_model=str(paths[1]), punc_model=str(paths[2]),
                   device="cpu", ncpu=2, disable_update=True, check_latest=False, trust_remote_code=False)


def transcribe(plan, model):
    validate_plan(plan)
    drafts, issues = [], []
    for index, item in enumerate(plan["inputs"], 1):
        try:
            output = model.generate(input=item["audio_path"], batch_size_s=30)
            text = output[0].get("text", "").strip() if output else ""
            if not text or len(text) > 2000 or any(character in text for character in "|\r\n\0"):
                raise ValueError("No safe transcription")
            drafts.append({"id": item["id"], "text": text, "language": "zh"})
        except Exception:
            issues.append({"id": item["id"], "message": "此段未能生成文字，请试听后手动填写。"})
        print("VOICELITE_ASR_PROGRESS " + json.dumps({"completed": index, "total": len(plan["inputs"])}), flush=True)
    return {"schema": "voicelite-asr-drafts-1", "human_verified": False, "drafts": drafts, "issues": issues}


def initialize(engine, scratch):
    sys.stdout.reconfigure(encoding="utf-8"); sys.stderr.reconfigure(encoding="utf-8")
    os.environ.update(HF_HUB_OFFLINE="1", TRANSFORMERS_OFFLINE="1", HF_HUB_DISABLE_TELEMETRY="1",
                      PYTHONDONTWRITEBYTECODE="1", OMP_NUM_THREADS="2", MKL_NUM_THREADS="2",
                      HF_HOME=str(scratch / "hf-cache"), MODELSCOPE_CACHE=str(scratch / "modelscope-cache"),
                      TORCH_HOME=str(scratch / "torch-cache"), TEMP=str(scratch), TMP=str(scratch))
    spec = importlib.util.spec_from_file_location("voice_lite_network_guard", Path(__file__).with_name("engine_runner.py"))
    guard = importlib.util.module_from_spec(spec); spec.loader.exec_module(guard); guard.block_external_network()
    sys.path.insert(0, str(engine))
    from tools import portable_runtime
    portable_runtime.activate()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--engine", type=Path, required=True)
    parser.add_argument("--plan", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    arguments = parser.parse_args()
    plan_path = arguments.plan.absolute(); output_path = arguments.output.absolute()
    plain(plan_path); plain(output_path.parent)
    if plan_path.stat().st_size > 2 * 1024 * 1024 or output_path.exists() or output_path.parent != plan_path.parent:
        raise ValueError("Use a new output in the private job directory")
    plan = validate_plan(json.loads(plan_path.read_text(encoding="utf-8")))
    initialize(arguments.engine.resolve(), plan_path.parent)
    result = transcribe(plan, build_model(arguments.engine))
    temporary = output_path.with_name(output_path.name + ".partial")
    with temporary.open("x", encoding="utf-8") as file:
        json.dump(result, file, ensure_ascii=False)
    temporary.replace(output_path)


if __name__ == "__main__": main()

"""Launch unchanged audited upstream stages inside their isolated training workspace."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import runpy
import sys

TOOLS = Path(__file__).resolve().parent
sys.path.insert(0, str(TOOLS))
from training_cli import STAGES, load_run, validate_dataset, verify_files, validate_stage_arguments, digest


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--work", type=Path, required=True); parser.add_argument("--script", required=True)
    parser.add_argument("--require-resume", action="store_true")
    args, stage_arguments = parser.parse_known_args()
    allowed = {"GPT_SoVITS/prepare_datasets/" + stage for stage in STAGES} | {"GPT_SoVITS/s2_train.py"}
    if args.script not in allowed: raise ValueError("Only the audited local preparation/SoVITS stages are allowed")
    work, state = load_run(args.work); validate_dataset(work / "data")
    validate_stage_arguments(work, args.script, stage_arguments)
    if args.script == "GPT_SoVITS/s2_train.py" and state.get("features_dataset_sha256") != digest(work / "data/dataset.json"):
        raise ValueError("Training features no longer match the dataset")
    source = work / "engine-source"
    profile = json.loads((work / "training-profile.json").read_text(encoding="utf-8"))
    verify_files(source, profile["sources"])
    verify_files(source, profile["models"])
    os.chdir(source)
    sys.stdout.reconfigure(encoding="utf-8"); sys.stderr.reconfigure(encoding="utf-8")
    os.environ.update(PYTHONDONTWRITEBYTECODE="1", HF_HUB_OFFLINE="1", TRANSFORMERS_OFFLINE="1", HF_HUB_DISABLE_TELEMETRY="1",
                      HF_HOME=str(work / "cache/hf"), MODELSCOPE_CACHE=str(work / "cache/modelscope"), TORCH_HOME=str(work / "cache/torch"),
                      TEMP=str(work / "temp"), TMP=str(work / "temp"), version="v2ProPlus", hz="25hz", is_half="True",
                      OMP_NUM_THREADS="2", MKL_NUM_THREADS="2", _CUDA_VISIBLE_DEVICES="0", i_part="0", all_parts="1",
                      exp_name=state["name"], inp_text=str(work / "data/upstream.list"), inp_wav_dir=str(work / "data/wavs"), opt_dir=str(work / "features"),
                      bert_path=str(source / "GPT_SoVITS/pretrained_models/chinese-roberta-wwm-ext-large"),
                      bert_pretrained_dir=str(source / "GPT_SoVITS/pretrained_models/chinese-roberta-wwm-ext-large"),
                      cnhubert_base_dir=str(source / "GPT_SoVITS/pretrained_models/chinese-hubert-base"),
                      pretrained_s2G=str(source / "GPT_SoVITS/pretrained_models/v2Pro/s2Gv2ProPlus.pth"),
                      s2config_path=str(source / "GPT_SoVITS/configs/s2v2ProPlus.json"),
                      sv_path=str(source / "GPT_SoVITS/pretrained_models/sv/pretrained_eres2netv2w24s4ep4.ckpt"))
    spec = importlib.util.spec_from_file_location("voice_lite_network_guard", TOOLS / "engine_runner.py")
    guard = importlib.util.module_from_spec(spec); spec.loader.exec_module(guard); guard.block_external_network()
    sys.path.insert(0, str(source))
    from tools import portable_runtime
    portable_runtime.activate()
    import torch
    if not torch.cuda.is_available() or torch.cuda.device_count() != 1:
        raise RuntimeError("This audited training adapter requires one available NVIDIA CUDA device")
    if args.require_resume:
        import gc
        config = json.loads((work / "configs/s2.json").read_text(encoding="utf-8"))
        epochs = []
        for name in ("G_233333333333.pth", "D_233333333333.pth"):
            checkpoint = torch.load(work / "features/logs_s2_v2ProPlus" / name, map_location="cpu", weights_only=False)
            epoch = checkpoint.get("iteration")
            if not checkpoint.get("optimizer") or type(epoch) is not int or not 0 < epoch < config["train"]["epochs"]:
                raise ValueError("Resume requires complete optimizer checkpoints and a strictly later target epoch")
            epochs.append(epoch); del checkpoint; gc.collect()
        if epochs[0] != epochs[1]: raise ValueError("G and D checkpoint epochs do not match")
    script = source / args.script
    sys.argv = [str(script), *stage_arguments]
    try: runpy.run_path(str(script), run_name="__main__")
    except SystemExit as error:
        if error.code not in (None, 0): raise
    if args.script == "GPT_SoVITS/s2_train.py":
        from training_export import verify_checkpoints
        config = json.loads((work / "configs/s2.json").read_text(encoding="utf-8"))
        verify_checkpoints(lambda path: torch.load(path, map_location="cpu", weights_only=False), work, config["train"]["epochs"])
        print("VOICELITE_VERIFIED_CHECKPOINTS " + str(config["train"]["epochs"]), flush=True)


if __name__ == "__main__": main()

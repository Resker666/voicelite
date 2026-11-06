"""Explicit v2ProPlus training adapter; use reviewed dataset exports and existing local resources."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import uuid
import wave

TOOLS = Path(__file__).resolve().parent
STAGES = ("1-get-text.py", "2-get-hubert-wav32k.py", "2-get-sv.py", "3-get-semantic.py")


def digest(path):
    with Path(path).open("rb") as file: return hashlib.file_digest(file, "sha256").hexdigest()


def plain(path):
    path = Path(path).absolute()
    for item in (path, *path.parents):
        if item.is_symlink() or (hasattr(item, "is_junction") and item.is_junction()):
            raise ValueError("Input paths must not contain links: " + str(item))


def write_json(path, data):
    temporary = Path(str(path) + ".partial")
    with temporary.open("x", encoding="utf-8") as file: json.dump(data, file, ensure_ascii=False, indent=2)
    temporary.replace(path)


def validate_dataset(folder):
    folder = Path(folder).absolute(); plain(folder)
    manifest = folder / "dataset.json"; plain(manifest)
    if manifest.stat().st_size > 2 * 1024 * 1024: raise ValueError("Dataset manifest too large")
    data = json.loads(manifest.read_text(encoding="utf-8"))
    if data.get("schema") != "voicelite-dataset-1" or not 1 <= len(data.get("clips", [])) <= 500:
        raise ValueError("Expected a nonempty VoiceLite reviewed training export")
    if uuid.UUID(hex=data["id"]).hex != data["id"]: raise ValueError("Invalid dataset ID")
    names, lines = set(), []
    for clip in data["clips"]:
        identity = clip["id"]
        if uuid.UUID(hex=identity).hex != identity or identity in names or clip["wavePath"] != f"wavs/{identity}.wav":
            raise ValueError("Invalid, duplicate or unsafe clip path")
        names.add(identity)
        text = clip.get("text", "")
        if clip.get("included") is not True or clip.get("reviewed") is not True or clip.get("language") not in ("zh", "en"):
            raise ValueError("Only included, reviewed clips may enter training")
        if not isinstance(text, str) or not text.strip() or len(text) > 2000 or any(char in text for char in "|\r\n\0"):
            raise ValueError("Invalid single-line training label")
        audio = folder / clip["wavePath"]; plain(audio)
        if audio.stat().st_size > 2 * 1024 * 1024 or digest(audio).lower() != clip["waveSha256"].lower():
            raise ValueError("Audio hash or size mismatch")
        with wave.open(str(audio), "rb") as file:
            if file.getframerate() != 32000 or file.getnchannels() != 1 or file.getsampwidth() != 2 or not 2 <= file.getnframes() / 32000 <= 18:
                raise ValueError("Expected 2-18 seconds of mono 32kHz PCM16")
        lines.append(f"{clip['wavePath']}|voicelite|{clip['language'].upper()}|{text.strip()}")
    if (folder / "train.list").read_text(encoding="utf-8").splitlines() != lines:
        raise ValueError("train.list differs from the reviewed dataset manifest")
    return data


def verify_files(root, entries):
    root = Path(root)
    for entry in entries:
        relative = entry["path"]
        if "\\" in relative or ":" in relative or relative.startswith("/") or any(part in ("", ".", "..") for part in relative.split("/")):
            raise ValueError("Unsafe audited source path")
        path = root / relative
        if digest(path).lower() != entry["sha256"].lower(): raise ValueError("Audited file changed: " + relative)


def junction(link, target):
    if os.name != "nt": raise RuntimeError("Windows junction support is required")
    link.parent.mkdir(parents=True, exist_ok=True)
    environment = dict(os.environ, VOICELITE_LINK=str(link), VOICELITE_TARGET=str(target))
    subprocess.run(["powershell.exe", "-NoProfile", "-NonInteractive", "-Command",
                    "New-Item -ItemType Junction -Path $env:VOICELITE_LINK -Target $env:VOICELITE_TARGET -ErrorAction Stop | Out-Null"],
                   env=environment, check=True, capture_output=True, timeout=20, creationflags=subprocess.CREATE_NO_WINDOW)


def init_workspace(engine, dataset, work, profile, make_links=True):
    engine, dataset, work = map(lambda path: Path(path).absolute(), (engine, dataset, work))
    plain(engine); plain(dataset); plain(work.parent)
    engine, dataset, work = (path.resolve() for path in (engine, dataset, work))
    if work.is_relative_to(engine) or work.is_relative_to(dataset): raise ValueError("Workspace must be outside the engine and input dataset")
    if work.exists(): raise FileExistsError("Use a fresh workspace; existing runs are never overwritten")
    data = validate_dataset(dataset)
    if profile.get("schema") != 1 or profile.get("model_version") != "v2ProPlus": raise ValueError("Unsupported training profile")
    if digest(engine / "api_v2.py").lower() != profile["api_sha256"].lower(): raise ValueError("Upstream API snapshot changed")
    verify_files(engine, profile["sources"] + profile["models"])
    work.mkdir(); (work / "INCOMPLETE").write_text("Workspace initialization in progress", encoding="utf-8")
    for name in ("engine-source", "data/wavs", "configs", "features/logs_s2_v2ProPlus", "weights/SoVITS", "logs", "cache", "temp"):
        (work / name).mkdir(parents=True, exist_ok=True)
    for entry in profile["sources"]:
        source = engine / entry["path"]; plain(source)
        target = work / "engine-source" / entry["path"]; target.parent.mkdir(parents=True, exist_ok=True); shutil.copy2(source, target)
    if make_links:
        for relative in ("runtime", "GPT_SoVITS/pretrained_models", "GPT_SoVITS/text/G2PWModel", "GPT_SoVITS/Accel/vendor"):
            target = engine / relative
            if not target.is_dir(): raise FileNotFoundError(target)
            junction(work / "engine-source" / relative, target)
    for clip in data["clips"]: shutil.copy2(dataset / clip["wavePath"], work / "data" / clip["wavePath"])
    write_json(work / "data/dataset.json", data)
    relative_lines = (dataset / "train.list").read_text(encoding="utf-8")
    (work / "data/train.list").write_text(relative_lines, encoding="utf-8")
    absolute_lines = [f"{(work / 'data' / clip['wavePath']).as_posix()}|voicelite|{clip['language'].upper()}|{clip['text'].strip()}" for clip in data["clips"]]
    (work / "data/upstream.list").write_text("\n".join(absolute_lines) + "\n", encoding="utf-8")
    state = {"schema": "voicelite-training-run-1", "workspace": str(work), "engine": str(engine), "dataset_id": data["id"],
             "name": "voicelite_" + data["id"][:12], "epochs_completed": 0, "features_verified": False,
             "input_files": {name: digest(work / "data" / name) for name in ("dataset.json", "train.list", "upstream.list")}}
    write_json(work / "training-profile.json", profile); write_json(work / "run.json", state)
    configure_training(work, epochs=8, resume=False)
    (work / "INCOMPLETE").unlink()
    return state


def load_run(work):
    work = Path(work).absolute(); plain(work)
    work = work.resolve()
    if (work / "INCOMPLETE").exists(): raise ValueError("Workspace is incomplete; use a new directory and inspect the initial failure")
    state = json.loads((work / "run.json").read_text(encoding="utf-8"))
    if state.get("schema") != "voicelite-training-run-1" or state["workspace"] != str(work): raise ValueError("Workspace identity mismatch")
    expected = state.get("input_files", {})
    if set(expected) != {"dataset.json", "train.list", "upstream.list"}:
        raise ValueError("Workspace lacks a frozen input snapshot; initialize a fresh workspace")
    for name, value in expected.items():
        if digest(work / "data" / name) != value:
            raise ValueError("Training inputs changed; initialize a new workspace for the corrected or expanded dataset")
    return work, state


def configure_training(work, epochs=8, resume=False):
    # init calls this before removing its INCOMPLETE marker.
    work = Path(work).absolute(); state = json.loads((work / "run.json").read_text(encoding="utf-8"))
    if type(epochs) is not int or not 1 <= epochs <= 100: raise ValueError("Epochs must be within 1-100")
    if resume:
        if epochs <= state["epochs_completed"]: raise ValueError("Resume epochs must exceed the completed epoch")
        for name in ("G_233333333333.pth", "D_233333333333.pth"):
            if not (work / "features/logs_s2_v2ProPlus" / name).is_file(): raise ValueError("Complete G and D checkpoints are required")
    elif state["epochs_completed"] or any((work / "features/logs_s2_v2ProPlus" / name).exists() for name in ("G_233333333333.pth", "D_233333333333.pth")):
        raise ValueError("Existing training must be resumed explicitly, or initialized in a new workspace")
    source = work / "engine-source"
    config = json.loads((source / "GPT_SoVITS/configs/s2v2ProPlus.json").read_text(encoding="utf-8"))
    config["train"].update(epochs=epochs, batch_size=2, learning_rate=0.0001, text_low_lr_rate=0.4,
                           fp16_run=True, grad_ckpt=True, if_save_latest=True, if_save_every_weights=True, save_every_epoch=1,
                           pretrained_s2G=str(source / "GPT_SoVITS/pretrained_models/v2Pro/s2Gv2ProPlus.pth"),
                           pretrained_s2D=str(source / "GPT_SoVITS/pretrained_models/v2Pro/s2Dv2ProPlus.pth"), gpu_numbers="0", log_interval=20)
    config["model"]["version"] = "v2ProPlus"; config["data"]["exp_dir"] = str(work / "features")
    config.update(s2_ckpt_dir=str(work / "features"), save_weight_dir=str(work / "weights/SoVITS"), name=state["name"], version="v2ProPlus")
    write_json(work / "configs/s2.json", config)
    return config


def run_stage(work, script, arguments=(), timeout=600, require_resume=False):
    sys.path.insert(0, str(TOOLS))
    from training_process import run_owned
    work, state = load_run(work)
    label = Path(script).stem + "-" + uuid.uuid4().hex[:8]
    log = work / "logs" / (label + ".log")
    print("Running " + script + "; log=" + str(log), flush=True)
    run_owned([Path(state["engine"]) / "runtime/python.exe", "-B", "-u", TOOLS / "training_stage.py", "--work", work,
               "--script", script, *(["--require-resume"] if require_resume else []), *arguments], work / "engine-source", log, timeout)
    return log


def validate_stage_arguments(work, script, arguments):
    work = Path(work).absolute()
    if script in {"GPT_SoVITS/prepare_datasets/" + stage for stage in STAGES}:
        if arguments: raise ValueError("Preparation stages use the generated workspace inputs only")
        return
    config_path = work / "configs/s2.json"
    if script != "GPT_SoVITS/s2_train.py" or list(arguments) != ["--config", str(config_path)]:
        raise ValueError("Only the generated local SoVITS configuration is allowed")
    plain(config_path)
    config = json.loads(config_path.read_text(encoding="utf-8"))
    for value, expected in ((config["data"]["exp_dir"], work / "features"), (config["s2_ckpt_dir"], work / "features"),
                            (config["save_weight_dir"], work / "weights/SoVITS")):
        if Path(value).absolute() != expected: raise ValueError("Training outputs must stay inside their workspace")
        plain(expected)
    if config.get("version") != "v2ProPlus" or config["model"].get("version") != "v2ProPlus":
        raise ValueError("Training configuration model version changed")
    for key, name in (("pretrained_s2G", "s2Gv2ProPlus.pth"), ("pretrained_s2D", "s2Dv2ProPlus.pth")):
        if Path(config["train"][key]).absolute() != work / "engine-source/GPT_SoVITS/pretrained_models/v2Pro" / name:
            raise ValueError("Only the audited local pretrained weights are allowed")


def prepare_features(work):
    work, state = load_run(work); data = validate_dataset(work / "data")
    for stage in STAGES: run_stage(work, "GPT_SoVITS/prepare_datasets/" + stage)
    folder = work / "features"
    text = (folder / "2-name2text-0.txt").read_text(encoding="utf-8")
    semantic = (folder / "6-name2semantic-0.tsv").read_text(encoding="utf-8")
    names = {clip["id"] + ".wav" for clip in data["clips"]}
    if names != {line.split("\t")[0] for line in text.splitlines() if line} or names != {line.split("\t")[0] for line in semantic.splitlines() if line}:
        raise ValueError("Text/semantic features did not match all input clips")
    by_name = {clip["id"] + ".wav": clip for clip in data["clips"]}
    for name in names:
        required = [("4-cnhubert", ".pt"), ("5-wav32k", ""), ("7-sv_cn", ".pt")]
        if by_name[name]["language"] == "zh": required.append(("3-bert", ".pt"))
        for directory, suffix in required:
            if not (folder / directory / (name + suffix)).is_file(): raise ValueError("Missing feature " + directory + "/" + name)
    (folder / "2-name2text.txt").write_text(text, encoding="utf-8")
    (folder / "6-name2semantic.tsv").write_text("item_name\tsemantic_audio\n" + semantic, encoding="utf-8")
    state["features_verified"] = True; state["features_dataset_sha256"] = digest(work / "data/dataset.json"); write_json(work / "run.json", state)


def train_sovits(work, epochs, resume):
    work, state = load_run(work); validate_dataset(work / "data")
    if not state["features_verified"]: raise ValueError("Run and verify features first")
    if state.get("features_dataset_sha256") != digest(work / "data/dataset.json"): raise ValueError("Dataset changed after feature generation; prepare features again")
    configure_training(work, epochs, resume)
    if resume:
        backup = work / "checkpoint-history" / uuid.uuid4().hex; backup.mkdir(parents=True)
        for name in ("G_233333333333.pth", "D_233333333333.pth"): shutil.copy2(work / "features/logs_s2_v2ProPlus" / name, backup / name)
    log = run_stage(work, "GPT_SoVITS/s2_train.py", ["--config", str(work / "configs/s2.json")], timeout=7200, require_resume=resume)
    if resume:
        output = log.read_text(encoding="utf-8", errors="replace")
        from training_export import verify_resume_log
        verify_resume_log(output, state["epochs_completed"], epochs)
    from training_export import collect_result
    collect_result(work, epochs)
    state["epochs_completed"] = epochs; write_json(work / "run.json", state)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    validate = commands.add_parser("validate"); validate.add_argument("--dataset", type=Path, required=True)
    init = commands.add_parser("init"); init.add_argument("--engine", type=Path, required=True); init.add_argument("--dataset", type=Path, required=True); init.add_argument("--work", type=Path, required=True)
    features = commands.add_parser("features"); features.add_argument("--work", type=Path, required=True)
    train = commands.add_parser("sovits"); train.add_argument("--work", type=Path, required=True); train.add_argument("--epochs", type=int, default=8); train.add_argument("--resume", action="store_true")
    export = commands.add_parser("export-role"); export.add_argument("--work", type=Path, required=True); export.add_argument("--name", required=True)
    export.add_argument("--reference", required=True); export.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.command == "validate":
        data = validate_dataset(args.dataset); print(json.dumps({"validated_clips": len(data["clips"]), "review_status": "user-marked reviewed; not independently certified"}))
    elif args.command == "init":
        profile_path = TOOLS / "training-profile.json"
        if not profile_path.is_file(): profile_path = TOOLS.parent / "packaging/training-profile.json"
        profile = json.loads(profile_path.read_text(encoding="utf-8"))
        print(json.dumps(init_workspace(args.engine, args.dataset, args.work, profile), ensure_ascii=False))
    elif args.command == "features": prepare_features(args.work)
    elif args.command == "sovits": train_sovits(args.work, args.epochs, args.resume)
    else:
        sys.path.insert(0, str(TOOLS))
        from training_export import export_role
        print(json.dumps(export_role(args.work, args.name, args.reference, args.output), ensure_ascii=False))


if __name__ == "__main__":
    try: main()
    except (ValueError, OSError, RuntimeError, KeyError) as error:
        print(type(error).__name__ + ": " + str(error), file=sys.stderr); sys.exit(1)

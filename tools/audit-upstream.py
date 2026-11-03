"""Read-only inventory; no installation, download or upstream mutation."""
import argparse
import hashlib
import importlib.metadata
import json
from pathlib import Path
import subprocess
import sys
import torch

parser = argparse.ArgumentParser()
parser.add_argument("engine", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
root = args.engine.resolve()
profile = json.loads(Path(__file__).resolve().parents[1].joinpath("packaging/engine-profile.json").read_text(encoding="utf-8"))
paths = [root / name for name in ["api_v2.py", "GPT_SoVITS/TTS_infer_pack/TTS.py", "tools/portable_runtime.py"]]
for name in profile["model_paths"]:
    resource = root / name
    paths.extend(resource.rglob("*") if resource.is_dir() else [resource])
files = []
for path in paths:
    if path.is_file():
        with path.open("rb") as stream:
            files.append({"path": path.relative_to(root).as_posix(), "bytes": path.stat().st_size, "sha256": hashlib.file_digest(stream, "sha256").hexdigest()})
packages = sorted([{"name": dist.metadata.get("Name"), "version": dist.version, "license": dist.metadata.get("License-Expression") or dist.metadata.get("License", "unresolved")} for dist in importlib.metadata.distributions()], key=lambda row: (row["name"] or "").lower())
report = {"upstream_commit": None, "git_metadata_present": (root/".git").exists(), "python": sys.version, "torch": torch.__version__, "torch_cuda": torch.version.cuda, "cuda_available": torch.cuda.is_available(), "gpu": torch.cuda.get_device_name(0) if torch.cuda.is_available() else None,
          "files": files, "python_packages": packages, "ffmpeg": subprocess.run([str(root/"runtime/ffmpeg.exe"), "-version"], capture_output=True, text=True).stdout,
          "ffprobe": subprocess.run([str(root/"runtime/ffprobe.exe"), "-version"], capture_output=True, text=True).stdout}
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps({"files_hashed": len(files), "python_distributions": len(packages), "output": str(args.output)}))

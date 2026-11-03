"""Refresh both component and whole-candidate hashes after a client-only update."""
import argparse
import hashlib
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("package", type=Path)
parser.add_argument("profile", type=Path)
args = parser.parse_args()
root = args.package.resolve()
profile = json.loads(args.profile.read_text(encoding="utf-8"))
for relative in profile["required_files"]:
    resource = (root / "engine" / relative).resolve()
    if not resource.is_relative_to(root / "engine") or not resource.is_file() or resource.stat().st_size == 0:
        raise SystemExit("Missing required resource: " + relative)


def entry(path):
    if path.is_symlink() or not path.resolve().is_relative_to(root):
        raise SystemExit("Unsafe package path")
    with path.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    return {"path": path.relative_to(root).as_posix(), "bytes": path.stat().st_size, "sha256": digest}


engine_files = [entry(path) for path in sorted((root / "engine").rglob("*")) if path.is_file()]
if next(item["sha256"] for item in engine_files if item["path"] == "engine/api_v2.py").lower() != profile["api_sha256"].lower():
    raise SystemExit("API snapshot mismatch")
(root / "engine-files.json").write_text(json.dumps({"profile": profile, "files": engine_files}, ensure_ascii=False, indent=2), encoding="utf-8")
other_files = [entry(path) for path in sorted(root.rglob("*")) if path.is_file() and not path.is_relative_to(root / "engine") and path.name != "package-files.json"]
all_files = engine_files + other_files
(root / "package-files.json").write_text(json.dumps({"profile": profile, "files": all_files}, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps({"engine_files": len(engine_files), "engine_bytes": sum(item["bytes"] for item in engine_files), "total_files": len(all_files), "total_bytes": sum(item["bytes"] for item in all_files)}))

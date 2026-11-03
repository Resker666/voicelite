import argparse
import hashlib
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("package", type=Path)
args = parser.parse_args()
root = args.package.resolve()
manifest_path = root / "package-files.json"
if not manifest_path.exists():
    manifest_path = root / "engine-files.json"
manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
for item in manifest["files"]:
    path = (root / item["path"]).resolve()
    if not path.is_relative_to(root) or not path.is_file() or path.stat().st_size != item["bytes"]:
        raise SystemExit("Invalid package entry: " + item["path"])
    with path.open("rb") as stream:
        actual = hashlib.file_digest(stream, "sha256").hexdigest()
    if actual != item["sha256"]:
        raise SystemExit("Package hash mismatch: " + item["path"])
print(json.dumps({"verified_files": len(manifest["files"]), "bytes": sum(item["bytes"] for item in manifest["files"])}))

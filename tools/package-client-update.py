"""Build an offline client-only update against a previously verified full package."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import zipfile

parser = argparse.ArgumentParser()
parser.add_argument("published", type=Path)
parser.add_argument("base_manifest", type=Path)
parser.add_argument("destination", type=Path)
parser.add_argument("guide", type=Path)
args = parser.parse_args()
source, destination = args.published.resolve(), args.destination.resolve()
base = json.loads(args.base_manifest.read_text(encoding="utf-8"))
if destination.exists():
    raise SystemExit("Choose a new update directory.")
if source.is_relative_to(destination) or destination.is_relative_to(source):
    raise SystemExit("Publish and update directories must be independent.")
entries = {item["path"]: item for item in base["files"]}
destination.mkdir(parents=True)


def entry(path, relative):
    with path.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    return {"path": relative, "bytes": path.stat().st_size, "sha256": digest}


changed = []
for path in sorted(source.rglob("*")):
    if not path.is_file():
        continue
    relative = path.relative_to(source).as_posix()
    if path.is_symlink() or not path.resolve().is_relative_to(source) or relative.startswith("engine/"):
        raise SystemExit("Unexpected publish path: " + relative)
    item = entry(path, relative)
    if entries.get(relative, {}).get("sha256") == item["sha256"]:
        continue
    target = destination / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(path, target)
    entries[relative] = item
    changed.append(item)
shutil.copy2(args.guide, destination / "START_HERE.txt")
(destination / "README-UPDATE.txt").write_text(
    "VoiceLite 0.1.1 OGG compatibility update\n\n"
    "Close VoiceLite, then copy ALL files from this ZIP into your existing full offline package folder, replacing matching files.\n"
    "Keep the original engine folder. This update contains no models or user audio.\n"
    "OGG Vorbis/Opus is converted locally for import and preview. Inference references still need to be 3-10 seconds.\n"
    "The original full package's local-validation-only and third-party license restrictions remain in effect.\n", encoding="utf-8")
(destination / "client-update.json").write_text(json.dumps({
    "version": "0.1.1", "required_engine_profile": base["profile"]["id"],
    "engine_unchanged": True, "files": changed,
}, ensure_ascii=False, indent=2), encoding="utf-8")
for path in destination.rglob("*"):
    if path.is_file():
        relative = path.relative_to(destination).as_posix()
        entries[relative] = entry(path, relative)
(destination / "package-files.json").write_text(json.dumps({
    "profile": base["profile"], "client_version": "0.1.1",
    "engine_hashes": "retained from the previously verified, unchanged engine",
    "files": sorted(entries.values(), key=lambda item: item["path"]),
}, ensure_ascii=False, indent=2), encoding="utf-8")
archive = Path(str(destination) + ".zip")
if archive.exists():
    raise SystemExit("ZIP already exists.")
with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as zip_file:
    for path in sorted(destination.rglob("*")):
        if path.is_file():
            zip_file.write(path, path.relative_to(destination).as_posix())
report = entry(archive, archive.name)
archive.with_suffix(".zip.sha256").write_text(report["sha256"] + "  " + archive.name + "\n", encoding="ascii")
print(json.dumps({"changed_client_files": len(changed), "archive": str(archive), **report}))

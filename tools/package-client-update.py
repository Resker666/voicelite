"""Build a cumulative client update against the original full package manifest."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import xml.etree.ElementTree as ET
import zipfile


def entry(path, relative):
    with path.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    return {"path": relative, "bytes": path.stat().st_size, "sha256": digest}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("published", type=Path)
    parser.add_argument("base_manifest", type=Path)
    parser.add_argument("destination", type=Path)
    parser.add_argument("guide", type=Path)
    parser.add_argument("--version")
    args = parser.parse_args()
    source, destination = args.published.resolve(), args.destination.resolve()
    version = args.version or ET.parse(Path(__file__).resolve().parents[1] / "Directory.Build.props").findtext(".//Version")
    if not version or not re.fullmatch(r"\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?", version):
        raise SystemExit("Invalid client version.")
    base = json.loads(args.base_manifest.read_text(encoding="utf-8-sig"))
    if not base["profile"].get("api_sha256"):
        raise SystemExit("Base manifest must pin the engine API SHA-256.")
    archive = Path(str(destination) + ".zip")
    if destination.exists() or archive.exists():
        raise SystemExit("Choose a new update directory and ZIP name.")
    if source.is_relative_to(destination) or destination.is_relative_to(source):
        raise SystemExit("Publish and update directories must be independent.")
    entries = {item["path"]: item for item in base["files"] if item["path"] not in {"package-files.json", "client-update.json"}}
    payload = []
    for path in sorted(source.rglob("*")):
        if not path.is_file():
            continue
        relative = path.relative_to(source).as_posix()
        if path.is_symlink() or not path.resolve().is_relative_to(source) or relative.split("/")[0].lower() == "engine":
            raise SystemExit("Unexpected publish path: " + relative)
        item = entry(path, relative)
        # A cumulative update must also restore a component changed by an earlier
        # update, even if this release returns to the original base bytes (e.g. LF/CRLF).
        client_owned = relative == "engine_runner.py" or ("/" not in relative and relative.startswith("VoiceLite."))
        if client_owned or entries.get(relative, {}).get("sha256") != item["sha256"]:
            payload.append((path, relative))
    destination.mkdir(parents=True)
    for path, relative in payload:
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, target)
    shutil.copy2(args.guide, destination / "START_HERE.txt")
    shutil.copy2(Path(__file__).with_name("apply-client-update.py"), destination / "apply-client-update.py")
    (destination / "README-UPDATE.txt").write_text(
        f"VoiceLite {version} cumulative client update\n\n"
        "Includes OGG import/preview, CPU/NVIDIA selection and optional auxiliary reference recordings. Apply directly to the original full offline package.\n"
        "Close VoiceLite and its engine before updating. Use the Python inside your full package:\n"
        '  "<package>\\engine\\runtime\\python.exe" -B apply-client-update.py "<update>" "<package>" --check\n'
        '  "<package>\\engine\\runtime\\python.exe" -B apply-client-update.py "<update>" "<package>" --backup "<new-backup-folder>"\n'
        'Rollback: "<package>\\engine\\runtime\\python.exe" -B apply-client-update.py --rollback "<backup-folder>" "<package>"\n'
        "The backup must be outside the package and update directories. Keep it until validation passes.\n"
        "This update does not replace engine files, models, recordings or user settings.\n"
        "The original package's local-validation-only and third-party license restrictions remain in effect.\n", encoding="utf-8")
    for path in sorted(destination.rglob("*")):
        if path.is_file():
            relative = path.relative_to(destination).as_posix()
            entries[relative] = entry(path, relative)
    (destination / "package-files.json").write_text(json.dumps({
        "profile": base["profile"], "client_version": version,
        "engine_hashes": "retained from the previously verified, unchanged engine",
        "files": sorted(entries.values(), key=lambda item: item["path"]),
    }, ensure_ascii=False, indent=2), encoding="utf-8")
    # Update metadata is not installed, so the whole-package manifest has no circular hash.
    changed = [entry(path, path.relative_to(destination).as_posix()) for path in sorted(destination.rglob("*")) if path.is_file()]
    (destination / "client-update.json").write_text(json.dumps({
        "schema": 2, "version": version, "required_engine_profile": base["profile"]["id"],
        "api_sha256": base["profile"]["api_sha256"], "engine_unchanged": True, "files": changed,
    }, ensure_ascii=False, indent=2), encoding="utf-8")
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as zipped:
        for path in sorted(destination.rglob("*")):
            if path.is_file():
                zipped.write(path, path.relative_to(destination).as_posix())
    report = entry(archive, archive.name)
    archive.with_suffix(".zip.sha256").write_text(report["sha256"] + "  " + archive.name + "\n", encoding="ascii")
    print(json.dumps({"version": version, "updated_files": len(changed), "archive": str(archive), **report}))


if __name__ == "__main__":
    main()

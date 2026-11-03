"""Assemble a local offline candidate without modifying the source engine."""
import argparse
import hashlib
import json
import shutil
from pathlib import Path


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def selected_files(source, profile):
    roots = [source / "api_v2.py", source / "LICENSE", source / "runtime", source / "GPT_SoVITS", source / "tools"]
    models = profile["model_paths"]
    for root in roots:
        for path in ([root] if root.is_file() else root.rglob("*")):
            if not path.is_file():
                continue
            if path.is_symlink() or not path.resolve().is_relative_to(source):
                raise ValueError("Refusing linked file outside engine: " + str(path))
            relative = path.relative_to(source).as_posix()
            if "__pycache__" in path.parts or ".git" in path.parts or ".cache" in path.parts:
                continue
            if path.suffix.lower() in {".wav", ".mp3", ".flac", ".ogg", ".mp4"}:
                continue
            if relative.startswith(("tools/asr/", "tools/uvr5/")):
                continue
            if relative.startswith("GPT_SoVITS/pretrained_models/") and not any(relative == model or relative.startswith(model + "/") for model in models):
                continue
            if relative.startswith("GPT_SoVITS/text/G2PWModel/") and not profile["g2pw"]:
                continue
            yield path, relative


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--destination", type=Path, required=True)
    parser.add_argument("--profile", type=Path, required=True)
    parser.add_argument("--local-validation", action="store_true")
    args = parser.parse_args()
    source, destination = args.source.resolve(), args.destination.resolve()
    profile = json.loads(args.profile.read_text(encoding="utf-8-sig"))
    if not profile["public_redistribution_cleared"] and not args.local_validation:
        raise SystemExit("Public release blocked: per-component redistribution review is incomplete. Use --local-validation for a private candidate.")
    if destination.is_relative_to(source) or source.is_relative_to(destination):
        raise SystemExit("Source and destination must be independent directories.")
    if sha256(source / "api_v2.py").lower() != profile["api_sha256"].lower():
        raise SystemExit("API snapshot does not match audited profile.")
    for relative in profile["required_files"]:
        candidate = (source / relative).resolve()
        if not candidate.is_relative_to(source) or not candidate.is_file() or candidate.stat().st_size == 0:
            raise SystemExit("Missing or unsafe required resource: " + relative)
    for relative in profile["model_paths"]:
        candidate = (source / relative).resolve()
        if not candidate.is_relative_to(source) or not candidate.exists():
            raise SystemExit("Missing or unsafe model resource: " + relative)
    if destination.exists():
        raise SystemExit("Destination already exists; choose a new staging directory.")
    files = list(selected_files(source, profile))
    selected = {relative for _, relative in files}
    excluded_required = set(profile["required_files"]) - selected
    if excluded_required:
        raise SystemExit("Profile excludes required resources: " + ", ".join(sorted(excluded_required)))
    total = sum(path.stat().st_size for path, _ in files)
    if shutil.disk_usage(destination.parent).free < total + 500_000_000:
        raise SystemExit("Insufficient free space for offline candidate.")
    print(json.dumps({"stage": "copy", "files": len(files), "bytes": total}), flush=True)
    manifest = []
    for index, (path, relative) in enumerate(files):
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, target)
        manifest.append({"path": "engine/" + relative, "bytes": target.stat().st_size, "sha256": sha256(target)})
        if index % 10000 == 0:
            print(json.dumps({"copied": index, "total": len(files)}), flush=True)
    manifest_file = destination.parent / "engine-files.json"
    manifest_file.write_text(json.dumps({"profile": profile, "files": manifest}, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"stage": "complete", "files": len(files), "bytes": total, "manifest": str(manifest_file)}), flush=True)


if __name__ == "__main__":
    main()

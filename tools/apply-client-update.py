"""Validate, apply and roll back a client-only VoiceLite update."""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import shutil
import sys
import uuid


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def relative_path(value):
    if not isinstance(value, str) or not value or "\\" in value or ":" in value:
        raise ValueError("Invalid update path: " + str(value))
    path = PurePosixPath(value)
    if path.is_absolute() or value != path.as_posix() or any(part in {".", ".."} or part.endswith((".", " ")) for part in path.parts):
        raise ValueError("Unsafe update path: " + value)
    return path


def client_path(value):
    path = relative_path(value)
    forbidden = {"engine", "runtime", "models", "references", "results", "previews", "logs", "outputs", ".git", ".tools"}
    if path.parts[0].lower() in forbidden or path.name.lower() in {"settings.json", "client-update.json"}:
        raise ValueError("Refusing non-client update path: " + value)
    if path.suffix.lower() in {".wav", ".mp3", ".ogg", ".flac", ".m4a", ".ckpt", ".pth", ".pt", ".onnx", ".safetensors"}:
        raise ValueError("Refusing model or user audio: " + value)
    return path


def local_path(root, relative):
    parts = relative_path(relative).parts
    candidate = root.joinpath(*parts)
    if not candidate.resolve().is_relative_to(root):
        raise ValueError("Path leaves package: " + relative)
    current = root
    for part in parts:
        current = current / part
        if current.is_symlink() or current.is_junction():
            raise ValueError("Linked update path: " + relative)
    return candidate


def entries(items):
    result = {}
    seen = set()
    for item in items:
        value = item["path"]
        relative_path(value)
        if value.casefold() in seen or not isinstance(item["bytes"], int) or item["bytes"] < 0:
            raise ValueError("Invalid or duplicate manifest entry: " + value)
        if len(item["sha256"]) != 64 or any(char not in "0123456789abcdefABCDEF" for char in item["sha256"]):
            raise ValueError("Invalid SHA-256: " + value)
        seen.add(value.casefold())
        result[value] = item
    return result


def verify(path, item):
    if not path.is_file() or path.stat().st_size != item["bytes"] or digest(path).lower() != item["sha256"].lower():
        raise ValueError("File checksum mismatch: " + item["path"])


def check_processes(package):
    if os.name != "nt":
        return
    try:
        import psutil
    except ImportError as exception:
        raise ValueError("Use the full package's engine/runtime/python.exe (psutil is required for the process check).") from exception
    for process in psutil.process_iter(["pid", "exe"], ad_value=None):
        executable = process.info["exe"]
        if process.pid != os.getpid() and executable and Path(executable).resolve().is_relative_to(package):
            raise ValueError(f"Close the package's running client/engine before updating (PID {process.pid}).")


def check_update(update, package):
    if update == package or update.is_relative_to(package) or package.is_relative_to(update):
        raise ValueError("Update and package directories must be independent.")
    metadata = read_json(update / "client-update.json")
    if metadata.get("schema") != 2 or metadata.get("engine_unchanged") is not True:
        raise ValueError("Unsupported client update schema.")
    files = entries(metadata["files"])
    if "package-files.json" not in files:
        raise ValueError("Update is missing the whole-package manifest.")
    for relative, item in files.items():
        client_path(relative)
        verify(local_path(update, relative), item)
        target = local_path(package, relative)
        if target.exists() and not target.is_file():
            raise ValueError("Update target is not a file: " + relative)
    manifest = read_json(update / "package-files.json")
    profile = manifest["profile"]
    engine_manifest = read_json(package / "engine-files.json")
    identity = (metadata["required_engine_profile"], metadata["api_sha256"].lower())
    for candidate in (profile, engine_manifest["profile"]):
        if (candidate["id"], candidate["api_sha256"].lower()) != identity:
            raise ValueError("Target engine profile does not match this update.")
    if manifest.get("client_version") != metadata["version"]:
        raise ValueError("Client version does not match the package manifest.")
    if digest(local_path(package, "engine/api_v2.py")).lower() != identity[1]:
        raise ValueError("Target engine API snapshot does not match this update.")
    installed_engine = entries(engine_manifest["files"])
    combined = entries(manifest["files"])
    expected_engine = {path: item for path, item in combined.items() if path.startswith("engine/")}
    if expected_engine != installed_engine:
        raise ValueError("Update engine hashes do not match the existing engine manifest.")
    for relative, item in combined.items():
        if relative.startswith("engine/"):
            continue
        client_path(relative)
        if relative in files:
            if item != files[relative]:
                raise ValueError("Update and package manifests disagree: " + relative)
        else:
            verify(local_path(package, relative), item)
    if any(relative != "package-files.json" and relative not in combined for relative in files):
        raise ValueError("Update payload is missing from the package manifest.")
    check_processes(package)
    return metadata, files


def replace_file(source, target, expected):
    target.parent.mkdir(parents=True, exist_ok=True)
    temporary = target.with_name(target.name + "." + uuid.uuid4().hex + ".partial")
    try:
        shutil.copy2(source, temporary)
        verify(temporary, expected)
        os.replace(temporary, target)
    finally:
        if temporary.exists():
            temporary.unlink()


def restore_backup(backup, package, force=False):
    metadata = read_json(backup / "backup.json")
    if metadata.get("schema") != 1 or Path(metadata["package"]).resolve() != package:
        raise ValueError("Backup belongs to a different package.")
    if backup == package or backup.is_relative_to(package) or package.is_relative_to(backup):
        raise ValueError("Backup and package directories must be independent.")
    items = metadata["files"]
    if len({item["path"].casefold() for item in items}) != len(items):
        raise ValueError("Duplicate backup entries.")
    for item in items:
        relative = item["path"]
        client_path(relative)
        target = local_path(package, relative)
        if item["previous"] is not None:
            verify(local_path(backup / "files", relative), item["previous"])
        if not force:
            current = digest(target) if target.is_file() else None
            old = item["previous"]["sha256"] if item["previous"] is not None else None
            if current not in {old, item["updated"]["sha256"]}:
                raise ValueError("File changed since this update; rollback refused: " + relative)
    check_processes(package)
    for item in reversed(items):
        relative = item["path"]
        target = local_path(package, relative)
        if item["previous"] is None:
            if target.exists():
                target.unlink()
        else:
            replace_file(local_path(backup / "files", relative), target, item["previous"])
    metadata["status"] = "rolled_back"
    (backup / "backup.json").write_text(json.dumps(metadata, indent=2), encoding="utf-8")


def apply_update(update, package, backup):
    metadata, files = check_update(update, package)
    if backup.exists():
        raise ValueError("Choose a new backup directory; existing backups are never overwritten.")
    for root in (package, update):
        if backup.is_relative_to(root) or root.is_relative_to(backup):
            raise ValueError("Backup must be independent of the package and update directories.")
    backup.mkdir(parents=True)
    previous = []
    for relative, item in files.items():
        target = local_path(package, relative)
        old = None
        if target.exists():
            old = {"path": relative, "bytes": target.stat().st_size, "sha256": digest(target)}
            saved = backup / "files" / relative
            saved.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(target, saved)
            verify(saved, old)
        previous.append({"path": relative, "previous": old, "updated": item})
    recovery = {"schema": 1, "package": str(package), "version": metadata["version"], "status": "prepared", "files": previous}
    recovery_path = backup / "backup.json"
    recovery_path.write_text(json.dumps(recovery, indent=2), encoding="utf-8")
    try:
        # Install the manifest last, after all payload files have been replaced.
        for relative in sorted(files, key=lambda name: name == "package-files.json"):
            replace_file(local_path(update, relative), local_path(package, relative), files[relative])
        recovery["status"] = "applied"
        recovery_path.write_text(json.dumps(recovery, indent=2), encoding="utf-8")
    except BaseException:
        restore_backup(backup, package, force=True)
        raise
    return metadata


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("update", type=Path, nargs="?")
    parser.add_argument("package", type=Path, nargs="?")
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--backup", type=Path)
    parser.add_argument("--rollback", type=Path)
    args = parser.parse_args()
    try:
        if args.rollback:
            package = args.package or args.update
            if package is None or args.check or args.backup or (args.package and args.update):
                parser.error("Rollback requires only --rollback <backup> <package>.")
            restore_backup(args.rollback.resolve(), package.resolve())
            print(json.dumps({"rolled_back": True, "package": str(package.resolve())}))
            return
        if args.update is None or args.package is None or (not args.check and args.backup is None) or (args.check and args.backup):
            parser.error("Use <update> <package> --check, or <update> <package> --backup <new-directory>.")
        update, package = args.update.resolve(), args.package.resolve()
        if args.check:
            metadata, files = check_update(update, package)
            print(json.dumps({"checked": True, "version": metadata["version"], "files": len(files)}))
        else:
            metadata = apply_update(update, package, args.backup.resolve())
            print(json.dumps({"applied": True, "version": metadata["version"], "backup": str(args.backup.resolve())}))
    except (ValueError, KeyError, OSError) as exception:
        raise SystemExit(str(exception)) from exception


if __name__ == "__main__":
    main()

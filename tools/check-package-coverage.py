import argparse
import importlib.util
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("source", type=Path)
parser.add_argument("package", type=Path)
parser.add_argument("profile", type=Path)
args = parser.parse_args()
spec = importlib.util.spec_from_file_location("resource_selector", Path(__file__).with_name("package-resources.py"))
selector = importlib.util.module_from_spec(spec)
spec.loader.exec_module(selector)
profile = json.loads(args.profile.read_text(encoding="utf-8"))
expected = {relative for _, relative in selector.selected_files(args.source.resolve(), profile)}
engine = args.package.resolve() / "engine"
actual = {path.relative_to(engine).as_posix() for path in engine.rglob("*") if path.is_file()}
missing, unexpected = sorted(expected - actual), sorted(actual - expected)
print(json.dumps({"expected": len(expected), "actual": len(actual), "missing": missing, "unexpected": unexpected}, ensure_ascii=True))
if missing or unexpected:
    raise SystemExit(1)

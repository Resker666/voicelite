"""Development-only real API smoke test. Run with the existing engine Python."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import socket
import subprocess
import time
import urllib.request
import wave

import psutil


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--engine", required=True, type=Path)
    parser.add_argument("--reference", required=True, type=Path)
    parser.add_argument("--runner", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--clean-env", action="store_true")
    args = parser.parse_args()
    engine = args.engine.resolve()
    reference = args.reference.resolve()
    out = (args.output or (Path(__file__).resolve().parents[1] / ".artifacts" / "smoke")).resolve()
    out.mkdir(parents=True, exist_ok=True)
    with socket.socket() as reservation:
        reservation.bind(("127.0.0.1", 0))
        port = reservation.getsockname()[1]
    config = {"custom": {
        "version": "v2ProPlus", "device": "cuda", "is_half": True,
        "bert_base_path": str(engine / "GPT_SoVITS/pretrained_models/chinese-roberta-wwm-ext-large"),
        "cnhuhbert_base_path": str(engine / "GPT_SoVITS/pretrained_models/chinese-hubert-base"),
        "t2s_weights_path": str(engine / "GPT_SoVITS/pretrained_models/s1v3.ckpt"),
        "vits_weights_path": str(engine / "GPT_SoVITS/pretrained_models/v2Pro/s2Gv2ProPlus.pth"),
    }}
    config_path = out / "smoke-config.yaml"
    config_path.write_text(json.dumps(config, ensure_ascii=False), encoding="utf-8")
    environment = dict(os.environ, PYTHONDONTWRITEBYTECODE="1", PYTHONUNBUFFERED="1",
                       HF_HUB_OFFLINE="1", TRANSFORMERS_OFFLINE="1", HF_HUB_DISABLE_TELEMETRY="1", G2PW="0")
    environment["VOICELITE_DIAGNOSTIC_STACKS"] = "1"
    if args.clean_env:
        environment.update(PATH=str(engine / "runtime") + os.pathsep + str(Path(os.environ["SystemRoot"]) / "System32"),
                           HF_HOME=str(out/"empty-hf"), TORCH_HOME=str(out/"empty-torch"), XDG_CACHE_HOME=str(out/"empty-xdg"))
    command = [str(engine / "runtime/python.exe"), "-B", str(engine / "api_v2.py"),
               "-a", "127.0.0.1", "-p", str(port), "-c", str(config_path)]
    if args.runner:
        command.insert(2, str(args.runner.resolve()))
    started = time.monotonic()
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    report = {"command": command, "model": "v2ProPlus", "reference_origin": "Windows installed TTS (SAPI)", "result": "failed"}
    process = None
    try:
        with (out / "engine.stdout.log").open("w", encoding="utf-8") as stdout, (out / "engine.stderr.log").open("w", encoding="utf-8") as stderr:
            process = subprocess.Popen(command, cwd=engine, env=environment, stdout=stdout, stderr=stderr)
            while time.monotonic() - started < 180:
                if process.poll() is not None:
                    raise RuntimeError(f"engine exited: {process.returncode}; see engine.stderr.log")
                owned = any(c.status == psutil.CONN_LISTEN and c.laddr.ip == "127.0.0.1" and c.laddr.port == port
                            for c in psutil.Process(process.pid).net_connections(kind="tcp"))
                if owned:
                    with opener.open(f"http://127.0.0.1:{port}/openapi.json", timeout=3) as response:
                        schema = json.load(response)
                    assert "post" in schema["paths"]["/tts"]
                    break
                time.sleep(.3)
            else:
                raise TimeoutError("engine startup exceeded 180s")
            report["startup_seconds"] = round(time.monotonic() - started, 3)
            prompt = json.loads(reference.with_suffix(".json").read_text(encoding="utf-8-sig"))
            request = {"text": "你好，这是 VoiceLite 的真实离线语音合成测试。", "text_lang": "zh",
                       "ref_audio_path": str(reference), "prompt_text": prompt["text"], "prompt_lang": prompt["language"],
                       "media_type": "wav", "streaming_mode": False, "batch_size": 1, "text_split_method": "cut5"}
            before = time.monotonic()
            data = opener.open(urllib.request.Request(f"http://127.0.0.1:{port}/tts", data=json.dumps(request).encode(),
                                                       headers={"Content-Type": "application/json"}), timeout=180).read()
            report["inference_seconds"] = round(time.monotonic() - before, 3)
            result = out / "real-output.wav"
            result.write_bytes(data)
            with wave.open(str(result)) as wav:
                report.update(sample_rate=wav.getframerate(), channels=wav.getnchannels(), frames=wav.getnframes(),
                              duration_seconds=wav.getnframes()/wav.getframerate(), sample_width=wav.getsampwidth())
                assert wav.getnframes() > 0 and wav.getsampwidth() == 2
            report.update(result="passed", sha256=hashlib.sha256(data).hexdigest(), bytes=len(data))
    except Exception as exc:
        report["error"] = f"{type(exc).__name__}: {exc}"
        raise
    finally:
        if process and process.poll() is None:
            for child in psutil.Process(process.pid).children(recursive=True):
                try:
                    child.kill()
                except psutil.NoSuchProcess:
                    pass
            process.kill()
            process.wait(timeout=15)
        report["process_exited"] = process is None or process.poll() is not None
        (out / "real-report.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
        print(json.dumps(report, ensure_ascii=False))


if __name__ == "__main__":
    main()

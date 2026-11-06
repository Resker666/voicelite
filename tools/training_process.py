"""Windows process-tree ownership for explicit, local training commands."""
import ctypes
from ctypes import wintypes
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import subprocess
import time


class BasicLimits(ctypes.Structure):
    _fields_ = [("process_time", ctypes.c_int64), ("job_time", ctypes.c_int64), ("flags", wintypes.DWORD),
                ("min_working", ctypes.c_size_t), ("max_working", ctypes.c_size_t), ("active", wintypes.DWORD),
                ("affinity", ctypes.c_size_t), ("priority", wintypes.DWORD), ("scheduling", wintypes.DWORD)]


class IoCounters(ctypes.Structure):
    _fields_ = [(name, ctypes.c_uint64) for name in ("read_ops", "write_ops", "other_ops", "read_bytes", "write_bytes", "other_bytes")]


class ExtendedLimits(ctypes.Structure):
    _fields_ = [("basic", BasicLimits), ("io", IoCounters), ("process_memory", ctypes.c_size_t),
                ("job_memory", ctypes.c_size_t), ("peak_process", ctypes.c_size_t), ("peak_job", ctypes.c_size_t)]


def run_owned(command, cwd, log, timeout):
    if os.name != "nt":
        raise RuntimeError("This training adapter supports Windows only")
    api = ctypes.WinDLL("kernel32", use_last_error=True)
    api.CreateJobObjectW.restype = wintypes.HANDLE
    api.SetInformationJobObject.argtypes = [wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD]
    api.AssignProcessToJobObject.argtypes = [wintypes.HANDLE, wintypes.HANDLE]
    api.TerminateJobObject.argtypes = [wintypes.HANDLE, wintypes.UINT]
    api.CloseHandle.argtypes = [wintypes.HANDLE]
    native = ctypes.WinDLL("ntdll")
    native.NtResumeProcess.argtypes = [wintypes.HANDLE]; native.NtResumeProcess.restype = ctypes.c_long
    job = api.CreateJobObjectW(None, None)
    if not job:
        raise ctypes.WinError(ctypes.get_last_error())
    limits = ExtendedLimits(); limits.basic.flags = 0x2000  # KILL_ON_JOB_CLOSE
    process = None; started = time.monotonic()
    record = {"utc": datetime.now(timezone.utc).isoformat(), "command": list(map(str, command)), "timeout": timeout, "log": str(log)}
    try:
        if not api.SetInformationJobObject(job, 9, ctypes.byref(limits), ctypes.sizeof(limits)):
            raise ctypes.WinError(ctypes.get_last_error())
        with Path(log).open("x", encoding="utf-8") as output:
            environment = dict(os.environ, PYTHONDONTWRITEBYTECODE="1", PYTHONUNBUFFERED="1")
            process = subprocess.Popen(list(map(str, command)), cwd=cwd, env=environment, stdin=subprocess.DEVNULL,
                                       stdout=output, stderr=subprocess.STDOUT, creationflags=0x4 | subprocess.CREATE_NO_WINDOW)
            record["pid"] = process.pid
            if not api.AssignProcessToJobObject(job, wintypes.HANDLE(process._handle)):
                raise ctypes.WinError(ctypes.get_last_error())
            if native.NtResumeProcess(wintypes.HANDLE(process._handle)) != 0:
                raise RuntimeError("Could not resume the owned process")
            while process.poll() is None:
                if time.monotonic() - started > timeout:
                    raise TimeoutError("Training stage exceeded its deadline")
                if Path(log).stat().st_size > 32 * 1024 * 1024:
                    raise RuntimeError("Training stage log exceeded 32 MiB")
                try: process.wait(timeout=1)
                except subprocess.TimeoutExpired: pass
            record["exit_code"] = process.returncode
            if process.returncode != 0:
                raise RuntimeError("Stage failed; inspect " + str(log))
    except BaseException as error:
        record["error"] = type(error).__name__
        raise
    finally:
        if process is not None and process.poll() is None:
            api.TerminateJobObject(job, 1)
            try: process.wait(timeout=10)
            except subprocess.TimeoutExpired: process.kill(); process.wait(timeout=5)
        api.CloseHandle(job)
        record["seconds"] = time.monotonic() - started
        with (Path(log).parent.parent / "commands.jsonl").open("a", encoding="utf-8") as file:
            file.write(json.dumps(record, ensure_ascii=False) + "\n")

"""Real Windows Job tests without loading any inference or training model."""
import ctypes
from ctypes import wintypes
import importlib.util
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest


@unittest.skipUnless(os.name == "nt", "Windows process ownership")
class TrainingProcessTests(unittest.TestCase):
    def setUp(self):
        spec = importlib.util.spec_from_file_location("training_process", Path(__file__).with_name("training_process.py"))
        self.worker = importlib.util.module_from_spec(spec); spec.loader.exec_module(self.worker)
        self.temporary = tempfile.TemporaryDirectory(prefix="VoiceLiteTrainingJob-"); self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name); (self.root / "logs").mkdir()

    def exited(self, process_id):
        api = ctypes.WinDLL("kernel32", use_last_error=True)
        api.OpenProcess.restype = wintypes.HANDLE
        api.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
        api.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
        api.CloseHandle.argtypes = [wintypes.HANDLE]
        handle = api.OpenProcess(0x100000, False, process_id)
        if not handle: return True
        try: return api.WaitForSingleObject(handle, 2000) == 0
        finally: api.CloseHandle(handle)

    def test_successful_root_cannot_leave_a_sleeping_child(self):
        pid = self.root / "child.pid"
        code = ("import subprocess,sys,time;from pathlib import Path;"
                "child=subprocess.Popen([sys.executable,'-c','import time;time.sleep(60)'],"
                "stdin=subprocess.DEVNULL,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,creationflags=subprocess.CREATE_NO_WINDOW);"
                f"Path({str(pid)!r}).write_text(str(child.pid));time.sleep(0.2)")
        self.worker.run_owned([sys.executable, "-B", "-c", code], self.root, self.root / "logs/success.log", 10)
        self.assertTrue(self.exited(int(pid.read_text())), "Job closure left the test child running")
        record = json.loads((self.root / "commands.jsonl").read_text(encoding="utf-8"))
        self.assertEqual(0, record["exit_code"])

    def test_timeout_stops_the_owned_root_and_records_failure(self):
        with self.assertRaises(TimeoutError):
            self.worker.run_owned([sys.executable, "-B", "-c", "import time;time.sleep(60)"], self.root, self.root / "logs/timeout.log", .2)
        record = json.loads((self.root / "commands.jsonl").read_text(encoding="utf-8"))
        self.assertEqual("TimeoutError", record["error"])
        self.assertTrue(self.exited(record["pid"]))


if __name__ == "__main__": unittest.main()

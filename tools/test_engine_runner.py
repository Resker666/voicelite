import subprocess
import sys
from pathlib import Path
import unittest
import os
import select
import socket


class OfflineNetworkTests(unittest.TestCase):
    def run_code(self, code):
        return subprocess.run([sys.executable, "-B", "-c", "import sys; sys.path.insert(0, " + repr(str(Path(__file__).parent)) + "); from engine_runner import block_external_network; block_external_network(); " + code], capture_output=True, text=True)

    def test_external_resolution_is_rejected_before_network(self):
        result = self.run_code("import socket; socket.getaddrinfo('example.com',443)")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("PermissionError", result.stderr)

    def test_loopback_listener_is_allowed(self):
        result = self.run_code("import socket; s=socket.socket(); s.bind(('127.0.0.1',0)); s.listen(); print(s.getsockname()[0]); s.close()")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("127.0.0.1", result.stdout.strip())

    def test_inherited_loopback_proxy_cannot_reach_external_model_url(self):
        with socket.socket() as trap:
            trap.bind(("127.0.0.1", 0))
            trap.listen()
            code = "import sys; sys.path.insert(0," + repr(str(Path(__file__).parent)) + "); from engine_runner import block_external_network; block_external_network(); import requests; requests.get('https://example.com/model', timeout=.25)"
            environment = dict(os.environ, HTTPS_PROXY=f"http://127.0.0.1:{trap.getsockname()[1]}", https_proxy=f"http://127.0.0.1:{trap.getsockname()[1]}")
            result = subprocess.run([sys.executable, "-B", "-c", code], env=environment, capture_output=True, text=True)
            self.assertNotEqual(0, result.returncode)
            self.assertFalse(select.select([trap], [], [], 0)[0], "External request reached inherited loopback proxy")


if __name__ == "__main__":
    unittest.main()

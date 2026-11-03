"""VoiceLite process wrapper; delegates inference to the unchanged upstream API."""
import sys
from pathlib import Path
import runpy
import os
import socket
import ipaddress


def block_external_network():
    for name in list(os.environ):
        if name.lower() in {"http_proxy", "https_proxy", "all_proxy", "ftp_proxy"}:
            os.environ.pop(name, None)
    # Also bypass Windows registry proxies; a loopback proxy must not tunnel outbound HTTP.
    os.environ["NO_PROXY"] = "*"
    os.environ["no_proxy"] = "*"
    def allowed(host):
        if isinstance(host, bytes):
            host = host.decode("ascii")
        if host == "localhost":
            return True
        try:
            return ipaddress.ip_address(host).is_loopback
        except (ValueError, TypeError):
            return False

    original_lookup = socket.getaddrinfo

    def lookup(host, *args, **kwargs):
        if not allowed(host):
            raise PermissionError("VoiceLite offline engine blocks external DNS/network access")
        return original_lookup(host, *args, **kwargs)

    socket.getaddrinfo = lookup
    for method in ("connect", "connect_ex", "bind"):
        original = getattr(socket.socket, method)

        def checked(self, address, *args, _original=original, **kwargs):
            if self.family in (socket.AF_INET, socket.AF_INET6) and (not isinstance(address, tuple) or not allowed(address[0])):
                raise PermissionError("VoiceLite offline engine permits loopback sockets only")
            return _original(self, address, *args, **kwargs)

        setattr(socket.socket, method, checked)


def main():
    api = Path(sys.argv.pop(1)).resolve()
    if api.name != "api_v2.py" or not api.is_file():
        raise ValueError("Expected an existing api_v2.py")
    os.chdir(api.parent)
    os.environ["bert_path"] = str(api.parent / "GPT_SoVITS/pretrained_models/chinese-roberta-wwm-ext-large")
    sys.path.insert(0, str(api.parent))
    if os.environ.get("VOICELITE_DIAGNOSTIC_STACKS") == "1":
        import faulthandler
        faulthandler.dump_traceback_later(40, repeat=True)
    block_external_network()
    sys.argv[0] = str(api)
    runpy.run_path(str(api), run_name="__main__")


if __name__ == "__main__":
    main()

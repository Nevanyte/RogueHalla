"""Serve the staging Caddyfile through a real Unix socket with a loopback Source stub."""

import base64
import gzip
import hashlib
import http.client
import json
import os
from pathlib import Path
import shutil
import socket
import subprocess
import tempfile
from threading import Thread
import time
import unittest
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


HERE = Path(__file__).resolve().parent
HOST = "alpha-stage.example.test"
ORIGIN = f"https://{HOST}"
VISITOR = "2001:db8::24"
KEY = "dGhlIHNhbXBsZSBub25jZQ=="


class Source(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, *args):
        pass

    def reply(self):
        if self.headers.get("Upgrade", "").lower() == "websocket":
            accept = base64.b64encode(
                hashlib.sha1((self.headers["Sec-WebSocket-Key"] +
                              "258EAFA5-E914-47DA-95CA-C5AB0DC85B11").encode()).digest()
            ).decode()
            self.send_response(101)
            self.send_header("Connection", "Upgrade")
            self.send_header("Upgrade", "websocket")
            self.send_header("Sec-WebSocket-Accept", accept)
            self.send_header("X-Observed-Visitor", self.headers["X-Forwarded-For"])
            self.send_header("X-Observed-Origin", self.headers["Origin"])
            self.send_header("X-Observed-Path", self.path)
            self.end_headers()
            self.wfile.write(b"\x82\x03rh!")
            self.close_connection = True
            return

        size = int(self.headers.get("Content-Length", "0"))
        payload = json.dumps({
            "method": self.command,
            "path": self.path,
            "body": self.rfile.read(size).decode(),
            "forwarded": self.headers.get_all("X-Forwarded-For"),
            "forwarded_proto": self.headers.get_all("X-Forwarded-Proto"),
            "forwarded_host": self.headers.get_all("X-Forwarded-Host"),
            "other_ips": [self.headers.get(k) for k in
                          ("Forwarded", "X-Real-IP", "CF-Connecting-IP")],
            "origin": self.headers.get("Origin"),
            "token": self.headers.get("X-Profile-Token"),
            "passcode": self.headers.get("X-Playtest-Passcode"),
        }).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(payload)))
        self.send_header("Cache-Control", "public, max-age=600")
        self.end_headers()
        self.wfile.write(payload)

    do_GET = do_POST = do_OPTIONS = reply


class TunnelConnection(http.client.HTTPConnection):
    def __init__(self, path):
        super().__init__(HOST, timeout=4)
        self.path = path

    def connect(self):
        self.sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        self.sock.settimeout(self.timeout)
        self.sock.connect(self.path)


class RouteContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        config = HERE / "Caddyfile"
        if not config.is_file():
            raise AssertionError("Missing staging Caddyfile")
        binary = os.environ.get("CADDY_BIN") or shutil.which("caddy")
        if not binary:
            raise AssertionError("Set CADDY_BIN to the Caddy 2.11.2 binary")

        temporary = tempfile.TemporaryDirectory(dir="/tmp", prefix="rh-alpha-")
        cls.addClassCleanup(temporary.cleanup)
        temp = Path(temporary.name)
        cls.endpoint = str(temp / "edge.sock")
        web = temp / "web"
        files = {
            "index.html": b"<h1>RogueHalla alpha</h1>",
            "Build/Web.wasm": b"\0asm\1\0\0\0",
            "Build/Web.data": b"Unity Data",
            "StreamingAssets/Audio/ground.mp3": b"audio",
            "Licenses/index.html": b"notices",
            "preview.txt": b"release asset",
            "probe": b"must remain private",
            "control/info": b"must remain private",
            "training/info": b"must remain private",
        }
        files["Build/Web.wasm.gz"] = gzip.compress(files["Build/Web.wasm"])
        for name, content in files.items():
            dest = web / name
            dest.parent.mkdir(exist_ok=True, parents=True)
            dest.write_bytes(content)

        source = ThreadingHTTPServer(("127.0.0.1", 0), Source)
        cls.addClassCleanup(source.server_close)
        thread = Thread(target=source.serve_forever, daemon=True)
        thread.start()
        cls.addClassCleanup(thread.join, 5)
        cls.addClassCleanup(source.shutdown)
        env = dict(os.environ, RH_ALPHA_HOST=HOST, RH_ALPHA_INGRESS_SOCKET=cls.endpoint,
                   RH_ALPHA_SOURCE=f"127.0.0.1:{source.server_port}", RH_ALPHA_WEB_ROOT=str(web))
        log = (temp / "caddy.log").open("w+")
        cls.addClassCleanup(log.close)
        proxy = subprocess.Popen(
            [binary, "run", "--config", str(config), "--adapter", "caddyfile"],
            stdout=log, stderr=subprocess.STDOUT, env=env,
        )

        def stop_proxy():
            if proxy.poll() is None:
                proxy.terminate()
                try:
                    proxy.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    proxy.kill()
                    proxy.wait(timeout=5)

        cls.addClassCleanup(stop_proxy)
        for _ in range(100):
            if proxy.poll() is not None:
                break
            try:
                with socket.socket(socket.AF_UNIX, socket.SOCK_STREAM) as client:
                    client.connect(cls.endpoint)
                return
            except OSError:
                time.sleep(0.05)
        log.seek(0)
        raise AssertionError(f"Caddy did not open its local ingress socket:\n{log.read()}")

    def request(self, path, method="GET", body=None, **headers):
        connection = TunnelConnection(self.endpoint)
        headers = {"Host": HOST, "CF-Connecting-IP": VISITOR, **headers}
        headers = {name: value for name, value in headers.items() if value is not None}
        try:
            connection.request(method, path, body, headers=headers)
            response = connection.getresponse()
            return response.status, dict(response.getheaders()), response.read()
        finally:
            connection.close()

    def test_source_paths_methods_and_cache(self):
        for path, method, body in [
            ("/ws", "GET", None), ("/info", "GET", None), ("/ping?t=1", "GET", None),
            ("/healthz", "GET", None), ("/readyz", "GET", None), ("/api", "GET", None),
            ("/api/profile/Visitor", "GET", None),
            ("/api/profile/Visitor/equip?k=1", "POST", '{"slot":1}'),
            ("/api/profile/Visitor/equip", "OPTIONS", None),
        ]:
            with self.subTest(path=path, method=method):
                status, headers, data = self.request(
                    path, method, body, Origin=ORIGIN,
                    **{"X-Profile-Token": "oracle-token", "X-Playtest-Passcode": "oracle-passcode"}
                )
                self.assertEqual(status, 200)
                self.assertEqual(headers["Cache-Control"], "no-store")
                seen = json.loads(data)
                self.assertEqual((seen["method"], seen["path"], seen["body"]), (method, path, body or ""))
                self.assertEqual((seen["origin"], seen["token"], seen["passcode"]),
                                 (ORIGIN, "oracle-token", "oracle-passcode"))

    def test_uses_only_canonical_tunnel_visitor_ip(self):
        for address, canonical in [("198.51.100.24", "198.51.100.24"),
                                   ("2001:0db8:0:0::0024", VISITOR)]:
            with self.subTest(address=address):
                status, _, body = self.request("/info", **{
                    "CF-Connecting-IP": address,
                    "Forwarded": 'for="192.0.2.1"',
                    "X-Real-IP": "192.0.2.2",
                    "X-Forwarded-For": "192.0.2.3, 192.0.2.4",
                    "X-Forwarded-Proto": "http",
                    "X-Forwarded-Host": "spoof.invalid",
                })
                self.assertEqual(status, 200)
                seen = json.loads(body)
                self.assertEqual(seen["forwarded"], [canonical])
                self.assertEqual(seen["forwarded_proto"], ["https"])
                self.assertEqual(seen["forwarded_host"], [HOST])
                self.assertEqual(seen["other_ips"], [None, None, None])

    def test_refuses_absent_or_malformed_visitor_identity(self):
        for address in [None, "not-an-ip", "198.51.100.24, 192.0.2.99", "2001:db8::24%eth0"]:
            with self.subTest(address=address):
                status, _, _ = self.request("/api/profile/Visitor", **{
                    "CF-Connecting-IP": address, "X-Forwarded-For": "198.51.100.24"
                })
                self.assertEqual(status, 400)

    def test_private_paths_and_unexpected_host(self):
        for path in ("/control", "/control/info", "/probe", "/probe/x", "/training",
                     "/training/ws", "/training/info", "/training/api/health", "/api-other", "/ws/"):
            with self.subTest(path=path):
                status, _, _ = self.request(path)
                self.assertEqual(status, 404)
        status, _, _ = self.request("/", Host="other.example.test")
        self.assertIn(status, (404, 421))

    def test_static_mime_precompressed_and_no_fallback(self):
        for path, mime in [("/", "text/html"), ("/index.html", "text/html"),
                           ("/Build/Web.data", "application/octet-stream"),
                           ("/StreamingAssets/Audio/ground.mp3", "audio/mpeg"),
                           ("/Licenses/", "text/html"), ("/preview.txt", "text/plain")]:
            with self.subTest(path=path):
                status, headers, _ = self.request(path)
                self.assertEqual(status, 200)
                self.assertEqual(headers["Cache-Control"], "no-cache")
                self.assertTrue(headers["Content-Type"].startswith(mime), headers)
        status, headers, body = self.request("/Build/Web.wasm", **{"Accept-Encoding": "gzip"})
        self.assertEqual(status, 200)
        self.assertEqual(headers["Content-Type"], "application/wasm")
        self.assertEqual(headers["Content-Encoding"], "gzip")
        self.assertEqual(gzip.decompress(body), b"\0asm\1\0\0\0")
        self.assertEqual(self.request("/Build/missing.wasm")[0], 404)

    def test_websocket_upgrade_and_frame(self):
        req = (
            f"GET /ws?proof=1 HTTP/1.1\r\nHost: {HOST}\r\nOrigin: {ORIGIN}\r\n"
            f"CF-Connecting-IP: {VISITOR}\r\nConnection: Upgrade\r\nUpgrade: websocket\r\n"
            f"Sec-WebSocket-Key: {KEY}\r\nSec-WebSocket-Version: 13\r\n\r\n"
        )
        with socket.socket(socket.AF_UNIX, socket.SOCK_STREAM) as client:
            client.settimeout(4)
            client.connect(self.endpoint)
            client.sendall(req.encode())
            with client.makefile("rb") as wire:
                self.assertIn(b"101 Switching Protocols", wire.readline())
                headers = http.client.parse_headers(wire)
                self.assertEqual(headers["Sec-WebSocket-Accept"], "s3pPLMBiTxaQ9kYGzzhZRbK+xOo=")
                self.assertEqual(headers["X-Observed-Visitor"], VISITOR)
                self.assertEqual(headers["X-Observed-Origin"], ORIGIN)
                self.assertEqual(headers["X-Observed-Path"], "/ws?proof=1")
                self.assertEqual(wire.read(5), b"\x82\x03rh!")

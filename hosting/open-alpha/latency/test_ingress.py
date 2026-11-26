"""Exercise the private TLS listener as an ALB peer with the existing Source stub."""

import gzip
import http.client
import json
import os
from pathlib import Path
import shutil
import socket
import ssl
import subprocess
import sys
import tempfile
from threading import Thread
import time
import unittest
from http.server import ThreadingHTTPServer

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))
from test_routes import KEY, Source

STAGING = "latency-staging.roguehalla.com"
LIVE = "game.roguehalla.com"
PLAY = "play.roguehalla.com"
PEER = "127.0.0.1"
VISITOR = "198.51.100.24"


class IngressContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        binary = os.environ.get("CADDY_BIN") or shutil.which("caddy")
        if not binary:
            raise AssertionError("Set CADDY_BIN to the Caddy 2.11.2 binary")
        scratch = tempfile.TemporaryDirectory(prefix="rh-alpha-ingress-")
        cls.addClassCleanup(scratch.cleanup)
        cls.root = Path(scratch.name)
        cls.listeners = {
            STAGING: cls.start_origin(binary, STAGING, "staging"),
            LIVE: cls.start_origin(binary, LIVE, "live"),
            "denied.example.test": cls.start_origin(
                binary, "denied.example.test", "denied", trusted="127.0.0.3/32"
            ),
        }

    @classmethod
    def start_origin(cls, binary, host, environment, trusted=f"{PEER}/32 127.0.0.3/32"):
        class Origin(Source):
            def end_headers(self):
                self.send_header("X-Origin-Environment", environment)
                super().end_headers()

        web = cls.root / environment
        (web / "Build").mkdir(parents=True)
        (web / "index.html").write_text(environment)
        (web / "alpha-gameplay-data.html").write_text("capture notice")
        (web / "Build/Web.wasm").write_bytes(b"\0asm\1\0\0\0")
        (web / "Build/Web.wasm.gz").write_bytes(gzip.compress(b"\0asm\1\0\0\0"))
        # Brotli encoding of the same eight-byte Wasm header.
        (web / "Build/Web.wasm.br").write_bytes(bytes.fromhex("8b03800061736d0100000003"))
        (web / "Build/Web.data").write_bytes(b"Unity Data")
        (web / "secret.txt").write_text("not a release path")
        for filename in ("control", "probe", "training", "__rh_alb_health"):
            (web / filename).write_text("private fixture")

        certificate = cls.root / f"{environment}.pem"
        key = cls.root / f"{environment}.key"
        subprocess.run(
            ["openssl", "req", "-new", "-newkey", "rsa:2048", "-nodes", "-x509",
             "-days", "2", "-subj", f"/CN={host}", "-addext", f"subjectAltName=DNS:{host}",
             "-keyout", str(key), "-out", str(certificate)],
            check=True, capture_output=True,
        )
        context = ssl.create_default_context(cafile=str(certificate))
        context.check_hostname = False  # ALB-to-target TLS can arrive without SNI.
        source = ThreadingHTTPServer((PEER, 0), Origin)
        cls.addClassCleanup(source.server_close)
        thread = Thread(target=source.serve_forever, daemon=True)
        thread.start()
        cls.addClassCleanup(thread.join, 5)
        cls.addClassCleanup(source.shutdown)
        with socket.socket() as reservation:
            reservation.bind((PEER, 0))
            port = reservation.getsockname()[1]
        env = dict(
            os.environ, RH_ALB_HOST=host, RH_ALB_TARGET_IP=PEER, RH_ALB_TARGET_PORT=str(port),
            RH_ALB_SUBNETS=trusted, RH_ALB_CERT_FILE=str(certificate), RH_ALB_KEY_FILE=str(key),
            RH_ALB_SOURCE=f"{PEER}:{source.server_port}", RH_ALB_WEB_ROOT=str(web),
            XDG_DATA_HOME=str(web / "caddy-state"),
        )
        log = (cls.root / f"{environment}.log").open("w+")
        cls.addClassCleanup(log.close)
        proc = subprocess.Popen(
            [binary, "run", "--config", str(HERE / "Caddyfile"), "--adapter", "caddyfile"],
            stdout=log, stderr=subprocess.STDOUT, env=env,
        )

        def stop():
            if proc.poll() is None:
                proc.terminate()
                try:
                    proc.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    proc.kill()
                    proc.wait()

        cls.addClassCleanup(stop)
        deadline = time.monotonic() + 5
        while proc.poll() is None and time.monotonic() < deadline:
            try:
                with socket.create_connection((PEER, port), timeout=0.25):
                    return port, context
            except ConnectionError:
                time.sleep(0.02)
        log.seek(0)
        raise AssertionError(log.read())

    def request(self, listener, path, headers=None, method="GET"):
        port, context = self.listeners[listener]
        conn = http.client.HTTPSConnection(PEER, port, timeout=3, context=context)
        request_headers = {"Host": listener, "X-Forwarded-For": VISITOR}
        if headers is not None:
            request_headers.update(headers)
        try:
            conn.request(method, path, headers=request_headers)
            response = conn.getresponse()
            return response.status, dict(response.getheaders()), response.read()
        finally:
            conn.close()

    def test_health_checks_use_private_host_no_sni_and_no_xff(self):
        for host in (STAGING, LIVE):
            port, context = self.listeners[host]
            with socket.create_connection((PEER, port), timeout=3) as sock:
                with context.wrap_socket(sock) as tls:
                    tls.sendall(
                        f"GET /__rh_alb_health HTTP/1.1\r\nHost: {PEER}:{port}\r\n"
                        "User-Agent: ELB-HealthChecker/2.0\r\nConnection: close\r\n\r\n".encode()
                    )
                    response = http.client.HTTPResponse(tls)
                    response.begin()
                    self.assertEqual(response.status, 200)
                    self.assertEqual(json.loads(response.read())["path"], "/readyz")
            self.assertEqual(self.request(host, "/__rh_alb_health")[0], 404)

    def test_only_target_host_and_public_routes_reach_origin(self):
        self.assertEqual(self.request(STAGING, "/")[2], b"staging")
        self.assertEqual(self.request(STAGING, "/alpha-gameplay-data.html")[2], b"capture notice")
        for host, wrong, environment in ((STAGING, LIVE, "staging"), (LIVE, STAGING, "live")):
            self.assertEqual(self.request(host, "/info")[1]["X-Origin-Environment"], environment)
            self.assertEqual(self.request(host, "/info", {"Host": wrong})[0], 421)
            self.assertEqual(self.request(host, "/", method="POST")[0], 404)
            for path in ("/control", "/control/health", "/probe/snapshot", "/training/ws",
                         "/__rh_alb_health", "/secret.txt"):
                self.assertEqual(self.request(host, path)[0], 404)
        self.assertEqual(self.request("denied.example.test", "/info")[0], 403)
        for path in ("/", "/index.html", "/Build/Web.wasm", "/TemplateData/style.css",
                     "/alpha-gameplay-data.html"):
            self.assertEqual(self.request(LIVE, path)[0], 404, path)

    def test_only_literal_last_alb_address_survives(self):
        status, headers, body = self.request(STAGING, "/api/profile", {
            "X-Forwarded-For": f"192.0.2.1 , 192.0.2.2, {VISITOR}",
            "X-Forwarded-Host": "spoof.example.test",
            "X-Forwarded-Proto": "http",
            "CF-Connecting-IP": "192.0.2.3",
            "X-Real-IP": "192.0.2.4",
            "Forwarded": "for=192.0.2.5",
            "Origin": f"https://{STAGING}",
            "X-Profile-Token": "stub-token",
            "X-Playtest-Passcode": "stub-passcode",
        })
        self.assertEqual(status, 200)
        self.assertEqual(headers["Cache-Control"], "no-store")
        observed = json.loads(body)
        self.assertEqual(observed["forwarded"], [VISITOR])
        self.assertEqual(observed["forwarded_proto"], ["https"])
        self.assertEqual(observed["forwarded_host"], [STAGING])
        self.assertEqual(observed["other_ips"], [None, None, None])
        self.assertEqual(observed["origin"], f"https://{STAGING}")
        self.assertEqual(observed["token"], "stub-token")
        self.assertEqual(observed["passcode"], "stub-passcode")
        for value in ("", f"{VISITOR}, nonsense", f"{VISITOR}, 127.0.0.3", f"{VISITOR}:1234"):
            self.assertEqual(self.request(STAGING, "/info", {"X-Forwarded-For": value})[0], 400)
        port, context = self.listeners[STAGING]
        with socket.create_connection((PEER, port), timeout=3) as sock:
            with context.wrap_socket(sock) as tls:
                tls.sendall(
                    f"GET /info HTTP/1.1\r\nHost: {STAGING}\r\nX-Forwarded-For: 192.0.2.10\r\n"
                    f"X-Forwarded-For: {VISITOR}\r\nConnection: close\r\n\r\n".encode()
                )
                response = http.client.HTTPResponse(tls)
                response.begin()
                self.assertEqual(response.status, 200)
                self.assertEqual(json.loads(response.read())["forwarded"], [VISITOR])

    def test_websocket_upgrade_and_static_mime(self):
        status, headers, body = self.request(STAGING, "/Build/Web.wasm", {"Accept-Encoding": "gzip"})
        self.assertEqual(status, 200)
        self.assertEqual(headers["Content-Encoding"], "gzip")
        self.assertEqual(headers["Content-Type"], "application/wasm")
        self.assertEqual(gzip.decompress(body), b"\0asm\1\0\0\0")
        status, headers, body = self.request(STAGING, "/Build/Web.wasm", {"Accept-Encoding": "br"})
        self.assertEqual(status, 200)
        self.assertEqual(headers["Content-Encoding"], "br")
        self.assertEqual(headers["Content-Type"], "application/wasm")
        self.assertEqual(body.hex(), "8b03800061736d0100000003")
        self.assertEqual(self.request(STAGING, "/Build/Web.data")[1]["Content-Type"],
                         "application/octet-stream")
        port, context = self.listeners[LIVE]
        with socket.create_connection((PEER, port), timeout=3) as sock:
            with context.wrap_socket(sock, server_hostname=LIVE) as tls:
                tls.sendall(
                    f"GET /ws HTTP/1.1\r\nHost: {LIVE}\r\nOrigin: https://{PLAY}\r\n"
                    f"X-Forwarded-For: 192.0.2.5, {VISITOR}\r\n"
                    "Connection: Upgrade\r\nUpgrade: websocket\r\n"
                    f"Sec-WebSocket-Version: 13\r\nSec-WebSocket-Key: {KEY}\r\n\r\n".encode()
                )
                with tls.makefile("rb") as stream:
                    self.assertIn(b"101 Switching Protocols", stream.readline())
                    received = http.client.parse_headers(stream)
                    self.assertEqual(received["X-Origin-Environment"], "live")
                    self.assertEqual(received["X-Observed-Visitor"], VISITOR)
                    self.assertEqual(received["X-Observed-Origin"], f"https://{PLAY}")
                    self.assertEqual(stream.read(5), b"\x82\x03rh!")


class ProductionTemplateContract(unittest.TestCase):
    def test_production_alb_rule_is_dynamic_only_and_host_pinned(self):
        template = (HERE / "alb.yml").read_text()
        production = template.split("  ProductionRoutes:", 1)[1].split("  WebAcl:", 1)[0]
        self.assertIn("Values: [!Ref ProductionHostname]", production)
        self.assertIn("'^/(ws|info|ping|healthz|api(/.*)?)$'", production)
        self.assertNotIn("Build", production)
        self.assertNotIn("index[.]html", production)
        self.assertIn("AllowedPattern: '(|game[.]roguehalla[.]com)'", template)

    def test_static_caddy_matcher_is_staging_only(self):
        config = (HERE / "Caddyfile").read_text()
        static = config.split("\t\t@static {", 1)[1].split("\n\t\t}", 1)[0]
        self.assertIn("host latency-staging.roguehalla.com", static)


if __name__ == "__main__":
    unittest.main()

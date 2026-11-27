"""Local proofs for the guarded release, using disposable assets and fake systemd/HTTP."""
from contextlib import ExitStack
import io
import os
import subprocess
from pathlib import Path
import tarfile
import tempfile
import unittest
from unittest.mock import patch

import balance_release as op


def loader(target, mode):
    endpoint = target.backend_hostname
    connect = f"https://{endpoint} wss://{endpoint}" if mode == "split" else "'self'"
    return (
        f'<meta name="roguehalla-backend" content="{mode}"> {connect} /alpha-gameplay-data.html '
        + " ".join(f"/alpha-web/{op.NEW}/{name}" for name in ("Build", "StreamingAssets"))
    ).encode()


class Guest(op.Host):
    def __init__(self, kind, root):
        super().__init__(kind, root, os.getgid())
        self.transient = kind == "stage"
        self.drift = False
        self.program = None
        self.next_fault = None
        self.fault = None
        self.occupied = False
        self.actions = []
        for path in (self.front, self.versions, self.prior, self.drop_dir):
            path.mkdir(parents=True, exist_ok=True)
        for filename, data, mode in (
            ("rh-server", b"prior native", 0o555), ("seed1.map.json", b"fallback", 0o444),
        ):
            path = self.prior / filename
            if not path.exists():
                path.write_bytes(data)
                path.chmod(mode)
        for filename, data in (("index.html", b"old root"), ("alpha-gameplay-data.html", b"old notice")):
            path = self.front / filename
            path.write_bytes(data)
            path.chmod(0o440)
        target = self.target
        capture = self.root / "srv/rh-alpha" / target.capture
        pg_socket = self.root / "run" / target.pg_socket
        self.program = (
            str(self.prior / "rh-server"), "--runtime", "source-arena", "--bind", f"127.0.0.1:{target.public_port}",
            "--control", f"127.0.0.1:{target.control_port}", "--control-token-file",
            str(self.root / "etc/rh-alpha" / target.token_name),
            "--max-rooms", "2", "--min-players", "2", "--fill-seconds", "2", "--fill-bots", "4",
            "--bot-learning", "off", "--result-spool", str(self.root / "srv/rh-alpha" / target.spool),
            "--alpha-metrics-dir", str(capture / "metrics"), "--alpha-metrics-key-file", str(capture / "metrics.key"),
            "--alpha-replay-dir", str(capture / "replays"), "--map", str(self.prior / "seed1.map.json"), "--map-pool",
            "--database-url", f"postgresql:///{target.user}?host={pg_socket}&user={target.user}",
        )
        self.prior_argv = self.program

    @property
    def candidate(self):
        return self.program is not None and self.program[0] == str(self.release / "rh-server")

    def loaded(self):
        if self.program is not None:
            return self.program
        if self.override.exists():
            return tuple(self.override.read_text().splitlines()[-1].removeprefix("ExecStart=").split())
        return self.prior_argv

    def unit_state(self):
        return op.UnitState("inactive", "success", 0) if self.program is None else op.UnitState("active", "success", 110)

    def running(self, pid):
        assert pid == 110
        return Path(self.program[0]), self.program

    def fingerprints(self):
        return {"/fake/fragment": "foreign" if self.drift else "prior"}

    def transient_kind(self):
        return self.transient

    def transient_fingerprint(self):
        return {"Type": "exec", "User": "rh_alpha", **self.fingerprints()}

    def request(self, control, url, post=False):
        if url == "/control/drain":
            assert post and not self.occupied and control
            self.actions.append("drain-new" if self.candidate else "drain-old")
            self.program = None
            return {"v": 1, "state": "draining"}
        candidate = self.candidate
        if url == "/info":
            return {
                "state": "ready", "protocol": op.PROTOCOL,
                "contentHash": (op.NEW_CONTENT if self.fault != "identity" else op.OLD_CONTENT)
                if candidate else op.OLD_CONTENT,
            }
        if url == "/readyz":
            return {"ok": self.fault != "storage", "storage": "ok" if self.fault != "storage" else "down", "state": "ready"}
        if url == "/api/lobby":
            return {
                "arenaAvailable": True, "arenaLimit": 2, "arenaRoomCount": 0, "rooms": [],
                "waiting": ["guest"] if self.occupied else [], "onlineCount": int(self.occupied),
            }
        if url == "/control/info":
            return {"state": "ready", "rooms": 0, "maxRooms": 2, "players": int(self.occupied)}
        assert url == "/control/health"
        writers = {"configured": True, "started": self.fault != "capture", "startFailed": False, "writerUnavailable": 0}
        return {"state": "ready", "v": 1, "rooms": 0, "build": op.NEW if candidate else op.OLD,
                "alphaCapture": {"metrics": writers, "replays": writers}}

    def start(self, argv, transient):
        self.assert_layout(argv, transient)
        self.program = argv
        self.actions.append("start-new" if self.candidate else "start-old")
        self.fault = self.next_fault if self.candidate else None
        if self.fault == "start":
            self.program = None
            raise op.Guard("fixture startup failure")
        if self.fault == "occupied":
            self.fault = "capture"
            self.occupied = True

    def assert_layout(self, argv, transient):
        assert transient == self.transient
        if not transient:
            approved = self.override.read_bytes() == op.override_bytes(argv) if self.override.exists() else argv == self.prior_argv
            assert approved

    def verify_edge(self, name, expected):
        if name == "index.html" and self.candidate and self.fault == "loader":
            raise op.Guard("fixture mismatched client")
        assert self.front_bytes(name) == expected

    def verify_assets(self, manifest):
        assert self.versions.joinpath(op.NEW, "Build/Web.loader.js.br").is_file()


class ReleaseTest(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name).resolve()
        self.stage = Guest("stage", self.root)
        self.old_index_sha = op.hexdigest(b"old root")
        self.archive = self.root / "release.tar.gz"
        self.patch("OLD_NATIVE_SHA", op.hexdigest(b"prior native"))
        elf = b"\x7fELF\x02\x01" + bytes(12) + b"\x3e\x00"
        self.files = {
            "rh-server": elf + b"new native", "rh-headless": elf + b"new verifier", "seed1.map.json": b"fallback",
            "web/index.html": b"unversioned fallback", "web/alpha-gameplay-data.html": b"new notice",
        }
        for name in ("Web.loader.js", "Web.framework.js", "Web.data", "Web.wasm"):
            for extension in ("", ".br"):
                key = "web/Build/" + name + extension
                self.files[key] = key.encode()
        for target in op.TARGETS.values():
            for mode in ("split", "same-origin"):
                self.files[f"loaders/{target.web_tag}-{mode}.html"] = loader(target, mode)
        self.seal()

    def patch(self, name, value):
        context = patch.object(op, name, value)
        self.addCleanup(context.stop)
        context.start()

    def seal(self, extra=None):
        manifest = {
            "releaseSource": op.NEW, "contentHash": op.NEW_CONTENT, "protocol": op.PROTOCOL,
            "web": {"verifiedBuildSource": op.NEW, "bakedContentHash": op.NEW_CONTENT},
            "native": {"build": op.NEW, "target": "x86_64-unknown-linux-gnu"},
            "databaseMigrations": {"gitTree": op.MIGRATIONS_TREE},
            "files": {name: {"sha256": op.hexdigest(data), "bytes": len(data)} for name, data in self.files.items()},
        }
        with tarfile.open(self.archive, "w:gz") as bundle:
            for name, data in {**self.files, op.MANIFEST: op.json_bytes(manifest)}.items():
                member = tarfile.TarInfo(name)
                member.size = len(data)
                bundle.addfile(member, io.BytesIO(data))
            if extra:
                bundle.addfile(extra)
        self.archive_sha = op.file_digest(self.archive)

    def apply(self, guest=None, mode="preserve"):
        return op.apply(guest or self.stage, self.archive, self.archive_sha, mode, self.old_index_sha, lambda _: None)

    def rollback(self, guest=None):
        return op.rollback(guest or self.stage, sleep=lambda _: None)

    def tearDown(self):
        for folder, _, _ in os.walk(self.root):
            Path(folder).chmod(0o700)

    def test_pair_repeat_fallback_restore_and_reapply(self):
        self.assertEqual(self.apply(), "PAIRED_RELEASE_SELECTED")
        self.assertEqual(self.apply(), "PAIRED_ALREADY_SELECTED")
        self.assertEqual(self.stage.actions, ["drain-old", "start-new"])
        self.assertEqual(self.stage.program[self.stage.program.index("--fill-bots") + 1], "4")
        self.assertEqual(op.fallback(self.stage, "same-origin"), "PAIRED_LOADER_SAME_ORIGIN")
        self.assertEqual(self.stage.front_bytes("index.html"), self.files["loaders/staging-same-origin.html"])
        self.assertEqual(self.rollback(), "PRIOR_PAIRED_RELEASE_RESTORED")
        self.assertEqual(self.rollback(), "PRIOR_ALREADY_RESTORED")
        self.assertEqual(self.stage.front_bytes("index.html"), b"old root")
        self.assertEqual(self.stage.front_bytes("alpha-gameplay-data.html"), b"old notice")
        self.assertEqual(self.apply(), "PAIRED_RELEASE_SELECTED")

    def test_raid_and_production_override_are_paired_and_reversible(self):
        production = Guest("production", self.root)
        self.patch("Host", lambda kind, root, gid: self.stage)
        with self.assertRaisesRegex(op.Guard, "staging first"):
            self.apply(production, mode="raid")
        self.assertEqual(production.actions, [])
        self.apply(mode="raid")
        with self.assertRaisesRegex(op.Guard, "staging first"):
            self.apply(production)
        self.apply(production, mode="raid")
        self.assertTrue(production.override.exists())
        self.assertIn("--signature-landmarks", production.program)
        for flag, expected in (("--fill-bots", "8"), ("--bot-brain", "raid"), ("--fill-seconds", "2"), ("--max-rooms", "2")):
            self.assertEqual(production.program[production.program.index(flag) + 1], expected)
        self.rollback(production)
        self.assertFalse(production.override.exists())
        self.assertEqual(production.program, production.prior_argv)
        self.assertEqual(production.front_bytes("index.html"), b"old root")
        self.assertTrue(self.stage.candidate)

    def test_corrupt_archive_and_traversal_are_refused_before_drain(self):
        saved = self.archive_sha
        self.archive_sha = "0" * 64
        with self.assertRaisesRegex(op.Guard, "Wrong release archive"):
            self.apply()
        self.archive_sha = saved
        link = tarfile.TarInfo("web/Build/link")
        link.type = tarfile.SYMTYPE
        link.linkname = "../../../../etc/passwd"
        self.seal(extra=link)
        with self.assertRaisesRegex(op.Guard, "link"):
            self.apply()
        self.seal(extra=tarfile.TarInfo("../outside"))
        with self.assertRaisesRegex(op.Guard, "archive"):
            self.apply()
        self.assertEqual(self.stage.actions, [])

    def test_optional_template_assets_still_require_versioned_urls_when_packaged(self):
        path = "web/TemplateData/icon.png"
        self.files[path] = b"fixture"
        self.seal()
        with self.assertRaisesRegex(op.Guard, "different client build"):
            self.apply()
        for name in op.LOADERS:
            self.files[name] += f" /alpha-web/{op.NEW}/TemplateData".encode()
        self.seal()
        self.assertEqual(self.apply(), "PAIRED_RELEASE_SELECTED")

    def test_candidate_client_mismatch_restores_prior_pair(self):
        self.stage.next_fault = "loader"
        with self.assertRaisesRegex(op.Guard, "mismatched client.*Prior pair restored"):
            self.apply()
        self.assertEqual(self.stage.actions, ["drain-old", "start-new", "drain-new", "start-old"])
        self.assertEqual(self.stage.front_bytes("index.html"), b"old root")
        self.assertEqual(self.stage.front_bytes("alpha-gameplay-data.html"), b"old notice")

    def test_candidate_native_start_storage_capture_and_identity_failures_restore(self):
        for fault in ("start", "storage", "capture", "identity"):
            with self.subTest(fault=fault):
                self.stage.next_fault = fault
                with self.assertRaisesRegex(op.Guard, "Prior pair restored"):
                    self.apply()
                self.assertEqual(self.stage.program, self.stage.prior_argv)
                self.assertEqual(self.stage.front_bytes("index.html"), b"old root")

    def test_lobby_and_unrecognized_configuration_never_drain(self):
        self.stage.occupied = True
        with self.assertRaisesRegex(op.Guard, "occupied"):
            self.apply()
        self.stage.occupied = False
        self.stage.override.write_bytes(b"unknown")
        with self.assertRaisesRegex(op.Guard, "unowned"):
            self.apply()
        self.assertEqual(self.stage.actions, [])

    def test_candidate_with_new_admissions_is_left_running_until_safe(self):
        self.stage.next_fault = "occupied"
        with self.assertRaisesRegex(op.Guard, "Rollback is pending.*occupied"):
            self.apply()
        self.assertTrue(self.stage.candidate)
        self.assertEqual(self.stage.actions, ["drain-old", "start-new"])
        self.stage.occupied = False
        self.assertEqual(self.rollback(), "PRIOR_PAIRED_RELEASE_RESTORED")
        self.assertEqual(self.stage.program, self.stage.prior_argv)

    def test_rollback_survives_damaged_candidate_asset(self):
        self.apply()
        file = self.stage.release / "web/Build/Web.data"
        file.chmod(0o640)
        file.write_bytes(b"damaged")
        self.assertEqual(self.rollback(), "PRIOR_PAIRED_RELEASE_RESTORED")

    def test_unexpected_live_front_is_left_in_place(self):
        self.apply()
        op.atomic_write(self.stage.front / "index.html", b"unexpected", 0o440, self.stage.gid)
        with self.assertRaisesRegex(op.Guard, "Unrecognized"):
            self.rollback()
        self.assertTrue(self.stage.candidate)
        self.assertEqual(self.stage.front_bytes("index.html"), b"unexpected")

    def test_systemd_transient_collection_releases_name_before_start(self):
        host = op.Host("stage", self.root, os.getgid())
        stopped = op.UnitState("inactive", "success", 0)
        missing = op.UnitState("missing", "", 0)
        with ExitStack() as contexts:
            contexts.enter_context(patch.object(host, "unit_state", side_effect=[stopped, stopped, missing]))
            contexts.enter_context(patch.object(op.socket, "create_connection", side_effect=ConnectionRefusedError))
            pause = contexts.enter_context(patch.object(op.time, "sleep"))
            run = contexts.enter_context(patch.object(op.subprocess, "run"))
            host.start(self.stage.prior_argv, transient=True)
        pause.assert_called_once()
        command = run.call_args.args[0]
        self.assertIn("--collect", command)
        self.assertIn("--property=TimeoutStopSec=90s", command)
        self.assertEqual(tuple(command[-len(self.stage.prior_argv):]), self.stage.prior_argv)

    def test_systemd_missing_and_systemd_failure_are_distinct(self):
        host = op.Host("stage", self.root, os.getgid())
        not_found = subprocess.CompletedProcess([], 1, "", f"Unit {host.target.unit} could not be found.")
        failure = subprocess.CompletedProcess([], 1, "", "Cannot connect to the system bus.")
        with patch.object(op.subprocess, "run", side_effect=[not_found, failure]):
            self.assertEqual(host.unit_state(), op.UnitState("missing", "", 0))
            with self.assertRaisesRegex(op.Guard, "Unknown Source unit state"):
                host.unit_state()

    def test_shared_lock_refuses_concurrent_operator(self):
        (self.root / "run").mkdir()
        with self.stage.locked():
            with self.assertRaises(BlockingIOError):
                with self.stage.locked():
                    self.fail("Lock allowed a concurrent deployment")


if __name__ == "__main__":
    unittest.main(verbosity=2)

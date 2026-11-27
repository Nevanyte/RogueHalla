#!/usr/bin/env python3
"""Exercise the production rollout state machine without touching host services."""
from contextlib import contextmanager
import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

source = Path(__file__).with_name("set-live-native.py")
spec = importlib.util.spec_from_file_location("live_native", source)
native = importlib.util.module_from_spec(spec)
spec.loader.exec_module(native)


class FakeHost(native.Host):
    def __init__(self, root):
        super().__init__(root)
        self.config.mkdir(parents=True)
        self.lock.parent.mkdir()
        self.fragment = self.config.parent / native.UNIT
        self.fragment.write_text("[Service]\nEnvironmentFile=/etc/rh-alpha/live.env\n")
        self.environment = self.config / "85-observability.conf"
        self.environment.write_text("[Service]\nEnvironment=FIXTURE=untouched\n")
        self.release(native.OLD).parent.mkdir(parents=True)
        self.release(native.NEW).parent.mkdir(parents=True)
        for build, name in ((native.OLD, "rh-server"), (native.NEW, "rh-server"), (native.NEW, "rh-headless")):
            artifact = self.release(build, name)
            artifact.write_text(build + "/" + name)
            artifact.chmod(0o755)
        self.body = "  ".join([
            str(self.release(native.OLD)), "--runtime source-arena", "--bind 127.0.0.1:8777",
            "--map " + str(self.release(native.OLD, "seed1.map.json")), "--map-pool",
            "--instance-id alpha-production-source", "--fill-bots 4", "--max-rooms 1",
            "--bot-learning off", "--database-url postgresql:///fixture?host=/run/fixture&user=fixture",
            "--api-cors-origins https://play.example.test", "--control 127.0.0.1:8778",
            "--control-token-file " + str(self.root / "etc/rh-alpha/live-control.token"),
            "--alpha-metrics-dir /srv/fixture/metrics", "--alpha-replay-dir /srv/fixture/replays",
        ])
        self.split.write_text("[Service]\nExecStart=\nExecStart=" + self.body + "\n")
        self.active = "active"
        self.result = "success"
        self.pid = 314
        self.loaded = tuple(self.body.split())
        self.argv = self.loaded
        self.call_log = []
        self.rooms = self.waiting = self.admissions = 0
        self.build = native.OLD
        self.lifecycle = "ready"
        self.dirty = False
        self.fail_start = self.fail_health = self.occupy_candidate = self.block_control = False
        self.fail_writer_before = self.fail_writer_after = False
        self.occupy_waiting_candidate = self.block_control_after = False
        self.drop_drain_response = self.hold_drain = self.foreign_listener = False
        self.bad_public_identity = False

    def run(self, program, *args, timeout=15):
        assert program == "systemd-analyze" and args == ("unit-paths",)
        return "/etc/systemd/system\n/run/systemd/system\n"

    def systemctl(self, action, *args, timeout=15):
        self.call_log.append((action, *args))
        if action == "show":
            paths = sorted(self.config.glob("*.conf"))
            props = {
                "FragmentPath": self.fragment, "DropInPaths": " ".join(str(path) for path in paths),
                "ExecStart": "{ path=" + self.loaded[0] + " ; argv[]=" + " ".join(self.loaded) +
                             " ; ignore_errors=no ; start_time=[n/a] ; pid=0 ; code=(null) ; status=0/0 }",
                "NeedDaemonReload": "yes" if self.dirty else "no",
                "ActiveState": self.active, "Result": self.result, "MainPID": self.pid,
            }
            return "\n".join(key + "=" + str(props[key]) for key in (arg.split("=", 1)[1] for arg in args[:-1]))
        if action == "daemon-reload":
            drops = [path for path in (self.native, self.cap, self.split) if path.exists()]
            line = next(line for line in drops[0].read_text().splitlines() if line.startswith("ExecStart=/"))
            self.loaded = tuple(line.removeprefix("ExecStart=").split())
            self.dirty = False
        elif action == "start":
            assert args == (native.UNIT,) and self.pid == 0
            target = native.NEW if self.loaded[0] == str(self.release(native.NEW)) else native.OLD
            if target == native.NEW and self.fail_start:
                self.active, self.result = "failed", "exit-code"
                raise native.subprocess.CalledProcessError(1, [action])
            self.argv, self.build = self.loaded, target
            self.active, self.result, self.lifecycle = "active", "success", "ready"
            self.pid = 315 if target == native.NEW else 316
            if target == native.NEW:
                self.rooms = int(self.occupy_candidate)
                self.waiting = int(self.occupy_waiting_candidate)
        else:
            raise AssertionError("Unsafe or unexpected systemctl action " + action)
        return ""

    def running(self, pid):
        assert pid == self.pid and self.pid > 0
        return Path(self.argv[0]), self.argv

    def verify_ports_closed(self):
        if self.foreign_listener or self.pid != 0:
            raise native.Guard("Live socket is already occupied")

    def request(self, port, path, post=False):
        self.call_log.append(("POST" if post else "GET", port, path))
        assert port == (native.CONTROL if path.startswith("/control/") else native.PUBLIC)
        assert self.pid > 0
        cap = 2 if self.build == native.NEW else 1
        if port == native.CONTROL and (self.block_control or (cap == 2 and self.block_control_after)):
            raise native.Guard("Control token rejected")
        if path == "/info":
            identity = dict(native.IDENTITY)
            if self.bad_public_identity:
                identity["contentHash"] = "changed"
            return {**identity, "state": self.lifecycle}
        if path == "/control/health":
            writer = {"configured": True, "started": True, "startFailed": False, "writerUnavailable": 0}
            replay = dict(writer)
            if (cap == 1 and self.fail_writer_before) or (cap == 2 and self.fail_writer_after):
                replay.update(started=False, startFailed=True)
            return {"v": 1, "build": self.build, "state": self.lifecycle, "rooms": self.rooms,
                    "alphaCapture": {"replays": replay, "metrics": writer}}
        if path == "/readyz":
            ready = cap != 2 or not self.fail_health
            return {"ok": ready, "storage": "ok" if ready else "unavailable", "state": self.lifecycle}
        if path == "/api/lobby":
            return {"arenaAvailable": True, "arenaLimit": cap, "arenaRoomCount": self.rooms,
                    "onlineCount": self.rooms + self.waiting, "rooms": [{}] * self.rooms, "waiting": [{}] * self.waiting}
        if path == "/control/info":
            return {"state": self.lifecycle, "maxRooms": cap, "rooms": self.rooms,
                    "players": self.rooms + self.admissions}
        assert path == "/control/drain" and post
        assert not self.rooms and not self.waiting and not self.admissions, "drained a live player"
        if self.hold_drain:
            self.lifecycle = "draining"
            return {"v": 1, "state": "draining"}
        self.clean_exit()
        if self.drop_drain_response:
            raise ConnectionResetError
        return {"v": 1, "state": "terminated"}

    def clean_exit(self):
        self.active, self.result, self.lifecycle, self.pid = "inactive", "success", "terminated", 0

    @contextmanager
    def locked(self):
        yield


class RolloutTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.host = FakeHost(Path(directory.name).resolve())
        patcher = patch.multiple(native, OLD_SERVER_SHA=native.digest(self.host.release(native.OLD)), NEW_SHA={
            name: native.digest(self.host.release(native.NEW, name)) for name in ("rh-server", "rh-headless")
        })
        patcher.start()
        self.addCleanup(patcher.stop)

    def run_op(self, op="apply"):
        return native.operate(self.host, op, sleep=lambda _: None)

    def assert_no_change(self):
        self.assertFalse(self.host.native.exists())
        self.assertFalse(self.host.record.exists())
        self.assertEqual(native.OLD, self.host.build)
        self.assertEqual(314, self.host.pid)
        self.assertFalse(any(call[0] in ("POST", "daemon-reload", "start") for call in self.host.call_log))

    def test_read_only_preflight_reports_identity_and_live_occupancy(self):
        self.assertIn("LIVE_PREFLIGHT_OK", self.run_op("check"))
        self.assert_no_change()
        self.host.rooms = 1
        with self.assertRaisesRegex(native.Guard, "occupied"):
            self.run_op("check")
        self.assert_no_change()

    def test_apply_repeat_and_guarded_rollback_preserve_arguments_and_env(self):
        original = self.host.split.read_bytes()
        env = self.host.environment.read_bytes()
        self.assertIn("LIVE_READY", self.run_op())
        old_map = str(self.host.release(native.OLD, "seed1.map.json"))
        self.assertIn(old_map, self.host.argv)
        expected = self.host.body.replace(str(self.host.release(native.OLD)), str(self.host.release(native.NEW)), 1)
        expected = expected.replace("--max-rooms 1", "--max-rooms 2")
        self.assertIn("ExecStart=" + expected + "\n", self.host.native.read_text())
        self.assertEqual(expected.split(), list(self.host.argv))
        self.assertIn("LIVE_ALREADY_READY", self.run_op())
        self.assertEqual(315, self.host.pid)
        self.assertTrue(self.host.record.exists())
        self.assertIn("LIVE_RESTORED", self.run_op("rollback"))
        self.assertEqual(tuple(self.host.body.split()), self.host.argv)
        self.assertEqual(original, self.host.split.read_bytes())
        self.assertEqual(env, self.host.environment.read_bytes())
        self.assertFalse(self.host.native.exists())
        self.assertIn("LIVE_RESTORED", self.run_op("rollback"))
        self.assertEqual(316, self.host.pid)

    def test_known_single_room_capacity_override_stays_byte_identical(self):
        contents = (native.CAPACITY_COMMENT + "\n[Service]\nExecStart=\nExecStart=" + self.host.body + "\n").encode()
        self.host.cap.write_bytes(contents)
        self.run_op()
        self.assertEqual(contents, self.host.cap.read_bytes())
        self.run_op("rollback")
        self.assertEqual(contents, self.host.cap.read_bytes())

    def test_occupied_room_refuses_before_drain(self):
        self.host.rooms = 1
        with self.assertRaisesRegex(native.Guard, "occupied"):
            self.run_op()
        self.assert_no_change()

    def test_waiting_player_refuses_before_drain(self):
        self.host.waiting = 1
        with self.assertRaisesRegex(native.Guard, "occupied"):
            self.run_op()
        self.assert_no_change()

    def test_private_player_counter_catches_admission(self):
        self.host.admissions = 1
        with self.assertRaisesRegex(native.Guard, "admissions or rooms"):
            self.run_op()
        self.assert_no_change()

    def test_replay_verifier_checksum_refuses_before_drain(self):
        self.host.release(native.NEW, "rh-headless").write_text("wrong build")
        with self.assertRaisesRegex(native.Guard, "Checksum mismatch for rh-headless"):
            self.run_op()
        self.assert_no_change()

    def test_unreadable_control_health_refuses_before_drain(self):
        self.host.block_control = True
        with self.assertRaisesRegex(native.Guard, "Control token rejected"):
            self.run_op()
        self.assert_no_change()

    def test_public_content_mismatch_refuses_before_drain(self):
        self.host.bad_public_identity = True
        with self.assertRaisesRegex(native.Guard, "protocol or content"):
            self.run_op()
        self.assert_no_change()

    def test_original_writer_failure_refuses_before_drain(self):
        self.host.fail_writer_before = True
        with self.assertRaisesRegex(native.Guard, "replays capture writer"):
            self.run_op()
        self.assert_no_change()

    def test_unloaded_other_drop_in_with_execstart_fails_closed(self):
        override = self.host.root / "run/systemd/system/rh-alpha-.service.d/95-extra.conf"
        override.parent.mkdir(parents=True)
        override.write_text("[Service]\n  ExecStart=/unreviewed\n")
        with self.assertRaisesRegex(native.Guard, "Unexpected drop-in defines ExecStart"):
            self.run_op()
        self.assert_no_change()

    def test_unknown_capacity_override_fails_closed(self):
        self.host.cap.write_text("[Service]\nExecStart=/unexpected\n")
        with self.assertRaisesRegex(native.Guard, "Unexpected 91-capacity"):
            self.run_op()
        self.assert_no_change()

    def test_unloaded_unit_edit_fails_closed(self):
        self.host.dirty = True
        with self.assertRaisesRegex(native.Guard, "unloaded unit edits"):
            self.run_op()
        self.assert_no_change()

    def test_failed_command_start_restores_original_build(self):
        self.host.fail_start = True
        with self.assertRaisesRegex(native.Guard, "Original native release and one-room cap are healthy"):
            self.run_op()
        self.assertEqual(native.OLD, self.host.build)
        self.assertEqual("active", self.host.active)
        self.assertFalse(self.host.native.exists())

    def test_active_unhealthy_candidate_drains_when_idle(self):
        self.host.fail_health = True
        with self.assertRaisesRegex(native.Guard, "Original native release and one-room cap are healthy"):
            self.run_op()
        self.assertEqual(2, self.drains())
        self.assertEqual(native.OLD, self.host.build)
        self.assertEqual("active", self.host.active)
        self.assertFalse(self.host.native.exists())

    def test_active_candidate_without_both_writers_rolls_back(self):
        self.host.fail_writer_after = True
        with self.assertRaisesRegex(native.Guard, "capture writer.*Original native release"):
            self.run_op()
        self.assertEqual(2, self.drains())
        self.assertEqual(native.OLD, self.host.build)

    def test_active_unhealthy_candidate_with_players_is_left_running(self):
        self.host.fail_health = self.host.occupy_candidate = True
        with self.assertRaisesRegex(native.Guard, "Rollback deferred"):
            self.run_op()
        self.assertEqual(1, self.drains())
        self.assertEqual(native.NEW, self.host.build)
        self.assertEqual(315, self.host.pid)
        self.assertTrue(self.host.native.exists())
        with self.assertRaisesRegex(native.Guard, "open rooms|occupied"):
            self.run_op("rollback")
        self.assertEqual(1, self.drains())
        self.host.rooms = 0
        self.assertIn("LIVE_RESTORED", self.run_op("rollback"))
        self.assertEqual(2, self.drains())
        self.assertEqual(native.OLD, self.host.build)

    def test_busy_hub_also_blocks_explicit_rollback(self):
        self.host.occupy_waiting_candidate = True
        self.run_op()
        with self.assertRaisesRegex(native.Guard, "occupied"):
            self.run_op("rollback")
        self.assertEqual(1, self.drains())
        self.assertEqual(315, self.host.pid)

    def test_failed_candidate_control_does_not_force_a_stop(self):
        self.host.block_control_after = True
        with self.assertRaisesRegex(native.Guard, "Rollback deferred .*Control token rejected"):
            self.run_op()
        self.assertEqual(1, self.drains())
        self.assertEqual(315, self.host.pid)
        self.assertTrue(self.host.native.exists())

    def test_clean_exit_without_drain_response_is_accepted(self):
        self.host.drop_drain_response = True
        self.assertIn("LIVE_READY", self.run_op())

    def test_drain_timeout_does_not_change_config_and_can_recover(self):
        self.host.hold_drain = True
        with self.assertRaisesRegex(native.Guard, "Graceful drain still in progress"):
            self.run_op()
        self.assertFalse(self.host.native.exists())
        self.assertEqual(314, self.host.pid)
        self.assertEqual(1, self.drains())
        self.host.clean_exit()
        self.assertIn("LIVE_RESTORED", self.run_op("rollback"))

    def test_foreign_listener_blocks_start_after_drain(self):
        self.host.foreign_listener = True
        with self.assertRaisesRegex(native.Guard, "Live socket is already occupied"):
            self.run_op()
        self.assertFalse(self.host.native.exists())
        self.assertEqual(0, self.host.pid)
        self.assertFalse(any(call[0] == "start" for call in self.host.call_log))

    def drains(self):
        return self.host.call_log.count(("POST", native.CONTROL, "/control/drain"))


if __name__ == "__main__":
    unittest.main(verbosity=2)

#!/usr/bin/env python3
"""Verify the fill switch and both rollback orders with a disposable systemd fixture."""
import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch


def load(name, file):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(file))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


fill = load("live_fill", "set-live-fill.py")
fixture = load("native_fixtures", "test-live-native.py")
native = fixture.native = fill.native


class FillHost(fixture.FakeHost):
    fail_fill_start = None
    fail_two_health = False

    def systemctl(self, action, *args, timeout=15):
        fill_seconds = 2 if self.loaded[-2:] == ("--fill-seconds", "2") else 5
        if action == "start" and self.loaded[0] == str(self.release(native.NEW)) and self.fail_fill_start == fill_seconds:
            self.call_log.append((action, *args))
            self.active, self.result = "failed", "exit-code"
            raise native.subprocess.CalledProcessError(1, [action])
        return super().systemctl(action, *args, timeout=timeout)

    def request(self, port, path, post=False):
        value = super().request(port, path, post)
        if path == "/readyz" and self.argv[-2:] == ("--fill-seconds", "2") and self.fail_two_health:
            value.update(ok=False, storage="unavailable")
        return value


class FillTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.host = FillHost(Path(directory.name).resolve())
        patcher = patch.multiple(native, OLD_SERVER_SHA=native.digest(self.host.release(native.OLD)), NEW_SHA={
            name: native.digest(self.host.release(native.NEW, name)) for name in ("rh-server", "rh-headless")
        })
        patcher.start()
        self.addCleanup(patcher.stop)
        native.operate(self.host, "apply", sleep=lambda _: None)
        self.original = self.host.native.read_bytes()
        self.record = self.host.record.read_bytes()
        self.split = self.host.split.read_bytes()
        self.env = self.host.environment.read_bytes()
        self.argv = self.host.argv
        self.host.call_log.clear()

    def execute(self, action, target=5):
        return fill.operate(self.host, action, target, sleep=lambda _: None)

    def assert_no_change(self):
        self.assertEqual(self.original, self.host.native.read_bytes())
        self.assertEqual(self.argv, self.host.argv)
        self.assertFalse(any(call[0] in ("POST", "daemon-reload", "start") for call in self.host.call_log))

    def test_checks_both_directions_without_writes_or_drain(self):
        self.assertIn("current=5s target=2s", self.execute("check", 2))
        self.assert_no_change()
        self.execute("apply", 2)
        at_two = self.host.native.read_bytes()
        self.host.call_log.clear()
        self.assertIn("current=2s target=5s", self.execute("check", 5))
        self.assertEqual(at_two, self.host.native.read_bytes())
        self.assertFalse(any(call[0] in ("POST", "daemon-reload", "start") for call in self.host.call_log))

    def test_apply_repeat_restore_and_native_rollback_order(self):
        self.assertIn("fill=2s", self.execute("apply", 2))
        self.assertEqual(self.argv + ("--fill-seconds", "2"), self.host.argv)
        self.assertEqual(self.original[:-1] + b" --fill-seconds 2\n", self.host.native.read_bytes())
        self.assertIn("ALREADY_READY", self.execute("apply", 2))
        with self.assertRaisesRegex(native.Guard, "Unexpected native override"):
            native.operate(self.host, "rollback", sleep=lambda _: None)
        self.assertEqual(self.record, self.host.record.read_bytes())
        self.assertIn("fill=5s", self.execute("apply", 5))
        self.assertEqual(self.original, self.host.native.read_bytes())
        self.assertEqual(self.split, self.host.split.read_bytes())
        self.assertEqual(self.env, self.host.environment.read_bytes())
        self.assertEqual(self.record, self.host.record.read_bytes())
        self.assertIn("roomLimit=1", native.operate(self.host, "rollback", sleep=lambda _: None))

    def test_refuse_when_no_guarded_native_rollout(self):
        native.operate(self.host, "rollback", sleep=lambda _: None)
        with self.assertRaisesRegex(native.Guard, "two-room native rollout"):
            self.execute("apply", 2)
        self.assertEqual(native.OLD, self.host.build)

    def test_occupied_room_refuses_before_drain_even_on_no_op(self):
        self.host.rooms = 1
        for target in (2, 5):
            with self.assertRaisesRegex(native.Guard, "occupied"):
                self.execute("apply", target)
        self.assert_no_change()

    def test_waiting_players_block_rollback(self):
        self.execute("apply", 2)
        self.host.waiting = 1
        self.host.call_log.clear()
        with self.assertRaisesRegex(native.Guard, "occupied"):
            self.execute("rollback")
        self.assertEqual(self.argv + ("--fill-seconds", "2"), self.host.argv)
        self.assertFalse(any(call[0] == "POST" for call in self.host.call_log))

    def test_unknown_override_fails_closed(self):
        (self.host.config / "99-unknown.conf").write_text("[Service]\nExecStart=/unexpected\n")
        with self.assertRaisesRegex(native.Guard, "Unexpected drop-in"):
            self.execute("apply", 2)
        self.assert_no_change()

    def test_unapproved_baseline_change_fails_closed(self):
        self.host.environment.write_text("[Service]\nEnvironment=FIXTURE=unexpected\n")
        with self.assertRaisesRegex(native.Guard, "Original unit files differ"):
            self.execute("apply", 2)
        self.assert_no_change()

    def test_different_effective_command_fails_closed(self):
        self.host.loaded = self.argv + ("--fill-seconds", "3")
        with self.assertRaisesRegex(native.Guard, "Effective live unit command"):
            self.execute("apply", 2)
        self.assert_no_change()

    def test_native_override_with_extra_directives_fails_closed(self):
        self.host.native.write_bytes(self.original + b"Environment=EXTRA=changed\n")
        with self.assertRaisesRegex(native.Guard, "Unexpected native ExecStart override"):
            self.execute("apply", 2)
        self.assertFalse(any(call[0] in ("POST", "start") for call in self.host.call_log))

    def test_failed_start_restores_five(self):
        self.host.fail_fill_start = 2
        with self.assertRaisesRegex(native.Guard, "prior 5s fill is healthy"):
            self.execute("apply", 2)
        self.assertEqual(self.original, self.host.native.read_bytes())
        self.assertEqual(self.argv, self.host.argv)
        self.assertEqual("active", self.host.active)

    def test_failed_five_second_start_restores_two(self):
        self.execute("apply", 2)
        prior = self.host.native.read_bytes()
        self.host.fail_fill_start = 5
        with self.assertRaisesRegex(native.Guard, "prior 2s fill is healthy"):
            self.execute("apply", 5)
        self.assertEqual(prior, self.host.native.read_bytes())
        self.assertEqual(self.argv + ("--fill-seconds", "2"), self.host.argv)
        self.assertEqual("active", self.host.active)

    def test_bad_replay_verifier_digest_refuses_before_drain(self):
        self.host.release(native.NEW, "rh-headless").write_text("unexpected verifier")
        with self.assertRaisesRegex(native.Guard, "Checksum mismatch for rh-headless"):
            self.execute("apply", 2)
        self.assert_no_change()

    def test_unhealthy_running_candidate_gracefully_rolls_back_when_idle(self):
        self.host.fail_two_health = True
        with self.assertRaisesRegex(native.Guard, "prior 5s fill is healthy"):
            self.execute("apply", 2)
        self.assertEqual(self.original, self.host.native.read_bytes())
        self.assertEqual(self.argv, self.host.argv)
        self.assertEqual(2, self.host.call_log.count(("POST", native.CONTROL, "/control/drain")))

    def test_unhealthy_occupied_candidate_is_left_running_and_recoverable(self):
        self.host.fail_two_health = self.host.occupy_candidate = True
        with self.assertRaisesRegex(native.Guard, "Recovery deferred"):
            self.execute("apply", 2)
        self.assertEqual(self.argv + ("--fill-seconds", "2"), self.host.argv)
        self.assertEqual("active", self.host.active)
        self.assertEqual(1, self.host.call_log.count(("POST", native.CONTROL, "/control/drain")))
        self.host.rooms = 0
        self.host.occupy_candidate = False
        self.assertIn("fill=5s", self.execute("rollback"))
        self.assertEqual(self.original, self.host.native.read_bytes())
        self.assertEqual(self.argv, self.host.argv)


if __name__ == "__main__":
    unittest.main(verbosity=2)

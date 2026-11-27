import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import health


class MonitorTest(unittest.TestCase):
    now = 1800

    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        for name in health.STREAMS:
            (root / name).mkdir()
        self.root = root
        self.streams = {
            name: dict.fromkeys(health.COUNTERS, 0) | {"configured": True, "started": True, "startFailed": False}
            for name in health.STREAMS
        }
        self.patch(health, "CAPTURE_SPOOL", root)
        self.patch(health, "CAPTURE_STATE", root / "capture.json")
        self.patch(health, "BACKUP_RECEIPT", root / "last-success.json")
        self.patch(health, "capture_streams", return_value=self.streams)
        self.active = self.patch(health, "active", return_value=True)
        self.successful = self.patch(health, "successful", return_value=True)

    def patch(self, *args, **kwargs):
        mock = patch.object(*args, **kwargs)
        self.addCleanup(mock.stop)
        return mock.start()

    def touch(self, relative, age=0):
        path = self.root / relative
        path.touch()
        os.utime(path, (self.now - age, self.now - age))

    def test_healthy_paths_use_the_live_timers(self):
        self.touch("metrics/pending.json.age", age=30)
        self.touch("last-success.json")
        self.assertTrue(health.capture(self.now))
        self.active.assert_called_with("rh-alpha-upload@live.timer", "rh-alpha-capture-credentials.timer")
        self.assertTrue(health.backup(self.now))
        self.active.assert_called_with("rh-alpha-live-backup.timer")

    def test_stale_queue_and_failed_writer_are_unhealthy(self):
        self.touch("replays/pending.json.age", age=health.SPOOL_MAX_AGE)
        self.assertFalse(health.capture(self.now))
        (self.root / "replays/pending.json.age").unlink()
        self.streams["replays"]["startFailed"] = True
        self.assertFalse(health.capture(self.now))

    def test_new_error_is_held_across_samples_even_if_writer_restarts(self):
        self.assertTrue(health.capture(self.now))
        self.streams["metrics"]["failed"] = 1
        self.assertFalse(health.capture(self.now + 1))
        self.streams["metrics"]["failed"] = 0
        self.assertFalse(health.capture(self.now + 600))
        self.assertTrue(health.capture(self.now + 1 + health.CAPTURE_ERROR_HOLD))

    def test_missing_stale_or_future_backup_is_unhealthy(self):
        self.assertFalse(health.backup(self.now))
        self.touch("last-success.json", age=health.BACKUP_MAX_AGE)
        self.assertFalse(health.backup(self.now))
        self.touch("last-success.json", age=-1)
        self.assertFalse(health.backup(self.now))
        self.touch("last-success.json")
        self.successful.return_value = False
        self.assertFalse(health.backup(self.now))

    def test_metric_contract_and_failed_control_sample(self):
        self.touch("last-success.json")
        self.streams.clear()
        self.patch(health.time, "time", return_value=self.now)
        publish = self.patch(health.subprocess, "run")
        health.main()
        command = publish.call_args.args[0]
        self.assertIn("RogueHalla/AlphaOperations", command)
        self.assertEqual(
            json.loads(command[-1]),
            [
                {"MetricName": "CaptureHealthy", "Dimensions": [{"Name": "Environment", "Value": "production"}],
                 "Value": 0, "Unit": "Count"},
                {"MetricName": "BackupHealthy", "Dimensions": [{"Name": "Environment", "Value": "production"}],
                 "Value": 1, "Unit": "Count"},
            ],
        )


if __name__ == "__main__":
    unittest.main()

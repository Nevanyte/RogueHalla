#!/usr/bin/env python3
"""Publish host-local capture and backup health once per timer invocation."""
import http.client
import json
from pathlib import Path
import subprocess
import time

CAPTURE_SPOOL = Path("/srv/rh-alpha/alpha-capture-live")
CAPTURE_STATE = Path("/var/lib/rh-alpha-monitor/capture.json")
BACKUP_RECEIPT = Path("/var/lib/rh-alpha-backup/last-success.json")
TOKEN = Path("/etc/rh-alpha/live-control.token")
STREAMS = ("metrics", "replays")
COUNTERS = ("droppedLimit", "droppedQueue", "failed", "writerUnavailable")
SPOOL_MAX_AGE = 300
CAPTURE_ERROR_HOLD = 750
BACKUP_MAX_AGE = 7500


def active(*units):
    return all(
        subprocess.run(["systemctl", "is-active", "--quiet", unit], timeout=5).returncode == 0
        for unit in units
    )


def successful(unit):
    result = subprocess.check_output(
        ["systemctl", "show", "-p", "Result", "--value", unit], text=True, timeout=5,
    )
    return result.strip() == "success"


def capture_streams():
    # A fixed HTTP connection keeps the token out of proxy and redirect paths.
    control = http.client.HTTPConnection("127.0.0.1", 8778, timeout=5)
    try:
        control.request("GET", "/control/health", headers={"Authorization": "Bearer " + TOKEN.read_text().strip()})
        response = control.getresponse()
        if response.status != 200:
            raise ValueError("capture health request failed")
        return json.load(response)["alphaCapture"]
    finally:
        control.close()


def fresh_spool_object(path, now):
    try:
        return now - path.stat().st_mtime < SPOOL_MAX_AGE
    except FileNotFoundError:
        return True  # The uploader committed this file during the census.


def capture(now):
    previous = json.loads(CAPTURE_STATE.read_text()) if CAPTURE_STATE.exists() else {}
    streams = capture_streams()
    counters = {name: [streams[name][key] for key in COUNTERS] for name in STREAMS}
    previous_counters = previous.get("counters", {})
    increased = any(
        current > old
        for name, values in counters.items()
        for current, old in zip(values, previous_counters.get(name, [0] * len(COUNTERS)))
    )
    hold_until = max(previous.get("hold_until", 0), now + CAPTURE_ERROR_HOLD if increased else 0)
    pending = CAPTURE_STATE.with_suffix(".new")
    pending.write_text(json.dumps({"counters": counters, "hold_until": hold_until}))
    pending.replace(CAPTURE_STATE)

    running = all(
        streams[name]["configured"] and streams[name]["started"] and not streams[name]["startFailed"]
        for name in STREAMS
    )
    spools = all(
        spool.is_dir() and all(fresh_spool_object(path, now) for path in spool.glob("*.json.age"))
        for spool in (CAPTURE_SPOOL / name for name in STREAMS)
    )
    return (
        running and spools and now >= hold_until
        and active("rh-alpha-upload@live.timer", "rh-alpha-capture-credentials.timer")
        and successful("rh-alpha-upload@live.service")
    )


def backup(now):
    return (
        BACKUP_RECEIPT.is_file() and 0 <= now - BACKUP_RECEIPT.stat().st_mtime < BACKUP_MAX_AGE
        and active("rh-alpha-live-backup.timer")
        and successful("rh-alpha-live-backup.service")
    )


def check(function, now):
    try:
        return int(function(now))
    except (OSError, ValueError, KeyError, TypeError, http.client.HTTPException, subprocess.SubprocessError):
        return 0


def main():
    now = time.time()
    values = {"CaptureHealthy": check(capture, now), "BackupHealthy": check(backup, now)}
    metrics = [
        {"MetricName": key, "Dimensions": [{"Name": "Environment", "Value": "production"}], "Value": value, "Unit": "Count"}
        for key, value in values.items()
    ]
    subprocess.run(
        [
            "/opt/rh-alpha/verified-capture-cli/bin/aws", "--region", "ca-west-1",
            "cloudwatch", "put-metric-data", "--namespace", "RogueHalla/AlphaOperations",
            "--metric-data", json.dumps(metrics),
        ],
        check=True, timeout=45,
    )
    print(json.dumps(values, sort_keys=True))


if __name__ == "__main__":
    main()

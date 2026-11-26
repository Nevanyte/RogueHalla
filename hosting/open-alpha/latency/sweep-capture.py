#!/usr/bin/env python3
"""Deliver at most eight committed captures and resume the sweep after a restart."""

from bisect import bisect_right
import os
from pathlib import Path
import re
import signal
import subprocess
import sys

UPLOADER = Path(__file__).with_name("upload-capture.sh")
CAPTURE = re.compile(r"(?:replay-[a-f0-9]{32}|metrics-[a-f0-9]{32}-[0-9]+)\.json\.age")
MAX_ATTEMPTS = 8
PER_OBJECT_SECONDS = 15


def upload(bucket, owner, environment, file):
    with subprocess.Popen(
        [UPLOADER, bucket, owner, environment, file],
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, start_new_session=True,
    ) as process:
        try:
            return process.wait(timeout=PER_OBJECT_SECONDS) == 0
        except subprocess.TimeoutExpired:
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            process.wait()
            return False


def sweep(bucket, owner, environment, state, spools):
    cursor_file = state / "cursor"
    cursor = cursor_file.read_text().strip() if cursor_file.exists() else ""
    candidates = {}
    for index, spool in enumerate(spools):
        if not spool.is_absolute() or not spool.is_dir() or spool.is_symlink():
            raise ValueError("capture spool must be an absolute, private directory")
        for path in spool.iterdir():
            if CAPTURE.fullmatch(path.name) and path.is_file() and not path.is_symlink():
                candidates[f"{index}/{path.name}"] = path
    order = sorted(candidates)
    start = bisect_right(order, cursor)
    attempts = (order[start:] + order[:start])[:MAX_ATTEMPTS]
    delivered = 0
    for key in attempts:
        path = candidates[key]
        if upload(bucket, owner, environment, path):
            path.unlink()
            delivered += 1
        temporary = state / "cursor.tmp"
        temporary.write_text(key + "\n")
        temporary.replace(cursor_file)
    remaining = len(attempts) - delivered
    print(f"capture sweep: confirmed={delivered} unresolved={remaining} queued={len(order)-delivered}")
    return 1 if remaining else 0


def main():
    if len(sys.argv) != 4 or sys.argv[1] not in ("staging", "live"):
        raise ValueError("expected environment, replay spool and metrics spool")
    return sweep(
        os.environ["RH_CAPTURE_BUCKET"], os.environ["RH_CAPTURE_OWNER"], sys.argv[1],
        Path(os.environ["STATE_DIRECTORY"]), [Path(argument) for argument in sys.argv[2:]],
    )


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ValueError, KeyError):
        print("capture sweep: check local spool, state and service configuration", file=sys.stderr)
        sys.exit(1)

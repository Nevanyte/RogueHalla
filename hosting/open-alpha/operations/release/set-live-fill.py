#!/usr/bin/env python3
"""Switch the admitted two-room native release between the original 5s and rehearsed 2s fill windows.

Install beside the vetted set-live-native.py. Use check 2, then apply 2.
Return to the original window with check 5, then apply 5. If startup failed,
rollback retries a guarded return to 5. Restore 5 before any native rollback.
"""
import argparse
from dataclasses import dataclass
import hashlib
import http.client
import importlib.util
import os
from pathlib import Path
import subprocess
import sys
import time

sys.dont_write_bytecode = True  # A read-only check must also leave the installed scripts untouched.
spec = importlib.util.spec_from_file_location("live_native", Path(__file__).with_name("set-live-native.py"))
native = importlib.util.module_from_spec(spec)
spec.loader.exec_module(native)

FAILURES = (native.Guard, OSError, ValueError, http.client.HTTPException, subprocess.SubprocessError)
CAP = 2
OVERRIDE = " --fill-seconds 2"


def drop_in(body):
    return f"{native.NATIVE_COMMENT}\n[Service]\nExecStart=\nExecStart={body}\n".encode()


@dataclass(frozen=True)
class FillCommands:
    original: str
    on_disk: int

    def body(self, fill):
        return self.original + (OVERRIDE if fill == 2 else "")

    def argv(self, fill):
        return tuple(self.body(fill).split())


def commands(host):
    native.require(host.native.is_file() and host.record.is_file(),
                   "Apply the approved two-room native rollout before changing fill")
    native.trusted_file(host.native)
    body, _ = native.split_exec(host.native)
    native.require(host.native.read_bytes() == drop_in(body), "Unexpected native ExecStart override or directives")
    on_disk = 2 if body.endswith(OVERRIDE) else 5
    original = body.removesuffix(OVERRIDE) if on_disk == 2 else body
    pair = FillCommands(original, on_disk)
    argv = pair.argv(5)
    native.require(argv[0] == str(host.release(native.NEW)), "Unexpected live release executable")
    native.flag(argv, "--runtime", "source-arena")
    native.flag(argv, "--max-rooms", str(CAP))
    native.require(not any(arg == "--fill-seconds" or arg.startswith("--fill-seconds=") for arg in argv),
                   "Original/native fill is not the admitted implicit five-second default")
    expected_record = {"v": 1, "prior": native.OLD, "candidate": native.NEW,
                       "original_unit_files": native.baseline(host),
                       "candidate_drop_in_sha256": hashlib.sha256(drop_in(original)).hexdigest()}
    # The native tool's own record gives the exact original command and config, without
    # duplicating it or storing any credential/environment values in a new record.
    native.guarded_record(host, native.Commands((), argv, drop_in(original), expected_record))
    return pair


def preflight(host, pair):
    native.verified_artifacts(host, native.NEW)
    argv = pair.argv(pair.on_disk)
    native.loaded_command(host, argv)
    native.healthy(host, native.NEW, CAP, argv)
    native.empty_lobby(host, CAP)


def running_fill(host, pair, state):
    exe, argv = host.running(state.pid)
    for fill in (2, 5):
        expected = pair.argv(fill)
        if exe == Path(expected[0]) and argv == expected:
            native.loaded_command(host, expected)
            return fill
    raise native.Guard("Unexpected live argv; left the process running")


def switch(host, target, sleep, verified=None):
    pair = commands(host)
    if verified is not None:
        native.require(pair == verified, "Live unit files changed after preflight")
    state = host.unit_state()
    if state.active == "active" and state.pid > 0:
        current = running_fill(host, pair, state)
        if current == target and pair.on_disk == target:
            try:
                native.healthy(host, native.NEW, CAP, pair.argv(current))
            except FAILURES:
                pass  # Recovery can drain an unhealthy process if its authenticated occupancy checks work.
            else:
                native.empty_lobby(host, CAP)
                return
        native.drain(host, native.NEW, CAP, pair.argv(current), sleep)
    else:
        native.require(state.stopped, "The native service is transitioning; wait and retry the guarded command")
    native.require(host.unit_state().stopped, "The native service is still running")
    host.verify_ports_closed()
    native.require(commands(host) == pair, "Live unit files changed during drain")
    native.atomic_write(host.native, drop_in(pair.body(target)), 0o644)
    native.start(host, pair.argv(target))
    native.wait_healthy(host, native.NEW, CAP, pair.argv(target), sleep)


def operate(host, action, target=5, sleep=time.sleep):
    native.require(target in (2, 5), "Fill must be 2s or 5s")
    if action == "check":
        pair = commands(host)
        preflight(host, pair)
        return f"LIVE_FILL_PREFLIGHT current={pair.on_disk}s target={target}s roomLimit={CAP} lobby=empty"

    with host.locked():
        if action == "rollback":
            native.verified_artifacts(host, native.NEW)
            switch(host, 5, sleep)
            return f"LIVE_FILL_RESTORED fill=5s roomLimit={CAP}"

        pair = commands(host)
        preflight(host, pair)
        if pair.on_disk == target:
            return f"LIVE_FILL_ALREADY_READY fill={target}s roomLimit={CAP}"
        try:
            switch(host, target, sleep, verified=pair)
        except FAILURES as exc:
            try:
                switch(host, pair.on_disk, sleep)
            except FAILURES as blocked:
                raise native.Guard(f"Fill change failed ({native.safe_error(exc)}). "
                                   f"Recovery deferred ({native.safe_error(blocked)}); "
                                   "retry set-live-fill.py rollback when the unit is empty and reachable.") from None
            raise native.Guard(f"Fill change failed ({native.safe_error(exc)}). "
                               f"The prior {pair.on_disk}s fill is healthy.") from None
        return f"LIVE_FILL_READY fill={target}s roomLimit={CAP}"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    actions = parser.add_subparsers(dest="action", required=True)
    for action in ("check", "apply"):
        sub = actions.add_parser(action)
        sub.add_argument("seconds", type=int, choices=(2, 5))
    actions.add_parser("rollback")
    args = parser.parse_args()
    os.umask(0o077)
    try:
        native.require(os.geteuid() == 0, "Run this guarded command as root on the Source host")
        print(operate(native.Host(), args.action, getattr(args, "seconds", 5)))
    except FAILURES as exc:
        print("Native fill: " + native.safe_error(exc), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
"""Guarded host-local checkout of the Saturday native build.

Run as root on the live Source host: set-live-native.py check, then set-live-native.py apply
Return to the recorded Source build and one room: set-live-native.py rollback
Both commands require an empty lobby before asking a live process to drain.
"""
import argparse
from contextlib import contextmanager
from dataclasses import dataclass
import fcntl
import hashlib
import http.client
import json
import os
from pathlib import Path
import re
import socket
import stat
import subprocess
import sys
import tempfile
import time

UNIT = "rh-alpha-live-source.service"
OLD = "dbaa701be6b0eefbee1c792fe8ee80e475baefc9"
NEW = "a553eb1231df4171bba587488f43018429aaecca"
IDENTITY = {"protocol": 24, "contentHash": "8a4bf0ce37b406f2"}
OLD_SERVER_SHA = "2c30342cd3a8d6f3e4bd87017e9ba40b72ae0b22e7932861adc531ac1adc3f72"
NEW_SHA = {
    "rh-server": "2c93c36124cd3999b1ca1e2a1a7df068fa92cc2ed18ca6e2d5b7877b20280039",
    "rh-headless": "62efe022ac2f52600b608a405d590ec1d6973ace20e5ae1030f63851cc6b837e",
}
PUBLIC, CONTROL = 8777, 8778
CAPACITY_COMMENT = "## RogueHalla Saturday room capacity"
NATIVE_COMMENT = "## RogueHalla Saturday native rollout " + NEW


class Guard(RuntimeError):
    pass


def require(condition, message):
    if not condition:
        raise Guard(message)


def digest(path):
    sha = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            sha.update(chunk)
    return sha.hexdigest()


def trusted_file(path, executable=False):
    mode = path.lstat()
    require(stat.S_ISREG(mode.st_mode) and mode.st_uid == os.geteuid() and not mode.st_mode & 0o022,
            "Release or operator state is not a trusted regular file")
    if executable:
        require(mode.st_mode & 0o111, "Native artifact is not executable")


def atomic_write(path, body, mode):
    descriptor, temporary = tempfile.mkstemp(prefix="." + path.name + ".", dir=path.parent)
    try:
        with os.fdopen(descriptor, "wb") as stream:
            os.fchmod(stream.fileno(), mode)
            stream.write(body)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
        sync_dir(path.parent)
    finally:
        Path(temporary).unlink(missing_ok=True)


def sync_dir(path):
    fd = os.open(path, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(fd)
    finally:
        os.close(fd)


@dataclass(frozen=True)
class UnitState:
    active: str
    result: str
    pid: int

    @property
    def stopped(self):
        return self.pid == 0 and self.active in {"inactive", "failed"}

    @property
    def drained(self):
        return self.pid == 0 and self.active == "inactive" and self.result == "success"


@dataclass(frozen=True)
class Commands:
    old: tuple
    new: tuple
    drop_in: bytes
    record: dict


class Host:
    """All paths are host-local. The injected root is only used by the local test harness."""
    def __init__(self, root=Path("/")):
        self.root = root
        self.config = root / "etc/systemd/system" / (UNIT + ".d")
        self.split = self.config / "80-split.conf"
        self.cap = self.config / "91-capacity.conf"
        self.native = self.config / "92-saturday-native.conf"
        self.state = root / "opt/rh-alpha/saturday-ops"
        self.record = self.state / ("native-" + NEW + ".json")
        self.lock = root / "run/rh-alpha-capacity.lock"

    @staticmethod
    def run(program, *args, timeout=15):
        return subprocess.run(["/usr/bin/" + program, *args], check=True,
                              stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True, timeout=timeout).stdout

    def systemctl(self, *args, timeout=15):
        return self.run("systemctl", *args, timeout=timeout)

    def unit_props(self, *names):
        args = ["--property=" + name for name in names]
        output = self.systemctl("show", *args, UNIT)
        props = dict(line.split("=", 1) for line in output.splitlines() if "=" in line)
        require(all(name in props for name in names), "Incomplete systemd unit state")
        return props

    def unit_state(self):
        props = self.unit_props("ActiveState", "Result", "MainPID")
        return UnitState(props["ActiveState"], props["Result"], int(props["MainPID"]))

    def unit_paths(self):
        return [self.root / part.lstrip("/") for part in self.run("systemd-analyze", "unit-paths").splitlines()]

    def release(self, build, filename="rh-server"):
        return self.root / "opt/rh-alpha/releases" / build / filename

    def running(self, pid):
        proc = self.root / "proc" / str(pid)
        path = Path(os.readlink(proc / "exe"))
        argv = tuple((proc / "cmdline").read_bytes().rstrip(b"\0").decode().split("\0"))
        return path, argv

    def request(self, port, path, post=False):
        headers = {"Accept": "application/json"}
        if port == CONTROL:
            # The existing token stays on the guest and is only sent to its fixed loopback listener.
            token_path = self.root / "etc/rh-alpha/live-control.token"
            require(not token_path.is_symlink() and token_path.is_file(), "Control token file is unavailable")
            token = token_path.read_text().strip()
            require(re.fullmatch(r"[!-~]+", token), "Control token file has an invalid format")
            headers["Authorization"] = "Bearer " + token
        if post:
            headers["Content-Type"] = "application/json"
        with_http = http.client.HTTPConnection("127.0.0.1", port, timeout=4)
        try:
            with_http.request("POST" if post else "GET", path, b'{"v":1}' if post else None, headers)
            response = with_http.getresponse()
            allowed = (200, 503) if path == "/control/health" else (200,)
            require(response.status in allowed, f"{path} returned HTTP {response.status}")
            payload = response.read(1024 * 1024 + 1)
            require(len(payload) <= 1024 * 1024, f"{path} response exceeded its bound")
            value = json.loads(payload)
            require(isinstance(value, dict), f"{path} returned an invalid response")
            return value
        finally:
            with_http.close()

    def verify_ports_closed(self):
        for port in (PUBLIC, CONTROL):
            try:
                with socket.create_connection(("127.0.0.1", port), timeout=2):
                    raise Guard(f"Live port {port} is occupied outside the stopped unit")
            except ConnectionRefusedError:
                continue

    @contextmanager
    def locked(self):
        with os.fdopen(os.open(self.lock, os.O_CREAT | os.O_RDWR | os.O_NOFOLLOW, 0o600)) as stream:
            fcntl.flock(stream.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
            yield


def verified_artifacts(host, build):
    hashes = NEW_SHA if build == NEW else {"rh-server": OLD_SERVER_SHA}
    for name, expected in hashes.items():
        path = host.release(build, name)
        require(path.resolve() == path, "Immutable release contains a symlink")
        trusted_file(path, executable=True)
        require(digest(path) == expected, "Checksum mismatch for " + name + " at " + build)


def split_exec(path):
    # The installed 80-split command uses literal, unquoted systemd tokens.
    # Refuse syntax that would need systemd expansion or reinterpretation.
    text = path.read_text()
    require(not re.search(r"\\\n", text), "80-split.conf contains a continuation")
    lines = re.findall(r"^\s*ExecStart\s*=(.*)$", text, re.MULTILINE)
    require(lines and all(not line.strip() for line in lines[:-1]), "80-split.conf has unexpected ExecStart entries")
    body = lines[-1]
    require(re.fullmatch(r"[ \t]*/[A-Za-z0-9/._-]+(?:[ \t]+[^\s'\"\\$%;]+)+[ \t]*", body),
            "80-split.conf command uses unexpected systemd syntax")
    return body, list(re.finditer(r"[^ \t]+", body))


def flag(argv, key, expected):
    require(argv.count(key) == 1, "Unexpected argument " + key)
    i = argv.index(key) + 1
    require(i < len(argv) and argv[i] == expected, "Unexpected live value for " + key)
    return i


def baseline(host):
    props = host.unit_props("FragmentPath", "DropInPaths")
    fragment = Path(props["FragmentPath"])
    require(fragment.is_absolute() and fragment.is_file(), "Live unit fragment is unavailable")
    paths = {Path(part) for part in props["DropInPaths"].split()} | {host.split}
    directories = ("service.d", "rh-.service.d", "rh-alpha-.service.d",
                   "rh-alpha-live-.service.d", UNIT + ".d")
    for directory in host.unit_paths():
        for name in directories:
            paths.update((directory / name).glob("*.conf"))
    paths.discard(host.native)
    fingerprints = {}
    for path in sorted(paths):
        if path != host.split and path != host.cap:
            body = path.read_bytes().replace(bytes((92, 13, 10)), b" ").replace(bytes((92, 10)), b" ")
            require(not re.search(rb"^\s*ExecStart\s*=", body, re.MULTILINE),
                    "Unexpected drop-in defines ExecStart: " + path.name)
        fingerprints[str(path)] = digest(path)
    fingerprints[str(fragment)] = digest(fragment)
    return fingerprints


def commands(host):
    body, words = split_exec(host.split)
    old = tuple(word[0] for word in words)
    require(old[0] == str(host.release(OLD)), "80-split.conf does not select the expected live release")
    flag(old, "--runtime", "source-arena")
    flag(old, "--bind", f"127.0.0.1:{PUBLIC}")
    flag(old, "--control", f"127.0.0.1:{CONTROL}")
    flag(old, "--control-token-file", str(host.root / "etc/rh-alpha/live-control.token"))
    require("--control-insecure-loopback" not in old, "Live unit has unauthenticated control")
    rooms = flag(old, "--max-rooms", "1")
    # Ordinary Source rooms use the build's eight-seat arena and the old map and data paths.
    for forbidden in ("--training", "--max-players", "--require-admit"):
        require(forbidden not in old, "Live unit has unexpected arena admission arguments")
    if host.cap.exists() or host.cap.is_symlink():
        capacity = f"{CAPACITY_COMMENT}\n[Service]\nExecStart=\nExecStart={body}\n".encode()
        require(host.cap.read_bytes() == capacity, "Unexpected 91-capacity.conf; expected the original single-room command")
    new = list(old)
    new[0], new[rooms] = str(host.release(NEW)), "2"
    for i in (rooms, 0):
        begin, end = words[i].span()
        body = body[:begin] + new[i] + body[end:]
    drop_in = f"{NATIVE_COMMENT}\n[Service]\nExecStart=\nExecStart={body}\n".encode()
    record = {"v": 1, "prior": OLD, "candidate": NEW, "original_unit_files": baseline(host),
              "candidate_drop_in_sha256": hashlib.sha256(drop_in).hexdigest()}
    if host.native.exists() or host.native.is_symlink():
        trusted_file(host.native)
        require(host.native.read_bytes() == drop_in, "Unexpected native override; refusing to replace it")
        require(host.record.exists(), "Native override exists without the guarded rollback record")
    return Commands(old, tuple(new), drop_in, record)


def loaded_command(host, argv, clean=True):
    props = host.unit_props("ExecStart", "NeedDaemonReload")
    if clean:
        require(props["NeedDaemonReload"] == "no", "Systemd has unloaded unit edits")
    lines = re.findall(r"\{ path=(.*?) ; argv\[\]=(.*?) ; ignore_errors=no ;", props["ExecStart"])
    require(len(lines) == 1 and props["ExecStart"].count("{ path=") == 1 and
            lines[0][0] == argv[0] and tuple(lines[0][1].split()) == argv,
            "Effective live unit command does not match the guarded argv")


def running_command(host, state, argv):
    require(state.active == "active" and state.pid > 0, "Live unit is not active")
    exe, args = host.running(state.pid)
    require(exe == Path(argv[0]) and args == argv, "Running Source command does not match the expected release")


def identity(host, build):
    info = host.request(PUBLIC, "/info")
    require(all(info.get(key) == value for key, value in IDENTITY.items()), "Public protocol or content identity changed")
    ctl = host.request(CONTROL, "/control/health")
    require(ctl.get("v") == 1 and ctl.get("build") == build, "Authenticated control build identity changed")
    return info, ctl


def healthy(host, build, cap, argv):
    running_command(host, host.unit_state(), argv)
    info, ctl = identity(host, build)
    ready = host.request(PUBLIC, "/readyz")
    require(ready.get("ok") is True and ready.get("storage") == "ok" and
            all(value.get("state") == "ready" for value in (info, ctl, ready)), "Source or PostgreSQL is not ready")
    capture = ctl.get("alphaCapture")
    require(isinstance(capture, dict), "Capture writer health is unavailable")
    for name in ("metrics", "replays"):
        writer = capture.get(name)
        require(isinstance(writer, dict) and writer.get("configured") is True and writer.get("started") is True and
                writer.get("startFailed") is False and count_is(writer, "writerUnavailable", 0),
                f"{name} capture writer is not available")
    lobby = host.request(PUBLIC, "/api/lobby")
    require(count_is(lobby, "arenaLimit", cap), "Unexpected live arena room capacity")


def count_is(payload, key, count):
    return type(payload.get(key)) is int and payload[key] == count


def empty_lobby(host, cap):
    lobby = host.request(PUBLIC, "/api/lobby")
    require(lobby.get("arenaAvailable") is True and count_is(lobby, "arenaLimit", cap) and
            count_is(lobby, "arenaRoomCount", 0) and lobby.get("rooms") == [] and
            lobby.get("waiting") == [] and count_is(lobby, "onlineCount", 0),
            "Live lobby or room occupied; left the process running")
    # Check the authoritative seat and room counters immediately before the drain request.
    info = host.request(CONTROL, "/control/info")
    require(info.get("state") == "ready" and count_is(info, "maxRooms", cap) and
            count_is(info, "rooms", 0) and count_is(info, "players", 0),
            "Live admissions or rooms are present; left the process running")


def poll(check, description, seconds, sleep=time.sleep):
    deadline = time.monotonic() + seconds
    for i in range(seconds + 1):
        if check():
            return
        if i == seconds or time.monotonic() >= deadline:
            raise Guard(description)
        sleep(1)


def drain(host, build, cap, argv, sleep):
    state = host.unit_state()
    running_command(host, state, argv)
    info, ctl = identity(host, build)
    if ctl.get("state") == "ready":
        require(info.get("state") == "ready" and count_is(ctl, "rooms", 0), "Live control reports open rooms")
        require(host.unit_state() == state, "Live process changed before drain")
        empty_lobby(host, cap)
        try:
            response = host.request(CONTROL, "/control/drain", post=True)
            require(response.get("v") == 1 and response.get("state") in {"draining", "terminated"},
                    "Control did not acknowledge graceful drain")
        except (OSError, http.client.HTTPException):
            # An empty server can exit before its HTTP reply. Still require a clean systemd exit.
            pass
    else:
        require(ctl.get("state") in {"draining", "terminated"}, "Source state does not allow guarded drain")

    def exited():
        now = host.unit_state()
        require(now.pid in {0, state.pid}, "Live process changed while draining")
        require(not now.stopped or now.drained, "Live Source exit was not clean")
        return now.drained

    poll(exited, "Graceful drain still in progress; no process was forcibly stopped", 180, sleep)


def wait_healthy(host, build, cap, argv, sleep):
    last = "unknown"

    def check():
        nonlocal last
        try:
            healthy(host, build, cap, argv)
            return True
        except (Guard, OSError, ValueError, http.client.HTTPException, subprocess.SubprocessError) as exc:
            last = safe_error(exc)
            return False

    try:
        poll(check, "Source startup did not pass health verification", 75, sleep)
    except Guard as exc:
        raise Guard(f"{exc} ({last})") from None


def current_release(host, state, pair):
    exe, _ = host.running(state.pid)
    if exe == Path(pair.old[0]):
        return OLD, 1, pair.old
    if exe == Path(pair.new[0]):
        return NEW, 2, pair.new
    raise Guard("Unexpected live Source executable; left it running")


def guarded_record(host, pair, create=False):
    if host.record.exists() or host.record.is_symlink():
        trusted_file(host.record)
        require(json.loads(host.record.read_text()) == pair.record, "Original unit files differ from the rollback record")
    elif create:
        host.state.mkdir(mode=0o700, exist_ok=True)
        mode = host.state.lstat()
        require(stat.S_ISDIR(mode.st_mode) and mode.st_uid == os.geteuid() and not mode.st_mode & 0o077,
                "Operator state directory is not private")
        atomic_write(host.record, (json.dumps(pair.record, sort_keys=True) + "\n").encode(), 0o600)
    else:
        raise Guard("No guarded rollback record found")


def start(host, argv):
    host.verify_ports_closed()
    host.systemctl("daemon-reload")
    loaded_command(host, argv)
    host.systemctl("start", UNIT, timeout=45)


def rollback(host, pair, sleep):
    require(commands(host) == pair, "Live unit files changed; rollback deferred")
    guarded_record(host, pair)
    verified_artifacts(host, OLD)
    state = host.unit_state()
    if state.active == "active":
        build, cap, argv = current_release(host, state, pair)
        if build == OLD and not host.native.exists():
            loaded_command(host, pair.old)
            info, ctl = identity(host, OLD)
            if info.get("state") == "ready" and ctl.get("state") == "ready":
                healthy(host, OLD, 1, pair.old)
                return
        drain(host, build, cap, argv, sleep)
    else:
        require(state.stopped, "Live unit is transitioning; wait and retry guarded rollback")
    require(host.unit_state().stopped, "Live unit is still running; rollback deferred")
    host.verify_ports_closed()
    require(commands(host) == pair, "Live unit files changed; rollback deferred")
    if host.native.exists():
        host.native.unlink()
        sync_dir(host.native.parent)
    start(host, pair.old)
    wait_healthy(host, OLD, 1, pair.old, sleep)


def preflight(host, pair):
    verified_artifacts(host, OLD)
    verified_artifacts(host, NEW)
    if host.native.exists():
        guarded_record(host, pair)
        loaded_command(host, pair.new)
        healthy(host, NEW, 2, pair.new)
        return NEW, 2
    loaded_command(host, pair.old)
    healthy(host, OLD, 1, pair.old)
    return OLD, 1


def operate(host, action, sleep=time.sleep):
    if action == "check":
        build, cap = preflight(host, commands(host))
        empty_lobby(host, cap)
        return f"LIVE_PREFLIGHT_OK build={build} roomLimit={cap} storage=ok capture=both lobby=empty"

    with host.locked():
        pair = commands(host)
        if action == "rollback":
            rollback(host, pair, sleep)
            return "LIVE_RESTORED build=" + OLD + " roomLimit=1"

        build, cap = preflight(host, pair)
        if build == NEW:
            return "LIVE_ALREADY_READY build=" + NEW + " roomLimit=2"
        empty_lobby(host, cap)
        guarded_record(host, pair, create=True)
        try:
            drain(host, OLD, 1, pair.old, sleep)
            require(commands(host) == pair, "Live unit files changed during drain")
            host.verify_ports_closed()
            atomic_write(host.native, pair.drop_in, 0o644)
            start(host, pair.new)
            wait_healthy(host, NEW, 2, pair.new, sleep)
        except (Guard, OSError, ValueError, http.client.HTTPException, subprocess.SubprocessError) as exc:
            try:
                rollback(host, pair, sleep)
            except (Guard, OSError, ValueError, http.client.HTTPException, subprocess.SubprocessError) as blocked:
                raise Guard(f"Rollout failed ({safe_error(exc)}). Rollback deferred ({safe_error(blocked)}); "
                            "retry set-live-native.py rollback when the unit is empty and reachable.") from None
            raise Guard(f"Rollout failed ({safe_error(exc)}). Original native release and one-room cap are healthy.") from None
        return "LIVE_READY build=" + NEW + " roomLimit=2 capture=both"


def safe_error(exc):
    # Avoid stdout, stderr, response bodies, argv and header details in operator output.
    return str(exc) if isinstance(exc, Guard) else type(exc).__name__


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("check", "apply", "rollback"))
    args = parser.parse_args()
    os.umask(0o077)
    try:
        require(os.geteuid() == 0, "Run this guarded command as root on the Source host")
        print(operate(Host(), args.action))
    except (Guard, OSError, ValueError, http.client.HTTPException, subprocess.SubprocessError) as exc:
        print("Native rollout: " + safe_error(exc), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())

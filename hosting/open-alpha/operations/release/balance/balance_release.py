#!/usr/bin/env python3
"""Install and switch the reviewed Saturday balance candidate on the Calgary guest."""
import argparse
import base64
from contextlib import contextmanager
from dataclasses import dataclass
import fcntl
import grp
import hashlib
import http.client
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import socket
import stat
import subprocess
import tarfile
import tempfile
import time

OLD = "a553eb1231df4171bba587488f43018429aaecca"
NEW = "0fac6aa75e9bcbc55b701b732e1169702c194d8f"
OLD_CONTENT = "8a4bf0ce37b406f2"
NEW_CONTENT = "cb811d19ebed6dfe"
OLD_NATIVE_SHA = "2c93c36124cd3999b1ca1e2a1a7df068fa92cc2ed18ca6e2d5b7877b20280039"
LIVE_INDEX_SHA = "83bbc43c1acb398fa6dbf54f15fe239e03611ae2fc54e52b438efad8371f5f5f"
PROTOCOL = 24
MIGRATIONS_TREE = "0d6df349d6cc79b6e627c58a22589dedbcdfbf85"
CAPACITY = 2
BUILD_DIRS = {"Build", "StreamingAssets", "TemplateData", "Licenses"}
WEB_FRONT = {"index.html", "alpha-gameplay-data.html", "favicon.ico"}
NATIVES = {"rh-server", "rh-headless", "seed1.map.json"}
LOADERS = {f"loaders/{site}-{mode}.html" for site in ("staging", "live") for mode in ("split", "same-origin")}
MANIFEST = "release-manifest.json"
OVERRIDE = "99-saturday-balance.conf"
COMMAND_RE = re.compile(r"\{ path=(.*?) ; argv\[\]=(.*?) ; ignore_errors=no ;")
MAX_ARCHIVE_BYTES = 512 * 1024 * 1024
MAX_EXPANDED_BYTES = 2 * 1024 * 1024 * 1024
MAX_FILES = 4000


@dataclass(frozen=True)
class Target:
    unit: str
    front_name: str
    web_tag: str
    public_port: int
    control_port: int
    token_name: str
    edge_dir: str
    hostname: str
    backend_hostname: str
    user: str
    spool: str
    capture: str
    pg_socket: str


TARGETS = {
    "stage": Target("rh-alpha-source.service", "staging-split", "staging", 7777, 7778, "control.token",
                    "rh-alpha-edge", "alpha-staging.roguehalla.com", "latency-staging.roguehalla.com",
                    "rh_alpha", "spool-source", "alpha-capture-staging", "rh-alpha-pg"),
    "production": Target("rh-alpha-live-source.service", "live-split", "live", 8777, 8778, "live-control.token",
                         "rh-alpha-live-edge", "play.roguehalla.com", "game.roguehalla.com",
                         "rh_alpha_live", "spool-live", "alpha-capture-live", "rh-alpha-live-pg"),
}


class Guard(RuntimeError):
    pass


FAILURES = (Guard, OSError, ValueError, KeyError, TypeError, http.client.HTTPException,
            subprocess.SubprocessError, tarfile.TarError)


def require(condition, reason):
    if not condition:
        raise Guard(reason)


def hexdigest(data):
    return hashlib.sha256(data).hexdigest()


def file_digest(path):
    with path.open("rb") as stream:
        checksum = hashlib.sha256()
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            checksum.update(chunk)
        return checksum.hexdigest()


def unique_object(pairs):
    result = dict(pairs)
    require(len(pairs) == len(result), "Duplicate JSON keys")
    return result


def json_bytes(data):
    return (json.dumps(data, sort_keys=True, indent=2) + "\n").encode()


def present(path):
    return os.path.lexists(path)


def trusted(path, *, directory=False, executable=False):
    info = path.lstat()
    check_type = stat.S_ISDIR if directory else stat.S_ISREG
    require(path.resolve() == path and check_type(info.st_mode) and
            info.st_uid == os.geteuid() and not info.st_mode & 0o022,
            "Untrusted host path: " + str(path))
    if executable:
        require(info.st_mode & 0o111, "Expected executable: " + str(path))
    return info


def read_trusted(path, limit=8 * 1024 * 1024):
    info = trusted(path)
    require(info.st_size <= limit, "Trusted input is larger than expected")
    with path.open("rb") as stream:
        return stream.read(limit + 1)


def sync_dir(path):
    descriptor = os.open(path, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    try:
        os.fsync(descriptor)
    finally:
        os.close(descriptor)


def private_dir(path):
    path.mkdir(mode=0o700, exist_ok=True)
    require(stat.S_IMODE(trusted(path, directory=True).st_mode) == 0o700, "Expected private operator state directory")


def atomic_write(path, data, mode, gid=None):
    descriptor, name = tempfile.mkstemp(prefix="." + path.name + ".", dir=path.parent)
    pending = Path(name)
    try:
        with os.fdopen(descriptor, "wb") as stream:
            if gid is not None:
                os.fchown(stream.fileno(), os.geteuid(), gid)
            os.fchmod(stream.fileno(), mode)
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(pending, path)
        sync_dir(path.parent)
    finally:
        pending.unlink(missing_ok=True)


def check_hash(value):
    require(isinstance(value, str) and re.fullmatch(r"[a-f0-9]{64}", value), "Expected lowercase SHA-256")
    return value


def allowed_name(name):
    require(isinstance(name, str) and len(name) <= 500, "Invalid archive path")
    parts = name.split("/")
    require(all(re.fullmatch(r"[A-Za-z0-9_.()+ @-]+", p) and not p.startswith(".") for p in parts),
            "Invalid or hidden archive member")
    if len(parts) == 1:
        return name in NATIVES | {MANIFEST, "web", "loaders"}
    if parts[0] == "loaders":
        return name in LOADERS
    return parts[0] == "web" and (parts[1] in BUILD_DIRS or (len(parts) == 2 and parts[1] in WEB_FRONT))


def inspect_manifest(tree):
    raw = read_trusted(tree / MANIFEST)
    manifest = json.loads(raw, object_pairs_hook=unique_object)
    require(manifest["releaseSource"] == NEW and manifest["protocol"] == PROTOCOL and
            manifest["contentHash"] == NEW_CONTENT and manifest["web"]["verifiedBuildSource"] == NEW and
            manifest["web"]["bakedContentHash"] == NEW_CONTENT and manifest["native"]["build"] == NEW and
            manifest["native"]["target"] == "x86_64-unknown-linux-gnu",
            "Manifest is not the approved paired native and Unity release")
    require(manifest["databaseMigrations"]["gitTree"] == MIGRATIONS_TREE, "Unexpected database migration tree")
    files = manifest["files"]
    require(isinstance(files, dict) and len(files) < MAX_FILES, "Invalid manifest file inventory")
    require(NATIVES | LOADERS | {"web/index.html", "web/alpha-gameplay-data.html"} <= files.keys(),
            "Missing native, Unity or fallback inputs")
    for name, entry in files.items():
        require(allowed_name(name) and name != MANIFEST and isinstance(entry, dict) and
                type(entry.get("bytes")) is int and 0 < entry["bytes"] <= MAX_EXPANDED_BYTES,
                "Invalid manifest file entry")
        check_hash(entry.get("sha256"))
    for suffix in (".loader.js", ".framework.js", ".data", ".wasm"):
        matches = [name for name in files if name.startswith("web/Build/") and name.endswith(suffix)]
        require(len(matches) == 1 and matches[0] + ".br" in files, "Missing unique compressed startup asset")
    check_tree(tree, {**files, MANIFEST: {"sha256": hexdigest(raw), "bytes": len(raw)}})
    for name in ("rh-server", "rh-headless"):
        with (tree / name).open("rb") as stream:
            elf = stream.read(20)
        require(elf[:6] == b"\x7fELF\x02\x01" and elf[18:20] == b"\x3e\x00",
                "Native file is not a Linux x86-64 executable: " + name)
    for target in TARGETS.values():
        for mode in ("split", "same-origin"):
            verify_loader(read_trusted(tree / f"loaders/{target.web_tag}-{mode}.html"), target, mode, NEW,
                          has_template=(tree / "web/TemplateData").is_dir())
    return manifest, hexdigest(raw)


def check_tree(root, files, gid=None):
    trusted(root, directory=True)
    wanted_dirs = {directory.as_posix() for name in files
                   for directory in PurePosixPath(name).parents if directory != PurePosixPath(".")}
    found = set()
    for dirname, dirs, filenames in os.walk(root):
        folder = Path(dirname)
        for name in dirs:
            child = folder / name
            info = trusted(child, directory=True)
            require(child.relative_to(root).as_posix() in wanted_dirs, "Unexpected directory in release inventory")
            if gid is not None:
                require((info.st_gid, stat.S_IMODE(info.st_mode)) == (gid, 0o550), "Unexpected Web directory permissions")
        for name in filenames:
            child = folder / name
            rel = child.relative_to(root).as_posix()
            info = trusted(child, executable=rel in {"rh-server", "rh-headless"})
            require(rel in files and info.st_size == files[rel]["bytes"] and file_digest(child) == files[rel]["sha256"],
                    "Release artifact differs: " + rel)
            if gid is not None:
                require((info.st_gid, stat.S_IMODE(info.st_mode)) == (gid, 0o440), "Unexpected Web file permissions")
            found.add(rel)
    require(found == set(files), "Incomplete release inventory")


def verify_loader(data, target, mode, build, has_template=False):
    marker = f'<meta name="roguehalla-backend" content="{mode}">'.encode()
    require(data.count(marker) == 1 and b"/alpha-gameplay-data.html" in data,
            "Unity loader does not declare the expected routing mode and gameplay notice")
    require(not re.search(rb'(?:href|src)="TemplateData/', data), "Unity loader references unversioned template assets")
    directories = ("Build", "StreamingAssets", "TemplateData") if has_template else ("Build", "StreamingAssets")
    for directory in directories:
        require(f"/alpha-web/{build}/{directory}".encode() in data, "Unity loader references a different client build")
    endpoint = target.backend_hostname.encode()
    if mode == "split":
        require(b"https://" + endpoint in data and b"wss://" + endpoint in data, "Unity loader points at the wrong game ingress")
    else:
        require(endpoint not in data, "Same-origin fallback contains the split endpoint")


def install_archive(archive, expected, parent):
    check_hash(expected)
    trusted(parent, directory=True)
    # Verify the same already-open, trusted archive that is used for extraction.
    info = trusted(archive)
    require(info.st_size < MAX_ARCHIVE_BYTES, "Archive exceeded the upload bound")
    pending = Path(tempfile.mkdtemp(prefix=".balance-pending-", dir=parent))
    try:
        with archive.open("rb") as stream:
            checksum = hashlib.sha256()
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                checksum.update(chunk)
            require(checksum.hexdigest() == expected, "Wrong release archive SHA-256")
            stream.seek(0)
            seen = set()
            expanded = 0
            with tarfile.open(fileobj=stream, mode="r|gz") as bundle:
                for member in bundle:
                    name = member.name
                    require(name not in seen and len(seen) < MAX_FILES and allowed_name(name),
                            "Unexpected, excess or duplicate archive path")
                    seen.add(name)
                    path = pending / name
                    if member.isdir():
                        require(name not in NATIVES | {MANIFEST} | LOADERS, "Payload file is an archive directory")
                        path.mkdir(parents=True, exist_ok=True, mode=0o755)
                        continue
                    require(member.isfile() and not member.issparse() and member.size > 0,
                            "Archive has a link, device, sparse or empty input")
                    expanded += member.size
                    require(expanded < MAX_EXPANDED_BYTES, "Expanded archive exceeds size limit")
                    path.parent.mkdir(parents=True, exist_ok=True, mode=0o755)
                    with bundle.extractfile(member) as source, path.open("xb") as destination:
                        shutil.copyfileobj(source, destination, 1024 * 1024)
                        os.fchmod(destination.fileno(), 0o555 if name in {"rh-server", "rh-headless"} else 0o444)
                        destination.flush()
                        os.fsync(destination.fileno())
        manifest, manifest_sha = inspect_manifest(pending)
        release = parent / NEW
        if present(release):
            _, installed_sha = inspect_manifest(release)
            require(installed_sha == manifest_sha, "Release name collision")
        else:
            for folder, _, _ in os.walk(pending, topdown=False):
                path = Path(folder)
                path.chmod(0o755 if path == pending else 0o555)
                sync_dir(path)
            pending.rename(release)
            sync_dir(parent)
        return manifest, manifest_sha
    finally:
        if pending.exists():
            for dirname, dirs, _ in os.walk(pending):
                Path(dirname).chmod(0o700)
            shutil.rmtree(pending)


def publish_web(release, parent, manifest, gid):
    trusted(parent, directory=True)
    files = {name.removeprefix("web/"): item for name, item in manifest["files"].items()
             if name.startswith("web/") and name.split("/")[1] in BUILD_DIRS}
    versions = parent / NEW
    if present(versions):
        check_tree(versions, files, gid)
        return
    pending = Path(tempfile.mkdtemp(prefix=".balance-publishing-", dir=parent))
    try:
        for name in files:
            path = pending / name
            path.parent.mkdir(parents=True, exist_ok=True)
            with (release / "web" / name).open("rb") as source, path.open("xb") as dest:
                shutil.copyfileobj(source, dest, 1024 * 1024)
                os.fchown(dest.fileno(), os.geteuid(), gid)
                os.fchmod(dest.fileno(), 0o440)
                dest.flush()
                os.fsync(dest.fileno())
        for dirname, _, _ in os.walk(pending, topdown=False):
            folder = Path(dirname)
            os.chown(folder, os.geteuid(), gid)
            folder.chmod(0o550)
            sync_dir(folder)
        check_tree(pending, files, gid)
        pending.rename(versions)
        sync_dir(parent)
    finally:
        if pending.exists():
            for dirname, _, _ in os.walk(pending):
                Path(dirname).chmod(0o700)
            shutil.rmtree(pending)


@dataclass(frozen=True)
class UnitState:
    active: str
    result: str
    pid: int

    @property
    def stopped(self):
        return self.pid == 0 and self.active in {"inactive", "failed", "missing"}

    @property
    def drained(self):
        return self.pid == 0 and self.active in {"inactive", "missing"} and self.result in {"success", ""}


class Host:
    def __init__(self, kind, root=Path("/"), gid=None):
        self.kind = kind
        self.target = TARGETS[kind]
        self.root = root
        self.base = root / "opt/rh-alpha"
        self.front = self.base / self.target.front_name / "active"
        self.versions = root / "srv/rh-alpha/web-public" / self.target.web_tag
        self.releases = self.base / "releases"
        self.release = self.releases / NEW
        self.prior = self.releases / OLD
        self.state_dir = self.base / "balance-operator"
        self.state = self.state_dir / (kind + ".json")
        self.drop_dir = root / "etc/systemd/system" / (self.target.unit + ".d")
        self.override = self.drop_dir / OVERRIDE
        self.lock = root / "run/rh-alpha-capacity.lock"
        self.gid = grp.getgrnam("rh_alpha_edge").gr_gid if gid is None else gid

    @contextmanager
    def locked(self):
        descriptor = os.open(self.lock, os.O_CREAT | os.O_RDWR | os.O_NOFOLLOW, 0o600)
        try:
            trusted(self.lock)
            fcntl.flock(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
            yield
        finally:
            os.close(descriptor)

    def props(self, *keys):
        result = self.systemctl("show", *(f"--property={key}" for key in keys), self.target.unit)
        parsed = dict(row.split("=", 1) for row in result.splitlines() if "=" in row)
        require(all(key in parsed for key in keys), "Incomplete systemd service properties")
        return parsed

    def systemctl(self, *args):
        return subprocess.run(["/usr/bin/systemctl", *args], check=True, text=True,
                              stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=35).stdout

    def unit_state(self):
        result = subprocess.run([
            "/usr/bin/systemctl", "show", "--property=LoadState", "--property=ActiveState",
            "--property=Result", "--property=MainPID", self.target.unit,
        ], text=True, capture_output=True, timeout=10, env={**os.environ, "LC_ALL": "C"})
        state = dict(row.split("=", 1) for row in result.stdout.splitlines() if "=" in row)
        if state.get("LoadState") == "not-found" or result.stderr.strip() == f"Unit {self.target.unit} could not be found.":
            return UnitState("missing", "", 0)
        require(result.returncode == 0 and state.get("LoadState") == "loaded"
                and all(key in state for key in ("MainPID", "Result", "ActiveState")), "Unknown Source unit state")
        return UnitState(state["ActiveState"], state["Result"], int(state["MainPID"]))

    def running(self, pid):
        proc = self.root / "proc" / str(pid)
        exe = Path(os.readlink(proc / "exe"))
        args = tuple((proc / "cmdline").read_bytes().rstrip(b"\0").decode().split("\0"))
        return exe, args

    def loaded(self):
        props = self.props("ExecStart", "NeedDaemonReload")
        matches = COMMAND_RE.findall(props["ExecStart"])
        require(props["NeedDaemonReload"] == "no" and len(matches) == 1 and props["ExecStart"].count("{ path=") == 1,
                "Unknown or stale effective systemd ExecStart")
        path, words = matches[0]
        argv = tuple(words.split())
        validate_words(argv)
        require(argv[0] == path, "Effective systemd executable and command disagree")
        return argv

    def unit_paths(self):
        output = subprocess.run(["/usr/bin/systemd-analyze", "unit-paths"], check=True, text=True,
                                stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=10).stdout
        return tuple(Path(path) for path in output.splitlines() if path.startswith("/"))

    def fingerprints(self):
        """Verify all effective and discoverable unit inputs, excluding the operator-owned override."""
        props = self.props("FragmentPath", "DropInPaths")
        fragment = Path(props["FragmentPath"])
        require(fragment.is_absolute(), "Unit fragment is not an existing absolute path")
        includes = {Path(name) for name in props["DropInPaths"].split()}
        parts = self.target.unit.removesuffix(".service").split("-")
        families = ["service.d", self.target.unit + ".d", *("-".join(parts[:k]) + "-.service.d" for k in range(1, len(parts)))]
        for directory in self.unit_paths():
            for family in families:
                includes.update((directory / family).glob("*.conf"))
        result = {}
        for path in sorted(({fragment} | includes) - {self.override}):
            require(path.is_absolute(), "Unknown systemd path")
            trusted(path)
            if path != fragment and path.name not in {"80-split.conf", "91-capacity.conf", "92-saturday-native.conf"}:
                body = read_trusted(path).replace(b"\\\n", b" ")
                require(not re.search(rb"^\s*ExecStart\s*=", body, re.MULTILINE),
                        "Unrecognized effective service override")
            result[str(path)] = file_digest(path)
        return result

    def transient_kind(self):
        transient = self.props("Transient")["Transient"]
        require(transient in {"no", "yes"}, "Unknown systemd service type")
        if transient == "no":
            return False
        require(self.kind == "stage", "Production must have a durable Source unit")
        return True

    def transient_fingerprint(self):
        """Allow only the reviewed transient staging service and its capture isolation."""
        keys = ("FragmentPath", "DropInPaths", "Type", "User", "Slice", "EnvironmentFiles", "TimeoutStopUSec",
                "CollectMode", "Restart", "RequiresMountsFor")
        properties = self.props(*keys)
        expected = {
            "FragmentPath": "/run/systemd/transient/" + self.target.unit,
            "DropInPaths": "",
            "Type": "exec",
            "User": self.target.user,
            "Slice": "rh-alpha-capture-isolated.slice",
            "EnvironmentFiles": "/etc/rh-alpha/source.env (ignore_errors=yes)",
            "TimeoutStopUSec": "1min 30s",
            "CollectMode": "inactive-or-failed",
            "Restart": "no",
            "RequiresMountsFor": "/srv/rh-alpha",
        }
        require(properties == expected, "Staging transient unit properties differ from the approved recipe")
        return expected

    def request(self, control, url, post=False):
        port = self.target.control_port if control else self.target.public_port
        headers = {"Accept": "application/json"}
        if control:
            token = self.root / "etc/rh-alpha" / self.target.token_name
            info = token.lstat()
            require(stat.S_ISREG(info.st_mode) and token.resolve() == token, "Control token file is unavailable")
            key = token.read_text().strip()
            require(re.fullmatch(r"[!-~]+", key), "Control token file is invalid")
            headers["Authorization"] = "Bearer " + key
        if post:
            headers["Content-Type"] = "application/json"
        connection = http.client.HTTPConnection("127.0.0.1", port, timeout=4)
        try:
            connection.request("POST" if post else "GET", url, b'{"v":1}' if post else None, headers)
            response = connection.getresponse()
            require(response.status == 200 or (url == "/control/health" and response.status == 503),
                    f"{url} returned HTTP {response.status}")
            payload = response.read(1024 * 1024 + 1)
            require(len(payload) <= 1024 * 1024, "Unexpected health response size")
            value = json.loads(payload, object_pairs_hook=unique_object)
            require(isinstance(value, dict), "Unexpected health response")
            return value
        finally:
            connection.close()

    def edge(self, path, max_bytes=2 * 1024 * 1024, headers=None):
        """Read the already running Caddy origin through its Unix socket."""
        sock_path = self.root / "run" / self.target.edge_dir / "caddy.sock"
        require(sock_path.is_socket(), "Expected local Caddy socket is unavailable")

        class EdgeConnection(http.client.HTTPConnection):
            def connect(self):
                self.sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
                self.sock.settimeout(5)
                self.sock.connect(str(sock_path))

        connection = EdgeConnection(self.target.hostname, timeout=5)
        try:
            connection.request("GET", path, headers={"CF-Connecting-IP": "198.51.100.10", **(headers or {})})
            response = connection.getresponse()
            require(response.status == 200, f"Local edge {path} returned HTTP {response.status}")
            data = response.read(max_bytes + 1)
            require(len(data) <= max_bytes, "Local edge response exceeded size limit")
            return data, dict(response.getheaders())
        finally:
            connection.close()

    def start(self, argv, transient):
        require(self.unit_state().stopped, "Refusing to start while the unit is active")
        for port in (self.target.public_port, self.target.control_port):
            try:
                with socket.create_connection(("127.0.0.1", port), timeout=2):
                    raise Guard("Source port occupied outside the stopped target service")
            except ConnectionRefusedError:
                pass
        if transient:
            # Wait for --collect to release the name before recreating the reviewed transient unit.
            for _ in range(20):
                state = self.unit_state()
                if state.active == "missing":
                    break
                require(state.stopped, "Staging restarted before its transient unit was collected")
                time.sleep(0.25)
            else:
                raise Guard("Stopped staging transient unit has not been collected")
            subprocess.run([
                "/usr/bin/systemd-run", "--quiet", "--unit=" + self.target.unit, "--service-type=exec",
                "--slice=rh-alpha-capture-isolated.slice", "--collect", "--uid=" + self.target.user,
                "--property=RequiresMountsFor=/srv/rh-alpha", "--property=TimeoutStopSec=90s",
                "--property=EnvironmentFile=-/etc/rh-alpha/source.env", *argv,
            ], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=35)
        else:
            self.systemctl("daemon-reload")
            require(self.loaded() == argv, "Effective unit refused the intended command")
            self.systemctl("start", self.target.unit)

    def front_bytes(self, name):
        trusted(self.front, directory=True)
        path = self.front / name
        info = trusted(path)
        require((info.st_gid, stat.S_IMODE(info.st_mode)) == (self.gid, 0o440),
                "Unexpected front loader or notice ownership/mode")
        return read_trusted(path, 2 * 1024 * 1024)

    def verify_edge(self, name, expected):
        route = "/" if name == "index.html" else "/" + name
        body, _ = self.edge(route)
        require(body == expected, "Served loader or notice differs from verified disk bytes")

    def verify_assets(self, manifest):
        # The 4 startup assets and .br sidecars have already been read and hashed on disk.
        # Read the smallest startup payload through Caddy, exercising origin routing and Brotli.
        web_files = manifest["files"]
        loader = next(name for name in web_files if name.startswith("web/Build/") and name.endswith(".loader.js"))
        compressed = web_files[loader + ".br"]
        path = "/alpha-web/" + NEW + "/" + loader.removeprefix("web/")
        data, headers = self.edge(path, max_bytes=compressed["bytes"], headers={"Accept-Encoding": "br"})
        encoding = next((value for key, value in headers.items() if key.lower() == "content-encoding"), "")
        require(encoding == "br" and len(data) == compressed["bytes"] and hexdigest(data) == compressed["sha256"],
                "Local edge served the wrong Unity build or compression layout")


def validate_words(argv):
    # No systemd specifiers, quoting, environment expansion, or executable prefixes.
    require(bool(argv) and all(isinstance(arg, str) and re.fullmatch(r"[^\s'\"\\$%;]+", arg) for arg in argv),
            "Unsafe systemd command syntax")


def flag(argv, key, value=None):
    require(argv.count(key) == 1 and not any(arg.startswith(key + "=") for arg in argv),
            "Unexpected argument form or count: " + key)
    index = argv.index(key) + 1
    if value is not None:
        require(index < len(argv) and argv[index] == value, "Unrecognized argument value: " + key)
    return index


def prior_command(host, argv, native_sha):
    validate_words(argv)
    target = host.target
    require(argv[0] == str(host.prior / "rh-server"), "Unexpected prior source revision")
    flag(argv, "--runtime", "source-arena")
    flag(argv, "--bind", f"127.0.0.1:{target.public_port}")
    flag(argv, "--control", f"127.0.0.1:{target.control_port}")
    flag(argv, "--control-token-file", str(host.root / "etc/rh-alpha" / target.token_name))
    flag(argv, "--max-rooms", "2")
    flag(argv, "--min-players", "2")
    flag(argv, "--fill-seconds", "2")
    flag(argv, "--fill-bots", "4")
    flag(argv, "--bot-learning", "off")
    flag(argv, "--result-spool", str(host.root / "srv/rh-alpha" / target.spool))
    capture = host.root / "srv/rh-alpha" / target.capture
    flag(argv, "--alpha-metrics-dir", str(capture / "metrics"))
    flag(argv, "--alpha-metrics-key-file", str(capture / "metrics.key"))
    flag(argv, "--alpha-replay-dir", str(capture / "replays"))
    flag(argv, "--map-pool")
    index = flag(argv, "--map")
    require(index < len(argv), "Prior map path is absent")
    map_path = Path(argv[index])
    require(map_path.name == "seed1.map.json" and map_path.is_relative_to(host.releases),
            "Unknown prior map path")
    trusted(map_path)
    trusted(Path(argv[0]), executable=True)
    require(file_digest(Path(argv[0])) == native_sha, "Prior native checksum changed")
    db = f"postgresql:///{target.user}?host={host.root / 'run' / target.pg_socket}&user={target.user}"
    flag(argv, "--database-url", db)
    unexpected = {"--control-insecure-loopback", "--training", "--first-playable", "--max-players", "--require-admit",
                  "--signature-landmarks"}
    require(not any(arg.split("=", 1)[0] in unexpected for arg in argv), "Unexpected prior admission or map policy")
    brains = [arg for arg in argv if arg == "--bot-brain" or arg.startswith("--bot-brain=")]
    if brains:
        flag(argv, "--bot-brain", "off")
    return map_path


def candidate_command(host, old_argv, bot_mode):
    argv = list(old_argv)
    argv[0] = str(host.release / "rh-server")
    argv[flag(argv, "--map")] = str(host.release / "seed1.map.json")
    if bot_mode == "raid":
        argv[flag(argv, "--fill-bots", "4")] = "8"
        if "--bot-brain" in argv:
            argv[flag(argv, "--bot-brain", "off")] = "raid"
        else:
            argv.extend(("--bot-brain", "raid"))
        argv.append("--signature-landmarks")
    return tuple(argv)


def override_bytes(argv):
    validate_words(argv)
    return ("# Saturday balance release " + NEW + "\n[Service]\nExecStart=\nExecStart=" + " ".join(argv) + "\n").encode()


def running_argv(host, argv, state=None):
    state = host.unit_state() if state is None else state
    require(state.active == "active" and state.pid > 0, "Target Source service is not running")
    exe, args = host.running(state.pid)
    require(exe == Path(argv[0]) and args == argv, "Running target command is not the reviewed command")
    return state


def identity(host, build):
    info = host.request(False, "/info")
    require(info.get("protocol") == PROTOCOL and info.get("contentHash") == (NEW_CONTENT if build == NEW else OLD_CONTENT),
            "Source protocol or content mismatch")
    ctl = host.request(True, "/control/health")
    require(ctl.get("v") == 1 and ctl.get("build") == build, "Authenticated Source build mismatch")
    return info, ctl


def is_int(payload, key, value):
    return type(payload.get(key)) is int and payload[key] == value


def healthy(host, build, argv):
    running_argv(host, argv)
    require(host.loaded() == argv, "Loaded and running Source commands disagree")
    info, ctl = identity(host, build)
    ready = host.request(False, "/readyz")
    require(ready.get("ok") is True and ready.get("storage") == "ok" and
            all(item.get("state") == "ready" for item in (info, ctl, ready)), "Source storage or lifecycle is unhealthy")
    for name in ("metrics", "replays"):
        writer = ctl.get("alphaCapture", {}).get(name, {})
        require(writer.get("configured") is True and writer.get("started") is True and
                writer.get("startFailed") is False and is_int(writer, "writerUnavailable", 0),
                "Capture writer is unhealthy: " + name)
    lobby = host.request(False, "/api/lobby")
    require(is_int(lobby, "arenaLimit", CAPACITY), "Unexpected arena room capacity")


def empty(host):
    lobby = host.request(False, "/api/lobby")
    require(lobby.get("arenaAvailable") is True and is_int(lobby, "arenaLimit", CAPACITY) and
            is_int(lobby, "arenaRoomCount", 0) and is_int(lobby, "onlineCount", 0) and
            lobby.get("waiting") == [] and lobby.get("rooms") == [],
            "Public lobby, seat or room is occupied")
    info = host.request(True, "/control/info")
    require(info.get("state") == "ready" and is_int(info, "maxRooms", CAPACITY) and
            is_int(info, "rooms", 0) and is_int(info, "players", 0),
            "Authoritative admission or room is occupied")


def wait(check, seconds, sleep):
    last = None
    for attempt in range(seconds + 1):
        try:
            check()
            return
        except FAILURES as error:
            last = safe_message(error)
        if attempt < seconds:
            sleep(1)
    raise Guard("Verification timed out: " + last)


def drain(host, build, argv, sleep, allow_bad_identity=False):
    state = running_argv(host, argv)
    if allow_bad_identity:
        # The reviewed candidate executable may report a mismatched public identity.
        # Its authenticated build check and authoritative occupancy still gate rollback.
        ctl = host.request(True, "/control/health")
        require(ctl.get("v") == 1 and ctl.get("build") == build, "Authenticated rollback build mismatch")
        info = ctl
    else:
        info, ctl = identity(host, build)
    if ctl.get("state") == "ready":
        require(info.get("state") == "ready" and is_int(ctl, "rooms", 0), "Target control reports open rooms")
        empty(host)
        require(host.unit_state() == state, "Source process changed before the drain request")
        try:
            reply = host.request(True, "/control/drain", post=True)
            require(reply.get("v") == 1 and reply.get("state") in {"draining", "terminated"},
                    "Source did not acknowledge graceful drain")
        except (OSError, http.client.HTTPException):
            pass  # An empty Source can exit before sending the HTTP response.
    else:
        require(ctl.get("state") in {"draining", "terminated"}, "Source state does not allow guarded drain")

    for _ in range(181):
        now = host.unit_state()
        require(now.pid in {0, state.pid}, "Source process changed while draining")
        if now.stopped:
            require(now.drained, "Source did not exit cleanly")
            return
        sleep(1)
    raise Guard("Graceful drain still in progress. The service was left running.")


def safe_message(error):
    return str(error) if isinstance(error, Guard) else type(error).__name__


STAGE_INDEX_SHA = "888ca9d75d52cdcd637b97439aebaaaad63486ee091e30d802e4182d67d64c2d"


def snapshot(host, mode, archive_sha, manifest_sha, prior_index_sha):
    require(not present(host.override), "An unowned balance override is already installed")
    old_argv = host.loaded()
    old_map = prior_command(host, old_argv, OLD_NATIVE_SHA)
    require(file_digest(old_map) == file_digest(host.release / "seed1.map.json"),
            "Candidate fallback map differs from the admitted prior map")
    healthy(host, OLD, old_argv)
    empty(host)
    old_index = host.front_bytes("index.html")
    old_notice = host.front_bytes("alpha-gameplay-data.html")
    require(hexdigest(old_index) == prior_index_sha, "Baseline homepage hash differs from reviewed bytes")
    host.verify_edge("index.html", old_index)
    host.verify_edge("alpha-gameplay-data.html", old_notice)
    transient = host.transient_kind()
    return {
        "v": 1, "target": host.kind, "phase": "prepared", "botMode": mode,
        "archive": archive_sha, "manifest": manifest_sha, "priorIndex": prior_index_sha,
        "oldArgv": list(old_argv), "newArgv": list(candidate_command(host, old_argv, mode)),
        "oldIndex": base64.b64encode(old_index).decode(),
        "oldNotice": base64.b64encode(old_notice).decode(),
        "candidateFront": {
            "index.html": [hexdigest(new_front(host, route, "index.html")) for route in ("split", "same-origin")],
            "alpha-gameplay-data.html": [hexdigest(new_front(host, "split", "alpha-gameplay-data.html"))],
        },
        "transient": transient,
        "unitFiles": host.transient_fingerprint() if transient else host.fingerprints(),
    }


def load_record(host):
    if not present(host.state):
        return None
    private_dir(host.state_dir)
    info = trusted(host.state)
    require(stat.S_IMODE(info.st_mode) == 0o600, "Rollback record is not private")
    record = json.loads(read_trusted(host.state), object_pairs_hook=unique_object)
    require(record.get("v") == 1 and record.get("target") == host.kind and
            record.get("phase") in {"prepared", "switching", "selected", "reverting", "restored"} and
            record.get("botMode") in {"preserve", "raid"} and type(record.get("transient")) is bool,
            "Unknown rollout journal state")
    old_argv = tuple(record["oldArgv"])
    prior_command(host, old_argv, OLD_NATIVE_SHA)
    require(tuple(record["newArgv"]) == candidate_command(host, old_argv, record["botMode"]),
            "Recorded candidate command is inconsistent")
    require(hexdigest(old_front(record, "index.html")) == record["priorIndex"], "Recorded prior loader checksum mismatch")
    check_hash(record["archive"])
    check_hash(record["manifest"])
    return record


def save_record(host, record, phase):
    private_dir(host.state_dir)
    record["phase"] = phase
    atomic_write(host.state, json_bytes(record), 0o600)


def old_front(record, name):
    return base64.b64decode(record["oldIndex"] if name == "index.html" else record["oldNotice"], validate=True)


def new_front(host, mode, name):
    path = "web/alpha-gameplay-data.html" if name == "alpha-gameplay-data.html" else f"loaders/{host.target.web_tag}-{mode}.html"
    return read_trusted(host.release / path, 2 * 1024 * 1024)


def verify_front(host, record):
    for name in ("index.html", "alpha-gameplay-data.html"):
        allowed = {hexdigest(old_front(record, name)), *map(check_hash, record["candidateFront"][name])}
        require(hexdigest(host.front_bytes(name)) in allowed, "Unrecognized current loader or gameplay notice")


def set_front(host, name, expected):
    if host.front_bytes(name) != expected:
        atomic_write(host.front / name, expected, 0o440, host.gid)
    host.verify_edge(name, expected)


def effective_target(host, record):
    old_argv = tuple(record["oldArgv"])
    new_argv = tuple(record["newArgv"])
    state = host.unit_state()
    if record["transient"]:
        require(not present(host.override), "Persistent override exists on a transient target")
        if not state.stopped:
            require(host.transient_kind() and host.transient_fingerprint() == record["unitFiles"],
                    "Staging transient service properties changed")
    else:
        require(not host.transient_kind() and host.fingerprints() == record["unitFiles"],
                "Target systemd configuration changed outside the rollout")
        if present(host.override):
            require(read_trusted(host.override) == override_bytes(new_argv), "Unknown balance override")
    if state.stopped:
        return None
    exe, args = host.running(state.pid)
    require(exe == Path(args[0]) and args in {old_argv, new_argv}, "Unknown running Source command")
    require(host.loaded() == args, "Loaded service selection differs from the running source")
    if not record["transient"]:
        require(present(host.override) == (args == new_argv), "Durable override disagrees with the running Source")
    return NEW if args == new_argv else OLD


def select_source(host, record, build, sleep):
    current = effective_target(host, record)
    old_argv = tuple(record["oldArgv"])
    new_argv = tuple(record["newArgv"])
    argv = new_argv if build == NEW else old_argv
    if current != build:
        if current is not None:
            drain(host, current, new_argv if current == NEW else old_argv, sleep, allow_bad_identity=(current == NEW))
        require(host.unit_state().stopped, "Target still has a Source process")
        if not record["transient"]:
            if build == NEW:
                trusted(host.drop_dir, directory=True)
                atomic_write(host.override, override_bytes(new_argv), 0o644)
            elif present(host.override):
                require(read_trusted(host.override) == override_bytes(new_argv), "Unknown balance override")
                host.override.unlink()
                sync_dir(host.drop_dir)
        host.start(argv, record["transient"])
    wait(lambda: healthy(host, build, argv), 60, sleep)


def verify_selected(host, record, manifest):
    require(record["phase"] == "selected" and effective_target(host, record) == NEW,
            "Candidate paired release has not been selected")
    healthy(host, NEW, tuple(record["newArgv"]))
    index = host.front_bytes("index.html")
    require(index in {new_front(host, mode, "index.html") for mode in ("split", "same-origin")},
            "Selected homepage does not belong to the running candidate")
    host.verify_edge("index.html", index)
    notice = new_front(host, "split", "alpha-gameplay-data.html")
    require(host.front_bytes("alpha-gameplay-data.html") == notice, "Selected gameplay notice differs")
    host.verify_edge("alpha-gameplay-data.html", notice)
    host.verify_assets(manifest)


def recover(host, record, sleep):
    verify_front(host, record)
    save_record(host, record, "reverting")
    select_source(host, record, OLD, sleep)
    set_front(host, "alpha-gameplay-data.html", old_front(record, "alpha-gameplay-data.html"))
    set_front(host, "index.html", old_front(record, "index.html"))
    save_record(host, record, "restored")


def apply(host, archive, archive_sha, mode, prior_index_sha, sleep=time.sleep):
    manifest, manifest_sha = install_archive(archive, archive_sha, host.releases)
    if host.kind == "production":
        stage = Host("stage", host.root, host.gid)
        stage_record = load_record(stage)
        require(stage_record is not None and stage_record["manifest"] == manifest_sha and
                stage_record["archive"] == archive_sha and stage_record["botMode"] == mode,
                "The same archive and bot policy must pass staging first")
        verify_selected(stage, stage_record, manifest)

    record = load_record(host)
    if record is None:
        record = snapshot(host, mode, archive_sha, manifest_sha, prior_index_sha)
    else:
        require((record["archive"], record["manifest"], record["botMode"], record["priorIndex"]) ==
                (archive_sha, manifest_sha, mode, prior_index_sha), "Apply differs from the reviewed journal")
        require(record["phase"] != "reverting", "Complete the explicit rollback before applying again")
        if record["phase"] == "selected":
            verify_selected(host, record, manifest)
            return "PAIRED_ALREADY_SELECTED"

    publish_web(host.release, host.versions, manifest, host.gid)
    host.verify_assets(manifest)
    verify_front(host, record)
    actual = effective_target(host, record)
    if actual == OLD and host.request(True, "/control/health").get("state") == "ready":
        healthy(host, OLD, tuple(record["oldArgv"]))
        empty(host)
    save_record(host, record, "switching")
    try:
        select_source(host, record, NEW, sleep)
        set_front(host, "alpha-gameplay-data.html", new_front(host, "split", "alpha-gameplay-data.html"))
        set_front(host, "index.html", new_front(host, "split", "index.html"))
        save_record(host, record, "selected")
        verify_selected(host, record, manifest)
    except FAILURES as error:
        try:
            recover(host, record, sleep)
        except FAILURES as rollback_error:
            raise Guard(f"Apply did not finish ({safe_message(error)}). Rollback is pending ({safe_message(rollback_error)}).") from None
        raise Guard(f"Apply rejected ({safe_message(error)}). Prior pair restored.") from None
    return "PAIRED_RELEASE_SELECTED"


def rollback(host, sleep=time.sleep):
    record = load_record(host)
    require(record is not None, "No guarded rollback record for this target")
    # Use journaled frontend digests so damaged candidate artifacts cannot block restoration.
    if record["phase"] == "restored":
        require(effective_target(host, record) == OLD, "Restored target no longer matches the prior Source")
        healthy(host, OLD, tuple(record["oldArgv"]))
        for name in ("index.html", "alpha-gameplay-data.html"):
            require(host.front_bytes(name) == old_front(record, name), "Restored front differs from journal")
            host.verify_edge(name, old_front(record, name))
        return "PRIOR_ALREADY_RESTORED"
    recover(host, record, sleep)
    return "PRIOR_PAIRED_RELEASE_RESTORED"


def fallback(host, mode):
    record = load_record(host)
    require(record is not None, "No guarded loader record for this target")
    manifest, manifest_sha = inspect_manifest(host.release)
    require(manifest_sha == record["manifest"], "Installed candidate differs from journal")
    verify_selected(host, record, manifest)
    previous = host.front_bytes("index.html")
    verify_loader(new_front(host, mode, "index.html"), host.target, mode, NEW,
                  has_template=(host.release / "web/TemplateData").is_dir())
    try:
        set_front(host, "index.html", new_front(host, mode, "index.html"))
        verify_selected(host, record, manifest)
    except FAILURES:
        set_front(host, "index.html", previous)
        raise
    return "PAIRED_LOADER_" + mode.upper().replace("-", "_")


def status(host):
    state = host.unit_state()
    print(f"TARGET={host.kind} UNIT={state.active} INDEX_SHA256={hexdigest(host.front_bytes('index.html'))}")
    if not state.stopped:
        info = host.request(False, "/info")
        ctl = host.request(True, "/control/health")
        lobby = host.request(False, "/api/lobby")
        print(json.dumps({key: value for key, value in {
            "protocol": info.get("protocol"), "contentHash": info.get("contentHash"), "build": ctl.get("build"),
            "state": ctl.get("state"), "arenaLimit": lobby.get("arenaLimit"), "rooms": lobby.get("arenaRoomCount"),
        }.items()}, sort_keys=True))
    if present(host.state):
        print("JOURNAL_PHASE=" + load_record(host)["phase"])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="action", required=True)
    verify = commands.add_parser("archive-check", help="verify and unpack in a disposable local directory")
    verify.add_argument("archive", type=Path)
    verify.add_argument("archive_sha256")
    for kind in TARGETS:
        install = commands.add_parser(kind + "-apply")
        install.add_argument("archive", type=Path)
        install.add_argument("archive_sha256")
        install.add_argument("--bots", choices=("preserve", "raid"), required=True)
        if kind == "stage":
            install.add_argument("--prior-index-sha256", default=STAGE_INDEX_SHA,
                                 help="reviewed expected hash of the current staging root homepage")
        for action in ("rollback", "status"):
            commands.add_parser(kind + "-" + action)
        route = commands.add_parser(kind + "-loader")
        route.add_argument("mode", choices=("split", "same-origin"))
    options = parser.parse_args()
    try:
        if options.action == "archive-check":
            with tempfile.TemporaryDirectory(prefix="rh-balance-verify-") as name:
                directory = Path(name).resolve()
                manifest, digest = install_archive(options.archive.absolute(), options.archive_sha256, directory)
                try:
                    print(f"ARCHIVE_VERIFIED source={NEW} files={len(manifest['files'])} manifest_sha256={digest}")
                finally:
                    for folder, _, _ in os.walk(directory):
                        Path(folder).chmod(0o700)
            return
        kind, action = options.action.split("-", 1)
        require(os.geteuid() == 0, "Run the operator as root on the reviewed guest")
        host = Host(kind)
        if action == "status":
            status(host)
            return
        with host.locked():
            if action == "apply":
                prior_index = options.prior_index_sha256 if kind == "stage" else LIVE_INDEX_SHA
                print(apply(host, options.archive.absolute(), check_hash(options.archive_sha256),
                            options.bots, check_hash(prior_index)))
            elif action == "rollback":
                print(rollback(host))
            else:
                print(fallback(host, options.mode))
    except FAILURES as error:
        parser.exit(1, "REFUSED: " + safe_message(error) + "\n")


if __name__ == "__main__":
    os.umask(0o077)
    main()

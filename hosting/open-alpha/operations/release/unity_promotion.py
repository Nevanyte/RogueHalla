#!/usr/bin/env python3
"""Publish or restore the pinned Unity homepage. No network or service-control calls."""
import argparse
import fcntl
import grp
import hashlib
import json
import os
from pathlib import Path
import shutil
import stat
import tempfile

MANIFEST_SHA = "ceeafdda23d5fa3a8a1efd0af91869eab40a0b3fbd1fed2ae86cdaeafc1fc217"
BASELINE = "af79fa027ec56ecc6f718ecd4bf43e0577061b02"
CANDIDATE = "9e41ed5e77c812ae170398fc86a1c331a68af9d9"
ROOTS = {"Build", "StreamingAssets", "Licenses", "TemplateData"}


class GuardError(Exception):
    pass


def require(value, reason):
    if not value:
        raise GuardError(reason)


def pinned_package(bundle):
    data = (bundle / "manifest.json").read_bytes()
    require(hashlib.sha256(data).hexdigest() == MANIFEST_SHA, "wrong operator manifest hash")
    manifest = json.loads(data)
    require(manifest["baseline"] == BASELINE and manifest["candidate"] == CANDIDATE, "wrong revision pins")
    for key in ("baselineFiles", "candidateFiles"):
        require(len(manifest[key]) == 38, "unexpected backing asset set")
        for name in manifest[key]:
            parts = name.split("/")
            require(len(parts) > 1 and parts[0] in ROOTS and not {"", ".", ".."}.intersection(parts),
                    "invalid versioned path")
    loader = read_pinned(bundle / "live-split.html", manifest["candidateLoader"])
    for literal in (
        b'<meta name="roguehalla-backend" content="split">',
        b'https://game.roguehalla.com wss://game.roguehalla.com',
        b'href="/alpha-gameplay-data.html"',
        b'can save completed arena matches as private replays',
        f'"/alpha-web/{CANDIDATE}/Build"'.encode(),
        f'"/alpha-web/{CANDIDATE}/StreamingAssets"'.encode(),
    ):
        require(literal in loader, "candidate live route or gameplay notice changed")
    require(b"staging.roguehalla.com" not in loader, "staging endpoint in live loader")
    return manifest, loader


def verify_directory(path):
    require(stat.S_ISDIR(path.lstat().st_mode), f"not a real directory: {path}")


def open_regular(path):
    # Reject FIFO/device inputs before opening and forbid a final symlink at open.
    require(stat.S_ISREG(path.lstat().st_mode), f"not a regular file: {path}")
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW)
    return os.fdopen(fd, "rb")


def checked_stream(path, receipt, destination=None):
    with open_regular(path) as source:
        require(os.fstat(source.fileno()).st_size == receipt["bytes"], f"wrong file size: {path}")
        digest = hashlib.sha256()
        for block in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(block)
            if destination is not None:
                destination.write(block)
        require(digest.hexdigest() == receipt["sha256"], f"wrong SHA256: {path}")


def read_pinned(path, receipt):
    with open_regular(path) as source:
        data = source.read(receipt["bytes"] + 1)
    require(len(data) == receipt["bytes"] and hashlib.sha256(data).hexdigest() == receipt["sha256"],
            f"wrong bytes: {path}")
    return data


def metadata(path, mode, uid, gid):
    st = path.lstat()
    require((stat.S_IMODE(st.st_mode), st.st_uid, st.st_gid) == (mode, uid, gid),
            f"unexpected ownership or mode: {path}")


def check_tree(root, receipts, identity=None):
    verify_directory(root)
    actual_files = set()
    valid_dirs = {part.as_posix() for name in receipts for part in Path(name).parents if part != Path(".")}
    if identity:
        metadata(root, 0o550, *identity)
    for dirpath, dirs, files in os.walk(root):
        folder = Path(dirpath)
        for name in dirs:
            child = folder / name
            verify_directory(child)
            require(child.relative_to(root).as_posix() in valid_dirs, f"unexpected asset directory: {child}")
            if identity:
                metadata(child, 0o550, *identity)
        for name in files:
            child = folder / name
            key = child.relative_to(root).as_posix()
            require(key in receipts, f"unexpected asset: {child}")
            checked_stream(child, receipts[key])
            if identity:
                metadata(child, 0o440, *identity)
            actual_files.add(key)
    require(actual_files == set(receipts), f"missing versioned assets: {root}")


def fsync_directory(path):
    fd = os.open(path, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    try:
        os.fsync(fd)
    finally:
        os.close(fd)


def atomic_bytes(path, data, mode, uid, gid):
    fd, pending = tempfile.mkstemp(prefix=".pending-unity-", dir=path.parent)
    try:
        with os.fdopen(fd, "wb") as dest:
            dest.write(data)
            os.fchown(dest.fileno(), uid, gid)
            os.fchmod(dest.fileno(), mode)
            dest.flush()
            os.fsync(dest.fileno())
        os.replace(pending, path)
        fsync_directory(path.parent)
    finally:
        if os.path.lexists(pending):
            os.unlink(pending)


def publish_tree(source, target, receipts, identity):
    if os.path.lexists(target):
        check_tree(target, receipts, identity)
        return
    temp = Path(tempfile.mkdtemp(prefix=".publishing-unity-", dir=target.parent))
    dirs = [temp]
    try:
        for name, receipt in receipts.items():
            dst = temp / name
            dst.parent.mkdir(parents=True, exist_ok=True)
            with dst.open("xb") as dest:
                checked_stream(source / name, receipt, dest)
                os.fchown(dest.fileno(), *identity)
                os.fchmod(dest.fileno(), 0o440)
                dest.flush()
                os.fsync(dest.fileno())
        dirs.extend(path for path in temp.rglob("*") if path.is_dir())
        for directory in reversed(dirs):
            os.chown(directory, *identity)
            directory.chmod(0o550)
            fsync_directory(directory)
        check_tree(temp, receipts, identity)
        # Atomic within one filesystem. The homepage still references the old bundle.
        temp.rename(target)
        fsync_directory(target.parent)
    finally:
        if temp.exists():
            for directory in dirs:
                if directory.exists():
                    directory.chmod(0o700)
            shutil.rmtree(temp)


def backup_path(state):
    # Keep loader backups outside both the mutable front and the immutable asset paths.
    return state / f"unity-index-before-{CANDIDATE}.html"


def record_backup(path, loader, manifest, uid):
    if os.path.lexists(path):
        data = read_pinned(path, manifest["baselineLoader"])
        metadata(path, 0o400, uid, os.getgid())
        require(data == loader, "rollback copy differs from the live baseline")
        return
    atomic_bytes(path, loader, 0o400, uid, os.getgid())


def run(command, service_root, identity, bundle):
    manifest, candidate_loader = pinned_package(bundle)
    web = service_root / "srv/rh-alpha/web-public"
    state = service_root / "opt/rh-alpha/live-split"
    active = state / "active"
    live = web / "live"
    staged = web / "staging" / CANDIDATE
    index = active / "index.html"
    saved = backup_path(state)
    uid, gid = identity
    for path in (web, live, web / "staging", state, active):
        verify_directory(path)
    lock = os.open(state, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    try:
        fcntl.flock(lock, fcntl.LOCK_EX)
        metadata(index, 0o440, uid, gid)
        notice = active / "alpha-gameplay-data.html"
        read_pinned(notice, manifest["notice"])
        metadata(notice, 0o440, uid, gid)
        check_tree(live / BASELINE, manifest["baselineFiles"], identity)
        with open_regular(index) as source:
            current = source.read(max(manifest[k]["bytes"] for k in ("baselineLoader", "candidateLoader")) + 1)
        prior_handoff = hashlib.sha256(current).hexdigest() == manifest["baselineLoader"]["sha256"]
        already_live = current == candidate_loader
        require(prior_handoff or already_live, "live index differs from both pinned loaders")
        if os.path.lexists(saved):
            read_pinned(saved, manifest["baselineLoader"])
            metadata(saved, 0o400, uid, os.getgid())
        if already_live:
            require(os.path.lexists(saved), "candidate already live but its exact rollback copy is missing")

        if command in {"check", "promote"}:
            check_tree(staged, manifest["candidateFiles"])
            if os.path.lexists(live / CANDIDATE):
                check_tree(live / CANDIDATE, manifest["candidateFiles"], identity)
            else:
                require(not already_live, "candidate already live but its published assets are missing")
        if command == "check":
            return "candidate already selected" if already_live else "baseline verified; candidate staged"

        if command == "promote":
            if already_live:
                return "candidate already selected"
            record_backup(saved, current, manifest, uid)
            publish_tree(staged, live / CANDIDATE, manifest["candidateFiles"], identity)
            require(read_pinned(index, manifest["baselineLoader"]) == current, "live baseline changed during publication")
            atomic_bytes(index, candidate_loader, 0o440, uid, gid)
            return "candidate selected; exact prior homepage saved"

        if not already_live:
            return "baseline already selected"
        previous = read_pinned(saved, manifest["baselineLoader"])
        require(read_pinned(index, manifest["candidateLoader"]) == candidate_loader, "live loader changed before restore")
        atomic_bytes(index, previous, 0o440, uid, gid)
        return "baseline restored; both immutable versions retained"
    finally:
        os.close(lock)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("check", "promote", "restore"))
    roots = parser.add_mutually_exclusive_group(required=True)
    roots.add_argument("--live-host", action="store_true", help="use the actual service paths (root only)")
    roots.add_argument("--fixture-root", type=Path, help="use a local test tree owned by the current user")
    args = parser.parse_args()
    try:
        if args.live_host:
            require(os.geteuid() == 0, "live host operations require root")
            root, identity = Path("/"), (0, grp.getgrnam("rh_alpha_edge").gr_gid)
        else:
            root = args.fixture_root.resolve(strict=True)
            require(root != Path("/") and (root / ".unity-promotion-fixture").is_file(),
                    "fixture mode requires the dedicated local fixture marker")
            identity = (os.geteuid(), os.getegid())
        verdict = run(args.command, root, identity, Path(__file__).resolve().parent)
        print(verdict)
    except (GuardError, OSError, KeyError) as error:
        parser.exit(1, f"REFUSED: {error}\n")


if __name__ == "__main__":
    main()

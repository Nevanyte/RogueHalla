"""Exercise promotion and restore in isolated trees with synthetic assets."""
import hashlib
import json
import os
from pathlib import Path
import stat
import tempfile
import unittest
from unittest.mock import patch

import unity_promotion as op

BUNDLE = Path(__file__).resolve().parent
def receipt(body):
    return {"bytes": len(body), "sha256": hashlib.sha256(body).hexdigest()}



def st_mode(path):
    return stat.S_IMODE(path.stat().st_mode)


class PromotionTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="unity-promotion-test-")
        self.root = Path(self.tmp.name)
        (self.root / ".unity-promotion-fixture").touch()
        self.web = self.root / "srv/rh-alpha/web-public"
        self.state = self.root / "opt/rh-alpha/live-split"
        self.active = self.state / "active"
        self.active.mkdir(parents=True)
        self.index = self.active / "index.html"
        self.notice = self.active / "alpha-gameplay-data.html"
        self.saved = op.backup_path(self.state)
        self.staged = self.web / "staging" / op.CANDIDATE
        self.old = self.web / "live" / op.BASELINE
        self.new = self.web / "live" / op.CANDIDATE
        self.bundle = self.root / "bundle"
        self.bundle.mkdir()
        self.new_loader = (
            '<meta name="roguehalla-backend" content="split">'
            '<meta content="https://game.roguehalla.com wss://game.roguehalla.com">'
            '<a href="/alpha-gameplay-data.html">Read the gameplay data notice</a>'
            '<p>can save completed arena matches as private replays</p>'
            f'<script>load("/alpha-web/{op.CANDIDATE}/Build", '
            f'"/alpha-web/{op.CANDIDATE}/StreamingAssets");</script>'
        ).encode()
        self.old_loader = self.new_loader.replace(op.CANDIDATE.encode(), op.BASELINE.encode())
        self.replace(self.index, self.old_loader)
        self.replace(self.notice, b"Synthetic gameplay data notice")
        self.manifest = {
            "baseline": op.BASELINE,
            "candidate": op.CANDIDATE,
            "baselineFiles": self.seed(self.old, b"old"),
            "candidateFiles": self.seed(self.staged, b"new"),
            "baselineLoader": receipt(self.old_loader),
            "candidateLoader": receipt(self.new_loader),
            "notice": receipt(self.notice.read_bytes()),
        }
        manifest = json.dumps(self.manifest).encode()
        self.pin = receipt(manifest)["sha256"]
        (self.bundle / "manifest.json").write_bytes(manifest)
        (self.bundle / "live-split.html").write_bytes(self.new_loader)

    def tearDown(self):
        for root, dirs, _ in os.walk(self.root):
            Path(root).chmod(0o700)
        self.tmp.cleanup()

    def seed(self, target, label):
        names = ["Licenses/index.html", *(f"Build/test-{index}" for index in range(37))]
        receipts = {}
        for name in names:
            dst = target / name
            dst.parent.mkdir(parents=True, exist_ok=True)
            body = label + name.encode()
            dst.write_bytes(body)
            dst.chmod(0o440)
            receipts[name] = receipt(body)
        for folder, _, _ in os.walk(target, topdown=False):
            Path(folder).chmod(0o550)
        return receipts

    def replace(self, path, data):
        path.parent.chmod(0o750)
        tmp = path.with_name(".fixture-mutation")
        tmp.write_bytes(data)
        tmp.chmod(0o440)
        tmp.replace(path)

    def call(self, command, expected=0, message=None):
        identity = (os.geteuid(), os.getegid())
        with patch.object(op, "MANIFEST_SHA", self.pin):
            if expected:
                with self.assertRaisesRegex(op.GuardError, message or ".+"):
                    op.run(command, self.root, identity, self.bundle)
                return
            result = op.run(command, self.root, identity, self.bundle)
            if message:
                self.assertIn(message, result)

    def test_committed_release_manifest_matches_production_pin(self):
        self.assertEqual(receipt((BUNDLE / "manifest.json").read_bytes())["sha256"], op.MANIFEST_SHA)

    def test_mismatched_bundle_pin_is_refused(self):
        with self.assertRaisesRegex(op.GuardError, "wrong operator manifest hash"):
            op.run("promote", self.root, (os.geteuid(), os.getegid()), self.bundle)
        self.assertFalse(self.saved.exists())
        self.assertEqual(self.index.read_bytes(), self.old_loader)

    def test_exact_round_trip_and_retry(self):
        self.assertEqual(self.new_loader.replace(op.CANDIDATE.encode(), op.BASELINE.encode()), self.old_loader)
        identity = (os.geteuid(), os.getegid())
        previous_inode = self.index.stat().st_ino
        notice_bytes = self.notice.read_bytes()
        self.call("check", message="baseline verified; candidate staged")
        self.assertEqual(self.index.stat().st_ino, previous_inode)
        self.assertFalse(self.saved.exists())
        self.assertFalse(self.new.exists())

        self.call("promote", message="exact prior homepage saved")
        self.assertNotEqual(self.index.stat().st_ino, previous_inode)
        self.assertEqual(self.index.read_bytes(), self.new_loader)
        self.assertEqual(self.saved.read_bytes(), self.old_loader)
        self.assertEqual(st_mode(self.saved), 0o400)
        self.assertEqual(st_mode(self.index), 0o440)
        new_inode = self.index.stat().st_ino
        self.call("promote", message="candidate already selected")
        self.assertEqual(self.index.stat().st_ino, new_inode)

        self.call("restore", message="both immutable versions retained")
        self.assertEqual(self.index.read_bytes(), self.old_loader)
        self.assertEqual(self.notice.read_bytes(), notice_bytes)
        self.call("restore", message="baseline already selected")
        self.call("check", message="baseline verified; candidate staged")
        op.check_tree(self.old, self.manifest["baselineFiles"], identity)
        op.check_tree(self.new, self.manifest["candidateFiles"], identity)

    def test_bad_stage_never_selects_candidate(self):
        asset = self.staged / "Licenses/index.html"
        saved = asset.read_bytes()
        self.replace(asset, bytes([saved[0] ^ 1]) + saved[1:])
        self.call("promote", 1, "wrong SHA256")
        self.assertEqual(self.index.read_bytes(), self.old_loader)
        self.assertFalse(self.new.exists())
        self.assertFalse(self.saved.exists())

    def test_bad_baseline_never_selects_candidate(self):
        self.replace(self.index, b"<html>unrecognized live homepage</html>")
        self.call("promote", 1, "differs from both pinned loaders")
        self.assertFalse(self.new.exists())
        self.assertFalse(self.saved.exists())

    def test_bad_baseline_asset_never_selects_candidate(self):
        asset = self.old / "Licenses/index.html"
        data = asset.read_bytes()
        self.replace(asset, bytes([data[0] ^ 1]) + data[1:])
        asset.parent.chmod(0o550)
        self.call("promote", 1, "wrong SHA256")
        self.assertEqual(self.index.read_bytes(), self.old_loader)
        self.assertFalse(self.saved.exists())

    def test_symlink_is_refused(self):
        asset = self.staged / "Licenses/index.html"
        asset.parent.chmod(0o750)
        asset.unlink()
        asset.symlink_to(self.old / "Licenses/index.html")
        self.call("promote", 1, "not a regular file")
        self.assertFalse(self.new.exists())

    def test_restore_is_allowed_even_if_candidate_backing_file_is_damaged(self):
        self.call("promote")
        asset = self.new / "Licenses/index.html"
        self.replace(asset, b"candidate damage")
        self.call("restore")
        self.assertEqual(self.index.read_bytes(), self.old_loader)
        self.assertEqual(asset.read_bytes(), b"candidate damage")

    def test_restore_requires_exact_backup_and_selected_loader(self):
        self.call("promote")
        self.replace(self.index, b"<html>unrecognized replacement</html>")
        self.call("restore", 1, "differs from both pinned loaders")
        self.replace(self.index, self.new_loader)
        self.saved.unlink()
        self.call("restore", 1, "rollback copy is missing")
        self.assertEqual(self.index.read_bytes(), self.new_loader)

    def test_existing_collision_is_preserved_and_refused(self):
        self.new.mkdir()
        (self.new / "unexpected").write_text("collision")
        self.call("promote", 1)
        self.assertEqual((self.new / "unexpected").read_text(), "collision")
        self.assertEqual(self.index.read_bytes(), self.old_loader)


if __name__ == "__main__":
    unittest.main(verbosity=2)

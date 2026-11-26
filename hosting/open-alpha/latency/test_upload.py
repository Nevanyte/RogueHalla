"""Verify the capture uploader's local validation and ciphertext upload contract offline."""

import base64
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

UPLOADER = Path(__file__).resolve().with_name("upload-capture.sh")
ACCOUNT = "111111111111"
REPLAY = f"replay-{'1' * 32}.json.age"
METRICS = f"metrics-{'2' * 32}-5.json.age"
ENVELOPE = b"age-encryption.org/v1\n-> X25519 " + b"A" * 43 + b"\nplaceholder-ciphertext"
AWS_STUB = """#!/usr/bin/env python3
import json
import os
from pathlib import Path
import sys

args = sys.argv[1:]
Path(os.environ["AWS_SPY"]).write_text(json.dumps(args))
if os.environ["AWS_OUTCOME"] == "precondition":
    sys.exit(254)
print("wrong-checksum" if os.environ["AWS_OUTCOME"] == "wrong"
      else args[args.index("--checksum-sha256") + 1])
"""


class UploadContract(unittest.TestCase):
    def setUp(self):
        scratch = tempfile.TemporaryDirectory(prefix="rh-alpha-upload-")
        self.addCleanup(scratch.cleanup)
        self.scratch = Path(scratch.name)
        aws = self.scratch / "aws"
        aws.write_text(AWS_STUB)
        aws.chmod(0o700)
        self.spy = self.scratch / "aws-spy"
        self.env = dict(os.environ, AWS_SPY=str(self.spy), AWS_PROFILE="test-role",
                        PATH=f"{self.scratch}{os.pathsep}{os.environ['PATH']}")

    def upload(self, artifact, environment="staging", outcome="ok"):
        return subprocess.run(
            [str(UPLOADER), "test-private-bucket", ACCOUNT, environment, str(artifact)],
            capture_output=True, text=True, env=dict(self.env, AWS_OUTCOME=outcome),
        )

    def test_committed_captures_use_scoped_immutable_checked_put(self):
        checksum = base64.b64encode(hashlib.sha256(ENVELOPE).digest()).decode()
        for name, environment in ((REPLAY, "staging"), (METRICS, "live")):
            artifact = self.scratch / name
            artifact.write_bytes(ENVELOPE)
            result = self.upload(artifact, environment)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(result.stdout, f"{checksum} {environment}/{name}\n")
            args = json.loads(self.spy.read_text())
            self.assertEqual(args[:4], ["--profile", "test-role", "s3api", "put-object"])
            for flag, value in (
                ("--region", "ca-west-1"), ("--expected-bucket-owner", ACCOUNT),
                ("--key", f"{environment}/{name}"), ("--body", str(artifact)),
                ("--if-none-match", "*"), ("--server-side-encryption", "AES256"),
                ("--checksum-algorithm", "SHA256"), ("--checksum-sha256", checksum),
            ):
                self.assertEqual(args[args.index(flag) + 1], value)
            self.assertEqual(artifact.read_bytes(), ENVELOPE)

    def test_ignores_plaintext_uncommitted_symlink_and_unsupported_environment(self):
        plaintext = self.scratch / REPLAY
        plaintext.write_bytes(b'{"profileToken":"stub"}')
        partial = self.scratch / f".{REPLAY}.partial"
        partial.write_bytes(ENVELOPE)
        symlink = self.scratch / METRICS
        symlink.symlink_to(partial)
        valid = self.scratch / REPLAY.replace("1", "3")
        valid.write_bytes(ENVELOPE)
        for artifact, environment in ((plaintext, "staging"), (partial, "staging"),
                                      (symlink, "staging"), (valid, "unreviewed")):
            self.assertEqual(self.upload(artifact, environment).returncode, 2)
            self.assertFalse(self.spy.exists())

    def test_ambiguous_or_wrong_receipt_preserves_local_ciphertext(self):
        artifact = self.scratch / REPLAY
        artifact.write_bytes(ENVELOPE)
        for outcome in ("wrong", "precondition"):
            result = self.upload(artifact, outcome=outcome)
            self.assertNotEqual(result.returncode, 0)
            self.assertEqual(result.stdout, "")
            self.assertEqual(artifact.read_bytes(), ENVELOPE)


if __name__ == "__main__":
    unittest.main()

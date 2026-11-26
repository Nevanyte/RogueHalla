#!/usr/bin/env python3
"""Refresh fixed capture-role credentials without accessing any capture spool."""

from datetime import datetime, timedelta, timezone
import grp
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import tempfile

CONFIG = Path("/etc/rh-alpha-capture/credential-broker.json")
CREDENTIALS = Path("/run/rh-alpha-capture-roles")
ROLE_ARN = re.compile(r"arn:aws:iam::[0-9]{12}:role/[A-Za-z0-9_+=,.@/-]+")
GROUP_NAME = re.compile(r"[a-z_][a-z0-9_-]*")
DURATION_SECONDS = 900


def configuration(path):
    info = path.lstat()
    if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_mode & 0o077:
        raise ValueError("credential broker configuration must be a private root-owned file")
    settings = json.loads(path.read_text())
    if not {"staging", "aws_cli"} <= settings.keys() <= {"staging", "live", "aws_cli"}:
        raise ValueError("credential broker configuration requires aws_cli and staging")
    aws = Path(settings["aws_cli"])
    if not aws.is_absolute() or not aws.is_file() or not os.access(aws, os.X_OK):
        raise ValueError("credential broker AWS CLI must be an absolute executable path")
    executable = aws.stat()
    if executable.st_uid != 0 or executable.st_mode & 0o022:
        raise ValueError("credential broker AWS CLI must be owned and writable only by root")
    mappings = {}
    for environment in ("staging", "live"):
        if environment not in settings:
            continue
        rule = settings[environment]
        if (rule.keys() != {"role_arn", "group"} or
                not ROLE_ARN.fullmatch(rule["role_arn"]) or not GROUP_NAME.fullmatch(rule["group"])):
            raise ValueError("credential broker needs exact IAM role ARNs and Unix group names")
        group = grp.getgrnam(rule["group"]).gr_gid
        if group == 0 or any(group == gid or rule["role_arn"] == arn for arn, gid in mappings.values()):
            raise ValueError("credential broker requires distinct non-root groups and role ARNs")
        mappings[environment] = (rule["role_arn"], group)
    return aws, mappings


def assume(aws, environment, arn):
    result = subprocess.run(
        [str(aws), "sts", "assume-role", "--region", "ca-west-1", "--no-cli-pager",
         "--role-arn", arn, "--role-session-name", f"rh-alpha-capture-{environment}",
         "--duration-seconds", str(DURATION_SECONDS), "--output", "json", "--query", "Credentials"],
        env={"AWS_CONFIG_FILE": "/dev/null", "AWS_SHARED_CREDENTIALS_FILE": "/dev/null",
             "AWS_IGNORE_CONFIGURED_ENDPOINT_URLS": "true", "AWS_EC2_METADATA_V1_DISABLED": "true",
             "AWS_MAX_ATTEMPTS": "2"},
        stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=25, check=True,
    )
    credentials = json.loads(result.stdout)
    expiry = datetime.fromisoformat(credentials["Expiration"].replace("Z", "+00:00"))
    now = datetime.now(timezone.utc)
    if not now < expiry <= now + timedelta(seconds=DURATION_SECONDS + 30):
        raise ValueError("credential broker rejected an invalid STS expiry")
    return {
        "Version": 1,
        "AccessKeyId": credentials["AccessKeyId"],
        "SecretAccessKey": credentials["SecretAccessKey"],
        "SessionToken": credentials["SessionToken"],
        "Expiration": expiry.isoformat(),
    }


def publish(root, environment, group, data):
    directory = root / environment
    directory.mkdir(mode=0o750, exist_ok=True)
    os.chown(directory, 0, group, follow_symlinks=False)
    os.chmod(directory, 0o750)
    fd, temporary = tempfile.mkstemp(dir=directory)
    try:
        with os.fdopen(fd, "w") as output:
            json.dump(data, output)
            output.flush()
            os.fchown(output.fileno(), 0, group)
            os.fchmod(output.fileno(), 0o440)
        os.replace(temporary, directory / "credential.json")
    finally:
        Path(temporary).unlink(missing_ok=True)


def main():
    if os.geteuid() != 0:
        raise ValueError("credential broker must run as root")
    aws, mappings = configuration(CONFIG)
    # Omitted live configuration grants no live credential, including after a config change.
    if "live" not in mappings:
        (CREDENTIALS / "live" / "credential.json").unlink(missing_ok=True)
    failed = False
    for environment, (role, group) in mappings.items():
        try:
            publish(CREDENTIALS, environment, group, assume(aws, environment, role))
        except (OSError, ValueError, TypeError, KeyError, subprocess.SubprocessError):
            print(f"capture credential refresh failed for {environment}; check broker configuration and STS")
            failed = True
    return int(failed)


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, TypeError, KeyError, AttributeError):
        raise SystemExit("capture credential broker configuration is invalid")

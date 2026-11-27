# Private alpha capture storage

Use [capture-bucket.yml](capture-bucket.yml) independently of the ALB.
Its versioned, bucket-owner-enforced, private Calgary S3 bucket accepts
conditional creation over HTTPS with SSE-S3. Each designated **IAM role ARN**
can write only capture objects in its own staging or live prefix. The optional
live ARN defaults to no writer. Keep the roles in the bucket account, give
them no other S3 permissions, and use a separate read-only retrieval role.
The bucket and policy are retained on stack removal. Both prefixes expire
after 14 days. S3 marks versions noncurrent at expiration and schedules
physical deletion after one more day. S3 processes lifecycle actions
asynchronously. Record the disposition of the retained bucket with the owner.
[S3 conditional writes](https://docs.aws.amazon.com/AmazonS3/latest/userguide/conditional-writes-enforce.html) · [S3 lifecycle expiration](https://docs.aws.amazon.com/AmazonS3/latest/userguide/lifecycle-expire-general-considerations.html)

Use distinct upload roles even on the shared EC2 host. Scope the EC2
instance role's `sts:AssumeRole` to the exact writer roles and configure
their trust. Keep IMDSv2 required. The root credential broker refreshes
15-minute role sessions and never accesses capture spools. Stage-only
configuration issues only the staging credential. The Source and upload
units run in a systemd slice that blocks both IMDS addresses. The role
credential files are on `/run` and are readable only by the matching
environment group. Keep age identities on the authorized analysis system.
[IMDS configuration](https://docs.aws.amazon.com/AWSEC2/latest/UserGuide/configuring-instance-metadata-options.html) · [CLI credential process](https://docs.aws.amazon.com/cli/latest/userguide/cli-configure-sourcing-external.html)

The Rust writer commits `replay-<32 lowercase hex digits>.json.age` and
`metrics-<32 lowercase hex digits>-<player id>.json.age` in separate private
spools. The uploader checks each final name and native age v1 X25519 envelope,
then sends ciphertext over TLS with a SHA-256 checksum and `If-None-Match: *`.
Use an AWS CLI v2 version that supports conditional `s3api put-object`.

## Configure the credential broker and uploader

Install `broker-capture-credentials.py`, `sweep-capture.py` and
`upload-capture.sh` as root under `/usr/local/lib/rh-alpha-capture/`. Keep the
scripts, containing directory and systemd definitions writable only by root.
Install the two broker units, two uploader units and the isolation slice
from this directory under `/etc/systemd/system/`.

1. Create a distinct private credential group for each environment using the
   group names below. Create root-owned mode 0600
   `/etc/rh-alpha-capture/credential-broker.json` with the exact approved role
   ARN and corresponding group. Omit the entire `live` key for staging-only
   operation. When adding live, use its distinct ARN and group. Replace the
   default AWS CLI path with the full approved, root-owned vendor CLI path
   when needed.
2. Assign `Slice=rh-alpha-capture-isolated.slice` to **both existing Source
   units** using a `[Service]` drop-in. For transient Source units pass
   `--slice=rh-alpha-capture-isolated.slice` in the owner-controlled launch
   command. The checked-in uploader unit already uses that slice. Confirm
   both independent Source units run in the slice before authorizing both
   writer roles. The root broker stays outside the slice.
3. Create `/etc/rh-alpha-capture/staging.env` as root with mode 0600, setting
   the values below. Install its named profile in the root-owned file at
   `AWS_CONFIG_FILE` and grant the staging user read access. The command
   must point only at its environment's credential file. No AWS CLI assume-role
   cache directory is needed for `credential_process`.
4. Add a drop-in to `rh-alpha-upload@staging.service` setting `[Service]`
   `User=rh_alpha`, `SupplementaryGroups=rh-alpha-capture-staging` and
   `ReadWritePaths=<absolute replay spool> <absolute metrics spool>`.
   Repeat for live with `rh_alpha_live`, `rh-alpha-capture-live` and the
   live spools when authorized. The stock writers own mode 0600 files
   in mode 0700 spools, so the current sweep uses the same UID as its Source
   writer. This isolates credentials across environments. It cannot isolate
   the uploader process from a compromised Source process with the same UID.
   A separate non-root ciphertext handoff and separate uploader UID would be
   required for that stronger property. Never run a spool sweep as root.
5. Run `systemctl daemon-reload` and start the broker service once. Check
   that staging's credential file exists with mode 0440 and the live file is
   absent for stage-only setup. Validate the slice before starting the upload
   timer. Enable the broker timer and the approved upload timers. The uploader
   attempts at most eight files per run, resumes across restarts, logs counts
   only and keeps all unconfirmed files. The broker never prints credentials.

```json
{
  "aws_cli": "/usr/local/bin/aws",
  "staging": {
    "role_arn": "<approved staging IAM role ARN>",
    "group": "rh-alpha-capture-staging"
  }
}
```

```ini
# /etc/rh-alpha-capture/staging.env
AWS_CONFIG_FILE=/etc/rh-alpha-capture/staging.aws
AWS_PROFILE=rh-alpha-staging-upload
RH_CAPTURE_AWS_BIN=/usr/local/bin/aws
RH_CAPTURE_BUCKET=<stack output CaptureBucketName>
RH_CAPTURE_OWNER=<approved 12-digit owner account ID>
RH_CAPTURE_REPLAY_SPOOL=<absolute staging replay spool>
RH_CAPTURE_METRICS_SPOOL=<absolute staging metrics spool>
```

```ini
# /etc/rh-alpha-capture/staging.aws
[profile rh-alpha-staging-upload]
credential_process = /bin/cat /run/rh-alpha-capture-roles/staging/credential.json
```

For live use `/run/rh-alpha-capture-roles/live/credential.json` in its profile.
Check `systemctl show -p Slice` for the active Source and uploader units. Prove
denial in the same slice for both `http://169.254.169.254/` and
`http://[fd00:ec2::254]/`, using `systemd-run --wait --pipe --collect
--slice=rh-alpha-capture-isolated.slice` and `curl --noproxy '*'
--connect-timeout 2 --max-time 3 -s -o /dev/null -w '%{http_code}' <URL>`. Any
HTTP status, including 401, means IMDS is reachable and blocks release. Run
the same test outside the slice to distinguish denied access from a disabled
endpoint. Confirm the slice has effective cgroup/BPF support on the host
even if the IPv6 metadata endpoint is disabled.
[systemd slice filtering](https://www.freedesktop.org/software/systemd/man/255/systemd.resource-control.html)
Probe the role files with
`test -r` as each unit's UID and supplementary group. With live enabled,
each identity must see only its own file, and each upload role must be
denied when writing the other prefix. Never print the file contents.

For an owner-operated upload, use the approved role profile with the
single-file command from a private working directory. Transfer only committed
ciphertext to that directory.

```bash
AWS_PROFILE=rh-alpha-staging-upload /usr/local/lib/rh-alpha-capture/upload-capture.sh   "$capture_bucket" "$approved_account_id" staging "$absolute_age_path"
```

Stop the timer and wait for any active sweep to finish before manual local reconciliation. For an ambiguous
network error, checksum mismatch or conditional failure, use a read-authorized
operator to compare `s3api head-object --checksum-mode ENABLED` against the
local ciphertext SHA-256. Remove a local artifact only after those checksums
agree. If the object is absent or different, keep the local ciphertext for
recovery. The service removes files only when the PutObject response confirms
the checksum. Keep checksums and receipts private. On teardown account for the
retained bucket, policy, role trust and outstanding ciphertext.

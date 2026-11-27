# Calgary production alpha operations

The [ingress split](../latency/README.md) serves the Unity page through Cloudflare and routes game traffic directly to the Calgary ALB. This runbook covers the production account backup, host alarms, and the pinned Saturday release. See the [release evidence](../../../docs/evidence/saturday-production-2026-11-26/README.md) for the November 26–27 outcomes.

## Backup storage and alarms

The owner supplies the production EC2 instance ID, IAM role name and path, and the ALB and target group CloudWatch dimension values when reviewing the [CloudFormation template](stack.cfn.json). Use the existing operations stack for updates. Preserve the bucket, bucket policy, topic and alarm identities in the change set. The template uses the deploying account and Region to construct the bucket name and policy ARNs, and the guest recipes target `ca-west-1`. Keep the recipient address and parameter values in private operator records.

The versioned S3 bucket blocks public access and requires TLS, SSE-S3 and create-only single-object writes. The guest role may write under `live/` and publish metrics to `RogueHalla/AlphaOperations`. A separate operator principal reads the encrypted backups. Current backup objects expire after 14 days. Noncurrent versions expire after a further day. [S3 conditional writes](https://docs.aws.amazon.com/AmazonS3/latest/userguide/conditional-writes-enforce.html)

Use the topic reported by the stack to subscribe the approved operator mailbox. Confirm the subscription before the alarm drill. Keep alarm messages limited to operational state and metrics. The Saturday topic used the deployed SNS configuration without a customer-managed key. A future KMS change requires verifying the CloudWatch publisher can still deliver both alarm and recovery notifications. [CloudWatch alarm notifications](https://docs.aws.amazon.com/AmazonCloudWatch/latest/monitoring/Notify_Users_Alarm_Changes.html)

## Hourly database backup

The backup expects a mounted `/srv/rh-alpha`, the production result spool in `spool-live`, PostgreSQL 16 database `rh_alpha_live` over socket directory `/run/rh-alpha-live-pg`, and the verified AWS CLI at `/opt/rh-alpha/verified-capture-cli/bin/aws`. The guest also needs GNU tar, age, jq, OpenSSL, and flock. Install a verified **public** age recipient file. The private decryption identity belongs on the independent restore machine.

Using the bucket output, its owner account and the guest's public recipient file, run from this folder on the target guest as the owner.

```sh
sudo bash backup/install-host.sh "$bucket" "$bucket_owner" "$public_age_recipient"
sudo systemctl start rh-alpha-live-backup.service
sudo journalctl -u rh-alpha-live-backup.service -n 10 -o cat --no-pager
sudo test -s /var/lib/rh-alpha-backup/last-success.json
sudo systemctl enable --now rh-alpha-live-backup.timer
systemctl list-timers rh-alpha-live-backup.timer
```

The service archives the spool first and takes a transactionally consistent `pg_dump` second. It encrypts both streams before they reach disk, checks the returned S3 SHA-256 after every put, uploads `receipt.json` last and atomically advances the local receipt only after that put succeeds. The timer runs hourly with up to five minutes of scheduling jitter and catches a missed schedule when the guest restarts. A backup can fail while the spool changes. Its last good receipt remains available.

The spool copy and database transaction have separate boundaries. A result committed during the spool copy is included in the later dump. A result committed after the dump snapshot and absent from the copied spool can be lost. A copied result already present in the dump must be replayed idempotently when restoring service.

### Independent restore check

Select one complete run with an independent S3 reader. Download `receipt.json`, `database.dump.age`, and `spool.tar.age` and compare both ciphertext SHA-256 values with that receipt. Verify S3 returned its SHA-256 for each object with checksum mode enabled. Use a workstation with age and Docker and the locally available `linux/amd64` image `postgres:16-bookworm`.

```sh
bash backup/restore-mac.sh "$db_ciphertext" "$db_sha256" "$private_age_identity" \
  "$spool_ciphertext" "$spool_sha256"
```

The program decrypts the dump into PostgreSQL tmpfs in a disposable container with networking disabled and validates the spool tar through a pipe. Compare the four printed table counts with a baseline captured at the backup boundary, accounting for intervening writes. A service recovery requires a separate clean host, restoration of both filesets, and validation of storage readiness and pending results before reopening admissions. See [the independent-host recovery procedure](../README.md) for the rehearsal pattern. Keep the production guest's age identity off-host.

## Health checks and response

Run the monitor installer from this folder on the same guest, after the first backup and capture writer health checks pass.

```sh
sudo bash monitor/install-monitor.sh
sudo journalctl -u rh-alpha-live-monitor.service -n 10 -o cat --no-pager
systemctl list-timers 'rh-alpha*'
```

| Alarm | Signal | First check |
|---|---|---|
| Host | EC2 status check fails in two of three one-minute periods | Instance status and system logs |
| Game target | ALB healthy target count falls below one in two of three one-minute periods | Target health reason, Source readiness, ingress listener |
| Capture delivery | Minimum `CaptureHealthy` below one in two five-minute periods | Both writer statuses, sealed spool age, upload service and credentials timer |
| Database backup | Minimum `BackupHealthy` below one in two five-minute periods | Backup service and timer, age of the last confirmed receipt, independent S3 readback |

All four evaluate missing data as breaching. The monitor publishes both health values about once a minute. A capture object waiting at least five minutes marks delivery unhealthy. Capture counter increases hold the signal unhealthy for 12½ minutes so they register across CloudWatch periods. The backup becomes unhealthy at 125 minutes since the last successful receipt or when its service reports failure.

After recipient confirmation, conduct a labeled owner-approved drill on the backup alarm. Capture the CloudWatch action history for ALARM and the return to OK, check the SNS publish/delivery/failure counts for the drill minute, and verify receipt with the operator. A forced state tests the notification route while normal metric evaluation subsequently exercises the transition to OK.

## Saturday rollout and rollback

The [pinned release scripts](release) expect the exact November 26 native artifacts staged in the guest release directories and the public Unity candidate already built and installed in the guest staging tree. Obtain the matching `live-split.html` from the approved operator bundle, place it beside `unity_promotion.py` and the tracked `manifest.json`, and let the preflight verify all pins. The manifest records the original and candidate loaders, the gameplay data notice and both 38-file backing sets.

Apply the native candidate first with `set-live-native.py check` followed by `apply`. Apply two-second fill with `set-live-fill.py check 2` followed by `apply 2`. Check and promote the homepage with `unity_promotion.py check --live-host` followed by `promote --live-host`. Run as root from the vetted host-local operator folder. Native and fill operations require an empty lobby, verify effective systemd arguments and binaries, request graceful drain and confirm database and capture writer health at startup. The homepage operation rehashes both asset sets and atomically selects the pinned loader.

For a full rollback, restore the prior homepage, restore five-second fill, then restore the original one-room native runtime. The following commands address the installed Saturday operator bundle.

```sh
sudo python3 /opt/rh-alpha/saturday-ops/unity-promotion-9e41ed5e/unity_promotion.py restore --live-host
sudo python3 /opt/rh-alpha/saturday-ops/set-live-fill.py rollback
sudo python3 /opt/rh-alpha/saturday-ops/set-live-native.py rollback
```

A blocked drain or changed identity requires investigation before any further switch. Verify the chosen loader SHA-256, compressed WASM, public storage readiness and direct game protocol/content identity. Use two browser sessions to confirm authoritative spawn, Ready and movement, then confirm both players leave and the lobby clears. Trigger a completed run when checking replay delivery. Review the Source disconnect classifications after the smoke and record any unexpected closure, affected layer, code or timeout, and recent RTT. Both immutable Unity revisions and the backup receipt remain available for inspection.

The paired balance release has a separate staging switch, production switch, and rollback. Check which build the live server and homepage select before using either rollback procedure. [Balance release and rollback](release/balance/README.md)

## Local validation

Run from the repository root.

```sh
cd hosting/open-alpha/operations
bash backup/test-backup.sh
bash backup/test-restore-synthetic.sh
bash -n monitor/install-monitor.sh
python3 -B monitor/test_health.py
python3 -B release/test-live-native.py
python3 -B release/test-live-fill.py
python3 -B release/test_promotion.py
python3 -B release/balance/test_balance_release.py
cfn-lint -r ca-west-1 -t stack.cfn.json
cfn-guard validate --rules operations.guard --data stack.cfn.json --output-format json
cfn-guard validate --rules ../latency/ingress.guard --data stack.cfn.json --output-format json
```

The restore test uses a disposable age identity and the local Docker image. The release tests generate temporary asset fixtures and exercise the production pins against the committed manifest. Installer verification of systemd units runs on the target Linux host.

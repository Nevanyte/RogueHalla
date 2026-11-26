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

Use distinct upload roles even on the shared EC2 host. The owner scopes
`sts:AssumeRole` on the existing instance role to the approved role ARNs
and configures each role's trust. Use IMDSv2. Each profile sets its own
`role_arn` and `credential_source = Ec2InstanceMetadata`. These role
scopes govern AWS writes. Host controls govern access to IMDS and isolation
between processes. Keep the age private decryption identities on the
authorized analysis system. [AWS CLI role credentials](https://docs.aws.amazon.com/cli/latest/userguide/cli-configure-role.html#cli-configure-role-ec2)

The Rust writer commits `replay-<32 lowercase hex digits>.json.age` and
`metrics-<32 lowercase hex digits>-<player id>.json.age` in separate private
spools. The uploader checks each final name and native age v1 X25519 envelope,
then sends ciphertext over TLS with a SHA-256 checksum and `If-None-Match: *`.
Use an AWS CLI v2 version that supports conditional `s3api put-object`.

## Configure the periodic uploader

Install the two upload scripts as root under `/usr/local/lib/rh-alpha-capture/`
and the two systemd units under `/etc/systemd/system/`. Keep the script
directory and unit definitions writable only by root. Start with the staging
instance. Repeat for live after its role and spool are approved.

1. Create `/etc/rh-alpha-capture/staging.env`, owned by root with mode 0600.
   Use the values below with the approved profile name, generated bucket name,
   account ID and actual absolute, environment-specific spool paths.
2. For each environment, put the role profile in its own root-owned
   `AWS_CONFIG_FILE`, readable by that environment's Source UID. Set `role_arn`
   and `credential_source = Ec2InstanceMetadata` in the named profile. Keep
   static AWS credentials off the guest.
3. On the EC2 host, resolve the passwd home with `getent passwd rh_alpha`
   for staging and `getent passwd rh_alpha_live` for live. The CLI caches
   temporary credentials in that user's `.aws/cli/cache`. Create only the
   private cache and required parents under that home, using the corresponding
   UID and mode 0700 for the credential directories. If the home is shared or
   unusable, keep the owner-operated upload workflow until the host is ready.
4. Add a drop-in to `rh-alpha-upload@staging.service` setting `[Service]`
   `User=<actual staging Source UID>` and `ReadWritePaths=<absolute replay spool>
   <absolute metrics spool> <absolute CLI cache>`. Use the existing Source UID because the writer
   creates mode 0600 files in mode 0700 directories. Keep separate Source UIDs,
   spools, AWS profiles and unit drop-ins for staging and live.
5. Run `systemctl daemon-reload`, then
   `systemctl enable --now rh-alpha-upload@staging.timer`.
   Check `systemctl list-timers` and the service journal. The sweep prioritizes
   progress across restarts with a saved private cursor and attempts up to eight
   files each run. It logs counts only and keeps all unconfirmed local files.

```ini
AWS_CONFIG_FILE=/etc/rh-alpha-capture/staging.aws
AWS_PROFILE=rh-alpha-staging-upload
RH_CAPTURE_BUCKET=<stack output CaptureBucketName>
RH_CAPTURE_OWNER=<approved 12-digit owner account ID>
RH_CAPTURE_REPLAY_SPOOL=<absolute staging replay spool>
RH_CAPTURE_METRICS_SPOOL=<absolute staging metrics spool>
```

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

# Private alpha capture storage

Use [capture-bucket.yml](capture-bucket.yml) independently of the ALB.
Its versioned, bucket-owner-enforced, private Calgary S3 bucket accepts
conditional creation over HTTPS with SSE-S3. Writer access is restricted to the
designated staging and live **IAM role ARNs**, with only the capture objects in
each role's own prefix allowed. The optional live ARN defaults to no writer.
Keep the roles in the same account as the bucket, give them no separate S3
permissions and keep read-only retrieval on an operator role. The bucket
and its enforcement policy are retained on stack removal. Staging objects
expire after 7 days and live objects after 30, with noncurrent versions eligible
for deletion after one more day. Record the disposition of the retained bucket
with the owner. [S3 conditional writes](https://docs.aws.amazon.com/AmazonS3/latest/userguide/conditional-writes-enforce.html) · [S3 lifecycle expiration](https://docs.aws.amazon.com/AmazonS3/latest/userguide/lifecycle-expire-general-considerations.html)

Use separately approved upload identities even on the shared EC2 host. Each
uploader runs under its designated local UID with read-only access to its
environment's committed replay and metrics files. In the uploader's private
AWS CLI config, set each approved profile's own `role_arn` and
`credential_source = Ec2InstanceMetadata`. Use IMDSv2 and have the owner
scope `sts:AssumeRole` to the exact approved roles on the existing instance
role. Start only the staging role workflow until live is separately enabled.
This scopes cloud writes by role. EC2 process isolation and access to IMDS
must be controlled on the host. Keep the age private decryption identities
on the separate, authorized analysis system. [AWS CLI role credentials](https://docs.aws.amazon.com/cli/latest/userguide/cli-configure-role.html#cli-configure-role-ec2)

The Rust capture writer commits `replay-<32 lowercase hex digits>.json.age` and
`metrics-<32 lowercase hex digits>-<player id>.json.age` in private spools.
The upload script accepts a single committed file with the binary age v1
X25519 envelope. It checks the suffix and envelope, then sends only the
ciphertext with a SHA-256 checksum and `If-None-Match: *`. Use an
AWS CLI v2 version supporting `s3api put-object --if-none-match`.
Use a root-operated spool sweep or the designated private uploader service,
and have its local operator remove a spool entry only after confirmed upload.
Capture checksums and receipt metadata are private.

```bash
AWS_PROFILE=rh-alpha-staging-upload hosting/open-alpha/latency/upload-capture.sh \
  "$capture_bucket" "$approved_account_id" staging "$absolute_age_path"
```

An ambiguous network failure, checksum mismatch or precondition failure
keeps the local ciphertext. Before cleaning it up, have a separate read-authorized
operator retrieve the matching object checksum with
`s3api head-object --checksum-mode ENABLED` and compare it to the local
SHA-256. If it differs or is absent, preserve the local artifact and reconcile
it using the authorized operator workflow. The upload identity has no object
retrieval or deletion capability. On stack teardown account for the retained
bucket, policy, role trust and any outstanding ciphertext receipts.

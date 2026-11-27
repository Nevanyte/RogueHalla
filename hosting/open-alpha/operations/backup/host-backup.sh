#!/usr/bin/env bash
# Owner-operated production backup. No private age identity or AWS access key is used here.
set -Eeuo pipefail
umask 077
[[ $(id -u) -eq 0 ]] || { echo 'root required' >&2; exit 1; }

bucket=${RH_BACKUP_BUCKET:?set RH_BACKUP_BUCKET}
owner=${RH_BACKUP_BUCKET_OWNER:?set RH_BACKUP_BUCKET_OWNER}
region=${RH_BACKUP_AWS_REGION:-ca-west-1}
recipient_file=${RH_BACKUP_RECIPIENT_FILE:?set RH_BACKUP_RECIPIENT_FILE}
[[ $owner =~ ^[0-9]{12}$ && $bucket == "roguehalla-alpha-db-backups-$owner-$region" && $region == ca-west-1 ]]
[[ -f $recipient_file && ! -L $recipient_file ]]
recipient=$(<"$recipient_file")
[[ $recipient =~ ^age1[a-z0-9]{58}$ ]] || { echo 'invalid public age recipient' >&2; exit 1; }
mountpoint -q /srv/rh-alpha
test -d /srv/rh-alpha/spool-live
test -x /usr/lib/postgresql/16/bin/pg_dump
test -S /run/rh-alpha-live-pg/.s.PGSQL.5432
command -v age >/dev/null
aws_bin=/opt/rh-alpha/verified-capture-cli/bin/aws
test -x "$aws_bin"
command -v jq >/dev/null
command -v openssl >/dev/null
export AWS_CONFIG_FILE=/dev/null AWS_SHARED_CREDENTIALS_FILE=/dev/null AWS_PAGER=
export AWS_IGNORE_CONFIGURED_ENDPOINT_URLS=true
unset AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY AWS_SESSION_TOKEN AWS_PROFILE

# One job at a time. The local ciphertext exists only until the upload attempt ends.
exec 9>/run/rh-alpha-live-backup.lock
flock -n 9 || { echo 'backup already running' >&2; exit 1; }
stamp=$(date -u +%Y%m%dT%H%M%SZ)
run_id="${stamp}-$(od -An -N4 -tx4 /dev/urandom | tr -d '[:space:]')"
key_base="live/$run_id"
scratch=$(mktemp -d /srv/rh-alpha/.backup-upload.XXXXXXXX)
trap 'rm -rf -- "$scratch"' EXIT
db_file=$scratch/database.dump.age
spool_file=$scratch/spool.tar.age

# Capture the spool first. A result settled during this copy is in the later DB snapshot.
# A changing file makes tar fail closed; an in-flight result after both boundaries is not covered.
test -z "$(find /srv/rh-alpha/spool-live ! -type d ! -type f -print -quit)"
tar --sort=name -C /srv/rh-alpha -cf - spool-live | age -r "$recipient" > "$spool_file"
test -s "$spool_file"
spool_sha=$(sha256sum "$spool_file" | cut -d ' ' -f 1)

# The later pg_dump transaction is the database recovery boundary while Source stays online.
runuser -u postgres -- /usr/lib/postgresql/16/bin/pg_dump \
  -Fc --no-owner --no-acl -h /run/rh-alpha-live-pg -d rh_alpha_live \
  | age -r "$recipient" > "$db_file"
test -s "$db_file"
db_sha=$(sha256sum "$db_file" | cut -d ' ' -f 1)

upload_checked() {
  local file=$1 key=$2 checksum received
  checksum=$(openssl dgst -sha256 -binary < "$file" | openssl base64 -A)
  received=$("$aws_bin" --no-cli-pager --region "$region" s3api put-object \
    --expected-bucket-owner "$owner" --bucket "$bucket" --key "$key" --body "$file" \
    --if-none-match '*' --server-side-encryption AES256 \
    --checksum-algorithm SHA256 --checksum-sha256 "$checksum" \
    --query ChecksumSHA256 --output text)
  [[ $received == "$checksum" ]] || { echo 'S3_CHECKSUM_CONFIRMATION_MISMATCH' >&2; return 1; }
}
upload_checked "$db_file" "$key_base/database.dump.age"
upload_checked "$spool_file" "$key_base/spool.tar.age"

# A completion receipt is uploaded last. No profile data or plaintext is included.
jq -n --arg run "$run_id" --arg region "$region" \
  --arg dbkey "$key_base/database.dump.age" --arg spoolkey "$key_base/spool.tar.age" \
  --arg dbsha "$db_sha" --arg spoolsha "$spool_sha" \
  --argjson dbbytes "$(stat -c %s "$db_file")" \
  --argjson spoolbytes "$(stat -c %s "$spool_file")" \
  '{version:1,run:$run,region:$region,objects:{database:{key:$dbkey,sha256:$dbsha,bytes:$dbbytes},spool:{key:$spoolkey,sha256:$spoolsha,bytes:$spoolbytes}},boundary:"Spool archive captured before live pg_dump transaction. A result settled during the spool copy is in the later database snapshot; results in flight after both boundaries are not covered."}' \
  > "$scratch/receipt.json"
upload_checked "$scratch/receipt.json" "$key_base/receipt.json"
install -d -o root -g root -m 0700 /var/lib/rh-alpha-backup
install -o root -g root -m 0600 "$scratch/receipt.json" /var/lib/rh-alpha-backup/.last-success.json.new
mv -f /var/lib/rh-alpha-backup/.last-success.json.new /var/lib/rh-alpha-backup/last-success.json
echo "BACKUP_UPLOAD_COMPLETE run=$run_id bucket=$bucket prefix=live database_sha256=$db_sha spool_sha256=$spool_sha"

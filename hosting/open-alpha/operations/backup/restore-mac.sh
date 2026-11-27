#!/usr/bin/env bash
# Isolated PostgreSQL 16 restore from an age ciphertext. Plaintext remains in pipes and Docker tmpfs.
set -Eeuo pipefail
umask 077
[[ $# -eq 3 || $# -eq 5 ]] || { echo 'usage: restore-mac.sh DATABASE_DUMP_AGE EXPECTED_SHA256 AGE_PRIVATE_IDENTITY_FILE [SPOOL_TAR_AGE EXPECTED_SPOOL_SHA256]' >&2; exit 2; }
ciphertext=$1 expected=$2 identity=$3
[[ $expected =~ ^[a-f0-9]{64}$ && -f $ciphertext && -f $identity ]]
command -v age >/dev/null
command -v docker >/dev/null
command -v shasum >/dev/null
actual=$(shasum -a 256 "$ciphertext" | awk '{print $1}')
[[ $actual == "$expected" ]] || { echo 'CIPHERTEXT_SHA256_MISMATCH' >&2; exit 1; }
if [[ $# -eq 5 ]]; then
  [[ $5 =~ ^[a-f0-9]{64}$ && -f $4 ]]
  spool_actual=$(shasum -a 256 "$4" | awk '{print $1}')
  [[ $spool_actual == "$5" ]] || { echo 'SPOOL_CIPHERTEXT_SHA256_MISMATCH' >&2; exit 1; }
  age -d -i "$identity" "$4" | tar -tf - >/dev/null
  echo "SPOOL_ARCHIVE_PASS ciphertext_sha256=$spool_actual"
fi
name="rh-restore-$(od -An -N4 -tx4 /dev/urandom | tr -d '[:space:]')"
cleanup() { docker rm -f "$name" >/dev/null 2>&1 || true; }
trap cleanup EXIT
docker run -d --rm --pull=never --platform linux/amd64 --network none --name "$name" \
  --tmpfs /var/lib/postgresql/data:rw,nosuid,nodev,size=512m \
  -e POSTGRES_HOST_AUTH_METHOD=trust postgres:16-bookworm >/dev/null
ready=0
for _ in $(seq 1 60); do
  if docker exec "$name" sh -c '[ "$(cat /proc/1/comm)" = postgres ] && pg_isready -U postgres -d postgres' >/dev/null 2>&1; then ready=1; break; fi
  sleep 1
done
[[ $ready -eq 1 ]] || { echo 'ISOLATED_POSTGRES_NOT_READY' >&2; exit 1; }
docker exec "$name" createdb -U postgres rh_alpha_live
age -d -i "$identity" "$ciphertext" \
  | docker exec -i "$name" pg_restore -U postgres -d rh_alpha_live \
      --exit-on-error --single-transaction --no-owner --no-acl
docker exec "$name" psql -X -U postgres -d rh_alpha_live -At -F, -c \
  'select (select count(*) from profiles), (select count(*) from matches), (select count(*) from match_results), (select count(*) from gv_ledger);' \
  | awk '{print "RESTORE_COUNTS profiles,matches,match_results,gv_ledger=" $0}'
echo "RESTORE_PASS isolated_container=$name database_ciphertext_sha256=$actual"

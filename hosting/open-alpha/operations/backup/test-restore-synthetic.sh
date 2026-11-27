#!/usr/bin/env bash
# Disposable synthetic end-to-end encryption/dump/isolated restore check.
set -Eeuo pipefail
umask 077
here=$(cd -- "$(dirname -- "$0")" && pwd -P)
work=$(mktemp -d "${TMPDIR:-/tmp}/rh-backup-test.XXXXXXXX")
source_name="rh-backup-source-$(od -An -N4 -tx4 /dev/urandom | tr -d '[:space:]')"
cleanup() { docker rm -f "$source_name" >/dev/null 2>&1 || true; rm -rf -- "$work"; }
trap cleanup EXIT
age-keygen -o "$work/private.agekey" >/dev/null 2>&1
recipient=$(age-keygen -y "$work/private.agekey")
docker run -d --rm --pull=never --platform linux/amd64 --network none --name "$source_name" \
  --tmpfs /var/lib/postgresql/data:rw,nosuid,nodev,size=512m \
  -e POSTGRES_HOST_AUTH_METHOD=trust postgres:16-bookworm >/dev/null
ready=0
for _ in $(seq 1 60); do
  if docker exec "$source_name" sh -c '[ "$(cat /proc/1/comm)" = postgres ] && pg_isready -U postgres -d postgres' >/dev/null 2>&1; then ready=1; break; fi
  sleep 1
done
[[ $ready -eq 1 ]]
docker exec "$source_name" createdb -U postgres rh_alpha_live
docker exec "$source_name" psql -X -U postgres -d rh_alpha_live -v ON_ERROR_STOP=1 -c \
  'create table profiles(id integer); create table matches(id integer); create table match_results(id integer); create table gv_ledger(id integer); insert into profiles values (1),(2); insert into matches values (1); insert into match_results values (1),(2); insert into gv_ledger values (1);' >/dev/null
docker exec "$source_name" sh -c 'mkdir -p /dev/shm/spool-live && printf synthetic > /dev/shm/spool-live/pending-test'
docker exec "$source_name" tar -C /dev/shm -cf - spool-live | age -r "$recipient" > "$work/spool.tar.age"
docker exec "$source_name" pg_dump -U postgres -Fc --no-owner --no-acl -d rh_alpha_live \
  | age -r "$recipient" > "$work/database.dump.age"
db_sha=$(shasum -a 256 "$work/database.dump.age" | awk '{print $1}')
spool_sha=$(shasum -a 256 "$work/spool.tar.age" | awk '{print $1}')
output=$(bash "$here/restore-mac.sh" "$work/database.dump.age" "$db_sha" "$work/private.agekey" "$work/spool.tar.age" "$spool_sha")
printf '%s\n' "$output"
[[ $output == *'RESTORE_COUNTS profiles,matches,match_results,gv_ledger=2,1,2,1'* ]]
[[ $output == *RESTORE_PASS* ]]
echo SYNTHETIC_RESTORE_PASS

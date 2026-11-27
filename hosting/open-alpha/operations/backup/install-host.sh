#!/usr/bin/env bash
# Run on the production guest as root with BUCKET BUCKET_OWNER RECIPIENT_FILE.
set -Eeuo pipefail
umask 077
[[ $(id -u) -eq 0 ]]
[[ $# -eq 3 ]] || { echo 'usage: install-host.sh BUCKET BUCKET_OWNER AGE_PUBLIC_RECIPIENT_FILE' >&2; exit 2; }
bucket=$1 owner=$2 recipient_file=$3
[[ $owner =~ ^[0-9]{12}$ && $bucket == "roguehalla-alpha-db-backups-$owner-ca-west-1" ]]
[[ $recipient_file =~ ^/[a-zA-Z0-9/_.-]+$ && -f $recipient_file && ! -L $recipient_file ]]
[[ $(<"$recipient_file") =~ ^age1[a-z0-9]{58}$ ]]
source_dir=$(cd -- "$(dirname -- "$0")" && pwd -P)
bash -n "$source_dir/host-backup.sh"
install -d -o root -g root -m 0700 /opt/rh-alpha/backup
install -o root -g root -m 0700 "$source_dir/host-backup.sh" /opt/rh-alpha/backup/host-backup.sh
# The game and capture services already traverse this shared directory.
# The backup owns only its private environment file inside it.
[[ -d /etc/rh-alpha && ! -L /etc/rh-alpha ]]
install -d -o root -g root -m 0700 /var/lib/rh-alpha-backup
env_file=$(mktemp /etc/rh-alpha/.live-backup.env.XXXXXXXX)
trap 'rm -f -- "$env_file"' EXIT
{
  printf 'RH_BACKUP_BUCKET=%s\n' "$bucket"
  printf 'RH_BACKUP_BUCKET_OWNER=%s\n' "$owner"
  printf 'RH_BACKUP_AWS_REGION=ca-west-1\n'
  printf 'RH_BACKUP_RECIPIENT_FILE=%s\n' "$recipient_file"
} > "$env_file"
mv -fT -- "$env_file" /etc/rh-alpha/live-backup.env
install -o root -g root -m 0644 "$source_dir/host-backup.service" /etc/systemd/system/rh-alpha-live-backup.service
install -o root -g root -m 0644 "$source_dir/host-backup.timer" /etc/systemd/system/rh-alpha-live-backup.timer
systemd-analyze verify /etc/systemd/system/rh-alpha-live-backup.service /etc/systemd/system/rh-alpha-live-backup.timer
systemctl daemon-reload
echo 'BACKUP_INSTALLED; owner must start service once, inspect receipt, then enable timer'

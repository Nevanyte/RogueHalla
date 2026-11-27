#!/usr/bin/env bash
set -Eeuo pipefail
here=$(cd -- "$(dirname -- "$0")" && pwd -P)
for script in host-backup.sh install-host.sh restore-mac.sh; do bash -n "$here/$script"; done
grep -Fqx 'EnvironmentFile=/etc/rh-alpha/live-backup.env' "$here/host-backup.service"
grep -Fqx 'Unit=rh-alpha-live-backup.service' "$here/host-backup.timer"
echo LOCAL_STATIC_PASS

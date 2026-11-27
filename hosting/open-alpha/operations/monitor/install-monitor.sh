#!/usr/bin/env bash
set -euo pipefail
umask 077
test "$(id -u)" = 0
dir=/opt/rh-alpha/saturday-ops
install -d -o root -g root -m 0700 "$dir" /var/lib/rh-alpha-monitor
source_dir=$(cd -- "$(dirname -- "$0")" && pwd -P)
install -o root -g root -m 0700 "$source_dir/health.py" "$dir/health.py"
for unit in rh-alpha-live-monitor.service rh-alpha-live-monitor.timer; do
  install -o root -g root -m 0644 "$source_dir/$unit" "/etc/systemd/system/$unit"
done
systemd-analyze verify /etc/systemd/system/rh-alpha-live-monitor.{service,timer}
systemctl daemon-reload
systemctl start rh-alpha-live-monitor.service
systemctl enable --now rh-alpha-live-monitor.timer
journalctl -u rh-alpha-live-monitor.service -n 5 -o cat --no-pager

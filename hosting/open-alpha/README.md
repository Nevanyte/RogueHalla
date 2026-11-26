# Open alpha staging rehearsal

One Source Arena room and fresh alpha profiles on a dedicated Ubuntu 24.04 x86-64 host in Calgary (`ca-west-1`). Use a new staging hostname, the reviewed one-room seat/fill settings, and a 2–4 hour window. Record the exact UTC expiry and owner cleanup contact before starting. The provisional CAD 100 ceiling covers the full test, including compute and credits, disks, transfer, backup storage and any IP charges. Price those against the chosen configuration before provisioning. Schedule an independent teardown at the recorded expiry and allow time for a clean drain before it. Training is closed. The existing production Worker, domain and legacy accounts remain in their current environment.

## Build and identify the release

Start with a clean reviewed commit and run the project release gate with PostgreSQL and `bash client-unity/sync-net.sh --check`. The baseline candidate is `96812695434574f8d491f5b17836d3b90e322646`. Build the actual reviewed release SHA on Linux x86-64 or in a matching build environment.

```sh
release_sha=$(git rev-parse HEAD)
test -z "$(git status --porcelain)"
RH_BUILD="$release_sha" cargo build --locked --release -p rh-server --features postgres
```

Start that binary with the staging runtime flags below and the exact reviewed map. Run `RH_SERVER_INFO_URL=http://127.0.0.1:7777/info client-unity/UnityProject/Tools/build-web.sh` against that Source instance on the Unity build host. When the binary is on another host, run an identical local Source instance or forward its private `/info` over an authenticated operator connection. Unity is pinned to 6000.0.85f2 with Web Build Support. Check protocol 24 and a nonzero 16-digit hexadecimal `contentHash`. Record the baked hash from `Assets/Scripts/BakedContent.cs` and compare it to Source `/info`. Optionally generate verified Brotli sidecars with `node client-unity/UnityProject/Tools/precompress-web.cjs` before sealing the Web tree.

Package the binary, chosen map and full `UnityProject/Build/Web` tree together under a release ID containing the SHA. Record SHA256 for the native binary, map and all Web files, toolchain/editor versions, migration revision, reviewed launch flags, protocol and content hash. Build into a fresh tree each time. Keep receipts and private configs outside the served Web root. On the host, verify the transferred checksums and use an operator-owned, read-only Web tree containing regular files, with no symlinks or credentials. Use the normal entry URL so the Web client chooses same-origin WSS.

## Start private PostgreSQL and Source

These commands are for the owner on the dedicated rehearsal guest. Provision PostgreSQL 16 tools, Caddy 2.11.2, `cloudflared`, `age`, Python 3, `jq` and the ordinary launch utilities first. Give the dedicated service accounts `rh_alpha`, `rh_alpha_caddy` and `rh_alpha_tunnel` no interactive shell. Create the shared group `rh_alpha_edge` for Caddy and the tunnel. Mount the encrypted persistent data volume at `/srv/rh-alpha`. Start with a fresh storage volume and no default PostgreSQL cluster listening on TCP. Use one Source writer.

```bash
# Run as root, on the guest with the verified persistent mount present.
set -euo pipefail
mountpoint -q /srv/rh-alpha
PG16=/usr/lib/postgresql/16/bin
"$PG16/pg_ctl" --version
install -d -o postgres -g postgres -m 0700 /srv/rh-alpha/pg16
install -d -o rh_alpha -g rh_alpha -m 0700 /srv/rh-alpha/spool-source
install -d -o postgres -g postgres -m 0755 /run/rh-alpha-pg
runuser -u postgres -- "$PG16/initdb" -D /srv/rh-alpha/pg16 \
  --auth-local=peer --auth-host=scram-sha-256 --locale=C.UTF-8 --encoding=UTF8
cat >> /srv/rh-alpha/pg16/postgresql.conf <<'PG'
listen_addresses = ''
unix_socket_directories = '/run/rh-alpha-pg'
fsync = on
synchronous_commit = on
full_page_writes = on
PG
systemd-run --unit=rh-alpha-postgres --collect --uid=postgres \
  --property=RequiresMountsFor=/srv/rh-alpha \
  "$PG16/postgres" -D /srv/rh-alpha/pg16
# After pg_isready -h /run/rh-alpha-pg succeeds:
runuser -u postgres -- "$PG16/createuser" -h /run/rh-alpha-pg rh_alpha
runuser -u postgres -- "$PG16/createdb" -h /run/rh-alpha-pg -O rh_alpha rh_alpha
```

Create `/etc/rh-alpha/control.token` with a random token readable only by root and `rh_alpha` (for example root:rh_alpha mode 0440 under a traversable private configuration directory). If the owner selects a claim passcode, set `PLAYTEST_PASSCODE` in root-only mode 0600 `/etc/rh-alpha/source.env`. Set `release_sha`, `release_bin` and `release_map` from the receipt, `stage_origin` to the exact HTTPS staging origin, and `stage_min_players` to the reviewed human minimum. Install the reviewed [alpha-gameplay.py](alpha-gameplay.py) as `/opt/rh-alpha/operator/alpha-gameplay.py` (root owned, mode 0755) alongside the verified release. It wraps an existing Source Arena command, preserving its database, origin, control, capture, room and timing arguments while selecting eight seats, signature landmarks and RaidBot. Use the same Source settings when baking Unity. Run Source under its database peer identity and preserve the same spool and instance ID across restarts of this origin.

```bash
set +x
set -euo pipefail
systemd-run --unit=rh-alpha-source --service-type=exec --uid=rh_alpha \
  --property=RequiresMountsFor=/srv/rh-alpha \
  --property=TimeoutStopSec=90s \
  --property=EnvironmentFile=-/etc/rh-alpha/source.env \
  /opt/rh-alpha/operator/alpha-gameplay.py "$release_bin" \
  --runtime source-arena --bind 127.0.0.1:7777 \
  --map "$release_map" --map-pool --instance-id alpha-stage-source \
  --min-players "$stage_min_players" --max-rooms 1 \
  --bot-learning off \
  --database-url 'postgresql:///rh_alpha?host=/run/rh-alpha-pg&user=rh_alpha' \
  --result-spool /srv/rh-alpha/spool-source \
  --api-cors-origins "$stage_origin" --ws-origins "$stage_origin" \
  --client-ip-header x-forwarded-for --trusted-proxies 127.0.0.1 \
  --control 127.0.0.1:7778 --control-token-file /etc/rh-alpha/control.token
# systemd confirms exec, then Source connects, migrates and binds both listeners.
control_auth=$(cat /etc/rh-alpha/control.token)
ready_deadline=$((SECONDS + 120))
until curl --max-time 2 -fs http://127.0.0.1:7777/readyz \
    | jq -e '.ok == true and .state == "ready" and .storage == "ok"' >/dev/null &&
    control_health=$(printf 'Authorization: Bearer %s\n' "$control_auth" \
      | curl --max-time 2 -fs -H @- http://127.0.0.1:7778/control/health); do
  if (( SECONDS >= ready_deadline )); then
    echo 'Source did not become durably ready within 120 seconds. Check journalctl -u rh-alpha-source.' >&2
    exit 1
  fi
  sleep 1
done
unset control_auth
curl --max-time 2 -fsS http://127.0.0.1:7777/info | jq -e \
  '.protocol == 24 and (.contentHash | test("^[0-9a-fA-F]{16}$") and . != "0000000000000000")'
printf '%s\n' "$control_health" | jq -e --arg expected "$release_sha" '.build == $expected'
unset control_health
```

For an existing live or staging Source unit, retain its verified binary and complete origin-specific argument list and environment. Prefix its `rh-server` invocation with `/opt/rh-alpha/operator/alpha-gameplay.py`; keep any existing `--fill-bots 4` or `--bot-brain off` arguments in place because the wrapper replaces those two settings. Review the resulting unit's executable and arguments locally before restart. This recipe does not restart either service or authorize a deployment.

If the guest reboots, recreate the runtime directories before restarting services. Reuse the initialized cluster, spool and checked release. Keep TCP 7777 and 7778 on loopback and restrict shell access to operators. Keep PostgreSQL Unix-socket-only. Use a filesystem check and `ss -lnpt` before opening admissions.

## Start Caddy and the named tunnel

From the owner’s Cloudflare admin workstation, create the named tunnel with `cloudflared tunnel create <staging-tunnel-name>` and route only the isolated staging hostname with `cloudflared tunnel route dns <tunnel-uuid> <staging-hostname>`. Transfer only the connector credential JSON to the guest. Keep the connector outbound and leave the guest without inbound public listeners. Install the reviewed Caddyfile and a filled-in copy of `cloudflared.yml.example` under `/etc/rh-alpha/`. Keep the connector's tunnel credential JSON readable only by its own service identity and root. Remove any Worker or request-header transform from this hostname that changes `CF-Connecting-IP`. Leave Pseudo IPv4 overwrite and Remove Visitor IP Headers disabled. Keep Cloudflare and connector request debugging off for credential routes.

```bash
# Set stage_hostname to the hostname alone and release_web to the receipt's Web tree.
install -d -o rh_alpha_caddy -g rh_alpha_edge -m 2750 /run/rh-alpha-edge
export RH_ALPHA_HOST="$stage_hostname" RH_ALPHA_INGRESS_SOCKET=/run/rh-alpha-edge/caddy.sock
export RH_ALPHA_SOURCE=127.0.0.1:7777 RH_ALPHA_WEB_ROOT="$release_web"
caddy validate --config /etc/rh-alpha/Caddyfile --adapter caddyfile
cloudflared tunnel --config /etc/rh-alpha/cloudflared.yml ingress validate
systemd-run --unit=rh-alpha-caddy --collect --uid=rh_alpha_caddy --gid=rh_alpha_edge \
  --setenv=RH_ALPHA_HOST="$RH_ALPHA_HOST" --setenv=RH_ALPHA_INGRESS_SOCKET="$RH_ALPHA_INGRESS_SOCKET" \
  --setenv=RH_ALPHA_SOURCE="$RH_ALPHA_SOURCE" --setenv=RH_ALPHA_WEB_ROOT="$RH_ALPHA_WEB_ROOT" \
  "$(command -v caddy)" run --config /etc/rh-alpha/Caddyfile --adapter caddyfile
# Start after the Caddy socket exists, owned by rh_alpha_caddy:rh_alpha_edge, mode 0660.
systemd-run --unit=rh-alpha-tunnel --collect --uid=rh_alpha_tunnel --gid=rh_alpha_edge \
  "$(command -v cloudflared)" tunnel --config /etc/rh-alpha/cloudflared.yml run
```

Caddy trusts the Cloudflare visitor address from its group-restricted Unix ingress only. The proxy sends one canonical address to Rust, preserves browser Origin and passes profile credentials in the existing headers. Caddy uses the Source backend for exact `/ws`, `/info`, `/ping`, `/healthz`, `/readyz`, `/api` and the `/api/*` prefix. Control, probe and Training paths return 404. The remaining paths serve exact static files and directory indexes with revalidation. Wasm uses `application/wasm`; Unity data uses `application/octet-stream`. Browser requests use the base asset names and negotiate Brotli or gzip sidecars.

Configure Cloudflare cache bypass for the entire staging hostname, preserve origin headers and disable speculative fetching for native paths. A profile GET can claim a name and return a token. Check that the edge does not cache native responses and that no cache or transform rule overrides the staging policy. Keep a copy of the reviewed configuration outside the guest. [Cloudflare Unix origin routing](https://developers.cloudflare.com/tunnel/concepts/routing/) · [Cloudflare visitor IP headers](https://developers.cloudflare.com/fundamentals/reference/http-headers/) · [Caddy trusted proxies](https://caddyserver.com/docs/caddyfile/options#trusted-proxies-unix)

## Back up and independently restore the database and spool

Keep Caddy and the tunnel connected during the drain so active players can finish. The authenticated control request closes admissions and lets rooms end naturally. Source then settles writes and exits. Reserve time before the stage expiry for this drain and the backup. Set `stage_drain_wait_secs` to the approved natural-drain wait for this run (900 seconds by default).

```bash
(
set +x
set -euo pipefail
drain_wait=${stage_drain_wait_secs:-900}
[[ "$drain_wait" =~ ^[1-9][0-9]*$ ]]
source_pid=$(systemctl show --property=MainPID --value rh-alpha-source.service)
[[ "$source_pid" =~ ^[1-9][0-9]*$ ]]
printf 'Authorization: Bearer %s\n' "$(cat /etc/rh-alpha/control.token)" \
  | curl --max-time 5 -fsS -H @- -H 'Content-Type: application/json' \
      --data '{"v":1}' http://127.0.0.1:7778/control/drain \
  | jq -e '.v == 1 and (.state == "draining" or .state == "terminated")'
drain_deadline=$((SECONDS + drain_wait))
while kill -0 "$source_pid" 2>/dev/null; do
  if (( SECONDS >= drain_deadline )); then
    echo 'Source is still draining. Leave the tunnel connected and wait for exit or use the bounded shutdown below.' >&2
    exit 1
  fi
  sleep 1
done
)
```

If that wait expires, either allow the process to finish naturally or use `systemctl stop rh-alpha-source.service` while the tunnel stays up. SIGTERM starts Source's bounded shutdown, with a 55-second room cap and up to 20 seconds to settle writes plus 5 seconds of HTTP shutdown at the default settings. Check the Source journal for any capped rooms, unsettled writes or failed shutdown, and record the outcome. A failed unit blocks capture. Stop the tunnel and start the backup only after Source has settled and exited successfully.

Use a root-only scratch directory on encrypted storage outside the Web root, and an empty backup output directory. Keep only the age public recipient on the source guest.

```bash
(
set +x
set -euo pipefail
umask 077
source_units=$(systemctl list-units --all --state=active,activating,reloading,deactivating,failed \
  --no-legend --no-pager rh-alpha-source.service)
test -z "$source_units"
systemctl stop rh-alpha-tunnel.service
# Verify the Source exit and database quiescence before proceeding.
test "$(runuser -u postgres -- "$PG16/psql" -X -h /run/rh-alpha-pg -d postgres -Atqc \
  "select count(*) from pg_stat_activity where datname = 'rh_alpha'")" = 0
for setting in fsync synchronous_commit full_page_writes; do
  test "$(runuser -u postgres -- "$PG16/psql" -X -h /run/rh-alpha-pg -d postgres -Atqc "show $setting")" = on
done
work=$(mktemp -d /srv/rh-alpha/backup.XXXXXX)
trap 'rm -rf -- "$work"' EXIT
runuser -u postgres -- "$PG16/pg_dump" -h /run/rh-alpha-pg -d rh_alpha \
  --format=custom --no-owner --no-acl > "$work/database.dump"
cp -a /srv/rh-alpha/spool-source "$work/spool-source"
test -z "$(find "$work" ! -type f ! -type d -print -quit)"
(
  cd "$work"
  find database.dump spool-source -type f -print0 | sort -z | xargs -0 sha256sum > SHA256SUMS
)
tmp_archive=$(mktemp "$backup_dir/.alpha.XXXXXX")
trap 'rm -rf -- "$work"; rm -f -- "$tmp_archive"' EXIT
tar -C "$work" -cf - database.dump spool-source SHA256SUMS | age -r "$age_recipient" > "$tmp_archive"
sync -f "$tmp_archive"
ln -T -- "$tmp_archive" "$backup_dir/alpha-backup.tar.age"
sha256sum "$backup_dir/alpha-backup.tar.age"
)
```

Copy the ciphertext to independent private storage and verify its SHA256 there against the receipt. Restore to a different clean encrypted target with a freshly created empty `rh_alpha` database and peer role as above. Keep its tunnel and Source stopped until both filesets are in place. Verify the sealed archive SHA256 against the receipt before decryption. Use only the operator-produced archive selected by that receipt. Keep the private decryption identity on the restore side.

```bash
(
set +x
set -euo pipefail
umask 077
work=$(mktemp -d /srv/rh-alpha/restore.XXXXXX)
trap 'rm -rf -- "$work"' EXIT
age --decrypt -i "$age_identity_file" "$selected_archive" | tar -xf - -C "$work"
(cd "$work" && sha256sum -c SHA256SUMS)
test -z "$(find "$work" ! -type f ! -type d -print -quit)"
test -z "$(find /srv/rh-alpha/spool-source -mindepth 1 -print -quit)"
test "$(runuser -u postgres -- "$PG16/psql" -X -h /run/rh-alpha-pg -d rh_alpha -Atqc \
  "select count(*) from pg_tables where schemaname not in ('pg_catalog','information_schema')")" = 0
runuser -u rh_alpha -- "$PG16/pg_restore" -h /run/rh-alpha-pg -d rh_alpha \
  --exit-on-error --single-transaction --no-owner --no-acl < "$work/database.dump"
cp -a "$work/spool-source/." /srv/rh-alpha/spool-source/
chown -R rh_alpha:rh_alpha /srv/rh-alpha/spool-source
)
```

If restore fails partway, use a fresh target for the next attempt. Start the matching release privately and verify storage readiness, existing test-account credentials and progression, pending results and idempotent replay where the archive contained pending records. The independent restore must have its own storage and no route to the original PostgreSQL instance. Reopen the original stage only after recording the backup receipt and deciding which copy is authoritative. Retain the encrypted backup and release receipt only for the approved window.

## Verify the rehearsal

- Run the recipe tests on Linux with `CADDY_BIN=/path/to/caddy python3 -B -m unittest discover -s hosting/open-alpha -p 'test_*.py'`. This uses a real Caddy instance and loopback Source stub. Caddy 2.11.2 reports an unnamed Unix peer differently on macOS, so run the oracle in a Linux container when developing on that host.
- From two independent networks, verify public HTTPS and protocol/hash, `/readyz` storage, Wasm content type and negotiated compressed payload, account claim/reopen, independent visitor limits, WSS admission, reconnect, full match and progression after restart. Verify private paths refuse access and there is no direct origin listener. Capture the five-zone smoke and review Rust's disconnect classifications.
- Rehearse restoration into the independent target and a matching-pair staging switch and rollback. Compare artifact receipts, including the private Source build field and baked Unity hash. Treat an identity mismatch, join failure or lost progression as a failed rehearsal.
- At expiry stop admissions and drain, then execute owner-approved cleanup. Record the resulting guest, disk, DNS/tunnel, backup and IP inventory against the retention decision. Production activation uses its own approved change and rollback plan.

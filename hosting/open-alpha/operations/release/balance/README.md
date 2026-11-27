# Quest candidate release and rollback

The operator selects the paired Rust server and Unity Web build
`e85db4eb11ca7087a9e45f6b847d002d4a06a54b`, protocol 24, content
`cb811d19ebed6dfe`. It uses existing source services, both capture writers and
the existing Caddy Unix sockets. It writes an immutable native release directory
and a separate immutable Web directory for each target. Staging's transient
Source is reconstructed using its reviewed service properties. Production's
durable Source receives a single new ExecStart drop-in. The existing preview,
old Unity trees, profile stores, spools, original systemd files, tunnels, ALB
and DNS remain available. The operator selects the active index and gameplay data notice for the target.

The [release manifest](release-manifest.json) records merge `173ad1b05e37309d57e670993cd3054709411448`,
which has the same Git tree as the artifact build. It lists the per-file SHA-256 values, Linux server and
replay verifier identities, and the Unity build. Its SHA-256 is
`7adf83042ed1d44c907f75690484c82e3f7f67303e67d1925d9d95a99c9ac7b3`.

The archive `roguehalla-open-alpha-e85db4eb11ca.tar.gz` contains 47 payload files and the manifest.
It is 212,520,979 bytes and has SHA-256 `8da31f2aec4168703aaa45e6524d1ecceb425c6969d93887a5bc3df45c111b09`.

## Local verification

```sh
python3 -B test_balance_release.py
python3 -B balance_release.py archive-check "$absolute_archive" "$archive_sha256"
```

The archive command unpacks into a disposable local directory. It verifies the
archive checksum and per-member files and metadata, matching client/server
content, Linux x86-64 native binaries, unchanged database migration tree, and
the four split/same-origin loaders. The test suite also covers wrong archive and
loader, path traversal/symlink rejection, concurrent lock, occupied lobby,
candidate failures, and rollback after a failed or interrupted switch.

## Guest application

Use the same verified script and archive on the host. Check the selected stage
root and both targets' identities before mutating either target. This operator expects
the original baseline Source `a553eb1231df4171bba587488f43018429aaecca`.
If staging still selects the prior balance release `3f0740ad84519c84af47604e0c0b758bc20efa88`,
use that release's [pinned operator and recovery instructions](https://github.com/Nevanyte/RogueHalla/tree/5cb3ab5b9b40782ba9df580c2328fb147288edfc/hosting/open-alpha/operations/release/balance)
with `stage-status` and `stage-rollback`. Verify the restored baseline Source and
staging root before running the quest operator. Use the prior operator's production
commands only if production independently requires recovery.

```sh
sudo python3 balance_release.py stage-status
sudo python3 balance_release.py production-status
sudo python3 balance_release.py stage-apply "$absolute_archive" "$archive_sha256" --bots preserve --signature-landmarks
sudo python3 balance_release.py stage-status
```

For the landmark alpha, use `--bots preserve --signature-landmarks` on both
targets. The pool generates the Sentenced church, Plague bog tree, Holy hall,
Fire volcano and Frost cave. The arena fills to four total participants.
When changing a previously selected staging release, wait for an empty lobby,
run `stage-rollback`, and verify the prior loader and Source before applying
the new map selection. The journal retains the original rollback destination.

The earlier `--bots preserve` invocation uses the baseline maps. The existing
`--bots raid` selection fills to eight, enables RaidBot, and includes the landmarks. Both retain two rooms, each accepting up to eight humans, and the two-second fill window. Use the same choice for staging and production. The stage command uses the independently
verified SHA-256 of the current staging root. A reviewed alternate prior
homepage can be passed explicitly with `--prior-index-sha256`. The preview loader is separate from the staging root. Use the staging root's
verified baseline bytes for that argument.

Verify the staging root in Unity, including admission, authoritative movement,
capture, and clearing the lobby. Production requires a separate command and a
selected staging record with the same archive, bot policy and landmark choice.

```sh
sudo python3 balance_release.py production-apply "$absolute_archive" "$archive_sha256" --bots preserve --signature-landmarks
sudo python3 balance_release.py production-status
```

The operator checks current and prior hashes, effective and running command,
both lobby and private counts, and graceful drain. After Source starts it verifies
the running build, protocol, content, database, and both capture writers before
selecting the paired loader. It confirms the local edge serves that loader and
the candidate's pinned Brotli loader asset. Run the real browser smoke
separately. Native and frontend selections are separate atomic operations, with
a brief maintenance interval between them. Stop the playtest window while
cutting over so new admissions cannot arrive in this interval.

## Recovery

```sh
sudo python3 balance_release.py stage-rollback
sudo python3 balance_release.py production-rollback
```

Run only the rollback for the intended target. It restores the prior command,
index and notice from its private per-target journal under
`balance-operator-e85db4eb11ca7087a9e45f6b847d002d4a06a54b/` without requiring the
candidate Web assets to be healthy. The prior balance operator uses the separate
`balance-operator/` journal directory and its own systemd drop-in. If an occupied candidate prevents rollback,
let those sessions finish and retry. The journal and status show the pending
rollback. There is no forced stop. To switch transport while retaining the
candidate build, use `stage-loader same-origin` or `production-loader same-origin`
and return using the corresponding `split` action. Loader actions preserve
existing source processes and check the same candidate's assets and route.

## Verify a completed replay

Use the packaged Linux amd64 `rh-headless` for a recording whose `build` is `e85db4eb11ca7087a9e45f6b847d002d4a06a54b`. Its SHA-256 is `0cd19b269a84eed5fe59086a040401f3eab8ebeca979db51e20de1bc239df355`. Check the encrypted object's checksum before decryption. On the authorized analysis machine, set `verifier` to the absolute path to that binary, `identity` to the local age identity and `replay` to the encrypted object. Run in Bash with the reviewed Linux image already present.

```bash
set -o pipefail
age --decrypt -i "$identity" "$replay" |
  docker run --rm -i --pull never --platform linux/amd64 --network none --read-only \
    --cap-drop ALL --security-opt no-new-privileges \
    -v "$verifier:/opt/rh-headless:ro" --entrypoint /opt/rh-headless \
    rust:1.97.0-bookworm --verify-replay /dev/stdin |
  jq -ec 'select(.verified and .steps > 0 and .contentHash == "same" and
    .build == "e85db4eb11ca7087a9e45f6b847d002d4a06a54b" and .recordedBuild == .build)
    | {build, steps, finalHash, verified}'
```

The stream stays in the pipeline. A successful receipt checks every recorded step against the corresponding simulation. For earlier recordings, select the verifier matching the build recorded in the file.
The prior balance release retains its [manifest and verifier checksum](https://github.com/Nevanyte/RogueHalla/blob/5cb3ab5b9b40782ba9df580c2328fb147288edfc/hosting/open-alpha/operations/release/balance/README.md#verify-a-completed-replay).

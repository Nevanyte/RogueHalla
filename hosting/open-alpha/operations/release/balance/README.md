# Saturday balance release and rollback

The operator selects the paired Rust server and Unity Web build
`0fac6aa75e9bcbc55b701b732e1169702c194d8f`, protocol 24, content
`cb811d19ebed6dfe`. It uses existing source services, both capture writers and
the existing Caddy Unix sockets. It writes an immutable native release directory
and a separate immutable Web directory for each target. Staging's transient
Source is reconstructed using its reviewed service properties. Production's
durable Source receives a single new ExecStart drop-in. The existing preview,
old Unity trees, profile stores, spools, original systemd files, tunnels, ALB
and DNS remain available. The operator selects the active index and gameplay data notice for the target.

The release archive contains 47 payload files and the manifest. The archive is 212,488,764 bytes and has SHA-256 `7692afe9e220780f74ed1d6481097070105419eed587fd2c211d17459c47e061`. The manifest records the per-file bytes and SHA-256, the Linux server and replay verifier identity, and the Unity build. [Release manifest](release-manifest.json)

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
root and both targets' identities before mutating either target.

```sh
sudo python3 balance_release.py stage-status
sudo python3 balance_release.py production-status
sudo python3 balance_release.py stage-apply "$absolute_archive" "$archive_sha256" --bots preserve
sudo python3 balance_release.py stage-status
```

Choose `--bots preserve` for the baseline four-fill policy or `--bots raid` for
eight fill seats, RaidBot and signature landmarks. Both retain two rooms, each accepting up to eight humans, and the two-second fill window. Use the same choice for staging and production. The stage command uses the independently
verified SHA-256 of the current staging root. A reviewed alternate prior
homepage can be passed explicitly with `--prior-index-sha256`. The preview
uses a separate loader and is not a candidate for that argument.

Verify the staging root in Unity, including admission, authoritative movement,
capture, and clearing the lobby. Production requires a separate command and a
selected staging record with the same archive and bot policy.

```sh
sudo python3 balance_release.py production-apply "$absolute_archive" "$archive_sha256" --bots preserve
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
index and notice from the private per-target journal, without requiring the
candidate web assets to be healthy. If an occupied candidate prevents rollback,
let those sessions finish and retry. The journal and status show the pending
rollback. There is no forced stop. To switch transport while retaining the
candidate build, use `stage-loader same-origin` or `production-loader same-origin`
and return using the corresponding `split` action. Loader actions preserve
existing source processes and check the same candidate's assets and route.

## Verify a completed replay

Use the packaged Linux amd64 `rh-headless` for a recording whose `build` is `0fac6aa75e9bcbc55b701b732e1169702c194d8f`. Its SHA-256 is `c58902885dd7a72d71347c4e292ba0c5474ffae6ffa15aa42868bbc666b2dfed`. Check the encrypted object's checksum before decryption. On the authorized analysis machine, set `verifier` to the absolute path to that binary, `identity` to the local age identity and `replay` to the encrypted object. Run in Bash with the reviewed Linux image already present.

```bash
set -o pipefail
age --decrypt -i "$identity" "$replay" |
  docker run --rm -i --pull never --platform linux/amd64 --network none --read-only \
    --cap-drop ALL --security-opt no-new-privileges \
    -v "$verifier:/opt/rh-headless:ro" --entrypoint /opt/rh-headless \
    rust:1.97.0-bookworm --verify-replay /dev/stdin |
  jq -ec 'select(.verified and .steps > 0 and .contentHash == "same" and
    .build == "0fac6aa75e9bcbc55b701b732e1169702c194d8f" and .recordedBuild == .build)
    | {build, steps, finalHash, verified}'
```

The stream stays in the pipeline. A successful receipt checks every recorded step against the corresponding simulation. For earlier recordings, select the verifier matching the build recorded in the file.

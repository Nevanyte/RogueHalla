# scripts

The acceptance gate and the helper scripts around it, plus the launch and deploy scripts of the legacy TypeScript game.

## The gate and its helpers

| File | What it does |
|---|---|
| `gate.sh` | The acceptance gate: formatting, clippy, tests and checks. Work is done when it prints `GATE PASS`. `--quick` while iterating, no flag for the full gate, `--oracle packet/<ID>` on a packet branch, `--help` for the rest. |
| `gate.test.sh` | Checks of the gate's own rules, on a temporary git repository. |
| `readability_check.py` | The readability step of the gate: README, module header and file-size rules ([docs/agent/READABILITY.md](../docs/agent/READABILITY.md)). |
| `readability-allowlist.txt` | What does not meet those rules yet. It only shrinks. |
| `test_readability_check.py` | Tests of the checker (`python3 -m unittest scripts/test_readability_check.py`). |
| `agent-worktrees.sh` | Records which session owns which agent worktree, so one session never removes another's (`claim`, `done`, `list`, `clean`). |
| `agent-worktrees.test.sh` | Checks of that script on a temporary repository. |

## Legacy game: launch and deploy (Node)

These serve the TypeScript game in `packages/`; see [docs/legacy/LEGACY_GAME_README.md](../docs/legacy/LEGACY_GAME_README.md).

| File | What it does |
|---|---|
| `playtest.mjs` | `npm run playtest`: builds the client, runs the production server and opens a Cloudflare quick tunnel. |
| `playtest-signin.mjs` | `npm run playtest:signin`: prints sign-in links for returning playtest players. |
| `playtestLink.mjs`, `tunnelGuard.mjs`, `tunnelPids.mjs` | Support for `playtest.mjs`: the current-address file, the tunnel watchdog, the tunnel process record. |
| `build-server.mjs`, `build-cloud-meta.mjs` | The server bundle and the build identity used by the deploy. |
| `deploy.mjs`, `deploy-lib.mjs`, `deploy-watch.mjs`, `deploy-server.mjs` (with `*.d.mts` types) | The local release deploy (`npm run deploy:local`), its status, rollback and scheduled check. |
| `cf-deploy.mjs` | `npm run deploy`: the Cloudflare Worker and game container publish. |
| `deploy-smoke.mjs` | Smoke test of a prepared release. |
| `cloud-arena-probe.mjs`, `latency-probe.mjs`, `network-probe.mjs` | Staging probes: an arena, ping sweeps and WebSocket round trips. |
| `merge-profile-snapshots.mjs` | Merges profile snapshots into a new private migration snapshot. |

# P-OPEN-ALPHA-STAGE-1: Isolated open-alpha staging origin

- **Serves:** Phase 4 temporary staging rehearsal and Phase 5 item 21 staged rollout preparation, owner-approved 2026-11-24.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer-opus
- **Integration base:** open-alpha candidate `96812695434574f8d491f5b17836d3b90e322646`
- **D-rules touched:** D1 no mechanic changes · D2 n/a · D3 bounded rehearsal · D6 collect existing disconnect classifications · D5/D9 unaffected

## 1. ID and outcome

Prepare one x86-64 Rust Source Arena room and fresh alpha accounts in Calgary for a 2–4 hour rehearsal under the owner's provisional CAD 100 total test cap. An isolated staging hostname uses an outbound Cloudflare Tunnel and a private Caddy origin. Training stays closed for the first rehearsal.

## 2. Dependencies

The exact reviewed alpha release pair, a separate Cloudflare staging hostname, and an owner-reviewed host, retention, expiry and cleanup plan before any provider action.

## 3. Relevant legacy paths

None. The staging rehearsal uses fresh accounts and the candidate Rust/Unity entry points.

## 4. New paths

`hosting/open-alpha/{README.md,Caddyfile,cloudflared.yml.example,test_routes.py}` and this brief.

## 5. Exclusive write scope

The new `hosting/open-alpha/` directory and this brief. The oracle and brief stay fixed during implementation. Owner approval covers these protected hosting files and their local validation.

## 6. Contracts and references

- C-PERSIST and ADR 0006 cover durable results, fenced leases and the result spool. ADR 0020 covers the historical bounded first-playable host. This staging recipe leaves that milestone's configuration in place.
- `crates/rh-server/src/lib.rs:52` and `crates/rh-server/src/api.rs:205` define the Source routes. `crates/rh-server/src/control.rs:955` exposes the release build on private operator health.
- `client-unity/UnityProject/Assets/Editor/RhBuild.cs:66` uses uncompressed Unity build URLs. `client-unity/UnityProject/Tools/precompress-web.cjs:2` produces optional negotiated Brotli sidecars.
- Existing `tools/staging-full-runtime/test_preparation.py` covers its frozen ARM64 runtime packet and remains independent. Existing `tools/unity-browser-reload-proof/local-router.test.mjs` checks the browser harness router. This packet runs the new Caddyfile with real HTTP and a WebSocket upgrade.

## 7. Behaviour to preserve

Keep Source paths, browser Origin, request methods and query intact. Preserve Rust admission, auth and rate-limit authority. Keep private endpoints private and leave production routing, legacy accounts, wire and mechanics untouched.

## 8. Acceptance checks

- Local live Caddy oracle passes. It verifies Source forwarding, WebSocket upgrade, private path refusals, separate verified visitor addresses, no-store credential responses and static content types and compression.
- Operator can identify the exact reviewed Rust and Unity pair and restore both alpha database and Source spool to an independent target. This packet prepares those steps. Provider and browser validation are rehearsal requirements.
- Packet quick gate with protected-path authorization. Integration full gate remains the coordinator's release check.

## Oracle files (read-only for implementers)

- `hosting/open-alpha/test_routes.py`

## 9. Validation commands

- `CADDY_BIN=/path/to/caddy python3 -B -m unittest discover -s hosting/open-alpha -p 'test_*.py'`
- `scripts/gate.sh --quick --allow-protected --oracle packet/P-OPEN-ALPHA-STAGE-1 --base 96812695434574f8d491f5b17836d3b90e322646`

## 10. Risk and suggested model tier

The ingress trust boundary, credential caching and rollback identity call for hosting review. Local tests must exercise the actual config.

## 11. Known traps

A generic loopback HTTP listener lets any guest process supply forwarding headers. A profile GET may claim a callsign. Unity requests stable asset names and negotiates precompressed sidecars. A database dump alone misses pending results.

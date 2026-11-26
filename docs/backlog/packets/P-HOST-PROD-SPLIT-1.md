# P-HOST-PROD-SPLIT-1: Dynamic-only production ingress and reversible split release

## ID and outcome
P-HOST-PROD-SPLIT-1. Prepare the local Calgary ingress recipe so `game.roguehalla.com` carries only HTTPS APIs and WSS through the DNS-only ALB to the existing authoritative Rust listener on loopback `:8777`, while `play.roguehalla.com` remains proxied through its current tunnel and serves the selected Unity release. Include reversible app/Web release selection and a versioned static CDN plan. This packet makes no provider changes.

## Dependencies
The existing Calgary ALB/WAF and staging `latency-staging.roguehalla.com` route. Owner approval, an issued ACM certificate for `game.roguehalla.com`, and owner managed DNS/service changes are required before production use.

## Relevant legacy paths (verified)
- `hosting/open-alpha/latency/{alb.yml,Caddyfile,test_ingress.py,README.md}`: current ALB, private TLS target, Caddy route and ingress oracle.
- `hosting/open-alpha/README.md`: immutable release packaging and isolated Source staging procedure.
- `crates/rh-server/src/main.rs`: `--ws-origins`, `--api-cors-origins`, and loopback Source listener options.

## New paths
`docs/backlog/packets/P-HOST-PROD-SPLIT-1.md`.

## Exclusive write scope
`hosting/open-alpha/latency/{alb.yml,Caddyfile,test_ingress.py,README.md}` and this packet only. No provider operations, DNS edits, Unity edits, protected production source paths, or shared status records.

## Contracts and references
Hosting invariants in `hosting/AGENTS.md`; C-NET/C-OBS; R-AUTH; `hosting/open-alpha/README.md`; ACM and ALB owner setup in `hosting/open-alpha/latency/README.md`.

## Behaviour to preserve
- Keep `play.roguehalla.com` proxied to its existing tunnel and its current served release until an owner-controlled reversible release switch.
- Route only `/ws`, `/info`, `/ping`, `/healthz` and `/api` paths on `game.roguehalla.com`; reject static assets, catch-all paths and private control, probe, training and internal-health paths.
- Preserve literal browser Origin `https://play.roguehalla.com` through ingress. Rust must allow exactly that origin for WSS and API CORS.
- Keep ALB target traffic private, with ALB health checks sent to loopback `/readyz`, and the authoritative live Source at `127.0.0.1:8777`.
- A tested release receipt identifies both the Rust app build and the play Web root; restoring the previous pair must not require DNS movement. Versioned assets are served as `/alpha-web/<full SHA>/...` from `/srv/rh-alpha/web-public/{staging,live}/<full SHA>/...` and old directories remain available for stale tabs.

## Acceptance checks
1. Focused Caddy test proves the staging host still serves its reviewed test assets while the production host returns 404 for `/`, Unity paths and unknown routes.
2. Focused Caddy test proves `game.roguehalla.com/ws` upgrades and forwards literal Origin `https://play.roguehalla.com` and the validated ALB client address.
3. Production listener rule in `alb.yml` contains only dynamic API/WSS route regexes; private routes remain denied by Caddy; static assets do not reach production target.
4. Recipe specifies ACM DNS validation for `game.roguehalla.com`, target `8444 -> 127.0.0.1:8777`, exact origin flags, separate app/Web release identities, stage/rollback without DNS changes, and the `/alpha-web/<full SHA>/` static asset namespace.
5. No AWS/Cloudflare writes, pushes, or runtime publication occur in this packet.

## Validation commands
`python3 -B -m unittest hosting/open-alpha/latency/test_ingress.py` with Caddy 2.11.2 supplied by `CADDY_BIN` (run on Linux where the existing Caddy oracle requires it); `caddy validate --config hosting/open-alpha/latency/Caddyfile --adapter caddyfile` with fixture env values; offline template checks available in the repo if installed.

## Risk and suggested model tier
Medium. Review the host/path allowlist carefully; the main risk is treating the page hostname and backend hostname as interchangeable or accidentally serving a static release through the production ALB.

## Known traps
- Existing docs and tooling describe `play.roguehalla.com` as production legacy. This owner-directed packet changes only the local recipe and does not alter Cloudflare, DNS, current tunnels, or service state.
- Unity loader asset URLs and cache semantics need a versioned namespace before long-lived immutable CDN caching is safe; do not claim the CDN is ready without a matching sealed HTML build.
- `game.roguehalla.com` must be the actual browser backend URL in the owner-sealed Unity build; do not alter Unity files here.

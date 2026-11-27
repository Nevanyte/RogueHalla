# P-ALPHA-CALGARY-LATENCY

## ID and outcome

Prepare a Calgary public IPv4 HTTPS/WSS ALB and independent private capture storage for open alpha.
Keep a parallel Cloudflare tunnel comparison and rehearse the reversible live ingress path.
Serves [Phase 4, item 18](../../ORDER_OF_OPERATIONS.md) with the owner's open-alpha ingress direction.

## Dependencies

Existing Calgary EC2 instance and two public subnets, issued regional ACM certificate for each enabled hostname,
reviewed Source and Unity Web release, isolated staging PostgreSQL/spools, and working staging and live tunnel routes.
The optional capture upload expects committed native age output from the alpha capture writer.

## Relevant legacy paths

No legacy change. The protected `packages/` reference and existing live legacy service remain outside this packet.

## New paths

- [Calgary ALB and Caddy recipe](../../../hosting/open-alpha/latency/).
- [Independent private capture bucket and uploader](../../../hosting/open-alpha/latency/CAPTURE.md).

## Exclusive write scope

`hosting/open-alpha/latency/**` and this packet.

## Contracts and references

[Hosting agent rules](../../../hosting/AGENTS.md), [Source staging rehearsal](../../../hosting/open-alpha/README.md),
[ORDER_OF_OPERATIONS](../../ORDER_OF_OPERATIONS.md) D3/D4, R-AUTH and C-OBS.

## Behaviour to preserve

Keep Source authoritative, TLS and same-origin WebSocket admission, existing per-IP and profile checks, separate staging
data and Unity content identity, and loopback-only control. Preserve both tunnel routes and existing live Source
persistence during cutover and rollback. Keep private capture bodies outside web storage and public routing.

## Acceptance checks

The focused oracle runs Caddy locally against both origin identities and checks the private health route, native API,
upgrade, static compression, spoofed visitor headers, and rejection of unsupported hosts/paths. The upload oracle
must reject plaintext and unsafe paths without invoking AWS, and the bounded sweep must recover its cursor and retain
ambiguous ciphertext across restarts. The staging-only broker refreshes only its fixed role and publishes mode 0440
temporary credentials atomically. The deployed Source and uploader units must pass IPv4 and IPv6 IMDS denial probes in
their effective service slice. Both templates lint in Calgary. For deployment acceptance,
the owner checks target health, listener TLS, release identity, real browser connect/reconnect, matched
tunnel/ALB measurements, exact DNS reversal and cost through cleanup.

## Validation commands

- `uvx --from cfn-lint==1.48.0 cfn-lint -r ca-west-1 -t hosting/open-alpha/latency/*.yml`
- `cfn-guard validate --rules hosting/open-alpha/latency/ingress.guard --data hosting/open-alpha/latency/alb.yml` (repeat for `capture-bucket.yml`)
- `CADDY_BIN=/absolute/path/to/caddy python3 -B -m unittest discover -s hosting/open-alpha/latency -p 'test_*.py'`
- `scripts/gate.sh --base 195ae90d0196d16138cfcd4f99bfa74c556c9449 --allow-protected`

## Risk and suggested model tier

High for any public routing or IAM change. Sonnet implementation and separate owner review of change sets, live
backend identity, ingress and budget before provider operations.

## Known traps

ALB health checks use the target's private IP in the Host header. Lock backend ports to the ALB SG, and require
the literal, valid last appended XFF address. A DNS rollback leaves existing sockets on the previous path until they
close. Confirm the saved live route serves Rust before considering a production change.

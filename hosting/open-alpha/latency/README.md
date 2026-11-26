# Calgary open-alpha ingress

This recipe reflects the owner-directed production split prepared in
`P-HOST-PROD-SPLIT-1`: `play.roguehalla.com` stays proxied through its current
tunnel as the Unity page origin; `game.roguehalla.com` is the DNS-only backend
hostname. The production ALB route serves dynamic APIs and WSS only. The older
single-host live cutover steps below are superseded by **Production split** at
the end of this file. This is local implementation guidance; no provider state
has been changed.

This route adds an IPv4 HTTPS/WSS Application Load Balancer in Calgary (`ca-west-1`).
The existing Cloudflare tunnels stay connected. Use [the staging rehearsal](../README.md)
for the reviewed build, isolated PostgreSQL and Source runtime, backups and drain.
[Capture storage](CAPTURE.md) is an independent stack.

## Owner setup

1. Record the release SHA, exact origin configuration, test window, operator and UTC
   teardown time. Save the current staging and live Cloudflare record IDs and full
   configurations (type, name, target, proxy flag, TTL, tags, comment and settings).
   Keep the existing Cloudflare ingress mappings, Worker routes and credentials in place.
2. Pick two VPC public IPv4 subnets in distinct Calgary Availability Zones, each with
   an Internet Gateway route, a /27 or larger prefix and at least eight free addresses.
   One enabled AZ must contain the existing Calgary EC2 host. Record the two subnet
   CIDRs, the VPC and subnet IDs, the EC2 ID and its primary private IPv4 address.
   Check the instance SGs and subnet ACLs. Only the ALB SG may reach 8443/8444. Remove
   or tighten any overlapping permissions before bringing up the new listeners.
3. Request a public DNS-validated ACM certificate for
   `latency-staging.roguehalla.com` in `ca-west-1`. In Cloudflare create the exact ACM
   validation CNAME as **DNS only**, using its complete target. Check the public
   answer with `dig @1.1.1.1 +short CNAME <validation-name>` and wait for ACM to show
   `ISSUED`. Keep the validation record for renewal. The owner reports the
   `game.roguehalla.com` certificate is already `ISSUED` in `ca-west-1`; verify
   its SAN, account and region during preflight and retain its validation
   record. If a replacement is needed, apply the same DNS validation process.
   `play.roguehalla.com` remains the proxied page/tunnel hostname; it is not the
   ALB certificate name.
   Check CAA permissions if
   validation stalls. [ACM DNS validation](https://docs.aws.amazon.com/acm/latest/userguide/dns-validation.html)
4. Confirm the complete test estimate is at most **CAD 100 total** including existing
   spend, taxes, currency conversion, host, EBS and backups. Include ALB hours, both
   or more public IPv4 IP-hours, LCU hours (including Unity Web downloads and live
   socket concurrency), WAF ACL, rules and requests, Internet egress, CloudWatch and
   any private capture storage/requests. Price the actual hours through teardown and
   check spend before extending the window. Record the owner stop time independently
   of this stack. [AWS calculator](https://calculator.aws/) · [ELB](https://aws.amazon.com/elasticloadbalancing/pricing/) · [VPC](https://aws.amazon.com/vpc/pricing/) · [WAF](https://aws.amazon.com/waf/pricing/) · [S3](https://aws.amazon.com/s3/pricing/)
5. Validate the staging CloudFormation change set in the approved account. Keep
   `EnableProduction=false`. Review only the resources in [alb.yml](alb.yml). The
   stack does not create a host, change any existing rules, request ACM certificates
   or modify DNS. After approval, execute the change set and add the output
   `TargetSecurityGroup` to the EC2 primary ENI **alongside** all existing SGs.

```bash
# Owner workstation. Supply local values from the preflight and use the approved AWS profile.
aws --region ca-west-1 cloudformation deploy --no-execute-changeset \
  --stack-name "$stack" --template-file hosting/open-alpha/latency/alb.yml \
  --parameter-overrides "VpcId=$vpc" "PublicSubnetA=$subnet_a" "PublicSubnetB=$subnet_b" \
    "SourceHostInstance=$instance" "StagingCertificateArn=$stage_certificate_arn" \
    EnableProduction=false
```

The default rule returns 404. The staging rule matches the exact staging hostname
and the reviewed native API and Web build paths. The production rule matches
`game.roguehalla.com` and only `/ws`, `/info`, `/ping`, `/healthz` and `/api/*`
dynamic routes; it never serves the Unity page or assets. The live certificate,
rule, target group and target SG rules are conditional. Managed WAF Core Rule Set counts at first. Keep
request sampling off to avoid storing claim and profile headers. Review counts,
request volume and connection metrics. Use a reviewed change set with
`EnforceManagedRules=true` to qualify blocking and `false` to restore Count.
Repeat the claim, Web and WSS tests when changing the WAF setting.
[ALB rules](https://docs.aws.amazon.com/elasticloadbalancing/latest/application/rule-condition-types.html) · [WAF Count](https://docs.aws.amazon.com/waf/latest/developerguide/web-acl-rule-group-override-options.html)

## Install the private target

On the existing EC2 host, verify the live Source runtime, loopback bind and active
Web root from the running service and its release receipt. The staging process must run
`--runtime source-arena` with a separate service user, loopback port, control listener
and token, database identity and storage, result spool and Web release root. Keep the
live process and its persistence running. Both staging paths must bring the browser to
the same staging binary and Web build. Set staging `--ws-origins` and
`--api-cors-origins` to a comma-separated list of the exact direct staging and isolated
Cloudflare staging HTTPS origins. Keep existing live origin, per-IP, profile and claim
settings. Verify that both Source configurations trust only loopback for
`--client-ip-header x-forwarded-for`. Browsers must use the normal same-origin URL.
Keep control, training, probe and PostgreSQL private.

Create the root-owned directory `/etc/rh-alpha-alb` with mode 0755.
Install Caddy 2.11.2's recipe at `/etc/rh-alpha-alb/Caddyfile` and
`rh-alpha-alb@.service` at `/etc/systemd/system/rh-alpha-alb@.service`. The unit
uses `/usr/local/bin/caddy`. Create non-login system users `rh-alpha-alb-staging` and
`rh-alpha-alb-live` for the instances you use, with read access to their respective
immutable, verified Web roots. Populate root-owned `/etc/rh-alpha-alb/staging.env`
and, later, `live.env` with mode 0600. Values below are placeholders for owner inputs.

```ini
RH_ALB_HOST=latency-staging.roguehalla.com
RH_ALB_TARGET_IP=<host primary private IPv4>
RH_ALB_TARGET_PORT=8443
RH_ALB_SUBNETS="<public subnet A IPv4 CIDR> <public subnet B IPv4 CIDR>"
RH_ALB_CERT_FILE=/etc/rh-alpha-alb/staging.crt
RH_ALB_KEY_FILE=/etc/rh-alpha-alb/staging.key
RH_ALB_SOURCE=127.0.0.1:<separate staging Source port>
RH_ALB_WEB_ROOT=<staging release Web root>
```

Use 8444, `game.roguehalla.com`, the existing live Rust loopback address
`127.0.0.1:8777`, and a dedicated certificate/key in `live.env`. The production
Caddy instance serves no Web content; set its otherwise-unused
`RH_ALB_WEB_ROOT=/var/empty` while only the staging instance receives a release
Web root.
On the
guest generate each private cert with `openssl req -new -newkey rsa:2048 -nodes
-x509 -days 90 -subj "/CN=$host" -addext "subjectAltName=DNS:$host" -keyout
"$key_file" -out "$cert_file"` under `umask 077`. Grant the appropriate Caddy
service group read access to its one key, mode 0440. Follow the same process before
expiry. Run `systemctl daemon-reload` and
`systemctl enable --now rh-alpha-alb@staging.service`. Add live Caddy only during the
live preflight. Keep existing Caddy and cloudflared service units running.

The public ALB authenticates the hostname with ACM. The second TLS leg stays in
the VPC and uses the ALB SG and target SG as its trust boundary. The ALB encrypts
traffic to Caddy without validating Caddy's private cert. Caddy binds only to the
primary private address, checks the listener and the two ALB source subnet CIDRs,
and only accepts the validated, literal rightmost address that the ALB appended
to XFF. Earlier values and alternate proxy identity headers cannot become Rust's
visitor address. The health request uses the target's private Host and checks
Source `/readyz` over loopback. Both the listener and Caddy reject public access
to the internal health route. Review target health explicitly. ALB can route to
unhealthy targets when all targets in a group fail checks.
[Target TLS](https://docs.aws.amazon.com/elasticloadbalancing/latest/application/load-balancer-target-groups.html) · [Health check Host](https://docs.aws.amazon.com/elasticloadbalancing/latest/application/load-balancer-troubleshooting.html) · [XFF append](https://docs.aws.amazon.com/elasticloadbalancing/latest/application/x-forwarded-headers.html)

## Stage and compare the two paths

1. Wait for the staging target group to report `healthy`. Before DNS publishing,
   use the check below to test the host, SNI and ALB together. Try `/`,
   the gameplay data notice, a build Wasm and data asset, `/info`, `/ping`, `/healthz` and `/readyz`.
   Confirm the Web and Source match the release receipt. Check that `/control`,
   `/probe`, `/training`, `/__rh_alb_health` and an unknown host/path all fail.
2. Make only the direct staging hostname a **DNS-only CNAME** to the ALB DNS
   name. Keep the distinct isolated staging tunnel hostname proxied and serving
   the same staging Source. Confirm public DNS, the browser's same-origin WSS,
   `Cache-Control: no-store` on native routes and correct gzip/Brotli and MIME on
   Unity assets. Test a valid browser Origin and a rejected foreign Origin,
   a claim and reconnect, and the existing per-IP admission behaviour. Check
   that a caller's forged XFF or CF visitor header cannot change the address
   used by Rust.
3. Run each path with the same build, client network/region and room workload in
   the same test window. Record matched samples, RTT p50/p95/p99, upgrades,
   reconnects and disconnects, with the cause or close code where observable.
   Exercise two concurrent paths, including a tunnel session during ALB DNS
   publication. Use client transport timestamps for game Ping/Pong and measure
   the handshake separately. The 120-second ALB idle timeout requires traffic
   within that interval. Include a real long-lived connection in the test.

```bash
host=latency-staging.roguehalla.com
curl --fail --show-error --silent --connect-to "$host:443:$alb_dns:443" "https://$host/info"
aws --region ca-west-1 elbv2 describe-target-health --target-group-arn "$stage_target_group_arn"
```

### Cloudflare per-connection comparison

When the matching Cloudflare zone has Enterprise Logpush, stream the
`websocket_analytics` dataset to approved private storage. Filter on the exact
tunnel hostname and `ClientRequestPath == "/ws"` and record UTC
`EdgeStartTimestamp` and `EdgeEndTimestamp`, `ColoCode`, directional byte counts,
`ConnectionCloseReason`, `ConnectionCloseSource` and
`ConnectionTransportCloseCode`. Keep `RayID` only where needed to correlate
an upgrade with permitted HTTP request logs. Omit client IP and user agent from
the export. Each record arrives after a connection closes. Calculate duration
from the timestamps using one documented timestamp unit. Without that dataset,
use only the matched client samples and note the provider telemetry gap.
[WebSocket Analytics availability](https://developers.cloudflare.com/analytics/changelog/product-group/analytics/2/) · [Dataset fields](https://developers.cloudflare.com/logs/logpush/logpush-job/datasets/zone/websocket_analytics/)

## Legacy single-host live cutover (superseded)

The historical procedure below describes the prior single-host model. Do not use
it for this production split: it moves the page hostname to the ALB and sends Web
assets through the production target, both of which are prohibited by the current
owner direction.

1. After staging acceptance and owner approval, confirm the intended live
   hostname currently reaches the same live Rust Source process that the ALB
   will use. If the hostname still leads to a legacy or different service, stop
   the cutover. Install and start the independent 8444 Caddy unit. Review a
   change set supplying `EnableProduction=true`,
   `ProductionHostname=<exact live Rust hostname>` and
   `ProductionCertificateArn=<issued Calgary ARN>`. Keep all staging inputs.
2. Execute only the reviewed changes, wait for the live target to become
   healthy, and test the direct ALB using `curl --connect-to` or a local
   browser DNS override for the live hostname. Verify the public cert and
   exact host, Web/protocol/content identity, valid and invalid Origin, claim,
   WSS progression and reconnect against the existing live Source socket.
   Keep existing player sessions and service processes running. Check the
   proxied route also works immediately before touching live DNS.
3. Edit only the saved live Cloudflare record by its ID. Keep its name and
   type `CNAME`, set content to the ALB DNS name, set the proxy flag to
   **DNS only**, and choose the approved, supported low TTL before starting
   the trial. Verify public authoritative DNS and external resolvers, and
   confirm live WSS and native route health from an external browser.
4. If the trial breaches a pre-agreed error, connection, latency or cost
   threshold, or at expiry, restore the exact saved proxied CNAME by record
   ID. Restore its original tunnel target, **Proxied** flag, TTL (including
   Auto), tags, comment and settings. Confirm the saved values in Cloudflare,
   then verify the proxied Web and WSS externally. Wait for DNS caches to
   converge and watch existing direct sockets drain. A DNS change only moves
   newly resolved connections. Do not stop live Source to move sockets.
5. After rollback/scheduled completion, remove the direct staging CNAME or
   restore its prior record. When direct traffic has drained, stop the
   added private Caddy units and remove only the added target SG from the
   EC2 ENI. Delete the ALB stack in the approved owner window. Check
   that the ALB/WAF, new rules, target groups and public IPv4 use are gone.
   The pre-existing staging and live tunnel configurations stay active.

## Production split: dynamic backend and proxied page

`play.roguehalla.com` remains **Proxied** and keeps its current Cloudflare tunnel
target and route. Its selected Unity Web root provides the page. The sealed Unity
release must set `<meta name="roguehalla-backend" content="split">`, causing its
game socket and API requests to use `https://game.roguehalla.com`; the owner
seals and publishes this HTML. The accepted browser Origin at the Rust server is
the literal `https://play.roguehalla.com`. Start the existing live Source with
both `--ws-origins https://play.roguehalla.com` and
`--api-cors-origins https://play.roguehalla.com`, preserving the other live
admission and persistence flags. `game.roguehalla.com` does not serve `/`,
Unity paths, or arbitrary paths. Its only ALB route patterns are `/ws`, `/info`,
`/ping`, `/healthz`, `/api`, and `/api/*`; private paths stay denied by Caddy.

### Certificate and listener preflight

The owner reports a public DNS-validated ACM certificate for the exact name
`game.roguehalla.com` is already `ISSUED` in `ca-west-1`. Verify its SAN,
account, region, and listener eligibility. Retain its validation CNAME for
renewal. If replacement is needed, publish the ACM validation CNAME as
DNS-only, verify it publicly with `dig`, wait for ACM `ISSUED`, and keep that
record. Configure the production listener rule for host
`game.roguehalla.com`, using that ACM certificate. Keep the ALB target TLS port
8444 restricted to the ALB security group. The production Caddy service binds
only the host's private IPv4 address, uses its private target certificate, and
reverse proxies only to `127.0.0.1:8777`. Keep the existing authoritative Source
process, database, cloudflared connector and tunnel route running throughout
preflight. The ALB target health request uses its private Host and internally
rewrites `/__rh_alb_health` to Source `/readyz`; public access to either internal
health path is denied.

Before any public DNS change, check the ALB host, TLS and routing while pinning
resolution to the ALB:

```bash
host=game.roguehalla.com
curl --fail --show-error --silent --connect-to "$host:443:$alb_dns:443" "https://$host/info"
curl --include --connect-to "$host:443:$alb_dns:443" "https://$host/"
curl --include --connect-to "$host:443:$alb_dns:443" "https://$host/Build/Web.wasm"
```

The first request must match the live Source release receipt. The latter two
must return 404. Also verify `healthz`, a valid API call and WSS with Origin
`https://play.roguehalla.com`; test an unrelated Origin is rejected by Rust.
Check `/control`, `/probe`, `/training`, `/__rh_alb_health` and unknown paths
are denied. Check the target group is healthy and that forged incoming XFF or
Cloudflare IP headers cannot replace the ALB-appended address. Test the
currently proxied `play.roguehalla.com` tunnel and page in parallel.

### Stage and rollback app and page together without DNS changes

Package each candidate as a release receipt with the Rust binary SHA, map,
protocol/content identity, Web tree SHA manifest, backend mode and the exact
sealed HTML SHA. Store app and Web files under separate immutable, read-only
release directories named with the same release SHA. Never overwrite a release
directory. Stage both from the isolated staging Source and staging tunnel first.
For production preflight, keep DNS unchanged: send `game.roguehalla.com` requests
through `curl --connect-to` or a local browser DNS override to the ALB, and use
the existing tunnel's staging hostname for the candidate page. The owner owns
the final tunnel release switch and HTML seal/publication.

At an approved release, switch the app binary and page Web root as one reviewed
release pair using the service and tunnel's existing release selectors, and
record both prior selector values before switching either. Keep both prior
immutable directories available. Validate the selected page's HTML identity,
protocol/content handshake, WSS admission, API claims, reconnect and a complete
match. If any check fails, restore both previous selectors from the receipt;
leave all DNS records and tunnel routes as they were. This pair switch is the
rollback boundary, and it does not require changing the `play` or `game` DNS
records. Do not replace the live Source binary beneath active rooms; drain or
use the existing restart policy before changing the app selector.

Only after the owner accepts the release and separately approves DNS, change
the saved `game.roguehalla.com` record to a DNS-only CNAME for the Calgary ALB.
Do not edit the `play.roguehalla.com` record: it remains Proxied to the current
tunnel. Rollback of the app/page release pair does not require DNS movement.
If DNS itself is later reverted, restore only the saved `game` record by ID and
its exact original type, target, proxy flag, TTL, tags, comment and settings;
leave `play` untouched. Existing sockets drain naturally; DNS only affects new
resolutions.

### Versioned static CDN plan

Static delivery stays on `play.roguehalla.com`, behind its existing proxied
tunnel/CDN path; the DNS-only game hostname carries no static bytes. Publish
each Web build into its immutable directory
`/srv/rh-alpha/web-public/{staging,live}/<full SHA>/{Build,TemplateData,StreamingAssets,Licenses}/`.
The page's sealed asset URLs use `/alpha-web/<full SHA>/...`; old directories
remain available while old pages can still be open. Apply long-lived immutable
cache headers only to that versioned prefix. Keep the selected HTML entry point
revalidating (`no-cache`) and APIs/WSS uncached. Verify Unity's loader and every
relative asset request resolve inside this namespace before enabling it. The
current fixed `/Build/*` paths are not sufficient to claim immutable CDN cache
safety. Until the sealed Unity HTML selects the prefix, use the existing
proxied tunnel with conservative asset cache headers and retain the known-good
Web root for rollback. No CDN or cache configuration is changed by this local
recipe.

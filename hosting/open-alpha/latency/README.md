# Calgary open-alpha ingress

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
   `ISSUED`. Keep the validation record for renewal. Apply the same process to the
   confirmed live Rust hostname before enabling live. Check CAA permissions if
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
and the reviewed native API and Web build paths. The live certificate, rule, target group
and target SG rules are conditional. Managed WAF Core Rule Set counts at first. Keep
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

Use 8444, the exact live Rust hostname, the existing live Rust loopback address,
the active live Web root and a dedicated certificate/key in `live.env`. On the
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

## Live cutover and exact reversal

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

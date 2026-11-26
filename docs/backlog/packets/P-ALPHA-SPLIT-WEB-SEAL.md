# P-ALPHA-SPLIT-WEB-SEAL deploys one Web build over both ingress routes

- **Serves** Owner-directed split ingress for the open alpha
- **Integration base** `feature/unity-rework` at `5a4fcee9c`
- **D-rules touched** D5 release deployment and rollback. D1, D2, D6 and D9 are out of scope.

## 1. ID and outcome

Keep the public loader and Unity download on the Cloudflare frontend. Prepare the reviewed
staging and production loader pages for direct gameplay and their same-origin rollback pages
from one Unity build. A release can retain old asset URLs while publishing a new loader page.

## 2. Dependencies

P-ALPHA-SPLIT-WEB supplies the trusted route marker and exact backend host policy.
P-HOST-PROD-SPLIT-1 supplies the direct production backend.

## 3. Relevant legacy paths

The compiled Unity Web loader is the release surface. No legacy project files change.

## 4. New paths

`hosting/open-alpha/prepare-split-web.py` writes four loader pages into a private release
directory. The tunnel Caddy serves reviewed static directories under `/alpha-web/<build SHA>/`.

## 5. Exclusive write scope

The preparation script, the tunnel Caddyfile, its routing tests, and this packet.

## 6. Contracts and references

Production and staging get only their exact HTTPS and WSS sources in the Content Security
Policy. Scope long-lived caching to existing files in versioned static asset directories.

## 7. Behaviour to preserve

Cloudflare remains the frontend. Both route modes use the same public page, browser origin,
account data notice, asset version and Unity ABI. Retain old versioned directory contents
while clients may still reference them. Keep the existing unversioned Web routes for older clients.

## 8. Acceptance checks and oracle

The Caddy route serves the expected encoded asset with its MIME type and immutable cache
policy. Deny missing, unversioned, raw compressed and private routes in the reserved namespace.
Seal only against the exact Unity build revision. A wrong or repeated loader marker fails.

## 9. Validation commands

Run `CADDY_BIN=/path/to/caddy python3 -m unittest discover -s hosting/open-alpha -p test_routes.py`
on Linux. Run the preparation script on the unpublished Web root, then compare all rendered
routes, same-host fallback and asset request paths during the staging browser checks.

## 10. Risk and suggested model tier

User-facing pages can load a mismatched Wasm file if an immutable asset namespace is reused.
Publish bytes under the sealed build SHA and refuse to replace an existing version.

## 11. Known traps

Keep the data notice absolute. Do not enable edge caching for the HTML shell, APIs, WebSocket
upgrades, or errors. Confirm the live and rehearsal web roots are scoped to their environments.

# P-ALPHA-SPLIT-WEB

## ID and outcome

Serve the Unity Web page through Cloudflare while routing account traffic and Arena sockets to the paired Calgary
backend. Supports Phase 4 item 18 and Phase 5 item 21 of the [order of operations](../../ORDER_OF_OPERATIONS.md).

## Dependencies

Each Web page and its paired backend use the same account database for that environment. The paired backend permits
the exact page origin for account CORS and WebSocket admission. Release HTML and players must come from the same release.

## Relevant legacy paths

This is a Unity and native Source rollout change. No legacy mechanics are involved.

## New paths

This packet brief.

## Exclusive write scope

This brief, `client-unity/RoguehallaNet/Net/{AccountConnectionPolicy,AccountEndpoint,AccountSession}.cs` and their
synchronized Unity copies, `client-unity/RoguehallaNet.Tests/AccountSessionTests.cs`, and these files under
`client-unity/UnityProject/Assets/`:

- `Plugins/WebGL/RhPage.jslib` and `WebGLTemplates/Roguehalla/index.html`
- `Scripts/Bootstrap.cs`, `Scripts/Net/{AccountCoordinator,MatchClient.Accounts,MatchClient.Resume}.cs` and `Scripts/UI/HubLobbyActivityView.cs`
- `Tests/Editor/MatchLaunchEditModeTests.cs`

## Contracts and references

[R-WEB](../../REQUIREMENTS.md), [C-NET and C-PERSIST](../../CONTRACTS.md),
[Unity client](../../adr/0008-unity-client.md), and [Calgary ingress](P-ALPHA-CALGARY-LATENCY.md).

## Behaviour to preserve

The account API and WSS share a backend origin for ownership. The split setting fixes the pair
`play.roguehalla.com` to `game.roguehalla.com` and `alpha-staging.roguehalla.com` to `latency-staging.roguehalla.com`.
Managed pages take their settings from the release document and ignore endpoint queries. The default release template
uses the page's own HTTPS origin and WSS. Local routes keep their existing overrides and diagnostics. The existing
PlayerPrefs key remains based on the original page account scope across cutover and rollback. Match checkpoints and
API request authorization retain the actual backend identity.

## Acceptance checks

- The account tests verify stored sign-in, exact REST and WSS pairs, rejection of a mismatched socket, and rollback.
- The Unity launch tests verify the real startup path ignores all three endpoint query arguments on both managed
  pages before and after split activation. Loopback Arena and Training remain usable with their existing overrides.
- Release acceptance uses the real staging player to claim an account on the same-origin route, load it and play after
  the split, then load it again after rollback. Check the browser's destinations and the corresponding backend identity
  across both transitions.

## Validation commands

- `dotnet test client-unity/RoguehallaNet.Tests`
- `bash client-unity/sync-net.sh --check`
- `scripts/gate.sh --base a915f833a`

## Risk and suggested model tier

Client networking implementer and independent review. A wrong database mapping can refuse existing sign-ins. An old
page or player cached during the release can delay activation or rollback.

## Known traps

Set the single `roguehalla-backend` meta tag in built `index.html` to `content="split"` to activate, and restore
`content="same-origin"` to roll back. Keep the static page origin and directory stable across both operations. Reloads
apply the new route. Already-open sockets stay on the route they joined.

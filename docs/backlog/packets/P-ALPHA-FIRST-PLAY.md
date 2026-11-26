# P-ALPHA-FIRST-PLAY · The first visit and first arena plan

## 1. ID and outcome

P-ALPHA-FIRST-PLAY serves the November 26 owner request for UI and first-play improvements to the Saturday Unity alpha. A first-time player can read how to enter the arena, sees available destinations, and receives planning instructions that match their own server-dealt choices.

## 2. Dependencies

Start from the deployed split client and Rust service at `dbaa701be`. Keep the existing public hostname, telemetry disclosure and account behaviour. The coordinator builds and rehearses a staging-only Web preview after both client changes.

## 3. Relevant legacy paths

The current shell follows `docs/rework/LEGACY_LAYOUT.md` section 15. The launch panel, walkable atrium, planning map and action names remain recognizable. This packet changes client presentation.

## 4. New paths

This packet. The regression cases extend the existing launch-screen and tactical-map EditMode fixtures.

## 5. Exclusive write scope

- Home lane owns `LaunchScreenView.cs`, `MatchHud.Accounts.cs`, the `HubAtriumView` files and focused additional launch/atrium tests. The staging walkthrough extended this lane to `MatchHud.Tutorial.cs`, the Training replay row in `ModernSettingsView.cs` and its focused label test.
- Planning lane owns `TacticalMapController.cs`, its presentation partials and focused additional planning tests.
- Coordinator owns the packet, the initial regression cases, integration and browser verification. Ask the coordinator before expanding either lane.

## 6. Contracts and references

R-AUTH, C-OBS, C-INPUT and D9 apply. Reuse the session's available destinations, the viewer's delivered planning options, current confirmation state and existing controls. Preserve privacy and keyboard access.

## 7. Behaviour to preserve

Returning profiles, faction selection, pending-launch Cancel, enabled Training deployments, existing shortcuts, server confirmation, reconnect feedback and route editing.

## 8. Acceptance checks

The entry instruction is legible in the existing card at both small and ordinary desktop sizes. A deployment without Training gives the arena a clear primary action. A deployment offering Training still exposes that destination. The planning panel guides the player through the available choices and the Ready action, including the case where the server offers zero boons. A confirmed-ready player can see what happens next.

## Oracle files

The initial new tests in `LaunchScreenEditModeTests.cs` and `TacticalMapEditModeTests.cs` reproduce the two first-visit faults. The coordinator recorded the deployed home at 960×600 and 1280×800 and the earlier public planning capture shows two spawn options, zero boons and a direction to choose a boon.

## 9. Validation commands

Run the two focused Unity tests before implementation. After integration, run the database-enabled full repository gate against `dbaa701be`, build WebGL with the staging content hash, and drive the actual staged client through its home and first match. Verify delivery over the existing Cloudflare page and direct Calgary game path.

## 10. Risk and suggested model tier

Use two Sol implementers. Give the planning lane additional reasoning for state-dependent guidance. Check the small viewport, pending launches and returning users before selecting the candidate for production.

## 11. Known traps

An empty callsign also disables Training. Use the deployment's availability independently from the player's current ability to join. The server can offer a valid plan with no boons. Ready can use defaults. Existing pending/error feedback takes precedence over general instructions.

A fresh account on the first public preview showed a Training invitation even though the hosted server offered only the Arena. The follow-up verifies both the first-visit prompt and the Settings replay action against the deployment’s available destinations, and preserves tutorial progress for a future deployment that offers Training.

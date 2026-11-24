# P-OPEN-ALPHA-HANDOFF-1

## ID and outcome

P-OPEN-ALPHA-HANDOFF-1 records the local rehearsal candidate and open-alpha release plan for the next chat. It supports the Phase 4 hosting and Phase 5 cutover work in the order of operations.

## Dependencies

Integration 88, published Storm Ink PRs 83–84, UI wave four through `093983b51`, and the two Integration 89 preview merges through `e001cae2d`.

## Relevant legacy paths

`deployment/cloudflare/wrangler.jsonc`, `deployment/cloudflare/src/profileSnapshots.ts`, `.github/workflows/deploy.yml` and `packages/client/` identify the public game and its release path.

## New paths

`docs/coordination/OPEN_ALPHA_HANDOFF_2026-11-24.md`, `docs/coordination/CLAUDE_FABLE_STOP_POINTS_2026-11-24.md`, `docs/coordination/OPEN_ALPHA_SCOPE_MENU_2026-11-24.md` and `docs/coordination/OPEN_ALPHA_ART_ESTIMATES_2026-11-24.tsv`.

## Exclusive write scope

The handoffs, effort menu and art estimates, this packet and its backlog entry. The local rehearsal merges reuse existing packet revisions.

## Contracts and references

R-AUTH, C-NET, D7, ADR 0006, ADR 0020, `hosting/AGENTS.md` and `docs/ORDER_OF_OPERATIONS.md`.

## Behaviour to preserve

Preserve the published legacy release, original worktrees, uncommitted files and the fully integrated fallback revision.

## Acceptance checks

The starting integration has no open-alpha handoff. The new handoff identifies a retrievable combined source, traces the live game's public build identity, verifies the five ground paintings, documents the two Fable stop points, and gives a clean final merge, staging, production, data and rollback plan. The first action asks the owner to choose the polish scope after seeing effort and importance estimates.

## Validation commands

Use `git merge-base --is-ancestor` for the component revisions, `git rev-parse <ref>:<path>` for asset identity, `scripts/gate.sh --base codex/open-alpha-freeze-2026-11-24` for the combined source and `git bundle verify` for the portable source. Record the actual gate outcome in the handoff.

## Risk and suggested model tier

Release integration requires coordinated review of UI, server identity, hosting and persistence. A coordinator should own candidate scope and release evidence.

## Known traps

The two play hostnames serve different clients. Uncommitted fixes and ignored lane evidence require their original working trees or the recovery kit. Provider-specific practice safeguards and the public release procedure have different scopes.

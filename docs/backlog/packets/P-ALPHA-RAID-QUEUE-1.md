# P-ALPHA-RAID-QUEUE-1

## ID and outcome

Phase 4, item 18. Configure the selected open-alpha quest release
`e85db4eb11ca7087a9e45f6b847d002d4a06a54b` for a 45-second human queue and
RaidBot fill to eight. The server seats bots at the deadline and advances to
planning two seconds later. Eight humans advance immediately. Restore the exact
four-seat baseline on the same release.

## Dependencies

The quest release's manifest and selected four-seat signature-landmark journal
on both origins, and the deployed native Source's matchmaking CLI.

## Relevant legacy paths

`packages/server/src/sim/step.ts` drives the legacy lobby lifecycle and
`packages/server/src/bots/BotManager.ts` implements legacy scheduled bot fill.
The owner-selected alpha opts into Rust's existing matchmaking timing (DEV-013
documents the mode's original default), with the explicit bot wait set to 45
seconds for this deployment.

## New paths

- `docs/backlog/packets/P-ALPHA-RAID-QUEUE-1.md`

## Exclusive write scope

- `hosting/open-alpha/operations/release/balance/balance_release.py`
- `hosting/open-alpha/operations/release/balance/test_balance_release.py`
- `hosting/open-alpha/operations/release/balance/README.md`
- `docs/backlog/packets/P-ALPHA-RAID-QUEUE-1.md`

## Contracts and references

R-AWS-3 and C-HOST cover protected sessions and graceful drain. The operator
passes the existing Rust options for server authority and leaves D6 capture
configuration and D9 movement prediction intact. D1 uses local operator
proof and a separate real-client queue rehearsal. D2 is out of scope.

## Behaviour to preserve

Exercise the selected native and Web artifacts, signature maps, two eight-seat
rooms, PostgreSQL, and both capture writers. Preserve the original release
journal and its archive rollback destination.

## Acceptance checks

- Toggle and restore one origin at a time. Check both use the identical native
  executable and that the loader, notice and release journal are byte-identical.
- Require a healthy staged RaidBot queue on the same artifact before enabling
  production. Refuse to drain rooms, seats, and presence in the waiting hub.
- Recover to the four-seat queue after a failed RaidBot start. Recognize the
  stopped RaidBot unit for guarded recovery. Keep the original release journal
  usable if an operator tries an archive rollback before restoring the queue.

## Validation commands

- `python3 -B hosting/open-alpha/operations/release/balance/test_balance_release.py`
- `scripts/gate.sh --quick --allow-protected`

## Risk and suggested model tier

The service is briefly unavailable during the guarded restart. Verify real
queue timing and the exact release on staging before production.

## Known traps

`--bot-wait-seconds` is the human queue window. The server supplies its own
two-second transition after bot seating. `--fill-seconds 2` is retained from
the baseline and takes effect again in legacy timing after restore.

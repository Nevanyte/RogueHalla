# P-OPS-SATURDAY-1

## ID and outcome

Preserve the operational recipes and evidence from the November 26–27 production alpha. Serves Phase 4 item 18 (single-host operation and D6 review), item 20 (budget-scaled capacity evidence), and the rollback portion of Phase 5 item 21. Covers a production alpha rehearsal within those items.

## Dependencies

The implemented split ingress and capture stack in [hosting/open-alpha/latency](../../../hosting/open-alpha/latency), and the pinned releases in the [evidence](../../evidence/saturday-production-2026-11-26/README.md).

## Relevant legacy paths

None. The work covers Rust/Unity host operations.

## New paths

- `hosting/open-alpha/operations/` for the backup, monitoring, CloudFormation, and pinned release recipes and tests.
- `docs/evidence/saturday-production-2026-11-26/README.md` for sanitized evidence.

## Exclusive write scope

The operations directory, the evidence README and this packet.

## Contracts and references

[C-OBS and C-RNG](../../CONTRACTS.md), [R-AUTH](../../REQUIREMENTS.md) and [Order of Operations](../../ORDER_OF_OPERATIONS.md). D1 and D2 have no new mechanic to assess. D5 keeps the existing design. D6 requires a disconnect-log review after playtest. D9 keeps gameplay decisions on the server.

## Behaviour to preserve

Keep the control interface on loopback, capture and account backups private, and restore operations isolated. Preserve the database and result-spool recovery boundaries, two-second fill and two-room selection, and the rollback order with lobby and drain checks.

## Acceptance checks

- The recurring backup recipe validates ciphertext uploads and advances its success receipt only after all three objects succeed. An isolated synthetic restore checks the real dump and both ciphertexts.
- Negative checks catch stale or missing backup signals, stuck capture delivery, missing writers, unsafe native state, and damaged or unrecognized homepage assets before the guarded switch.
- CloudFormation schema and Guard checks cover storage protections, scoped publishing and alarm notification wiring. The evidence records live browser and latency results, recovery proof and delivery confirmation with private data excluded.

## Validation commands

Run the local checks listed in the [operations runbook](../../../hosting/open-alpha/operations/README.md#local-validation).

## Risk and suggested model tier

Deployment mistakes can interrupt admissions or compromise recovery. Have an experienced implementation and operations reviewer inspect the retained bucket and policy, role permissions, and pinned rollback inputs. Run a reviewed change set for any future provider update.

## Known traps

Use the verified production backup recipient and an independent reader. Keep private parameters and deployed identifiers in operator-controlled records. Check the shared `/etc/rh-alpha` traversal permissions after installation, and verify email delivery beyond the pending subscription state. Select five-second fill before native rollback. A healthy backup alarm tracks a recent successful receipt and the hourly service result.

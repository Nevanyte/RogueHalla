# Hand-off: P-BUILD-PROFILE-1, and the room-tick report (2026-11-22, backend-efficiency lane)

```
PACKET:          P-BUILD-PROFILE-1 — serves REQUIREMENTS R-OPT-1/R-OPT-2, ORDER_OF_OPERATIONS phase 4 item 18; owner
                 direction 2026-11-22 (backend audit findings 3 and 7), dispatched by the coordinator session
BRANCH:          agent/opus/P-BUILD-PROFILE-1 (brief on packet/P-BUILD-PROFILE-1 @ 9fbc9d91c; base feature/unity-rework
                 @ 83edb687f)
GATE:            PASS (quick, --oracle packet/P-BUILD-PROFILE-1) [no crate changed: fmt and area checks only;
                 full gate not run by the lane]
CHANGED:         docs/evidence/backend-efficiency-2026-11-22/README.md (the room-tick report, audit finding 7) and this
                 hand-off. No source, manifest or lock file.
D-RULES:         D1 n/a (codegen only; the determinism sweep and headless state hashes were identical under LTO);
                 D2 n/a; D5/D6/D9 untouched
DEVIATIONS:      none
SHARED RECORDS:  proposed: docs/backlog/README.md, R-OPT rows "Done (measured, not adopted)" for P-BUILD-PROFILE-1 and
                 "Report" for the room-tick finding; Cargo.lock: no change (mimalloc not adopted)
LEGACY DOUBTS:   none
NOT ESTABLISHED: a quiet host (load average 35-95 throughout: another session's full gate, implementers, a game
                 client); the instance class we deploy to (2 vCPU); thin LTO (not tried)
RISKS:           none (nothing changes)
```

## The verdicts

- **Build profile (finding 3): measured, not adopted.** Server CPU at 16 matches x 8 humans did not move (0.219, 0.221,
  0.218 cores). Fat LTO with one codegen unit retires 1.8-2.7% fewer instructions in the benchmarks and makes release
  builds cost about 50% more CPU (each binary also gets a single-threaded whole-program link), which the gate and CI
  would pay on every run. mimalloc adds nothing and raises peak RSS from 46
  to 63 MB. The table and the commands are in `docs/backlog/packets/P-BUILD-PROFILE-1.md`. One option is offered to the
  owner and not built: a `[profile.dist]` for the shipped binary only (24% smaller image, `hosting/Dockerfile` change).
- **Room ticks on the shared workers (finding 7): no dedicated runtime proposed.** At 22 rooms the server used 0.28
  cores of 18 workers. step-to-connection p99 rose 0.8 -> 2.6 ms with room count, together with the wake lateness of an
  oversubscribed host. Clients' snapshot interval p99 stayed 27-29 ms. The report is
  `docs/evidence/backend-efficiency-2026-11-22/README.md`, with what to re-measure on the deployed instance.

## Review

`reviewer` (Sonnet): **LAND WITH NOTES**, two notes, both applied by the lane before hand-off (no fix packet, the
brief was not merged), in `brief-amend` 9fbc9d91c:
1. The build-time claim compared wall times taken at different host loads (about 20 and about 90). The brief now
   states CPU time (+52%) and marks the wall times as not comparable.
2. The build-time row's scope was mislabelled (zsh's `time` line cuts the command) and the mimalloc build was
   incremental. Both are now labelled, and a "Not established" section was added: the aarch64 Linux deploy target, the
   Dockerfile's features and jobs, gains below about 5%, mimalloc memory beyond 30 s, thin LTO.

The reviewer checked every table cell against the raw data and every citation, and agreed the verdict follows from
the flat server CPU.

Minor, not notes (no packet):
- The raw `rh-load` reports are scratch, not committed; the tables carry the figures.
- The room-tick table's room counts are steady means (now labelled).

## What changes for a player, on the wire, in a pinned value

Nothing.

# Saturday production alpha evidence

## Release selected on November 26, 2026

| Surface | Observed identity |
|---|---|
| Unity Web | `9e41ed5e77c812ae170398fc86a1c331a68af9d9` |
| Calgary native server | `a553eb1231df4171bba587488f43018429aaecca` |
| Public handshake | Protocol 24, content hash `8a4bf0ce37b406f2` |
| Lobby | Two rooms, eight seats per room, two-second bot fill |
| Selected homepage SHA-256 | `83bbc43c1acb398fa6dbf54f15fe239e03611ae2fc54e52b438efad8371f5f5f` |

The exact Unity candidate's full local gate passed with 2,850 Unity EditMode and 2,742 C# library tests, Rust, PostgreSQL, and the legacy parity report. CI-only cross-architecture and Linux witness jobs were outside that run. At cutover the public homepage matched the pinned loader, the WASM response used Brotli, PostgreSQL readiness passed, and both production capture writers started. The original homepage was saved for rollback.

### Production browser smoke

Two ordinary Chrome windows on the same Edmonton network loaded Unity from the public homepage and opened direct WSS connections to Calgary. Both received HTTP 101 and a server Welcome advertising 40 Hz. Both selected a spawn, confirmed Ready and moved 17 authoritative units. The test used the normal network path.

| Seat | Returned / sent | Median game RTT | p95 game RTT | First confirmed movement |
|---|---:|---:|---:|---:|
| A | 151 / 151 | 12.657 ms | 17.282 ms | 92.115 ms |
| B | 151 / 151 | 12.196 ms | 18.463 ms | 113.674 ms |
| Pooled | 302 / 302 | 12.491 ms | 17.693 ms | — |

Both seats chose Leave. The lobby cleared and S3 confirmed two separate age-encrypted input-metric objects with S3 checksums and SSE-S3. The production smoke exercised the live UI and movement. Its evidence ends at Leave.

### Staging rehearsals

- At two-second fill, two groups of eight authenticated Chrome protocol clients joined on their first attempt and all received input acknowledgements. Over 45 seconds, 720 of 720 game pings returned, with 12.4 ms median RTT and 17.6 ms p95. The rehearsal used protocol clients on one network.
- A separate two-human match on the native candidate ran to Results in 655 seconds. Private S3 returned the sealed recording and verified the ciphertext SHA-256. The matching offline verifier reproduced all 26,698 steps and the final state hash.
- Live readiness and gameplay smoke were successful at the sampled times. Evidence for weak hardware, a broad network and browser mix, and a full production match played through Results remains open. A D6 review of the post-smoke disconnect log is not recorded in these receipts.

## Account recovery and alert delivery

The first hourly run committed a database dump, result-spool archive, and receipt to private S3. The independent reader matched both ciphertext checksums. An isolated PostgreSQL 16 instance restored **7 profiles, 6 matches, 9 player results, and 2 ledger entries**, matching the captured source baseline. The result-spool archive decrypted and listed successfully. Four committed hourly receipts were present by 23:09 UTC on November 26. This establishes independent readback and database restoration for the sampled backup. Full service recovery, spool replay against the restored database and loss bounds across a host outage remain separate checks.

At **00:38:03 UTC November 27**, an approved test forced the database-backup alarm into ALARM. Metric evaluation returned it to OK at **00:38:17 UTC**. CloudWatch recorded successful SNS actions for both transitions. SNS measured **2 published, 2 delivered, 0 failed** in the drill minute. The owner confirmed receipt of the recovery email. All four alarms subsequently reported OK with actions enabled.

[Operator runbook](../../../hosting/open-alpha/operations/README.md)


## November 27 release checks

At 08:49 UTC a fresh Chrome session opened the public homepage, fetched all four Unity startup assets for `9e41ed5e77`, and initialized Unity without a page exception. The homepage matched the November 26 seal after accounting for Cloudflare’s browser analytics script. Public storage reported healthy, and the direct game endpoint reported protocol 24, content `8a4bf0ce37b406f2` and a two-room capacity. The check ended at the entry screen.

The balance candidate pairs the Linux server, replay verifier and Unity client built from `3f0740ad84519c84af47604e0c0b758bc20efa88` with content `cb811d19ebed6dfe` on protocol 24. A local Chrome match loaded the versioned assets, saw five distinct planning quests and ten at raid start, and recorded zero client or decoder errors. In a separate isolated two-human PostgreSQL 16 match, both movement inputs were acknowledged, one player left, and the remaining player received Results. The two encrypted input records and one encrypted replay were produced using a disposable key. The packaged verifier matched all 94 recorded steps. [Prior balance build and file manifest](https://github.com/Nevanyte/RogueHalla/blob/5cb3ab5b9b40782ba9df580c2328fb147288edfc/hosting/open-alpha/operations/release/balance/release-manifest.json)

The GitHub performance gate on `aca018ac6` compared nine workloads. All nine matched the baseline allocation counts. The highest instruction ratio was 1.0217 against a limit of 1.03. The historical replay fixture reproduced all 3,000 steps and the same final hash. Its expected content-registry label change is documented on the balance PR. [Performance check](https://github.com/Nevanyte/RogueHalla/actions/runs/45978616307/job/140436674560)

The Linux release checkpoint suite restored 202 snapshots from the original eight seeds and 26 from supplemental seed 11. The packaged Source build accepts the first two mob positions at the same instant when displacement resets the history before movement. Later times still have to progress by a full simulation tick. The integration test commit `aca018ac6f5a7a3b8d3a45b66daae180d3ad377d` differs from the packaged game source only in two Rust test modules compiled under `cfg(test)`. That test drives a Frost Wall cast through the normal server tick. The eight focused checkpoint tests and all 14 schedule-conformance tests passed on Linux amd64. [Checkpoint packet](../../backlog/packets/P-MOB-REWIND-RESTORE-1.md)


The final local gate passed with PostgreSQL and 2,860 of 2,860 Unity tests. All 14 GitHub checks passed on the same test revision. Six deterministic scenarios matched on Linux x86-64 and ARM64. Each architecture verified the other’s replay. The packaged verifier also verified both exported tapes with final hash `b0b16be735f469f6`. The game and balance changes merged into `feature/unity-rework` at `97d3a115d` on November 27. The public balance cutover requires a separate Calgary rehearsal. [Game merge](https://github.com/Nevanyte/RogueHalla/pull/94) · [Cross-platform replay checks](https://github.com/Nevanyte/RogueHalla/actions/runs/45978616297)

## Quest release with signature landmarks on staging

The staged Unity Web, native Source and replay verifier share build `e85db4eb11ca7087a9e45f6b847d002d4a06a54b`, protocol 24 and content hash `cb811d19ebed6dfe` at 40 Hz. The quest merge `173ad1b05` has the identical Git tree. The release manifest and guarded operator were retained in PR #97. PR #98 added independent signature-landmark selection for the same artifacts. Staging selected it with the standard bot policy, four total participants as the fill target, a two-second fill window and two available rooms. Production still served Web `9e41ed5e77` and native `a553eb123` when these receipts were recorded. [Quest merge](https://github.com/Nevanyte/RogueHalla/pull/96) · [Release manifest and operator](https://github.com/Nevanyte/RogueHalla/pull/97) · [Landmark selection](https://github.com/Nevanyte/RogueHalla/pull/98)

- Both normal staging players received the same five authoritative Welcome footprints for the Fire volcano, Sentenced church, Plague bog tree, Frost cave and Holy hall.
- Two cold bursts of eight authenticated Chrome protocol clients reached active play in two rooms. All 16 received authoritative input acknowledgements. All 720 game pings returned over 45 seconds, with 12.2 ms median RTT, 15.1 ms p95 and 28.8 ms maximum. The rooms cleared afterward.
- A separate staging match with two humans and two bots finished, delivered both Results and cleared the arena. Private storage received one age-encrypted replay with AES256 storage encryption and two encrypted player-metrics objects. The retrieved replay checksum matched. The paired verifier matched the build and content, verified all 5,855 recorded steps and reproduced final state hash `bf8d225d0f257049`.
- A separate local check used the exact sealed Unity client with a normally generated signature map. The High preset at 1920×1080 rendered all five landmarks and the Plague peat, grass and water surfaces. The visual sweep recorded 12 captures with zero reported errors.

### Earlier quest staging selections and remaining performance checks

Before enabling signature landmarks, a cold Chrome retry reached authoritative movement in 37.945 seconds on a modeled 20 Mbit/s link with 40 ms latency. In a separate two-seat Unity run, each client received all 151 game pongs. Median RTT was 10.055 and 10.486 ms, with p95 of 13.279 and 15.746 ms. Both seats moved authoritatively and the arena cleared after browser shutdown. One seat's ordinary Leave frame was absent from the receipt.

The earlier Apple M5 Pro foreground sample used Ultra at 960×600 for 306.066 seconds in active staged gameplay. It recorded 36,713 Unity canvas draws, 119.948 draws per second and a draw-interval p95 of 9.30 ms and p99 of 9.70 ms. Peak sampled renderer RSS was 1.470 GB. GPU and browser process memory were measured separately, and complete tab memory was unresolved. The sample establishes canvas draw cadence on that hardware. Native 2560×1440 at 144 Hz in a full Ultra fight and the weak-hardware Phaser comparison remain unverified.

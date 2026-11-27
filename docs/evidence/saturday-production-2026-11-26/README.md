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

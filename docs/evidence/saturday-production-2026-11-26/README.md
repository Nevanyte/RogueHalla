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

- Two ordinary Chrome Unity clients received the same authoritative Welcome map with all five landmarks: Fire volcano, Sentenced church, Plague bog tree, Frost cave and Holy hall. Both confirmed Ready, moved 17 authoritative units and chose Leave. The lobby cleared. Each received 151 of 151 game pongs. Seat medians were 11.047 and 10.902 ms, and p95 values were 13.951 and 12.867 ms.
- Two cold bursts of eight authenticated Chrome protocol clients reached active play in two rooms. All 16 received authoritative input acknowledgements. All 720 game pings returned over 45 seconds, with 12.2 ms median RTT, 15.1 ms p95 and 28.8 ms maximum. The rooms cleared afterward.
- A separate staging match with two humans and two bots finished, delivered both Results and cleared the arena. Private storage received one age-encrypted replay with AES256 storage encryption and two encrypted player-metrics objects. The retrieved replay checksum matched. The paired verifier matched the build and content, verified all 5,855 recorded steps and reproduced final state hash `bf8d225d0f257049`.
- A separate local check used the exact sealed Unity client with a normally generated signature map. The High preset at 1920×1080 rendered all five landmarks and the Plague peat, grass and water surfaces. The visual sweep recorded 12 captures with zero reported errors. [Six-panel landmark gallery rendered locally with the exact shipped Unity package](signature-landmarks.jpg)

### Cold startup and foreground rendering

With landmarks enabled, three cold Chrome trials on the Apple M5 Pro took 40.644, 40.855 and 43.062 seconds to reach server-confirmed movement at a modeled 20 Mbit/s downstream and 40 ms latency. The range was 40.644–43.062 seconds and the median was 40.855 seconds. **Zero of three met the approved 40-second target.**

The external 144 Hz display was unavailable during the rehearsal. A separate 60-second Chrome comparison on the M5 Pro’s built-in Retina display used a verified 2560×1440 Ultra backbuffer. Browser frame cadence averaged 120.004 frames per second, with p95 of 10.20 ms, p99 of 10.40 ms and a 1% low of 96.128 FPS. The fight delivered 203 authoritative hit effects. Physical 144 Hz presentation and weak-laptop performance remain unverified.

### Earlier quest staging selections

Before enabling signature landmarks, a cold Chrome retry reached authoritative movement in 37.945 seconds on a modeled 20 Mbit/s link with 40 ms latency. In a separate two-seat Unity run, each client received all 151 game pongs. Median RTT was 10.055 and 10.486 ms, with p95 of 13.279 and 15.746 ms. Both seats moved authoritatively and the arena cleared after browser shutdown. One seat's ordinary Leave frame was absent from the receipt.

The earlier Apple M5 Pro foreground sample used Ultra at 960×600 for 306.066 seconds in active staged gameplay. It recorded 36,713 Unity canvas draws, 119.948 draws per second and a draw-interval p95 of 9.30 ms and p99 of 9.70 ms. Peak sampled renderer RSS was 1.470 GB. GPU and browser process memory were measured separately, and complete tab memory was unresolved. The sample establishes canvas draw cadence on that hardware. Native 2560×1440 at 144 Hz in a full Ultra fight and the weak-hardware Phaser comparison remain unverified.

## November 27 quest release selected on production

The paired release operator selected Unity Web and native Source build `e85db4eb11ca7087a9e45f6b847d002d4a06a54b` on production. The previous Source build was `a553eb1231df4171bba587488f43018429aaecca`. The selected pair uses protocol 24 and content hash `cb811d19ebed6dfe`. The selected production HTML SHA-256 is `a8a90cb1ac50f3cd1268a9c6beeb05ed7a004a77cd62c49332ef9bc3f8bb0bb6`. At the initial cutover, Source had two available rooms, a four-participant fill target, a two-second fill window, signature-landmark maps and both capture writers running. The following production runs used this initial queue. [Release manifest and operator](../../../hosting/open-alpha/operations/release/balance/README.md)

The public homepage, gameplay data notice and versioned Brotli loader matched the release. The direct gameplay endpoint reported the paired protocol and content hash, and PostgreSQL reported ready. The production target was healthy. Capture delivery, database backup, game-target and host alarms were all OK. Both credential and upload timers were active, and the last upload service run succeeded. Both new-build capture writers started with zero reported failures or drops.

The owner approved a 45-second cold-start ceiling for this alpha. All three landmark-mode staging trials above fell within the approved alpha ceiling. Physical 144 Hz sign-off was deferred. The weak-hardware comparison remains open.

### First production Unity smoke

Two ordinary Chrome Unity clients received the five signature landmarks, confirmed Ready and moved 17 authoritative units. The server acknowledgement times were 93.265 and 113.664 ms. Both seats sent ordinary Leave and the lobby cleared.

| Seat | Game pongs | Median RTT | Mean RTT | p95 RTT |
|---|---:|---:|---:|---:|
| A | 151 / 151 | 10.930 ms | 17.089 ms | 59.851 ms |
| B | 151 / 151 | 10.929 ms | 17.113 ms | 60.769 ms |

The run had zero missed replies. Both seats exceeded the mean RTT limit of less than 15 ms and the p95 limit of 45 ms.

Both encrypted seat metrics from this smoke reached private storage. Both objects were nonempty and had S3 checksums and AES256 storage encryption. At review, all four S3 public-access blocks were enabled, the bucket policy was nonpublic and the live prefix had a 14-day expiration. The five-minute CloudWatch point for 19:40 UTC showed 5.8% average and 7.6% maximum host CPU. That aggregate does not identify the source of the RTT tail.

### Completed production match

In a separate four-seat match with two humans and two fill bots, both ordinary Unity clients moved and received authoritative acknowledgements. Both received MatchEnded at tick 2,795 and their own Results. The room cleared. The winning human's win bonus was withheld under the existing rule that requires at least three human starters. The test helper's expectation of an awarded bonus did not fit this two-human setup.

Private storage received two more encrypted player metrics and one 497,042-byte encrypted replay with AES256 storage encryption. The downloaded ciphertext matched its recorded checksum. The paired verifier reproduced all 2,795 steps and final state hash `c0cd57f2fbcd7d0c`, with matching build and content. The validated trajectory contained 5,584 human position samples and 1,022 human game commands.

### Foreground latency diagnostic

A separate single-seat Unity run confirmed 17 units of authoritative movement and received all 115 game pongs before the round ended. Median, mean and p95 RTT were 10.699, 10.909 and 12.131 ms. The shorter single-seat run provides a separate observation from the full two-seat sample.

### Production paired latency recheck

A later two-seat run met both live RTT limits. Both ordinary Unity clients sent Leave, and the lobby cleared.

| Seat | Game pongs | Median RTT | Mean RTT | p95 RTT |
|---|---:|---:|---:|---:|
| A | 151 / 151 | 10.906 ms | 10.964 ms | 12.124 ms |
| B | 151 / 151 | 10.583 ms | 10.903 ms | 13.371 ms |

The first run's RTT tail did not recur in this recheck. The cause of that tail remains unresolved.

## November 27 RaidBot queue selected on production

Calgary production switched the selected `e85db4eb11ca7087a9e45f6b847d002d4a06a54b` release in place after checking that the lobby was empty. The running Source uses `--bot-brain raid --fill-bots 8 --lobby-timing matchmaking --bot-wait-seconds 45`. The Unity Web release, protocol 24, content hash `cb811d19ebed6dfe`, two-room capacity, signature-landmark maps, and capture configuration carried forward.

The server gives humans a 45-second queue window from the first admission. When the window expires, RaidBot fills the missing seats and Planning begins two seconds later. Eight human arrivals advance to Planning immediately. Planning has its own 45-second deadline and can advance to Active earlier after the Ready conditions are met.

### Hosted staging rehearsal

Seventeen authenticated seats joined directly over Chrome WebSockets across three scenarios on the same build. Phase times below use the logical server clock from Welcome and the authoritative phase snapshots.

| Scenario | Queue result | First Welcome to Planning | Eighth Welcome to Planning | Planning to Active | Active pongs | Active RTT mean / p95 |
|---|---|---:|---:|---:|---:|---:|
| Solo | Seven bots first appeared on the roster at 45.025 s | 47.000 s | — | 8.350 s | 60 / 60 | 10.74 / 13.20 ms |
| Eighth human joined later | Eight humans, zero bots | 28.125 s | 25 ms | 50 ms | 192 / 192 | 11.20 / 13.50 ms |
| Eight humans joined in a burst | Eight humans, zero bots | 50 ms | 25 ms | 50 ms | 192 / 192 | 11.15 / 13.10 ms |

The solo run reached Active 55.350 seconds after admission. The server journal confirmed seven RaidBot selections and zero policy fallbacks, and the client saw a nearby bot move at least 16 world units. The reported p99 simulation step took 7.167 ms against the 25 ms tick budget, with zero dropped steps. Private storage returned the solo participant's encrypted metric with a checksum and AES256 server-side encryption. All scenarios cleared the lobby with ordinary Leave during Active. The operator then rehearsed restoring the original four-seat, two-second queue and reselecting RaidBot on the same build before enabling production.

### Completed staging match

A separate match with two human QA seats and six RaidBots reached MatchEnded after 1,088 seconds of Active play. A RaidBot won and both humans tied for second. The server journal confirmed all six RaidBot policy selections with zero fallbacks through the finish. Both clients received Results. Each confirmed 162 gameplay input acknowledgements and recorded 1,072 samples of its own authoritative position. All 2,282 combined game pings returned, and the lobby cleared after ordinary cleanup.

Private storage held both encrypted player metrics and the 1,853,931-byte encrypted replay. The replay's ciphertext checksum matched and its storage encryption was SSE-S3. The capture prefixes had an active 14-day expiration policy. The shipped Linux verifier reproduced all 45,679 ticks and final hash `85940fe4867e8517`.

A separate export from the same Rust source verified every tick again and extracted 91,356 human position rows and 326 human game commands. All 2,144 browser position samples matched the corresponding actor and tick, with a maximum absolute coordinate difference of 0.0051 world units. The browser recorded coordinates to two decimal places.

### Live Unity verification

One ordinary Chrome Unity client still saw one human and zero bots at 44.597 seconds. It first saw all seven bots at 45.111 seconds and entered Planning at 47.161 seconds. The server acknowledged movement in Active. All 151 game pings returned, with an 11.220 ms median, a 14.873 ms mean and a 36.215 ms p95. The client exited cleanly and the lobby cleared.

The production journal confirmed seven RaidBot selections and zero policy fallbacks. The p99 simulation step took 16.383 ms against the 25 ms tick budget, with zero dropped steps. The player's encrypted metrics reached private storage, and all four operational alarms reported OK at readback.

### Operator verification

All 18 offline release-operator tests, the local quick gate, and GitHub Hosting validate passed for the queue change. A fresh full gate reached workspace test linking after passing both Clippy modes and the C# suite. The Mac ran out of disk space during linking. The original `e85db4eb1` full gate passed with both PostgreSQL suites and 2,863 Unity EditMode tests. The queue change leaves the tested Rust, Unity, wire, and parity inputs byte-identical. [Quest release gate](https://github.com/Nevanyte/RogueHalla/pull/96) · [Queue operator](https://github.com/Nevanyte/RogueHalla/pull/102) · [Hosting validate](https://github.com/Nevanyte/RogueHalla/actions/runs/46061642319/job/140717060398)

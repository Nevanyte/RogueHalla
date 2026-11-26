# RogueHalla open-alpha handoff

**Prepared November 24, 2026. The release target is https://play.roguehalla.com.** Start with the combined Unity/Rust source at **`96812695434574f8d491f5b17836d3b90e322646`**. It includes the five-zone Storm Ink ground, the latest committed UI work from Claude Fable and its committed backend preview. The game tree is `3f07a2189585590bcb2f7e02d95a956c41ba84e4`. The candidate branch also carries this handoff in a subsequent documentation commit. Production currently serves an older game.

## Start the new chat here

> Your goal is to polish and finish the alpha scope I choose, then make a clean final merge and prepare the exact merged revision for play.roguehalla.com. Start read-only. Verify the source and read the polish menu. In your **first reply**, give me the remaining items, your estimated work for each, their importance to players or launch, and your recommended scope with a total. Ask **“Which items would you like finished for this alpha, and which should I defer?”** Wait for my selection before implementing polish. New code, art adjustments and tests are welcome for the selected fixes. If you discover another unfinished item, give it the same estimate and importance rating, and tell me how including it changes the total. Carry the approved scope through a clean merge, verification and staged release rehearsal.

**Choosing scope.** [Alpha polish and effort menu](OPEN_ALPHA_SCOPE_MENU_2026-11-24.md) lists individual decisions, dependencies and initial person-hour estimates. Start with the player-facing items, call out work required by the chosen hosting/data policy, and offer a custom selection. Keep optional polish and release operations visible as separate totals. The user's choice controls the release contents.

On this Mac, open **`/Users/nevinmckinnon/Documents/Projects/RogueHalla-worktrees/open-alpha-candidate`**, on `codex/open-alpha-candidate-2026-11-24`. The primary checkout `/Users/nevinmckinnon/Documents/Projects/RogueHalla` contains the original working files and Claude’s ignored handoffs. A clean branch at GitHub’s default revision omits the candidate. In a different clone, fetch the included Git bundle after fetching the published integration branch. The bundle includes committed source and both local alpha branches. Uncommitted files are indexed separately. The companion ZIP also contains the revised UI text-fit spec, Web review brief and partial captures, the line-of-sight worktree patch, the branch/file inventory, and checksums. The source bundle is incremental against published integration at `59ac6d2b1`; a clone with that history can restore it.

```sh
# For a different clone with the supplied bundle. From the existing candidate worktree, skip this block.
git fetch origin feature/unity-rework
git bundle verify /path/to/roguehalla-open-alpha.bundle
git fetch /path/to/roguehalla-open-alpha.bundle \
  refs/heads/codex/open-alpha-candidate-2026-11-24:refs/heads/codex/open-alpha-candidate-2026-11-24 \
  refs/heads/codex/open-alpha-freeze-2026-11-24:refs/heads/codex/open-alpha-freeze-2026-11-24
git worktree add ../RogueHalla-alpha codex/open-alpha-candidate-2026-11-24
```

To inspect the parked packets in a separate clone, use `git bundle list-heads /path/to/roguehalla-open-alpha.bundle`. Fetch a selected tip into a new review branch. The saved LOS patch applies to `aeb98bab1` and has already been applied in the original working copy. The inventory describes the other dirty files on the original Mac.

## Which source contains what

| Revision | Verified contents and disposition |
|---|---|
| `75c5b83d3` | Public **play.roguehalla.com** returned this November 4 source revision from `/build-info.json` on November 24. Its Cloudflare Worker serves `packages/client/dist`, the Phaser client. The current manual production workflow builds that client. [Live build identity](https://play.roguehalla.com/build-info.json) · [Worker configuration](https://github.com/Nevanyte/RogueHalla/blob/349ac091f/deployment/cloudflare/wrangler.jsonc) |
| `2f33045d6` | Last Unity beta identified in the migration record, on **play.rougehalla.com** (the letters **ou** precede the **g**). It used protocol 18 and had a November 15 expiry. HTTPS root and `/healthz` timed out during this audit. This is a different hostname and an older release. |
| `349ac091f` / `59ac6d2b1` | GitHub `origin/main` / `origin/feature/unity-rework` at the audit. PRs #83 and #84 landed on the latter. The five approved paintings are absent from `origin/main`. [Ground merge](https://github.com/Nevanyte/RogueHalla/pull/83) · [Draft art merge](https://github.com/Nevanyte/RogueHalla/pull/84) |
| `e4da3c71f` | Local fallback `codex/open-alpha-freeze-2026-11-24`. Reconciles Integration 88 at `f3647ff0f` with published integration at `59ac6d2b1`. Local integration was 636 commits ahead and two behind. The remote side adds the owner-signed DEV-041 record to the local tree. All files outside `docs/` match `3618186b1`, which passed the full gate with PostgreSQL, 2,302 Unity tests and 2,583 C# tests. |
| `0a2a46c4d` → `968126954` | The candidate merges UI wave 4 at `093983b51`, then the Integration 89 preview at `e001cae2d`. Both merged cleanly. This is the latest assembled source selected by this audit. The direct parent `0a2a46c4d` is available if the release needs the updated UI without the two preview bot changes. |

### Ground and art identity

All six entries below matched byte for byte in PR #83, both integration branches and the combined candidate. All paths are relative to `client-unity/UnityProject/Assets/Resources/StormInk/`. Use a generated five-zone Source Arena map during acceptance. A fixed practice scene cannot establish that every zone or boundary is visible.

| Zone | Asset | Git blob prefix |
|---|---|---|
| Plague | `Plague/MarshWash.png` | `6ebd41666b` |
| Holy | `Holy/OasisSand.png` | `66b4264e64` |
| Haunted | `Surfaces/HauntedFlagstones.png` | `efaf89297b` |
| Frost | `Surfaces/FrostSnow.png` | `1723af1926` |
| Fire | `Surfaces/FireAsh.png` | `29f5394d18` |
| Biome boundaries | `Surfaces/RegionTransitions.asset` | `452636ec6b` |

The freeze also contains the church, shared equipment and rough art accepted for merging. Frost Wall’s directional shelves and the Chain Warden’s lash are active presentation. Five alternate environment paintings and the eight-facing Inquisitor are editable studies. The Inquisitor requires its shared-sword grip before runtime use. Preserve the Rust map, wall footprints and authoritative combat. Outstanding art checks, including contrast and final Web traversal, are in [the visual tracker](../art/STORM_INK_VISUAL_CHECKS.csv).

## Where Claude Fable stopped

The two Fable 5.2 transcripts record the weekly limit on November 23 at 18:48:24 UTC (UI) and 18:48:37 UTC (backend). Several worktree logs are newer than the top of `docs/MIGRATION.md`. The original machine paths and source lines are in [the detailed Claude handoff](CLAUDE_FABLE_STOP_POINTS_2026-11-24.md).

| Workstream | In the candidate | Work left at interruption |
|---|---|---|
| Gameplay UI | Waves 1–3 were already integrated. All **35** wave-four packets are now in the candidate. They cover HUD and ability feedback, quest and trader UI, map labels and camp-stat display, notification placement, settings and layout. Last product change `c702dea15` hides the “MAP KEY” heading when the key is hidden and equalizes camp-cell text. With the key visible at 1.5×, two label overlaps were still recorded. The last two packets restore tests. | Finish Web review 3e, especially the 1280×720 check and written verdict. The saved `P-UI-TEXT-FIT-1` spec covers large text and HUD labels after resize. A complete implementation and final wave-four handoff were absent. Decide which visual findings block alpha based on the actual combined build. |
| Backend and optimization | Integration 88 includes the database pool/admission, purchases index and leaderboard work, the headless balance lab and its second 40-seed pilot. Build profiling produced measurements. Integration 89 preview adds the camp-spell range fix and Hook/Bolas rules. The latter are dormant until the bot selects a player fight. | `P-RAID-LOS-1` at `aeb98bab1` also has a one-line uncommitted correction. Its quick gate passed with that correction present. Preserve it before the twelve-seed comparison and review. `P-BAL-LAB-2` at `b890808eb` has its revised tests and stub. The implementation stopped at quota. Both remain outside the candidate. |
| Balance | At the scripted normal camps, Fire cleared in 13.1 s versus 17.6–26.4 s for the other factions. Owner direction keeps the experimental bot in the lab until explicitly selected for live use. | Q30 asks which camp-clear rate and faction basis should price quest time. Both rates remain in the evaluator. The quest/augment rebalance is unfinished and can be scheduled independently of an alpha using the current rules. [Pilot evidence](../evidence/balance-patch-2026-11/pilot-2/README.md) · [Owner queue](OWNER_QUEUE.md) |
| Other release work | Earlier security, prediction and database work already on the integration line is retained. | The staging proxy at `ff474f2c1` and fleet-bridge changes at `40ce8d1b7` are separate hosting proposals. The latter also has two dirty files. Select them only if the chosen topology requires them, with owner review of protected hosting paths. Open PR #69 targets `main` and has a failing Rust check. [PR #69](https://github.com/Nevanyte/RogueHalla/pull/69) |

**Local recovery.** The pre-freeze inventory covers 612 local branches, 48 remote-tracking branches, 102 registered worktree records, four stashes and nine other Git stores. Of the 612 branches, 194 were outside local integration by ancestry. Of 45 present registered worktrees, 19 had changes. The other 57 paths were absent on disk. Stale branch names and different blob IDs can also represent work already replaced. The companion inventory lists paths, current/historical byte matches, independent stores, and recovery bundles. The primary checkout’s Claude launch/settings files and beta configuration were left in place. Claude Desktop’s hourly RogueHalla disk cleanup was disabled and verified.

## Release verification and build

**The full local gate passed on game revision `968126954` with PostgreSQL 16.** All 2,732 standalone C# tests and all 2,828 Unity EditMode tests passed (2,826 headless and two graphics). The Rust workspace, database suites, legacy parity report, eight-bot smoke and release-build bot tests also passed. The first run stopped at the 68-actor C# allocation probe, which measured 2,896.012 bytes per call against an exact expectation of 2,896. The full rerun used identical game source. [First gate log on this Mac](/Users/nevinmckinnon/Documents/Projects/RogueHalla-worktrees/open-alpha-candidate/.agent-runs/gate/20261124T151301-codex-open-alpha-candidate-2026-11-24-full.log) · [Passing gate log on this Mac](/Users/nevinmckinnon/Documents/Projects/RogueHalla-worktrees/open-alpha-candidate/.agent-runs/gate/20261124T151418-codex-open-alpha-candidate-2026-11-24-full.log)

The gate leaves source-witness captures, Valgrind, cross-architecture determinism, browser proof and the Unity Web build to other jobs. Verify CI, the packaged Web client/server pair, hardware performance and the final public deployment on the release revision. On this Mac, later release work may have its own receipts under `.agent-runs/open-alpha/`. Match each receipt to the source and artifact it actually tested.

1. **Pin the source and resolve the release checks.** Work from a clean checkout of the candidate. Keep a record of the Git SHA, dirty status, chosen bot and map settings, database migrations and release settings. Run `bash client-unity/sync-net.sh --check`, then the full project gate with PostgreSQL. Use the freeze ref below for the candidate comparison. The project’s full gate and CI cover distinct checks.
2. **Build the two halves from that SHA.** Set compile-time `RH_BUILD` to the exact release SHA so checkpoints and replays identify the build. For durable profiles compile the server with its `postgres` feature and rehearse migrations against PostgreSQL 16. The candidate declares Rust/C# wire protocol **24**, observation **37**, policy contract **33**, checkpoint **rh-checkpoint/39** and replay **rh-replay/28**. Start the Rust binary, read `/info`, and bake its actual `contentHash` into Unity with `build-web.sh`. The historical hashes in beta notes belong to their respective builds. The project pins Unity **6000.0.85f2** with Web Build Support. Keep the generated player and server binary together with a source manifest, compiler/editor versions, package checksums and selected map pool.
3. **Use a local smoke before packaging.** This fixed-map loopback smoke uses the diagnostic Arena route. The normal account-backed menu and Training require their real HTTP and WebSocket services. Run the server and static server in separate terminals. For the five-zone acceptance run, use the regular Source Arena configuration with `--map-pool` and the intended landmark settings. Obtain server settings from the candidate’s startup code and the Source launch-settings section of the technical-demo runbook.

```sh
# In a clean verification worktree. pgtest is a disposable local database.
eval "$(tools/pgtest/start.sh)"
scripts/gate.sh --base codex/open-alpha-freeze-2026-11-24
tools/pgtest/stop.sh

cargo run -p rh-server -- --first-playable --bind 127.0.0.1:7777 \
  --min-players 1 --fill-bots 4 --lobby-seconds 5 --fill-seconds 3
# In another terminal, from the same worktree:
RH_SERVER_INFO_URL=http://127.0.0.1:7777/info client-unity/UnityProject/Tools/build-web.sh
python3 -m http.server 8080 -d client-unity/UnityProject/Build/Web
# Local diagnostic Arena only, open this address in a browser:
# http://localhost:8080/?server=ws://127.0.0.1:7777/ws&diagnostic=1&auto=0
```

4. **Review one complete play session on the actual packaged pair.** Use a fresh browser and a second independent client. Join through the normal menu, select a faction, fight, loot, reconnect, finish a match and return to the hub. Check results, equipment and profile persistence across a server restart if progression is offered. Capture each zone and at least two biome crossings at gameplay zoom, including the Haunted paving. Check church traversal, shelter/lighting, the Tab map at 1280×720 and large text, toast placement and targeting in a crowded fight. Capture 1440p frame times. For D7 use a named weak machine and a matched Phaser run. Its backstops are p99 ≤16.7 ms at 960×600, tab memory ≤1.5 GB and time to playable ≤10 s on 20 Mbit/s. Record the pass or an owner-reset bar with the measurements attached. For D6, inspect the classified disconnect log after each staging playtest. Verify cold load, stale-tab reload and browser console/network errors.

[Unity build instructions](../../client-unity/README.md) · [Build script](../../client-unity/UnityProject/Tools/build-web.sh) · [Source map settings](../../hosting/technical-demo/README.md) · [Project acceptance criteria](../ORDER_OF_OPERATIONS.md)

## Make one clean final merge

The assembled candidate is a starting point. After the owner selects the scope, record included IDs, estimates and acceptance checks in the release branch. For each chosen item, finish the code or art, review the affected game path, and link a test or gameplay capture. Keep deferred work recoverable on its existing branch. If effort grows beyond the agreed range, present the additional hours and release impact before expanding the item.

Fetch the canonical refs again before integration. Compare source trees and packet history so already-incorporated work is counted once. Resolve conflicts in a dedicated clean worktree; keep owner settings, deployment parameters, caches and diagnostic artifacts out of the shipping tree. Produce one auditable final integration PR against the owner-confirmed release branch (`feature/unity-rework` is the current canonical integration target). Record its reviewed parents and the actual merge SHA. Run the gates and the release smoke against that final merge, and rebuild or attest the artifacts to that SHA. Publishing to `main` or changing a Worker route is a separate release action with its own evidence.

## Launch plan for play.roguehalla.com

**Proposed alpha topology.** Keep Cloudflare as the public entry point. Publish a versioned Unity asset release behind that hostname, with a same-origin reverse proxy to an authenticated Rust Source Arena service and persistent PostgreSQL 16. Route Training separately if it is offered. Give a staging hostname the same route map and headers, with a separate database. This is a proposed production design. Existing deploy scripts implement the legacy site or bounded practice hosts.

**Player data is part of the switch.** For carry-over, rehearse P-MIG-1/2 against an isolated copy using `rh-persist-admin import-legacy --dry-run`, reconcile counts and conflicts, settle any case-colliding callsigns identified in the dry run, then test representative returning accounts. Preserve the original legacy profile file for rollback because Rust hashes tokens. Keep live credentials, exports and connection strings out of Git and out of the chat. [Persistence decision](../adr/0006-persistence.md) · [Admin commands](../../crates/rh-persist/README.md) · [Cutover backlog](../backlog/README.md)

| Stage | Action and required evidence |
|---|---|
| Select the alpha host | Record the intended region, concurrent seats and rooms, duration, budget, account model, data retention and on-call owner. The historical single-host staging and technical-demo recipes have different architectures, expiry limits and artifact contracts. Select a supported target, build the Rust binary for its OS/CPU, and record the binary hash. The staging-MVP helper expects an already-built ARM64 binary and source manifest. The checked-in x86_64 native staging artifact is labeled non-deployable. A disposable demo had no recovery promise for profiles. An open alpha offering accounts needs a backed-up database and a successful restore rehearsal. |
| Stage the exact pair | Use an isolated test hostname and data store. Serve the Unity player with correct MIME and Cache-Control. Unity builds uncompressed files here. Where the host packager adds gzip sidecars, verify the negotiated Content-Encoding against the actual bytes. Terminate TLS, route same-origin `/ws` to Rust with WebSocket upgrade support, and route `/info`, `/ping`, `/healthz` and the public account/lobby APIs used by the selected build. Check real account flows end to end. The older Caddy sample only forwards four game paths. Keep the control API on loopback and use the selected production auth mode. Specify API and WebSocket origins, configure forwarded client identity only for trusted proxy peers, and verify two real clients are separately rate-limited. Record served hashes and a joined browser match. |
| Prepare the production switch | Save the existing Worker deployment/version ID, custom-domain mapping, DNS/TLS settings, cache rules and legacy storage bindings. Recheck `/build-info.json` on the change day. Decide what happens to existing player accounts and progress, with a tested backup or explicit fresh-alpha policy. Stage a matched Unity manifest and Rust origin reachable at the final hostname, and verify allowed origins and TLS. The manual workflow in `.github/workflows/deploy.yml` currently republishes the Phaser build. The practice-domain tooling deliberately refuses production writes. The new hostname path needs an owner-reviewed production release change. |
| Cut over with an owner-approved rollback | Rehearse the switch on staging first. Record the exact production routing change, release SHA, artifacts, operator, monitoring window and rollback target. Drain active sessions. If carrying profiles forward, quiesce legacy writes, preserve and hash the final original snapshot, then import and reconcile it before admitting alpha players. Switch the HTML, immutable asset URLs, public API and WebSocket origin as one release. Verify from an external network and a fresh profile that the production hostname serves the matching Unity build and protocol/hash, joins two clients, completes a match, records the result and survives reconnect. Check error and join rates, room capacity, latency and persistence before widening access. |
| Roll back on a failed release check | On identity mismatch, repeated join failures, missing ground, lost results or a persistence failure, stop new admissions to the candidate and restore the recorded Worker version and routing. Verify the former live identity and a successful legacy join. Keep the candidate database isolated for reconciliation so a rollback cannot overwrite progress in either environment. Retain the release receipts and failure evidence. |

The project assigns provider mutations and production activation to the owner. The alpha target is established. The remaining approval is for the concrete deployment design, cost, data policy and cutover once staging evidence is available. [Hosting rules](../../hosting/AGENTS.md) · [Staging packaging](../../hosting/staging-mvp/README.md) · [Temporary-host decision](../adr/0020-temporary-single-host-staging.md) · [Existing production workflow](../../.github/workflows/deploy.yml)

**Definition of launched.** A fresh browser at **https://play.roguehalla.com** displays the intended Unity build, joins the matching Rust server, shows the approved ground and UI, completes and reconnects to a match, and retains the promised progression. The handoff back to the owner includes exact deployed source and artifact hashes, browser evidence, gate and CI results, capacity measurements, and the verified rollback target.

# P-NET-DRAWAHEAD-1b: the Unity client draws walking remote players ahead (legacy draw-ahead)

- **Serves:** not in the plan: owner approval 2026-11-19 ("Yes please add draw ahead back in", project thread "Cut delay
  on other players"). Second half of the draw-ahead; P-NET-DELAY-1 measured the 55 ms floor and named this as the larger
  win (about 50 ms).
- **Implementer tier:** implementer-opus (it moves what the drawn picture and the attack stamp are measured against; the
  server's rewind must not move)
- **Reviewer:** reviewer-opus
- **Integration base:** `feature/unity-rework` @ `06ebf2e9`, stacked on P-NET-DRAWAHEAD-1a (`packet/P-NET-DRAWAHEAD-1a`).
  This packet's oracle is on `packet/P-NET-DRAWAHEAD-1`. Start it after 1a has landed (rebuild the branch on the
  integration branch then), or from `packet/P-NET-DRAWAHEAD-1` with 1a's implementation merged in: its C# codec must
  decode `WalkDir`/`WalkTicks` for the game to use the hint (the oracle itself builds `WireActor`s directly and does not
  need the codec).
- **D-rules touched:** D1 loose proof (headless numbers against the legacy constants: ahead one tick, at most one tick's
  walk past the newest sample, mobs 25 ms) · D2 n/a · D9: presentation only; the server rewinds as before (the stamp does
  not move); local walk prediction (`LocalMotion`) is untouched.

## 1. ID and outcome

`P-NET-DRAWAHEAD-1b`. Port the legacy client's draw-ahead into `RoguehallaNet`. A remote PLAYER whose newest snapshot
says it keeps walking (`WalkTicks >= 1`) is drawn past its newest sample along `WalkDir`, at the speed its last two
samples show, for at most min(ahead, `WalkTicks` x tick, one tick's walk), clipped at walls, never into a root, stun,
cast, windup, death or cut. With no hint it holds the newest sample. A moving mob walks on at its newest step's velocity
for at most 25 ms. The render clock's floor, start and target delay drop by the ahead amount (one tick), so on a calm link
a walking remote player is drawn within a few units of where the server has it now, instead of 55-67 ms of walk (9-11 u)
behind. Corrections go into a per-fighter offset that decays, speed-capped, never by a jump. Attacks and casts keep the
time stamp they have today, so the server's rewind is unchanged.

## 2. Dependencies

P-NET-DRAWAHEAD-1a (the hint on the wire; the C# `WireActor.WalkDir`/`WalkTicks` fields are declared by its stub).

## 3. Relevant legacy paths (verified with `rg`)

- `packages/client/src/net/remoteDraw.ts:1-21`: the whole design in its header (players with a hint, mobs, corrections,
  attacks); `:31-54` constants (`REMOTE_SNAP_DISTANCE = 32`, `REMOTE_VISUAL_TAU_MS = 50`, `REMOTE_CORRECT_SPEED_MOVING = 120`,
  `REMOTE_CORRECT_SPEED_REST = 44`, `CATCHUP_SHARE = 1.0`, `AHEAD_SHARE = 0.9`, `REMOTE_MAX_AHEAD_U = PLAYER_SPEED x
  EXTRAPOLATE_MS / 1000` = 8.5 u, `MIN_WALK_SPEED = 5`, `MAX_WALK_SPEED = 2.5 x PLAYER_SPEED`, `CLIP_RADIUS = PLAYER_RADIUS - 1`);
  `:186-216` `noteFx` (hold windows: a windup from `t + max(0, ms - RANGED_RELEASE_PAUSE_MS)` for a ranged weapon, else
  from `t`; a cast; a stun or root status; cancel/stagger drop the windup; death holds forever until a revive);
  `:222-380` `draw` (budget, wall clip via `clearRun`, the offset model, cut/snap/decay, the slide along a wall);
  `:389-395` `attackTime`; `:497-516` `correctionCap`.
- `packages/shared/src/interp.ts:101-120` `SnapshotBuffer.walk` (newest step; speed = the faster of the last two steps;
  a step across a cut or a gap over two ticks does not count); `:127-135` `sampleAhead`; `:230-233`, `:305-323`
  `RenderClock` `aheadMs`/`setAheadMs` (the asked delay, its floor and the initial delay drop by it; `maxMs` does not);
  `:416-433` `want()` ("Draw-ahead keeps the same lateness cover, counted from where it starts to hold").
- `packages/client/src/net/interpolation.ts:42,53-62` (`drawAhead` sets `aheadMs = EXTRAPOLATE_MS`), `:93-101` (the hint is
  kept per player with the time of the patch it came with; it applies only when it is from the same patch as the newest
  sample, `remoteDraw.ts:244`).
- `packages/shared/src/constants.ts:19` `MIN_INTERP_DELAY_MS = 70`; `:26` `EXTRAPOLATE_MS = TICK_MS`; `:28`
  `MOB_EXTRAPOLATE_MS = 25`; `:33` `WALK_HINT_MAX_TICKS = 3`.
- Legacy draw-ahead was a client setting, on by default (`packages/client/src/settings.ts:24,41`; the switch "Draw remote
  fighters ahead", `ui/settingsPanel.ts:57`; applied at `scenes/RaidScene.ts:351,417` through `RemoteDraw.setEnabled`, with
  a drain when turned off, `remoteDraw.ts:154-180`). This packet turns it on in `Session` (the legacy default) and adds no
  setting UI (a setting is its own packet), so the drain is not needed.
- `docs/OWNER_DECISIONS.md`: no row on draw-ahead or extrapolation (searched "draw-ahead", "draw ahead", "extrapolat").
  Legacy "owner decision 5" (`remoteDraw.ts:18-20`, `:383-388`) is the legacy owner's ruling on the attack stamp; this
  packet keeps it.

## Established facts (Unity today)

- `client-unity/RoguehallaNet/Net/RenderClock.cs`: floor `MinDelayMs = 55` (P-NET-DELAY-1), `MaxDelayMs = 150`, start 100;
  target = clamp(`TickMs` + 98th-percentile arrival excess); `RenderMs` is the one time used for drawing AND stamping.
  `Session.cs:609` sets `Clock.TickMs` from the Welcome's tick rate.
- `client-unity/RoguehallaNet/Net/SnapshotBuffer.cs:122-171`: interpolates, holds the newest when starved ("no
  extrapolation at this stage"), never lerps across a teleport (`TeleportDistance = 256`) or a `DisplaceSeq` change.
- Where Unity reads remote positions: `client-unity/UnityProject/Assets/Scripts/Net/MatchClient.cs:312`
  (`Session.Snapshots.Sample(Session.Clock.RenderMs, _actors)`: the drawn fighters); `View/ArenaView.cs:300`
  (`_renderMs = session.Clock.RenderMs`, the time fx cues are due, `ArenaView.Fx.cs:56,79`). Every other
  `Snapshots.Sample(Snapshots.NewestMs, …)` (MatchClient `:534,559,574` targeting, TacticalMap, SentencedHuntPanel,
  UiLayoutReview, ScenarioProbe, ArenaView `:304` membership) reads the newest snapshot, not a drawn position: unchanged.
- The attack/cast stamp: `MatchClient.cs:229,379,387,426,430` send `Session.Clock.RenderMs`; `Session.cs:215` stamps a
  Wish cast with `Clock.RenderMs`. `LocalMotion.AttackPause.cs:132-137` `AttackTime(renderMs)` is the identity, with the
  doc "There is no draw-ahead in this packet ... the hook exists so P-NET-2 can change one body".

### Design (the oracle fixes the API it names; the rest is the implementer's)

- `RenderClock` gains `bool DrawAhead` (off on a bare clock, so every existing clock test is untouched; `Session` turns it
  on: `new RenderClock { DrawAhead = true }`), `AheadMs` = `DrawAhead ? TickMs : 0` (one tick; 25 ms at a 40 Hz Welcome),
  `DrawMs` (the time remote fighters, projectiles and fx are drawn at) and `LeadMs`.
  - Internally the clock runs the draw time exactly as today with floor `MinDelayMs - AheadMs`, start
    `StartDelayMs - AheadMs` and target `TickMs + excess - AheadMs` (ceiling `MaxDelayMs` unchanged, legacy).
  - `LeadMs` = min(`AheadMs`, max(0, `MaxDelayMs` - draw delay)) (legacy `attackTime`, `remoteDraw.ts:391-394`).
  - **`RenderMs` = `DrawMs - LeadMs`**: it stays the attack/cast stamp, the render time the clock without draw-ahead
    gives. So no stamp call site changes (`MatchClient.cs`, `Session.cs:215`), `LocalMotion.AttackTime` stays the
    identity, and the server's rewind (`crates/rh-sim/src/systems/weapons.rs` `note_lag`, clamped to [50, 300] ms) sees
    the same lag as today. Checked with a scratch prototype (not committed): `WishSessionTests` (`RenderMs` 1150 at the
    first snapshot of 1250, 1100 after a cut) holds with `DrawAhead` on, because draw start 1200 minus lead 50 is 1150.
  - `DelayMs`/`TargetDelayMs` report the draw clock's delay (floor `MinDelayMs - AheadMs` when on); with `DrawAhead` off
    they are what they are today.
- `RemoteDraw` (new, `client-unity/RoguehallaNet/Net/RemoteDraw.cs`; the stub fixes the constructor
  `RemoteDraw(SnapshotBuffer)`, `Map`, `TickMs`, `MobExtrapolateMs = 25`, and `bool Sample(double drawMs, List<InterpActor>)`):
  the port of `remoteDraw.ts`. It fills the list as `SnapshotBuffer.Sample` does and moves each remote fighter's `X, Y`.
  Per-fighter state by id (offset, last model, last drawn spot, frame number); fx hold windows fed by `Session` from fx
  frames (`Session.cs:536-560` already routes the owner's own windup/stagger: add every fighter's windup, cast, cast end,
  stun/root status, cancel, stagger and death the same way); `Effects` stunned/rooted/casting bits and `Status != alive`
  hold; a `DisplaceSeq` change or a 256 u jump is a cut (drawn where it lands, offset reset); walls from the Welcome map
  (`Session.Map`), clipped with a body of `PLAYER_RADIUS - 1`. Ticks to ms with `TickMs` from the Welcome (never 50 as a
  constant), and the cap `min(AheadMs, WalkTicks x TickMs, REMOTE_MAX_AHEAD_U / speed)`.
- `Session` owns one `RemoteDraw Remote` (constructed with `Snapshots`, `Map` and `TickMs` set at Welcome, reset with the
  timeline on a source-clock cut, `TimelineReset`).
- Unity call sites: `MatchClient.cs:312` becomes `Session.Remote.Sample(Session.Clock.DrawMs, _actors)`;
  `ArenaView.cs:300` `_renderMs = session.Clock.DrawMs` (fx, projectiles and fighters share the one draw time, legacy
  `remoteDraw.ts:11`).
- Mob hold: legacy holds a mob whose bow is drawn (`drawStartedAt !== 0`, `remoteDraw.ts:265`); the wire has no mob draw
  clock, only `ACTOR_FLAG_ENGAGED` (cooldown running or bow drawn). Use `Engaged` (holds a little more often than legacy;
  25 ms of a mob's walk is at most a few units). Flagged as a legacy doubt.

### Existing tests that pin what this packet changes

| Test | Asserts | With this packet |
|---|---|---|
| `client-unity/RoguehallaNet.Tests/NetTests.cs` `SnapshotAndClockTests.*` (`CalmLinksDrawRemoteFightersCloserToNowWithoutStalling`, `StartsAtAHundredMilliseconds`, the jitter/stall/catch-up tests) | a bare `RenderClock`: floor 55, start 100, render time never more than 5 ms past the newest sample on more than two frames | unchanged and green: `DrawAhead` is off on a bare clock |
| `client-unity/RoguehallaNet.Tests/LocalMotionTests.cs:354` `AttackTimeIsTheRenderTimeInThisPacket` | `LocalMotion.AttackTime(x) == x` | stays green, body untouched: `RenderMs` is still the stamp. Its premise ("no draw-ahead") is superseded by this packet; the implementer updates the hook's doc in `LocalMotion.AttackPause.cs:132-137` (comment only). Whether to retire the test is the coordinator's call (not needed). |
| `client-unity/RoguehallaNet.Tests/WishSessionTests.cs:136,209,212,216` | `cast.T == s.Clock.RenderMs`; `RenderMs` 1150, 1100, 100 after first snapshots and cuts | stays green with `DrawAhead` on in `Session` (RenderMs = draw - lead; see Design) |
| `SnapshotAndClockTests` `SnapshotBuffer` tests (`HoldsNewestWhenStarved…`, `FacingTakesTheShortWay…`) | the buffer holds the newest and never extrapolates | unchanged: `SnapshotBuffer.Sample` keeps its behaviour; draw-ahead lives in `RemoteDraw` |
| `MobContinuityTests`, `SessionTests` | read `Snapshots`, not drawn positions | unaffected |

No existing test needs an amendment.

## 4. New paths

- `client-unity/RoguehallaNet/Net/RemoteDraw.cs` (the stub creates it; the implementer replaces its body) and its Unity
  copy `client-unity/UnityProject/Assets/RoguehallaNet/Net/RemoteDraw.cs` (+ `.cs.meta`, created by the stub).

## 5. Exclusive write scope

- `client-unity/RoguehallaNet/Net/{RenderClock.cs, RemoteDraw.cs, SnapshotBuffer.cs, Session.cs}` and
  `LocalMotion.AttackPause.cs` (doc comment only); their Unity copies via `bash client-unity/sync-net.sh`.
- `client-unity/UnityProject/Assets/Scripts/Net/MatchClient.cs` (line 312 only) and
  `client-unity/UnityProject/Assets/Scripts/View/ArenaView.cs` (line 300 only). These pull the Unity EditMode suite into
  the gate (it reports "no Editor" without one).
- New unit tests of its own beside the oracle are welcome (`client-unity/RoguehallaNet.Tests/RemoteDraw*Tests.cs`).

Out: the codec (`Protocol/`, 1a), `LocalMotion` behaviour, every Rust crate, `docs/MIGRATION.md`.

## 6. Contracts and references

R-AUTH / D9 (presentation only; the server decides; nothing new is sent), ADR 0008, C-NET (no wire change here),
`client-unity/AGENTS.md` (library is the source of truth; sync the copy).

## 7. Behaviour to preserve

- The attack/release/cast `t` the server receives: the same as without draw-ahead (the oracle compares it with a bare
  clock on the same stream, within 2 ms once settled).
- A bare `RenderClock` behaves exactly as today (all `NetTests` green).
- `SnapshotBuffer.Sample` (used for targeting and the newest-snapshot readers) is unchanged.
- No per-frame garbage in `RemoteDraw.Sample` (the buffer's no-garbage rule, `SnapshotBuffer.cs:44-48`; `GarbageTests`
  measure the library's hot paths: keep per-fighter state in reused storage).

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-NET-DRAWAHEAD-1` passes (C# tests and the Unity copy check).
- Every `DrawAheadTests` test green; every existing C# test green with no edit.

## Oracle files (read-only for implementers)

- `client-unity/RoguehallaNet.Tests/DrawAheadTests.cs`
- `docs/backlog/packets/P-NET-DRAWAHEAD-1b.md`

Implementer-replaceable (stub commit): the `DrawAhead`/`AheadMs`/`LeadMs`/`DrawMs` members of
`client-unity/RoguehallaNet/Net/RenderClock.cs`, the whole of `client-unity/RoguehallaNet/Net/RemoteDraw.cs`, their Unity
copies and the copy's `.meta`.

The oracle drives the library headlessly: one snapshot per 50 ms tick, 30 ms of latency, each stamped at the frame
(60 and 144 fps) that polls it; each frame `clock.Update`, then `RemoteDraw.Sample(clock.DrawMs, …)` on an open floor map.
"True position" is the server's position at local time minus the latency.

| Test | Asserts | Today (stub) |
|---|---|---|
| `AWalkingPlayerWithAHintIsDrawnNearItsTrueServerPositionAndNeverPastTheCap` | walking east at 170 u/s with `WalkTicks` 3: mean error <= 3.5 u, worst <= 5.5 u (after 4 s); never more than 8.5 u past its newest sample; y unchanged | **red**: `fps 60: mean error 9.99 u` (`Expected: less than or equal to 3.5d But was: 9.98…`, line 95) |
| `APlayerWithoutAHintIsHeldAtItsNewestSample` | `WalkTicks` 0: never drawn past its newest sample | green (guard) |
| `AnAbruptStopSettlesOnTheStopWithoutAJump` | a walker whose last walking samples still promise 3 ticks stops dead: each frame moves at most 1.05 x 170 u/s x dt + 0.25 u; never more than 8.5 u past the stop; within 0.25 u of it 1 s after the stop arrives | green (guard; it is red for a draw-ahead that snaps the overshoot back instead of decaying it) |
| `AMovingMobIsDrawnAtMostTwentyFiveMillisecondsPastItsNewestSample` | a regional mob at 100 u/s: at most 2.5 u past its newest sample, and at least 1 u past it on some frame | **red**: `fps 60: … Expected: greater than or equal to 1.0d But was: -2.17` (line 146) |
| `TheAttackStampIsRenderTimeMinusTheLeadAndMatchesTheClockWithoutDrawAhead` | `AheadMs` is one tick; `DrawMs == RenderMs + LeadMs`; both never run backwards; on a calm link `LeadMs` = 50 +/- 0.5 and the draw-ahead clock's `RenderMs` is within 2 ms of a bare clock's | **red**: `fps 60 at 5000: on a calm link the draw runs one tick ahead of the stamp. Expected: 50.0d +/- 0.5d But was: 0.0d` (line 176) |

Feasibility, measured with a crude scratch prototype (the clock design above, plus straight extrapolation with no offset
model; reverted, never committed): mean error 1.49 u (60 fps) and 1.15 u (144 fps), worst 2.49 u, at most 7.3 u past the
newest sample; the stamp test and the mob test green; `WishSessionTests` and `NetTests` green. The prototype failed only
the stop test (a 4.8 u snap at 60 fps), as a port without the offset decay should.

## 9. Validation commands

- `dotnet test client-unity/RoguehallaNet.Tests --filter FullyQualifiedName~DrawAheadTests`
- `dotnet test client-unity/RoguehallaNet.Tests`, `bash client-unity/sync-net.sh --check`
- `scripts/gate.sh --quick --oracle packet/P-NET-DRAWAHEAD-1`

## 10. Risk and suggested model tier

The stamp: if `RenderMs` is allowed to become the draw time, every attack is stamped up to 50 ms later and the server's
rewind shrinks by that much (a silent hit-registration change). The offset model is subtle (legacy spent several
iterations on reversals and wall slides). Opus.

## 11. Known traps

- `RenderMs` must stay the stamp (render minus lead), or `WishSessionTests` and the rewind move. Do not "fix" the
  stamp call sites in `MatchClient.cs`.
- The hint applies only with the newest sample it came in (`remoteDraw.ts:244`, `h.t === w.t`): a stale hint must not
  carry a fighter on after its newest snapshot stopped carrying one.
- Ticks to ms with the Welcome's `TickMs` (40 Hz is coming: 25 ms ticks, `WalkTicks` up to 6).
- `SnapshotBuffer.Sample` is also the newest-snapshot reader for targeting: do not put draw-ahead into it.
- A cut (teleport, `DisplaceSeq`) resets the offset and is never slid into; a death holds.
- `MIN_WALK_SPEED` 5 u/s and `MAX_WALK_SPEED` 2.5 x 170 bound what counts as a walk.
- No garbage per frame; run `bash client-unity/sync-net.sh` after every library edit; do not commit `BakedContent.cs`,
  Showcase fonts or `ShaderGraphSettings.asset`.

DEVIATIONS: proposed DEV row: Unity draws ahead always (legacy's default) with no setting to turn it off, and holds a mob
on `Engaged` where legacy used the mob's bow draw clock (not on the wire).

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->

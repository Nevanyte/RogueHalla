# P-UI-ABILITY-FEEDBACK-1: a cast key that cannot work says why, in legacy's words, and plays no success pop

- **Serves:** not in ORDER_OF_OPERATIONS. Owner direction 2026-11-22, chat of the UI rework lane: "ability and spell hub
  needs optimizing", "make sure all in-game notifications are thoughtfully placed". Legacy parity: the legacy ability slot
  has a deny line.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** lane branch `lane/ui-rework/base-3` (checked at f90ca108a by the oracle author, 2026-11-23; the
  spec named fdd42f54a, the later lane merges touched no file of this packet). Of the packets still open only
  P-UI-QUEST-OFFER-1-fix touches a file this packet reads (`MatchHud.QuestRewards.cs`).
- **Worktree:** `/Users/nevinmckinnon/Documents/Projects/RH-ui-b`
- **D-rules touched:** D1: the Wish line is legacy's, in its words and by its rule (`packages/client/src/ui/abilitySlot.ts`
  `castDenial` :140-160, `formatCooldown` :128-132, `BLOCK_LABEL` :134, `DENY_MS` :211; callers
  `packages/client/src/scenes/RaidScene.ts` `castSlot` :1521-1592); the Shift and Q lines extend it by the server's rule.
  D2 n/a. D5/D6 none. D9: the client holds no rule; it reads delivered facts to pick one of two looks of a press and never
  stops, delays or changes what is sent. No owner decision covers this (searched `docs/OWNER_DECISIONS.md` for cast, deny,
  ability: no row).

**Proposed `DEVIATIONS.md` rows** (the implementer lists them in the hand-off, never edits the file; none exists):
(A) place, look and life: a lane pill in the feedback lane, ONE shared line for R, Shift and Q where legacy has one
`.as-deny` line per slot (`abilitySlot.ts` :225, :324; the newest press wins), legacy's 1 s hold inside the lane's .12 s in
and .25 s out (1.37 s in all). (B) what is sent: legacy returns before sending when it shows a line, and silently for a
missing, hidden or empty slot (`RaidScene.ts` :1527, :1540), a planning world or a time before a teleport cut (:1527), a
non-self cast in a peek (:1532), an unanswered cast (:1535) and the teleport grace (:1538); Unity sends and the server
decides. (C) Shift and Q: legacy's slot has no quest-spell line; these extend it ("Casting", "Ready in N") by the server's
rule. Lines left out: section 1, point 7.

## 1. ID and outcome

`InputCue` (`client-unity/UnityProject/Assets/Scripts/UI/MatchHud.Pulses.cs` :20) runs from `Session.CueSent`, raised
inside `Session.FlushInput` after the frame is sent (`client-unity/RoguehallaNet/Net/Session.cs` :362-386). On every Cast
command it pops `_wishTile` or `_questSlotTiles[slot - 1]` (:24-26), whatever the facts say, and calls
`ArenaView.LocalCastCue`. The server refuses a cast it cannot take without any event
(`crates/rh-sim/src/systems/abilities/mod.rs` `handle` :70, refusals :96-127; `quests/board/rewards/casts.rs` `cast` :36),
so a refused press looks like an accepted one. After this packet a press that the delivered facts say cannot work shows one
line in the feedback lane and no pop; a press that can work pops as today.

1. **One pure function** (new `CastReadiness`, below) says from delivered facts why a cast key would do nothing now. It
   calls `AbilityFaces.Wish` / `AbilityFaces.Spell` (`Net/Ui/AbilityFace.cs` :55, :36), the code the tiles draw with, so
   its phase is the tile's by construction. Legacy's order (`castDenial`): an empty slot gives no line and `Empty` true;
   casting (`face.Cast >= 0`, i.e. the Wish cast bar, or Effects bit 4096) "Casting"; Cooling "Ready in " +
   `Cooldown(face.Seconds)`; then a blocker whose bit is in the caller's mask: "Stunned", "Silenced" or "Rooted" (that
   priority); else no line.
2. **The server's rule for ready.** Of the server's Wish refusals (`systems/abilities/mod.rs` :96-127) the function covers
   four: the cooldown (`now < ready_at` :117, which `spend_charge` sets to the next charge's arrival, so `ReadyAt` alone
   decides, as in `AbilityFaces.Wish`), a cast in progress (:116), stun and silence (`CANCELLED_BY`,
   `crates/rh-sim/src/abilities/mod.rs` :244, root false; legacy `WISH_BLOCKED_BY`, `packages/shared/src/abilities.ts` :169,
   agrees), so the Wish keeps "Stunned" and "Silenced" (mask 5). It does NOT cover stasis (:96), a taunt or a pull (:104),
   the teleport grace (:109-114), nor target, blink and aim validity (:127 on). "Rooted" stays in the library function as
   legacy has it; no HUD caller passes it. Quest spell: castable when `Charges > 0` (the server recharges in its tick);
   `ReadyAt` is the next recharge. **Shift and Q get "Casting" and "Ready in N" only, no blocker line:** a silence, a stun
   unless Cleanse, a taunt, a pull and a Spell Shield refuse a quest spell by a table the client would copy from the
   server. **Lantern Ward** (a charge in hand while the next recharges) follows the server: no line, the pop plays, though
   the tile shows Cooling (a fault of `AbilityFaces.Spell`, which reads `ReadyAt` alone: a later packet).
3. **Where:** the feedback lane (`_feedbackModel`, above the action row), `NoticeTone.Warn`, key `CastReadiness.LineKey`
   ("cast", shared by R, Shift and Q): a press replaces the line in place and restarts its hold, a press of another key
   too; one pill, never a stack. No label on a tile. New in `UI/MatchHud.Notices.cs`: `void PushFeedbackLine(string text,
   NoticeTone tone, string key)` calling `_feedbackModel?.Push(text, tone, key, CastReadiness.HoldSeconds, dropIfLate:
   true)`. **Lifetime:** legacy's 1000 ms hold (`HoldSeconds` 1f) inside the lane's .12 s in and .25 s out. **Never late:**
   a line that cannot show at once is dropped, not queued, and a shown line is removed, not paused, when the lane stops
   being visible (one that appears or returns later says something untrue; model change below). The lane holds 2 lines
   (`HudRegions.FeedbackMax`, `Net/Ui/HudRegions.cs` :57); a sticky line takes one.
4. **The server's own notice:** none for a refusal; an accepted spell sends "Used {name}" (event lane). No code.
5. **The pop** (`InputCue`, Cast case only): `ReadCast(slot)`. A line: `PushFeedbackLine(line, NoticeTone.Warn,
   CastReadiness.LineKey)`, no pop. No line and not `Empty`: `_feedbackModel?.End(CastReadiness.LineKey)` (a showing cast
   line is untrue once a press goes through; the line has no slot, so it ends whichever key's line shows), then
   `Pulse(tile)` as today. `Empty`: neither, and a showing line is left alone. No key is bound, nothing a key sends
   changes, no shake, no frame, no new motion. `LocalCastCue`, Swap, Pickup and UseConsumable stay. Reduced motion already
   drops the pop (`HudPulses.Play`); the line still shows. The comment at the top of `MatchHud.Pulses.cs` (:14-15, "It
   shows that the key was heard and nothing about the outcome") is reworded: a pop means the client knows no reason for a
   refusal, not that the server accepted.
6. **Facts, and when each is read.** A press is handled in `MatchClient.Update` (`ReadInput`, then `Session.FlushInput`,
   which raises `CueSent`); `MatchHud.LateUpdate` runs after it. A new private `CastReading ReadCast(int slot)` below
   `InputCue` reads the time `_session.ServerTimeMs` (`Session.cs` :131) for every key (the newest snapshot's; `HudClock`
   runs up to 150 ms ahead and would flip the line before the tile) and `_session.Me.Effects` (`Net/Views.cs` :138; 0 when
   `Me` is null), and per key:
   - R (slot 0): `_session.Me?.Wish` of the newest snapshot. Its tile refreshes in `LateUpdate` of the same frame
     (`RefreshOwnerPresentation`), so at the press the Wish tile is one snapshot behind its line.
   - Shift and Q (slots 1, 2): `_questSlotKeys[slot - 1]` (null is Empty; `.Charges`; declared
     `MatchHud.QuestRewards.cs` :16) and `_questTiles[slot - 1].ReadyAtMs` (`MatchHud.ActionBar.cs` :94;
     `AbilityTile.ReadyAtMs` :161). `RefreshQuestTiles` (`MatchHud.QuestRewards.cs` :418-458) writes both at delivery (the
     `Collections` handler, through `RefreshPlayerFlows`) and in every `TickPlayerFlows`: a State delivered before the
     press is in, so line and tile agree on the facts (only the time the tile last drew with can trail).
   A slot outside 0..2 or no session: Empty. Never throws.
7. **Legacy lines left out, each for lack of a client fact:** "No target" and the unit-cast denials ("Out of range", sight,
   immunity) need each ability's targeting kind and range (a copied table is a game rule). "Aim with the cursor": a press
   with no ground point sends nothing (`CollectSpellInput`, `Session.cs` :268, returns before `CueSent`). "Can't blink
   there" needs landing geometry; "Can't cast now" cannot occur; blocker lines for Shift and Q: point 2.
8. **What is sent does not change.** `Session`, `MatchClient`, `InputSender` are untouched; a denied press still queues its
   Cast and `CueSent` still fires once for it.

### Change to `ToastLaneModel` (`client-unity/RoguehallaNet/Net/Ui/ToastLaneModel.cs`, `Push` :45, `Restart` :88, `Tick` :120)

`Push(string text, NoticeTone tone, string key = null, float holdSeconds = 0f, bool dropIfLate = false)`: default
arguments, not an overload. The one product caller, `MatchHud.RouteNotice` (`MatchHud.Notices.cs` :47-55), and every call
in `ToastLaneModelTests` pass three arguments at most, so they get today's behaviour.
- **Hold.** `holdSeconds` finite and above 0 is the line's hold; 0, below 0, NaN and infinity use
  `NoticeRouter.HoldSeconds(text)` (2.2 s for a short text). `Item` keeps it; `Restart` uses it, not
  `HoldSeconds(BaseText)`. A keyed push that hits a line replaces text, tone, count and hold (the pushed one), restarts it
  and revives a leaving line, as today; an unkeyed hit keeps the line's hold.
- **Never late.** `dropIfLate` with a null key is a plain push (its hold still applies). With a key it searches only the
  showing lines: a hit is replaced in place (text, tone, hold), restarted and marked never-late. With no hit, if anything
  waits or the lane holds `capacity` lines (a sticky one counts), it returns and adds nothing to `_waiting`; with room it
  shows the line at once, marked never-late. At the first `Tick` with `visible` false every never-late line is removed,
  not paused (`WorldHudVisible` is false while the Tab map is open or the journal or a chest choice takes the keyboard; a
  paused line would show again, untrue, when the HUD returns). The other lines keep today's pause (`Tick` :122
  `if (!visible) return;`). Default false: every other caller queues as today.
- **`public void End(string key)`:** a showing, non-sticky line with that key that is not leaving starts its fade out as
  `SetSticky(null)` ends a sticky line (`Phase` Leaving, `Fade = Alpha * FadeOutSeconds`, :107); a null or unknown key, a
  waiting line and a sticky line are left alone.

### New file `client-unity/RoguehallaNet/Net/Ui/CastReadiness.cs` (namespace `Roguehalla.Net.Ui`)

Each type has a `/// <summary>`. `Empty`: the slot holds nothing; `Line`: why the key does nothing now, null if it goes.
```csharp
public readonly struct CastReading
{
    public readonly bool Empty; public readonly string Line;
    public CastReading(bool empty, string line);
    public bool Pops { get; }   // !Empty && Line == null
}
public static class CastReadiness   // legacy abilitySlot.ts castDenial
{
    public const string LineKey = "cast", Casting = "Casting", Stunned = "Stunned", Silenced = "Silenced",
        Rooted = "Rooted";
    public const ushort WishBlockers = (ushort)(Indicators.Stunned | Indicators.Silenced);   // 5
    public const float HoldSeconds = 1f;   // legacy DENY_MS 1000
    public static CastReading Wish(WishView? wish, double nowMs, ushort effects);
    public static CastReading Spell(bool has, byte charges, double readyAtMs, double nowMs, ushort effects);
    public static CastReading Judge(AbilityFace face, bool chargeInHand, ushort effects, ushort blockedBy);
    public static string Cooldown(double seconds);   // legacy formatCooldown
}
```
`Wish` is `Judge(AbilityFaces.Wish(wish, nowMs), false, effects, WishBlockers)`. `Spell` is
`Judge(AbilityFaces.Spell(has, charges, readyAtMs, nowMs, 0d), charges > 0, effects, 0)`. `Judge`, in order:
`face.Phase == Empty` gives (true, null); `face.Cast >= 0f` or `effects & 4096` gives "Casting"; `face.Phase == Cooling &&
!chargeInHand` gives "Ready in " + `Cooldown(face.Seconds)`; then the first of Stunned (bit 1), Silenced (4), Rooted (2)
set in both `effects` and `blockedBy`; else (false, null). The line is built from `face.Seconds` (whole seconds, rounded
up, capped at `int.MaxValue`) and this `Cooldown`, not `SlotText.ReadyIn` (`Net/Ui/SlotText.cs` :21), which takes
milliseconds and returns "" at 0. `Cooldown` takes seconds and has a cap: NaN, negative and -inf count 0; `s = ceil(x)`
capped at 2147483647; under 60 the plain integer, else `floor(s/60)` + ":" + two digits of `s % 60`, invariant culture:
0 "0", 59 "59", 59.001 "1:00", 60 "1:00", 299 "4:59", 3600 "60:00", 2147483647 "35791394:07"; it equals
`SlotText.Cooldown` (:12) of the same time in milliseconds. `ReadyAt` 52100 at now 50000 says "Ready in 3". The bit 4096 is
`Indicators.Casting` (`Net/Ui/Indicators.cs`; Stunned 1, Rooted 2, Silenced 4 at :43).

## 2. Dependencies

The lane base `lane/ui-rework/base-3` (P-UI-ABILITY-ROW-1, -MOTION-1, P-UI-WISH-TILE-1, P-UI-TOAST-LANES-1,
P-UI-TILE-PLATES-1, P-UI-ALLOC-METER-1 and -2 and the other lane merges). `AllocationMeter` is on the base
(`Assets/Tests/Editor/AllocationMeter.cs`).

## 3. Relevant legacy paths
- `packages/client/src/ui/abilitySlot.ts:128-132` `formatCooldown`; `:134` `BLOCK_LABEL`; `:140-160` `castDenial`; `:211`
  `DENY_MS`; `:225`, `:324-330` the one `.as-deny` line per slot (`deny(text, now)`).
- `packages/client/src/scenes/RaidScene.ts:1521-1592` `castSlot`: returns before sending when it shows a line (:1541-1545
  `castDenial`), silently on :1527, :1532, :1535, :1538.
- `packages/shared/src/abilities.ts:169` `WISH_BLOCKED_BY = ["stun", "silence"]`.
- Rust (the server's rule): `crates/rh-sim/src/systems/abilities/mod.rs:96-127`; `crates/rh-sim/src/abilities/mod.rs:244`
  `CANCELLED_BY`; `crates/rh-sim/src/quests/board/rewards/casts.rs:36`.

## 4. New paths
- `client-unity/RoguehallaNet/Net/Ui/CastReadiness.cs` and its synced copy
  `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/CastReadiness.cs` with `.meta`.
- Tests (oracle, committed): `client-unity/RoguehallaNet.Tests/CastReadinessTests.cs`,
  `client-unity/RoguehallaNet.Tests/ToastLaneHoldTests.cs`,
  `client-unity/UnityProject/Assets/Tests/Editor/AbilityFeedbackEditModeTests.cs` with `.meta`.

## 5. Exclusive write scope
- `client-unity/RoguehallaNet/Net/Ui/CastReadiness.cs` (the stub exists; the implementer replaces its bodies),
  `client-unity/RoguehallaNet/Net/Ui/ToastLaneModel.cs` (`Push`, `End`), and their synced copies under
  `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/` (run `bash client-unity/sync-net.sh`).
- Under `client-unity/UnityProject/Assets/Scripts/UI/`: `MatchHud.Pulses.cs` (the Cast case of `InputCue`, new `ReadCast`,
  the comment), `MatchHud.Notices.cs` (`PushFeedbackLine` only).
- Not in scope: `AbilityTile.cs`, `AbilityFace.cs`, `UiMotion.cs`, `MatchHud.ActionBar.cs`, `MatchHud.QuestRewards.cs` (read
  only), `ToastLane.cs`, `NoticeRouter.cs`, `Net/Session.cs`, `Net/MatchClient.cs`, `Tools/hud-seams.test.cjs`, and the
  files of the packets beside or after: P-UI-QUEST-OFFER-1-fix, P-UI-TEXT-AWAKE-1, P-UI-PLAYER-WORDS-3,
  P-UI-HUD-GARBAGE-1 (`MatchHud.cs`, `.Legacy.cs`, `.IconPresentation.cs`, `.Features.cs`), P-UI-TEXT-FIT-1 last.
- Never touch `CardDisplayCatalog.cs` or `BakedContent.cs`.

## 6. Contracts and references
- R-AUTH, ADR 0008, D9: Unity holds no game rule; the line reads delivered facts only. C-INPUT: what is sent does not
  change.
- Lane rules `.agent-runs/lane-ui-rework/specs/COMMON.md`: readability gate (a `/// <summary>` before the first type of a
  new non-test file, at most 800 lines), motion on unscaled time and instant under reduced motion, no steady-frame
  allocation, theme colours only.

## 7. Behaviour to preserve
- Nothing a key sends changes; `CueSent` fires once per sent Cast, Swap, Pickup, UseConsumable
  (`RoguehallaNet.Tests/InputCueTests.cs`, `InputCueFaultTests.cs` stay green; the Session is untouched).
- Swap, Pickup and UseConsumable pops; `ArenaView.LocalCastCue` on every Cast press.
- Every other `ToastLaneModel` push queues, pauses and holds exactly as today (`ToastLaneModelTests` green).
- The first-run hint, event lane routing, and `RouteNotice` unchanged.

## 8. Acceptance checks
- `scripts/gate.sh --quick --oracle packet/P-UI-ABILITY-FEEDBACK-1 --base lane/ui-rework/base-3` prints `GATE PASS`.
- `node --test client-unity/UnityProject/Tools/hud-seams.test.cjs` through the wrapper: no method declared in two
  `MatchHud*.cs` files (`PushFeedbackLine`, `ReadCast` are new); `MatchHud.Pulses.cs` contains no "ShowcaseSettings" and no
  `List<(`.
- The four `oracle-amend:` tests (below) pass with the implementation.

## 9. Validation commands
- `dotnet test client-unity/RoguehallaNet.Tests --filter "CastReadinessTests|ToastLaneHoldTests|ToastLaneModelTests|WishFaceTests|AbilityFaceTests|SlotTextTests"` (through `in.sh`).
- Unity: `unity-class.sh <worktree> AbilityFeedbackEditModeTests,AbilityMotionEditModeTests,ToastLanesEditModeTests`
  and `AbilityRowEditModeTests,WishTileEditModeTests`.

## 10. Risk and suggested model tier
implementer-sonnet. A pure function, a small state-machine change and one HUD hook. The risk is timing in the EditMode
rig (deliveries rise, seam time) and the "never late" lane rule; both are pinned by the oracle.

## 11. Known traps
- `hud-seams.test.cjs`: no method name may be declared in two `MatchHud*.cs` files; `MatchHud.Pulses.cs` may not contain
  "ShowcaseSettings" or `List<(`.
- `_questSlotKeys` is declared in `MatchHud.QuestRewards.cs` :16, `_questTiles` in `MatchHud.ActionBar.cs` :94; this packet
  only reads them, and P-UI-QUEST-OFFER-1-fix and P-UI-TEXT-FIT-1 edit files beside them. If a merge removed or renamed
  one, read `_session.Collections.Quests.Spells` and take the last spell of the slot with its `Charges` and `ReadyAt`, as
  `RefreshQuestTiles` does.
- `InputCue` runs inside `FlushInput`'s cue loop: a throw is held and rethrown after all cues. Guard every null.
- `Unbind` reaches `BindPlayerFlows(null)` and `_orderedNotices.Clear`, which clears both lane models: no new state
  survives a rebind. Statics to restore in tests: `UiMotion.ReducedOverride`, `MatchHud.NoticeDtOverride`.
- Time in EditMode does not advance: tests use `MatchHud.HudLocalMs` and `NoticeDtOverride`.
- Not established: a stale reading gives a line for a cast the server accepts (its "Used {name}" then shows in the event
  lane) or a pop for one it refuses. `ServerTimeMs` trails the server's clock by the one-way trip, so with the 1 s hold a
  line can outlive the cooldown it names by up to a second (legacy had the same window). The server also refuses, with no
  line, for R as well as Shift and Q: in stasis, under a taunt or a pull and in the teleport grace; and for R with no valid
  target, out of range, behind a wall or with no blink landing: such a press still pops. A stunned or silenced press of
  Shift or Q pops, as does one refused by the quest spell's own effect or a Spell Shield, and a Lantern Ward press
  although its tile shows Cooling. The shared line carries no slot: a popping press ends whichever key's line shows, and a
  press of another key replaces it. Repeated presses never queue; a notice waiting behind a full lane takes the cast
  line's slot no sooner than 1 s after the last press. With the lane full (the first-run hint and one more line) a denied
  press shows nothing. Frost's Wall aim sends on the click, so its line shows then. The character's own cast clip
  (`ArenaView.LocalCastCue`) still plays on a denied or empty press. Not seen in play.

## Existing tests that pin this code (each read by the oracle author, 2026-11-23)
- `RoguehallaNet.Tests/ToastLaneModelTests.cs`: the whole lane state machine through three-argument `Push` calls (hold 2.2 s,
  fade .12 / .25, waiting queue, `MaxWaiting`, sticky, pause while invisible). Stays green; guards that every other push
  gets today's behaviour.
- `RoguehallaNet.Tests/WishFaceTests.cs`, `AbilityFaceTests.cs`, `SlotTextTests.cs`: `AbilityFaces.Wish`, `.Spell`,
  `SlotText.Cooldown` / `ReadyIn`. Untouched; `CastReadiness` stands on them.
- `RoguehallaNet.Tests/InputCueTests.cs`, `InputCueFaultTests.cs`: `Session.CueSent` once per sent Cast, Swap, Pickup,
  UseConsumable; a faulty subscriber costs no frame. Untouched (Session is out of scope).
- `Assets/Tests/Editor/AbilityMotionEditModeTests.cs`: the four tests below press a cast key through `InputCue` and assert a
  pop. They change (oracle-amend, for the owner).
  - `APressPopsAboutTheCentre` (:331): cases the Wish tile (Cast slot 0) and QuestSpell1 (Cast slot 1), asserts the
    centre stays put, scale above 1 at half time, scale exactly 1 after.
  - `EveryPressPulseGoesThroughTheOneHudPulses` (:453): five cues pulse five elements, two of them casts, and none with
    reduced motion.
  - `TheWishTileHasOneScaleWriter` (:383): presses R on a cooling Wish (`AcceptedCast`) and bounds the scale; now a denied
    press.
  - `ReducedMotionKeepsTheWashAndTheWedgesAndDropsThePop` (:427): presses R on a cooling Wish and asserts scale 1 and no
    pulse; now passes for the wrong reason.
  The other tests of the class (wedges, cast bar, attack bar, accepted-cast pop, ready pulse, no garbage, new session, dirty
  frame) press nothing and stay green.
- `Assets/Tests/Editor/AbilityRowEditModeTests.cs` :409, :418: `InputCue` with Swap only (unchanged by this packet).
- `Assets/Tests/Editor/ToastLanesEditModeTests.cs`: the two lanes through `RouteNotice`; stays green.
- `Tools/hud-seams.test.cjs` :52 requires `InputCue` to stay `void InputCue(...)` in a `MatchHud.*.cs` file.

## Oracle files (read-only for implementers)
ORACLE_FILES_PLACEHOLDER

## Notes from the oracle author
NOTES_PLACEHOLDER

## Notes from previous attempts
<!-- Coordinator appends on escalation. -->

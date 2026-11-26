# P-UI-INPUT-CUES-1: casts, pickups, swaps and potions answer the key on the frame it is pressed

- **Serves:** not in ORDER_OF_OPERATIONS. Owner direction 2026-11-22 (MIGRATION checkpoint "Balance patch and test bot":
  the Sonnet audit he asked for, finding 5 in `docs/backlog/BALANCE_LAB_PATCH.md` "System improvements"). Run by the
  client-input lane at the coordinator's request.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `feature/unity-rework` @ `83edb687f`
- **D-rules touched:** D1 n/a (presentation) · D2 n/a · D5 none · D6 none · D9 unchanged: no outcome is predicted

## 1. ID and outcome

`P-UI-INPUT-CUES-1`. Only a left-click attack has an immediate local cue (`MatchClient.cs:444`
`Arena?.LocalAttackCue(Session.PlayerId)`, `SpriteActorPresenter.CombatCues.cs:82`). A Wish (R), a quest spell (Shift,
slot 1; Q, slot 2), a potion (E, or the HUD potion button), a weapon swap (Space, or the HUD slot buttons) and a pickup
(F, or the HUD Pickup button) show nothing until the server's answer arrives, a round trip plus up to a tick later. ADR
0008 allows click-time feedback that animates and predicts no outcome (`client-unity/AGENTS.md`: "click-time feedback is
presentation only").

When this lands: the session raises one cue per such intent on the frame its input frame is sent, whichever path queued
it (key, HUD button, Wish aim), and the HUD answers at once: the matching tile or button gives a short press pulse, and a
cast also gives a short local cue on the own actor. Nothing shows an outcome early.

## 2. Dependencies

None. `P-NET-ARRIVAL-CLOCK-1` also edits `Session.cs` (`OnReceived` and a new interface beside `ITransport`); this
packet edits `FlushInput` and the event list only, so the two merge without overlap. The coordinator merges either first.

## 3. Relevant legacy paths

- None required: presentation only. (Legacy showed casts from the server's state.)

## 4. New paths

None expected. If a new Unity script is unavoidable it needs a committed `.meta` with a fresh GUID.

## 5. Exclusive write scope

- `client-unity/RoguehallaNet/Net/Session.cs`: raise the stub event in `FlushInput`, nothing else
- the synced copy under `client-unity/UnityProject/Assets/RoguehallaNet/Net/` (by `bash client-unity/sync-net.sh` only)
- `client-unity/UnityProject/Assets/Scripts/UI/MatchHud.cs`, `MatchHud.Legacy.cs`, `MatchHud.QuestRewards.cs` (subscribe
  on bind, unsubscribe on unbind; the pulses)
- `client-unity/UnityProject/Assets/Scripts/View/ArenaView.Fx.cs` and
  `client-unity/UnityProject/Assets/Scripts/Presentation/SpriteActorPresenter.CombatCues.cs` (the own-actor cast cue)
- one new EditMode test file under `client-unity/UnityProject/Assets/Tests/Editor/` with its `.meta`, optional
- Not `MatchClient.cs` (P-NET-STEER-1 edits it).

## 6. Contracts and references

- ADR 0008, R-AUTH, `client-unity/AGENTS.md` UnityProject rules: every outcome comes from server observations.
- The existing click cue `SpriteActorPresenter.LocalAttackCue` and its correction window (`LocalCueWindow = .6f`,
  `CombatCues.cs:100-131`) are the pattern for a local cue that the server's own fact replaces or cancels.

## 7. Behaviour to preserve

- Nothing about what is sent: same commands, same order, same frames.
- No predicted outcome: no swapped weapon drawn, no potion count or health change, no item removed from the ground or
  added to the bag, no cooldown sweep started, no effect at the cast's target point. Those wait for the server, as today.
- A refused intent (no stock, wrong phase, out of range) shows the press pulse only, and nothing that looks like success.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-INPUT-CUES-1` passes, the Unity EditMode suite included.

### Design the implementer builds

- `Session.CueSent` (`event Action<WireCommand>`, declared by the stub): raised in `FlushInput`, after each frame is
  sent, once per command in it whose kind is `Cast`, `Swap`, `Pickup` or `UseConsumable`, in frame order. Never for a
  command the sender dropped (invalid values are never queued) or a frame that was not sent (not `InMatch`, detached).
- `MatchHud`: subscribes when it binds a session and unsubscribes when it unbinds or the session changes. Per cue:
  `Cast` slot 0 pulses the Wish tile; slots 1 and 2 pulse their quest-spell tiles; `UseConsumable` pulses the potion
  button; `Swap` pulses the weapon slots (the toggled one if known, else the bar); `Pickup` pulses the Pickup button.
  A pulse: about 150 ms of scale or brightness pop on an existing element; no new art.
- A `Cast` also calls a new `ArenaView.LocalCastCue(id)` that starts a short own-actor cue (reuse what the presenter
  already draws for a cast if it has one; otherwise a brief tint or ring), dropped when the server's cast fact arrives or
  after `LocalCueWindow`.

## Oracle files (read-only for implementers)

- `client-unity/RoguehallaNet.Tests/InputCueTests.cs`

## 9. Validation commands

- `export PATH="$HOME/.cargo/bin:$HOME/.dotnet:$PATH"; dotnet test client-unity/RoguehallaNet.Tests --filter InputCue`
- `bash client-unity/sync-net.sh` then `bash client-unity/sync-net.sh --check`
- `scripts/gate.sh --quick --oracle packet/P-UI-INPUT-CUES-1`

## 10. Risk and suggested model tier

Sonnet: one seam in the library and HUD wiring. The risk is a cue that reads as an outcome; review checks section 7.
Not proven by the gate: how the pulses look in a real build (no browser run in this packet).

## 11. Known traps

- The HUD can bind and unbind sessions many times (resume, spectate, training): never leave a handler on an old session.
- `Swap()` without a slot toggles: the client does not know which slot the server will pick, so pulse the bar.
- Edit `client-unity/RoguehallaNet/`, then run `sync-net.sh`; never edit the Unity copy.

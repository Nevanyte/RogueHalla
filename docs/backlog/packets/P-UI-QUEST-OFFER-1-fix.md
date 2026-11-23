# P-UI-QUEST-OFFER-1-fix: one accept latch for the journal and the offer card, it lets go after 2.5 s, and no click on Accept can abandon

- **Serves:** note 1 of the coordinator's merge review of the UI rework lane's wave 3 (reviewer-opus, 2026-11-23): two
  separate accept latches, and the journal's never times out. Owner direction 2026-11-22 in the lane's chat: "Quest UI
  could be perfected". Not in ORDER_OF_OPERATIONS. The lane lead's abandon guard (Outcome 5) is part of it.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** the lane branch `lane/ui-rework/base-3` (`fdd42f54a`; `QuestJournalView.cs`, `QuestOfferCard.cs`,
  `MatchHud.QuestRewards.cs` and `MatchHud.PlayerFlows.cs` are unchanged since `be8da07a0`). Worktree
  `/Users/nevinmckinnon/Documents/Projects/RH-ui-x`. Gate: `scripts/gate.sh --quick --oracle packet/P-UI-QUEST-OFFER-1-fix --base lane/ui-rework/base-3`.
- **D-rules touched:** D1: legacy has ONE latch. `packages/client/src/ui/questDialog.ts` keeps one `pending` accept
  (`accept()` returns false while it is set, `:380`), drops it after `ACCEPT_PENDING_EXPIRE_MS` = `ORDER_EXPIRE_MS` =
  `DELAY_MAX_MS` 400 + 200 = 600 ms (`questDialog.ts:23`, `:301-303`; `packages/client/src/net/localMotion.ts:131,138`) and then
  says "The server did not confirm your accept" (`:30`); it also drops it at once, showing the refusal text, when the
  server's refusal toast arrives (`noteToast`, `:394-404`). The legacy quest log has no accept button
  (`questLog.ts:198-204`: "Talk & accept" opens the dialog). One shared latch restores that shape. Proposed ONE row
  `DEV-<n>` (the coordinator writes it): legacy holds one accept for 0.6 s, ends the hold the moment the server's refusal
  toast arrives and shows that text, and shows a "not confirmed" line when it expires; Unity holds each quest's accept (up
  to four) for about 2.5 s of rendered frames (each frame counts at most 0.1 s), does not hook refusals (the owner-notice
  toast still appears) and shows no line when a hold expires; a hold also ends when the quest's row changes, including a
  `CanAccept` or `State` change for another reason (worst case one more `QuestAccept`, which the server answers); and for
  the same 2.5 s after an accept press the journal's Abandon of that quest takes no press, answered or not (a Unity-only
  guard against a triple click). D2 n/a. D5/D6 none. D9 untouched: the latch only decides whether the client sends
  `QuestAccept` again or takes an Abandon press; the server decides every outcome. No key is bound and nothing a key does
  changes.

## 1. ID and outcome

Today two separate latches guard "accept this quest": the journal's `_acceptPendingFor` (never times out) and the offer
card's `_acceptLeft` (2.5 s). They never see each other, so Journal ACCEPT, close it, F, Enter inside a second sends two
`QuestAccept` for one handle (the server answers the second "You already accepted that quest",
`crates/rh-sim/src/quests/live.rs:686`). A dropped command is never answered (`ActionBuf::push`,
`crates/rh-sim/src/input.rs:819`, returns false at `MAX_ACTIONS_PER_TICK` = 6, `crates/rh-sim/src/constants.rs:98`;
`crates/rh-server/src/room/commands.rs:202` tells the client nothing), so the journal shows "ACCEPTING…" until a view, a
rebind, a new epoch or a disconnect, while the card recovers. And a third click on ACCEPT abandons the quest just taken:
click 1 accepts, the server's answer swaps the button for Abandon in the same rectangle (`RenderAbandon`,
`QuestJournalView.cs:527`), click 2 arms it (`PressAbandon`, `:547`), click 3 sends.

When this lands: ONE latch owned by the HUD (`QuestAcceptLatch`, a pure class in the engine-free library) answers "may an
accept for this handle be sent now?" and "was an accept for this handle pressed less than 2.5 s ago?". The journal and the
card read it. A hold ends by time (2.5 s of frame ticks), by the server's answer for that quest, or by a clear; and an
Abandon press inside 2.5 s of an accept press for the same quest is ignored.

## 2. Dependencies

Base `lane/ui-rework/base-3` (it holds P-UI-JOURNAL-DETAILS-1, P-UI-QUEST-ACTION-PROMPT-1, P-UI-ALLOC-METER-1/2 and
P-UI-WORLD-NAMES-2). No other packet.

## 3. Relevant legacy paths

- `packages/client/src/ui/questDialog.ts:23` `ACCEPT_PENDING_EXPIRE_MS = ORDER_EXPIRE_MS`; `:30` the not-confirmed text;
  `:301-303` expiry of `pending`; `:380` `accept()` refuses while `pending !== null`; `:394-404` `noteToast` drops it on a
  refusal toast.
- `packages/client/src/net/localMotion.ts:131` `DELAY_MAX_MS = 400`; `:138` `ORDER_EXPIRE_MS = DELAY_MAX_MS + 200`.
- `packages/client/src/ui/questLog.ts:198-204`: the legacy quest log has no accept button.
- Server (what the client must not provoke): `crates/rh-sim/src/quests/live.rs:686` (already accepted),
  `crates/rh-sim/src/input.rs:819` and `crates/rh-sim/src/constants.rs:98` (six commands a tick, the rest dropped
  silently), `crates/rh-server/src/room/commands.rs:202`.
- Wire limits for test data: `crates/rh-protocol/src/encode/client.rs` (the `QuestInteract` line of the command check,
  about :128): the action is 1 to 9; Abandon, Cash out and Meditate (1, 4, 6) take target 0 only. The client codec also
  refuses an action without its quest row.

Owner decisions: none found on this subject in `docs/OWNER_DECISIONS.md` (searched "accept", "latch").

## 4. New paths

- `client-unity/RoguehallaNet/Net/Ui/QuestAcceptLatch.cs` (namespace `Roguehalla.Net.Ui`), its synced copy
  `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/QuestAcceptLatch.cs` and that copy's `.meta`.
- `client-unity/RoguehallaNet.Tests/QuestAcceptLatchTests.cs` (oracle).
- `client-unity/UnityProject/Assets/Tests/Editor/QuestAcceptLatchEditModeTests.cs` and its `.meta` (oracle).

## 5. Exclusive write scope

`A/` is `client-unity/UnityProject/Assets/`.
- `client-unity/RoguehallaNet/Net/Ui/QuestAcceptLatch.cs` and its copy under `A/RoguehallaNet/Net/Ui/` (run
  `bash client-unity/sync-net.sh` through the lane wrapper) with its `.meta`.
- `A/Scripts/UI/QuestJournalView.cs` (632 lines now; about twelve more): `Initialize`, the field, `Bind`, `Refresh`, the
  `acceptPending` line of `RenderSelection`, `TryAcceptFrom`, the first line of `PressAbandon`.
- `A/Scripts/UI/QuestOfferCard.cs`; `A/Scripts/UI/MatchHud.QuestRewards.cs` (field, property, `TickOffer`,
  `OfferFrameDeltaOverride`, the frame block of `RefreshOffer`, constructor call, `AcceptOffer`, `ResetQuestRewardHud`; 598
  lines now); `A/Scripts/UI/MatchHud.PlayerFlows.cs` (the `Initialize` call only).
- Not in scope: `MatchHud.Features.cs`, `MatchHud.cs`, `QuestTrackerView.cs`, `QuestOffer.cs`,
  `Tools/hud-seams.test.cjs` (it names no member this packet changes; run it through the wrapper anyway:
  `node --test client-unity/UnityProject/Tools/hud-seams.test.cjs`).
- A later packet may cut what a hidden journal does per frame; it must keep the latch's answer rule (4b), the stamp (4d)
  and the clears of 4(c). If the hidden-panel part of the stale P-UI-FRAME-COST-1 is ever taken up, its accept lines
  (`_acceptPendingFor`) are redrafted against `_latch` first. P-UI-TEXT-FIT-1 may touch one creation statement of the card
  or of `InitializeQuestRewardHud`: whichever merges second re-reads the base.
- The oracle files below are read-only; the three amended tests are changed by the `oracle-amend:` commits, not by the
  implementer.

## 6. Contracts and references

- Presentation only: no wire, protocol, `crates/`, `packages/` change. C-INPUT: every accept still goes through
  `session.Input.QuestAccept`. Lane rules: `.agent-runs/lane-ui-rework/specs/COMMON.md` (the implementer reads the
  rules this packet repeats below).
- Readability gate: a new non-test C# file has a `/// <summary>` before its first type and at most 800 lines;
  `QuestJournalView.cs` and `MatchHud.QuestRewards.cs` stay under 800 lines.
- Tests are a separate assembly with no `InternalsVisibleTo`: every member a test reads is `public`.
- No new object name contains "accept" (`Tools/ui-scenarios.cjs:433` clicks the first name that matches `/accept/i`).

## 7. Behaviour to preserve

- `TryAccept(QuestHandle)` and `TryAcceptFrom(Session, uint, QuestView, QuestHandle)` keep their signatures (tests reach
  the second by reflection).
- The journal's settled path: an unchanged `Refresh` writes no rectangle and allocates nothing
  (`QuestJournalDetailsEditModeTests.AnUnchangedRefreshWritesNoRectAndAllocatesNothing`).
- `RenderActions`, `RenderAbandon`, `_abandonArmed` and the arm label are untouched. The Abandon arm-then-send behaviour
  is unchanged after the guard runs out (`AbandonIsArmedUntilSomethingChanges`).
- `QuestOfferCard.GraceSeconds` (0.5 s) and the card's fade stay. The card sends nothing itself.
- The keys: nothing is bound and no key's meaning changes (Enter accepts the open card as today).
- Never edit `CardDisplayCatalog.cs` or `BakedContent.cs`.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-QUEST-OFFER-1-fix --base lane/ui-rework/base-3` prints `GATE PASS`.
- `dotnet test client-unity/RoguehallaNet.Tests --filter QuestAcceptLatchTests` green (14 tests).
- Unity EditMode classes `QuestAcceptLatchEditModeTests` (16 tests), `QuestOfferEditModeTests`,
  `QuestJournalPolishEditModeTests`, `QuestJournalDetailsEditModeTests`, `QuestJournalViewEditModeTests`,
  `PlayerFlowsEditModeTests` green.
- `bash client-unity/sync-net.sh --check` clean; `node --test client-unity/UnityProject/Tools/hud-seams.test.cjs` green.

## Design (the contract the implementer builds to)

### What is wrong today (code, on the base)

1. `A/Scripts/UI/QuestJournalView.cs` keeps `_acceptPendingFor` (field at `:41`). `TryAcceptFrom` (`:593`) returns false
   while it equals the handle (`:598`), else sends `session.Input.QuestAccept(handle)` (`:599`), sets it (`:600`) and calls
   `RenderSelection` (`:601`), which reads it as `acceptPending` (`:367`: the button reads "ACCEPTING…" and is not
   interactable, `:375-379`; `RenderStatus` says "Accepting…"). The click closure that `BindAccept` binds (`:395-399`)
   only calls `TryAcceptFrom`. Only `Bind` (`:212`, a new session) and `Refresh` (`:222`, `:229`, `:235`, `:242`, `:252`)
   clear the field: session not InMatch, a new `TimelineEpoch`, a null view, an unavailable new view, any new `QuestView`
   object.
2. `A/Scripts/UI/QuestOfferCard.cs` keeps its own `_acceptLeft` (`:31`): `MarkAccepting` (`:148`) sets it to `AcceptSeconds`
   (`:14`, 2.5), `Tick` (`:155`) ages it, `Open` (`:81`) and `Close` (`:132`) zero it; `Accepting` (`:75`) reads it.
   `MatchHud.AcceptOffer` (`MatchHud.QuestRewards.cs:210`) refuses while `card.Accepting`.
3. The guards never see each other (see 1. above).
4. A dropped command is never answered (see 1. above).
5. A third click on ACCEPT abandons the quest just taken (see 1. above). The journal's own double-click test stops at
   click 2.

### Outcome, item by item

1. **One latch, owned by the HUD.** `QuestAcceptLatch` (below). Holds are per handle and last `HoldSeconds` = 2.5 s of
   ticks. `QuestOfferCard.AcceptSeconds` (it shares a declaration with `GraceSeconds`, which stays) and `_acceptLeft` are
   deleted. `MatchHud.QuestRewards.cs` declares `readonly QuestAcceptLatch _acceptLatch = new QuestAcceptLatch();` (a field
   initialiser: it exists before `InitializePlayerFlows`) and `public QuestAcceptLatch AcceptLatch { get { return _acceptLatch; } }`.
   `InitializePlayerFlows` (`MatchHud.PlayerFlows.cs:34`) calls `_questJournal.Initialize(_root, ClosePlayerFlows, handle =>
   ..., _acceptLatch)`; `InitializeQuestRewardHud` (`MatchHud.QuestRewards.cs:67`) builds `new QuestOfferCard(_hud,
   _acceptLatch, () => AcceptOffer(), CloseOffer)` (the latch is the second argument; null throws `ArgumentNullException`).
2. **One clock, ticked by the HUD, not by the card.** New `public void TickOffer(float dt)` in `MatchHud.QuestRewards.cs`:
   `_acceptLatch.Tick(dt);` then `_offerCard.Tick(dt)` when the card exists (latch first, so the card's redraw in the same
   call sees a hold that has just ended). It is the seam EditMode time passes through. `RefreshOffer`'s once-per-
   `Time.frameCount` block (`:227-231`) calls `TickOffer(OfferFrameDeltaOverride ?? Mathf.Min(Time.unscaledDeltaTime, .1f))`
   in place of `card.Tick(...)`; `public static float? OfferFrameDeltaOverride` is null in play (the
   `UiMotion.ReducedOverride` pattern). Every frame the HUD runs reaches it (`LateUpdate` at `MatchHud.cs:244`,
   `TickFeatures`, `TickPlayerFlows` at `MatchHud.Features.cs:328`, `RefreshPlayerFlows`, `RefreshQuestRewardHud`) and
   nothing before the block tests the card, the journal or `WorldHudVisible`: the latch ages with the card closed, the
   journal open or the world HUD hidden. (`RefreshOffer` returns early only when the card is null, `:226`.)
3. **Journal.** `Initialize(Transform canvasRoot, Action onClose = null, Action<QuestHandle?> onTracked = null,
   QuestAcceptLatch latch = null)`; the field `QuestAcceptLatch _latch` (not readonly) starts as `new QuestAcceptLatch()`
   and is replaced by a non-null argument (a journal built alone keeps a private latch nobody ticks unless its test passes
   one in). `_acceptPendingFor` is deleted. The statements that change:
   - `TryAcceptFrom` (signature unchanged) keeps its checks (`IsCurrentView`, `LastResult`, `Find`, `CanAccept`) and its
     last two statements (`session.Input.QuestAccept(handle); RenderSelection();`). The `if (_acceptPendingFor.HasValue
     ...) return false;` line becomes `if (!_latch.TryBegin(handle, QuestAcceptLatch.SignatureOf(current))) return false;`;
     `_acceptPendingFor = handle;` is deleted. `BindAccept` (rebound only when session, epoch, view or handle changes) does
     not change: its closure calls `TryAcceptFrom`, which reads `_latch` at the press; no closure captures the latch.
   - `RenderSelection` (`:367`): only the line `bool acceptPending = has && _acceptPendingFor...` becomes `bool
     acceptPending = has && _latch.IsHeld(q.Handle);`. The status call, the label write guarded by `_acceptShowsPending`,
     the `_accept.interactable` line and the accept rebind after them stay.
   - `PressAbandon(Session, uint, QuestView, QuestActionView)` (`:547`, signature unchanged) gets one new first line: `if
     (_latch.PressedRecently(action.Quest)) return;`. `RenderAbandon`, `_abandonArmed`, the arm and the label are
     untouched; the stamp is read in this click handler and nowhere else.
4. **What ends a hold: these and no other.**
   (a) Its time, `HoldSeconds`.
   (b) The server's answer for that quest. The client keeps no view reference: each hold keeps a signature of the row given
   at `TryBegin` (`QuestAcceptLatch.Signature`: an int of `CanAccept`, `OwnerAccepted` and `State`, the facts an accept
   depends on; `Gone` for a missing row). In `Refresh`'s `collectionChanged` block, right after `_view = next;` (`:251`),
   the line `_acceptPendingFor = null;` (`:252`) becomes a loop over `0..Capacity-1`: `if (_latch.TryGetHeld(i, out h))
   _latch.Answer(h, QuestAcceptLatch.SignatureOf(Find(h)))`. A hold ends only when its row's signature now differs or the
   row is gone. A new view with that row unchanged does NOT end it: the server replaces the view about once a second while
   a timed quest runs, and ending every hold on every delivery would make the latch useless exactly then. A refusal that
   leaves the row unchanged is not answered: the hold runs on. A row whose `CanAccept` or `State` changes for another
   reason (reach, phase) ends the hold the same way: that window is accepted (Traps).
   (c) `Clear()`: the five other clear points of the journal (`Bind` with a new session `:212`; `Refresh` when the state is
   not InMatch `:222`; on a new `TimelineEpoch` `:229`; when `next == null` `:235`; for an unavailable new view `:242`)
   replace `_acceptPendingFor = null` with `_latch.Clear()`; and `ResetQuestRewardHud` (`MatchHud.QuestRewards.cs:494`)
   calls `_acceptLatch.Clear()` right after `CloseOffer();` (`:497`) at the top of the method, away from the lines
   P-UI-QUEST-ACTION-PROMPT-1 added at its end (`_nearbyCaption = "";` `:511` and the caption write `:512`). A handle is
   seat-scoped and must not outlive its session.
   (d) **The press stamp is beside the hold and the answer does not end it.** Every accept press that begins a hold
   (`TryBegin` returns true) also stamps its handle for `HoldSeconds`. `Answer` ends the hold and leaves the stamp; `Tick`
   ages both; `Clear()` ends both, at the same six points; nothing else does. Stamps share the slots of the holds, so there
   are at most `Capacity` (four): a fifth handle's press takes the slot of the oldest press and ends that press's stamp as
   well as its hold (Abandon of that quest takes presses again at once; accepted). A new accept press for a handle whose
   hold an answer ended reuses its slot and restarts both.
5. **The journal's panel, hold by hold, and the Abandon guard.** The latch touches `RenderSelection` and `PressAbandon` in
   one line each, and nothing in `RenderActions` or `RenderAbandon`. *During a hold:* ACCEPT is shown, reads "ACCEPTING…"
   and is not interactable; the status line reads "Accepting…"; Abandon shows only if the server delivered one, as today.
   *When the server's answer arrives* (the row turns accepted or unacceptable): the same `Refresh` ends the hold (4b), so
   the ACCEPT button's Accepting state ends at once, as before; ACCEPT is hidden and the delivered Abandon appears in its
   rectangle UNARMED (`RenderAbandon` arms only by a press and clears the arm for a quest it was not showing,
   `_abandonQuest`; unchanged). *The guard (4d):* for `HoldSeconds` after an accept press for that quest, answered or not, a
   press on its Abandon returns at once: it neither arms nor sends, and the label stays "Abandon" (the button is drawn and
   interactable as before; nothing is drawn for the refusal). That closes the third click of item 5 above. It is per
   quest: an accept press for 72 blocks nothing of 71. After `HoldSeconds` one press arms and the next sends, as today.
   *When the hold ends with no answer:* the next `Refresh` makes ACCEPT interactable and "ACCEPT QUEST" again; Abandon was
   never shown. A hold that ends arms nothing and disarms nothing (EditMode test 14).
6. **Card.** `Accepting` becomes `_open && _shown.HasValue && _latch.IsHeld(_shown.Value)`. `Open` and `Close` no longer
   touch a hold (it survives Close and shows again on Open; today Close forgets it; accepted). `MarkAccepting()` stays
   public with the body `UpdateAccept()`. `Tick(dt)` loses its `_acceptLeft` block, keeps the grace and the fade, and ends
   with `UpdateAccept()`.
7. **`AcceptOffer`** (`MatchHud.QuestRewards.cs:210`) drops its `card.Accepting` test. After every other check, last
   before the send: `if (!_acceptLatch.TryBegin(card.Shown.Value, QuestAcceptLatch.SignatureOf(row))) return false;` (`row`
   is the `QuestOffer.Find` result it holds), then `QuestAccept`, `MarkAccepting()`. In `TryAcceptFrom` too, `TryBegin` is
   the last check: a hold begun and then refused would block a legitimate press for 2.5 s.
8. **Across the two** (never both on screen: opening the journal closes the card). Journal ACCEPT, close it, F, then Enter
   or the card's button inside the hold: no `QuestAccept`, the card reads "ACCEPTING…". The other order: Enter, J, select:
   the journal's button reads "ACCEPTING…", is not interactable, `TryAccept` returns false. **The dropped command:** an
   accept sent and no view (or one with that row unchanged) after it: presses inside the hold send nothing and do not
   restart it; after 2.5 s both offer the accept again and a press sends a second `QuestAccept` with the same handle.
9. **Enter minor: recorded, not fixed.** One press of Enter CAN do both: the `EventSystem` runs first (`executionOrder:
   -1000`) and its `InputSystemUIInputModule` (made in `MatchHud.Initialize`, `MatchHud.cs:64`) runs `submitHandler` on the
   selected object for `enter` only; then `MatchClient.Update` (`Net/MatchClient.cs:312`) calls `Hud.ReadUiInput`, whose
   Enter branch (`MatchHud.Features.cs:260`) runs `AcceptOffer`. A `HudUi.Button` keeps the selection after a left click, as
   do Tab and C. The new latch makes the doubled Enter harmless: the second `AcceptOffer` is refused by `TryBegin`. No rule
   is added: skipping the card's Enter while a Selectable is selected breaks Enter after a click, deselecting in
   `ReadUiInput` is a frame late, releasing every frame drops Tab and chest focus, and releasing on a left press needs
   `MatchHud.Features.cs` (two packets edit it).

### New file `client-unity/RoguehallaNet/Net/Ui/QuestAcceptLatch.cs` (namespace `Roguehalla.Net.Ui`)

```csharp
/// <summary>The quest accepts sent and not yet answered, per handle, each held for HoldSeconds.</summary>
public sealed class QuestAcceptLatch   // key: Roguehalla.Net.QuestHandle; state: Protocol.QuestRowState
{
    public const float HoldSeconds = 2.5f;
    public const int Capacity = 4;
    public const int Gone = -1;                  // the signature of a row that is not in the view
    public static int Signature(bool canAccept, bool ownerAccepted, QuestRowState state);  // 0 or more
    public static int SignatureOf(QuestRowView row);  // null: Gone; else Signature(CanAccept, OwnerAccepted, State)
    public bool TryBegin(QuestHandle handle, int signature);  // true: hold and stamp start; false while held
    public bool IsHeld(QuestHandle handle);
    public bool PressedRecently(QuestHandle handle);  // the stamp: an accept press less than HoldSeconds ago
    public int Held { get; }                     // holds running (stamps are not counted)
    public bool TryGetHeld(int slot, out QuestHandle handle);  // slot 0..Capacity-1; true when its HOLD runs
    public void Answer(QuestHandle handle, int signature);     // ends the hold (not the stamp) if signature differs
    public void Tick(float dt);
    public void Clear();                         // ends holds and stamps
}
```

Four fixed arrays of `Capacity` (`uint[] _handle`, `float[] _left` the hold, `float[] _pressed` the stamp, `int[]
_signature`); `_left[i] > 0` means slot i's hold runs, `_pressed[i] > 0` that its stamp runs, and `_pressed[i] >= _left[i]`
always (the answer zeroes only `_left`); nothing allocates after construction. `IsHeld` and `PressedRecently` are plain loops
comparing `handle.Value` with `_handle[i]` and testing `_left[i] > 0f` or `_pressed[i] > 0f`; they write nothing.
`Signature` packs `canAccept` as bit 0, `ownerAccepted` as bit 1 and `(int)state << 2`. `TryBegin`: an invalid handle
(`!handle.IsValid`) returns false; a slot that carries the handle with `_left > 0` returns false and changes nothing; else
the slot that carries the handle (its hold ended) is reused, else the slot with the least `_pressed` (a free slot has 0; the
least is the oldest press, as every press starts at `HoldSeconds`) takes the handle; `_left` and `_pressed` become
`HoldSeconds`, the signature is stored, and it returns true (a full table loses its oldest press). `Answer`: a handle with
no running hold, or with an equal signature, changes nothing; else its `_left` is 0 and `_pressed` stays. `Tick`: a `dt` that
is NaN or not above 0 does nothing; else every running stamp and hold loses `dt`, and a `_pressed` at or below 0 becomes 0
together with `_left`. `Clear`: sets `_left[i]` and `_pressed[i]` to 0 for the slots where either is above 0 (nothing held,
nothing written). Numbers: begin 7, `Tick(2.25f)` holds (.25 left), `Tick(.25f)` ends it exactly, begin 7 is true again.
Begin 1, `Tick(1f)`, begin 2, `Tick(1.5f)`: 1 ended (2.5 s old), 2 held (1 left). Full table: begin 1 to 4, each followed by
`Tick(.25f)`; begin 5 takes the slot of 1; `IsHeld(1)` false, 2 to 5 held, `Held` 4. Stamp: begin 7, `Answer(7, s + 1)`:
`IsHeld(7)` false, `PressedRecently(7)` true; `Tick(2.25f)` true; `Tick(.25f)` false. Oldest press: begin 1, `Tick(.25f)`,
begin 2, `Answer(2, s + 1)`, begin 3, 4, 5: `PressedRecently(1)` false, `PressedRecently(2)` true, `IsHeld(3)`, `(4)`, `(5)` true.

### The settled path (trap)

`QuestJournalDetailsEditModeTests` pins that an unchanged `Refresh` writes no rectangle and allocates nothing. The journal
reads the latch there by one call, `_latch.IsHeld(q.Handle)` in the `acceptPending` line, once per `RenderSelection` and
only with a quest selected: a loop over four slots, no write, no closure, no delegate created per call, no string. `Held`,
`TryGetHeld` and `PressedRecently` are never called there: the first two belong to the answer loop (view object changed),
the third to the Abandon click handler. The journal reads the field `_latch` (no property, no LINQ) and writes the button
label and the status line only when `acceptPending` differs from the last drawn value, as today, so a hold running or
ending costs one write at its end. `Clear()` writes only held slots, so a journal with no view writes nothing.

## 9. Validation commands

Through the lane wrapper `.agent-runs/lane-ui-rework/bin/in.sh <worktree> <cmd>`:
- `dotnet test client-unity/RoguehallaNet.Tests --filter QuestAcceptLatchTests`
- `bash client-unity/sync-net.sh` then `bash client-unity/sync-net.sh --check`
- `bash .agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> QuestAcceptLatchEditModeTests,QuestOfferEditModeTests,QuestJournalPolishEditModeTests`
  (and `QuestJournalDetailsEditModeTests,QuestJournalViewEditModeTests,PlayerFlowsEditModeTests`)
- `node --test client-unity/UnityProject/Tools/hud-seams.test.cjs`
- `scripts/gate.sh --quick --oracle packet/P-UI-QUEST-OFFER-1-fix --base lane/ui-rework/base-3`

## 10. Risk and suggested model tier

Sonnet implementer. Two parts are subtle: the answer rule (a new view with the row unchanged must not end a hold) and the
settled-path allocation budget of the journal. The reviewer is a plain `reviewer`: presentation only, no state or wire.

## 11. Known traps

- **The settled path** (above).
- A hold now outlives `Close` and `Open`. Only the journal's `Refresh` (also while hidden; it clears every frame while there
  is no view or no match: allowed) and `ResetQuestRewardHud` end it other than by time; never clear it in `RefreshOffer`,
  which runs every frame. The answer loop runs only when the view object changes.
- **Accepted windows; a reviewer does not call them defects.** (a) A row whose `CanAccept` or `State` changes for another
  reason than the answer (a `CanAccept` flap from leaving reach and returning before the answer arrives) ends the hold early;
  the worst case is one more `QuestAccept`, which the server answers. (b) A fifth press evicts the oldest press, hold and
  stamp. (c) For 2.5 s after an accept press the Abandon of that quest is dead and says nothing: a player who presses it at
  once sees no change.
- `TryAccept(QuestHandle)` keeps its signature. No `Assert.Multiple`, no `InternalsVisibleTo`.
- **Time in EditMode does not advance** (`Time.unscaledTime`, `Time.frameCount` are constant inside a test): a test drives
  time through `MatchHud.TickOffer(dt)` and `QuestAcceptLatch.Tick(dt)`; `OfferFrameDeltaOverride` and the private
  `_offerTickFrame` are the seams for test 5.
- `HudFade` and hidden-built labels: nothing in this packet builds a new label.

## Existing tests that pin this code (all read on the base)

A journal built alone (`Initialize(canvas)`: the View and Polish classes) keeps a private latch nobody ticks: a hold there
ends only by an answer or a clear, and the expectations below hold with that. **Three are amended**, each alone in its own
`oracle-amend:` commit, listed here for the owner:

1. `QuestOfferEditModeTests.EnterAcceptsOnce` (both cases, `Assets/Tests/Editor/QuestOfferEditModeTests.cs:440`; asserts one
   `QuestAccept` for the shown handle, "ACCEPTING…", a non-interactable button, nothing on a second Enter, and after
   `OfferCard.Tick(2.6f)` the caption "ACCEPT · Enter" and an interactable button): `_hud.OfferCard.Tick(2.6f)` becomes
   `_hud.TickOffer(2.6f)`, because the card's `Tick` no longer ages the accept. Kind: a re-statement (same assertions, the
   clock's seam moves). Green on the stub (the stub's `TickOffer` calls `_offerCard.Tick`) and on the implementation.
2. `QuestJournalPolishEditModeTests.ANewQuestViewEndsThePendingState` (`QuestJournalPolishEditModeTests.cs:79`; asserts that
   a pending accept reads "ACCEPTING…" and is not interactable, a new view clears it, a second press sends a second command,
   and an accepted row hides ACCEPT): its second delivery is `Offer(_first)` unchanged (State Open, CanAccept true), which
   no longer ends a hold (4b); it delivers a copy with `State` set to `QuestRowState.Preview`. Kind: a re-statement (same
   assertion lines) that narrows what the test pins; EditMode test 6 pins the other side. Green on the stub.
3. `QuestJournalDetailsEditModeTests.ADoubleClickOnAcceptNeverAbandons` (`QuestJournalDetailsEditModeTests.cs:457`; asserts
   click 1 on ACCEPT sends one `QuestAccept`, the accepted row swaps in Abandon in the same rectangle, click 2 arms
   ("Confirm abandon", no `QuestInteract`), click 3 sends Abandon): a strengthening (the earlier assertions stand). That
   class builds the journal alone, so the commit gives the test a latch it ticks: a field
   `Roguehalla.Net.Ui.QuestAcceptLatch _latch = new Roguehalla.Net.Ui.QuestAcceptLatch();` (fully qualified: no new `using`)
   and `SetUp` calls `_view.Initialize(_canvas.transform, null, null, _latch)`. The test: click 1 on `AcceptQuest`; the
   accepted row and its Abandon are delivered as today; clicks 2, 3 and 4 on `AbandonQuest`: after `FlushInput` one
   `QuestAccept`, no `QuestInteract`, the label "Abandon" after each click (unarmed). Then
   `_latch.Tick(Roguehalla.Net.Ui.QuestAcceptLatch.HoldSeconds)`; one click: "Confirm abandon", no `QuestInteract`; a
   second: `AssertLast(1, 0, ...)` and "Abandon". RED on the stub (no guard), green on the implementation.

No other test of the Details class changes: the four that press Abandon (`AbandonStandsInTheFooterAndTheDeliveredTuplesAreSent`
`:173-180`, `AnUnchangedRefreshWritesNoRectAndAllocatesNothing`, `AbandonIsArmedUntilSomethingChanges` `:487`,
`TheArmedTextFitsTheButton`) press no ACCEPT and call no `TryAccept` (the only `AcceptQuest` press in the class is the amended
test's), so no stamp is set there.

Every other test that presses accept or reads the accepting state stays green, unamended:
- Refused before `TryBegin`, so no hold begins: View `OldAcceptTrackAndRowCallbacksCannotActAfterReplacementBeforeRefresh`
  and `OldSessionCallbacksAndRemovedRowsCannotAcceptAfterRebindWelcomeOrDisconnect` (`QuestJournalViewEditModeTests.cs:341`:
  asserts TryAccept false after rebind, disconnect and resumed Welcome); PlayerFlows
  `PlanningJournalDropAndResumedWelcomeClearRowsAndRestoreMapInput` and
  `MalformedFrameRetainsCanonicalViewButInvalidQuestItemReferenceFailsAndDisablesActions`; Offer
  `TheCardClosesHalfASecondAfterTheOfferLeavesReach` (`QuestOfferEditModeTests.cs:491`; its `OfferCard.Tick(.6f)` is grace,
  which the card keeps).
- One hold begins and nothing in the test ages it: Polish `AcceptCannotBeSentTwice` (`:61`, journal alone: the second press
  stays refused, one `QuestAccept`); PlayerFlows `PlanningPreviewRetiresDeferredAcceptAndWaitsForFreshNativePermission`
  (the HUD's shared latch, "Accepting" read at once, `PlayerFlowsEditModeTests.cs:467`); Offer `AShortLossOfReachKeepsTheCard`
  (`:513`, its Enter is the first begin; `OfferCard.Tick(.2f)` and `(.4f)` age grace only).
- A delivery ends it: View `AcceptUsesCurrentHandleAndFreshCanAcceptThenSessionBatchesOnlyThatIntent`
  (`QuestJournalViewEditModeTests.cs:298`; journal alone; the next delivery revokes `CanAccept`, so the signature differs
  and the answer loop ends the hold; the later refusal is `LastResult`, checked before the latch); Offer
  `AcceptChecksTheCurrentView` (`:758`; its Enter begins one hold; the accepted row it delivers next ends it through the
  journal's hidden `Refresh`; its last Enter meets a closed card).
- No press: Details `AnUnchangedRefreshWritesNoRectAndAllocatesNothing` (the standing guard of the settled path); Offer
  `AnOpenCardAllocatesNothing` (`:978`, the only allocation test that reaches the latch through the card, via `Accepting`).
  The rest of PlayerFlows sends `QuestAccept` through `_session.Input` and never meets the latch.

## Oracle

Test layers (the oracle author's list; the implementer's job is to make them pass, not to edit them):

dotnet `QuestAcceptLatchTests` (NUnit, namespace `Roguehalla.Net.Tests`), handles 1 to 5, 7, 9: 1.
`BeginHoldsAndASecondBeginIsRefused`; 2. `TheHoldEndsAtExactlyHoldSeconds`; 3. `AfterTheHoldABeginIsAllowedAgain`; 4.
`HoldsArePerHandle`; 5. `TheFullTableReplacesTheOldest`; 6. `ClearEndsAll`; 7. `BadInputsAreSafe`; 8.
`AnAnswerEndsTheHoldOnlyWhenTheRowDiffers`; 9. `TheSignatureSeparatesTheFactsAnAcceptDependsOn`; 10.
`TryGetHeldListsTheHeldSlots`; 11. `TheLatchAllocatesNothing`; 12. `TheStampOutlivesTheAnswer`; 13.
`AStampEndsByTimeAndByClearOnly`; 14. `TheFullTableEvictsTheOldestPress`.

EditMode `QuestAcceptLatchEditModeTests` (the rig of `QuestOfferEditModeTests`, copied): 1. `JournalThenCardSendsOneAccept`;
2. `CardThenJournalSendsOneAccept`; 3. `ADroppedAcceptIsOfferedAgainFromTheJournal`; 4.
`ADroppedAcceptIsOfferedAgainFromTheCard`; 5. `TheFrameAgesTheHoldWithTheJournalOpenAndTheCardClosed`; 6.
`AnUnrelatedDeliveryKeepsTheHold`; 7. `ARowThatTurnedAcceptedEndsTheHold`; 8. `ARowThatCanNoLongerBeAcceptedEndsTheHold`; 9.
`AGoneRowEndsTheHold`; 10. `HoldsArePerHandle`; 11. `ASessionChangeEndsTheHolds`; 12. `TheRewardHudResetEndsTheHolds`; 13.
`ASettledRefreshDuringAHoldWritesNothingAndAllocatesNothing`; 14. `AHoldThatEndsNeitherArmsNorDisarmsAbandon`; 15.
`AbandonTakesNoPressInsideTheHoldOfAnAcceptThroughTheCard`; 16. `ACloseOfTheTransportEndsTheHoldsAndStamps`.

## Oracle files (read-only for implementers)

(filled in by the oracle commit)

## Notes from the oracle author

(filled in by the oracle commit)

## NOT ESTABLISHED

- That the Enter double-fire (item 9) happens in a real player: read in package code, not run. That the Submit binding is
  the package's default asset (no `.inputactions` in the project; the default assignment not read).
- How often the server leaves a refused row unchanged, and whether it republishes the quest view on a pure `CanAccept`
  change: not traced in `rh-server`. A row whose `CanAccept` or `State` changes for another reason ends a hold early (worst
  case one more `QuestAccept`).
- The proposed DEV row's wording is the coordinator's.

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->

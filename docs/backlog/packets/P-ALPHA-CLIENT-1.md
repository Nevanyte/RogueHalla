# P-ALPHA-CLIENT-1: friendly connection copy, a reconnect budget that matches the server, a connect timeout

- **Serves:** not in the plan: owner request 2026-11-22 ("multiplayer experience polish, not gameplay", open alpha with 10+ players)
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer (Sonnet): C# only, no wire, no `rh-sim`
- **Integration base:** feature/unity-rework @ 37a2200d (packet branch `packet/P-ALPHA-CLIENT-1`)
- **D-rules touched:** D1 n/a (no legacy mechanic; client presentation and transport policy) · D2 n/a · D5/D6/D9 none (no prediction or sim change)

## 1. ID and outcome

During the open alpha a player who is rejected, loses the network or hits a dead server sees plain sentences, not raw
server reasons or exception text, and a brief drop (a tab in the background, a Wi-Fi blip) is survived instead of
ending the run after ~15 s. Landed means: every `RejectCode` and every terminal failure has fixed player copy produced by
one engine-free type (`ConnectionCopy`); a dropped seat is retried for ~55 s (the server will hold it 60 s after a 30 s
idle timeout, sibling packet P-ALPHA-SERVER-1) with the attempt number and maximum exposed for a "Reconnecting… (3/12)"
banner; a connect or Hello with no Welcome after 10 s ends as Failed with friendly copy. The Unity-side items of section 8
ride along (they cannot be tested with dotnet).

## 2. Dependencies

None to build. The server today holds a seat only `RECONNECT_GRACE` = 10 s (`crates/rh-server/src/room.rs:41`) behind a
10 s `IDLE_TIMEOUT` (`crates/rh-server/src/conn.rs:21`); the 55 s client budget is useful only once P-ALPHA-SERVER-1 raises
them (60 s / 30 s). Landing this first is harmless: a late retry gets `NoSuchSession` (already final, `SessionTests`).
Out of scope (other threads): bot-autofill wait, active-player list (HubLobbyActivityView).

## 3. Relevant legacy paths

No legacy equivalent: legacy used Colyseus' own reconnect (`packages/` has no friendly reject copy to match). Nothing to
port, so no parity claim. Current Roguehalla behaviour that this changes (cited in this tree):
- `client-unity/RoguehallaNet/Net/Session.cs`: `SessionOptions` (`MaxReconnectAttempts = 5`, `ReconnectBaseMs = 500`,
  `ReconnectMaxMs = 8000`), `ScheduleRetry` (`++_attempt > Max` then `Fail("reconnect failed after N attempts: <reason>")`;
  backoff `min(Max, base * 2^(attempt-1))`), `OnClosed` (InMatch with a token starts retrying; `Connecting`/`HelloSent`
  without one fails "closed before welcome: <reason>"), `Poll` (starts an attempt when `Now >= _nextAttemptAt`; an attempt
  whose socket never opens or answers is never timed out: `_attemptInFlight` stays true forever), `TooSlow` reject
  resumes the seat (`ScheduleRetry("too slow")`).
- `client-unity/RoguehallaNet/Protocol/Messages.cs:8`: `RejectCode` 1..11, append-only, an unknown value decodes as itself.
- `UnityProject/Assets/Scripts/UI/MatchHud.cs:268-270` (banner: "Connection interrupted · resuming seat (attempt n)",
  "Join rejected: <reason>", "Connection failed: <error>"); `:309-311` (result message `banner + "\nNo authoritative result received."`).
- `UnityProject/Assets/Scripts/UI/LaunchCopy.cs:64-70` `SessionFailureText` (Rejected appends the raw reason; Failed and
  Ended both read "Couldn't reach the game server"). Called from `MatchHud.Accounts.cs:249`.
- `UnityProject/Assets/Scripts/Net/MatchClient.cs:122` logs `[net] state code reason error` (raw text stays in the log).

Same-tick / boundary facts the implementer must keep: `Detach()` is terminal and must also stop the new timers; `Leave()`
moves InMatch to Ended without a failure kind; `OnClosed` can fire twice for one connection in tests (`FakeTransport.Drop`)
so attempt counting stays in `ScheduleRetry` only; `TooSlow` while InMatch still resumes the seat; `NoSuchSession` on a
resume stays final (Rejected). No RNG, no tick, no wire.

## 4. New paths

- `client-unity/RoguehallaNet/Net/ConnectionCopy.cs` (stub committed; implementer replaces the bodies) and its synced copy
  `client-unity/UnityProject/Assets/RoguehallaNet/Net/ConnectionCopy.cs` plus `.meta`.
- Oracle: `client-unity/RoguehallaNet.Tests/ConnectionCopyTests.cs`, `client-unity/RoguehallaNet.Tests/SessionResilienceTests.cs`.

## 5. Exclusive write scope

- `client-unity/RoguehallaNet/Net/ConnectionCopy.cs`, `client-unity/RoguehallaNet/Net/Session.cs`; then `bash client-unity/sync-net.sh`
  (copies under `client-unity/UnityProject/Assets/RoguehallaNet/Net/`).
- `client-unity/UnityProject/Assets/Scripts/UI/MatchHud.cs`, `UI/MatchHud.Accounts.cs`, `UI/LaunchCopy.cs`,
  `UI/UiDisplayPreferences.cs`, `Net/MatchClient.cs`, `Plugins/WebGL/RhWebSocket.jslib`,
  `client-unity/UnityProject/ProjectSettings/ProjectSettings.asset` (`runInBackground: 1` only).
- A Leave button on the banner, if small, in the existing `UI/` files only; no new wire, no protocol bump.
- Not editable: any oracle file below. `ProjectSettings.asset`: edit only that line. No `Cargo`/`crates/` change.

## 6. Contracts and references

C-INPUT / C-OBS untouched (no input or observation change). `client-unity/AGENTS.md`: edit the library, run `sync-net.sh`;
`sync-net.sh --check` must pass. Unity holds no game rules: copy is presentation. `Session.Error` keeps the raw text
(log only); the player never sees it. `docs/OWNER_DECISIONS.md` has no row on reconnect or copy (searched "reconnect").

## 7. Behaviour to preserve

- Existing tests pin explicit small budgets and must stay green unchanged: `SessionTests.FailedReconnectsBackOffExponentiallyThenGiveUp`
  (`MaxReconnectAttempts = 3`, waits 500/1000/2000 then Failed, `ReconnectBaseMs = 500`); `SeatResumeTests.cs:59`
  (`MaxReconnectAttempts = 2`, `ReconnectMaxMs = 1000`); `DropInMatchReconnectsWithBackoffAndTheResumeToken`;
  `ReconnectRejectedAsNoSuchSessionIsFinal`; `TooSlowInAMatchResumesTheSeatInsteadOfEnding`;
  `ClosedBeforeWelcomeFails` (asserts `Error` contains "refused"). So an explicit `MaxReconnectAttempts` stays a hard cap
  (new default: no attempt cap, the budget decides; explicit value still wins) and `ReconnectMaxMs` stays an option
  (default becomes 5000).
- Wire bytes, `PROTOCOL_VERSION`, the Hello/Resume contents: unchanged.

## 8. Acceptance checks

Oracle (dotnet; red on the stub, to be green):
- `ConnectionCopyTests`: every `RejectCode` (and unknown 999, null/empty reason) maps to non-empty copy with no raw reason,
  code name, "websocket", "1006", "attempts:", "Exception"; the six named examples are exact; every `FailureKind` has
  copy; `LostConnection` exact; `ConnectTimeout`, `Other`, `None` start with "Couldn't reach the game server"; `Ended`
  copy has none of reach/connection/network/error/failed/couldn't; `ForState` routes Rejected/Failed/Ended and returns ""
  for live states.
- `SessionResilienceTests` (default options): retries until a 50..60 s failure with 10..16 attempts, `FailureKind.LostConnection`;
  gaps 500/1000/2000/4000 then capped at 5000; `ReconnectAttempt` counts 1,2,..; `ReconnectAttemptMax` (10..16, constant
  during a drop) equals the attempts really made; a seat back at 30 s resumes with the token and attempt 0; a reconnect
  attempt that never opens times out and the budget still ends (Failed by 80 s, 3+ connects); a connect or a Hello with no
  Welcome fails at exactly 10 s (not 9.95 s) with `ConnectTimeout` and the transport closed; a Welcome stops the clock;
  closed-before-welcome has `FailureKind.ClosedBeforeWelcome`; Leave gives Ended with `FailureKind.None`.
  Pinned shape (the implementer must expose exactly this): `ConnectionCopy.ForReject(RejectCode, string)`,
  `ForFailure(FailureKind)`, `ForState(SessionState, RejectCode?, string, FailureKind)`; `enum FailureKind {None,
  ConnectTimeout, ClosedBeforeWelcome, LostConnection, Other}`; `Session.FailureKind`, `Session.ReconnectAttemptMax`;
  `SessionOptions.ConnectTimeoutMs = 10000`, `ReconnectBudgetMs = 55000`.
  Budget rule the tests assume: attempt k (k-th backoff, cumulative from the drop) is made iff its start is within the
  budget; with the defaults that is 13 attempts, failing at 52.5 s. The connect timeout is measured from `Connect()`.
- `scripts/gate.sh --quick --oracle packet/P-ALPHA-CLIENT-1`; `bash client-unity/sync-net.sh --check`.

Unity-side acceptance for review (no dotnet test possible; not oracle):
- `ProjectSettings.asset` `runInBackground: 1` (now 0, line 86).
- `MatchClient.OnApplicationFocus(true)` (line ~224) and page-visibility return trigger an immediate resume attempt when
  the session is Reconnecting (zero the wait; do not start a second attempt while one is in flight).
- `RhWebSocket.jslib` closes sockets with code 1000 on `pagehide`.
- `UiDisplayPreferences.cs:35` `PlayerPrefs.GetInt(ShowPingKey, 1)` (ping on by default).
- `Debug.Log` of "[aim]" (`MatchClient.cs` ~403) and the timing log (~334) wrapped in `#if UNITY_EDITOR || DEVELOPMENT_BUILD`.
- Banner reads "Reconnecting… (3/12)" from `ReconnectAttempt` / `ReconnectAttemptMax`; Rejected/Failed/Ended banner and
  `LaunchCopy.SessionFailureText` use `ConnectionCopy.ForState`; optional Leave button on the reconnect banner.

**Existing test the packet changes (owner approval needed):** `UnityProject/Assets/Tests/Editor/LaunchScreenEditModeTests.cs:279`
asserts `SessionFailureText(Rejected, "room full")` contains "room full". The new rule is that raw reasons never reach the
player, so this line is amended in the `oracle-amend:` commit to `Does.Not.Contain("room full")` (EditMode, not runnable
with dotnet, unverified here). Line 278 (`Failed` starts with "Couldn't reach the game server") and 280 stay valid by design.
`SessionFailureText` keeps its two-argument signature (add an overload taking the code if wanted).

## Oracle files (read-only for implementers)
- `client-unity/RoguehallaNet.Tests/ConnectionCopyTests.cs`
- `client-unity/RoguehallaNet.Tests/SessionResilienceTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/LaunchScreenEditModeTests.cs` (one amended assertion, own commit)
- `docs/backlog/packets/P-ALPHA-CLIENT-1.md`

## 9. Validation commands

- `dotnet test client-unity/RoguehallaNet.Tests` (whole suite: 2270 tests, 13 red on the stub, all new).
- `dotnet test client-unity/RoguehallaNet.Tests --filter "FullyQualifiedName~ConnectionCopyTests|FullyQualifiedName~SessionResilienceTests"`
- `bash client-unity/sync-net.sh --check`; `scripts/gate.sh --quick --oracle packet/P-ALPHA-CLIENT-1`.
- `scripts/gate.sh --oracle-check` reads Rust test targets only and refuses a branch with no changed crate; it does not
  apply to this C# packet (the gate's own message says to run `--quick` and confirm the red tests by hand: done above).

## 10. Risk and suggested model tier

Sonnet. Small state-machine change in a well-tested class; the risk is timer interplay: the connect clock must not run in
InMatch, the hung-attempt timeout must go through the same `ScheduleRetry` accounting, and `Detach()` must leave no live timer.

## 11. Known traps

- `FakeTransport.Drop` calls `Closed` even while already Reconnecting, which burns an attempt: tests drop only after an
  attempt starts, and the real transports raise `Closed` once per connection.
- The attempts-equal-max test needs the max computed from the same backoff the loop uses (cumulative base*2^(k-1) capped
  at `ReconnectMaxMs`, start within `ReconnectBudgetMs`), not a constant.
- Raw `Session.Error` / `RejectReason` still feed the `[net]` log and must not be removed; only player-facing strings change.
- A hung attempt: close the transport (`_t.Close()`) before retrying so a late `Opened` of the abandoned socket is not
  taken for the new one; real transports ignore events after `Close`.
- Server limits today are 10 s / 10 s (see section 2): do not hard-code 60 s anywhere beyond the 55 s budget option.

## Notes from previous attempts

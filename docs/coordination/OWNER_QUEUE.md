# Owner queue

Decisions waiting for the owner, one line each, with the default the coordinator applies until an answer arrives. The
coordinator edits this file in place: a row leaves when the owner answers (the answer is recorded in
[MIGRATION.md](../MIGRATION.md) and indexed in [OWNER_DECISIONS.md](../OWNER_DECISIONS.md)). To answer, name the row
number in chat: "Q18 yes".

Last updated 2026-11-14 (Q20, Q22 and Q23 answered; Q24 and Q25 wait for the pull request), after the owner's "Please go with your recommendations" closed Q18, Q19 and Q21; earlier the same day he answered Q1 to Q13 and Q15 to Q17 (MIGRATION "Owner answers 2026-11-14").
Q14 was closed by the standing instruction that emptied files stay as placeholders.

## Before the beta host goes up

| # | Question | Default in force | Record |
|---|---|---|---|
| Q26 | Fill bots on the host. Measured on the release commit with the host's flags: with today's fill bots (HumanBot) the seven bots die inside a minute and a lone human's match ends at 41 to 112 s; with the ported Brain (`--bot-brain full`) no match ended within 600 s in 12 runs and the server stayed at 3 to 6 percent of a core. Run the Brain on the host (your decision 8; needs a small `hosting/` edit to pass the flag), or no fill bots, or leave as is? The coordinator recommends the Brain. | The `hosting/` edit is prepared on a branch and not merged; the host is not deployed until this is answered. | MIGRATION "Release checks (2026-11-14)" |
| Q27 | The pattern-kill hook is registered only in the named agent definitions; a general-purpose agent of the coordinator's ran `pkill -f` at 14:47 and most likely terminated background shells of two sessions. Register `.claude/hooks/no-pattern-kill.sh` for every agent in your own `.claude/settings.json` (one PreToolUse entry; the coordinator gives you the text)? | Process-driving work no longer goes through general-purpose agents. | MIGRATION "Release checks (2026-11-14)" |

## At the pull request to `main`

| # | Question | Default in force | Record |
|---|---|---|---|
| Q24 | DEV-001 to DEV-006, DEV-008 and DEV-011 are still "pending", and DEV-007 says "confirm": early privacy and platform differences from legacy (hidden ids not leaked through tie order, id gaps or damage chunks; case-insensitive callsigns; mob separation). Sign them as a set? | They stay pending; nothing in the release depends on the signature. | final combined review, 2026-11-14; DEVIATIONS |
| Q25 | The committed `.claude/settings.json` (the shared one you approved on 2026-11-12: it allows every Bash command and names a path under your home directory) would reach `main` with the pull request and become every contributor's project setting. Intended? | The PR is opened with it; say if it should be trimmed first. | final combined review, 2026-11-14 |

## Deferred by the owner

| # | Item | Until then | Record |
|---|---|---|---|
| Q16 | `hosting/technical-demo/README.md:199` names the old content hash; the next deploy needs protocol 18 and a rebuilt client. Owner 2026-11-14: "we can work on this later when all changes are done". | Untouched; raised again at the deploy step. | MIGRATION "Owner answers 2026-11-14" |

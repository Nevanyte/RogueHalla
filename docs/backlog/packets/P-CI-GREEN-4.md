# P-CI-GREEN-4: the bot private-Brain witness on pull request #65

- **Serves:** CI is the final word (AGENTS.md); the last red `rust` step before `feature/unity-rework` merges into
  `main` (owner approval 2026-11-16: "Yes, both" to the merge and the readability sweep). Follows P-CI-GREEN-3.
- **Tier:** `implementer-sonnet` (tools and a derived fixture; no `crates/` source). Reviewer: `reviewer` (Sonnet).
- **Write scope:** `tools/bot-private-brain-fixtures/**`; the derived files under `parity/policy/bot-private-brain/`
  **only** by the documented command `bash tools/bot-private-brain-fixtures/capture.sh` (no `--check`), and only after
  the cause is shown to be a legitimate input change (below); `docs/evidence/ci-witness-refresh-2026-11-16/` (new);
  this file (hand-off section only).
- **Oracle (already failing, nothing to write):** CI run 43724992331, job `rust`, step "Admitted private, Card and
  Room source witnesses remain byte exact", on `0557e49b5`:
  `bash tools/bot-private-brain-fixtures/capture.sh --check` prints `source.json` matching (31 cases, 855 operations),
  then fails `coverage.json: exact bytes`, actual `c4ea7643d5dd…` against pinned `e7487e049d93…`. The script runs the
  pinned `node:22.23.4` image under `--platform linux/amd64`, so it reproduces on this Mac with Docker.
  The other four `capture.sh --check` lines of that step (card-bleed, bot-private-modules, bot-private-tactics,
  quest-room-feedback) did not run after this failure: run them too; any that is red is in scope on the same terms.

## What to do

1. Reproduce: `bash tools/bot-private-brain-fixtures/capture.sh --check` on this branch. Red with the same actual hash
   is the expected start.
2. **Find the cause before changing anything.** Work out what `coverage.json` is computed from (see `capture.mts`,
   `checks.mts`, `manifest.mjs`) and which input moved since the capture of 2026-11-10 (`5a5a28c6d`). Diff the
   recaptured `coverage.json` (write it to a scratch output via `RH_BOT_BRAIN_OUTPUT` under the repo, not over the
   pinned file) against the pinned one and name the fields that differ and the commit that moved them.
3. Decide by the cause:
   - **Legitimate input change** (a repo file the coverage reads changed for a reviewed reason, e.g. a ported source
     list or a renamed path): recapture with the documented command, and record the cause and the field diff in
     `docs/evidence/ci-witness-refresh-2026-11-16/README.md`.
   - **The tool is nondeterministic or reads something it should not** (time, absolute paths, directory order,
     environment): fix the tool so the pinned bytes come back unchanged, or, if the pinned bytes were themselves
     produced by the flaw, fix the tool and then recapture; say which.
   - **The Rust or legacy behaviour it witnesses changed:** stop, change nothing, and report; that is the coordinator's
     call, not a refresh.
4. Run all five `capture.sh --check` lines of that CI step; all green.

## Must not

- Hand-edit anything under `parity/`. Touch `crates/`, `packages/`, `.github/`, `scripts/gate.sh`.
- Recapture before step 2 has a named cause.

## Done

`scripts/gate.sh --quick --oracle packet/P-CI-GREEN-4` PASS; the five `--check` lines green, their output in the
hand-off; the AGENTS.md hand-off report, with the cause in one paragraph under `NOT ESTABLISHED`/`RISKS` as fits.

## Step 2 hand-off (2026-11-19)

Owner choice 2026-11-19 (decision card in the "Cut delay on other players" thread): "Record reached or not, no counts".

**Cause.** Both `source.json` witnesses were byte-exact; only `coverage.json` moved, and it moved between two runs in
the same container (brain: write `8549e292…`, check of the same tree recomputed a different hash). Field diff against the
pin: V8 hit counts (`144` vs `138`), and functions that report block detail in one run but only a function-level count
in the next (`isBlockCoverage` false vs true; a tsx `__name` helper listed or not). The tool modules are imported before
`Profiler.startPreciseCoverage`, so their functions' block counters depend on when V8 lazily compiles or flushes their
bytecode: the tool was nondeterministic, not an input change.

**Fix.** `reached.mts` (one copy per tool directory) projects each script's coverage onto the merged source ranges whose
innermost covering V8 range ran; counts are dropped. Ledger formats are now `rh-bot-private-{brain,modules}-coverage/2`.
Recaptured with the documented commands, modules first (brain pins modules' tools and outputs), then each `controls`
and `manifest.mjs --write`. Mutation controls unchanged: modules 26/26 killed; brain 23 killed, the same single survivor
(`redundant-first-flag-only`) as before.

**Evidence.** Each witness ran three times (write, check, check again) in the pinned image with identical bytes:
modules `coverage.json` `03ef7834…`, brain `217d4973…`. All five `capture.sh --check` lines of the CI step pass;
`scripts/gate.sh --quick` PASS. Run locally with the image's npm pointed at this session's proxy CA (`--network host`,
`npm_config_cafile`); nothing in the captured bytes depends on that.

**Not established.** That CI's x86_64 runner gives the same bytes: the next CI run is the proof.

# P-GH-1: the repository front page (root tidy, a README per top-level folder, language attributes, PR template)

## ID and outcome

**Serves:** owner direction 2026-11-18 ("Please begin working on it all using sub agents", the answer to the coordinator's
list of ways to make the GitHub repository readable and easy to navigate; follows "Yes, both" of 2026-11-16 and the
readability standard `docs/agent/READABILITY.md`). Not an ORDER_OF_OPERATIONS item: repository hygiene the owner asked for.

**Outcome.** A visitor to the GitHub page sees a root with no stray folders, every top-level folder explained in the root
README and in its own README, a language bar that shows what the project is (Rust and C#, not the legacy TypeScript or
captured HTML), large generated fixtures collapsed in pull-request diffs, and a pull-request template that asks for the
AGENTS.md hand-off report. The root rule becomes part of `scripts/readability_check.py`, so the gate keeps it true.

## Dependencies

None. Branches from the integration branch (`feature/unity-rework` @ `e50ca28cc`, the tree merged to `main` by PR #66).

## Relevant legacy paths

`packages/` is read-only and stays where it is. `Unfinished code/README.md` says its contents are archived legacy-game
history (the Sentenced redesign shipped in `6f06d9d`; "There is no unfinished Sentenced implementation here").

## New paths

- `docs/legacy/saved-work/` (moved from `Unfinished code/`, all six files, content unchanged).
- `docs/agent-prompts/7_DeepSeek_Post_Rust_Development.md` (moved from `DeepSeek_post_rust_development.md`, content
  unchanged; follows the numbering of that folder; update `docs/agent-prompts/README.md`, and
  `docs/agent-prompts/archive-manifest.json` only if it lists that folder's files).
- `crates/README.md`, `deployment/README.md`, `testing/README.md`: short READMEs in the READABILITY.md style (what the folder
  is, what is in it, where to start, what not to touch). `deployment/` is the legacy game's Cloudflare deployment, used by
  `.github/workflows/deploy.yml`; `testing/` is the legacy balance workbench (Python sims and tuning notes), not a test
  suite: say so in its first line.
- `.github/pull_request_template.md`: a short template whose body is the AGENTS.md hand-off report block (every field,
  same order), preceded by one line asking for the packet ID and what the change serves.

## Exclusive write scope

- **oracle-author:** `scripts/test_repo_front_page.py` (new), new test cases for the root rule in
  `scripts/test_readability_check.py` (append only; existing tests unchanged), this file's "Oracle notes" section.
- **implementer:** `scripts/readability_check.py` (the root rule), `scripts/readability-allowlist.txt`,
  `docs/agent/READABILITY.md` (document the root rule in its table), the moves above, the three new READMEs, root
  `README.md` (the repository map), `.gitattributes` (append; keep the existing whitespace lines), 
  `.github/pull_request_template.md` (owner approval 2026-11-18), and references to the two moved paths in
  `docs/legacy/INVENTORY.md`, `docs/agent/REVIEW-2026-11-10.md`, `testing/TUNING_SENTENCED.md`, `docs/agent-prompts/README.md`.
  Not `docs/evidence/**` (captured records; links there stay as they were) and not `packages/`.

## Contracts and references

`docs/agent/READABILITY.md` (rules, allowlist grammar), `scripts/readability_check.py` and its oracle
`scripts/test_readability_check.py`, AGENTS.md "Work and hand-off" (the report block), AGENTS.md protected paths.

## Behaviour to preserve

No source file, fixture, capture or golden vector changes. `.gitattributes` gains only `linguist-*` attributes, which no
build, test or capture tool reads. Moves keep file contents byte for byte (`git log --follow` works).

## Acceptance checks

1. **Root rule in the readability check** (new rule `root`, reported as `root <path>: <message>`):
   - every tracked top-level entry name is free of whitespace;
   - every tracked top-level directory whose name does not start with `.` is named in the root `README.md`, as text
     containing `` `<name>/`` or `](<name>/` (so `` `crates/rh-sim` `` names `crates`);
   - every such directory has a tracked `README.md` directly in it, unless the allowlist has `readme <name>` for it (the
     existing `readme` kind; the stale-entry rule applies to it as to any `readme` line).
   `packages` gets a `readme packages` allowlist line (protected, read-only; the root README describes it).
2. `Unfinished code/` and `DeepSeek_post_rust_development.md` are no longer tracked at the root; their files are tracked at
   the new paths with identical blobs.
3. `git check-attr` reports:
   - `linguist-vendored: set` for `packages/server/src/index.ts` and for any path under
     `client-unity/UnityProject/Assets/Art/ThirdParty/`;
   - `linguist-generated: set` for `parity/card-runtime/source.json` (any `parity/**/*.json`), any path under
     `protocol/fixtures/`, `crates/rh-sim/src/mapgen/fixtures/map-repair.json` (any file under that folder) and
     `client-unity/UnityProject/Assets/Scripts/BakedContent.cs`;
   - `linguist-documentation: set` for any path under `docs/evidence/`;
   - none of the three set for `crates/rh-sim/src/lib.rs`, `client-unity/RoguehallaNet/README.md` or `scripts/gate.sh`.
4. `.github/pull_request_template.md` exists and contains, in order, every field label of the AGENTS.md hand-off report
   block (`PACKET:` … `RISKS:`), read from AGENTS.md by the test, not copied into it.
5. `python3 scripts/readability_check.py` prints `readability: OK (...)` on the finished branch.

## Validation commands

- `python3 -m unittest scripts/test_repo_front_page.py scripts/test_readability_check.py`
- `python3 scripts/readability_check.py`
- `scripts/gate.sh --quick --allow-protected --oracle packet/P-GH-1` (protected: `.github/` and the oracle's edits to the
  gate-run check; owner approval 2026-11-18)

## Risk and suggested model tier

Low. oracle-author (Sonnet), implementer-sonnet, reviewer (Sonnet). No crate code.

## Known traps

- `deployment/`, `testing/` and `maps/` stay at the root: `deployment/cloudflare` is the live legacy deploy path in
  `.github/workflows/deploy.yml` and the root `.gitignore`; `maps/first-playable.json` is read by `rh-server` tests,
  `rh-headless` examples and Unity tools; `testing/` is cited by 68 files, many of them captured records. Do not move them.
- `.codex/`, `.claude/`, `.github/` stay (tool config; dot folders are outside the root rule).
- `git ls-files` lists `Unfinished code/...` with a space: quote paths in shell.
- The readability check's `readme` rule already covers the fixed directories of READABILITY.md; the root rule adds the
  other top-level folders. Do not report the same missing README twice.

## Oracle notes

Oracle files: `scripts/test_repo_front_page.py` (new), `scripts/test_readability_check.py` (class `RootRule`
appended; no existing test changed).

**Conflict for the coordinator and owner (an existing test the root rule breaks).**
`ReadmeRule.test_readme_not_required_in_other_directories` (scripts/test_readability_check.py) builds a root
`README.md` that names nothing, plus top-level `misc/`, `crates/` and `client-unity/` with no READMEs, and asserts
exit 0. Check 1 requires each such directory to be named in the root README and to hold a README, so the test is red
under any implementation of the packet. It needs an `oracle-amend:` commit (give the test a root README that names
`misc/`, `crates/`, `client-unity/` and add the three READMEs, or an allowlist) with the owner's approval. This oracle
leaves it unchanged; a reference implementation made the other 137 pass and left only this test red.

**Readings pinned (each has a test in `RootRule`):**
- The root rule runs only when a root `README.md` is tracked. Most existing tests build repositories with no root
  README and `crates/foo/...` only; this keeps them green (`test_root_rule_is_off_without_a_tracked_root_readme`).
  Whether the whitespace check also runs without a root README is left open (untested).
- Naming: the root README text contains `` `<name>/`` or `](<name>/`; `` `name` `` without a slash, a bare word,
  `` `docs/name/` `` and `` `foo<name>/` `` do not name it.
- Whitespace is judged on the first path component only and reported once per entry, for directories and for files
  (`root my notes.txt: ...`); whitespace deeper down is allowed.
- A non-fixed directory without a README is reported as either `root <dir>: ...` or `readme <dir>/: ...`
  (the tests accept both): it must be in the `readme` set so `readme <name>` allowlisting and the stale rule work. It
  is reported once. An allowlisted missing README never excuses an unnamed directory.
- A fixed directory (e.g. `tools`) missing its README is reported once as `readme tools/: ...`, never also as `root`.
- Top-level files, dot directories and untracked directories are exempt from naming and README.
- Check 3 reading: "any file under" `crates/rh-sim/src/mapgen/fixtures` and `protocol/fixtures` is tested on tracked
  non-`.md` files (the fixtures folder holds a README.md, which stays a document), `parity` on tracked `*.json`,
  `docs/evidence` on every tracked file.
- Check 2 compares blob ids against commit `e0f257a29` (so the branch must contain it) via `git hash-object` of the
  working file, and requires the new paths tracked. The moved-reference test also asserts `Unfinished code/` is gone
  from `docs/legacy/INVENTORY.md` and `testing/TUNING_SENTENCED.md`, the old DeepSeek name from
  `docs/agent/REVIEW-2026-11-10.md`, and the new name present in `docs/agent-prompts/README.md`.
- Check 4: labels are the `^[A-Z][A-Z -]*:` lines of the fenced AGENTS.md block starting `PACKET:`; the template
  needs them in order at line start, and a non-empty text mentioning "packet" before `PACKET:`.

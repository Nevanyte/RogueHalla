# P-STORM-EDITMODE-GPU-1

## ID and outcome

Keep the established null-renderer EditMode suite intact, and run the two GPU readback brush cases once on a graphics-capable batch Editor. On Linux hosts without a display, use `xvfb-run` when installed; otherwise try the installed Editor directly.

## Dependencies

Owner-approved integration repair for the Storm Ink GPU readback EditMode profiles; base `5f48ac74f7300a84703bbc4a75b724c5e62d85d5`.

## Relevant legacy paths (verified with `rg`)

None. This is test-runner infrastructure; it changes no game behavior or rendering code.

## New paths

- `client-unity/UnityProject/Tools/editmode.sh`: standard EditMode invocation.
- `docs/backlog/packets/P-STORM-EDITMODE-GPU-1.md`: scope and proof record.

## Exclusive write scope

Only the two paths above. All test assertions, art, runtime, generated results, and oracle files are outside scope and must remain unchanged.

## Contracts and references

- `client-unity/AGENTS.md`: Unity owns rendering and tests; no game rules.
- `docs/agent/README.md`: every change is scoped to a packet and carries focused validation.

## Behaviour to preserve

Keep the configured/installed Editor selection, project-open and missing-Editor exit code 3, result and log paths, test result parsing, compile-error reporting, failure exit codes, Showcase restoration, and untracked-meta reporting. Keep `-nographics` for the ordinary suite excluding only the two GPU readback case names. Use a second graphics-enabled EditMode invocation matching only those two names. Use `xvfb-run -a` only on Linux when neither `DISPLAY` nor `WAYLAND_DISPLAY` is set and the helper exists; without it invoke the installed Editor directly. Parse both result files, require the graphics result to contain exactly the two expected cases, reject duplicate test names, sum counts, and report failures from both runs. Preserve the Editor selection, result/log paths, compile-error reporting, Showcase restoration, and untracked-meta reporting.

## Acceptance checks

- The prior full null-renderer run fails the two `SnowAndEmberTexturesReadAsLongAsymmetricBrushMarks` readback checks with measured aspect ratio 1.0; the graphics-enabled result passes all four profile checks. Evidence: `/private/tmp/rh-storm-proof/elemental-light-evidence/brush-oracle-final.xml` and `brush-oracle-final-graphics.xml`.
- Run the normal `client-unity/UnityProject/Tools/editmode.sh` entry point. Confirm the headless suite excludes only the two GPU readback cases, the graphics suite contains exactly both expected cases, names are unique across XMLs, and combined counts pass. Verify filter syntax against the installed `com.unity.test-framework` package documentation/source.
- Confirm test source/oracle files are byte-identical to base `5f48ac74f`.

## Validation commands

- `bash client-unity/UnityProject/Tools/editmode.sh`
- `git diff --check`
- `git diff 5f48ac74f -- client-unity/UnityProject/Assets/Tests`

## Risk and suggested model tier

Low. Platform launcher selection could affect test startup. Keep all existing result handling and use the installed Editor; no Unity project or rendering changes.

## Known traps

A null renderer can return a solid rectangle for GPU texture readback, creating false image assertion failures. A graphics API may also promote a nominal depth-buffer request, so keep unrelated tests on the existing headless path. Do not modify tests, profiles, fixtures, or art.

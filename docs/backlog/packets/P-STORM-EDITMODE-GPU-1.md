# P-STORM-EDITMODE-GPU-1

## ID and outcome

Make the standard Unity EditMode entry point use a graphics-capable batch Editor so GPU readback checks exercise a real renderer. On Linux hosts without a display, use `xvfb-run` when installed; and otherwise try the installed Editor directly so its normal diagnostics report any missing graphics device.

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

Keep the configured/installed Editor selection, project-open and missing-Editor exit code 3, result and log paths, test result parsing, compile-error reporting, failure exit codes, Showcase restoration, and untracked-meta reporting. Remove only `-nographics` from the ordinary EditMode command. Use `xvfb-run -a` only on Linux when neither `DISPLAY` nor `WAYLAND_DISPLAY` is set and the helper exists; if `xvfb-run` is absent, invoke the installed Editor directly so its usual failure-report path remains authoritative.

## Acceptance checks

- The old standard entry point's `-nographics` run is known to fail the two `SnowAndEmberTexturesReadAsLongAsymmetricBrushMarks` readback checks with a measured aspect ratio of 1.0; the same assertion file run through a graphics-enabled Editor passes all four cases. Evidence: `/private/tmp/rh-storm-proof/elemental-light-evidence/brush-oracle-final.xml` and `brush-oracle-final-graphics.xml`.
- Run the normal `client-unity/UnityProject/Tools/editmode.sh` entry point in this packet worktree and confirm the full suite executes with graphics and passes.
- Confirm test source/oracle files are byte-identical to base `5f48ac74f`.

## Validation commands

- `bash client-unity/UnityProject/Tools/editmode.sh`
- `git diff --check`
- `git diff 5f48ac74f -- client-unity/UnityProject/Assets/Tests`

## Risk and suggested model tier

Low. Platform launcher selection could affect test startup. Keep all existing result handling and use the installed Editor; no Unity project or rendering changes.

## Known traps

A null renderer can return a solid rectangle for GPU texture readback, creating false image assertion failures. Do not modify tests, profiles, fixtures, or art to accommodate this runner defect. The EditMode invocation needs graphics even though it remains in batch mode.

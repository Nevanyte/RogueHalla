# P-ALPHA-CAPTURE-NOTICE: Tell players about alpha capture before play

- **Serves** Owner-approved open-alpha capture, 2026-11-26
- **Implementer tier** implementer-sonnet
- **Reviewer** reviewer
- **Integration base** `feature/unity-rework` at `5a4fcee9c`
- **D-rules touched** D1 and D2 n/a, D5 owner-approved release communications, D6 and D9 n/a

## 1. ID and outcome

Before a player enters the arena, the Web loader describes the capture enabled for the release. It links to a static,
same-host page that describes the contents, use, and retention of the captured files. An operator uses the release's
actual capture settings to choose the notice text.

## 2. Dependencies

P-ALPHA-CAPTURE defines what the server captures. Match the notice mode to the flags that passed rehearsal.

## 3. Relevant legacy paths

None. The Unity Web loader is the release surface.

## 4. New paths

`hosting/open-alpha/capture-notice.py` generates the loader notice and the static page when packaging.

## 5. Exclusive write scope

The script and this packet. The script operates on the unpublished release's Web directory.

## 6. Contracts and references

C-RNG keeps replay secrets private. The static page describes collection and contains no captured data.

## 7. Behaviour to preserve

Reuse the sealed Unity build assets. Match the server flags to the notice at cutover and rollback.

## 8. Acceptance checks and oracle

An unpacked copy of the sealed Web package produces a notice in every enabled mode. The loader link and standalone copy
agree with the mode, and the Unity startup assets keep their SHA-256 values. A changed template or repeat application fails.

## 9. Validation commands

Run `python3 hosting/open-alpha/capture-notice.py --help`, then run the script against an unpublished copy of the Web
release with `--mode metrics`, `--mode replays`, or `--mode both`. Re-seal the package after generation.

## 10. Risk and suggested model tier

A flag and notice mismatch gives players inaccurate information. Verify both in staging and switch the release and its
notice together.

## 11. Known traps

Edit only a new release directory. For a deployment with both capture streams disabled, use the sealed original loader.

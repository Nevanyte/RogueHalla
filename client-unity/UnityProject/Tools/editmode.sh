#!/usr/bin/env bash
# Runs the Unity EditMode suite in batch mode and prints totals, failed tests (with messages) and compile errors.
#   client-unity/UnityProject/Tools/editmode.sh
# Exit: 0 every test passed; 1 failures or compile errors; 3 unavailable (no Editor installed, or the project is open
# in an Editor), so a caller can report a named skip instead of a pass.
# Editor: $RH_UNITY_EDITOR, else the version in ProjectSettings/ProjectVersion.txt under /Applications/Unity/Hub/Editor.
# In a git worktree without Library/, the Library of the main checkout (first path of `git worktree list`) is cloned
# copy-on-write (cp -cR) so the first run does not reimport everything.
set -euo pipefail

PROJECT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REPO="$(git -C "$PROJECT" rev-parse --show-toplevel)"
unavailable() { echo "editmode: unavailable: $*" >&2; exit 3; }

EDITOR_BIN="${RH_UNITY_EDITOR:-}"
if [[ -z "$EDITOR_BIN" ]]; then
  VERSION="$(sed -n 's/^m_EditorVersion: *//p' "$PROJECT/ProjectSettings/ProjectVersion.txt" | head -1 | tr -d '[:space:]')"
  EDITOR_BIN="/Applications/Unity/Hub/Editor/$VERSION/Unity.app/Contents/MacOS/Unity"
fi
[[ -x "$EDITOR_BIN" ]] || unavailable "no Unity Editor at $EDITOR_BIN (set RH_UNITY_EDITOR)"
[[ ! -e "$PROJECT/Temp/UnityLockfile" ]] || unavailable "the project is open in an Editor (Temp/UnityLockfile exists)"

if [[ ! -d "$PROJECT/Library" ]]; then
  MAIN="$(git -C "$REPO" worktree list --porcelain | sed -n '1s/^worktree //p')"
  if [[ -n "$MAIN" && "$MAIN" != "$REPO" && -d "$MAIN/client-unity/UnityProject/Library" ]]; then
    echo "editmode: cloning Library/ copy-on-write from $MAIN"
    cp -cR "$MAIN/client-unity/UnityProject/Library" "$PROJECT/Library" || { rm -rf "$PROJECT/Library"; echo "editmode: clone failed; Unity will reimport" >&2; }
  fi
fi

# Unity regenerates the font assets under Showcase/ (kerning tables) on an EditMode run. They are under Resources/, so a
# committed copy would reach the player build: every one that was clean before the run is restored after it.
SHOWCASE="client-unity/UnityProject/Assets/Resources/Showcase"
dirty_showcase() { git -C "$REPO" status --porcelain -- "$SHOWCASE" | awk '$1 == "M" { print $2 }' | sort || true; }
SHOWCASE_DIRTY_BEFORE="$(dirty_showcase)"
metas() { git -C "$REPO" ls-files --others --exclude-standard -- client-unity/UnityProject/Assets | grep '\.meta$' | sort || true; }
METAS_BEFORE="$(metas)"

# Unity rejects result paths under a dot-directory, so keep them in a plain temporary directory.
OUT="$(mktemp -d "${TMPDIR:-/tmp}/rh-editmode.XXXXXX")"
HEADLESS_RESULTS="$OUT/headless-results.xml" HEADLESS_LOG="$OUT/headless-editor.log"
GRAPHICS_RESULTS="$OUT/graphics-results.xml" GRAPHICS_LOG="$OUT/graphics-editor.log"
echo "editmode: $EDITOR_BIN"
echo "editmode: headless results $HEADLESS_RESULTS, log $HEADLESS_LOG"
echo "editmode: graphics results $GRAPHICS_RESULTS, log $GRAPHICS_LOG"

# The existing brush-profile cases read pixels back from the GPU. Keep all other
# EditMode cases on their established null renderer; run exactly those two cases
# once on a graphics device to avoid device-specific descriptor differences.
BRUSH_FILTER='^Roguehalla\.Client\.Tests\.AtmosphereBrushProfileEditModeTests\.SnowAndEmberTexturesReadAsLongAsymmetricBrushMarks\(.*\)$'
HEADLESS_FILTER="!$BRUSH_FILTER"
GRAPHICS_FILTER="$BRUSH_FILTER"

HEADLESS_RC=0
"$EDITOR_BIN" -batchmode -nographics -projectPath "$PROJECT" -runTests -testPlatform EditMode \
  -testFilter "$HEADLESS_FILTER" -testResults "$HEADLESS_RESULTS" -logFile "$HEADLESS_LOG" >/dev/null 2>&1 || HEADLESS_RC=$?

GRAPHICS_LAUNCH=("$EDITOR_BIN")
if [[ "$(uname -s)" == "Linux" && -z "${DISPLAY:-}" && -z "${WAYLAND_DISPLAY:-}" ]] && command -v xvfb-run >/dev/null 2>&1; then
  GRAPHICS_LAUNCH=(xvfb-run -a "$EDITOR_BIN")
fi
GRAPHICS_RC=0
"${GRAPHICS_LAUNCH[@]}" -batchmode -projectPath "$PROJECT" -runTests -testPlatform EditMode \
  -testFilter "$GRAPHICS_FILTER" -testResults "$GRAPHICS_RESULTS" -logFile "$GRAPHICS_LOG" >/dev/null 2>&1 || GRAPHICS_RC=$?

# Leave the tree as found.
f=""
while IFS= read -r f; do
  [[ -z "$f" ]] && continue
  git -C "$REPO" checkout -- "$f"
  echo "editmode: restored $(basename "$f")"
done < <(comm -13 <(printf '%s\n' "$SHOWCASE_DIRTY_BEFORE") <(dirty_showcase))
NEW_METAS="$(comm -13 <(printf '%s\n' "$METAS_BEFORE") <(metas) | grep -v '^$' || true)"
if [[ -n "$NEW_METAS" ]]; then
  echo "editmode: untracked .meta files left under Assets/ (track them if they belong to new scripts):"
  sed 's/^/  /' <<<"$NEW_METAS"
fi

CS_ERRORS="$(grep -hE 'error CS[0-9]+' "$HEADLESS_LOG" "$GRAPHICS_LOG" 2>/dev/null | sort -u || true)"
if [[ -n "$CS_ERRORS" ]]; then
  echo "editmode: compile errors:"
  sed 's/^/  /' <<<"$CS_ERRORS"
fi
if [[ ! -s "$HEADLESS_RESULTS" || ! -s "$GRAPHICS_RESULTS" ]]; then
  echo "editmode: FAIL: missing headless or graphics results file (headless exit $HEADLESS_RC, graphics exit $GRAPHICS_RC)"
  for log in "$HEADLESS_LOG" "$GRAPHICS_LOG"; do
    echo "  $log:"
    tail -n 20 "$log" 2>/dev/null | sed 's/^/    /' || true
  done
  exit 1
fi

node - "$HEADLESS_RESULTS" "$GRAPHICS_RESULTS" "$HEADLESS_RC" "$GRAPHICS_RC" <<'JS' || exit 1
const fs = require('fs');
const inputs = process.argv.slice(2, 4);
const codes = process.argv.slice(4).map(Number);
const unesc = (s) => s.replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&apos;/g, "'").replace(/&amp;/g, '&');
const reports = inputs.map((path) => {
  const xml = fs.readFileSync(path, 'utf8');
  const run = /<test-run\b[^>]*>/.exec(xml);
  const attr = (tag, name) => { const m = new RegExp('\\b' + name + '="([^"]*)"').exec(tag || ''); return m ? m[1] : '0'; };
  const cases = xml.match(/<test-case\b[\s\S]*?<\/test-case>/g) || [];
  const names = cases.map((c) => {
    const head = /<test-case\b[^>]*>/.exec(c)[0];
    return { name: unesc(attr(head, 'fullname')), result: attr(head, 'result'), message: c };
  });
  return { total: +attr(run && run[0], 'total'), passed: +attr(run && run[0], 'passed'), failed: +attr(run && run[0], 'failed'), names };
});
const [headless, graphics] = reports;
const combined = reports.reduce((a, r) => ({ total: a.total + r.total, passed: a.passed + r.passed, failed: a.failed + r.failed }), { total: 0, passed: 0, failed: 0 });
console.log(`editmode: total ${combined.total}, passed ${combined.passed}, failed ${combined.failed} (headless ${headless.total}; graphics ${graphics.total})`);
const all = reports.flatMap((r) => r.names);
const seen = new Set();
let unique = true;
for (const test of all) {
  if (seen.has(test.name)) { console.log(`  DUPLICATE ${test.name}`); unique = false; }
  seen.add(test.name);
  if (test.result !== 'Failed') continue;
  const msg = /<message>\s*(?:<!\[CDATA\[([\s\S]*?)\]\]>|([\s\S]*?))\s*<\/message>/.exec(test.message);
  console.log(`  FAILED ${unesc(test.name)}`);
  if (msg) console.log(unesc(msg[1] !== undefined ? msg[1] : msg[2]).trim().split('\n').map((l) => '    ' + l).join('\n'));
}
const filterExpected = /^Roguehalla\.Client\.Tests\.AtmosphereBrushProfileEditModeTests\.SnowAndEmberTexturesReadAsLongAsymmetricBrushMarks\("(?:flake|ember)"\)$/;
const graphicsExpected = graphics.names.length === 2 && graphics.names.every((t) => filterExpected.test(t.name));
const passed = unique && seen.size === combined.total && headless.total > 0 && graphicsExpected &&
  combined.failed === 0 && combined.passed === combined.total && codes.every((code) => code === 0);
if (!graphicsExpected) console.log('  FAIL graphics result did not contain exactly the two brush readback cases');
if (!unique || seen.size !== combined.total) console.log('  FAIL test names are not unique across the two runs');
process.exit(passed ? 0 : 1);
JS
echo "editmode: PASS"

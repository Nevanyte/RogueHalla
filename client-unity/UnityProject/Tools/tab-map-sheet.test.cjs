'use strict';
// P-UI-TABMAP-SHEET-1: the capture tools' copy of the wide Tab map layout follows MapScreenLayout (sheet + header), and one place
// in the map controller writes WorldLabelVisibility.Hidden. The source is read as text; nothing here needs Unity.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const toolsDir = __dirname;
const uiDir = path.resolve(__dirname, '../Assets/Scripts/UI');

// The text of `function tabMapRect(...) { ... }`: from its header to the closing brace at the start of a line.
function extractTabMapRect(file) {
  const text = fs.readFileSync(path.join(toolsDir, file), 'utf8');
  const start = text.indexOf('function tabMapRect');
  assert.ok(start >= 0, file + ' has a function tabMapRect');
  const end = text.indexOf('\n}', start);
  assert.ok(end > start, file + ': tabMapRect ends with a closing brace on its own line');
  return text.slice(start, end + 2);
}

const near = (actual, expected, what) => assert.ok(Math.abs(actual - expected) <= 0.01, `${what}: ${actual} is not ${expected}`);

for (const file of ['hud-shots.cjs', 'vision-shots.cjs']) {
  test(`${file}: tabMapRect is the sheet layout's Map rect`, () => {
    const tabMapRect = new Function(`${extractTabMapRect(file)}\nreturn tabMapRect;`)();
    // MapScreenLayout Map rects (399, 106, 624) and (290, 148, 700) in stage units, times the scale.
    const a = tabMapRect(1920, 1080, 1.35);
    near(a.x, 538.65, '1920x1080 x'); near(a.y, 143.1, '1920x1080 y'); near(a.size, 842.4, '1920x1080 size');
    const b = tabMapRect(1024, 768, 0.8);
    near(b.x, 232, '1024x768 x'); near(b.y, 118.4, '1024x768 y'); near(b.size, 560, '1024x768 size');
  });
}

test('one place writes WorldLabelVisibility.Hidden in the map controller', () => {
  let count = 0;
  for (const name of ['TacticalMapController.cs', 'TacticalMapController.Sheet.cs']) {
    const file = path.join(uiDir, name);
    if (!fs.existsSync(file)) continue;
    const code = fs.readFileSync(file, 'utf8').split('\n').map((l) => l.replace(/\/\/.*$/, '')).join('\n');
    count += (code.match(/WorldLabelVisibility\.Hidden\s*=(?!=)/g) || []).length;
  }
  assert.equal(count, 1, 'exactly one assignment to WorldLabelVisibility.Hidden (the panel decision)');
});

'use strict';
// P-UI-HUD-SEAMS-1: MatchHud's top-centre, action-bar, pulse, notice and visibility code each live in their own
// partial file. The source is read as text; nothing here needs Unity.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const uiDir = path.resolve(__dirname, '../Assets/Scripts/UI');
const read = (name) => fs.readFileSync(path.join(uiDir, name), 'utf8');
const exists = (name) => fs.existsSync(path.join(uiDir, name));
// Declarations only: drop `//` comments (doc comments included) so prose never matches.
const code = (name) => read(name).split('\n').map((l) => l.replace(/\/\/.*$/, '')).join('\n');

const OLD = ['MatchHud.Legacy.cs', 'MatchHud.cs', 'MatchHud.Features.cs', 'MatchHud.PlayerFlows.cs'];

const method = (type, name) => new RegExp(`\\b${type}\\s+${name}\\s*\\(`);
const constant = (name) => new RegExp(`\\bconst\\s+float[^;]*\\b${name}\\b`);

// file -> [label, regex of the declaration]
const MOVED = {
  'MatchHud.TopCentre.cs': [
    ['CreateTopAndToastPills', method('void', 'CreateTopAndToastPills')],
    ['SyncTopCentre', method('void', 'SyncTopCentre')],
    ['PlacePhasePill', method('void', 'PlacePhasePill')],
    ['ApplyPhaseStrip', method('void', 'ApplyPhaseStrip')],
    ['BannerTop', constant('BannerTop')],
  ],
  'MatchHud.ActionBar.cs': [
    ['ApplyWishRow', method('void', 'ApplyWishRow')],
    ['CreateWeaponBar', method('void', 'CreateWeaponBar')],
    ['CreateWishTile', method('void', 'CreateWishTile')],
    ['CreateConsumables', method('void', 'CreateConsumables')],
    ['CreatePickupHint', method('void', 'CreatePickupHint')],
    ['RefreshWishTile', method('void', 'RefreshWishTile')],
    ['TickWishTileMotion', method('void', 'TickWishTileMotion')],
    ['RefreshCooldownSweep', method('void', 'RefreshCooldownSweep')],
    ['RowSlot', method('Rect2', 'RowSlot')],
    ['struct Rect2', /\bstruct\s+Rect2\b/],
    ['WeaponBarWidth', /\bfloat\s+WeaponBarWidth\b/],
    ['BarBottom', constant('BarBottom')],
    ['SlotWidth', constant('SlotWidth')],
    ['SlotHeight', constant('SlotHeight')],
    ['SwapWidth', constant('SwapWidth')],
    ['WishSize', constant('WishSize')],
    ['BarGap', constant('BarGap')],
    ['ConsumablesHeight', constant('ConsumablesHeight')],
  ],
  'MatchHud.Pulses.cs': [
    ['InputCue', method('void', 'InputCue')],
    ['Pulse', method('void', 'Pulse')],
    ['TickPulses', method('void', 'TickPulses')],
    ['ClearPulses', method('void', 'ClearPulses')],
    ['_pulses', /\b_pulses\s*=\s*new\b/],
    ['PulseSeconds', constant('PulseSeconds')],
    ['PulsePop', constant('PulsePop')],
  ],
  'MatchHud.Notices.cs': [
    ['RotateNoticeEvents', method('void', 'RotateNoticeEvents')],
    ['_boundNoticeEvents', /\bAction<ServerMsg\.Events>\s+_boundNoticeEvents\s*;/],
    ['_orderedNotices', /\bOrderedNoticeFeed\s+_orderedNotices\s*;/],
    ['CreateNoticeFeed', method('void', 'CreateNoticeFeed')],
    ['TickNotices', method('void', 'TickNotices')],
  ],
  'MatchHud.Visibility.cs': [
    ['RefreshHudElementVisibility', method('void', 'RefreshHudElementVisibility')],
    ['SetActiveIfChanged', method('void', 'SetActiveIfChanged')],
    ['WorldHudVisible', /\bbool\s+WorldHudVisible\b/],
    ['_worldHudVisible', /\bbool\s+_worldHudVisible\b/],
    ['_worldHudFocus', /\bGameObject\s+_worldHudFocus\s*;/],
    ['RefreshWorldHudVisibility', method('void', 'RefreshWorldHudVisibility')],
    ['RememberWorldHudFocus', method('void', 'RememberWorldHudFocus')],
    ['RefreshHeaderActionsVisibility', method('void', 'RefreshHeaderActionsVisibility')],
  ],
};

const requireFile = (file) => assert.ok(exists(file), `${file} must exist under Assets/Scripts/UI`);

for (const file of Object.keys(MOVED)) {
  test(`${file} exists, is a MatchHud partial with a summary, and is at most 800 lines`, () => {
    requireFile(file);
    const text = read(file);
    const lines = text.split('\n');
    const typeLine = lines.findIndex((l) => /\bpartial\s+class\s+MatchHud\b/.test(l));
    assert.ok(typeLine >= 0, `${file} declares partial class MatchHud`);
    assert.match(lines[typeLine], /public\s+sealed\s+partial\s+class\s+MatchHud\b/, 'same modifiers as the other files');
    // A /// <summary> sits directly above the type line (the first type of the file).
    const above = lines.slice(0, typeLine).join('\n');
    assert.match(above, /\/\/\/\s*<summary>/, `${file} has a /// <summary> before the type`);
    assert.ok(lines.length <= 800, `${file} has ${lines.length} lines, limit 800`);
  });

  test(`${file} declares each member that moved into it`, () => {
    requireFile(file);
    const text = code(file);
    for (const [label, re] of MOVED[file]) assert.match(text, re, `${file} declares ${label}`);
  });

  test(`the four old MatchHud files no longer declare what moved into ${file}`, () => {
    requireFile(file);
    for (const old of OLD) {
      const text = code(old);
      for (const [label, re] of MOVED[file]) assert.doesNotMatch(text, re, `${old} still declares ${label}`);
    }
  });
}

test('PlayerFlows calls the two extracted notice methods from the old places', () => {
  requireFile('MatchHud.Notices.cs');
  const flows = code('MatchHud.PlayerFlows.cs');
  const init = flows.slice(flows.indexOf('void InitializePlayerFlows'), flows.indexOf('void AddPlayerFlowTabs'));
  assert.match(init, /\bCreateNoticeFeed\(\);/, 'InitializePlayerFlows calls CreateNoticeFeed()');
  assert.ok(init.indexOf('CreateNoticeFeed();') > init.indexOf('_traderDialog.Initialize'), 'after the trader dialog');
  assert.ok(init.indexOf('CreateNoticeFeed();') < init.indexOf('AddPlayerFlowTabs(_questJournal.Root)'), 'before the tabs');
  assert.doesNotMatch(init, /AddComponent<OrderedNoticeFeed>/, 'the feed is no longer created inline');
  const tick = flows.slice(flows.indexOf('void TickPlayerFlows'), flows.indexOf('void RefreshJournalLauncher'));
  assert.match(tick, /\bTickNotices\(\);/, 'TickPlayerFlows calls TickNotices()');
  assert.doesNotMatch(tick, /SetRecentVisible/, 'the recent card is no longer toggled inline');
  const notices = code('MatchHud.Notices.cs');
  const create = notices.slice(notices.indexOf('void CreateNoticeFeed'));
  assert.match(create, /AddComponent<OrderedNoticeFeed>\(\)/);
  assert.match(create, /_orderedNotices\.Initialize\(_root, ClosePlayerFlows\)/);
});

test('no method name is declared in two MatchHud*.cs files', () => {
  for (const file of Object.keys(MOVED)) requireFile(file);
  const decl = /^ {8}(?:(?:internal|public|private|protected|static|override)\s+)*(?:void|bool|float|int|uint|double|string|Rect2|Transform|RectTransform)\s+(\w+)\s*\(/;
  const seen = new Map();
  const files = fs.readdirSync(uiDir).filter((f) => /^MatchHud(\..+)?\.cs$/.test(f));
  assert.ok(files.length >= 21, `expected the 16 old and 5 new MatchHud files, found ${files.length}`);
  for (const file of files) {
    for (const line of code(file).split('\n')) {
      const m = decl.exec(line);
      if (!m) continue;
      const list = seen.get(m[1]) || [];
      list.push(file);
      seen.set(m[1], list);
    }
  }
  const dup = [...seen].filter(([, list]) => list.length > 1).map(([name, list]) => `${name}: ${list.join(', ')}`);
  assert.deepEqual(dup, [], 'a method name is declared in more than one file');
});

test('the old files shrank: MatchHud.Legacy.cs under 420 lines, MatchHud.cs under 600', () => {
  for (const file of Object.keys(MOVED)) requireFile(file);
  const count = (f) => read(f).split('\n').length;
  assert.ok(count('MatchHud.Legacy.cs') < 420, `MatchHud.Legacy.cs has ${count('MatchHud.Legacy.cs')} lines`);
  assert.ok(count('MatchHud.cs') < 600, `MatchHud.cs has ${count('MatchHud.cs')} lines`);
});

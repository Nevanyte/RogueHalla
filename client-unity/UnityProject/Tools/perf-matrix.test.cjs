'use strict';
// P-UI-PERF-WORST-FRAME-1: the pure functions of perf-matrix.cjs that read the recorder's worst frames and the page's rAF stamps.
const test = require('node:test');
const assert = require('node:assert/strict');
const M = require('./perf-matrix.cjs');

const NAMES = ['frame', 'preUpdate', 'scripts', 'render', 'uiRebuild', 'netPoll', 'hud', 'camera', 'present', 'actors', 'collections', 'surfaceFx', 'vision', 'props',
  'wishFx', 'effects', 'atmosphere', 'soundscape', 'mapUi', 'hudAccount', 'hudFlows', 'hudMap', 'hudEdge', 'hudDrawer', 'hudOwner', 'fxCues'];
const CTX0 = { actors: 0, walls: 0, wallsChanged: 0, walkMapChanged: 0, terrainMapChanged: 0, stateFrames: 0, snapshots: 0, dead: 0, orderPending: 0, mapTiles: 0 };

/** A recorder row. `ms` and `bytes` are given by section name; the rest has defaults. */
function row(o) {
  const ms = Array(NAMES.length).fill(0), bytes = Array(NAMES.length).fill(0);
  for (const [k, v] of Object.entries(o.ms || {})) ms[NAMES.indexOf(k)] = v;
  for (const [k, v] of Object.entries(o.bytes || {})) bytes[NAMES.indexOf(k)] = v;
  return { i: o.i, t: o.t, ms, bytes, gc: o.gc || [10, 10, 10, 10, 10], ctx: Object.assign({}, CTX0, o.ctx || {}) };
}
/** A property of a result that may be missing: undefined instead of a TypeError, so the assertion that follows is the one that fails. */
const P = (v, k) => (v === null || v === undefined ? undefined : v[k]);
const slot = (section, maxMs, before, at, after) => ({ section, maxMs, before, at, after });
const unityOf = (frame, actors, render) => ({ frames: 100, seconds: 8, worstFrames: { sections: NAMES, slots: { frame, actors, render }, gc: {}, context: {} } });

// The fixture of the spec: a 69.4 ms frame whose time is mostly actors.
const LONG_MS = { frame: 69.4, preUpdate: 1.6, scripts: 59.0, render: 8.0, netPoll: 1.0, hud: 1.0, camera: 0.4, present: 56.0, actors: 54.8 };
const longRow = () => row({ i: 1523, t: 7.512, ms: LONG_MS, bytes: { actors: 2000000 }, gc: [10, 10, 10, 10, 10], ctx: { actors: 31, walls: 2, stateFrames: 1, snapshots: 2, orderPending: 1 } });
const beforeRow = () => row({ i: 1522, t: 7.45, ms: { frame: 6 }, gc: [9, 9, 9, 9, 9], ctx: { actors: 30 } });
const PAGE = { index: 3, ms: 57.2, startSeconds: 7.4, endSeconds: 7.457 };
const FULL = 'worst frame #1523 at 7.51 s: 69.4 ms; actors 54.8 ms 1953 kB, render 8.0 ms 0 kB, preUpdate 1.6 ms 0 kB; gc in frame +0, just before +1; '
  + 'actors 31 (before 30); walls 2 same; walk map same; terrain same; state frames 1; snapshots 2; dead no; order pending yes; page interval 57.2 ms by time';

test('the five old exports exist', () => {
  for (const n of ['budget', 'summarise', 'readyPoint', 'minimapRect', 'legacyScale']) assert.equal(typeof M[n], 'function', n);
});

test('worstPageFrame finds the longest interval and its neighbours', () => {
  assert.equal(typeof M.worstPageFrame, 'function');
  assert.equal(M.worstPageFrame([], 1000), null);
  assert.equal(M.worstPageFrame([1000], 1000), null);
  const stamps = [1000, 1016, 1032, 1100, 1116, 1133];
  assert.deepEqual(M.worstPageFrame(stamps, 1000), { deltaIndex: 2, ms: 68, endSeconds: 0.1, beforeMs: [16, 16], afterMs: [16, 17] });
  assert.equal(P(M.worstPageFrame(stamps, 990), 'endSeconds'), 0.11);
  const last = M.worstPageFrame([0, 10, 20, 30, 90], 1000);
  assert.equal(P(last, 'deltaIndex'), 3); assert.equal(P(last, 'ms'), 60); assert.deepEqual(P(last, 'afterMs'), []); assert.deepEqual(P(last, 'beforeMs'), [10, 10]);
  const first = M.worstPageFrame([0, 50, 60, 70], 1000);
  assert.equal(P(first, 'deltaIndex'), 0); assert.equal(P(first, 'ms'), 50); assert.deepEqual(P(first, 'beforeMs'), []); assert.deepEqual(P(first, 'afterMs'), [10, 10]);
  const tie = M.worstPageFrame([0, 20, 40, 50], 1000);
  assert.equal(P(tie, 'deltaIndex'), 0, 'a tie keeps the earlier interval'); assert.equal(P(tie, 'ms'), 20);
  const one = M.worstPageFrame([1000, 1016], 1000);
  assert.equal(P(one, 'deltaIndex'), 0); assert.equal(P(one, 'ms'), 16); assert.equal(P(one, 'endSeconds'), 0.016); assert.deepEqual(P(one, 'beforeMs'), []); assert.deepEqual(P(one, 'afterMs'), []);
});

test('worstPageFrame ignores a non-finite stamp and keeps its neighbours in place', () => {
  assert.equal(typeof M.worstPageFrame, 'function');
  assert.equal(M.worstPageFrame([NaN, 5], 0), null, 'no finite delta');
  assert.deepEqual(M.worstPageFrame([0, 10, NaN, 30, 90, 100], 0), { deltaIndex: 3, ms: 60, endSeconds: 0.09, beforeMs: [null, null], afterMs: [10] });
  assert.equal(P(M.worstPageFrame([0, 10, NaN, 30, 90, 100], NaN), 'endSeconds'), null, 'a resetAt that is not finite gives a null end');
});

test('worstPageFrame ms equals the maximum budget() reports', () => {
  assert.equal(typeof M.worstPageFrame, 'function');
  const stamps = [1000, 1016, 1032, 1100, 1116, 1133];
  const deltas = []; for (let i = 1; i < stamps.length; i++) deltas.push(stamps[i] - stamps[i - 1]);
  const w = M.worstPageFrame(stamps, 1000);
  assert.ok(w, 'a result');
  assert.equal(P(w, 'ms'), M.budget(deltas).maxMs);
  assert.equal(deltas[P(w, 'deltaIndex')], P(w, 'ms'), 'deltaIndex indexes the deltas list');
});

test('pageIntervalAt gives the interval holding a time, a boundary going to the interval that ends there', () => {
  assert.equal(typeof M.pageIntervalAt, 'function');
  const s = [1000, 1016, 1032, 1100];   // at .01, .026, .042, .11 s after resetAt 990
  assert.deepEqual(M.pageIntervalAt(s, 990, 0.03), { index: 1, ms: 16, startSeconds: 0.026, endSeconds: 0.042 });
  assert.equal(P(M.pageIntervalAt(s, 990, 0.026), 'index'), 0, 'a boundary belongs to the interval that ends there');
  assert.equal(P(M.pageIntervalAt(s, 990, 0.11), 'index'), 2);
  assert.equal(P(M.pageIntervalAt(s, 990, 0.11), 'ms'), 68);
});

test('pageIntervalAt gives null outside the stamps and for bad input', () => {
  assert.equal(typeof M.pageIntervalAt, 'function');
  const s = [1000, 1016, 1032, 1100];
  assert.equal(M.pageIntervalAt(s, 990, 0.005), null, 'before the first stamp');
  assert.equal(M.pageIntervalAt(s, 990, 0.01), null, 'at the first stamp');
  assert.equal(M.pageIntervalAt(s, 990, 0.111), null, 'after the last stamp');
  assert.equal(M.pageIntervalAt([], 990, 0.03), null);
  assert.equal(M.pageIntervalAt([1000], 990, 0.03), null);
  assert.equal(M.pageIntervalAt(s, 990, NaN), null);
  assert.equal(M.pageIntervalAt(s, 990, Infinity), null);
});

test('pageIntervalAt skips an interval with a non-finite stamp', () => {
  assert.equal(typeof M.pageIntervalAt, 'function');
  const s = [1000, 1016, NaN, 1100, 1116];
  assert.equal(P(M.pageIntervalAt(s, 990, 0.02), 'index'), 0);
  assert.equal(M.pageIntervalAt(s, 990, 0.05), null, 'the two intervals at the NaN stamp are never chosen');
  assert.deepEqual(M.pageIntervalAt(s, 990, 0.12), { index: 3, ms: 16, startSeconds: 0.11, endSeconds: 0.126 });
});

test('slotPageIntervals picks the longest page interval overlapping the frame span', () => {
  assert.equal(typeof M.slotPageIntervals, 'function');
  const s = [1000, 1016, 1032, 1100];
  const at = (t, frameMs) => row({ i: 7, t, ms: { frame: frameMs } });
  const u = r => unityOf(slot('frame', 1, null, r, null), slot('actors', 1, null, r, null), slot('render', 1, null, r, null));
  const short = M.slotPageIntervals(u(at(0.03, 2)), s, 990);   // span .028 to .03: only interval 1
  assert.deepEqual(P(short, 'frame'), { index: 1, ms: 16, startSeconds: 0.026, endSeconds: 0.042 });
  assert.deepEqual(P(short, 'actors'), P(short, 'frame')); assert.deepEqual(P(short, 'render'), P(short, 'frame'));
  const wide = M.slotPageIntervals(u(at(0.03, 5)), s, 990);    // span .025 to .03 overlaps intervals 0 and 1, both 16 ms
  assert.equal(P(P(wide, 'frame'), 'index'), 0, 'a tie keeps the earlier interval');
  assert.equal(P(P(wide, 'frame'), 'ms'), 16);
  const none = M.slotPageIntervals(u(at(0.005, 2)), s, 990);
  assert.deepEqual(none, { frame: null, actors: null, render: null });
});

test('slotPageIntervals is independent per slot and null for a slot without a frame or a record', () => {
  assert.equal(typeof M.slotPageIntervals, 'function');
  const s = [1000, 1016, 1032, 1100];
  const r = row({ i: 7, t: 0.03, ms: { frame: 2 } });
  const mixed = M.slotPageIntervals(unityOf(slot('frame', 0, null, null, null), slot('actors', 1, null, r, null), slot('render', 0, null, null, null)), s, 990);
  assert.equal(P(mixed, 'frame'), null); assert.equal(P(mixed, 'render'), null); assert.equal(P(P(mixed, 'actors'), 'index'), 1);
  const old = { frame: null, actors: null, render: null };
  assert.deepEqual(M.slotPageIntervals(null, s, 990), old);
  assert.deepEqual(M.slotPageIntervals({ frames: 3 }, s, 990), old);
  assert.deepEqual(M.slotPageIntervals({ error: 'x' }, s, 990), old);
  const bad = row({ i: 7, t: NaN, ms: { frame: 2 } });
  assert.deepEqual(M.slotPageIntervals(unityOf(slot('frame', 1, null, bad, null), slot('actors', 1, null, bad, null), slot('render', 1, null, bad, null)), s, 990), old);
});

test('slotPageIntervals: a frame ms that is not above 0 or not finite makes the span the instant the frame ended', () => {
  assert.equal(typeof M.slotPageIntervals, 'function');
  const s = [1000, 1016, 1032, 1100];
  const one = (t, frameMs) => { const r = row({ i: 7, t, ms: { frame: frameMs } }); return P(M.slotPageIntervals(unityOf(slot('frame', 1, null, r, null), slot('actors', 1, null, r, null), slot('render', 1, null, r, null)), s, 990), 'frame'); };
  assert.equal(P(one(0.03, 0), 'index'), 1);
  assert.equal(P(one(0.03, NaN), 'index'), 1);
  assert.equal(P(one(0.03, -3), 'index'), 1);
  assert.equal(P(one(0.026, 0), 'index'), 0, 'as pageIntervalAt: a boundary belongs to the interval that ends there');
  assert.equal(one(0.005, 0), null);
});

test('slotPageIntervals: the long frame is matched by overlap, not by its end time', () => {
  assert.equal(typeof M.slotPageIntervals, 'function');
  const s = [1000, 1016, 1032, 1100, 1104, 1108];   // starts .01 .026 .042 .11 .114 .118 s after resetAt 990
  const r = row({ i: 1523, t: 0.112, ms: { frame: 69.4 } });   // span .0426 to .112
  const u = unityOf(slot('frame', 69.4, null, r, null), slot('actors', 69.4, null, r, null), slot('render', 69.4, null, r, null));
  assert.deepEqual(P(M.slotPageIntervals(u, s, 990), 'frame'), { index: 2, ms: 68, startSeconds: 0.042, endSeconds: 0.11 });
  assert.equal(P(M.pageIntervalAt(s, 990, 0.112), 'index'), 3, 'the time alone would pick the next interval');
});

test('clockCheckLine says something only when the clocks differ by more than 5 ms', () => {
  assert.equal(typeof M.clockCheckLine, 'function');
  assert.equal(M.clockCheckLine({ pageSeconds: 9, unitySeconds: 8.9 }), 'clock check: page minus recorder +100.0 ms');
  assert.equal(M.clockCheckLine({ pageSeconds: 8, unitySeconds: 8.006 }), 'clock check: page minus recorder -6.0 ms');
  assert.equal(M.clockCheckLine({ pageSeconds: 8, unitySeconds: 8.003 }), null);
  assert.equal(M.clockCheckLine(null), null);
  assert.equal(M.clockCheckLine({ pageSeconds: NaN, unitySeconds: 8 }), null);
  assert.equal(M.clockCheckLine({ pageSeconds: 8, unitySeconds: Infinity }), null);
});

test('describeWorstFrame prints the worst frame as one line', () => {
  assert.equal(typeof M.describeWorstFrame, 'function');
  const u = unityOf(slot('frame', 69.4, beforeRow(), longRow(), null), slot('actors', 69.4, null, longRow(), null), slot('render', 8, null, longRow(), null));
  assert.equal(M.describeWorstFrame(u, 'frame', PAGE), FULL);
});

test('describeWorstFrame: no frame before it, and no page interval', () => {
  assert.equal(typeof M.describeWorstFrame, 'function');
  const u = unityOf(slot('frame', 69.4, null, longRow(), null), slot('actors', 69.4, null, longRow(), null), slot('render', 8, null, longRow(), null));
  assert.equal(M.describeWorstFrame(u, 'frame', null),
    FULL.replace('just before +1', 'just before n/a').replace('actors 31 (before 30)', 'actors 31 (before n/a)').replace('page interval 57.2 ms by time', 'page interval n/a'));
});

test('describeWorstFrame: a slot other than frame is labelled and reads its own row', () => {
  assert.equal(typeof M.describeWorstFrame, 'function');
  const own = row({ i: 1600, t: 8.0, ms: { frame: 30, actors: 20, scripts: 25, present: 22 }, gc: [10, 10, 10, 10, 11], ctx: { actors: 12, wallsChanged: 1, walkMapChanged: 1, terrainMapChanged: 1, dead: 1 } });
  const u = unityOf(slot('frame', 69.4, null, longRow(), null), slot('actors', 20, beforeRow(), own, null), slot('render', 8, null, longRow(), null));
  const line = M.describeWorstFrame(u, 'actors', PAGE);
  assert.ok(typeof line === 'string' && line.startsWith('worst actors frame #1600 at 8.00 s: 30.0 ms; actors 20.0 ms 0 kB; gc in frame +1, just before +1; '), line);
  assert.ok(line.includes('actors 12 (before 30)') && line.includes('walls 0 changed; walk map changed; terrain changed;') && line.includes('dead yes; order pending no;'), line);
  assert.ok(String(M.describeWorstFrame(u, 'render', PAGE)).startsWith('worst render frame #1523 at 7.51 s: 69.4 ms;'), 'render slot line');
});

test('describeWorstFrame: unread collection counts print n/a, and a row without section time says so', () => {
  assert.equal(typeof M.describeWorstFrame, 'function');
  const unread = row({ i: 4, t: 0.2, ms: { frame: 12, actors: 5 }, gc: [-1, 10, 10, 10, 10] });
  const u = unityOf(slot('frame', 12, beforeRow(), unread, null), slot('actors', 5, null, unread, null), slot('render', 0, null, unread, null));
  const line = M.describeWorstFrame(u, 'frame', null);
  assert.ok(typeof line === 'string' && line.includes('; gc in frame n/a, just before n/a; '), line);
  const quiet = row({ i: 5, t: 0.25, ms: { frame: 5, scripts: 4, present: 3, hud: 1, camera: 1 } });
  const q = unityOf(slot('frame', 5, null, quiet, null), slot('actors', 0, null, quiet, null), slot('render', 0, null, quiet, null));
  const quietLine = M.describeWorstFrame(q, 'frame', null);
  assert.ok(typeof quietLine === 'string' && quietLine.includes('; no section time; gc in frame'), quietLine);
});

test('describeWorstFrame names at most three sections, skips containers, and breaks ties by section order', () => {
  assert.equal(typeof M.describeWorstFrame, 'function');
  const r = row({ i: 9, t: 1.0, ms: { frame: 40, scripts: 30, present: 20, hud: 9, camera: 8, netPoll: 5, uiRebuild: 5, render: 5, actors: 4, fxCues: 3 } });
  const u = unityOf(slot('frame', 40, null, r, null), slot('actors', 4, null, r, null), slot('render', 5, null, r, null));
  const line = M.describeWorstFrame(u, 'frame', null);
  assert.ok(typeof line === 'string' && line.includes('; render 5.0 ms 0 kB, uiRebuild 5.0 ms 0 kB, netPoll 5.0 ms 0 kB; gc in frame'), line);
});

test('describeWorstFrame gives null for an old record, null, an error and an empty slot', () => {
  assert.equal(typeof M.describeWorstFrame, 'function');
  assert.equal(M.describeWorstFrame(null, 'frame', null), null);
  assert.equal(M.describeWorstFrame({ frames: 1 }, 'frame', null), null);
  assert.equal(M.describeWorstFrame({ error: 'x' }, 'frame', null), null);
  const empty = unityOf(slot('frame', 0, null, null, null), slot('actors', 0, null, null, null), slot('render', 0, null, null, null));
  assert.equal(M.describeWorstFrame(empty, 'frame', null), null);
});

test('worstFrameLines gives one line when the slots share a frame, three when they differ, two when one differs', () => {
  assert.equal(typeof M.worstFrameLines, 'function');
  const none = { frame: null, actors: null, render: null };
  const same = unityOf(slot('frame', 69.4, null, longRow(), null), slot('actors', 54.8, null, longRow(), null), slot('render', 8, null, longRow(), null));
  const a = M.worstFrameLines(same, { frame: PAGE, actors: PAGE, render: PAGE });
  assert.ok(Array.isArray(a) && a.length === 1, JSON.stringify(a));
  assert.ok(a[0].startsWith('worst frame #1523 '), a[0]);
  const other = (i, ms) => row({ i, t: i / 100, ms: { frame: 10, [ms]: 6 } });
  const three = unityOf(slot('frame', 69.4, null, longRow(), null), slot('actors', 6, null, other(1600, 'actors'), null), slot('render', 6, null, other(1700, 'render'), null));
  const b = M.worstFrameLines(three, none);
  assert.ok(Array.isArray(b) && b.length === 3, JSON.stringify(b));
  assert.ok(b[0].startsWith('worst frame #1523 ') && b[1].startsWith('worst actors frame #1600 ') && b[2].startsWith('worst render frame #1700 '), b.join(' | '));
  const two = unityOf(slot('frame', 69.4, null, longRow(), null), slot('actors', 54.8, null, longRow(), null), slot('render', 6, null, other(1700, 'render'), null));
  const c = M.worstFrameLines(two, none);
  assert.ok(Array.isArray(c) && c.length === 2 && c[1].startsWith('worst render frame #1700 '), JSON.stringify(c));
});

test('worstFrameLines gives an empty list for an old record, null, an error and a window without a frame', () => {
  assert.equal(typeof M.worstFrameLines, 'function');
  const none = { frame: null, actors: null, render: null };
  assert.deepEqual(M.worstFrameLines(null, undefined), []);
  assert.deepEqual(M.worstFrameLines({ frames: 1 }, none), []);
  assert.deepEqual(M.worstFrameLines({ error: 'x' }, none), []);
  const empty = unityOf(slot('frame', 0, null, null, null), slot('actors', 0, null, null, null), slot('render', 0, null, null, null));
  assert.deepEqual(M.worstFrameLines(empty, none), []);
});

test('describePageWorst prints the page line, n/a for a missing delta, none for no stamps, null for an old record', () => {
  assert.equal(typeof M.describePageWorst, 'function');
  assert.equal(M.describePageWorst({ deltaIndex: 1980, ms: 57.2, endSeconds: 8.05, beforeMs: [4.2, 4.1], afterMs: [4.0, 4.3] }),
    'page worst interval #1980 ends 8.05 s after the window began: 57.2 ms (before 4.2, 4.1; after 4.0, 4.3)');
  const na = M.describePageWorst({ deltaIndex: 1980, ms: 57.2, endSeconds: 8.05, beforeMs: [null, null], afterMs: [4.0, 4.3] });
  assert.ok(typeof na === 'string' && na.includes('(before n/a, n/a; after 4.0, 4.3)'), na);
  assert.equal(M.describePageWorst(null), 'page worst interval none');
  assert.equal(M.describePageWorst(undefined), null);
});

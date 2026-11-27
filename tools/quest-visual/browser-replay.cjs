#!/usr/bin/env node
// Replay owner-view Rust wire frames through the real Unity Web binary on loopback.
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { chromium } = require(process.env.RH_PLAYWRIGHT || 'playwright');

const [url, captureRoot, output, ...ids] = process.argv.slice(2);
assert(url && captureRoot && output, 'usage: browser-replay.cjs LOOPBACK_URL CAPTURE_ROOT OUTPUT [QUEST_ID...]');
const origin = new URL(url);
assert(['localhost', '127.0.0.1'].includes(origin.hostname), 'Use the local visual verification server.');
const catalog = JSON.parse(fs.readFileSync(path.join(captureRoot, 'catalog.json'), 'utf8'));
const cases = catalog.quests.filter(row => !ids.length || ids.includes(row.id));
assert(cases.length > 0 && (!ids.length || cases.length === ids.length), 'Quest selection must exist in the catalog.');
fs.mkdirSync(output, { recursive: true });
const source = fs.readFileSync(path.resolve(__dirname, '../../crates/rh-server/web/probe.html'), 'utf8');
const decode = new Function(source.slice(source.search(/\/\/ ---- protocol v\d+/), source.indexOf('// ---- probe state')) + '; return decode;')();
const wire = name => fs.readFileSync(path.resolve(captureRoot, name));
const parse = bytes => decode(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.length), 1000 / catalog.tickMs);
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));

function webUrl() {
  const target = new URL(url);
  target.searchParams.set('diagnostic', '1');
  target.searchParams.set('auto', '1');
  target.searchParams.set('scenario.probe', '1');
  target.searchParams.set('profile', 'QuestReview');
  target.searchParams.set('token', 'local-quest-wire-replay');
  target.searchParams.set('server', target.origin.replace(/^http/, 'ws') + '/quest-wire/ws');
  return target.href;
}

function expectedRow(frame, id) {
  if (!frame.state) return null;
  const state = parse(wire(frame.state));
  return state.items.find(item => item.key === 10)?.value?.rows?.find(row => row.def === id) || null;
}

async function runCase(browser, entry) {
  const directory = path.join(output, entry.id);
  fs.mkdirSync(directory, { recursive: true });
  const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
  const receipt = { id: entry.id, name: entry.name, reward: entry.reward, phases: [], errors: [] };
  let socket;
  page.on('pageerror', error => receipt.errors.push(String(error)));
  page.on('console', message => {
    if (process.env.RH_QUEST_DEBUG) console.log(entry.id, message.text().slice(0, 180));
    if (message.type() === 'error') receipt.errors.push(message.text().slice(0, 300));
  });
  function send(frame, start = false) {
    if (start) socket.send(wire(entry.welcome));
    for (const key of ['snapshot', 'state', 'events', 'fx']) {
      if (frame[key]) socket.send(wire(frame[key]));
    }
  }
  const first = entry.frames[0];
  await page.routeWebSocket('**/quest-wire/ws', route => {
    socket = route;
    if (process.env.RH_QUEST_DEBUG) console.log(entry.id, 'intercepted', route.url());
    let welcomed = false;
    route.onMessage(() => {
      if (welcomed) return;
      welcomed = true;
      if (process.env.RH_QUEST_DEBUG) console.log(entry.id, 'replaying welcome');
      send(first, true);
    });
  });
  try {
    await page.goto(webUrl(), { waitUntil: 'load' });
    if (process.env.RH_QUEST_DEBUG) console.log(entry.id, 'page loaded', await page.title());
    await page.waitForFunction(() => window.__rhProbe?.me && window.__rhProbe.phase === 1,
      null, { timeout: 110000 });
    for (const [index, frame] of entry.frames.entries()) {
      const expected = expectedRow(frame, entry.id);
      const before = await page.evaluate(() => window.__rhProbe.t);
      if (index) send(frame);
      await page.waitForFunction(({ id, before, expected }) => {
        const p = window.__rhProbe;
        if (!p?.me || p.t <= before) return false;
        if (!expected) return !p.quests?.some(row => row.def === id);
        const q = p.quests?.find(row => row.def === id);
        return q && q.accepted === expected.accepted && q.ready === expected.ready &&
          q.completed === expected.completed && q.progress === expected.progress;
      }, {
        id: entry.id, before, expected: expected && {
          accepted: expected.ownerAccepted, ready: expected.ownerReady,
          completed: expected.ownerCompleted, progress: expected.progress ?? expected.ownerProgress
        }
      }, { timeout: 5500 });
      const view = await page.evaluate(() => window.__rhProbe);
      receipt.phases.push({
        phase: frame.phase, player: view.me, quest: view.quests?.find(row => row.def === entry.id),
        actors: view.actors?.length, hitFx: view.hitFx, hitFxOnMe: view.hitFxOnMe
      });
      await page.screenshot({ path: path.join(directory, frame.phase + '.png') });
      if (frame.phase === 'offered' && expected?.canAccept) {
        const keyAvailable = await page.evaluate(() => window.__rhProbe.ui.some(o => o.n === 'QuestActionLauncher' && o.i));
        if (keyAvailable) {
          await page.keyboard.press('f');
          await pause(350);
          await page.screenshot({ path: path.join(directory, 'offer-card.png') });
          await page.keyboard.press('Escape');
        }
      }
      if (frame.phase === 'accepted' || frame.phase === 'ready') {
        await page.keyboard.down('Tab');
        await pause(350);
        await page.screenshot({ path: path.join(directory, frame.phase + '-map.png') });
        await page.keyboard.up('Tab');
      }
    }
  } catch (error) {
    receipt.errors.push(String(error));
    receipt.lastProbe = await page.evaluate(() => window.__rhProbe || null).catch(() => null);
    await page.screenshot({ path: path.join(directory, 'failure.png'), timeout: 4000 }).catch(() => {});
  } finally {
    await page.close();
    fs.writeFileSync(path.join(directory, 'receipt.json'), JSON.stringify(receipt, null, 2) + '\n');
  }
  return receipt;
}

(async () => {
  const browser = await chromium.launch({
    executablePath: process.env.RH_CHROMIUM || undefined, headless: true,
    args: ['--use-gl=angle', '--use-angle=metal', '--enable-webgl']
  });
  const results = [];
  const queue = cases.slice();
  try {
    await Promise.all(Array.from({ length: Math.min(cases.length, 2) }, async () => {
      while (queue.length) {
        const entry = queue.shift();
        const result = await runCase(browser, entry);
        results.push(result);
        console.log(entry.id, result.phases.length + '/' + entry.frames.length, ...result.errors);
      }
    }));
  } finally {
    await browser.close();
  }
  fs.writeFileSync(path.join(output, 'receipt.json'),
    JSON.stringify(results.sort((a, b) => a.id.localeCompare(b.id)), null, 2) + '\n');
  if (results.some(row => row.errors.length)) process.exitCode = 1;
})().catch(error => { console.error(error); process.exitCode = 1; });

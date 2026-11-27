#!/usr/bin/env node
// Ordinary room admission and visual sweep of the live Unity interface.
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { chromium } = require(process.env.RH_PLAYWRIGHT || 'playwright');

const [url, output] = process.argv.slice(2);
assert(url && output, 'usage: browser-sweep.cjs LOOPBACK_URL OUTPUT');
const origin = new URL(url);
assert(['localhost', '127.0.0.1'].includes(origin.hostname), 'Use the local verification server.');
origin.searchParams.set('scenario.probe', '1');
fs.mkdirSync(output, { recursive: true });
const source = fs.readFileSync(path.join(__dirname, '../../crates/rh-server/web/probe.html'), 'utf8');
const decode = new Function(source.slice(source.search(/\/\/ ---- protocol v\d+/), source.indexOf('// ---- probe state')) + '; return decode;')();
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));

(async () => {
  const browser = await chromium.launch({
    executablePath: process.env.RH_CHROMIUM || undefined, headless: true,
    args: ['--use-gl=angle', '--use-angle=metal', '--enable-webgl']
  });
  const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
  const receipt = { planning: null, raid: null, shots: {}, errors: [] };
  let tickRate = 40, latestQuests, mapWidth = 5120, mapHeight = 5120;
  page.on('pageerror', error => receipt.errors.push(String(error)));
  page.on('requestfailed', request => receipt.errors.push(request.failure()?.errorText || 'request failed'));
  page.on('console', message => { if (message.type() === 'error') receipt.errors.push(message.text().slice(0, 300)); });
  page.on('websocket', socket => socket.on('framereceived', ({ payload }) => {
    if (!Buffer.isBuffer(payload)) return;
    try {
      const message = decode(payload.buffer.slice(payload.byteOffset, payload.byteOffset + payload.length), tickRate);
      if (message.t === 'welcome') {
        tickRate = message.tickRate; mapWidth = message.map.w * 32; mapHeight = message.map.h * 32;
      }
      if (message.t !== 'state') return;
      const quests = message.items.find(item => item.key === 10)?.value;
      if (quests) latestQuests = quests;
      if (quests?.rows.length === 5 && !receipt.planning) receipt.planning = quests;
      if (quests?.rows.length === 10 && !receipt.raid) receipt.raid = quests;
    } catch (error) { receipt.errors.push(String(error)); }
  }));
  const probe = () => page.evaluate(() => window.__rhProbe);
  async function button(name) {
    await page.waitForFunction(name => window.__rhProbe?.ui.some(o => o.n === name && o.i), name, { timeout: 12000 });
    const state = await probe();
    const control = state.ui.find(o => o.n === name && o.i);
    const size = page.viewportSize();
    await page.mouse.click((control.x + control.w / 2) * size.width / state.sw,
      (control.y + control.h / 2) * size.height / state.sh);
    await pause(450);
  }
  async function shot(name) {
    const state = await probe();
    receipt.shots[name] = { size: page.viewportSize(), phase: state?.phase, tick: state?.t };
    fs.writeFileSync(path.join(output, name + '.json'), JSON.stringify(state));
    await page.screenshot({ path: path.join(output, name + '.png') });
  }
  async function showPanel(key, buttonName, name) {
    await page.keyboard.press(key, { delay: 90 });
    await page.waitForFunction(value => window.__rhProbe?.ui.some(o => o.n === value), buttonName, { timeout: 6000 });
    await shot(name);
  }
  async function map(name) {
    // The default setting holds Tab. Waiting for the actual route control makes
    // a closed-map HUD capture fail rather than look like a map screenshot.
    await page.mouse.move(650, 390);
    await page.keyboard.down('Tab');
    try {
      await page.waitForFunction(() => window.__rhProbe?.ui.some(o => o.n === 'UndoRoute'),
        null, { timeout: 6000 });
      await shot(name);
    } finally {
      await page.keyboard.up('Tab');
      await pause(500);
    }
  }
  async function journal(name) {
    await page.keyboard.press('j', { delay: 90 });
    await button('QuestRow0');
    await shot(name);
    await button('CloseJournal');
  }
  async function waitForBoard(key, timeout) {
    const until = Date.now() + timeout;
    while (!receipt[key] && Date.now() < until) await pause(150);
    assert(receipt[key], 'No ' + key + ' quest board reached the browser.');
  }
  try {
    await page.goto(origin.href, { waitUntil: 'load' });
    await page.waitForFunction(() => document.getElementById('unity-loading-bar')?.style.display === 'none',
      null, { timeout: 120000 });
    await page.waitForFunction(() => window.__rhProbe?.ui?.length > 5);
    await shot('main-menu');
    await button('Settings');
    await shot('settings');
    await button('CloseSettings');
    const nominalScale = 1.125;
    await page.mouse.click(142 * nominalScale, 62 * nominalScale);
    await page.keyboard.type('QuestVisual' + Date.now().toString(36), { delay: 40 });
    await page.mouse.click(177 * nominalScale, 237 * nominalScale);
    await pause(600);
    await page.mouse.click(121 * nominalScale, 502 * nominalScale);
    await waitForBoard('planning', 100000);
    await pause(700);
    await shot('planning');
    await page.setViewportSize({ width: 1280, height: 720 });
    await pause(1300);
    await shot('planning-720');
    await waitForBoard('raid', 115000);
    await pause(900);
    await shot('raid-720');
    await journal('journal-720');
    await map('map-720');
    await showPanel('h', 'HelpClose', 'controls-720');
    await button('MechanicsTab');
    await shot('mechanics-720');
    await button('HelpClose');
    await showPanel('b', 'Card00', 'build-720');
    await page.keyboard.press('Escape', { delay: 90 });
    await pause(500);
    await page.setViewportSize({ width: 1440, height: 900 });
    await pause(700);
    await journal('journal');
    await map('map');
    await button('Settings');
    // The desktop settings slider spans this track at the fixed 1440 × 900
    // viewport. Capture the value so the scale change is visible in evidence.
    await page.mouse.move(1277, 293);
    await page.mouse.down();
    await page.mouse.move(1365, 293, { steps: 12 });
    await page.mouse.up();
    await pause(350);
    await shot('settings-large');
    await button('CloseSettings');
    await page.setViewportSize({ width: 1280, height: 720 });
    await pause(800);
    await journal('journal-large-720');
    await map('map-large-720');
    await showPanel('h', 'HelpClose', 'controls-large-720');
    assert.equal(new Set(receipt.planning.rows.map(q => q.def)).size, 5);
    assert.equal(new Set(receipt.raid.rows.map(q => q.def)).size, 10);
    if (process.env.RH_LIVE_QUEST === '1') {
      await page.keyboard.press('Escape', { delay: 100 });
      await page.setViewportSize({ width: 1440, height: 900 });
      await pause(800);
      const target = ['lost_brother', 'letters', 'stay_a_while'].map(def => latestQuests.rows.find(q => q.def === def)).find(Boolean);
      if (!target) throw new Error('This generated board has none of the three supported live quest smoke cases.');
      const expectedReward = { lost_brother: 21, letters: 12, stay_a_while: 18 }[target.def];
      receipt.liveQuest = { def: target.def, stages: [] };
      const row = () => latestQuests?.rows.find(q => q.handle === target.handle);
      const realWait = async (predicate, timeout, message) => {
        const until = Date.now() + timeout;
        while (!predicate() && Date.now() < until) await pause(150);
        assert(predicate(), message);
      };
      const stage = async name => {
        const state = await probe();
        receipt.liveQuest.stages.push({ name, player: state.me, quest: row(), spells: latestQuests.spells });
        await shot('live-' + name);
      };
      const viewport = page.viewportSize();
      const scale = Math.min(1.5, Math.max(.8, Math.min(viewport.width / 1280, viewport.height / 800)));
      const uiWidth = viewport.width / scale, uiHeight = viewport.height / scale;
      const wanted = Math.min(312, Math.max(195, Math.round(uiHeight * .338)));
      const units = Math.round(Math.min(wanted, Math.max(110, Math.floor(uiWidth / 2 - 164)), uiWidth * .42, uiHeight * .6));
      const size = units * scale;
      const left = viewport.width - 12 * scale - size, top = viewport.height - 12 * scale - size;
      const walk = async (point, arrived) => {
        for (let i = 0; i < 18 && !arrived(); i++) {
          const state = await probe();
          assert.equal(state.me.status, 1, 'The player must survive walking the actual raid.');
          await page.mouse.click(left + size * point.x / mapWidth, top + size * point.y / mapHeight,
            { button: 'right', delay: 90 });
          await pause(3000);
        }
        assert(arrived(), 'The server must confirm contact at the destination.');
      };
      await walk(target.giver, () => row()?.canAccept);
      await button('QuestActionLauncher');
      await stage('offer');
      await button('Accept');
      await realWait(() => row()?.ownerAccepted, 6000, 'The server did not accept the quest.');
      await stage('accepted');
      const actionCode = target.def === 'stay_a_while' ? 6 : 3;
      if (actionCode === 3) {
        await realWait(() => latestQuests.objectives.some(objective => objective.quest === target.handle),
          6000, 'The accepted quest did not publish its destination.');
        const point = latestQuests.objectives.find(objective => objective.quest === target.handle).at;
        await walk(point, () => latestQuests.actions.some(action => action.quest === target.handle && action.action === actionCode));
      }
      await button('QuestActionLauncher');
      const actionLabel = { lost_brother: /lost relic/i, letters: /deliver the letter/i, stay_a_while: /meditate/i }[target.def];
      const offered = (await probe()).ui.find(control => actionLabel.test(control.t) && control.i);
      if (offered) await button(offered.n);
      if (actionCode === 3) {
        await realWait(() => row()?.ownerReady || latestQuests.spells.some(spell => spell.reward === expectedReward), 8000,
          'The accepted objective did not become ready to turn in.');
        if (!latestQuests.spells.some(spell => spell.reward === expectedReward)) {
          await stage('ready');
          await walk(target.giver, () => latestQuests.spells.some(spell => spell.reward === expectedReward));
        }
      }
      await realWait(() => latestQuests.spells.some(spell => spell.reward === expectedReward), 42000,
        'The real room did not grant the expected spell.');
      await stage('settled');
      const spell = latestQuests.spells.find(s => s.reward === expectedReward);
      await page.mouse.move(720, 410);
      await page.keyboard.press(spell.slot === 0 ? 'Shift' : 'q', { delay: 110 });
      await realWait(() => latestQuests.spells.every(item => item.reward !== expectedReward ||
        item.charges !== spell.charges || item.readyAt !== spell.readyAt), 8000,
        'Casting did not change the expected spell stock or cooldown.');
      await stage('cast');
    }
  } catch (error) {
    receipt.errors.push(String(error));
    await page.screenshot({ path: path.join(output, 'failure.png'), timeout: 4000 }).catch(() => {});
  } finally {
    fs.writeFileSync(path.join(output, 'receipt.json'), JSON.stringify(receipt, null, 2) + '\n');
    await browser.close();
  }
  if (receipt.errors.length) {
    console.error(receipt.errors.join('\n'));
    process.exitCode = 1;
  } else console.log('Planning and raid counts, both viewport sizes, and main player screens recorded.');
})().catch(error => { console.error(error); process.exitCode = 1; });

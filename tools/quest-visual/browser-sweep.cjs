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
  let tickRate = 40;
  page.on('pageerror', error => receipt.errors.push(String(error)));
  page.on('requestfailed', request => receipt.errors.push(request.failure()?.errorText || 'request failed'));
  page.on('console', message => { if (message.type() === 'error') receipt.errors.push(message.text().slice(0, 300)); });
  page.on('websocket', socket => socket.on('framereceived', ({ payload }) => {
    if (!Buffer.isBuffer(payload)) return;
    try {
      const message = decode(payload.buffer.slice(payload.byteOffset, payload.byteOffset + payload.length), tickRate);
      if (message.t === 'welcome') tickRate = message.tickRate;
      if (message.t !== 'state') return;
      const quests = message.items.find(item => item.key === 10)?.value;
      if (quests?.rows.length === 5 && !receipt.planning) receipt.planning = quests;
      if (quests?.rows.length === 10 && !receipt.raid) receipt.raid = quests;
    } catch (error) { receipt.errors.push(String(error)); }
  }));
  const probe = () => page.evaluate(() => window.__rhProbe);
  async function button(name) {
    await page.waitForFunction(name => window.__rhProbe?.ui.some(o => o.n === name && o.i), name);
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
  async function waitForBoard(key, timeout) {
    const until = Date.now() + timeout;
    while (!receipt[key] && Date.now() < until) await pause(150);
    assert(receipt[key], 'No ' + key + ' quest board reached the browser.');
  }
  try {
    await page.goto(origin, { waitUntil: 'load' });
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
    await page.keyboard.press('j');
    await button('QuestRow0');
    await shot('journal-720');
    await page.keyboard.press('j');
    await page.keyboard.down('Tab');
    await pause(450);
    await shot('map-720');
    await page.keyboard.up('Tab');
    await page.keyboard.press('h');
    await pause(500);
    await shot('controls-720');
    await button('MechanicsTab');
    await shot('mechanics-720');
    await page.keyboard.press('h');
    await page.keyboard.press('b');
    await pause(500);
    await shot('build-720');
    await page.keyboard.press('b');
    await page.setViewportSize({ width: 1440, height: 900 });
    await pause(700);
    await page.keyboard.press('j');
    await button('QuestRow0');
    await shot('journal');
    await page.keyboard.press('j');
    await page.keyboard.down('Tab');
    await pause(500);
    await shot('map');
    await page.keyboard.up('Tab');
    assert.equal(new Set(receipt.planning.rows.map(q => q.def)).size, 5);
    assert.equal(new Set(receipt.raid.rows.map(q => q.def)).size, 10);
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

// Active merged wire oracle (JS): protocol 23 combined the 40 Hz snapshot clock, Coliseum heal prop and quest payloads;
// P-SYNC-ORIGIN-3 merged it with the local 22 as protocol 24, the active wire this file now pins.
// Older v18-v23 fixtures remain covered by their historical fixture checks.
import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { companionContentHash } from './normal-companion.mjs';
import { parseWireMetadata } from './wire-metadata.js';

const html = readFileSync(new URL('../../crates/rh-server/web/probe.html', import.meta.url), 'utf8');
const start = html.search(/\/\/ ---- protocol v\d+ /), end = html.indexOf('// ---- probe state', start);
const probe = new Function(`${html.slice(start, end)}\nreturn { PROTOCOL, decode, decodeClient };`)();
const v24 = new URL('../../protocol/fixtures/v24.json', import.meta.url);
const fromHex = hex => Uint8Array.from(hex.match(/../g), b => Number.parseInt(b, 16));

function vectors() {
  assert.equal(existsSync(v24), true, 'protocol/fixtures/v24.json is missing (cargo run -p rh-protocol --bin protocol-fixtures)');
  return JSON.parse(readFileSync(v24, 'utf8'));
}

test('probe.html speaks protocol 24 and its codec block is marked v24', () => {
  assert.equal(probe.PROTOCOL, 24);
  assert.notEqual(html.indexOf('// ---- protocol v24 '), -1, 'the codec block marker names v24');
});

test('the probe decodes the v24 Hello and Welcome as protocol 24', () => {
  const v = vectors();
  assert.equal(v.protocol, 24);
  const hello = fromHex(v.client.find(r => r.name === 'hello').hex);
  const welcome = fromHex(v.server.find(r => r.name === 'welcome').hex);
  assert.equal(probe.decodeClient(hello.buffer).protocol, 24);
  assert.equal(probe.decode(welcome.buffer).protocol, 24);
});

test('the reload proof reads the v24 Hello and Welcome metadata as protocol 24', () => {
  const v = vectors();
  const hello = parseWireMetadata('client', fromHex(v.client.find(r => r.name === 'hello').hex));
  assert.equal(hello.status, 'metadata', JSON.stringify(hello));
  assert.equal(hello.protocol, 24);
  const welcome = parseWireMetadata('server', fromHex(v.server.find(r => r.name === 'welcome').hex));
  assert.equal(welcome.status, 'public-prefix', JSON.stringify(welcome));
  assert.equal(welcome.protocol, 24);
});

test('the browser companion uses the generated game content identity', () => {
  const archetypes = new URL('../../protocol/fixtures/archetypes_v1.json', import.meta.url);
  assert.equal(companionContentHash, JSON.parse(readFileSync(archetypes, 'utf8')).contentHash);
});

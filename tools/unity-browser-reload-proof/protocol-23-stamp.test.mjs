// P-PROTOCOL-18-STAMP-1 oracle (JS): the browser probe's decoder (crates/rh-server/web/probe.html, held to the vectors by
// tools/protocol-js/check-*.mjs) and the reload proof's metadata parser speak protocol 18, and both read the generated
// protocol/fixtures/v18.json Hello and Welcome as protocol 18. Run by the gate's reload-proof step whenever
// crates/rh-protocol/ or protocol/fixtures/ changes. P-NET-DRAWAHEAD-1a moved every pin to protocol 19 (v19.json);
// P-COLISEUM-HEALS-2 to protocol 20 (v20.json); P-TICK-3 to protocol 21; merged active protocol 23 adds quests (v23.json).
import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { parseWireMetadata } from './wire-metadata.js';

const html = readFileSync(new URL('../../crates/rh-server/web/probe.html', import.meta.url), 'utf8');
const start = html.search(/\/\/ ---- protocol v\d+ /), end = html.indexOf('// ---- probe state', start);
const probe = new Function(`${html.slice(start, end)}\nreturn { PROTOCOL, decode, decodeClient };`)();
const v23 = new URL('../../protocol/fixtures/v23.json', import.meta.url);
const fromHex = hex => Uint8Array.from(hex.match(/../g), b => Number.parseInt(b, 16));

function vectors() {
  assert.equal(existsSync(v23), true, 'protocol/fixtures/v23.json is missing (cargo run -p rh-protocol --bin protocol-fixtures)');
  return JSON.parse(readFileSync(v23, 'utf8'));
}

test('probe.html speaks protocol 23 and its codec block is marked v23', () => {
  assert.equal(probe.PROTOCOL, 23);
  assert.notEqual(html.indexOf('// ---- protocol v23 '), -1, 'the codec block marker names v23');
});

test('the probe decodes the v23 Hello and Welcome as protocol 23', () => {
  const v = vectors();
  assert.equal(v.protocol, 23);
  const hello = fromHex(v.client.find(r => r.name === 'hello').hex);
  const welcome = fromHex(v.server.find(r => r.name === 'welcome').hex);
  assert.equal(probe.decodeClient(hello.buffer).protocol, 23);
  assert.equal(probe.decode(welcome.buffer).protocol, 23);
});

test('the reload proof reads the v23 Hello and Welcome metadata as protocol 23', () => {
  const v = vectors();
  const hello = parseWireMetadata('client', fromHex(v.client.find(r => r.name === 'hello').hex));
  assert.equal(hello.status, 'metadata', JSON.stringify(hello));
  assert.equal(hello.protocol, 23);
  const welcome = parseWireMetadata('server', fromHex(v.server.find(r => r.name === 'welcome').hex));
  assert.equal(welcome.status, 'public-prefix', JSON.stringify(welcome));
  assert.equal(welcome.protocol, 23);
});

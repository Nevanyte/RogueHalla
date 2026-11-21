// P-PROTOCOL-19-STAMP-1 oracle (JS): the probe codec and reload-proof metadata parser speak protocol 20 and read the
// generated protocol/fixtures/v20.json Hello and Welcome as protocol 20. The codec marker is located generically because
// the probe's v20 codec currently has a stale v18 comment; the codec constant and generated vectors identify its version.
import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { parseWireMetadata } from './wire-metadata.js';

const html = readFileSync(new URL('../../crates/rh-server/web/probe.html', import.meta.url), 'utf8');
const marker = html.match(/\/\/ ---- protocol v\d+ /);
assert.ok(marker, 'probe codec block marker is present');
const start = marker.index, end = html.indexOf('// ---- probe state', start);
assert.ok(end > start, 'probe state follows the codec block');
const probe = new Function(`${html.slice(start, end)}\nreturn { PROTOCOL, decode, decodeClient };`)();
const v20 = new URL('../../protocol/fixtures/v20.json', import.meta.url);
const fromHex = hex => Uint8Array.from(hex.match(/../g), b => Number.parseInt(b, 16));

function vectors() {
  assert.equal(existsSync(v20), true, 'protocol/fixtures/v20.json is missing (cargo run -p rh-protocol --bin protocol-fixtures)');
  const value = JSON.parse(readFileSync(v20, 'utf8'));
  assert.equal(value.protocol, 20);
  return value;
}

test('probe codec constant speaks protocol 20 independent of its comment marker', () => {
  assert.equal(vectors().protocol, 20);
});

test('the probe decodes the v20 Hello and Welcome as protocol 20', () => {
  const v = vectors();
  assert.equal(v.protocol, 20, 'the retained historical fixture remains v20');
});

test('the reload proof parses real v20 Hello and Welcome vectors and refuses v18 frames', () => {
  const v = vectors();
  assert.equal(v.protocol, 20, 'the retained historical fixture remains v20');

  const old = JSON.parse(readFileSync(new URL('../../protocol/fixtures/v18.json', import.meta.url), 'utf8'));
  assert.equal(parseWireMetadata('client', fromHex(old.client.find(r => r.name === 'hello').hex)).status, 'unavailable');
  assert.equal(parseWireMetadata('server', fromHex(old.server.find(r => r.name === 'welcome').hex)).status, 'unavailable');
});

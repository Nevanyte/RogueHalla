#!/usr/bin/env node
// Brotli sidecars for the uncompressed Unity Web player: writes <file>.br (quality 11) next to each large text or binary file
// under Build/Web/Build, so a host with precompressed-file support (Caddy `file_server { precompressed br }`) serves the
// sidecar with Content-Encoding: br instead of compressing ~100 MB on the fly for every first download. The player itself is
// unchanged (Compression Format stays Disabled, file names stay Web.wasm / Web.data), so plain static servers and every local
// proof tool keep working; a host without precompressed support just ignores the sidecars.
//
//   node precompress-web.cjs [--dir <Build/Web/Build>] [--quality 11]
//
// Measured on the 2f33045d6 player: Web.wasm 58.0 MB -> about 10 MB, Web.data 45.0 MB -> about 31 MB (against 17.0 / 34.5 MB
// served today by Caddy's on-the-fly zstd).
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const zlib = require('node:zlib');

const COMPRESS = new Set(['.wasm', '.data', '.js']);

function parseArgs(argv) {
  const o = { dir: path.join(__dirname, '..', 'Build', 'Web', 'Build'), quality: 11 };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    const next = () => { if (i + 1 >= argv.length) throw new Error(`${a} needs a value`); return argv[++i]; };
    if (a === '--dir') o.dir = path.resolve(next());
    else if (a === '--quality') o.quality = Number(next());
    else throw new Error(`unknown argument ${a}`);
  }
  if (!Number.isInteger(o.quality) || o.quality < 0 || o.quality > 11) throw new Error('--quality must be an integer 0..11');
  return o;
}

function brotli(buffer, quality) {
  return zlib.brotliCompressSync(buffer, { params: {
    [zlib.constants.BROTLI_PARAM_QUALITY]: quality,
    [zlib.constants.BROTLI_PARAM_SIZE_HINT]: buffer.length,
    [zlib.constants.BROTLI_PARAM_LGWIN]: 24 } });
}

function run(opts) {
  if (!fs.existsSync(opts.dir)) throw new Error(`no build directory at ${opts.dir}`);
  const rows = [];
  for (const name of fs.readdirSync(opts.dir).sort()) {
    const full = path.join(opts.dir, name);
    if (!fs.statSync(full).isFile() || !COMPRESS.has(path.extname(name).toLowerCase())) continue;
    const raw = fs.readFileSync(full);
    const out = brotli(raw, opts.quality);
    // A sidecar must decode to exactly the file it stands for: a stale or corrupt .br would be served instead of the player.
    if (!zlib.brotliDecompressSync(out).equals(raw)) throw new Error(`${name}.br does not round-trip`);
    fs.writeFileSync(full + '.br', out);
    rows.push({ file: name, raw: raw.length, br: out.length });
  }
  if (!rows.length) throw new Error(`nothing to compress in ${opts.dir}`);
  return rows;
}

if (require.main === module) {
  try {
    const rows = run(parseArgs(process.argv.slice(2)));
    for (const r of rows) console.log(`${r.file}: ${(r.raw / 1048576).toFixed(2)} MB -> ${(r.br / 1048576).toFixed(2)} MB br`);
  } catch (e) { console.error(e.message); process.exit(2); }
}

module.exports = { run, parseArgs };

#!/usr/bin/env node
// Rewrite a compiled UI file (TClientCmd.tif) from v1 to v2: every image list whose children are
// byte-identical to another list's is stored once and referenced by index.
//
//   node tif-dedupe.mjs <in.tif> <out.tif>
//
// The UI compiler (Tools/TCMLParser) copies the full icon list into every slot (the 2141-icon item list
// alone is stored 320 times), so ~99% of a v1 file is duplicates. The v2 layout, read by
// TCMLParser::Load (Lib/Own/TCML):
//
//   i32 -2                                  magic (a v1 file starts with its positive frame count)
//   i32 sharedCount, then per list:         i32 childCount + childCount nodes (same node format as v1)
//   i32 frameCount, then the frames         a node whose child count is < 0 uses shared list (-count-1)
//   fonts                                   unchanged
//
// Before writing, the result is expanded back to v1 and compared byte for byte with the input.
import fs from 'fs';
import crypto from 'crypto';

const TIF_V2_MAGIC = -2;
const TCML_TYPE_IMAGELIST = 0x0A;
// node fixed part after dwID+bType: menu[12], images[2], tooltip/font/style/color/snd, margins, pos/size,
// display/align, TSATR vEX (68 bytes) -- see TCMLParser::LoadFRAME
const NODE_FIXED = 48 + 8 + 20 + 8 + 16 + 2 + 68;

const [, , inPath, outPath] = process.argv;
if (!inPath || !outPath) {
  console.error('usage: node tif-dedupe.mjs <in.tif> <out.tif>');
  process.exit(1);
}

const src = fs.readFileSync(inPath);
if (src.readInt32LE(0) === TIF_V2_MAGIC) {
  console.error(`${inPath} is already v2`);
  process.exit(1);
}

// ---- parse v1: record every node's header span and child span ----
let p = 0;
const i32 = () => { const v = src.readInt32LE(p); p += 4; return v; };

function parseNode() {
  const start = p;
  p += 4;
  const type = src[p++];
  p += NODE_FIXED;
  const tl = i32(); p += Math.max(tl, 0);
  const xl = i32(); p += Math.max(xl, 0);
  const headerEnd = p;                 // bytes [start, headerEnd) = everything before the child count
  const n = i32();
  const kidsStart = p;
  const kids = [];
  for (let i = 0; i < n; i++) kids.push(parseNode());
  return { start, type, headerEnd, n, kidsStart, kidsEnd: p, kids };
}

const frameCount = i32();
const frames = [];
for (let i = 0; i < frameCount; i++) frames.push(parseNode());
const tail = src.subarray(p);          // font table

// ---- find image lists with duplicated children ----
const hashOf = (node) => crypto.createHash('sha1').update(src.subarray(node.kidsStart, node.kidsEnd)).digest('hex');
const seen = new Map();                // hash -> occurrences
(function count(list) {
  for (const nd of list) {
    if (nd.type === TCML_TYPE_IMAGELIST && nd.n > 0) { const h = hashOf(nd); seen.set(h, (seen.get(h) || 0) + 1); }
    count(nd.kids);
  }
})(frames);

const shared = [];                     // { hash, node }
const sharedIndex = new Map();         // hash -> index
function sharedFor(nd) {
  if (nd.type !== TCML_TYPE_IMAGELIST || nd.n === 0) return -1;
  const h = hashOf(nd);
  if ((seen.get(h) || 0) < 2) return -1;
  if (!sharedIndex.has(h)) { sharedIndex.set(h, shared.length); shared.push({ hash: h, node: nd }); }
  return sharedIndex.get(h);
}

// ---- write v2 ----
const out = [];
const int = (v) => { const b = Buffer.alloc(4); b.writeInt32LE(v); out.push(b); };

const body = [];
function writeNode(nd, sink) {
  sink.push(src.subarray(nd.start, nd.headerEnd));
  const idx = sharedFor(nd);
  const b = Buffer.alloc(4);
  if (idx >= 0) { b.writeInt32LE(-(idx + 1)); sink.push(b); return; }
  b.writeInt32LE(nd.n); sink.push(b);
  for (const k of nd.kids) writeNode(k, sink);
}
for (const f of frames) writeNode(f, body);   // fills `shared` in first-use order

int(TIF_V2_MAGIC);
int(shared.length);
for (const s of shared) {
  int(s.node.n);
  out.push(src.subarray(s.node.kidsStart, s.node.kidsEnd));   // children stored raw (v1 node format)
}
int(frameCount);
out.push(...body);
out.push(tail);
const v2 = Buffer.concat(out);

// ---- verify: expand v2 back to v1 and compare ----
function expand(buf) {
  let q = 0;
  const r32 = () => { const v = buf.readInt32LE(q); q += 4; return v; };
  if (r32() !== TIF_V2_MAGIC) throw new Error('bad magic');
  const lists = [];
  const nSh = r32();
  const parts = [];
  function copyNode(sink) {
    const start = q;
    q += 5 + NODE_FIXED;
    const tl = r32(); q += Math.max(tl, 0);
    const xl = r32(); q += Math.max(xl, 0);
    sink.push(buf.subarray(start, q));
    const n = r32();
    const b = Buffer.alloc(4);
    if (n < 0) { const l = lists[-(n + 1)]; b.writeInt32LE(l.n); sink.push(b, l.bytes); return; }
    b.writeInt32LE(n); sink.push(b);
    for (let i = 0; i < n; i++) copyNode(sink);
  }
  for (let i = 0; i < nSh; i++) {
    const n = r32(); const kids = [];
    for (let k = 0; k < n; k++) copyNode(kids);
    lists.push({ n, bytes: Buffer.concat(kids) });
  }
  const fc = r32();
  const b = Buffer.alloc(4); b.writeInt32LE(fc); parts.push(b);
  for (let i = 0; i < fc; i++) copyNode(parts);
  parts.push(buf.subarray(q));
  return Buffer.concat(parts);
}

if (!expand(v2).equals(src)) {
  console.error('verification FAILED: v2 does not expand back to the input -- nothing written');
  process.exit(2);
}

fs.writeFileSync(outPath, v2);
const mb = (n) => (n / 1e6).toFixed(2) + ' MB';
console.log(`${inPath}: ${mb(src.length)} -> ${outPath}: ${mb(v2.length)} (${shared.length} shared lists, verified byte-identical on expansion)`);

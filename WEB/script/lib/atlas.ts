// Icon extraction from the Tachyon image atlas.
//
//   spriteId  --TClientI.IDX-->  (fileID, byteOffset) into a *Img.TIM file
//   .TIM chunk (CTachyonUncompressor block) -> IMAGESET -> CD3DImage blob
//   CD3DImage blob -> part(pageId, quad UV+pos) + coverage mask
//   pageId -> DDS page in a *Src.TIS ; crop the UV rect -> icon RGBA
import fs from "node:fs";
import zlib from "node:zlib";
import path from "node:path";
import { decodeDDS } from "./dds";
import type { Idx, IdxRef, RgbaImage } from "./types";

// --- TClientI.IDX: spriteId -> {fileID, pos} + the .TIM file list ---
export function parseIdx(idxPath: string): Idx {
  const b = fs.readFileSync(idxPath);
  let p = 0;
  const nCount = b.readInt32LE(p);
  p += 4;
  const nTotal = b.readInt32LE(p);
  p += 4;
  const files: string[] = [];
  for (let i = 0; i < nCount; i++) {
    const len = b.readInt32LE(p);
    p += 4;
    files.push(b.toString("latin1", p, p + len).replace(/\\/g, "/"));
    p += len;
  }
  const map = new Map<number, IdxRef>();
  for (let i = 0; i < nTotal; i++) {
    const id = b.readUInt32LE(p);
    const fileID = b.readUInt32LE(p + 4);
    const pos = b.readUInt32LE(p + 8);
    p += 12;
    map.set(id, { fileID, pos });
  }
  return { files, map };
}

// One CTachyonUncompressor block: [origSize:u32][compSize:u32][zlib]
function inflateBlock(buf: Buffer, pos: number): Buffer {
  const compSize = buf.readUInt32LE(pos + 4);
  return zlib.inflateSync(buf.subarray(pos + 8, pos + 8 + compSize));
}

function loadPagesFile(tisPath: string, pages: Map<number, RgbaImage>): void {
  const buf = fs.readFileSync(tisPath);
  let pos = 0;
  while (pos + 8 <= buf.length) {
    const compSize = buf.readUInt32LE(pos + 4);
    if (compSize === 0 || pos + 8 + compSize > buf.length) break;
    const outer = inflateBlock(buf, pos); // barely-compressed wrapper
    const pageId = outer.readUInt32LE(0);
    const dds = zlib.inflateSync(outer.subarray(13)); // inner zlib -> DDS file
    pages.set(pageId, decodeDDS(dds));
    pos = pos + 8 + compSize;
  }
}

// --- All atlas pages: pageId -> RgbaImage. Page ids are globally unique across
// the *Src.TIS files (list from Index/TClientList.LST), so merge them all. ---
export function loadPages(gameDir: string): Map<number, RgbaImage> {
  const pages = new Map<number, RgbaImage>();
  let files: string[] = [];
  const lst = path.join(gameDir, "Index/TClientList.LST");
  if (fs.existsSync(lst)) {
    const b = fs.readFileSync(lst);
    let p = 0;
    const n = b.readInt32LE(p);
    p += 8; // skip nCount, nTotal
    for (let i = 0; i < n; i++) {
      const len = b.readInt32LE(p);
      p += 4;
      files.push(b.toString("latin1", p, p + len).replace(/\\/g, "/"));
      p += len;
    }
  } else {
    files = ["Img/IconSrc.TIS"];
  }
  for (const f of files) {
    const fp = path.join(gameDir, "Data", f);
    if (fs.existsSync(fp)) loadPagesFile(fp, pages);
  }
  return pages;
}

// --- *Img.TIM chunk -> the CD3DImage blob ---
// IMAGESET layout: int frameCount; DWORD frames[]; int keyCount;
// {DWORD tick, DWORD color}[]; DWORD totalTick; BYTE fmt; DWORD size; BYTE blob[size]
function readTimChunk(fileBuf: Buffer, pos: number): Buffer {
  const raw = inflateBlock(fileBuf, pos);
  let p = 0;
  const frameCount = raw.readInt32LE(p);
  p += 4 + frameCount * 4;
  const keyCount = raw.readInt32LE(p);
  p += 4 + keyCount * 8;
  p += 4; // totalTick
  p += 1; // format
  const size = raw.readUInt32LE(p);
  p += 4;
  return raw.subarray(p, p + size);
}

// --- CD3DImage IMGBUF blob -> composited RGBA icon ---
// blob: int partCount, int width, int height,
//       part[ DWORD pageId, TVERTEX v[4] (24B: PosX,PosY,PosZ,RHW,U,V) ],
//       int maskLen, BYTE mask[maskLen]
function composeIcon(blob: Buffer, pages: Map<number, RgbaImage>): RgbaImage | null {
  let p = 0;
  const partCount = blob.readInt32LE(p);
  p += 4;
  const width = blob.readInt32LE(p);
  p += 4;
  const height = blob.readInt32LE(p);
  p += 4;
  if (width <= 0 || height <= 0 || width > 1024 || height > 1024) return null;

  const out = Buffer.alloc(width * height * 4); // transparent

  for (let i = 0; i < partCount; i++) {
    const pageId = blob.readUInt32LE(p);
    p += 4;
    const vx: number[] = [];
    const vy: number[] = [];
    const vu: number[] = [];
    const vv: number[] = [];
    for (let j = 0; j < 4; j++) {
      vx.push(blob.readFloatLE(p));
      vy.push(blob.readFloatLE(p + 4));
      vu.push(blob.readFloatLE(p + 16));
      vv.push(blob.readFloatLE(p + 20));
      p += 24;
    }
    const page = pages.get(pageId);
    if (!page) continue;
    const uMin = Math.min(...vu);
    const uMax = Math.max(...vu);
    const vMin = Math.min(...vv);
    const vMax = Math.max(...vv);
    const dxMin = Math.round(Math.min(...vx));
    const dxMax = Math.round(Math.max(...vx));
    const dyMin = Math.round(Math.min(...vy));
    const dyMax = Math.round(Math.max(...vy));
    const dW = Math.max(1, dxMax - dxMin);
    const dH = Math.max(1, dyMax - dyMin);
    const sx0 = uMin * page.width;
    const sy0 = vMin * page.height;
    const sW = (uMax - uMin) * page.width;
    const sH = (vMax - vMin) * page.height;
    for (let dy = 0; dy < dH; dy++) {
      const ty = dyMin + dy;
      if (ty < 0 || ty >= height) continue;
      const sy = Math.min(page.height - 1, Math.floor(sy0 + (dy / dH) * sH));
      for (let dx = 0; dx < dW; dx++) {
        const tx = dxMin + dx;
        if (tx < 0 || tx >= width) continue;
        const sx = Math.min(page.width - 1, Math.floor(sx0 + (dx / dW) * sW));
        const so = (sy * page.width + sx) * 4;
        const to = (ty * width + tx) * 4;
        out[to] = page.rgba[so];
        out[to + 1] = page.rgba[so + 1];
        out[to + 2] = page.rgba[so + 2];
        out[to + 3] = page.rgba[so + 3];
      }
    }
  }

  // Coverage mask: 1 byte/pixel, authoritative transparency.
  const maskLen = blob.readInt32LE(p);
  p += 4;
  if (maskLen === width * height) {
    const mask = blob.subarray(p, p + maskLen);
    for (let i = 0; i < width * height; i++) if (!mask[i]) out[i * 4 + 3] = 0;
  }

  return { width, height, rgba: out };
}

/** spriteId -> RgbaImage | null. */
export function extractIcon(
  spriteId: number,
  idx: Idx,
  timBufs: (Buffer | null)[],
  pages: Map<number, RgbaImage>,
): RgbaImage | null {
  const ref = idx.map.get(spriteId);
  if (!ref) return null;
  const fb = timBufs[ref.fileID];
  if (!fb) return null;
  let blob: Buffer;
  try {
    blob = readTimChunk(fb, ref.pos);
  } catch {
    return null;
  }
  if (!blob.length) return null;
  try {
    return composeIcon(blob, pages);
  } catch {
    return null;
  }
}

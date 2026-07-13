// Minimal DDS decoder -> RGBA8888. Handles DXT1 (BC1), DXT3 (BC2), DXT5 (BC3)
// and uncompressed 16/24/32-bpp surfaces. Enough for the client's icon pages.
import type { RgbaImage } from "./types";

interface Masks {
  r: number;
  g: number;
  b: number;
  a: number;
}

function rgb565(c: number, out: Uint8Array, o: number): void {
  const r = (c >> 11) & 0x1f;
  const g = (c >> 5) & 0x3f;
  const b = c & 0x1f;
  out[o] = (r << 3) | (r >> 2);
  out[o + 1] = (g << 2) | (g >> 4);
  out[o + 2] = (b << 3) | (b >> 2);
}

function decodeDxt(data: Buffer, width: number, height: number, fourcc: string): Buffer {
  const out = Buffer.alloc(width * height * 4);
  const bc1 = fourcc === "DXT1";
  const blockBytes = bc1 ? 8 : 16;
  const c = new Uint8Array(16); // 4 colors * RGBA
  let off = 0;
  for (let by = 0; by < height; by += 4) {
    for (let bx = 0; bx < width; bx += 4) {
      let alpha: Uint8Array | null = null; // per-texel alpha (0..255), for BC2/BC3
      let colorOff = off;
      if (fourcc === "DXT3") {
        alpha = new Uint8Array(16);
        for (let i = 0; i < 8; i++) {
          const b = data[off + i];
          alpha[i * 2] = (b & 0x0f) * 17;
          alpha[i * 2 + 1] = (b >> 4) * 17;
        }
        colorOff = off + 8;
      } else if (fourcc === "DXT5") {
        alpha = new Uint8Array(16);
        const a0 = data[off];
        const a1 = data[off + 1];
        const al: number[] = [a0, a1];
        if (a0 > a1) {
          for (let i = 1; i < 7; i++) al.push(Math.round(((7 - i) * a0 + i * a1) / 7));
        } else {
          for (let i = 1; i < 5; i++) al.push(Math.round(((5 - i) * a0 + i * a1) / 5));
          al.push(0, 255);
        }
        let bits = 0n;
        for (let i = 0; i < 6; i++) bits |= BigInt(data[off + 2 + i]) << BigInt(8 * i);
        for (let i = 0; i < 16; i++) alpha[i] = al[Number((bits >> BigInt(3 * i)) & 7n)];
        colorOff = off + 8;
      }

      const c0 = data.readUInt16LE(colorOff);
      const c1 = data.readUInt16LE(colorOff + 2);
      rgb565(c0, c, 0);
      rgb565(c1, c, 4);
      c[3] = c[7] = 255;
      const transparent = bc1 && c0 <= c1; // BC1 1-bit alpha mode
      if (!transparent) {
        for (let k = 0; k < 3; k++) {
          c[8 + k] = (2 * c[k] + c[4 + k] + 1) / 3;
          c[12 + k] = (c[k] + 2 * c[4 + k] + 1) / 3;
        }
        c[11] = c[15] = 255;
      } else {
        for (let k = 0; k < 3; k++) c[8 + k] = (c[k] + c[4 + k]) >> 1;
        c[11] = 255;
        c[12] = c[13] = c[14] = 0;
        c[15] = 0; // 4th = transparent black
      }
      const idx = data.readUInt32LE(colorOff + 4);
      for (let py = 0; py < 4; py++) {
        for (let px = 0; px < 4; px++) {
          const ti = py * 4 + px;
          const sel = (idx >> (2 * ti)) & 3;
          const x = bx + px;
          const y = by + py;
          if (x >= width || y >= height) continue;
          const o = (y * width + x) * 4;
          out[o] = c[sel * 4];
          out[o + 1] = c[sel * 4 + 1];
          out[o + 2] = c[sel * 4 + 2];
          out[o + 3] = alpha ? alpha[ti] : c[sel * 4 + 3];
        }
      }
      off += blockBytes;
    }
  }
  return out;
}

function maskShift(mask: number): { shift: number; bits: number } {
  if (!mask) return { shift: 0, bits: 0 };
  // Unsigned shifts: alpha mask 0xFF000000 is negative under signed `>>`, which
  // sign-extends and makes `m >>= 1` loop forever.
  let shift = 0;
  while (((mask >>> shift) & 1) === 0 && shift < 32) shift++;
  let bits = 0;
  let m = mask >>> shift;
  while (m & 1) {
    bits++;
    m >>>= 1;
  }
  return { shift, bits };
}

function scaleChannel(val: number, bits: number): number {
  if (bits === 0) return 255;
  if (bits === 8) return val;
  return Math.round((val * 255) / ((1 << bits) - 1));
}

function decodeUncompressed(data: Buffer, width: number, height: number, bpp: number, masks: Masks): Buffer {
  const out = Buffer.alloc(width * height * 4);
  const bytes = bpp / 8;
  const R = maskShift(masks.r);
  const G = maskShift(masks.g);
  const B = maskShift(masks.b);
  const A = maskShift(masks.a);
  for (let i = 0; i < width * height; i++) {
    let px = 0;
    for (let k = 0; k < bytes; k++) px |= data[i * bytes + k] << (8 * k);
    px = px >>> 0;
    const o = i * 4;
    out[o] = scaleChannel((px & masks.r) >>> R.shift, R.bits);
    out[o + 1] = scaleChannel((px & masks.g) >>> G.shift, G.bits);
    out[o + 2] = scaleChannel((px & masks.b) >>> B.shift, B.bits);
    out[o + 3] = masks.a ? scaleChannel((px & masks.a) >>> A.shift, A.bits) : 255;
  }
  return out;
}

/** dds: Buffer of a full .dds file. Returns {width,height,rgba}. */
export function decodeDDS(dds: Buffer): RgbaImage {
  if (dds.toString("latin1", 0, 4) !== "DDS ") throw new Error("not a DDS");
  const height = dds.readUInt32LE(12);
  const width = dds.readUInt32LE(16);
  const pfFlags = dds.readUInt32LE(80);
  const fourcc = dds.toString("latin1", 84, 88);
  const rgbBitCount = dds.readUInt32LE(88);
  const masks: Masks = {
    r: dds.readUInt32LE(92),
    g: dds.readUInt32LE(96),
    b: dds.readUInt32LE(100),
    a: dds.readUInt32LE(104),
  };
  const body = dds.subarray(128);
  let rgba: Buffer;
  if (pfFlags & 0x4) {
    if (fourcc === "DXT1" || fourcc === "DXT3" || fourcc === "DXT5") {
      rgba = decodeDxt(body, width, height, fourcc);
    } else {
      throw new Error("unsupported fourcc " + fourcc);
    }
  } else {
    rgba = decodeUncompressed(body, width, height, rgbBitCount || 32, masks);
  }
  return { width, height, rgba };
}

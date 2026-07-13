// Parser for TClientCmd.tif — the compiled TCML UI. We only need the item-slot
// image list: a TCML_TYPE_IMAGELIST (0x0A) component whose Nth child's ENABLE
// sprite id is the icon for icon-index N.
//
// Node layout (TCMLParser::LoadFRAME): dwID(u32), bType(u8), menu[12](u32),
// images[2](u32; [0]=ENABLE), tooltipID, fontID, style, color, snd (u32×5),
// marginH,marginV,posX,posY,width,height (i32×6), display,align (u8×2),
// TSATR(68 bytes), tooltip(i32 len+bytes), text(i32 len+bytes),
// childCount(i32), children×node. File: i32 frameCount, then frameCount nodes.
import fs from "node:fs";

const IMAGELIST = 0x0a;
const NULL_ID = 0xffffffff;

/** Returns index -> sprite id (null for empty slots). */
export function loadIconList(tifPath: string): (number | null)[] {
  const buf = fs.readFileSync(tifPath);
  let p = 0;
  const i32 = (): number => {
    const v = buf.readInt32LE(p);
    p += 4;
    return v;
  };
  const u32 = (): number => {
    const v = buf.readUInt32LE(p);
    p += 4;
    return v;
  };

  let best: { count: number; sprites: number[] } | null = null;

  function node(): number {
    u32(); // dwID
    const type = buf.readUInt8(p);
    p += 1;
    p += 48; // menu[12]
    const img0 = u32();
    u32(); // images[1]
    p += 20; // tooltipID,fontID,style,color,snd
    p += 24; // marginH,marginV,posX,posY,width,height
    p += 2; // display, align
    p += 68; // TSATR
    const tlen = i32();
    if (tlen > 0) p += tlen;
    const txlen = i32();
    if (txlen > 0) p += txlen;
    const childCount = i32();
    const childSprites: number[] | null = type === IMAGELIST ? new Array<number>(childCount) : null;
    for (let i = 0; i < childCount; i++) {
      const cImg0 = node();
      if (childSprites) childSprites[i] = cImg0;
    }
    // The item-slot list is the IMAGELIST with the most children.
    if (type === IMAGELIST && childSprites && (!best || childCount > best.count)) {
      best = { count: childCount, sprites: childSprites };
    }
    return img0;
  }

  const frameCount = i32();
  for (let i = 0; i < frameCount; i++) node();

  if (!best) throw new Error("No item-slot image list found in " + tifPath);
  return (best as { count: number; sprites: number[] }).sprites.map((s) => (s === NULL_ID || s === 0 ? null : s));
}

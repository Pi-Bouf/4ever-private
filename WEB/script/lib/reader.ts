// Sequential little-endian reader with MFC-CArchive CString support.
// The .tcd files are MFC `CArchive` dumps; strings use MFC's length-prefix
// scheme and are CP949 (Korean codepage) in this MBCS build.

const cp949 = new TextDecoder("euc-kr"); // Node full-ICU: decodes CP949/EUC-KR

export class Reader {
  private b: Buffer;
  private p = 0;

  constructor(buf: Buffer) {
    this.b = buf;
  }

  get eof(): boolean {
    return this.p >= this.b.length;
  }
  get remaining(): number {
    return this.b.length - this.p;
  }

  u8(): number {
    return this.b.readUInt8(this.p++);
  }
  u16(): number {
    const v = this.b.readUInt16LE(this.p);
    this.p += 2;
    return v;
  }
  u32(): number {
    const v = this.b.readUInt32LE(this.p);
    this.p += 4;
    return v;
  }
  i32(): number {
    const v = this.b.readInt32LE(this.p);
    this.p += 4;
    return v;
  }
  f32(): number {
    const v = this.b.readFloatLE(this.p);
    this.p += 4;
    return v;
  }
  skip(n: number): void {
    this.p += n;
  }
  bytes(n: number): Buffer {
    const s = this.b.subarray(this.p, this.p + n);
    this.p += n;
    return s;
  }

  // MFC `CArchive::operator>>(CString&)` length-prefix + CP949 bytes.
  // byte len; if 0xFF -> word len; if that word is 0xFFFF -> dword len.
  // (0xFFFE would flag a UNICODE string — never occurs for item names here.)
  cstring(): string {
    let len = this.u8();
    if (len === 0xff) {
      len = this.u16();
      if (len === 0xfffe) {
        len = this.u16();
        if (len === 0xffff) len = this.u32();
        const raw = this.bytes(len * 2);
        return new TextDecoder("utf-16le").decode(raw);
      }
      if (len === 0xffff) len = this.u32();
    }
    const raw = this.bytes(len);
    return cp949.decode(raw).replace(/\0+$/, "");
  }
}

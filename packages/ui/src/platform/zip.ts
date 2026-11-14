/**
 * A small zip writer for Download Notebook (PRD §5): entries are stored as they are (no compression), with a
 * CRC-32 each and UTF-8 names. Without Zip64 an archive holds up to 65,535 entries and 4 GiB.
 */

/** A file in the archive, or a folder when `bytes` is absent. Paths use `/` and never start with one. */
export interface ZipEntry {
  readonly path: string;
  readonly bytes?: Uint8Array;
  /** Milliseconds since the epoch; the archive keeps local time to two seconds, as zip does. */
  readonly modified?: number;
}

const LOCAL_HEADER = 0x04034b50;
const CENTRAL_HEADER = 0x02014b50;
const END_OF_CENTRAL = 0x06054b50;
const VERSION = 20; // 2.0: folders and stored entries
const UTF8_NAMES = 0x0800;
const MAX_ENTRIES = 0xffff;
const MAX_SIZE = 0xffffffff;
const DOS_EPOCH_YEAR = 1980;
const FOLDER_ATTRIBUTE = 0x10;

let crcTable: Uint32Array | null = null;

/** The CRC-32 (IEEE) of `bytes`, as zip, gzip and PNG use it. */
export function crc32(bytes: Uint8Array): number {
  if (!crcTable) {
    crcTable = new Uint32Array(256);
    for (let n = 0; n < 256; n++) {
      let c = n;
      for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
      crcTable[n] = c >>> 0;
    }
  }
  let crc = 0xffffffff;
  for (const b of bytes) crc = crcTable[(crc ^ b) & 0xff]! ^ (crc >>> 8);
  return (crc ^ 0xffffffff) >>> 0;
}

/** A time as MS-DOS packs it: [time, date], local time, two-second steps, 1980 at the earliest. */
function dosTime(ms: number): [number, number] {
  const d = new Date(ms);
  if (!Number.isFinite(d.getTime()) || d.getFullYear() < DOS_EPOCH_YEAR) return [0, (1 << 5) | 1];
  const time = (d.getHours() << 11) | (d.getMinutes() << 5) | (d.getSeconds() >> 1);
  const date = ((d.getFullYear() - DOS_EPOCH_YEAR) << 9) | ((d.getMonth() + 1) << 5) | d.getDate();
  return [time, date];
}

/** Write `entries` into one zip archive. Throws when the archive would need Zip64. */
export function zip(entries: readonly ZipEntry[]): Uint8Array {
  if (entries.length > MAX_ENTRIES) throw new Error('The notebook has too many files for a zip archive.');
  const encoder = new TextEncoder();
  const parts: Uint8Array[] = [];
  const central: Uint8Array[] = [];
  let offset = 0;
  for (const e of entries) {
    const folder = e.bytes === undefined;
    const data = e.bytes ?? new Uint8Array();
    const name = encoder.encode(folder && !e.path.endsWith('/') ? `${e.path}/` : e.path);
    const crc = folder ? 0 : crc32(data);
    const [time, date] = dosTime(e.modified ?? Date.now());
    if (data.length > MAX_SIZE || offset > MAX_SIZE) throw new Error('The notebook is too large to zip.');

    const local = new Uint8Array(30 + name.length);
    const l = new DataView(local.buffer);
    l.setUint32(0, LOCAL_HEADER, true);
    l.setUint16(4, VERSION, true);
    l.setUint16(6, UTF8_NAMES, true);
    l.setUint16(8, 0, true); // stored
    l.setUint16(10, time, true);
    l.setUint16(12, date, true);
    l.setUint32(14, crc, true);
    l.setUint32(18, data.length, true);
    l.setUint32(22, data.length, true);
    l.setUint16(26, name.length, true);
    l.setUint16(28, 0, true);
    local.set(name, 30);

    const head = new Uint8Array(46 + name.length);
    const c = new DataView(head.buffer);
    c.setUint32(0, CENTRAL_HEADER, true);
    c.setUint16(4, VERSION, true);
    c.setUint16(6, VERSION, true);
    c.setUint16(8, UTF8_NAMES, true);
    c.setUint16(10, 0, true);
    c.setUint16(12, time, true);
    c.setUint16(14, date, true);
    c.setUint32(16, crc, true);
    c.setUint32(20, data.length, true);
    c.setUint32(24, data.length, true);
    c.setUint16(28, name.length, true);
    // Extra field, comment, disk number and internal attributes stay 0.
    c.setUint32(38, folder ? FOLDER_ATTRIBUTE : 0, true);
    c.setUint32(42, offset, true);
    head.set(name, 46);

    parts.push(local, data);
    central.push(head);
    offset += local.length + data.length;
  }
  const centralSize = central.reduce((n, p) => n + p.length, 0);
  if (offset > MAX_SIZE || offset + centralSize > MAX_SIZE)
    throw new Error('The notebook is too large to zip.');
  const end = new Uint8Array(22);
  const v = new DataView(end.buffer);
  v.setUint32(0, END_OF_CENTRAL, true);
  v.setUint16(8, entries.length, true);
  v.setUint16(10, entries.length, true);
  v.setUint32(12, centralSize, true);
  v.setUint32(16, offset, true);

  const out = new Uint8Array(offset + centralSize + end.length);
  let at = 0;
  for (const p of [...parts, ...central, end]) {
    out.set(p, at);
    at += p.length;
  }
  return out;
}

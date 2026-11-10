/**
 * Reading a file's bytes into text and writing them back unchanged. A byte-order mark is
 * trusted. Without one, the bytes are checked as UTF-8 rather than assumed to be UTF-8. Bytes
 * that aren't valid UTF-8 fall back to Latin-1 and open read-only, instead of being rewritten
 * through a guess.
 *
 * The read-only part matters. A lenient UTF-8 decoder replaces bytes it can't read with U+FFFD,
 * so a Latin-1 file opened as UTF-8 loses its accented characters the moment it is saved.
 *
 * Pure (bytes in, bytes out, no file system), and it uses no `TextDecoder`, so it runs the same
 * everywhere.
 */

/** An encoding a file can be read and written with. */
export type TextEncodingName = 'utf-8' | 'utf-16le' | 'utf-16be' | 'latin-1';

/** How to write a document back: its encoding and whether it starts with a BOM. */
export interface TextFileFormat {
  readonly encoding: TextEncodingName;
  readonly bom: boolean;
  /** A short name for the status bar. */
  readonly displayName: string;
}

export const utf8NoBom: TextFileFormat = { encoding: 'utf-8', bom: false, displayName: 'UTF-8' };
export const utf8Bom: TextFileFormat = { encoding: 'utf-8', bom: true, displayName: 'UTF-8 BOM' };
export const utf16Le: TextFileFormat = { encoding: 'utf-16le', bom: true, displayName: 'UTF-16 LE' };
export const utf16Be: TextFileFormat = { encoding: 'utf-16be', bom: true, displayName: 'UTF-16 BE' };
/**
 * Latin-1: the fallback for bytes that aren't UTF-8. Every byte 0x00–0xFF maps to a code point
 * and back, so the file round-trips byte for byte even if the guess about what it means is wrong.
 */
export const latin1: TextFileFormat = { encoding: 'latin-1', bom: false, displayName: 'Latin-1' };

/**
 * How the encoding was decided:
 * - `declared`: a BOM said so, or the user picked it.
 * - `valid-utf8`: no BOM, and the bytes are valid UTF-8.
 * - `guessed`: not UTF-8, read as Latin-1. The document is read-only until the user confirms an
 *   encoding, because saving would write the guess back.
 */
export type TextDecodeConfidence = 'declared' | 'valid-utf8' | 'guessed';

export interface DecodedText {
  readonly text: string;
  readonly format: TextFileFormat;
  readonly confidence: TextDecodeConfidence;
}

/** True when the encoding was guessed, so the document must open read-only. */
export function isReadOnlyDecode(d: DecodedText): boolean {
  return d.confidence === 'guessed';
}

/** Decode `bytes`, reporting how much was guessed. */
export function readText(bytes: Uint8Array): DecodedText {
  if (startsWith(bytes, [0xef, 0xbb, 0xbf])) return declared(utf8Bom, bytes, 3);
  if (startsWith(bytes, [0xff, 0xfe])) return declared(utf16Le, bytes, 2);
  if (startsWith(bytes, [0xfe, 0xff])) return declared(utf16Be, bytes, 2);
  const utf8 = decodeUtf8Strict(bytes, 0);
  if (utf8 !== undefined) return { text: utf8, format: utf8NoBom, confidence: 'valid-utf8' };
  return { text: decodeLatin1(bytes, 0), format: latin1, confidence: 'guessed' };
}

/** Read `bytes` again as `format`, once the user has chosen it. This makes the document saveable. */
export function readTextAs(bytes: Uint8Array, format: TextFileFormat): DecodedText {
  const bom = preamble(format);
  return declared(format, bytes, format.bom && startsWith(bytes, bom) ? bom.length : 0);
}

/** Encode `text` as `format`, with its BOM when it has one. */
export function encodeText(text: string, format: TextFileFormat): Uint8Array {
  const body = encodeBody(text, format.encoding);
  if (!format.bom) return body;
  const bom = preamble(format);
  const out = new Uint8Array(bom.length + body.length);
  out.set(bom, 0);
  out.set(body, bom.length);
  return out;
}

function declared(format: TextFileFormat, bytes: Uint8Array, skip: number): DecodedText {
  let text: string;
  switch (format.encoding) {
    case 'utf-8':
      // A declared UTF-8 file is decoded leniently: the BOM said what it is.
      text = decodeUtf8Strict(bytes, skip) ?? decodeUtf8Lenient(bytes, skip);
      break;
    case 'utf-16le':
      text = decodeUtf16(bytes, skip, true);
      break;
    case 'utf-16be':
      text = decodeUtf16(bytes, skip, false);
      break;
    case 'latin-1':
      text = decodeLatin1(bytes, skip);
      break;
  }
  return { text, format, confidence: 'declared' };
}

function preamble(format: TextFileFormat): number[] {
  switch (format.encoding) {
    case 'utf-8':
      return [0xef, 0xbb, 0xbf];
    case 'utf-16le':
      return [0xff, 0xfe];
    case 'utf-16be':
      return [0xfe, 0xff];
    case 'latin-1':
      return [];
  }
}

function startsWith(bytes: Uint8Array, prefix: readonly number[]): boolean {
  if (prefix.length === 0 || bytes.length < prefix.length) return false;
  return prefix.every((b, i) => bytes[i] === b);
}

// ----- UTF-8 -----

/** Decode UTF-8, returning `undefined` on any invalid sequence, overlong form or surrogate. */
function decodeUtf8Strict(bytes: Uint8Array, start: number): string | undefined {
  return decodeUtf8(bytes, start, true);
}

function decodeUtf8Lenient(bytes: Uint8Array, start: number): string {
  return decodeUtf8(bytes, start, false) ?? '';
}

function decodeUtf8(bytes: Uint8Array, start: number, strict: boolean): string | undefined {
  const parts: string[] = [];
  let units: number[] = [];
  const flush = (): void => {
    parts.push(String.fromCharCode(...units));
    units = [];
  };
  let i = start;
  while (i < bytes.length) {
    const b0 = bytes[i]!;
    let cp = -1;
    let need = 0;
    let min = 0;
    if (b0 < 0x80) {
      cp = b0;
    } else if (b0 >= 0xc2 && b0 <= 0xdf) {
      cp = b0 & 0x1f;
      need = 1;
      min = 0x80;
    } else if (b0 >= 0xe0 && b0 <= 0xef) {
      cp = b0 & 0x0f;
      need = 2;
      min = 0x800;
    } else if (b0 >= 0xf0 && b0 <= 0xf4) {
      cp = b0 & 0x07;
      need = 3;
      min = 0x10000;
    }
    let ok = cp >= 0;
    for (let k = 1; ok && k <= need; k++) {
      const b = bytes[i + k];
      if (b === undefined || (b & 0xc0) !== 0x80) ok = false;
      else cp = (cp << 6) | (b & 0x3f);
    }
    if (ok && need > 0 && (cp < min || cp > 0x10ffff || (cp >= 0xd800 && cp <= 0xdfff))) ok = false;
    if (!ok) {
      if (strict) return undefined;
      units.push(0xfffd);
      i++;
    } else {
      if (cp >= 0x10000) {
        const v = cp - 0x10000;
        units.push(0xd800 + (v >> 10), 0xdc00 + (v & 0x3ff));
      } else {
        units.push(cp);
      }
      i += need + 1;
    }
    if (units.length >= 8192) flush();
  }
  flush();
  return parts.join('');
}

function encodeUtf8(text: string): Uint8Array {
  const out = new Uint8Array(text.length * 3);
  let n = 0;
  for (let i = 0; i < text.length; i++) {
    let cp = text.charCodeAt(i);
    if (cp >= 0xd800 && cp <= 0xdbff && i + 1 < text.length) {
      const lo = text.charCodeAt(i + 1);
      if (lo >= 0xdc00 && lo <= 0xdfff) {
        cp = 0x10000 + ((cp - 0xd800) << 10) + (lo - 0xdc00);
        i++;
      }
    }
    if (cp >= 0xd800 && cp <= 0xdfff) cp = 0xfffd; // a lone surrogate can't be written as UTF-8
    if (cp < 0x80) {
      out[n++] = cp;
    } else if (cp < 0x800) {
      out[n++] = 0xc0 | (cp >> 6);
      out[n++] = 0x80 | (cp & 0x3f);
    } else if (cp < 0x10000) {
      out[n++] = 0xe0 | (cp >> 12);
      out[n++] = 0x80 | ((cp >> 6) & 0x3f);
      out[n++] = 0x80 | (cp & 0x3f);
    } else {
      out[n++] = 0xf0 | (cp >> 18);
      out[n++] = 0x80 | ((cp >> 12) & 0x3f);
      out[n++] = 0x80 | ((cp >> 6) & 0x3f);
      out[n++] = 0x80 | (cp & 0x3f);
    }
  }
  return out.slice(0, n);
}

// ----- UTF-16 and Latin-1 -----

function decodeUtf16(bytes: Uint8Array, start: number, littleEndian: boolean): string {
  const parts: string[] = [];
  let units: number[] = [];
  for (let i = start; i + 1 < bytes.length; i += 2) {
    units.push(littleEndian ? bytes[i]! | (bytes[i + 1]! << 8) : (bytes[i]! << 8) | bytes[i + 1]!);
    if (units.length >= 8192) {
      parts.push(String.fromCharCode(...units));
      units = [];
    }
  }
  if ((bytes.length - start) % 2 === 1) units.push(0xfffd); // a dangling odd byte
  parts.push(String.fromCharCode(...units));
  return parts.join('');
}

function encodeUtf16(text: string, littleEndian: boolean): Uint8Array {
  const out = new Uint8Array(text.length * 2);
  for (let i = 0; i < text.length; i++) {
    const u = text.charCodeAt(i);
    out[i * 2 + (littleEndian ? 0 : 1)] = u & 0xff;
    out[i * 2 + (littleEndian ? 1 : 0)] = u >> 8;
  }
  return out;
}

function decodeLatin1(bytes: Uint8Array, start: number): string {
  const parts: string[] = [];
  for (let i = start; i < bytes.length; i += 8192) {
    parts.push(String.fromCharCode(...bytes.subarray(i, Math.min(bytes.length, i + 8192))));
  }
  return parts.join('');
}

function encodeLatin1(text: string): Uint8Array {
  const out = new Uint8Array(text.length);
  for (let i = 0; i < text.length; i++) {
    const u = text.charCodeAt(i);
    out[i] = u <= 0xff ? u : 0x3f; // '?' for a character Latin-1 can't hold
  }
  return out;
}

function encodeBody(text: string, encoding: TextEncodingName): Uint8Array {
  switch (encoding) {
    case 'utf-8':
      return encodeUtf8(text);
    case 'utf-16le':
      return encodeUtf16(text, true);
    case 'utf-16be':
      return encodeUtf16(text, false);
    case 'latin-1':
      return encodeLatin1(text);
  }
}

import { describe, expect, it } from 'vitest';
import {
  computeStats,
  countLineEndings,
  encodeText,
  isReadOnlyDecode,
  latin1,
  newlineLabel,
  normalizeLineEndings,
  readText,
  readTextAs,
  TextDocument,
  utf8NoBom,
} from '../src/index.ts';

const utf8 = (s: string): Uint8Array => new TextEncoder().encode(s);
const latin1Bytes = (s: string): Uint8Array => Uint8Array.from(s, (c) => c.charCodeAt(0));

describe('text file codec', () => {
  it('round-trips UTF-8 without a BOM', () => {
    const bytes = utf8('héllo \u{1F600}');
    const read = readText(bytes);
    expect(read.text).toBe('héllo \u{1F600}');
    expect(read.format.bom).toBe(false);
    expect(read.confidence).toBe('valid-utf8');
    expect(encodeText(read.text, read.format)).toEqual(bytes);
  });

  it('detects and keeps a UTF-8 BOM', () => {
    const bytes = Uint8Array.of(0xef, 0xbb, 0xbf, ...utf8('hi'));
    const read = readText(bytes);
    expect(read.text).toBe('hi');
    expect(read.format.displayName).toBe('UTF-8 BOM');
    expect(read.confidence).toBe('declared');
    expect(isReadOnlyDecode(read)).toBe(false);
    expect(encodeText(read.text, read.format)).toEqual(bytes);
  });

  it('detects and keeps UTF-16 LE and BE', () => {
    const le = Uint8Array.of(0xff, 0xfe, 0x6f, 0x00, 0x6b, 0x00);
    const readLe = readText(le);
    expect(readLe.text).toBe('ok');
    expect(readLe.format.displayName).toBe('UTF-16 LE');
    expect(encodeText(readLe.text, readLe.format)).toEqual(le);
    const be = Uint8Array.of(0xfe, 0xff, 0x00, 0x6f, 0x00, 0x6b);
    const readBe = readText(be);
    expect(readBe.text).toBe('ok');
    expect(readBe.format.displayName).toBe('UTF-16 BE');
    expect(encodeText(readBe.text, readBe.format)).toEqual(be);
  });

  it('writes nothing for an empty new document', () => {
    expect(encodeText('', utf8NoBom)).toHaveLength(0);
  });

  it('opens bytes that are not UTF-8 read-only instead of mangling them', () => {
    const read = readText(latin1Bytes('café'));
    expect(read.confidence).toBe('guessed');
    expect(isReadOnlyDecode(read)).toBe(true);
    expect(read.text).not.toContain('�');
  });

  it('rejects overlong forms and encoded surrogates as UTF-8', () => {
    expect(readText(Uint8Array.of(0xc0, 0xaf)).confidence).toBe('guessed');
    expect(readText(Uint8Array.of(0xed, 0xa0, 0x80)).confidence).toBe('guessed');
    expect(readText(Uint8Array.of(0xe2, 0x82)).confidence).toBe('guessed');
  });

  it('opens a UTF-8 BOM file with invalid bytes read-only', () => {
    const read = readText(Uint8Array.of(0xef, 0xbb, 0xbf, 0x61, 0xff, 0x62));
    expect(read.confidence).toBe('guessed');
    expect(isReadOnlyDecode(read)).toBe(true);
    expect(read.format.encoding).toBe('utf-8');
  });

  it('opens UTF-16 with a dangling odd byte read-only', () => {
    const le = readText(Uint8Array.of(0xff, 0xfe, 0x61, 0x00, 0x62));
    expect(le.confidence).toBe('guessed');
    expect(le.format.encoding).toBe('utf-16le');
    const be = readText(Uint8Array.of(0xfe, 0xff, 0x00, 0x61, 0x62));
    expect(be.confidence).toBe('guessed');
    expect(be.format.encoding).toBe('utf-16be');
  });

  it('round-trips an undecodable file byte for byte', () => {
    const bytes = Uint8Array.from({ length: 256 }, (_, i) => i);
    const read = readText(bytes);
    expect(read.confidence).toBe('guessed');
    expect(encodeText(read.text, read.format)).toEqual(bytes);
  });

  it('turns the guess into a declaration once the user picks an encoding', () => {
    const chosen = readTextAs(latin1Bytes('café'), latin1);
    expect(chosen.text).toBe('café');
    expect(chosen.confidence).toBe('declared');
    expect(isReadOnlyDecode(chosen)).toBe(false);
  });

  it('reads an empty file as ordinary UTF-8', () => {
    const read = readText(new Uint8Array());
    expect(read.text).toBe('');
    expect(isReadOnlyDecode(read)).toBe(false);
  });
});

describe('line endings and stats', () => {
  it('counts LF and CRLF and ignores a lone CR', () => {
    expect(countLineEndings('a\nb\r\nc\rd\n')).toEqual({ lf: 2, crlf: 1 });
  });

  it('normalizes without doubling CRs, and returns the same string when nothing changes', () => {
    expect(normalizeLineEndings('a\r\nb\nc', 'crlf')).toBe('a\r\nb\r\nc');
    expect(normalizeLineEndings('a\r\nb\nc', 'lf')).toBe('a\nb\nc');
    const same = 'a\nb';
    expect(normalizeLineEndings(same, 'lf')).toBe(same);
  });

  it('computes words, graphemes, lines, reading time and a mixed newline label', () => {
    const stats = computeStats(TextDocument.fromString('one two\r\nthree \u{1F600}\n'));
    expect(stats.words).toBe(4);
    expect(stats.characters).toBe(16);
    expect(stats.lines).toBe(3);
    expect(stats.readingMinutes).toBe(1);
    expect(newlineLabel(stats)).toBe('LF (mixed)');
    expect(computeStats(TextDocument.empty).readingMinutes).toBe(0);
  });
});

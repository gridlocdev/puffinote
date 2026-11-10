import { describe, expect, it } from 'vitest';
import { TextDocument } from '../src/index.ts';

describe('TextDocument.changeSince', () => {
  it('reports the single edit that made a snapshot', () => {
    const base = TextDocument.fromString('hello world');
    expect(base.changeSince(base)).toEqual({ start: 0, removedLength: 0, insertedLength: 0 });
    expect(base.insert(5, ',').changeSince(base)).toEqual({ start: 5, removedLength: 0, insertedLength: 1 });
    expect(base.delete(0, 6).changeSince(base)).toEqual({ start: 0, removedLength: 6, insertedLength: 0 });
    expect(base.replace(6, 5, 'there!').changeSince(base)).toEqual({
      start: 6,
      removedLength: 5,
      insertedLength: 6,
    });
  });

  it('knows nothing about snapshots further apart', () => {
    const base = TextDocument.fromString('abc');
    const one = base.insert(0, 'x');
    expect(one.insert(0, 'y').changeSince(base)).toBeNull();
    expect(base.changeSince(one)).toBeNull();
    expect(TextDocument.fromString('abc').changeSince(base)).toBeNull();
  });
});

describe('add buffer', () => {
  it('reads correctly after a large paste followed by typing', () => {
    const paste = Array.from({ length: 100000 }, (_, i) => `line ${i}`).join('\n');
    let doc = TextDocument.fromString('start\n').insert(6, paste);
    let expected = 'start\n' + paste;
    for (let k = 0; k < 500; k++) {
      const at = (k * 7919) % (doc.length + 1);
      const ch = String.fromCharCode(97 + (k % 26));
      doc = doc.insert(at, ch);
      expected = expected.slice(0, at) + ch + expected.slice(at);
    }
    expect(doc.getText()).toBe(expected);
    const lines = expected.split('\n');
    for (const line of [0, 1, 500, 50000, lines.length - 1]) expect(doc.getLineText(line)).toBe(lines[line]);
    for (const at of [0, 6, 300000, expected.length - 1]) expect(doc.charAt(at)).toBe(expected[at]);
  });

  it('reads a typed run that crosses add-buffer chunks', () => {
    let doc = TextDocument.fromString('[]');
    let typed = '';
    for (let k = 0; k < 40000; k++) {
      const ch = String.fromCharCode(97 + (k % 26)) + (k % 1000 === 999 ? '\n' : '');
      doc = doc.insert(1 + typed.length, ch);
      typed += ch;
    }
    expect(doc.getText()).toBe('[' + typed + ']');
    expect(doc.getText(16000, 800)).toBe(('[' + typed).slice(16000, 16800));
    expect(doc.getLineText(20)).toBe(typed.split('\n')[20]);
  });

  it('keeps typing after a paste cheap', () => {
    let doc = TextDocument.fromString('').insert(0, 'x'.repeat(5_000_000));
    const started = performance.now();
    for (let k = 0; k < 200; k++) {
      doc = doc.insert(doc.length, 'y');
      doc.getLineText(0);
    }
    // Each keystroke used to re-flatten the whole add buffer (about 90ms for 200 here).
    expect(performance.now() - started).toBeLessThan(500);
    expect(doc.length).toBe(5_000_200);
  });
});

function expectLineStartsMatchFromScratch(doc: TextDocument): void {
  const fresh = TextDocument.fromString(doc.getText());
  expect(doc.lineCount).toBe(fresh.lineCount);
  for (let line = 0; line < fresh.lineCount; line++) {
    expect(doc.getLineStart(line)).toBe(fresh.getLineStart(line));
  }
}

/** A small seeded PRNG, so the fuzz test is repeatable. */
function rng(seed: number): (n: number) => number {
  let s = seed >>> 0;
  return (n) => {
    s = (s * 1664525 + 1013904223) >>> 0;
    return s % n;
  };
}

describe('TextDocument', () => {
  it('round-trips a string', () => {
    const doc = TextDocument.fromString('hello world');
    expect(doc.length).toBe(11);
    expect(doc.getText()).toBe('hello world');
    expect(doc.getText(6, 5)).toBe('world');
  });

  it('has one empty line when empty', () => {
    expect(TextDocument.empty.length).toBe(0);
    expect(TextDocument.empty.lineCount).toBe(1);
    expect(TextDocument.empty.getLineText(0)).toBe('');
  });

  it('returns a new document from insert and leaves the old one alone', () => {
    const a = TextDocument.fromString('abcd');
    const b = a.insert(2, 'XY');
    expect(a.getText()).toBe('abcd');
    expect(b.getText()).toBe('abXYcd');
    expect(a.insert(0, '[').getText()).toBe('[abcd');
    expect(a.insert(4, ']').getText()).toBe('abcd]');
  });

  it('deletes and replaces ranges', () => {
    const a = TextDocument.fromString('abcdef');
    expect(a.delete(2, 2).getText()).toBe('abef');
    expect(a.delete(0, 6).getText()).toBe('');
    expect(TextDocument.fromString('the quick fox').replace(4, 5, 'slow').getText()).toBe('the slow fox');
  });

  it('reads characters and ranges across piece boundaries', () => {
    let doc = TextDocument.fromString('ABCDEFGH');
    doc = doc.insert(2, '12');
    doc = doc.insert(6, 'xy');
    const expected = 'AB12CDxyEFGH';
    expect(doc.getText()).toBe(expected);
    for (let i = 0; i < expected.length; i++) expect(doc.charAt(i)).toBe(expected[i]);
    expect(doc.getText(2, 6)).toBe('12CDxy');
    expect(doc.getText(expected.length - 1, 1)).toBe('H');
  });

  it('splits lines on LF and keeps CR as line content', () => {
    const doc = TextDocument.fromString('one\ntwo\nthree');
    expect(doc.lineCount).toBe(3);
    expect([0, 1, 2].map((l) => doc.getLineText(l))).toEqual(['one', 'two', 'three']);
    const trailing = TextDocument.fromString('a\n');
    expect(trailing.lineCount).toBe(2);
    expect(trailing.getLineText(1)).toBe('');
    const crlf = TextDocument.fromString('a\r\nb');
    expect(crlf.getLineText(0)).toBe('a\r');
    expect(crlf.getLineText(1)).toBe('b');
  });

  it.each([
    [0, 0, 0],
    [3, 0, 3],
    [4, 1, 0],
    [6, 1, 2],
  ])('maps offset %i to line %i column %i', (offset, line, column) => {
    expect(TextDocument.fromString('abc\ndef').getPosition(offset)).toEqual({ line, column });
  });

  it('makes getOffset the inverse of getPosition', () => {
    const doc = TextDocument.fromString('hello\nworld\n!');
    for (let off = 0; off <= doc.length; off++) {
      const p = doc.getPosition(off);
      expect(doc.getOffset(p.line, p.column)).toBe(off);
    }
  });

  it('stays consistent across many edits', () => {
    let doc = TextDocument.fromString('');
    doc = doc.insert(0, 'Hello');
    doc = doc.insert(5, ' World');
    doc = doc.replace(0, 5, 'Goodbye');
    doc = doc.delete(7, 6);
    expect(doc.getText()).toBe('Goodbye');
  });

  it('coalesces sequential typing into few pieces', () => {
    let doc = TextDocument.fromString('start: ');
    for (let i = 0; i < 50; i++) doc = doc.insert(doc.length, 'x');
    expect(doc.getText()).toBe('start: ' + 'x'.repeat(50));
    expect(doc.pieceCount).toBeLessThanOrEqual(3);
  });

  it('patches line starts for the boundary cases', () => {
    let doc = TextDocument.fromString('abcd');
    void doc.lineCount;
    doc = doc.insert(2, 'X\nY');
    expect(doc.lineCount).toBe(2);
    expectLineStartsMatchFromScratch(doc);

    doc = TextDocument.fromString('ab\ncd');
    void doc.lineCount;
    doc = doc.delete(2, 1);
    expect(doc.lineCount).toBe(1);
    expectLineStartsMatchFromScratch(doc);

    doc = TextDocument.fromString('one\ntwo\nthree');
    void doc.lineCount;
    expectLineStartsMatchFromScratch(doc.replace(2, 4, 'X\nY\nZ'));

    doc = TextDocument.fromString('a\nb\nc');
    void doc.lineCount;
    doc = doc.delete(2, 2);
    expect(doc.getText()).toBe('a\nc');
    expectLineStartsMatchFromScratch(doc);
  });

  it('keeps patched line starts equal to a fresh scan across random edits', () => {
    const next = rng(0xc0ffee);
    const chunk = (): string => {
      let s = '';
      for (let n = next(5); n > 0; n--) s += ['\n', '\r', '\n', 'a', 'b', 'c'][next(6)];
      return s;
    };
    let doc = TextDocument.fromString('line one\nline two\r\nthree\n\nlast');
    for (let step = 0; step < 3000; step++) {
      void doc.lineCount;
      const len = doc.length;
      const offset = next(len + 1);
      const span = next(Math.min(6, len - offset) + 1);
      const op = next(3);
      doc =
        op === 0
          ? doc.insert(offset, chunk())
          : op === 1
            ? doc.delete(offset, span)
            : doc.replace(offset, span, chunk());
      expectLineStartsMatchFromScratch(doc);
    }
  });
});

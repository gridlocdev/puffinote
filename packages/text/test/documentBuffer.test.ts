import { describe, expect, it } from 'vitest';
import { caretAt, DocumentBuffer, nextBoundary, previousBoundary } from '../src/index.ts';

const text = (b: DocumentBuffer): string => b.document.getText();

describe('DocumentBuffer editing', () => {
  it('inserts at the caret and moves it', () => {
    const buf = new DocumentBuffer('');
    buf.insert('hi');
    expect(text(buf)).toBe('hi');
    expect(buf.selection.caret).toBe(2);
    expect(buf.isDirty).toBe(true);
  });

  it('replaces the selection', () => {
    const buf = new DocumentBuffer('hello');
    buf.selection = { anchor: 0, caret: 5 };
    buf.insert('bye');
    expect(text(buf)).toBe('bye');
    expect(buf.selection.caret).toBe(3);
  });

  it('deletes whole graphemes backwards and forwards', () => {
    const back = new DocumentBuffer('a\u{1F600}');
    back.selection = caretAt(back.document.length);
    back.backspace();
    expect(text(back)).toBe('a');
    const fwd = new DocumentBuffer('\u{1F600}z');
    fwd.deleteForward();
    expect(text(fwd)).toBe('z');
  });

  it('undoes and redoes text and selection', () => {
    const buf = new DocumentBuffer('');
    buf.insert('abc');
    buf.selection = caretAt(0);
    buf.insert('X');
    expect(text(buf)).toBe('Xabc');
    expect(buf.undo()).toBe(true);
    expect(text(buf)).toBe('abc');
    expect(buf.undo()).toBe(true);
    expect(text(buf)).toBe('');
    expect(buf.undo()).toBe(false);
    expect(buf.redo()).toBe(true);
    expect(buf.redo()).toBe(true);
    expect(text(buf)).toBe('Xabc');
  });

  it('coalesces a typing run into one undo step', () => {
    const buf = new DocumentBuffer('');
    for (const c of 'hello') buf.insert(c);
    expect(buf.undo()).toBe(true);
    expect(text(buf)).toBe('');
  });

  it('breaks the typing run on a caret jump and on a newline', () => {
    const jump = new DocumentBuffer('');
    jump.insert('ab');
    jump.selection = caretAt(0);
    jump.insert('X');
    jump.undo();
    expect(text(jump)).toBe('ab');
    const nl = new DocumentBuffer('');
    nl.insert('a');
    nl.insert('\n');
    nl.insert('b');
    nl.undo();
    expect(text(nl)).toBe('a\n');
  });

  it('is clean again after undoing or retyping back to the saved text', () => {
    const buf = new DocumentBuffer('hello');
    buf.selection = caretAt(5);
    buf.insert('!');
    expect(buf.isDirty).toBe(true);
    buf.undo();
    expect(buf.isDirty).toBe(false);
    buf.redo();
    expect(buf.isDirty).toBe(true);
    buf.backspace();
    expect(text(buf)).toBe('hello');
    expect(buf.isDirty).toBe(false);
    buf.replaceRange(0, 1, 'j');
    expect(buf.isDirty).toBe(true);
  });

  it('tracks dirty against the saved baseline and clears history on load', () => {
    const buf = new DocumentBuffer('x');
    expect(buf.isDirty).toBe(false);
    buf.insert('y');
    expect(buf.isDirty).toBe(true);
    buf.markSaved();
    expect(buf.isDirty).toBe(false);
    buf.insert('!');
    buf.load('fresh');
    expect(text(buf)).toBe('fresh');
    expect(buf.isDirty).toBe(false);
    expect(buf.canUndo).toBe(false);
  });

  it('caches stats until the text changes', () => {
    const buf = new DocumentBuffer('one two three');
    const first = buf.stats;
    expect(first.words).toBe(3);
    expect(buf.stats).toBe(first);
    buf.selection = caretAt(buf.document.length);
    buf.insert(' four');
    expect(buf.stats.words).toBe(4);
  });

  it('notifies listeners and reports the first changed line', () => {
    const buf = new DocumentBuffer('a\nb\nc');
    let changes = 0;
    let textChanges = 0;
    buf.onChange(() => changes++);
    buf.onTextChange(() => textChanges++);
    buf.selection = caretAt(4);
    buf.insert('!');
    expect(changes).toBe(2);
    expect(textChanges).toBe(1);
    expect(buf.lastChangedLine).toBe(2);
  });
});

describe('line-scoped caret navigation', () => {
  it.each([
    'hello world',
    'line one\nline two\nthree',
    'crlf\r\nlines\r\nhere',
    'emoji \u{1F600} mid\nand \u{1F468}‍\u{1F469} family',
    'accent é and more\ncombining áb',
    '\n\n\nblank lines\n\n',
  ])('matches a whole-document walk for %j', (full) => {
    const buf = new DocumentBuffer(full);
    for (let pos = 0; pos < full.length;) {
      const actual = buf.nextCaret(pos);
      expect(actual).toBe(nextBoundary(full, pos));
      pos = actual;
    }
    expect(buf.nextCaret(full.length)).toBe(full.length);
    for (let pos = full.length; pos > 0;) {
      const actual = buf.previousCaret(pos);
      expect(actual).toBe(previousBoundary(full, pos));
      pos = actual;
    }
    expect(buf.previousCaret(0)).toBe(0);
  });

  it('crosses CRLF as one unit', () => {
    const buf = new DocumentBuffer('a\r\nb');
    expect(buf.nextCaret(1)).toBe(3);
    expect(buf.previousCaret(3)).toBe(1);
  });
});

describe('reloadFrom', () => {
  it('keeps the history, so undo gives back what was in the editor', () => {
    const buf = new DocumentBuffer('mine');
    buf.selection = caretAt(4);
    buf.insert(' and mine');
    buf.reloadFrom('theirs');
    expect(text(buf)).toBe('theirs');
    expect(buf.isDirty).toBe(false);
    expect(buf.undo()).toBe(true);
    expect(text(buf)).toBe('mine and mine');
    expect(buf.isDirty).toBe(true);
    expect(buf.redo()).toBe(true);
    expect(text(buf)).toBe('theirs');
    buf.undo();
    buf.undo();
    expect(text(buf)).toBe('mine');
  });

  it('records nothing when the text is identical', () => {
    const buf = new DocumentBuffer('same');
    buf.reloadFrom('same');
    expect(buf.canUndo).toBe(false);
    expect(buf.isDirty).toBe(false);
  });

  it('keeps the caret where the new text is long enough, and clamps it otherwise', () => {
    const buf = new DocumentBuffer('one two three');
    buf.selection = caretAt(8);
    buf.reloadFrom('one two THREE');
    expect(buf.selection.caret).toBe(8);
    buf.reloadFrom('no');
    expect(buf.selection.caret).toBe(2);
  });
});

describe('undo effects', () => {
  it('undoes and redoes work outside the text with its edit, never merged with typing', () => {
    const buf = new DocumentBuffer('ab');
    const log: string[] = [];
    buf.replaceRange(0, 1, '', 0, { undo: () => log.push('undo'), redo: () => log.push('redo') });
    buf.insert('x');
    buf.undo(); // the typing only
    expect(log).toEqual([]);
    buf.undo();
    expect(text(buf)).toBe('ab');
    buf.redo();
    expect(text(buf)).toBe('b');
    expect(log).toEqual(['undo', 'redo']);
  });
});

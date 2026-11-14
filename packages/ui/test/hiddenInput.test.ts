import { TextDocument } from '@puffinote/text';
import { describe, expect, it } from 'vitest';
import { EditorState } from '../src/editor/editorState.ts';
import { emptyMirror, mirrorFor, readInput, type InputMirror } from '../src/editor/hiddenInput.ts';

/**
 * The hidden textarea as the browser and the OS see it, wired as the editor wires it: after each change the
 * editor reads it (`readInput`), applies the edit, empties it, and mirrors the new selection (`mirrorFor`).
 */
class Surface {
  readonly state: EditorState;
  value = '';
  selStart = 0;
  selEnd = 0;
  composing = false;
  private mirror: InputMirror = emptyMirror;

  constructor(text: string, caret = text.length) {
    this.state = new EditorState(text);
    this.state.select(caret);
    this.sync();
  }

  /** The editor's render: mirror the selection or the letter before the caret. */
  private sync(): void {
    this.mirror = mirrorFor(this.state.document, this.state.selection.peek(), 2000);
    this.value = this.mirror.text;
    this.selStart = this.mirror.kind === 'selection' ? 0 : this.value.length;
    this.selEnd = this.value.length;
  }

  /** The editor's `input` / `compositionend` handler. */
  private take(): void {
    const edit = readInput(this.mirror, this.value, this.state.document, this.state.selection.peek());
    this.value = '';
    this.mirror = emptyMirror;
    if (edit?.kind === 'type') this.state.insert(edit.text);
    else if (edit) {
      const caret = edit.start + edit.text.length;
      this.state.apply({ start: edit.start, length: edit.length, text: edit.text, anchor: caret, caret });
    }
    this.sync();
  }

  /** What a textarea does for insertText: replace its selection (or the OS's range) with `text`. */
  private put(text: string, from = this.selStart, to = this.selEnd): void {
    this.value = this.value.slice(0, from) + text + this.value.slice(to);
    this.selStart = this.selEnd = from + text.length;
  }

  /** A key that types `text`. */
  type(text: string): void {
    this.put(text);
    if (!this.composing) this.take();
  }

  /** The OS replaces `[from, to)` of the input with `text` (Safari's press-and-hold commit). */
  replaceRange(from: number, to: number, text: string): void {
    this.put(text, from, to);
    this.take();
  }

  /** An input method opens a composition over `[from, to)` of the input (its caret by default). */
  compositionStart(from = this.selStart, to = this.selEnd): void {
    this.composing = true;
    this.compFrom = from;
    this.compTo = to;
  }
  private compFrom = 0;
  private compTo = 0;

  /** The composition shows `text` (marked text, still open). */
  compositionUpdate(text: string): void {
    this.value = this.value.slice(0, this.compFrom) + text + this.value.slice(this.compTo);
    this.compTo = this.compFrom + text.length;
    this.selStart = this.selEnd = this.compTo;
  }

  /** The composition commits `text`; the editor reads the input on `compositionend`. */
  compositionEnd(text: string): void {
    this.compositionUpdate(text);
    this.composing = false;
    this.take();
  }

  get text(): string {
    return this.state.text;
  }
}

describe('the hidden input', () => {
  it('mirrors the letter before an empty selection, and nothing after punctuation or at a line start', () => {
    const doc = TextDocument.fromString('café x-\n- a');
    expect(mirrorFor(doc, { anchor: 5, caret: 5 }, 10)).toEqual({ kind: 'context', text: 'é' });
    expect(mirrorFor(doc, { anchor: 3, caret: 3 }, 10)).toEqual({ kind: 'context', text: 'f' });
    expect(mirrorFor(doc, { anchor: 6, caret: 6 }, 10)).toEqual(emptyMirror); // after a space
    expect(mirrorFor(doc, { anchor: 8, caret: 8 }, 10)).toEqual(emptyMirror); // after '-'
    expect(mirrorFor(doc, { anchor: 9, caret: 9 }, 10)).toEqual(emptyMirror); // line start
    expect(mirrorFor(doc, { anchor: 0, caret: 0 }, 10)).toEqual(emptyMirror);
    expect(mirrorFor(doc, { anchor: 0, caret: 3 }, 10)).toEqual({ kind: 'selection', text: 'caf' });
    expect(mirrorFor(doc, { anchor: 0, caret: 3 }, 2)).toEqual({ kind: 'selection', text: ' ' });
    const jp = TextDocument.fromString('日本');
    expect(mirrorFor(jp, { anchor: 2, caret: 2 }, 10)).toEqual({ kind: 'context', text: '本' });
  });

  it('types ordinary keys at the caret', () => {
    const s = new Surface('');
    for (const c of 'Hi there') s.type(c);
    expect(s.text).toBe('Hi there');
  });

  it('replaces the held letter with the accent chosen from the press-and-hold menu (OS range insert)', () => {
    const s = new Surface('caf');
    s.type('e'); // the key goes down: an e is typed
    expect(s.value).toBe('e'); // mirrored, so the OS can address it
    s.replaceRange(0, 1, 'é'); // the accent menu: insertText "é" over the letter before the caret
    expect(s.text).toBe('café');
    expect(s.state.selection.peek()).toEqual({ anchor: 4, caret: 4 });
    s.type('s');
    expect(s.text).toBe('cafés');
    s.state.undo();
    s.state.undo();
    expect(s.text).toBe('cafe');
  });

  it('replaces the held letter when the browser marks it as a composition first (Chrome, Firefox)', () => {
    const s = new Surface('na');
    s.type('i');
    s.compositionStart(0, 1); // the held i becomes marked text
    s.compositionUpdate('i');
    s.compositionEnd('ï');
    expect(s.text).toBe('naï');
    s.type('v');
    expect(s.text).toBe('naïv');
  });

  it('keeps an ordinary IME composition after a letter (Japanese, Chinese)', () => {
    const s = new Surface('ab');
    s.compositionStart();
    s.compositionUpdate('に');
    s.compositionUpdate('にほ');
    s.compositionUpdate('日本');
    expect(s.text).toBe('ab'); // nothing reaches the note while composing
    s.compositionEnd('日本');
    expect(s.text).toBe('ab日本');
    s.compositionStart();
    s.compositionUpdate('ご');
    s.compositionEnd('語');
    expect(s.text).toBe('ab日本語');
  });

  it('keeps dead keys (⌥E then e) inserting after the letter before the caret', () => {
    const s = new Surface('cr');
    s.compositionStart();
    s.compositionUpdate('´');
    s.compositionEnd('é');
    s.type('m');
    s.type('e');
    expect(s.text).toBe('créme');
  });

  it('types over a selection, composition included', () => {
    const s = new Surface('one two');
    s.state.select(4, 7);
    s['sync']();
    s.type('2');
    expect(s.text).toBe('one 2');
    s.state.select(0, 3);
    s['sync']();
    s.compositionStart();
    s.compositionEnd('一');
    expect(s.text).toBe('一 2');
  });

  it('never deletes from an input event, and types instead of replacing when the note moved on', () => {
    const doc = TextDocument.fromString('cafe');
    const sel = { anchor: 4, caret: 4 };
    const mirror: InputMirror = { kind: 'context', text: 'e' };
    expect(readInput(mirror, '', doc, sel)).toBeNull();
    expect(readInput(mirror, 'e', doc, sel)).toBeNull();
    expect(readInput(mirror, 'es', doc, sel)).toEqual({ kind: 'type', text: 's' });
    expect(readInput(mirror, 'é', doc, sel)).toEqual({ kind: 'replace', start: 3, length: 1, text: 'é' });
    // A decomposed accent adds a combining mark after the letter.
    expect(readInput(mirror, 'é', doc, sel)).toEqual({ kind: 'type', text: '́' });
    // The note no longer has the mirrored letter before the caret (changed under the input): type, don't replace.
    expect(readInput(mirror, 'é', TextDocument.fromString('cafx'), sel)).toEqual({ kind: 'type', text: 'é' });
    expect(readInput(mirror, 'é', doc, { anchor: 0, caret: 4 })).toEqual({ kind: 'type', text: 'é' });
  });
});

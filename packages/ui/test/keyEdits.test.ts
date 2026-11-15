// The editor's key decisions, on EditorState and in caretMotion, without a DOM.
import { TextDocument } from '@puffinote/text';
import { describe, expect, it } from 'vitest';
import { EditorState } from '../src/editor/editorState.ts';
import { caretMotion, type CaretLayout } from '../src/editor/motion.ts';

/** A state whose text is `marked` with `|` at the caret (or `[`…`]` around a selection, the caret at `]`). */
function at(marked: string, mode: 'live' | 'raw' = 'live'): EditorState {
  const anchor = marked.includes('[') ? marked.indexOf('[') : marked.indexOf('|');
  const text = marked.replace(/[[\]|]/g, '');
  const caret = marked.includes(']') ? marked.indexOf(']') - 1 : anchor;
  const state = new EditorState(text);
  state.mode.value = mode;
  state.select(anchor, caret);
  return state;
}

const shown = (s: EditorState) => {
  const c = s.selection.value.caret;
  return s.text.slice(0, c) + '|' + s.text.slice(c);
};

describe('EditorState key edits', () => {
  it('Backspace takes a list marker in Rich view, but a character in Markdown view', () => {
    const live = at('- |item');
    live.deleteBackward('char');
    expect(shown(live)).toBe('|item');
    const raw = at('- |item', 'raw');
    raw.deleteBackward('char');
    expect(shown(raw)).toBe('-|item');
  });

  it('Backspace removes an auto-inserted pair, joins lines and deletes the selection', () => {
    const typed = new EditorState('');
    typed.applyTyping({ start: 0, length: 0, text: '()', anchor: 1, caret: 1 });
    typed.deleteBackward('char');
    expect(typed.text).toBe('');
    const join = at('one\n|two');
    join.deleteBackward('char');
    expect(shown(join)).toBe('one|two');
    const sel = at('a[bc]d');
    sel.deleteBackward('word');
    expect(shown(sel)).toBe('a|d');
  });

  it('⌥⌫ deletes the word before, and ⌘⌫ back to where the layout says the row starts', () => {
    const word = at('one two|');
    word.deleteBackward('word');
    expect(shown(word)).toBe('one |');
    const line = at('one two|');
    line.deleteBackward('line', undefined, 4);
    expect(shown(line)).toBe('one |');
    const none = at('one two|');
    none.deleteBackward('line', undefined, null);
    expect(shown(none)).toBe('one tw|');
  });

  it('Delete and ⌃K delete forward by character, word and line', () => {
    const ch = at('|ab');
    ch.deleteForward('char');
    expect(shown(ch)).toBe('|b');
    const word = at('|one two');
    word.deleteForward('word');
    expect(shown(word)).toBe('| two');
    const line = at('o|ne\ntwo');
    line.deleteForward('line');
    expect(shown(line)).toBe('o|\ntwo');
    line.deleteForward('line');
    expect(shown(line)).toBe('o|two');
  });

  it('Enter continues a list, Shift+Enter breaks the line, Tab indents', () => {
    const list = at('- one|');
    list.enter(false);
    expect(shown(list)).toBe('- one\n- |');
    const soft = at('- one|');
    soft.enter(true);
    expect(shown(soft)).not.toContain('\n- ');
    expect(soft.text.startsWith('- one')).toBe(true);
    const tab = at('- |one');
    tab.indent(false);
    expect(tab.text).toBe('    - one');
    tab.indent(true);
    expect(tab.text).toBe('- one');
  });
});

/** A layout with no wrapping and nothing concealed, unless a test says otherwise. */
function flat(doc: TextDocument, extra: Partial<CaretLayout> = {}): CaretLayout {
  return {
    rowEdge: () => null,
    drawnBelow: () => false,
    snap: (o) => o,
    nextCaret: (o) => Math.min(doc.length, o + 1),
    previousCaret: (o) => Math.max(0, o - 1),
    ...extra,
  };
}

describe('caretMotion', () => {
  const doc = TextDocument.fromString('- one two\n- three');
  const move = (caret: number, dir: 1 | -1, unit: 'char' | 'word' | 'row' | 'line', layout = flat(doc)) =>
    caretMotion(doc, 'live', { anchor: caret, caret }, dir, false, unit, layout);

  it('collapses a selection to its edge before stepping', () => {
    expect(caretMotion(doc, 'live', { anchor: 2, caret: 5 }, -1, false, 'char', flat(doc))).toBe(2);
    expect(caretMotion(doc, 'live', { anchor: 2, caret: 5 }, 1, true, 'char', flat(doc))).toBe(6);
  });

  it('hops over a list marker in Rich view, and steps through it in Markdown view', () => {
    expect(move(12, -1, 'char')).toBe(9);
    expect(move(9, 1, 'char')).toBe(12);
    expect(caretMotion(doc, 'raw', { anchor: 12, caret: 12 }, -1, false, 'char', flat(doc))).toBe(11);
  });

  it('steps out of a concealed marker as the layout reports it', () => {
    expect(move(3, 1, 'char', flat(doc, { snap: (o, dir) => (o === 4 ? 4 + dir : o) }))).toBe(5);
  });

  it('goes to the item text, then the line start, with Home on an unwrapped line', () => {
    expect(move(6, -1, 'line')).toBe(2);
    expect(move(2, -1, 'line')).toBe(0);
    expect(move(2, 1, 'line')).toBe(9);
    expect(move(6, -1, 'row')).toBe(2);
  });

  it('stops End before the space a wrapped row ends at', () => {
    // "- one two" wrapped after "one ": the row's end (6) is drawn at the start of the next row.
    const wrapped = flat(doc, {
      rowEdge: (_o, dir) => (dir > 0 ? 6 : 0),
      drawnBelow: (o, from) => o === 6 && from < 6,
    });
    expect(move(3, 1, 'row', wrapped)).toBe(5);
    expect(move(3, 1, 'word')).toBe(5);
  });
});

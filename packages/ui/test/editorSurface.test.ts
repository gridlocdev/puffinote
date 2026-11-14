import { token, type Token } from '@puffinote/markdown';
import { describe, expect, it } from 'vitest';
import { findBlocks } from '../src/editor/blocks.ts';
import { EditorState, enterEdit } from '../src/index.ts';
import { firstRowEndingBelow, nearestRowIndex, type RectSource } from '../src/editor/geometry.ts';
import { keyAction, primaryHeld, undoRunFor, type KeyLike } from '../src/editor/keys.ts';
import { joinLinesDeleteEdit } from '../src/editor/commands.ts';
import { lineViewPropsEqual, type LineViewProps } from '../src/editor/lineProps.ts';
import type { LineReveal } from '../src/editor/reveal.ts';

const idle = (): LineReveal => ({
  lineActive: false,
  prefixActive: false,
  nodes: new Set(),
  blockRevealed: false,
});
const resolve = (url: string) => url;

/** The props the surface hands line `i`, built fresh each time as a render does. */
function propsOf(state: EditorState, i: number): LineViewProps {
  const doc = state.document;
  return {
    index: i,
    text: doc.getLineText(i),
    start: doc.getLineStart(i),
    tokens: [...state.model.lineTokens(i)],
    reveal: idle(),
    mode: 'live',
    block: null,
    resolveImage: resolve,
  };
}

/** Render every line keyed by `lineKey`; count the lines whose memo would skip the redraw. */
function render(state: EditorState): Map<number, LineViewProps> {
  const rows = new Map<number, LineViewProps>();
  for (let i = 0; i < state.document.lineCount; i++) rows.set(state.lineKey(i), propsOf(state, i));
  return rows;
}

function memoHits(before: Map<number, LineViewProps>, after: Map<number, LineViewProps>): number {
  let hits = 0;
  for (const [key, p] of after) {
    const old = before.get(key);
    if (old && lineViewPropsEqual(old, p)) hits++;
  }
  return hits;
}

const longNote = () =>
  Array.from({ length: 1000 }, (_, i) =>
    i % 7 === 0 ? `## Heading ${i}` : `- item **bold** and [l](u) ${i}`,
  ).join('\n');

describe('line memo (finding 1)', () => {
  const base: LineViewProps = {
    index: 3,
    text: 'a **b**',
    start: 10,
    tokens: [token(12, 2, 'marker', 'marker', null, 1), token(14, 1, 'strong', 'content', null, 1)],
    reveal: idle(),
    mode: 'live',
    block: null,
    resolveImage: resolve,
  };

  it('treats a shifted line as the same line', () => {
    const shifted: LineViewProps = {
      ...base,
      index: 4,
      start: 25,
      tokens: base.tokens.map((t) => ({ ...t, start: t.start + 15 })),
      reveal: idle(),
    };
    expect(lineViewPropsEqual(base, shifted)).toBe(true);
  });

  it('redraws when the text, a token, the reveal or the mode changes', () => {
    expect(lineViewPropsEqual(base, { ...base, text: 'a **c**' })).toBe(false);
    const moved: Token[] = [base.tokens[0]!, { ...base.tokens[1]!, start: 15 }];
    expect(lineViewPropsEqual(base, { ...base, tokens: moved })).toBe(false);
    expect(lineViewPropsEqual(base, { ...base, reveal: { ...idle(), nodes: new Set([1]) } })).toBe(false);
    expect(lineViewPropsEqual(base, { ...base, reveal: { ...idle(), lineActive: true } })).toBe(false);
    expect(lineViewPropsEqual(base, { ...base, mode: 'source' })).toBe(false);
    expect(lineViewPropsEqual(base, { ...base, block: 'table' })).toBe(false);
  });

  it('only cares about the image resolver on a line with an image', () => {
    expect(lineViewPropsEqual(base, { ...base, resolveImage: (u) => `x${u}` })).toBe(true);
    const img = { ...base, tokens: [token(12, 3, 'imageUrl', 'content', null, 1)] };
    expect(lineViewPropsEqual(img, { ...img, resolveImage: (u) => `x${u}` })).toBe(false);
  });

  it('keeps every other line of a long note when typing at the top', () => {
    const state = new EditorState(longNote());
    state.select(3);
    const before = render(state);
    state.insert('x');
    expect(memoHits(before, render(state))).toBe(999);
  });

  it('keeps every other line of a long note when pressing Enter at the top', () => {
    const state = new EditorState(longNote());
    state.select(3);
    const before = render(state);
    state.apply(enterEdit(state.document, state.selection.value, 'live', 'text'));
    // the split line and the inserted lines redraw; the other 999 lines keep their keys and props
    expect(memoHits(before, render(state))).toBe(999);
  });

  it('moves line keys with their lines', () => {
    const state = new EditorState('a\nb\nc\nd');
    const keys = () => [0, 1, 2, 3, 4].slice(0, state.document.lineCount).map((i) => state.lineKey(i));
    const [ka, kb, kc, kd] = keys();
    state.select(1);
    state.insert('\nnew');
    expect(keys()).toEqual([ka, expect.any(Number), kb, kc, kd]);
    expect(new Set(keys()).size).toBe(5);
    state.select(2, 9); // "new\nb\nc" → removes two line breaks
    state.insert('');
    expect(state.text).toBe('a\n\nd');
    expect(keys()[0]).toBe(ka);
    expect(keys()[2]).toBe(kd);
  });
});

describe('incremental blocks (finding 1)', () => {
  it('match a full scan after every edit', () => {
    let seed = 7;
    const rand = (n: number) => {
      seed = (seed * 1103515245 + 12345) % 2147483648;
      return seed % n;
    };
    const snippets = [
      '```\n',
      '```js\n',
      '| a | b |\n',
      '| - | - |\n',
      '---\n',
      'x',
      '\n',
      'text\n',
      '|',
      '`',
    ];
    const state = new EditorState(
      '---\ntitle: t\n---\n# h\n\n```\ncode\n```\n| a | b |\n| - | - |\n| 1 | 2 |\n',
    );
    for (let step = 0; step < 400; step++) {
      const len = state.document.length;
      const at = rand(len + 1);
      if (rand(3) === 0 && len > 0) {
        state.select(at, Math.min(len, at + 1 + rand(12)));
        state.insert('');
      } else {
        state.select(at);
        state.insert(snippets[rand(snippets.length)]!);
      }
      expect(state.blocks, `step ${step}: ${JSON.stringify(state.text)}`).toEqual(
        findBlocks(state.document, state.model),
      );
    }
  });
});

describe('undo steps (finding 2)', () => {
  it('undoes a list continuation on its own, not with the typing before it', () => {
    const state = new EditorState('');
    state.insert('- a');
    state.apply(enterEdit(state.document, state.selection.value, 'live', 'text'));
    state.insert('b');
    expect(state.text).toBe('- a\n- b');
    state.undo();
    expect(state.text).toBe('- a\n- ');
    state.undo();
    expect(state.text).toBe('- a');
  });

  it('undoes a paste on its own', () => {
    const state = new EditorState('');
    state.insert('ab');
    state.paste('pasted');
    state.insert('cd');
    expect(state.text).toBe('abpastedcd');
    state.undo();
    expect(state.text).toBe('abpasted');
    state.undo();
    expect(state.text).toBe('ab');
  });
});

describe('undo runs of one key', () => {
  const enter = (state: EditorState, repeat: boolean) =>
    state.apply(
      enterEdit(state.document, state.selection.value, 'live', 'text'),
      undefined,
      undoRunFor({ kind: 'enter', soft: false }, 'Enter', repeat),
    );
  const backspace = (state: EditorState) => {
    const run = undoRunFor({ kind: 'deleteBack', unit: 'char' }, 'Backspace', false);
    const sel = state.selection.value;
    if (!state.apply(joinLinesDeleteEdit(state.document, sel.caret - 1, sel.caret), undefined, run)) {
      state.buffer.backspace();
    }
  };

  it('undoes Enter held down as one step, and separate presses one at a time', () => {
    const state = new EditorState('- a');
    state.select(3);
    enter(state, false);
    enter(state, true);
    enter(state, true);
    const held = state.text;
    enter(state, false);
    state.undo();
    expect(state.text).toBe(held);
    state.undo();
    expect(state.text).toBe('- a');
  });

  it('undoes a run of Backspace across lines as one step, renumbering included', () => {
    const state = new EditorState('1. a\n2. b\n3. c');
    state.select(9); // end of "2. b"
    for (let i = 0; i < 5; i++) backspace(state);
    expect(state.text).toBe('1. a\n2. c');
    state.undo();
    expect(state.text).toBe('1. a\n2. b\n3. c');
    expect(state.buffer.canUndo).toBe(false);
  });

  it('starts a new step when typing follows a run of Backspace', () => {
    const state = new EditorState('abc');
    state.select(3);
    backspace(state);
    state.insert('X');
    state.undo();
    expect(state.text).toBe('ab');
  });
});

describe('platform keys (finding 5)', () => {
  const key = (k: string, mods: Partial<KeyLike> = {}): KeyLike => ({
    key: k,
    metaKey: false,
    ctrlKey: false,
    altKey: false,
    shiftKey: false,
    ...mods,
  });

  it('deletes a word with Ctrl+Backspace off macOS and a line with Cmd+Backspace on macOS', () => {
    expect(keyAction(key('Backspace', { ctrlKey: true }), false)).toEqual({
      kind: 'deleteBack',
      unit: 'word',
    });
    expect(keyAction(key('Backspace', { altKey: true }), true)).toEqual({ kind: 'deleteBack', unit: 'word' });
    expect(keyAction(key('Backspace', { metaKey: true }), true)).toEqual({
      kind: 'deleteBack',
      unit: 'line',
    });
    expect(keyAction(key('Backspace'), false)).toEqual({ kind: 'deleteBack', unit: 'char' });
    expect(keyAction(key('Delete', { ctrlKey: true }), false)).toEqual({
      kind: 'deleteForward',
      unit: 'word',
    });
  });

  it('goes to the document start and end with Ctrl+Home/End off macOS and Cmd+Up/Down on macOS', () => {
    expect(keyAction(key('Home', { ctrlKey: true }), false)).toEqual({ kind: 'move', unit: 'doc', dir: -1 });
    expect(keyAction(key('End', { ctrlKey: true }), false)).toEqual({ kind: 'move', unit: 'doc', dir: 1 });
    expect(keyAction(key('Home'), false)).toEqual({ kind: 'move', unit: 'line', dir: -1 });
    expect(keyAction(key('ArrowUp', { metaKey: true }), true)).toEqual({
      kind: 'move',
      unit: 'doc',
      dir: -1,
    });
    expect(keyAction(key('ArrowLeft', { metaKey: true }), true)).toEqual({
      kind: 'move',
      unit: 'line',
      dir: -1,
    });
    expect(keyAction(key('ArrowLeft', { ctrlKey: true }), false)).toEqual({
      kind: 'move',
      unit: 'word',
      dir: -1,
    });
  });

  it('leaves modified Enter and Tab to the shortcut catalog', () => {
    expect(keyAction(key('Enter', { metaKey: true }), true)).toBeNull();
    expect(keyAction(key('Tab', { ctrlKey: true }), false)).toBeNull();
    // ⌃Tab and ⌃⇧Tab on macOS switch tabs.
    expect(keyAction(key('Tab', { ctrlKey: true }), true)).toBeNull();
    expect(keyAction(key('Tab', { ctrlKey: true, shiftKey: true }), true)).toBeNull();
    expect(keyAction(key('Enter', { shiftKey: true }), false)).toEqual({ kind: 'enter', soft: true });
  });
});

describe('row lookup (findings 6 and 9)', () => {
  /** Rows of height 20 stacked from y = 0; `hidden` rows measure zero. Counts rect reads. */
  function rows(n: number, hidden: (i: number) => boolean = () => false) {
    let reads = 0;
    let y = 0;
    const list: RectSource[] = [];
    for (let i = 0; i < n; i++) {
      const h = hidden(i) ? 0 : 20;
      const top = y;
      y += h;
      list.push({
        getBoundingClientRect: () => {
          reads++;
          return { top, bottom: top + h, height: h };
        },
      });
    }
    return { list, reads: () => reads, reset: () => (reads = 0) };
  }

  it('finds the row under a y with a logarithmic number of reads', () => {
    const r = rows(10000);
    expect(firstRowEndingBelow(r.list, 0)).toBe(0);
    expect(firstRowEndingBelow(r.list, 100_005)).toBe(5000);
    expect(firstRowEndingBelow(r.list, 1e9)).toBe(10000);
    r.reset();
    expect(nearestRowIndex(r.list, 54_321)).toBe(2716);
    expect(r.reads()).toBeLessThan(40);
  });

  it('skips collapsed rows', () => {
    const r = rows(100, (i) => i % 3 !== 0); // only every third row is visible
    const i = nearestRowIndex(r.list, 50);
    expect(i % 3).toBe(0);
    expect(i).toBe(6); // visible rows: 0 at 0–20, 3 at 20–40, 6 at 40–60
    expect(nearestRowIndex(r.list, -10)).toBe(0);
    expect(nearestRowIndex(r.list, 1e6)).toBe(99);
    expect(nearestRowIndex(rows(5, () => true).list, 10)).toBe(-1);
  });

  it('picks the nearer row in a gap between rows', () => {
    const list: RectSource[] = [
      { getBoundingClientRect: () => ({ top: 0, bottom: 20, height: 20 }) },
      { getBoundingClientRect: () => ({ top: 60, bottom: 80, height: 20 }) },
    ];
    expect(nearestRowIndex(list, 30)).toBe(0);
    expect(nearestRowIndex(list, 50)).toBe(1);
  });
});

describe('drag selection', () => {
  it('ends once the primary button is no longer held, even if its release was missed', () => {
    expect(primaryHeld({ buttons: 1 })).toBe(true);
    expect(primaryHeld({ buttons: 3 })).toBe(true);
    expect(primaryHeld({ buttons: 0 })).toBe(false);
    expect(primaryHeld({ buttons: 2 })).toBe(false);
  });
});

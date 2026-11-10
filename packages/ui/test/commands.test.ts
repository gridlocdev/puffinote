import { TextDocument, type Selection } from '@puffinote/text';
import { describe, expect, it } from 'vitest';
import {
  applyEditTo,
  backspacePairEdit,
  duplicateLinesEdit,
  EditorState,
  enterEdit,
  headingEdit,
  indentEdit,
  linkEdit,
  moveLinesEdit,
  outdentEdit,
  setTaskStateEdit,
  toggleInlineEdit,
  toggleTaskEdit,
  typeCharEdit,
  wordLeft,
  wordRight,
  type Edit,
} from '../src/index.ts';

/**
 * Markdown with `|` for the caret, or `‹` … `›` for a selection (anchor first), so a test reads like the
 * editor looks. Use `\\|` for a literal pipe.
 */
function parse(marked: string): { doc: TextDocument; sel: Selection } {
  let text = '';
  let anchor = -1;
  let caret = -1;
  for (let i = 0; i < marked.length; i++) {
    const c = marked.charAt(i);
    if (c === '\\' && marked.charAt(i + 1) === '|') {
      text += '|';
      i++;
    } else if (c === '|') anchor = caret = text.length;
    else if (c === '‹') anchor = text.length;
    else if (c === '›') caret = text.length;
    else text += c;
  }
  return { doc: TextDocument.fromString(text), sel: { anchor, caret } };
}

function show(doc: TextDocument, edit: Edit | null): string {
  if (!edit) return '(none)';
  const out = applyEditTo(doc, edit).getText();
  if (edit.anchor === edit.caret) return out.slice(0, edit.caret) + '|' + out.slice(edit.caret);
  const [a, b] = [Math.min(edit.anchor, edit.caret), Math.max(edit.anchor, edit.caret)];
  return out.slice(0, a) + '‹' + out.slice(a, b) + '›' + out.slice(b);
}

function run(marked: string, cmd: (doc: TextDocument, sel: Selection) => Edit | null): string {
  const { doc, sel } = parse(marked);
  return show(doc, cmd(doc, sel));
}

describe('Enter (PRD §10.1)', () => {
  const enter = (marked: string, mode: 'live' | 'raw' = 'live', context: 'text' | 'code' = 'text') =>
    run(marked, (d, s) => enterEdit(d, s, mode, context));

  it('starts a new paragraph with a blank line in the formatted views', () => {
    expect(enter('hello|')).toBe('hello\n\n|');
    expect(enter('hello|', 'raw')).toBe('hello\n|');
  });

  it('writes a single newline on a blank line', () => {
    expect(enter('a\n|')).toBe('a\n\n|');
  });

  it('continues a list, a task and a quote', () => {
    expect(enter('- one|')).toBe('- one\n- |');
    expect(enter('- [x] done|')).toBe('- [x] done\n- [ ] |');
    expect(enter('> quoted|')).toBe('> quoted\n> |');
  });

  it('opens one empty item above when the caret is at the start of an item', () => {
    expect(enter('- [ ] a\n|- [ ] b')).toBe('- [ ] a\n- [ ] \n- [ ] |b');
    expect(enter('- [ ] a\n- [x|] b')).toBe('- [ ] a\n- [ ] \n- [x] |b');
    expect(enter('1. a\n|2. b')).toBe('1. a\n2. \n3. |b');
  });

  it('exits an empty item', () => {
    expect(enter('- a\n- |')).toBe('- a\n|');
  });

  it('numbers the next ordered item and renumbers those below', () => {
    expect(enter('1. a|\n2. b')).toBe('1. a\n2. |\n3. b');
  });

  it('keeps the indent inside code', () => {
    expect(enter('    x = 1|', 'live', 'code')).toBe('    x = 1\n    |');
  });
});

describe('Tab and Shift+Tab', () => {
  it('indents a list item and keeps the caret with its text', () => {
    expect(run('- a\n- b|', (d, s) => indentEdit(d, s))).toBe('- a\n    - b|');
  });

  it('inserts an indent in a paragraph', () => {
    expect(run('a|b', (d, s) => indentEdit(d, s))).toBe('a    |b');
  });

  it('indents every selected line', () => {
    expect(run('‹a\nb›', (d, s) => indentEdit(d, s, '  '))).toBe('  ‹a\n  b›');
  });

  it('outdents by one step', () => {
    expect(run('      - b|', (d, s) => outdentEdit(d, s))).toBe('  - b|');
    expect(run('\t- b|', (d, s) => outdentEdit(d, s))).toBe('- b|');
    expect(run('- b|', (d, s) => outdentEdit(d, s))).toBe('(none)');
  });

  it('does nothing on a selection of only blank lines', () => {
    expect(run('‹\n\n›', (d, s) => indentEdit(d, s))).toBe('(none)');
    expect(run('a\n‹\n\n›b', (d, s) => indentEdit(d, s))).toBe('(none)');
  });

  it('renumbers an ordered list when an item moves into it', () => {
    expect(run('1. a\n    1. b|\n2. c', (d, s) => outdentEdit(d, s))).toBe('1. a\n2. b|\n3. c');
  });
});

describe('auto-pairing', () => {
  const type = (marked: string, ch: string) => run(marked, (d, s) => typeCharEdit(d, s, ch));

  it('inserts a pair at a word start', () => {
    expect(type('a |', '*')).toBe('a *|*');
    expect(type('|', '[')).toBe('[|]');
    expect(type('x |', '(')).toBe('x (|)');
    expect(type('a |', '`')).toBe('a `|`');
  });

  it('types a second star into bold', () => {
    expect(type('*|*', '*')).toBe('**|**');
  });

  it('does not pair inside a word', () => {
    expect(type('snake|', '_')).toBe('(none)');
    expect(type('2|', '*')).toBe('(none)');
    expect(type('|word', '(')).toBe('(none)');
  });

  it('steps over a closer that is already there', () => {
    expect(type('(x|)', ')')).toBe('(x)|');
    expect(type('*it|*', '*')).toBe('*it*|');
  });

  it('wraps a selection', () => {
    expect(type('‹word›', '*')).toBe('*‹word›*');
    expect(type('‹word›', '(')).toBe('(‹word›)');
  });

  it('does not pair at the start of a line, where the character is a list marker, rule or fence', () => {
    expect(type('|', '*')).toBe('(none)');
    expect(type('    |', '*')).toBe('(none)');
    expect(type('> |', '_')).toBe('(none)');
    expect(type('|', '`')).toBe('(none)');
  });

  it('does not pair the next character of a run, such as a fence', () => {
    expect(type('`|', '`')).toBe('(none)');
    expect(type('``|', '`')).toBe('(none)');
    expect(type('a _|', '_')).toBe('(none)');
  });

  it('drops the closer when a space follows a lone opener', () => {
    expect(type('2 *|*', ' ')).toBe('2 * |');
    expect(type('a `|`', ' ')).toBe('a ` |');
    expect(type('**|**', ' ')).toBe('(none)');
    expect(type('snake_|_case', ' ')).toBe('(none)');
  });

  it('types common sequences without stray closers', () => {
    const typed = (keys: string): string => {
      const state = new EditorState('');
      for (const ch of keys) {
        const edit = typeCharEdit(state.document, state.selection.value, ch);
        if (edit) state.applyTyping(edit);
        else state.insert(ch);
      }
      return state.text;
    };
    expect(typed('* item')).toBe('* item');
    expect(typed('2 * 3')).toBe('2 * 3');
    expect(typed('```js')).toBe('```js');
    expect(typed('- *word* and _it_ and `code`')).toBe('- *word* and _it_ and `code`');
    expect(typed('a **bold** b')).toBe('a **bold** b');
    expect(typed('*word*')).toBe('*word*');
  });

  it('deletes an empty pair with Backspace', () => {
    expect(run('(|)', backspacePairEdit)).toBe('|');
    expect(run('(x|)', backspacePairEdit)).toBe('(none)');
  });
});

describe('inline formatting', () => {
  const bold = (marked: string) => run(marked, (d, s) => toggleInlineEdit(d, s, '**'));

  it('wraps and unwraps a selection', () => {
    expect(bold('a ‹b› c')).toBe('a **‹b›** c');
    expect(bold('a **‹b›** c')).toBe('a ‹b› c');
    expect(bold('a ‹**b**› c')).toBe('a ‹b› c');
  });

  it('acts on the word at the caret', () => {
    expect(bold('a wo|rd c')).toBe('a **wo|rd** c');
    expect(bold('a **wo|rd** c')).toBe('a wo|rd c');
  });

  it('inserts an empty pair where there is no word', () => {
    expect(bold('a | c')).toBe('a **|** c');
  });

  it('does not take italic out of bold', () => {
    expect(run('**‹b›**', (d, s) => toggleInlineEdit(d, s, '*'))).toBe('***‹b›***');
  });

  it('makes a link and puts the caret where the URL goes', () => {
    expect(run('‹home›', linkEdit)).toBe('[home](|)');
    expect(run('‹https://x.io›', linkEdit)).toBe('[|](https://x.io)');
    expect(run('|', linkEdit)).toBe('[|]()');
  });
});

describe('headings', () => {
  it('sets, changes and clears the level', () => {
    expect(run('Ti|tle', (d, s) => headingEdit(d, s, 2))).toBe('## Ti|tle');
    expect(run('## Ti|tle', (d, s) => headingEdit(d, s, 3))).toBe('### Ti|tle');
    expect(run('## Ti|tle', (d, s) => headingEdit(d, s, 2))).toBe('Ti|tle');
    expect(run('# Ti|tle', (d, s) => headingEdit(d, s, 0))).toBe('Ti|tle');
  });
});

describe('tasks', () => {
  it('toggles a task between not started and done', () => {
    expect(run('- [ ] a|', toggleTaskEdit)).toBe('- [x] a|');
    expect(run('- [x] a|', toggleTaskEdit)).toBe('- [ ] a|');
    expect(run('- [/] a|', toggleTaskEdit)).toBe('- [x] a|');
  });

  it('adds a box to a list item and makes a plain line a task', () => {
    expect(run('- a|', toggleTaskEdit)).toBe('- [ ] a|');
    expect(run('a|', toggleTaskEdit)).toBe('- [ ] a|');
    expect(run('> a|', toggleTaskEdit)).toBe('> - [ ] a|');
  });

  it('sets a state on one line', () => {
    expect(run('- [ ] a\n- [ ] b|', (d, s) => setTaskStateEdit(d, s, 0, 'inProgress'))).toBe(
      '- [/] a\n- [ ] b|',
    );
    expect(run('plain|', (d, s) => setTaskStateEdit(d, s, 0, 'done'))).toBe('(none)');
  });
});

describe('word motion', () => {
  it('moves by words and across lines', () => {
    const doc = TextDocument.fromString('one two\nthree');
    expect(wordRight(doc, 0)).toBe(3);
    expect(wordRight(doc, 3)).toBe(7);
    expect(wordRight(doc, 7)).toBe(8);
    expect(wordLeft(doc, 7)).toBe(4);
    expect(wordLeft(doc, 8)).toBe(7);
  });
});

describe('line moves and duplicates keep the line endings', () => {
  const move = (marked: string, dir: -1 | 1) => run(marked, (d, s) => moveLinesEdit(d, s, dir));

  it('moves the last line of a CRLF file up without mixing endings', () => {
    expect(move('a\r\nb\r\nc|', -1)).toBe('a\r\nc|\r\nb');
    expect(move('a\r\nb|\r\nc', 1)).toBe('a\r\nc\r\nb|');
  });

  it('moves lines in an LF file and keeps the selection with them', () => {
    expect(move('a\n‹b\nc›\nd', -1)).toBe('‹b\nc›\na\nd');
    expect(move('a\n‹b\nc›\nd', 1)).toBe('a\nd\n‹b\nc›');
    expect(move('a\n‹b\n›c', 1)).toBe('a\nc\n‹b›');
    expect(move('a|\nb', -1)).toBe('(none)');
  });

  it('duplicates with the document line ending, also on the last line', () => {
    expect(run('a\r\nb|', duplicateLinesEdit)).toBe('a\r\nb\r\nb|');
    expect(run('a|\r\nb', duplicateLinesEdit)).toBe('a\r\na|\r\nb');
    expect(run('a\nb|', duplicateLinesEdit)).toBe('a\nb\nb|');
  });
});

describe('EditorState', () => {
  it('keeps the model in step with edits, undo and redo', () => {
    const state = new EditorState('hello');
    const v0 = state.version.value;
    state.select(5);
    state.apply(enterEdit(state.document, state.selection.value, 'live', 'text'));
    state.insert('# Title');
    expect(state.text).toBe('hello\n\n# Title');
    expect(state.model.lineTokens(2)[0]!.type).toBe('headingMarker');
    expect(state.version.value).toBeGreaterThan(v0);
    state.undo();
    expect(state.model.lineTokens(2)).toEqual([]);
    expect(state.selection.value.caret).toBe(7);
  });

  it('tells code from text for Enter and Tab', () => {
    const state = new EditorState('```\nx\n```\n| a | b |\n| - | - |\ntext');
    expect(state.contextAt(4)).toBe('code');
    expect(state.contextAt(12)).toBe('table');
    expect(state.contextAt(state.document.length)).toBe('text');
  });

  it('copies the Markdown source of the selection', () => {
    const state = new EditorState('a **b** c');
    state.select(2, 7);
    expect(state.selectedText()).toBe('**b**');
  });
});

import { describe, expect, it } from 'vitest';
import {
  caretAt,
  DocumentBuffer,
  encodeText,
  readText,
  type TextFileFormat,
  utf8NoBom,
} from '../src/index.ts';
import { runRandomOps, type Random } from './support/randomOps.ts';

const FORMATS: readonly TextFileFormat[] = [
  utf8NoBom,
  { encoding: 'utf-8', bom: true, displayName: 'UTF-8 BOM' },
  { encoding: 'utf-16le', bom: true, displayName: 'UTF-16 LE' },
  { encoding: 'utf-16be', bom: true, displayName: 'UTF-16 BE' },
];

/**
 * The buffer next to a plain string that every op also edits the obvious way. `start` is the text that undoing
 * every step must give back: an op that changes the text with no undo step (opening the file again) moves it.
 */
interface State {
  readonly buffer: DocumentBuffer;
  text: string;
  saved: string;
  start: string;
  /** The file as the last save or open left it. */
  file: Uint8Array;
  format: TextFileFormat;
}

/**
 * A spot between two code points. The editor never puts the caret or an edit inside a surrogate pair (half an
 * emoji can't be saved), so neither does the test.
 */
function offset(s: State, random: Random): number {
  const at = random.int(s.text.length + 1);
  return /[\uD800-\uDBFF]/.test(s.text.charAt(at - 1)) ? at - 1 : at;
}

function select(s: State, random: Random): string {
  const a = offset(s, random);
  const b = random.chance(0.5) ? a : offset(s, random);
  s.buffer.selection = { anchor: a, caret: b };
  return `${a}..${b}`;
}

function replace(s: State, start: number, length: number, text: string): void {
  s.text = s.text.slice(0, start) + text + s.text.slice(start + length);
}

/** Each line's text: the document splits at \n only, so a \r stays in its line, and a final \n starts an empty one. */
function lines(text: string): string[] {
  return text.split('\n');
}

describe('random edits', () => {
  it('keep the buffer equal to a plain string, through undo, redo, saving and opening again', async () => {
    await runRandomOps<State>({
      steps: 80,
      setup: (random) => {
        const text = random.text(20);
        const format = random.pick(FORMATS);
        return {
          buffer: new DocumentBuffer(text),
          text,
          saved: text,
          start: text,
          file: encodeText(text, format),
          format,
        };
      },
      ops: [
        {
          name: 'type',
          weight: 4,
          run: (s, random) => {
            const at = s.buffer.selection;
            const typed = random.text(3);
            const from = Math.min(at.anchor, at.caret);
            replace(s, from, Math.abs(at.caret - at.anchor), typed);
            s.buffer.insert(typed);
            return JSON.stringify(typed);
          },
        },
        { name: 'select', weight: 2, run: select },
        {
          name: 'replace range',
          weight: 2,
          run: (s, random) => {
            const start = offset(s, random);
            const length = offset(s, random) - start;
            if (length < 0) return 'skipped: the end came before the start';
            const text = random.text(4);
            replace(s, start, length, text);
            s.buffer.replaceRange(start, length, text);
            return `${start}+${length} ${JSON.stringify(text)}`;
          },
        },
        {
          name: 'backspace',
          weight: 2,
          run: (s) => {
            s.buffer.backspace();
            s.text = s.buffer.document.getText();
          },
        },
        {
          name: 'delete forward',
          run: (s) => {
            s.buffer.deleteForward();
            s.text = s.buffer.document.getText();
          },
        },
        {
          name: 'break the typing run',
          run: (s) => s.buffer.breakCoalescing(),
        },
        {
          name: 'undo',
          weight: 2,
          when: (s) => s.buffer.canUndo,
          run: (s) => {
            s.buffer.undo();
            s.text = s.buffer.document.getText();
          },
        },
        {
          name: 'redo',
          when: (s) => s.buffer.canRedo,
          run: (s) => {
            s.buffer.redo();
            s.text = s.buffer.document.getText();
          },
        },
        {
          name: 'undo everything, then redo it',
          when: (s) => s.buffer.canUndo,
          run: (s) => {
            const before = s.buffer.document.getText();
            let undone = 0;
            while (s.buffer.undo()) undone++;
            expect(s.buffer.document.getText(), 'undoing every step gives back the start').toBe(s.start);
            for (let i = 0; i < undone; i++) expect(s.buffer.redo()).toBe(true);
            expect(
              s.buffer.document.getText(),
              `redoing the ${undone} undone steps gives back the text`,
            ).toBe(before);
            return `${undone} steps`;
          },
        },
        {
          name: 'an edit that changes nothing',
          run: (s, random) => {
            const canRedo = s.buffer.canRedo;
            const start = offset(s, random);
            s.buffer.replaceRange(start, 0, '');
            expect(s.buffer.canRedo, 'an edit that changes nothing records nothing').toBe(canRedo);
          },
        },
        {
          name: 'save',
          weight: 2,
          run: (s) => {
            s.file = encodeText(s.buffer.document.getText(), s.format);
            s.buffer.markSaved();
            s.saved = s.text;
            return s.format.displayName;
          },
        },
        {
          name: 'open the file again',
          run: (s) => {
            const read = readText(s.file);
            s.buffer.load(read.text);
            s.text = s.saved = s.start = read.text;
            s.format = read.format;
          },
        },
        {
          name: 'an outside change, reloaded',
          run: (s, random) => {
            const outside = random.text(20);
            s.file = encodeText(outside, random.pick(FORMATS));
            const read = readText(s.file);
            s.buffer.reloadFrom(read.text);
            s.text = s.saved = read.text;
            s.format = read.format;
            return JSON.stringify(outside);
          },
        },
        {
          name: 'caret to the end',
          run: (s) => {
            s.buffer.selection = caretAt(s.text.length);
          },
        },
      ],
      check: (s) => {
        const { buffer } = s;
        const doc = buffer.document;
        expect(doc.getText()).toBe(s.text);
        expect(doc.length).toBe(s.text.length);
        const expected = lines(s.text);
        expect(doc.lineCount).toBe(expected.length);
        let at = 0;
        expected.forEach((line, i) => {
          expect(doc.getLineStart(i)).toBe(at);
          expect(doc.getLineText(i)).toBe(line);
          expect(doc.getPosition(at)).toEqual({ line: i, column: 0 });
          at += line.length + 1;
        });
        expect(buffer.isDirty, 'dirty exactly when the text differs from the last save').toBe(
          s.text !== s.saved,
        );
        const { anchor, caret } = buffer.selection;
        for (const end of [anchor, caret]) expect(end >= 0 && end <= s.text.length).toBe(true);
        const read = readText(s.file);
        expect(encodeText(read.text, read.format), 'opening and saving again writes the same bytes').toEqual(
          s.file,
        );
        // A UTF-8 file that starts with U+FEFF can't be told from one with a byte-order mark, so that character
        // opens as the mark. Every other text opens as it was saved.
        const asMark = s.format === utf8NoBom && s.saved.startsWith('\uFEFF');
        expect(read.text, 'the saved file opens as the text that was saved').toBe(
          asMark ? s.saved.slice(1) : s.saved,
        );
        expect(read.format.encoding).toBe(s.format.encoding);
      },
      finish: (s) => {
        while (s.buffer.undo());
        expect(s.buffer.document.getText(), 'undoing every step gives back the start').toBe(s.start);
      },
    });
  });
});

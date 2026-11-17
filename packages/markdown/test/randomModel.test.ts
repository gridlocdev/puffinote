import { TextDocument } from '@puffinote/text';
import { describe, expect, it } from 'vitest';
import { MarkdownModel } from '../src/index.ts';
import { RANDOM_TIMEOUT, runRandomOps, type Random } from './support/randomOps.ts';

/** Pieces that open, close and continue Markdown blocks and spans, so edits flip what the lines below mean. */
const MARKDOWN = [
  '\n',
  '\n\n',
  '# ',
  '## ',
  '- ',
  '1. ',
  '- [ ] ',
  '> ',
  '```',
  '```\n',
  '~~~',
  '---',
  '===',
  '| a | b |',
  '| - | - |',
  ' | ',
  '**',
  '*',
  '_',
  '`',
  '~~',
  '[',
  '](x)',
  '![p](p.png)',
  '<b>',
  '\\',
  '    ',
  '\t',
  'text',
  ' ',
];

function markdown(random: Random, pieces: number): string {
  let out = '';
  for (let i = random.int(pieces + 1); i > 0; i--)
    out += random.chance(0.8) ? random.pick(MARKDOWN) : random.text(2);
  return out;
}

interface State {
  doc: TextDocument;
  model: MarkdownModel;
}

describe('random markdown edits', () => {
  it(
    'keep the incremental model equal to a full rebuild',
    async () => {
      await runRandomOps<State>({
        steps: 60,
        setup: (random) => {
          const doc = TextDocument.fromString(markdown(random, 30));
          return { doc, model: MarkdownModel.build(doc) };
        },
        ops: [
          {
            name: 'edit',
            run: (s, random) => {
              const start = random.int(s.doc.length + 1);
              const length = random.int(Math.min(8, s.doc.length - start) + 1);
              const text = markdown(random, 3);
              const doc = s.doc.replace(start, length, text);
              s.model = s.model.update(doc, s.doc.getPosition(start).line).model;
              s.doc = doc;
              return `${start}+${length} ${JSON.stringify(text)}`;
            },
          },
        ],
        check: (s) => {
          expect(s.model.document).toBe(s.doc);
          expect(s.model.lineCount).toBe(s.doc.lineCount);
          expect(s.model.allTokens()).toEqual(MarkdownModel.build(s.doc).allTokens());
        },
      });
    },
    RANDOM_TIMEOUT,
  );
});

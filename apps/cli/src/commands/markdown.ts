import { MarkdownModel, outlineOf, renderHtmlBody, renderHtmlDocument, tokenEnd } from '@puffinote/markdown';
import { computeStats } from '@puffinote/text';
import { checkHtmlOutput, loadDocument, saveText, titleFor } from '../infra/files.ts';
import type { ArgumentSpec, CommandSpec } from '../spec.ts';

/**
 * The Markdown commands: thin calls into the same engine the editor uses, so what the CLI renders,
 * counts and outlines matches what the app shows. Each reads one note, or stdin for `-`.
 */

const fileArg: ArgumentSpec = { name: 'file', description: 'Markdown file to read (- reads stdin)' };

export const html: CommandSpec = {
  name: 'html',
  description: 'Render a note to self-contained HTML',
  args: [fileArg],
  options: [
    { name: '--output', aliases: ['-o'], description: 'Write the HTML here instead of stdout' },
    {
      name: '--fragment',
      kind: 'flag',
      description: 'Only the <body> markup, without the document around it',
    },
    {
      name: '--force',
      kind: 'flag',
      description: 'Write the output even if its name does not end in .html or .htm',
    },
  ],
  run(p, con) {
    const file = p.arg('file');
    const output = p.str('--output') ?? '-';
    checkHtmlOutput(file, output, p.flag('--force'));
    const doc = loadDocument(con, file);
    const out = p.flag('--fragment') ? renderHtmlBody(doc) : renderHtmlDocument(doc, titleFor(file));
    saveText(con, out, output);
    return 0;
  },
};

export const cat: CommandSpec = {
  name: 'cat',
  description: 'Print the Markdown source as text (decoded the way the editor reads it)',
  args: [fileArg],
  run(p, con) {
    con.out(loadDocument(con, p.arg('file')).getText());
    return 0;
  },
};

export const tokens: CommandSpec = {
  name: 'tokens',
  description: "Dump the editor's token stream (for debugging the tokenizer)",
  args: [fileArg],
  run(p, con) {
    const doc = loadDocument(con, p.arg('file'));
    let out = '';
    for (const t of MarkdownModel.build(doc).allTokens()) {
      const text = doc.getText(t.start, t.length).replaceAll('\r', '\\r');
      out += `${t.type}\t${t.role}\t[${t.start}..${tokenEnd(t)}]\t«${text}»`;
      if (t.meta !== null) out += `\tmeta=${t.meta}`;
      out += '\n';
    }
    con.out(out);
    return 0;
  },
};

export const stats: CommandSpec = {
  name: 'stats',
  description: 'Word, character and line counts, and reading time',
  args: [fileArg],
  run(p, con) {
    const s = computeStats(loadDocument(con, p.arg('file')));
    con.out(
      `words\t${s.words}\ncharacters\t${s.characters}\nlines\t${s.lines}\nreading_minutes\t${s.readingMinutes}\n`,
    );
    return 0;
  },
};

export const outline: CommandSpec = {
  name: 'outline',
  description: "List the note's headings as an indented outline",
  args: [fileArg],
  run(p, con) {
    const doc = loadDocument(con, p.arg('file'));
    const items = outlineOf(doc, MarkdownModel.build(doc));
    con.out(items.map((h) => `${'  '.repeat(h.level - 1)}${h.text}\n`).join(''));
    return 0;
  },
};

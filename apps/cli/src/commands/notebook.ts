import { readFileSync, statSync } from 'node:fs';
import path from 'node:path';
import { isMarkdownFile } from '@puffinote/markdown';
import { readText } from '@puffinote/text';
import { CliError } from '../infra/errors.ts';
import { compareNames, resolveNotebook, walkNotebook } from '../infra/files.ts';
import type { ArgumentSpec, CommandSpec } from '../spec.ts';

/**
 * The notebook commands: the sidebar's file tree and search, for scripts and CI. They only read;
 * the notebook argument defaults to the current folder.
 */

const notebookArg: ArgumentSpec = {
  name: 'notebook',
  description: 'Notebook folder (default: the current folder)',
  arity: 'optional',
};

const notebookOf = (value: string | null): string => resolveNotebook(value ?? '.');

/** `/` separators on every platform, so output reads the same in scripts everywhere. */
const relative = (root: string, file: string): string => path.relative(root, file).split(path.sep).join('/');

export const tree: CommandSpec = {
  name: 'tree',
  description: "Print the notebook's file tree (folders first, dotfiles hidden)",
  args: [notebookArg],
  run(p, con) {
    const root = notebookOf(p.argOrNull('notebook'));
    let out = '';
    for (const e of walkNotebook(root))
      out += `${'  '.repeat(e.depth)}${e.name}${e.isDirectory ? '/' : ''}\n`;
    con.out(out);
    return 0;
  },
};

const SNIPPET_CONTEXT = 24;
const SNIPPET_LENGTH = 72;

export const search: CommandSpec = {
  name: 'search',
  description: 'Search the notes in a notebook (case-insensitive); prints path, hit count and a snippet',
  args: [{ name: 'query', description: 'Text to search for' }, notebookArg],
  options: [{ name: '--max', aliases: ['-n'], description: 'Maximum results', type: 'int', default: '200' }],
  run(p, con) {
    const query = p.arg('query').trim();
    if (query.length === 0) throw new CliError('Search query must not be empty.');
    const root = notebookOf(p.argOrNull('notebook'));
    const max = p.num('--max')!;
    if (max < 0) throw new CliError('--max must not be negative.');

    const hits: { rel: string; count: number; snippet: string }[] = [];
    for (const e of walkNotebook(root)) {
      if (e.isDirectory || !isMarkdownFile(e.name)) continue;
      let text: string;
      try {
        text = readText(readFileSync(e.path)).text;
      } catch {
        continue; // vanished or unreadable: skip it, like the sidebar's index
      }
      const count = countOccurrences(text, query);
      if (count > 0) hits.push({ rel: relative(root, e.path), count, snippet: snippet(text, query) });
    }
    hits.sort((a, b) => b.count - a.count || compareNames(a.rel, b.rel));
    con.out(
      hits
        .slice(0, max)
        .map((h) => `${h.rel}\t${h.count}\t${h.snippet}\n`)
        .join(''),
    );
    return 0;
  },
};

export const info: CommandSpec = {
  name: 'info',
  description: 'Summarize the notebook: file counts and total size',
  args: [notebookArg],
  run(p, con) {
    const root = notebookOf(p.argOrNull('notebook'));
    let files = 0;
    let notes = 0;
    let bytes = 0;
    for (const e of walkNotebook(root)) {
      if (e.isDirectory) continue;
      files++;
      if (isMarkdownFile(e.name)) notes++;
      try {
        bytes += statSync(e.path).size;
      } catch {
        // A file that vanished mid-walk adds nothing to the total.
      }
    }
    con.out(
      `notebook\t${root}\nfiles\t${files}\nnotes\t${notes}\nother\t${files - notes}\nbytes\t${bytes}\n`,
    );
    return 0;
  },
};

/**
 * Case-insensitive, non-overlapping matches. Lower-casing can change a string's length (`İ`), so
 * matching runs on a folded copy only when folding keeps the length; otherwise it falls back to a
 * case-insensitive regular expression over the original.
 */
function foldedIndexer(text: string, query: string): (from: number) => number {
  const t = text.toLowerCase();
  const q = query.toLowerCase();
  if (t.length === text.length && q.length === query.length) return (from) => t.indexOf(q, from);
  const re = new RegExp(query.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'giu');
  return (from) => {
    re.lastIndex = from;
    return re.exec(text)?.index ?? -1;
  };
}

export function countOccurrences(text: string, query: string): number {
  const next = foldedIndexer(text, query);
  let count = 0;
  for (let at = next(0); at !== -1; at = next(at + query.length)) count++;
  return count;
}

/** The first hit with some text around it, on one line, with "…" where it was cut. */
export function snippet(text: string, query: string): string {
  const at = Math.max(0, foldedIndexer(text, query)(0));
  const start = Math.max(0, at - SNIPPET_CONTEXT);
  const end = Math.min(text.length, start + SNIPPET_LENGTH);
  const body = text.slice(start, end).replace(/\s+/g, ' ');
  return ((start > 0 ? '…' : '') + body + (end < text.length ? '…' : '')).trim();
}

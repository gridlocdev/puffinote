import { findAll, readText, TextDocument } from '@puffinote/text';
import type { FileData } from '../platform/notebookStore.ts';
import { baseName, stemOf } from '../platform/paths.ts';
import { fileKind } from './tree.ts';

/** At most this many hits come back from one search, and at most `HITS_PER_NOTE` from one note. */
const MAX_HITS = 500;
const HITS_PER_NOTE = 50;
/** How much of a line shows before the match in a hit's preview. */
const PREVIEW_LEAD = 40;

export interface SearchHit {
  readonly path: string;
  readonly line: number;
  /** Offset of the match in the file. */
  readonly offset: number;
  readonly length: number;
  /** The line, trimmed, with the match at `[matchStart, matchStart + length)`. */
  readonly preview: string;
  readonly matchStart: number;
  /** The match is in the note's name (`preview` is the file name); opening it doesn't select anything. */
  readonly inTitle?: boolean;
}

/** What a search reads: the notebook's files in tree order, their bytes, and open tabs' unsaved text. */
export interface SearchSource {
  files(): readonly { readonly path: string }[];
  read(path: string): Promise<FileData>;
  /** The text of each open note by path, unsaved edits included. */
  openTexts(): ReadonlyMap<string, string>;
}

/** Text search across a notebook's notes: the Search panel (PRD §6.3) and the Library's Filter box (PRD §6). */
export class NotebookSearch {
  readonly #source: SearchSource;

  constructor(source: SearchSource) {
    this.#source = source;
  }

  /**
   * Search across the notebook's notes (PRD §6.3): their names, then their text, open tabs' unsaved text
   * included. An aborted
   * `signal` stops the scan between files and resolves with no hits.
   */
  async find(
    query: string,
    opts: { caseSensitive?: boolean; regex?: boolean; signal?: AbortSignal } = {},
  ): Promise<SearchHit[]> {
    if (!query.trim()) return [];
    const find = { caseSensitive: opts.caseSensitive ?? false, regex: opts.regex ?? false };
    // Notes whose name matches come first, each led by its name; the rest follow in tree order.
    const titled: SearchHit[] = [];
    const rest: SearchHit[] = [];
    const done = () => titled.concat(rest).slice(0, MAX_HITS);
    const open = this.#source.openTexts();
    for (const file of this.#source.files()) {
      if (opts.signal?.aborted) return [];
      if (fileKind(file.path) !== 'note') continue;
      const name = findAll(stemOf(file.path), query, find)[0];
      const hits = name ? titled : rest;
      if (name)
        hits.push({
          path: file.path,
          line: 0,
          offset: 0,
          length: name.length,
          preview: baseName(file.path),
          matchStart: name.start,
          inTitle: true,
        });
      let text: string;
      try {
        text = open.get(file.path) ?? readText((await this.#source.read(file.path)).bytes).text;
      } catch {
        continue;
      }
      if (opts.signal?.aborted) return [];
      const matches = findAll(text, query, find);
      if (matches.length === 0) continue;
      const doc = TextDocument.fromString(text);
      for (const m of matches.slice(0, HITS_PER_NOTE)) {
        const pos = doc.getPosition(m.start);
        const raw = doc.getLineText(pos.line);
        const lead = raw.length - raw.trimStart().length;
        const cut = Math.max(lead, pos.column - PREVIEW_LEAD);
        const preview = (cut > lead ? '…' : '') + raw.slice(cut).trimEnd();
        hits.push({
          path: file.path,
          line: pos.line,
          offset: m.start,
          length: m.length,
          preview,
          matchStart: pos.column - cut + (cut > lead ? 1 : 0),
        });
        if (titled.length + rest.length >= MAX_HITS) return done();
      }
    }
    return done();
  }

  /**
   * Which of the notes at `paths` have `query` in their text, ignoring case, open tabs' unsaved text included:
   * the Library's Filter box (PRD §6). Unlike `find`, it has no cap, so a common word keeps every note that
   * has it. An aborted `signal` stops the scan between files and resolves with nothing.
   */
  async notesContaining(
    query: string,
    paths: readonly string[],
    opts: { signal?: AbortSignal } = {},
  ): Promise<Set<string>> {
    const found = new Set<string>();
    if (!query.trim()) return found;
    const open = this.#source.openTexts();
    for (const path of paths) {
      if (opts.signal?.aborted) return new Set();
      if (fileKind(path) !== 'note') continue;
      let text: string;
      try {
        text = open.get(path) ?? readText((await this.#source.read(path)).bytes).text;
      } catch {
        continue;
      }
      if (containsText(text, query)) found.add(path);
    }
    return opts.signal?.aborted ? new Set() : found;
  }
}

function containsText(text: string, query: string): boolean {
  return findAll(text, query, { caseSensitive: false, regex: false }).length > 0;
}

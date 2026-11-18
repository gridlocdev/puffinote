import {
  findAll,
  isEmptyQuery,
  parseWordQuery,
  readText,
  TextDocument,
  WordIndex,
  wordMatches,
  type WordQuery,
} from '@puffinote/text';
import { ConflictError, type FileData, type NotebookStore } from '../platform/notebookStore.ts';
import { baseName, stemOf } from '../platform/paths.ts';
import { after, type Cancel } from '../schedule.ts';
import { cacheDir, fileKind } from './tree.ts';

/** At most this many hits come back from one search, and at most `HITS_PER_NOTE` from one note. */
const MAX_HITS = 500;
const HITS_PER_NOTE = 50;
/** How much of a line shows before the match in a hit's preview. */
const PREVIEW_LEAD = 40;
/** Notes read at once while the index catches up (one request each on the NAS). */
const READ_AHEAD = 8;
/** The saved index is written this long after the last change it took in. */
const SAVE_DELAY = 5000;

/** The saved index (PRD §6.3): each note's text as last read, with the size and time it had then. */
export const searchCachePath = `${cacheDir}/search-index.json`;
const CACHE_FORMAT = 1;

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

/** A file's size and time as the listing shows them; the index reads a note again when either changes. */
export interface SearchStat {
  readonly size: number;
  /** 0 when the store can't tell: such a note is read again on every search. */
  readonly modified: number;
}

/** What a search reads: the notebook's files in tree order, their sizes and times, bytes, and open tabs' text. */
export interface SearchSource {
  files(): readonly { readonly path: string }[];
  stat(path: string): SearchStat | undefined;
  read(path: string): Promise<FileData>;
  /** The text of each open note by path, unsaved edits included. */
  openTexts(): ReadonlyMap<string, string>;
  /** Where the index is saved between sessions; without it the index lives in memory only. */
  readonly store?: NotebookStore;
  /** The notebook closed: write nothing more. */
  closed?(): boolean;
  /** A failure nobody waits on (saving the index). */
  report?(e: unknown): void;
}

interface CachedNote {
  readonly modified: number;
  readonly size: number;
  readonly text: string;
}

/**
 * Search across a notebook's notes: the Search panel (PRD §6.3) and the Library's Filter box (PRD §6).
 *
 * It keeps an index: each note's text with the size and time the listing gave when it was read, and the words
 * of each note's name and text. A search reads only the notes whose size or time changed since, and the index
 * is saved in `.puffinote/cache/`, so a reopened notebook starts where it left off.
 */
export class NotebookSearch {
  readonly #source: SearchSource;
  readonly #notes = new Map<string, CachedNote>();
  readonly #index = new WordIndex();
  /** The text each path is indexed with: the cached text, or an open tab's unsaved text. */
  readonly #indexed = new Map<string, string>();
  #loaded: Promise<void> | null = null;
  /** Refreshes run one at a time; a call while one waits to start shares it. */
  #tail: Promise<void> = Promise.resolve();
  #queued: Promise<void> | null = null;
  #saveTimer: Cancel | null = null;
  #wroteIgnore = false;

  constructor(source: SearchSource) {
    this.#source = source;
  }

  /** Drop the pending save: the notebook closed. */
  dispose(): void {
    this.#saveTimer?.();
    this.#saveTimer = null;
  }

  /**
   * Bring the index up to date with the listing: read the notes that are new or whose size or time changed, and
   * drop the ones that went away. Safe to call often; a pass over unchanged notes reads nothing.
   */
  refresh(): Promise<void> {
    if (this.#queued) return this.#queued;
    const run = this.#tail.then(() => {
      this.#queued = null;
      return this.#refreshOnce();
    });
    this.#queued = run;
    this.#tail = run.catch((e: unknown) => this.#source.report?.(e));
    return run;
  }

  async #refreshOnce(): Promise<void> {
    await (this.#loaded ??= this.#load());
    const notes = this.#source.files().filter((f) => fileKind(f.path) === 'note');
    const present = new Set(notes.map((f) => f.path));
    let changed = false;
    for (const path of [...this.#notes.keys()])
      if (!present.has(path)) {
        this.#forget(path);
        changed = true;
      }
    const stale = notes
      .map((f) => ({ path: f.path, stat: this.#source.stat(f.path) }))
      .filter(({ path, stat }) => {
        const had = this.#notes.get(path);
        return (
          !had || !stat || stat.modified === 0 || had.modified !== stat.modified || had.size !== stat.size
        );
      });
    let next = 0;
    const worker = async () => {
      while (next < stale.length) {
        const { path, stat } = stale[next++]!;
        let text: string;
        try {
          text = readText((await this.#source.read(path)).bytes).text;
        } catch {
          // Gone or unreadable: it isn't searched until the listing changes again.
          if (this.#notes.has(path)) changed = true;
          this.#forget(path);
          continue;
        }
        const had = this.#notes.get(path);
        const note = { modified: stat?.modified ?? 0, size: stat?.size ?? -1, text };
        if (!had || had.modified !== note.modified || had.size !== note.size || had.text !== text)
          changed = true;
        this.#notes.set(path, note);
        this.#indexAs(path, text);
      }
    };
    await Promise.all(Array.from({ length: Math.min(READ_AHEAD, stale.length) }, worker));
    if (changed) this.#scheduleSave();
  }

  /**
   * Refresh, but stop waiting when `signal` aborts (a newer query): the index keeps catching up for the next one.
   * True when the refresh finished first.
   */
  async #refreshUnless(signal: AbortSignal | undefined): Promise<boolean> {
    if (signal?.aborted) return false;
    if (!signal) {
      await this.refresh();
      return true;
    }
    const stop = new Promise<false>((resolve) => signal.addEventListener('abort', () => resolve(false)));
    return Promise.race([this.refresh().then(() => true), stop]);
  }

  #forget(path: string): void {
    this.#notes.delete(path);
    this.#indexed.delete(path);
    this.#index.delete(path);
  }

  #indexAs(path: string, text: string): void {
    if (this.#indexed.get(path) === text) return;
    this.#indexed.set(path, text);
    this.#index.set(path, `${stemOf(path)}\n${text}`);
  }

  /** Index open tabs' unsaved text in place of what was read, and put back what was read for tabs closed since. */
  #overlay(open: ReadonlyMap<string, string>): void {
    for (const [path, text] of this.#indexed) {
      const want = open.get(path) ?? this.#notes.get(path)?.text;
      if (want === undefined) this.#forget(path);
      else if (want !== text) this.#indexAs(path, want);
    }
    for (const [path, text] of open) if (this.#notes.has(path)) this.#indexAs(path, text);
  }

  /**
   * Search the notebook's notes (PRD §6.3): every word of the query must start a word in the note's name or text,
   * and every "quoted phrase" must appear, ignoring case and accents. Notes whose name matches the whole query
   * come first; within each group the better match comes first (more hits, rarer words), then the more recently
   * edited note. An aborted `signal` resolves with no hits.
   */
  async find(query: string, opts: { signal?: AbortSignal } = {}): Promise<SearchHit[]> {
    const q = parseWordQuery(query);
    if (isEmptyQuery(q)) return [];
    if (!(await this.#refreshUnless(opts.signal))) return [];
    this.#overlay(this.#source.openTexts());

    const order = new Map(this.#source.files().map((f, i) => [f.path, i]));
    const ranked = [...this.#index.candidates(q)]
      .filter(([path]) => order.has(path))
      .map(([path, score]) => ({
        path,
        score,
        named: wordMatches(stemOf(path), q).length > 0,
        modified: this.#source.stat(path)?.modified ?? 0,
      }))
      .sort(
        (a, b) =>
          Number(b.named) - Number(a.named) ||
          b.score - a.score ||
          b.modified - a.modified ||
          order.get(a.path)! - order.get(b.path)!,
      );

    const hits: SearchHit[] = [];
    for (const note of ranked) {
      if (opts.signal?.aborted) return [];
      const text = this.#indexed.get(note.path) ?? '';
      // The index narrows by words; phrases are checked here, against the name and text together.
      if (q.phrases.length > 0 && wordMatches(`${stemOf(note.path)}\n${text}`, q).length === 0) continue;
      hits.push(...noteHits(note.path, text, q));
      if (hits.length >= MAX_HITS) break;
    }
    return hits.slice(0, MAX_HITS);
  }

  /**
   * Which of the notes at `paths` have `query` in their text, ignoring case, open tabs' unsaved text included:
   * the Library's Filter box (PRD §6). Unlike `find`, it has no cap, so a common word keeps every note that
   * has it. It reads the index's copy of each note. An aborted `signal` resolves with nothing.
   */
  async notesContaining(
    query: string,
    paths: readonly string[],
    opts: { signal?: AbortSignal } = {},
  ): Promise<Set<string>> {
    const found = new Set<string>();
    if (!query.trim()) return found;
    if (!(await this.#refreshUnless(opts.signal))) return found;
    const open = this.#source.openTexts();
    for (const path of paths) {
      if (fileKind(path) !== 'note') continue;
      const text = open.get(path) ?? this.#notes.get(path)?.text;
      if (text !== undefined && findAll(text, query, { caseSensitive: false, regex: false }).length > 0)
        found.add(path);
    }
    return found;
  }

  async #load(): Promise<void> {
    const store = this.#source.store;
    if (!store) return;
    let data: unknown;
    try {
      data = JSON.parse(readText((await store.read(searchCachePath)).bytes).text);
    } catch {
      return; // none yet, or unreadable: it is built again
    }
    for (const [path, note] of parseCache(data)) {
      this.#notes.set(path, note);
      this.#indexAs(path, note.text);
    }
  }

  #scheduleSave(): void {
    if (!this.#source.store) return;
    this.#saveTimer?.();
    this.#saveTimer = after(SAVE_DELAY, () => {
      this.#saveTimer = null;
      this.save().catch((e: unknown) => this.#source.report?.(e));
    });
  }

  /** Write the index to `.puffinote/cache/` now (it is otherwise written a few seconds after it changes). */
  async save(): Promise<void> {
    const store = this.#source.store;
    if (!store || this.#source.closed?.()) return;
    if (!this.#wroteIgnore) {
      // Keeps the cache out of git whatever the notebook's own .gitignore says.
      await store
        .write(`${cacheDir}/.gitignore`, new TextEncoder().encode('*\n'), { create: true })
        .catch((e: unknown) => {
          if (!(e instanceof ConflictError)) throw e;
        });
      this.#wroteIgnore = true;
    }
    const notes = [...this.#notes].map(([path, n]) => [path, n.modified, n.size, n.text]);
    const bytes = new TextEncoder().encode(JSON.stringify({ format: CACHE_FORMAT, notes }));
    await store.write(searchCachePath, bytes, { force: true });
  }
}

/** The saved index's notes; a malformed row is skipped, and another format is ignored whole. */
function parseCache(data: unknown): Map<string, CachedNote> {
  const out = new Map<string, CachedNote>();
  if (typeof data !== 'object' || data === null) return out;
  const { format, notes } = data as { format?: unknown; notes?: unknown };
  if (format !== CACHE_FORMAT || !Array.isArray(notes)) return out;
  for (const row of notes as unknown[]) {
    if (!Array.isArray(row)) continue;
    const [path, modified, size, text] = row as unknown[];
    if (
      typeof path === 'string' &&
      typeof modified === 'number' &&
      typeof size === 'number' &&
      typeof text === 'string'
    )
      out.set(path, { modified, size, text });
  }
  return out;
}

/**
 * A note's hits: its name when that has any part of the query, then each line with a match, its first match
 * marked. Parts of the query may be split between the name and the text, so each marks wherever it is.
 */
function noteHits(path: string, text: string, q: WordQuery): SearchHit[] {
  const hits: SearchHit[] = [];
  const name = wordMatches(stemOf(path), q, { partial: true })[0];
  if (name)
    hits.push({
      path,
      line: 0,
      offset: 0,
      length: name.length,
      preview: baseName(path),
      matchStart: name.start,
      inTitle: true,
    });
  const doc = TextDocument.fromString(text);
  let lastLine = -1;
  for (const m of wordMatches(text, q, { partial: true })) {
    const pos = doc.getPosition(m.start);
    if (pos.line === lastLine) continue;
    lastLine = pos.line;
    const raw = doc.getLineText(pos.line);
    const lead = raw.length - raw.trimStart().length;
    const cut = Math.max(lead, pos.column - PREVIEW_LEAD);
    const preview = (cut > lead ? '…' : '') + raw.slice(cut).trimEnd();
    hits.push({
      path,
      line: pos.line,
      offset: m.start,
      length: m.length,
      preview,
      matchStart: pos.column - cut + (cut > lead ? 1 : 0),
    });
    if (hits.length >= HITS_PER_NOTE + 1) break;
  }
  return hits;
}

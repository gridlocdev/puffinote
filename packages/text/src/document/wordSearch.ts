/**
 * Word search: what the notebook's Search panel matches (PRD §6.3). Text is folded (lower case, accents taken
 * off) and split into words of letters and digits. A query is words, each matching the start of a word, and
 * "quoted phrases", each matching that run of text. A note matches when it has every one.
 */

/** A query, folded: words that must each start a word, and phrases that must each appear. */
export interface WordQuery {
  readonly words: readonly string[];
  readonly phrases: readonly string[];
}

/** Text folded for matching, with each folded code unit's offset in the original text. */
export interface FoldedText {
  readonly text: string;
  /** `starts[i]` is the original offset of folded unit `i`; `starts[text.length]` is the original length. */
  readonly starts: Uint32Array;
}

const WORD = /[\p{L}\p{N}]+/gu;
const MARK = /\p{M}/gu;
const ASCII = /^\p{ASCII}*$/u;

/** One character folded: lower case, with its accents taken off ("É" → "e", "ß" stays "ß"). */
function foldChar(ch: string): string {
  return ch.normalize('NFD').replace(MARK, '').toLowerCase();
}

/** `text` folded for matching (lower case, no accents). */
export function foldString(text: string): string {
  return ASCII.test(text) ? text.toLowerCase() : text.normalize('NFD').replace(MARK, '').toLowerCase();
}

/** `text` folded for matching, keeping where each folded unit came from so matches map back to it. */
export function foldWithOffsets(text: string): FoldedText {
  if (ASCII.test(text)) {
    const starts = new Uint32Array(text.length + 1);
    for (let i = 0; i <= text.length; i++) starts[i] = i;
    return { text: text.toLowerCase(), starts };
  }
  let out = '';
  const starts: number[] = [];
  let i = 0;
  for (const ch of text) {
    const folded = ch.charCodeAt(0) < 0x80 ? ch.toLowerCase() : foldChar(ch);
    for (let k = 0; k < folded.length; k++) starts.push(i);
    out += folded;
    i += ch.length;
  }
  starts.push(i);
  return { text: out, starts: Uint32Array.from(starts) };
}

/** The words of already folded text. */
export function foldedWords(folded: string): string[] {
  return folded.match(WORD) ?? [];
}

/** How many times each word appears in `text`. */
export function countWords(text: string): Map<string, number> {
  const counts = new Map<string, number>();
  for (const w of foldedWords(foldString(text))) counts.set(w, (counts.get(w) ?? 0) + 1);
  return counts;
}

/**
 * Parse a query: "double-quoted" parts are phrases, the rest is words. Punctuation between words is ignored
 * ("re-plan" is the words "re" and "plan"). An unclosed quote runs to the end. A phrase with no letters or
 * digits is dropped.
 */
export function parseWordQuery(query: string): WordQuery {
  const words: string[] = [];
  const phrases: string[] = [];
  const parts = query.split('"');
  parts.forEach((part, i) => {
    const folded = foldString(part);
    if (i % 2 === 0) words.push(...foldedWords(folded));
    else {
      const phrase = folded.trim().replace(/\s+/g, ' ');
      if (foldedWords(phrase).length > 0) phrases.push(phrase);
    }
  });
  return { words: [...new Set(words)], phrases: [...new Set(phrases)] };
}

/** True when the query has nothing to look for. */
export function isEmptyQuery(q: WordQuery): boolean {
  return q.words.length === 0 && q.phrases.length === 0;
}

/** Every word a query needs, phrases' words included: what the index can narrow by. */
function neededWords(q: WordQuery): string[] {
  const all = [...q.words];
  for (const p of q.phrases) all.push(...foldedWords(p));
  return [...new Set(all)];
}

/** One match of a query in a text, in the original text's offsets. */
export interface WordMatch {
  readonly start: number;
  readonly length: number;
}

/**
 * Where the query matches in `text`, in order and not overlapping: each word at the start of a word, each phrase
 * anywhere (whitespace in it matching any run of whitespace). Empty when the text lacks a word or phrase, unless
 * `partial` is set: then it marks whichever of them are there.
 */
export function wordMatches(
  text: string,
  q: WordQuery,
  opts: { readonly partial?: boolean } = {},
): WordMatch[] {
  if (isEmptyQuery(q)) return [];
  const every = !opts.partial;
  const folded = foldWithOffsets(text);
  const ranges: { start: number; end: number }[] = [];
  for (const phrase of q.phrases) {
    const re = new RegExp(phrase.split(' ').map(escape).join('\\s+'), 'g');
    let found = false;
    for (const m of folded.text.matchAll(re)) {
      found = true;
      ranges.push({ start: m.index, end: m.index + m[0].length });
    }
    if (!found && every) return [];
  }
  if (q.words.length > 0) {
    const seen = new Set<string>();
    for (const m of folded.text.matchAll(WORD)) {
      const word = m[0];
      // The longest query word this word starts with is the match.
      let best = '';
      for (const w of q.words) if (w.length > best.length && word.startsWith(w)) best = w;
      if (best) {
        seen.add(best);
        ranges.push({ start: m.index, end: m.index + best.length });
      }
    }
    // A word the text never starts with means no match, unless a longer query word covered it ("plan" and "planet").
    if (every)
      for (const w of q.words)
        if (!seen.has(w) && !q.words.some((o) => o !== w && seen.has(o) && o.startsWith(w))) return [];
  }
  ranges.sort((a, b) => a.start - b.start || b.end - a.end);
  const out: WordMatch[] = [];
  let end = -1;
  for (const r of ranges) {
    if (r.start < end) continue;
    const start = folded.starts[r.start] ?? 0;
    const stop = folded.starts[r.end] ?? text.length;
    out.push({ start, length: stop - start });
    end = r.end;
  }
  return out;
}

function escape(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

/**
 * Word counts of many documents, for finding which ones have every word of a query (each as a word start) and
 * ranking them. Pure and in memory; the caller decides what a document is and when it changes.
 */
export class WordIndex {
  /** Each document's word counts. */
  readonly #docs = new Map<string, ReadonlyMap<string, number>>();
  /** Which documents have each word. */
  readonly #postings = new Map<string, Set<string>>();
  /** Every word, sorted, for finding the words a prefix starts; rebuilt when words come or go. */
  #sorted: string[] | null = null;

  get size(): number {
    return this.#docs.size;
  }

  has(id: string): boolean {
    return this.#docs.has(id);
  }

  /** Index (or index again) document `id` with `text`. */
  set(id: string, text: string): void {
    this.delete(id);
    const counts = countWords(text);
    this.#docs.set(id, counts);
    for (const w of counts.keys()) {
      let ids = this.#postings.get(w);
      if (!ids) {
        ids = new Set();
        this.#postings.set(w, ids);
        this.#sorted = null;
      }
      ids.add(id);
    }
  }

  delete(id: string): void {
    const counts = this.#docs.get(id);
    if (!counts) return;
    this.#docs.delete(id);
    for (const w of counts.keys()) {
      const ids = this.#postings.get(w);
      ids?.delete(id);
      if (ids?.size === 0) {
        this.#postings.delete(w);
        this.#sorted = null;
      }
    }
  }

  /** Every indexed word that starts with `prefix`. */
  #wordsStarting(prefix: string): string[] {
    this.#sorted ??= [...this.#postings.keys()].sort();
    const sorted = this.#sorted;
    let lo = 0;
    let hi = sorted.length;
    while (lo < hi) {
      const mid = (lo + hi) >> 1;
      if (sorted[mid]! < prefix) lo = mid + 1;
      else hi = mid;
    }
    const out: string[] = [];
    for (let i = lo; i < sorted.length && sorted[i]!.startsWith(prefix); i++) out.push(sorted[i]!);
    return out;
  }

  /**
   * The documents that may match `q`, with a score: every word of the query (phrases' words included) starts a
   * word in them. Phrases themselves are checked against the text by the caller (`wordMatches`). A higher score
   * is a better match: the words appear more often, and rarer words count for more.
   */
  candidates(q: WordQuery): Map<string, number> {
    const words = neededWords(q);
    const scores = new Map<string, number>();
    if (words.length === 0) return scores;
    const total = this.#docs.size;
    let first = true;
    for (const w of words) {
      const terms = this.#wordsStarting(w);
      const found = new Map<string, number>();
      for (const t of terms)
        for (const id of this.#postings.get(t) ?? [])
          found.set(id, (found.get(id) ?? 0) + (this.#docs.get(id)?.get(t) ?? 0));
      if (found.size === 0) return new Map();
      const idf = Math.log(1 + total / found.size);
      if (first) {
        for (const [id, n] of found) scores.set(id, Math.log(1 + n) * idf);
        first = false;
      } else {
        for (const [id, s] of scores) {
          const n = found.get(id);
          if (n === undefined) scores.delete(id);
          else scores.set(id, s + Math.log(1 + n) * idf);
        }
      }
      if (scores.size === 0) return scores;
    }
    return scores;
  }
}

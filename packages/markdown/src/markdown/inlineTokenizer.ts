import { isAsciiPunctuation, isLetterOrDigit, isWhiteSpace } from './chars.ts';
import { token, type Token, type TokenRole, type TokenType } from './token.ts';

/**
 * Tokenizes the inline constructs inside one line's content region: emphasis, code, links, images,
 * autolinks, wiki-links, embeds, escapes, strikethrough, highlight. The scanner is tolerant (PRD §10.2): an
 * unmatched opening marker stays literal text until validly closed, so styling only applies to a complete
 * construct and typing never flickers.
 *
 * Emphasis, strong, strike and highlight content is a single token with no recursion (PRD §11.6–§11.7); link
 * text recurses so emphasis inside a link still styles.
 */
export function tokenizeInline(
  text: string,
  absStart: number,
  from: number,
  to: number,
  output: Token[],
  detectHardBreak = false,
): void {
  let contentTo = to;
  let breakStart = -1;
  if (detectHardBreak && to > from) {
    if (text.charAt(to - 1) === '\\') {
      // Only an unescaped backslash breaks: `C:\\` ends in an escaped one (CommonMark §6.7).
      let s = to - 1;
      while (s > from && text.charAt(s - 1) === '\\') s--;
      if (((to - s) & 1) === 1) breakStart = to - 1;
    } else {
      let s = to;
      while (s > from && text.charAt(s - 1) === ' ') s--;
      if (to - s >= 2 && s > from) breakStart = s;
    }
  }
  if (breakStart >= 0) contentTo = breakStart;

  new InlineTokenizer(text, absStart, output).run(from, contentTo);
  if (breakStart >= 0) output.push(token(absStart + breakStart, to - breakStart, 'hardBreak', 'marker'));
}

/** A remembered forward search: scanning from `from` found `result` (-1 for none). */
interface ScanMemo {
  readonly from: number;
  readonly result: number;
}

/**
 * Every opener looks ahead for its closer, so a line of unmatched openers would rescan to its end once per
 * opener. Searches are memoized instead: a forward scan's answer holds for any later start up to the position
 * it found (or for every later start when it found nothing), and brackets are matched for the whole line in
 * one pass. That keeps a line near-linear without changing what it tokenizes to.
 */
class InlineTokenizer {
  private readonly text: string;
  private readonly absStart: number;
  private readonly out: Token[];
  private pendingTextStart = -1;
  /** Per-line node id; each construct claims one, shared across link-text recursion. */
  private nextNode = 1;
  private scans: Map<string, ScanMemo> | undefined;
  private pairTables: Map<string, Int32Array> | undefined;
  /** Characters examined by plain (unmemoized) scans so far, and how many the line may spend that way. */
  private work = 0;
  private readonly budget: number;
  private runChar = '';
  private runTo = -1;
  private runFrom = 0;
  private runEnd = 0;

  constructor(text: string, absStart: number, out: Token[]) {
    this.text = text;
    this.absStart = absStart;
    this.out = out;
    this.budget = 4 * text.length + 256;
  }

  run(from: number, to: number): void {
    let i = from;
    while (i < to) {
      const consumed = this.tryConstruct(i, to);
      if (consumed > 0) {
        i += consumed;
      } else {
        if (this.pendingTextStart < 0) this.pendingTextStart = i;
        i++;
      }
    }
    this.flushText(to);
  }

  private tryConstruct(i: number, to: number): number {
    switch (this.text.charAt(i)) {
      case '\\':
        return this.tryEscape(i, to);
      case '`':
        return this.tryInlineCode(i, to);
      case '!':
        return this.tryImageOrEmbed(i, to);
      case '[':
        return this.tryLinkOrWiki(i, to);
      case '<':
        return this.tryAutolink(i, to);
      case '~':
        return this.tryDelimited(i, to, '~', 2, 2, 'strikethrough');
      case '=':
        return this.tryDelimited(i, to, '=', 2, 2, 'highlight');
      case '*':
        return this.tryEmphasis(i, to, '*');
      case '_':
        return this.tryEmphasis(i, to, '_');
      case 'h':
      case 'H':
        return this.tryBareUrl(i, to);
      default:
        return 0;
    }
  }

  private tryEscape(i: number, to: number): number {
    if (i + 1 >= to || !isAsciiPunctuation(this.text.charAt(i + 1))) return 0;
    const node = this.nextNode++;
    this.flushText(i);
    this.emit(i, 1, 'escape', 'marker', null, node);
    this.emit(i + 1, 1, 'escape', 'content', null, node);
    return 2;
  }

  private tryInlineCode(i: number, to: number): number {
    const run = this.runLength(i, to, '`');
    const contentStart = i + run;
    const j = this.memoScan('`', run, to, contentStart, () => {
      let k = contentStart;
      while (k < to) {
        if (this.text.charAt(k) === '`') {
          const closeRun = this.runLength(k, to, '`');
          if (closeRun === run) return k;
          k += closeRun;
        } else {
          k++;
        }
      }
      return -1;
    });
    if (j < 0 || j === contentStart) return 0; // an empty `` `` stays literal
    const node = this.nextNode++;
    this.flushText(i);
    this.emit(i, run, 'marker', 'marker', null, node);
    this.emit(contentStart, j - contentStart, 'inlineCode', 'content', null, node);
    this.emit(j, run, 'marker', 'marker', null, node);
    return j + run - i;
  }

  private tryImageOrEmbed(i: number, to: number): number {
    const t = this.text;
    // ![[target]] embed
    if (i + 2 < to && t.charAt(i + 1) === '[' && t.charAt(i + 2) === '[') {
      const close = this.indexOf(']]', i + 3, to);
      if (close > i + 3) {
        const node = this.nextNode++;
        this.flushText(i);
        this.emit(i, 3, 'marker', 'marker', null, node);
        this.emit(i + 3, close - (i + 3), 'embed', 'content', t.slice(i + 3, close), node);
        this.emit(close, 2, 'marker', 'marker', null, node);
        return close + 2 - i;
      }
      return 0;
    }

    // ![alt](url) image
    if (i + 1 < to && t.charAt(i + 1) === '[') {
      const altClose = this.matchBracket(i + 1, to);
      if (altClose > 0 && altClose + 1 < to && t.charAt(altClose + 1) === '(') {
        const urlClose = this.matchParen(altClose + 1, to);
        if (urlClose > 0) {
          const node = this.nextNode++;
          const altStart = i + 2;
          const [urlStart, urlLen] = this.destination(altClose + 2, urlClose);
          this.flushText(i);
          this.emit(i, 2, 'marker', 'marker', null, node);
          this.emit(altStart, altClose - altStart, 'imageAlt', 'content', t.slice(altStart, altClose), node);
          this.emit(altClose, 2, 'marker', 'marker', null, node);
          this.emit(urlStart, urlLen, 'imageUrl', 'marker', t.slice(urlStart, urlStart + urlLen), node);
          // The closing marker covers any title plus the ')'.
          this.emit(urlStart + urlLen, urlClose + 1 - (urlStart + urlLen), 'marker', 'marker', null, node);
          return urlClose + 1 - i;
        }
      }
    }
    return 0;
  }

  private tryLinkOrWiki(i: number, to: number): number {
    const t = this.text;
    // [[target]] wiki-link
    if (i + 1 < to && t.charAt(i + 1) === '[') {
      const close = this.indexOf(']]', i + 2, to);
      if (close > i + 2) {
        const node = this.nextNode++;
        this.flushText(i);
        this.emit(i, 2, 'marker', 'marker', null, node);
        this.emit(i + 2, close - (i + 2), 'wikiLink', 'content', t.slice(i + 2, close), node);
        this.emit(close, 2, 'marker', 'marker', null, node);
        return close + 2 - i;
      }
      return 0;
    }

    // [text](url) link
    const textClose = this.matchBracket(i, to);
    if (textClose > 0 && textClose + 1 < to && t.charAt(textClose + 1) === '(') {
      const urlClose = this.matchParen(textClose + 1, to);
      if (urlClose > 0) {
        const node = this.nextNode++;
        const [urlStart, urlLen] = this.destination(textClose + 2, urlClose);
        this.flushText(i);
        this.emit(i, 1, 'marker', 'marker', null, node);
        this.run(i + 1, textClose); // link text recurses, sharing this tokenizer's node counter
        this.emit(textClose, 2, 'marker', 'marker', null, node);
        this.emit(urlStart, urlLen, 'linkUrl', 'marker', t.slice(urlStart, urlStart + urlLen), node);
        this.emit(urlStart + urlLen, urlClose + 1 - (urlStart + urlLen), 'marker', 'marker', null, node);
        return urlClose + 1 - i;
      }
    }
    return 0;
  }

  private tryAutolink(i: number, to: number): number {
    const close = this.find('>', i + 1);
    if (close < 0 || close >= to) return 0;
    if (close === i + 1 || this.within(' ', i + 1, close) || !this.looksLikeUri(i + 1, close)) return 0;
    const inner = this.text.slice(i + 1, close);
    const node = this.nextNode++;
    this.flushText(i);
    this.emit(i, 1, 'marker', 'marker', null, node);
    this.emit(i + 1, close - (i + 1), 'autolink', 'content', inner, node);
    this.emit(close, 1, 'marker', 'marker', null, node);
    return close + 1 - i;
  }

  private tryBareUrl(i: number, to: number): number {
    const t = this.text;
    if (!t.startsWith('http://', i) && !t.startsWith('https://', i)) return 0;
    // Only at a word boundary.
    if (i > 0 && (isLetterOrDigit(t.charAt(i - 1)) || t.charAt(i - 1) === '/')) return 0;

    let j = i;
    while (j < to && !isWhiteSpace(t.charAt(j)) && t.charAt(j) !== '<' && t.charAt(j) !== ')') j++;
    while (j > i && isTrailingUrlPunct(t.charAt(j - 1))) j--;
    if (j - i < 11) return 0;

    this.flushText(i);
    this.emit(i, j - i, 'autolink', 'content', t.slice(i, j));
    return j - i;
  }

  private tryEmphasis(i: number, to: number, c: string): number {
    const run = this.runLength(i, to, c);
    const candidates: [number, TokenType][] =
      run >= 3
        ? [
            [3, 'strongEmphasis'],
            [2, 'strong'],
            [1, 'emphasis'],
          ]
        : run === 2
          ? [
              [2, 'strong'],
              [1, 'emphasis'],
            ]
          : [[1, 'emphasis']];
    for (const [count, type] of candidates) {
      const n = this.tryDelimited(i, to, c, count, count, type);
      if (n > 0) return n;
    }
    return 0;
  }

  /** Match a paired delimiter run of `c` of length `count`. */
  private tryDelimited(
    i: number,
    to: number,
    c: string,
    minRun: number,
    count: number,
    type: TokenType,
  ): number {
    const t = this.text;
    if (this.runLength(i, to, c) < minRun) return 0;

    const contentStart = i + count;
    // No leading space inside; no empty span.
    if (contentStart >= to || t.charAt(contentStart) === c || isWhiteSpace(t.charAt(contentStart))) return 0;

    // Underscore does not delimit intra-word, so snake_case stays literal (CommonMark's left-flanking rule).
    if (c === '_' && i > 0 && isLetterOrDigit(t.charAt(i - 1))) return 0;

    // `contentStart` is never a `c`, so every scan enters each run of `c` at its first char and the answer
    // depends only on where the scan starts: memoizable.
    const j = this.memoScan(c, count, to, contentStart, () => {
      let k = contentStart;
      while (k < to) {
        // A delimiter escaped by an odd run of backslashes is a literal, not a closer.
        if (t.charAt(k) === c && !isWhiteSpace(t.charAt(k - 1)) && !this.isEscaped(k)) {
          const closeRun = this.runLength(k, to, c);
          const intraWord = c === '_' && k + closeRun < to && isLetterOrDigit(t.charAt(k + closeRun));
          if (closeRun >= count && !intraWord) return k;
          k += closeRun;
        } else {
          k++;
        }
      }
      return -1;
    });
    if (j < 0) return 0;
    const node = this.nextNode++;
    this.flushText(i);
    this.emit(i, count, 'marker', 'marker', null, node);
    this.emit(contentStart, j - contentStart, type, 'content', null, node);
    this.emit(j, count, 'marker', 'marker', null, node);
    return j + count - i;
  }

  /**
   * A forward scan from `from` that stops at `to` at the latest, memoized under `kind`, `param` and `to`
   * (which must fix everything the scan depends on besides its start). A scan from `from` that found
   * `result` answers any start in `[from, result]`, or any later start when it found nothing, provided
   * starts never fall inside a run the scan skips over.
   *
   * Scans run plainly until they have cost a few times the line's length; only a line that pathological
   * pays for the memo.
   */
  private memoScan(kind: string, param: number, to: number, from: number, scan: () => number): number {
    if (this.work <= this.budget) {
      const result = scan();
      this.work += (result < 0 ? to : result) - from;
      return result;
    }
    const key = `${kind}${param}:${to}`;
    const scans = (this.scans ??= new Map());
    const memo = scans.get(key);
    if (memo && from >= memo.from && (memo.result < 0 || from <= memo.result)) return memo.result;
    const result = scan();
    scans.set(key, { from, result });
    return result;
  }

  /** `text.indexOf(needle, from)`, memoized per needle. */
  private find(needle: string, from: number): number {
    return this.memoScan(needle, 0, this.text.length, from, () => this.text.indexOf(needle, from));
  }

  /** Whether `needle` occurs inside `[from, to)`. */
  private within(needle: string, from: number, to: number): boolean {
    const k = this.find(needle, from);
    return k >= 0 && k + needle.length <= to;
  }

  /** Whether `text[from, to)` looks like an autolink target: `scheme:…`, or a bare email address. */
  private looksLikeUri(from: number, to: number): boolean {
    const colon = this.find(':', from);
    if (colon < 0 || colon >= to || colon === from)
      return this.within('@', from, to) && this.within('.', from, to);
    for (let k = from; k < colon; k++) {
      const c = this.text.charAt(k);
      if (!isLetterOrDigit(c) && c !== '+' && c !== '-' && c !== '.') return false;
    }
    return true;
  }

  private isEscaped(pos: number): boolean {
    let slashes = 0;
    for (let k = pos - 1; k >= 0 && this.text.charAt(k) === '\\'; k--) slashes++;
    return (slashes & 1) === 1;
  }

  private flushText(end: number): void {
    if (this.pendingTextStart >= 0 && end > this.pendingTextStart) {
      this.emit(this.pendingTextStart, end - this.pendingTextStart, 'text', 'content');
    }
    this.pendingTextStart = -1;
  }

  private emit(
    start: number,
    length: number,
    type: TokenType,
    role: TokenRole,
    meta: string | null = null,
    node = 0,
  ): void {
    if (length <= 0) return;
    this.out.push(token(this.absStart + start, length, type, role, meta, node));
  }

  /**
   * The destination inside a link's parentheses `[from, to)`: leading whitespace skipped, ending at the first
   * whitespace, so an optional `"title"` is excluded from the URL.
   */
  private destination(from: number, to: number): [number, number] {
    let s = from;
    while (s < to && isWhiteSpace(this.text.charAt(s))) s++;
    let e = s;
    while (e < to && !isWhiteSpace(this.text.charAt(e))) e++;
    return [s, e - s];
  }

  /** The length of the run of `c` at `i`, bounded by `to`. Remembers the last run, so each position inside a
   * long run costs O(1) instead of rescanning the rest of the run. */
  private runLength(i: number, to: number, c: string): number {
    if (c === this.runChar && to === this.runTo && i >= this.runFrom && i < this.runEnd)
      return this.runEnd - i;
    let n = 0;
    while (i + n < to && this.text.charAt(i + n) === c) n++;
    this.runChar = c;
    this.runTo = to;
    this.runFrom = i;
    this.runEnd = i + n;
    return n;
  }

  private indexOf(needle: string, from: number, to: number): number {
    const idx = this.find(needle, from);
    return idx >= 0 && idx + needle.length <= to ? idx : -1;
  }

  /** Index of the `]` closing the `[` at `open`, honouring nesting and escapes; or -1. */
  private matchBracket(open: number, to: number): number {
    return this.matchPair(open, to, '[', ']');
  }

  /** Index of the `)` closing the `(` at `open`, honouring nesting and escapes; or -1. */
  private matchParen(open: number, to: number): number {
    return this.matchPair(open, to, '(', ')');
  }

  /**
   * Matches come from one stack pass over the whole line (`pairTable`). A scan from `open` sees the same
   * escapes as that pass, because it enters every backslash run at its start, so the first closer that
   * balances `open` is the same; it only has to lie before `to`. An opener that is itself escaped isn't in
   * the table, and falls back to the direct scan.
   */
  private matchPair(open: number, to: number, opener: string, closer: string): number {
    if (this.work <= this.budget || this.isEscaped(open)) {
      const k = this.scanPair(open, to, opener, closer);
      this.work += (k < 0 ? to : k) - open;
      return k;
    }
    const k = this.pairTable(opener, closer)[open]!;
    return k >= 0 && k < to ? k : -1;
  }

  /** For each unescaped `opener` in the line, the index of its balancing `closer`, or -1. */
  private pairTable(opener: string, closer: string): Int32Array {
    const tables = (this.pairTables ??= new Map());
    let table = tables.get(opener);
    if (!table) {
      const t = this.text;
      table = new Int32Array(t.length).fill(-1);
      const stack: number[] = [];
      for (let k = 0; k < t.length; k++) {
        const ch = t.charAt(k);
        if (ch === '\\') {
          k++;
        } else if (ch === opener) {
          stack.push(k);
        } else if (ch === closer) {
          const o = stack.pop();
          if (o !== undefined) table[o] = k;
        }
      }
      tables.set(opener, table);
    }
    return table;
  }

  private scanPair(open: number, to: number, opener: string, closer: string): number {
    let depth = 0;
    for (let k = open; k < to; k++) {
      const ch = this.text.charAt(k);
      if (ch === '\\') {
        k++;
        continue;
      }
      if (ch === opener) {
        depth++;
      } else if (ch === closer) {
        depth--;
        if (depth === 0) return k;
      }
    }
    return -1;
  }
}

function isTrailingUrlPunct(c: string): boolean {
  return c === '.' || c === ',' || c === ';' || c === ':' || c === '!' || c === '?' || c === ')';
}

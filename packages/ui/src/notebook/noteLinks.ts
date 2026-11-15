/**
 * Following links between notes (PRD §10.3, §11.5): what a ⌘-clicked link points at, and which notebook file
 * it names. Pure, so the rules are tested without a notebook or a DOM.
 */
import { baseName, extOf, joinPath, parentOf, pathKey, resolveRelative } from '../platform/paths.ts';
import { leavesNotebook } from './urls.ts';

/** A link as the editor hands it over: its destination, or `[[target]]` for a wiki link. */
export type NoteLink =
  /** An `http`, `https` or `mailto` link: handed to the browser or the OS. */
  | { readonly kind: 'external'; readonly url: string }
  /** A relative link (`other.md`, `sub/other%20note.md#plan`), resolved against the note's folder. */
  | { readonly kind: 'file'; readonly ref: string; readonly fragment: string }
  /** A wiki link (`[[name]]`, `[[folder/name|label]]`, `[[name#heading]]`). */
  | { readonly kind: 'wiki'; readonly target: string; readonly fragment: string }
  /** A link to a heading in the same note (`#plan`, `[[#plan]]`). */
  | { readonly kind: 'heading'; readonly fragment: string };

/** What a link's destination resolves to in the notebook. */
export type LinkTarget =
  | { readonly kind: 'found'; readonly path: string }
  | { readonly kind: 'missing'; readonly name: string }
  | { readonly kind: 'ambiguous'; readonly name: string };

/** The `href` the editor gives a wiki link, so one attribute carries both kinds of link. */
export function wikiHref(target: string): string {
  return `[[${target}]]`;
}

/**
 * What `href` points at, or null for a link nothing follows: another scheme (`file:`, `javascript:`, `data:`
 * and so on are never handed to the OS), a protocol-relative `//host` link, or an empty one.
 */
export function parseLinkHref(href: string): NoteLink | null {
  const h = href.trim();
  if (h.startsWith('[[') && h.endsWith(']]')) {
    const inner = h.slice(2, -2);
    const bar = inner.indexOf('|');
    const target = (bar >= 0 ? inner.slice(0, bar) : inner).trim();
    const hash = target.indexOf('#');
    const name = (hash >= 0 ? target.slice(0, hash) : target).trim();
    const fragment = hash >= 0 ? target.slice(hash + 1).trim() : '';
    if (!name) return fragment ? { kind: 'heading', fragment } : null;
    return { kind: 'wiki', target: name, fragment };
  }
  if (/^(https?:|mailto:)/i.test(h)) return { kind: 'external', url: h };
  if (!h || leavesNotebook(h)) return null;
  const hash = h.indexOf('#');
  const path = (hash >= 0 ? h.slice(0, hash) : h).replace(/\?.*$/, '');
  const fragment = hash >= 0 ? decodeSafe(h.slice(hash + 1)) : '';
  if (!path) return fragment ? { kind: 'heading', fragment } : null;
  return { kind: 'file', ref: path, fragment };
}

/**
 * The notebook file a `file` or `wiki` link written in the note at `from` names, among `files` (every file
 * path in the notebook).
 *
 * - A relative link resolves against the note's folder (a leading `/` against the notebook's top level), as
 *   written; when no file has that exact path, one that differs only in case or Unicode form is taken.
 * - A wiki link tries the path as written from the note's folder and then from the top level, with `.md`
 *   added unless it has an extension, exactly and then ignoring case; then a file anywhere in the notebook
 *   whose name (or trailing folders and name, for `[[folder/name]]`) matches, ignoring case. More than one
 *   such file is `ambiguous`.
 */
export function resolveNoteLink(
  from: string,
  link: Extract<NoteLink, { kind: 'file' | 'wiki' }>,
  files: readonly string[],
): LinkTarget {
  const exact = new Set(files);
  const loose = new Map<string, string[]>();
  for (const f of files) {
    const k = pathKey(f, true);
    loose.set(k, [...(loose.get(k) ?? []), f]);
  }
  const lookup = (p: string): string | null => {
    if (exact.has(p)) return p;
    const hits = loose.get(pathKey(p, true));
    return hits?.length === 1 ? hits[0]! : null;
  };

  if (link.kind === 'file') {
    const p = resolveRelative(from, link.ref);
    const hit = p === null ? null : lookup(p);
    return hit ? { kind: 'found', path: hit } : { kind: 'missing', name: decodeSafe(link.ref) };
  }

  const target = link.target.replace(/^\/+/, '');
  const names = extOf(target) ? [target, `${target}.md`] : [`${target}.md`, target];
  for (const dir of [parentOf(from), '']) {
    for (const n of names) {
      const p = resolveRelative(joinPath(dir, 'x'), n);
      const hit = p === null ? null : exact.has(p) ? p : null;
      if (hit) return { kind: 'found', path: hit };
    }
  }
  for (const dir of [parentOf(from), '']) {
    for (const n of names) {
      const p = resolveRelative(joinPath(dir, 'x'), n);
      const hit = p === null ? null : lookup(p);
      if (hit) return { kind: 'found', path: hit };
    }
  }
  for (const n of names) {
    const want = pathKey(n, true);
    const nested = want.includes('/');
    const hits = files.filter((f) => {
      const k = pathKey(f, true);
      return nested ? k === want || k.endsWith(`/${want}`) : pathKey(baseName(f), true) === want;
    });
    if (hits.length === 1) return { kind: 'found', path: hits[0]! };
    if (hits.length > 1) return { kind: 'ambiguous', name: target };
  }
  return { kind: 'missing', name: target };
}

/**
 * The heading a link's `#fragment` names, among the note's headings: the one whose text matches, ignoring
 * case, either as written (`[[note#Next steps]]`) or as a GitHub-style anchor (`note.md#next-steps`). Null when
 * none does.
 */
export function headingFor<T extends { readonly text: string }>(
  headings: readonly T[],
  fragment: string,
): T | null {
  const want = slug(fragment);
  if (!want) return null;
  return headings.find((h) => slug(h.text) === want) ?? null;
}

function slug(s: string): string {
  return s
    .normalize('NFC')
    .toLowerCase()
    .trim()
    .replace(/[^\p{L}\p{N}\s_-]/gu, '')
    .replace(/\s/g, '-');
}

function decodeSafe(s: string): string {
  try {
    return decodeURIComponent(s);
  } catch {
    return s;
  }
}

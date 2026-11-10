import { MarkdownModel } from '@puffinote/markdown';
import { TextDocument } from '@puffinote/text';
import {
  baseName,
  extOf,
  isImagePath,
  joinPath,
  parentOf,
  resolveRelative,
  stemOf,
} from '../platform/paths.ts';
import type { Settings } from './settings.ts';

/*
 * Pictures a note shows (PRD §10.6): where a pasted or dropped one is saved, the link written for it, and
 * keeping those links right when notes and pictures move.
 */

/** A link or image destination in a note that points at a file in the notebook. */
export interface FileRef {
  /** Offset of the destination in the note, `<…>` included when it has them. */
  readonly start: number;
  readonly length: number;
  /** The destination as written, without `<…>`. */
  readonly url: string;
  readonly angle: boolean;
}

/** `scheme:` or `//host`: not a notebook file. */
function isExternal(url: string): boolean {
  return /^[a-z][a-z0-9+.-]*:/i.test(url) || url.startsWith('//') || url.startsWith('#') || url === '';
}

/** Every `![](…)` and `[](…)` destination in `text` that names a notebook file (code is skipped). */
export function fileRefs(text: string): FileRef[] {
  const doc = TextDocument.fromString(text);
  const out: FileRef[] = [];
  for (const t of MarkdownModel.build(doc).allTokens()) {
    if (t.type !== 'imageUrl' && t.type !== 'linkUrl') continue;
    const url = t.meta ?? '';
    if (isExternal(url)) continue;
    out.push({ start: t.start, length: t.length, url, angle: text.charAt(t.start) === '<' });
  }
  return out;
}

/** The notebook paths of the pictures `text` (the note at `notePath`) links to. */
export function imageTargets(text: string, notePath: string): Set<string> {
  const out = new Set<string>();
  for (const ref of fileRefs(text)) {
    const path = resolveRelative(notePath, ref.url);
    if (path !== null && isImagePath(path)) out.add(path);
  }
  return out;
}

/** `to` written relative to the folder of the note at `from`, `/`-separated (`../attachments/a.png`). */
export function relativePath(from: string, to: string): string {
  const base = parentOf(from).split('/').filter(Boolean);
  const target = to.split('/');
  let i = 0;
  while (i < base.length && i < target.length - 1 && base[i] === target[i]) i++;
  return [...base.slice(i).map(() => '..'), ...target.slice(i)].join('/');
}

/** A link destination for `path`: spaces and brackets percent-encoded, so it needs no `<…>`. */
export function encodeRef(path: string): string {
  return path.replace(/[%\s()<>]/g, (c) => `%${c.charCodeAt(0).toString(16).toUpperCase().padStart(2, '0')}`);
}

/** A replacement of one range of a note's text. */
export interface RefEdit {
  readonly start: number;
  readonly length: number;
  readonly text: string;
}

/**
 * The edits that keep the picture links in a note right after a move. The note was at `oldNote` and is now
 * at `newNote` (the same path when it didn't move); `moved` maps a picture's old path to its new one, or
 * returns it unchanged. Links to other files, and links that already resolve, are left as written.
 */
export function relinkEdits(
  text: string,
  oldNote: string,
  newNote: string,
  moved: (path: string) => string,
): RefEdit[] {
  const edits: RefEdit[] = [];
  for (const ref of fileRefs(text)) {
    const [bare, suffix = ''] = splitSuffix(ref.url);
    const was = resolveRelative(oldNote, bare);
    if (was === null || !isImagePath(was)) continue;
    const now = moved(was);
    if (resolveRelative(newNote, bare) === now) continue;
    const rel = relativePath(newNote, now);
    const url = (ref.angle ? rel : encodeRef(rel)) + suffix;
    edits.push({ start: ref.start, length: ref.length, text: ref.angle ? `<${url}>` : url });
  }
  return edits;
}

/** Apply `edits` (in document order, not overlapping) to `text`. */
export function applyRefEdits(text: string, edits: readonly RefEdit[]): string {
  let out = '';
  let at = 0;
  for (const e of edits) {
    out += text.slice(at, e.start) + e.text;
    at = e.start + e.length;
  }
  return out + text.slice(at);
}

function splitSuffix(url: string): [string, string] {
  const i = url.search(/[?#]/);
  return i < 0 ? [url, ''] : [url.slice(0, i), url.slice(i)];
}

/** The folder a picture pasted into the note at `notePath` is saved in (PRD §10.3). */
export function attachmentDir(settings: Settings, notePath: string): string {
  if (settings.attachmentLocation === 'note')
    return joinPath(parentOf(notePath), `${stemOf(notePath)}.assets`);
  return cleanFolder(settings.attachmentFolder) || 'attachments';
}

/** A folder setting as a notebook path: no leading, trailing or doubled slashes, no `.` or `..`. */
export function cleanFolder(folder: string): string {
  return folder
    .split(/[/\\]/)
    .map((s) => s.trim())
    .filter((s) => s && s !== '.' && s !== '..')
    .join('/');
}

const mimeExts: Record<string, string> = {
  'image/png': '.png',
  'image/jpeg': '.jpg',
  'image/gif': '.gif',
  'image/webp': '.webp',
  'image/svg+xml': '.svg',
  'image/avif': '.avif',
  'image/bmp': '.bmp',
};

/** Whether a pasted or dropped file of this type is a picture Puffinote saves. */
export function isImageMime(mime: string): boolean {
  return mime in mimeExts;
}

/**
 * The file name for a pasted or dropped picture: a file keeps its own name, but a screenshot or a copied
 * picture (the clipboard calls them all "image.png") is named after the time, like
 * `Pasted image 2026-11-10 14.03.22.png`.
 */
export function attachmentName(name: string, mime: string, now: Date): string {
  const ext = mimeExts[mime] ?? (extOf(name) || '.png');
  if (name && isImagePath(name) && !/^image\.[a-z]+$/i.test(baseName(name))) return baseName(name);
  const p = (n: number) => String(n).padStart(2, '0');
  const stamp =
    `${now.getFullYear()}-${p(now.getMonth() + 1)}-${p(now.getDate())} ` +
    `${p(now.getHours())}.${p(now.getMinutes())}.${p(now.getSeconds())}`;
  return `Pasted image ${stamp}${ext}`;
}

/** The Markdown for a picture saved at `path`, shown in the note at `notePath`. */
export function imageMarkdown(notePath: string, path: string): string {
  return `![](${encodeRef(relativePath(notePath, path))})`;
}

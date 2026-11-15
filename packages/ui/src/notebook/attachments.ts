import { MarkdownModel } from '@puffinote/markdown';
import { TextDocument } from '@puffinote/text';
import {
  baseName,
  extOf,
  isImagePath,
  joinPath,
  parentOf,
  pathKey,
  resolveRelative,
  stemOf,
} from '../platform/paths.ts';
import type { Settings } from './settings.ts';
import { leavesNotebook } from './urls.ts';

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
  return leavesNotebook(url) || url.startsWith('#') || url === '';
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

/** Every `![[…]]` embed target in `text`, without its `|size` or `#part` (code is skipped). */
export function embedTargets(text: string): string[] {
  if (!text.includes('![[')) return [];
  const out: string[] = [];
  for (const t of MarkdownModel.build(TextDocument.fromString(text)).allTokens()) {
    if (t.type !== 'embed') continue;
    const target = (t.meta ?? '').split(/[|#]/)[0]!.trim();
    if (target) out.push(target);
  }
  return out;
}

/** The `src` of every `<img>` tag in `text` that names a notebook file. Code isn't skipped: it only keeps more. */
export function htmlImageSources(text: string): string[] {
  if (!/<img/i.test(text)) return [];
  const out: string[] = [];
  for (const m of text.matchAll(/<img\b[^>]*?\bsrc\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+))/gi)) {
    const url = (m[1] ?? m[2] ?? m[3] ?? '').trim();
    if (!isExternal(url)) out.push(url);
  }
  return out;
}

/**
 * The pictures a set of notes shows (PRD §10.6), to tell used pictures from unused ones: `![](…)` and
 * `[…](…)` links, `<img src="…">` tags, and `![[name.png]]` wiki embeds. An embed has no folder of its own:
 * it counts for the picture it names from the note's folder, from the top level, and for every picture whose
 * path ends with it (`![[a.png]]` keeps every `a.png`), so a picture is never wrongly called unused.
 */
export class PictureUsage {
  private readonly paths = new Set<string>();
  private readonly embeds = new Set<string>();
  /** Paths are compared by `pathKey`: NFC, and without case when `ignoreCase`. */
  private readonly key: (path: string) => string;

  constructor(ignoreCase = false) {
    this.key = (p) => pathKey(p, ignoreCase);
  }

  /** Count what the note at `notePath`, whose text is `text`, shows. */
  add(text: string, notePath: string): void {
    if (text.includes('](')) for (const p of imageTargets(text, notePath)) this.paths.add(this.key(p));
    for (const src of htmlImageSources(text)) {
      const p = resolveRelative(notePath, src);
      if (p !== null && isImagePath(p)) this.paths.add(this.key(p));
    }
    for (const target of embedTargets(text)) {
      if (!isImagePath(target)) continue;
      const here = resolveRelative(notePath, target);
      if (here !== null) this.paths.add(this.key(here));
      const bare = resolveRelative('', target);
      if (bare !== null) this.embeds.add(this.key(bare));
    }
  }

  /** Whether some note shows the picture at `path`. */
  has(picture: string): boolean {
    const path = this.key(picture);
    if (this.paths.has(path) || this.embeds.has(path)) return true;
    for (let i = path.indexOf('/'); i >= 0; i = path.indexOf('/', i + 1)) {
      if (this.embeds.has(path.slice(i + 1))) return true;
    }
    return false;
  }
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

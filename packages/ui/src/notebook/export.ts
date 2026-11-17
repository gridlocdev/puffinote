import { type HtmlOptions, imageSources, renderHtmlBody, renderHtmlDocument } from '@puffinote/markdown';
import { TextDocument } from '@puffinote/text';
import type { NotebookStore } from '../platform/notebookStore.ts';
import { extOf, isImagePath, resolveRelative, stemOf } from '../platform/paths.ts';
import { embedTargets } from './attachments.ts';
import { leavesNotebook } from './urls.ts';

/*
 * Export a note as one standalone HTML file (PRD §10.5): the export renderer's document, with the notebook
 * pictures it shows read through the store and embedded as `data:` URLs, so the file works on its own.
 */

/** A picture larger than this stays a link instead of being embedded. */
export const MAX_EMBED_BYTES = 10 * 1024 * 1024;
/** Pictures stop being embedded once this much has been embedded. */
export const MAX_EMBED_TOTAL = 50 * 1024 * 1024;

/** The raster types the renderer accepts as `data:` images (never SVG), by extension. */
const embeddable: Readonly<Record<string, string>> = {
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.gif': 'image/gif',
  '.webp': 'image/webp',
  '.avif': 'image/avif',
  '.bmp': 'image/bmp',
};

export interface NoteHtml {
  readonly html: string;
  /** The file name to offer: the note's name with `.html`. */
  readonly name: string;
  /** Pictures the note shows that stayed links: missing, unreadable, too large, or not a raster type. */
  readonly skipped: readonly string[];
}

/** A scheme (`https:`, `data:`) or `//host`: not a notebook file. */
function isExternal(src: string): boolean {
  return leavesNotebook(src) || src.startsWith('#');
}

/** Base64 of `bytes`, in chunks so a large picture doesn't overflow the argument list. */
export function toBase64(bytes: Uint8Array): string {
  let bin = '';
  for (let i = 0; i < bytes.length; i += 0x8000) {
    bin += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
  }
  return btoa(bin);
}

/**
 * The notebook path a picture reference in the note at `notePath` names: a link as written, or `[[name]]` for a
 * wiki embed (`NotebookPictures.pathOf`). Null when it names no file.
 */
export type PicturePath = (notePath: string, ref: string) => string | null;

/** Without the notebook's file list, a link resolves from the note's folder and a wiki embed finds nothing. */
const linksOnly: PicturePath = (notePath, ref) =>
  ref.startsWith('[[') ? null : resolveRelative(notePath, ref);

interface Embedded {
  /** The `HtmlOptions` that swap each embedded picture's link or wiki embed for its `data:` URL. */
  readonly options: HtmlOptions;
  readonly skipped: readonly string[];
}

/**
 * Read every notebook picture `doc` shows (links and wiki embeds) one at a time through `store`, as `data:`
 * URLs within the size limits. A picture that can't be embedded is listed in `skipped` and stays a link.
 */
async function embedPictures(
  store: NotebookStore,
  notePath: string,
  doc: TextDocument,
  pathOf: PicturePath,
): Promise<Embedded> {
  const images = new Map<string, string>();
  const embeds = new Map<string, string>();
  const skipped: string[] = [];
  let total = 0;
  const refs: { ref: string; shown: string; into: Map<string, string>; key: string }[] = [];
  for (const src of imageSources(doc)) {
    if (!isExternal(src)) refs.push({ ref: src, shown: src, into: images, key: src });
  }
  for (const target of new Set(embedTargets(doc.getText()))) {
    if (!isImagePath(target)) continue;
    refs.push({ ref: `[[${target}]]`, shown: target, into: embeds, key: target });
  }
  for (const { ref, shown, into, key } of refs) {
    const path = pathOf(notePath, ref);
    const mime = path === null ? undefined : embeddable[extOf(path).toLowerCase()];
    if (path === null || !mime) {
      skipped.push(shown);
      continue;
    }
    try {
      const { bytes } = await store.read(path);
      if (bytes.length > MAX_EMBED_BYTES || total + bytes.length > MAX_EMBED_TOTAL) {
        skipped.push(shown);
        continue;
      }
      total += bytes.length;
      into.set(key, `data:${mime};base64,${toBase64(bytes)}`);
    } catch {
      skipped.push(shown);
    }
  }
  return { options: { image: (src) => images.get(src), embed: (target) => embeds.get(target) }, skipped };
}

/**
 * The standalone HTML for the note at `notePath` whose text is `text` (the editor's, unsaved changes
 * included). Pictures are read one at a time through `store`.
 */
export async function noteHtml(
  store: NotebookStore,
  notePath: string,
  text: string,
  pathOf: PicturePath = linksOnly,
): Promise<NoteHtml> {
  const doc = TextDocument.fromString(text);
  const { options, skipped } = await embedPictures(store, notePath, doc, pathOf);
  const title = stemOf(notePath);
  return { html: renderHtmlDocument(doc, title, options), name: `${title}.html`, skipped };
}

/**
 * The formatted copy of `text`, a selection from the note at `notePath` (PRD §10.1): an HTML fragment with the
 * pictures it shows embedded as export embeds them, for apps that paste formatted text.
 */
export async function clipboardHtml(
  store: NotebookStore,
  notePath: string,
  text: string,
  pathOf: PicturePath = linksOnly,
): Promise<{ readonly html: string; readonly skipped: readonly string[] }> {
  const doc = TextDocument.fromString(text);
  const { options, skipped } = await embedPictures(store, notePath, doc, pathOf);
  return { html: renderHtmlBody(doc, options), skipped };
}

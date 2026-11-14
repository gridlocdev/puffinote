import { imageSources, renderHtmlDocument } from '@puffinote/markdown';
import { TextDocument } from '@puffinote/text';
import type { NotebookStore } from '../platform/notebookStore.ts';
import { extOf, resolveRelative, stemOf } from '../platform/paths.ts';

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
  return /^[a-z][a-z0-9+.-]*:/i.test(src) || src.startsWith('//') || src.startsWith('#');
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
 * The standalone HTML for the note at `notePath` whose text is `text` (the editor's, unsaved changes
 * included). Pictures are read one at a time through `store`.
 */
export async function noteHtml(store: NotebookStore, notePath: string, text: string): Promise<NoteHtml> {
  const doc = TextDocument.fromString(text);
  const embedded = new Map<string, string>();
  const skipped: string[] = [];
  let total = 0;
  for (const src of imageSources(doc)) {
    if (isExternal(src)) continue;
    const path = resolveRelative(notePath, src);
    const mime = path === null ? undefined : embeddable[extOf(path).toLowerCase()];
    if (path === null || !mime) {
      skipped.push(src);
      continue;
    }
    try {
      const { bytes } = await store.read(path);
      if (bytes.length > MAX_EMBED_BYTES || total + bytes.length > MAX_EMBED_TOTAL) {
        skipped.push(src);
        continue;
      }
      total += bytes.length;
      embedded.set(src, `data:${mime};base64,${toBase64(bytes)}`);
    } catch {
      skipped.push(src);
    }
  }
  const title = stemOf(notePath);
  const html = renderHtmlDocument(doc, title, { image: (src) => embedded.get(src) });
  return { html, name: `${title}.html`, skipped };
}

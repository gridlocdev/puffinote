import { signal, type Signal } from '@preact/signals';
import { readText } from '@puffinote/text';
import type { NotebookStore } from '../platform/notebookStore.ts';
import { baseName } from '../platform/paths.ts';
import { fileKind } from './tree.ts';

/** What the Library layout's note list shows under a file's name. */
export interface NoteMeta {
  /** A note's opening text, Markdown taken out. */
  readonly excerpt?: string;
  /** A picture's format and size in pixels; null when the header can't be read. */
  readonly image?: ImageInfo | null;
}

export interface ImageInfo {
  readonly format: string;
  readonly width: number;
  readonly height: number;
}

const EXCERPT_CHARS = 220;

/**
 * A note's text as a plain excerpt: front matter, a leading title heading, and Markdown marks taken out, white
 * space folded. The title heading goes because the card shows the name already.
 */
export function excerptOf(text: string): string {
  let t = text.replace(/^\uFEFF/, '');
  t = t.replace(/^---\r?\n[\s\S]*?\r?\n---\r?\n/, '');
  t = t.replace(/^\s*#\s+[^\n]*\n?/, '');
  t = t
    .replace(/^(```|~~~)[^\n]*$/gm, '')
    .replace(/<!--[\s\S]*?-->/g, '')
    .replace(/!\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/<[^>\n]+>/g, '')
    .replace(/^\s{0,3}#{1,6}\s+(.*?)\s*#*\s*$/gm, '$1 —')
    .replace(/^\s*>\s?/gm, '')
    .replace(/^\s*(?:[-*+]|\d+[.)])\s+(?:\[[ xX]\]\s+)?/gm, '')
    .replace(/^\s*(?:[-*_]\s*){3,}$/gm, '')
    .replace(/(\*\*|__|~~|==)(?=\S)([\s\S]*?\S)\1/g, '$2')
    .replace(/(^|[^\w*])[*_](?=\S)([^*_\n]*?\S)[*_](?!\w)/g, '$1$2')
    .replace(/`([^`\n]*)`/g, '$1')
    .replace(/\\([\\`*_{}[\]()#+\-.!>~=|])/g, '$1');
  t = t.replace(/\s+/g, ' ').trim().replace(/\s*—$/, '');
  return t.length > EXCERPT_CHARS ? `${t.slice(0, EXCERPT_CHARS).trimEnd()}…` : t;
}

/** A picture's format from its name, for a size the page measured (`NoteMetaCache`): `photo.jpg` → JPEG. */
export function imageFormatOf(name: string): string {
  const ext = name.slice(name.lastIndexOf('.') + 1).toLowerCase();
  const known: Record<string, string> = {
    png: 'PNG',
    jpg: 'JPEG',
    jpeg: 'JPEG',
    gif: 'GIF',
    webp: 'WebP',
    avif: 'AVIF',
    bmp: 'BMP',
    svg: 'SVG',
  };
  return known[ext] ?? ext.toUpperCase();
}

/** A picture's format and size from its first bytes: PNG, JPEG, GIF, WebP, or SVG's own attributes. */
export function imageInfo(bytes: Uint8Array, name: string): ImageInfo | null {
  const ext = name.slice(name.lastIndexOf('.') + 1).toLowerCase();
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const at = (i: number, sig: readonly number[]) => sig.every((b, k) => bytes[i + k] === b);
  try {
    if (at(0, [0x89, 0x50, 0x4e, 0x47]) && bytes.length >= 24) {
      return { format: 'PNG', width: view.getUint32(16), height: view.getUint32(20) };
    }
    if (at(0, [0x47, 0x49, 0x46, 0x38]) && bytes.length >= 10) {
      return { format: 'GIF', width: view.getUint16(6, true), height: view.getUint16(8, true) };
    }
    if (at(0, [0xff, 0xd8])) return jpegInfo(bytes, view);
    if (at(0, [0x52, 0x49, 0x46, 0x46]) && at(8, [0x57, 0x45, 0x42, 0x50])) return webpInfo(bytes, view);
    if (ext === 'svg') return svgInfo(readText(bytes).text);
  } catch {
    // A file shorter than its header says: no size.
  }
  return null;
}

function jpegInfo(bytes: Uint8Array, view: DataView): ImageInfo | null {
  let i = 2;
  while (i + 9 < bytes.length) {
    if (bytes[i] !== 0xff) return null;
    const marker = bytes[i + 1]!;
    // SOF0–SOF15 hold the size, apart from DHT (C4), JPG (C8) and DAC (CC).
    if (marker >= 0xc0 && marker <= 0xcf && marker !== 0xc4 && marker !== 0xc8 && marker !== 0xcc) {
      return { format: 'JPEG', width: view.getUint16(i + 7), height: view.getUint16(i + 5) };
    }
    i += 2 + view.getUint16(i + 2);
  }
  return null;
}

function webpInfo(bytes: Uint8Array, view: DataView): ImageInfo | null {
  const chunk = String.fromCharCode(...bytes.subarray(12, 16));
  if (chunk === 'VP8 ' && bytes.length >= 30) {
    return {
      format: 'WebP',
      width: view.getUint16(26, true) & 0x3fff,
      height: view.getUint16(28, true) & 0x3fff,
    };
  }
  if (chunk === 'VP8L' && bytes.length >= 25) {
    const b = view.getUint32(21, true);
    return { format: 'WebP', width: (b & 0x3fff) + 1, height: ((b >> 14) & 0x3fff) + 1 };
  }
  if (chunk === 'VP8X' && bytes.length >= 30) {
    const w = 1 + (bytes[24]! | (bytes[25]! << 8) | (bytes[26]! << 16));
    const h = 1 + (bytes[27]! | (bytes[28]! << 8) | (bytes[29]! << 16));
    return { format: 'WebP', width: w, height: h };
  }
  return null;
}

function svgInfo(text: string): ImageInfo | null {
  const tag = /<svg\b[^>]*>/i.exec(text)?.[0];
  if (!tag) return null;
  const attr = (n: string) => new RegExp(`\\s${n}\\s*=\\s*["']([^"']*)["']`, 'i').exec(tag)?.[1];
  const px = (v: string | undefined) =>
    v && /^\s*[\d.]+\s*(px)?\s*$/.test(v) ? Math.round(parseFloat(v)) : 0;
  let width = px(attr('width'));
  let height = px(attr('height'));
  if (!width || !height) {
    const box = attr('viewBox')
      ?.trim()
      .split(/[\s,]+/)
      .map(Number);
    if (box?.length === 4 && box.every(Number.isFinite)) {
      width ||= Math.round(box[2]!);
      height ||= Math.round(box[3]!);
    }
  }
  return width && height ? { format: 'SVG', width, height } : null;
}

const DAY = 24 * 60 * 60 * 1000;
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const DAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

/** A short date for a list: "Now", "5m", "2h", "Yesterday", "Mon", "Oct 28", "Oct 28, 2024". */
export function relativeDate(ms: number, now = Date.now()): string {
  if (!ms) return '';
  const diff = now - ms;
  if (diff < 60_000) return 'Now';
  if (diff < 60 * 60_000) return `${Math.floor(diff / 60_000)}m`;
  const d = new Date(ms);
  const today = new Date(now);
  const midnight = new Date(today.getFullYear(), today.getMonth(), today.getDate()).getTime();
  if (ms >= midnight) return `${Math.floor(diff / (60 * 60_000))}h`;
  if (ms >= midnight - DAY) return 'Yesterday';
  if (ms >= midnight - 6 * DAY) return DAYS[d.getDay()]!;
  const md = `${MONTHS[d.getMonth()]} ${d.getDate()}`;
  return d.getFullYear() === today.getFullYear() ? md : `${md}, ${d.getFullYear()}`;
}

/**
 * Reads what the note list shows for each file, once per version of it, a few at a time. Each file's answer
 * is a signal of its own, so a card redraws when its own file's arrives, not on every file's (with thousands
 * of notes, a shared signal redrew every card once per note).
 */
export class NoteMetaCache {
  private readonly store: NotebookStore;
  private readonly cache = new Map<string, { modified: number; meta: NoteMeta }>();
  private readonly slots = new Map<string, Signal<NoteMeta | null>>();
  private readonly queue: { path: string; modified: number }[] = [];
  private readonly queued = new Set<string>();
  private running = 0;
  private disposed = false;
  private readonly report: (e: unknown) => void;

  constructor(store: NotebookStore, report: (e: unknown) => void) {
    this.store = store;
    this.report = report;
  }

  /**
   * What's known about `path` at `modified` (or an older version, until that arrives); asks for it when it
   * isn't known yet. A component or effect that calls this redraws when `path`'s answer arrives.
   */
  get(path: string, modified: number): NoteMeta | null {
    const hit = this.cache.get(path);
    if (!hit || hit.modified !== modified) this.request(path, modified);
    return this.slot(path).value;
  }

  private slot(path: string): Signal<NoteMeta | null> {
    let s = this.slots.get(path);
    if (!s) {
      s = signal(this.cache.get(path)?.meta ?? null);
      this.slots.set(path, s);
    }
    return s;
  }

  dispose(): void {
    this.disposed = true;
    this.queue.length = 0;
  }

  private request(path: string, modified: number): void {
    const key = `${path}\n${modified}`;
    if (this.queued.has(key)) return;
    this.queued.add(key);
    this.queue.push({ path, modified });
    this.pump();
  }

  private pump(): void {
    while (this.running < 4 && this.queue.length > 0 && !this.disposed) {
      const job = this.queue.shift()!;
      this.running++;
      this.run(job).catch(this.report);
    }
  }

  private async run(job: { readonly path: string; readonly modified: number }): Promise<void> {
    try {
      const meta = await this.load(job.path);
      if (meta && !this.disposed) {
        this.cache.set(job.path, { modified: job.modified, meta });
        this.slot(job.path).value = meta;
      }
    } finally {
      // Even when the slot's update throws, so the queue never stalls with four jobs that won't finish.
      this.running--;
      this.queued.delete(`${job.path}\n${job.modified}`);
      this.pump();
    }
  }

  private async load(path: string): Promise<NoteMeta | null> {
    try {
      const kind = fileKind(path);
      if (kind === 'other') return {};
      // A picture the page loads from a URL (the NAS, the desktop) is downloaded for its thumbnail anyway:
      // the card takes its size from that (`imageFormatOf`), rather than downloading it a second time here.
      // An SVG is read, since its size is in its attributes, which the page's image doesn't always report.
      if (kind === 'image' && !/\.svg$/i.test(path) && this.store.fileUrl(path) !== null) return {};
      const { bytes } = await this.store.read(path);
      if (kind === 'image') return { image: imageInfo(bytes, baseName(path)) };
      return { excerpt: excerptOf(readText(bytes).text) };
    } catch {
      return null;
    }
  }
}

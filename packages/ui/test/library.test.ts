import { effect } from '@preact/signals';
import { describe, expect, it, vi } from 'vitest';
import { scopeList, sortNotes } from '../src/notebook/library.ts';
import { excerptOf, imageInfo, NoteMetaCache, relativeDate } from '../src/notebook/noteMeta.ts';
import { resolveSettings, sanitize } from '../src/notebook/settings.ts';
import { Notebook, sessionPath } from '../src/notebook/notebook.ts';
import { memoryPrefs } from '../src/notebook/settings.ts';
import { MemoryStore } from '../src/platform/memoryStore.ts';
import type { FileStat } from '../src/notebook/tree.ts';

describe('layout and accent settings', () => {
  it('keeps known values and drops others', () => {
    expect(sanitize({ layout: 'library', accent: 'teal' })).toEqual({ layout: 'library', accent: 'teal' });
    expect(sanitize({ layout: 'grid', accent: 'red' })).toEqual({});
  });

  it('opens tree rows with a single click unless set to a double click', () => {
    expect(resolveSettings().treeClick).toBe('single');
    expect(resolveSettings({ treeClick: 'double' }).treeClick).toBe('double');
    expect(sanitize({ treeClick: 'triple' })).toEqual({});
  });
});

describe('settings from a newer Puffinote', () => {
  it('ignores what it does not understand, and keeps it when it rewrites the notebook settings', async () => {
    const store = new MemoryStore('N', {
      '.puffinote/settings.json': JSON.stringify({ accent: 'green', future: { on: true }, theme: 'dark' }),
      'a.md': 'x',
    });
    const nb = await Notebook.open(store, { prefs: memoryPrefs() });
    expect(nb.settings.value.accent).toBe('blue');
    expect(nb.settings.value.theme).toBe('dark');
    await nb.setOverrides({ theme: 'light' });
    const saved = JSON.parse(new TextDecoder().decode((await store.read('.puffinote/settings.json')).bytes));
    expect(saved).toEqual({ accent: 'green', future: { on: true }, theme: 'light' });
  });
});

describe('excerptOf', () => {
  it('drops front matter, the title heading and Markdown marks', () => {
    const md =
      '---\ntags: x\n---\n# Title\n\nSome **bold** and _it_ with [a link](x.md) and `code`.\n\n## Next\n- [x] done\n> quoted';
    expect(excerptOf(md)).toBe('Some bold and it with a link and code. Next — done quoted');
  });

  it('caps long text', () => {
    const t = excerptOf('word '.repeat(100));
    expect(t.length).toBeLessThanOrEqual(221);
    expect(t.endsWith('…')).toBe(true);
  });
});

describe('imageInfo', () => {
  const bytes = (...parts: (number[] | string)[]) =>
    new Uint8Array(parts.flatMap((p) => (typeof p === 'string' ? [...p].map((c) => c.charCodeAt(0)) : p)));
  const be32 = (n: number) => [(n >>> 24) & 255, (n >>> 16) & 255, (n >>> 8) & 255, n & 255];
  const le16 = (n: number) => [n & 255, n >> 8];

  it('reads PNG, GIF, JPEG, WebP and SVG sizes', () => {
    const png = bytes([0x89], 'PNG\r\n\x1a\n', be32(13), 'IHDR', be32(160), be32(90));
    expect(imageInfo(png, 'a.png')).toEqual({ format: 'PNG', width: 160, height: 90 });
    expect(imageInfo(bytes('GIF89a', le16(32), le16(16)), 'a.gif')).toEqual({
      format: 'GIF',
      width: 32,
      height: 16,
    });
    const jpeg = bytes([0xff, 0xd8, 0xff, 0xe0, 0, 4, 0, 0, 0xff, 0xc0, 0, 17, 8, 0, 48, 0, 64, 3]);
    expect(imageInfo(jpeg, 'a.jpg')).toEqual({ format: 'JPEG', width: 64, height: 48 });
    const vp8l = bytes('RIFF', [0, 0, 0, 0], 'WEBPVP8L', [0, 0, 0, 0, 0x2f], [99, 0xc0, 0x0e, 0]);
    expect(imageInfo(vp8l, 'a.webp')).toEqual({ format: 'WebP', width: 100, height: 60 });
    const svg = new TextEncoder().encode('<svg xmlns="x" viewBox="0 0 24 12"></svg>');
    expect(imageInfo(svg, 'a.svg')).toEqual({ format: 'SVG', width: 24, height: 12 });
    expect(imageInfo(bytes('nope'), 'a.png')).toBeNull();
  });
});

describe('relativeDate', () => {
  const now = new Date(2026, 10, 9, 15, 0).getTime();
  it('shortens by distance', () => {
    expect(relativeDate(now - 10_000, now)).toBe('Now');
    expect(relativeDate(now - 5 * 60_000, now)).toBe('5m');
    expect(relativeDate(now - 2 * 3600_000, now)).toBe('2h');
    expect(relativeDate(new Date(2026, 10, 8, 9).getTime(), now)).toBe('Yesterday');
    expect(relativeDate(new Date(2026, 10, 5, 9).getTime(), now)).toBe('Thu');
    expect(relativeDate(new Date(2026, 9, 28).getTime(), now)).toBe('Oct 28');
    expect(relativeDate(new Date(2024, 9, 28).getTime(), now)).toBe('Oct 28, 2024');
  });
});

describe('Library note list', () => {
  async function open(files: Record<string, string>) {
    const store = new MemoryStore('Notes', files);
    const nb = await Notebook.open(store, { prefs: memoryPrefs({ autoSave: false }) });
    return { store, nb };
  }
  const stats = (m: Record<string, [number, number]>) =>
    new Map<string, FileStat>(
      Object.entries(m).map(([p, [modified, created]]) => [p, { size: 0, modified, created }]),
    );

  it('sorts by edit, creation or name', async () => {
    const { nb } = await open({ 'note2.md': '', 'note10.md': '', 'a.md': '' });
    const files = nb.tree.value.children;
    const s = stats({ 'note2.md': [3, 1], 'note10.md': [2, 3], 'a.md': [1, 2] });
    expect(sortNotes(files, 'edited', s).map((f) => f.name)).toEqual(['note2.md', 'note10.md', 'a.md']);
    expect(sortNotes(files, 'created', s).map((f) => f.name)).toEqual(['note10.md', 'a.md', 'note2.md']);
    expect(sortNotes(files, 'title', s).map((f) => f.name)).toEqual(['a.md', 'note2.md', 'note10.md']);
  });

  it('lists a scope with pinned notes first', async () => {
    const { nb } = await open({ 'a.md': '', 'b.md': '', 'f/c.md': '', 'f/g/d.md': '' });
    const inputs = { pinned: ['b.md'], recent: ['f/c.md', 'a.md'], stats: new Map(), sort: 'title' as const };
    const root = nb.tree.value;
    expect(scopeList(root, 'folder:', inputs)).toMatchObject({ folders: 1 });
    expect(scopeList(root, 'folder:', inputs).files.map((f) => f.path)).toEqual(['b.md', 'a.md']);
    expect(scopeList(root, 'folder:f', inputs).files.map((f) => f.path)).toEqual(['f/c.md']);
    expect(scopeList(root, 'all', inputs).files.map((f) => f.path)).toEqual([
      'b.md',
      'a.md',
      'f/c.md',
      'f/g/d.md',
    ]);
    expect(scopeList(root, 'pinned', inputs).files.map((f) => f.path)).toEqual(['b.md']);
    expect(scopeList(root, 'recent', inputs).files.map((f) => f.path)).toEqual(['f/c.md', 'a.md']);
  });

  it('carries pins and the folder along on a move, drops them on delete, and remembers them', async () => {
    const { store, nb } = await open({ 'f/a.md': '', 'g/x.md': '', 'b.md': '' });
    nb.togglePin('f/a.md');
    nb.togglePin('b.md');
    nb.scope.value = 'folder:f';
    nb.sort.value = 'title';
    await nb.move('f', 'g');
    expect(nb.pinned.value).toEqual(['g/f/a.md', 'b.md']);
    expect(nb.scope.value).toBe('folder:g/f');
    await nb.remove('g/f');
    expect(nb.pinned.value).toEqual(['b.md']);
    expect(nb.scope.value).toBe('folder:g');
    await nb.persist();
    expect(JSON.parse(new TextDecoder().decode((await store.read(sessionPath)).bytes)).pinned).toEqual([
      'b.md',
    ]);
    const again = await Notebook.open(store, { prefs: memoryPrefs() });
    expect(again.pinned.value).toEqual(['b.md']);
    expect(again.scope.value).toBe('folder:g');
    expect(again.sort.value).toBe('title');
  });
});

describe('NoteMetaCache', () => {
  /** A MemoryStore that counts reads and, optionally, serves files from a URL as the NAS and desktop do. */
  class UrlStore extends MemoryStore {
    readonly readPaths: string[] = [];
    urls = false;

    override read(path: string) {
      this.readPaths.push(path);
      return super.read(path);
    }

    override fileUrl(path: string): string | null {
      return this.urls ? `/files/${path}` : null;
    }
  }

  it('wakes only the reader of the note whose excerpt arrived', async () => {
    const files: Record<string, string> = {};
    for (let i = 0; i < 20; i++) files[`n${i}.md`] = `note ${i}`;
    const cache = new NoteMetaCache(new UrlStore('N', files));
    let runs = 0;
    let seen: string | undefined;
    const stop = effect(() => {
      runs++;
      seen = cache.get('n0.md', 1)?.excerpt;
    });
    for (let i = 1; i < 20; i++) cache.get(`n${i}.md`, 1);
    await vi.waitFor(() => expect(seen).toBe('note 0'));
    await new Promise((r) => setTimeout(r, 20));
    expect(runs).toBe(2);
    stop();
    cache.dispose();
  });

  it('reads a picture for its size only when the page can’t load it from a URL', async () => {
    const png = new Uint8Array([
      0x89, 0x50, 0x4e, 0x47, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82, 0, 0, 0, 4,
    ]);
    const store = new UrlStore('N', { 'a.png': png, 'b.svg': '<svg width="4" height="2"></svg>' });
    store.urls = true;
    const cache = new NoteMetaCache(store);
    cache.get('a.png', 1);
    await vi.waitFor(() =>
      expect(cache.get('b.svg', 1)?.image).toEqual({ format: 'SVG', width: 4, height: 2 }),
    );
    expect(store.readPaths).toEqual(['b.svg']);
    expect(cache.get('a.png', 1)).toEqual({});
    cache.dispose();
  });
});

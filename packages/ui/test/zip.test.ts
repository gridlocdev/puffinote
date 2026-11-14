import { describe, expect, it, vi } from 'vitest';
import { archiveName, leftOutOfArchive, notebookArchive } from '../src/notebook/archive.ts';
import { Notebook } from '../src/notebook/notebook.ts';
import { memoryPrefs } from '../src/notebook/settings.ts';
import { MemoryStore } from '../src/platform/memoryStore.ts';
import { crc32, zip } from '../src/platform/zip.ts';

/** Read a stored (uncompressed) zip back through its central directory, checking each entry's CRC. */
function unzip(bytes: Uint8Array): Map<string, string | null> {
  const v = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const end = bytes.length - 22;
  expect(v.getUint32(end, true)).toBe(0x06054b50);
  const count = v.getUint16(end + 10, true);
  let at = v.getUint32(end + 16, true);
  expect(at + v.getUint32(end + 12, true)).toBe(end);
  const out = new Map<string, string | null>();
  const decoder = new TextDecoder();
  for (let i = 0; i < count; i++) {
    expect(v.getUint32(at, true)).toBe(0x02014b50);
    expect(v.getUint16(at + 8, true) & 0x0800).toBe(0x0800);
    const crc = v.getUint32(at + 16, true);
    const size = v.getUint32(at + 20, true);
    const nameLen = v.getUint16(at + 28, true);
    const local = v.getUint32(at + 42, true);
    const name = decoder.decode(bytes.subarray(at + 46, at + 46 + nameLen));
    expect(v.getUint32(local, true)).toBe(0x04034b50);
    expect(v.getUint16(local + 8, true)).toBe(0);
    const localNameLen = v.getUint16(local + 26, true);
    const start = local + 30 + localNameLen + v.getUint16(local + 28, true);
    const data = bytes.subarray(start, start + size);
    expect(crc32(data)).toBe(crc);
    out.set(name, name.endsWith('/') ? null : decoder.decode(data));
    at += 46 + nameLen + v.getUint16(at + 30, true) + v.getUint16(at + 32, true);
  }
  return out;
}

describe('zip', () => {
  it('computes the standard CRC-32', () => {
    expect(crc32(new TextEncoder().encode('123456789'))).toBe(0xcbf43926);
    expect(crc32(new Uint8Array())).toBe(0);
  });

  it('stores files and folders with UTF-8 names', () => {
    const text = new TextEncoder();
    const bytes = zip([
      { path: 'Notes' },
      { path: 'Notes/Café ☕.md', bytes: text.encode('# Hi\n'), modified: Date.UTC(2026, 10, 14) },
      { path: 'Notes/empty.txt', bytes: new Uint8Array() },
    ]);
    expect([...unzip(bytes)]).toEqual([
      ['Notes/', null],
      ['Notes/Café ☕.md', '# Hi\n'],
      ['Notes/empty.txt', ''],
    ]);
  });

  it('writes an empty archive', () => {
    expect(zip([]).length).toBe(22);
  });
});

describe('notebookArchive', () => {
  it('holds every file and folder under a folder named after the notebook', async () => {
    const store = new MemoryStore('My Notes', {
      'a.md': 'A',
      'sub/b.md': 'B',
      'attachments/p.png': new Uint8Array([137, 80, 78, 71]),
      '.puffinote/settings.json': '{}',
      '.puffinote/session.json': '{"tabs":[]}',
      '.git/HEAD': 'ref',
    });
    await store.mkdir('empty');
    const files = unzip(await notebookArchive(store));
    expect([...files.keys()].sort()).toEqual([
      'My Notes/',
      'My Notes/.puffinote/',
      'My Notes/.puffinote/settings.json',
      'My Notes/a.md',
      'My Notes/attachments/',
      'My Notes/attachments/p.png',
      'My Notes/empty/',
      'My Notes/sub/',
      'My Notes/sub/b.md',
    ]);
    expect(files.get('My Notes/sub/b.md')).toBe('B');
  });

  it('leaves out git and the window state only', () => {
    expect(leftOutOfArchive('.puffinote/session.json')).toBe(true);
    expect(leftOutOfArchive('.git/config')).toBe(true);
    expect(leftOutOfArchive('sub/.git')).toBe(true);
    expect(leftOutOfArchive('.puffinote/settings.json')).toBe(false);
    expect(leftOutOfArchive('sub/session.json')).toBe(false);
  });

  it('names the archive safely', () => {
    expect(archiveName('Browser notebook')).toBe('Browser notebook');
    expect(archiveName('a/b:c')).toBe('a-b-c');
    expect(archiveName('..')).toBe('Notebook');
  });
});

describe('Notebook.downloadArchive', () => {
  it('saves unsaved notes first and downloads the zip', async () => {
    const store = new MemoryStore('Notes', { 'a.md': 'old' });
    const download = vi.fn();
    const nb = await Notebook.open(store, { prefs: memoryPrefs(), download });
    const tab = (await nb.openFile('a.md'))!;
    tab.state!.insert('new ');
    expect(await nb.downloadArchive()).toBe(true);
    expect(download).toHaveBeenCalledOnce();
    const [name, bytes, mime] = download.mock.calls[0]! as [string, Uint8Array, string];
    expect(name).toBe('Notes.zip');
    expect(mime).toBe('application/zip');
    expect(unzip(bytes).get('Notes/a.md')).toBe('new old');
    nb.dispose();
  });

  it('is off without a way to download', async () => {
    const nb = await Notebook.open(new MemoryStore(), { prefs: memoryPrefs() });
    expect(nb.canDownload).toBe(false);
    expect(await nb.downloadArchive()).toBe(false);
    nb.dispose();
  });
});

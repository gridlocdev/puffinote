import { describe, expect, it } from 'vitest';
import { importName, isImportJunk, type ImportFile } from '../src/notebook/importFiles.ts';
import { Notebook } from '../src/notebook/notebook.ts';
import { memoryPrefs } from '../src/notebook/settings.ts';
import { ConflictError } from '../src/platform/notebookStore.ts';
import { MemoryStore } from '../src/platform/memoryStore.ts';
import { ShellModel } from '../src/shell/shellModel.ts';

const text = (s: string) => new TextEncoder().encode(s);
const file = (path: string, body = path): ImportFile => ({ path, read: async () => text(body) });

async function read(store: MemoryStore, path: string): Promise<string> {
  return new TextDecoder().decode((await store.read(path)).bytes);
}

async function open(files: Record<string, string> = {}) {
  const store = new MemoryStore('Notes', files);
  const nb = await Notebook.open(store, { prefs: memoryPrefs() });
  return { store, nb };
}

describe('import rules', () => {
  it('skips OS clutter and git', () => {
    expect(isImportJunk('Trip/.DS_Store')).toBe(true);
    expect(isImportJunk('Trip/._a.md')).toBe(true);
    expect(isImportJunk('Trip/Thumbs.db')).toBe(true);
    expect(isImportJunk('Trip/.git/config')).toBe(true);
    expect(isImportJunk('__MACOSX/a.md')).toBe(true);
    expect(isImportJunk('Trip/.hidden.md')).toBe(false);
    expect(isImportJunk('Trip/a.md')).toBe(false);
  });

  it('turns names the notebook refuses into usable ones', () => {
    expect(importName('Meeting: notes?.md')).toBe('Meeting- notes-.md');
    expect(importName(' padded ')).toBe('padded');
    expect(importName('..')).toBeNull();
    expect(importName('  ')).toBeNull();
  });
});

describe('Notebook.importFiles', () => {
  it('copies a folder under its own name, skipping clutter', async () => {
    const { store, nb } = await open();
    const r = await nb.importFiles('', [
      file('Trip/a.md'),
      file('Trip/photos/p.png'),
      file('Trip/.DS_Store'),
      file('Trip/photos/._p.png'),
    ]);
    expect(r).toEqual({ imported: 2, failed: 0, roots: ['Trip'] });
    expect(await read(store, 'Trip/a.md')).toBe('Trip/a.md');
    expect(await read(store, 'Trip/photos/p.png')).toBe('Trip/photos/p.png');
    await expect(store.read('Trip/.DS_Store')).rejects.toThrow();
    expect(nb.selected.value).toBe('Trip');
    expect(nb.notice.value).toEqual({ kind: 'info', text: 'Imported 2 files into “Trip”.' });
    nb.dispose();
  });

  it('never overwrites: a taken folder or file name gets a number, compared without case', async () => {
    const { store, nb } = await open({ 'trip/a.md': 'mine', 'b.md': 'mine', 'b 2.md': 'mine too' });
    const r = await nb.importFiles('', [file('Trip/a.md', 'new'), file('B.md', 'new')]);
    expect(r.imported).toBe(2);
    expect(r.roots).toEqual(['Trip 2', 'B 3.md']);
    expect(await read(store, 'trip/a.md')).toBe('mine');
    expect(await read(store, 'Trip 2/a.md')).toBe('new');
    expect(await read(store, 'b.md')).toBe('mine');
    expect(await read(store, 'B 3.md')).toBe('new');
    expect(nb.notice.value?.text).toBe('Imported 2 files.');
    nb.dispose();
  });

  it('copies into the given folder and opens it', async () => {
    const { store, nb } = await open({ 'Work/x.md': 'x' });
    await nb.importFiles('Work', [file('x.md', 'other'), file('y.md')]);
    expect(await read(store, 'Work/x.md')).toBe('x');
    expect(await read(store, 'Work/x 2.md')).toBe('other');
    expect(await read(store, 'Work/y.md')).toBe('y.md');
    expect(nb.expanded.value.has('Work')).toBe(true);
    expect(nb.notice.value?.text).toBe('Imported 2 files into “Work”.');
    nb.dispose();
  });

  it('picks the next name when a write finds the name taken after all', async () => {
    const { store, nb } = await open();
    const write = store.write.bind(store);
    let refused = false;
    store.write = async (path, bytes, opts) => {
      if (path === 'a.md' && !refused) {
        refused = true;
        throw new ConflictError('exists', 'taken');
      }
      return write(path, bytes, opts);
    };
    const r = await nb.importFiles('', [file('a.md')]);
    expect(r.roots).toEqual(['a 2.md']);
    nb.dispose();
  });

  it('counts files it could not read and goes on', async () => {
    const { store, nb } = await open();
    const r = await nb.importFiles('', [
      { path: 'Trip/bad.md', read: () => Promise.reject(new Error('gone')) },
      file('Trip/good.md'),
    ]);
    expect(r).toEqual({ imported: 1, failed: 1, roots: ['Trip'] });
    expect(await read(store, 'Trip/good.md')).toBe('Trip/good.md');
    expect(nb.notice.value).toEqual({
      kind: 'error',
      text: 'Imported 1 file into “Trip”. 1 file couldn’t be copied.',
    });
    nb.dispose();
  });

  it('says so when there was nothing to import', async () => {
    const { nb } = await open();
    expect((await nb.importFiles('', [file('.DS_Store')])).imported).toBe(0);
    expect(nb.notice.value?.text).toBe('There was nothing to import.');
    nb.dispose();
  });
});

describe('Import Notes…', () => {
  it('is offered only where the host can pick files', () => {
    expect(new ShellModel().menuCatalog.get('shell.importNotes')).toBeUndefined();
    const m = new ShellModel({ pickFiles: async () => null });
    expect(m.menuCatalog.get('shell.importNotes')).toBeDefined();
  });

  it('asks folder or files, then imports into the selected folder', async () => {
    const asked: boolean[] = [];
    const m = new ShellModel({
      pickFiles: async (folder) => {
        asked.push(folder);
        return [file('Trip/a.md')];
      },
    });
    const { store, nb } = await open({ 'Work/x.md': 'x' });
    m.notebook.value = nb;
    nb.selected.value = 'Work/x.md';
    const run = m.importNotes();
    const o = m.overlay.value;
    expect(o?.kind).toBe('confirm');
    if (o?.kind === 'confirm') o.done('folder');
    await run;
    expect(asked).toEqual([true]);
    expect(await read(store, 'Work/Trip/a.md')).toBe('Trip/a.md');
    nb.dispose();
  });

  it('does nothing when the dialog is cancelled', async () => {
    let picked = false;
    const m = new ShellModel({ pickFiles: async () => ((picked = true), null) });
    const { nb } = await open();
    m.notebook.value = nb;
    const run = m.importNotes();
    const o = m.overlay.value;
    if (o?.kind === 'confirm') o.done('cancel');
    await run;
    expect(picked).toBe(false);
    nb.dispose();
  });
});

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryStore } from '../src/platform/memoryStore.ts';
import { Notebook } from '../src/notebook/notebook.ts';
import { memoryPrefs } from '../src/notebook/settings.ts';
import {
  MAX_RECOVERY_CHARS,
  memoryRecovery,
  webRecovery,
  type RecoveryEntry,
} from '../src/notebook/recovery.ts';

const text = async (store: MemoryStore, path: string) =>
  new TextDecoder().decode((await store.read(path)).bytes);

async function open(
  files: Record<string, string>,
  initial: Record<string, RecoveryEntry> = {},
  autoSave = false,
) {
  const store = new MemoryStore('Notes', files);
  const recovery = memoryRecovery(initial);
  const nb = await Notebook.open(store, { prefs: memoryPrefs({ autoSave }), recovery });
  return { store, nb, recovery };
}

describe('recovery copies of unsaved edits (PRD §5)', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('keeps a copy a second after the last edit, and drops it on save', async () => {
    const { store, nb, recovery } = await open({ 'a.md': 'hello' });
    const tab = (await nb.openFile('a.md'))!;
    tab.state!.select(5);
    tab.state!.insert('!');
    vi.advanceTimersByTime(900);
    expect(recovery.entries.has('a.md')).toBe(false);
    vi.advanceTimersByTime(200);
    expect(recovery.get('a.md')?.text).toBe('hello!');
    expect(await text(store, 'a.md')).toBe('hello'); // the file is untouched
    await nb.save(tab);
    expect(recovery.entries.has('a.md')).toBe(false);
  });

  it('takes a copy at least every few seconds while typing goes on', async () => {
    const { nb, recovery } = await open({ 'a.md': '' });
    const tab = (await nb.openFile('a.md'))!;
    for (let i = 0; i < 12; i++) {
      tab.state!.insert('x');
      vi.advanceTimersByTime(500);
    }
    expect(recovery.get('a.md')?.text.length).toBeGreaterThan(0);
  });

  it('drops the copy on Revert to Saved, Don’t Save and closing the tab', async () => {
    const { nb, recovery } = await open({ 'a.md': 'a', 'b.md': 'b', 'c.md': 'c' });
    const tabs = [];
    for (const p of ['a.md', 'b.md', 'c.md']) {
      const t = (await nb.openFile(p))!;
      t.state!.insert('!');
      tabs.push(t);
    }
    vi.advanceTimersByTime(1100);
    expect(recovery.paths().sort()).toEqual(['a.md', 'b.md', 'c.md']);
    await nb.reload(tabs[0]!);
    expect(recovery.entries.has('a.md')).toBe(false);
    nb.closeTab(tabs[1]!);
    expect(recovery.entries.has('b.md')).toBe(false);
    nb.forgetRecovery([tabs[2]!]);
    tabs[2]!.state!.insert('?');
    vi.advanceTimersByTime(1100);
    expect(recovery.entries.has('c.md')).toBe(false);
  });

  it('follows a renamed note', async () => {
    const { nb, recovery } = await open({ 'a.md': 'a' });
    const tab = (await nb.openFile('a.md'))!;
    tab.state!.insert('!');
    vi.advanceTimersByTime(1100);
    await nb.rename('a.md', 'b.md');
    expect(recovery.paths()).toEqual(['b.md']);
  });

  it('offers a copy that differs from the file, and Restore loads it unsaved', async () => {
    const { store, nb, recovery } = await open(
      { 'a.md': 'on disk', 'b.md': 'same' },
      { 'a.md': { text: 'on disk, edited', time: 5 }, 'b.md': { text: 'same', time: 5 } },
    );
    // Not in the session, but it has a copy: it gets a tab.
    const tab = nb.tabs.value.find((t) => t.path.value === 'a.md')!;
    expect(tab.recovered.value).toEqual({ text: 'on disk, edited', time: 5 });
    expect(tab.dirty.value).toBe(false);
    // A copy that matches the file is dropped quietly.
    expect(nb.tabs.value.some((t) => t.path.value === 'b.md')).toBe(true);
    expect(recovery.entries.has('b.md')).toBe(false);
    nb.restoreRecovered(tab);
    expect(tab.state!.text).toBe('on disk, edited');
    expect(tab.dirty.value).toBe(true);
    expect(await text(store, 'a.md')).toBe('on disk');
    tab.state!.undo();
    expect(tab.state!.text).toBe('on disk');
  });

  it('Discard throws the copy away; closing the tab unanswered keeps it', async () => {
    const entry = { text: 'lost words', time: 5 };
    let opened = await open({ 'a.md': '' }, { 'a.md': entry });
    let tab = opened.nb.tabs.value[0]!;
    opened.nb.closeTab(tab);
    expect(opened.recovery.get('a.md')).toEqual(entry);
    opened = await open({ 'a.md': '' }, { 'a.md': entry });
    tab = opened.nb.tabs.value[0]!;
    opened.nb.discardRecovered(tab);
    expect(tab.recovered.value).toBeNull();
    expect(opened.recovery.entries.has('a.md')).toBe(false);
  });

  it('never saves a recovered copy before the user restores it, even with auto-save', async () => {
    const { store, nb } = await open({ 'a.md': 'disk' }, { 'a.md': { text: 'copy', time: 1 } }, true);
    vi.advanceTimersByTime(5000);
    expect(await text(store, 'a.md')).toBe('disk');
    expect(nb.tabs.value[0]!.recovered.value?.text).toBe('copy');
  });
});

describe('webRecovery', () => {
  it('keys copies by notebook and skips notes that are too big', () => {
    const items = new Map<string, string>();
    const fake = {
      get length() {
        return items.size;
      },
      key: (i: number) => [...items.keys()][i] ?? null,
      getItem: (k: string) => items.get(k) ?? null,
      setItem: (k: string, v: string) => void items.set(k, v),
      removeItem: (k: string) => void items.delete(k),
    };
    vi.stubGlobal('localStorage', fake);
    try {
      const a = webRecovery('nas:t:Notes');
      const b = webRecovery('private');
      expect(a.set('x/y.md', { text: 'one', time: 1 })).toBe(true);
      expect(b.set('x/y.md', { text: 'two', time: 2 })).toBe(true);
      expect(a.get('x/y.md')?.text).toBe('one');
      expect(b.paths()).toEqual(['x/y.md']);
      expect(a.set('big.md', { text: 'x'.repeat(MAX_RECOVERY_CHARS + 1), time: 3 })).toBe(false);
      a.remove('x/y.md');
      expect(a.paths()).toEqual([]);
    } finally {
      vi.unstubAllGlobals();
    }
  });
});

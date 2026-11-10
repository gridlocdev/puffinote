import { describe, expect, it } from 'vitest';
import { Notebook } from '../src/notebook/notebook.ts';
import { memoryPrefs } from '../src/notebook/settings.ts';
import { MemoryStore } from '../src/platform/memoryStore.ts';
import { ShellModel } from '../src/shell/shellModel.ts';

const buttons = [
  { label: 'Cancel', value: 'cancel' },
  { label: 'OK', value: 'ok', kind: 'primary' as const },
];

describe('ShellModel overlays', () => {
  it('answers a confirm as cancelled when another overlay replaces it', async () => {
    const m = new ShellModel();
    const first = m.confirm({ title: 'First?', message: '', buttons });
    expect(m.asking).toBe(true);
    const second = m.confirm({ title: 'Second?', message: '', buttons });
    expect(await first).toBe('cancel');
    const o = m.overlay.value;
    expect(o?.kind === 'confirm' && o.options.title).toBe('Second?');
    if (o?.kind === 'confirm') o.done('ok');
    expect(await second).toBe('ok');
    expect(m.overlay.value).toBeNull();
    expect(m.asking).toBe(false);
  });

  it('answers a prompt with null when it is replaced or closed', async () => {
    const m = new ShellModel();
    const replaced = m.prompt({ title: 'Name', label: 'Name', value: 'x', confirm: 'OK' });
    m.open({ kind: 'commands' });
    expect(await replaced).toBeNull();
    expect(m.overlay.value?.kind).toBe('commands');
    const closed = m.prompt({ title: 'Name', label: 'Name', value: 'x', confirm: 'OK' });
    m.close();
    expect(await closed).toBeNull();
  });

  it('lets a quit go ahead when a close-tab question is still up', async () => {
    const store = new MemoryStore('N', { 'a.md': '' });
    const nb = await Notebook.open(store, { prefs: memoryPrefs({ autoSave: false }) });
    const m = new ShellModel();
    m.setNotebook(nb);
    const tab = (await nb.openFile('a.md'))!;
    tab.state!.insert('x');
    const closing = m.closeTab(tab);
    await Promise.resolve();
    expect(m.overlay.value?.kind).toBe('confirm');
    const quitting = m.confirmDiscardAll();
    expect(await closing).toBe(false); // the close was cancelled, the tab stays
    expect(nb.tabs.value).toContain(tab);
    const o = m.overlay.value;
    if (o?.kind === 'confirm') o.done('discard');
    expect(await quitting).toBe(true);
    m.setNotebook(null);
  });
});

import { signal } from '@preact/signals';
import { describe, expect, it } from 'vitest';
import { Notebook } from '../src/notebook/notebook.ts';
import { memoryPrefs, type PartialSettings } from '../src/notebook/settings.ts';
import { MemoryStore } from '../src/platform/memoryStore.ts';
import { LayoutPolicy } from '../src/shell/layoutPolicy.ts';

async function setUp(prefs: PartialSettings) {
  const store = new MemoryStore('N', { 'a.md': 'a', 'b.md': 'b', 'c.md': 'c' });
  const nb = await Notebook.open(store, { prefs: memoryPrefs({ autoSave: false, ...prefs }) });
  const notebook = signal<Notebook | null>(nb);
  return { nb, notebook, policy: new LayoutPolicy(notebook) };
}

const paths = (nb: Notebook) => nb.tabs.value.map((t) => t.path.value);

describe('LayoutPolicy', () => {
  it('keeps one note open in the Library layout, but not one with unsaved edits', async () => {
    const { nb, policy } = await setUp({ layout: 'library' });
    expect(policy.layout.value).toBe('library');
    await nb.openFile('a.md');
    await nb.openFile('b.md');
    expect(paths(nb)).toEqual(['b.md']);
    (await nb.openFile('a.md'))!.state!.insert('edit');
    expect(paths(nb)).toEqual(['a.md']);
    await nb.openFile('c.md');
    expect(paths(nb).sort()).toEqual(['a.md', 'c.md']);
    policy.dispose();
  });

  it('leaves tabs alone in the rail layout and shows the strip once there is one', async () => {
    const { nb, policy } = await setUp({ layout: 'rail', showTabBar: true });
    expect(policy.showTabBar.value).toBe(false);
    await nb.openFile('a.md');
    await nb.openFile('b.md');
    expect(paths(nb)).toEqual(['a.md', 'b.md']);
    expect(policy.showTabBar.value).toBe(true);
    policy.tabBarOverride.value = !policy.tabBarShown(nb);
    expect(policy.showTabBar.value).toBe(false);
    policy.dispose();
  });

  it('hides the tab strip in the Library layout and with no notebook', async () => {
    const { notebook, policy } = await setUp({ layout: 'library', showTabBar: true });
    expect(policy.showTabBar.value).toBe(false);
    notebook.value = null;
    expect(policy.layout.value).toBe('rail');
    expect(policy.showTabBar.value).toBe(false);
    policy.dispose();
  });
});

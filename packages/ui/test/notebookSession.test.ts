import { describe, expect, it } from 'vitest';
import { Notebook } from '../src/notebook/notebook.ts';
import { memoryPrefs } from '../src/notebook/settings.ts';
import { MemoryStore } from '../src/platform/memoryStore.ts';
import { NotebookSession } from '../src/shell/notebookSession.ts';

const open = (name: string) => Notebook.open(new MemoryStore(name, { 'a.md': '' }), { prefs: memoryPrefs() });

function setup(answer = true) {
  const calls: string[] = [];
  const session = new NotebookSession({
    leaving: () => calls.push('leaving'),
    entered: () => calls.push('entered'),
    confirmDiscardAll: async () => (calls.push('confirm'), answer),
  });
  return { session, calls };
}

describe('NotebookSession', () => {
  it('closes the old notebook and resets the view state when another comes in front', async () => {
    const { session, calls } = setup();
    const a = await open('A');
    const b = await open('B');
    session.set(a);
    session.filterQuery.value = 'puffin';
    session.searchQuery.value = 'gannet';
    session.popover.value = 'recent';
    calls.length = 0;
    const disposed: string[] = [];
    a.dispose = () => void disposed.push('A');
    session.set(b);
    expect(session.notebook.value).toBe(b);
    expect(disposed).toEqual(['A']);
    expect(calls).toEqual(['leaving', 'entered']);
    expect([session.filterQuery.value, session.searchQuery.value, session.popover.value]).toEqual([
      '',
      '',
      null,
    ]);
  });

  it('keeps the notebook when the same one is set again, but still resets the view', async () => {
    const { session, calls } = setup();
    const a = await open('A');
    session.set(a);
    calls.length = 0;
    session.filterQuery.value = 'x';
    session.set(a);
    expect(calls).toEqual(['entered']);
    expect(session.filterQuery.value).toBe('');
  });

  it('drops an open that a later one overtook', async () => {
    const { session } = setup();
    const first = session.beginOpen();
    const second = session.beginOpen();
    const a = await open('A');
    const b = await open('B');
    let aClosed = false;
    a.dispose = () => void (aClosed = true);
    expect(await session.adopt(a, first)).toBe(false);
    expect(aClosed).toBe(true);
    expect(await session.adopt(b, second)).toBe(true);
    expect(session.notebook.value).toBe(b);
  });

  it('keeps the notebook in front when the user cancels over unsaved tabs', async () => {
    const { session, calls } = setup(false);
    const a = await open('A');
    session.set(a);
    const b = await open('B');
    expect(await session.adopt(b, session.beginOpen())).toBe(false);
    expect(session.notebook.value).toBe(a);
    expect(calls).toContain('confirm');
  });
});

import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  NotFoundError,
  type NasClient,
  type NasRoot,
  type PrefsStorage,
  type PuffinoteNative,
  type ShellModel,
} from '@puffinote/ui';
import { Launcher, type HostKind } from '../src/launcher.ts';

function launcher(kind: HostKind, roots: NasRoot[] = []): Launcher {
  const nas = kind === 'nas' ? ({ roots: async () => roots } as unknown as NasClient) : null;
  return new Launcher({} as ShellModel, {} as PrefsStorage, { kind, native: null, nas });
}

describe('Launcher.start', () => {
  afterEach(() => vi.restoreAllMocks());

  it('opens the browser notebook straight away in a browser', async () => {
    const l = launcher('browser');
    let startingDuringOpen = false;
    const open = vi.spyOn(l, 'openPrivate').mockImplementation(async () => {
      startingDuringOpen = l.starting.value;
    });
    await l.start();
    expect(open).toHaveBeenCalledOnce();
    expect(startingDuringOpen).toBe(true);
    expect(l.starting.value).toBe(false);
  });

  it('never opens the browser notebook on the NAS', async () => {
    const l = launcher('nas');
    const open = vi.spyOn(l, 'openPrivate').mockResolvedValue();
    await l.start();
    expect(open).not.toHaveBeenCalled();
  });

  it('reopens a recent browser notebook rather than starting another', async () => {
    const l = launcher('browser');
    l.recents.value = [{ kind: 'private', name: 'Browser notebook' }];
    const reopen = vi.spyOn(l, 'open').mockResolvedValue(true);
    const fresh = vi.spyOn(l, 'openPrivate');
    await l.start();
    expect(reopen).toHaveBeenCalledWith({ kind: 'private', name: 'Browser notebook' });
    expect(fresh).not.toHaveBeenCalled();
  });

  it('skips a NAS recent in a plain browser and opens the browser notebook', async () => {
    const l = launcher('browser');
    l.recents.value = [{ kind: 'nas', root: 'sample', rootName: 'Sample', base: '', name: 'Sample' }];
    const reopen = vi.spyOn(l, 'open').mockResolvedValue(true);
    await l.start();
    expect(reopen).toHaveBeenCalledWith({ kind: 'private', name: 'Browser notebook' });
  });

  it('opens a NAS server’s only root as the notebook', async () => {
    const l = launcher('nas', [{ id: 'r1', name: 'Notes' }]);
    const open = vi.spyOn(l, 'openNas').mockResolvedValue();
    await l.start();
    expect(open).toHaveBeenCalledWith({ id: 'r1', name: 'Notes' }, '');
  });

  it('leaves the choice of root to the welcome screen when there are several', async () => {
    const l = launcher('nas', [
      { id: 'a', name: 'A' },
      { id: 'b', name: 'B' },
    ]);
    const open = vi.spyOn(l, 'openNas').mockResolvedValue();
    await l.start();
    expect(open).not.toHaveBeenCalled();
  });
});

describe('recents', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('drops folders picked through the old Chromium folder picker', async () => {
    const saved = JSON.stringify([
      { kind: 'browser', id: 'x', name: 'Old' },
      { kind: 'private', name: 'Browser notebook' },
    ]);
    vi.stubGlobal('localStorage', { getItem: () => saved, setItem: () => undefined });
    vi.resetModules();
    const { Launcher: Fresh } = await import('../src/launcher.ts');
    const l = new Fresh({} as ShellModel, {} as PrefsStorage, { kind: 'browser', native: null, nas: null });
    expect(l.recents.value).toEqual([{ kind: 'private', name: 'Browser notebook' }]);
  });
});

describe('Launcher.prune (PRD §6)', () => {
  const nasRecent = (base: string) =>
    ({ kind: 'nas', root: 'r', rootName: 'Notes', base, name: base || 'Notes' }) as const;

  it('drops a desktop folder that was deleted and keeps one that can’t be reached', async () => {
    const gone = new Set(['/a/deleted']);
    const native = { folderGone: async (p: string) => gone.has(p) } as unknown as PuffinoteNative;
    const l = new Launcher({} as ShellModel, {} as PrefsStorage, { kind: 'native', native, nas: null });
    l.recents.value = [
      { kind: 'native', path: '/a/deleted', name: 'deleted' },
      { kind: 'native', path: '/Volumes/Usb/notes', name: 'notes' },
    ];
    await l.prune();
    expect(l.recents.value.map((r) => r.name)).toEqual(['notes']);
  });

  it('never drops the notebook open now', async () => {
    const native = { folderGone: async () => true } as unknown as PuffinoteNative;
    const l = new Launcher({} as ShellModel, {} as PrefsStorage, { kind: 'native', native, nas: null });
    l.recents.value = [{ kind: 'native', path: '/a/open', name: 'open' }];
    l.current.value = 'native:/a/open';
    await l.prune();
    expect(l.recents.value).toHaveLength(1);
  });

  it('drops a NAS folder whose parent answers without it, and keeps all when the server is off', async () => {
    const list = vi.fn(async (_root: string, path: string) => {
      if (path === 'gone') throw new NotFoundError(path);
      return [];
    });
    const nas = { list, roots: async () => [{ id: 'r', name: 'Notes' }] } as unknown as NasClient;
    const l = new Launcher({} as ShellModel, {} as PrefsStorage, { kind: 'nas', native: null, nas });
    l.recents.value = [nasRecent('gone'), nasRecent('here'), nasRecent('')];
    await l.prune();
    expect(l.recents.value.map((r) => r.name)).toEqual(['here', 'Notes']);

    const off = {
      list: async () => Promise.reject(new Error('fetch failed')),
      roots: async () => Promise.reject(new Error('fetch failed')),
    } as unknown as NasClient;
    const l2 = new Launcher({} as ShellModel, {} as PrefsStorage, { kind: 'nas', native: null, nas: off });
    l2.recents.value = [nasRecent('gone'), nasRecent('')];
    await l2.prune();
    expect(l2.recents.value).toHaveLength(2);
  });

  it('drops a NAS root the server no longer has', async () => {
    const nas = { roots: async () => [{ id: 'other', name: 'Other' }] } as unknown as NasClient;
    const l = new Launcher({} as ShellModel, {} as PrefsStorage, { kind: 'nas', native: null, nas });
    l.recents.value = [nasRecent('')];
    await l.prune();
    expect(l.recents.value).toEqual([]);
  });
});

describe('Launcher.requestFolder', () => {
  it('opens a folder passed at launch instead of the last notebook', async () => {
    const native = { folderGone: async () => false } as unknown as PuffinoteNative;
    const model = { confirmDiscardAll: async () => true } as unknown as ShellModel;
    const l = new Launcher(model, {} as PrefsStorage, { kind: 'native', native, nas: null });
    l.recents.value = [{ kind: 'native', path: '/last', name: 'last' }];
    const open = vi.spyOn(l, 'open').mockResolvedValue(true);
    l.requestFolder('/asked/Notes');
    await l.start();
    await vi.waitFor(() => expect(open).toHaveBeenCalledOnce());
    expect(open).toHaveBeenCalledWith({ kind: 'native', path: '/asked/Notes', name: 'Notes' });
  });
});

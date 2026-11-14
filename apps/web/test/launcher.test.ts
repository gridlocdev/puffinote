import { afterEach, describe, expect, it, vi } from 'vitest';
import type { NasClient, NasRoot, PrefsStorage, ShellModel } from '@puffinote/ui';
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

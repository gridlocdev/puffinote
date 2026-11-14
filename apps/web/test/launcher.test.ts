import { afterEach, describe, expect, it, vi } from 'vitest';
import type { PrefsStorage, ShellModel } from '@puffinote/ui';
import { Launcher, type HostKind } from '../src/launcher.ts';

const g = globalThis as { showDirectoryPicker?: unknown };

function launcher(kind: HostKind): Launcher {
  return new Launcher({} as ShellModel, {} as PrefsStorage, { kind, native: null, nas: null });
}

describe('Launcher.start', () => {
  afterEach(() => {
    delete g.showDirectoryPicker;
    vi.restoreAllMocks();
  });

  it('opens the browser notebook straight away where folders can’t be opened', async () => {
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

  it('leaves the choice to the welcome screen where a folder can be opened', async () => {
    g.showDirectoryPicker = () => undefined;
    const l = launcher('browser');
    const open = vi.spyOn(l, 'openPrivate').mockResolvedValue();
    await l.start();
    expect(open).not.toHaveBeenCalled();
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
});

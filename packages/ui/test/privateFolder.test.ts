import { afterEach, describe, expect, it, vi } from 'vitest';
import { persistStorage, privateFolder } from '../src/platform/browserStore.ts';

describe('privateFolder', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('gives up on storage that never answers instead of hanging start-up', async () => {
    vi.stubGlobal('navigator', { storage: { getDirectory: () => new Promise(() => undefined) } });
    expect(await privateFolder(20)).toBeNull();
  });

  it('is null where the browser refuses storage', async () => {
    vi.stubGlobal('navigator', {
      storage: { getDirectory: () => Promise.reject(new Error('SecurityError')) },
    });
    expect(await privateFolder(20)).toBeNull();
  });
});

describe('persistStorage', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('asks the browser to keep the notebook', async () => {
    const persist = vi.fn(async () => true);
    vi.stubGlobal('navigator', { storage: { persisted: async () => false, persist } });
    expect(await persistStorage()).toBe(true);
    expect(persist).toHaveBeenCalledOnce();
  });

  it('does not ask again when the storage is already persistent', async () => {
    const persist = vi.fn(async () => true);
    vi.stubGlobal('navigator', { storage: { persisted: async () => true, persist } });
    expect(await persistStorage()).toBe(true);
    expect(persist).not.toHaveBeenCalled();
  });

  it('ignores a refusal, a failure and a browser without the API', async () => {
    vi.stubGlobal('navigator', { storage: { persist: async () => false } });
    expect(await persistStorage()).toBe(false);
    vi.stubGlobal('navigator', { storage: { persist: () => Promise.reject(new Error('nope')) } });
    expect(await persistStorage()).toBe(false);
    vi.stubGlobal('navigator', { storage: {} });
    expect(await persistStorage()).toBe(false);
    vi.stubGlobal('navigator', {});
    expect(await persistStorage()).toBe(false);
  });
});

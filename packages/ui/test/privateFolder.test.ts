import { afterEach, describe, expect, it, vi } from 'vitest';
import { privateFolder } from '../src/platform/browserStore.ts';

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

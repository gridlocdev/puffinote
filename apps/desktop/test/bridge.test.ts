import { describe, expect, it } from 'vitest';
import { asResult, CONFLICT_CHANGED, CONFLICT_EXISTS, NOT_FOUND } from '../src/bridge.ts';

describe('asResult', () => {
  it('passes a handler’s result through', async () => {
    expect(await asResult(() => Promise.resolve('v1'))).toBe('v1');
  });

  it('sends conflicts and missing files back as results, so Electron does not print them', async () => {
    for (const code of [CONFLICT_CHANGED, CONFLICT_EXISTS, NOT_FOUND]) {
      const message = `${code}: .gitignore`;
      expect(await asResult(() => Promise.reject(new Error(message)))).toEqual({ puffinoteFailure: message });
    }
  });

  it('still throws anything else', async () => {
    await expect(asResult(() => Promise.reject(new Error('Forbidden')))).rejects.toThrow('Forbidden');
  });
});

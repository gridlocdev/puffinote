import { describe, expect, it } from 'vitest';
import { afterRendererGone } from '../src/recovery.ts';

describe('afterRendererGone', () => {
  it('finishes a close or quit that was under way', () => {
    expect(afterRendererGone({ closing: true, quitting: false, headless: false })).toBe('destroy');
    expect(afterRendererGone({ closing: false, quitting: true, headless: false })).toBe('destroy');
  });

  it('otherwise offers to reload instead of leaving a blank window', () => {
    expect(afterRendererGone({ closing: false, quitting: false, headless: false })).toBe('ask');
  });

  it('reloads without a dialog when headless', () => {
    expect(afterRendererGone({ closing: false, quitting: false, headless: true })).toBe('reload');
  });
});

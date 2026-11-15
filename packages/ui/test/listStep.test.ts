import { describe, expect, it } from 'vitest';
import { listStep } from '../src/shell/listStep.ts';

describe('listStep', () => {
  const k = (key: string, shiftKey = false) => ({
    key,
    shiftKey,
    ctrlKey: false,
    metaKey: false,
    altKey: false,
  });
  it('Tab and Shift+Tab move the highlight like ↓ and ↑, stopping at the ends', () => {
    expect(listStep(k('Tab'), 0, 3)).toBe(1);
    expect(listStep(k('ArrowDown'), 0, 3)).toBe(1);
    expect(listStep(k('Tab'), 2, 3)).toBe(2);
    expect(listStep(k('Tab', true), 2, 3)).toBe(1);
    expect(listStep(k('ArrowUp'), 2, 3)).toBe(1);
    expect(listStep(k('Tab', true), 0, 3)).toBe(0);
    expect(listStep(k('Tab'), 0, 0)).toBe(0);
    expect(listStep({ ...k('Tab'), ctrlKey: true }, 0, 3)).toBeUndefined();
    expect(listStep(k('Enter'), 0, 3)).toBeUndefined();
  });
});

import { useEffect, useState } from 'preact/hooks';

/** How long typing pauses before the word count and the Outline catch up. */
export const settleMs = 300;

/**
 * `value`, held back while it keeps changing: the latest value once it has stayed the same for `delay` ms. A
 * change of `key` (another note) passes the new value through at once. The status bar's word count and the
 * Outline settle the note's version through this, so a keystroke in a long note doesn't recount it.
 */
export function useSettled<T>(value: T, key: unknown, delay: number): T {
  const [held, setHeld] = useState({ value, key });
  useEffect(() => {
    if (held.key !== key) {
      setHeld({ value, key });
      return;
    }
    if (Object.is(held.value, value)) return;
    const timer = setTimeout(() => setHeld({ value, key }), delay);
    return () => clearTimeout(timer);
  }, [value, key]);
  return held.key === key ? held.value : value;
}

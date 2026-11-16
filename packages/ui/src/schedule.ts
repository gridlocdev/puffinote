/**
 * The page's only timers. Anything that waits goes through a named helper here, so every wait in the app can be
 * found and judged in one place. A timer is never the fix for things happening in the wrong order: change the
 * model first and let the signals redraw.
 */

/** Cancels a pending call. Calling it after the call ran, or twice, does nothing. */
export type Cancel = () => void;

/** Calls `run` once after `ms` (0: once the current task and its microtasks are done). */
export function after(ms: number, run: () => void): Cancel {
  const id = setTimeout(run, ms);
  return () => clearTimeout(id);
}

/** Calls `run` before the next paint, once the page has laid out what changed. */
export function nextFrame(run: (now: number) => void): Cancel {
  const id = requestAnimationFrame(run);
  return () => cancelAnimationFrame(id);
}

import { realOfNearest } from './paths.ts';

/**
 * Per-file locks: `run` waits for every earlier call on the same file to settle, so a save's check-then-write
 * can't interleave with another save of that file. The same file under any spelling gets one lock: paths are
 * symlink-resolved (a new file through its nearest existing folder), then case- and normalization-folded,
 * since `Note.md` and `note.md` are one file on a case-insensitive volume. Folding on a case-sensitive one
 * only makes two files wait for each other.
 */
export class FileLocks {
  readonly #tails = new Map<string, Promise<unknown>>();
  /**
   * Keys are worked out one call at a time, so calls take a lock in the order they were made. A key that can't
   * be worked out fails its own call, not the ones after it.
   */
  #keying: Promise<unknown> = Promise.resolve();

  async run<T>(p: string, fn: () => Promise<T>): Promise<T> {
    const keyed = this.#keying.then(() => lockKey(p));
    this.#keying = Promise.allSettled([keyed]);
    return this.#runKey(await keyed, fn);
  }

  /** `run` over several files, taken in one order so two callers can't wait on each other. */
  async runAll<T>(paths: readonly string[], fn: () => Promise<T>): Promise<T> {
    const keyed = this.#keying.then(() => Promise.all(paths.map(lockKey)));
    this.#keying = Promise.allSettled([keyed]);
    const keys = [...new Set(await keyed)].sort();
    const next = (i: number): Promise<T> => {
      const key = keys[i];
      return key === undefined ? fn() : this.#runKey(key, () => next(i + 1));
    };
    return next(0);
  }

  async #runKey<T>(key: string, fn: () => Promise<T>): Promise<T> {
    const prev = this.#tails.get(key) ?? Promise.resolve();
    const run = prev.then(fn, fn);
    // The next call waits for this one to settle either way; this one's failure goes to its own caller.
    const tail = Promise.allSettled([run]);
    this.#tails.set(key, tail);
    try {
      return await run;
    } finally {
      if (this.#tails.get(key) === tail) this.#tails.delete(key);
    }
  }
}

async function lockKey(p: string): Promise<string> {
  return (await realOfNearest(p)).normalize('NFC').toLowerCase();
}

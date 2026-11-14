/**
 * Recovery copies of unsaved edits (PRD §5): what a dirty tab holds, kept outside the notebook so a crash, a
 * killed app or a closed browser tab doesn't lose it, and the real file is never touched. One storage is bound
 * to one notebook; entries are keyed by note path.
 */
export interface RecoveryEntry {
  readonly text: string;
  /** When the copy was taken, in milliseconds since the epoch. */
  readonly time: number;
}

export interface RecoveryStorage {
  get(path: string): RecoveryEntry | null;
  /** Keep a copy. False when it couldn't (too big, storage full or disabled). */
  set(path: string, entry: RecoveryEntry): boolean;
  remove(path: string): void;
  /** The paths with a copy. */
  paths(): string[];
}

/** A note longer than this (in UTF-16 code units, about 2 MB) gets no recovery copy. */
export const MAX_RECOVERY_CHARS = 1_000_000;

const PREFIX = 'puffinote.recovery:';

/**
 * Recovery copies in the page's localStorage, which every host has: the browser, the NAS client and the
 * desktop renderer (whose storage lives in the app's user-data folder). It is written synchronously, so a copy
 * taken survives the page dying right after. `notebook` names the notebook (the launcher's recent key).
 */
export function webRecovery(notebook: string): RecoveryStorage {
  const prefix = `${PREFIX}${encodeURIComponent(notebook)}:`;
  const storage = (): Storage | null => {
    try {
      return globalThis.localStorage ?? null;
    } catch {
      return null;
    }
  };
  return {
    get(path) {
      try {
        return parseEntry(storage()?.getItem(prefix + path) ?? null);
      } catch {
        return null;
      }
    },
    set(path, entry) {
      const s = storage();
      if (!s || entry.text.length > MAX_RECOVERY_CHARS) return false;
      const value = JSON.stringify(entry);
      // Storage full: make room by dropping the oldest copies of other notes (any notebook), then try again.
      for (let attempt = 0; attempt < 8; attempt++) {
        try {
          s.setItem(prefix + path, value);
          return true;
        } catch {
          if (!dropOldest(s, prefix + path)) return false;
        }
      }
      return false;
    },
    remove(path) {
      try {
        storage()?.removeItem(prefix + path);
      } catch {
        // Storage disabled: nothing was kept.
      }
    },
    paths() {
      const s = storage();
      if (!s) return [];
      const out: string[] = [];
      try {
        for (let i = 0; i < s.length; i++) {
          const k = s.key(i);
          if (k?.startsWith(prefix)) out.push(k.slice(prefix.length));
        }
      } catch {
        return [];
      }
      return out;
    },
  };
}

function parseEntry(raw: string | null): RecoveryEntry | null {
  if (raw === null) return null;
  try {
    const v = JSON.parse(raw) as Partial<RecoveryEntry> | null;
    if (typeof v?.text !== 'string') return null;
    return { text: v.text, time: typeof v.time === 'number' ? v.time : 0 };
  } catch {
    return null;
  }
}

/** Remove the oldest recovery copy other than `keep`. False when there is none to remove. */
function dropOldest(s: Storage, keep: string): boolean {
  let oldest: { key: string; time: number } | null = null;
  for (let i = 0; i < s.length; i++) {
    const k = s.key(i);
    if (!k?.startsWith(PREFIX) || k === keep) continue;
    const time = parseEntry(s.getItem(k))?.time ?? 0;
    if (!oldest || time < oldest.time) oldest = { key: k, time };
  }
  if (!oldest) return false;
  s.removeItem(oldest.key);
  return true;
}

/** Recovery copies in memory (tests). */
export function memoryRecovery(initial: Record<string, RecoveryEntry> = {}): RecoveryStorage & {
  readonly entries: Map<string, RecoveryEntry>;
} {
  const entries = new Map(Object.entries(initial));
  return {
    entries,
    get: (path) => entries.get(path) ?? null,
    set: (path, entry) => {
      if (entry.text.length > MAX_RECOVERY_CHARS) return false;
      entries.set(path, entry);
      return true;
    },
    remove: (path) => void entries.delete(path),
    paths: () => [...entries.keys()],
  };
}

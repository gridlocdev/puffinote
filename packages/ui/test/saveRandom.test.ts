import { effect } from '@preact/signals';
import { encodeText } from '@puffinote/text';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Notebook } from '../src/notebook/notebook.ts';
import { memoryPrefs } from '../src/notebook/settings.ts';
import type { Tab } from '../src/notebook/tab.ts';
import { MemoryStore } from '../src/platform/memoryStore.ts';
import type { FileData, WriteOptions } from '../src/platform/notebookStore.ts';
import { runRandomOps, type Op } from './support/randomOps.ts';

const NOTES = ['a.md', 'b.md'];
const encode = (text: string) => new TextEncoder().encode(text);

interface Parked {
  readonly kind: 'read' | 'write';
  readonly path: string;
  /** Answer the call. `early` answers a read with the file as it was when the read started. */
  let(fail: boolean, early: boolean): void;
}

/**
 * A notebook store whose note reads and writes wait until the test lets each one through, in any order, and
 * whose writes check the version when they land, as a real disk or server does. Every text a note has held
 * is kept by version, so the test can tell what a tab's version stands for.
 */
class GatedStore extends MemoryStore {
  gated = false;
  readonly parked: Parked[] = [];
  /** The bytes of each version the notes have had. */
  readonly held = new Map<string, Uint8Array>();
  /** Writes that went out while the tab was in a conflict, unforced. */
  readonly writesDuringConflict: string[] = [];
  tabFor: (path: string) => Tab | undefined = () => undefined;

  override read(path: string): Promise<FileData> {
    if (!this.gated || !NOTES.includes(path)) return super.read(path);
    const before = super.read(path);
    before.catch(() => undefined);
    return new Promise((resolve, reject) => {
      this.parked.push({
        kind: 'read',
        path,
        let: (fail, early) => {
          if (fail) reject(new Error('offline'));
          else (early ? before : super.read(path)).then(resolve, reject);
        },
      });
    });
  }

  override write(path: string, bytes: Uint8Array, opts?: WriteOptions): Promise<string | null> {
    if (!this.gated || !NOTES.includes(path)) return super.write(path, bytes, opts);
    const tab = this.tabFor(path);
    if (tab && tab.conflict.peek() !== 'none' && !opts?.force) this.writesDuringConflict.push(path);
    return new Promise((resolve, reject) => {
      this.parked.push({
        kind: 'write',
        path,
        let: (fail) => {
          if (fail) return reject(new Error('disk full'));
          super.write(path, bytes, opts).then((version) => {
            if (version !== null) this.held.set(version, bytes.slice());
            resolve(version);
          }, reject);
        },
      });
    });
  }

  /** Someone else writes the note (another app, another machine). */
  async outside(path: string, text: string): Promise<void> {
    const version = await super.write(path, encode(text), { force: true });
    if (version !== null) this.held.set(version, encode(text));
  }

  async outsideRemove(path: string): Promise<void> {
    await super.remove(path).catch(() => undefined);
  }
}

interface World {
  store: GatedStore;
  nb: Notebook;
  tabs: Tab[];
  /** Failures nobody injected. */
  errors: string[];
  /** The newest version each tab has had; it never goes back to an older one. */
  newest: Map<Tab, number>;
  stop: () => void;
}

/** Lets every promise that can move on do so. */
const settle = async () => {
  for (let i = 0; i < 5; i++) await new Promise((resolve) => setImmediate(resolve));
};

/** The test's own failures, and a note it deleted on disk. */
const INJECTED = /offline|disk full|isn’t in the notebook any more/;

function track(w: World, promise: Promise<unknown>): void {
  promise.catch((e: unknown) => {
    const message = e instanceof Error ? e.message : String(e);
    if (!INJECTED.test(message)) w.errors.push(message);
  });
}

function pickTab(w: World, r: { pick<T>(items: readonly T[]): T }): Tab {
  return r.pick(w.tabs);
}

const ops: Op<World>[] = [
  {
    name: 'type',
    weight: 4,
    run: async (w, r) => {
      const tab = pickTab(w, r);
      const state = tab.state!;
      state.select(r.chance(0.5) ? 0 : state.document.length);
      const typed = r.text(3) || 'x';
      state.insert(typed);
      await settle();
      return `${tab.path.peek()} ${JSON.stringify(typed)}`;
    },
  },
  {
    name: 'undo',
    run: async (w, r) => {
      const tab = pickTab(w, r);
      tab.state!.undo();
      await settle();
      return tab.path.peek();
    },
  },
  {
    name: 'the clock moves past the autosave delay',
    weight: 2,
    run: async () => {
      vi.advanceTimersByTime(1100);
      await settle();
    },
  },
  {
    name: 'save (⌘S)',
    run: async (w, r) => {
      const tab = pickTab(w, r);
      track(w, w.nb.save(tab));
      await settle();
      return tab.path.peek();
    },
  },
  {
    name: 'refresh (window focus)',
    run: async (w) => {
      track(w, w.nb.refresh());
      await settle();
    },
  },
  {
    name: 'outside write',
    weight: 0.7,
    run: async (w, r) => {
      const path = r.pick(NOTES);
      const text = r.text(6);
      await w.store.outside(path, text);
      await settle();
      return `${path} ${JSON.stringify(text)}`;
    },
  },
  {
    name: 'outside delete',
    weight: 0.2,
    run: async (w, r) => {
      const path = r.pick(NOTES);
      await w.store.outsideRemove(path);
      await settle();
      return path;
    },
  },
  {
    name: 'let a call through',
    weight: 5,
    when: (w) => w.store.parked.length > 0,
    run: async (w, r) => {
      const [call] = w.store.parked.splice(r.int(w.store.parked.length), 1);
      const fail = r.chance(0.15);
      const early = r.chance(0.5);
      call!.let(fail, early);
      await settle();
      return `${call!.kind} ${call!.path}${fail ? ' (fails)' : early && call!.kind === 'read' ? ' (as it was)' : ''}`;
    },
  },
  {
    // Offered for a note that changed on disk; one deleted there offers Save Again and Close Tab.
    name: 'Reload from Disk',
    weight: 0.5,
    when: (w) => w.tabs.some((t) => t.conflict.peek() === 'changed'),
    run: async (w, r) => {
      const tab = r.pick(w.tabs.filter((t) => t.conflict.peek() === 'changed'));
      track(w, w.nb.reload(tab));
      await settle();
      return tab.path.peek();
    },
  },
  {
    name: 'Keep My Version',
    weight: 0.5,
    when: (w) => w.tabs.some((t) => t.conflict.peek() !== 'none'),
    run: async (w, r) => {
      const tab = r.pick(w.tabs.filter((t) => t.conflict.peek() !== 'none'));
      track(w, w.nb.save(tab, true));
      await settle();
      return tab.path.peek();
    },
  },
];

describe('saving open notes', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it('random typing, autosaves, refreshes, outside changes and slow or failing calls never lose a save or go back to an older file', async () => {
    await runRandomOps<World>({
      steps: 120,
      setup: async (r) => {
        const store = new GatedStore('N', Object.fromEntries(NOTES.map((p) => [p, r.text(6)])));
        for (const p of NOTES) {
          const data = await store.read(p);
          store.held.set(data.version!, data.bytes);
        }
        const nb = await Notebook.open(store, { prefs: memoryPrefs({ autoSave: true }) });
        const tabs: Tab[] = [];
        for (const p of NOTES) tabs.push((await nb.openFile(p))!);
        store.tabFor = (path) => tabs.find((t) => t.path.peek() === path);
        const errors: string[] = [];
        const stop = effect(() => {
          const notice = nb.notice.value;
          if (notice?.kind === 'error' && !INJECTED.test(notice.text)) errors.push(notice.text);
        });
        store.gated = true;
        return { store, nb, tabs, errors, newest: new Map(), stop };
      },
      ops,
      check: (w) => {
        expect(w.errors, 'nothing fails but the calls the test failed').toEqual([]);
        expect(
          w.store.writesDuringConflict,
          'nothing is written over a conflict until the user chooses',
        ).toEqual([]);
        for (const tab of w.tabs) {
          const path = tab.path.peek();
          const writes = w.store.parked.filter((c) => c.kind === 'write' && c.path === path).length;
          expect(writes, `${path}: never two writes at once`).toBeLessThanOrEqual(1);
          const version = tab.version;
          if (version === null) continue;
          const n = Number(version);
          expect(n, `${path}: the tab never goes back to an older file`).toBeGreaterThanOrEqual(
            w.newest.get(tab) ?? 0,
          );
          w.newest.set(tab, n);
          expect(
            encodeText(tab.savedText, tab.format),
            `${path}: what the tab thinks is on disk is the file at its version`,
          ).toEqual(w.store.held.get(version));
          expect(tab.dirty.peek(), `${path}: dirty exactly when the text differs from the disk's`).toBe(
            tab.state!.text !== tab.savedText,
          );
        }
      },
      teardown: (w) => {
        w.stop();
        w.store.gated = false;
        for (const call of w.store.parked.splice(0)) call.let(true, false);
        w.nb.dispose();
      },
    });
  });
});

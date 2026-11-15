import { computed, signal, type ReadonlySignal, type Signal } from '@preact/signals';
import { readText } from '@puffinote/text';
import {
  ConflictError,
  NotFoundError,
  type FileData,
  type NotebookStore,
} from '../platform/notebookStore.ts';
import {
  changedSettings,
  resolveSettings,
  sanitize,
  unrecognized,
  withChanges,
  type PartialSettings,
  type PrefsStorage,
  type Settings,
} from './settings.ts';
import { encodeJson, ensureStateDir, sameBytes, writeOver } from './storeWrites.ts';
import { stateDir } from './tree.ts';

export const overridesPath = `${stateDir}/settings.json`;

/** What a notebook's settings need of the notebook and the host. */
export interface SettingsHost {
  readonly store: NotebookStore;
  /** The user's preferences, shared by every notebook. */
  readonly prefs: PrefsStorage;
  /** Whether the notebook closed; a closed notebook writes nothing. */
  closed(): boolean;
  report(e: unknown): void;
}

/** What `.puffinote/settings.json` holds now. */
interface OverridesOnDisk {
  /** The settings this version knows. */
  readonly known: PartialSettings;
  /** Everything else in the file, kept as it is. */
  readonly rest: Record<string, unknown>;
  /** The file's version; undefined when there is no file. */
  readonly version: string | null | undefined;
  readonly bytes: Uint8Array;
}

/**
 * A notebook's settings (PRD §9): the user's preferences, this notebook's overrides in
 * `.puffinote/settings.json`, and the two resolved into the settings everything reads.
 */
export class NotebookSettings {
  readonly userPrefs: Signal<PartialSettings>;
  readonly overrides = signal<PartialSettings>({});
  readonly settings: ReadonlySignal<Settings>;
  readonly #host: SettingsHost;
  /** Writes of `.puffinote/settings.json` in flight; a reload doesn't read it meanwhile. */
  #writes = 0;

  constructor(host: SettingsHost) {
    this.#host = host;
    this.userPrefs = signal(host.prefs.load());
    this.settings = computed(() => resolveSettings(this.userPrefs.value, this.overrides.value));
  }

  /** Read this notebook's overrides as the notebook opens. A file that can't be read or parsed counts as none. */
  async load(): Promise<void> {
    let raw: unknown = null;
    try {
      raw = JSON.parse(readText((await this.#host.store.read(overridesPath)).bytes).text) as unknown;
    } catch {
      // No file, or not JSON: no overrides.
    }
    this.overrides.value = sanitize(raw);
  }

  /**
   * Change the user's preferences. With `base` (what the Settings dialog showed when it opened), only the
   * preferences that differ from it are changed, over what is saved by now: another window's changes stay.
   */
  setPreferences(prefs: PartialSettings, base?: PartialSettings): void {
    const store = this.#host.prefs;
    const clean = sanitize(
      base ? withChanges(store.load(), prefs, changedSettings(base, sanitize(prefs))) : prefs,
    );
    store.save(clean);
    this.userPrefs.value = clean;
  }

  /**
   * Change this notebook's overrides, saved in `.puffinote/settings.json`. Written only when they changed.
   * Another program (a sync, git, another window) may have changed the file since it was read, so the
   * settings that changed here are applied over what the file holds now, and written against its version;
   * the rest of the file, settings this version doesn't know included, stays as it is (PRD §9). `base` is what
   * the overrides were when the change began (the Settings dialog opening); only settings that differ from it
   * count as changed, so one a refresh brought in meanwhile isn't undone.
   */
  async setOverrides(
    overrides: PartialSettings,
    base: PartialSettings = this.overrides.peek(),
  ): Promise<void> {
    const after = sanitize(overrides);
    const changed = changedSettings(base, after);
    if (changed.length === 0) return;
    const { store } = this.#host;
    this.overrides.value = sanitize(withChanges(this.overrides.peek(), after, changed));
    this.#writes++;
    try {
      await ensureStateDir(store);
      for (let attempt = 0; ; attempt++) {
        const disk = await this.#read();
        const merged = withChanges(disk.known, after, changed);
        try {
          const bytes = encodeJson({ ...disk.rest, ...merged });
          if (disk.version === undefined) await store.write(overridesPath, bytes, { create: true });
          else
            await writeOver(
              store,
              overridesPath,
              bytes,
              disk.version,
              (now) => sameBytes(now, disk.bytes),
              () => this.#host.closed(),
            );
          this.overrides.value = sanitize(merged);
          return;
        } catch (e) {
          // Changed again between the read and the write: read it again.
          if (!(e instanceof ConflictError) || e.reason === 'locked' || attempt >= 3) throw e;
        }
      }
    } catch (e) {
      this.#host.report(e);
    } finally {
      this.#writes--;
    }
  }

  /** Take changes another program made to `.puffinote/settings.json` (on refresh). */
  async reload(): Promise<void> {
    if (this.#writes > 0) return;
    let disk: OverridesOnDisk;
    try {
      disk = await this.#read();
    } catch {
      return;
    }
    if (this.#writes > 0) return;
    if (JSON.stringify(disk.known) !== JSON.stringify(this.overrides.peek()))
      this.overrides.value = disk.known;
  }

  async #read(): Promise<OverridesOnDisk> {
    let data: FileData;
    try {
      data = await this.#host.store.read(overridesPath);
    } catch (e) {
      if (e instanceof NotFoundError)
        return { known: {}, rest: {}, version: undefined, bytes: new Uint8Array() };
      throw e;
    }
    let raw: unknown = null;
    try {
      raw = JSON.parse(readText(data.bytes).text) as unknown;
    } catch {
      // Not JSON: nothing in it can be kept.
    }
    return { known: sanitize(raw), rest: unrecognized(raw), version: data.version, bytes: data.bytes };
  }
}

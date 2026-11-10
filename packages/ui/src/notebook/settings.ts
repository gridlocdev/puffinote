import type { ViewMode } from '../editor/reveal.ts';

/**
 * Puffinote's settings (PRD §9). They resolve in layers, lowest first: the defaults here, the user's
 * preferences (every notebook), then the notebook's own overrides in `.puffinote/settings.json`, which travel
 * with the folder.
 */
export interface Settings {
  readonly theme: 'system' | 'light' | 'dark';
  /** The view a note opens in. */
  readonly defaultView: ViewMode;
  /** Save a note a moment after the last edit. */
  readonly autoSave: boolean;
  /** What Tab inserts and how far a list item indents. */
  readonly indent: 'spaces2' | 'spaces4' | 'tab';
  /** Show `.md` and other extensions in the file tree. */
  readonly showFileExtensions: boolean;
  /** Show files and folders whose names start with a dot. */
  readonly showHiddenFiles: boolean;
  /** Show the tab strip over the editor (PRD §9). */
  readonly showTabBar: boolean;
}

export type SettingKey = keyof Settings;
export type PartialSettings = { -readonly [K in SettingKey]?: Settings[K] };

export const defaultSettings: Settings = {
  theme: 'system',
  defaultView: 'live',
  autoSave: true,
  indent: 'spaces4',
  showFileExtensions: false,
  showHiddenFiles: false,
  showTabBar: true,
};

/** One field in the Settings dialog: its label, help, and the values it takes. */
export interface SettingField<K extends SettingKey = SettingKey> {
  readonly key: K;
  readonly label: string;
  readonly help: string;
  readonly options?: readonly { readonly value: Settings[K]; readonly label: string }[];
}

export const settingFields: readonly SettingField[] = [
  {
    key: 'theme',
    label: 'Appearance',
    help: 'Light, dark, or whatever the system uses.',
    options: [
      { value: 'system', label: 'Match system' },
      { value: 'light', label: 'Light' },
      { value: 'dark', label: 'Dark' },
    ],
  },
  {
    key: 'defaultView',
    label: 'Notes open in',
    help: 'Rich hides Markdown markers until the caret reaches them; Formatted always shows them.',
    options: [
      { value: 'live', label: 'Rich view' },
      { value: 'source', label: 'Formatted view' },
      { value: 'raw', label: 'Raw text' },
    ],
  },
  { key: 'autoSave', label: 'Save automatically', help: 'Save a note a second after you stop typing.' },
  {
    key: 'indent',
    label: 'Indent with',
    help: 'What Tab inserts, and how far a nested list item moves.',
    options: [
      { value: 'spaces4', label: '4 spaces' },
      { value: 'spaces2', label: '2 spaces' },
      { value: 'tab', label: 'Tab' },
    ],
  },
  {
    key: 'showFileExtensions',
    label: 'Show file extensions',
    help: 'Show “.md” and the like in the file tree.',
  },
  {
    key: 'showHiddenFiles',
    label: 'Show hidden files',
    help: 'Show files and folders whose names start with a dot.',
  },
  { key: 'showTabBar', label: 'Show the tab bar', help: 'Show open notes as tabs above the editor.' },
];

export function indentUnit(s: Settings): string {
  return s.indent === 'tab' ? '\t' : s.indent === 'spaces2' ? '  ' : '    ';
}

/** Merge the layers, ignoring keys or values a hand-edited file got wrong (never throws). */
export function resolveSettings(...layers: readonly unknown[]): Settings {
  const out: Record<string, unknown> = { ...defaultSettings };
  for (const layer of layers) {
    for (const [k, v] of Object.entries(sanitize(layer))) out[k] = v;
  }
  return out as unknown as Settings;
}

/** The valid keys of `raw`, as a partial: unknown keys and wrong types are dropped. */
export function sanitize(raw: unknown): PartialSettings {
  if (!raw || typeof raw !== 'object') return {};
  const out: Record<string, unknown> = {};
  for (const field of settingFields) {
    const v = (raw as Record<string, unknown>)[field.key];
    if (v === undefined) continue;
    const ok = field.options
      ? field.options.some((o) => o.value === v)
      : typeof v === typeof defaultSettings[field.key];
    if (ok) out[field.key] = v;
  }
  return out as PartialSettings;
}

/** Where the user's own preferences live: localStorage in a browser, a config file on the desktop. */
export interface PrefsStorage {
  load(): PartialSettings;
  save(prefs: PartialSettings): void;
}

const PREFS_KEY = 'puffinote.prefs';

export const localPrefs: PrefsStorage = {
  load() {
    try {
      return sanitize(JSON.parse(localStorage.getItem(PREFS_KEY) ?? '{}'));
    } catch {
      return {};
    }
  },
  save(prefs) {
    try {
      localStorage.setItem(PREFS_KEY, JSON.stringify(prefs));
    } catch {
      // Storage disabled: preferences last until the page closes.
    }
  },
};

/** Preferences kept only in memory (tests, the demo). */
export function memoryPrefs(initial: PartialSettings = {}): PrefsStorage {
  let value = { ...initial };
  return {
    load: () => ({ ...value }),
    save: (p) => {
      value = { ...p };
    },
  };
}

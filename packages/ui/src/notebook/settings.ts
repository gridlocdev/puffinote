import type { ViewMode } from '../editor/reveal.ts';
import { parseJson } from './storeWrites.ts';

/**
 * Puffinote's settings (PRD §9). They resolve in layers, lowest first: the defaults here, the user's
 * preferences (every notebook), then the notebook's own overrides in `.puffinote/settings.json`, which travel
 * with the folder.
 */
export interface Settings {
  readonly theme: 'system' | 'light' | 'dark';
  /**
   * How the window is laid out (PRD §6): an icon rail with a floating panel and tabs, or a library of folders
   * beside a list of notes, one note at a time.
   */
  readonly layout: 'rail' | 'library';
  /** The colour of selections, active icons and highlights. */
  readonly accent: 'blue' | 'amber' | 'teal' | 'purple';
  /** The view a note opens in. */
  readonly defaultView: ViewMode;
  /** Save a note a moment after the last edit. */
  readonly autoSave: boolean;
  /** Move a task below the open ones in its list when it's ticked done, as Apple Notes does. */
  readonly sortCheckedTasks: boolean;
  /** What Tab inserts and how far a list item indents. */
  readonly indent: 'spaces2' | 'spaces4' | 'tab';
  /**
   * How a file tree row opens: one click (⌥-click selects), or two (one click selects, as in Finder).
   */
  readonly treeClick: 'single' | 'double';
  /** Show `.md` and other extensions in the file tree. */
  readonly showFileExtensions: boolean;
  /** Show files and folders whose names start with a dot. */
  readonly showHiddenFiles: boolean;
  /** Show the tab strip over the editor (PRD §9). */
  readonly showTabBar: boolean;
  /** Ask before a file Puffinote can't show opens in its default app. */
  readonly confirmOpenExternal: boolean;
  /** Where a pasted or dropped picture is saved (PRD §10.6): one folder for the notebook, or one per note. */
  readonly attachmentLocation: 'notebook' | 'note';
  /** The notebook-wide picture folder, from the notebook's top level. */
  readonly attachmentFolder: string;
  /** When a save drops the last link to a picture: offer to delete the file, or keep it quietly. */
  readonly unusedImages: 'ask' | 'keep';
}

export type SettingKey = keyof Settings;
export type PartialSettings = { -readonly [K in SettingKey]?: Settings[K] };

export const defaultSettings: Settings = {
  theme: 'system',
  layout: 'rail',
  accent: 'blue',
  defaultView: 'live',
  autoSave: true,
  sortCheckedTasks: true,
  indent: 'spaces4',
  treeClick: 'single',
  showFileExtensions: false,
  showHiddenFiles: false,
  showTabBar: true,
  confirmOpenExternal: true,
  attachmentLocation: 'notebook',
  attachmentFolder: 'attachments',
  unusedImages: 'ask',
};

/** One field in the Settings dialog: its label, help, and the values it takes. */
export interface SettingField<K extends SettingKey = SettingKey> {
  readonly key: K;
  readonly label: string;
  readonly help: string;
  readonly options?: readonly { readonly value: Settings[K]; readonly label: string }[];
  /** A free-text field; an empty value means the default (or, in a notebook, the preference). */
  readonly text?: boolean;
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
    key: 'layout',
    label: 'Layout',
    help: 'Icon rail: a floating panel beside tabbed notes. Library: folders, a list of notes, and one note.',
    options: [
      { value: 'rail', label: 'Icon rail' },
      { value: 'library', label: 'Library and note list' },
    ],
  },
  {
    key: 'accent',
    label: 'Accent colour',
    help: 'The colour of selections, active icons and highlights.',
    options: [
      { value: 'blue', label: 'Blue' },
      { value: 'amber', label: 'Amber' },
      { value: 'teal', label: 'Teal' },
      { value: 'purple', label: 'Purple' },
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
    key: 'sortCheckedTasks',
    label: 'Move ticked tasks to the bottom',
    help: 'When you tick a task done, move it below the unticked ones in its list. Unticking moves it back up.',
  },
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
    key: 'treeClick',
    label: 'Open notes and folders with',
    help: 'In the file tree. With a double click, one click selects a note or folder, so Enter renames it.',
    options: [
      { value: 'single', label: 'A single click' },
      { value: 'double', label: 'A double click' },
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
  {
    key: 'showTabBar',
    label: 'Show the tab bar',
    help: 'Show open notes as tabs above the editor (Icon rail layout).',
  },
  {
    key: 'confirmOpenExternal',
    label: 'Ask before opening other files',
    help: 'Ask before a file Puffinote can’t show, like a PDF, opens in its default app.',
  },
  {
    key: 'attachmentLocation',
    label: 'Save pasted pictures in',
    help: 'One folder for the whole notebook, or a folder beside each note named after it (“Note.assets”).',
    options: [
      { value: 'notebook', label: 'The notebook’s picture folder' },
      { value: 'note', label: 'A folder beside each note' },
    ],
  },
  {
    key: 'attachmentFolder',
    label: 'Picture folder',
    help: 'The notebook-wide folder for pasted pictures, from the notebook’s top level.',
    text: true,
  },
  {
    key: 'unusedImages',
    label: 'When a picture is no longer used',
    help: 'After you remove the last link to a picture, offer to delete the file, or keep it.',
    options: [
      { value: 'ask', label: 'Offer to delete it' },
      { value: 'keep', label: 'Keep it' },
    ],
  },
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

/** The settings whose values differ between `base` and `next`. */
export function changedSettings(base: PartialSettings, next: PartialSettings): string[] {
  const a = base as Record<string, unknown>;
  const b = next as Record<string, unknown>;
  return [...new Set([...Object.keys(a), ...Object.keys(b)])].filter(
    (k) => JSON.stringify(a[k]) !== JSON.stringify(b[k]),
  );
}

/**
 * `current` with `keys` set as `next` has them, and removed where `next` has none: what a dialog opened on one
 * version of the settings changed, applied over what they hold by now (PRD §9).
 */
export function withChanges<T extends object>(current: T, next: PartialSettings, keys: readonly string[]): T {
  const out = { ...current } as Record<string, unknown>;
  for (const k of keys) {
    const v = (next as Record<string, unknown>)[k];
    if (v === undefined) delete out[k];
    else out[k] = v;
  }
  return out as T;
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
    if (ok && !(field.text && (v as string).trim() === '')) out[field.key] = v;
  }
  return out as PartialSettings;
}

/**
 * The entries of `raw` that `sanitize` drops: keys this version doesn't know, or values it doesn't accept.
 * Writing them back keeps what a newer Puffinote stored, so opening the same notebook in an older one and
 * changing a setting doesn't erase it.
 */
export function unrecognized(raw: unknown): Record<string, unknown> {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return {};
  const kept = sanitize(raw) as Record<string, unknown>;
  return Object.fromEntries(Object.entries(raw).filter(([k]) => !(k in kept)));
}

/** Where the user's own preferences live: localStorage in a browser, a config file on the desktop. */
export interface PrefsStorage {
  load(): PartialSettings;
  /** Whether they were stored: with storage off, they last until the page closes. */
  save(prefs: PartialSettings): boolean;
}

const PREFS_KEY = 'puffinote.prefs';

export const localPrefs: PrefsStorage = {
  load() {
    return sanitize(parseJson(readStoredPrefs() ?? '{}'));
  },
  save(prefs) {
    // Keys this version doesn't know (a newer one's) are kept; an unreadable value has none to keep.
    const stored = parseJson(readStoredPrefs() ?? 'null');
    try {
      localStorage.setItem(PREFS_KEY, JSON.stringify({ ...unrecognized(stored), ...prefs }));
      return true;
    } catch {
      return false;
    }
  },
};

/** The stored preferences' text, or null when there are none or storage is off. */
function readStoredPrefs(): string | null {
  try {
    return localStorage.getItem(PREFS_KEY);
  } catch {
    return null;
  }
}

/** Preferences kept only in memory (tests, the demo). */
export function memoryPrefs(initial: PartialSettings = {}): PrefsStorage {
  let value = { ...initial };
  return {
    load: () => ({ ...value }),
    save: (p) => {
      value = { ...p };
      return true;
    },
  };
}

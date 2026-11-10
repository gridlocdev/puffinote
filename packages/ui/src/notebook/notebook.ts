import { computed, effect, signal, type Signal } from '@preact/signals';
import {
  encodeText,
  findAll,
  isReadOnlyDecode,
  readText,
  TextDocument,
  utf8NoBom,
  type TextFileFormat,
} from '@puffinote/text';
import { EditorState } from '../editor/editorState.ts';
import { ConflictError, NotFoundError, type NotebookStore } from '../platform/notebookStore.ts';
import {
  baseName,
  invalidNameReason,
  isWithin,
  joinPath,
  mimeOf,
  parentOf,
  rebase,
  resolveRelative,
} from '../platform/paths.ts';
import {
  indentUnit,
  resolveSettings,
  sanitize,
  type PartialSettings,
  type PrefsStorage,
  type Settings,
} from './settings.ts';
import {
  allFiles,
  ancestorsOf,
  emptyTree,
  fileKind,
  findNode,
  loadTree,
  stateDir,
  type TreeNode,
} from './tree.ts';

export type SidebarPanel = 'files' | 'filter' | 'search' | 'outline' | 'recent';

export const sidebarPanels: readonly { id: SidebarPanel; title: string; icon: string }[] = [
  { id: 'files', title: 'Files', icon: 'folder' },
  { id: 'filter', title: 'Filter', icon: 'filter' },
  { id: 'search', title: 'Search', icon: 'search' },
  { id: 'outline', title: 'Outline', icon: 'list' },
  { id: 'recent', title: 'Recent', icon: 'history' },
];

/** What changed on disk under an open tab that has unsaved edits (PRD §7.1). */
export type TabConflict = 'none' | 'changed' | 'deleted';

const AUTOSAVE_MS = 1000;
const SAVED_FLASH_MS = 3000;
const RECENT_LIMIT = 20;

/** One open document: a note in the editor, or a picture. */
let nextTabId = 0;

export class Tab {
  /** Stable for the tab's life, for keying its view. */
  readonly id = ++nextTabId;
  readonly path: Signal<string>;
  readonly kind: 'note' | 'image';
  /** The note's editor state; null for a picture. */
  readonly state: EditorState | null;
  /** Unsaved edits. */
  readonly dirty = signal(false);
  readonly conflict = signal<TabConflict>('none');
  /** Just saved: the title bar says "— Saved" for a moment (PRD §7.3). */
  readonly saved = signal(false);
  /** The encoding was guessed; saving would write the guess back, so the note is read-only. */
  readonly readOnly: boolean;
  /** A picture's URL. */
  readonly imageUrl: string | null;
  format: TextFileFormat;
  version: string | null;
  savedText: string;
  /** Where the editor was scrolled, restored when the tab comes back. */
  scrollTop = 0;
  private readonly disposers: (() => void)[] = [];

  constructor(
    path: string,
    init: {
      kind: 'note' | 'image';
      text?: string;
      format?: TextFileFormat;
      version: string | null;
      readOnly?: boolean;
      imageUrl?: string | null;
    },
  ) {
    this.path = signal(path);
    this.kind = init.kind;
    this.format = init.format ?? utf8NoBom;
    this.version = init.version;
    this.savedText = init.text ?? '';
    this.readOnly = init.readOnly ?? false;
    this.imageUrl = init.imageUrl ?? null;
    this.state = init.kind === 'note' ? new EditorState(this.savedText) : null;
    if (this.state) {
      const state = this.state;
      this.disposers.push(
        effect(() => {
          void state.version.value;
          const dirty = state.text !== this.savedText;
          this.dirty.value = dirty;
          if (dirty) this.saved.value = false;
        }),
      );
    }
  }

  get name(): string {
    return baseName(this.path.value);
  }

  /** Mark the current text as what's on disk. */
  markSaved(version: string | null): void {
    this.version = version;
    this.savedText = this.state?.text ?? '';
    this.dirty.value = false;
    this.conflict.value = 'none';
  }

  onDispose(fn: () => void): void {
    this.disposers.push(fn);
  }

  dispose(): void {
    for (const d of this.disposers) d();
    this.disposers.length = 0;
  }
}

/** A message for the status line: what just failed, or what needs the user. */
export interface Notice {
  readonly kind: 'info' | 'error';
  readonly text: string;
}

export interface SearchHit {
  readonly path: string;
  readonly line: number;
  /** Offset of the match in the file. */
  readonly offset: number;
  readonly length: number;
  /** The line, trimmed, with the match at `[matchStart, matchStart + length)`. */
  readonly preview: string;
  readonly matchStart: number;
}

/** What a notebook remembers between sessions, in `.puffinote/session.json` (PRD §7.1). */
export interface Session {
  readonly tabs: readonly { path: string; caret?: number; mode?: string }[];
  readonly active: string | null;
  readonly expanded: readonly string[];
  readonly recent: readonly string[];
  readonly sidebar: { collapsed: boolean; width: number; panel: SidebarPanel };
}

export const sessionPath = `${stateDir}/session.json`;
export const overridesPath = `${stateDir}/settings.json`;
/** Volatile state stays out of git; the notebook's settings travel with it (PRD §7.1). */
const gitignore = '# Puffinote: machine-local state\nsession.json\n';

export interface NotebookOptions {
  readonly prefs: PrefsStorage;
  /** Hand a file nobody here can open to the user (a download), when the store can't open it externally. */
  readonly download?: (name: string, bytes: Uint8Array, mime: string) => void;
}

/**
 * One open notebook (PRD §7.1): its file tree, open tabs, sidebar, recent files and settings, published as
 * signals for the shell to draw. All file changes go through here so tabs, the tree and the recent list stay
 * reconciled with the folder.
 */
export class Notebook {
  readonly store: NotebookStore;
  readonly tree = signal<TreeNode>(emptyTree);
  readonly expanded = signal<ReadonlySet<string>>(new Set());
  /** The tree's selected row (where New Note goes, what F2 renames). */
  readonly selected = signal<string | null>(null);
  readonly tabs = signal<readonly Tab[]>([]);
  readonly active = signal<Tab | null>(null);
  readonly sidebarCollapsed = signal(false);
  readonly sidebarWidth = signal(260);
  readonly panel = signal<SidebarPanel>('files');
  readonly recent = signal<readonly string[]>([]);
  readonly userPrefs: Signal<PartialSettings>;
  readonly overrides = signal<PartialSettings>({});
  readonly settings = computed<Settings>(() => resolveSettings(this.userPrefs.value, this.overrides.value));
  readonly notice = signal<Notice | null>(null);
  /** Bumps when an image a note shows has loaded, so the editor redraws it. */
  readonly imagesLoaded = signal(0);

  private readonly options: NotebookOptions;
  private readonly images = new Map<string, string | null>();
  private readonly disposers: (() => void)[] = [];
  private persistTimer: ReturnType<typeof setTimeout> | null = null;
  private refreshTimer: ReturnType<typeof setTimeout> | null = null;
  private loaded = false;

  private constructor(store: NotebookStore, options: NotebookOptions) {
    this.store = store;
    this.options = options;
    this.userPrefs = signal(options.prefs.load());
  }

  /** Open `store` as the notebook: read its settings, session and tree, and reopen its tabs. */
  static async open(store: NotebookStore, options: NotebookOptions): Promise<Notebook> {
    const nb = new Notebook(store, options);
    await nb.load();
    return nb;
  }

  private async load(): Promise<void> {
    this.overrides.value = sanitize(await this.readJson(overridesPath));
    const session = (await this.readJson(sessionPath)) as Partial<Session> | null;
    await this.reloadTree();
    if (session) {
      if (Array.isArray(session.expanded)) this.expanded.value = new Set(session.expanded.filter(isString));
      if (Array.isArray(session.recent)) this.recent.value = session.recent.filter(isString);
      const sb = session.sidebar;
      if (sb) {
        this.sidebarCollapsed.value = sb.collapsed === true;
        if (typeof sb.width === 'number') this.sidebarWidth.value = clampWidth(sb.width);
        if (typeof sb.panel === 'string') this.panel.value = sb.panel;
      }
      for (const t of session.tabs ?? []) {
        if (typeof t?.path !== 'string') continue;
        const tab = await this.openTab(t.path).catch(() => null);
        if (tab?.state) {
          if (t.mode === 'live' || t.mode === 'source' || t.mode === 'raw') tab.state.mode.value = t.mode;
          if (typeof t.caret === 'number') tab.state.select(Math.min(t.caret, tab.state.document.length));
        }
      }
      const active =
        this.tabs.value.find((t) => t.path.value === session.active) ?? this.tabs.value[0] ?? null;
      this.active.value = active;
    }
    this.loaded = true;

    // Remember the session whenever any of its parts changes.
    this.disposers.push(
      effect(() => {
        void this.tabs.value;
        void this.active.value;
        void this.expanded.value;
        void this.recent.value;
        void this.sidebarCollapsed.value;
        void this.sidebarWidth.value;
        void this.panel.value;
        this.schedulePersist();
      }),
    );
    // Settings reach the open tabs live (PRD §7.5).
    this.disposers.push(
      effect(() => {
        const unit = indentUnit(this.settings.value);
        for (const t of this.tabs.value) if (t.state) t.state.indentUnit = unit;
      }),
    );
    if (this.store.watch) this.disposers.push(this.store.watch(() => this.scheduleRefresh()));
    else if (typeof window !== 'undefined') {
      // No change notification (a browser folder, the NAS): look again whenever the window comes back.
      const onFocus = () => this.scheduleRefresh();
      window.addEventListener('focus', onFocus);
      this.disposers.push(() => window.removeEventListener('focus', onFocus));
    }
  }

  dispose(): void {
    for (const d of this.disposers) d();
    for (const t of this.tabs.value) t.dispose();
    if (this.persistTimer) clearTimeout(this.persistTimer);
    if (this.refreshTimer) clearTimeout(this.refreshTimer);
    for (const url of this.images.values()) if (url?.startsWith('blob:')) URL.revokeObjectURL(url);
  }

  get name(): string {
    return this.store.name;
  }

  get dirtyTabs(): Tab[] {
    return this.tabs.value.filter((t) => t.dirty.value);
  }

  // ----- Tree -----

  async reloadTree(): Promise<void> {
    this.tree.value = await loadTree(this.store, this.settings.peek().showHiddenFiles);
  }

  toggleFolder(path: string, open?: boolean): void {
    const next = new Set(this.expanded.value);
    const want = open ?? !next.has(path);
    if (want) next.add(path);
    else next.delete(path);
    this.expanded.value = next;
  }

  setExpanded(paths: Iterable<string>): void {
    this.expanded.value = new Set(paths);
  }

  /** Open the folders above `path` and select it, so the active file is a visible row. */
  reveal(path: string): void {
    const missing = ancestorsOf(path).filter((p) => !this.expanded.value.has(p));
    if (missing.length > 0) this.expanded.value = new Set([...this.expanded.value, ...missing]);
    this.selected.value = path;
  }

  /** Re-read the tree and reconcile every open tab against the disk (PRD §7.1). */
  async refresh(): Promise<void> {
    try {
      await this.reloadTree();
    } catch (e) {
      this.report(e);
      return;
    }
    for (const tab of this.tabs.value) {
      if (tab.kind !== 'note') continue;
      try {
        const data = await this.store.read(tab.path.value);
        if (data.version !== null && data.version === tab.version) {
          if (tab.conflict.value === 'deleted') tab.conflict.value = 'none';
          continue;
        }
        const text = readText(data.bytes).text;
        if (text === tab.savedText) {
          tab.version = data.version;
          continue;
        }
        if (tab.dirty.value) tab.conflict.value = 'changed';
        else this.replaceText(tab, text, data.version);
      } catch (e) {
        if (e instanceof NotFoundError) tab.conflict.value = 'deleted';
      }
    }
  }

  scheduleRefresh(): void {
    if (this.refreshTimer) clearTimeout(this.refreshTimer);
    this.refreshTimer = setTimeout(() => {
      this.refreshTimer = null;
      void this.refresh();
    }, 150);
  }

  // ----- Tabs -----

  /**
   * Open `path`: a note or picture into a tab (or the tab it already has), anything else in its default app
   * or as a download. Returns the tab, or null when the file opened elsewhere.
   */
  async openFile(path: string): Promise<Tab | null> {
    const kind = fileKind(path);
    if (kind === 'other') {
      await this.openElsewhere(path);
      return null;
    }
    try {
      const tab = this.tabs.value.find((t) => t.path.value === path) ?? (await this.openTab(path));
      this.active.value = tab;
      this.touchRecent(path);
      return tab;
    } catch (e) {
      this.report(e);
      return null;
    }
  }

  private async openTab(path: string): Promise<Tab> {
    const kind = fileKind(path);
    const data = await this.store.read(path);
    let tab: Tab;
    if (kind === 'image') {
      const url = this.store.fileUrl(path);
      const blob = url
        ? null
        : URL.createObjectURL(new Blob([data.bytes as Uint8Array<ArrayBuffer>], { type: mimeOf(path) }));
      tab = new Tab(path, { kind: 'image', version: data.version, imageUrl: url ?? blob });
      if (blob) tab.onDispose(() => URL.revokeObjectURL(blob));
    } else {
      const decoded = readText(data.bytes);
      tab = new Tab(path, {
        kind: 'note',
        text: decoded.text,
        format: decoded.format,
        version: data.version,
        readOnly: isReadOnlyDecode(decoded),
      });
      tab.state!.mode.value = this.settings.peek().defaultView;
      tab.state!.indentUnit = indentUnit(this.settings.peek());
      this.watchAutosave(tab);
    }
    this.tabs.value = [...this.tabs.value, tab];
    return tab;
  }

  private watchAutosave(tab: Tab): void {
    let timer: ReturnType<typeof setTimeout> | null = null;
    tab.onDispose(
      effect(() => {
        void tab.state!.version.value;
        const go =
          tab.dirty.value && tab.conflict.value === 'none' && this.settings.value.autoSave && !tab.readOnly;
        if (timer) clearTimeout(timer);
        timer = go
          ? setTimeout(() => {
              timer = null;
              if (tab.dirty.peek() && this.tabs.peek().includes(tab)) void this.save(tab);
            }, AUTOSAVE_MS)
          : null;
      }),
    );
    tab.onDispose(() => {
      if (timer) clearTimeout(timer);
    });
  }

  activate(tab: Tab | null): void {
    this.active.value = tab;
    if (tab) this.touchRecent(tab.path.value);
  }

  /** Close `tab`, discarding unsaved edits: the shell asks first. */
  closeTab(tab: Tab): void {
    const tabs = this.tabs.value;
    const i = tabs.indexOf(tab);
    if (i < 0) return;
    const next = tabs.filter((t) => t !== tab);
    this.tabs.value = next;
    if (this.active.value === tab) this.active.value = next[Math.min(i, next.length - 1)] ?? null;
    tab.dispose();
  }

  /** Step the active tab by `delta`, wrapping. */
  cycleTab(delta: number): void {
    const tabs = this.tabs.value;
    if (tabs.length === 0) return;
    const i = this.active.value ? tabs.indexOf(this.active.value) : -1;
    this.activate(tabs[(i + delta + tabs.length) % tabs.length]!);
  }

  moveTab(tab: Tab, to: number): void {
    const tabs = this.tabs.value.filter((t) => t !== tab);
    tabs.splice(Math.max(0, Math.min(to, tabs.length)), 0, tab);
    this.tabs.value = tabs;
  }

  // ----- Saving -----

  /**
   * Save `tab`. A file changed on disk since it was read is a conflict: the tab is flagged and nothing is
   * written unless `force`. Returns whether it saved.
   */
  async save(tab: Tab, force = false): Promise<boolean> {
    if (!tab.state || tab.readOnly) return false;
    if (!force && tab.conflict.value !== 'none') return false;
    const text = tab.state.text;
    try {
      const version = await this.store.write(tab.path.value, encodeText(text, tab.format), {
        expected: tab.version,
        force,
      });
      // Edits made while the write was in flight stay unsaved.
      tab.version = version;
      tab.savedText = text;
      tab.dirty.value = tab.state.text !== text;
      tab.conflict.value = 'none';
      this.flashSaved(tab);
      return true;
    } catch (e) {
      if (e instanceof ConflictError && e.reason === 'changed') {
        tab.conflict.value = 'changed';
        return false;
      }
      this.report(e);
      return false;
    }
  }

  async saveAll(): Promise<boolean> {
    let ok = true;
    for (const t of this.dirtyTabs) ok = (await this.save(t)) && ok;
    return ok;
  }

  private flashSaved(tab: Tab): void {
    tab.saved.value = true;
    setTimeout(() => {
      if (!tab.dirty.peek()) tab.saved.value = false;
    }, SAVED_FLASH_MS);
  }

  /** Take the disk's version into `tab`, as one undoable step (PRD §7.1). */
  async reload(tab: Tab): Promise<void> {
    try {
      const data = await this.store.read(tab.path.value);
      this.replaceText(tab, readText(data.bytes).text, data.version);
    } catch (e) {
      this.report(e);
    }
  }

  private replaceText(tab: Tab, text: string, version: string | null): void {
    const state = tab.state;
    if (!state) return;
    const caret = Math.min(state.selection.peek().caret, text.length);
    const old = state.document;
    // The smallest replacement, so the caret and undo history keep their footing.
    let p = 0;
    const oldText = old.getText();
    const max = Math.min(oldText.length, text.length);
    while (p < max && oldText.charCodeAt(p) === text.charCodeAt(p)) p++;
    let s = 0;
    while (s < max - p && oldText.charCodeAt(oldText.length - 1 - s) === text.charCodeAt(text.length - 1 - s))
      s++;
    if (p !== oldText.length || p !== text.length) {
      state.apply({
        start: p,
        length: oldText.length - p - s,
        text: text.slice(p, text.length - s),
        anchor: caret,
        caret,
      });
    }
    tab.savedText = text;
    tab.markSaved(version);
  }

  // ----- Files and folders -----

  /** Create a note in `dir` and open it. `name` may leave off `.md`. Returns the path. */
  async createNote(dir: string, name: string): Promise<string | null> {
    const file = /\.[A-Za-z0-9]+$/.test(name) ? name : `${name}.md`;
    const path = joinPath(dir, file);
    if (!this.checkName(file)) return null;
    try {
      await this.store.write(path, new Uint8Array(), { create: true });
    } catch (e) {
      this.report(e);
      return null;
    }
    if (dir) this.toggleFolder(dir, true);
    await this.reloadTree();
    this.selected.value = path;
    await this.openFile(path);
    return path;
  }

  async createFolder(dir: string, name: string): Promise<string | null> {
    if (!this.checkName(name)) return null;
    const path = joinPath(dir, name);
    if (findNode(this.tree.value, path)) {
      this.notify('error', `“${name}” already exists there.`);
      return null;
    }
    try {
      await this.store.mkdir(path);
    } catch (e) {
      this.report(e);
      return null;
    }
    if (dir) this.toggleFolder(dir, true);
    await this.reloadTree();
    this.selected.value = path;
    return path;
  }

  /** Rename a file or folder in place. Returns the new path. */
  async rename(path: string, name: string): Promise<string | null> {
    if (name === baseName(path)) return path;
    if (!this.checkName(name)) return null;
    return this.relocate(path, joinPath(parentOf(path), name));
  }

  /**
   * Move a file or folder into `dir` (PRD §7.2: a plain move; links aren't rewritten). Moving into its own
   * folder or into itself does nothing. Returns the new path.
   */
  async move(path: string, dir: string): Promise<string | null> {
    if (parentOf(path) === dir || isWithin(dir, path)) return null;
    return this.relocate(path, joinPath(dir, baseName(path)));
  }

  /** Whether moving `path` into `dir` would do anything (the drag indicator shows only then). */
  canMove(path: string, dir: string): boolean {
    if (parentOf(path) === dir || isWithin(dir, path)) return false;
    return !findNode(this.tree.value, joinPath(dir, baseName(path)));
  }

  private async relocate(from: string, to: string): Promise<string | null> {
    try {
      await this.store.move(from, to);
    } catch (e) {
      this.report(e);
      return null;
    }
    for (const t of this.tabs.value) {
      if (isWithin(t.path.value, from)) t.path.value = rebase(t.path.value, from, to);
    }
    this.tabs.value = [...this.tabs.value];
    this.recent.value = this.recent.value.map((p) => (isWithin(p, from) ? rebase(p, from, to) : p));
    this.expanded.value = new Set(
      [...this.expanded.value]
        .map((p) => (isWithin(p, from) ? rebase(p, from, to) : p))
        .concat(parentOf(to) || []),
    );
    if (this.selected.value && isWithin(this.selected.value, from)) {
      this.selected.value = rebase(this.selected.value, from, to);
    }
    await this.reloadTree();
    return to;
  }

  /** Delete a file or folder (the shell confirms first). Its tabs close without saving. */
  async remove(path: string): Promise<boolean> {
    try {
      await this.store.remove(path);
    } catch (e) {
      this.report(e);
      return false;
    }
    for (const t of [...this.tabs.value]) if (isWithin(t.path.value, path)) this.closeTab(t);
    this.recent.value = this.recent.value.filter((p) => !isWithin(p, path));
    if (this.selected.value && isWithin(this.selected.value, path)) this.selected.value = null;
    await this.reloadTree();
    return true;
  }

  private async openElsewhere(path: string): Promise<void> {
    try {
      if (this.store.openExternal) {
        await this.store.openExternal(path);
        return;
      }
      const { bytes } = await this.store.read(path);
      if (this.options.download) this.options.download(baseName(path), bytes, mimeOf(path));
      else this.notify('info', `Puffinote can’t open “${baseName(path)}”.`);
    } catch (e) {
      this.report(e);
    }
  }

  private checkName(name: string): boolean {
    const reason = invalidNameReason(name);
    if (reason) this.notify('error', reason);
    return reason === null;
  }

  // ----- Recent, search, images -----

  private touchRecent(path: string): void {
    const next = [path, ...this.recent.value.filter((p) => p !== path)].slice(0, RECENT_LIMIT);
    if (next.join('\n') !== this.recent.value.join('\n')) this.recent.value = next;
  }

  /** Full-text search across the notebook's notes (PRD §7.2.3), open tabs' unsaved text included. */
  async search(query: string, opts: { caseSensitive?: boolean; regex?: boolean } = {}): Promise<SearchHit[]> {
    if (!query.trim()) return [];
    const hits: SearchHit[] = [];
    const open = new Map(this.tabs.value.filter((t) => t.state).map((t) => [t.path.value, t.state!]));
    for (const file of allFiles(this.tree.value)) {
      if (fileKind(file.path) !== 'note') continue;
      let text: string;
      try {
        text = open.get(file.path)?.text ?? readText((await this.store.read(file.path)).bytes).text;
      } catch {
        continue;
      }
      const matches = findAll(text, query, {
        caseSensitive: opts.caseSensitive ?? false,
        regex: opts.regex ?? false,
      });
      if (matches.length === 0) continue;
      const doc = TextDocument.fromString(text);
      for (const m of matches.slice(0, 50)) {
        const pos = doc.getPosition(m.start);
        const raw = doc.getLineText(pos.line);
        const lead = raw.length - raw.trimStart().length;
        const cut = Math.max(lead, pos.column - 40);
        const preview = (cut > lead ? '…' : '') + raw.slice(cut).trimEnd();
        hits.push({
          path: file.path,
          line: pos.line,
          offset: m.start,
          length: m.length,
          preview,
          matchStart: pos.column - cut + (cut > lead ? 1 : 0),
        });
        if (hits.length >= 500) return hits;
      }
    }
    return hits;
  }

  /**
   * The URL for an image a note at `notePath` refers to: web and data URLs as they are, notebook files
   * straight from the store, or read once into a blob URL (the editor redraws when it arrives).
   */
  resolveImage(notePath: string, ref: string): string {
    if (/^(https?:|data:|blob:)/i.test(ref)) return ref;
    const path = resolveRelative(notePath, ref);
    if (path === null) return '';
    const direct = this.store.fileUrl(path);
    if (direct) return direct;
    const cached = this.images.get(path);
    if (cached !== undefined) return cached ?? '';
    this.images.set(path, null);
    void this.store
      .read(path)
      .then(({ bytes }) => {
        this.images.set(
          path,
          URL.createObjectURL(new Blob([bytes as Uint8Array<ArrayBuffer>], { type: mimeOf(path) })),
        );
        this.imagesLoaded.value++;
      })
      .catch(() => undefined);
    return '';
  }

  // ----- Settings -----

  /** Change the user's preferences (every notebook). */
  setPreferences(prefs: PartialSettings): void {
    const clean = sanitize(prefs);
    this.options.prefs.save(clean);
    this.userPrefs.value = clean;
  }

  /** Change this notebook's overrides, saved in `.puffinote/settings.json`. Written only when they changed. */
  async setOverrides(overrides: PartialSettings): Promise<void> {
    const clean = sanitize(overrides);
    if (JSON.stringify(clean) === JSON.stringify(this.overrides.value)) return;
    const hiddenBefore = this.settings.peek().showHiddenFiles;
    this.overrides.value = clean;
    try {
      await this.ensureStateDir();
      await this.store.write(overridesPath, encodeJson(clean), { force: true });
    } catch (e) {
      this.report(e);
    }
    if (this.settings.peek().showHiddenFiles !== hiddenBefore) await this.reloadTree();
  }

  // ----- Session -----

  session(): Session {
    return {
      tabs: this.tabs.value.map((t) => ({
        path: t.path.value,
        ...(t.state ? { caret: t.state.selection.value.caret, mode: t.state.mode.value } : {}),
      })),
      active: this.active.value?.path.value ?? null,
      expanded: [...this.expanded.value],
      recent: this.recent.value,
      sidebar: {
        collapsed: this.sidebarCollapsed.value,
        width: this.sidebarWidth.value,
        panel: this.panel.value,
      },
    };
  }

  private schedulePersist(): void {
    if (!this.loaded) return;
    if (this.persistTimer) clearTimeout(this.persistTimer);
    this.persistTimer = setTimeout(() => {
      this.persistTimer = null;
      void this.persist();
    }, 500);
  }

  /** Write the session now (before closing the notebook). */
  async persist(): Promise<void> {
    try {
      await this.ensureStateDir();
      await this.store.write(sessionPath, encodeJson(this.session()), { force: true });
    } catch {
      // A read-only notebook just doesn't remember its tabs.
    }
  }

  private async ensureStateDir(): Promise<void> {
    try {
      await this.store.write(`${stateDir}/.gitignore`, new TextEncoder().encode(gitignore), { create: true });
    } catch (e) {
      if (!(e instanceof ConflictError)) throw e;
    }
  }

  private async readJson(path: string): Promise<unknown> {
    try {
      return JSON.parse(readText((await this.store.read(path)).bytes).text) as unknown;
    } catch {
      return null;
    }
  }

  // ----- Notices -----

  notify(kind: Notice['kind'], text: string): void {
    this.notice.value = { kind, text };
  }

  report(e: unknown): void {
    this.notify('error', e instanceof Error ? e.message : String(e));
  }
}

function encodeJson(value: unknown): Uint8Array {
  return new TextEncoder().encode(JSON.stringify(value, null, 2) + '\n');
}

function isString(v: unknown): v is string {
  return typeof v === 'string';
}

export function clampWidth(w: number): number {
  return Math.max(180, Math.min(560, Math.round(w)));
}

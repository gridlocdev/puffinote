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
import {
  applyRefEdits,
  attachmentDir,
  attachmentName,
  imageMarkdown,
  imageTargets,
  isImageMime,
  relinkEdits,
  type RefEdit,
} from './attachments.ts';
import { NoteMetaCache } from './noteMeta.ts';
import {
  ConflictError,
  NotFoundError,
  type Entry,
  type FileData,
  type NotebookStore,
} from '../platform/notebookStore.ts';
import {
  baseName,
  stemOf,
  invalidNameReason,
  isImagePath,
  isWithin,
  joinPath,
  mimeOf,
  parentOf,
  rebase,
  resolveRelative,
  uniqueName,
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
  noteFileName,
  stateDir,
  type FileStat,
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

/** How the Library layout's note list is ordered. */
export type NoteSort = 'edited' | 'title' | 'created';

/** What the Library layout's note list shows: every note, the pinned ones, recent ones, or one folder's. */
export type LibraryScope = 'all' | 'pinned' | 'recent' | `folder:${string}`;

/** What changed on disk under an open tab that has unsaved edits (PRD §5). */
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
  /** Just saved: the title bar says "— Saved" for a moment (PRD §7). */
  readonly saved = signal(false);
  /** The encoding was guessed; saving would write the guess back, so the note is read-only. */
  readonly readOnly: boolean;
  /** A picture's URL. */
  readonly imageUrl: string | null;
  format: TextFileFormat;
  version: string | null;
  savedText: string;
  /**
   * Bumps whenever `version` or `savedText` changes (a save, a reload, a move), so a refresh that read the
   * disk before the change can tell its answer is stale and drop it.
   */
  generation = 0;
  /** Where the editor was scrolled, restored when the tab comes back. */
  scrollTop = 0;
  /** Kept at the front of the strip and in view (PRD §7); Close Other and Close All leave it open. */
  readonly pinned = signal(false);
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
    this.generation++;
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
  /** A button beside the text (Move to Trash for a picture no note uses). */
  readonly action?: { readonly label: string; readonly run: () => void };
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
  /** The match is in the note's name (`preview` is the file name); opening it doesn't select anything. */
  readonly inTitle?: boolean;
}

/** `tabs` with the pinned ones moved to the front, each group keeping its order. */
function pinnedFirst(tabs: readonly Tab[]): Tab[] {
  return [...tabs.filter((t) => t.pinned.peek()), ...tabs.filter((t) => !t.pinned.peek())];
}

/** What a notebook remembers between sessions, in `.puffinote/session.json` (PRD §5). */
export interface Session {
  readonly tabs: readonly { path: string; caret?: number; mode?: string; pinned?: boolean }[];
  readonly active: string | null;
  readonly expanded: readonly string[];
  readonly recent: readonly string[];
  readonly sidebar: { collapsed: boolean; width: number; panel: SidebarPanel };
  /** Notes pinned to the top of the Library layout's list. */
  readonly pinned?: readonly string[];
  readonly library?: { scope: LibraryScope; sort: NoteSort };
}

export const sessionPath = `${stateDir}/session.json`;
export const overridesPath = `${stateDir}/settings.json`;
/** Volatile state stays out of git; the notebook's settings travel with it (PRD §5). */
const gitignore = '# Puffinote: machine-local state\nsession.json\n';

export interface NotebookOptions {
  readonly prefs: PrefsStorage;
  /** Hand a file nobody here can open to the user (a download), when the store can't open it externally. */
  readonly download?: (name: string, bytes: Uint8Array, mime: string) => void;
  /** Ask whether to open a file in its default app; true opens it. Without it, files open straight away. */
  readonly confirmOpenExternal?: (name: string) => Promise<boolean>;
}

/**
 * One open notebook (PRD §5): its file tree, open tabs, sidebar, recent files and settings, published as
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
  /** Each file's size and times from the last tree read, by path. */
  readonly stats = signal<ReadonlyMap<string, FileStat>>(new Map());
  readonly pinned = signal<readonly string[]>([]);
  readonly sort = signal<NoteSort>('edited');
  readonly scope = signal<LibraryScope>('folder:');
  readonly userPrefs: Signal<PartialSettings>;
  readonly overrides = signal<PartialSettings>({});
  readonly settings = computed<Settings>(() => resolveSettings(this.userPrefs.value, this.overrides.value));
  readonly notice = signal<Notice | null>(null);
  /** Bumps when an image a note shows has loaded, so the editor redraws it. */
  readonly imagesLoaded = signal(0);
  /** Excerpts and picture sizes for the note list, read as it asks. */
  readonly meta: NoteMetaCache;

  private readonly options: NotebookOptions;
  private readonly images = new Map<string, string | null>();
  private readonly disposers: (() => void)[] = [];
  private persistTimer: ReturnType<typeof setTimeout> | null = null;
  private refreshTimer: ReturnType<typeof setTimeout> | null = null;
  private loaded = false;
  /** Each tab's saves run one at a time (see `save`). */
  private readonly saveSlots = new WeakMap<Tab, SaveSlot>();
  /** Tabs with a write to the store in flight. */
  private readonly writing = new Set<Tab>();
  /** Session writes, one after another, so an older one never lands last. */
  private persistChain: Promise<void> = Promise.resolve();

  private constructor(store: NotebookStore, options: NotebookOptions) {
    this.store = store;
    this.options = options;
    this.userPrefs = signal(options.prefs.load());
    this.meta = new NoteMetaCache(store);
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
      if (Array.isArray(session.pinned)) this.pinned.value = session.pinned.filter(isString);
      const lib = session.library;
      if (lib) {
        if (lib.sort === 'edited' || lib.sort === 'title' || lib.sort === 'created')
          this.sort.value = lib.sort;
        const scope = lib.scope as unknown;
        if (scope === 'all' || scope === 'pinned' || scope === 'recent') this.scope.value = scope;
        else if (typeof scope === 'string' && scope.startsWith('folder:')) {
          const dir = scope.slice('folder:'.length);
          if (findNode(this.tree.peek(), dir)?.kind === 'dir') this.scope.value = `folder:${dir}`;
        }
      }
      const sb = session.sidebar;
      if (sb) {
        this.sidebarCollapsed.value = sb.collapsed === true;
        if (typeof sb.width === 'number') this.sidebarWidth.value = clampWidth(sb.width);
        if (typeof sb.panel === 'string') this.panel.value = sb.panel;
      }
      for (const t of session.tabs ?? []) {
        if (typeof t?.path !== 'string') continue;
        const tab = await this.openTab(t.path).catch(() => null);
        if (tab && t.pinned === true) this.setTabPinned(tab, true);
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
        void this.pinned.value;
        void this.sort.value;
        void this.scope.value;
        void this.sidebarCollapsed.value;
        void this.sidebarWidth.value;
        void this.panel.value;
        this.schedulePersist();
      }),
    );
    // Settings reach the open tabs live (PRD §9).
    this.disposers.push(
      effect(() => {
        const unit = indentUnit(this.settings.value);
        for (const t of this.tabs.value) if (t.state) t.state.indentUnit = unit;
      }),
    );
    // Show Hidden Files changes the tree, from whichever layer it comes (PRD §6, §9).
    let hidden = this.settings.peek().showHiddenFiles;
    this.disposers.push(
      effect(() => {
        const now = this.settings.value.showHiddenFiles;
        if (now === hidden) return;
        hidden = now;
        void this.reloadTree().catch((e: unknown) => this.report(e));
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
    // A session write still waiting on its debounce goes out now, before the tabs it describes go away.
    if (this.persistTimer) {
      clearTimeout(this.persistTimer);
      this.persistTimer = null;
      void this.persist();
    }
    for (const d of this.disposers) d();
    this.meta.dispose();
    for (const t of this.tabs.value) t.dispose();
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

  /** Re-read the tree. The signal keeps its old value when nothing changed, so a refresh redraws nothing. */
  async reloadTree(): Promise<void> {
    const stats = new Map<string, FileStat>();
    const next = await loadTree(this.store, this.settings.peek().showHiddenFiles, stats);
    if (!sameTree(this.tree.peek(), next)) this.tree.value = next;
    if (!sameStats(this.stats.peek(), stats)) this.stats.value = stats;
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

  /** Re-read the tree and reconcile every open tab against the disk (PRD §5). */
  async refresh(): Promise<void> {
    try {
      await this.reloadTree();
    } catch (e) {
      this.report(e);
      return;
    }
    for (const tab of this.tabs.value) {
      if (tab.kind !== 'note') continue;
      // A save in flight is about to move the tab on: its answer, not this read, says what's on disk.
      if (this.writing.has(tab)) continue;
      const path = tab.path.value;
      const generation = tab.generation;
      // While the read is out, a save, reload or move may land. Then this answer is stale: drop it, so a
      // clean tab never goes back to the text it had before the save.
      const stale = () =>
        tab.generation !== generation ||
        tab.path.value !== path ||
        this.writing.has(tab) ||
        !this.tabs.peek().includes(tab);
      let data: FileData;
      try {
        data = await this.store.read(path);
      } catch (e) {
        if (e instanceof NotFoundError && !stale()) tab.conflict.value = 'deleted';
        continue;
      }
      if (stale()) continue;
      if (data.version !== null && data.version === tab.version) {
        if (tab.conflict.value === 'deleted') tab.conflict.value = 'none';
        continue;
      }
      const text = readText(data.bytes).text;
      if (text === tab.savedText) {
        // Touched, or deleted and put back, with the same text: nothing to resolve.
        tab.version = data.version;
        tab.generation++;
        if (tab.conflict.value === 'deleted') tab.conflict.value = 'none';
        continue;
      }
      if (tab.dirty.value) tab.conflict.value = 'changed';
      else this.replaceText(tab, text, data.version);
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
    this.tabs.value = pinnedFirst(tabs);
  }

  /** Pin or unpin `tab` (PRD §7): a pinned tab joins the end of the pinned ones; an unpinned one leads the rest. */
  setTabPinned(tab: Tab, pinned: boolean): void {
    const tabs = this.tabs.value;
    if (!tabs.includes(tab) || tab.pinned.peek() === pinned) return;
    tab.pinned.value = pinned;
    const others = tabs.filter((t) => t !== tab);
    const at = others.filter((t) => t.pinned.peek()).length;
    others.splice(at, 0, tab);
    this.tabs.value = others;
  }

  // ----- Saving -----

  /**
   * Save `tab`. A file changed on disk since it was read is a conflict: the tab is flagged and nothing is
   * written unless `force`. A tab without unsaved edits isn't written unless `force`. Returns whether the
   * tab's text is now on disk.
   *
   * A tab's saves run one at a time: a save asked for while one is in flight waits for it, then writes the
   * text as it is by then, against the version that save got back. Saves asked for while one already waits
   * share it.
   */
  save(tab: Tab, force = false): Promise<boolean> {
    let slot = this.saveSlots.get(tab);
    if (!slot) {
      slot = { queued: null, force: false, tail: Promise.resolve(true) };
      this.saveSlots.set(tab, slot);
    }
    slot.force ||= force;
    if (slot.queued) return slot.queued;
    const s = slot;
    const run = s.tail.then(() => {
      s.queued = null;
      const f = s.force;
      s.force = false;
      return this.writeTab(tab, f);
    });
    s.queued = run;
    s.tail = run;
    return run;
  }

  private async writeTab(tab: Tab, force: boolean): Promise<boolean> {
    if (!tab.state || tab.readOnly || !this.tabs.peek().includes(tab)) return false;
    if (!force && tab.conflict.peek() !== 'none') return false;
    if (!force && !tab.dirty.peek()) return true;
    const text = tab.state.text;
    const path = tab.path.peek();
    const before = tab.savedText;
    this.writing.add(tab);
    try {
      const version = await this.store.write(path, encodeText(text, tab.format), {
        expected: tab.version,
        force,
      });
      // Edits made while the write was in flight stay unsaved.
      tab.version = version;
      tab.savedText = text;
      tab.generation++;
      tab.dirty.value = tab.state.text !== text;
      tab.conflict.value = 'none';
      const stat = this.stats.peek().get(path);
      const now = Date.now();
      this.stats.value = new Map(this.stats.peek()).set(path, {
        size: text.length,
        modified: now,
        created: stat?.created || now,
      });
      this.flashSaved(tab);
      if (before.includes('](')) {
        const now = imageTargets(text, path);
        const dropped = [...imageTargets(before, path)].filter((p) => !now.has(p));
        if (dropped.length > 0) void this.offerUnused(dropped);
      }
      return true;
    } catch (e) {
      if (e instanceof ConflictError && (e.reason === 'changed' || e.reason === 'deleted')) {
        tab.conflict.value = e.reason === 'deleted' ? 'deleted' : await this.changedOrDeleted(path);
        return false;
      }
      this.report(e);
      return false;
    } finally {
      this.writing.delete(tab);
    }
  }

  /** The NAS and the desktop answer a stale write to a deleted file as "changed": look to tell them apart. */
  private async changedOrDeleted(path: string): Promise<TabConflict> {
    try {
      await this.store.read(path);
      return 'changed';
    } catch (e) {
      return e instanceof NotFoundError ? 'deleted' : 'changed';
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

  /** Take the disk's version into `tab`, as one undoable step (PRD §5). */
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
    tab.markSaved(version);
  }

  // ----- Files and folders -----

  /** Create a note in `dir` and open it. `name` may leave off `.md` (see `noteFileName`). Returns the path. */
  async createNote(dir: string, name: string): Promise<string | null> {
    const file = noteFileName(name);
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
   * Move a file or folder into `dir` (PRD §6: a plain move; links aren't rewritten). Moving into its own
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
    const node = findNode(this.tree.peek(), from);
    const movedFiles = !node ? [] : node.kind === 'file' ? [from] : allFiles(node).map((f) => f.path);
    try {
      await this.store.move(from, to);
    } catch (e) {
      this.report(e);
      return null;
    }
    const moved = this.tabs.value.filter((t) => isWithin(t.path.value, from));
    for (const t of moved) t.path.value = rebase(t.path.value, from, to);
    this.tabs.value = [...this.tabs.value];
    await Promise.all(moved.map((t) => this.adoptMovedVersion(t)));
    this.recent.value = this.recent.value.map((p) => (isWithin(p, from) ? rebase(p, from, to) : p));
    if (this.pinned.value.some((p) => isWithin(p, from))) {
      this.pinned.value = this.pinned.value.map((p) => (isWithin(p, from) ? rebase(p, from, to) : p));
    }
    const scope = this.scope.value;
    if (scope.startsWith('folder:') && isWithin(scope.slice(7), from) && scope !== 'folder:') {
      this.scope.value = `folder:${rebase(scope.slice(7), from, to)}`;
    }
    this.expanded.value = new Set(
      [...this.expanded.value]
        .map((p) => (isWithin(p, from) ? rebase(p, from, to) : p))
        .concat(parentOf(to) || []),
    );
    if (this.selected.value && isWithin(this.selected.value, from)) {
      this.selected.value = rebase(this.selected.value, from, to);
    }
    await this.reloadTree();
    await this.relinkPictures(from, to, movedFiles);
    return to;
  }

  /**
   * Keep picture links right after `from` moved to `to` (PRD §6.1): a moved note's links to pictures that
   * stayed put, and every note's links to pictures that moved. Pictures never move with a note.
   */
  private async relinkPictures(from: string, to: string, moved: readonly string[]): Promise<void> {
    const pictureMoved = moved.some(isImagePath);
    const notes = pictureMoved
      ? allFiles(this.tree.peek())
          .map((f) => f.path)
          .filter((p) => fileKind(p) === 'note')
      : moved.filter((p) => fileKind(p) === 'note').map((p) => rebase(p, from, to));
    const follow = (p: string) => (isWithin(p, from) ? rebase(p, from, to) : p);
    let changed = 0;
    for (const now of notes) {
      const was = isWithin(now, to) ? rebase(now, to, from) : now;
      if (await this.editNote(now, (text) => relinkEdits(text, was, now, follow))) changed++;
    }
    if (changed > 0) {
      this.notify('info', `Updated picture links in ${changed} ${changed === 1 ? 'note' : 'notes'}.`);
    }
  }

  /**
   * Apply `edits` to the note at `path`: in its tab when it is open (one undo step each, saved straight
   * away when the tab had nothing unsaved), on disk otherwise. True when anything changed.
   */
  private async editNote(path: string, edits: (text: string) => RefEdit[]): Promise<boolean> {
    const tab = this.tabs.peek().find((t) => t.state && t.path.peek() === path);
    if (tab?.state) {
      const list = edits(tab.state.text);
      if (list.length === 0) return false;
      const clean = !tab.dirty.peek();
      let caret = tab.state.selection.peek().caret;
      for (const e of [...list].reverse()) {
        if (caret >= e.start + e.length) caret += e.text.length - e.length;
        else if (caret > e.start) caret = e.start;
      }
      for (const e of [...list].reverse()) {
        tab.state.apply({ start: e.start, length: e.length, text: e.text, anchor: e.start, caret: e.start });
      }
      tab.state.select(caret);
      if (clean && !tab.readOnly) await this.save(tab);
      return true;
    }
    try {
      const data = await this.store.read(path);
      const decoded = readText(data.bytes);
      if (isReadOnlyDecode(decoded)) return false;
      const list = edits(decoded.text);
      if (list.length === 0) return false;
      await this.store.write(path, encodeText(applyRefEdits(decoded.text, list), decoded.format), {
        expected: data.version,
      });
      return true;
    } catch (e) {
      this.report(e);
      return false;
    }
  }

  /**
   * A move can give the file a new version token (the browser store copies, then deletes). Take the moved
   * file's version when its text is still what the tab last saved, so the next save isn't a false conflict.
   * A file that changed on disk before the move keeps the old token and conflicts as it should.
   */
  private async adoptMovedVersion(tab: Tab): Promise<void> {
    if (tab.kind !== 'note' || this.writing.has(tab)) return;
    const generation = tab.generation;
    try {
      const data = await this.store.read(tab.path.value);
      if (tab.generation !== generation || this.writing.has(tab) || data.version === tab.version) return;
      if (readText(data.bytes).text !== tab.savedText) return;
      tab.version = data.version;
      tab.generation++;
    } catch {
      // Gone already: the next refresh flags it.
    }
  }

  /** Delete a file or folder (the shell confirms first). Its tabs close without saving. */
  async remove(path: string): Promise<boolean> {
    const node = findNode(this.tree.peek(), path);
    const notes = (!node ? [] : node.kind === 'file' ? [path] : allFiles(node).map((f) => f.path)).filter(
      (p) => fileKind(p) === 'note',
    );
    const linked = new Set<string>();
    if (this.settings.peek().unusedImages === 'ask') {
      for (const p of notes) {
        const text = await this.noteText(p);
        if (text !== null) for (const t of imageTargets(text, p)) if (!isWithin(t, path)) linked.add(t);
      }
    }
    try {
      await this.store.remove(path);
    } catch (e) {
      this.report(e);
      return false;
    }
    for (const t of [...this.tabs.value]) if (isWithin(t.path.value, path)) this.closeTab(t);
    this.recent.value = this.recent.value.filter((p) => !isWithin(p, path));
    if (this.pinned.value.some((p) => isWithin(p, path))) {
      this.pinned.value = this.pinned.value.filter((p) => !isWithin(p, path));
    }
    const scope = this.scope.value;
    if (scope !== 'folder:' && scope.startsWith('folder:') && isWithin(scope.slice(7), path)) {
      this.scope.value = `folder:${parentOf(path)}`;
    }
    if (this.selected.value && isWithin(this.selected.value, path)) this.selected.value = null;
    await this.reloadTree();
    if (linked.size > 0) void this.offerUnused([...linked]);
    return true;
  }

  private async openElsewhere(path: string): Promise<void> {
    try {
      if (this.store.openExternal) {
        const ask = this.options.confirmOpenExternal;
        if (ask && this.settings.peek().confirmOpenExternal && !(await ask(baseName(path)))) return;
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

  /** Pin `path` to the top of the note list, or unpin it. */
  togglePin(path: string): void {
    const pinned = this.pinned.value;
    this.pinned.value = pinned.includes(path) ? pinned.filter((p) => p !== path) : [...pinned, path];
  }

  private touchRecent(path: string): void {
    const next = [path, ...this.recent.value.filter((p) => p !== path)].slice(0, RECENT_LIMIT);
    if (next.join('\n') !== this.recent.value.join('\n')) this.recent.value = next;
  }

  /**
   * Search across the notebook's notes (PRD §6.3): their names, then their text, open tabs' unsaved text
   * included. An aborted
   * `signal` stops the scan between files and resolves with no hits.
   */
  async search(
    query: string,
    opts: { caseSensitive?: boolean; regex?: boolean; signal?: AbortSignal } = {},
  ): Promise<SearchHit[]> {
    if (!query.trim()) return [];
    const find = { caseSensitive: opts.caseSensitive ?? false, regex: opts.regex ?? false };
    // Notes whose name matches come first, each led by its name; the rest follow in tree order.
    const titled: SearchHit[] = [];
    const rest: SearchHit[] = [];
    const done = () => titled.concat(rest).slice(0, 500);
    const open = new Map(this.tabs.value.filter((t) => t.state).map((t) => [t.path.value, t.state!]));
    for (const file of allFiles(this.tree.value)) {
      if (opts.signal?.aborted) return [];
      if (fileKind(file.path) !== 'note') continue;
      const name = findAll(stemOf(file.path), query, find)[0];
      const hits = name ? titled : rest;
      if (name)
        hits.push({
          path: file.path,
          line: 0,
          offset: 0,
          length: name.length,
          preview: baseName(file.path),
          matchStart: name.start,
          inTitle: true,
        });
      let text: string;
      try {
        text = open.get(file.path)?.text ?? readText((await this.store.read(file.path)).bytes).text;
      } catch {
        continue;
      }
      if (opts.signal?.aborted) return [];
      const matches = findAll(text, query, find);
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
        if (titled.length + rest.length >= 500) return done();
      }
    }
    return done();
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

  // ----- Pictures -----

  /**
   * Save pasted or dropped pictures for the note at `notePath` in its picture folder (PRD §10.6) and return
   * the Markdown that shows each. Files that aren't pictures are skipped.
   */
  async addPictures(
    notePath: string,
    files: readonly { name: string; type: string; bytes: Uint8Array }[],
  ): Promise<string[]> {
    const dir = attachmentDir(this.settings.peek(), notePath);
    let entries: Entry[];
    try {
      entries = await this.store.list(dir);
    } catch {
      entries = [];
    }
    const taken = new Set(entries.map((e) => e.name.toLowerCase()));
    const links: string[] = [];
    for (const f of files) {
      if (!isImageMime(f.type)) continue;
      // The same picture pasted again (Copy Picture from another note) links to the file that's already there.
      const same = await this.samePicture(dir, entries, f.bytes);
      if (same !== null) {
        links.push(imageMarkdown(notePath, same));
        continue;
      }
      let name = attachmentName(f.name, f.type, new Date());
      // Disks that ignore case would refuse "Photo.png" beside "photo.png".
      name = uniqueName(name, { has: (n: string) => taken.has(n.toLowerCase()) } as ReadonlySet<string>);
      taken.add(name.toLowerCase());
      const path = joinPath(dir, name);
      try {
        await this.store.write(path, f.bytes, { create: true });
      } catch (e) {
        this.report(e);
        continue;
      }
      links.push(imageMarkdown(notePath, path));
    }
    if (links.length > 0) await this.reloadTree();
    return links;
  }

  /** A note's text: its tab's, unsaved edits included, or what's on disk. Null when it can't be read. */
  private async noteText(path: string): Promise<string | null> {
    const tab = this.tabs.peek().find((t) => t.state && t.path.peek() === path);
    if (tab?.state) return tab.state.text;
    try {
      return readText((await this.store.read(path)).bytes).text;
    } catch {
      return null;
    }
  }

  /** Every picture some note links to, by notebook path. */
  private async linkedPictures(): Promise<Set<string>> {
    const out = new Set<string>();
    for (const f of allFiles(this.tree.peek())) {
      if (fileKind(f.path) !== 'note') continue;
      const text = await this.noteText(f.path);
      if (text?.includes('](')) for (const t of imageTargets(text, f.path)) out.add(t);
    }
    return out;
  }

  /** The picture in `dir` (listed as `entries`) whose bytes are `bytes`, or null. */
  private async samePicture(
    dir: string,
    entries: readonly Entry[],
    bytes: Uint8Array,
  ): Promise<string | null> {
    for (const e of entries) {
      if (e.kind !== 'file' || e.size !== bytes.length || !isImagePath(e.name)) continue;
      const path = e.path;
      try {
        const other = (await this.store.read(path)).bytes;
        if (other.length === bytes.length && other.every((b, i) => b === bytes[i])) return path;
      } catch {
        // Unreadable: not a match.
      }
    }
    return null;
  }

  /** Put a deleted picture's file back (undoing Delete Picture), and redraw the notes that show it. */
  async restorePicture(path: string, bytes: Uint8Array): Promise<void> {
    try {
      await this.store.write(path, bytes, { create: true });
    } catch (e) {
      this.report(e);
      return;
    }
    this.images.delete(path);
    this.imagesLoaded.value++;
    await this.reloadTree();
  }

  /** The notes other than `except` that link to the picture at `path`, in tree order. */
  async notesShowing(path: string, except: string): Promise<string[]> {
    const out: string[] = [];
    for (const f of allFiles(this.tree.peek())) {
      if (fileKind(f.path) !== 'note' || f.path === except) continue;
      const text = await this.noteText(f.path);
      if (text?.includes('](') && imageTargets(text, f.path).has(path)) out.push(f.path);
    }
    return out;
  }

  /** The notebook's pictures that no note links to (Find Unused Pictures…), in tree order. */
  async unusedPictures(): Promise<string[]> {
    const linked = await this.linkedPictures();
    return allFiles(this.tree.peek())
      .map((f) => f.path)
      .filter((p) => fileKind(p) === 'image' && !isWithin(p, stateDir) && !linked.has(p));
  }

  /**
   * After the links to `paths` went away, offer to delete those no note links to any more (the "When a
   * picture is no longer used" setting).
   */
  private async offerUnused(paths: readonly string[]): Promise<void> {
    if (this.settings.peek().unusedImages !== 'ask') return;
    const present = paths.filter((p) => findNode(this.tree.peek(), p)?.kind === 'file');
    if (present.length === 0) return;
    const linked = await this.linkedPictures();
    const unused = present.filter((p) => !linked.has(p));
    if (unused.length === 0) return;
    const trash = this.store.kind === 'native';
    this.notice.value = {
      kind: 'info',
      text:
        unused.length === 1
          ? `No note shows “${baseName(unused[0]!)}” any more.`
          : `No note shows ${unused.length} of these pictures any more.`,
      action: {
        label: trash ? 'Move to Trash' : 'Delete',
        run: () => void this.removeAll(unused),
      },
    };
  }

  /** Delete each of `paths` (unused pictures). */
  async removeAll(paths: readonly string[]): Promise<void> {
    let done = 0;
    for (const p of paths) if (await this.remove(p)) done++;
    if (done > 0) this.notify('info', `Deleted ${done} ${done === 1 ? 'picture' : 'pictures'}.`);
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
    // The tree follows Show Hidden Files by itself (see `load`).
    this.overrides.value = clean;
    try {
      await this.ensureStateDir();
      await this.store.write(overridesPath, encodeJson(clean), { force: true });
    } catch (e) {
      this.report(e);
    }
  }

  // ----- Session -----

  session(): Session {
    return {
      tabs: this.tabs.value.map((t) => ({
        path: t.path.value,
        ...(t.state ? { caret: t.state.selection.value.caret, mode: t.state.mode.value } : {}),
        ...(t.pinned.value ? { pinned: true } : {}),
      })),
      active: this.active.value?.path.value ?? null,
      expanded: [...this.expanded.value],
      recent: this.recent.value,
      pinned: this.pinned.value,
      library: { scope: this.scope.value, sort: this.sort.value },
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

  /**
   * Write the session now (before closing the notebook). The session is taken at the call; writes go out one
   * after another, so the last call's session is the one left on disk.
   */
  persist(): Promise<void> {
    const bytes = encodeJson(this.session());
    const run = this.persistChain.then(async () => {
      try {
        await this.ensureStateDir();
        await this.store.write(sessionPath, bytes, { force: true });
      } catch {
        // A read-only notebook just doesn't remember its tabs.
      }
    });
    this.persistChain = run;
    return run;
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

interface SaveSlot {
  /** The save that hasn't started yet; later calls share it. */
  queued: Promise<boolean> | null;
  /** Whether that save overwrites. */
  force: boolean;
  /** The last save, the next one waits for. */
  tail: Promise<boolean>;
}

function sameStats(a: ReadonlyMap<string, FileStat>, b: ReadonlyMap<string, FileStat>): boolean {
  if (a.size !== b.size) return false;
  for (const [k, v] of a) {
    const w = b.get(k);
    if (!w || w.size !== v.size || w.modified !== v.modified || w.created !== v.created) return false;
  }
  return true;
}

/** Whether two trees list the same entries, so a refresh can keep the old object. */
function sameTree(a: TreeNode, b: TreeNode): boolean {
  if (a === b) return true;
  if (a.path !== b.path || a.name !== b.name || a.kind !== b.kind) return false;
  if (a.children.length !== b.children.length) return false;
  return a.children.every((c, i) => sameTree(c, b.children[i]!));
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

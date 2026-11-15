import { computed, effect, signal, type Signal } from '@preact/signals';
import { encodeText, isReadOnlyDecode, readText } from '@puffinote/text';
import {
  applyRefEdits,
  attachmentDir,
  attachmentName,
  imageMarkdown,
  imageTargets,
  isImageMime,
  PictureUsage,
  relinkEdits,
  type RefEdit,
} from './attachments.ts';
import { NoteMetaCache } from './noteMeta.ts';
import { NotebookSearch } from './search.ts';
import { NotebookTransfer } from './transfer.ts';
import { Tab, type TabConflict } from './tab.ts';
import {
  ConflictError,
  NotFoundError,
  type Entry,
  type FileData,
  type NotebookStore,
} from '../platform/notebookStore.ts';
import {
  baseName,
  invalidNameReason,
  isImagePath,
  isWithin,
  joinPath,
  mimeOf,
  parentOf,
  pathKey,
  rebase,
  resolveRelative,
  uniqueName,
} from '../platform/paths.ts';
import {
  indentUnit,
  resolveSettings,
  changedSettings,
  sanitize,
  withChanges,
  unrecognized,
  type PartialSettings,
  type PrefsStorage,
  type Settings,
} from './settings.ts';
import {
  allFiles,
  ancestorsOf,
  emptyTree,
  fileKind,
  ignoredChange,
  findNode,
  loadTree,
  noteFileName,
  stateDir,
  type FileStat,
  type TreeNode,
} from './tree.ts';
import {
  emptyTrash,
  hasNotebookTrash,
  listTrash,
  moveToTrash,
  restoreFromTrash,
  type TrashItem,
} from './trash.ts';
import type { RecoveryStorage } from './recovery.ts';

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

const AUTOSAVE_MS = 1000;
/** A recovery copy is taken this long after the last edit (PRD §5)… */
const RECOVERY_MS = 1000;
/** …and at least this often while the typing goes on. */
const RECOVERY_MAX_WAIT_MS = 5000;
const SAVED_FLASH_MS = 3000;
const RECENT_LIMIT = 20;

/** A message for the status line: what just failed, or what needs the user. */
export interface Notice {
  readonly kind: 'info' | 'error';
  readonly text: string;
  /** A button beside the text (Move to Trash for a picture no note uses). */
  readonly action?: { readonly label: string; readonly run: () => void };
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
  /**
   * Print a standalone HTML document or save it as a PDF named `name` (PRD §10.5): the browser's print dialog,
   * or the desktop app's save dialog. Without it, Export as PDF is off.
   */
  readonly print?: (html: string, name: string) => Promise<void>;
  /** Ask whether to open a file in its default app; true opens it. Without it, files open straight away. */
  readonly confirmOpenExternal?: (name: string) => Promise<boolean>;
  /** Where recovery copies of unsaved edits go (PRD §5). Without it, none are kept. */
  readonly recovery?: RecoveryStorage;
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
  /** Writes of `.puffinote/settings.json` in flight; a refresh doesn't read it meanwhile. */
  private settingsWrites = 0;
  readonly settings = computed<Settings>(() => resolveSettings(this.userPrefs.value, this.overrides.value));
  readonly notice = signal<Notice | null>(null);
  /** Bumps when an image a note shows has loaded, so the editor redraws it. */
  readonly imagesLoaded = signal(0);
  /** Excerpts and picture sizes for the note list, read as it asks. */
  readonly meta: NoteMetaCache;
  /** Import, download and export. */
  readonly transfer: NotebookTransfer;
  /** Searching the notes' text (the Search panel, the Library's Filter box). */
  readonly textSearch: NotebookSearch;

  private readonly options: NotebookOptions;
  private readonly images = new Map<string, string | null>();
  /** The read in flight for each picture being loaded; a read whose token was replaced is dropped. */
  private readonly imageReads = new Map<string, object>();
  private readonly disposers: (() => void)[] = [];
  private persistTimer: ReturnType<typeof setTimeout> | null = null;
  private refreshTimer: ReturnType<typeof setTimeout> | null = null;
  /** Each note tab's file as the listing showed it when a refresh last read it, to skip it while unchanged. */
  private readonly checkedStats = new WeakMap<Tab, FileStat & { readonly path: string }>();
  /** Tree reads started; only the newest one's answer is taken (see `reloadTree`). */
  private treeReads = 0;
  private latestTreeRead: Promise<void> = Promise.resolve();
  private loaded = false;
  /** Closed: a dialog or menu left over from it must not change files any more. */
  private disposed = false;
  /** Opens whose read is out, by path (see `pendingOpen`). */
  private readonly opening = new Map<string, Promise<Tab>>();
  /** Bumps on every open and activation; an open brings its tab to the front only if it is still the last. */
  private frontTicket = 0;
  /** Each tab's saves run one at a time (see `save`). */
  private readonly saveSlots = new WeakMap<Tab, SaveSlot>();
  /** Tabs with a write to the store in flight. */
  private readonly writing = new Set<Tab>();
  /** Each in-flight write's end (after the tab took its answer), for a move to wait on. */
  private readonly writeDone = new Map<Tab, Promise<void>>();
  /**
   * Held while a rename or move changes paths under the store: saves wait for it, then write to the path
   * the move left the tab at. One move at a time.
   */
  private moveLock: Promise<void> | null = null;
  /** Session writes, one after another, so an older one never lands last. */
  private persistChain: Promise<void> = Promise.resolve();

  private constructor(store: NotebookStore, options: NotebookOptions) {
    this.store = store;
    this.options = options;
    this.userPrefs = signal(options.prefs.load());
    this.meta = new NoteMetaCache(store);
    this.transfer = new NotebookTransfer({
      store,
      ...(options.download ? { download: options.download } : {}),
      ...(options.print ? { print: options.print } : {}),
      name: () => this.name,
      disposed: () => this.disposed,
      saveAll: () => this.saveAll(),
      imported: async (dir, roots) => {
        if (dir) this.toggleFolder(dir, true);
        await this.reloadTree().catch((e: unknown) => this.report(e));
        if (roots.length === 1) this.selected.value = roots[0]!;
      },
      notify: (kind, text) => this.notify(kind, text),
      report: (e) => this.report(e),
    });
    this.textSearch = new NotebookSearch({
      files: () => allFiles(this.tree.value),
      read: (path) => this.store.read(path),
      openTexts: () =>
        new Map(this.tabs.value.filter((t) => t.state).map((t) => [t.path.value, t.state!.text])),
    });
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
    await this.openRecovered();
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
    if (this.store.watch)
      this.disposers.push(
        this.store.watch((paths) => {
          // A git client touching `.git/index` says nothing about the notes. (An empty list means "look again".)
          if (paths.length > 0 && paths.every(ignoredChange)) return;
          this.forgetImages(paths);
          this.scheduleRefresh();
        }),
      );
    if (typeof window !== 'undefined') {
      // Look again whenever the window comes back. Stores without change events (a browser folder, the NAS)
      // only learn of changes this way, and a watcher on a network folder (SMB, NFS) never hears of edits
      // made on another machine.
      const onFocus = () => this.scheduleRefresh();
      window.addEventListener('focus', onFocus);
      this.disposers.push(() => window.removeEventListener('focus', onFocus));
    }
  }

  dispose(): void {
    this.disposed = true;
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

  /**
   * Re-read the tree. The signal keeps its old value when nothing changed, so a refresh redraws nothing. A
   * read that a newer one started after is dropped (a slow refresh landing after a rename would bring the old
   * name back); its caller waits for the newest read instead.
   */
  reloadTree(): Promise<void> {
    const run = this.readTree(++this.treeReads);
    this.latestTreeRead = run;
    return run;
  }

  private async readTree(ticket: number): Promise<void> {
    const stats = new Map<string, FileStat>();
    let next: TreeNode | null = null;
    try {
      next = await loadTree(this.store, this.settings.peek().showHiddenFiles, stats);
    } catch (e) {
      if (ticket === this.treeReads) throw e; // a superseded read's failure (a folder renamed under it) is moot
    }
    if (next === null || ticket !== this.treeReads) return this.latestTreeRead.catch(() => {});
    // A picture whose size or time changed (or that went away) is read again when next drawn. Stores without
    // change events (a browser folder, the NAS) only tell this way.
    const before = this.stats.peek();
    this.forgetImages(
      [...this.images.keys()].filter((p) => {
        const a = before.get(p);
        const b = stats.get(p);
        return a?.size !== b?.size || a?.modified !== b?.modified;
      }),
    );
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
    const stats = this.stats.peek();
    for (const tab of this.tabs.value) {
      if (tab.kind !== 'note') continue;
      // A save in flight is about to move the tab on: its answer, not this read, says what's on disk.
      if (this.writing.has(tab)) continue;
      const path = tab.path.value;
      // The listing says the file is as it was when this tab was last checked: don't read it again.
      const listed = stats.get(path);
      const checked = this.checkedStats.get(tab);
      if (tab.conflict.value === 'none' && listed && checked && sameListing(checked, path, listed)) continue;
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
      // The listing came before this read, so the tab is at least as new as it says (see the check above).
      if (listed && listed.modified !== 0) this.checkedStats.set(tab, { path, ...listed });
      if (data.version !== null && data.version === tab.version) {
        if (tab.conflict.value === 'deleted') tab.conflict.value = 'none';
        continue;
      }
      const text = readText(data.bytes).text;
      if (text === tab.savedText) {
        // Touched, or deleted and put back, with the same text: nothing to resolve.
        tab.adoptVersion(data.version);
        if (tab.conflict.value === 'deleted') tab.conflict.value = 'none';
        continue;
      }
      if (tab.dirty.value) tab.conflict.value = 'changed';
      else this.replaceText(tab, text, data.version);
    }
    await this.reloadOverrides();
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
    // A later open or a tab picked meanwhile wins: a slow read must not bring an older choice to the front.
    const ticket = ++this.frontTicket;
    try {
      const tab = this.tabs.value.find((t) => t.path.value === path) ?? (await this.pendingOpen(path));
      if (ticket === this.frontTicket) {
        this.active.value = tab;
        this.touchRecent(path);
      }
      return tab;
    } catch (e) {
      this.report(e);
      return null;
    }
  }

  /** The tab for `path`, sharing a read already out for it, so a double click opens one tab. */
  private pendingOpen(path: string): Promise<Tab> {
    const pending = this.opening.get(path);
    if (pending) return pending;
    const run = this.openTab(path).finally(() => this.opening.delete(path));
    this.opening.set(path, run);
    return run;
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
      this.watchRecovery(tab);
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

  /**
   * Keep a recovery copy of the tab's text while it is dirty (PRD §5): a second after the last edit, and at
   * least every five seconds while typing goes on. A tab that is clean again (saved, reverted) drops its copy.
   */
  private watchRecovery(tab: Tab): void {
    const store = this.options.recovery;
    if (!store || tab.readOnly) return;
    const offered = store.get(tab.path.peek());
    if (offered) {
      if (offered.text !== tab.savedText) tab.recovered.value = offered;
      else store.remove(tab.path.peek());
    }
    let timer: ReturnType<typeof setTimeout> | null = null;
    let since = 0;
    let lastPath = tab.path.peek();
    const write = () => {
      if (timer) clearTimeout(timer);
      timer = null;
      if (tab.recoveryOff || tab.recovered.peek() || !tab.dirty.peek() || !this.tabs.peek().includes(tab))
        return;
      store.set(tab.path.peek(), { text: tab.state!.text, time: Date.now() });
    };
    tab.onDispose(
      effect(() => {
        void tab.state!.version.value;
        const path = tab.path.value;
        const dirty = tab.dirty.value;
        const pending = tab.recovered.value;
        if (path !== lastPath) {
          // Renamed or moved: the copy follows the note.
          const old = store.get(lastPath);
          store.remove(lastPath);
          if (old) store.set(path, old);
          lastPath = path;
        }
        if (pending || tab.recoveryOff) return;
        if (!dirty) {
          if (timer) clearTimeout(timer);
          timer = null;
          store.remove(path);
          return;
        }
        const now = Date.now();
        if (!timer) since = now;
        else if (now - since >= RECOVERY_MAX_WAIT_MS) return;
        else clearTimeout(timer);
        timer = setTimeout(write, RECOVERY_MS);
      }),
    );
    this.flushers.set(tab, write);
    tab.onDispose(() => {
      if (timer) clearTimeout(timer);
      this.flushers.delete(tab);
    });
  }

  /** Recovery writes waiting on their debounce, by tab; `flushRecovery` runs them now. */
  private readonly flushers = new Map<Tab, () => void>();

  /** Take every pending recovery copy now (the page is going away). */
  flushRecovery(): void {
    for (const f of this.flushers.values()) f();
  }

  /** Load a tab's recovered text as an undoable edit; the tab is dirty and nothing is saved yet. */
  restoreRecovered(tab: Tab): void {
    const entry = tab.recovered.peek();
    if (!entry || !tab.state) return;
    tab.recovered.value = null;
    this.applyText(tab, entry.text);
  }

  /** Throw a tab's recovered text away. */
  discardRecovered(tab: Tab): void {
    if (!tab.recovered.peek()) return;
    tab.recovered.value = null;
    this.options.recovery?.remove(tab.path.peek());
  }

  /** The user chose Don't Save for `tabs`: drop their recovery copies and take no more. */
  forgetRecovery(tabs: readonly Tab[]): void {
    for (const t of tabs) {
      t.recoveryOff = true;
      if (!t.recovered.peek()) this.options.recovery?.remove(t.path.peek());
    }
  }

  /**
   * Notes with a recovery copy that the session didn't reopen get a tab now, behind the others, so their
   * banner shows. A copy whose note is gone is kept until it comes back.
   */
  private async openRecovered(): Promise<void> {
    const store = this.options.recovery;
    if (!store) return;
    for (const path of store.paths()) {
      if (this.tabs.peek().some((t) => t.path.peek() === path)) continue;
      if (fileKind(path) !== 'note' || findNode(this.tree.peek(), path)?.kind !== 'file') continue;
      const tab = await this.openTab(path).catch(() => null);
      if (tab && !this.active.peek()) this.active.value = tab;
    }
  }

  activate(tab: Tab | null): void {
    this.frontTicket++;
    this.active.value = tab;
    if (tab) this.touchRecent(tab.path.value);
  }

  /** Close `tab`, discarding unsaved edits: the shell asks first. */
  closeTab(tab: Tab): void {
    const tabs = this.tabs.value;
    const i = tabs.indexOf(tab);
    if (i < 0) return;
    // Its edits are thrown away with it, but a recovered copy nobody answered yet is kept for next time.
    if (!tab.recovered.peek()) this.options.recovery?.remove(tab.path.peek());
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
    // A move in progress: wait, so the path read below is where the move put the note. From here to the
    // write going out, nothing awaits, so a move can't start in between.
    while (this.moveLock) await this.moveLock;
    // A closed notebook changes no files (PRD §5): a save queued before it closed doesn't go out.
    if (this.disposed) return false;
    if (!tab.state || tab.readOnly || !this.tabs.peek().includes(tab)) return false;
    if (!force && tab.conflict.peek() !== 'none') return false;
    if (!force && !tab.dirty.peek()) return true;
    const text = tab.state.text;
    const path = tab.path.peek();
    const before = tab.savedText;
    this.writing.add(tab);
    let finished!: () => void;
    this.writeDone.set(tab, new Promise<void>((r) => (finished = r)));
    try {
      const bytes = encodeText(text, tab.format);
      const version = force
        ? await this.store.write(path, bytes, { force })
        : await this.writeOver(path, bytes, tab.version, (now) => readText(now).text === before);
      // Edits made while the write was in flight stay unsaved.
      tab.savedAs(version, text);
      const stat = this.stats.peek().get(path);
      const now = Date.now();
      this.stats.value = new Map(this.stats.peek()).set(path, {
        size: text.length,
        modified: now,
        created: stat?.created || now,
      });
      this.flashSaved(tab);
      this.withdrawOfferShownBy(text, path);
      if (before.includes('](')) {
        const key = (p: string) => pathKey(p, this.ignoresCase);
        const now = new Set([...imageTargets(text, path)].map(key));
        const dropped = [...imageTargets(before, path)].filter((p) => !now.has(key(p)));
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
      this.writeDone.delete(tab);
      finished();
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

  /**
   * Write `bytes` over a file last read at version `expected`. A store that gave no version (a server without
   * ETags) can't refuse a stale write itself, so the file is read again first and must still hold what was
   * read (`unchanged`); otherwise this throws the ConflictError a versioned store would (PRD §5).
   */
  private async writeOver(
    path: string,
    bytes: Uint8Array,
    expected: string | null,
    unchanged: (now: Uint8Array) => boolean,
  ): Promise<string | null> {
    if (expected != null) return this.store.write(path, bytes, { expected });
    let now: FileData;
    try {
      now = await this.store.read(path);
    } catch (e) {
      if (e instanceof NotFoundError)
        throw new ConflictError('deleted', 'The file was deleted since it was opened.');
      throw e;
    }
    if (!unchanged(now.bytes)) throw new ConflictError('changed', 'The file changed since it was opened.');
    if (this.disposed) throw new Error('The notebook was closed.');
    return this.store.write(path, bytes, { expected: now.version });
  }

  async saveAll(): Promise<boolean> {
    let ok = true;
    // A note deleted on disk is saved again (PRD §5); one that changed there is left flagged.
    for (const t of this.dirtyTabs) ok = (await this.save(t, t.conflict.peek() === 'deleted')) && ok;
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
    this.applyText(tab, text);
    tab.markSaved(version);
  }

  /** Replace the tab's text as one undoable step, changing only the part that differs. */
  private applyText(tab: Tab, text: string): void {
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
  }

  // ----- Files and folders -----

  /** Create a note in `dir` and open it. `name` may leave off `.md` (see `noteFileName`). Returns the path. */
  async createNote(dir: string, name: string): Promise<string | null> {
    if (this.disposed) return null;
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
    if (this.disposed) return null;
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
    if (this.disposed) return null;
    while (this.moveLock) await this.moveLock;
    let release!: () => void;
    this.moveLock = new Promise<void>((r) => (release = r));
    let moved: Tab[];
    const node = findNode(this.tree.peek(), from);
    const movedFiles = !node ? [] : node.kind === 'file' ? [from] : allFiles(node).map((f) => f.path);
    try {
      // Saves already writing to the old path land first; later ones wait for the lock (see writeTab).
      await Promise.all(
        this.tabs.peek().flatMap((t) => (isWithin(t.path.peek(), from) ? [this.writeDone.get(t)] : [])),
      );
      try {
        await this.store.move(from, to);
      } catch (e) {
        this.report(e);
        return null;
      }
      moved = this.tabs.value.filter((t) => isWithin(t.path.value, from));
      for (const t of moved) t.path.value = rebase(t.path.value, from, to);
      this.tabs.value = [...this.tabs.value];
      await Promise.all(moved.map((t) => this.adoptMovedVersion(t)));
    } finally {
      this.moveLock = null;
      release();
    }
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
      if (this.disposed) return;
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
    // A closed notebook changes no files (PRD §5).
    if (this.disposed) return false;
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
      if (list.length === 0 || this.disposed) return false;
      await this.writeOver(
        path,
        encodeText(applyRefEdits(decoded.text, list), decoded.format),
        data.version,
        (now) => sameBytes(now, data.bytes),
      );
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
      tab.adoptVersion(data.version);
    } catch {
      // Gone already: the next refresh flags it.
    }
  }

  /** Deleted files go to the notebook's own Trash (browser, NAS), not the system's (desktop). PRD §6.1. */
  get notebookTrash(): boolean {
    return hasNotebookTrash(this.store);
  }

  /**
   * Delete a file or folder (the shell confirms first). Its tabs close without saving. Where the notebook keeps
   * its own Trash, the status bar says so with an Undo button.
   */
  async remove(path: string): Promise<boolean> {
    const result = await this.deleteEntry(path);
    if (result === false) return false;
    if (result) this.offerUndo([result], `Moved “${baseName(path)}” to the notebook’s Trash.`);
    return true;
  }

  /** The status bar says what went to the Trash, with Undo. */
  private offerUndo(items: readonly TrashItem[], text: string): void {
    this.offer = null;
    this.notice.value = {
      kind: 'info',
      text,
      action: { label: 'Undo', run: () => void this.restore(items) },
    };
  }

  /** What is in the notebook's Trash, newest first (none on the desktop, whose files go to the system Trash). */
  async trashItems(): Promise<TrashItem[]> {
    if (!this.notebookTrash) return [];
    return listTrash(this.store);
  }

  /**
   * Put trashed items back where they were, under a free name when the old one is taken (never overwriting).
   * Returns the paths they went to.
   */
  async restore(items: readonly TrashItem[]): Promise<string[]> {
    if (this.disposed) return [];
    const out: string[] = [];
    for (const item of items) {
      try {
        out.push(await restoreFromTrash(this.store, item));
      } catch (e) {
        this.report(e);
      }
    }
    if (out.length === 0) return out;
    // A note showing a restored picture drew it as missing.
    this.forgetImages(out);
    await this.reloadTree();
    const renamed = items.length === 1 && out[0] !== items[0]!.path;
    if (out.length === 1) {
      this.notify(
        'info',
        renamed
          ? `Restored “${baseName(items[0]!.path)}” as “${baseName(out[0]!)}”: the name was taken.`
          : `Restored “${baseName(out[0]!)}”.`,
      );
    } else this.notify('info', `Restored ${out.length} items.`);
    return out;
  }

  /** Delete everything in the notebook's Trash for good (the shell confirms first). */
  async emptyTrash(): Promise<boolean> {
    if (this.disposed || !this.notebookTrash) return false;
    try {
      await emptyTrash(this.store);
    } catch (e) {
      this.report(e);
      return false;
    }
    this.notify('info', 'The notebook’s Trash is empty.');
    return true;
  }

  /**
   * Delete `path` (to the notebook's Trash or the system's) and reconcile tabs, lists and the tree. False when
   * it failed; the trash item when it went to the notebook's Trash; null otherwise.
   */
  private async deleteEntry(path: string): Promise<TrashItem | null | false> {
    if (this.disposed) return false;
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
    let item: TrashItem | null = null;
    try {
      if (this.notebookTrash) item = await moveToTrash(this.store, path, node?.kind ?? 'file');
      else await this.store.remove(path);
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
    return item;
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
    const token = {};
    this.imageReads.set(path, token);
    const current = () => this.imageReads.get(path) === token && !this.disposed;
    void this.store
      .read(path)
      .then(({ bytes }) => {
        if (!current()) return;
        this.imageReads.delete(path);
        this.images.set(
          path,
          URL.createObjectURL(new Blob([bytes as Uint8Array<ArrayBuffer>], { type: mimeOf(path) })),
        );
        this.imagesLoaded.value++;
      })
      .catch(() => {
        // Not kept: the picture may turn up (a sync, a NAS back online), and the next redraw asks again.
        if (!current()) return;
        this.imageReads.delete(path);
        this.images.delete(path);
      });
    return '';
  }

  /** Drop what was read for pictures at or inside `paths`, so the editor reads them again. */
  private forgetImages(paths: readonly string[]): void {
    if (paths.length === 0) return;
    let dropped = false;
    for (const path of [...this.images.keys()]) {
      if (!paths.some((p) => isWithin(path, p))) continue;
      const url = this.images.get(path);
      if (url?.startsWith('blob:')) URL.revokeObjectURL(url);
      this.images.delete(path);
      this.imageReads.delete(path);
      dropped = true;
    }
    if (dropped) this.imagesLoaded.value++;
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

  /**
   * Every note in the notebook, hidden folders included whatever Show Hidden Files says: a picture a hidden
   * note shows is still used.
   */
  private async allNotes(): Promise<string[]> {
    const tree = await loadTree(this.store, true);
    return allFiles(tree)
      .map((f) => f.path)
      .filter((p) => fileKind(p) === 'note');
  }

  /** A note's text, like `noteText`, but a note that can't be read is an error: nothing can be said about it. */
  private async readNote(path: string): Promise<string> {
    const text = await this.noteText(path);
    if (text === null)
      throw new Error(`Puffinote couldn’t read “${path}”, so it can’t tell which pictures are used.`);
    return text;
  }

  /** Whether picture names compare without case: when the store says so, or can't tell. */
  private get ignoresCase(): boolean {
    return this.store.capabilities.ignoresCase !== false;
  }

  /**
   * Every picture some note (other than those in `except`) links to, by notebook path. Throws when a note can't be read, so a picture it
   * shows is never called unused.
   */
  private async linkedPictures(except: ReadonlySet<string> = new Set()): Promise<PictureUsage> {
    const out = new PictureUsage(this.ignoresCase);
    for (const path of await this.allNotes()) {
      if (!except.has(path)) out.add(await this.readNote(path), path);
    }
    return out;
  }

  /** Whether `text`, the note at `notePath`, shows the picture at `picture` in any of the ways that count. */
  showsPicture(text: string, notePath: string, picture: string): boolean {
    const usage = new PictureUsage(this.ignoresCase);
    usage.add(text, notePath);
    return usage.has(picture);
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
    if (this.disposed) return;
    try {
      await this.store.write(path, bytes, { create: true });
    } catch (e) {
      this.report(e);
      return;
    }
    this.forgetImages([path]);
    this.imagesLoaded.value++;
    await this.reloadTree();
  }

  /**
   * The notes other than `except` that link to the picture at `path`, in tree order, hidden ones included.
   * Throws when a note can't be read.
   */
  async notesShowing(path: string, except: string): Promise<string[]> {
    const out: string[] = [];
    for (const note of await this.allNotes()) {
      if (note === except) continue;
      const text = await this.readNote(note);
      if (this.showsPicture(text, note, path)) out.push(note);
    }
    return out;
  }

  /**
   * The notes outside `path` (a file or folder) that show a picture at or inside it, in tree order: what
   * deleting `path` leaves with a missing picture. Throws when a note can't be read.
   */
  async notesShowingPicturesIn(path: string): Promise<string[]> {
    const tree = await loadTree(this.store, true);
    const node = findNode(tree, path);
    const files = !node ? [] : node.kind === 'file' ? [node.path] : allFiles(node).map((f) => f.path);
    const pictures = files.filter((p) => fileKind(p) === 'image');
    if (pictures.length === 0) return [];
    const out: string[] = [];
    for (const f of allFiles(tree)) {
      if (fileKind(f.path) !== 'note' || isWithin(f.path, path)) continue;
      const usage = new PictureUsage(this.ignoresCase);
      usage.add(await this.readNote(f.path), f.path);
      if (pictures.some((p) => usage.has(p))) out.push(f.path);
    }
    return out;
  }

  /**
   * The notebook's pictures that no note links to (Find Unused Pictures…), in tree order. Throws when a note
   * can't be read.
   */
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
    let linked: PictureUsage;
    try {
      linked = await this.linkedPictures();
    } catch {
      // A note that can't be read may show them: say nothing rather than offer to delete a used picture.
      return;
    }
    const unused = present.filter((p) => !linked.has(p));
    if (unused.length === 0) return;
    const notice: Notice = {
      kind: 'info',
      text:
        unused.length === 1
          ? `No note shows “${baseName(unused[0]!)}” any more.`
          : `No note shows ${unused.length} of these pictures any more.`,
      action: {
        label: 'Move to Trash',
        run: () => void this.removeUnused(unused),
      },
    };
    this.offer = { notice, paths: unused };
    this.notice.value = notice;
  }

  /** The no-longer-used offer, while it is the notice, taken down when a save shows one of its pictures again. */
  private offer: { readonly notice: Notice; readonly paths: readonly string[] } | null = null;

  private withdrawOfferShownBy(text: string, notePath: string): void {
    const offer = this.offer;
    if (!offer) return;
    if (this.notice.peek() !== offer.notice) {
      this.offer = null;
      return;
    }
    if (offer.paths.some((p) => this.showsPicture(text, notePath, p))) {
      this.offer = null;
      this.notice.value = null;
    }
  }

  /**
   * Delete those of `paths` no note shows now, leaving out the notes in `ignoring` (the no-longer-used offer,
   * Find Unused Pictures, Delete Picture's Redo). A picture a note shows again by now is kept, and so is every
   * one when a note can't be read.
   */
  async removeUnused(paths: readonly string[], ignoring: ReadonlySet<string> = new Set()): Promise<void> {
    if (this.disposed) return;
    let linked: PictureUsage;
    try {
      linked = await this.linkedPictures(ignoring);
    } catch (e) {
      this.report(e);
      return;
    }
    const unused = paths.filter((p) => !linked.has(p));
    const kept = paths.length - unused.length;
    const { done, items } = await this.deleteEach(unused);
    if (kept > 0) {
      const which = kept === 1 ? `“${baseName(paths.find((p) => linked.has(p))!)}”` : `${kept} pictures`;
      this.notify('info', `Kept ${which}: a note shows ${kept === 1 ? 'it' : 'them'} again.`);
    } else this.sayPicturesDeleted(done, items);
  }

  /** Delete each of `paths`, whoever shows them (Delete Picture's Delete File). */
  async removeAll(paths: readonly string[]): Promise<void> {
    if (this.disposed) return;
    const { done, items } = await this.deleteEach(paths);
    this.sayPicturesDeleted(done, items);
  }

  private async deleteEach(paths: readonly string[]): Promise<{ done: number; items: TrashItem[] }> {
    let done = 0;
    const items: TrashItem[] = [];
    for (const p of paths) {
      const result = await this.deleteEntry(p);
      if (result === false) continue;
      done++;
      if (result) items.push(result);
    }
    return { done, items };
  }

  private sayPicturesDeleted(done: number, items: readonly TrashItem[]): void {
    if (done === 0) return;
    const what = `${done} ${done === 1 ? 'picture' : 'pictures'}`;
    if (items.length > 0) this.offerUndo(items, `Moved ${what} to the notebook’s Trash.`);
    else this.notify('info', `Moved ${what} to the Trash.`);
  }

  // ----- Settings -----

  /** Change the user's preferences (every notebook). */
  /**
   * Change the user's preferences. With `base` (what the Settings dialog showed when it opened), only the
   * preferences that differ from it are changed, over what is saved by now: another window's changes stay.
   */
  setPreferences(prefs: PartialSettings, base?: PartialSettings): void {
    const clean = sanitize(
      base ? withChanges(this.options.prefs.load(), prefs, changedSettings(base, sanitize(prefs))) : prefs,
    );
    this.options.prefs.save(clean);
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
    // The tree follows Show Hidden Files by itself (see `load`).
    this.overrides.value = sanitize(withChanges(this.overrides.peek(), after, changed));
    this.settingsWrites++;
    try {
      await this.ensureStateDir();
      for (let attempt = 0; ; attempt++) {
        const disk = await this.readOverrides();
        const merged = withChanges(disk.known, after, changed);
        try {
          const bytes = encodeJson({ ...disk.rest, ...merged });
          if (disk.version === undefined) await this.store.write(overridesPath, bytes, { create: true });
          else await this.writeOver(overridesPath, bytes, disk.version, (now) => sameBytes(now, disk.bytes));
          this.overrides.value = sanitize(merged);
          return;
        } catch (e) {
          // Changed again between the read and the write: read it again.
          if (!(e instanceof ConflictError) || e.reason === 'locked' || attempt >= 3) throw e;
        }
      }
    } catch (e) {
      this.report(e);
    } finally {
      this.settingsWrites--;
    }
  }

  /** `.puffinote/settings.json` as it is now: what this version uses, the rest, and its version (undefined: no file). */
  private async readOverrides(): Promise<{
    known: PartialSettings;
    rest: Record<string, unknown>;
    version: string | null | undefined;
    bytes: Uint8Array;
  }> {
    let data: FileData;
    try {
      data = await this.store.read(overridesPath);
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

  /** Take changes another program made to `.puffinote/settings.json` (on refresh). */
  private async reloadOverrides(): Promise<void> {
    if (this.settingsWrites > 0) return;
    let disk: Awaited<ReturnType<Notebook['readOverrides']>>;
    try {
      disk = await this.readOverrides();
    } catch {
      return;
    }
    if (this.settingsWrites > 0) return;
    if (JSON.stringify(disk.known) !== JSON.stringify(this.overrides.peek()))
      this.overrides.value = disk.known;
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

function sameBytes(a: Uint8Array, b: Uint8Array): boolean {
  return a.length === b.length && a.every((x, i) => x === b[i]);
}

/** Is `listed` the same listing as `checked` for the file at `path`? A store that can't tell times never is. */
function sameListing(checked: FileStat & { readonly path: string }, path: string, listed: FileStat): boolean {
  return (
    checked.path === path &&
    listed.modified !== 0 &&
    checked.modified === listed.modified &&
    checked.size === listed.size
  );
}

/** Does `text` contain `query`, ignoring case, as `search` matches it? */

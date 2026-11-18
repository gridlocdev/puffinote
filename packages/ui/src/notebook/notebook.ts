import { after, type Cancel } from '../schedule.ts';
import { effect, signal } from '@preact/signals';
import { type DecodedText, encodeText, isReadOnlyDecode, readText, reloadEdit } from '@puffinote/text';
import { applyRefEdits, imageTargets, relinkEdits, type RefEdit } from './attachments.ts';
import { NoteMetaCache } from './noteMeta.ts';
import { NotebookSearch } from './search.ts';
import { NotebookTransfer } from './transfer.ts';
import { Tab, type TabConflict } from './tab.ts';
import {
  ConflictError,
  NotFoundError,
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
  rebase,
} from '../platform/paths.ts';
import { indentUnit, type PrefsStorage } from './settings.ts';
import { NotebookSettings } from './notebookSettings.ts';
import { encodeJson, ensureStateDir, parseJson, sameBytes, writeOver } from './storeWrites.ts';
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
import type { TrashItem } from './trash.ts';
import { NotebookTrash } from './notebookTrash.ts';
import { NotebookPictures } from './notebookPictures.ts';
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
/** Moves kept for a rename or move asked for with an older path (see `follow`). */
const MOVES_KEPT = 50;

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

/** How long after opening the notebook the search index catches up with the notes. */
const warmSearchDelay = 2000;

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
  /** The user's preferences, this notebook's overrides, and the settings they resolve to (PRD §9). */
  readonly config: NotebookSettings;
  /** Where deleted files go, and getting them back. */
  readonly trash: NotebookTrash;
  readonly notice = signal<Notice | null>(null);
  /** Reading, saving and cleaning up the notebook's pictures. */
  readonly pictures: NotebookPictures;
  /** Excerpts and picture sizes for the note list, read as it asks. */
  readonly meta: NoteMetaCache;
  /** Import, download and export. */
  readonly transfer: NotebookTransfer;
  /** Searching the notes' text (the Search panel, the Library's Filter box). */
  readonly textSearch: NotebookSearch;

  private readonly options: NotebookOptions;
  private readonly disposers: (() => void)[] = [];
  private persistTimer: Cancel | null = null;
  private refreshTimer: Cancel | null = null;
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
  /** A refresh's reads of open notes that are out, for a move to wait on. */
  private readonly readsOut = new Set<{ readonly path: string; readonly done: Promise<boolean> }>();
  /**
   * Held while a rename, a move or a new file changes paths under the store: saves wait for it, then write to
   * the path the move left the tab at. One at a time, so a folder can't move while a note is being made in it.
   */
  private moveLock: Promise<void> | null = null;
  /**
   * The last moves made, oldest first. A rename or move asked for with a path from before one (a dialog opened
   * while it was on its way, on a slow NAS) follows them to where the file or folder is now.
   */
  private readonly moves: { readonly from: string; readonly to: string }[] = [];
  /** Session writes, one after another, so an older one never lands last. */
  private persistChain: Promise<unknown> = Promise.resolve();

  private constructor(store: NotebookStore, options: NotebookOptions) {
    this.store = store;
    this.options = options;
    this.config = new NotebookSettings({
      store,
      prefs: options.prefs,
      closed: () => this.disposed,
      report: (e) => this.report(e),
    });
    this.meta = new NoteMetaCache(store, this.report);
    this.trash = new NotebookTrash({
      store,
      closed: () => this.disposed,
      restored: async (paths) => {
        // A note showing a restored picture drew it as missing.
        this.pictures.forget(paths);
        await this.reloadTree();
      },
      offer: (text, label, run) => {
        this.notice.value = { kind: 'info', text, action: { label, run } };
      },
      notify: (kind, text) => this.notify(kind, text),
      report: (e) => this.report(e),
    });
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
      picturePath: (notePath, ref) => this.pictures.pathOf(notePath, ref),
      report: (e) => this.report(e),
    });
    this.pictures = new NotebookPictures({
      store,
      notice: this.notice,
      trash: this.trash,
      settings: () => this.config.settings.peek(),
      tree: () => this.tree.peek(),
      closed: () => this.disposed,
      noteText: (path) => this.noteText(path),
      reloadTree: () => this.reloadTree(),
      deleteEntry: (path) => this.deleteEntry(path),
      notify: (kind, text) => this.notify(kind, text),
      report: (e) => this.report(e),
    });
    this.textSearch = new NotebookSearch({
      files: () => allFiles(this.tree.value),
      stat: (path) => this.stats.peek().get(path),
      read: (path) => this.store.read(path),
      store,
      closed: () => this.disposed,
      report: (e) => this.report(e),
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
    await this.config.load();
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
    // Bring the search index up to date once the notebook has settled, so the first search is quick (PRD §6.3).
    this.disposers.push(after(warmSearchDelay, () => void this.textSearch.refresh().catch(this.report)));

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
        const unit = indentUnit(this.config.settings.value);
        for (const t of this.tabs.value) if (t.state) t.state.indentUnit = unit;
      }),
    );
    // Show Hidden Files changes the tree, from whichever layer it comes (PRD §6, §9).
    let hidden = this.config.settings.peek().showHiddenFiles;
    this.disposers.push(
      effect(() => {
        const now = this.config.settings.value.showHiddenFiles;
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
          this.pictures.forget(paths);
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
      this.persistTimer();
      this.persistTimer = null;
      this.persist().catch(this.report);
    }
    for (const d of this.disposers) d();
    this.meta.dispose();
    this.textSearch.dispose();
    for (const t of this.tabs.value) t.dispose();
    this.refreshTimer?.();
    this.pictures.dispose();
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
      next = await loadTree(this.store, this.config.settings.peek().showHiddenFiles, stats);
    } catch (e) {
      if (ticket === this.treeReads) throw e; // a superseded read's failure (a folder renamed under it) is moot
    }
    // A superseded read waits for the latest one; that one's failure goes to its own caller.
    if (next === null || ticket !== this.treeReads) {
      await Promise.allSettled([this.latestTreeRead]);
      return;
    }
    // A picture whose size or time changed (or that went away) is read again when next drawn. Stores without
    // change events (a browser folder, the NAS) only tell this way.
    const before = this.stats.peek();
    this.pictures.forgetChanged(before, stats);
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
      // A move under way: read where it leaves the note. One that starts while the read is out waits for it
      // (see relocate), so a slow NAS never gets a read for a path that just moved.
      while (this.moveLock) await this.moveLock;
      if (stale()) continue;
      const reading = this.store.read(path);
      // Whether it worked; the move only waits for it, and this refresh handles a failure below.
      const out = {
        path,
        done: reading.then(
          () => true,
          () => false,
        ),
      };
      this.readsOut.add(out);
      let data: FileData;
      try {
        data = await reading;
      } catch (e) {
        if (e instanceof NotFoundError && !stale()) tab.conflict.value = 'deleted';
        continue;
      } finally {
        this.readsOut.delete(out);
      }
      if (stale()) continue;
      // The listing came before this read, so the tab is at least as new as it says (see the check above).
      if (listed && listed.modified !== 0) this.checkedStats.set(tab, { path, ...listed });
      if (data.version !== null && data.version === tab.version) {
        if (tab.conflict.value === 'deleted') tab.conflict.value = 'none';
        continue;
      }
      const decoded = readText(data.bytes);
      const text = decoded.text;
      if (text === tab.savedText) {
        // Touched, or deleted and put back, with the same text: nothing to resolve. It may have a new encoding.
        tab.adoptVersion(data.version);
        tab.format = decoded.format;
        if (tab.conflict.value === 'deleted') tab.conflict.value = 'none';
        continue;
      }
      if (tab.dirty.value) tab.conflict.value = 'changed';
      else this.replaceText(tab, decoded, data.version);
    }
    await this.config.reload();
  }

  scheduleRefresh(): void {
    this.refreshTimer?.();
    this.refreshTimer = after(150, () => {
      this.refreshTimer = null;
      this.refresh().catch(this.report);
    });
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
      tab.state!.mode.value = this.config.settings.peek().defaultView;
      tab.state!.indentUnit = indentUnit(this.config.settings.peek());
      this.watchAutosave(tab);
      this.watchRecovery(tab);
    }
    this.tabs.value = [...this.tabs.value, tab];
    return tab;
  }

  private watchAutosave(tab: Tab): void {
    let timer: Cancel | null = null;
    tab.onDispose(
      effect(() => {
        void tab.state!.version.value;
        const go =
          tab.dirty.value &&
          tab.conflict.value === 'none' &&
          this.config.settings.value.autoSave &&
          !tab.readOnly;
        timer?.();
        timer = go
          ? after(AUTOSAVE_MS, () => {
              timer = null;
              if (tab.dirty.peek() && this.tabs.peek().includes(tab)) this.save(tab).catch(this.report);
            })
          : null;
      }),
    );
    tab.onDispose(() => timer?.());
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
    let timer: Cancel | null = null;
    let since = 0;
    let lastPath = tab.path.peek();
    const write = () => {
      timer?.();
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
          timer?.();
          timer = null;
          store.remove(path);
          return;
        }
        const now = Date.now();
        if (!timer) since = now;
        else if (now - since >= RECOVERY_MAX_WAIT_MS) return;
        else timer();
        timer = after(RECOVERY_MS, write);
      }),
    );
    this.flushers.set(tab, write);
    tab.onDispose(() => {
      timer?.();
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
      const format = tab.format;
      const bytes = encodeText(text, format);
      const version = force
        ? await this.store.write(path, bytes, { force })
        : await writeOver(
            this.store,
            path,
            bytes,
            tab.version,
            (now) => readText(now).text === before,
            () => this.disposed,
          );
      // Edits made while the write was in flight stay unsaved.
      tab.savedAs(version, text, format);
      const stat = this.stats.peek().get(path);
      const now = Date.now();
      this.stats.value = new Map(this.stats.peek()).set(path, {
        size: text.length,
        modified: now,
        created: stat?.created || now,
      });
      this.flashSaved(tab);
      this.pictures.saved(path, before, text);
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

  async saveAll(): Promise<boolean> {
    let ok = true;
    // A note deleted on disk is saved again (PRD §5); one that changed there is left flagged.
    for (const t of this.dirtyTabs) ok = (await this.save(t, t.conflict.peek() === 'deleted')) && ok;
    return ok;
  }

  private flashSaved(tab: Tab): void {
    tab.saved.value = true;
    after(SAVED_FLASH_MS, () => {
      if (!tab.dirty.peek()) tab.saved.value = false;
    });
  }

  /** Take the disk's version into `tab`, as one undoable step (PRD §5). */
  async reload(tab: Tab): Promise<void> {
    const generation = tab.generation;
    const path = tab.path.value;
    try {
      const data = await this.store.read(path);
      // A save, move or another reload landed while this read was out: the tab is newer than this answer.
      if (tab.generation !== generation || tab.path.peek() !== path || !this.tabs.peek().includes(tab))
        return;
      this.replaceText(tab, readText(data.bytes), data.version);
    } catch (e) {
      this.report(e);
    }
  }

  /** Take the file as read into `tab`: its text as one undoable step, and its encoding for the next save. */
  private replaceText(tab: Tab, file: DecodedText, version: string | null): void {
    this.applyText(tab, file.text);
    tab.format = file.format;
    tab.markSaved(version);
  }

  /** Replace the tab's text as one undoable step, changing only the part that differs (`reloadEdit`). */
  private applyText(tab: Tab, text: string): void {
    const state = tab.state;
    if (!state) return;
    const edit = reloadEdit(state.document.getText(), text, state.selection.peek().caret);
    if (edit) state.apply({ ...edit, anchor: edit.caret });
  }

  // ----- Files and folders -----

  /** Create a note in `dir` and open it. `name` may leave off `.md` (see `noteFileName`). Returns the path. */
  async createNote(dir: string, name: string): Promise<string | null> {
    if (this.disposed) return null;
    const file = noteFileName(name);
    const path = joinPath(dir, file);
    if (!this.checkName(file)) return null;
    return this.exclusive(async () => {
      if (this.disposed || this.taken(path, `“${file}” already exists.`)) return null;
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
    });
  }

  async createFolder(dir: string, name: string): Promise<string | null> {
    if (this.disposed) return null;
    if (!this.checkName(name)) return null;
    const path = joinPath(dir, name);
    return this.exclusive(async () => {
      if (this.disposed || this.taken(path, `“${name}” already exists there.`)) return null;
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
    });
  }

  /** Rename a file or folder in place. Returns the new path. */
  async rename(path: string, name: string): Promise<string | null> {
    if (name === baseName(path)) return path;
    if (!this.checkName(name)) return null;
    return this.relocate(path, (from) => joinPath(parentOf(from), name));
  }

  /**
   * Move a file or folder into `dir` (PRD §6: a plain move; links aren't rewritten). Moving into its own
   * folder or into itself does nothing. Returns the new path.
   */
  async move(path: string, dir: string): Promise<string | null> {
    if (parentOf(path) === dir || isWithin(dir, path)) return null;
    return this.relocate(path, (from) => {
      const into = this.follow(dir);
      return parentOf(from) === into || isWithin(into, from) ? null : joinPath(into, baseName(from));
    });
  }

  /** Whether moving `path` into `dir` would do anything (the drag indicator shows only then). */
  canMove(path: string, dir: string): boolean {
    if (parentOf(path) === dir || isWithin(dir, path)) return false;
    return !findNode(this.tree.value, joinPath(dir, baseName(path)));
  }

  /** Run `fn` holding the move lock, after the moves and new files before it. */
  private async exclusive<T>(fn: () => Promise<T>): Promise<T> {
    while (this.moveLock) await this.moveLock;
    let release!: () => void;
    this.moveLock = new Promise<void>((r) => (release = r));
    try {
      return await fn();
    } finally {
      this.moveLock = null;
      release();
    }
  }

  /** Where `path` is now: a path the tree no longer has follows the moves made since it was read. */
  private follow(path: string): string {
    if (!path || findNode(this.tree.peek(), path)) return path;
    const moves = this.moves;
    let i = moves.length - 1;
    while (i >= 0 && !isWithin(path, moves[i]!.from)) i--;
    for (; i >= 0 && i < moves.length; i++) {
      const m = moves[i]!;
      if (isWithin(path, m.from)) path = rebase(path, m.from, m.to);
    }
    return path;
  }

  /**
   * Move `path` to where `target` says, given where it is by the time the lock is free (a move before it may
   * have taken it, or its folder, elsewhere). `target` returns null when there's nothing to do.
   */
  private async relocate(path: string, target: (from: string) => string | null): Promise<string | null> {
    if (this.disposed) return null;
    const done = await this.exclusive(async () => {
      if (this.disposed) return null;
      const from = this.follow(path);
      const to = target(from);
      if (to === null) return null;
      if (to === from) return { from, to, movedFiles: [] };
      if (this.taken(to, `“${baseName(to)}” already exists there.`)) return null;
      const node = findNode(this.tree.peek(), from);
      const movedFiles = !node ? [] : node.kind === 'file' ? [from] : allFiles(node).map((f) => f.path);
      // Saves already writing to the old path, and a refresh's reads there, land first; later ones wait for
      // the lock (see writeTab and refresh).
      await Promise.all([
        ...this.tabs.peek().flatMap((t) => (isWithin(t.path.peek(), from) ? [this.writeDone.get(t)] : [])),
        ...[...this.readsOut].flatMap((r) => (isWithin(r.path, from) ? [r.done] : [])),
      ]);
      try {
        await this.store.move(from, to);
      } catch (e) {
        this.report(e);
        return null;
      }
      this.moves.push({ from, to });
      if (this.moves.length > MOVES_KEPT) this.moves.shift();
      const moved = this.tabs.value.filter((t) => isWithin(t.path.value, from));
      for (const t of moved) t.path.value = rebase(t.path.value, from, to);
      this.tabs.value = [...this.tabs.value];
      await Promise.all(moved.map((t) => this.adoptMovedVersion(t)));
      this.afterMove(from, to);
      // Before the lock goes, so the next move finds this one's paths in the tree.
      await this.reloadTree();
      return { from, to, movedFiles };
    });
    if (!done) return null;
    const { from, to, movedFiles } = done;
    if (from !== to) await this.relinkPictures(from, to, movedFiles);
    return to;
  }

  /** Paths the notebook keeps (recent, pinned, the Library's folder, open folders, the selection) follow a move. */
  private afterMove(from: string, to: string): void {
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
      await writeOver(
        this.store,
        path,
        encodeText(applyRefEdits(decoded.text, list), decoded.format),
        data.version,
        (now) => sameBytes(now, data.bytes),
        () => this.disposed,
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
    // Gone already (no data): the next refresh flags it.
    const data = await this.store.read(tab.path.value).catch(() => null);
    if (!data || tab.generation !== generation || this.writing.has(tab) || data.version === tab.version)
      return;
    if (readText(data.bytes).text !== tab.savedText) return;
    tab.adoptVersion(data.version);
  }

  /**
   * Delete a file or folder (the shell confirms first). Its tabs close without saving. Where the notebook keeps
   * its own Trash, the status bar says so with an Undo button.
   */
  async remove(path: string): Promise<boolean> {
    const result = await this.deleteEntry(path);
    if (result === false) return false;
    if (result) this.trash.offerUndo([result], `Moved “${baseName(path)}” to the notebook’s Trash.`);
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
    if (this.config.settings.peek().unusedImages === 'ask') {
      for (const p of notes) {
        const text = await this.noteText(p);
        if (text !== null) for (const t of imageTargets(text, p)) if (!isWithin(t, path)) linked.add(t);
      }
    }
    let item: TrashItem | null;
    try {
      item = await this.trash.take(path, node?.kind ?? 'file');
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
    if (linked.size > 0) this.pictures.offerUnused([...linked]).catch(this.report);
    return item;
  }

  private async openElsewhere(path: string): Promise<void> {
    try {
      if (this.store.openExternal) {
        const ask = this.options.confirmOpenExternal;
        if (ask && this.config.settings.peek().confirmOpenExternal && !(await ask(baseName(path)))) return;
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

  /**
   * Whether `path` is in the tree, saying so if it is. Called holding the lock: a dialog checks the name when it
   * is answered, but a new file or move queued before it may have taken the name since.
   */
  private taken(path: string, message: string): boolean {
    if (!findNode(this.tree.peek(), path)) return false;
    this.notify('error', message);
    return true;
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
    this.persistTimer?.();
    this.persistTimer = after(500, () => {
      this.persistTimer = null;
      this.persist().catch(this.report);
    });
  }

  /**
   * Write the session now (before closing the notebook). The session is taken at the call; writes go out one
   * after another, so the last call's session is the one left on disk.
   */
  persist(): Promise<boolean> {
    const bytes = encodeJson(this.session());
    const run = this.persistChain.then(async () => {
      try {
        await ensureStateDir(this.store);
        await this.store.write(sessionPath, bytes, { force: true });
        return true;
      } catch {
        // A read-only notebook just doesn't remember its tabs.
        return false;
      }
    });
    this.persistChain = run;
    return run;
  }

  private async readJson(path: string): Promise<unknown> {
    const data = await this.store.read(path).catch(() => null);
    return data && parseJson(readText(data.bytes).text);
  }

  // ----- Notices -----

  notify(kind: Notice['kind'], text: string): void {
    this.notice.value = { kind, text };
  }

  /** Show a failure in the status bar. Bound, so it can be passed to `.catch` as it is. */
  readonly report = (e: unknown): void => {
    this.notify('error', e instanceof Error ? e.message : String(e));
  };
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

function isString(v: unknown): v is string {
  return typeof v === 'string';
}

export function clampWidth(w: number): number {
  return Math.max(180, Math.min(560, Math.round(w)));
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

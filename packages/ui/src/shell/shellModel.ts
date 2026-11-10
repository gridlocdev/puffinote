import { computed, signal } from '@preact/signals';
import { CommandCatalog, type Command, type CommandSpec } from '../commands/command.ts';
import { formatShortcut, parseShortcut } from '../commands/shortcut.ts';
import { removeRangeEdit } from '../editor/commands.ts';
import { markdownCommandSpecs } from '../editor/editorCommands.ts';
import type { EditorMenuPoint, PictureAt } from '../editor/MarkdownEditor.tsx';
import type { EditorState } from '../editor/editorState.ts';
import { sidebarPanels, type Notebook, type SidebarPanel, type Tab } from '../notebook/notebook.ts';
import { allFolders, fileKind, findNode, hasFolders, noteFileName, type TreeNode } from '../notebook/tree.ts';
import { imageTargets } from '../notebook/attachments.ts';
import { baseName, invalidNameReason, mimeOf, parentOf, resolveRelative, stemOf } from '../platform/paths.ts';
import type { ConfirmOptions, PromptOptions } from './Dialog.tsx';

/** A row in a context menu; `null` draws a separator. */
export interface MenuItem {
  readonly label: string;
  readonly run: () => void;
  readonly shortcut?: string;
  readonly disabled?: boolean;
  readonly danger?: boolean;
  /** A choice that's on: drawn with a tick, and announced as a checked radio item. */
  readonly checked?: boolean;
}

export type Overlay =
  | { readonly kind: 'commands' }
  | { readonly kind: 'quickOpen' }
  | { readonly kind: 'moveTo'; readonly path: string }
  | { readonly kind: 'settings' }
  | { readonly kind: 'unusedPictures'; readonly paths: readonly string[] }
  | { readonly kind: 'prompt'; readonly options: PromptOptions; readonly done: (v: string | null) => void }
  | { readonly kind: 'confirm'; readonly options: ConfirmOptions; readonly done: (v: string) => void }
  | {
      readonly kind: 'menu';
      readonly x: number;
      readonly y: number;
      readonly items: readonly (MenuItem | null)[];
    };

/** What the page around the shell provides (phase 5's hosts). */
export interface ShellHost {
  /** Offer Open Notebook… (a folder picker, the NAS browser). Absent: the command is hidden. */
  readonly openNotebook?: () => void;
  /** Show the window title somewhere besides `document.title` (the desktop window). */
  readonly setTitle?: (title: string) => void;
  /** Draw a title bar at the top of the page (the macOS desktop window, whose own bar is hidden). */
  readonly titleBar?: boolean;
  /** Centre the window's own buttons (the macOS traffic lights) in a title bar this tall. */
  readonly setTitleBarHeight?: (height: number) => void;
}

/** The editor's zoom range and step (1 is 100%), as in Cellestra. */
export const MIN_ZOOM = 0.5;
export const MAX_ZOOM = 3;
const ZOOM_STEP = 0.1;
const ZOOM_KEY = 'puffinote.zoom';

function loadZoom(): number {
  try {
    const z = Number(localStorage.getItem(ZOOM_KEY));
    return Number.isFinite(z) && z >= MIN_ZOOM && z <= MAX_ZOOM ? z : 1;
  } catch {
    return 1;
  }
}

/**
 * The shell's state and verbs, apart from its drawing: which overlay is up, the merged command catalog, and
 * the file and tab actions that need to ask the user something first. One per window.
 */
export class ShellModel {
  readonly notebook = signal<Notebook | null>(null);
  readonly overlay = signal<Overlay | null>(null);
  /** Toggle Tab Bar's session-only answer; null follows the setting (PRD §9). */
  readonly tabBarOverride = signal<boolean | null>(null);
  /** The Filter and Search panels' boxes, kept while the user looks elsewhere. */
  readonly filterQuery = signal('');
  readonly searchQuery = signal('');
  /** Bumps when Filter or Search should put the caret in its box. */
  readonly focusPanelTick = signal(0);
  /** The Library layout's popover under the path bar, if one is open. */
  readonly popover = signal<'outline' | 'recent' | null>(null);
  /** How large notes are drawn (1 is 100%). Remembered across sessions in this browser or app. */
  readonly zoom = signal(loadZoom());
  readonly host: ShellHost;
  /** Keys: the shell's own commands. The editor handles its own keys while it has focus. */
  readonly shellCatalog: CommandCatalog;
  /** ⌘K: the shell's commands merged with the editor's (PRD §8.1). */
  readonly menuCatalog: CommandCatalog;
  private readonly shellIds: ReadonlySet<string>;

  readonly activeTab = computed(() => this.notebook.value?.active.value ?? null);
  readonly activeState = computed<EditorState | null>(() => this.activeTab.value?.state ?? null);
  /** The window's layout (PRD §6): the icon rail with tabs, or the library with one note at a time. */
  readonly layout = computed(() => this.notebook.value?.settings.value.layout ?? 'rail');
  readonly showTabBar = computed(() => {
    const nb = this.notebook.value;
    if (!nb || nb.tabs.value.length === 0 || this.layout.value === 'library') return false;
    return this.tabBarOverride.value ?? nb.settings.value.showTabBar;
  });

  constructor(host: ShellHost = {}) {
    this.host = host;
    const shell = shellCommandSpecs(this);
    this.shellIds = new Set(shell.map((s) => s.id));
    this.shellCatalog = new CommandCatalog(shell);
    const editor = markdownCommandSpecs({
      state: () => this.activeState.peek(),
      focus: () => this.focusEditor(),
      input: () => document.querySelector<HTMLElement>('.pn-shell-main .pn-input'),
      sortTasks: () => this.notebook.peek()?.settings.peek().sortCheckedTasks ?? false,
    });
    this.menuCatalog = new CommandCatalog([...shell, ...editor]);
  }

  /** Whether the Command Menu lists `c` now: an editor command only while a note is in front. */
  offers(c: Command): boolean {
    return this.shellIds.has(c.id) || this.activeState.value !== null;
  }

  setNotebook(nb: Notebook | null): void {
    const old = this.notebook.peek();
    if (old && old !== nb) old.dispose();
    this.tabBarOverride.value = null;
    this.filterQuery.value = '';
    this.searchQuery.value = '';
    this.popover.value = null;
    this.notebook.value = nb;
  }

  // ----- Overlays -----

  private returnFocus: HTMLElement | null = null;

  /**
   * Show `overlay`. One replacing a prompt or confirm that is still up answers it as cancelled first, so
   * whoever waits on it (a close, Open Notebook…, quit) goes on instead of waiting forever.
   */
  open(overlay: Overlay): void {
    const previous = this.overlay.peek();
    // Keys typed before the overlay draws must not land in the note: let go of the editor now, and take the
    // keyboard back to it on close.
    if (!previous && typeof document !== 'undefined') {
      const el = document.activeElement;
      this.returnFocus = el instanceof HTMLElement && el !== document.body ? el : null;
      this.returnFocus?.blur();
    }
    this.overlay.value = overlay;
    if (previous && previous !== overlay) cancelDialog(previous);
  }

  close(): void {
    const previous = this.overlay.peek();
    this.overlay.value = null;
    const el = this.returnFocus;
    this.returnFocus = null;
    if (previous) cancelDialog(previous);
    if (el?.isConnected) el.focus({ preventScroll: true });
  }

  /** Whether a prompt or confirm is up: a question someone is waiting on. */
  get asking(): boolean {
    const kind = this.overlay.peek()?.kind;
    return kind === 'prompt' || kind === 'confirm';
  }

  prompt(options: PromptOptions): Promise<string | null> {
    return new Promise((resolve) => {
      let answered = false;
      const overlay: Overlay = {
        kind: 'prompt',
        options,
        done: (v) => {
          if (answered) return;
          answered = true;
          if (this.overlay.peek() === overlay) this.close();
          resolve(v);
        },
      };
      this.open(overlay);
    });
  }

  confirm(options: ConfirmOptions): Promise<string> {
    return new Promise((resolve) => {
      let answered = false;
      const overlay: Overlay = {
        kind: 'confirm',
        options,
        done: (v) => {
          if (answered) return;
          answered = true;
          if (this.overlay.peek() === overlay) this.close();
          resolve(v);
        },
      };
      this.open(overlay);
    });
  }

  /** Ask before handing `name` to its default app; Open is focused, so Enter opens it (PRD §5). */
  async confirmOpenExternal(name: string): Promise<boolean> {
    const answer = await this.confirm({
      title: `Open “${name}”?`,
      message: 'Puffinote can’t show this file. It opens in its default app.',
      buttons: [
        { label: 'Cancel', value: 'cancel' },
        { label: 'Open', value: 'open', kind: 'primary' },
      ],
    });
    return answer === 'open';
  }

  // ----- Focus -----

  focusEditor(): void {
    setTimeout(() => globalThis.document?.querySelector<HTMLElement>('.pn-shell-main .pn-input')?.focus());
  }

  focusTree(): void {
    const nb = this.notebook.peek();
    if (!nb) return;
    nb.sidebarCollapsed.value = false;
    if (this.layout.peek() === 'library') {
      setTimeout(() => document.querySelector<HTMLElement>('.pn-notes-list')?.focus());
      return;
    }
    if (nb.panel.peek() !== 'files' && nb.panel.peek() !== 'filter') nb.panel.value = 'files';
    // With nothing selected, the keyboard starts on the open note. (Getting focus back, say from a dialog, keeps
    // "nothing selected", so New Note still goes to the top level.)
    const open = nb.active.peek()?.path.peek();
    if (!nb.selected.peek() && open) nb.reveal(open);
    setTimeout(() => document.querySelector<HTMLElement>('.pn-sidebar .pn-tree')?.focus());
  }

  showPanel(panel: SidebarPanel): void {
    const nb = this.notebook.peek();
    if (!nb) return;
    if (this.layout.peek() === 'library') {
      // The library has no panels: Outline and Recent are popovers, and the rest is the note list's box.
      if (panel === 'outline' || panel === 'recent') {
        this.popover.value = this.popover.peek() === panel ? null : panel;
        return;
      }
      nb.sidebarCollapsed.value = false;
      if (panel === 'files') this.focusTree();
      else this.focusPanelTick.value++;
      return;
    }
    nb.sidebarCollapsed.value = false;
    nb.panel.value = panel;
    // The typed-into panels take the caret; the read-only ones leave it be (PRD §6.3).
    if (panel === 'filter' || panel === 'search') this.focusPanelTick.value++;
  }

  /**
   * The context menu for a file or folder (`node`), or for empty space in `dir`, at `at`: the tree's, the
   * library's folders', and the note list's.
   */
  openFileMenu(node: TreeNode | null, dir: string, at: { x: number; y: number }): void {
    const nb = this.notebook.peek();
    if (!nb) return;
    const store = nb.store;
    const items: (MenuItem | null)[] = [];
    if (node?.kind === 'file') {
      items.push({
        label: fileKind(node.path) === 'other' ? 'Open in Default App' : 'Open',
        run: () => void this.openFile(node.path),
      });
      if (store.openExternal && fileKind(node.path) !== 'other') {
        items.push({ label: 'Open in Default App', run: () => void store.openExternal!(node.path) });
      }
      if (this.layout.peek() === 'library') {
        const pinned = nb.pinned.peek().includes(node.path);
        items.push({ label: pinned ? 'Unpin' : 'Pin to Top', run: () => nb.togglePin(node.path) });
      }
      items.push(null);
    }
    items.push(
      { label: 'New Note…', run: () => void this.newNote(dir) },
      { label: 'New Folder…', run: () => void this.newFolder(dir) },
    );
    if (node && node.path) {
      items.push(null, { label: 'Rename…', shortcut: 'F2', run: () => void this.rename(node.path) });
      items.push({ label: 'Move To…', run: () => this.open({ kind: 'moveTo', path: node.path }) });
      if (store.reveal) items.push({ label: revealLabel(), run: () => void store.reveal!(node.path) });
      items.push(null, { label: 'Delete…', danger: true, run: () => void this.remove(node.path) });
    }
    this.open({ kind: 'menu', x: at.x, y: at.y, items });
  }

  /**
   * The editor's right-click menu (PRD §10.1): the clipboard and Select All, from the command catalog. On a
   * picture, the picture's own menu instead (PRD §10.6).
   */
  openEditorMenu(at: EditorMenuPoint): void {
    if (at.picture) {
      this.openPictureMenu(at.picture, at);
      return;
    }
    const item = (id: string, shortcut: string): MenuItem | null => {
      const c = this.menuCatalog.get(id);
      if (!c) return null;
      return {
        label: c.title,
        shortcut,
        disabled: c.enabled ? !c.enabled() : false,
        run: () => void c.run(),
      };
    };
    const key = (k: string) => formatShortcut(parseShortcut(k));
    const items = [
      item('edit.cut', key('mod+x')),
      item('edit.copy', key('mod+c')),
      item('edit.paste', key('mod+v')),
      null,
      item('edit.selectAll', key('mod+a')),
    ];
    this.open({ kind: 'menu', x: at.x, y: at.y, items });
  }

  /** A picture's right-click menu: open it, copy it, show its Markdown, or delete it (PRD §10.6). */
  private openPictureMenu(picture: PictureAt, at: { x: number; y: number }): void {
    const nb = this.notebook.peek();
    const tab = this.activeTab.peek();
    if (!nb || !tab?.state) return;
    const file = this.pictureFile(picture.url, tab.path.peek());
    const items: (MenuItem | null)[] = [
      { label: 'Open Picture', disabled: file === null, run: () => void this.openFile(file!) },
      {
        label: 'Copy Picture',
        run: () =>
          void this.copyPicture(file ?? nb.resolveImage(tab.path.peek(), picture.url), file !== null),
      },
      {
        label: 'View Link Text',
        run: () => {
          tab.state?.select(picture.start, picture.end);
          this.focusEditor();
        },
      },
      null,
      { label: 'Delete Picture', danger: true, run: () => void this.deletePicture(picture) },
    ];
    this.open({ kind: 'menu', x: at.x, y: at.y, items });
  }

  /** The notebook file a picture link written in note `notePath` points at, or null (a web picture, missing). */
  private pictureFile(url: string, notePath: string): string | null {
    if (/^[a-z][a-z0-9+.-]*:/i.test(url)) return null;
    const path = resolveRelative(notePath, url);
    const nb = this.notebook.peek();
    return path !== null && nb && findNode(nb.tree.peek(), path)?.kind === 'file' ? path : null;
  }

  /**
   * Put a picture on the clipboard, as a PNG: a notebook file read through the store (a canvas fed straight
   * from the store's file URL can't be read back), or a web picture by its URL.
   */
  private async copyPicture(source: string, inNotebook: boolean): Promise<void> {
    let url: string | null = null;
    try {
      const nb = this.notebook.peek();
      if (inNotebook && nb) {
        const { bytes } = await nb.store.read(source);
        url = URL.createObjectURL(new Blob([bytes as Uint8Array<ArrayBuffer>], { type: mimeOf(source) }));
      }
      const img = new Image();
      img.crossOrigin = 'anonymous';
      img.src = url ?? source;
      await img.decode();
      const canvas = document.createElement('canvas');
      canvas.width = img.naturalWidth;
      canvas.height = img.naturalHeight;
      canvas.getContext('2d')!.drawImage(img, 0, 0);
      const png = await new Promise<Blob>((resolve, reject) =>
        canvas.toBlob((b) => (b ? resolve(b) : reject(new Error('no image'))), 'image/png'),
      );
      await navigator.clipboard.write([new ClipboardItem({ 'image/png': png })]);
    } catch {
      this.notebook.peek()?.notify('error', 'Puffinote couldn’t copy this picture.');
    } finally {
      if (url) URL.revokeObjectURL(url);
    }
  }

  /**
   * Delete Picture: take the picture out of the note and delete its file. When another note (or another spot in
   * this one) shows the same file, ask first, listing those notes; the default keeps the file.
   */
  async deletePicture(picture: PictureAt): Promise<void> {
    const nb = this.notebook.peek();
    const tab = this.activeTab.peek();
    const state = tab?.state;
    if (!nb || !tab || !state) return;
    const notePath = tab.path.peek();
    const written = state.document.getText(picture.start, picture.end - picture.start);
    const file = this.pictureFile(picture.url, notePath);
    let deleteFile = file !== null;
    if (file !== null) {
      const text = state.text;
      const hereAgain = imageTargets(text.slice(0, picture.start) + text.slice(picture.end), notePath).has(
        file,
      );
      const others = await nb.notesShowing(file, notePath);
      if (hereAgain || others.length > 0) {
        const names = others.map((p) => p.replace(/\.[^./]+$/, ''));
        if (hereAgain) names.unshift(`${stemOf(notePath)} (this note, elsewhere)`);
        const trash = nb.store.kind === 'native';
        const answer = await this.confirm({
          title: `“${baseName(file)}” is in other notes`,
          message:
            `Keep File takes it out of this note only. Delete File ${trash ? 'moves the file to the Trash' : 'deletes the file'}, ` +
            `so it's gone from ${names.length === 1 ? 'that note' : 'those notes'} too:`,
          list: names,
          buttons: [
            { label: 'Cancel', value: 'cancel' },
            { label: 'Delete File', value: 'file', kind: 'danger' },
            { label: 'Keep File', value: 'link', kind: 'primary' },
          ],
        });
        if (answer === 'cancel') return;
        deleteFile = answer === 'file';
      }
    }
    // Keep the file's bytes, so Undo can put it back and Redo delete it again.
    let bytes: Uint8Array | null = null;
    if (deleteFile && file !== null) {
      try {
        bytes = (await nb.store.read(file)).bytes;
      } catch {
        // Unreadable: it's deleted all the same, and Undo brings back only the link.
      }
    }
    // The note may have changed while the question was open: only take out the picture if it's still there.
    if (state.document.getText(picture.start, picture.end - picture.start) !== written) return;
    const effect =
      file !== null && bytes !== null
        ? {
            undo: () => void nb.restorePicture(file, bytes),
            redo: () => void nb.removeAll([file]),
          }
        : undefined;
    state.apply(removeRangeEdit(state.document, picture.start, picture.end), effect);
    this.focusEditor();
    if (deleteFile && file !== null) await nb.removeAll([file]);
  }

  /** Set the zoom, clamped and rounded to whole percents, and remember it. */
  setZoom(z: number): void {
    const v = Math.round(Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, z)) * 100) / 100;
    this.zoom.value = v;
    try {
      if (v === 1) localStorage.removeItem(ZOOM_KEY);
      else localStorage.setItem(ZOOM_KEY, String(v));
    } catch {
      // Storage disabled: the zoom lasts until the page closes.
    }
  }

  toggleSidebar(): void {
    const nb = this.notebook.peek();
    if (!nb) return;
    nb.sidebarCollapsed.value = !nb.sidebarCollapsed.peek();
    if (nb.sidebarCollapsed.peek()) this.focusEditor();
  }

  // ----- Tabs -----

  async openFile(path: string): Promise<void> {
    const nb = this.notebook.peek();
    if (!nb) return;
    const tab = await nb.openFile(path);
    if (tab?.state) this.focusEditor();
  }

  /** Close `tab`, asking about unsaved edits. Resolves false when the user cancelled. */
  async closeTab(tab: Tab): Promise<boolean> {
    const nb = this.notebook.peek();
    if (!nb) return false;
    if (tab.dirty.peek()) {
      nb.activate(tab);
      const answer = await this.confirm({
        title: `Save changes to “${tab.name}”?`,
        message: 'Your changes will be lost if you don’t save them.',
        buttons: [
          { label: 'Don’t Save', value: 'discard', kind: 'danger' },
          { label: 'Cancel', value: 'cancel' },
          { label: 'Save', value: 'save', kind: 'primary' },
        ],
      });
      if (answer === 'cancel') return false;
      if (answer === 'save' && !(await nb.save(tab, tab.conflict.peek() === 'deleted'))) return false;
    }
    nb.closeTab(tab);
    if (nb.active.peek()?.state) this.focusEditor();
    return true;
  }

  async closeTabs(keep: Tab | null): Promise<void> {
    const nb = this.notebook.peek();
    if (!nb) return;
    if (keep) nb.activate(keep);
    for (const t of [...nb.tabs.peek()]) {
      // Pinned tabs stay (PRD §7); ⌘W or the tab's own menu closes one.
      if (t === keep || t.pinned.peek()) continue;
      if (!(await this.closeTab(t))) return;
    }
  }

  /** Ask about every unsaved tab before the notebook goes away. Resolves false on Cancel. */
  async confirmDiscardAll(): Promise<boolean> {
    const nb = this.notebook.peek();
    if (!nb) return true;
    const dirty = nb.dirtyTabs;
    if (dirty.length === 0) return true;
    const answer = await this.confirm({
      title:
        dirty.length === 1
          ? `Save changes to “${dirty[0]!.name}”?`
          : `Save changes to ${dirty.length} notes?`,
      message: 'Your changes will be lost if you don’t save them.',
      buttons: [
        { label: 'Don’t Save', value: 'discard', kind: 'danger' },
        { label: 'Cancel', value: 'cancel' },
        { label: 'Save All', value: 'save', kind: 'primary' },
      ],
    });
    if (answer === 'cancel') return false;
    if (answer === 'save') return nb.saveAll();
    return true;
  }

  // ----- Files -----

  /** Where New Note and New Folder put things: the selected folder, or the selected file's folder. */
  targetDir(): string {
    const nb = this.notebook.peek();
    const sel = nb?.selected.peek();
    if (!nb || !sel) return '';
    const node = findNode(nb.tree.peek(), sel);
    if (!node) return '';
    return node.kind === 'dir' ? node.path : parentOf(node.path);
  }

  async newNote(dir = this.targetDir()): Promise<void> {
    const nb = this.notebook.peek();
    if (!nb) return;
    const taken = (name: string) => findNode(nb.tree.peek(), dir ? `${dir}/${name}` : name) !== null;
    const name = await this.prompt({
      title: 'New Note',
      label: dir ? `Name (in ${dir})` : 'Name',
      value: 'Untitled',
      confirm: 'Create',
      validate: (v) => {
        const file = noteFileName(v);
        return invalidNameReason(file) ?? (taken(file) ? `“${file}” already exists.` : null);
      },
    });
    if (name === null) return;
    if (await nb.createNote(dir, name)) this.focusEditor();
  }

  async newFolder(dir = this.targetDir()): Promise<void> {
    const nb = this.notebook.peek();
    if (!nb) return;
    const name = await this.prompt({
      title: 'New Folder',
      label: dir ? `Name (in ${dir})` : 'Name',
      value: 'New Folder',
      confirm: 'Create',
      validate: (v) =>
        invalidNameReason(v) ??
        (findNode(nb.tree.peek(), dir ? `${dir}/${v}` : v) ? `“${v}” already exists.` : null),
    });
    if (name === null) return;
    // Like Finder: the new folder is selected with the keyboard on it, ready to rename or fill (PRD §6.2).
    if ((await nb.createFolder(dir, name)) && this.layout.peek() === 'rail') this.focusTree();
  }

  /** The folders `path` can move into (PRD §6.1), the top level (`''`) first. */
  moveTargets(path: string): string[] {
    const nb = this.notebook.peek();
    if (!nb) return [];
    return ['', ...allFolders(nb.tree.peek())].filter((dir) => nb.canMove(path, dir));
  }

  async rename(path: string): Promise<void> {
    const nb = this.notebook.peek();
    if (!nb || !path) return;
    const name = baseName(path);
    const node = findNode(nb.tree.peek(), path);
    const stem = node?.kind === 'file' ? stemOf(name).length : name.length;
    const next = await this.prompt({
      title: 'Rename',
      label: 'Name',
      value: name,
      select: [0, stem || name.length],
      confirm: 'Rename',
      validate: (v) =>
        invalidNameReason(v) ??
        (v !== name && findNode(nb.tree.peek(), parentOf(path) ? `${parentOf(path)}/${v}` : v)
          ? `“${v}” already exists.`
          : null),
    });
    if (next !== null) await nb.rename(path, next);
  }

  async remove(path: string): Promise<void> {
    const nb = this.notebook.peek();
    if (!nb || !path) return;
    const node = findNode(nb.tree.peek(), path);
    const trash = nb.store.kind === 'native';
    const what = node?.kind === 'dir' ? 'folder and everything in it' : 'file';
    const answer = await this.confirm({
      title: `Delete “${baseName(path)}”?`,
      message: trash
        ? `The ${what} will be moved to the Trash.`
        : `The ${what} will be deleted. This can’t be undone.`,
      buttons: [
        { label: 'Cancel', value: 'cancel' },
        { label: trash ? 'Move to Trash' : 'Delete', value: 'delete', kind: 'danger' },
      ],
    });
    if (answer === 'delete') await nb.remove(path);
  }

  /** Find Unused Pictures… (PRD §10.6): list them, or say there are none. */
  async findUnusedPictures(): Promise<void> {
    const nb = this.notebook.peek();
    if (!nb) return;
    const paths = await nb.unusedPictures();
    if (paths.length === 0) nb.notify('info', 'Every picture in the notebook is used by a note.');
    else this.open({ kind: 'unusedPictures', paths });
  }

  /** Expand every folder, or collapse them all when all are open (the tree header's toggle, PRD §6). */
  toggleAllFolders(): void {
    const nb = this.notebook.peek();
    if (!nb || !hasFolders(nb.tree.peek())) return;
    nb.setExpanded(this.allExpanded() ? [] : allFolders(nb.tree.peek()));
  }

  allExpanded(): boolean {
    const nb = this.notebook.value;
    if (!nb) return false;
    const folders = allFolders(nb.tree.value);
    const open = nb.expanded.value;
    return folders.length > 0 && folders.every((f) => open.has(f));
  }

  /** The window title: the document, its unsaved dot or "— Saved" (PRD §7), and the notebook. */
  readonly title = computed(() => {
    const nb = this.notebook.value;
    if (!nb) return 'Puffinote';
    const tab = nb.active.value;
    if (!tab) return `${nb.name} — Puffinote`;
    const mark = tab.dirty.value ? ' •' : tab.saved.value ? ' — Saved' : '';
    return `${tab.name}${mark} — ${nb.name}`;
  });
}

/** What the file manager is called here. */
export function revealLabel(): string {
  const p = typeof navigator !== 'undefined' ? navigator.platform : '';
  return /Mac/.test(p) ? 'Reveal in Finder' : /Win/.test(p) ? 'Show in Explorer' : 'Show in Folder';
}

/** Answer a prompt or confirm that is going away unanswered: a prompt with null, a confirm with 'cancel'. */
function cancelDialog(o: Overlay): void {
  if (o.kind === 'prompt') o.done(null);
  else if (o.kind === 'confirm') o.done('cancel');
}

/** The shell's own commands (PRD §8.1). Ids start `shell.` or `view.sidebar.`, apart from the editor's. */
function shellCommandSpecs(m: ShellModel): CommandSpec[] {
  const nb = () => m.notebook.peek();
  const hasNb = () => nb() !== null;
  const tab = () => m.activeTab.peek();
  const hasTab = () => tab() !== null;
  const specs: CommandSpec[] = [
    {
      id: 'shell.commandMenu',
      title: 'Command Menu',
      description: 'Search and run any command.',
      group: 'Go',
      shortcut: 'mod+k',
      hidden: true,
      global: true,
      run: () => m.open({ kind: 'commands' }),
    },
    {
      id: 'shell.quickOpen',
      title: 'Go to File…',
      description: 'Open any file in the notebook by typing part of its name.',
      group: 'Go',
      shortcut: 'mod+o',
      icon: 'search',
      keywords: 'quick open find file jump',
      global: true,
      enabled: hasNb,
      run: () => m.open({ kind: 'quickOpen' }),
    },
    {
      id: 'shell.newNote',
      title: 'New Note…',
      description: 'Create a note in the selected folder.',
      group: 'File',
      shortcut: 'mod+n',
      icon: 'file-plus',
      global: true,
      enabled: hasNb,
      run: () => m.newNote(),
    },
    {
      id: 'shell.newFolder',
      title: 'New Folder…',
      description: 'Create a folder in the selected folder.',
      group: 'File',
      shortcut: 'mod+shift+n',
      icon: 'folder-plus',
      global: true,
      enabled: hasNb,
      run: () => m.newFolder(),
    },
    {
      id: 'shell.save',
      title: 'Save',
      description: 'Save the note in front.',
      group: 'File',
      shortcut: 'mod+s',
      icon: 'save',
      global: true,
      enabled: () => tab()?.state != null && !tab()!.readOnly,
      run: async () => {
        const t = tab();
        if (t) await nb()?.save(t, t.conflict.peek() === 'deleted');
      },
    },
    {
      id: 'shell.saveAll',
      title: 'Save All',
      description: 'Save every note with unsaved changes.',
      group: 'File',
      enabled: hasNb,
      run: async () => void (await nb()?.saveAll()),
    },
    {
      id: 'shell.revert',
      title: 'Revert to Saved',
      description: 'Replace the note with what is on disk. Undo brings your version back.',
      group: 'File',
      icon: 'refresh-cw',
      keywords: 'reload discard',
      enabled: () => tab()?.state != null,
      run: async () => {
        const t = tab();
        if (t) await nb()?.reload(t);
      },
    },
    {
      id: 'shell.closeTab',
      title: 'Close Tab',
      description: 'Close the tab in front.',
      group: 'File',
      shortcut: 'mod+w',
      icon: 'x',
      global: true,
      enabled: hasTab,
      run: async () => {
        const t = tab();
        if (t) await m.closeTab(t);
      },
    },
    {
      id: 'shell.pinTab',
      title: 'Pin Tab',
      description: 'Keep the note in front at the start of the tab strip, always in view.',
      group: 'File',
      icon: 'pin',
      enabled: hasTab,
      active: () => tab()?.pinned.value ?? false,
      run: () => {
        const t = tab();
        if (t) nb()?.setTabPinned(t, !t.pinned.peek());
      },
    },
    {
      id: 'shell.closeOtherTabs',
      title: 'Close Other Tabs',
      description: 'Close every tab but the one in front and the pinned ones.',
      group: 'File',
      enabled: () => (nb()?.tabs.peek().length ?? 0) > 1,
      run: () => m.closeTabs(tab()),
    },
    {
      id: 'shell.closeAllTabs',
      title: 'Close All Tabs',
      description: 'Close every tab but the pinned ones.',
      group: 'File',
      shortcut: 'mod+alt+w',
      global: true,
      enabled: hasTab,
      run: () => m.closeTabs(null),
    },
    {
      id: 'shell.rename',
      title: 'Rename…',
      description: 'Rename the selected file or folder, or the note in front.',
      group: 'File',
      icon: 'pencil',
      enabled: () => hasNb() && !!(nb()!.selected.peek() ?? tab()?.path.peek()),
      run: () => m.rename(nb()!.selected.peek() ?? tab()!.path.peek()),
    },
    {
      id: 'shell.delete',
      title: 'Delete…',
      description: 'Delete the selected file or folder.',
      group: 'File',
      icon: 'trash-2',
      enabled: () => !!nb()?.selected.peek(),
      run: () => m.remove(nb()!.selected.peek()!),
    },
    {
      id: 'shell.unusedPictures',
      title: 'Find Unused Pictures…',
      description: 'List the pictures no note links to, and move the ones you pick to the Trash.',
      group: 'File',
      icon: 'image',
      enabled: hasNb,
      run: () => m.findUnusedPictures(),
    },
    {
      id: 'shell.revealActive',
      title: 'Reveal Active File in Sidebar',
      description: 'Show the note in front in the file tree.',
      group: 'Go',
      enabled: hasTab,
      run: () => {
        const n = nb();
        const t = tab();
        if (!n || !t) return;
        m.showPanel('files');
        n.reveal(t.path.peek());
        if (m.layout.peek() === 'library') n.scope.value = `folder:${parentOf(t.path.peek())}`;
      },
    },
    {
      id: 'shell.refresh',
      title: 'Refresh Notebook',
      description: 'Read the folder again and pick up changes made outside Puffinote.',
      group: 'File',
      icon: 'refresh-cw',
      enabled: hasNb,
      run: async () => void (await nb()?.refresh()),
    },
    {
      id: 'shell.nextTab',
      title: 'Next Tab',
      description: 'Show the tab to the right.',
      group: 'Go',
      shortcut: 'mod+shift+]',
      alternates: ['ctrl+tab'],
      global: true,
      enabled: () => (nb()?.tabs.peek().length ?? 0) > 1,
      run: () => nb()?.cycleTab(1),
    },
    {
      id: 'shell.previousTab',
      title: 'Previous Tab',
      description: 'Show the tab to the left.',
      group: 'Go',
      shortcut: 'mod+shift+[',
      alternates: ['ctrl+shift+tab'],
      global: true,
      enabled: () => (nb()?.tabs.peek().length ?? 0) > 1,
      run: () => nb()?.cycleTab(-1),
    },
    {
      id: 'shell.focusTree',
      title: 'Focus File Tree',
      description: 'Move the keyboard to the sidebar’s file tree.',
      group: 'Go',
      shortcut: 'mod+alt+s',
      global: true,
      enabled: hasNb,
      run: () => m.focusTree(),
    },
    {
      id: 'shell.focusEditor',
      title: 'Focus Editor',
      description: 'Move the keyboard to the note in front.',
      group: 'Go',
      shortcut: 'mod+alt+e',
      global: true,
      enabled: () => tab()?.state != null,
      run: () => m.focusEditor(),
    },
    {
      id: 'view.toggleSidebar',
      title: 'Toggle Sidebar',
      description: 'Hide or show the sidebar.',
      group: 'View',
      shortcut: 'mod+\\',
      icon: 'panel-left',
      global: true,
      enabled: hasNb,
      active: () => nb()?.sidebarCollapsed.peek() === false,
      run: () => m.toggleSidebar(),
    },
    {
      id: 'view.toggleTabBar',
      title: 'Toggle Tab Bar',
      description: 'Hide or show the tab strip for this session.',
      group: 'View',
      enabled: () => m.layout.peek() === 'rail',
      active: () => m.tabBarOverride.peek() ?? nb()?.settings.peek().showTabBar ?? true,
      run: () => {
        const shown = m.tabBarOverride.peek() ?? nb()?.settings.peek().showTabBar ?? true;
        m.tabBarOverride.value = !shown;
      },
    },
    {
      id: 'view.zoomIn',
      title: 'Zoom In',
      description: 'Make notes bigger.',
      group: 'View',
      shortcut: 'mod+=',
      alternates: ['mod++'],
      icon: 'zoom-in',
      keywords: 'bigger larger text size',
      global: true,
      enabled: () => m.zoom.peek() < MAX_ZOOM,
      run: () => m.setZoom(m.zoom.peek() + ZOOM_STEP),
    },
    {
      id: 'view.zoomOut',
      title: 'Zoom Out',
      description: 'Make notes smaller.',
      group: 'View',
      shortcut: 'mod+-',
      icon: 'zoom-out',
      keywords: 'smaller text size',
      global: true,
      enabled: () => m.zoom.peek() > MIN_ZOOM,
      run: () => m.setZoom(m.zoom.peek() - ZOOM_STEP),
    },
    {
      id: 'view.zoomReset',
      title: 'Actual Size',
      description: 'Reset the zoom to 100%.',
      group: 'View',
      keywords: 'zoom reset 100%',
      global: true,
      enabled: () => m.zoom.peek() !== 1,
      run: () => m.setZoom(1),
    },
    ...sidebarPanels.map((p): CommandSpec => ({
      id: `view.sidebar.${p.id}`,
      title: `${p.title} (Sidebar)`,
      description: `Show the sidebar’s ${p.title} panel.`,
      group: 'View',
      icon: p.icon,
      enabled: hasNb,
      active: () => nb()?.panel.peek() === p.id && !nb()!.sidebarCollapsed.peek(),
      run: () => m.showPanel(p.id),
    })),
    {
      id: 'view.sidebar.settings',
      title: 'Settings…',
      description: 'Change preferences and this notebook’s settings.',
      group: 'View',
      shortcut: 'mod+,',
      icon: 'settings-2',
      keywords: 'preferences options',
      global: true,
      run: () => m.open({ kind: 'settings' }),
    },
  ];
  if (m.host.openNotebook) {
    const openNotebook = m.host.openNotebook;
    specs.splice(1, 0, {
      id: 'shell.openNotebook',
      title: 'Open Notebook…',
      description: 'Open another folder as the notebook.',
      group: 'File',
      shortcut: 'mod+shift+o',
      icon: 'folder-open',
      global: true,
      run: async () => {
        if (await m.confirmDiscardAll()) openNotebook();
      },
    });
  }
  return specs;
}

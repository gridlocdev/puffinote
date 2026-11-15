import { computed, effect, signal } from '@preact/signals';
import { CommandCatalog, type Command, type CommandSpec, type CommandTarget } from '../commands/command.ts';
import { formatShortcut, isMac, parseShortcut } from '../commands/shortcut.ts';
import { outlineOf } from '@puffinote/markdown';
import { removeRangeEdit } from '../editor/commands.ts';
import { markdownCommandSpecs } from '../editor/editorCommands.ts';
import type { EditorMenuPoint } from '../editor/MarkdownEditor.tsx';
import type { EditorState } from '../editor/editorState.ts';
import { embedRefName, isPictureToken, pictureWithUrlAt, type PictureAt } from '../editor/reveal.ts';
import { findAll, matchAt, matchFrom, replaceAllEdit, type TextRange } from '../editor/find.ts';
import { sidebarPanels, type Notebook, type SidebarPanel } from '../notebook/notebook.ts';
import type { Tab } from '../notebook/tab.ts';
import { folderOf } from '../notebook/library.ts';
import { headingFor, parseLinkHref, resolveNoteLink } from '../notebook/noteLinks.ts';
import type { TrashItem } from '../notebook/trash.ts';
import { hasScheme } from '../notebook/urls.ts';
import {
  allFiles,
  allFolders,
  fileKind,
  findNode,
  hasFolders,
  noteFileName,
  type TreeNode,
} from '../notebook/tree.ts';
import {
  baseName,
  invalidNameReason,
  isWithin,
  mimeOf,
  parentOf,
  resolveRelative,
  stemOf,
} from '../platform/paths.ts';
import type { ImportFile } from '../notebook/importFiles.ts';
import type { ConfirmOptions, PromptOptions } from './Dialog.tsx';
import { LayoutPolicy } from './layoutPolicy.ts';
import { NotebookSession } from './notebookSession.ts';

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
  | { readonly kind: 'trash'; readonly items: readonly TrashItem[] }
  | { readonly kind: 'prompt'; readonly options: PromptOptions; readonly done: (v: string | null) => void }
  | { readonly kind: 'confirm'; readonly options: ConfirmOptions; readonly done: (v: string) => void }
  | {
      readonly kind: 'menu';
      readonly x: number;
      readonly y: number;
      readonly items: readonly (MenuItem | null)[];
    };

/** A picture of the note in front, shown on its own (PRD §10.6). */
export interface PicturePreview {
  readonly picture: PictureAt;
  /** The tab whose note shows it; the preview closes when another tab comes to the front. */
  readonly tab: Tab;
  /** A URL the page can load it from. */
  readonly src: string;
  /** Its file name, or its URL when it isn't a notebook file. */
  readonly name: string;
}

/** What the page around the shell provides (phase 5's hosts). */
export interface ShellHost {
  /** Offer Open Notebook… (a folder picker, the NAS browser). Absent: the command is hidden. */
  readonly openNotebook?: () => void;
  /**
   * Ask for files to import, a folder's or separate ones (the browser's file dialog). Absent: Import Notes… is
   * hidden (the desktop app, whose menu bar can't bring up a page's file dialog).
   */
  readonly pickFiles?: (folder: boolean) => Promise<readonly ImportFile[] | null>;
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
  /** Which notebook the window shows, the switch to another, and the view state that goes with one. */
  readonly session: NotebookSession = new NotebookSession({
    leaving: () => {
      if (this.overlay.peek()) this.close();
      this.preview.value = null;
      this.findQuery.value = null;
      this.replaceOpen.value = false;
    },
    entered: () => (this.layoutPolicy.tabBarOverride.value = null),
    confirmDiscardAll: () => this.confirmDiscardAll(),
  });
  readonly notebook = this.session.notebook;
  readonly overlay = signal<Overlay | null>(null);
  /** The picture preview (PRD §10.6): under any menu or dialog, over everything else. */
  readonly preview = signal<PicturePreview | null>(null);
  /** Bumps when Filter or Search should put the caret in its box. */
  readonly focusPanelTick = signal(0);
  /** Find in Note's box (PRD §10.7): its text while the bar is open, null while it is closed. */
  readonly findQuery = signal<string | null>(null);
  /** Whether the find bar shows its Replace row (Find and Replace…, PRD §10.7). */
  readonly replaceOpen = signal(false);
  /** The Replace box's text, kept while the bar is closed. */
  readonly replaceText = signal('');
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
  /** The matches of the find bar's text in the note in front, searched again only when the text or query changes. */
  private readonly findHits = computed<readonly TextRange[] | null>(() => {
    const query = this.findQuery.value;
    const state = this.activeState.value;
    if (!query || !state) return null;
    void state.version.value;
    return findAll(state.text, query);
  });
  /** The matches of the find bar's text in the note in front, and the one the selection is on (or -1). */
  readonly findMatches = computed<{ matches: readonly TextRange[]; current: number } | null>(() => {
    const matches = this.findHits.value;
    const state = this.activeState.value;
    if (!matches || !state) return null;
    const sel = state.selection.value;
    return {
      matches,
      current: matchAt(matches, Math.min(sel.anchor, sel.caret), Math.max(sel.anchor, sel.caret)),
    };
  });
  /** The window's layout (PRD §6), its tab strip, and the Library layout's one note at a time. */
  readonly layoutPolicy = new LayoutPolicy(this.notebook);
  readonly layout = this.layoutPolicy.layout;
  readonly showTabBar = this.layoutPolicy.showTabBar;
  /** Whether the find bar has text: Find Next and Previous follow this, not every letter typed. */
  private readonly hasFindText = computed(() => !!this.findQuery.value);
  /**
   * Everything the commands' enabled and checked states read, as one signal that changes when any of them
   * does. A host that mirrors the catalog in a native menu rebuilds it on this. Add here what a new command's
   * `enabled` or `active` reads.
   */
  readonly menuInputs = computed(() => {
    const nb = this.notebook.value;
    const tab = this.activeTab.value;
    return [
      nb,
      tab,
      tab?.dirty.value,
      tab?.pinned.value,
      this.activeState.value?.mode.value,
      this.layout.value,
      this.showTabBar.value,
      nb?.tabs.value,
      nb?.selected.value,
      nb?.pinned.value,
      nb?.sidebarCollapsed.value,
      this.zoom.value,
      this.hasFindText.value,
      this.replaceOpen.value,
    ];
  });

  constructor(host: ShellHost = {}) {
    this.host = host;
    const shell = [...shellCommandSpecs(this), ...this.pictureCommandSpecs()];
    this.shellIds = new Set(shell.map((s) => s.id));
    this.shellCatalog = new CommandCatalog(shell);
    const editor = markdownCommandSpecs({
      state: () => this.activeState.peek(),
      focus: () => this.focusEditor(),
      input: () => document.querySelector<HTMLElement>('.pn-shell-main .pn-input'),
      sortTasks: () => this.notebook.peek()?.config.settings.peek().sortCheckedTasks ?? false,
    });
    this.menuCatalog = new CommandCatalog([...shell, ...editor]);
    // A question someone waits on (Save changes?, a name) takes no commands, from keys, menus or the palette:
    // answer it first.
    this.shellCatalog.gate = this.menuCatalog.gate = () => !this.asking;
    // The preview's offsets belong to the note it was opened from: another note in front closes it.
    effect(() => {
      const tab = this.activeTab.value;
      if (this.preview.peek() && this.preview.peek()!.tab !== tab) this.preview.value = null;
    });
  }

  /** Whether the Command Menu lists `c` now: an editor command only while a note is in front. */
  offers(c: Command): boolean {
    return this.shellIds.has(c.id) || this.activeState.value !== null;
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

  // ----- Find in Note (PRD §10.7) -----

  /** The find bar's text when it was last closed, for the next ⌘F with nothing selected. */
  private lastFind = '';

  /**
   * Open the find bar with the caret in its box, starting from the selected text when it is on one line. With
   * `replace`, the bar shows its Replace row too, and the caret goes to the Replace box when there is text to
   * find. ⌘F alone keeps the Replace row as it was.
   */
  openFind(replace = false): void {
    const state = this.activeState.peek();
    if (!state) return;
    const sel = state.selection.peek();
    const picked = state.text.slice(Math.min(sel.anchor, sel.caret), Math.max(sel.anchor, sel.caret));
    if (picked && !picked.includes('\n') && picked.length <= 200) this.findQuery.value = picked;
    else if (this.findQuery.peek() === null) this.findQuery.value = this.lastFind;
    if (replace) this.replaceOpen.value = true;
    const which = replace && this.findQuery.peek() ? '.pn-find-replace input' : '.pn-find input';
    setTimeout(() => {
      const box = globalThis.document?.querySelector<HTMLInputElement>(which);
      box?.focus();
      box?.select();
    });
  }

  /** Whether the note in front can take a replace: a note, not a picture, that Puffinote will save. */
  canReplace(): boolean {
    const tab = this.activeTab.peek();
    return tab?.state != null && !tab.readOnly && !!this.findQuery.peek();
  }

  /**
   * Replace (PRD §10.7): the match the selection is on becomes the Replace box's text, as one undo step, and
   * the next match is selected. With the selection on no match, it only goes to the next one.
   */
  replaceCurrent(): void {
    const state = this.activeState.peek();
    const query = this.findQuery.peek();
    if (!state || !query || !this.canReplace()) return;
    const sel = state.selection.peek();
    const start = Math.min(sel.anchor, sel.caret);
    const end = Math.max(sel.anchor, sel.caret);
    const matches = findAll(state.text, query);
    if (matchAt(matches, start, end) === -1) {
      this.findStep(0);
      return;
    }
    const text = this.replaceText.peek();
    const after = start + text.length;
    state.apply({ start, length: end - start, text, anchor: after, caret: after });
    const next = findAll(state.text, query);
    const i = matchFrom(next, after, 0);
    if (i >= 0) state.reveal(next[i]!.start, next[i]!.end);
  }

  /** Replace All (PRD §10.7): every match, as one undo step. Says how many it replaced. */
  replaceAll(): void {
    const state = this.activeState.peek();
    const query = this.findQuery.peek();
    if (!state || !query || !this.canReplace()) return;
    const matches = findAll(state.text, query, Infinity);
    const edit = replaceAllEdit(state.text, matches, this.replaceText.peek());
    if (edit) state.apply(edit);
    state.requestReveal();
    const n = matches.length;
    this.notebook
      .peek()
      ?.notify(
        'info',
        n === 0 ? 'Nothing to replace' : n === 1 ? 'Replaced 1 match' : `Replaced ${n} matches`,
      );
  }

  /** The find bar's text changed: select the first match from where the selection starts. */
  setFindQuery(query: string): void {
    this.findQuery.value = query;
    this.findStep(0);
  }

  /** Select the next match (1), the previous one (-1), or the one at or after the selection (0). */
  findStep(dir: -1 | 0 | 1): void {
    const state = this.activeState.peek();
    const query = this.findQuery.peek();
    if (!state || !query) return;
    const matches = findAll(state.text, query);
    const sel = state.selection.peek();
    const i = matchFrom(matches, Math.min(sel.anchor, sel.caret), dir);
    if (i >= 0) state.reveal(matches[i]!.start, matches[i]!.end);
  }

  /** Close the find bar; the match stays selected, and the caret goes back to the note. */
  closeFind(): void {
    this.lastFind = this.findQuery.peek() ?? '';
    this.findQuery.value = null;
    this.replaceOpen.value = false;
    if (this.activeState.peek()) this.focusEditor();
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
        this.session.popover.value = this.session.popover.peek() === panel ? null : panel;
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
    const item = (id: string, target: CommandTarget, extra?: Partial<MenuItem>) =>
      this.commandItem(id, target, extra);
    const items: (MenuItem | null)[] = [];
    if (node?.kind === 'file') {
      const path = node.path;
      items.push(
        item('shell.openFile', { path }, fileKind(path) === 'other' ? { label: 'Open in Default App' } : {}),
      );
      if (store.openExternal && fileKind(path) !== 'other') items.push(item('shell.openExternal', { path }));
      if (this.layout.peek() === 'library') {
        items.push(
          item(
            'shell.pinToTop',
            { path },
            { label: nb.pinned.peek().includes(path) ? 'Unpin' : 'Pin to Top' },
          ),
        );
      }
      items.push(null);
    }
    items.push(
      item('shell.newNote', { dir }, { shortcut: undefined }),
      item('shell.newFolder', { dir }, { shortcut: undefined }),
    );
    if (node && node.path) {
      const path = node.path;
      items.push(null, item('shell.rename', { path }, { shortcut: 'F2' }), item('shell.moveTo', { path }));
      if (store.reveal) items.push(item('shell.revealInFolder', { path }));
      items.push(null, item('shell.delete', { path }, { danger: true }));
    }
    this.open({ kind: 'menu', x: at.x, y: at.y, items });
  }

  /** A tab's right-click menu: pin it, or close it, the others, or all of them. */
  openTabMenu(tab: Tab, at: { x: number; y: number }): void {
    const target = { tabId: tab.id };
    const items = [
      this.commandItem('shell.pinTab', target, { label: tab.pinned.peek() ? 'Unpin Tab' : 'Pin Tab' }),
      null,
      this.commandItem('shell.closeTab', target, { shortcut: undefined }),
      this.commandItem('shell.closeOtherTabs', target),
      this.commandItem('shell.closeAllTabs', undefined, { shortcut: undefined }),
    ];
    this.open({ kind: 'menu', x: at.x, y: at.y, items });
  }

  /** A menu row that runs command `id` on `target` through the catalog, so it passes the same gate as a key. */
  private commandItem(id: string, target?: CommandTarget, extra: Partial<MenuItem> = {}): MenuItem {
    const c = this.menuCatalog.get(id);
    if (!c) throw new Error(`No command ${id}`);
    return {
      label: c.title,
      shortcut: c.shortcutText || undefined,
      disabled: !this.menuCatalog.isEnabled(c, target),
      run: () => void this.menuCatalog.run(id, target),
      ...extra,
    };
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
    const item = (id: string, shortcut: string): MenuItem | null =>
      this.menuCatalog.get(id) ? this.commandItem(id, undefined, { shortcut }) : null;
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

  /** A picture's right-click menu, in the note or in the picture preview (PRD §10.6). */
  private openPictureMenu(picture: PictureAt, at: { x: number; y: number }): void {
    const items = this.pictureMenuItems(picture);
    if (items) this.open({ kind: 'menu', x: at.x, y: at.y, items });
  }

  /**
   * What a picture's menu offers: show it, open it elsewhere, copy it or its Markdown, change its description
   * or file name, show its link text, or delete it. The same in the note and in the preview.
   */
  pictureMenuItems(picture: PictureAt): (MenuItem | null)[] | null {
    const nb = this.notebook.peek();
    if (!nb || !this.activeTab.peek()?.state) return null;
    const target = { picture };
    const item = (id: string) => this.commandItem(id, target);
    const items: (MenuItem | null)[] = [];
    if (!this.preview.peek()) items.push(item('picture.preview'));
    items.push(item('picture.openInTab'));
    if (nb.store.openExternal) items.push(item('picture.openExternal'));
    if (nb.store.reveal) items.push(item('picture.reveal'));
    items.push(
      null,
      item('picture.copy'),
      item('picture.copyMarkdown'),
      null,
      item('picture.editDescription'),
      item('picture.rename'),
      item('picture.viewLinkText'),
      null,
      { ...item('picture.delete'), danger: true },
    );
    return items;
  }

  /**
   * The picture menu's commands. Each acts on the picture its target names, in the note in front; they are
   * hidden from the palette, which has no picture to act on.
   */
  private pictureCommandSpecs(): CommandSpec[] {
    /** The picture a target names, with the note it is in and the notebook file it shows (null if none). */
    const at = (t?: CommandTarget) => {
      const nb = this.notebook.peek();
      const tab = this.activeTab.peek();
      const state = tab?.state;
      if (!t?.picture || !nb || !tab || !state) return null;
      const picture = t.picture;
      return { nb, tab, state, picture, file: this.pictureFile(picture.url, tab.path.peek()) };
    };
    const spec = (
      id: string,
      title: string,
      description: string,
      run: (p: NonNullable<ReturnType<typeof at>>) => void | Promise<void>,
      enabled: (p: NonNullable<ReturnType<typeof at>>) => boolean = () => true,
    ): CommandSpec => ({
      id,
      title,
      description,
      group: 'Edit',
      hidden: true,
      enabled: (t) => {
        const p = at(t);
        return p !== null && enabled(p);
      },
      run: (t) => {
        const p = at(t);
        if (p && enabled(p)) return run(p);
      },
    });
    return [
      spec('picture.preview', 'Preview Picture', 'Show the picture on its own, over the note.', (p) =>
        this.openPreview(p.picture),
      ),
      spec(
        'picture.openInTab',
        'Open in Tab',
        'Open the picture’s file in a tab.',
        (p) => this.openPictureFile(p.file!),
        (p) => p.file !== null,
      ),
      spec(
        'picture.openExternal',
        'Open in Default App',
        'Open the picture’s file in the app the system opens it with.',
        (p) => p.nb.store.openExternal!(p.file!),
        (p) => p.file !== null && !!p.nb.store.openExternal,
      ),
      spec(
        'picture.reveal',
        revealLabel(),
        'Show the picture’s file in the system’s file manager.',
        (p) => p.nb.store.reveal!(p.file!),
        (p) => p.file !== null && !!p.nb.store.reveal,
      ),
      spec('picture.copy', 'Copy Picture', 'Put the picture on the clipboard.', (p) =>
        this.copyPicture(p.file ?? p.nb.pictures.resolve(p.tab.path.peek(), p.picture.url), p.file !== null),
      ),
      spec('picture.copyMarkdown', 'Copy Markdown', 'Put the picture’s Markdown on the clipboard.', (p) =>
        this.copyText(p.state.document.getText(p.picture.start, p.picture.end - p.picture.start)),
      ),
      spec(
        'picture.editDescription',
        'Edit Description…',
        'Change the text that describes the picture.',
        (p) => this.editPictureDescription(p.picture),
        // A wiki embed has no description to edit.
        (p) => embedRefName(p.picture.url) === null,
      ),
      spec(
        'picture.rename',
        'Rename Picture…',
        'Rename the picture’s file, keeping the links to it.',
        (p) => this.renamePicture(p.file!),
        // A rename keeps `![](…)` links, not wiki embeds, which name the file: renaming from one would break it.
        (p) => p.file !== null && embedRefName(p.picture.url) === null,
      ),
      spec('picture.viewLinkText', 'View Link Text', 'Show the picture’s Markdown in the note.', (p) => {
        this.closePreview(false);
        p.state.showLinkText(p.picture.start, p.picture.end);
        this.focusEditor();
      }),
      spec(
        'picture.delete',
        'Delete Picture',
        'Take the picture out of the note, and offer to delete its file.',
        (p) => this.deletePicture(p.picture),
      ),
    ];
  }

  /** Show a picture of the note in front in the picture preview (PRD §10.6). */
  openPreview(picture: PictureAt): void {
    const nb = this.notebook.peek();
    const tab = this.activeTab.peek();
    if (!nb || !tab?.state) return;
    const file = this.pictureFile(picture.url, tab.path.peek());
    this.preview.value = {
      picture,
      tab,
      src: nb.pictures.resolve(tab.path.peek(), picture.url),
      name: file ? baseName(file) : picture.url,
    };
  }

  /** The previewed picture's menu, at `at`. */
  openPreviewMenu(at: { x: number; y: number }): void {
    const shown = this.preview.peek();
    if (shown) this.openPictureMenu(shown.picture, at);
  }

  /** Close the picture preview, giving the keyboard back to the note unless `refocus` is false. */
  closePreview(refocus = true): void {
    if (!this.preview.peek()) return;
    this.preview.value = null;
    if (refocus) this.focusEditor();
  }

  private async openPictureFile(file: string): Promise<void> {
    this.closePreview(false);
    await this.openFile(file);
  }

  private async copyText(text: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(text);
    } catch {
      this.notebook.peek()?.notify('error', 'Puffinote couldn’t copy to the clipboard.');
    }
  }

  /** Edit Description…: change a picture's alt text, the words read out in its place (one undo step). */
  async editPictureDescription(picture: PictureAt): Promise<void> {
    const state = this.activeTab.peek()?.state;
    if (!state || !pictureStillAt(state, picture)) return;
    const written = state.document.getText(picture.start, picture.end - picture.start);
    const m = /^!\[((?:\\.|[^\]\\])*)\]/.exec(written);
    if (!m) return;
    const next = await this.prompt({
      title: 'Edit Description',
      label: 'Description',
      value: m[1]!.replace(/\\([\\[\]])/g, '$1'),
      confirm: 'Save',
    });
    if (next === null) return;
    if (state.document.getText(picture.start, picture.end - picture.start) !== written) return;
    const alt = next.replace(/[\r\n]+/g, ' ').replace(/([\\[\]])/g, '\\$1');
    const text = `![${alt}]${written.slice(m[0].length)}`;
    const end = picture.start + text.length;
    state.apply({ start: picture.start, length: written.length, text, anchor: end, caret: end });
    const shown = this.preview.peek();
    if (shown && shown.picture.start === picture.start) {
      this.preview.value = { ...shown, picture: { ...shown.picture, end } };
    }
  }

  /** Rename Picture…: rename the picture's file; every note's links to it follow (PRD §6.1). */
  private async renamePicture(file: string): Promise<void> {
    this.closePreview(false);
    await this.rename(file);
  }

  /** The notebook file a picture link written in note `notePath` points at, or null (a web picture, missing). */
  private pictureFile(url: string, notePath: string): string | null {
    if (hasScheme(url)) return null;
    const nb = this.notebook.peek();
    const path = nb ? nb.pictures.pathOf(notePath, url) : resolveRelative(notePath, url);
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
    if (!nb || !tab || !state || !pictureStillAt(state, picture)) return;
    const notePath = tab.path.peek();
    const written = state.document.getText(picture.start, picture.end - picture.start);
    const file = this.pictureFile(picture.url, notePath);
    let deleteFile = file !== null;
    /** The notes the user agreed to take the file from, by choosing Delete File. */
    const agreed = new Set<string>();
    if (file !== null) {
      const text = state.text;
      const hereAgain = nb.pictures.shows(
        text.slice(0, picture.start) + text.slice(picture.end),
        notePath,
        file,
      );
      let others: string[];
      try {
        others = await nb.pictures.notesShowing(file, notePath);
      } catch (e) {
        nb.report(e);
        return;
      }
      if (hereAgain || others.length > 0) {
        const names = others.map((p) => p.replace(/\.[^./]+$/, ''));
        if (hereAgain) names.unshift(`${stemOf(notePath)} (this note, elsewhere)`);
        const trash = nb.trash.available ? 'the notebook’s Trash' : 'the Trash';
        const answer = await this.confirm({
          title: `“${baseName(file)}” is in other notes`,
          message:
            `Keep File takes it out of this note only. Delete File moves the file to ${trash}, ` +
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
        if (deleteFile) {
          for (const p of others) agreed.add(p);
          if (hereAgain) agreed.add(notePath);
        }
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
    // The notebook may have been switched, or the note changed, while the question was open: only take out
    // the picture if it's still there.
    if (this.notebook.peek() !== nb) return;
    if (state.document.getText(picture.start, picture.end - picture.start) !== written) return;
    const effect =
      file !== null && bytes !== null
        ? {
            undo: () => void nb.pictures.restore(file, bytes),
            // Only if no note shows it by then, apart from those the user agreed to take it from.
            redo: () => void nb.pictures.removeUnused([file], agreed),
          }
        : undefined;
    state.apply(removeRangeEdit(state.document, picture.start, picture.end), effect);
    this.closePreview(false);
    this.focusEditor();
    if (deleteFile && file !== null) await nb.pictures.removeAll([file]);
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

  /**
   * Follow a ⌘-clicked link in the note at `from` (PRD §10.3): a relative link or a wiki link opens that note in
   * a tab, at the heading its `#fragment` names; a link to a missing note says so and creates nothing.
   */
  async followLink(from: string, href: string): Promise<void> {
    const nb = this.notebook.peek();
    const link = parseLinkHref(href);
    if (!nb || !link || link.kind === 'external') return;
    if (link.kind === 'heading') {
      this.revealHeading(link.fragment);
      return;
    }
    const files = allFiles(nb.tree.peek()).map((n) => n.path);
    const target = resolveNoteLink(from, link, files);
    if (target.kind === 'missing') {
      nb.notify('info', `There’s no “${target.name}” in this notebook.`);
      return;
    }
    if (target.kind === 'ambiguous') {
      nb.notify('info', `More than one file is called “${target.name}”.`);
      return;
    }
    await this.openFile(target.path);
    if (link.fragment && this.activeTab.peek()?.path.peek() === target.path)
      this.revealHeading(link.fragment);
  }

  /** Put the caret on the heading `fragment` names in the note in front, if it has one. */
  private revealHeading(fragment: string): void {
    const state = this.activeState.peek();
    if (!state) return;
    const h = headingFor(outlineOf(state.document, state.model), fragment);
    if (h) state.reveal(h.offset);
  }

  /** Close `tab`, asking about unsaved edits. Resolves false when the user cancelled. */
  async closeTab(tab: Tab): Promise<boolean> {
    const nb = this.notebook.peek();
    if (!nb) return false;
    if (tab.dirty.peek()) {
      nb.activate(tab);
      // A plain save won't write over a file that changed on disk, so the question offers to.
      const changed = tab.conflict.peek() === 'changed';
      const answer = await this.confirm({
        title: `Save changes to “${tab.name}”?`,
        message: changed
          ? `“${tab.name}” changed on disk since you opened it. Keep My Version writes yours over it.`
          : 'Your changes will be lost if you don’t save them.',
        buttons: [
          { label: 'Don’t Save', value: 'discard', kind: 'danger' },
          { label: 'Cancel', value: 'cancel' },
          { label: changed ? 'Keep My Version' : 'Save', value: 'save', kind: 'primary' },
        ],
      });
      if (answer === 'cancel') return false;
      if (answer === 'save' && !(await nb.save(tab, tab.conflict.peek() !== 'none'))) {
        this.explainUnsaved(nb, [tab]);
        return false;
      }
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
    if (answer === 'discard') nb.forgetRecovery(dirty);
    if (answer === 'save' && !(await nb.saveAll())) {
      this.explainUnsaved(nb, dirty);
      return false;
    }
    return true;
  }

  /** A save asked for on the way out failed: bring the first tab still unsaved to the front and say why. */
  private explainUnsaved(nb: Notebook, tabs: readonly Tab[]): void {
    const tab = tabs.find((t) => nb.tabs.peek().includes(t) && t.dirty.peek());
    if (!tab) return;
    nb.activate(tab);
    if (tab.conflict.peek() === 'changed') {
      nb.notify(
        'error',
        `“${tab.name}” changed on disk since you opened it. Reload from Disk or Keep My Version, then try again.`,
      );
    } else if (nb.notice.peek()?.kind !== 'error') {
      nb.notify('error', `Puffinote couldn’t save “${tab.name}”.`);
    }
  }

  // ----- Files -----

  /**
   * What Rename… and Delete… act on: the tree's selected row, or in the Library layout, which has no tree, the
   * note in front.
   */
  selectedPath(): string | null {
    const nb = this.notebook.peek();
    if (!nb) return null;
    if (this.layout.peek() === 'library') return this.activeTab.peek()?.path.peek() ?? null;
    return nb.selected.peek();
  }

  /**
   * Where New Note and New Folder put things: the selected folder, or the selected file's folder. In the
   * Library layout, the folder the note list shows (as its New Note button does).
   */
  targetDir(): string {
    const nb = this.notebook.peek();
    if (nb && this.layout.peek() === 'library') return folderOf(nb.scope.peek());
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
    const what = node?.kind === 'dir' ? 'folder and everything in it' : 'file';
    let message = nb.trash.available
      ? `The ${what} will be moved to the notebook’s Trash, where Show Trash… can restore it.`
      : `The ${what} will be moved to the Trash.`;
    // Its tabs close without saving, so say what that loses.
    const unsaved = nb.dirtyTabs.filter((t) => isWithin(t.path.peek(), path)).length;
    if (unsaved > 0) {
      message += ` ${unsaved} ${unsaved === 1 ? 'note has' : 'notes have'} unsaved changes that will be lost.`;
    }
    // Pictures in it that other notes show go missing from them (as Delete Picture says, PRD §10.6).
    let showing: string[] = [];
    try {
      showing = await nb.pictures.notesShowingPicturesIn(path);
    } catch {
      message += ' Puffinote couldn’t check which notes show pictures in it.';
    }
    if (this.notebook.peek() !== nb) return;
    if (showing.length > 0) {
      const it = node?.kind === 'dir' ? 'pictures in it' : 'it';
      message += ` ${showing.length === 1 ? 'This note shows' : 'These notes show'} ${it}:`;
    }
    const answer = await this.confirm({
      title: `Delete “${baseName(path)}”?`,
      message,
      list: showing.map((p) => p.replace(/\.[^./]+$/, '')),
      buttons: [
        { label: 'Cancel', value: 'cancel' },
        { label: 'Move to Trash', value: 'delete', kind: 'danger' },
      ],
    });
    if (answer === 'delete') await nb.remove(path);
  }

  /** Show Trash… (PRD §6.1): what the notebook's Trash holds, to restore, or say it is empty. */
  async showTrash(): Promise<void> {
    const nb = this.notebook.peek();
    if (!nb?.trash.available) return;
    let items: TrashItem[];
    try {
      items = await nb.trash.items();
    } catch (e) {
      nb.report(e);
      return;
    }
    if (this.notebook.peek() !== nb) return;
    if (items.length === 0) nb.notify('info', 'The notebook’s Trash is empty.');
    else this.open({ kind: 'trash', items });
  }

  /** Empty Trash… (PRD §6.1): asks first; the only delete that can't be undone. */
  async emptyTrash(): Promise<boolean> {
    const nb = this.notebook.peek();
    if (!nb?.trash.available) return false;
    let count: number;
    try {
      count = (await nb.trash.items()).length;
    } catch (e) {
      nb.report(e);
      return false;
    }
    if (this.notebook.peek() !== nb) return false;
    if (count === 0) {
      nb.notify('info', 'The notebook’s Trash is empty.');
      return false;
    }
    const answer = await this.confirm({
      title: 'Empty the notebook’s Trash?',
      message: `${count === 1 ? 'The item' : `All ${count} items`} in the Trash will be deleted for good. This can’t be undone.`,
      // Cancel is last, so Enter doesn't delete for good.
      buttons: [
        { label: 'Empty Trash', value: 'empty', kind: 'danger' },
        { label: 'Cancel', value: 'cancel', kind: 'primary' },
      ],
    });
    if (answer !== 'empty' || this.notebook.peek() !== nb) return false;
    return nb.trash.empty();
  }

  /** Find Unused Pictures… (PRD §10.6): list them, or say there are none. */
  /**
   * Import Notes… (PRD §5): ask whether to pick a folder or files, then copy them into the selected folder (or
   * the top level), never over a file that's there.
   */
  async importNotes(dir = this.targetDir()): Promise<void> {
    const nb = this.notebook.peek();
    const pick = this.host.pickFiles;
    if (!nb || !pick) return;
    const choice = await this.confirm({
      title: 'Import Notes',
      message: `Copy a folder or files from this computer into ${dir ? `“${baseName(dir)}”` : 'the notebook'}. Nothing is replaced: a name that’s taken gets a number.`,
      buttons: [
        { label: 'Cancel', value: 'cancel' },
        { label: 'Choose Files…', value: 'files' },
        { label: 'Choose Folder…', value: 'folder', kind: 'primary' },
      ],
    });
    if (choice === 'cancel') return;
    const files = await pick(choice === 'folder');
    if (!files || this.notebook.peek() !== nb) return;
    await nb.transfer.importFiles(dir, files);
  }

  /** Files dropped from the desktop onto folder `dir` (PRD §6.1): copied in as Import Notes… copies them. */
  async importDropped(dir: string, files: Promise<readonly ImportFile[]>): Promise<void> {
    const nb = this.notebook.peek();
    if (!nb) return;
    let list: readonly ImportFile[];
    try {
      list = await files;
    } catch (e) {
      nb.report(e);
      return;
    }
    if (this.notebook.peek() === nb) await nb.transfer.importFiles(dir, list);
  }

  async findUnusedPictures(): Promise<void> {
    const nb = this.notebook.peek();
    if (!nb) return;
    let paths: string[];
    try {
      paths = await nb.pictures.unused();
    } catch (e) {
      nb.report(e);
      return;
    }
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
  /** The tab a context menu named, or the one in front. */
  const tabOf = (t?: CommandTarget) =>
    (t?.tabId !== undefined
      ? nb()
          ?.tabs.peek()
          .find((x) => x.id === t.tabId)
      : tab()) ?? null;
  /** The file or folder a context menu named, or the selected one. */
  const pathOf = (t?: CommandTarget) => t?.path ?? m.selectedPath();
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
      id: 'shell.find',
      title: 'Find in Note…',
      description: 'Find text in the note in front.',
      group: 'Edit',
      shortcut: 'mod+f',
      icon: 'search',
      keywords: 'search find text in note',
      global: true,
      enabled: () => m.activeState.peek() !== null,
      run: () => m.openFind(),
    },
    {
      id: 'shell.findNext',
      title: 'Find Next',
      description: 'Select the next match of the find bar’s text.',
      group: 'Edit',
      shortcut: 'mod+g',
      global: true,
      enabled: () => m.activeState.peek() !== null && !!m.findQuery.peek(),
      run: () => m.findStep(1),
    },
    {
      id: 'shell.findPrevious',
      title: 'Find Previous',
      description: 'Select the previous match of the find bar’s text.',
      group: 'Edit',
      shortcut: 'mod+shift+g',
      global: true,
      enabled: () => m.activeState.peek() !== null && !!m.findQuery.peek(),
      run: () => m.findStep(-1),
    },
    {
      id: 'shell.replace',
      title: 'Find and Replace…',
      description: 'Find text in the note in front and replace it.',
      group: 'Edit',
      shortcut: isMac ? 'mod+alt+f' : 'mod+h',
      icon: 'search',
      keywords: 'search find replace text in note substitute',
      global: true,
      enabled: () => m.activeState.peek() !== null,
      run: () => m.openFind(true),
    },
    {
      id: 'shell.replaceOne',
      title: 'Replace',
      description: 'Replace the selected match with the Replace box’s text and go to the next one.',
      group: 'Edit',
      keywords: 'find replace next',
      global: true,
      enabled: () => m.replaceOpen.peek() && m.canReplace(),
      run: () => m.replaceCurrent(),
    },
    {
      id: 'shell.replaceAll',
      title: 'Replace All',
      description: 'Replace every match of the find bar’s text in the note.',
      group: 'Edit',
      keywords: 'find replace all every',
      global: true,
      enabled: () => m.replaceOpen.peek() && m.canReplace(),
      run: () => m.replaceAll(),
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
      run: (t) => m.newNote(t?.dir),
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
      run: (t) => m.newFolder(t?.dir),
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
      id: 'shell.exportHtml',
      title: 'Export as HTML…',
      description: 'Save the note in front as one HTML file, its pictures included.',
      group: 'File',
      icon: 'file-text',
      keywords: 'download web page share',
      enabled: () => tab()?.state != null,
      run: async () => {
        const t = tab();
        if (t) await nb()?.transfer.exportHtml(t);
      },
    },
    {
      id: 'shell.exportPdf',
      title: 'Export as PDF…',
      description: 'Print the note in front, or save it as a PDF.',
      group: 'File',
      shortcut: 'mod+p',
      global: true,
      keywords: 'print',
      enabled: () => tab()?.state != null && !!nb()?.transfer.canPrint,
      run: async () => {
        const t = tab();
        if (t) await nb()?.transfer.exportPdf(t);
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
      enabled: (t) => tabOf(t) !== null,
      run: async (target) => {
        const t = tabOf(target);
        if (t) await m.closeTab(t);
      },
    },
    {
      id: 'shell.pinTab',
      title: 'Pin Tab',
      description: 'Keep the note in front at the start of the tab strip, always in view.',
      group: 'File',
      icon: 'pin',
      enabled: (t) => tabOf(t) !== null,
      active: (t) => tabOf(t)?.pinned.value ?? false,
      run: (target) => {
        const t = tabOf(target);
        if (t) nb()?.setTabPinned(t, !t.pinned.peek());
      },
    },
    {
      id: 'shell.closeOtherTabs',
      title: 'Close Other Tabs',
      description: 'Close every tab but the one in front and the pinned ones.',
      group: 'File',
      enabled: () => (nb()?.tabs.peek().length ?? 0) > 1,
      run: (t) => m.closeTabs(tabOf(t)),
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
      enabled: (t) => hasNb() && !!(pathOf(t) ?? tab()?.path.peek()),
      run: (t) => m.rename(pathOf(t) ?? tab()!.path.peek()),
    },
    {
      id: 'shell.delete',
      title: 'Delete…',
      description: 'Delete the selected file or folder.',
      group: 'File',
      icon: 'trash-2',
      enabled: (t) => !!pathOf(t),
      run: (t) => m.remove(pathOf(t)!),
    },
    {
      id: 'shell.moveTo',
      title: 'Move To…',
      description: 'Move the selected file or folder to another folder.',
      group: 'File',
      enabled: (t) => !!pathOf(t),
      run: (t) => m.open({ kind: 'moveTo', path: pathOf(t)! }),
    },
    {
      id: 'shell.openFile',
      title: 'Open',
      description: 'Open the selected file.',
      group: 'File',
      hidden: true,
      enabled: (t) => !!t?.path,
      run: (t) => m.openFile(t!.path!),
    },
    {
      id: 'shell.openExternal',
      title: 'Open in Default App',
      description: 'Open the selected file in the app the system opens it with.',
      group: 'File',
      hidden: true,
      enabled: (t) => !!t?.path && !!nb()?.store.openExternal,
      run: (t) => nb()!.store.openExternal!(t!.path!),
    },
    {
      id: 'shell.pinToTop',
      title: 'Pin to Top',
      description: 'Keep the selected note at the top of the note list.',
      group: 'File',
      hidden: true,
      enabled: (t) => hasNb() && !!t?.path,
      active: (t) => !!t?.path && (nb()?.pinned.value.includes(t.path) ?? false),
      run: (t) => nb()!.togglePin(t!.path!),
    },
    {
      id: 'shell.revealInFolder',
      title: revealLabel(),
      description: 'Show the selected file or folder in the system’s file manager.',
      group: 'File',
      hidden: true,
      enabled: (t) => !!t?.path && !!nb()?.store.reveal,
      run: (t) => nb()!.store.reveal!(t!.path!),
    },
    {
      id: 'shell.showTrash',
      title: 'Show Trash…',
      description:
        'List what was deleted in this notebook, and restore it. (The desktop app uses the system Trash.)',
      group: 'File',
      icon: 'trash-2',
      enabled: () => !!nb()?.trash.available,
      run: () => m.showTrash(),
    },
    {
      id: 'shell.emptyTrash',
      title: 'Empty Trash…',
      description: 'Delete everything in this notebook’s Trash for good.',
      group: 'File',
      icon: 'trash-2',
      enabled: () => !!nb()?.trash.available,
      run: () => void m.emptyTrash(),
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
      id: 'shell.downloadNotebook',
      title: 'Download Notebook (.zip)…',
      description: 'Save every file in the notebook to one zip file, as a backup or to move it.',
      group: 'File',
      icon: 'save',
      keywords: 'export backup archive zip',
      enabled: () => nb()?.transfer.canDownload ?? false,
      run: async () => void (await nb()?.transfer.downloadArchive()),
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
      active: () => m.layoutPolicy.tabBarShown(nb()),
      run: () => {
        m.layoutPolicy.tabBarOverride.value = !m.layoutPolicy.tabBarShown(nb());
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
      description:
        p.id === 'search'
          ? 'Search the names and text of every note.'
          : `Show the sidebar’s ${p.title} panel.`,
      group: 'View',
      icon: p.icon,
      ...(p.id === 'search' ? { shortcut: 'mod+shift+f', global: true } : {}),
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
  if (m.host.pickFiles) {
    const at = specs.findIndex((s) => s.id === 'shell.downloadNotebook');
    specs.splice(at, 0, {
      id: 'shell.importNotes',
      title: 'Import Notes…',
      description: 'Copy a folder or files from this computer into the notebook.',
      group: 'File',
      icon: 'folder-plus',
      keywords: 'import add copy upload folder files',
      enabled: hasNb,
      run: () => m.importNotes(),
    });
  }
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

/**
 * Whether `state`'s note still has `picture` at its offsets: a picture there, from `start` to `end`, with the
 * same link. A menu or preview opened over another note (or before an edit) must not act on whatever is there.
 */
function pictureStillAt(state: EditorState, picture: PictureAt): boolean {
  const doc = state.document;
  if (picture.end > doc.length || picture.start >= picture.end) return false;
  const tokens = state.model.lineTokens(doc.getPosition(picture.start).line);
  const url = tokens.find((t) => isPictureToken(t) && t.start >= picture.start && t.start < picture.end);
  if (!url) return false;
  const found = pictureWithUrlAt(tokens, url.start, doc);
  return found?.start === picture.start && found.end === picture.end && found.url === picture.url;
}

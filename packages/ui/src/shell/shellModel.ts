import { computed, signal } from '@preact/signals';
import { CommandCatalog, type Command, type CommandSpec } from '../commands/command.ts';
import { markdownCommandSpecs } from '../editor/editorCommands.ts';
import type { EditorState } from '../editor/editorState.ts';
import { sidebarPanels, type Notebook, type SidebarPanel, type Tab } from '../notebook/notebook.ts';
import { allFolders, findNode, hasFolders, noteFileName } from '../notebook/tree.ts';
import { baseName, invalidNameReason, parentOf, stemOf } from '../platform/paths.ts';
import type { ConfirmOptions, PromptOptions } from './Dialog.tsx';

/** A row in a context menu; `null` draws a separator. */
export interface MenuItem {
  readonly label: string;
  readonly run: () => void;
  readonly shortcut?: string;
  readonly disabled?: boolean;
  readonly danger?: boolean;
}

export type Overlay =
  | { readonly kind: 'commands' }
  | { readonly kind: 'quickOpen' }
  | { readonly kind: 'settings' }
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
  readonly host: ShellHost;
  /** Keys: the shell's own commands. The editor handles its own keys while it has focus. */
  readonly shellCatalog: CommandCatalog;
  /** ⌘K: the shell's commands merged with the editor's (PRD §8.1). */
  readonly menuCatalog: CommandCatalog;
  private readonly shellIds: ReadonlySet<string>;

  readonly activeTab = computed(() => this.notebook.value?.active.value ?? null);
  readonly activeState = computed<EditorState | null>(() => this.activeTab.value?.state ?? null);
  readonly showTabBar = computed(() => {
    const nb = this.notebook.value;
    if (!nb || nb.tabs.value.length === 0) return false;
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

  // ----- Focus -----

  focusEditor(): void {
    setTimeout(() => document.querySelector<HTMLElement>('.pn-shell-main .pn-input')?.focus());
  }

  focusTree(): void {
    const nb = this.notebook.peek();
    if (!nb) return;
    nb.sidebarCollapsed.value = false;
    if (nb.panel.peek() !== 'files' && nb.panel.peek() !== 'filter') nb.panel.value = 'files';
    setTimeout(() => document.querySelector<HTMLElement>('.pn-sidebar .pn-tree')?.focus());
  }

  showPanel(panel: SidebarPanel): void {
    const nb = this.notebook.peek();
    if (!nb) return;
    nb.sidebarCollapsed.value = false;
    nb.panel.value = panel;
    // The typed-into panels take the caret; the read-only ones leave it be (PRD §6.2).
    if (panel === 'filter' || panel === 'search') this.focusPanelTick.value++;
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
      if (t === keep) continue;
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
    if (name !== null) await nb.createFolder(dir, name);
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
      id: 'shell.closeOtherTabs',
      title: 'Close Other Tabs',
      description: 'Close every tab but the one in front.',
      group: 'File',
      enabled: () => (nb()?.tabs.peek().length ?? 0) > 1,
      run: () => m.closeTabs(tab()),
    },
    {
      id: 'shell.closeAllTabs',
      title: 'Close All Tabs',
      description: 'Close every tab.',
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
      active: () => m.tabBarOverride.peek() ?? nb()?.settings.peek().showTabBar ?? true,
      run: () => {
        const shown = m.tabBarOverride.peek() ?? nb()?.settings.peek().showTabBar ?? true;
        m.tabBarOverride.value = !shown;
      },
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

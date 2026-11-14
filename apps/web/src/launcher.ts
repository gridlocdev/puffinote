import { signal } from '@preact/signals';
import {
  AuthError,
  BrowserStore,
  canPickFolder,
  ensurePermission,
  NasClient,
  NasStore,
  nativeBridge,
  NativeStore,
  Notebook,
  pickFolder,
  privateFolder,
  type NotebookStore,
  type PrefsStorage,
  type PuffinoteNative,
  type ShellModel,
} from '@puffinote/ui';
import { deleteHandle, findHandle, getHandle, putHandle } from './handles.ts';

/** Where this page runs: inside the desktop app, served by the NAS server, or as a plain web page. */
export type HostKind = 'native' | 'nas' | 'browser';

/** A notebook opened before, offered again on the welcome screen. */
export type Recent =
  | { readonly kind: 'native'; readonly path: string; readonly name: string }
  | {
      readonly kind: 'nas';
      readonly root: string;
      readonly rootName: string;
      readonly base: string;
      readonly name: string;
    }
  | { readonly kind: 'browser'; readonly id: string; readonly name: string }
  | { readonly kind: 'private'; readonly name: string };

const RECENTS_KEY = 'puffinote.recentNotebooks';
const MAX_RECENTS = 8;

export function recentKey(r: Recent): string {
  switch (r.kind) {
    case 'native':
      return `native:${r.path}`;
    case 'nas':
      return `nas:${r.root}:${r.base}`;
    case 'browser':
      return `browser:${r.id}`;
    case 'private':
      return 'private';
  }
}

/** Where a recent notebook is, for the line under its name. */
export function recentPlace(r: Recent): string {
  switch (r.kind) {
    case 'native':
      return r.path.replace(/[\\/][^\\/]*$/, '') || r.path;
    case 'nas':
      return [r.rootName, ...r.base.split('/').slice(0, -1)].filter(Boolean).join(' › ');
    case 'browser':
      return 'A folder on this computer';
    case 'private':
      return 'Kept in this browser';
  }
}

function loadRecents(): Recent[] {
  try {
    const raw: unknown = JSON.parse(localStorage.getItem(RECENTS_KEY) ?? '[]');
    return Array.isArray(raw) ? (raw as Recent[]).filter((r) => r && typeof r.kind === 'string') : [];
  } catch {
    return [];
  }
}

/** Hand bytes to the browser as a download (a file the web app can't open itself). */
export function download(name: string, bytes: Uint8Array, mime: string): void {
  const url = URL.createObjectURL(new Blob([bytes as Uint8Array<ArrayBuffer>], { type: mime }));
  const a = Object.assign(document.createElement('a'), { href: url, download: name });
  document.body.append(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 10_000);
}

/**
 * Opens notebooks for the shell: finds out what this page can reach, remembers what was opened, and reopens
 * the last notebook on start when it can do that without asking.
 */
export class Launcher {
  readonly model: ShellModel;
  readonly prefs: PrefsStorage;
  readonly kind: HostKind;
  readonly native: PuffinoteNative | null;
  readonly nas: NasClient | null;
  readonly recents = signal<Recent[]>(loadRecents());
  /** The notebook being opened, while it loads. */
  readonly opening = signal<string | null>(null);
  readonly error = signal<string | null>(null);
  /** The NAS server wants a token we don't have (or ours is wrong). */
  readonly needsToken = signal(false);
  /** `start` is reopening the last notebook (or the browser notebook). */
  readonly starting = signal(false);

  constructor(
    model: ShellModel,
    prefs: PrefsStorage,
    host: { kind: HostKind; native: PuffinoteNative | null; nas: NasClient | null },
  ) {
    this.model = model;
    this.prefs = prefs;
    this.kind = host.kind;
    this.native = host.native;
    this.nas = host.nas;
  }

  /** The desktop bridge first, then a NAS server answering /api/info, else a plain web page. */
  static async detect(): Promise<{ kind: HostKind; native: PuffinoteNative | null; nas: NasClient | null }> {
    const native = nativeBridge();
    if (native) return { kind: 'native', native, nas: null };
    const nas = new NasClient();
    const info = await nas.info();
    if (info) return { kind: 'nas', native: null, nas };
    return { kind: 'browser', native: null, nas: null };
  }

  /**
   * Reopen the last notebook, if that needs no permission prompt. A browser that can't open folders (Firefox,
   * Safari) has nothing to choose, so it goes straight into the browser notebook.
   */
  async start(): Promise<void> {
    this.starting.value = true;
    try {
      const last = this.recents.value[0];
      if (last && (await this.canReopen(last))) await this.open(last);
      else if (this.kind === 'browser' && !canPickFolder()) await this.openPrivate();
    } finally {
      this.starting.value = false;
    }
  }

  private async canReopen(last: Recent): Promise<boolean> {
    if (last.kind === 'nas') return this.kind === 'nas';
    if (last.kind === 'native') return this.kind === 'native';
    if (this.kind !== 'browser') return false;
    if (last.kind === 'private') return true;
    const handle = await getHandle(last.id);
    return !!handle && (await ensurePermission(handle, false));
  }

  /** The recents this page can open. */
  available(): Recent[] {
    return this.recents.value.filter((r) =>
      this.kind === 'native'
        ? r.kind === 'native'
        : this.kind === 'nas'
          ? r.kind === 'nas'
          : r.kind !== 'nas',
    );
  }

  async open(r: Recent): Promise<boolean> {
    try {
      const store = await this.storeFor(r);
      if (!store) return false;
      return await this.openStore(store, r);
    } catch (e) {
      this.reportError(e);
      return false;
    }
  }

  /** Ask for a folder (the OS dialog, or the browser's folder picker) and open it. */
  async openFolder(): Promise<void> {
    try {
      if (this.native) {
        const path = await this.native.pickFolder();
        if (path) await this.openNativePath(path);
        return;
      }
      const handle = await pickFolder();
      if (!handle) return;
      const ids = this.recents.value.flatMap((r) => (r.kind === 'browser' ? [r.id] : []));
      const id = (await findHandle(ids, handle)) ?? crypto.randomUUID();
      await putHandle(id, handle);
      await this.openStore(new BrowserStore(handle), { kind: 'browser', id, name: handle.name });
    } catch (e) {
      this.reportError(e);
    }
  }

  /** A folder the desktop app was asked to open (command line, Dock, second launch). */
  async openNativePath(path: string): Promise<void> {
    if (!this.native) return;
    const name = path.replace(/[\\/]+$/, '').replace(/^.*[\\/]/, '') || path;
    await this.open({ kind: 'native', path, name });
  }

  async openPrivate(): Promise<void> {
    await this.open({ kind: 'private', name: 'Browser notebook' });
  }

  async openNas(root: { id: string; name: string }, base: string): Promise<void> {
    const name = base.split('/').pop() || root.name;
    await this.open({ kind: 'nas', root: root.id, rootName: root.name, base, name });
  }

  async setToken(token: string): Promise<boolean> {
    if (!this.nas) return false;
    this.nas.token = token.trim() || null;
    try {
      await this.nas.roots();
      this.needsToken.value = false;
      this.error.value = null;
      return true;
    } catch (e) {
      this.reportError(e);
      return false;
    }
  }

  forget(r: Recent): void {
    const key = recentKey(r);
    this.saveRecents(this.recents.value.filter((x) => recentKey(x) !== key));
    if (r.kind === 'browser') void deleteHandle(r.id);
  }

  /** Back to the welcome screen. The caller has already dealt with unsaved notes. */
  close(): void {
    this.model.setNotebook(null);
    this.error.value = null;
  }

  private async storeFor(r: Recent): Promise<NotebookStore | null> {
    switch (r.kind) {
      case 'native':
        if (!this.native) return null;
        return new NativeStore(this.native, r.path);
      case 'nas':
        if (!this.nas) return null;
        return new NasStore(this.nas, r.root, r.base, r.name);
      case 'private': {
        const dir = await privateFolder();
        if (!dir) throw new Error('This browser can’t keep a notebook.');
        return new BrowserStore(dir, true);
      }
      case 'browser': {
        const handle = await getHandle(r.id);
        if (!handle) {
          this.forget(r);
          throw new Error(`“${r.name}” can’t be opened any more. Open the folder again.`);
        }
        // Asking needs the click that got us here; a refusal leaves the welcome screen up.
        if (!(await ensurePermission(handle, true))) return null;
        return new BrowserStore(handle);
      }
    }
  }

  private async openStore(store: NotebookStore, r: Recent): Promise<boolean> {
    this.opening.value = r.name;
    this.error.value = null;
    const ticket = this.model.beginNotebookOpen();
    try {
      const nb = await Notebook.open(store, {
        prefs: this.prefs,
        download,
        confirmOpenExternal: (name) => this.model.confirmOpenExternal(name),
      });
      if (!(await this.model.adoptNotebook(nb, ticket))) return false;
      const key = recentKey(r);
      this.saveRecents([r, ...this.recents.value.filter((x) => recentKey(x) !== key)]);
      return true;
    } catch (e) {
      this.reportError(e);
      return false;
    } finally {
      this.opening.value = null;
    }
  }

  /** Show what went wrong; a NAS auth failure asks for the token instead. */
  reportError(e: unknown): void {
    if (e instanceof AuthError) {
      this.needsToken.value = true;
      this.error.value = this.nas?.token ? 'That token wasn’t accepted.' : null;
      return;
    }
    const message = e instanceof Error ? e.message : String(e);
    this.error.value = /not found|NOT_FOUND|isn’t in the notebook/i.test(message)
      ? 'That folder isn’t there any more.'
      : message;
  }

  private saveRecents(list: Recent[]): void {
    this.recents.value = list.slice(0, MAX_RECENTS);
    try {
      localStorage.setItem(RECENTS_KEY, JSON.stringify(this.recents.value));
    } catch {
      // Storage disabled: recents last for this visit.
    }
  }
}

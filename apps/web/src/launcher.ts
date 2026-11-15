import { signal } from '@preact/signals';
import {
  AuthError,
  BrowserStore,
  NasClient,
  NasStore,
  nativeBridge,
  NativeStore,
  Notebook,
  NotFoundError,
  persistStorage,
  printHtml,
  privateFolder,
  webRecovery,
  type NotebookStore,
  type PrefsStorage,
  type PuffinoteNative,
  type ShellModel,
} from '@puffinote/ui';

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
  | { readonly kind: 'private'; readonly name: string };

const RECENTS_KEY = 'puffinote.recentNotebooks';
const MAX_RECENTS = 8;

export function recentKey(r: Recent): string {
  switch (r.kind) {
    case 'native':
      return `native:${r.path}`;
    case 'nas':
      return `nas:${r.root}:${r.base}`;
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
    case 'private':
      return 'Kept in this browser';
  }
}

function loadRecents(): Recent[] {
  try {
    const raw: unknown = JSON.parse(localStorage.getItem(RECENTS_KEY) ?? '[]');
    // Folders picked in Chromium (kind 'browser') aren't supported any more.
    const kinds = new Set(['native', 'nas', 'private']);
    return Array.isArray(raw) ? (raw as Recent[]).filter((r) => r && kinds.has(r.kind)) : [];
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
  /** The recent notebook open now, by `recentKey`. */
  readonly current = signal<string | null>(null);

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
   * Reopen the last notebook. Otherwise open the one obvious notebook: in a browser its own notebook, on a NAS
   * server with one root that root. The welcome screen is left up where there's a choice to make: the desktop
   * app with nothing to reopen, or a server with several roots.
   */
  async start(): Promise<void> {
    this.starting.value = true;
    try {
      await this.prune();
      const last = this.available()[0];
      if (last) await this.open(last);
      else if (this.kind === 'browser') await this.openPrivate();
      else if (this.kind === 'nas') await this.openOnlyRoot();
    } finally {
      this.starting.value = false;
    }
  }

  private async openOnlyRoot(): Promise<void> {
    if (!this.nas) return;
    try {
      const roots = await this.nas.roots();
      if (roots.length === 1) await this.openNas(roots[0]!, '');
    } catch (e) {
      this.reportError(e);
    }
  }

  /** The recents this page can open. */
  available(): Recent[] {
    return this.recents.value.filter((r) =>
      this.kind === 'native'
        ? r.kind === 'native'
        : this.kind === 'nas'
          ? r.kind === 'nas'
          : r.kind === 'private',
    );
  }

  /**
   * Forget recents whose folder was deleted: its parent folder is there and it isn't (PRD §6). One that can't
   * be reached (an unplugged drive, a server that's off) stays. The notebook open now is never dropped.
   */
  async prune(): Promise<void> {
    const gone = new Set<string>();
    await Promise.all(
      this.available().map(async (r) => {
        if (recentKey(r) !== this.current.peek() && (await this.isGone(r))) gone.add(recentKey(r));
      }),
    );
    if (gone.size) this.saveRecents(this.recents.value.filter((r) => !gone.has(recentKey(r))));
  }

  /** Is this recent's folder clearly deleted? False when it's there or when it can't be told. */
  async isGone(r: Recent): Promise<boolean> {
    try {
      if (r.kind === 'native') return (await this.native?.folderGone(r.path)) ?? false;
      if (r.kind === 'nas' && this.nas) {
        if (!r.base) return !(await this.nas.roots()).some((x) => x.id === r.root);
        // The parent answering is what tells "deleted" from "unreachable".
        await this.nas.list(r.root, r.base.split('/').slice(0, -1).join('/'));
        return await this.nas.list(r.root, r.base).then(
          () => false,
          (e: unknown) => e instanceof NotFoundError,
        );
      }
    } catch {
      // Can't tell: keep it.
    }
    return false;
  }

  /**
   * Open a recent notebook from the notebook menu, once unsaved notes are dealt with. A failure shows in the
   * notebook still open, and a deleted folder leaves the list.
   */
  async openRecent(key: string): Promise<void> {
    const r = this.available().find((x) => recentKey(x) === key);
    if (!r || key === this.current.peek()) return;
    if (!(await this.model.confirmDiscardAll())) return;
    if (await this.open(r)) return;
    if (await this.isGone(r)) this.forget(r);
    const message = this.error.peek();
    const nb = this.model.notebook.peek();
    if (message && nb) {
      nb.notify('error', message);
      this.error.value = null;
    }
  }

  async open(r: Recent): Promise<boolean> {
    this.opening.value = r.name;
    try {
      const store = await this.storeFor(r);
      if (!store) return false;
      return await this.openStore(store, r);
    } catch (e) {
      this.reportError(e);
      return false;
    } finally {
      this.opening.value = null;
    }
  }

  /** Ask for a folder in the OS dialog (the desktop app) and open it. */
  async openFolder(): Promise<void> {
    if (!this.native) return;
    try {
      const path = await this.native.pickFolder();
      if (path) await this.openNativePath(path);
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
  }

  /** Back to the welcome screen. The caller has already dealt with unsaved notes. */
  close(): void {
    this.model.session.set(null);
    this.current.value = null;
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
        // Not awaited: Firefox asks the user, and the notebook opens meanwhile. A refusal changes nothing.
        void persistStorage();
        return new BrowserStore(dir, true);
      }
    }
  }

  /**
   * Print a note's HTML (PRD §10.5): the desktop app saves it as a PDF where the user picks; a browser shows
   * its print dialog, where Save as PDF is one choice.
   */
  private async print(html: string, name: string): Promise<void> {
    if (this.native) await this.native.savePdf(html, name);
    else await printHtml(html);
  }

  private async openStore(store: NotebookStore, r: Recent): Promise<boolean> {
    this.opening.value = r.name;
    this.error.value = null;
    const ticket = this.model.session.beginOpen();
    try {
      const nb = await Notebook.open(store, {
        prefs: this.prefs,
        download,
        print: (html, name) => this.print(html, name),
        confirmOpenExternal: (name) => this.model.confirmOpenExternal(name),
        recovery: webRecovery(recentKey(r)),
      });
      if (!(await this.model.session.adopt(nb, ticket))) return false;
      const key = recentKey(r);
      this.current.value = key;
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

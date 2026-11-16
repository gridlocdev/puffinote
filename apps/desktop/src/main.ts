import {
  app,
  BrowserWindow,
  dialog,
  ipcMain,
  Menu,
  nativeTheme,
  net,
  protocol,
  screen,
  shell,
} from 'electron';
import type { MenuItemConstructorOptions } from 'electron';
import { writeAtomic } from '@puffinote/node-files';
import { mkdir, stat } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { Channels, CONFLICT_EXISTS, FILE_SCHEME, NOT_FOUND, type NativeMenuItem } from './bridge.ts';
import {
  FolderGrants,
  fileVersion,
  folderArgs,
  isAppUrl,
  isOpenableDocument,
  listDir,
  moveEntry,
  pdfFileName,
  readWithMtime,
  sameVersion,
  saveFile,
  watchTree,
} from './files.ts';
import { htmlToPdf } from './pdf.ts';
import { afterRendererGone } from './recovery.ts';
import { loadSavedWindow, placeWindow, saveWindow, type SavedWindow } from './windowState.ts';
import {
  checkDue,
  fetchLatestRelease,
  isNewer,
  loadUpdatePrefs,
  saveUpdatePrefs,
  type UpdatePrefs,
  UpdateChecks,
} from './updates.ts';

const here = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.join(here, 'web');
/** How long the renderer has to acknowledge a close request before we offer to force it. */
const CLOSE_ACK_MS = 3000;
/** PUFFINOTE_HEADLESS=1: render offscreen and never show a window or Dock icon (automated checks). */
const headless = process.env.PUFFINOTE_HEADLESS === '1';

/** A `.catch` handler for work nobody waits on: the main process has no window to show it in, so it logs. */
function logFailure(what: string): (e: unknown) => void {
  return (e) => console.error(`Puffinote: ${what} failed:`, e);
}
if (headless) app.dock?.hide();

// The web client loads from app://puffinote/ — a standard, secure scheme so fetch, modules and CSP behave like
// https. Notebook pictures load from pnfile://local/<path>, served only inside granted folders.
protocol.registerSchemesAsPrivileged([
  { scheme: 'app', privileges: { standard: true, secure: true, supportFetchAPI: true } },
  { scheme: FILE_SCHEME, privileges: { standard: true, secure: true, supportFetchAPI: true } },
]);

/** Per-window state the main process must know: allow-close flag, folder watcher, renderer health. */
interface WindowState {
  allowClose: boolean;
  stopWatch: (() => void) | null;
  /** The notebook folder being watched. */
  watchRoot: string | null;
  /** Running while a close request waits for the renderer's ack. */
  closeTimer: NodeJS.Timeout | null;
  /** The renderer crashed or was killed: nothing is left to ask about unsaved changes. */
  rendererGone: boolean;
  unresponsive: boolean;
}
const windows = new Map<number, WindowState>();
/** Notebook folders the renderer may touch: picked in a dialog, opened by the OS, or opened before. */
const grants = new FolderGrants(path.join(app.getPath('userData'), 'granted-folders.json'));
/** Another instance is running: it gets our argv (second-instance) and owns the grants file. */
const single = app.requestSingleInstanceLock();
if (!single) {
  grants.disablePersist();
  app.quit();
}
/** The version (`fileVersion`) we last wrote per path, so the watcher can leave out our own saves. */
const knownVersion = new Map<string, string>();
/** Set by Cmd+Q / app.quit() so the last window closing ends the app on macOS too. */
let quitting = false;

function grantAll(paths: readonly string[]): string[] {
  for (const p of paths) grants.grant(p);
  return [...paths];
}

/** Folders asked for before a window was ready (macOS open-file at launch, argv). */
const pendingOpen: string[] = grantAll(folderArgs(process.argv.slice(app.isPackaged ? 1 : 2), process.cwd()));

function stopWatching(state: WindowState): void {
  state.stopWatch?.();
  state.stopWatch = null;
  const root = state.watchRoot;
  state.watchRoot = null;
  // Forget our saves in that folder unless another window still watches it.
  if (root === null || [...windows.values()].some((s) => s.watchRoot === root)) return;
  for (const p of knownVersion.keys()) {
    const rel = path.relative(root, p);
    if (!rel.startsWith('..') && !path.isAbsolute(rel)) knownVersion.delete(p);
  }
}

/** Where the window was when it last closed (PRD §3), in the user-data folder. */
const windowFile = path.join(app.getPath('userData'), 'window-state.json');
let lastWindow: SavedWindow | null = null;
const MIN_SIZE = { width: 560, height: 400 };

/** Remember `win`'s place, unless this is a headless run (which must not move the user's window). */
function rememberWindow(win: BrowserWindow): void {
  if (headless || win.isDestroyed() || win.isMinimized()) return;
  const b = win.getNormalBounds();
  lastWindow = { x: b.x, y: b.y, width: b.width, height: b.height, maximized: win.isMaximized() };
  void saveWindow(windowFile, lastWindow).catch(() => undefined);
}

function createWindow(): BrowserWindow {
  // Where it last was, kept on a screen still attached; the first time, fill the usable part of the screen the
  // pointer is on: below the menu bar, clear of the Dock or taskbar.
  const fallback = screen.getDisplayNearestPoint(screen.getCursorScreenPoint()).workArea;
  const placed = placeWindow(
    headless ? null : lastWindow,
    screen.getAllDisplays().map((d) => d.workArea),
    fallback,
    MIN_SIZE,
  );
  const { x, y, width, height } = placed;
  const win = new BrowserWindow({
    x,
    y,
    width,
    height,
    minWidth: MIN_SIZE.width,
    minHeight: MIN_SIZE.height,
    title: 'Puffinote',
    // macOS ignores this and shows the bundle's icon.
    ...(process.platform === 'darwin' ? {} : { icon: path.join(here, '..', 'build', 'icon.png') }),
    // On macOS the page draws its own title bar under the traffic lights, in the app's colours (as Cellestra does).
    titleBarStyle: process.platform === 'darwin' ? 'hidden' : 'default',
    show: false,
    // The page's background (styles.css --pn-bg), so a resize or a slow first paint doesn't flash white.
    backgroundColor: nativeTheme.shouldUseDarkColors ? '#1e1e1e' : '#ffffff',
    webPreferences: {
      preload: path.join(here, 'preload.cjs'),
      contextIsolation: true,
      sandbox: true,
      nodeIntegration: false,
      offscreen: headless,
    },
  });
  const state: WindowState = {
    allowClose: false,
    stopWatch: null,
    watchRoot: null,
    closeTimer: null,
    rendererGone: false,
    unresponsive: false,
  };
  windows.set(win.id, state);

  if (!headless) {
    win.once('ready-to-show', () => {
      if (placed.maximized) win.maximize();
      win.show();
    });
  }
  let placeTimer: ReturnType<typeof setTimeout> | null = null;
  const placeChanged = () => {
    if (placeTimer) clearTimeout(placeTimer);
    placeTimer = setTimeout(() => rememberWindow(win), 500);
  };
  win.on('resize', placeChanged);
  win.on('move', placeChanged);
  win.on('maximize', placeChanged);
  win.on('unmaximize', placeChanged);
  win.on('close', (e) => {
    if (placeTimer) clearTimeout(placeTimer);
    rememberWindow(win);
    if (state.allowClose || state.rendererGone) return;
    e.preventDefault();
    if (state.unresponsive) return void offerForceClose(win, state);
    if (state.closeTimer) return; // already asked; waiting for the renderer
    win.webContents.send(Channels.closeRequested);
    state.closeTimer = setTimeout(() => {
      state.closeTimer = null;
      offerForceClose(win, state).catch(logFailure('offerForceClose'));
    }, CLOSE_ACK_MS);
  });
  win.on('closed', () => {
    if (placeTimer) clearTimeout(placeTimer);
    stopWatching(state);
    if (state.closeTimer) clearTimeout(state.closeTimer);
    windows.delete(win.id);
  });
  win.on('unresponsive', () => (state.unresponsive = true));
  win.on('responsive', () => (state.unresponsive = false));
  win.webContents.on('render-process-gone', (_e, details) => {
    state.rendererGone = true;
    stopWatching(state);
    const action = afterRendererGone({ closing: state.closeTimer !== null, quitting, headless });
    if (state.closeTimer) clearTimeout(state.closeTimer);
    state.closeTimer = null;
    if (action === 'destroy') win.destroy();
    else recoverRenderer(win, state, details.reason, action === 'ask').catch(logFailure('recoverRenderer'));
  });
  // The page's beforeunload guard blocks unloading while notes are dirty. Once the close handshake has asked
  // (and the user chose Don't Save), let it through; otherwise (a reload, a page-initiated close) it holds.
  win.webContents.on('will-prevent-unload', (e) => {
    if (state.allowClose) e.preventDefault();
  });
  // Links in notes open in the user's browser, never inside the app.
  win.webContents.setWindowOpenHandler(({ url }) => {
    if (/^(https?|mailto):/i.test(url)) shell.openExternal(url).catch(logFailure('shell.openExternal'));
    return { action: 'deny' };
  });
  win.webContents.on('will-navigate', (e, url) => {
    if (isAppUrl(url)) return;
    e.preventDefault();
    if (/^(https?|mailto):/i.test(url)) shell.openExternal(url).catch(logFailure('shell.openExternal'));
  });
  win.webContents.on('did-finish-load', () => {
    for (const p of pendingOpen.splice(0)) win.webContents.send(Channels.openFolder, p);
  });

  win.loadURL('app://puffinote/index.html').catch(logFailure('win.loadURL'));
  return win;
}

/**
 * The page crashed or was killed while the window stays open: say so and reload it (the notebook reopens on
 * its own), or close the window if the user prefers. Without this the window stays blank.
 */
async function recoverRenderer(
  win: BrowserWindow,
  state: WindowState,
  reason: string,
  ask: boolean,
): Promise<void> {
  if (win.isDestroyed()) return;
  if (ask) {
    const { response } = await dialog.showMessageBox(win, {
      type: 'warning',
      buttons: ['Reload', 'Close Window'],
      defaultId: 0,
      cancelId: 0,
      message: 'Puffinote’s window stopped working.',
      detail: `The page ${reason === 'killed' ? 'was stopped' : 'crashed'} (${reason}). Changes that weren’t saved yet are lost; reloading opens the notebook again.`,
    });
    if (win.isDestroyed()) return;
    if (response === 1) return void win.destroy();
  }
  state.rendererGone = false;
  state.unresponsive = false;
  win.webContents.reload();
}

/** The renderer is hung or never answered the close request: let the user force it. */
async function offerForceClose(win: BrowserWindow, state: WindowState): Promise<void> {
  if (win.isDestroyed()) return;
  const { response } = await dialog.showMessageBox(win, {
    type: 'warning',
    buttons: ['Close Anyway', 'Wait'],
    defaultId: 1,
    cancelId: 1,
    message: 'Puffinote is not responding.',
    detail: 'Closing now loses unsaved changes.',
  });
  if (win.isDestroyed()) return;
  if (response === 0) {
    state.allowClose = true;
    win.hide();
    win.destroy();
  } else quitting = false;
}

type IpcEvent = Electron.IpcMainEvent | Electron.IpcMainInvokeEvent;

function fromEvent(e: IpcEvent): BrowserWindow | null {
  return BrowserWindow.fromWebContents(e.sender);
}

/** Only our own page may use the bridge. */
function trusted(e: IpcEvent): boolean {
  return isAppUrl(e.senderFrame?.url);
}

function forbidden(): never {
  throw new Error('Forbidden');
}

/** Throw unless the request is from our page and `p` lies inside a granted notebook folder. */
async function guard(e: IpcEvent, p: unknown): Promise<string> {
  if (!trusted(e) || !(await grants.allows(p))) forbidden();
  return path.resolve(p as string);
}

/** Node's not-found becomes the marker the renderer turns into a NotFoundError. */
function notFound(e: unknown, p: string): never {
  if ((e as NodeJS.ErrnoException)?.code === 'ENOENT') throw new Error(`${NOT_FOUND}: ${path.basename(p)}`);
  throw e;
}

const exists = (p: string) =>
  stat(p).then(
    () => true,
    () => false,
  );

/**
 * A folder the renderer asks for that isn't granted (a Recent from before the grants file, or a moved
 * profile): open it only if it is a folder and the user agrees.
 */
async function allowFolder(win: BrowserWindow | null, p: unknown): Promise<void> {
  if (await grants.allows(p)) return;
  if (typeof p !== 'string' || !path.isAbsolute(p)) forbidden();
  const s = await stat(p).catch((e: unknown) => notFound(e, p));
  if (!s.isDirectory()) forbidden();
  const opts = {
    type: 'question' as const,
    buttons: ['Open', 'Cancel'],
    defaultId: 0,
    cancelId: 1,
    message: `Open “${path.basename(p)}” as a notebook?`,
    detail: p,
  };
  const { response } = win ? await dialog.showMessageBox(win, opts) : await dialog.showMessageBox(opts);
  if (response !== 0) throw new Error('Opening the folder was cancelled');
  grants.grant(p);
}

/** Atomic save (`saveFile`), remembering the version written so the watcher can leave this save out. */
async function writeNative(
  p: string,
  bytes: Uint8Array,
  expected: string | null,
  force: boolean,
  create: boolean,
): Promise<string> {
  const version = await saveFile(p, bytes, { expected, force, create, allows: (q) => grants.allows(q) });
  knownVersion.set(p, version);
  return version;
}

function toTemplate(items: readonly NativeMenuItem[], win: BrowserWindow): MenuItemConstructorOptions[] {
  return items.map((item): MenuItemConstructorOptions => {
    if (item.type === 'separator') return { type: 'separator' };
    if (item.role)
      return {
        role: item.role as MenuItemConstructorOptions['role'],
        ...(item.label ? { label: item.label } : {}),
      };
    const opts: MenuItemConstructorOptions = {
      label: item.label ?? '',
      enabled: item.enabled ?? true,
      ...(item.accelerator ? { accelerator: item.accelerator } : {}),
      ...(item.type === 'checkbox' ? { type: 'checkbox', checked: item.checked ?? false } : {}),
    };
    if (item.submenu) opts.submenu = toTemplate(item.submenu, win);
    else if (item.command) {
      const id = item.command;
      opts.click = () => win.webContents.send(Channels.command, id);
    }
    return opts;
  });
}

function registerIpc(): void {
  ipcMain.handle(Channels.pickFolder, async (e) => {
    if (!trusted(e)) forbidden();
    const win = fromEvent(e);
    const opts = {
      properties: ['openDirectory', 'createDirectory'] as Array<'openDirectory' | 'createDirectory'>,
    };
    const result = win ? await dialog.showOpenDialog(win, opts) : await dialog.showOpenDialog(opts);
    const p = result.filePaths[0];
    if (result.canceled || !p) return null;
    grants.grant(p);
    app.addRecentDocument(p);
    return p;
  });
  ipcMain.handle(Channels.folderGone, async (e, p: unknown) => {
    if (!trusted(e)) forbidden();
    // Only folders opened before: the page learns nothing about other paths.
    if (!grants.isRoot(p)) return false;
    const folder = path.resolve(p);
    const parent = await stat(path.dirname(folder)).catch(() => null);
    if (!parent?.isDirectory()) return false; // the drive or share isn't there: keep it
    return !(await exists(folder));
  });
  ipcMain.handle(Channels.list, async (e, dir: unknown) => {
    if (!trusted(e)) forbidden();
    // Listing a notebook's own folder is how one opens: a folder not granted yet gets asked about.
    await allowFolder(fromEvent(e), dir);
    const folder = path.resolve(dir as string);
    return listDir(folder, (p) => grants.allows(p)).catch((err: unknown) => notFound(err, folder));
  });
  ipcMain.handle(Channels.read, async (e, p: unknown) => {
    const file = await guard(e, p);
    // One handle for the mtime and the bytes, so an external write can't slip between them and hand the
    // renderer old text under the new version (its next save would then overwrite that write).
    return readWithMtime(file).catch((err: unknown) => notFound(err, file));
  });
  ipcMain.handle(
    Channels.write,
    async (e, p: unknown, bytes: unknown, expected: unknown, force: unknown, create: unknown) => {
      const file = await guard(e, p);
      if (!(bytes instanceof Uint8Array)) forbidden();
      return writeNative(
        file,
        bytes,
        typeof expected === 'string' ? expected : null,
        force === true,
        create === true,
      );
    },
  );
  ipcMain.handle(Channels.mkdir, async (e, p: unknown) => {
    const dir = await guard(e, p);
    const s = await stat(dir).catch(() => null);
    if (s && !s.isDirectory()) throw new Error(`${CONFLICT_EXISTS}: ${path.basename(dir)}`);
    await mkdir(dir, { recursive: true });
  });
  ipcMain.handle(Channels.move, async (e, from: unknown, to: unknown) => {
    const src = await guard(e, from);
    const dest = await guard(e, to);
    // Like trash: a granted notebook folder itself never moves.
    await moveEntry(src, dest, (p) => grants.isRoot(p));
  });
  ipcMain.handle(Channels.trash, async (e, p: unknown) => {
    const target = await guard(e, p);
    if (grants.isRoot(target)) forbidden();
    if (!(await exists(target))) return;
    await shell.trashItem(target);
  });
  ipcMain.handle(Channels.reveal, async (e, p: unknown) => {
    shell.showItemInFolder(await guard(e, p));
  });
  ipcMain.handle(Channels.openExternal, async (e, p: unknown) => {
    const target = await guard(e, p);
    // Never launch an app or script from a notebook: show it in the file manager instead.
    if (!(await isOpenableDocument(target))) return shell.showItemInFolder(target);
    const error = await shell.openPath(target);
    if (error) throw new Error(error);
  });
  ipcMain.handle(Channels.savePdf, async (e, html: unknown, name: unknown) => {
    if (!trusted(e) || typeof html !== 'string') forbidden();
    const win = fromEvent(e);
    const opts = { defaultPath: pdfFileName(name), filters: [{ name: 'PDF', extensions: ['pdf'] }] };
    const result = win ? await dialog.showSaveDialog(win, opts) : await dialog.showSaveDialog(opts);
    if (result.canceled || !result.filePath) return false;
    await writeAtomic(result.filePath, await htmlToPdf(html));
    return true;
  });
  ipcMain.on(Channels.watch, (e, root: unknown) => {
    const win = fromEvent(e);
    const state = win && windows.get(win.id);
    if (!win || !state || !trusted(e)) return;
    stopWatching(state);
    if (!grants.isRoot(root)) return;
    app.addRecentDocument(root);
    state.watchRoot = root;
    state.stopWatch = watchTree(root, (paths) => {
      Promise.all(
        paths.map(async (p) => {
          const known = knownVersion.get(p);
          if (known === undefined) return p;
          const s = await stat(p).catch(() => null);
          if (s && sameVersion(fileVersion(s), known)) return null; // our own save
          // Changed by someone else, or gone: forget it, unless a newer save of ours landed meanwhile.
          if (knownVersion.get(p) === known) knownVersion.delete(p);
          return p;
        }),
      )
        .then((changed) => {
          const out = changed.filter((p): p is string => p !== null);
          if (out.length > 0 && !win.isDestroyed()) win.webContents.send(Channels.changed, out);
        })
        .catch(logFailure('Promise.all'));
    });
  });
  ipcMain.on(Channels.setTitle, (e, title: unknown, dirty: unknown) => {
    const win = fromEvent(e);
    if (!win || !trusted(e) || typeof title !== 'string') return;
    win.setTitle(title);
    win.setDocumentEdited(dirty === true);
  });
  ipcMain.on(Channels.setTitleBarHeight, (e, height: unknown) => {
    const win = fromEvent(e);
    if (!win || !trusted(e) || process.platform !== 'darwin') return;
    if (typeof height !== 'number' || !Number.isFinite(height) || height < 14 || height > 80) return;
    // The buttons are about 14px tall; their top sits so they're centred, as far in from the left.
    const y = Math.round((height - 14) / 2);
    win.setWindowButtonPosition({ x: y + 6, y });
  });
  ipcMain.on(Channels.setMenu, (e, items: readonly NativeMenuItem[]) => {
    const win = fromEvent(e);
    if (!win || !trusted(e) || !Array.isArray(items)) return;
    const template = toTemplate(items, win);
    const updates: MenuItemConstructorOptions = {
      label: 'Check for Updates…',
      click: () => {
        updateChecks.request(true).catch(logFailure('updateChecks.request'));
      },
    };
    if (process.platform === 'darwin') {
      template.unshift({
        label: app.name,
        submenu: [
          { role: 'about' },
          updates,
          { type: 'separator' },
          { role: 'services' },
          { type: 'separator' },
          { role: 'hide' },
          { role: 'hideOthers' },
          { role: 'unhide' },
          { type: 'separator' },
          { role: 'quit' },
        ],
      });
    } else {
      let help = template.find((m) => m.label === 'Help');
      if (!help) template.push((help = { label: 'Help', submenu: [] }));
      const sub = Array.isArray(help.submenu) ? help.submenu : [];
      help.submenu = [...sub, ...(sub.length > 0 ? [{ type: 'separator' as const }] : []), updates];
    }
    Menu.setApplicationMenu(Menu.buildFromTemplate(template));
  });
  ipcMain.on(Channels.closeAck, (e) => {
    const win = fromEvent(e);
    const state = win && windows.get(win.id);
    if (!state || !trusted(e)) return;
    if (state.closeTimer) clearTimeout(state.closeTimer);
    state.closeTimer = null;
  });
  ipcMain.on(Channels.confirmClose, (e) => {
    const win = fromEvent(e);
    const state = win && windows.get(win.id);
    if (!win || !state || !trusted(e)) return;
    state.allowClose = true;
    // Hide first: the page unloads before the window goes, and would show a blank (white) frame meanwhile.
    win.hide();
    win.close();
  });
  ipcMain.on(Channels.cancelClose, (e) => {
    if (trusted(e)) quitting = false;
  });
}

/** Open folders in the first window, or remember them until one has loaded. */
function openInWindow(paths: readonly string[]): void {
  const win = BrowserWindow.getAllWindows()[0];
  if (win && !win.webContents.isLoading()) {
    for (const p of paths) win.webContents.send(Channels.openFolder, p);
    return;
  }
  pendingOpen.push(...paths);
  if (!win && app.isReady()) createWindow();
}

app.on('open-file', (e, p) => {
  e.preventDefault();
  openInWindow(grantAll(folderArgs([p], process.cwd())));
});

app.on('second-instance', (_e, argv, workingDirectory) => {
  const folders = grantAll(folderArgs(argv.slice(1), workingDirectory));
  const win = BrowserWindow.getAllWindows()[0];
  if (win) {
    if (win.isMinimized()) win.restore();
    win.focus();
  }
  if (folders.length > 0 || !win) openInWindow(folders.slice(-1));
});

app.on('before-quit', () => {
  quitting = true;
});

const updatePrefsFile = path.join(app.getPath('userData'), 'updates.json');
/** One check at a time; Check for Updates… waits for an automatic check that is running, then answers. */
const updateChecks = new UpdateChecks(checkForUpdates);

/**
 * Look for a newer release on GitHub (PRD §3.1). Puffinote never downloads or installs anything itself: when
 * there is one, Download opens its release page. A check from the menu always answers; the daily automatic
 * one stays quiet unless there's a version the user hasn't skipped.
 */
async function checkForUpdates(manual: boolean): Promise<void> {
  let prefs: UpdatePrefs = await loadUpdatePrefs(updatePrefsFile);
  if (!manual) {
    prefs = { ...prefs, lastCheck: Date.now() };
    await saveUpdatePrefs(updatePrefsFile, prefs).catch(() => undefined);
  }
  const parent = BrowserWindow.getFocusedWindow() ?? BrowserWindow.getAllWindows()[0];
  const show = (opts: Electron.MessageBoxOptions) =>
    parent ? dialog.showMessageBox(parent, opts) : dialog.showMessageBox(opts);
  let release;
  try {
    release = await fetchLatestRelease(net.fetch as typeof fetch);
  } catch (err) {
    if (manual)
      await show({
        type: 'warning',
        message: 'Puffinote couldn’t check for updates.',
        detail: `${err instanceof Error ? err.message : String(err)}. Check your connection and try again.`,
      });
    return;
  }
  const current = app.getVersion();
  if (!isNewer(release.version, current)) {
    if (manual)
      await show({
        type: 'info',
        message: 'Puffinote is up to date.',
        detail: `Version ${current} is the newest.`,
      });
    return;
  }
  if (!manual && prefs.skipped === release.version) return;
  const { response, checkboxChecked } = await show({
    type: 'info',
    message: `Puffinote ${release.version} is available.`,
    detail: `You have ${current}. Download opens the release page, where you can read what’s new and get the new version.`,
    buttons: ['Download', 'Later', 'Skip This Version'],
    defaultId: 0,
    cancelId: 1,
    checkboxLabel: 'Check for updates automatically',
    checkboxChecked: prefs.auto,
  });
  prefs = {
    ...prefs,
    auto: checkboxChecked,
    skipped: response === 2 ? release.version : prefs.skipped,
  };
  await saveUpdatePrefs(updatePrefsFile, prefs).catch(() => undefined);
  if (response === 0) await shell.openExternal(release.url);
}

/** On start, in a packaged app, check once a day unless the user turned it off. */
async function autoCheckForUpdates(): Promise<void> {
  if (!app.isPackaged || headless || process.env.PUFFINOTE_NO_UPDATE_CHECK === '1') return;
  if (!checkDue(await loadUpdatePrefs(updatePrefsFile), Date.now())) return;
  setTimeout(() => {
    updateChecks.request(false).catch(logFailure('updateChecks.request'));
  }, 5000);
}

/** `pnfile://local/Users/me/Notes/pic.png` → the absolute path, on macOS/Linux and Windows. */
function fileFromUrl(url: string): string {
  const pathname = decodeURIComponent(new URL(url).pathname);
  return /^\/[A-Za-z]:\//.test(pathname) ? pathname.slice(1) : pathname;
}

app
  .whenReady()
  .then(async () => {
    if (!single) return; // quitting: the running instance has the folders
    protocol.handle('app', (req) => {
      const { pathname } = new URL(req.url);
      const file = path.normalize(path.join(webRoot, decodeURIComponent(pathname)));
      if (!file.startsWith(webRoot + path.sep)) return new Response('Forbidden', { status: 403 });
      return net.fetch(pathToFileURL(file).toString());
    });
    protocol.handle(FILE_SCHEME, async (req) => {
      const file = fileFromUrl(req.url);
      if (!(await grants.allows(file))) return new Response('Forbidden', { status: 403 });
      return net.fetch(pathToFileURL(file).toString());
    });
    registerIpc();
    await grants.load();
    lastWindow = await loadSavedWindow(windowFile);
    createWindow();
    autoCheckForUpdates().catch(logFailure('autoCheckForUpdates'));
    app.on('activate', () => {
      if (BrowserWindow.getAllWindows().length === 0) createWindow();
    });
  })
  .catch(logFailure('app.whenReady'));

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin' || quitting) app.quit();
});

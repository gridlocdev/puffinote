/**
 * The IPC contract between the Electron main process and the preload, shared by `main.ts` and `preload.cts`.
 * The renderer sees `window.puffinoteNative`, typed on the UI side as `PuffinoteNative` in
 * `packages/ui/src/platform/nativeStore.ts` — keep the three in step.
 */

/** A menu the renderer builds from its command catalog; `command` ids come back via onCommand. */
export interface NativeMenuItem {
  readonly label?: string;
  readonly command?: string;
  readonly accelerator?: string;
  readonly enabled?: boolean;
  readonly checked?: boolean;
  readonly role?: string;
  readonly type?: 'normal' | 'separator' | 'checkbox' | 'submenu';
  readonly submenu?: readonly NativeMenuItem[];
}

export interface NativeEntry {
  readonly path: string;
  readonly name: string;
  readonly kind: 'file' | 'dir';
  readonly size: number;
  readonly modified: number;
  readonly created?: number;
}

export const Channels = {
  pickFolder: 'puffinote:pick-folder',
  /** Was this notebook folder deleted (its parent still there)? For pruning Recent Notebooks. */
  folderGone: 'puffinote:folder-gone',
  list: 'puffinote:list',
  read: 'puffinote:read',
  write: 'puffinote:write',
  mkdir: 'puffinote:mkdir',
  move: 'puffinote:move',
  trash: 'puffinote:trash',
  reveal: 'puffinote:reveal',
  openExternal: 'puffinote:open-external',
  /** Lay a note's exported HTML out for print and save it as a PDF where the user picks. */
  savePdf: 'puffinote:save-pdf',
  watch: 'puffinote:watch',
  setTitle: 'puffinote:set-title',
  /** The height of the page's title bar, so the macOS traffic lights sit in its middle. */
  setTitleBarHeight: 'puffinote:set-title-bar-height',
  setMenu: 'puffinote:set-menu',
  confirmClose: 'puffinote:confirm-close',
  /** The renderer kept the window open (the user cancelled the unsaved-changes prompt). */
  cancelClose: 'puffinote:cancel-close',
  /** Sent by the preload as soon as a close request arrives: proves the renderer is alive. */
  closeAck: 'puffinote:close-ack',
  // main → renderer
  command: 'puffinote:command',
  openFolder: 'puffinote:open-folder',
  closeRequested: 'puffinote:close-requested',
  changed: 'puffinote:changed',
} as const;

/** IPC error prefixes the renderer turns back into ConflictError / NotFoundError (see nativeStore.ts). */
export const CONFLICT_CHANGED = 'PUFFINOTE_CONFLICT_CHANGED';
export const CONFLICT_EXISTS = 'PUFFINOTE_CONFLICT_EXISTS';
export const NOT_FOUND = 'PUFFINOTE_NOT_FOUND';

/**
 * An expected failure (a conflict or a missing file), sent back from a file handler as its result instead of
 * thrown: Electron prints every handler that throws to the terminal, and these happen in normal use (the state
 * folder's `.gitignore` already being there). The preload turns it back into a rejection with this message.
 */
export interface IpcFailure {
  readonly puffinoteFailure: string;
}

/** Run a file handler, returning a conflict or not-found error as an `IpcFailure`; anything else still throws. */
export async function asResult<T>(run: () => Promise<T>): Promise<T | IpcFailure> {
  try {
    return await run();
  } catch (e) {
    const message = e instanceof Error ? e.message : '';
    if ([CONFLICT_CHANGED, CONFLICT_EXISTS, NOT_FOUND].some((code) => message.startsWith(code))) {
      return { puffinoteFailure: message };
    }
    throw e;
  }
}

/** The scheme notebook pictures load from: `pnfile://local/<absolute path, URL-encoded>`. */
export const FILE_SCHEME = 'pnfile';

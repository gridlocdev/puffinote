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
  list: 'puffinote:list',
  read: 'puffinote:read',
  write: 'puffinote:write',
  mkdir: 'puffinote:mkdir',
  move: 'puffinote:move',
  trash: 'puffinote:trash',
  reveal: 'puffinote:reveal',
  openExternal: 'puffinote:open-external',
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

/** The scheme notebook pictures load from: `pnfile://local/<absolute path, URL-encoded>`. */
export const FILE_SCHEME = 'pnfile';

// Sandboxed preload: may only require('electron'), so channel names are inlined here.
// Keep them in step with `Channels` in bridge.ts.
import type { NativeEntry, NativeMenuItem } from './bridge.ts';

// A sandboxed preload is CommonJS and can only reach Electron through require().
// eslint-disable-next-line @typescript-eslint/no-require-imports, @typescript-eslint/consistent-type-imports
const { contextBridge, ipcRenderer } = require('electron') as typeof import('electron');

function on(channel: string, cb: (...args: never[]) => void): () => void {
  const listener = (_e: unknown, ...args: unknown[]) => (cb as (...a: unknown[]) => void)(...args);
  ipcRenderer.on(channel, listener);
  return () => ipcRenderer.removeListener(channel, listener);
}

contextBridge.exposeInMainWorld('puffinoteNative', {
  platform: process.platform,
  pickFolder: (): Promise<string | null> => ipcRenderer.invoke('puffinote:pick-folder'),
  list: (dir: string): Promise<NativeEntry[]> => ipcRenderer.invoke('puffinote:list', dir),
  read: (path: string): Promise<{ bytes: Uint8Array; modified: number }> =>
    ipcRenderer.invoke('puffinote:read', path),
  write: (
    path: string,
    bytes: Uint8Array,
    expected: number | null,
    force: boolean,
    create: boolean,
  ): Promise<number> => ipcRenderer.invoke('puffinote:write', path, bytes, expected, force, create),
  mkdir: (path: string): Promise<void> => ipcRenderer.invoke('puffinote:mkdir', path),
  move: (from: string, to: string): Promise<void> => ipcRenderer.invoke('puffinote:move', from, to),
  trash: (path: string): Promise<void> => ipcRenderer.invoke('puffinote:trash', path),
  reveal: (path: string): Promise<void> => ipcRenderer.invoke('puffinote:reveal', path),
  openExternal: (path: string): Promise<void> => ipcRenderer.invoke('puffinote:open-external', path),
  savePdf: (html: string, name: string): Promise<boolean> =>
    ipcRenderer.invoke('puffinote:save-pdf', html, name),
  fileUrl: (path: string): string =>
    'pnfile://local/' +
    path.replace(/\\/g, '/').replace(/^\//, '').split('/').map(encodeURIComponent).join('/'),
  watch: (root: string | null): void => ipcRenderer.send('puffinote:watch', root),
  onChanged: (cb: (paths: readonly string[]) => void) => on('puffinote:changed', cb),
  onOpenFolder: (cb: (path: string) => void) => on('puffinote:open-folder', cb),
  setTitle: (title: string, dirty: boolean): void => ipcRenderer.send('puffinote:set-title', title, dirty),
  setTitleBarHeight: (height: number): void => ipcRenderer.send('puffinote:set-title-bar-height', height),
  setMenu: (menu: readonly NativeMenuItem[]): void => ipcRenderer.send('puffinote:set-menu', menu),
  onCommand: (cb: (id: string) => void) => on('puffinote:command', cb),
  onCloseRequested: (cb: () => void) =>
    on('puffinote:close-requested', () => {
      // Tell the main process the page is alive before it starts asking about unsaved changes.
      ipcRenderer.send('puffinote:close-ack');
      cb();
    }),
  confirmClose: (): void => ipcRenderer.send('puffinote:confirm-close'),
  cancelClose: (): void => ipcRenderer.send('puffinote:cancel-close'),
});

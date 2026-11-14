import { describe, expect, it } from 'vitest';
import { ShellModel, toAccelerator, type NativeMenuItem } from '@puffinote/ui';
import { nativeMenu } from '../src/nativeMenu.ts';

/** The accelerators Electron gives its menu roles, per platform (Electron's `roles.ts`). */
function roleAccelerators(role: string, mac: boolean): string[] {
  const own: Record<string, string> = {
    undo: 'CmdOrCtrl+Z',
    redo: mac ? 'Shift+CmdOrCtrl+Z' : 'CmdOrCtrl+Y',
    cut: 'CmdOrCtrl+X',
    copy: 'CmdOrCtrl+C',
    paste: 'CmdOrCtrl+V',
    selectAll: 'CmdOrCtrl+A',
    minimize: 'CmdOrCtrl+M',
    close: 'CmdOrCtrl+W',
    quit: mac ? 'CmdOrCtrl+Q' : '',
    reload: 'CmdOrCtrl+R',
    forceReload: 'Shift+CmdOrCtrl+R',
    toggleDevTools: mac ? 'Alt+CmdOrCtrl+I' : 'Ctrl+Shift+I',
    togglefullscreen: mac ? 'Ctrl+Cmd+F' : 'F11',
  };
  // The window menu role expands to these items (Close only off macOS).
  const expands: Record<string, string[]> = {
    windowMenu: mac ? ['minimize', 'zoom', 'front'] : ['minimize', 'zoom', 'close'],
    fileMenu: [mac ? 'close' : 'quit'],
  };
  return (expands[role] ?? [role]).map((r) => own[r] ?? '').filter(Boolean);
}

function roles(items: readonly NativeMenuItem[]): string[] {
  return items.flatMap((i) => [...(i.role ? [i.role] : []), ...roles(i.submenu ?? [])]);
}

describe('the desktop menu bar', () => {
  for (const mac of [true, false])
    it(`gives no menu role a shortcut a catalog command uses (${mac ? 'macOS' : 'Windows, Linux'})`, () => {
      const catalog = new ShellModel().menuCatalog;
      const taken = new Set(
        catalog.all
          .flatMap((c) => [c.shortcutKey, ...c.alternateKeys])
          .filter((k) => k !== null)
          .map((k) => toAccelerator(k).toLowerCase()),
      );
      expect(taken.has('cmdorctrl+w')).toBe(true); // Close Tab
      const clashes = roles(nativeMenu(catalog, mac)).flatMap((r) =>
        roleAccelerators(r, mac)
          .filter((a) => taken.has(a.toLowerCase()))
          .map((a) => `${r}: ${a}`),
      );
      expect(clashes).toEqual([]);
    });
});

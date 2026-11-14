import {
  isMac,
  toAccelerator,
  type CommandCatalog,
  type CommandGroup,
  type NativeMenuItem,
} from '@puffinote/ui';

const MENU_ORDER: readonly CommandGroup[] = ['File', 'Edit', 'Insert', 'Format', 'View', 'Go', 'Help'];

/** Clipboard items are Electron roles, so the note gets real cut/copy/paste events. Added after these ids. */
const CLIPBOARD_AFTER = 'edit.redo';
const CLIPBOARD: readonly NativeMenuItem[] = [
  { type: 'separator' },
  { role: 'cut' },
  { role: 'copy' },
  { role: 'paste' },
];

/** These go at the end of their menu, after the editor's own items. */
const LAST = new Set([
  'shell.find',
  'shell.findNext',
  'shell.findPrevious',
  'shell.replace',
  'shell.replaceOne',
  'shell.replaceAll',
]);

/** Separators before these ids. */
const BREAKS = new Set([
  'shell.find',
  'shell.openNotebook',
  'shell.save',
  'shell.exportHtml',
  'shell.closeTab',
  'shell.rename',
  'shell.refresh',
  'edit.selectAll',
  'edit.moveLineUp',
  'format.task',
  'format.quote',
  'format.paragraph',
  'format.link',
  'view.viewRich',
  'view.toggleSidebar',
  'view.zoomIn',
  'view.sidebar.files',
  'shell.nextTab',
  'shell.focusTree',
]);

/**
 * The desktop app's menu bar, built from the same catalog as ⌘K. Roles bring their own accelerators, so none
 * may use a catalog shortcut: off macOS Electron's `windowMenu` has Close on Ctrl+W, which is Close Tab.
 */
export function nativeMenu(catalog: CommandCatalog, mac = isMac): NativeMenuItem[] {
  const menus: NativeMenuItem[] = [];
  for (const group of MENU_ORDER) {
    const items: NativeMenuItem[] = [];
    const inGroup = catalog.all.filter((c) => c.group === group);
    for (const c of [...inGroup.filter((c) => !LAST.has(c.id)), ...inGroup.filter((c) => LAST.has(c.id))]) {
      if (BREAKS.has(c.id) && items.length > 0 && items.at(-1)?.type !== 'separator')
        items.push({ type: 'separator' });
      items.push({
        label: c.title,
        command: c.id,
        enabled: catalog.isEnabled(c),
        ...(c.shortcutKey ? { accelerator: toAccelerator(c.shortcutKey) } : {}),
        ...(c.active ? { type: 'checkbox' as const, checked: c.active() } : {}),
      });
      if (c.id === CLIPBOARD_AFTER) items.push(...CLIPBOARD);
    }
    if (items.length > 0) menus.push({ label: group, submenu: items });
  }
  menus.push(mac ? { role: 'windowMenu' } : { label: 'Window', submenu: [{ role: 'minimize' }] });
  return menus;
}

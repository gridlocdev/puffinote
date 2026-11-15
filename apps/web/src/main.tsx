import { computed, effect } from '@preact/signals';
import { render } from 'preact';
import { localPrefs, pickImportFiles, Shell, ShellModel } from '@puffinote/ui';
import '@puffinote/ui/styles.css';
import { Launcher } from './launcher.ts';
import { nativeMenu } from './nativeMenu.ts';
import { Welcome } from './Welcome.tsx';
import './welcome.css';

const host = await Launcher.detect();
const model = new ShellModel({
  // The desktop app brings the folder dialog straight up; a NAS shows the server's folders on the welcome
  // screen. A plain browser has only its own notebook, so the command is hidden there.
  openNotebook: host.native
    ? (): void => void launcher.openFolder()
    : host.kind === 'nas'
      ? (): void => launcher.close()
      : undefined,
  // The file dialog needs a click in the page; the desktop menu bar can't give it one, and Finder is there.
  pickFiles: host.native ? undefined : pickImportFiles,
  titleBar: host.native?.platform === 'darwin',
  setTitleBarHeight: (h) => host.native?.setTitleBarHeight(h),
});
const launcher = new Launcher(model, localPrefs, host);
document.documentElement.dataset.host = host.kind;

const app = document.getElementById('app')!;
app.replaceChildren(); // index.html's "Starting Puffinote…", shown until the script got this far
render(<Shell model={model} prefs={localPrefs} welcome={<Welcome launcher={launcher} />} />, app);
await launcher.start();
// Dev-only handle for poking at the app from the console.
if (import.meta.env.DEV) Object.assign(globalThis, { puffinote: { model, launcher } });

// ── Electron ────────────────────────────────────────────────────────────
const native = host.native;
if (native) {
  const catalog = model.menuCatalog;
  // Menu accelerators fire before the page sees the key. In a text field other than the note (a dialog, the
  // filter box), the note's commands give the field its usual behaviour instead (⌘A selects its text).
  const TEXT_FALLBACK: Record<string, string> = {
    'edit.selectAll': 'selectAll',
    'edit.undo': 'undo',
    'edit.redo': 'redo',
  };
  native.onCommand((id) => {
    const field = document.activeElement?.closest('input, textarea, select, [contenteditable]');
    const inField = !!field && !field.classList.contains('pn-input');
    if ((inField || model.overlay.value) && /^(edit|format|insert)\./.test(id)) {
      const fallback = TEXT_FALLBACK[id];
      if (fallback && inField) document.execCommand(fallback);
      return;
    }
    // A question someone waits on takes no menu commands (the catalog's gate). Quit and Open Folder still
    // work; they aren't catalog commands, and their own question replaces it, answering it as cancelled.
    if (model.overlay.value && !catalog.get(id)?.global) return;
    void catalog.run(id);
  });
  native.onOpenFolder((path) => {
    void model.confirmDiscardAll().then((ok) => (ok ? launcher.openNativePath(path) : undefined));
  });
  native.onCloseRequested(() => {
    void model.confirmDiscardAll().then((ok) => (ok ? native.confirmClose() : native.cancelClose()));
  });

  effect(() => {
    const nb = model.notebook.value;
    native.setTitle(model.title.value, !!nb?.tabs.value.some((t) => t.dirty.value));
  });

  // Rebuild the menu when enabled/checked states may have moved; coalesced to one send.
  let queued: ReturnType<typeof setTimeout> | null = null;
  // Find Next and Find Previous follow whether the find bar has text, not every letter typed into it.
  const hasFindText = computed(() => !!model.findQuery.value);
  effect(() => {
    const nb = model.notebook.value;
    void model.activeTab.value;
    void model.activeTab.value?.dirty.value;
    void model.activeState.value?.mode.value;
    void model.showTabBar.value;
    void nb?.tabs.value;
    void nb?.selected.value;
    void nb?.sidebarCollapsed.value;
    void model.zoom.value;
    void hasFindText.value;
    if (queued) return;
    queued = setTimeout(() => {
      queued = null;
      native.setMenu(nativeMenu(catalog));
    }, 30);
  });
}

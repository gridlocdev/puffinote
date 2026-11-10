import { effect } from '@preact/signals';
import { render } from 'preact';
import { localPrefs, Shell, ShellModel } from '@puffinote/ui';
import '@puffinote/ui/styles.css';
import { Launcher } from './launcher.ts';
import { nativeMenu } from './nativeMenu.ts';
import { Welcome } from './Welcome.tsx';
import './welcome.css';

const host = await Launcher.detect();
const model = new ShellModel({
  // On the desktop the folder dialog comes straight up; on the web it's back to the welcome screen.
  openNotebook: () => (host.native ? void launcher.openFolder() : launcher.close()),
  titleBar: host.native?.platform === 'darwin',
});
const launcher = new Launcher(model, localPrefs, host);
document.documentElement.dataset.host = host.kind;

render(
  <Shell model={model} prefs={localPrefs} welcome={<Welcome launcher={launcher} />} />,
  document.getElementById('app')!,
);
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
    // A question someone waits on (Save changes?, a name) takes no menu commands: answer it first. Quit and
    // Open Folder still work; their own question replaces it, answering it as cancelled.
    if (model.asking) return;
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
    if (queued) return;
    queued = setTimeout(() => {
      queued = null;
      native.setMenu(nativeMenu(catalog));
    }, 30);
  });
}

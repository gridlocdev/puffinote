import { useSignalEffect } from '@preact/signals';
import type { ComponentChildren } from 'preact';
import { useEffect } from 'preact/hooks';
import { MarkdownEditor } from '../editor/MarkdownEditor.tsx';
import type { Notebook, Tab } from '../notebook/notebook.ts';
import { allFiles } from '../notebook/tree.ts';
import type { PrefsStorage } from '../notebook/settings.ts';
import { ContextMenu } from './ContextMenu.tsx';
import { ConfirmDialog, PromptDialog } from './Dialog.tsx';
import { Icon } from './Icon.tsx';
import { CommandMenu, QuickOpen } from './ListPopup.tsx';
import { SettingsDialog } from './SettingsDialog.tsx';
import type { ShellModel } from './shellModel.ts';
import { Sidebar } from './Sidebar.tsx';
import { TabStrip } from './TabStrip.tsx';

const NOTICE_MS = 6000;

/**
 * The notebook window (PRD §7): sidebar, tab strip, the note in front, a status bar, and the overlays. The
 * hosts (web, NAS, desktop) put one on the page and hand it a notebook; everything else is in here.
 */
export function Shell(props: {
  readonly model: ShellModel;
  readonly prefs: PrefsStorage;
  /** What the window shows with no notebook open (the host's "open a folder" screen). */
  readonly welcome?: ComponentChildren;
}) {
  const { model } = props;
  const nb = model.notebook.value;

  // Shell keys: the reserved set wins over everything but an open overlay (PRD §7.6).
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.defaultPrevented || model.overlay.peek()) return;
      const t = e.target as HTMLElement | null;
      const inText = !!t && (t.matches('input, textarea, select') || t.isContentEditable);
      const command = model.shellCatalog.forEvent(e, inText);
      if (!command) return;
      e.preventDefault();
      e.stopPropagation();
      model.shellCatalog.run(command.id);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [model]);

  // Unsaved work is never lost to a closed page; the session is written on the way out.
  useEffect(() => {
    const onBeforeUnload = (e: BeforeUnloadEvent) => {
      if (model.notebook.peek()?.dirtyTabs.length) e.preventDefault();
    };
    const onHide = () => void model.notebook.peek()?.persist();
    window.addEventListener('beforeunload', onBeforeUnload);
    window.addEventListener('pagehide', onHide);
    return () => {
      window.removeEventListener('beforeunload', onBeforeUnload);
      window.removeEventListener('pagehide', onHide);
    };
  }, [model]);

  useSignalEffect(() => {
    const title = model.title.value;
    document.title = title;
    model.host.setTitle?.(title);
  });

  useSignalEffect(() => {
    const theme = model.notebook.value?.settings.value.theme ?? props.prefs.load().theme ?? 'system';
    if (theme === 'system') delete document.documentElement.dataset.theme;
    else document.documentElement.dataset.theme = theme;
  });

  useSignalEffect(() => {
    const n = model.notebook.value;
    if (!n?.notice.value) return;
    const t = setTimeout(() => (n.notice.value = null), NOTICE_MS);
    return () => clearTimeout(t);
  });

  const tab = nb?.active.value ?? null;
  const collapsed = nb?.sidebarCollapsed.value ?? true;
  return (
    <div class="pn-shell">
      {nb && !collapsed && <Sidebar model={model} nb={nb} />}
      {nb && collapsed && (
        <div class="pn-handle">
          <button
            class="pn-icon-button"
            title={`Show Sidebar (${model.menuCatalog.get('view.toggleSidebar')?.shortcutText ?? ''})`}
            onClick={() => model.toggleSidebar()}
          >
            <Icon name="panel-left" />
          </button>
        </div>
      )}
      <main class="pn-shell-main">
        {model.showTabBar.value && nb && <TabStrip model={model} nb={nb} />}
        <div class="pn-document">
          {nb && tab ? (
            <Document key={tab.id} model={model} nb={nb} tab={tab} />
          ) : nb ? (
            <EmptyNotebook model={model} />
          ) : (
            (props.welcome ?? <div class="pn-empty">No notebook open.</div>)
          )}
        </div>
        {nb && <StatusBar nb={nb} tab={tab} />}
      </main>
      <Overlays model={model} prefs={props.prefs} />
    </div>
  );
}

function Document(props: { readonly model: ShellModel; readonly nb: Notebook; readonly tab: Tab }) {
  const { model, nb, tab } = props;
  if (!tab.state) {
    return <div class="pn-image-view">{tab.imageUrl && <img src={tab.imageUrl} alt={tab.name} />}</div>;
  }
  void nb.imagesLoaded.value;
  const conflict = tab.conflict.value;
  const path = tab.path.value;
  return (
    <>
      {conflict === 'changed' && (
        <Banner
          text={`“${tab.name}” changed on disk since you opened it.`}
          actions={[
            { label: 'Reload from Disk', run: () => void nb.reload(tab) },
            { label: 'Keep My Version', run: () => void nb.save(tab, true) },
          ]}
        />
      )}
      {conflict === 'deleted' && (
        <Banner
          text={`“${tab.name}” was deleted or moved outside Puffinote.`}
          actions={[
            { label: 'Save Again', run: () => void nb.save(tab, true) },
            { label: 'Close Tab', run: () => void model.closeTab(tab) },
          ]}
        />
      )}
      {tab.readOnly && (
        <Banner
          text="Puffinote couldn’t tell how this file is encoded, so it won’t save changes to it."
          actions={[]}
        />
      )}
      <div class="pn-editor-host">
        <MarkdownEditor
          state={tab.state}
          resolveImage={(url) => nb.resolveImage(path, url)}
          spellcheck={nb.settings.value.spellcheck}
          initialScrollTop={tab.scrollTop}
          onScroll={(top) => (tab.scrollTop = top)}
          placeholder="Start writing…"
        />
      </div>
    </>
  );
}

function Banner(props: {
  readonly text: string;
  readonly actions: readonly { label: string; run: () => void }[];
}) {
  return (
    <div class="pn-banner" role="alert">
      <Icon name="info" />
      <span>{props.text}</span>
      {props.actions.map((a) => (
        <button key={a.label} onClick={a.run}>
          {a.label}
        </button>
      ))}
    </div>
  );
}

function EmptyNotebook({ model }: { readonly model: ShellModel }) {
  const key = (id: string) => model.menuCatalog.get(id)?.shortcutText ?? '';
  return (
    <div class="pn-empty">
      <p>No note open.</p>
      <div class="pn-empty-actions">
        <button onClick={() => void model.newNote()}>
          New Note <kbd>{key('shell.newNote')}</kbd>
        </button>
        <button onClick={() => model.open({ kind: 'quickOpen' })}>
          Go to File <kbd>{key('shell.quickOpen')}</kbd>
        </button>
        <button onClick={() => model.open({ kind: 'commands' })}>
          Commands <kbd>{key('shell.commandMenu')}</kbd>
        </button>
      </div>
    </div>
  );
}

const modeLabels = { live: 'Rich', source: 'Formatted', raw: 'Raw' } as const;

function StatusBar({ nb, tab }: { readonly nb: Notebook; readonly tab: Tab | null }) {
  const notice = nb.notice.value;
  const state = tab?.state ?? null;
  let position = '';
  let words = 0;
  if (state) {
    void state.version.value;
    const p = state.document.getPosition(state.selection.value.caret);
    position = `Ln ${p.line + 1}, Col ${p.column + 1}`;
    words = state.text.match(/\S+/g)?.length ?? 0;
  }
  return (
    <footer class="pn-status">
      <span class={`pn-status-notice${notice?.kind === 'error' ? ' pn-error' : ''}`} role="status">
        {notice?.text ?? (tab?.saved.value ? 'Saved' : '')}
      </span>
      {state && (
        <>
          <span>{position}</span>
          <span>
            {words} {words === 1 ? 'word' : 'words'}
          </span>
          <span class="pn-modes" role="group" aria-label="View">
            {(['live', 'source', 'raw'] as const).map((m) => (
              <button key={m} aria-pressed={state.mode.value === m} onClick={() => (state.mode.value = m)}>
                {modeLabels[m]}
              </button>
            ))}
          </span>
        </>
      )}
    </footer>
  );
}

function Overlays({ model, prefs }: { readonly model: ShellModel; readonly prefs: PrefsStorage }) {
  const o = model.overlay.value;
  const nb = model.notebook.value;
  const close = () => model.close();
  if (!o) return null;
  switch (o.kind) {
    case 'commands':
      return <CommandMenu catalog={model.menuCatalog} filter={(c) => model.offers(c)} onClose={close} />;
    case 'quickOpen':
      return nb ? (
        <QuickOpen
          files={allFiles(nb.tree.value).map((f) => f.path)}
          recent={nb.recent.value}
          showExtensions={nb.settings.value.showFileExtensions}
          onOpen={(p) => void model.openFile(p)}
          onClose={close}
        />
      ) : null;
    case 'settings':
      return <SettingsDialog nb={nb} prefs={prefs} onClose={close} />;
    case 'prompt':
      return <PromptDialog {...o.options} onDone={o.done} />;
    case 'confirm':
      return <ConfirmDialog {...o.options} onDone={o.done} />;
    case 'menu':
      return <ContextMenu x={o.x} y={o.y} items={o.items} onClose={close} />;
  }
}

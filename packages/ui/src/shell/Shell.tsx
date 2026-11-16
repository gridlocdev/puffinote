import { after } from '../schedule.ts';
import { PicturePreview } from './PicturePreview.tsx';
import { useSignalEffect } from '@preact/signals';
import type { ComponentChildren } from 'preact';
import { useEffect } from 'preact/hooks';
import { isComposingKey } from '../commands/shortcut.ts';
import { allFiles } from '../notebook/tree.ts';
import type { PrefsStorage } from '../notebook/settings.ts';
import { ContextMenu } from './ContextMenu.tsx';
import { ConfirmDialog, PromptDialog } from './Dialog.tsx';
import { draggingFiles } from './importPicker.ts';
import { LibraryLayout } from './LibraryLayout.tsx';
import { CommandMenu, MoveToPopup, QuickOpen } from './ListPopup.tsx';
import { baseName } from '../platform/paths.ts';
import { SettingsDialog } from './SettingsDialog.tsx';
import { TrashDialog } from './TrashDialog.tsx';
import { UnusedPicturesDialog } from './UnusedPicturesDialog.tsx';
import type { ShellModel } from './shellModel.ts';
import { RailLayout } from './RailLayout.tsx';

const NOTICE_MS = 6000;
/** Title bar heights, as in shell.css; the desktop window centres its traffic lights in them. */
const RAIL_TITLE_BAR = 34;
const LIBRARY_TITLE_BAR = 38;

/**
 * The notebook window (PRD §5–§9): one of the two layouts around the note in front, and the overlays. The
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

  // Shell keys: the reserved set wins over everything but an open overlay (PRD §8.2).
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      // A key that belongs to an IME composition is the composition's (PRD §8.2).
      if (e.defaultPrevented || model.overlay.peek() || isComposingKey(e)) return;
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

  // As on macOS, a click on a toolbar button (rail, panel header, tabs, view switch) leaves the keyboard where it
  // was, so the caret stays in the note and a dialog's Cancel gives it back there (PRD §6.2). Tab still reaches
  // the buttons, and buttons in dialogs and menus take focus as usual.
  useEffect(() => {
    const onMouseDown = (e: MouseEvent) => {
      const t = e.target as Element | null;
      if (t?.closest('button') && !t.closest('[role="dialog"], [role="menu"], [role="listbox"]'))
        e.preventDefault();
    };
    window.addEventListener('mousedown', onMouseDown, true);
    return () => window.removeEventListener('mousedown', onMouseDown, true);
  }, []);

  // Files dragged from the desktop and let go anywhere but a drop zone (the tree, the library, a note) are
  // ignored, rather than the browser opening them in place of the app (PRD §6.1).
  useEffect(() => {
    const ignore = (e: DragEvent) => {
      if (e.defaultPrevented || !draggingFiles(e)) return;
      e.preventDefault();
      e.dataTransfer!.dropEffect = 'none';
    };
    window.addEventListener('dragover', ignore);
    window.addEventListener('drop', ignore);
    return () => {
      window.removeEventListener('dragover', ignore);
      window.removeEventListener('drop', ignore);
    };
  }, []);

  // Unsaved work is never lost to a closed page; the session is written on the way out.
  useEffect(() => {
    const onBeforeUnload = (e: BeforeUnloadEvent) => {
      if (model.notebook.peek()?.dirtyTabs.length) e.preventDefault();
    };
    const onHide = () => {
      const nb = model.notebook.peek();
      nb?.flushRecovery();
      void nb?.persist();
    };
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

  // Theme, accent and layout live on the root element, where the colour tokens are (styles.css, shell.css).
  useSignalEffect(() => {
    const settings = model.notebook.value?.config.settings.value;
    const prefs = props.prefs.load();
    const root = document.documentElement;
    const theme = settings?.theme ?? prefs.theme ?? 'system';
    if (theme === 'system') delete root.dataset.theme;
    else root.dataset.theme = theme;
    root.dataset.accent = settings?.accent ?? prefs.accent ?? 'blue';
    const layout = settings?.layout ?? prefs.layout ?? 'rail';
    root.dataset.layout = layout;
    model.host.setTitleBarHeight?.(layout === 'library' ? LIBRARY_TITLE_BAR : RAIL_TITLE_BAR);
  });

  useSignalEffect(() => {
    const n = model.notebook.value;
    if (!n?.notice.value) return;
    // An offer (Move to Trash) stays up longer, so there's time to take it.
    return after(n.notice.value.action ? NOTICE_MS * 2 : NOTICE_MS, () => (n.notice.value = null));
  });

  const layout = nb?.config.settings.value.layout ?? props.prefs.load().layout ?? 'rail';
  const shell = (
    <div class={`pn-shell pn-layout-${layout}`}>
      {nb && layout === 'library' && <LibraryLayout model={model} nb={nb} />}
      {nb && layout === 'rail' && <RailLayout model={model} nb={nb} />}
      {!nb && (
        <main class="pn-shell-main">
          <div class="pn-document">{props.welcome ?? <div class="pn-empty">No notebook open.</div>}</div>
        </main>
      )}
      {model.preview.value && (
        <PicturePreview
          preview={model.preview.value}
          onClose={() => model.closePreview()}
          onMenu={(at) => model.openPreviewMenu(at)}
        />
      )}
      <Overlays model={model} prefs={props.prefs} />
    </div>
  );
  if (!model.host.titleBar) return shell;
  // The macOS desktop window hides its own title bar; this one sits under the traffic lights and drags the window.
  return (
    <div class="pn-window">
      <header class="pn-titlebar">
        <span class="pn-title">{model.title.value}</span>
      </header>
      {shell}
    </div>
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
          showExtensions={nb.config.settings.value.showFileExtensions}
          onOpen={(p) => void model.openFile(p)}
          onClose={close}
        />
      ) : null;
    case 'moveTo':
      return nb ? (
        <MoveToPopup
          name={baseName(o.path)}
          folders={model.moveTargets(o.path)}
          notebookName={nb.name}
          onMove={(dir) => void nb.move(o.path, dir)}
          onClose={close}
        />
      ) : null;
    case 'settings':
      return <SettingsDialog nb={nb} prefs={prefs} onClose={close} />;
    case 'unusedPictures':
      return nb ? <UnusedPicturesDialog nb={nb} paths={o.paths} onClose={close} /> : null;
    case 'trash':
      return nb ? (
        <TrashDialog
          nb={nb}
          items={o.items}
          onClose={close}
          onEmpty={() => {
            close();
            void model.emptyTrash();
          }}
        />
      ) : null;
    case 'prompt':
      return <PromptDialog {...o.options} onDone={o.done} />;
    case 'confirm':
      return <ConfirmDialog {...o.options} onDone={o.done} />;
    case 'menu':
      return <ContextMenu x={o.x} y={o.y} title={o.title} items={o.items} onClose={close} />;
  }
}

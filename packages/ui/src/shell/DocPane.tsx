import { MarkdownEditor } from '../editor/MarkdownEditor.tsx';
import type { Notebook, Tab } from '../notebook/notebook.ts';
import { Icon } from './Icon.tsx';
import type { ShellModel } from './shellModel.ts';

/* The note in front, its banners, and the status bar under it: the same in both layouts. */

export function Document(props: { readonly model: ShellModel; readonly nb: Notebook; readonly tab: Tab }) {
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
          initialScrollTop={tab.scrollTop}
          onScroll={(top) => (tab.scrollTop = top)}
          placeholder="Start writing…"
          zoom={model.zoom.value}
          onContextMenu={(at) => model.openEditorMenu(at)}
          onPictures={async (files) =>
            nb.addPictures(
              path,
              await Promise.all(
                files.map(async (f) => ({
                  name: f.name,
                  type: f.type,
                  bytes: new Uint8Array(await f.arrayBuffer()),
                })),
              ),
            )
          }
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

export function EmptyNotebook({ model }: { readonly model: ShellModel }) {
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

export function StatusBar({
  model,
  nb,
  tab,
}: {
  readonly model: ShellModel;
  readonly nb: Notebook;
  readonly tab: Tab | null;
}) {
  const notice = nb.notice.value;
  const state = tab?.state ?? null;
  const zoom = model.zoom.value;
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
        {notice?.action && (
          <button
            class="pn-status-action"
            onClick={() => {
              nb.notice.value = null;
              notice.action!.run();
            }}
          >
            {notice.action.label}
          </button>
        )}
      </span>
      {state && (
        <>
          <span>{position}</span>
          <span>
            {words} {words === 1 ? 'word' : 'words'}
          </span>
          {zoom !== 1 && (
            <button class="pn-status-zoom" title="Actual Size" onClick={() => model.setZoom(1)}>
              {Math.round(zoom * 100)}%
            </button>
          )}
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

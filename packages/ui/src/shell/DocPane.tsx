import { useMemo } from 'preact/hooks';
import { MAX_MATCHES } from '../editor/find.ts';
import { MarkdownEditor } from '../editor/MarkdownEditor.tsx';
import { settleMs, useSettled } from '../editor/settled.ts';
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
        <FindBar model={model} />
        <MarkdownEditor
          state={tab.state}
          resolveImage={(url) => nb.resolveImage(path, url)}
          initialScrollTop={tab.scrollTop}
          onScroll={(top) => (tab.scrollTop = top)}
          placeholder="Start writing…"
          zoom={model.zoom.value}
          sortTasks={nb.settings.value.sortCheckedTasks}
          onContextMenu={(at) => model.openEditorMenu(at)}
          onOpenPicture={(picture) => model.openPreview(picture)}
          findMatches={model.findMatches.value ?? undefined}
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

/** Find in Note (PRD §10.7): a box over the note's top right corner, open from ⌘F until Escape. */
function FindBar({ model }: { readonly model: ShellModel }) {
  const query = model.findQuery.value;
  if (query === null) return null;
  const found = model.findMatches.value;
  const count = found?.matches.length ?? 0;
  const total = count >= MAX_MATCHES ? `${MAX_MATCHES}+` : String(count);
  const status = !query
    ? ''
    : count === 0
      ? 'No results'
      : found!.current >= 0
        ? `${found!.current + 1} of ${total}`
        : `${total} found`;
  const key = (id: string) => model.menuCatalog.get(id)?.shortcutText ?? '';
  return (
    <div class="pn-find" role="search">
      <input
        type="text"
        value={query}
        placeholder="Find in note"
        aria-label="Find in note"
        spellcheck={false}
        autocomplete="off"
        onInput={(e) => model.setFindQuery((e.target as HTMLInputElement).value)}
        onKeyDown={(e) => {
          if (e.key === 'Enter') model.findStep(e.shiftKey ? -1 : 1);
          else if (e.key === 'Escape') model.closeFind();
          else return;
          e.preventDefault();
          e.stopPropagation();
        }}
      />
      <span class="pn-find-count" aria-live="polite">
        {status}
      </span>
      <button
        title={`Previous Match (${key('shell.findPrevious')})`}
        aria-label="Previous match"
        disabled={count === 0}
        onClick={() => model.findStep(-1)}
      >
        <Icon name="chevron-up" />
      </button>
      <button
        title={`Next Match (${key('shell.findNext')})`}
        aria-label="Next match"
        disabled={count === 0}
        onClick={() => model.findStep(1)}
      >
        <Icon name="chevron-down" />
      </button>
      <button title="Close (Esc)" aria-label="Close" onClick={() => model.closeFind()}>
        <Icon name="x" />
      </button>
    </div>
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
  const version = state?.version.value ?? 0;
  // Counted once typing pauses, not on every keystroke or caret move.
  const settled = useSettled(version, state, settleMs);
  const words = useMemo(() => (state ? (state.text.match(/\S+/g)?.length ?? 0) : 0), [state, settled]);
  let position = '';
  if (state) {
    const p = state.document.getPosition(state.selection.value.caret);
    position = `Ln ${p.line + 1}, Col ${p.column + 1}`;
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

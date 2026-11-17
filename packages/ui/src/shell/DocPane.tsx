import { useMemo } from 'preact/hooks';
import { isComposingKey } from '../commands/shortcut.ts';
import { MAX_MATCHES } from '../editor/find.ts';
import { MarkdownEditor } from '../editor/MarkdownEditor.tsx';
import { settleMs, useSettled } from '../editor/settled.ts';
import type { Notebook } from '../notebook/notebook.ts';
import type { Tab } from '../notebook/tab.ts';
import { Icon } from './Icon.tsx';
import type { ShellModel } from './shellModel.ts';

/* The note in front, its banners, and the status bar under it: the same in both layouts. */

const recoveredAt = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' });

export function Document(props: { readonly model: ShellModel; readonly nb: Notebook; readonly tab: Tab }) {
  const { model, nb, tab } = props;
  if (!tab.state) {
    return <div class="pn-image-view">{tab.imageUrl && <img src={tab.imageUrl} alt={tab.name} />}</div>;
  }
  void nb.pictures.loaded.value;
  // A wiki embed is looked up by name in the file tree, so its picture can change when the tree does.
  void nb.tree.value;
  const conflict = tab.conflict.value;
  const recovered = tab.recovered.value;
  const path = tab.path.value;
  return (
    <>
      {conflict === 'changed' && (
        <Banner
          text={`“${tab.name}” changed on disk since you opened it.`}
          actions={[
            {
              label: 'Reload from Disk',
              run: () => {
                nb.reload(tab).catch(nb.report);
              },
            },
            {
              label: 'Keep My Version',
              run: () => {
                nb.save(tab, true).catch(nb.report);
              },
            },
          ]}
        />
      )}
      {conflict === 'deleted' && (
        <Banner
          text={`“${tab.name}” was deleted or moved outside Puffinote.`}
          actions={[
            {
              label: 'Save Again',
              run: () => {
                nb.save(tab, true).catch(nb.report);
              },
            },
            {
              label: 'Close Tab',
              run: () => {
                model.closeTab(tab).catch(model.report);
              },
            },
          ]}
        />
      )}
      {recovered && (
        <Banner
          text={`Recovered unsaved changes to “${tab.name}” from ${recoveredAt.format(recovered.time)}.`}
          actions={[
            { label: 'Restore', run: () => nb.restoreRecovered(tab) },
            { label: 'Discard', run: () => nb.discardRecovered(tab) },
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
          resolveImage={(url) => nb.pictures.resolve(path, url)}
          initialScrollTop={tab.scrollTop}
          onScroll={(top) => (tab.scrollTop = top)}
          placeholder="Start writing…"
          zoom={model.zoom.value}
          sortTasks={nb.config.settings.value.sortCheckedTasks}
          onContextMenu={(at) => model.openEditorMenu(at)}
          onOpenPicture={(picture) => model.openPreview(picture)}
          onFollowLink={(href) => {
            model.followLink(path, href).catch(model.report);
          }}
          findMatches={model.findMatches.value ?? undefined}
          onError={nb.report}
          onCopyPictures={(markdown) => model.copyWithPictures(path, markdown)}
          onPictures={async (files) => {
            // A picture that can't be read or saved is said in the status bar, and nothing is inserted.
            try {
              return await nb.pictures.add(
                path,
                await Promise.all(
                  files.map(async (f) => ({
                    name: f.name,
                    type: f.type,
                    bytes: new Uint8Array(await f.arrayBuffer()),
                  })),
                ),
              );
            } catch (e) {
              nb.report(e);
              return [];
            }
          }}
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
  const replaceOpen = model.replaceOpen.value;
  // Read the signals the enabled state depends on, so the buttons follow them.
  void model.activeTab.value;
  const canReplace = count > 0 && model.canReplace();
  const run = (id: string) => model.shellCatalog.run(id);
  return (
    <div class="pn-find" role="search">
      <div class="pn-find-row">
        <input
          type="text"
          value={query}
          placeholder="Find in note"
          aria-label="Find in note"
          spellcheck={false}
          autocomplete="off"
          onInput={(e) => model.setFindQuery((e.target as HTMLInputElement).value)}
          onKeyDown={(e) => {
            // Enter or Escape that commits or cancels an IME composition stays with it.
            if (isComposingKey(e)) return;
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
      {replaceOpen && (
        <div class="pn-find-row pn-find-replace">
          <input
            type="text"
            value={model.replaceText.value}
            placeholder="Replace with"
            aria-label="Replace with"
            spellcheck={false}
            autocomplete="off"
            onInput={(e) => (model.replaceText.value = (e.target as HTMLInputElement).value)}
            onKeyDown={(e) => {
              if (isComposingKey(e)) return;
              if (e.key === 'Enter') run('shell.replaceOne');
              else if (e.key === 'Escape') model.closeFind();
              else return;
              e.preventDefault();
              e.stopPropagation();
            }}
          />
          <button
            class="pn-find-text"
            title={
              model.activeTab.value?.readOnly
                ? 'This note can’t be saved, so it can’t be changed'
                : 'Replace (Enter)'
            }
            disabled={!canReplace}
            onClick={() => run('shell.replaceOne')}
          >
            Replace
          </button>
          <button class="pn-find-text" disabled={!canReplace} onClick={() => run('shell.replaceAll')}>
            Replace All
          </button>
        </div>
      )}
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
        <button
          onClick={() => {
            model.newNote().catch(model.report);
          }}
        >
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

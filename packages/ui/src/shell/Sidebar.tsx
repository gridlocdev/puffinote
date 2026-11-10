import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'preact/hooks';
import { sidebarPanels, type Notebook, type SearchHit } from '../notebook/notebook.ts';
import { outlineOf } from '@puffinote/markdown';
import { filterTree, findNode, hasFolders } from '../notebook/tree.ts';
import { baseName, parentOf } from '../platform/paths.ts';
import { FileTree } from './FileTree.tsx';
import { Icon } from './Icon.tsx';
import type { ShellModel } from './shellModel.ts';

/** The left sidebar (PRD §6): header, the panel switcher, and the panel. */
export function Sidebar({ model, nb }: { readonly model: ShellModel; readonly nb: Notebook }) {
  const panel = nb.panel.value;
  const tip = (id: string) => {
    const c = model.menuCatalog.get(id);
    return c ? `${c.title}${c.shortcutText ? ` (${c.shortcutText})` : ''}` : '';
  };
  const folders = hasFolders(nb.tree.value);
  const allOpen = model.allExpanded();
  return (
    <aside class="pn-sidebar" style={{ width: `${nb.sidebarWidth.value}px` }} aria-label="Sidebar">
      <header class="pn-sidebar-head">
        <button
          class="pn-icon-button"
          title={tip('view.toggleSidebar')}
          onClick={() => model.toggleSidebar()}
        >
          <Icon name="panel-left" />
        </button>
        <span class="pn-sidebar-title" title={nb.name}>
          {nb.name}
        </span>
        <button class="pn-icon-button" title={tip('shell.newNote')} onClick={() => void model.newNote()}>
          <Icon name="file-plus" />
        </button>
        <button class="pn-icon-button" title={tip('shell.newFolder')} onClick={() => void model.newFolder()}>
          <Icon name="folder-plus" />
        </button>
        <button
          class="pn-icon-button"
          aria-disabled={folders ? undefined : true}
          aria-label={allOpen ? 'Collapse All Folders' : 'Expand All Folders'}
          aria-description={folders ? undefined : 'No folders available for expanding'}
          title={
            folders
              ? allOpen
                ? 'Collapse All Folders'
                : 'Expand All Folders'
              : 'No folders available for expanding'
          }
          onClick={() => model.toggleAllFolders()}
        >
          <Icon name={allOpen ? 'chevrons-up' : 'chevrons-down'} />
        </button>
      </header>
      <nav class="pn-switcher" aria-label="Sidebar panels">
        {sidebarPanels.map((p) => (
          <button
            key={p.id}
            class="pn-icon-button"
            aria-pressed={panel === p.id}
            title={tip(`view.sidebar.${p.id}`)}
            aria-label={p.title}
            onClick={() => model.showPanel(p.id)}
          >
            <Icon name={p.icon} />
          </button>
        ))}
        <span class="pn-switcher-gap" />
        <button
          class="pn-icon-button"
          title={tip('view.sidebar.settings')}
          aria-label="Settings"
          onClick={() => model.open({ kind: 'settings' })}
        >
          <Icon name="settings-2" />
        </button>
      </nav>
      <div class="pn-panel">
        {panel === 'files' && (
          <FileTree
            model={model}
            nb={nb}
            root={nb.tree.value}
            expanded={nb.expanded.value}
            onToggle={(p, open) => nb.toggleFolder(p, open)}
            label="Files"
          />
        )}
        {panel === 'filter' && <FilterPanel model={model} nb={nb} />}
        {panel === 'search' && <SearchPanel model={model} nb={nb} />}
        {panel === 'outline' && <OutlinePanel model={model} nb={nb} />}
        {panel === 'recent' && <RecentPanel model={model} nb={nb} />}
      </div>
      <Resizer nb={nb} />
    </aside>
  );
}

/** A box whose caret arrives when its panel is chosen (PRD §6.2). */
function PanelBox(props: {
  readonly model: ShellModel;
  readonly value: string;
  readonly placeholder: string;
  readonly label: string;
  readonly onInput: (v: string) => void;
  readonly onKeyDown?: (e: KeyboardEvent) => void;
}) {
  const ref = useRef<HTMLInputElement>(null);
  const tick = props.model.focusPanelTick.value;
  useLayoutEffect(() => {
    ref.current?.focus();
    ref.current?.select();
  }, [tick]);
  return (
    <input
      ref={ref}
      class="pn-panel-box"
      type="search"
      value={props.value}
      placeholder={props.placeholder}
      aria-label={props.label}
      spellcheck={false}
      autocomplete="off"
      onInput={(e) => props.onInput((e.target as HTMLInputElement).value)}
      onKeyDown={(e) => {
        if (e.key === 'Escape' && props.value) {
          e.preventDefault();
          props.onInput('');
        } else if (e.key === 'ArrowDown' && !props.onKeyDown) {
          e.preventDefault();
          (e.currentTarget as HTMLElement).parentElement?.querySelector<HTMLElement>('.pn-tree')?.focus();
        } else props.onKeyDown?.(e);
      }}
    />
  );
}

function FilterPanel({ model, nb }: { readonly model: ShellModel; readonly nb: Notebook }) {
  const query = model.filterQuery.value;
  const result = useMemo(() => filterTree(nb.tree.value, query), [nb.tree.value, query]);
  // The folders leading to matches open themselves, but aren't held open: these toggles are the panel's own.
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(result.opened);
  useEffect(() => setExpanded(result.opened), [result]);
  return (
    <>
      <PanelBox
        model={model}
        value={query}
        placeholder="Filter by name"
        label="Filter files by name"
        onInput={(v) => (model.filterQuery.value = v)}
      />
      {query.trim() && (
        <p class="pn-panel-note" role="status">
          {result.matches === 0
            ? 'No matches'
            : `${result.matches} ${result.matches === 1 ? 'match' : 'matches'}`}
        </p>
      )}
      <FileTree
        model={model}
        nb={nb}
        root={query.trim() ? result.tree : nb.tree.value}
        expanded={query.trim() ? expanded : nb.expanded.value}
        onToggle={(p, open) => {
          if (!query.trim()) return nb.toggleFolder(p, open);
          const next = new Set(expanded);
          if (open) next.add(p);
          else next.delete(p);
          setExpanded(next);
        }}
        label="Filtered files"
      />
    </>
  );
}

function SearchPanel({ model, nb }: { readonly model: ShellModel; readonly nb: Notebook }) {
  const query = model.searchQuery.value;
  const [hits, setHits] = useState<readonly SearchHit[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [index, setIndex] = useState(0);
  const list = useRef<HTMLUListElement>(null);
  useEffect(() => {
    if (!query.trim()) {
      setHits(null);
      setBusy(false);
      return;
    }
    // A newer query (or a changed tree) stops this scan where it is, not just hides its answer.
    const abort = new AbortController();
    setBusy(true);
    const t = setTimeout(() => {
      void nb.search(query, { signal: abort.signal }).then((h) => {
        if (abort.signal.aborted) return;
        setHits(h);
        setIndex(0);
        setBusy(false);
      });
    }, 200);
    return () => {
      abort.abort();
      clearTimeout(t);
    };
    // The tree keeps its object while its entries are the same (`Notebook.reloadTree`), so a refresh on
    // window focus doesn't start the scan again.
  }, [query, nb.tree.value]);

  const open = async (hit: SearchHit) => {
    const tab = await nb.openFile(hit.path);
    if (hit.inTitle) model.focusEditor();
    else if (tab?.state) {
      tab.state.reveal(hit.offset, hit.offset + hit.length);
      model.focusEditor();
    }
  };
  const files = hits ? new Set(hits.map((h) => h.path)).size : 0;
  const onKey = (e: KeyboardEvent) => {
    if (!hits || hits.length === 0) return;
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault();
      const i = Math.max(0, Math.min(hits.length - 1, index + (e.key === 'ArrowDown' ? 1 : -1)));
      setIndex(i);
      list.current?.children[i]?.scrollIntoView({ block: 'nearest' });
    } else if (e.key === 'Enter') {
      e.preventDefault();
      const h = hits[index];
      if (h) void open(h);
    }
  };
  let lastPath = '';
  return (
    <>
      <PanelBox
        model={model}
        value={query}
        placeholder="Search notes"
        label="Search the notebook"
        onInput={(v) => (model.searchQuery.value = v)}
        onKeyDown={onKey}
      />
      {query.trim() && (
        <p class="pn-panel-note" role="status">
          {busy && !hits
            ? 'Searching…'
            : hits && hits.length > 0
              ? `${hits.length}${hits.length >= 500 ? '+' : ''} results in ${files} ${files === 1 ? 'note' : 'notes'}`
              : 'No results'}
        </p>
      )}
      <ul
        class="pn-list"
        role="listbox"
        aria-label="Search results"
        ref={list}
        tabIndex={-1}
        onKeyDown={onKey}
      >
        {(hits ?? []).map((h, i) => {
          const head = h.path !== lastPath;
          lastPath = h.path;
          return (
            <li
              key={`${h.path}:${h.inTitle ? 'title' : h.offset}`}
              role="option"
              aria-selected={i === index}
              class={head ? 'pn-hit pn-hit-first' : 'pn-hit'}
              onClick={() => {
                setIndex(i);
                void open(h);
              }}
            >
              {h.inTitle ? (
                <span class="pn-hit-file">
                  <Marked hit={h} />
                </span>
              ) : (
                <>
                  {head && <span class="pn-hit-file">{baseName(h.path)}</span>}
                  <span class="pn-hit-line">
                    <Marked hit={h} />
                  </span>
                </>
              )}
            </li>
          );
        })}
      </ul>
    </>
  );
}

/** A hit's preview with the match highlighted. */
function Marked({ hit }: { readonly hit: SearchHit }) {
  return (
    <>
      {hit.preview.slice(0, hit.matchStart)}
      <mark>{hit.preview.slice(hit.matchStart, hit.matchStart + hit.length)}</mark>
      {hit.preview.slice(hit.matchStart + hit.length)}
    </>
  );
}

function OutlinePanel({ model, nb }: { readonly model: ShellModel; readonly nb: Notebook }) {
  const state = nb.active.value?.state ?? null;
  if (!state) return <p class="pn-panel-note">Open a note to see its headings.</p>;
  void state.version.value;
  const items = outlineOf(state.document, state.model);
  const caret = state.selection.value.caret;
  let current = -1;
  for (let i = 0; i < items.length; i++) if (items[i]!.offset <= caret + 1) current = i;
  if (items.length === 0) return <p class="pn-panel-note">This note has no headings.</p>;
  const min = Math.min(...items.map((o) => o.level));
  return (
    <ul class="pn-list pn-outline" role="listbox" aria-label="Outline">
      {items.map((o, i) => (
        <li
          key={o.line}
          role="option"
          tabIndex={0}
          aria-selected={i === current}
          style={{ paddingLeft: `${10 + (o.level - min) * 14}px` }}
          onClick={() => {
            state.reveal(o.offset);
            model.focusEditor();
          }}
          onKeyDown={(e) => {
            if (e.key === 'Enter') {
              state.reveal(o.offset);
              model.focusEditor();
            } else if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
              e.preventDefault();
              const sib = (e.currentTarget as HTMLElement)[
                e.key === 'ArrowDown' ? 'nextElementSibling' : 'previousElementSibling'
              ] as HTMLElement | null;
              sib?.focus();
            }
          }}
        >
          {o.text || '(untitled)'}
        </li>
      ))}
    </ul>
  );
}

function RecentPanel({ model, nb }: { readonly model: ShellModel; readonly nb: Notebook }) {
  const recent = nb.recent.value.filter((p) => findNode(nb.tree.value, p));
  if (recent.length === 0) return <p class="pn-panel-note">Notes you open show up here.</p>;
  return (
    <ul class="pn-list" role="listbox" aria-label="Recent files">
      {recent.map((p) => (
        <li
          key={p}
          role="option"
          tabIndex={0}
          aria-selected={nb.active.value?.path.value === p}
          title={p}
          onClick={() => void model.openFile(p)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') void model.openFile(p);
            else if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
              e.preventDefault();
              const sib = (e.currentTarget as HTMLElement)[
                e.key === 'ArrowDown' ? 'nextElementSibling' : 'previousElementSibling'
              ] as HTMLElement | null;
              sib?.focus();
            }
          }}
        >
          <span class="pn-recent-name">{baseName(p).replace(/\.[^.]+$/, '')}</span>
          <span class="pn-recent-dir">{parentOf(p)}</span>
        </li>
      ))}
    </ul>
  );
}

/** Drag the sidebar's right edge to resize it; the width is remembered per notebook. */
function Resizer({ nb }: { readonly nb: Notebook }) {
  return (
    <div
      class="pn-resizer"
      role="separator"
      aria-orientation="vertical"
      aria-label="Resize sidebar"
      onPointerDown={(e) => {
        e.preventDefault();
        const startX = e.clientX;
        const startW = nb.sidebarWidth.peek();
        const move = (ev: PointerEvent) => {
          nb.sidebarWidth.value = Math.max(180, Math.min(560, startW + ev.clientX - startX));
        };
        const up = () => {
          window.removeEventListener('pointermove', move);
          window.removeEventListener('pointerup', up);
        };
        window.addEventListener('pointermove', move);
        window.addEventListener('pointerup', up);
      }}
    />
  );
}

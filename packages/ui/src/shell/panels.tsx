/* The panels the Icon rail layout shows (PRD §6.3); the Library layout borrows Outline and Recent. */

import { after } from '../schedule.ts';
import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'preact/hooks';
import type { Notebook } from '../notebook/notebook.ts';
import type { SearchHit } from '../notebook/search.ts';
import { outlineOf } from '@puffinote/markdown';
import { settleMs, useSettled } from '../editor/settled.ts';
import { filterTree, findNode } from '../notebook/tree.ts';
import { baseName, parentOf } from '../platform/paths.ts';
import { FileTree } from './FileTree.tsx';
import type { ShellModel } from './shellModel.ts';

/** A box whose caret arrives when its panel is chosen (PRD §6.3). */
export function PanelBox(props: {
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

export function FilterPanel({ model, nb }: { readonly model: ShellModel; readonly nb: Notebook }) {
  const query = model.session.filterQuery.value;
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
        onInput={(v) => (model.session.filterQuery.value = v)}
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

/** What one search found, or why it failed, with the query it was for. */
export type SearchResult =
  | { readonly query: string; readonly hits: readonly SearchHit[] }
  | { readonly query: string; readonly error: string };

/**
 * Run one search and hand `done` its hits or its error, so the panel never stays on "Searching…". Says
 * nothing once `signal` is aborted: a newer query (or a changed tree) owns the panel by then.
 */
export async function runSearch(
  search: (query: string, opts: { signal: AbortSignal }) => Promise<readonly SearchHit[]>,
  query: string,
  signal: AbortSignal,
  done: (result: SearchResult) => void,
): Promise<void> {
  let result: SearchResult;
  try {
    result = { query, hits: await search(query, { signal }) };
  } catch (e) {
    result = { query, error: e instanceof Error ? e.message : String(e) };
  }
  if (!signal.aborted) done(result);
}

export function SearchPanel({ model, nb }: { readonly model: ShellModel; readonly nb: Notebook }) {
  const query = model.session.searchQuery.value;
  const [result, setResult] = useState<SearchResult | null>(null);
  const [index, setIndex] = useState(0);
  const list = useRef<HTMLUListElement>(null);
  // Hits for an older query are stale: they go as soon as the query changes, and the panel says it is
  // searching until this query's answer (or error) comes.
  const fresh = result !== null && result.query === query ? result : null;
  const hits = fresh && 'hits' in fresh ? fresh.hits : null;
  const failed = fresh && 'error' in fresh ? fresh.error : null;
  useEffect(() => {
    if (!query.trim()) {
      setResult(null);
      return;
    }
    // A newer query (or a changed tree) stops this scan where it is, not just hides its answer.
    const abort = new AbortController();
    const cancel = after(200, () => {
      runSearch(
        (q, opts) => nb.textSearch.find(q, opts),
        query,
        abort.signal,
        (r) => {
          setResult(r);
          setIndex(0);
        },
      ).catch(nb.report);
    });
    return () => {
      abort.abort();
      cancel();
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
      if (h) open(h).catch(nb.report);
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
        onInput={(v) => (model.session.searchQuery.value = v)}
        onKeyDown={onKey}
      />
      {query.trim() && (
        <p class="pn-panel-note" role="status">
          {failed !== null
            ? `Search failed: ${failed}`
            : !fresh
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
                open(h).catch(nb.report);
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
export function Marked({ hit }: { readonly hit: SearchHit }) {
  return (
    <>
      {hit.preview.slice(0, hit.matchStart)}
      <mark>{hit.preview.slice(hit.matchStart, hit.matchStart + hit.length)}</mark>
      {hit.preview.slice(hit.matchStart + hit.length)}
    </>
  );
}

export function OutlinePanel({ model, nb }: { readonly model: ShellModel; readonly nb: Notebook }) {
  const state = nb.active.value?.state ?? null;
  const version = state?.version.value ?? 0;
  // Read again once typing pauses, not on every keystroke or caret move.
  const settled = useSettled(version, state, settleMs);
  const items = useMemo(() => (state ? outlineOf(state.document, state.model) : []), [state, settled]);
  if (!state) return <p class="pn-panel-note">Open a note to see its headings.</p>;
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
            state.reveal(Math.min(o.offset, state.document.length));
            model.focusEditor();
          }}
          onKeyDown={(e) => {
            if (e.key === 'Enter') {
              state.reveal(Math.min(o.offset, state.document.length));
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

export function RecentPanel({ model, nb }: { readonly model: ShellModel; readonly nb: Notebook }) {
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
          onClick={() => {
            model.openFile(p).catch(model.report);
          }}
          onKeyDown={(e) => {
            if (e.key === 'Enter') model.openFile(p).catch(model.report);
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
export function Resizer({ nb }: { readonly nb: Notebook }) {
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

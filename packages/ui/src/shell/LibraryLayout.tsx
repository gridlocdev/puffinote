import { useSignalEffect } from '@preact/signals';
import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'preact/hooks';
import { folderOf, scopeList } from '../notebook/library.ts';
import type { LibraryScope, Notebook, NoteSort } from '../notebook/notebook.ts';
import { excerptOf, imageFormatOf, relativeDate, type ImageInfo } from '../notebook/noteMeta.ts';
import { fileKind, findNode, type TreeNode } from '../notebook/tree.ts';
import { parentOf, stemOf } from '../platform/paths.ts';
import { Document, EmptyNotebook, StatusBar } from './DocPane.tsx';
import { useFileDrop } from './fileDrop.ts';
import { Icon } from './Icon.tsx';
import { OutlinePanel, RecentPanel } from './panels.tsx';
import { tip } from './RailLayout.tsx';
import type { ShellModel } from './shellModel.ts';

const sortLabels: Record<NoteSort, string> = { edited: 'Last Edited', title: 'Title', created: 'Created' };

/**
 * The Library layout (PRD §6): the library and its folders, a list of the notes in the chosen one with their
 * opening lines, and one note at a time beside it, with its outline and recent notes a click away.
 */
export function LibraryLayout({ model, nb }: { readonly model: ShellModel; readonly nb: Notebook }) {
  const tab = nb.active.value;
  // One note at a time: opening another lets go of the last, unless it still has unsaved edits.
  useSignalEffect(() => {
    const active = nb.active.value;
    if (!active) return;
    for (const t of nb.tabs.peek()) if (t !== active && !t.dirty.peek()) nb.closeTab(t);
  });
  return (
    <>
      {!nb.sidebarCollapsed.value && (
        <>
          <LibraryNav model={model} nb={nb} />
          <NoteList model={model} nb={nb} />
        </>
      )}
      <main class="pn-shell-main">
        <PathBar model={model} nb={nb} />
        <div class="pn-document">
          {tab ? <Document key={tab.id} model={model} nb={nb} tab={tab} /> : <EmptyNotebook model={model} />}
        </div>
        <StatusBar model={model} nb={nb} tab={tab} />
        <Popover model={model} nb={nb} />
      </main>
    </>
  );
}

/** The library folder a drag over `t` is on, by its row's `data-folder`. */
function libraryFolderAt(t: EventTarget | null): string | null {
  if (!(t instanceof Element)) return null;
  return t.closest<HTMLElement>('[data-folder]')?.dataset.folder ?? null;
}

function LibraryNav({ model, nb }: { readonly model: ShellModel; readonly nb: Notebook }) {
  const scope = nb.scope.value;
  // Files from the desktop dropped on a folder go into it (PRD §6.1).
  const fileDrop = useFileDrop(
    model,
    libraryFolderAt,
    (t) => {
      const dir = libraryFolderAt(t);
      return dir && !nb.expanded.peek().has(dir) ? dir : null;
    },
    (path) => nb.toggleFolder(path, true),
  );
  const item = (s: LibraryScope, icon: string, label: string) => (
    <button
      key={s}
      class="pn-lib-item"
      aria-current={scope === s ? 'true' : undefined}
      onClick={() => (nb.scope.value = s)}
    >
      <Icon name={icon} />
      <span class="pn-lib-name">{label}</span>
    </button>
  );
  return (
    <nav class="pn-library" aria-label="Library">
      <h2 class="pn-lib-label">Library</h2>
      {item('all', 'inbox', 'All notes')}
      {item('pinned', 'pin', 'Pinned')}
      {item('recent', 'clock', 'Recent')}
      <h2 class="pn-lib-label pn-lib-folders">Folders</h2>
      <div class="pn-lib-tree" {...fileDrop.handlers}>
        <FolderRow model={model} nb={nb} node={nb.tree.value} depth={0} dropTarget={fileDrop.target} />
      </div>
      <div class="pn-lib-foot">
        <button
          class="pn-icon-button"
          title={tip(model, 'shell.newFolder')}
          aria-label="New Folder"
          onClick={() => void model.newFolder(folderOf(scope))}
        >
          <Icon name="folder-plus" />
        </button>
        <span class="pn-rail-gap" />
        <button
          class="pn-icon-button"
          title={tip(model, 'view.sidebar.settings')}
          aria-label="Settings"
          onClick={() => model.open({ kind: 'settings' })}
        >
          <Icon name="settings-2" />
        </button>
      </div>
    </nav>
  );
}

/** A folder in the library and, when it's open, the folders in it. The notebook's own folder is always open. */
function FolderRow(props: {
  readonly model: ShellModel;
  readonly nb: Notebook;
  readonly node: TreeNode;
  readonly depth: number;
  /** The folder files dragged in from the desktop would land in. */
  readonly dropTarget: string | null;
}) {
  const { model, nb, node, depth, dropTarget } = props;
  const subs = node.children.filter((c) => c.kind === 'dir');
  const root = depth === 0;
  const open = root || nb.expanded.value.has(node.path);
  const current = nb.scope.value === `folder:${node.path}`;
  return (
    <>
      <div
        class={`pn-lib-item pn-lib-folder${dropTarget === node.path ? ' pn-drop' : ''}`}
        data-folder={node.path}
        role="button"
        tabIndex={0}
        aria-current={current ? 'true' : undefined}
        aria-expanded={subs.length > 0 && !root ? open : undefined}
        style={{ paddingLeft: `${10 + Math.max(0, depth - 1) * 14 + (root ? 0 : 18)}px` }}
        title={node.path || node.name}
        onClick={() => (nb.scope.value = `folder:${node.path}`)}
        onKeyDown={(e) => {
          if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault();
            nb.scope.value = `folder:${node.path}`;
          } else if (!root && subs.length > 0 && (e.key === 'ArrowRight' || e.key === 'ArrowLeft')) {
            nb.toggleFolder(node.path, e.key === 'ArrowRight');
          }
        }}
        onContextMenu={(e) => {
          e.preventDefault();
          model.openFileMenu(root ? null : node, node.path, { x: e.clientX, y: e.clientY });
        }}
      >
        {!root && (
          <span
            class="pn-chevron"
            onClick={(e) => {
              e.stopPropagation();
              if (subs.length > 0) nb.toggleFolder(node.path, !open);
            }}
          >
            {subs.length > 0 && <Icon name={open ? 'chevron-down' : 'chevron-right'} />}
          </span>
        )}
        <Icon name={open && subs.length > 0 && !root ? 'folder-open' : 'folder'} />
        <span class="pn-lib-name">{node.name}</span>
      </div>
      {open &&
        subs.map((s) => (
          <FolderRow key={s.path} model={model} nb={nb} node={s} depth={depth + 1} dropTarget={dropTarget} />
        ))}
    </>
  );
}

function scopeTitle(nb: Notebook, scope: LibraryScope): string {
  if (scope === 'all') return 'All notes';
  if (scope === 'pinned') return 'Pinned';
  if (scope === 'recent') return 'Recent';
  const dir = folderOf(scope);
  return dir ? (findNode(nb.tree.value, dir)?.name ?? dir) : nb.name;
}

function plural(n: number, one: string, many = `${one}s`): string {
  return `${n} ${n === 1 ? one : many}`;
}

/** Whether the filter box's text is in `name`, ignoring case. */
function nameMatches(name: string, query: string): boolean {
  return name.toLowerCase().includes(query.toLowerCase());
}

function NoteList({ model, nb }: { readonly model: ShellModel; readonly nb: Notebook }) {
  const scope = nb.scope.value;
  const sort = nb.sort.value;
  const query = model.filterQuery.value.trim();
  const { files, folders } = useMemo(
    () =>
      scopeList(nb.tree.value, scope, {
        pinned: nb.pinned.value,
        recent: nb.recent.value,
        stats: nb.stats.value,
        sort,
      }),
    [nb.tree.value, scope, nb.pinned.value, nb.recent.value, nb.stats.value, sort],
  );
  const textHits = useTextHits(nb, query, files);
  const shown = query ? files.filter((f) => nameMatches(f.name, query) || textHits.has(f.path)) : files;
  const activePath = nb.active.value?.path.value ?? null;
  const list = useRef<HTMLDivElement>(null);
  const box = useRef<HTMLInputElement>(null);
  const tick = model.focusPanelTick.value;
  useLayoutEffect(() => {
    if (tick === 0) return;
    box.current?.focus();
    box.current?.select();
  }, [tick]);
  useLayoutEffect(() => {
    list.current?.querySelector('[aria-selected="true"]')?.scrollIntoView({ block: 'nearest' });
  }, [activePath]);

  const step = (by: number) => {
    if (shown.length === 0) return;
    const i = shown.findIndex((f) => f.path === activePath);
    const next = shown[Math.max(0, Math.min(shown.length - 1, i < 0 ? 0 : i + by))]!;
    // Arrowing through the list shows each note but leaves the keyboard in the list.
    if (fileKind(next.path) !== 'other') void nb.openFile(next.path);
  };

  // Files from the desktop dropped on the list go into the folder it shows, or the top level (PRD §6.1).
  const fileDrop = useFileDrop(model, () => folderOf(scope));

  const subtitle =
    scope.startsWith('folder:') && folders > 0
      ? `${plural(files.length, 'note')} · ${plural(folders, 'folder')}`
      : plural(files.length, 'note');
  return (
    <section
      class={`pn-notes${fileDrop.target !== null ? ' pn-drop' : ''}`}
      aria-label="Notes"
      {...fileDrop.handlers}
    >
      <header class="pn-notes-head">
        <div class="pn-notes-titles">
          <h2 class="pn-notes-title">{scopeTitle(nb, scope)}</h2>
          <p class="pn-notes-sub">{subtitle}</p>
        </div>
        <button
          class="pn-icon-button"
          title={`Sort: ${sortLabels[sort]}`}
          aria-label={`Sort: ${sortLabels[sort]}`}
          aria-haspopup="menu"
          onClick={(e) => {
            const r = (e.currentTarget as HTMLElement).getBoundingClientRect();
            model.open({
              kind: 'menu',
              x: r.left,
              y: r.bottom + 4,
              items: (['edited', 'title', 'created'] as const).map((s) => ({
                label: sortLabels[s],
                checked: s === sort,
                run: () => (nb.sort.value = s),
              })),
            });
          }}
        >
          <Icon name="arrow-up-down" />
        </button>
        <button
          class="pn-icon-button"
          title={tip(model, 'shell.newNote')}
          aria-label="New Note"
          onClick={() => void model.newNote(folderOf(scope))}
        >
          <Icon name="pencil" />
        </button>
      </header>
      <label class="pn-notes-filter">
        <Icon name="search" />
        <input
          ref={box}
          type="search"
          value={model.filterQuery.value}
          placeholder="Filter"
          aria-label="Filter notes by name or text"
          spellcheck={false}
          autocomplete="off"
          onInput={(e) => (model.filterQuery.value = (e.target as HTMLInputElement).value)}
          onKeyDown={(e) => {
            if (e.key === 'Escape' && model.filterQuery.value) {
              e.preventDefault();
              model.filterQuery.value = '';
            } else if (e.key === 'ArrowDown') {
              e.preventDefault();
              list.current?.focus();
              if (!shown.some((f) => f.path === activePath)) step(0);
            } else if (e.key === 'Enter' && shown[0]) {
              e.preventDefault();
              void model.openFile(shown[0].path);
            }
          }}
        />
      </label>
      <div
        ref={list}
        class="pn-notes-list"
        role="listbox"
        tabIndex={0}
        aria-label={scopeTitle(nb, scope)}
        onKeyDown={(e) => {
          if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
            e.preventDefault();
            step(e.key === 'ArrowDown' ? 1 : -1);
          } else if (e.key === 'Enter') {
            e.preventDefault();
            if (activePath) model.focusEditor();
          }
        }}
        onContextMenu={(e) => {
          if ((e.target as HTMLElement).closest('[data-path]')) return;
          e.preventDefault();
          model.openFileMenu(null, folderOf(scope), { x: e.clientX, y: e.clientY });
        }}
      >
        {shown.map((f) => (
          <NoteCard key={f.path} model={model} nb={nb} file={f} active={f.path === activePath} />
        ))}
        {shown.length === 0 && (
          <p class="pn-panel-note">
            {query
              ? 'No matches.'
              : scope === 'pinned'
                ? 'Pin a note from its menu to keep it here.'
                : scope === 'recent'
                  ? 'Notes you open show up here.'
                  : 'No notes here yet.'}
          </p>
        )}
      </div>
    </section>
  );
}

/**
 * The notes in the list whose text has the filter box's words in it, looked up a moment after typing stops.
 * Every note is checked: no cap, so a common word doesn't drop notes from the list (PRD §6).
 */
function useTextHits(nb: Notebook, query: string, files: readonly TreeNode[]): ReadonlySet<string> {
  const [hits, setHits] = useState<ReadonlySet<string>>(new Set());
  const paths = useMemo(() => files.map((f) => f.path), [files]);
  // The same notes in another order (a save re-sorting the list) need no new look.
  const key = [...paths].sort().join('\n');
  useEffect(() => {
    if (!query) {
      setHits(new Set());
      return;
    }
    const abort = new AbortController();
    const t = setTimeout(() => {
      void nb.notesContaining(query, paths, { signal: abort.signal }).then((found) => {
        if (!abort.signal.aborted) setHits(found);
      });
    }, 200);
    return () => {
      abort.abort();
      clearTimeout(t);
    };
  }, [query, key, nb.tree.value]);
  return hits;
}

function NoteCard(props: {
  readonly model: ShellModel;
  readonly nb: Notebook;
  readonly file: TreeNode;
  readonly active: boolean;
}) {
  const { model, nb, file, active } = props;
  const stat = nb.stats.value.get(file.path);
  const sort = nb.sort.value;
  const kind = fileKind(file.path);
  // Reading this file's meta redraws this card alone when it arrives.
  const meta = stat && kind !== 'other' ? nb.meta.get(file.path, stat.modified) : null;
  // A picture served from a URL isn't read for its size: the thumbnail's own load says it.
  const [measured, setMeasured] = useState<ImageInfo | null>(null);
  const image = meta?.image ?? measured;
  const pinned = nb.pinned.value.includes(file.path);
  const showExt = nb.settings.value.showFileExtensions;
  const name = showExt ? file.name : stemOf(file.name) || file.name;
  // An open note's excerpt follows its unsaved text.
  const tab = nb.tabs.value.find((t) => t.path.value === file.path);
  let excerpt = meta?.excerpt ?? '';
  if (tab?.state) {
    void tab.state.version.value;
    excerpt = excerptOf(tab.state.text);
  }
  const when = stat ? relativeDate(sort === 'created' ? stat.created : stat.modified) : '';
  const url = kind === 'image' ? nb.store.fileUrl(file.path) : null;
  return (
    <div
      class={`pn-card-note${kind === 'image' ? ' pn-card-image' : ''}`}
      role="option"
      aria-selected={active}
      data-path={file.path}
      title={file.path}
      onClick={() => void model.openFile(file.path)}
      onContextMenu={(e) => {
        e.preventDefault();
        model.openFileMenu(file, parentOf(file.path), { x: e.clientX, y: e.clientY });
      }}
    >
      <div class="pn-card-text">
        <div class="pn-card-head">
          {pinned && <Icon name="pin" title="Pinned" />}
          <span class="pn-card-name">{name}</span>
          {kind === 'note' && <span class="pn-card-date">{when}</span>}
        </div>
        {kind === 'note' ? (
          <p class="pn-card-excerpt">{excerpt || (meta ? 'Empty note' : ' ')}</p>
        ) : kind === 'image' ? (
          <p class="pn-card-info">
            {image
              ? `${image.format} · ${image.width} × ${image.height}`
              : file.name.slice(file.name.lastIndexOf('.') + 1).toUpperCase()}
          </p>
        ) : (
          <p class="pn-card-info">Plain file · no preview</p>
        )}
      </div>
      {kind === 'image' && (
        <span class="pn-card-thumb">
          {url ? (
            <img
              src={url}
              alt=""
              loading="lazy"
              onLoad={(e) => {
                const img = e.currentTarget;
                if (img.naturalWidth > 0)
                  setMeasured({
                    format: imageFormatOf(file.name),
                    width: img.naturalWidth,
                    height: img.naturalHeight,
                  });
              }}
            />
          ) : (
            <Icon name="image" />
          )}
        </span>
      )}
    </div>
  );
}

/** Where the note is, as links to its folders, and the Outline and History popovers. */
function PathBar({ model, nb }: { readonly model: ShellModel; readonly nb: Notebook }) {
  const tab = nb.active.value;
  const path = tab?.path.value ?? '';
  const parts = path ? path.split('/') : [];
  const popover = model.popover.value;
  const showExt = nb.settings.value.showFileExtensions;
  const crumbs: { label: string; dir: string }[] = [{ label: nb.name, dir: '' }];
  for (let i = 0; i < parts.length - 1; i++)
    crumbs.push({ label: parts[i]!, dir: parts.slice(0, i + 1).join('/') });
  const last = parts.at(-1) ?? '';
  return (
    <header class="pn-pathbar">
      <nav class="pn-crumbs" aria-label="Path">
        {crumbs.map((c) => (
          <span key={c.dir} class="pn-crumb">
            <button onClick={() => (nb.scope.value = `folder:${c.dir}`)}>{c.label}</button>
            {last && <span class="pn-crumb-sep">/</span>}
          </span>
        ))}
        {last && <strong class="pn-crumb-here">{showExt ? last : stemOf(last) || last}</strong>}
      </nav>
      <button
        class="pn-icon-button"
        aria-pressed={popover === 'outline'}
        title={tip(model, 'view.sidebar.outline')}
        aria-label="Outline"
        data-popover="outline"
        onClick={() => model.showPanel('outline')}
      >
        <Icon name="list" />
      </button>
      <button
        class="pn-icon-button"
        aria-pressed={popover === 'recent'}
        title={tip(model, 'view.sidebar.recent')}
        aria-label="History"
        data-popover="recent"
        onClick={() => model.showPanel('recent')}
      >
        <Icon name="history" />
      </button>
    </header>
  );
}

/** The Outline or Recent list, floating under the path bar until a pick, Escape or a click elsewhere. */
function Popover({ model, nb }: { readonly model: ShellModel; readonly nb: Notebook }) {
  const which = model.popover.value;
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!which) return;
    const close = () => (model.popover.value = null);
    const onDown = (e: PointerEvent) => {
      const t = e.target as HTMLElement;
      if (ref.current?.contains(t) || t.closest('[data-popover]')) return;
      close();
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.preventDefault();
        close();
        model.focusEditor();
      }
    };
    window.addEventListener('pointerdown', onDown, true);
    window.addEventListener('keydown', onKey, true);
    // The list takes the keyboard, so arrows and Enter work at once.
    setTimeout(() => ref.current?.querySelector<HTMLElement>('li[aria-selected="true"], li')?.focus());
    return () => {
      window.removeEventListener('pointerdown', onDown, true);
      window.removeEventListener('keydown', onKey, true);
    };
  }, [which]);
  if (!which) return null;
  return (
    <div
      ref={ref}
      class="pn-popover"
      role="dialog"
      aria-label={which === 'outline' ? 'Outline' : 'History'}
      onClick={(e) => {
        if ((e.target as HTMLElement).closest('li')) model.popover.value = null;
      }}
      onKeyDown={(e) => {
        if (e.key === 'Enter' && (e.target as HTMLElement).closest('li')) model.popover.value = null;
      }}
    >
      <h2 class="pn-popover-title">{which === 'outline' ? 'Outline' : 'History'}</h2>
      {which === 'outline' ? <OutlinePanel model={model} nb={nb} /> : <RecentPanel model={model} nb={nb} />}
    </div>
  );
}

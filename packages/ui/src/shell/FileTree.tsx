import { useLayoutEffect, useRef, useState } from 'preact/hooks';
import type { Notebook } from '../notebook/notebook.ts';
import { fileKind, visibleRows, type TreeNode, type TreeRow } from '../notebook/tree.ts';
import { baseName, parentOf } from '../platform/paths.ts';
import { Icon } from './Icon.tsx';
import type { MenuItem, ShellModel } from './shellModel.ts';

const DRAG_START_PX = 4;
const HOVER_OPEN_MS = 1000;
const TYPE_AHEAD_MS = 800;

interface DragState {
  readonly path: string;
  readonly x: number;
  readonly y: number;
  /** Where a drop would land, or null when it would do nothing. */
  readonly target: string | null;
}

/**
 * The notebook's file tree (PRD §6): one focusable `tree` whose rows are moved through with the keyboard
 * (§6.1), a context menu per row, and a drag-to-move the tree draws itself — a ghost, an outline on the
 * folder it would land in, and a hovered closed folder opening after a second.
 */
export function FileTree(props: {
  readonly model: ShellModel;
  readonly nb: Notebook;
  readonly root: TreeNode;
  readonly expanded: ReadonlySet<string>;
  readonly onToggle: (path: string, open: boolean) => void;
  readonly label: string;
}) {
  const { model, nb, root, expanded } = props;
  const rows = visibleRows(root, expanded);
  const selected = nb.selected.value;
  const activePath = nb.active.value?.path.value ?? null;
  const showExt = nb.settings.value.showFileExtensions;
  const selIndex = rows.findIndex((r) => r.node.path === selected);
  const ref = useRef<HTMLDivElement>(null);
  const [drag, setDrag] = useState<DragState | null>(null);
  const typeAhead = useRef({ text: '', at: 0 });
  const suppressClick = useRef(false);

  useLayoutEffect(() => {
    ref.current?.querySelector('.pn-row[aria-selected="true"]')?.scrollIntoView({ block: 'nearest' });
  }, [selected, rows.length]);

  const select = (row: TreeRow | undefined) => {
    if (row) nb.selected.value = row.node.path;
  };

  const activate = (node: TreeNode) => {
    if (node.kind === 'dir') props.onToggle(node.path, !expanded.has(node.path));
    else void model.openFile(node.path);
  };

  // ----- Keyboard (PRD §6.1) -----
  const onKeyDown = (e: KeyboardEvent) => {
    if (e.metaKey || e.ctrlKey) {
      if ((e.key === 'Backspace' || e.key === 'Delete') && selected) {
        e.preventDefault();
        void model.remove(selected);
      }
      return;
    }
    const row = rows[selIndex];
    const go = (i: number) => {
      e.preventDefault();
      select(rows[Math.max(0, Math.min(rows.length - 1, i))]);
    };
    switch (e.key) {
      case 'ArrowDown':
        return go(selIndex < 0 ? 0 : selIndex + 1);
      case 'ArrowUp':
        return go(selIndex < 0 ? rows.length - 1 : selIndex - 1);
      case 'Home':
        return go(0);
      case 'End':
        return go(rows.length - 1);
      case 'PageDown':
        return go(selIndex + 10);
      case 'PageUp':
        return go(selIndex - 10);
      case 'ArrowRight':
        e.preventDefault();
        if (row?.node.kind === 'dir') {
          if (!row.expanded) props.onToggle(row.node.path, true);
          else if (row.node.children.length > 0) select(rows[selIndex + 1]);
        }
        return;
      case 'ArrowLeft': {
        e.preventDefault();
        if (!row) return;
        if (row.expanded) props.onToggle(row.node.path, false);
        else if (parentOf(row.node.path) !== root.path) nb.selected.value = parentOf(row.node.path);
        return;
      }
      case 'Enter':
        e.preventDefault();
        if (row) activate(row.node);
        return;
      case 'F2':
        e.preventDefault();
        if (row) void model.rename(row.node.path);
        return;
      case 'Delete':
        e.preventDefault();
        if (row) void model.remove(row.node.path);
        return;
      case 'ContextMenu':
        e.preventDefault();
        openMenu(row?.node ?? null, null);
        return;
      case 'F10':
        if (e.shiftKey) {
          e.preventDefault();
          openMenu(row?.node ?? null, null);
        }
        return;
    }
    // Type-to-search: jump to the next row whose name starts with what was typed.
    if (e.key.length === 1 && !e.altKey && e.key !== ' ') {
      const now = Date.now();
      const t = typeAhead.current;
      t.text = now - t.at > TYPE_AHEAD_MS ? e.key : t.text + e.key;
      t.at = now;
      const q = t.text.toLowerCase();
      const from = t.text.length === 1 ? selIndex + 1 : Math.max(0, selIndex);
      for (let k = 0; k < rows.length; k++) {
        const r = rows[(from + k) % rows.length]!;
        if (r.node.name.toLowerCase().startsWith(q)) {
          select(r);
          break;
        }
      }
      e.preventDefault();
    }
  };

  // ----- Context menu -----
  function openMenu(node: TreeNode | null, at: { x: number; y: number } | null) {
    const pos = at ?? rowPoint(node);
    const dir = node ? (node.kind === 'dir' ? node.path : parentOf(node.path)) : root.path;
    const store = nb.store;
    const items: (MenuItem | null)[] = [];
    if (node?.kind === 'file') {
      items.push({
        label: fileKind(node.path) === 'other' ? 'Open in Default App' : 'Open',
        run: () => activate(node),
      });
      if (store.openExternal && fileKind(node.path) !== 'other') {
        items.push({ label: 'Open in Default App', run: () => void store.openExternal!(node.path) });
      }
      items.push(null);
    }
    items.push(
      { label: 'New Note…', run: () => void model.newNote(dir) },
      { label: 'New Folder…', run: () => void model.newFolder(dir) },
    );
    if (node) {
      items.push(null, { label: 'Rename…', shortcut: 'F2', run: () => void model.rename(node.path) });
      if (store.reveal) items.push({ label: revealLabel(), run: () => void store.reveal!(node.path) });
      items.push(null, { label: 'Delete…', danger: true, run: () => void model.remove(node.path) });
    }
    model.open({ kind: 'menu', x: pos.x, y: pos.y, items });
  }

  function rowPoint(node: TreeNode | null) {
    const el = node ? ref.current?.querySelector(`[data-path="${CSS.escape(node.path)}"]`) : null;
    const r = (el ?? ref.current)?.getBoundingClientRect();
    return r ? { x: r.left + 24, y: r.bottom } : { x: 0, y: 0 };
  }

  // ----- Drag to move -----
  function onPointerDown(e: PointerEvent, node: TreeNode) {
    if (e.button !== 0) return;
    const startX = e.clientX;
    const startY = e.clientY;
    let started = false;
    let hoverDir: string | null = null;
    let hoverTimer: ReturnType<typeof setTimeout> | null = null;
    const clearHover = () => {
      if (hoverTimer) clearTimeout(hoverTimer);
      hoverTimer = null;
      hoverDir = null;
    };
    const targetAt = (x: number, y: number): { dir: string | null; row: TreeNode | null } => {
      const tree = ref.current;
      const hit = document.elementFromPoint(x, y);
      if (!tree || !hit || !tree.contains(hit)) return { dir: null, row: null };
      const rowEl = hit.closest<HTMLElement>('[data-path]');
      const path = rowEl?.dataset.path;
      const row = path !== undefined ? (rows.find((r) => r.node.path === path)?.node ?? null) : null;
      const dir = row ? (row.kind === 'dir' ? row.path : parentOf(row.path)) : root.path;
      return { dir: nb.canMove(node.path, dir) ? dir : null, row };
    };
    const onMove = (ev: PointerEvent) => {
      if (!started) {
        if (Math.hypot(ev.clientX - startX, ev.clientY - startY) < DRAG_START_PX) return;
        started = true;
        suppressClick.current = true;
      }
      const { dir, row } = targetAt(ev.clientX, ev.clientY);
      setDrag({ path: node.path, x: ev.clientX, y: ev.clientY, target: dir });
      // A closed folder held under the pointer opens, so you can aim inside it.
      const closed = row?.kind === 'dir' && !expandedRef.current.has(row.path) ? row.path : null;
      if (closed !== hoverDir) {
        clearHover();
        hoverDir = closed;
        if (closed) hoverTimer = setTimeout(() => props.onToggle(closed, true), HOVER_OPEN_MS);
      }
    };
    const finish = (drop: boolean, ev?: PointerEvent) => {
      window.removeEventListener('pointermove', onMove);
      window.removeEventListener('pointerup', onUp);
      window.removeEventListener('keydown', onKey, true);
      clearHover();
      setDrag(null);
      if (drop && started && ev) {
        const { dir } = targetAt(ev.clientX, ev.clientY);
        if (dir !== null) void nb.move(node.path, dir);
      }
      setTimeout(() => (suppressClick.current = false));
    };
    const onUp = (ev: PointerEvent) => finish(true, ev);
    const onKey = (ev: KeyboardEvent) => {
      if (ev.key === 'Escape' && started) {
        ev.preventDefault();
        ev.stopPropagation();
        finish(false);
      }
    };
    window.addEventListener('pointermove', onMove);
    window.addEventListener('pointerup', onUp);
    window.addEventListener('keydown', onKey, true);
  }
  // The drag's listeners outlive renders; they read the current open folders through this.
  const expandedRef = useRef(expanded);
  expandedRef.current = expanded;

  const dropTarget = drag?.target ?? null;
  return (
    <div
      ref={ref}
      class={`pn-tree${dropTarget === root.path && drag ? ' pn-drop-root' : ''}${drag ? ' pn-dragging' : ''}`}
      role="tree"
      tabIndex={0}
      aria-label={props.label}
      aria-activedescendant={selIndex >= 0 ? `pn-tree-${selIndex}` : undefined}
      onKeyDown={onKeyDown}
      onFocus={() => {
        if (selIndex < 0 && rows.length > 0) select(rows.find((r) => r.node.path === activePath) ?? rows[0]);
      }}
      onContextMenu={(e) => {
        if ((e.target as HTMLElement).closest('[data-path]')) return;
        e.preventDefault();
        openMenu(null, { x: e.clientX, y: e.clientY });
      }}
    >
      {rows.map((row, i) => {
        const n = row.node;
        const name = n.kind === 'file' && !showExt ? n.name.replace(/\.[^.]+$/, '') || n.name : n.name;
        return (
          <div
            key={n.path}
            id={`pn-tree-${i}`}
            class={`pn-row${n.path === activePath ? ' pn-row-active' : ''}${dropTarget === n.path ? ' pn-drop' : ''}${drag?.path === n.path ? ' pn-row-dragged' : ''}`}
            role="treeitem"
            aria-level={row.depth + 1}
            aria-expanded={n.kind === 'dir' ? row.expanded : undefined}
            aria-selected={n.path === selected}
            aria-label={n.name}
            data-path={n.path}
            title={n.path}
            style={{ paddingLeft: `${8 + row.depth * 14}px` }}
            onPointerDown={(e) => onPointerDown(e, n)}
            onClick={() => {
              if (suppressClick.current) return;
              nb.selected.value = n.path;
              activate(n);
            }}
            onContextMenu={(e) => {
              e.preventDefault();
              nb.selected.value = n.path;
              openMenu(n, { x: e.clientX, y: e.clientY });
            }}
          >
            <span class="pn-chevron">
              {n.kind === 'dir' && <Icon name={row.expanded ? 'chevron-down' : 'chevron-right'} />}
            </span>
            <Icon name={iconFor(n, row.expanded)} />
            <span class="pn-row-name">{name}</span>
          </div>
        );
      })}
      {drag && (
        <div class="pn-drag-ghost" style={{ transform: `translate(${drag.x + 12}px, ${drag.y + 8}px)` }}>
          {baseName(drag.path)}
        </div>
      )}
    </div>
  );
}

function iconFor(n: TreeNode, open: boolean): string {
  if (n.kind === 'dir') return open ? 'folder-open' : 'folder';
  const kind = fileKind(n.path);
  return kind === 'note' ? 'file-text' : kind === 'image' ? 'image' : 'file';
}

function revealLabel(): string {
  const p = typeof navigator !== 'undefined' ? navigator.platform : '';
  return /Mac/.test(p) ? 'Reveal in Finder' : /Win/.test(p) ? 'Show in Explorer' : 'Show in Folder';
}

import { useLayoutEffect, useRef, useState } from 'preact/hooks';
import { isMac } from '../commands/shortcut.ts';
import type { Notebook } from '../notebook/notebook.ts';
import { fileKind, visibleRows, type TreeNode, type TreeRow } from '../notebook/tree.ts';
import { baseName, parentOf } from '../platform/paths.ts';
import { useFileDrop } from './fileDrop.ts';
import { Icon } from './Icon.tsx';
import type { ShellModel } from './shellModel.ts';

const DRAG_START_PX = 4;
const HOVER_OPEN_MS = 1000;
const TYPE_AHEAD_MS = 800;
/** Drag autoscroll (PRD §6.1): the edge band, the hold before it starts, and the top speed in px per frame. */
const SCROLL_EDGE_PX = 28;
const SCROLL_HOLD_MS = 500;
const SCROLL_MAX_PX = 14;

interface DragState {
  readonly path: string;
  readonly x: number;
  readonly y: number;
  /** Where a drop would land, or null when it would do nothing. */
  readonly target: string | null;
}

/**
 * The notebook's file tree (PRD §6): one focusable `tree` whose rows are moved through with the keyboard
 * (§6.2), a context menu per row, and a drag-to-move the tree draws itself — a ghost, an outline on the
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
  const doubleClick = nb.settings.value.treeClick === 'double';
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

  /** A plain click that opens a note or folder (not ⌥-click, and not when rows open with a double click). */
  const opensOnClick = (e: MouseEvent) =>
    !doubleClick && !e.altKey && !e.metaKey && !e.ctrlKey && !e.shiftKey;

  const activate = (node: TreeNode) => {
    if (node.kind === 'dir') props.onToggle(node.path, !expanded.has(node.path));
    else void model.openFile(node.path);
  };

  // ----- Keyboard (PRD §6.2) -----
  const onKeyDown = (e: KeyboardEvent) => {
    const row = rows[selIndex];
    if (isMac && e.metaKey && e.key === 'ArrowDown') {
      e.preventDefault();
      if (row) activate(row.node);
      return;
    }
    if (e.metaKey || e.ctrlKey) {
      if ((e.key === 'Backspace' || e.key === 'Delete') && selected) {
        e.preventDefault();
        void model.remove(selected);
      }
      return;
    }
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
        // Finder renames on Enter; elsewhere Enter opens.
        if (row) {
          if (isMac) void model.rename(row.node.path);
          else activate(row.node);
        }
        return;
      case ' ':
        e.preventDefault();
        if (row) activate(row.node);
        return;
      case 'Escape': {
        // Up one level: the folder that holds the selected row, or none (the notebook) from the top level.
        if (!row) return;
        e.preventDefault();
        e.stopPropagation();
        const up = parentOf(row.node.path);
        nb.selected.value = up === root.path ? null : up;
        return;
      }
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
    const dir = node ? (node.kind === 'dir' ? node.path : parentOf(node.path)) : root.path;
    model.openFileMenu(node, dir, at ?? rowPoint(node));
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
    // Autoscroll near the tree's top or bottom edge, after a short hold (PRD §6.1).
    let edgeSince = 0;
    let scrollFrame = 0;
    let lastPointer: { x: number; y: number } | null = null;
    const edgeSpeed = (y: number): number => {
      const tree = ref.current;
      if (!tree) return 0;
      const r = tree.getBoundingClientRect();
      const band = Math.min(SCROLL_EDGE_PX, r.height / 4);
      if (y < r.top + band) return -SCROLL_MAX_PX * Math.min(1, (r.top + band - y) / band);
      if (y > r.bottom - band) return SCROLL_MAX_PX * Math.min(1, (y - (r.bottom - band)) / band);
      return 0;
    };
    const tick = (now: number) => {
      scrollFrame = 0;
      const tree = ref.current;
      if (!tree || !lastPointer) return;
      const speed = edgeSpeed(lastPointer.y);
      if (speed === 0) {
        edgeSince = 0;
        return;
      }
      if (!edgeSince) edgeSince = now;
      if (now - edgeSince >= SCROLL_HOLD_MS) {
        const before = tree.scrollTop;
        tree.scrollTop += speed;
        if (tree.scrollTop !== before) {
          const { dir } = targetAt(lastPointer.x, lastPointer.y);
          setDrag({ path: node.path, x: lastPointer.x, y: lastPointer.y, target: dir });
        }
      }
      scrollFrame = requestAnimationFrame(tick);
    };
    const stopScroll = () => {
      if (scrollFrame) cancelAnimationFrame(scrollFrame);
      scrollFrame = 0;
      edgeSince = 0;
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
      lastPointer = { x: ev.clientX, y: ev.clientY };
      if (!scrollFrame && edgeSpeed(ev.clientY) !== 0) scrollFrame = requestAnimationFrame(tick);
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
      stopScroll();
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

  // ----- Files dropped from the desktop (PRD §6.1) -----
  const rowAt = (t: EventTarget | null): TreeNode | null => {
    const path = t instanceof Element ? t.closest<HTMLElement>('[data-path]')?.dataset.path : undefined;
    return path === undefined ? null : (rows.find((r) => r.node.path === path)?.node ?? null);
  };
  const fileDrop = useFileDrop(
    model,
    (t) => {
      const row = rowAt(t);
      return row ? (row.kind === 'dir' ? row.path : parentOf(row.path)) : root.path;
    },
    (t) => {
      const row = rowAt(t);
      return row?.kind === 'dir' && !expandedRef.current.has(row.path) ? row.path : null;
    },
    (path) => props.onToggle(path, true),
  );

  const dropTarget = drag ? drag.target : fileDrop.target;
  return (
    <div
      ref={ref}
      class={`pn-tree${dropTarget !== null && dropTarget === root.path ? ' pn-drop-root' : ''}${drag ? ' pn-dragging' : ''}`}
      {...fileDrop.handlers}
      role="tree"
      tabIndex={0}
      aria-label={props.label}
      aria-activedescendant={selIndex >= 0 ? `pn-tree-${selIndex}` : undefined}
      onKeyDown={onKeyDown}
      onClick={(e) => {
        // Empty space: nothing selected, so New Note and New Folder go in the notebook's top level.
        // The path as dispatched: opening a folder can redraw the clicked icon out of the page before this runs.
        const onRow = e.composedPath().some((el) => el instanceof Element && el.hasAttribute('data-path'));
        if (!onRow) nb.selected.value = null;
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
            onMouseDown={(e) => {
              // A click that opens a note sends the keyboard to the editor, and one that opens a folder
              // leaves the selection alone: either way the tree doesn't take the keyboard, so no row flashes
              // the selection's accent.
              if (opensOnClick(e)) e.preventDefault();
            }}
            onClick={(e) => {
              if (suppressClick.current) return;
              if (opensOnClick(e)) {
                // A note is selected once it's open; a folder opens or closes without being selected.
                if (n.kind === 'file') void model.openFile(n.path).then(() => (nb.selected.value = n.path));
                else activate(n);
                return;
              }
              nb.selected.value = n.path;
              // A folder's arrow always opens or closes it.
              if (doubleClick && n.kind === 'dir' && (e.target as Element).closest('.pn-chevron')) {
                activate(n);
                return;
              }
              // ⌥-click (or any click, when rows open with a double click) only selects, so Enter renames
              // it; the tree keeps focus.
              if (e.altKey || doubleClick) ref.current?.focus();
              else activate(n);
            }}
            onDblClick={(e) => {
              if (!doubleClick || e.altKey || suppressClick.current) return;
              if (n.kind === 'dir' && (e.target as Element).closest('.pn-chevron')) return;
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

import type { LibraryScope, NoteSort } from './notebook.ts';
import { allFiles, findNode, type FileStat, type TreeNode } from './tree.ts';

/** The Library layout's note list for a scope, before the filter box narrows it. */
export interface ScopeList {
  readonly files: readonly TreeNode[];
  /** Folders directly in a folder scope (its subtitle counts them); 0 otherwise. */
  readonly folders: number;
}

export interface ScopeInputs {
  readonly pinned: readonly string[];
  readonly recent: readonly string[];
  readonly stats: ReadonlyMap<string, FileStat>;
  readonly sort: NoteSort;
}

/** The folder a scope shows, where New Note and New Folder put things: the top level for All, Pinned, Recent. */
export function folderOf(scope: LibraryScope): string {
  return scope.startsWith('folder:') ? scope.slice('folder:'.length) : '';
}

/**
 * The files a scope lists: a folder's own files, every file, the pinned ones, or the recent ones. Pinned
 * files come first in their pinning order, then the rest by `sort`; Recent keeps the order they were opened.
 */
export function scopeList(root: TreeNode, scope: LibraryScope, inputs: ScopeInputs): ScopeList {
  let files: TreeNode[];
  let folders = 0;
  if (scope === 'all') files = allFiles(root);
  else if (scope === 'pinned') files = inputs.pinned.map((p) => findNode(root, p)).filter(isFile);
  else if (scope === 'recent') {
    return { files: inputs.recent.map((p) => findNode(root, p)).filter(isFile), folders: 0 };
  } else {
    const dir = findNode(root, scope.slice('folder:'.length));
    const kids = dir?.kind === 'dir' ? dir.children : [];
    files = kids.filter((c) => c.kind === 'file');
    folders = kids.length - files.length;
  }
  const order = sortNotes(files, inputs.sort, inputs.stats);
  const pinned = inputs.pinned.filter((p) => order.some((f) => f.path === p));
  if (pinned.length === 0) return { files: order, folders };
  const first = pinned.map((p) => order.find((f) => f.path === p)!);
  return { files: [...first, ...order.filter((f) => !pinned.includes(f.path))], folders };
}

/** Sort notes newest edit first, newest created first, or by name (numbers in names counted as numbers). */
export function sortNotes(
  files: readonly TreeNode[],
  sort: NoteSort,
  stats: ReadonlyMap<string, FileStat>,
): TreeNode[] {
  const byName = (a: TreeNode, b: TreeNode) => collator.compare(a.name, b.name);
  const time = (f: TreeNode) => {
    const s = stats.get(f.path);
    return (sort === 'created' ? s?.created || s?.modified : s?.modified) ?? 0;
  };
  if (sort === 'title') return [...files].sort(byName);
  return [...files].sort((a, b) => time(b) - time(a) || byName(a, b));
}

const collator = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' });

function isFile(n: TreeNode | null): n is TreeNode {
  return n?.kind === 'file';
}

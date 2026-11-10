import { isMarkdownFile } from '@puffinote/markdown';
import type { Entry, NotebookStore } from '../platform/notebookStore.ts';
import { isImagePath, isWithin, parentOf } from '../platform/paths.ts';

/** A file or folder in the notebook. A folder's children are sorted: folders first, then by name. */
export interface TreeNode {
  readonly path: string;
  readonly name: string;
  readonly kind: 'file' | 'dir';
  readonly children: readonly TreeNode[];
}

/** The notebook's own state folder: never shown in the tree. */
export const stateDir = '.puffinote';

export const emptyTree: TreeNode = { path: '', name: '', kind: 'dir', children: [] };

/** How a file opens: in the editor, as a picture, or in another app (PRD §6). */
export type FileKind = 'note' | 'image' | 'other';

export function fileKind(path: string): FileKind {
  if (isMarkdownFile(path)) return 'note';
  if (isImagePath(path)) return 'image';
  return 'other';
}

/**
 * The file name New Note creates for what the user typed (PRD §6): the name as typed when it already ends in
 * a note extension (PRD §5), otherwise with `.md` added. A dot elsewhere ("Meeting 2024.11", "v1.2") is part
 * of the name.
 */
export function noteFileName(name: string): string {
  return isMarkdownFile(name) ? name : `${name}.md`;
}

const collator = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' });

export function compareEntries(a: { kind: string; name: string }, b: { kind: string; name: string }): number {
  if (a.kind !== b.kind) return a.kind === 'dir' ? -1 : 1;
  return collator.compare(a.name, b.name);
}

/** Read the whole notebook. `.puffinote` and `.git` are never listed; other dot-names only when `hidden`. */
/** A file's size and times, as the store listed them (0 when it can't tell). */
export interface FileStat {
  readonly size: number;
  readonly modified: number;
  readonly created: number;
}

/** Read the whole tree. `stats`, when given, collects each file's size and times by path. */
export async function loadTree(
  store: NotebookStore,
  hidden: boolean,
  stats?: Map<string, FileStat>,
): Promise<TreeNode> {
  const walk = async (dir: string, name: string): Promise<TreeNode> => {
    const entries = (await store.list(dir)).filter((e) => shown(e, hidden)).sort(compareEntries);
    if (stats) {
      for (const e of entries) {
        if (e.kind === 'file') {
          stats.set(e.path, { size: e.size, modified: e.modified, created: e.created || e.modified });
        }
      }
    }
    const children = await Promise.all(
      entries.map((e) =>
        e.kind === 'dir'
          ? walk(e.path, e.name)
          : { path: e.path, name: e.name, kind: 'file' as const, children: [] },
      ),
    );
    return { path: dir, name, kind: 'dir', children };
  };
  return walk('', store.name);
}

function shown(e: Entry, hidden: boolean): boolean {
  if (e.name === stateDir || e.name === '.git') return false;
  return hidden || !e.name.startsWith('.');
}

export function findNode(root: TreeNode, path: string): TreeNode | null {
  if (path === root.path) return root;
  for (const c of root.children) {
    if (isWithin(path, c.path)) return findNode(c, path);
  }
  return null;
}

/** Every file in the tree, depth first. */
export function allFiles(root: TreeNode): TreeNode[] {
  const out: TreeNode[] = [];
  const walk = (n: TreeNode) => {
    for (const c of n.children) {
      if (c.kind === 'file') out.push(c);
      else walk(c);
    }
  };
  walk(root);
  return out;
}

export function hasFolders(root: TreeNode): boolean {
  return root.children.some((c) => c.kind === 'dir');
}

export function allFolders(root: TreeNode): string[] {
  const out: string[] = [];
  const walk = (n: TreeNode) => {
    for (const c of n.children) {
      if (c.kind === 'dir') {
        out.push(c.path);
        walk(c);
      }
    }
  };
  walk(root);
  return out;
}

/** One visible row of the tree: what the tree view draws and the keyboard moves through. */
export interface TreeRow {
  readonly node: TreeNode;
  readonly depth: number;
  readonly expanded: boolean;
}

/** The rows the tree shows with `expanded` folders open. */
export function visibleRows(root: TreeNode, expanded: ReadonlySet<string>): TreeRow[] {
  const rows: TreeRow[] = [];
  const walk = (n: TreeNode, depth: number) => {
    for (const c of n.children) {
      const open = c.kind === 'dir' && expanded.has(c.path);
      rows.push({ node: c, depth, expanded: open });
      if (open) walk(c, depth + 1);
    }
  };
  walk(root, 0);
  return rows;
}

/**
 * The Filter panel's tree (PRD §6.3): entries whose whole name (extension included) contains `query`,
 * case-insensitively, plus the folders above them and everything inside a matching folder. `opened` gets the
 * folders that lead to a match, so the match is a row you can see.
 */
export function filterTree(
  root: TreeNode,
  query: string,
): { tree: TreeNode; matches: number; opened: Set<string> } {
  const q = query.trim().toLowerCase();
  const opened = new Set<string>();
  let matches = 0;
  if (!q) return { tree: root, matches: 0, opened };
  const count = (n: TreeNode): void => {
    for (const c of n.children) {
      if (c.name.toLowerCase().includes(q)) matches++;
      count(c);
    }
  };
  const walk = (n: TreeNode): TreeNode | null => {
    if (n.path !== '' && n.name.toLowerCase().includes(q)) {
      matches++;
      if (n.kind === 'dir') count(n);
      return n;
    }
    if (n.kind === 'file') return null;
    const kept = n.children.map(walk).filter((c): c is TreeNode => c !== null);
    if (kept.length === 0 && n.path !== '') return null;
    if (n.path !== '') opened.add(n.path);
    return { ...n, children: kept };
  };
  const tree = walk(root) ?? { ...root, children: [] };
  for (const p of [...opened]) if (p === '') opened.delete(p);
  return { tree, matches, opened };
}

/** The folders that hold `path`, outermost first (to reveal it). */
export function ancestorsOf(path: string): string[] {
  const out: string[] = [];
  for (let p = parentOf(path); p !== ''; p = parentOf(p)) out.unshift(p);
  return out;
}

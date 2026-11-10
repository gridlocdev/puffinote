/**
 * Notebook paths: relative to the notebook root, `/`-separated, no leading or trailing slash. The root itself
 * is `''`. Every store speaks these, whatever its own addressing is.
 */

export function joinPath(dir: string, name: string): string {
  return dir ? `${dir}/${name}` : name;
}

export function parentOf(path: string): string {
  const i = path.lastIndexOf('/');
  return i < 0 ? '' : path.slice(0, i);
}

export function baseName(path: string): string {
  return path.slice(path.lastIndexOf('/') + 1);
}

/** The extension with its dot, lower-cased (`.md`), or '' for none. A leading dot (`.gitignore`) isn't one. */
export function extOf(path: string): string {
  const name = baseName(path);
  const i = name.lastIndexOf('.');
  return i <= 0 ? '' : name.slice(i).toLowerCase();
}

/** The name without its extension. */
export function stemOf(path: string): string {
  const name = baseName(path);
  const ext = extOf(name);
  return ext ? name.slice(0, -ext.length) : name;
}

/** True when `path` is `ancestor` or lies inside it. */
export function isWithin(path: string, ancestor: string): boolean {
  return ancestor === '' || path === ancestor || path.startsWith(ancestor + '/');
}

/** Re-root `path` from under `from` to under `to` (after a folder rename or move). */
export function rebase(path: string, from: string, to: string): string {
  return path === from ? to : joinPath(to, path.slice(from.length + 1));
}

/** Why `name` can't be a file or folder name, or null when it can. */
export function invalidNameReason(name: string): string | null {
  if (name.trim().length === 0) return 'Enter a name.';
  if (name === '.' || name === '..') return 'That name is reserved.';
  if (/[/\\:*?"<>|]/.test(name)) return 'Names can’t contain / \\ : * ? " < > |';
  if (name !== name.trim()) return 'Names can’t start or end with a space.';
  return null;
}

/** Resolve a link or image reference written in the note at `from` to a notebook path, or null if outside. */
export function resolveRelative(from: string, ref: string): string | null {
  const clean = decodeURIComponentSafe(ref.split(/[?#]/)[0] ?? '');
  const parts = clean.startsWith('/') ? [] : parentOf(from).split('/').filter(Boolean);
  for (const seg of clean.split('/')) {
    if (seg === '' || seg === '.') continue;
    if (seg === '..') {
      if (parts.length === 0) return null;
      parts.pop();
    } else parts.push(seg);
  }
  return parts.join('/');
}

function decodeURIComponentSafe(s: string): string {
  try {
    return decodeURIComponent(s);
  } catch {
    return s;
  }
}

/** `name` if no sibling has it, else `stem 2.ext`, `stem 3.ext`, … */
export function uniqueName(name: string, taken: ReadonlySet<string>): string {
  if (!taken.has(name)) return name;
  const ext = extOf(name);
  const stem = ext ? name.slice(0, -ext.length) : name;
  for (let n = 2; ; n++) {
    const candidate = `${stem} ${n}${ext}`;
    if (!taken.has(candidate)) return candidate;
  }
}

const imageExts = new Set(['.png', '.jpg', '.jpeg', '.gif', '.webp', '.svg', '.avif', '.bmp', '.ico']);

export function isImagePath(path: string): boolean {
  return imageExts.has(extOf(path));
}

export function mimeOf(path: string): string {
  const ext = extOf(path);
  const known: Record<string, string> = {
    '.md': 'text/markdown',
    '.markdown': 'text/markdown',
    '.txt': 'text/plain',
    '.png': 'image/png',
    '.jpg': 'image/jpeg',
    '.jpeg': 'image/jpeg',
    '.gif': 'image/gif',
    '.webp': 'image/webp',
    '.svg': 'image/svg+xml',
    '.avif': 'image/avif',
    '.bmp': 'image/bmp',
    '.ico': 'image/x-icon',
    '.pdf': 'application/pdf',
    '.json': 'application/json',
  };
  return known[ext] ?? 'application/octet-stream';
}

import { readlink, realpath } from 'node:fs/promises';
import path from 'node:path';

/** Is `child` the folder `parent` or inside it? Both are compared as given, without following links. */
export function isInside(parent: string, child: string): boolean {
  const rel = path.relative(parent, child);
  return rel === '' || (!rel.startsWith('..') && !path.isAbsolute(rel));
}

/** The file a path names after following symlinks; a path that doesn't exist yet is its own target. */
export async function realTarget(p: string): Promise<string> {
  const abs = path.resolve(p);
  const real = await realpath(abs).catch(() => null);
  if (real !== null) return real;
  const link = await readlink(abs).catch(() => null); // dangling: write where it points
  return link === null ? abs : path.resolve(path.dirname(abs), link);
}

/**
 * A path with its nearest existing folder's symlinks resolved, so a file that doesn't exist yet still gets one
 * spelling: `/link/new.md` becomes `/real/new.md`.
 */
export async function realOfNearest(p: string): Promise<string> {
  let probe = path.resolve(p);
  const tail: string[] = [];
  for (;;) {
    const real = await realpath(probe).catch(() => null);
    if (real !== null) return path.join(real, ...tail);
    const parent = path.dirname(probe);
    if (parent === probe) return path.resolve(p);
    tail.unshift(path.basename(probe));
    probe = parent;
  }
}

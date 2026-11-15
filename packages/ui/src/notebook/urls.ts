/** A URL scheme (`https:`, `mailto:`, `data:`), as RFC 3986 spells one. */
const SCHEME = /^[a-z][a-z0-9+.-]*:/i;

/** Whether `url` starts with a scheme, so it names no file in the notebook. */
export function hasScheme(url: string): boolean {
  return SCHEME.test(url);
}

/** A scheme or a `//host` (network-path) reference: a link that leaves the notebook, never a file in it. */
export function leavesNotebook(url: string): boolean {
  return hasScheme(url) || url.startsWith('//');
}

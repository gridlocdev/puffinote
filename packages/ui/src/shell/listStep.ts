/**
 * Where a key moves the highlight in a popup list of `count` items (⌘K, Go to File, Move To): ↓ and Tab go
 * down, ↑ and Shift+Tab go up, stopping at the ends. `undefined` for any other key.
 */
export function listStep(
  e: Pick<KeyboardEvent, 'key' | 'shiftKey' | 'ctrlKey' | 'metaKey' | 'altKey'>,
  index: number,
  count: number,
): number | undefined {
  const tab = e.key === 'Tab' && !e.ctrlKey && !e.metaKey && !e.altKey;
  if (e.key === 'ArrowDown' || (tab && !e.shiftKey)) return Math.max(0, Math.min(count - 1, index + 1));
  if (e.key === 'ArrowUp' || (tab && e.shiftKey)) return Math.max(0, index - 1);
  return undefined;
}

import { contentLength } from './chars.ts';

/** A `|` that splits table cells: one not escaped by a backslash. */
export function isUnescapedPipe(line: string, k: number): boolean {
  return line.charAt(k) === '|' && (k === 0 || line.charAt(k - 1) !== '\\');
}

/**
 * Whether `line` can be a table row: it has an unescaped `|`. The model starts a table at such a line with a
 * delimiter row under it and keeps every such line after it in the table; the block list, and so the editor
 * and the export, end the table where the model does.
 */
export function isTableRowText(line: string): boolean {
  const len = contentLength(line);
  for (let k = 0; k < len; k++) {
    if (isUnescapedPipe(line, k)) return true;
  }
  return false;
}

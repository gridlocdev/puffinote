/**
 * The file names Puffinote opens as notes. One list, read by everything that must agree: the file tree
 * (which files open in the editor and which open externally), the desktop Open dialog's filter, and the
 * `puffinote <file>` launcher. `.txt` is here because a plain-text file is valid Markdown.
 */
export const markdownExtensions: readonly string[] = ['.md', '.markdown', '.mdown', '.mkd', '.txt'];

/** The same list as glob patterns, for a file picker's filter. */
export const markdownPatterns: readonly string[] = markdownExtensions.map((e) => `*${e}`);

/** The extensions as a person reads them, for the message when an argument isn't one. */
export const markdownExtensionsSummary = markdownExtensions.join(', ');

/** The dotted, lowercased extension of the last segment of `path`, or '' when it has none. */
export function extensionOf(path: string): string {
  const name = path.slice(Math.max(path.lastIndexOf('/'), path.lastIndexOf('\\')) + 1);
  const dot = name.lastIndexOf('.');
  return dot < 0 ? '' : name.slice(dot).toLowerCase();
}

/** Whether `path` names a note. Case-insensitive, because the filesystems this ships on are. */
export function isMarkdownFile(path: string): boolean {
  return markdownExtensions.includes(extensionOf(path));
}

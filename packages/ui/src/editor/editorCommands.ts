import { parseLinePrefix, taskStateOrder } from '@puffinote/markdown';
import type { CommandSpec } from '../commands/command.ts';
import { isMac } from '../commands/shortcut.ts';
import {
  codeBlockEdit,
  deleteLinesEdit,
  duplicateLinesEdit,
  headingEdit,
  linkEdit,
  listEdit,
  moveLinesEdit,
  quoteEdit,
  removeTaskEdit,
  ruleEdit,
  setTaskStateEdit,
  toggleInlineEdit,
  toggleTaskEdit,
  withTaskSort,
  type Edit,
} from './commands.ts';
import type { EditorState } from './editorState.ts';
import type { ViewMode } from './reveal.ts';
import type { Selection, TextDocument } from '@puffinote/text';

/** What an editor command acts on: the editor in front of the user, if any. */
export interface EditorCommandTarget {
  readonly state: () => EditorState | null;
  /** Put the keyboard back in the editor after a command run from elsewhere (the Command Menu). */
  readonly focus: () => void;
  /** The editor's text input, which takes the clipboard events (Cut, Copy and Paste from a menu). */
  readonly input?: () => HTMLElement | null;
  /** Whether ticking a task done moves it below the open ones in its list (Settings, PRD §9). */
  readonly sortTasks?: () => boolean;
}

/**
 * The Markdown editor's one command catalog (PRD §10.2): the editor's own key handling and the shell's merged
 * Command Menu both read it, so a binding and its label can't disagree. Ids are namespaced (`edit.`,
 * `format.`, `insert.`, `view.`) apart from the shell's `shell.` commands.
 */
export function markdownCommandSpecs(target: EditorCommandTarget): CommandSpec[] {
  const has = () => target.state() !== null;
  const sorted = (doc: TextDocument, e: Edit | null) => (target.sortTasks?.() ? withTaskSort(doc, e) : e);
  const edit = (fn: (doc: TextDocument, sel: Selection) => Edit | null) => (): void => {
    const s = target.state();
    if (!s) return;
    s.apply(fn(s.document, s.selection.value));
    target.focus();
  };
  const mode = (m: ViewMode) => (): void => {
    const s = target.state();
    if (!s) return;
    s.mode.value = m;
    target.focus();
  };
  const caretHeading = (): number => {
    const s = target.state();
    if (!s) return -1;
    const line = s.document.getLineText(s.document.getPosition(s.selection.value.caret).line);
    const m = /^(#{1,6})(?:[ \t]|$)/.exec(line);
    return m ? m[1]!.length : 0;
  };

  const hasSelection = (): boolean => {
    const sel = target.state()?.selection.value;
    return !!sel && sel.anchor !== sel.caret;
  };
  const input = (): HTMLElement | null => {
    const el = target.input?.() ?? null;
    el?.focus({ preventScroll: true });
    return el;
  };
  const clipboard = (verb: 'cut' | 'copy'): void => {
    // The editor's own copy and cut handlers write the Markdown of the selection.
    if (input()) document.execCommand(verb);
  };
  async function paste(): Promise<void> {
    const text = await navigator.clipboard.readText().catch(() => '');
    const el = input();
    if (!el || !text) return;
    // Hand it to the editor's paste handler, which tidies line endings and doubled task markers.
    const data = new DataTransfer();
    data.setData('text/plain', text);
    el.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }));
  }

  const specs: CommandSpec[] = [
    {
      id: 'edit.undo',
      title: 'Undo',
      description: 'Step backward through your changes.',
      group: 'Edit',
      shortcut: 'mod+z',
      icon: 'undo',
      enabled: has,
      run: () => target.state()?.undo(),
    },
    {
      id: 'edit.redo',
      title: 'Redo',
      description: 'Re-apply a change you undid.',
      group: 'Edit',
      shortcut: 'mod+shift+z',
      alternates: isMac ? [] : ['mod+y'],
      icon: 'redo',
      enabled: has,
      run: () => target.state()?.redo(),
    },
    // Cut, Copy and Paste for menus (PRD §10.1). They carry no shortcut here: ⌘X, ⌘C and ⌘V stay the
    // platform's own, so the editor's clipboard handlers see real events.
    {
      id: 'edit.cut',
      title: 'Cut',
      description: 'Move the selection to the clipboard.',
      group: 'Edit',
      keywords: 'clipboard',
      enabled: () => hasSelection(),
      run: () => clipboard('cut'),
    },
    {
      id: 'edit.copy',
      title: 'Copy',
      description: 'Copy the selection to the clipboard.',
      group: 'Edit',
      keywords: 'clipboard',
      enabled: () => hasSelection(),
      run: () => clipboard('copy'),
    },
    {
      id: 'edit.paste',
      title: 'Paste',
      description: 'Insert the text on the clipboard.',
      group: 'Edit',
      keywords: 'clipboard',
      enabled: has,
      run: paste,
    },
    {
      id: 'edit.selectAll',
      title: 'Select All',
      description: 'Select the whole document.',
      group: 'Edit',
      shortcut: 'mod+a',
      enabled: has,
      run: () => {
        const s = target.state();
        s?.select(0, s.document.length);
        target.focus();
      },
    },
    {
      id: 'edit.moveLineUp',
      title: 'Move Line Up',
      description: 'Move the current line or selected lines up.',
      group: 'Edit',
      shortcut: 'alt+up',
      enabled: has,
      run: edit((d, s) => moveLinesEdit(d, s, -1)),
    },
    {
      id: 'edit.moveLineDown',
      title: 'Move Line Down',
      description: 'Move the current line or selected lines down.',
      group: 'Edit',
      shortcut: 'alt+down',
      enabled: has,
      run: edit((d, s) => moveLinesEdit(d, s, 1)),
    },
    {
      id: 'edit.duplicateLine',
      title: 'Duplicate Line',
      description: 'Duplicate the current line or selected lines.',
      group: 'Edit',
      shortcut: 'mod+d',
      icon: 'copy',
      enabled: has,
      run: edit(duplicateLinesEdit),
    },
    {
      id: 'edit.deleteLine',
      title: 'Delete Line',
      description: 'Delete the current line or selected lines.',
      group: 'Edit',
      shortcut: 'mod+shift+k',
      icon: 'trash-2',
      enabled: has,
      run: edit(deleteLinesEdit),
    },
    {
      id: 'format.bold',
      title: 'Bold',
      description: 'Make the selection bold.',
      group: 'Format',
      shortcut: 'mod+b',
      icon: 'bold',
      enabled: has,
      run: edit((d, s) => toggleInlineEdit(d, s, '**')),
    },
    {
      id: 'format.italic',
      title: 'Italic',
      description: 'Make the selection italic.',
      group: 'Format',
      shortcut: 'mod+i',
      icon: 'italic',
      enabled: has,
      run: edit((d, s) => toggleInlineEdit(d, s, '*')),
    },
    {
      id: 'format.code',
      title: 'Inline Code',
      description: 'Format the selection as inline code.',
      group: 'Format',
      shortcut: 'mod+e',
      icon: 'code',
      enabled: has,
      run: edit((d, s) => toggleInlineEdit(d, s, '`')),
    },
    {
      id: 'format.strike',
      title: 'Strikethrough',
      description: 'Strike through the selection.',
      group: 'Format',
      shortcut: 'mod+shift+x',
      icon: 'strikethrough',
      enabled: has,
      run: edit((d, s) => toggleInlineEdit(d, s, '~~')),
    },
    {
      id: 'format.highlight',
      title: 'Highlight',
      description: 'Highlight the selection (==text==).',
      group: 'Format',
      shortcut: 'mod+shift+h',
      icon: 'highlighter',
      enabled: has,
      run: edit((d, s) => toggleInlineEdit(d, s, '==')),
    },
    {
      id: 'format.task',
      title: 'Checkbox',
      description: 'Add a task checkbox to this line, or toggle it done.',
      group: 'Format',
      shortcut: 'mod+l',
      icon: 'square-check',
      enabled: has,
      run: edit((doc, sel) => sorted(doc, toggleTaskEdit(doc, sel))),
    },
    {
      id: 'format.taskStatus',
      title: 'Next Task Status',
      description: 'Step this task through not started, in progress, and done.',
      group: 'Format',
      shortcut: 'mod+alt+l',
      keywords: 'in progress started',
      enabled: has,
      run: edit((doc, sel) => {
        const line = doc.getPosition(sel.caret).line;
        const p = parseLinePrefix(doc.getLineText(line));
        if (!p.hasTask) return null;
        const next = taskStateOrder[(taskStateOrder.indexOf(p.task) + 1) % taskStateOrder.length]!;
        return sorted(doc, setTaskStateEdit(doc, sel, line, next));
      }),
    },
    {
      id: 'format.removeTask',
      title: 'Remove Checkbox',
      description: 'Remove the task checkbox from this line.',
      group: 'Format',
      shortcut: 'mod+shift+l',
      enabled: has,
      run: edit(removeTaskEdit),
    },
    {
      id: 'format.quote',
      title: 'Blockquote',
      description: 'Toggle a blockquote on the line or lines.',
      group: 'Format',
      icon: 'text-quote',
      enabled: has,
      run: edit(quoteEdit),
    },
    {
      id: 'format.bulletList',
      title: 'Bulleted List',
      description: 'Toggle a bulleted list on the line or lines.',
      group: 'Format',
      shortcut: 'mod+.',
      icon: 'list',
      enabled: has,
      run: edit((d, s) => listEdit(d, s, 'unordered')),
    },
    {
      id: 'format.numberedList',
      title: 'Numbered List',
      description: 'Toggle a numbered list on the line or lines.',
      group: 'Format',
      shortcut: 'mod+/',
      icon: 'list-ordered',
      enabled: has,
      run: edit((d, s) => listEdit(d, s, 'ordered')),
    },
  ];
  for (let level = 1; level <= 6; level++) {
    specs.push({
      id: `format.h${level}`,
      title: `Heading ${level}`,
      description: `Make this line a level-${level} heading.`,
      group: 'Format',
      shortcut: `mod+${level}`,
      icon: `heading-${level}`,
      enabled: has,
      active: () => caretHeading() === level,
      run: edit((d, s) => headingEdit(d, s, level)),
    });
  }
  specs.push(
    {
      id: 'format.paragraph',
      title: 'Paragraph',
      description: 'Remove the heading from this line.',
      group: 'Format',
      shortcut: 'mod+0',
      icon: 'remove-formatting',
      enabled: has,
      run: edit((d, s) => headingEdit(d, s, 0)),
    },
    {
      id: 'format.link',
      title: 'Insert Link',
      description: 'Make the selection a link, or insert an empty one.',
      group: 'Format',
      shortcut: 'mod+alt+k',
      icon: 'link',
      enabled: has,
      run: edit(linkEdit),
    },
    {
      id: 'insert.codeBlock',
      title: 'Code Block',
      description: 'Insert a fenced code block, or fence the selected lines.',
      group: 'Insert',
      shortcut: 'mod+shift+e',
      icon: 'square-code',
      enabled: has,
      run: edit(codeBlockEdit),
    },
    {
      id: 'insert.hr',
      title: 'Horizontal Rule',
      description: 'Insert a horizontal rule.',
      group: 'Insert',
      icon: 'minus',
      enabled: has,
      run: edit(ruleEdit),
    },
    {
      id: 'view.viewRich',
      title: 'View Rich View',
      description: 'Live preview: formatted, with Markdown markers hidden.',
      group: 'View',
      icon: 'eye',
      enabled: has,
      active: () => target.state()?.mode.value === 'live',
      run: mode('live'),
    },
    {
      id: 'view.viewFormatted',
      title: 'View Formatted View',
      description: 'Formatted, with the raw Markdown markers revealed.',
      group: 'View',
      icon: 'markdown',
      enabled: has,
      active: () => target.state()?.mode.value === 'source',
      run: mode('source'),
    },
    {
      id: 'view.viewRaw',
      title: 'View Raw Text',
      description: 'Plain-text source: no rich formatting or decorations.',
      group: 'View',
      icon: 'file-text',
      enabled: has,
      active: () => target.state()?.mode.value === 'raw',
      run: mode('raw'),
    },
  );
  return specs;
}

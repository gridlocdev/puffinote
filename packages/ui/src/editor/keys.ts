import { deleteBackRun, deleteForwardRun, type UndoRun } from '@puffinote/text';

/** The parts of a keyboard event the editor's own keys read. */
export interface KeyLike {
  readonly key: string;
  readonly metaKey: boolean;
  readonly ctrlKey: boolean;
  readonly altKey: boolean;
  readonly shiftKey: boolean;
}

/** What a key the catalog doesn't bind does in the editor: caret motion, deletion, Enter and Tab. */
export type KeyAction =
  | { readonly kind: 'move'; readonly unit: 'char' | 'word' | 'row' | 'line' | 'doc'; readonly dir: 1 | -1 }
  | { readonly kind: 'vertical'; readonly dir: 1 | -1 }
  | { readonly kind: 'page'; readonly dir: 1 | -1 }
  | { readonly kind: 'deleteBack'; readonly unit: 'char' | 'word' | 'line' }
  | { readonly kind: 'deleteForward'; readonly unit: 'char' | 'word' | 'line' }
  | { readonly kind: 'enter'; readonly soft: boolean }
  | { readonly kind: 'tab'; readonly outdent: boolean };

/**
 * The macOS Emacs keys (Control plus a letter), which every Cocoa text field takes: ⌃A and ⌃E go to the
 * line's start and end, ⌃F, ⌃B, ⌃N and ⌃P move by character and row, ⌃D and ⌃H delete forward and back, and
 * ⌃K deletes to the end of the line.
 */
const emacsKeys: Readonly<Record<string, KeyAction>> = {
  a: { kind: 'move', unit: 'line', dir: -1 },
  e: { kind: 'move', unit: 'line', dir: 1 },
  f: { kind: 'move', unit: 'char', dir: 1 },
  b: { kind: 'move', unit: 'char', dir: -1 },
  n: { kind: 'vertical', dir: 1 },
  p: { kind: 'vertical', dir: -1 },
  d: { kind: 'deleteForward', unit: 'char' },
  h: { kind: 'deleteBack', unit: 'char' },
  k: { kind: 'deleteForward', unit: 'line' },
};

/**
 * Map a key to its editing action, by platform convention. On macOS ⌘ works by row or document (⌘←, ⌘↑,
 * ⌘⌫), ⌥ by word (⌥←, ⌥⌫), and Control takes the Emacs keys (`emacsKeys`). Elsewhere Ctrl works by word
 * (Ctrl+←, Ctrl+Backspace) and Ctrl+Home/End by document; there is no delete-to-line-start key. Home, End
 * and ⌘←/→ go by visual row; Page Up and Page Down by a screen. Shift extends a motion; the caller reads
 * it. Null means the editor leaves the key alone.
 */
export function keyAction(e: KeyLike, mac: boolean): KeyAction | null {
  const mod = mac ? e.metaKey : e.ctrlKey;
  const word = mac ? e.altKey : e.ctrlKey;
  if (mac && e.ctrlKey && !e.metaKey && !e.altKey && e.key.length === 1) {
    return emacsKeys[e.key.toLowerCase()] ?? null;
  }
  switch (e.key) {
    case 'ArrowLeft':
    case 'ArrowRight': {
      const dir = e.key === 'ArrowRight' ? 1 : -1;
      if (mac && e.metaKey) return { kind: 'move', unit: 'row', dir };
      return { kind: 'move', unit: word ? 'word' : 'char', dir };
    }
    case 'ArrowUp':
    case 'ArrowDown': {
      const dir = e.key === 'ArrowDown' ? 1 : -1;
      return mod ? { kind: 'move', unit: 'doc', dir } : { kind: 'vertical', dir };
    }
    case 'Home':
    case 'End': {
      const dir = e.key === 'End' ? 1 : -1;
      return { kind: 'move', unit: !mac && e.ctrlKey ? 'doc' : 'row', dir };
    }
    case 'PageUp':
    case 'PageDown':
      return { kind: 'page', dir: e.key === 'PageDown' ? 1 : -1 };
    case 'Backspace':
      if (mac && e.metaKey) return { kind: 'deleteBack', unit: 'line' };
      return { kind: 'deleteBack', unit: word ? 'word' : 'char' };
    case 'Delete':
      return { kind: 'deleteForward', unit: word ? 'word' : 'char' };
    case 'Enter':
      return mod ? null : { kind: 'enter', soft: e.shiftKey };
    case 'Tab':
      // ⌃Tab is Next Tab on macOS too, so any modifier but Shift leaves Tab to the catalog.
      return mod || e.ctrlKey || e.altKey ? null : { kind: 'tab', outdent: e.shiftKey };
    default:
      return null;
  }
}

/**
 * How the edit an editor key makes joins the undo history (PRD §10.1): a run of Backspace, or of Delete, is
 * one step however it is pressed; any other key held down (auto-repeat, `repeat`) is one step until it is
 * released. Undefined for keys that make no edit.
 */
export function undoRunFor(action: KeyAction, key: string, repeat: boolean): UndoRun | undefined {
  switch (action.kind) {
    case 'deleteBack':
      return deleteBackRun;
    case 'deleteForward':
      return deleteForwardRun;
    case 'enter':
    case 'tab':
      return { group: `key:${key}`, join: repeat };
    default:
      return undefined;
  }
}

/**
 * Whether a mouse event still has the primary button down. A drag-selection ends when it doesn't: the
 * release can happen outside the window, or after it lost focus, and then no `mouseup` arrives.
 */
export function primaryHeld(e: { readonly buttons: number }): boolean {
  return (e.buttons & 1) !== 0;
}

/**
 * Whether Tab leaves the editor instead of indenting: right after Escape, Tab and Shift+Tab move the focus
 * on as they do everywhere else, so a keyboard user is never trapped in the note (PRD §10.1).
 */
export function leavesEditor(e: KeyLike, afterEscape: boolean): boolean {
  return afterEscape && e.key === 'Tab' && !e.metaKey && !e.ctrlKey && !e.altKey;
}

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
  | { readonly kind: 'move'; readonly unit: 'char' | 'word' | 'line' | 'doc'; readonly dir: 1 | -1 }
  | { readonly kind: 'vertical'; readonly dir: 1 | -1 }
  | { readonly kind: 'deleteBack'; readonly unit: 'char' | 'word' | 'line' }
  | { readonly kind: 'deleteForward'; readonly unit: 'char' | 'word' }
  | { readonly kind: 'enter'; readonly soft: boolean }
  | { readonly kind: 'tab'; readonly outdent: boolean };

/**
 * Map a key to its editing action, by platform convention. On macOS ⌘ works by line or document (⌘←, ⌘↑,
 * ⌘⌫) and ⌥ by word (⌥←, ⌥⌫). Elsewhere Ctrl works by word (Ctrl+←, Ctrl+Backspace) and Ctrl+Home/End by
 * document; there is no delete-to-line-start key. Shift extends a motion; the caller reads it. Null means the
 * editor leaves the key alone.
 */
export function keyAction(e: KeyLike, mac: boolean): KeyAction | null {
  const mod = mac ? e.metaKey : e.ctrlKey;
  const word = mac ? e.altKey : e.ctrlKey;
  switch (e.key) {
    case 'ArrowLeft':
    case 'ArrowRight': {
      const dir = e.key === 'ArrowRight' ? 1 : -1;
      if (mac && e.metaKey) return { kind: 'move', unit: 'line', dir };
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
      return { kind: 'move', unit: !mac && e.ctrlKey ? 'doc' : 'line', dir };
    }
    case 'Backspace':
      if (mac && e.metaKey) return { kind: 'deleteBack', unit: 'line' };
      return { kind: 'deleteBack', unit: word ? 'word' : 'char' };
    case 'Delete':
      return { kind: 'deleteForward', unit: word ? 'word' : 'char' };
    case 'Enter':
      return mod ? null : { kind: 'enter', soft: e.shiftKey };
    case 'Tab':
      return mod || e.altKey ? null : { kind: 'tab', outdent: e.shiftKey };
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

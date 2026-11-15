import { describe, expect, it } from 'vitest';
import { CommandCatalog, type CommandSpec } from '../src/commands/command.ts';
import { isComposingKey, matches, parseShortcut, type ShortcutEvent } from '../src/commands/shortcut.ts';

type Ev = Partial<ShortcutEvent> & { altGraph?: boolean };

function ev(key: string, code: string, mods: Ev = {}): ShortcutEvent {
  return {
    key,
    code,
    metaKey: mods.metaKey ?? false,
    ctrlKey: mods.ctrlKey ?? false,
    altKey: mods.altKey ?? false,
    shiftKey: mods.shiftKey ?? false,
    getModifierState: (k: string) => k === 'AltGraph' && !!mods.altGraph,
  };
}

const spec = (id: string, shortcut: string, alternates?: string[]): CommandSpec => ({
  id,
  title: id,
  description: '',
  group: 'View',
  shortcut,
  ...(alternates ? { alternates } : {}),
  run: () => {},
});

/** The shell's and the editor's punctuation and digit shortcuts, in catalog order. */
const catalog = new CommandCatalog([
  spec('nextTab', 'mod+shift+]', ['ctrl+tab']),
  spec('previousTab', 'mod+shift+['),
  spec('focusTree', 'mod+alt+s'),
  spec('focusEditor', 'mod+alt+e'),
  spec('zoomIn', 'mod+=', ['mod++']),
  spec('zoomOut', 'mod+-'),
  spec('bullets', 'mod+.'),
  spec('numbers', 'mod+/'),
  spec('heading6', 'mod+6'),
  spec('taskStatus', 'mod+alt+l'),
  spec('undo', 'mod+z'),
]);
const win = (e: ShortcutEvent) => catalog.forEvent(e, false, false)?.id;
const mac = (e: ShortcutEvent) => catalog.forEvent(e, false, true)?.id;

describe('shortcuts on other keyboard layouts', () => {
  it('ignores characters typed with AltGr on Windows (Polish ś, €)', () => {
    expect(win(ev('ś', 'KeyS', { ctrlKey: true, altKey: true, altGraph: true }))).toBeUndefined();
    expect(win(ev('ę', 'KeyE', { ctrlKey: true, altKey: true }))).toBeUndefined();
    expect(win(ev('€', 'KeyE', { ctrlKey: true, altKey: true, altGraph: true }))).toBeUndefined();
    // Ctrl+Alt on a US layout still works.
    expect(win(ev('s', 'KeyS', { ctrlKey: true, altKey: true }))).toBe('focusTree');
  });

  it('matches punctuation by the character typed, not the key it is on', () => {
    // French AZERTY: "-" is on the 6 key.
    expect(win(ev('-', 'Digit6', { ctrlKey: true }))).toBe('zoomOut');
    // French AZERTY: the digit needs shift.
    expect(win(ev('6', 'Digit6', { ctrlKey: true, shiftKey: true }))).toBe('heading6');
    // macOS French: "-" is on the = key.
    expect(mac(ev('-', 'Equal', { metaKey: true }))).toBe('zoomOut');
    // German: "-" is on the / key.
    expect(mac(ev('-', 'Slash', { metaKey: true }))).toBe('zoomOut');
    // Dvorak ⌘+ is ⇧ on the ] key: zoom, not Next Tab.
    expect(mac(ev('+', 'BracketRight', { metaKey: true, shiftKey: true }))).toBe('zoomIn');
  });

  it('keeps the US shortcuts', () => {
    expect(mac(ev('}', 'BracketRight', { metaKey: true, shiftKey: true }))).toBe('nextTab');
    expect(mac(ev(']', 'BracketRight', { metaKey: true, shiftKey: true }))).toBe('nextTab');
    expect(win(ev('{', 'BracketLeft', { ctrlKey: true, shiftKey: true }))).toBe('previousTab');
    expect(mac(ev('=', 'Equal', { metaKey: true }))).toBe('zoomIn');
    expect(mac(ev('+', 'Equal', { metaKey: true, shiftKey: true }))).toBe('zoomIn');
    expect(mac(ev('-', 'Minus', { metaKey: true }))).toBe('zoomOut');
    expect(mac(ev('/', 'Slash', { metaKey: true }))).toBe('numbers');
    expect(mac(ev('6', 'Digit6', { metaKey: true }))).toBe('heading6');
    expect(mac(ev('Tab', 'Tab', { ctrlKey: true }))).toBe('nextTab');
  });

  it('goes by the physical key when the key types no ASCII (⌥ layers, Cyrillic)', () => {
    expect(mac(ev('¬', 'KeyL', { metaKey: true, altKey: true }))).toBe('taskStatus');
    expect(win(ev('я', 'KeyZ', { ctrlKey: true }))).toBe('undo');
    expect(mac(ev('≠', 'Equal', { metaKey: true, altKey: true }))).toBeUndefined();
  });

  it('keeps shift significant for letters', () => {
    expect(matches(parseShortcut('mod+z'), ev('Z', 'KeyZ', { metaKey: true, shiftKey: true }), true)).toBe(
      false,
    );
    expect(
      matches(parseShortcut('mod+shift+z'), ev('z', 'KeyZ', { metaKey: true, shiftKey: true }), true),
    ).toBe(true);
  });
});

describe('keys during IME composition', () => {
  it('runs no shortcut for a key that belongs to a composition', () => {
    const z = ev('z', 'KeyZ', { metaKey: true });
    expect(mac(z)).toBe('undo');
    expect(mac({ ...z, isComposing: true })).toBeUndefined();
    expect(mac({ ...z, keyCode: 229 })).toBeUndefined();
  });

  it('tells a composing key from an ordinary one', () => {
    expect(isComposingKey({ isComposing: true, keyCode: 13 })).toBe(true);
    expect(isComposingKey({ isComposing: false, keyCode: 229 })).toBe(true);
    expect(isComposingKey({ isComposing: false, keyCode: 13 })).toBe(false);
    expect(isComposingKey({})).toBe(false);
  });
});

describe('CommandCatalog.run', () => {
  it('hands the target to the command and to its enabled check, and asks the gate first', () => {
    const seen: unknown[] = [];
    const cat = new CommandCatalog([
      {
        id: 'x',
        title: 'X',
        description: '',
        group: 'File',
        enabled: (t) => t?.path !== 'locked.md',
        run: (t) => void seen.push(t),
      },
    ]);
    expect(cat.run('x', { path: 'a.md' })).toBe(true);
    expect(cat.run('x', { path: 'locked.md' })).toBe(false);
    cat.gate = () => false;
    expect(cat.run('x')).toBe(false);
    expect(seen).toEqual([{ path: 'a.md' }]);
  });
});

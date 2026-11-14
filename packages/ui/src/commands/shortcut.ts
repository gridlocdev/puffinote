/**
 * A platform-agnostic shortcut: `mod` is ⌘ on macOS and Ctrl elsewhere. Written as strings like
 * `mod+shift+k`, `alt+enter`, `f2`. The same value drives key matching, labels, and Electron
 * accelerators, so they never disagree.
 */
export interface Shortcut {
  readonly key: string;
  readonly mod: boolean;
  readonly shift: boolean;
  readonly alt: boolean;
  /** Literal Ctrl on macOS (rare; e.g. ⌃Tab). */
  readonly ctrl: boolean;
}

export function parseShortcut(spec: string): Shortcut {
  const lower = spec.toLowerCase();
  const plus = lower.endsWith('++') || lower === '+';
  const parts = (plus ? lower.slice(0, -1) : lower).split('+');
  const last = parts.pop() ?? '';
  const key = plus ? '+' : last;
  return {
    key: key === 'plus' ? '+' : key,
    mod: parts.includes('mod'),
    shift: parts.includes('shift'),
    alt: parts.includes('alt'),
    ctrl: parts.includes('ctrl'),
  };
}

export const isMac: boolean =
  typeof navigator !== 'undefined' && /Mac|iPhone|iPad/.test(navigator.platform || navigator.userAgent);

/** The character each physical punctuation key types on a US layout, for keys that type no ASCII here. */
const PUNCTUATION: Record<string, string> = {
  Semicolon: ';',
  Equal: '=',
  Minus: '-',
  Comma: ',',
  Period: '.',
  Slash: '/',
  Backquote: '`',
  BracketLeft: '[',
  BracketRight: ']',
  Backslash: '\\',
  Quote: "'",
};

/** Shifted punctuation and the character on the same key unshifted (`}` is ⇧]), for `mod+shift+]`. */
const UNSHIFTED: Record<string, string> = {
  '{': '[',
  '}': ']',
  '+': '=',
  _: '-',
  ':': ';',
  '"': "'",
  '<': ',',
  '>': '.',
  '?': '/',
  '|': '\\',
  '~': '`',
};

const NAMES: Record<string, string> = {
  ' ': 'space',
  arrowup: 'up',
  arrowdown: 'down',
  arrowleft: 'left',
  arrowright: 'right',
  escape: 'esc',
};

/** The parts of a keyboard event shortcut matching reads. */
export type ShortcutEvent = Pick<
  KeyboardEvent,
  'key' | 'code' | 'metaKey' | 'ctrlKey' | 'altKey' | 'shiftKey'
> & { getModifierState?: (key: string) => boolean };

/**
 * True when the event typed a character through AltGr rather than pressing a shortcut. Windows reports
 * AltGr as Ctrl+Alt, so Polish AltGr+S (ś) would otherwise run Ctrl+Alt+S. macOS has no AltGr (⌥ is Alt).
 */
function isAltGraph(e: ShortcutEvent, mac: boolean): boolean {
  if (mac) return false;
  if (e.getModifierState?.('AltGraph')) return true;
  return e.ctrlKey && e.altKey && [...e.key].length === 1 && !/^[\x20-\x7e]$/.test(e.key);
}

/**
 * How well a key event matches a shortcut: 0 when it doesn't, else a rank where lower is better, so a lookup
 * can prefer exact matches across the whole catalog before looser ones.
 *
 * 1. The typed character (or the named key) and shift are exactly the shortcut's. A character goes by what the
 *    layout typed, so AZERTY Ctrl+- is Ctrl+- even though the key is labelled 6, and German ⌘- is ⌘-.
 * 2. Typed punctuation or a digit that needed shift on this layout (Dvorak ⌘+, AZERTY Ctrl+⇧6) matches the
 *    shortcut without shift.
 * 3. Shifted punctuation matches the shortcut written with shift and the unshifted character (⇧⌘} is ⇧⌘]).
 * 4. A key that typed no ASCII character (an ⌥ layer, a Cyrillic layout) goes by its physical key, so ⌥⌘L
 *    (¬) and ⌘Я are ⌥⌘L and ⌘Z.
 */
export function matchRank(s: Shortcut, e: ShortcutEvent, mac = isMac): number {
  const mod = mac ? e.metaKey : e.ctrlKey;
  const ctrl = mac ? e.ctrlKey : false;
  if (mod !== s.mod || e.altKey !== s.alt || ctrl !== s.ctrl) return 0;
  if (isAltGraph(e, mac)) return 0;
  if (/^[\x21-\x7e]$/.test(e.key)) {
    const ch = e.key.toLowerCase();
    if (ch === s.key && e.shiftKey === s.shift) return 1;
    if (ch === s.key && e.shiftKey && !s.shift && !/^[a-z]$/.test(ch)) return 2;
    if (UNSHIFTED[ch] === s.key && e.shiftKey && s.shift) return 3;
    return 0;
  }
  if ([...e.key].length === 1 && e.key !== ' ') {
    // A character outside ASCII: the physical key.
    let physical: string | undefined;
    if (/^Key[A-Z]$/.test(e.code)) physical = e.code.slice(3).toLowerCase();
    else if (/^Digit\d$/.test(e.code)) physical = e.code.slice(5);
    else physical = PUNCTUATION[e.code];
    return physical === s.key && e.shiftKey === s.shift ? 4 : 0;
  }
  const k = e.key.toLowerCase();
  return (NAMES[k] ?? k) === s.key && e.shiftKey === s.shift ? 1 : 0;
}

export function matches(s: Shortcut, e: ShortcutEvent, mac = isMac): boolean {
  return matchRank(s, e, mac) > 0;
}

const MAC_SYMBOLS: Record<string, string> = {
  enter: '↩',
  esc: '⎋',
  backspace: '⌫',
  delete: '⌦',
  tab: '⇥',
  up: '↑',
  down: '↓',
  left: '←',
  right: '→',
  space: '␣',
  pageup: '⇞',
  pagedown: '⇟',
  home: '↖',
  end: '↘',
};

export function formatShortcut(s: Shortcut, mac = isMac): string {
  const key =
    s.key.length === 1 ? s.key.toUpperCase() : ((mac ? MAC_SYMBOLS[s.key] : undefined) ?? cap(s.key));
  if (mac) return `${s.ctrl ? '⌃' : ''}${s.alt ? '⌥' : ''}${s.shift ? '⇧' : ''}${s.mod ? '⌘' : ''}${key}`;
  return [s.mod && 'Ctrl', s.alt && 'Alt', s.shift && 'Shift', key].filter(Boolean).join('+');
}

/** Electron accelerator string (CmdOrCtrl+Shift+K). */
export function toAccelerator(s: Shortcut): string {
  const names: Record<string, string> = {
    enter: 'Return',
    esc: 'Escape',
    up: 'Up',
    down: 'Down',
    left: 'Left',
    right: 'Right',
    '+': 'Plus',
    pageup: 'PageUp',
    pagedown: 'PageDown',
  };
  const key = names[s.key] ?? (s.key.length === 1 ? s.key.toUpperCase() : cap(s.key));
  return [s.mod && 'CmdOrCtrl', s.ctrl && 'Ctrl', s.alt && 'Alt', s.shift && 'Shift', key]
    .filter(Boolean)
    .join('+');
}

function cap(s: string): string {
  return s.charAt(0).toUpperCase() + s.slice(1);
}

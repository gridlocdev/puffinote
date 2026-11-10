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

/** Which key a keyboard event pressed, normalised to the names `parseShortcut` uses. */
function eventKey(e: KeyboardEvent): string {
  // The layout's own letter or digit wins (AZERTY ⌘Z is the key labelled Z, Dvorak too) ...
  if (/^[a-z0-9]$/i.test(e.key)) return e.key.toLowerCase();
  // ... else the physical key, so ⌥/⇧ layers don't change the match (⌥K → "˚").
  if (/^Key[A-Z]$/.test(e.code)) return e.code.slice(3).toLowerCase();
  if (/^Digit\d$/.test(e.code)) return e.code.slice(5);
  // Punctuation by physical key too, so ⌘⇧; and ⌥= match their specs on any shift/option layer.
  const punct = PUNCTUATION[e.code];
  if (punct) return punct;
  const k = e.key.toLowerCase();
  const names: Record<string, string> = {
    ' ': 'space',
    arrowup: 'up',
    arrowdown: 'down',
    arrowleft: 'left',
    arrowright: 'right',
    escape: 'esc',
  };
  return names[k] ?? k;
}

export function matches(s: Shortcut, e: KeyboardEvent, mac = isMac): boolean {
  const mod = mac ? e.metaKey : e.ctrlKey;
  const ctrl = mac ? e.ctrlKey : false;
  if (mod !== s.mod || e.altKey !== s.alt || ctrl !== s.ctrl) return false;
  const key = eventKey(e);
  // Punctuation produced with shift (e.g. "+" on ⇧=) matches without requiring shift in the spec.
  if (key === s.key && e.shiftKey === s.shift) return true;
  return !/^[a-z0-9]$/.test(s.key) && e.key === s.key && !s.shift;
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

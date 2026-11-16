/**
 * The rules for saving one open note, as a table (PRD §5). `Notebook` keeps each tab's state in its signals
 * (`dirty`, `conflict`) and its own bookkeeping (the write in flight, the autosave timer, the recovery copy);
 * this table says which moves those may make. `docs/architecture.md` draws it, and a test checks the drawing
 * matches.
 */

export type SaveState =
  /** The note on disk has the tab's text. */
  | 'clean'
  /** Unsaved edits; with Auto-save on, a save follows a moment after the last one. */
  | 'dirty'
  /** Writing the note; nothing changed since the write started. */
  | 'saving'
  /** Writing the note; the tab changed since, so it is still dirty when the write lands. */
  | 'savingDirty'
  /** Save Again is writing back a note that went on disk under a tab without edits. */
  | 'restoring'
  /** A write failed; the next edit or ⌘S tries again. */
  | 'failed'
  /** The note changed or went on disk under unsaved edits; nothing is written until the user chooses. */
  | 'conflict'
  /** The note went on disk under a tab without edits; Save Again writes it back. */
  | 'missing'
  /** The tab is closed. */
  | 'closed';

export type SaveEvent =
  /** The user edits, so the text differs from the disk's. */
  | 'edit'
  /** Undo or an edit brings the text back to the disk's. */
  | 'editBack'
  /** The autosave delay passed since the last edit. */
  | 'timer'
  /** ⌘S, Save All, or saving before a close or a move. */
  | 'save'
  | 'saved'
  | 'failed'
  /** The write found the note changed or gone on disk. */
  | 'versionChanged'
  /** A refresh read a different text from disk. */
  | 'outsideChange'
  /** A refresh found the note gone. */
  | 'outsideDelete'
  /** A refresh found the note back, with the text the tab last saved. */
  | 'fileBack'
  /** Reload from Disk: take the disk's version. */
  | 'reloadFromDisk'
  /** Keep My Version or Save Again: write over the disk's version. */
  | 'keepMine'
  /** Close Tab, after the shell asked about unsaved edits. */
  | 'close';

export type Effect =
  /** Start the autosave delay again, when Auto-save is on. */
  | 'startTimer'
  | 'stopTimer'
  /** Keep a recovery copy of the text, a moment after the last edit (PRD §5). */
  | 'keepCopy'
  /** Remove the recovery copy: the disk has every edit, or the user threw them away. */
  | 'dropCopy'
  /** Write the text, checking the disk still has the version the tab last read or saved. */
  | 'writeFile'
  /**
   * Write the text whatever the disk has. The user chose it, so the tab is no longer in a conflict (`Notebook`
   * keeps its flag up until the write lands, and a failed one leaves it up).
   */
  | 'overwriteFile'
  /** A save asked for while one is in flight waits for it. */
  | 'queueSave'
  /** The save that waited goes out now. */
  | 'runQueued'
  /** Take the disk's text and encoding, as one undo step. */
  | 'takeFile'
  /** Take the disk's text and encoding over the tab's edits, as one undo step: the user chose it. */
  | 'replaceWithFile'
  /** Show in the tab that the note changed or went on disk. */
  | 'flag'
  | 'unflag'
  /** Say in the status bar why the write failed. */
  | 'report'
  /** Throw the tab's unsaved edits away: the user chose it. */
  | 'discard';

export interface Transition {
  readonly from: SaveState;
  readonly on: SaveEvent;
  readonly to: SaveState;
  readonly effects: readonly Effect[];
}

/** Every allowed move. An event with no row for the state it arrives in changes nothing. */
export const SAVE_TRANSITIONS: readonly Transition[] = [
  { from: 'clean', on: 'edit', to: 'dirty', effects: ['startTimer', 'keepCopy'] },
  { from: 'clean', on: 'outsideChange', to: 'clean', effects: ['takeFile'] },
  { from: 'clean', on: 'outsideDelete', to: 'missing', effects: ['flag'] },
  { from: 'clean', on: 'close', to: 'closed', effects: [] },

  { from: 'dirty', on: 'edit', to: 'dirty', effects: ['startTimer', 'keepCopy'] },
  { from: 'dirty', on: 'editBack', to: 'clean', effects: ['stopTimer', 'dropCopy'] },
  { from: 'dirty', on: 'timer', to: 'saving', effects: ['writeFile'] },
  { from: 'dirty', on: 'save', to: 'saving', effects: ['stopTimer', 'writeFile'] },
  { from: 'dirty', on: 'outsideChange', to: 'conflict', effects: ['stopTimer', 'flag'] },
  { from: 'dirty', on: 'outsideDelete', to: 'conflict', effects: ['stopTimer', 'flag'] },
  { from: 'dirty', on: 'close', to: 'closed', effects: ['stopTimer', 'discard', 'dropCopy'] },

  // A refresh skips a tab whose write is in flight: the write's answer says what is on disk.
  { from: 'saving', on: 'edit', to: 'savingDirty', effects: ['startTimer', 'keepCopy'] },
  { from: 'saving', on: 'saved', to: 'clean', effects: ['dropCopy'] },
  { from: 'saving', on: 'failed', to: 'failed', effects: ['report'] },
  { from: 'saving', on: 'versionChanged', to: 'conflict', effects: ['flag'] },
  { from: 'saving', on: 'close', to: 'closed', effects: ['discard', 'dropCopy'] },

  { from: 'savingDirty', on: 'edit', to: 'savingDirty', effects: ['startTimer', 'keepCopy'] },
  { from: 'savingDirty', on: 'timer', to: 'savingDirty', effects: ['queueSave'] },
  { from: 'savingDirty', on: 'save', to: 'savingDirty', effects: ['queueSave'] },
  { from: 'savingDirty', on: 'saved', to: 'dirty', effects: ['startTimer', 'runQueued'] },
  { from: 'savingDirty', on: 'failed', to: 'dirty', effects: ['report', 'startTimer', 'runQueued'] },
  { from: 'savingDirty', on: 'versionChanged', to: 'conflict', effects: ['stopTimer', 'flag'] },
  { from: 'savingDirty', on: 'close', to: 'closed', effects: ['stopTimer', 'discard', 'dropCopy'] },

  { from: 'failed', on: 'edit', to: 'dirty', effects: ['startTimer', 'keepCopy'] },
  { from: 'failed', on: 'editBack', to: 'clean', effects: ['dropCopy'] },
  { from: 'failed', on: 'save', to: 'saving', effects: ['writeFile'] },
  { from: 'failed', on: 'outsideChange', to: 'conflict', effects: ['flag'] },
  { from: 'failed', on: 'outsideDelete', to: 'conflict', effects: ['flag'] },
  { from: 'failed', on: 'close', to: 'closed', effects: ['discard', 'dropCopy'] },

  { from: 'conflict', on: 'edit', to: 'conflict', effects: ['keepCopy'] },
  { from: 'conflict', on: 'fileBack', to: 'dirty', effects: ['unflag', 'startTimer'] },
  { from: 'conflict', on: 'reloadFromDisk', to: 'clean', effects: ['replaceWithFile', 'unflag', 'dropCopy'] },
  { from: 'conflict', on: 'keepMine', to: 'saving', effects: ['overwriteFile'] },
  { from: 'conflict', on: 'close', to: 'closed', effects: ['discard', 'dropCopy'] },

  { from: 'missing', on: 'edit', to: 'conflict', effects: ['keepCopy'] },
  { from: 'missing', on: 'outsideChange', to: 'clean', effects: ['takeFile', 'unflag'] },
  { from: 'missing', on: 'fileBack', to: 'clean', effects: ['unflag'] },
  { from: 'missing', on: 'keepMine', to: 'restoring', effects: ['overwriteFile'] },
  { from: 'missing', on: 'close', to: 'closed', effects: [] },

  { from: 'restoring', on: 'edit', to: 'savingDirty', effects: ['startTimer', 'keepCopy'] },
  { from: 'restoring', on: 'saved', to: 'clean', effects: [] },
  { from: 'restoring', on: 'failed', to: 'missing', effects: ['report', 'flag'] },
  { from: 'restoring', on: 'versionChanged', to: 'missing', effects: ['flag'] },
  { from: 'restoring', on: 'close', to: 'closed', effects: [] },
];

const NONE: readonly Effect[] = [];

/** Where `event` takes `state`, and what to do on the way. */
export function step(state: SaveState, event: SaveEvent): { to: SaveState; effects: readonly Effect[] } {
  const row = SAVE_TRANSITIONS.find((t) => t.from === state && t.on === event);
  return row ? { to: row.to, effects: row.effects } : { to: state, effects: NONE };
}

/** The table as a Mermaid state diagram, for `docs/architecture.md`. */
export function toMermaid(): string {
  const lines = ['stateDiagram-v2', '  [*] --> clean', '  closed --> [*]'];
  for (const t of SAVE_TRANSITIONS) {
    const effects = t.effects.length ? ` / ${t.effects.join(', ')}` : '';
    lines.push(`  ${t.from} --> ${t.to} : ${t.on}${effects}`);
  }
  return lines.join('\n');
}

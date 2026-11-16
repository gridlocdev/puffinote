import { readFileSync, writeFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import {
  SAVE_TRANSITIONS,
  step,
  toMermaid,
  type SaveEvent,
  type SaveState,
} from '../src/notebook/saveChart.ts';
import { runRandomOps, type Op } from './support/randomOps.ts';

/** The chart's state, plus a model of the note on disk and the user's typing around it. */
interface World {
  state: SaveState;
  autoSave: boolean;
  /** Edits the disk doesn't have yet. */
  unsaved: boolean;
  /** Edits made since the write in flight started. */
  editedSinceWrite: boolean;
  /** Writes started and not yet answered. */
  writes: number;
  /** The note changed on disk while a write was out, so that write can't land as it was sent. */
  diskMoved: boolean;
  /** The autosave delay is running. */
  timer: boolean;
  /** A save waits for the write in flight. */
  queued: boolean;
  /** A recovery copy of the text is kept, or will be a moment after the last edit. */
  copy: boolean;
  /** The tab shows that the note changed or went on disk. */
  flagged: boolean;
}

function fire(w: World, event: SaveEvent): void {
  const { to, effects } = step(w.state, event);
  w.state = to;
  let runQueued = false;
  for (const effect of effects) {
    switch (effect) {
      case 'startTimer':
        w.timer = w.autoSave;
        break;
      case 'stopTimer':
        w.timer = false;
        break;
      case 'keepCopy':
        w.copy = true;
        break;
      case 'dropCopy':
        expect(w.unsaved, 'the recovery copy goes only when the disk has every edit').toBe(false);
        w.copy = false;
        break;
      case 'writeFile':
        expect(w.writes, 'never two writes at once').toBe(0);
        expect(w.flagged, 'nothing is written over a conflict until the user chooses').toBe(false);
        w.writes++;
        w.editedSinceWrite = false;
        break;
      case 'overwriteFile':
        expect(w.writes, 'never two writes at once').toBe(0);
        expect(w.flagged, 'only a conflict choice writes over the disk').toBe(true);
        w.writes++;
        w.editedSinceWrite = false;
        w.flagged = false;
        break;
      case 'queueSave':
        w.queued = true;
        break;
      case 'runQueued':
        runQueued = w.queued;
        w.queued = false;
        break;
      case 'takeFile':
        expect(w.unsaved, 'a refresh never replaces unsaved edits').toBe(false);
        expect(w.writes, 'a refresh never takes a read while a write is out').toBe(0);
        break;
      case 'replaceWithFile':
      case 'discard':
        // The user chose to drop the tab's edits.
        w.unsaved = false;
        w.editedSinceWrite = false;
        break;
      case 'flag':
        w.flagged = true;
        break;
      case 'unflag':
        w.flagged = false;
        break;
      case 'report':
        break;
    }
  }
  if (runQueued) fire(w, 'save');
}

/** A write's answer arrives. */
function answer(w: World, event: 'saved' | 'failed' | 'versionChanged'): void {
  w.writes--;
  if (event === 'saved') w.unsaved = w.editedSinceWrite;
  w.diskMoved = false;
  fire(w, event);
}

const user = (event: SaveEvent, weight = 1): Op<World> => ({
  name: event,
  weight,
  run: (w) => fire(w, event),
});

const ops: Op<World>[] = [
  {
    name: 'edit',
    weight: 4,
    run: (w) => {
      w.unsaved = true;
      w.editedSinceWrite = true;
      fire(w, 'edit');
    },
  },
  {
    name: 'editBack',
    when: (w) => w.state === 'dirty' || w.state === 'failed',
    run: (w) => {
      w.unsaved = false;
      fire(w, 'editBack');
    },
  },
  {
    name: 'timer',
    weight: 2,
    when: (w) => w.timer,
    run: (w) => {
      w.timer = false;
      fire(w, 'timer');
    },
  },
  user('save'),
  { name: 'saved', weight: 2, when: (w) => w.writes > 0 && !w.diskMoved, run: (w) => answer(w, 'saved') },
  { name: 'failed', when: (w) => w.writes > 0, run: (w) => answer(w, 'failed') },
  { name: 'versionChanged', when: (w) => w.writes > 0, run: (w) => answer(w, 'versionChanged') },
  {
    // Another app or machine writes the note, then a refresh sees it (or a write's answer will).
    name: 'outsideChange',
    run: (w) => {
      if (w.writes > 0) w.diskMoved = true;
      else fire(w, 'outsideChange');
    },
  },
  {
    name: 'outsideDelete',
    weight: 0.5,
    run: (w) => {
      if (w.writes > 0) w.diskMoved = true;
      else fire(w, 'outsideDelete');
    },
  },
  user('fileBack', 0.3),
  user('reloadFromDisk', 0.5),
  user('keepMine', 0.5),
  user('close', 0.05),
];

const SAVING: readonly SaveState[] = ['saving', 'savingDirty', 'restoring'];

describe('the save chart', () => {
  it('random events never write twice at once or over a conflict, keep a copy of every unsaved edit, and autosave every one', async () => {
    await runRandomOps<World>({
      steps: 200,
      setup: (r) => ({
        state: 'clean',
        autoSave: r.chance(0.7),
        unsaved: false,
        editedSinceWrite: false,
        writes: 0,
        diskMoved: false,
        timer: false,
        queued: false,
        copy: false,
        flagged: false,
      }),
      ops,
      check: (w) => {
        if (w.state === 'closed') return;
        const s = w.state;
        expect(w.writes, 'a write is out exactly while saving').toBe(SAVING.includes(s) ? 1 : 0);
        expect(w.flagged, 'flagged exactly in a conflict').toBe(s === 'conflict' || s === 'missing');
        if (s === 'clean' || s === 'missing' || s === 'restoring')
          expect(w.unsaved, 'clean means the disk has every edit').toBe(false);
        if (s === 'dirty' || s === 'failed' || s === 'conflict' || s === 'savingDirty')
          expect(w.unsaved, `${s} means there are unsaved edits`).toBe(true);
        if (s === 'saving') expect(w.editedSinceWrite, 'saving: no edit since the write started').toBe(false);
        if (s === 'savingDirty')
          expect(w.editedSinceWrite, 'savingDirty: an edit since the write started').toBe(true);
        if (s === 'dirty' && w.autoSave)
          expect(w.timer, 'with Auto-save on, a dirty tab saves soon').toBe(true);
        if (w.unsaved) expect(w.copy, 'every unsaved edit has a recovery copy').toBe(true);
      },
    });
  });

  it('lists each move once', () => {
    const keys = SAVE_TRANSITIONS.map((t) => `${t.from} ${t.on}`);
    expect(new Set(keys).size).toBe(keys.length);
  });

  it('is drawn in docs/architecture.md', () => {
    const file = new URL('../../../docs/architecture.md', import.meta.url);
    const doc = readFileSync(file, 'utf8');
    const block = /```mermaid\n(stateDiagram-v2\n[\s\S]*?)\n```/;
    const drawn = block.exec(doc)?.[1];
    expect(drawn, 'docs/architecture.md needs a ```mermaid stateDiagram-v2 block').toBeDefined();
    // `just diagram` redraws it from the table.
    if (process.env.WRITE_DIAGRAM === '1' && drawn !== toMermaid())
      writeFileSync(
        file,
        doc.replace(block, () => '```mermaid\n' + toMermaid() + '\n```'),
      );
    else expect(drawn, 'the diagram is out of date: run `just diagram`').toBe(toMermaid());
  });
});

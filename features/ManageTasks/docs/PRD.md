# Pivotask — Product Requirements

**Pivotask** is a Trello-inspired task board for the Puffinote family. One board lives in one JSON file.

Design rationale — why it is a separate app, why it uses stock controls, what the phases were — lives in
[proposal 0004](../../../docs/proposals/0004-pivotask.md), and the
[prototype](../../../pivotask-prototype/pivotask-prototype.html) is the behavioural spec it was drawn
from. This document is what it does.

---

## 1. The document

```
Board
├── labels[]       { id, name, color }
└── groups[]       { id, name, collapsed, foldDone, tasks[] }
    └── tasks[]    { id, title, notes, due, important, done, steps[], labels[] }
        └── steps[] { id, text, done }
```

**One board per file.** The root object *is* the board — no wrapping array, no board id, no in-app board
switcher. More boards means more files, which a notebook app already handles.

## 2. The file format

Extension **`.pivotask.json`**. The `.json` tail is deliberate: the file stays readable, diffable, and
openable by anything, and Plaintain can open it as text without ceremony.

Four rules, and they are the format's real design, because this file lives in git:

| Rule | Why |
|---|---|
| **Deterministic output** — stable key order, 2-space indent, LF, trailing newline | A save that changed nothing produces byte-identical output, so it never appears in `git diff`. Without this, "did anything actually change?" stops being answerable by looking. |
| **Unknown fields preserved** | A file written by a newer Pivotask, opened and saved by an older one, must not lose data. Every object round-trips what it didn't recognise. |
| **`"version": 1`, and a version ahead of ours is refused** | Reading half of a format we don't know and writing our half back is how a file gets quietly destroyed. |
| **Atomic save** — temp file in the target's own directory, then rename | Same directory because a rename across filesystems is a copy, and a copy is not atomic. |
| **External changes are noticed** — the open board is watched against a `FileStamp` baseline | This file lives in git, so the most likely way it changes is a `git pull` arriving while it is open. A clean board reloads. A board with unsaved edits keeps them, records the conflict (the status line says so), and asks at the save: Overwrite / Reload and Lose My Edits / Cancel, with Cancel the default. Auto-save stands down until it is settled — deciding whose version of a pulled file survives is not a decision to make on a timer with nobody watching. |

**Dates are dates.** `YYYY-MM-DD`, no times, no timezones, no recurrence. "Thursday" is what people mean
by a due date, and the moment a time is stored, the question of *whose* Thursday arrives with it.

**Labels live in the document**, referenced by id from tasks. Fixed palettes don't survive contact with
real users.

**Notes are plain text**, never Markdown. A note attached to a task is a sentence or two; if it genuinely
wants to be a document, that document is a `.md` file.

A JSON Schema ships at [`pivotask.schema.json`](pivotask.schema.json).

## 3. The board

One column per group. Each card shows a checkbox, the title, and — **only when it has them** — label
chips, a due pill, a step count, and a star. A card shows nothing it has nothing to say about, which is
what keeps a column of plain one-line tasks from looking like a form.

- **Add a task** inline at the foot of any column; **add a group** at the end of the board.
- **Collapse** a column, or **fold** its completed tasks away.
- **Drag** a card between columns or within one: a line shows exactly where it would land, the column it
  would join lights up, and **Escape puts it back** — a drag you have thought better of should not have to
  be dropped somewhere and undone.
- **The keyboard reaches everything the drag does.** Arrows walk the cards (←/→ across columns, ↑/↓ within
  one; on the list, ↑/↓ run the whole thing as one list); **⌥⇧ and an arrow move the task itself** —
  vertically it changes places with its neighbour, horizontally it joins the column beside it at the same
  height. Both go through the same undoable `MoveTask` a drop does, so ⌘Z reverses either identically.
  ⌥⇧-arrow is the family's existing idiom, not a new one: it is what Cellestra's grid does to a cell's
  contents and what the radial settings list does to a segment. This is the path the design has always
  said must exist (`ptask move`, and the Move command), so the feature is never blocked on drag-and-drop
  feeling right — and a board that can only be rearranged by dragging is a board somebody cannot rearrange
  at all.
- **Overdue** is the one card state that gets a colour of its own. Everything else stays chrome-muted,
  because a board where three things shout is a board where nothing does.

## 4. The task dialog

Everything about one task: title, notes, checklist with a progress bar, labels, due date.

**Progressive disclosure**: labels, the due date, and the steps section appear only once the task has
them, or once you ask for them from the "+" menu. A task is usually a sentence, and a dialog that opens
with five empty sections implies you have failed to fill them in.

Edits apply as they are made — there is no OK/Cancel, because "Cancel" would have to mean something
ambiguous about a checklist you already ticked. Everything is undoable instead.

## 5. Views, filters, and search

Two readings of the same tasks — **Board** (columns) and **List** (grouped rows) — sharing **one** filter
set and **one** search box. That sharing is not an implementation detail: it is why the list view could
be added later without reworking the board.

| Filter | Means |
|---|---|
| All | everything |
| Starred | `important` |
| Due soon | due today or tomorrow — **or overdue, however late**. A task three weeks late is the most "due soon" thing there is. The window is deliberately one day: this filter answers "what do I have to deal with now", and it is only useful while the answer is short enough to act on. Three days quietly made it "this week", which a board column already says. |
| Unfinished | not done |

**Search** matches the title, the notes, **and the step text**. A checklist item is where the specific
words usually are; a search that couldn't reach them would send you opening tasks one at a time to look.

## 6. Undo

Command-based and session-only, capped at 1,000 steps, never written to the file. Every mutation is a
command that knows how to reverse itself, and a few are deliberately coarse: moving a task across
columns is *one* undo, and clearing a checklist is *one* undo, because that is how many presses the
person who made the mistake expects to need.

## 7. Commands

Every command lives in `TaskEditingCommands` (`Pivotask.Control`), so the standalone window and a
Puffinote tab offer the same set under the same chords.

| Action | Shortcut |
|---|---|
| Undo / Redo | ⌘Z / ⇧⌘Z |
| Add Task | ⇧⌘N |
| Search | ⌘F |
| Board / List, and the four filters | (menu) |

⌘K is **not** Pivotask's: it opens the host's Command Menu, as in the sibling editors.

### 7.1 Preferences

⌘, opens Preferences over what `settings.json` keeps. **Appearance → Theme** — *Follow System* (the
default), *Light*, or *Dark* — is the family's, worded and ordered the same in all five apps: unset, the
window wears whatever the desktop is wearing and keeps following it when that changes; choosing one takes
the choice back for good. It applies the moment the dialog is accepted, board included, and is remembered
for the next launch. Embedded in Puffinote, this setting isn't Pivotask's to make — the shell settles
appearance once for every editor it hosts, and this file's value is what Pivotask wears standalone.

## 8. The `ptask` CLI

A **full mutating verb set**, because task management is scriptable in a way text editing isn't:

```
ptask list [--group G] [--label L] [--starred] [--due-soon] [--open] [--json]
ptask show <task>
ptask add "title" [--group G] [--label L] [--due DATE] [--star]
ptask done <task> | ptask undone <task>
ptask move <task> --group G [--before <task>]
ptask groups
```

A task is named by id **or by part of its title** — ids are what a script uses, titles are how a person
refers to their own tasks. An ambiguous title lists the candidates rather than picking one.

The board is found automatically when there is exactly one `*.pivotask.json` in the working directory.
With none, or with several, it says what to do instead: guessing which board `ptask done` meant is the
kind of help that loses work.

The binary is `ptask`, not `pivotask` — a task CLI is typed many times a day and an editor's is not. The
app, assembly, bundle, and justfile recipes stay `Pivotask`.

## 9. Non-goals

No assignees or multi-user anything. No recurring tasks. No due *times* or timezones. No task
dependencies, no nesting beyond the one `steps[]` level, no Gantt or calendar views. No sync, no server,
no notifications. No attachments. No plugin system. No new NuGet above the trusted base.

**No app branding inside the editor.** The prototype opens with a brand block — the mark, "Pivot", and
"same tasks, turned 90°" — and that block is the one part of it that is a web page rather than an app. The
editor here is an embeddable control: it is the whole window in standalone Pivotask and one tab among
several in Puffinote, and in both the document is already named directly above it by the shared
`DocumentTitleBar`. A band underneath repeating the app's own name would say nothing the reader needs in
the first case and something actively wrong in the second, where the tab is a board and the window is a
notebook. Everything else in the prototype's header strip — the search, the filters, the view switch — is
about the tasks, and is kept.

And three boundaries worth stating as such: **task notes are plain text**; **one board per file**; and
**Puffinote does not aggregate tasks across the notebook** — a `.pivotask.json` opens in a tab like any
other file, and the shell keeps delegating to editors rather than reading document contents itself.

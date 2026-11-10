/**
 * What a task-list item's box says (PRD §8.3.2). One character between the brackets, three states:
 * `[ ]` not started, `[/]` in progress, `[x]` done. Any other character is not a box; the line stays literal
 * text. This is dialect, not presentation: colour and glyph belong to the surfaces.
 */
export type TaskState = 'notStarted' | 'inProgress' | 'done';

/** The states in the order the work moves in. The picker and any legend read this. */
export const taskStateOrder: readonly TaskState[] = ['notStarted', 'inProgress', 'done'];

/** The character between the brackets, canonically (`x`, never `X`). */
export function taskMark(state: TaskState): string {
  return state === 'inProgress' ? '/' : state === 'done' ? 'x' : ' ';
}

/** The box as spelled in the source: `[ ]`, `[/]` or `[x]`. */
export function taskCode(state: TaskState): string {
  return `[${taskMark(state)}]`;
}

/** The state's name, for a menu row, a tooltip or an exported `aria-label`. */
export function taskLabel(state: TaskState): string {
  return state === 'inProgress' ? 'In progress' : state === 'done' ? 'Done' : 'Not started';
}

/** What the state means, in a phrase. */
export function taskDescription(state: TaskState): string {
  return state === 'inProgress'
    ? 'work is under way'
    : state === 'done'
      ? 'the task is complete'
      : 'defined, no work yet';
}

/** Whether `mark` is a character a task box may hold. */
export function isTaskMark(mark: string): boolean {
  return mark === ' ' || mark === 'x' || mark === 'X' || mark === '/';
}

/** The state `mark` spells; not started for anything else. */
export function taskStateFromMark(mark: string): TaskState {
  return mark === 'x' || mark === 'X' ? 'done' : mark === '/' ? 'inProgress' : 'notStarted';
}

/** The state a task marker's source text (`"[x] "`, box first) spells. */
export function taskStateFromMarker(markerText: string): TaskState {
  return markerText.length > 1 ? taskStateFromMark(markerText.charAt(1)) : 'notStarted';
}

/**
 * The state a click or ⌘L lands on: done, unless already done, then back to not started. In progress is
 * chosen from the picker, never cycled through.
 */
export function toggledTaskState(state: TaskState): TaskState {
  return state === 'done' ? 'notStarted' : 'done';
}

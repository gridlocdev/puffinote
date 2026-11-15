import {
  formatShortcut,
  isComposingKey,
  isMac,
  matchRank,
  parseShortcut,
  type Shortcut,
  type ShortcutEvent,
} from './shortcut.ts';

/** Menu groups — the native menu bar and the palette's section labels. */
export type CommandGroup = 'File' | 'Edit' | 'View' | 'Insert' | 'Format' | 'Go' | 'Help';

/**
 * What a command acts on when it isn't the selection or the note in front: the file, folder or tab a context
 * menu was opened on. Commands that take one fall back to the selection without it.
 */
export interface CommandTarget {
  /** A notebook-relative file or folder. */
  readonly path?: string;
  /** The folder a new file goes in. */
  readonly dir?: string;
  /** An open tab, by its id. */
  readonly tabId?: number;
  /** A picture in the note in front: its Markdown's range and its link as written. */
  readonly picture?: { readonly start: number; readonly end: number; readonly url: string };
}

export interface CommandSpec {
  readonly id: string;
  readonly title: string;
  readonly description: string;
  readonly group: CommandGroup;
  readonly shortcut?: string;
  /** Unadvertised extra chords (⌘+ alongside ⌘=). */
  readonly alternates?: readonly string[];
  readonly icon?: string;
  /** Extra search words for the palette. */
  readonly keywords?: string;
  readonly run: (target?: CommandTarget) => void | Promise<void>;
  readonly enabled?: (target?: CommandTarget) => boolean;
  /** Toggle commands: whether it is on. */
  readonly active?: (target?: CommandTarget) => boolean;
  /** Hidden from the palette (e.g. the palette command itself). */
  readonly hidden?: boolean;
  /** Works while a text field (the find box, a dialog field) has focus. */
  readonly global?: boolean;
}

export interface Command extends CommandSpec {
  readonly shortcutKey: Shortcut | null;
  readonly alternateKeys: readonly Shortcut[];
  readonly shortcutText: string;
}

/**
 * The one command catalog: menus, context menus, toolbar, ⌘K palette, and key bindings all read it. Running a
 * command from anywhere goes through `run`, which checks the gate and records it for the palette's recents.
 */
export class CommandCatalog {
  readonly all: readonly Command[];
  private readonly byId: Map<string, Command>;
  private readonly recent: string[] = [];

  constructor(specs: readonly CommandSpec[]) {
    this.all = specs.map((s) => {
      const key = s.shortcut ? parseShortcut(s.shortcut) : null;
      return {
        ...s,
        shortcutKey: key,
        alternateKeys: (s.alternates ?? []).map(parseShortcut),
        shortcutText: key ? formatShortcut(key) : '',
      };
    });
    this.byId = new Map(this.all.map((c) => [c.id, c]));
    if (this.byId.size !== this.all.length) throw new Error('Duplicate command id');
  }

  get(id: string): Command | undefined {
    return this.byId.get(id);
  }

  isEnabled(c: Command, target?: CommandTarget): boolean {
    return c.enabled?.(target) ?? true;
  }

  /** Asked before every run, from any source; returning false cancels it (a question is waiting on the user). */
  gate: ((c: Command) => boolean) | null = null;

  /** Run `id` on `target` (or on the selection) if it is enabled and the gate lets it. */
  run(id: string, target?: CommandTarget): boolean {
    const c = this.byId.get(id);
    if (!c || !this.isEnabled(c, target)) return false;
    if (this.gate && !this.gate(c)) return false;
    if (!c.hidden) {
      const at = this.recent.indexOf(id);
      if (at >= 0) this.recent.splice(at, 1);
      this.recent.unshift(id);
      this.recent.length = Math.min(this.recent.length, 5);
    }
    void Promise.resolve(c.run(target)).catch((e: unknown) => console.error(`command ${id} failed`, e));
    return true;
  }

  get recentIds(): readonly string[] {
    return this.recent;
  }

  /**
   * The command a key event triggers, if any. `inText` = focus is in a text field. The best match wins across
   * the whole catalog (an exact one before one by the physical key; see `matchRank`), then catalog order.
   */
  forEvent(e: ShortcutEvent, inText: boolean, mac = isMac): Command | undefined {
    if (isComposingKey(e)) return undefined;
    let best: Command | undefined;
    let bestRank = Infinity;
    for (const c of this.all) {
      if (inText && !c.global) continue;
      for (const k of [c.shortcutKey, ...c.alternateKeys]) {
        const rank = k ? matchRank(k, e, mac) : 0;
        if (rank > 0 && rank < bestRank) {
          best = c;
          bestRank = rank;
        }
      }
    }
    return best;
  }

  /**
   * Palette search. Spaces in the query are ignored, so "insertrow" and "ins row" both find
   * Insert Row. Best first: the title starts with the query, then contains it, then the query
   * appears in the group, keywords, or description (or each typed word does), then the title
   * holds the query's letters in order (fuzzy).
   */
  search(query: string): Command[] {
    const q = compact(query);
    const visible = this.all.filter((c) => !c.hidden);
    if (!q) {
      const recent = this.recent.map((id) => this.byId.get(id)!).filter((c) => c && !c.hidden);
      return [...recent, ...visible.filter((c) => !recent.includes(c))];
    }
    const words = query.toLowerCase().split(/\s+/).filter(Boolean);
    const scored: [Command, number][] = [];
    for (const c of visible) {
      const title = compact(c.title);
      const rest = `${c.group} ${c.keywords ?? ''} ${c.description}`.toLowerCase();
      const score = title.startsWith(q)
        ? 0
        : title.includes(q)
          ? 1
          : compact(rest).includes(q) || words.every((w) => `${c.title.toLowerCase()} ${rest}`.includes(w))
            ? 2
            : inOrder(q, title)
              ? 3
              : -1;
      if (score >= 0) scored.push([c, score]);
    }
    // Array.sort is stable, so equal scores keep the catalog's order.
    return scored.sort((a, b) => a[1] - b[1]).map(([c]) => c);
  }
}

/** Lower-cased with every space removed: what the palette compares. */
export function compact(text: string): string {
  return text.toLowerCase().replace(/\s+/g, '');
}

/** True when every character of `needle` appears in `hay`, in order (gaps allowed). */
export function inOrder(needle: string, hay: string): boolean {
  let at = 0;
  for (const ch of needle) {
    at = hay.indexOf(ch, at) + 1;
    if (at === 0) return false;
  }
  return true;
}

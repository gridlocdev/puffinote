import { computed, effect, signal, type ReadonlySignal } from '@preact/signals';
import type { Notebook } from '../notebook/notebook.ts';
import type { Settings } from '../notebook/settings.ts';

export type Layout = Settings['layout'];

/**
 * What the window's layout (PRD §6) decides apart from drawing: which layout is up, whether the tab strip
 * shows, and the Library layout's one note at a time. It lives beside the shell's state rather than in a view,
 * so the rule holds whichever components are mounted and can be tested without them.
 */
export class LayoutPolicy {
  /** Toggle Tab Bar's session-only answer; null follows the setting (PRD §9). */
  readonly tabBarOverride = signal<boolean | null>(null);
  /** The icon rail with tabs, or the library with one note at a time. */
  readonly layout: ReadonlySignal<Layout>;
  readonly showTabBar: ReadonlySignal<boolean>;
  readonly #stop: () => void;

  constructor(notebook: ReadonlySignal<Notebook | null>) {
    this.layout = computed(() => notebook.value?.config.settings.value.layout ?? 'rail');
    this.showTabBar = computed(() => {
      const nb = notebook.value;
      if (!nb || nb.tabs.value.length === 0 || this.layout.value === 'library') return false;
      return this.tabBarOverride.value ?? nb.config.settings.value.showTabBar;
    });
    // The Library layout shows one note at a time: opening another lets go of the last, unless it still has
    // unsaved edits.
    this.#stop = effect(() => {
      const nb = notebook.value;
      if (!nb || this.layout.value !== 'library') return;
      const active = nb.active.value;
      if (!active) return;
      for (const t of nb.tabs.peek()) if (t !== active && !t.dirty.peek()) nb.closeTab(t);
    });
  }

  /** Whether Toggle Tab Bar would show the strip now, before the override flips it. */
  tabBarShown(nb: Notebook | null): boolean {
    return this.tabBarOverride.peek() ?? nb?.config.settings.peek().showTabBar ?? true;
  }

  dispose(): void {
    this.#stop();
  }
}

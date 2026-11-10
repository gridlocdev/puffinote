import { useLayoutEffect, useRef } from 'preact/hooks';
import type { Notebook, Tab } from '../notebook/notebook.ts';
import { Icon } from './Icon.tsx';
import type { ShellModel } from './shellModel.ts';

/** The open documents (PRD §7.3): click to show, middle-click or × to close, right-click for the closing verbs. */
export function TabStrip({ model, nb }: { readonly model: ShellModel; readonly nb: Notebook }) {
  const tabs = nb.tabs.value;
  const active = nb.active.value;
  const ref = useRef<HTMLDivElement>(null);
  const showExt = nb.settings.value.showFileExtensions;

  // The shortest scroll that brings the active tab into view; a tab already on screen doesn't move.
  useLayoutEffect(() => {
    ref.current
      ?.querySelector('[aria-selected="true"]')
      ?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
  }, [active, tabs.length]);

  const menu = (tab: Tab, e: MouseEvent) => {
    e.preventDefault();
    model.open({
      kind: 'menu',
      x: e.clientX,
      y: e.clientY,
      items: [
        { label: 'Close Tab', run: () => void model.closeTab(tab) },
        { label: 'Close Other Tabs', disabled: tabs.length < 2, run: () => void model.closeTabs(tab) },
        { label: 'Close All Tabs', run: () => void model.closeTabs(null) },
      ],
    });
  };

  return (
    <div class="pn-tabs" role="tablist" aria-label="Open notes" ref={ref}>
      {tabs.map((tab) => {
        const name = showExt ? tab.name : tab.name.replace(/\.[^.]+$/, '') || tab.name;
        const dirty = tab.dirty.value;
        const conflict = tab.conflict.value !== 'none';
        return (
          <div
            key={tab.path.value}
            role="tab"
            class={`pn-tab${conflict ? ' pn-tab-conflict' : ''}`}
            aria-selected={tab === active}
            title={tab.path.value}
            onPointerDown={(e) => {
              if (e.button === 0) {
                nb.activate(tab);
                if (tab.state) model.focusEditor();
              }
            }}
            onAuxClick={(e) => {
              if (e.button === 1) void model.closeTab(tab);
            }}
            onContextMenu={(e) => menu(tab, e)}
          >
            <span class="pn-tab-name">{name}</span>
            <button
              class={`pn-tab-close${dirty ? ' pn-tab-dirty' : ''}`}
              tabIndex={-1}
              aria-label={dirty ? `Close ${name} (unsaved)` : `Close ${name}`}
              onPointerDown={(e) => e.stopPropagation()}
              onClick={() => void model.closeTab(tab)}
            >
              <span class="pn-dot" />
              <Icon name="x" />
            </button>
          </div>
        );
      })}
    </div>
  );
}

import { useLayoutEffect, useRef } from 'preact/hooks';
import type { Notebook } from '../notebook/notebook.ts';
import type { Tab } from '../notebook/tab.ts';
import { Icon } from './Icon.tsx';
import type { ShellModel } from './shellModel.ts';

/**
 * The open documents (PRD §7): click to show, middle-click or × to close, right-click for pinning and the
 * closing verbs. Pinned tabs sit in their own group at the start, outside the part that scrolls.
 */
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
    model.openTabMenu(tab, { x: e.clientX, y: e.clientY });
  };

  const render = (tab: Tab) => {
    const pinned = tab.pinned.value;
    const name = showExt ? tab.name : tab.name.replace(/\.[^.]+$/, '') || tab.name;
    const dirty = tab.dirty.value;
    const conflict = tab.conflict.value !== 'none';
    return (
      <div
        key={tab.path.value}
        role="tab"
        class={`pn-tab${conflict ? ' pn-tab-conflict' : ''}${pinned ? ' pn-tab-pinned' : ''}`}
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
        {pinned ? (
          <button
            class={`pn-tab-close pn-tab-pin${dirty ? ' pn-tab-dirty' : ''}`}
            tabIndex={-1}
            aria-label={dirty ? `Unpin ${name} (unsaved)` : `Unpin ${name}`}
            title="Unpin"
            onPointerDown={(e) => e.stopPropagation()}
            onClick={() => nb.setTabPinned(tab, false)}
          >
            <span class="pn-dot" />
            <Icon name="pin" />
          </button>
        ) : (
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
        )}
      </div>
    );
  };
  const pinned = tabs.filter((t) => t.pinned.value);
  return (
    <div class="pn-tabs" role="tablist" aria-label="Open notes">
      {pinned.length > 0 && (
        <div class="pn-tabs-pinned" role="presentation">
          {pinned.map(render)}
        </div>
      )}
      <div class="pn-tabs-scroll" role="presentation" ref={ref}>
        {tabs.filter((t) => !t.pinned.value).map(render)}
      </div>
    </div>
  );
}

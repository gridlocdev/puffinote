import { sidebarPanels, type Notebook } from '../notebook/notebook.ts';
import { allFolders } from '../notebook/tree.ts';
import { Document, EmptyNotebook, StatusBar } from './DocPane.tsx';
import { FileTree } from './FileTree.tsx';
import { Icon } from './Icon.tsx';
import { FilterPanel, OutlinePanel, RecentPanel, Resizer, SearchPanel } from './panels.tsx';
import type { ShellModel } from './shellModel.ts';
import { TabStrip } from './TabStrip.tsx';

/**
 * The Icon rail layout (PRD §6), the default: a rail of panel buttons, the chosen panel in a floating card,
 * and the open notes as tabs in a second card.
 */
export function RailLayout({ model, nb }: { readonly model: ShellModel; readonly nb: Notebook }) {
  const tab = nb.active.value;
  return (
    <>
      <RailNav model={model} nb={nb} />
      {!nb.sidebarCollapsed.value && <RailPanel model={model} nb={nb} />}
      <main class="pn-shell-main pn-card">
        {model.showTabBar.value && <TabStrip model={model} nb={nb} />}
        <div class="pn-document">
          {tab ? <Document key={tab.id} model={model} nb={nb} tab={tab} /> : <EmptyNotebook model={model} />}
        </div>
        <StatusBar model={model} nb={nb} tab={tab} />
      </main>
    </>
  );
}

/** A command's title and its keys, for a button's tooltip. */
export function tip(model: ShellModel, id: string): string {
  const c = model.menuCatalog.get(id);
  return c ? `${c.title}${c.shortcutText ? ` (${c.shortcutText})` : ''}` : '';
}

/** The notebook's initials, for the rail's notebook button: "nb", "WN" for "Work Notes". */
export function initials(name: string): string {
  const words = name.split(/[\s_\-.]+/).filter(Boolean);
  if (words.length >= 2) return (words[0]![0]! + words[1]![0]!).toUpperCase();
  return (words[0] ?? '?').slice(0, 2);
}

const RECENT_TITLE = 'Recent Notebooks';

function RailNav({ model, nb }: { readonly model: ShellModel; readonly nb: Notebook }) {
  const panel = nb.panel.value;
  const collapsed = nb.sidebarCollapsed.value;
  const menuOpen = model.overlay.value?.kind === 'menu' && model.overlay.value.title === RECENT_TITLE;
  return (
    <nav class="pn-rail" aria-label="Panels">
      <button
        class="pn-rail-notebook"
        title={`${nb.name}: Recent Notebooks`}
        aria-label={`${nb.name}: Recent Notebooks`}
        aria-haspopup="menu"
        aria-expanded={menuOpen}
        onClick={(e) => {
          // The menu grows out of the button's right side, level with its top.
          const r = (e.currentTarget as HTMLElement).getBoundingClientRect();
          model.open({
            kind: 'menu',
            x: r.right + 6,
            y: r.top,
            title: RECENT_TITLE,
            items: model.notebookMenu(nb),
          });
        }}
      >
        {initials(nb.name)}
      </button>
      {sidebarPanels.map((p) => (
        <button
          key={p.id}
          class="pn-rail-button"
          aria-pressed={panel === p.id && !collapsed}
          title={tip(model, `view.sidebar.${p.id}`)}
          aria-label={p.title}
          onClick={() => {
            // The panel already showing folds away; any other comes up.
            if (panel === p.id && !collapsed) model.toggleSidebar();
            else model.showPanel(p.id);
          }}
        >
          <Icon name={p.icon} />
        </button>
      ))}
      <span class="pn-rail-gap" />
      <button
        class="pn-rail-button"
        aria-expanded={!collapsed}
        title={tip(model, 'view.toggleSidebar')}
        aria-label="Toggle Sidebar"
        onClick={() => model.toggleSidebar()}
      >
        <Icon name="panel-left" />
      </button>
      <button
        class="pn-rail-button"
        title={tip(model, 'view.sidebar.settings')}
        aria-label="Settings"
        onClick={() => model.open({ kind: 'settings' })}
      >
        <Icon name="settings-2" />
      </button>
    </nav>
  );
}

function RailPanel({ model, nb }: { readonly model: ShellModel; readonly nb: Notebook }) {
  const panel = nb.panel.value;
  const title = sidebarPanels.find((p) => p.id === panel)?.title ?? '';
  const files = panel === 'files' || panel === 'filter';
  return (
    <aside class="pn-sidebar pn-card" style={{ width: `${nb.sidebarWidth.value}px` }} aria-label={title}>
      <header
        class="pn-panel-head"
        onClick={(e) => {
          // Like the tree's empty space: a click off the buttons selects nothing, so New Note and New Folder
          // add to the top level (PRD §6.2).
          if (files && !(e.target as Element).closest('button')) {
            nb.selected.value = null;
            // The click was in the sidebar, so the keyboard goes there (↓ then picks the first row).
            (e.currentTarget as HTMLElement).parentElement?.querySelector<HTMLElement>('.pn-tree')?.focus();
          }
        }}
      >
        <h2 class="pn-panel-title">{title}</h2>
        {files && (
          <>
            <button
              class="pn-icon-button"
              title={tip(model, 'shell.newNote')}
              onClick={() => {
                model.newNote().catch(model.report);
              }}
            >
              <Icon name="file-plus" />
            </button>
            <button
              class="pn-icon-button"
              title={tip(model, 'shell.newFolder')}
              onClick={() => {
                model.newFolder().catch(model.report);
              }}
            >
              <Icon name="folder-plus" />
            </button>
            <button
              class="pn-icon-button"
              title="More"
              aria-label="More"
              aria-haspopup="menu"
              onClick={(e) => {
                const r = (e.currentTarget as HTMLElement).getBoundingClientRect();
                const folders = allFolders(nb.tree.peek());
                const open = nb.expanded.peek();
                model.open({
                  kind: 'menu',
                  x: r.left,
                  y: r.bottom + 4,
                  items: [
                    {
                      label: 'Expand All Folders',
                      disabled: folders.every((f) => open.has(f)),
                      run: () => nb.setExpanded(folders),
                    },
                    {
                      label: 'Collapse All Folders',
                      disabled: !folders.some((f) => open.has(f)),
                      run: () => nb.setExpanded([]),
                    },
                    null,
                    {
                      label: 'Refresh',
                      run: () => {
                        nb.refresh().catch(nb.report);
                      },
                    },
                  ],
                });
              }}
            >
              <Icon name="ellipsis" />
            </button>
          </>
        )}
      </header>
      <div
        class="pn-panel"
        onClick={(e) => {
          if (files && e.target === e.currentTarget) nb.selected.value = null;
        }}
      >
        {panel === 'files' && (
          <FileTree
            model={model}
            nb={nb}
            root={nb.tree.value}
            expanded={nb.expanded.value}
            onToggle={(p, open) => nb.toggleFolder(p, open)}
            label="Files"
          />
        )}
        {panel === 'filter' && <FilterPanel model={model} nb={nb} />}
        {panel === 'search' && <SearchPanel model={model} nb={nb} />}
        {panel === 'outline' && <OutlinePanel model={model} nb={nb} />}
        {panel === 'recent' && <RecentPanel model={model} nb={nb} />}
      </div>
      <Resizer nb={nb} />
    </aside>
  );
}

import { useState } from 'preact/hooks';
import type { Notebook } from '../notebook/notebook.ts';
import type { TrashItem } from '../notebook/trash.ts';
import { baseName, parentOf } from '../platform/paths.ts';
import { Dialog } from './Dialog.tsx';

const when = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' });

/**
 * Show Trash… (PRD §6.1): what the notebook's own Trash holds, newest first, each with Restore, and Empty
 * Trash, which asks first.
 */
export function TrashDialog(props: {
  readonly nb: Notebook;
  readonly items: readonly TrashItem[];
  readonly onClose: () => void;
  readonly onEmpty: () => void;
}) {
  const { nb } = props;
  const [items, setItems] = useState(props.items);
  const [busy, setBusy] = useState(false);
  const restore = async (picked: readonly TrashItem[]) => {
    setBusy(true);
    const done = await nb.trash.restore(picked);
    setBusy(false);
    // Whatever didn't come back (an error was shown) stays listed.
    const left = done.length === picked.length ? items.filter((i) => !picked.includes(i)) : items;
    setItems(left);
    if (left.length === 0) props.onClose();
  };
  return (
    <Dialog
      title="Notebook Trash"
      class="pn-unused pn-trash"
      onClose={props.onClose}
      onSubmit={props.onClose}
      actions={
        <>
          <button type="button" class="pn-danger" disabled={busy} onClick={props.onEmpty}>
            Empty Trash…
          </button>
          <button type="submit">Done</button>
        </>
      }
    >
      <p>
        Deleted files stay here until you empty the Trash. Restore puts one back where it was, under a new
        name if that one is taken.
      </p>
      <ul class="pn-unused-list pn-trash-list">
        {items.map((item) => (
          <li key={item.id}>
            <span class="pn-unused-name">
              {baseName(item.path)}
              {item.kind === 'dir' ? '/' : ''}
            </span>
            <span class="pn-unused-dir">
              {parentOf(item.path) || nb.name}
              {item.deleted ? ` · ${when.format(item.deleted)}` : ''}
            </span>
            <button type="button" disabled={busy} onClick={() => void restore([item])}>
              Restore
            </button>
          </li>
        ))}
      </ul>
    </Dialog>
  );
}

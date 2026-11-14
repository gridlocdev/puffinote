import { useState } from 'preact/hooks';
import type { Notebook } from '../notebook/notebook.ts';
import { baseName, parentOf } from '../platform/paths.ts';
import { Dialog } from './Dialog.tsx';

/**
 * Find Unused Pictures… (PRD §10.6): the notebook's pictures that no note links to, each ticked, and a button
 * that moves the ticked ones to the Trash (or deletes them where the notebook has no Trash).
 */
export function UnusedPicturesDialog(props: {
  readonly nb: Notebook;
  readonly paths: readonly string[];
  readonly onClose: () => void;
}) {
  const { nb, paths } = props;
  const [picked, setPicked] = useState<ReadonlySet<string>>(new Set(paths));
  const trash = nb.store.kind === 'native';
  const count = picked.size;
  const toggle = (p: string) => {
    const next = new Set(picked);
    if (!next.delete(p)) next.add(p);
    setPicked(next);
  };
  return (
    <Dialog
      title="Unused Pictures"
      class="pn-unused"
      onClose={props.onClose}
      onSubmit={() => {
        const chosen = paths.filter((p) => picked.has(p));
        props.onClose();
        void nb.removeUnused(chosen);
      }}
      actions={
        <>
          <button type="button" onClick={props.onClose}>
            Cancel
          </button>
          <button type="submit" class="pn-danger" disabled={count === 0}>
            {trash ? 'Move to Trash' : 'Delete'}
            {count > 0 ? ` (${count})` : ''}
          </button>
        </>
      }
    >
      <p>
        No note links to {paths.length === 1 ? 'this picture' : `these ${paths.length} pictures`}.
        {trash ? ' Ticked ones go to the Trash.' : ' Ticked ones are deleted, which can’t be undone.'}
      </p>
      <ul class="pn-unused-list">
        {paths.map((p) => (
          <li key={p}>
            <label>
              <input type="checkbox" checked={picked.has(p)} onChange={() => toggle(p)} />
              <img src={nb.resolveImage('', p)} alt="" />
              <span class="pn-unused-name">{baseName(p)}</span>
              <span class="pn-unused-dir">{parentOf(p) || nb.name}</span>
            </label>
          </li>
        ))}
      </ul>
    </Dialog>
  );
}

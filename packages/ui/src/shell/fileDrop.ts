import { after, type Cancel } from '../schedule.ts';
import { useRef, useState } from 'preact/hooks';
import { draggingFiles, droppedFiles } from './importPicker.ts';
import type { ShellModel } from './shellModel.ts';

const HOVER_OPEN_MS = 1000;

/**
 * A drop zone for files dragged in from the desktop (PRD §6.1). `folderAt` says which notebook folder a drag
 * over `target` would land in. `target` is that folder while a drag is over the zone, for the outline. A closed
 * folder held under the pointer for a second opens, when `closedFolderAt` finds one there and `open` is given.
 */
export function useFileDrop(
  model: ShellModel,
  folderAt: (target: EventTarget | null) => string | null,
  closedFolderAt?: (target: EventTarget | null) => string | null,
  open?: (path: string) => void,
) {
  const [target, setTarget] = useState<string | null>(null);
  const hover = useRef<{ path: string | null; timer: Cancel | null }>({
    path: null,
    timer: null,
  });
  const clearHover = () => {
    hover.current.timer?.();
    hover.current = { path: null, timer: null };
  };
  const leave = () => {
    clearHover();
    setTarget(null);
  };
  const onDragOver = (e: DragEvent) => {
    if (!draggingFiles(e) || !model.notebook.peek()) return;
    const dir = folderAt(e.target);
    if (dir === null) return;
    e.preventDefault();
    e.stopPropagation();
    e.dataTransfer!.dropEffect = 'copy';
    if (dir !== target) setTarget(dir);
    const closed = closedFolderAt?.(e.target) ?? null;
    if (open && closed !== hover.current.path) {
      clearHover();
      hover.current = { path: closed, timer: closed ? after(HOVER_OPEN_MS, () => open(closed)) : null };
    }
  };
  const onDragLeave = (e: DragEvent) => {
    const zone = e.currentTarget as Element;
    if (!(e.relatedTarget instanceof Node) || !zone.contains(e.relatedTarget)) leave();
  };
  const onDrop = (e: DragEvent) => {
    if (!draggingFiles(e) || !model.notebook.peek()) return;
    const dir = folderAt(e.target);
    leave();
    if (dir === null) return;
    e.preventDefault();
    e.stopPropagation();
    // Read the drop now: its items are gone once this handler returns.
    void model.importDropped(dir, droppedFiles(e.dataTransfer!));
  };
  return { target, handlers: { onDragOver, onDragEnter: onDragOver, onDragLeave, onDrop } };
}

import { useLayoutEffect, useRef, useState } from 'preact/hooks';
import type { MenuItem } from './shellModel.ts';

/** A context menu at a point: arrow keys and Enter, Escape or a click outside to close. */
export function ContextMenu(props: {
  readonly x: number;
  readonly y: number;
  readonly items: readonly (MenuItem | null)[];
  readonly onClose: () => void;
}) {
  const ref = useRef<HTMLUListElement>(null);
  const enabled = props.items.map((it, i) => (it && !it.disabled ? i : -1)).filter((i) => i >= 0);
  const [index, setIndex] = useState(enabled[0] ?? -1);
  const [pos, setPos] = useState({ x: props.x, y: props.y });

  useLayoutEffect(() => {
    ref.current?.focus();
  }, []);
  // Keep the menu on screen.
  useLayoutEffect(() => {
    const r = ref.current?.getBoundingClientRect();
    if (!r) return;
    setPos({
      x: Math.max(4, Math.min(props.x, window.innerWidth - r.width - 4)),
      y: Math.max(4, Math.min(props.y, window.innerHeight - r.height - 4)),
    });
  }, []);

  const run = (item: MenuItem | null | undefined) => {
    if (!item || item.disabled) return;
    props.onClose();
    queueMicrotask(item.run);
  };
  const step = (d: number) => {
    const at = enabled.indexOf(index);
    setIndex(enabled[(at + d + enabled.length) % enabled.length] ?? -1);
  };

  return (
    <div
      class="pn-backdrop pn-backdrop-clear"
      onPointerDown={(e) => {
        if (e.target === e.currentTarget) props.onClose();
      }}
      onContextMenu={(e) => {
        e.preventDefault();
        props.onClose();
      }}
    >
      <ul
        ref={ref}
        class="pn-menu"
        role="menu"
        tabIndex={-1}
        style={{ left: `${pos.x}px`, top: `${pos.y}px` }}
        onKeyDown={(e) => {
          e.stopPropagation();
          if (e.key === 'ArrowDown') step(1);
          else if (e.key === 'ArrowUp') step(-1);
          else if (e.key === 'Enter' || e.key === ' ') run(props.items[index]);
          else if (e.key === 'Escape' || e.key === 'Tab') props.onClose();
          else return;
          e.preventDefault();
        }}
      >
        {props.items.map((item, i) =>
          item === null ? (
            <li key={i} role="separator" class="pn-menu-sep" />
          ) : (
            <li
              key={i}
              role="menuitem"
              class={`${i === index ? 'pn-menu-current' : ''}${item.danger ? ' pn-menu-danger' : ''}`}
              aria-disabled={item.disabled ? true : undefined}
              onPointerMove={() => !item.disabled && setIndex(i)}
              onClick={() => run(item)}
            >
              <span>{item.label}</span>
              {item.shortcut && <kbd>{item.shortcut}</kbd>}
            </li>
          ),
        )}
      </ul>
    </div>
  );
}

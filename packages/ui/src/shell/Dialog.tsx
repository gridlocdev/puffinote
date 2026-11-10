import type { ComponentChildren } from 'preact';
import { useLayoutEffect, useRef, useState } from 'preact/hooks';

/**
 * A modal dialog: Escape and the backdrop close it; focus moves in, Tab and Shift+Tab cycle through its controls
 * without leaving it, and focus goes back where it was on close.
 */
export function Dialog(props: {
  readonly title?: string;
  readonly onClose: () => void;
  readonly class?: string;
  readonly children: ComponentChildren;
  readonly actions?: ComponentChildren;
  readonly onSubmit?: () => void;
}) {
  const ref = useRef<HTMLFormElement>(null);
  useLayoutEffect(() => {
    const first =
      ref.current?.querySelector<HTMLElement>('[data-autofocus]') ??
      ref.current?.querySelector<HTMLElement>('input, select, button');
    first?.focus();
    if (first instanceof HTMLInputElement) {
      const [a, b] = (first.dataset.select ?? '').split(',').map(Number);
      first.setSelectionRange(a || 0, Number.isFinite(b) ? (b as number) : first.value.length);
    }
  }, []);
  return (
    <div
      class="pn-backdrop"
      onPointerDown={(e) => {
        if (e.target === e.currentTarget) props.onClose();
      }}
    >
      <div class={`pn-dialog ${props.class ?? ''}`} role="dialog" aria-modal="true" aria-label={props.title}>
        <form
          ref={ref}
          class="pn-dialog-form"
          onKeyDown={(e) => {
            if (e.key === 'Escape') {
              e.preventDefault();
              e.stopPropagation();
              props.onClose();
            } else if (e.key === 'Tab' && ref.current) {
              const els = focusables(ref.current);
              if (els.length === 0) return;
              const at = els.indexOf(document.activeElement as HTMLElement);
              e.preventDefault();
              els[nextTabStop(els.length, at, e.shiftKey)]!.focus();
            }
          }}
          onSubmit={(e) => {
            e.preventDefault();
            props.onSubmit?.();
          }}
        >
          {props.title && <h2>{props.title}</h2>}
          <div class="pn-dialog-body">{props.children}</div>
          {props.actions && <div class="pn-dialog-actions">{props.actions}</div>}
        </form>
      </div>
    </div>
  );
}

/** Where Tab (or Shift+Tab, `back`) goes from stop `at` of `count`, wrapping; `at` is -1 when focus is elsewhere. */
export function nextTabStop(count: number, at: number, back: boolean): number {
  if (back) return at <= 0 ? count - 1 : at - 1;
  return at < 0 || at === count - 1 ? 0 : at + 1;
}

/** The controls Tab stops on, in order: enabled, visible, and not taken out of the tab order. */
function focusables(root: HTMLElement): HTMLElement[] {
  return [
    ...root.querySelectorAll<HTMLElement>('button, input, select, textarea, a[href], [tabindex]'),
  ].filter((el) => !(el as HTMLButtonElement).disabled && el.tabIndex >= 0 && el.getClientRects().length > 0);
}

export interface PromptOptions {
  readonly title: string;
  readonly label: string;
  readonly value: string;
  /** The part of `value` to select at first (a name without its extension). */
  readonly select?: readonly [number, number];
  readonly confirm: string;
  /** A reason the value can't be accepted, shown under the field; null when it can. */
  readonly validate?: (value: string) => string | null;
}

/** Ask for one line of text (a name). Resolves null on Cancel. */
export function PromptDialog(props: PromptOptions & { readonly onDone: (value: string | null) => void }) {
  const [value, setValue] = useState(props.value);
  const problem = value.trim() === '' ? '' : (props.validate?.(value.trim()) ?? null);
  const ok = value.trim() !== '' && problem === null;
  return (
    <Dialog
      title={props.title}
      onClose={() => props.onDone(null)}
      onSubmit={() => ok && props.onDone(value.trim())}
      actions={
        <>
          <button type="button" onClick={() => props.onDone(null)}>
            Cancel
          </button>
          <button type="submit" class="pn-primary" disabled={!ok}>
            {props.confirm}
          </button>
        </>
      }
    >
      <label class="pn-field">
        <span>{props.label}</span>
        <input
          value={value}
          spellcheck={false}
          autocomplete="off"
          data-select={props.select ? props.select.join(',') : undefined}
          aria-invalid={problem ? true : undefined}
          onInput={(e) => setValue((e.target as HTMLInputElement).value)}
        />
      </label>
      {problem && <p class="pn-field-error">{problem}</p>}
    </Dialog>
  );
}

export interface ConfirmButton {
  readonly label: string;
  readonly value: string;
  readonly kind?: 'primary' | 'danger';
}

export interface ConfirmOptions {
  readonly title: string;
  readonly message: string;
  /** Names listed under the message (the notes a shared picture shows in). */
  readonly list?: readonly string[];
  /** Left to right; the last is the default. Escape answers 'cancel'. */
  readonly buttons: readonly ConfirmButton[];
}

/** Ask a question with a few answers. */
export function ConfirmDialog(props: ConfirmOptions & { readonly onDone: (value: string) => void }) {
  const last = props.buttons[props.buttons.length - 1];
  return (
    <Dialog
      title={props.title}
      class="pn-confirm"
      onClose={() => props.onDone('cancel')}
      onSubmit={() => props.onDone(last?.value ?? 'cancel')}
      actions={props.buttons.map((b) => (
        <button
          key={b.value}
          type={b === last ? 'submit' : 'button'}
          class={b.kind ? `pn-${b.kind}` : undefined}
          data-autofocus={b === last ? '' : undefined}
          onClick={b === last ? undefined : () => props.onDone(b.value)}
        >
          {b.label}
        </button>
      ))}
    >
      <p>{props.message}</p>
      {props.list && props.list.length > 0 && (
        <ul class="pn-confirm-list">
          {props.list.map((name) => (
            <li key={name}>{name}</li>
          ))}
        </ul>
      )}
    </Dialog>
  );
}

import type { ComponentChildren } from 'preact';
import { useLayoutEffect, useMemo, useRef, useState } from 'preact/hooks';
import type { Command, CommandCatalog } from '../commands/command.ts';
import { rankFiles } from '../notebook/quickOpen.ts';
import { baseName, parentOf } from '../platform/paths.ts';
import { Icon } from './Icon.tsx';

/** ⌘K: search and run any command (PRD §8.1). Recent commands come first while the query is empty. */
export function CommandMenu(props: {
  readonly catalog: CommandCatalog;
  /** Which commands to offer (the editor's only while a note is in front). */
  readonly filter: (c: Command) => boolean;
  readonly onClose: () => void;
}) {
  const { catalog } = props;
  const [query, setQuery] = useState('');
  const [index, setIndex] = useState(0);
  const results = useMemo(() => catalog.search(query).filter(props.filter).slice(0, 80), [catalog, query]);
  const pick = (c: Command | undefined) => {
    if (c && !catalog.isEnabled(c)) return;
    props.onClose();
    // Let the menu unmount (and focus go back) before the command runs.
    if (c) queueMicrotask(() => catalog.run(c.id));
  };
  return (
    <ListPopup
      label="Command Menu"
      placeholder="Search commands…"
      query={query}
      onQuery={(q) => {
        setQuery(q);
        setIndex(0);
      }}
      index={index}
      onIndex={setIndex}
      count={results.length}
      onPick={(i) => pick(results[i])}
      onClose={props.onClose}
      empty="No matching commands"
    >
      {results.map((c, i) => (
        <li
          key={c.id}
          id={`pn-option-${i}`}
          role="option"
          aria-selected={i === index}
          aria-disabled={catalog.isEnabled(c) ? undefined : true}
          onPointerMove={() => setIndex(i)}
          onClick={() => pick(c)}
        >
          {c.icon ? <Icon name={c.icon} /> : <span class="pn-icon" />}
          <span class="pn-option-title">
            {c.title}
            {c.active?.() && <span class="pn-option-on"> ✓</span>}
          </span>
          <span class="pn-option-group">{c.group}</span>
          {c.shortcutText && <kbd>{c.shortcutText}</kbd>}
        </li>
      ))}
    </ListPopup>
  );
}

/** ⌘O: jump to any file by part of its name (PRD §6.3). */
export function QuickOpen(props: {
  readonly files: readonly string[];
  readonly recent: readonly string[];
  readonly showExtensions: boolean;
  readonly onOpen: (path: string) => void;
  readonly onClose: () => void;
}) {
  const [query, setQuery] = useState('');
  const [index, setIndex] = useState(0);
  const results = useMemo(
    () => rankFiles(props.files, query, props.recent).slice(0, 100),
    [props.files, props.recent, query],
  );
  const pick = (path: string | undefined) => {
    props.onClose();
    if (path !== undefined) queueMicrotask(() => props.onOpen(path));
  };
  return (
    <ListPopup
      label="Quick Open"
      placeholder="Go to file…"
      query={query}
      onQuery={(q) => {
        setQuery(q);
        setIndex(0);
      }}
      index={index}
      onIndex={setIndex}
      count={results.length}
      onPick={(i) => pick(results[i])}
      onClose={props.onClose}
      empty={props.files.length === 0 ? 'This notebook has no files yet' : 'No matching files'}
    >
      {results.map((path, i) => {
        const name = baseName(path);
        return (
          <li
            key={path}
            id={`pn-option-${i}`}
            role="option"
            aria-selected={i === index}
            onPointerMove={() => setIndex(i)}
            onClick={() => pick(path)}
          >
            <Icon name={/\.(md|markdown|mdown|mkd|txt)$/i.test(name) ? 'file-text' : 'file'} />
            <span class="pn-option-title">
              {props.showExtensions ? name : name.replace(/\.[^.]+$/, '') || name}
            </span>
            <span class="pn-option-group">{parentOf(path)}</span>
          </li>
        );
      })}
    </ListPopup>
  );
}

function ListPopup(props: {
  readonly label: string;
  readonly placeholder: string;
  readonly query: string;
  readonly onQuery: (q: string) => void;
  readonly index: number;
  readonly onIndex: (i: number) => void;
  readonly count: number;
  readonly onPick: (i: number) => void;
  readonly onClose: () => void;
  readonly empty: string;
  readonly children: ComponentChildren;
}) {
  const input = useRef<HTMLInputElement>(null);
  const list = useRef<HTMLUListElement>(null);
  useLayoutEffect(() => {
    input.current?.focus();
  }, []);
  useLayoutEffect(() => {
    list.current?.querySelector('[aria-selected="true"]')?.scrollIntoView({ block: 'nearest' });
  }, [props.index, props.count]);
  const onKeyDown = (e: KeyboardEvent) => {
    const page = 8;
    const move = (i: number) => {
      e.preventDefault();
      props.onIndex(Math.max(0, Math.min(props.count - 1, i)));
    };
    if (e.key === 'ArrowDown') move(props.index + 1);
    else if (e.key === 'ArrowUp') move(props.index - 1);
    else if (e.key === 'PageDown') move(props.index + page);
    else if (e.key === 'PageUp') move(props.index - page);
    else if (e.key === 'Enter') {
      e.preventDefault();
      props.onPick(props.index);
    } else if (e.key === 'Escape') {
      e.preventDefault();
      e.stopPropagation();
      props.onClose();
    }
  };
  return (
    <div
      class="pn-backdrop pn-backdrop-top"
      onPointerDown={(e) => {
        if (e.target === e.currentTarget) props.onClose();
      }}
    >
      <div class="pn-dialog pn-palette" role="dialog" aria-modal="true" aria-label={props.label}>
        <input
          ref={input}
          value={props.query}
          placeholder={props.placeholder}
          spellcheck={false}
          autocomplete="off"
          onInput={(e) => props.onQuery((e.target as HTMLInputElement).value)}
          onKeyDown={onKeyDown}
          role="combobox"
          aria-expanded="true"
          aria-controls="pn-palette-list"
          aria-activedescendant={props.count > 0 ? `pn-option-${props.index}` : undefined}
        />
        <ul id="pn-palette-list" role="listbox" ref={list}>
          {props.children}
        </ul>
        {props.count === 0 && <p class="pn-palette-empty">{props.empty}</p>}
      </div>
    </div>
  );
}

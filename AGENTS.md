# Puffinote — agent guide

A TypeScript notes app for folders of Markdown files. One UI runs in the browser, in the NAS server's client and in the Electron app; a CLI reads notebooks with the same engine. The spec is [docs/plans/prd.md](docs/plans/prd.md), the code layout is [docs/architecture.md](docs/architecture.md), and the commands are in [docs/development.md](docs/development.md).

## Gates

- `just check` (= `pnpm check`) must pass before you commit: Prettier check, ESLint, `tsc -b`, Vitest, and the build.
- When you change behaviour, add or update a test in the package's `test/` folder, and update the PRD section the code cites (`PRD §n`).
- Check UI changes headlessly (a browser driven in the background, or the desktop app with `PUFFINOTE_HEADLESS=1`). Never open a visible window to check your work.

## Layout

| Path | What it is |
| --- | --- |
| `packages/text` | Text documents: `TextDocument`, selections, graphemes, decoding, undo history, search, stats. No DOM or Node (ESLint enforces it). |
| `packages/markdown` | The Markdown engine: incremental tokenizers, `MarkdownModel`, editing helpers, the HTML renderer, the outline. No DOM or Node. |
| `packages/node-files` | Notebook files on Node, shared by the desktop app, the server and the CLI: atomic saves, per-file locks, listings, moves. |
| `packages/ui` | Preact and signals: the editor, the command catalog, the notebook model, the shell, and the `NotebookStore` adapters. |
| `apps/web` | The Vite entry point. One static build serves the browser, the NAS server and the desktop app. |
| `apps/server` | The NAS server (`node:http`): serves the client and `/api` for the configured roots. No framework. |
| `apps/desktop` | The Electron shell: main process, sandboxed preload bridge, native menu, folder watcher. |
| `apps/cli` | The `puffinote` CLI: notebook and note commands, run from source. |

## Golden rules

1. **The engine packages stay pure.** `text` and `markdown` know nothing about the DOM, Node, Preact or styling. The engine emits semantic tokens; concealment and styling belong to the editor.
2. **The files are the source of truth.** No database. Editing never rewrites bytes the user didn't touch, and a save writes exactly the buffer.
3. **One command catalog.** The command menu, shortcuts, context menus and the Electron menu bar all read it. Don't wire a key or a menu item past it.
4. **The UI reaches files only through `NotebookStore`.** It never knows whether a notebook is in memory, in the browser, on the NAS or on disk.
5. **Never lose text.** Saves are atomic everywhere. A conflict is shown and the user chooses; nothing merges or overwrites silently.
6. **Security.** The Electron renderer is sandboxed with context isolation; its only native access is the `puffinoteNative` bridge, and only inside folders the user granted. The server confines every path to its root, symlinks included, and checks `Host` when there is no token.
7. **Dependencies.** The runtime dependencies are Preact and `@preact/signals`. Don't add a third-party package without the owner's approval. Pin versions exactly.
8. **Nothing left to interpretation.** If a behaviour isn't in the PRD, stop and ask, then record the answer in the PRD.

## TypeScript conventions

- Strict mode with `noUncheckedIndexedAccess` and `erasableSyntaxOnly`: no enums, parameter properties or namespaces. Use union types and `const` objects.
- Relative imports end in `.ts`. Use `import type` for types only.
- Offsets are UTF-16 code units into the document. Caret motion goes by grapheme cluster.

## Git workflow

- Work in a git worktree off `main`, and rebase onto `main` before landing. Don't merge `main` into a branch.
- Keep commits small and coherent.
- Use Conventional Commits with an area scope: `feat(puffinote): …`, `fix(editor): …`, `test(markdown): …`.
- Commit only when `just check` is green and no questions are outstanding.

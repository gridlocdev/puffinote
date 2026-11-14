# Architecture

Puffinote is one TypeScript code base with three hosts and a CLI. The browser, the NAS server's client and the Electron app run the same UI. The only thing that differs between them is where the notebook's files come from.

```
     ┌──────────────────── packages/ui (Preact + signals) ─────────────────────┐
     │  Shell · Rail/Library layouts · FileTree · TabStrip · ⌘K / ⌘O · dialogs  │
     │  ShellModel (commands, overlays) · Notebook (tree, tabs, session)        │
     │  MarkdownEditor (DOM lines, hidden textarea, caret overlay)              │
     │  NotebookStore ─┬─ BrowserStore  (File System Access / private folder)   │
     │                 ├─ NasStore      (fetch /api, ETag)                      │
     │                 ├─ NativeStore   (window.puffinoteNative IPC)            │
     │                 └─ MemoryStore   (demo, tests)                           │
     └───────────────────────────────────┬──────────────────────────────────────┘
                                         │ imports
     ┌──────────── packages/markdown (pure TS) ────────────┐
     │  line + inline tokenizers · MarkdownModel            │
     │  editing helpers · HTML renderer · outline           │◄──── apps/cli
     └──────────────────────────┬───────────────────────────┘
                                │
     ┌──────────── packages/text (pure TS) ────────────────┐
     │  TextDocument · selections · graphemes · codec       │
     │  DocumentBuffer · EditHistory (undo) · search · stats│
     └──────────────────────────────────────────────────────┘

 apps/web      Vite entry; one static build with relative URLs (base './') for every host
 apps/server   node:http: the static client + /api/{info,roots,list,file,mkdir,move}
 apps/desktop  Electron main + sandboxed preload; loads the apps/web build via app://puffinote/
 apps/cli      `puffinote`: reads notebooks and notes with the same engine; runs from source
```

## Layering

- `@puffinote/text` knows text, not Markdown. `@puffinote/markdown` knows Markdown, not styling. Both use no DOM or Node APIs and import no UI library. ESLint enforces this, so they run in a browser, a worker and Node.
- `@puffinote/ui` owns everything visual: styling, concealment, the shell and the stores.
- The apps are thin. They pick a store, put the shell on the page, and wire the host's native parts.

## Text (`packages/text`)

- **`TextDocument`** is the immutable document: the text plus a line index. Offsets are UTF-16 code units. Line and column conversions are O(log n).
- **`DocumentBuffer`** applies `TextEdit`s and keeps the current document. **`EditHistory`** groups edits into undo steps: typing runs coalesce, and commands are single steps.
- **Selections** are anchor and caret offsets. Caret motion goes by grapheme cluster (`Intl.Segmenter`).
- **`readText`** decodes bytes: a BOM wins, then strict UTF-8, then a guessed encoding that the caller must treat as read-only. Line endings are kept as found.
- **`computeStats`** gives words, characters, lines and reading minutes. **`search`** finds text for the sidebar.

## Markdown (`packages/markdown`)

- **Tokens.** A token is `{ type, start, length, role, meta, node }`. The role is `content` or `marker`. The editor hides markers in the live preview; the engine doesn't know that. `meta` carries a heading level or a task state. `node` groups the tokens of one inline construct, so the editor can reveal it as a unit.
- **Line tokenizer.** It classifies one line from the block state it enters with, and returns the tokens and the state it leaves. Block state covers fences, indented code, quotes, lists, tables and front matter.
- **Inline tokenizer.** It runs within one line, with delimiter runs for emphasis. It is tolerant: an unclosed marker is literal text.
- **`MarkdownModel`** holds the tokens and the entering state of every line. After an edit it re-tokenizes from the first changed line until the state converges with the old run.
- **Editing helpers** are pure functions from a document and selection to an edit: line prefixes (lists, quotes, headings, tasks) and ordered-list renumbering.
- **`renderHtmlDocument` / `renderHtmlBody`** walk the same tokens to self-contained HTML. Task boxes are inline SVG that matches the editor's.
- **`outlineOf`** lists the headings for the Outline panel and the CLI.

## UI (`packages/ui`)

- **Editor (`editor/`).**
  - `EditorState` holds the buffer, history, selection, view mode and a version signal for one note.
  - `MarkdownEditor` renders each line as DOM from its tokens (`lineRender.ts`), with concealed markers collapsed and decorations (bullets, checkboxes, quote bars, pictures) drawn in their place.
  - A hidden `<textarea>` at the caret takes keyboard, IME and clipboard input.
  - `geometry.ts` maps offsets to positions and back, from the DOM the surface rendered. Clicks, arrows, selection painting and the caret all go through it.
  - `reveal.ts` decides, per line, which markers show for the current selection.
  - `commands.ts` holds the editing behaviour (Enter, Tab, pairing, line moves), and `editorCommands.ts` turns it into catalog commands.
- **Commands (`commands/`).** `CommandCatalog` is the one list of commands. The command menu, the shortcuts, the context menus and the Electron menu bar all read it. Shortcuts are written once (`mod+shift+k`) and formatted per platform.
- **Notebook (`notebook/`).** `Notebook` is one open notebook as signals: the tree, the tabs, the active tab, the recent files, the settings and the notices. Every file change goes through it, so the tree, the tabs and the recent list stay reconciled with the folder. It also writes `.puffinote/session.json`.
- **Shell (`shell/`).** `ShellModel` holds the open notebook, the overlays (dialogs, ⌘K, ⌘O) and the shell commands. `Shell` and its components draw it.
- **Stores (`platform/`).** `NotebookStore` is the only way the UI reaches files. Paths are notebook-relative and `/`-separated. Each store has its own version token for conflict detection: an mtime (native and browser), an ETag (NAS) or a counter (memory). A write with a stale version fails with a `ConflictError`.

## Web (`apps/web`)

- `Launcher.detect()` finds out where the page runs: inside Electron (`window.puffinoteNative`), served by the NAS server (`/api/info` answers), or a plain page.
- The welcome screen offers what that host can open. Recent notebooks are kept in local storage, and browser folder handles in IndexedDB.
- The built client has a strict CSP. Pictures load from `/api/file`, from blob URLs, or from the desktop's `pnfile:` scheme.

## Server (`apps/server`)

- **Configuration** is environment only:
  - `PUFFINOTE_ROOTS=Name=/dir;Other=/dir2` names the folders that hold notebooks (required).
  - `PUFFINOTE_STATIC` is the built web client (default `../web/dist`; empty means API only).
  - `PUFFINOTE_TOKEN` requires a bearer token.
  - `PUFFINOTE_HOST` and `PORT` set the bind address (default `0.0.0.0:8790`).
  - `PUFFINOTE_HOSTS` lists extra Host names accepted without a token.
  - `PUFFINOTE_MAX_MB` caps an upload (default 64).
- **Paths.** Every path is confined to its root, including `..`, absolute paths, NUL bytes and symlinks that point out.
- **Writes.**
  - Each save is atomic: a temp file beside the target, fsync, rename. The replacement keeps the file's mode and, where permitted, its owner.
  - `If-Match` gives optimistic concurrency (412 if the file changed or is gone). `If-None-Match: *` stops a new note from overwriting a file.
  - Writes to one file are serialized under a key that is symlink-resolved and case-folded, so the check and the rename cannot interleave.
- **Host check.** Without a token, a request whose `Host` is not an IP literal, `localhost`, or a name in `PUFFINOTE_HOSTS` gets 421. This blocks DNS rebinding.
- **Files served raw.** Pictures a note shows are served with their type. Anything else is served as bytes with a sandboxing CSP, so an opened SVG runs nothing.

## Desktop (`apps/desktop`)

- **Isolation.** The renderer runs with `sandbox`, `contextIsolation` and no Node.
- **Bridge.** The preload exposes one object, `window.puffinoteNative`. The contract is in `bridge.ts`, mirrored in `packages/ui/src/platform/nativeStore.ts` and in the inlined channel names of `preload.cts`. Keep the three in step.
- **Loading.** The UI loads from `app://puffinote/`, a privileged standard scheme backed by `out/web`. Navigation away is blocked, and links open in the system browser. Pictures load from `pnfile://local/<path>`, served only inside granted folders.
- **Grants.** The renderer may only touch folders the user handed over: the open dialog, `open-file`, argv, or an earlier open, kept in `granted-files.json` under userData. Any other folder asks first. Every IPC call must come from an `app://puffinote/` frame.
- **Files.** Saves are atomic with an mtime conflict check and one save per file at a time. Deletes go to the Trash. A folder watcher reports changes, debounced, and leaves out the app's own temp files and saves.
- **Menu and window.** The native menu is built from the renderer's catalog. The close guard asks the renderer about unsaved tabs; if it doesn't answer within 3 s, the user may force the close.
- **`PUFFINOTE_HEADLESS=1`** renders offscreen with no window or Dock icon, for automated checks.

## CLI (`apps/cli`)

- `src/spec.ts` is a small declarative parser (commands, arguments, options, help). `src/index.ts` builds the command tree. Commands are in `src/commands/`.
- All I/O goes through `CliIo` (stdout, stderr, stdin, launch), so the tests run commands in process.
- A bare path launches the desktop app with `open -a Puffinote <folder>` (macOS only).
- `html -o` writes atomically, with the same temp-file shape the desktop watcher ignores.

## Testing

- Vitest runs every package's and app's `test/` folder in Node. The UI tests cover models and pure functions (the shell model, the notebook, rendering decisions); the drawn UI is checked headlessly in a browser.
- The editor's offset ↔ visual map and the incremental tokenizer have dedicated suites. The incremental tests compare against a full re-tokenization.
- `just check` is the gate: Prettier, ESLint, `tsc -b`, Vitest, then the build.

Setup, commands and workflows are in the [developer guide](Developer-Guide.md).

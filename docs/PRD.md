# PRD — Puffinote

A Markdown notes app. A notebook is a folder of plain `.md` files. Puffinote shows the folder as a tree, opens
notes in tabs, and edits them in a live-preview editor. The files stay ordinary text that any other tool can
read, diff and sync.

The code cites this document as `PRD §n`. If you renumber a section, update those references too
(`grep -rn 'PRD §' packages apps`).

---

## 1. Summary

- **One notebook, one window.** The window shows a sidebar (Files, Filter, Search, Outline and Recent), a tab
  strip, the note in front, and a status bar.
- **The editor is built for Markdown.** It is a live preview: formatting shows in place, and the Markdown
  markers stay hidden until the caret reaches them. Saving writes exactly the text in the buffer.
- **Three hosts, one client:**
  - **Web:** a static page that opens a folder from disk, or the browser's private storage.
  - **NAS:** the same page, served by a small Node server that exposes named folders.
  - **Desktop:** an Electron app that opens folders from disk.
- **A CLI**, `puffinote`, reads notebooks and notes for scripts and CI (§12).

## 2. Goals and non-goals

### 2.1 Goals

- **The files are the source of truth.** The app keeps no database. A note is a file, a folder is a folder,
  and the app's own state lives in `.puffinote/` inside the notebook (§5).
- **Keyboard first.** Every command is in the command menu (⌘K) and in the native menu bar on the desktop.
  Files can be reached with Quick Open (⌘O), and the whole tree works from the keyboard (§6.1).
- **The same behaviour in every host.** One UI package runs everywhere. A host only supplies a
  `NotebookStore` (§3).
- **Few dependencies.** Preact and `@preact/signals` at runtime. Vite, Electron and electron-builder to build.
  Nothing else without the owner's approval.

### 2.2 Non-goals

- Editors for other file types. A non-Markdown file shows in the tree and opens in its default app (or
  downloads). A picture opens in a tab as a read-only view.
- Rewriting links when a file moves, backlinks, graph views, and plug-ins.
- Real-time collaboration and automatic merging. A conflict is shown, never merged (§5).
- Full CommonMark/GFM fidelity (§11.6).

## 3. Hosts

| Host    | Where notebooks come from                                                                   | Change detection                            |
| ------- | ------------------------------------------------------------------------------------------- | ------------------------------------------- |
| Web     | A folder picked through the File System Access API (Chromium), or the origin-private folder | Re-read the tree when the window gets focus |
| NAS     | Folders named in `PUFFINOTE_ROOTS` on the server, browsed from the welcome screen           | ETags; re-read the tree on focus            |
| Desktop | Any folder on disk: the open dialog, the Dock, `puffinote <folder>`, or Recent              | A folder watcher in the main process        |

- **One build serves all three.** The page detects at runtime whether it is in Electron, served by the NAS
  server, or a plain page. There is no build flag.
- **The welcome screen** lists the recent notebooks and the ways to open one that this host has. The last
  notebook reopens on start when it can do so without asking.
- **The server is optional to deploy and safe by default:** paths are confined to their root, writes are
  atomic, an optional bearer token protects it, and a Host check blocks DNS rebinding (ARCHITECTURE.md).

## 4. Principles

1. **Never lose text.** A save is atomic. A failed save keeps the buffer. Closing, switching notebooks and
   quitting all ask about unsaved tabs.
2. **Never rewrite bytes the user didn't touch.** The editor edits the source minimally (§11.7).
3. **Dim, don't hide.** A command that can't run right now stays in its menu, dimmed, so the menus don't
   change shape under the user.
4. **No surprise network traffic.** Remote pictures and links load only on explicit action. The desktop app
   and the plain web page never contact a server.

## 5. The notebook

- **A notebook is one root folder.** One notebook is open at a time. Open Notebook… (⇧⌘O) asks about unsaved
  tabs, then switches.
- **The notebook keeps its own state in `.puffinote/`:**
  - `session.json`: the open tabs, the active tab, the sidebar (width, panel, open folders) and the recent
    files.
  - `settings.json`: this notebook's settings overrides (§9).
  - `.gitignore`: ignores `session.json` and keeps `settings.json`, so a notebook under git shares its
    settings but not its window state.
- **What opens where:**
  - A Markdown file (`.md`, `.markdown`, `.mdown`, `.mkd`, `.txt`) opens in the editor.
  - A picture opens in a tab as a read-only view.
  - Anything else opens in its default app (desktop) or downloads (web, NAS).
- **Encoding.** A file is read as UTF-8, with or without a BOM. If it is not valid UTF-8, the encoding is
  guessed and the tab is read-only, so that a save cannot write the guess back.
- **External changes.** The tree is re-read when the folder changes (desktop) or the window regains focus
  (web, NAS). Every open tab is then reconciled with the disk:
  - A tab without unsaved edits reloads.
  - A tab with unsaved edits is left alone and flagged. Auto-save stops for it. Saving it asks whether to
    overwrite the file on disk or take the disk's version.
  - Taking the disk's version is **one undoable step**, so ⌘Z gives back what the tab held.
  - A file deleted on disk under a dirty tab is flagged the same way. Saving recreates it.
- **Saving.** ⌘S saves the active tab; Save All saves every dirty tab. With auto-save on (the default), a note
  saves a second after the last edit. Revert to Saved discards the edits.

## 6. The sidebar

- **Files** shows every file and folder in the notebook, folders first, sorted case-insensitively with
  numbers by value (`note2` before `note10`). Dotfiles are hidden unless Show Hidden Files is on. Extensions
  are hidden unless Show File Extensions is on.
- **Tree verbs:** new note, new folder, rename (F2), delete (confirmed), reveal the active file, refresh, and
  expand or collapse all. They are in each row's context menu and in the command menu.
- **Drag to move.** The tree draws the drag itself: a ghost under the pointer, an outline on the folder it
  would land in, and a closed folder that opens after the pointer rests on it for a second.
  - A drop on a folder means that folder. A drop on a file means the folder that holds it. A drop on the empty
    space means the top level.
  - A move that changes nothing, or a folder into itself, shows no indicator. A name that is already taken is
    refused. Escape cancels.
  - A move is a plain move. Links to the file are not rewritten. Open tabs and the recent list follow it.
- **Collapsible.** ⌘\ hides and shows the sidebar. The width and state persist per notebook.

### 6.1 Keyboard

- ⌥⌘S focuses the tree; ⌥⌘E focuses the editor.
- In the tree: ↑/↓ move, ←/→ collapse or expand (or go to the parent or first child), Home/End jump, typing
  jumps to a matching name, Enter opens a file or toggles a folder, F2 renames, Delete deletes.
- The tree is one ARIA `tree` with a visible focus ring.

### 6.2 Panels

The switcher row changes the panel. Filter and Search put the caret in their box; Files, Outline and Recent
leave the focus where it was.

- **Filter** narrows the tree to entries whose whole name (extension included) contains the text,
  case-insensitively. It keeps the folders above a match and everything inside a matching folder, opens the
  folders that lead to matches, and counts the matches (or says "No matches"). It is the same tree, with the
  same keys, menu and drag.
- **Search** finds text in every note, including the unsaved text of open tabs. Each hit shows its line with
  the match marked. Enter or a click opens the note at the hit.
- **Outline** lists the headings of the active note, indented by level. A click moves the caret to the
  heading.
- **Recent** lists this notebook's recently opened files.

### 6.3 Quick Open (⌘O)

Type part of a file name to open it. Ranking: names that start with the query, then names that contain it,
then paths that contain it, then names and paths that contain its letters in order. Ties go to the more
recently opened file. With an empty query, recent files come first. The command menu is for commands only;
files are opened here.

## 7. Tabs

- One tab per open file. A click shows it, a middle-click or × closes it, and the context menu has Close,
  Close Others and Close All. ⇧⌘] and ⇧⌘[ step through tabs.
- A dirty tab shows a dot. Closing it asks Save, Don't Save or Cancel.
- After a save, the status bar says "Saved" for a moment.
- Each tab keeps its scroll position, caret, undo history and view (§10).
- Show Tab Bar (§9) hides the strip. The tabs stay open.

## 8. Commands and shortcuts

### 8.1 The command catalog and the command menu

- **One catalog.** Every command has an id, a title, a group, an optional shortcut, and `enabled`/`active`
  predicates. The command menu, the shortcuts, the context menus and the Electron menu bar all read it.
- **The command menu (⌘K)** filters the catalog as you type. Recent commands come first while the query is
  empty. A disabled command still shows, dimmed, and does nothing.
- Ids start with `shell.`, `view.`, `edit.`, `format.` or `insert.`.

### 8.2 Shortcuts

- Shortcuts are written once as `mod+shift+k` and shown in each platform's style (⌘⇧K, Ctrl+Shift+K).
- **Resolution order:** an open overlay (a dialog, the command menu) gets keys first. Then the shell's
  reserved set: ⌘K, ⌘O, ⌘N, ⇧⌘N, ⌘S, ⌘W, ⌥⌘W, ⇧⌘], ⇧⌘[, ⌥⌘S, ⌥⌘E, ⌘\, ⌘, and ⇧⌘O. Then the editor.
- In a text field other than the note (a dialog, the filter box), editing shortcuts keep the field's own
  behaviour. For example, ⌘A selects the field's text.

## 9. Settings

Settings resolve in layers, lowest first: the defaults, the user's preferences (every notebook), then the
notebook's overrides in `.puffinote/settings.json`. The Settings dialog (⌘,) has two scopes over the same
fields: **Preferences** and **This notebook**. A notebook field can be reset to inherit.

| Setting              | Values                              | Default      |
| -------------------- | ----------------------------------- | ------------ |
| Appearance           | Match system, Light, Dark           | Match system |
| Notes open in        | Rich view, Formatted view, Raw text | Rich view    |
| Save automatically   | on/off                              | on           |
| Indent with          | 4 spaces, 2 spaces, Tab             | 4 spaces     |
| Show file extensions | on/off                              | off          |
| Show hidden files    | on/off                              | off          |
| Spell check          | on/off (the browser's checker)      | off          |
| Show tab bar         | on/off                              | on           |

## 10. The editor

A custom editing surface, not a `contenteditable`. Lines render as DOM. A hidden `<textarea>` takes keyboard,
IME and clipboard input. The caret and selection are drawn from an offset ↔ visual map. There are three views,
switched from the status bar or the View commands:

- **Rich view** (`live`): the live preview. Markers are hidden until the caret reaches them (§10.2).
- **Formatted view** (`source`): the same styling with every marker shown.
- **Raw text** (`raw`): the literal source in one monospace style.

### 10.1 Editing

- **Caret and selection** move by grapheme cluster. IME composition works in place.
- **Undo and redo.** Typing coalesces into runs. Structural edits and commands are single steps. Revert
  resets the history. Taking an external change is one step (§5).
- **Enter.** In the two formatted views, Enter writes a paragraph break (`\n\n`) that shows as one new line.
  In Raw text, Enter writes one newline. Either way, one press makes one visible line.
- **Lists and quotes.** Enter continues a list item, task (always as `[ ]`), or quote. Enter on an empty
  top-level item leaves the block and lands on a visible line. An empty nested item goes up one level
  instead. Ordered lists renumber as items are added, moved or re-indented.
- **Tab and Shift+Tab** indent and outdent list items, by the Indent with setting. Elsewhere, Tab inserts the
  indent.
- **Auto-pairing:** `*`, `_`, `` ` ``, `[` and `(` wrap a selection, or insert a pair.
- **Line commands:** move line up/down (⌥↑/⌥↓), duplicate (⌘D), delete (⇧⌘K).
- **Formatting:** bold ⌘B, italic ⌘I, inline code ⌘E, strikethrough ⇧⌘X, highlight ⇧⌘H, headings ⌘1–⌘6,
  paragraph ⌘0, bullet list ⌘., numbered list ⌘/, blockquote, link ⌥⌘K, code block ⇧⌘E, horizontal rule.
- **Tasks:** ⌘L toggles a checkbox (and adds one where there is none), ⌥⌘L steps the status, ⇧⌘L removes it.
- **Clipboard:** copy gives the Markdown source. Paste inserts plain text, and drops a duplicate task marker
  when pasting a task into a task.

### 10.2 Live preview

**What "reveal" means depends on the construct:**

- **Inline marks** (emphasis, strikethrough, highlight, inline code, links, autolinks): the markers show when
  the caret or selection touches that node. Otherwise they are hidden and the styled text shows.
- **Line-prefix marks** (heading `#`, `>`, list and task markers): they show when the caret is on the line.
  When hidden, they are drawn as decorations: bullets, numbers, checkboxes, a quote bar.
- **Blocks** (fenced code, tables, front matter): the raw source shows when the selection touches the block.
- A selection reveals every node it touches.

**Concealed markers are atomic.** Arrow keys skip over a hidden marker run. Once revealed, its characters are
ordinary text. The offset ↔ visual map keeps clicks, arrows and selection on the right document offsets.

**Unfinished syntax is the common case while typing.** An unclosed marker is literal text until it closes.
An unterminated fence runs to the end of the file as code. Nothing flickers.

**Pictures.** A line that holds only `![alt](url)` shows the picture, scaled down to the text column (never
up). The caret sits before or after it, never inside. Relative paths resolve against the note's folder.

### 10.3 Status bar, links and spell check

- **Status bar:** the latest notice (an error, "Saved"), Ln/Col, the word count, and the view switcher.
- **Links.** Hovering shows the destination. ⌘-click (Ctrl-click elsewhere) opens it. A plain click puts the
  caret in the link, so it stays editable. Only `http`, `https` and `mailto` links open. Other schemes,
  including `file:`, `javascript:` and `data:`, are never handed to the OS.
- **Spell check** uses the browser's checker, when Spell check is on.

### 10.4 The heading type scale

Sizes are ratios of a 16px body. Sizes step down by about 1.25× to H4. Below that, weight and colour carry the
level, so H5 and H6 do not drop below the body size.

| Level | Size | Space before | Space after | Weight   |
| ----- | ---- | ------------ | ----------- | -------- |
| H1    | 30   | 48           | 12          | Bold     |
| H2    | 24   | 38           | 10          | Bold     |
| H3    | 20   | 32           | 8           | SemiBold |
| H4    | 17   | 27           | 7           | SemiBold |
| H5    | 15   | 24           | 6           | Bold     |
| H6    | 13   | 21           | 5           | Bold     |
| Body  | 16   | 0            | 12          | Normal   |

Headings bond downward: a large space before and a small space after. Adjacent margins collapse, and the
first block takes no space before. Nothing uppercases or letter-spaces a heading, because both would break
the character-exact offset map.

### 10.5 Export to HTML

The renderer walks the same tokens as the editor and writes one self-contained HTML document (styles
included) or a fragment. It has no dependencies. The CLI's `html` command uses it (§12).

## 11. Markdown processing

An incremental, line-based tokenizer with block state and a checkpoint cache. There is no document AST.
Inline tokenization runs within each line. The engine (`@puffinote/markdown`) emits semantic tokens only —
types, source ranges and roles (`content` or `marker`) — and knows nothing about styling or concealment.

### 11.1 Block state and checkpoints

The tokenizer carries block state from line to line: normal, fenced code (fence character and length),
indented code, blockquote, list, table, and front matter. Each line stores the state it starts in, so any line
can be re-tokenized without starting from the top.

### 11.2 Incremental re-tokenization

An edit re-tokenizes from the first changed line and stops once the block state matches the previous run
again. A common edit re-runs a short tail. The worst case is O(n): an opening fence at the top turns the rest
of the file into code.

### 11.3 Supported constructs

| Construct        | Syntax                      | Rendering                         |
| ---------------- | --------------------------- | --------------------------------- |
| ATX heading      | `#` … `######`              | Type scale (§10.4)                |
| Setext heading   | text over `===` / `---`     | Same as `#` / `##`                |
| Bold, italic     | `**x**` `__x__` `*x*` `_x_` | Bold, italic                      |
| Bold italic      | `***x***`                   | Bold italic                       |
| Strikethrough    | `~~x~~`                     | Struck                            |
| Highlight        | `==x==`                     | Highlighted                       |
| Inline code      | `` `x` ``                   | Monospace on a tint               |
| Fenced code      | ` ```lang `                 | Monospace band                    |
| Indented code    | four spaces                 | Monospace band                    |
| Blockquote       | `>` (nested `>>`)           | One bar per level                 |
| Lists            | `-` `*` `+` `1.`            | Bullet or number                  |
| Task             | `- [ ]` `- [/]` `- [x]`     | Checkbox, three states (§11.4)    |
| Link             | `[text](url)`               | Styled; ⌘-click opens (§10.3)     |
| Autolink         | `<https://…>`, bare URLs    | Styled                            |
| Image            | `![alt](url)`               | The picture, on a line of its own |
| Horizontal rule  | `---` `***`                 | A rule                            |
| Table            | GFM pipe tables             | Styled cells                      |
| Hard break       | trailing `\` or two spaces  | Line break                        |
| Escape           | `\*`, `\_`, …               | The literal character             |
| Front matter     | leading `---` … `---`       | A dimmed block                    |
| Wiki link, embed | `[[target]]`, `![[target]]` | Tokenized; drawn as styled text   |

A setext heading carries a zero-length heading marker that holds its level, so consumers read the level from
the line's first token whichever spelling made it. A lone `---` with no paragraph above it is a rule.

### 11.4 Task states

One character between the brackets, three states:

| State       | Source | Drawn as                  |
| ----------- | ------ | ------------------------- |
| Not started | `[ ]`  | An empty box              |
| In progress | `[/]`  | An accent dash            |
| Done        | `[x]`  | A check on an accent fill |

- `[X]` also reads as done. Any other character (`[-]`, `[?]`) is literal text.
- The three states differ by shape first. Colour is never the only signal. A done item's text is dimmed and
  struck through.
- **A click on the box completes it**, and a click on a done box clears it. A click never stops at in
  progress on the way. To mark a task in progress, use ⌥⌘L (Next Task Status).
- A state change rewrites the one character between the brackets and leaves the caret where it was.
- The HTML export draws the same three boxes as inline SVG.

### 11.5 Links

See §10.3 for how links behave in the editor. A relative link is resolved against the note's folder for
pictures. A relative link is never opened from a ⌘-click.

### 11.6 Out of scope

Full CommonMark/GFM edge cases (lazy continuation, complete emphasis precedence, link reference definitions,
nested HTML blocks); raw HTML rendering; footnotes, definition lists, math, and diagrams.

### 11.7 Determinism and round trip

Editing never rewrites untouched bytes. The document is the source of truth and rendering is derived from it.
Saving writes exactly the buffer, with its line endings kept. Formatting commands and checkbox changes edit
the source minimally and predictably.

## 12. The CLI

`puffinote` runs from source with Node (`just cli …` in the repo). Every command only reads, and a `-` file
argument reads stdin.

| Command                       | Output                                                           |
| ----------------------------- | ---------------------------------------------------------------- |
| `puffinote [<path>]`          | Opens the folder (or a file's folder) in the desktop app (macOS) |
| `tree [<notebook>]`           | The file tree, folders first, dotfiles hidden                    |
| `search <query> [<notebook>]` | `path\thits\tsnippet`, ranked by hits; `-n/--max` (default 200)  |
| `info [<notebook>]`           | Notebook path, file, note and other counts, total bytes          |
| `html <file>`                 | A self-contained HTML document; `--fragment`, `-o/--output`      |
| `cat <file>`                  | The text, decoded the way the editor reads it                    |
| `tokens <file>`               | The token stream (for debugging the tokenizer)                   |
| `stats <file>`                | Words, characters, lines, reading minutes                        |
| `outline <file>`              | The headings, indented by level                                  |

A notebook argument defaults to the current folder. Errors print `puffinote: <message>` on stderr and exit 1.
A usage error prints the message and then the command's help.

## 13. Non-functional requirements

- **Responsiveness.** Typing re-tokenizes only the changed tail (§11.2). The editor renders only the lines it
  needs.
- **Accessibility.** The tree, tabs, menus and dialogs carry ARIA roles and labels and a visible focus ring.
  Concealment never removes text from the accessibility tree. Everything works from the keyboard.
- **Themes.** Light and dark, following the system by default. Colours are tokens.
- **Security.** The desktop renderer is sandboxed with context isolation, and its bridge only touches folders
  the user granted. The NAS server confines paths to their roots. Notes cannot run script: the built client
  has a strict CSP.
- **Portability.** The engine packages (`@puffinote/text`, `@puffinote/markdown`) use no DOM or Node APIs
  (ESLint enforces this). They run in a browser, a worker and Node.
